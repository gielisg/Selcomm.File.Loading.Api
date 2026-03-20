using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using FileLoading.Interfaces;
using FileLoading.Models;
using FileLoading.Repositories;
using FileLoading.Validation;
using Selcomm.Data.Common;

namespace FileLoading.Services;

public class AiReviewService : IAiReviewService
{
    private readonly IFileLoaderRepository _repository;
    private readonly IValidationConfigProvider _validationConfigProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiReviewOptions _options;
    private readonly ILogger<AiReviewService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public AiReviewService(
        IFileLoaderRepository repository,
        IValidationConfigProvider validationConfigProvider,
        IHttpClientFactory httpClientFactory,
        IOptions<AiReviewOptions> options,
        ILogger<AiReviewService> logger)
    {
        _repository = repository;
        _validationConfigProvider = validationConfigProvider;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    // ============================================
    // AI Review
    // ============================================

    public async Task<DataResult<AiReviewResponse>> ReviewFileAsync(
        int ntFileNum, AiReviewRequest? request, SecurityContext securityContext)
    {
        var domain = securityContext.Domain ?? string.Empty;

        // 1. Load & validate domain config
        var configCheck = await ValidateDomainConfigAsync(domain);
        if (!configCheck.IsSuccess)
            return Forward<AiReviewResponse>(configCheck);
        var domainConfig = configCheck.Data!;

        // 2. Load file metadata
        var fileResult = await _repository.GetFileStatusAsync(ntFileNum, securityContext);
        if (!fileResult.IsSuccess)
        {
            return new DataResult<AiReviewResponse>
            {
                StatusCode = fileResult.StatusCode == 404 ? 404 : fileResult.StatusCode,
                ErrorCode = fileResult.StatusCode == 404 ? "FILE_NOT_FOUND" : fileResult.ErrorCode,
                ErrorMessage = fileResult.StatusCode == 404 ? $"File {ntFileNum} not found." : fileResult.ErrorMessage
            };
        }

        var fileStatus = fileResult.Data!;

        // 3. Check for cached review (unless ForceRefresh)
        if (request?.ForceRefresh != true)
        {
            var cachedResult = await _repository.GetCachedAiReviewAsync(ntFileNum);
            if (cachedResult.IsSuccess && cachedResult.Data != null)
            {
                _logger.LogInformation("Returning cached AI review for file {NtFileNum}", ntFileNum);
                cachedResult.Data.IsCached = true;
                return cachedResult;
            }
        }

        // 4. Load file specification
        var validationConfig = _validationConfigProvider.GetConfig(fileStatus.FileType);

        // 5. Get file path and sample content
        var filePath = await ResolveFilePathAsync(ntFileNum);
        if (filePath == null || !File.Exists(filePath))
        {
            return new DataResult<AiReviewResponse>
            {
                StatusCode = 404,
                ErrorCode = "FILE_NOT_FOUND",
                ErrorMessage = $"File content not found on disk for file {ntFileNum}."
            };
        }

        var sample = await SampleFileContentAsync(filePath, _options.MaxSampleRecords);

        // 6. Load existing validation summary
        var validationSummary = await _repository.GetValidationSummaryAsync(ntFileNum);

        // 7. Load example file
        string? exampleContent = request?.ExampleFileContent;
        if (string.IsNullOrEmpty(exampleContent))
        {
            var exampleResult = await _repository.GetExampleFileAsync(fileStatus.FileType);
            if (exampleResult.IsSuccess && exampleResult.Data != null)
            {
                var examplePath = exampleResult.Data.FilePath;
                if (File.Exists(examplePath))
                {
                    try
                    {
                        exampleContent = await File.ReadAllTextAsync(examplePath);
                        // Limit example content to avoid huge prompts
                        if (exampleContent.Length > 10000)
                            exampleContent = exampleContent[..10000] + "\n... (truncated)";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not read example file at {Path}", examplePath);
                    }
                }
            }
        }

        // 8. Build prompt
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(fileStatus, validationConfig, sample, validationSummary.Data, exampleContent, request?.FocusAreas);

        // 9. Call Claude API
        var apiResult = await CallClaudeApiAsync(domainConfig, systemPrompt, userPrompt);
        if (!apiResult.IsSuccess)
            return Forward<AiReviewResponse>(apiResult);

        var (reviewResult, usage) = apiResult.Data!;

        // 10. Build response
        var response = new AiReviewResponse
        {
            NtFileNum = ntFileNum,
            FileType = fileStatus.FileType,
            OverallAssessment = reviewResult.OverallAssessment,
            Summary = reviewResult.Summary,
            Issues = reviewResult.Issues,
            RecordsSampled = sample.RecordsSampled,
            TotalRecords = sample.TotalRecords,
            ReviewedAt = DateTime.Now,
            IsCached = false,
            Usage = usage
        };

        // 11. Store result and increment count
        var expiresAt = DateTime.Now.AddMinutes(_options.CacheDurationMinutes);
        await _repository.StoreAiReviewAsync(response, securityContext.UserCode ?? "system", expiresAt);
        await _repository.IncrementAiReviewCountAsync(domain);

        _logger.LogInformation("AI review completed for file {NtFileNum}: {Assessment}, {IssueCount} issues",
            ntFileNum, response.OverallAssessment, response.Issues.Count);

        return new DataResult<AiReviewResponse>
        {
            StatusCode = 200,
            Data = response
        };
    }

    public async Task<DataResult<AiReviewResponse>> ReviewContentAsync(
        AiContentReviewRequest request, SecurityContext securityContext)
    {
        var domain = securityContext.Domain ?? string.Empty;

        // 1. Validate domain config (same checks as file review)
        var configCheck = await ValidateDomainConfigAsync(domain);
        if (!configCheck.IsSuccess)
            return Forward<AiReviewResponse>(configCheck);
        var domainConfig = configCheck.Data!;

        if (string.IsNullOrWhiteSpace(request.FileContent))
        {
            return new DataResult<AiReviewResponse>
            {
                StatusCode = 400,
                ErrorCode = "VALIDATION_ERROR",
                ErrorMessage = "FileContent is required."
            };
        }

        // 2. Sample from provided content
        var lines = request.FileContent.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var sample = SampleFromLines(lines, _options.MaxSampleRecords);

        // 3. Load file spec if file type provided
        FileValidationConfig? validationConfig = null;
        if (!string.IsNullOrEmpty(request.FileTypeCode))
            validationConfig = _validationConfigProvider.GetConfig(request.FileTypeCode);

        // 4. Load example file
        string? exampleContent = request.ExampleFileContent;
        if (string.IsNullOrEmpty(exampleContent) && !string.IsNullOrEmpty(request.FileTypeCode))
        {
            var exampleResult = await _repository.GetExampleFileAsync(request.FileTypeCode);
            if (exampleResult.IsSuccess && exampleResult.Data != null && File.Exists(exampleResult.Data.FilePath))
            {
                try
                {
                    exampleContent = await File.ReadAllTextAsync(exampleResult.Data.FilePath);
                    if (exampleContent.Length > 10000)
                        exampleContent = exampleContent[..10000] + "\n... (truncated)";
                }
                catch { /* ignore */ }
            }
        }

        // 5. Build prompt — use a lightweight FileStatusResponse for prompt construction
        var pseudoStatus = new FileStatusResponse
        {
            FileType = request.FileTypeCode ?? "UNKNOWN",
            FileName = request.FileName ?? "uploaded-content"
        };

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(pseudoStatus, validationConfig, sample, null, exampleContent, request.FocusAreas);

        // 6. Call Claude API
        var apiResult = await CallClaudeApiAsync(domainConfig, systemPrompt, userPrompt);
        if (!apiResult.IsSuccess)
            return Forward<AiReviewResponse>(apiResult);

        var (reviewResult, usage) = apiResult.Data!;

        // 7. Build response (no caching for ad-hoc content reviews)
        var response = new AiReviewResponse
        {
            NtFileNum = 0,
            FileType = request.FileTypeCode ?? "UNKNOWN",
            OverallAssessment = reviewResult.OverallAssessment,
            Summary = reviewResult.Summary,
            Issues = reviewResult.Issues,
            RecordsSampled = sample.RecordsSampled,
            TotalRecords = sample.TotalRecords,
            ReviewedAt = DateTime.Now,
            IsCached = false,
            Usage = usage
        };

        await _repository.IncrementAiReviewCountAsync(domain);

        _logger.LogInformation("AI content review completed: {Assessment}, {IssueCount} issues",
            response.OverallAssessment, response.Issues.Count);

        return new DataResult<AiReviewResponse>
        {
            StatusCode = 200,
            Data = response
        };
    }

    public async Task<DataResult<AiReviewResponse>> GetCachedReviewAsync(int ntFileNum)
    {
        return await _repository.GetCachedAiReviewAsync(ntFileNum);
    }

    // ============================================
    // Example File CRUD
    // ============================================

    public async Task<DataResult<List<ExampleFileRecord>>> ListExampleFilesAsync()
    {
        return await _repository.GetAllExampleFilesAsync();
    }

    public async Task<DataResult<ExampleFileRecord>> GetExampleFileAsync(string fileTypeCode)
    {
        return await _repository.GetExampleFileAsync(fileTypeCode);
    }

    public async Task<DataResult<ExampleFileRecord>> SaveExampleFileAsync(
        string fileTypeCode, ExampleFileRequest request, SecurityContext securityContext)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            return new DataResult<ExampleFileRecord>
            {
                StatusCode = 400,
                ErrorCode = "VALIDATION_ERROR",
                ErrorMessage = "FilePath is required."
            };
        }

        if (!File.Exists(request.FilePath))
        {
            return new DataResult<ExampleFileRecord>
            {
                StatusCode = 400,
                ErrorCode = "EXAMPLE_FILE_NOT_FOUND",
                ErrorMessage = $"Example file path does not exist: {request.FilePath}"
            };
        }

        var record = new ExampleFileRecord
        {
            FileTypeCode = fileTypeCode,
            FilePath = request.FilePath,
            Description = request.Description,
            UpdatedAt = DateTime.Now,
            UpdatedBy = securityContext.UserCode
        };

        var result = await _repository.UpsertExampleFileAsync(record);
        if (!result.IsSuccess)
        {
            return new DataResult<ExampleFileRecord>
            {
                StatusCode = result.StatusCode,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };
        }

        return new DataResult<ExampleFileRecord>
        {
            StatusCode = 200,
            Data = record
        };
    }

    public async Task<RawCommandResult> DeleteExampleFileAsync(string fileTypeCode)
    {
        return await _repository.DeleteExampleFileAsync(fileTypeCode);
    }

    // ============================================
    // Domain AI Config CRUD
    // ============================================

    public async Task<DataResult<AiDomainConfig>> GetDomainConfigAsync(string domain)
    {
        var result = await _repository.GetAiDomainConfigAsync(domain);
        if (result.IsSuccess && result.Data != null)
        {
            // Mask API key for display
            result.Data.ApiKey = MaskApiKey(result.Data.ApiKey);
        }
        return result;
    }

    public async Task<DataResult<AiDomainConfig>> SaveDomainConfigAsync(
        string domain, AiDomainConfigRequest request, SecurityContext securityContext)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return new DataResult<AiDomainConfig>
            {
                StatusCode = 400,
                ErrorCode = "VALIDATION_ERROR",
                ErrorMessage = "ApiKey is required."
            };
        }

        var config = new AiDomainConfig
        {
            Domain = domain,
            ApiKey = request.ApiKey,
            Model = request.Model ?? "claude-sonnet-4-20250514",
            Enabled = request.Enabled,
            MaxReviewsPerDay = request.MaxReviewsPerDay ?? 50,
            MaxOutputTokens = request.MaxOutputTokens ?? 4096,
            CreatedAt = DateTime.Now,
            CreatedBy = securityContext.UserCode,
            UpdatedAt = DateTime.Now,
            UpdatedBy = securityContext.UserCode
        };

        var result = await _repository.UpsertAiDomainConfigAsync(config);
        if (!result.IsSuccess)
        {
            return new DataResult<AiDomainConfig>
            {
                StatusCode = result.StatusCode,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };
        }

        config.ApiKey = MaskApiKey(config.ApiKey);
        return new DataResult<AiDomainConfig>
        {
            StatusCode = 200,
            Data = config
        };
    }

    public async Task<RawCommandResult> DeleteDomainConfigAsync(string domain)
    {
        return await _repository.DeleteAiDomainConfigAsync(domain);
    }

    public async Task<DataResult<AiConfigStatusResponse>> GetConfigStatusAsync(string domain)
    {
        var configResult = await _repository.GetAiDomainConfigAsync(domain);

        if (configResult.StatusCode == 404 || configResult.Data == null)
        {
            return new DataResult<AiConfigStatusResponse>
            {
                StatusCode = 200,
                Data = new AiConfigStatusResponse
                {
                    IsConfigured = false,
                    IsEnabled = false
                }
            };
        }

        if (!configResult.IsSuccess)
            return Forward<AiConfigStatusResponse>(configResult);

        var config = configResult.Data;

        // Reset count if needed
        var reviewsToday = config.ReviewsToday;
        if (config.ReviewsResetDt == null || config.ReviewsResetDt.Value.Date < DateTime.Today)
            reviewsToday = 0;

        return new DataResult<AiConfigStatusResponse>
        {
            StatusCode = 200,
            Data = new AiConfigStatusResponse
            {
                IsConfigured = !string.IsNullOrWhiteSpace(config.ApiKey),
                IsEnabled = config.Enabled,
                Model = config.Model,
                ReviewsToday = reviewsToday,
                MaxReviewsPerDay = config.MaxReviewsPerDay
            }
        };
    }

    // ============================================
    // Domain Config Validation (shared)
    // ============================================

    private async Task<DataResult<AiDomainConfig>> ValidateDomainConfigAsync(string domain)
    {
        var configResult = await _repository.GetAiDomainConfigAsync(domain);
        if (configResult.StatusCode == 404)
        {
            return new DataResult<AiDomainConfig>
            {
                StatusCode = 400,
                ErrorCode = "AI_NOT_CONFIGURED",
                ErrorMessage = "AI review has not been configured for this domain. Use PUT /ai-review/config to set up your API key."
            };
        }
        if (!configResult.IsSuccess)
            return configResult;

        var domainConfig = configResult.Data!;

        if (!domainConfig.Enabled)
        {
            return new DataResult<AiDomainConfig>
            {
                StatusCode = 400,
                ErrorCode = "AI_REVIEW_DISABLED",
                ErrorMessage = "AI review is disabled for this domain."
            };
        }

        if (string.IsNullOrWhiteSpace(domainConfig.ApiKey))
        {
            return new DataResult<AiDomainConfig>
            {
                StatusCode = 400,
                ErrorCode = "AI_NOT_CONFIGURED",
                ErrorMessage = "No API key configured. Use PUT /ai-review/config to set your Anthropic API key."
            };
        }

        // Reset daily count if needed
        if (domainConfig.ReviewsResetDt == null || domainConfig.ReviewsResetDt.Value.Date < DateTime.Today)
        {
            await _repository.ResetAiReviewCountAsync(domain);
            domainConfig.ReviewsToday = 0;
        }

        if (domainConfig.ReviewsToday >= domainConfig.MaxReviewsPerDay)
        {
            return new DataResult<AiDomainConfig>
            {
                StatusCode = 429,
                ErrorCode = "AI_RATE_LIMITED",
                ErrorMessage = $"Daily review limit reached ({domainConfig.ReviewsToday}/{domainConfig.MaxReviewsPerDay}). Resets at midnight."
            };
        }

        return configResult;
    }

    // ============================================
    // File Path Resolution
    // ============================================

    private async Task<string?> ResolveFilePathAsync(int ntFileNum)
    {
        // Try to find the file via transfer record (destination_path)
        var transferResult = await _repository.ListTransferRecordsAsync(null, null, null, 100);
        if (transferResult.IsSuccess && transferResult.Data != null)
        {
            var transfer = transferResult.Data.FirstOrDefault(t => t.NtFileNum == ntFileNum);
            if (transfer?.DestinationPath != null && File.Exists(transfer.DestinationPath))
                return transfer.DestinationPath;
        }

        return null;
    }

    // ============================================
    // File Sampling
    // ============================================

    private class FileSample
    {
        public string? HeaderLine { get; set; }
        public string? TrailerLine { get; set; }
        public List<string> FirstRecords { get; set; } = new();
        public List<string> RandomRecords { get; set; } = new();
        public List<string> LastRecords { get; set; } = new();
        public int RecordsSampled { get; set; }
        public int TotalRecords { get; set; }
        public Dictionary<int, FieldStats> FieldStatistics { get; set; } = new();
    }

    private class FieldStats
    {
        public string FieldName { get; set; } = string.Empty;
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public decimal Sum { get; set; }
        public int Count { get; set; }
        public int NullCount { get; set; }
        public decimal Mean => Count > 0 ? Sum / Count : 0;
    }

    private async Task<FileSample> SampleFileContentAsync(string filePath, int maxSampleRecords)
    {
        var sample = new FileSample();
        var allLines = new List<string>();
        var lineCount = 0;

        // Read all lines (streaming)
        using (var reader = new StreamReader(filePath))
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                allLines.Add(line);
                lineCount++;
            }
        }

        if (allLines.Count == 0)
            return sample;

        // Identify header and trailer
        var firstLine = allLines[0];
        var lastLine = allLines[^1];

        // Simple heuristic: header starts with H| or has fewer delimiters than detail records
        if (firstLine.StartsWith("H|") || firstLine.StartsWith("H,") || firstLine.StartsWith("HDR"))
        {
            sample.HeaderLine = firstLine;
        }
        if (lastLine.StartsWith("T|") || lastLine.StartsWith("T,") || lastLine.StartsWith("TRL") || lastLine.StartsWith("FTR"))
        {
            sample.TrailerLine = lastLine;
        }

        // Detail records (exclude header/trailer)
        var detailStart = sample.HeaderLine != null ? 1 : 0;
        var detailEnd = sample.TrailerLine != null ? allLines.Count - 1 : allLines.Count;
        var detailCount = detailEnd - detailStart;

        sample.TotalRecords = detailCount;

        // First 20 records
        var firstCount = Math.Min(20, detailCount);
        for (var i = detailStart; i < detailStart + firstCount; i++)
            sample.FirstRecords.Add(allLines[i]);

        // Last 10 records
        var lastCount = Math.Min(10, detailCount);
        for (var i = Math.Max(detailEnd - lastCount, detailStart + firstCount); i < detailEnd; i++)
            sample.LastRecords.Add(allLines[i]);

        // Random 50 records (reservoir sampling)
        var randomCount = Math.Min(50, detailCount - firstCount - lastCount);
        if (randomCount > 0)
        {
            var rng = new Random(42); // deterministic seed for reproducibility
            var reservoir = new List<string>();
            var candidateStart = detailStart + firstCount;
            var candidateEnd = detailEnd - lastCount;

            for (var i = candidateStart; i < candidateEnd; i++)
            {
                if (reservoir.Count < randomCount)
                {
                    reservoir.Add(allLines[i]);
                }
                else
                {
                    var j = rng.Next(i - candidateStart + 1);
                    if (j < randomCount)
                        reservoir[j] = allLines[i];
                }
            }
            sample.RandomRecords = reservoir;
        }

        sample.RecordsSampled = sample.FirstRecords.Count + sample.RandomRecords.Count + sample.LastRecords.Count;

        // Field statistics for numeric fields
        ComputeFieldStatistics(sample, allLines, detailStart, detailEnd);

        return sample;
    }

    private void ComputeFieldStatistics(FileSample sample, List<string> allLines, int detailStart, int detailEnd)
    {
        if (detailEnd <= detailStart) return;

        // Detect delimiter from first detail record
        var firstDetail = allLines[detailStart];
        var delimiter = firstDetail.Contains('|') ? '|' : firstDetail.Contains(',') ? ',' : '\t';

        for (var i = detailStart; i < detailEnd; i++)
        {
            var fields = allLines[i].Split(delimiter);
            for (var fieldIdx = 0; fieldIdx < fields.Length; fieldIdx++)
            {
                if (!sample.FieldStatistics.ContainsKey(fieldIdx))
                    sample.FieldStatistics[fieldIdx] = new FieldStats { FieldName = $"Field{fieldIdx}" };

                var stats = sample.FieldStatistics[fieldIdx];
                var value = fields[fieldIdx].Trim();

                if (string.IsNullOrEmpty(value))
                {
                    stats.NullCount++;
                    continue;
                }

                if (decimal.TryParse(value, out var numValue))
                {
                    stats.Count++;
                    stats.Sum += numValue;
                    if (stats.Min == null || numValue < stats.Min) stats.Min = numValue;
                    if (stats.Max == null || numValue > stats.Max) stats.Max = numValue;
                }
            }
        }
    }

    /// <summary>
    /// Sample from an in-memory list of lines (for pasted/uploaded content).
    /// Reuses the same sampling logic as the file-based method.
    /// </summary>
    private FileSample SampleFromLines(List<string> allLines, int maxSampleRecords)
    {
        var sample = new FileSample();
        if (allLines.Count == 0)
            return sample;

        var firstLine = allLines[0];
        var lastLine = allLines[^1];

        if (firstLine.StartsWith("H|") || firstLine.StartsWith("H,") || firstLine.StartsWith("HDR"))
            sample.HeaderLine = firstLine;
        if (lastLine.StartsWith("T|") || lastLine.StartsWith("T,") || lastLine.StartsWith("TRL") || lastLine.StartsWith("FTR"))
            sample.TrailerLine = lastLine;

        var detailStart = sample.HeaderLine != null ? 1 : 0;
        var detailEnd = sample.TrailerLine != null ? allLines.Count - 1 : allLines.Count;
        var detailCount = detailEnd - detailStart;

        sample.TotalRecords = detailCount;

        var firstCount = Math.Min(20, detailCount);
        for (var i = detailStart; i < detailStart + firstCount; i++)
            sample.FirstRecords.Add(allLines[i]);

        var lastCount = Math.Min(10, detailCount);
        for (var i = Math.Max(detailEnd - lastCount, detailStart + firstCount); i < detailEnd; i++)
            sample.LastRecords.Add(allLines[i]);

        var randomCount = Math.Min(50, detailCount - firstCount - lastCount);
        if (randomCount > 0)
        {
            var rng = new Random(42);
            var reservoir = new List<string>();
            var candidateStart = detailStart + firstCount;
            var candidateEnd = detailEnd - lastCount;
            for (var i = candidateStart; i < candidateEnd; i++)
            {
                if (reservoir.Count < randomCount)
                    reservoir.Add(allLines[i]);
                else
                {
                    var j = rng.Next(i - candidateStart + 1);
                    if (j < randomCount)
                        reservoir[j] = allLines[i];
                }
            }
            sample.RandomRecords = reservoir;
        }

        sample.RecordsSampled = sample.FirstRecords.Count + sample.RandomRecords.Count + sample.LastRecords.Count;
        ComputeFieldStatistics(sample, allLines, detailStart, detailEnd);

        return sample;
    }

    // ============================================
    // Prompt Construction
    // ============================================

    private static string BuildSystemPrompt()
    {
        return @"You are a data quality analyst for a telecom billing system. Your task is to analyze file content against its specification and identify semantic issues, pattern anomalies, data quality concerns, and format drift that rule-based validation might miss.

Return your analysis as a JSON object with this exact structure:
{
  ""OverallAssessment"": ""Good|Acceptable|Concerns|Poor"",
  ""Summary"": ""A narrative paragraph summarizing your findings"",
  ""Issues"": [
    {
      ""Severity"": ""Critical|Warning|Info"",
      ""Category"": ""Semantic|Pattern|DataQuality|FormatDrift"",
      ""Description"": ""Clear description of the issue"",
      ""AffectedField"": ""field name or null"",
      ""Examples"": [""example values""],
      ""Suggestion"": ""How to fix or investigate""
    }
  ]
}

Return ONLY the JSON object, no markdown fences or other text.";
    }

    private string BuildUserPrompt(
        FileStatusResponse fileStatus,
        FileValidationConfig? validationConfig,
        FileSample sample,
        ValidationSummaryForAI? validationSummary,
        string? exampleContent,
        List<string>? focusAreas)
    {
        var sb = new StringBuilder();

        // 1. File Specification
        sb.AppendLine("## File Specification");
        sb.AppendLine($"File Type: {fileStatus.FileType}");
        sb.AppendLine($"File Name: {fileStatus.FileName}");
        sb.AppendLine($"Total Records: {sample.TotalRecords}");

        if (validationConfig != null)
        {
            sb.AppendLine($"Delimiter: pipe-delimited");
            if (validationConfig.FieldRules?.Count > 0)
            {
                sb.AppendLine("\nField Rules:");
                foreach (var rule in validationConfig.FieldRules)
                {
                    sb.Append($"  - {rule.EffectiveLabel} (index {rule.FieldIndex}, type {rule.Type}");
                    if (rule.Required) sb.Append(", required");
                    if (rule.MinValue.HasValue) sb.Append($", min={rule.MinValue}");
                    if (rule.MaxValue.HasValue) sb.Append($", max={rule.MaxValue}");
                    if (!string.IsNullOrEmpty(rule.DateFormat)) sb.Append($", format={rule.DateFormat}");
                    if (rule.AllowedValues?.Count > 0) sb.Append($", allowed=[{string.Join(",", rule.AllowedValues)}]");
                    sb.AppendLine(")");
                }
            }
        }

        // 2. Field Statistics
        if (sample.FieldStatistics.Count > 0)
        {
            sb.AppendLine("\n## Field Statistics (computed across all records)");
            foreach (var (idx, stats) in sample.FieldStatistics.OrderBy(k => k.Key))
            {
                if (stats.Count > 0) // only show numeric fields
                {
                    var label = stats.FieldName;
                    if (validationConfig?.FieldRules != null)
                    {
                        var rule = validationConfig.FieldRules.FirstOrDefault(r => r.FieldIndex == idx);
                        if (rule != null) label = rule.EffectiveLabel;
                    }
                    sb.AppendLine($"  {label} (index {idx}): min={stats.Min}, max={stats.Max}, mean={stats.Mean:F2}, nulls={stats.NullCount}, count={stats.Count}");
                }
            }
        }

        // 3. Sample Records
        sb.AppendLine("\n## Sample Records");

        if (sample.HeaderLine != null)
            sb.AppendLine($"Header: {sample.HeaderLine}");

        if (sample.FirstRecords.Count > 0)
        {
            sb.AppendLine($"\nFirst {sample.FirstRecords.Count} records:");
            foreach (var line in sample.FirstRecords)
                sb.AppendLine(line);
        }

        if (sample.RandomRecords.Count > 0)
        {
            sb.AppendLine($"\nRandom sample of {sample.RandomRecords.Count} records from middle of file:");
            foreach (var line in sample.RandomRecords)
                sb.AppendLine(line);
        }

        if (sample.LastRecords.Count > 0)
        {
            sb.AppendLine($"\nLast {sample.LastRecords.Count} records:");
            foreach (var line in sample.LastRecords)
                sb.AppendLine(line);
        }

        if (sample.TrailerLine != null)
            sb.AppendLine($"\nTrailer: {sample.TrailerLine}");

        // 4. Existing validation results
        if (validationSummary != null)
        {
            sb.AppendLine("\n## Existing Rule-Based Validation Results");
            sb.AppendLine($"Status: {validationSummary.OverallStatus}");
            if (validationSummary.MainIssues?.Count > 0)
            {
                sb.AppendLine("Known Issues:");
                foreach (var issue in validationSummary.MainIssues)
                    sb.AppendLine($"  - {issue}");
            }
            sb.AppendLine("\nThese issues are already detected by rule-based validation. Focus on issues NOT covered above.");
        }

        // 5. Example file
        if (!string.IsNullOrEmpty(exampleContent))
        {
            sb.AppendLine("\n## Example File (known good format)");
            sb.AppendLine("Compare the sample records above against this example of correct format:");
            sb.AppendLine(exampleContent);
        }

        // 6. Focus areas
        if (focusAreas?.Count > 0)
        {
            sb.AppendLine("\n## Focus Areas");
            sb.AppendLine("Pay special attention to these areas:");
            foreach (var area in focusAreas)
                sb.AppendLine($"  - {area}");
        }

        return sb.ToString();
    }

    // ============================================
    // Claude API Integration
    // ============================================

    private async Task<DataResult<(ClaudeReviewResult, AiReviewUsage)>> CallClaudeApiAsync(
        AiDomainConfig domainConfig, string systemPrompt, string userPrompt)
    {
        var client = _httpClientFactory.CreateClient("ClaudeApi");

        var requestBody = new ClaudeMessagesRequest
        {
            Model = domainConfig.Model,
            Max_tokens = domainConfig.MaxOutputTokens,
            System = systemPrompt,
            Messages = new List<ClaudeMessage>
            {
                new() { Role = "user", Content = userPrompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody, JsonOpts);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Add("x-api-key", domainConfig.ApiKey);

        _logger.LogDebug("Calling Claude API with model {Model}", domainConfig.Model);

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await client.SendAsync(httpRequest);
        }
        catch (TaskCanceledException)
        {
            return new DataResult<(ClaudeReviewResult, AiReviewUsage)>
            {
                StatusCode = 504,
                ErrorCode = "AI_API_TIMEOUT",
                ErrorMessage = "AI review timed out. Try again or reduce file complexity."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Claude API");
            return new DataResult<(ClaudeReviewResult, AiReviewUsage)>
            {
                StatusCode = 502,
                ErrorCode = "AI_API_ERROR",
                ErrorMessage = "Anthropic API returned an error. Try again later."
            };
        }

        var responseBody = await httpResponse.Content.ReadAsStringAsync();

        if (httpResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new DataResult<(ClaudeReviewResult, AiReviewUsage)>
            {
                StatusCode = 502,
                ErrorCode = "AI_API_AUTH_ERROR",
                ErrorMessage = "The configured API key was rejected by Anthropic. Check your key in PUT /ai-review/config."
            };
        }

        if (!httpResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Claude API error: {StatusCode} {Body}", httpResponse.StatusCode, responseBody);
            return new DataResult<(ClaudeReviewResult, AiReviewUsage)>
            {
                StatusCode = 502,
                ErrorCode = "AI_API_ERROR",
                ErrorMessage = "Anthropic API returned an error. Try again later."
            };
        }

        // Parse response
        var claudeResponse = JsonSerializer.Deserialize<ClaudeMessagesResponse>(responseBody, JsonOpts);
        if (claudeResponse == null || claudeResponse.Content == null || claudeResponse.Content.Count == 0)
        {
            return new DataResult<(ClaudeReviewResult, AiReviewUsage)>
            {
                StatusCode = 502,
                ErrorCode = "AI_API_ERROR",
                ErrorMessage = "Empty response from Anthropic API."
            };
        }

        var textContent = claudeResponse.Content.FirstOrDefault(c => c.Type == "text")?.Text ?? string.Empty;

        // Strip markdown fences if present
        textContent = textContent.Trim();
        if (textContent.StartsWith("```json"))
            textContent = textContent[7..];
        else if (textContent.StartsWith("```"))
            textContent = textContent[3..];
        if (textContent.EndsWith("```"))
            textContent = textContent[..^3];
        textContent = textContent.Trim();

        ClaudeReviewResult? reviewResult;
        try
        {
            reviewResult = JsonSerializer.Deserialize<ClaudeReviewResult>(textContent, JsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Claude review JSON: {Text}", textContent[..Math.Min(500, textContent.Length)]);
            // Fallback: wrap raw text as summary
            reviewResult = new ClaudeReviewResult
            {
                OverallAssessment = "Acceptable",
                Summary = textContent[..Math.Min(2000, textContent.Length)],
                Issues = new List<AiReviewIssue>()
            };
        }

        var usage = new AiReviewUsage
        {
            InputTokens = claudeResponse.Usage?.Input_tokens ?? 0,
            OutputTokens = claudeResponse.Usage?.Output_tokens ?? 0,
            Model = domainConfig.Model
        };

        return new DataResult<(ClaudeReviewResult, AiReviewUsage)>
        {
            StatusCode = 200,
            Data = (reviewResult!, usage)
        };
    }

    // ============================================
    // Helpers
    // ============================================

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 12)
            return "***";
        return apiKey[..8] + "..." + apiKey[^4..];
    }

    private static DataResult<T> Forward<T>(DataResult<AiDomainConfig> source)
    {
        return new DataResult<T>
        {
            StatusCode = source.StatusCode,
            ErrorCode = source.ErrorCode,
            ErrorMessage = source.ErrorMessage
        };
    }

    private static DataResult<T> Forward<T>(DataResult<(ClaudeReviewResult, AiReviewUsage)> source)
    {
        return new DataResult<T>
        {
            StatusCode = source.StatusCode,
            ErrorCode = source.ErrorCode,
            ErrorMessage = source.ErrorMessage
        };
    }

    private static DataResult<T> Forward<T>(DataResult<AiConfigStatusResponse> source)
    {
        return new DataResult<T>
        {
            StatusCode = source.StatusCode,
            ErrorCode = source.ErrorCode,
            ErrorMessage = source.ErrorMessage
        };
    }
}
