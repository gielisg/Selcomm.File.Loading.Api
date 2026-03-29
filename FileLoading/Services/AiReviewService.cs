using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
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
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
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
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        IOptions<AiReviewOptions> options,
        ILogger<AiReviewService> logger)
    {
        _repository = repository;
        _validationConfigProvider = validationConfigProvider;
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
        _options = options.Value;
        _logger = logger;
    }

    // ============================================
    // AI Review
    // ============================================

    public async Task<DataResult<AiReviewResponse>> ReviewFileAsync(
        int ntFileNum, AiReviewRequest? request, SecurityContext securityContext)
    {
        // 1. Load file metadata
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

        // 2. Check for cached review (unless ForceRefresh)
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

        // 3. Load file specification
        var validationConfig = _validationConfigProvider.GetConfig(fileStatus.FileType);

        // 4. Get file path and sample content
        var filePath = await ResolveFilePathAsync(ntFileNum);
        if (filePath == null || !File.Exists(filePath))
        {
            return new DataResult<AiReviewResponse>
            {
                StatusCode = 404,
                ErrorCode = "FileLoading.FileNotFound",
                ErrorMessage = $"File content not found on disk for file {ntFileNum}."
            };
        }

        var sample = await SampleFileContentAsync(filePath, _options.MaxSampleRecords);

        // 5. Load existing validation summary
        var validationSummary = await _repository.GetValidationSummaryAsync(ntFileNum);

        // 6. Load example files
        string? exampleContent = request?.ExampleFileContent;
        if (string.IsNullOrEmpty(exampleContent))
        {
            exampleContent = await LoadExampleContentForTypeAsync(fileStatus.FileType);
        }

        // 7. Build prompt
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(fileStatus, validationConfig, sample, validationSummary.Data, exampleContent, request?.FocusAreas);

        // 8. Call AI Gateway
        var apiResult = await CallGatewayAsync(systemPrompt, userPrompt, "file-review");
        if (!apiResult.IsSuccess)
            return Forward<AiReviewResponse>(apiResult);

        var (textContent, usage) = apiResult.Data!;

        // 9. Parse review result
        var reviewResult = ParseReviewResult(textContent);

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

        // 11. Store result
        var expiresAt = DateTime.Now.AddMinutes(_options.CacheDurationMinutes);
        await _repository.StoreAiReviewAsync(response, securityContext.FullName ?? securityContext.UserCode ?? "system", expiresAt);

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
        if (string.IsNullOrWhiteSpace(request.FileContent))
        {
            return new DataResult<AiReviewResponse>
            {
                StatusCode = 400,
                ErrorCode = "FileLoading.ValidationError",
                ErrorMessage = "FileContent is required."
            };
        }

        // 1. Sample from provided content
        var lines = request.FileContent.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var sample = SampleFromLines(lines, _options.MaxSampleRecords);

        // 2. Load file spec if file type provided
        FileValidationConfig? validationConfig = null;
        if (!string.IsNullOrEmpty(request.FileTypeCode))
            validationConfig = _validationConfigProvider.GetConfig(request.FileTypeCode);

        // 3. Load example files
        string? exampleContent = request.ExampleFileContent;
        if (string.IsNullOrEmpty(exampleContent) && !string.IsNullOrEmpty(request.FileTypeCode))
        {
            exampleContent = await LoadExampleContentForTypeAsync(request.FileTypeCode);
        }

        // 4. Build prompt
        var pseudoStatus = new FileStatusResponse
        {
            FileType = request.FileTypeCode ?? "UNKNOWN",
            FileName = request.FileName ?? "uploaded-content"
        };

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(pseudoStatus, validationConfig, sample, null, exampleContent, request.FocusAreas);

        // 5. Call AI Gateway
        var apiResult = await CallGatewayAsync(systemPrompt, userPrompt, "file-review");
        if (!apiResult.IsSuccess)
            return Forward<AiReviewResponse>(apiResult);

        var (textContent, usage) = apiResult.Data!;

        // 6. Parse and build response (no caching for ad-hoc content reviews)
        var reviewResult = ParseReviewResult(textContent);

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
    // AI Analysis Results (persisted per file-type)
    // ============================================

    public async Task<DataResult<List<AiAnalysisResultRecord>>> GetAnalysisResultsAsync(string fileTypeCode)
    {
        return await _repository.GetAnalysisResultsAsync(fileTypeCode);
    }

    public async Task<DataResult<AiAnalysisResultRecord>> GetAnalysisResultAsync(int analysisId)
    {
        return await _repository.GetAnalysisResultAsync(analysisId);
    }

    public async Task<DataResult<AiAnalysisResultRecord>> UpdateAnalysisResultAsync(
        int analysisId, AiAnalysisResultUpdateRequest request, SecurityContext securityContext)
    {
        var existing = await _repository.GetAnalysisResultAsync(analysisId);
        if (!existing.IsSuccess || existing.Data == null)
            return existing;

        var record = existing.Data;
        if (request.Summary != null) record.Summary = request.Summary;
        if (request.IngestionReadiness != null) record.IngestionReadiness = request.IngestionReadiness;
        if (request.AnalysisJson != null) record.AnalysisJson = request.AnalysisJson;
        record.UpdatedBy = securityContext.FullName ?? securityContext.UserCode;

        var result = await _repository.UpdateAnalysisResultAsync(record);
        if (!result.IsSuccess)
            return new DataResult<AiAnalysisResultRecord> { StatusCode = result.StatusCode, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };

        return new DataResult<AiAnalysisResultRecord> { StatusCode = 200, Data = record };
    }

    public async Task<RawCommandResult> DeleteAnalysisResultAsync(int analysisId)
    {
        return await _repository.DeleteAnalysisResultAsync(analysisId);
    }

    // ============================================
    // AI File-Type Prompts (versioned per file-type)
    // ============================================

    public async Task<DataResult<List<AiFileTypePromptRecord>>> GetFileTypePromptsAsync(string fileTypeCode)
    {
        return await _repository.GetFileTypePromptsAsync(fileTypeCode);
    }

    public async Task<DataResult<AiFileTypePromptRecord>> GetCurrentFileTypePromptAsync(string fileTypeCode)
    {
        return await _repository.GetCurrentFileTypePromptAsync(fileTypeCode);
    }

    public async Task<DataResult<AiFileTypePromptRecord>> CreateFileTypePromptAsync(
        string fileTypeCode, AiFileTypePromptCreateRequest request, SecurityContext securityContext)
    {
        if (string.IsNullOrWhiteSpace(request.PromptContent))
            return new DataResult<AiFileTypePromptRecord> { StatusCode = 400, ErrorCode = "VALIDATION_ERROR", ErrorMessage = "PromptContent is required" };

        // Get next version number
        var existing = await _repository.GetFileTypePromptsAsync(fileTypeCode);
        var nextVersion = (existing.Data?.Count > 0 ? existing.Data.Max(p => p.Version) : 0) + 1;

        // Deactivate existing current and make this one current
        await _repository.ActivateFileTypePromptAsync(fileTypeCode, -1); // deactivate all

        var record = new AiFileTypePromptRecord
        {
            FileTypeCode = fileTypeCode,
            PromptContent = request.PromptContent,
            IsCurrent = true,
            Version = nextVersion,
            Description = request.Description,
            Source = "USER",
            CreatedBy = securityContext.FullName ?? securityContext.UserCode ?? "SYSTEM"
        };

        var result = await _repository.InsertFileTypePromptAsync(record);
        if (!result.IsSuccess)
            return new DataResult<AiFileTypePromptRecord> { StatusCode = result.StatusCode, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };

        record.PromptId = result.Value;
        return new DataResult<AiFileTypePromptRecord> { StatusCode = 201, Data = record };
    }

    public async Task<DataResult<AiFileTypePromptRecord>> GenerateFileTypePromptAsync(
        string fileTypeCode, AiFileTypePromptGenerateRequest request, SecurityContext securityContext)
    {
        // 1. Load the specified analysis result
        var analysisResult = await _repository.GetAnalysisResultAsync(request.AnalysisId);
        if (!analysisResult.IsSuccess || analysisResult.Data == null)
        {
            return new DataResult<AiFileTypePromptRecord>
            {
                StatusCode = 404,
                ErrorCode = "FileLoading.AnalysisNotFound",
                ErrorMessage = $"Analysis result {request.AnalysisId} not found"
            };
        }

        // 2. Load file-class instructions as base
        var fileTypeResult = await _repository.GetFileTypeRecordAsync(fileTypeCode);
        var fileClassCode = fileTypeResult.Data?.FileClassCode ?? "CHG";
        var classInstructions = await LoadInstructionContentAsync(fileClassCode);

        // 3. Ask AI to generate a file-type-specific prompt
        var systemPrompt = @"You are a billing file analysis expert. Generate a detailed validation/parsing prompt for a specific file type based on the analysis results provided. The prompt should:
1. Describe the exact file structure (columns, types, delimiters)
2. Specify validation rules for each column
3. Define the billing concept mappings
4. Include data quality checks specific to this file type
5. Be ready to use for validating/parsing future instances of this file type

Return ONLY the prompt text as markdown, no JSON wrapping.";

        var userPrompt = $@"## File Type: {fileTypeCode}
## File Class: {fileClassCode}

## Base File-Class Instructions:
{classInstructions ?? "(none)"}

## Analysis Results:
{analysisResult.Data.AnalysisJson ?? analysisResult.Data.Summary ?? "(no analysis data)"}

Generate a detailed, file-type-specific validation/parsing prompt based on the above analysis.";

        var apiResult = await CallGatewayAsync(systemPrompt, userPrompt, "prompt-generation", 8192);
        if (!apiResult.IsSuccess)
            return Forward<AiFileTypePromptRecord>(apiResult);

        var (promptContent, _) = apiResult.Data!;
        promptContent = SanitizeForInformix(promptContent);

        // 4. Get next version number
        var existing = await _repository.GetFileTypePromptsAsync(fileTypeCode);
        var nextVersion = (existing.Data?.Count > 0 ? existing.Data.Max(p => p.Version) : 0) + 1;

        // 5. Deactivate existing current
        await _repository.ActivateFileTypePromptAsync(fileTypeCode, -1);

        // 6. Save new prompt as current
        var record = new AiFileTypePromptRecord
        {
            FileTypeCode = fileTypeCode,
            PromptContent = promptContent,
            IsCurrent = true,
            Version = nextVersion,
            Description = $"Auto-generated from analysis #{request.AnalysisId}",
            Source = "AI",
            CreatedBy = securityContext.FullName ?? securityContext.UserCode ?? "SYSTEM"
        };

        var insertResult = await _repository.InsertFileTypePromptAsync(record);
        if (!insertResult.IsSuccess)
            return new DataResult<AiFileTypePromptRecord> { StatusCode = insertResult.StatusCode, ErrorCode = insertResult.ErrorCode, ErrorMessage = insertResult.ErrorMessage };

        record.PromptId = insertResult.Value;

        _logger.LogInformation("Generated file-type prompt for {FileTypeCode} v{Version} from analysis #{AnalysisId}",
            fileTypeCode, nextVersion, request.AnalysisId);

        return new DataResult<AiFileTypePromptRecord> { StatusCode = 201, Data = record };
    }

    public async Task<DataResult<AiFileTypePromptRecord>> UpdateFileTypePromptAsync(
        string fileTypeCode, int promptId, AiFileTypePromptUpdateRequest request, SecurityContext securityContext)
    {
        var existing = await _repository.GetFileTypePromptAsync(promptId);
        if (!existing.IsSuccess || existing.Data == null)
            return existing;

        if (request.PromptContent != null) existing.Data.PromptContent = request.PromptContent;
        if (request.Description != null) existing.Data.Description = request.Description;
        existing.Data.Source = "USER";
        existing.Data.UpdatedBy = securityContext.FullName ?? securityContext.UserCode;

        var result = await _repository.UpdateFileTypePromptAsync(existing.Data);
        if (!result.IsSuccess)
            return new DataResult<AiFileTypePromptRecord> { StatusCode = result.StatusCode, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };

        return new DataResult<AiFileTypePromptRecord> { StatusCode = 200, Data = existing.Data };
    }

    public async Task<RawCommandResult> ActivateFileTypePromptAsync(string fileTypeCode, int promptId)
    {
        return await _repository.ActivateFileTypePromptAsync(fileTypeCode, promptId);
    }

    public async Task<RawCommandResult> DeleteFileTypePromptAsync(int promptId)
    {
        return await _repository.DeleteFileTypePromptAsync(promptId);
    }

    // ============================================
    // AI Instruction File CRUD
    // ============================================

    public async Task<DataResult<List<AiInstructionFileRecord>>> ListInstructionFilesAsync()
    {
        return await _repository.GetAllInstructionFilesAsync();
    }

    public async Task<DataResult<AiInstructionFileRecord>> GetInstructionFileAsync(string fileClassCode)
    {
        // Try DB first
        var dbResult = await _repository.GetInstructionFileAsync(fileClassCode);
        if (dbResult.IsSuccess)
            return dbResult;

        // Fall back to default file
        var defaultContent = LoadDefaultInstructionFile(fileClassCode);
        if (defaultContent != null)
        {
            return new DataResult<AiInstructionFileRecord>
            {
                StatusCode = 200,
                Data = new AiInstructionFileRecord
                {
                    FileClassCode = fileClassCode,
                    InstructionContent = defaultContent,
                    IsDefault = true,
                    Description = $"Default {fileClassCode} analysis instructions"
                }
            };
        }

        return new DataResult<AiInstructionFileRecord>
        {
            StatusCode = 404,
            ErrorCode = "FileLoading.InstructionNotFound",
            ErrorMessage = $"No instruction file found for file class '{fileClassCode}'"
        };
    }

    public DataResult<AiInstructionFileRecord> GetDefaultInstructionFile(string fileClassCode)
    {
        var defaultContent = LoadDefaultInstructionFile(fileClassCode);
        if (defaultContent != null)
        {
            return new DataResult<AiInstructionFileRecord>
            {
                StatusCode = 200,
                Data = new AiInstructionFileRecord
                {
                    FileClassCode = fileClassCode,
                    InstructionContent = defaultContent,
                    IsDefault = true,
                    Description = $"Default {fileClassCode} analysis instructions"
                }
            };
        }

        return new DataResult<AiInstructionFileRecord>
        {
            StatusCode = 404,
            ErrorCode = "FileLoading.InstructionNotFound",
            ErrorMessage = $"No default instruction file found for file class '{fileClassCode}'"
        };
    }

    public async Task<DataResult<AiInstructionFileRecord>> SaveInstructionFileAsync(
        string fileClassCode, AiInstructionFileRequest request, SecurityContext securityContext)
    {
        if (string.IsNullOrWhiteSpace(request.InstructionContent))
        {
            return new DataResult<AiInstructionFileRecord>
            {
                StatusCode = 400,
                ErrorCode = "FileLoading.ValidationError",
                ErrorMessage = "InstructionContent is required."
            };
        }

        var record = new AiInstructionFileRecord
        {
            FileClassCode = fileClassCode,
            InstructionContent = request.InstructionContent,
            IsDefault = false,
            Description = request.Description,
            CreatedBy = securityContext.FullName ?? securityContext.UserCode ?? "SYSTEM",
            UpdatedBy = securityContext.FullName ?? securityContext.UserCode ?? "SYSTEM"
        };

        var result = await _repository.UpsertInstructionFileAsync(record);
        if (!result.IsSuccess)
        {
            return new DataResult<AiInstructionFileRecord>
            {
                StatusCode = result.StatusCode,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };
        }

        return new DataResult<AiInstructionFileRecord>
        {
            StatusCode = 200,
            Data = record
        };
    }

    public async Task<RawCommandResult> DeleteInstructionFileAsync(string fileClassCode)
    {
        return await _repository.DeleteInstructionFileAsync(fileClassCode);
    }

    // ============================================
    // Example File CRUD
    // ============================================

    public async Task<DataResult<List<ExampleFileRecord>>> ListExampleFilesAsync()
    {
        return await _repository.GetAllExampleFilesAsync();
    }

    public async Task<DataResult<List<ExampleFileRecord>>> GetExampleFilesByTypeAsync(string fileTypeCode)
    {
        return await _repository.GetExampleFilesByTypeAsync(fileTypeCode);
    }

    public async Task<DataResult<ExampleFileRecord>> UploadExampleFileAsync(
        string fileTypeCode, IFormFile file, string? description, SecurityContext securityContext)
    {
        // Resolve example folder path
        var rootBase = _configuration["LocalStorage:BasePath"] ?? "/var/www";
        var domain = securityContext.Domain ?? "default";
        var exampleFolder = $"{rootBase}/{domain}/files/{fileTypeCode}/example";

        // Ensure directory exists
        Directory.CreateDirectory(exampleFolder);

        // Save uploaded file to disk
        var fileName = Path.GetFileName(file.FileName);
        var filePath = $"{exampleFolder}/{fileName}";

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Insert DB record
        var record = new ExampleFileRecord
        {
            FileTypeCode = fileTypeCode,
            FilePath = filePath,
            FileName = fileName,
            Description = description,
            CreatedBy = securityContext.FullName ?? securityContext.UserCode ?? "SYSTEM",
            UpdatedBy = securityContext.FullName ?? securityContext.UserCode ?? "SYSTEM"
        };

        var result = await _repository.InsertExampleFileAsync(record);
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

    public async Task<DataResult<ExampleFileRecord>> DeleteExampleFileAsync(int exampleFileId)
    {
        // Look up the record to get the file path
        var existing = await _repository.GetExampleFileByIdAsync(exampleFileId);
        if (!existing.IsSuccess || existing.Data == null)
        {
            return new DataResult<ExampleFileRecord>
            {
                StatusCode = 404,
                ErrorCode = "FileLoading.ExampleFileNotFound",
                ErrorMessage = $"No example file found with ID {exampleFileId}"
            };
        }

        // Delete from database
        var deleteResult = await _repository.DeleteExampleFileAsync(exampleFileId);
        if (!deleteResult.IsSuccess)
        {
            return new DataResult<ExampleFileRecord>
            {
                StatusCode = deleteResult.StatusCode,
                ErrorCode = deleteResult.ErrorCode,
                ErrorMessage = deleteResult.ErrorMessage
            };
        }

        // Delete the physical file from disk
        if (!string.IsNullOrEmpty(existing.Data.FilePath) && File.Exists(existing.Data.FilePath))
        {
            try
            {
                File.Delete(existing.Data.FilePath);
                _logger.LogInformation("Deleted example file from disk: {FilePath}", existing.Data.FilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete example file from disk: {FilePath}", existing.Data.FilePath);
            }
        }

        return new DataResult<ExampleFileRecord>
        {
            StatusCode = 200,
            Data = existing.Data
        };
    }

    private async Task<string?> LoadExampleContentForTypeAsync(string fileTypeCode)
    {
        var exampleResult = await _repository.GetExampleFilesByTypeAsync(fileTypeCode);
        if (!exampleResult.IsSuccess || exampleResult.Data == null || exampleResult.Data.Count == 0)
            return null;

        var parts = new List<string>();
        foreach (var example in exampleResult.Data)
        {
            if (!File.Exists(example.FilePath))
                continue;

            try
            {
                string content;
                var ext = Path.GetExtension(example.FilePath).ToLowerInvariant();

                if (ext is ".xlsx" or ".xls")
                {
                    content = ReadExcelAsText(example.FilePath);
                }
                else
                {
                    content = await File.ReadAllTextAsync(example.FilePath);
                }

                if (content.Length > 10000)
                    content = content[..10000] + "\n... (truncated)";

                if (exampleResult.Data.Count > 1)
                    parts.Add($"--- Example: {example.FileName ?? Path.GetFileName(example.FilePath)} ---\n{content}");
                else
                    parts.Add(content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read example file at {Path}", example.FilePath);
            }
        }

        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }

    /// <summary>
    /// Read an Excel file and convert it to CSV-like text for AI analysis.
    /// Reads up to 100 data rows from each worksheet (up to 3 sheets).
    /// </summary>
    private static string ReadExcelAsText(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var workbook = new XLWorkbook(stream);

        var sb = new StringBuilder();
        var sheetsToRead = Math.Min(workbook.Worksheets.Count, 3);

        for (var s = 0; s < sheetsToRead; s++)
        {
            var worksheet = workbook.Worksheets.Skip(s).First();
            var rangeUsed = worksheet.RangeUsed();
            if (rangeUsed == null) continue;

            if (sheetsToRead > 1)
                sb.AppendLine($"--- Sheet: {worksheet.Name} ---");

            var lastRow = Math.Min(rangeUsed.LastRow().RowNumber(), 101); // header + 100 data rows
            var lastCol = rangeUsed.LastColumn().ColumnNumber();

            for (var r = 1; r <= lastRow; r++)
            {
                var row = worksheet.Row(r);
                var cells = new List<string>();
                for (var c = 1; c <= lastCol; c++)
                {
                    var cell = row.Cell(c);
                    cells.Add(cell.IsEmpty() ? "" : cell.GetFormattedString());
                }
                sb.AppendLine(string.Join(",", cells.Select(v => v.Contains(',') || v.Contains('"') || v.Contains('\n')
                    ? $"\"{v.Replace("\"", "\"\"")}\""
                    : v)));
            }

            if (rangeUsed.LastRow().RowNumber() > 101)
                sb.AppendLine($"... ({rangeUsed.LastRow().RowNumber() - 101} more rows)");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    // ============================================
    // AI File Analysis (discovery/configuration)
    // ============================================

    public async Task<DataResult<AiFileAnalysisResponse>> AnalyseExampleFileAsync(
        string fileTypeCode, AiFileAnalysisRequest? request, SecurityContext securityContext)
    {
        // 1. Look up file type to get file class
        var fileTypeResult = await _repository.GetFileTypeRecordAsync(fileTypeCode);
        if (!fileTypeResult.IsSuccess || fileTypeResult.Data == null)
        {
            return new DataResult<AiFileAnalysisResponse>
            {
                StatusCode = 404,
                ErrorCode = "FileLoading.FileTypeNotFound",
                ErrorMessage = $"File type '{fileTypeCode}' not found."
            };
        }

        var fileClassCode = fileTypeResult.Data.FileClassCode;

        // 2. Load example files for this file type
        var exampleContent = await LoadExampleContentForTypeAsync(fileTypeCode);
        if (string.IsNullOrEmpty(exampleContent))
        {
            return new DataResult<AiFileAnalysisResponse>
            {
                StatusCode = 404,
                ErrorCode = "FileLoading.NoExampleFiles",
                ErrorMessage = $"No example files found for file type '{fileTypeCode}'. Upload one via POST /ai-review/example-files/{fileTypeCode}"
            };
        }

        // 3. Build prompt — either file-class instructions (discovery) or file-type prompt (validation)
        string systemPrompt;
        if (request?.UseFileTypePrompt == true)
        {
            var currentPrompt = await _repository.GetCurrentFileTypePromptAsync(fileTypeCode);
            if (!currentPrompt.IsSuccess || currentPrompt.Data == null)
            {
                return new DataResult<AiFileAnalysisResponse>
                {
                    StatusCode = 404,
                    ErrorCode = "FileLoading.NoCurrentPrompt",
                    ErrorMessage = $"No current file-type prompt found for '{fileTypeCode}'. Generate one first via POST /ai-review/prompts/{fileTypeCode}/generate"
                };
            }
            systemPrompt = BuildAnalysisSystemPrompt(fileClassCode, currentPrompt.Data.PromptContent);
        }
        else
        {
            var instructionContent = await LoadInstructionContentAsync(fileClassCode);
            systemPrompt = BuildAnalysisSystemPrompt(fileClassCode, instructionContent);
        }

        var userPrompt = BuildAnalysisUserPrompt(fileTypeCode, exampleContent, request?.FocusAreas);

        // 4. Call AI Gateway (8192 tokens for analysis — large JSON with many column mappings)
        var apiResult = await CallGatewayAsync(systemPrompt, userPrompt, "file-analysis", 8192);
        if (!apiResult.IsSuccess)
            return Forward<AiFileAnalysisResponse>(apiResult);

        var (textContent, usage) = apiResult.Data!;

        // 5. Parse analysis result
        var analysisResult = ParseAnalysisResult(textContent);

        // 6. Build response
        var response = new AiFileAnalysisResponse
        {
            FileTypeCode = fileTypeCode,
            IngestionReadiness = analysisResult.IngestionReadiness,
            Summary = analysisResult.Summary,
            DetectedFormat = analysisResult.DetectedFormat,
            Columns = analysisResult.Columns,
            BillingConceptMappings = analysisResult.BillingConceptMappings,
            DataQualityIssues = analysisResult.DataQualityIssues,
            Observations = analysisResult.Observations,
            SuggestedParserConfig = analysisResult.SuggestedParserConfig,
            Usage = usage,
            AnalysedAt = DateTime.Now
        };

        // 7. Persist analysis result
        var analysisRecord = new AiAnalysisResultRecord
        {
            FileTypeCode = fileTypeCode,
            IngestionReadiness = response.IngestionReadiness,
            Summary = response.Summary,
            AnalysisJson = SanitizeForInformix(JsonSerializer.Serialize(response, JsonOpts)),
            CreatedBy = securityContext.FullName ?? securityContext.UserCode ?? "SYSTEM"
        };
        var insertResult = await _repository.InsertAnalysisResultAsync(analysisRecord);
        if (insertResult.IsSuccess)
        {
            response.AnalysisId = insertResult.Value;
            _logger.LogInformation("Persisted analysis result {AnalysisId} for {FileTypeCode} (JSON length: {JsonLength})",
                insertResult.Value, fileTypeCode, analysisRecord.AnalysisJson?.Length ?? 0);
        }
        else
        {
            _logger.LogWarning("Failed to persist analysis result for {FileTypeCode}: {ErrorCode} {ErrorMessage}",
                fileTypeCode, insertResult.ErrorCode, insertResult.ErrorMessage);
        }

        _logger.LogInformation("AI file analysis completed for {FileTypeCode}: readiness={Readiness}, {ColumnCount} columns, {MappingCount} mappings",
            fileTypeCode, response.IngestionReadiness, response.Columns.Count, response.BillingConceptMappings.Count);

        return new DataResult<AiFileAnalysisResponse>
        {
            StatusCode = 200,
            Data = response
        };
    }

    // ============================================
    // AI Charge Map Seeding
    // ============================================

    public async Task<DataResult<AiChargeMapSeedResponse>> SeedChargeMapsAsync(
        string fileTypeCode, AiChargeMapSeedRequest? request, SecurityContext securityContext)
    {
        // 1. Validate file type exists and get file class
        var ftResult = await _repository.GetFileTypeRecordAsync(fileTypeCode);
        if (!ftResult.IsSuccess || ftResult.Data == null)
            return new DataResult<AiChargeMapSeedResponse> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"File type '{fileTypeCode}' not found" };

        var fileClassCode = ftResult.Data.FileClassCode?.Trim() ?? "";

        // 2. Load analysis result
        AiAnalysisResultRecord? analysis = null;
        if (request?.AnalysisId != null)
        {
            var arResult = await _repository.GetAnalysisResultAsync(request.AnalysisId.Value);
            if (arResult.IsSuccess) analysis = arResult.Data;
        }
        else
        {
            var arList = await _repository.GetAnalysisResultsAsync(fileTypeCode);
            analysis = arList.Data?.FirstOrDefault();
        }

        if (analysis == null)
            return new DataResult<AiChargeMapSeedResponse> { StatusCode = 404, ErrorCode = "FileLoading.NoAnalysis", ErrorMessage = "No analysis results found for this file type" };

        // 3. Deserialize analysis to extract charge descriptions
        AiFileAnalysisResponse? analysisResponse = null;
        if (!string.IsNullOrEmpty(analysis.AnalysisJson))
        {
            try { analysisResponse = JsonSerializer.Deserialize<AiFileAnalysisResponse>(analysis.AnalysisJson, JsonOpts); }
            catch { /* ignore parse errors */ }
        }

        var chargeDescriptions = new List<string>();
        if (analysisResponse?.Columns != null)
        {
            var chargeCol = analysisResponse.Columns.FirstOrDefault(c =>
                c.SuggestedTargetField?.Equals("ChargeType", StringComparison.OrdinalIgnoreCase) == true);
            if (chargeCol?.SampleValues != null)
                chargeDescriptions.AddRange(chargeCol.SampleValues.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct());
        }

        if (chargeDescriptions.Count == 0)
            return new DataResult<AiChargeMapSeedResponse> { StatusCode = 400, ErrorCode = "FileLoading.NoChargeData", ErrorMessage = "Analysis did not discover a ChargeType column with sample values" };

        // 4. Get charge code dictionary
        var chgCodesResult = await _repository.GetChargeCodesAsync();
        var chargeCodes = chgCodesResult.Data ?? new List<ChargeCodeLookup>();

        // 5. Get cross-reference mappings from sibling file types
        var crossRefMappings = new List<NtflChgMapRecord>();
        if (request?.UseCrossReference != false && !string.IsNullOrEmpty(fileClassCode))
        {
            var crossRefResult = await _repository.GetChargeMapsByFileClassAsync(fileClassCode);
            if (crossRefResult.IsSuccess && crossRefResult.Data != null)
                crossRefMappings = crossRefResult.Data.Where(m => m.FileTypeCode.Trim() != fileTypeCode).ToList();
        }

        // 6. Get existing mappings to avoid duplicates
        var existingResult = await _repository.GetChargeMapsAsync(fileTypeCode);
        var existingPatterns = existingResult.Data?.Select(m => m.FileChgDesc.Trim().ToUpperInvariant()).ToHashSet() ?? new HashSet<string>();
        var maxSeqNo = existingResult.Data?.Max(m => (int?)m.SeqNo) ?? 0;

        // 7. Build AI prompt
        var systemPrompt = BuildChargeMapSeedSystemPrompt();
        var userPrompt = BuildChargeMapSeedUserPrompt(fileTypeCode, chargeDescriptions, chargeCodes, crossRefMappings, existingPatterns);

        // 8. Call AI Gateway
        var apiResult = await CallGatewayAsync(systemPrompt, userPrompt, "charge-map-seed", 4096);
        if (!apiResult.IsSuccess)
            return new DataResult<AiChargeMapSeedResponse> { StatusCode = apiResult.StatusCode, ErrorCode = apiResult.ErrorCode, ErrorMessage = apiResult.ErrorMessage };

        var (textContent, usage) = apiResult.Data!;

        // 9. Parse AI response
        var suggestions = ParseChargeMapSeedResponse(textContent);

        // 10. Validate and persist
        var validChgCodes = chargeCodes.Select(c => c.ChgCode.Trim().ToUpperInvariant()).ToHashSet();
        var chgCodeNarrs = chargeCodes.ToDictionary(c => c.ChgCode.Trim().ToUpperInvariant(), c => c.ChgNarr);
        var created = new List<AiChargeMapSuggestion>();
        var skipped = 0;
        var seqNo = maxSeqNo + 10;
        var userName = securityContext.FullName ?? securityContext.UserCode ?? "AI_SEED";

        foreach (var suggestion in suggestions)
        {
            var chgCodeUpper = suggestion.ChgCode?.Trim().ToUpperInvariant() ?? "";
            if (!validChgCodes.Contains(chgCodeUpper))
            {
                skipped++;
                continue;
            }

            var pattern = suggestion.FileChgDesc?.Trim() ?? "";
            if (string.IsNullOrEmpty(pattern) || existingPatterns.Contains(pattern.ToUpperInvariant()))
            {
                skipped++;
                continue;
            }

            // Insert charge map with AI_SUGGESTED source
            var record = new NtflChgMapRecord
            {
                FileTypeCode = fileTypeCode,
                FileChgDesc = pattern,
                SeqNo = seqNo,
                ChgCode = suggestion.ChgCode!.Trim(),
                Source = "AI_SUGGESTED",
                UpdatedBy = userName
            };

            var insertResult = await _repository.InsertChargeMapAsync(record);
            if (!insertResult.IsSuccess) { skipped++; continue; }

            var chgMapId = insertResult.Value;

            // Insert AI reason
            chgCodeNarrs.TryGetValue(chgCodeUpper, out var narr);
            var reasonRecord = new ChgMapAiReasonRecord
            {
                ChgMapId = chgMapId,
                AnalysisId = analysis.AnalysisId,
                FileChgDesc = suggestion.FileChgDesc ?? "",
                MatchedChgCode = suggestion.ChgCode!.Trim(),
                MatchedChgNarr = narr,
                Confidence = suggestion.Confidence ?? "MEDIUM",
                Reasoning = suggestion.Reasoning ?? "",
                MatchMethod = suggestion.MatchMethod ?? "PATTERN_MATCH",
                CreatedBy = userName
            };
            await _repository.InsertChgMapAiReasonAsync(reasonRecord);

            created.Add(new AiChargeMapSuggestion
            {
                ChgMapId = chgMapId,
                FileChgDesc = pattern,
                ChgCode = suggestion.ChgCode!.Trim(),
                ChgNarr = narr ?? "",
                Confidence = suggestion.Confidence ?? "MEDIUM",
                Reasoning = suggestion.Reasoning ?? "",
                MatchMethod = suggestion.MatchMethod ?? "PATTERN_MATCH"
            });

            existingPatterns.Add(pattern.ToUpperInvariant());
            seqNo += 10;
        }

        return new DataResult<AiChargeMapSeedResponse>
        {
            StatusCode = 201,
            Data = new AiChargeMapSeedResponse
            {
                FileTypeCode = fileTypeCode,
                SuggestionsCreated = created.Count,
                SkippedExisting = skipped,
                Suggestions = created,
                Usage = usage
            }
        };
    }

    private string BuildChargeMapSeedSystemPrompt() => @"You are a charge mapping specialist. Your job is to map file charge descriptions to Selcomm charge codes.

You will be given:
1. Sample charge descriptions found in a vendor file
2. A dictionary of valid Selcomm charge codes with their narratives
3. Optionally, existing charge mappings from similar file types for cross-reference

For each distinct charge concept, create a LIKE pattern (using % wildcards) that will match variations of that charge description, and map it to the most appropriate Selcomm charge code.

Respond with ONLY a JSON array. Each element must have:
- FileChgDesc: SQL LIKE pattern (e.g., ""%Monthly Service%"")
- ChgCode: The matched Selcomm charge code (must be from the provided dictionary)
- Confidence: HIGH, MEDIUM, or LOW
- Reasoning: Brief explanation of why this mapping was chosen
- MatchMethod: NARRATIVE_MATCH (matched charge_code narrative), CROSS_REFERENCE (found in sibling file type), or PATTERN_MATCH (inferred from patterns)

Do not include patterns that already exist. Only use charge codes from the provided dictionary.";

    private string BuildChargeMapSeedUserPrompt(
        string fileTypeCode,
        List<string> chargeDescriptions,
        List<ChargeCodeLookup> chargeCodes,
        List<NtflChgMapRecord> crossRefMappings,
        HashSet<string> existingPatterns)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"File Type: {fileTypeCode}");
        sb.AppendLine();
        sb.AppendLine("## Charge Descriptions Found in File:");
        foreach (var desc in chargeDescriptions.Take(100))
            sb.AppendLine($"- {desc}");

        sb.AppendLine();
        sb.AppendLine("## Valid Selcomm Charge Codes:");
        foreach (var cc in chargeCodes)
            sb.AppendLine($"- {cc.ChgCode}: {cc.ChgNarr}");

        if (crossRefMappings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Cross-Reference: Existing Mappings from Similar File Types:");
            foreach (var m in crossRefMappings.Take(50))
                sb.AppendLine($"- [{m.FileTypeCode.Trim()}] {m.FileChgDesc} → {m.ChgCode.Trim()}");
        }

        if (existingPatterns.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Already Mapped (skip these):");
            foreach (var p in existingPatterns.Take(50))
                sb.AppendLine($"- {p}");
        }

        return sb.ToString();
    }

    private List<AiChargeMapSuggestion> ParseChargeMapSeedResponse(string textContent)
    {
        try
        {
            // Extract JSON array from response (may have markdown wrapping)
            var jsonStart = textContent.IndexOf('[');
            var jsonEnd = textContent.LastIndexOf(']');
            if (jsonStart < 0 || jsonEnd < 0) return new List<AiChargeMapSuggestion>();

            var json = textContent.Substring(jsonStart, jsonEnd - jsonStart + 1);
            return JsonSerializer.Deserialize<List<AiChargeMapSuggestion>>(json, JsonOpts) ?? new List<AiChargeMapSuggestion>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse charge map seed response");
            return new List<AiChargeMapSuggestion>();
        }
    }

    public async Task<DataResult<List<AiChargeMapSuggestion>>> GetPendingSuggestionsAsync(
        string fileTypeCode, SecurityContext securityContext)
    {
        var pendingMaps = await _repository.GetPendingAiSuggestionsAsync(fileTypeCode);
        var pendingReasons = await _repository.GetPendingAiReasonsAsync(fileTypeCode);

        if (!pendingMaps.IsSuccess || pendingMaps.Data == null)
            return new DataResult<List<AiChargeMapSuggestion>> { StatusCode = 200, Data = new List<AiChargeMapSuggestion>() };

        var reasonsByMapId = (pendingReasons.Data ?? new List<ChgMapAiReasonRecord>())
            .GroupBy(r => r.ChgMapId)
            .ToDictionary(g => g.Key, g => g.First());

        var suggestions = pendingMaps.Data.Select(m =>
        {
            reasonsByMapId.TryGetValue(m.Id, out var reason);
            return new AiChargeMapSuggestion
            {
                ChgMapId = m.Id,
                FileChgDesc = m.FileChgDesc,
                ChgCode = m.ChgCode,
                ChgNarr = reason?.MatchedChgNarr ?? "",
                Confidence = reason?.Confidence ?? "MEDIUM",
                Reasoning = reason?.Reasoning ?? "",
                MatchMethod = reason?.MatchMethod ?? ""
            };
        }).ToList();

        return new DataResult<List<AiChargeMapSuggestion>> { StatusCode = 200, Data = suggestions };
    }

    public async Task<DataResult<NtflChgMapRecord>> ReviewSuggestionAsync(
        string fileTypeCode, int chgMapId, AiChargeMapReviewRequest request, SecurityContext securityContext)
    {
        var action = request.Action?.Trim().ToUpperInvariant() ?? "";
        if (action != "ACCEPT" && action != "REJECT" && action != "MODIFY")
            return new DataResult<NtflChgMapRecord> { StatusCode = 400, ErrorCode = "FileLoading.InvalidAction", ErrorMessage = "Action must be ACCEPT, REJECT, or MODIFY" };

        var mapResult = await _repository.GetChargeMapAsync(chgMapId);
        if (!mapResult.IsSuccess || mapResult.Data == null)
            return new DataResult<NtflChgMapRecord> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"Charge mapping {chgMapId} not found" };

        var map = mapResult.Data;
        if (map.Source != "AI_SUGGESTED")
            return new DataResult<NtflChgMapRecord> { StatusCode = 409, ErrorCode = "FileLoading.AlreadyReviewed", ErrorMessage = "This mapping is not an AI suggestion pending review" };

        var reviewer = securityContext.FullName ?? securityContext.UserCode ?? "SYSTEM";

        // Update reason record
        var reasons = await _repository.GetChgMapAiReasonsAsync(chgMapId);
        var reason = reasons.Data?.FirstOrDefault(r => r.ReviewStatus == "PENDING");

        if (action == "REJECT")
        {
            if (reason != null)
                await _repository.UpdateChgMapAiReasonReviewAsync(reason.ReasonId, "REJECTED", reviewer);
            await _repository.DeleteChargeMapAsync(chgMapId);
            return new DataResult<NtflChgMapRecord> { StatusCode = 200, Data = map };
        }

        if (action == "MODIFY")
        {
            if (!string.IsNullOrEmpty(request.CorrectedChgCode)) map.ChgCode = request.CorrectedChgCode;
            if (!string.IsNullOrEmpty(request.CorrectedFileChgDesc)) map.FileChgDesc = request.CorrectedFileChgDesc;
            map.Source = "AI_ACCEPTED";
            map.UpdatedBy = reviewer;
            await _repository.UpdateChargeMapAsync(map);
            if (reason != null)
                await _repository.UpdateChgMapAiReasonReviewAsync(reason.ReasonId, "MODIFIED", reviewer);
        }
        else // ACCEPT
        {
            await _repository.UpdateChargeMapSourceAsync(chgMapId, "AI_ACCEPTED");
            map.Source = "AI_ACCEPTED";
            if (reason != null)
                await _repository.UpdateChgMapAiReasonReviewAsync(reason.ReasonId, "ACCEPTED", reviewer);
        }

        var saved = await _repository.GetChargeMapAsync(chgMapId);
        return new DataResult<NtflChgMapRecord> { StatusCode = 200, Data = saved.Data ?? map };
    }

    public async Task<DataResult<int>> AcceptAllSuggestionsAsync(string fileTypeCode, SecurityContext securityContext)
    {
        var pending = await _repository.GetPendingAiSuggestionsAsync(fileTypeCode);
        if (!pending.IsSuccess || pending.Data == null)
            return new DataResult<int> { StatusCode = 200, Data = 0 };

        var reviewer = securityContext.FullName ?? securityContext.UserCode ?? "SYSTEM";
        var count = 0;

        foreach (var map in pending.Data)
        {
            await _repository.UpdateChargeMapSourceAsync(map.Id, "AI_ACCEPTED");
            var reasons = await _repository.GetChgMapAiReasonsAsync(map.Id);
            var reason = reasons.Data?.FirstOrDefault(r => r.ReviewStatus == "PENDING");
            if (reason != null)
                await _repository.UpdateChgMapAiReasonReviewAsync(reason.ReasonId, "ACCEPTED", reviewer);
            count++;
        }

        return new DataResult<int> { StatusCode = 200, Data = count };
    }

    public async Task<DataResult<int>> RejectAllSuggestionsAsync(string fileTypeCode, SecurityContext securityContext)
    {
        var pending = await _repository.GetPendingAiSuggestionsAsync(fileTypeCode);
        if (!pending.IsSuccess || pending.Data == null)
            return new DataResult<int> { StatusCode = 200, Data = 0 };

        var reviewer = securityContext.FullName ?? securityContext.UserCode ?? "SYSTEM";
        var count = 0;

        foreach (var map in pending.Data)
        {
            var reasons = await _repository.GetChgMapAiReasonsAsync(map.Id);
            var reason = reasons.Data?.FirstOrDefault(r => r.ReviewStatus == "PENDING");
            if (reason != null)
                await _repository.UpdateChgMapAiReasonReviewAsync(reason.ReasonId, "REJECTED", reviewer);
            await _repository.DeleteChargeMapAsync(map.Id);
            count++;
        }

        return new DataResult<int> { StatusCode = 200, Data = count };
    }

    public async Task<DataResult<List<ChgMapAiReasonRecord>>> GetAiReasonsAsync(int chgMapId, SecurityContext securityContext)
    {
        return await _repository.GetChgMapAiReasonsAsync(chgMapId);
    }

    // ============================================
    // Domain AI Config CRUD (proxied to AI Gateway)
    // ============================================

    public async Task<DataResult<AiDomainConfig>> GetDomainConfigAsync()
    {
        var result = await _repository.GetAiDomainConfigAsync();
        if (result.IsSuccess && result.Data != null)
        {
            result.Data.ApiKey = MaskApiKey(result.Data.ApiKey);
        }
        return result;
    }

    public async Task<DataResult<AiDomainConfig>> SaveDomainConfigAsync(
        AiDomainConfigRequest request, SecurityContext securityContext)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return new DataResult<AiDomainConfig>
            {
                StatusCode = 400,
                ErrorCode = "FileLoading.ValidationError",
                ErrorMessage = "ApiKey is required."
            };
        }

        var config = new AiDomainConfig
        {
            ApiKey = request.ApiKey,
            Model = request.Model ?? "claude-sonnet-4-20250514",
            Enabled = request.Enabled,
            MaxReviewsPerDay = request.MaxReviewsPerDay ?? 50,
            MaxOutputTokens = request.MaxOutputTokens ?? 4096,
            CreatedAt = DateTime.Now,
            CreatedBy = securityContext.FullName ?? securityContext.UserCode,
            UpdatedAt = DateTime.Now,
            UpdatedBy = securityContext.FullName ?? securityContext.UserCode
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

    public async Task<RawCommandResult> DeleteDomainConfigAsync()
    {
        return await _repository.DeleteAiDomainConfigAsync();
    }

    public async Task<DataResult<AiConfigStatusResponse>> GetConfigStatusAsync()
    {
        var configResult = await _repository.GetAiDomainConfigAsync();

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
    // AI Gateway Integration
    // ============================================

    /// <summary>
    /// Call the AI Gateway (Selcomm.Ai.Api) to get a completion.
    /// Forwards the caller's JWT so the gateway knows the domain.
    /// Returns the raw text content and usage info.
    /// </summary>
    private async Task<DataResult<(string TextContent, AiReviewUsage Usage)>> CallGatewayAsync(
        string systemPrompt, string userPrompt, string agentName, int maxTokens = 4096)
    {
        var client = _httpClientFactory.CreateClient("AiGateway");

        var requestBody = new GatewayCompletionRequest
        {
            System = systemPrompt,
            Messages = new List<GatewayMessage>
            {
                new() { Role = "user", Content = userPrompt }
            },
            AppName = "file-loading",
            AgentName = agentName,
            MaxTokens = maxTokens,
            Temperature = 0.0
        };

        var json = JsonSerializer.Serialize(requestBody, JsonOpts);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v4/ai/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        // Forward the caller's auth token to the gateway
        var authHeader = _httpContextAccessor.HttpContext?.Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader))
            httpRequest.Headers.TryAddWithoutValidation("Authorization", authHeader);

        // Forward API key if present
        var apiKeyHeader = _httpContextAccessor.HttpContext?.Request.Headers["X-API-Key"].FirstOrDefault();
        if (!string.IsNullOrEmpty(apiKeyHeader))
            httpRequest.Headers.TryAddWithoutValidation("X-API-Key", apiKeyHeader);

        _logger.LogDebug("Calling AI Gateway: agent={AgentName}", agentName);

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await client.SendAsync(httpRequest);
        }
        catch (TaskCanceledException)
        {
            return new DataResult<(string, AiReviewUsage)>
            {
                StatusCode = 504,
                ErrorCode = "FileLoading.AiApiTimeout",
                ErrorMessage = "AI request timed out. Try again or reduce file complexity."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling AI Gateway");
            return new DataResult<(string, AiReviewUsage)>
            {
                StatusCode = 502,
                ErrorCode = "FileLoading.AiApiError",
                ErrorMessage = "AI Gateway is unavailable. Try again later."
            };
        }

        var responseBody = await httpResponse.Content.ReadAsStringAsync();

        if (!httpResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("AI Gateway error: {StatusCode} {Body}", (int)httpResponse.StatusCode, responseBody);
            return new DataResult<(string, AiReviewUsage)>
            {
                StatusCode = (int)httpResponse.StatusCode,
                ErrorCode = "FileLoading.AiApiError",
                ErrorMessage = $"AI Gateway returned {httpResponse.StatusCode}. Check gateway config and API key."
            };
        }

        // Parse gateway response
        var gatewayResponse = JsonSerializer.Deserialize<GatewayCompletionResponse>(responseBody, JsonOpts);
        if (gatewayResponse?.Content == null || gatewayResponse.Content.Count == 0)
        {
            return new DataResult<(string, AiReviewUsage)>
            {
                StatusCode = 502,
                ErrorCode = "FileLoading.AiApiError",
                ErrorMessage = "Empty response from AI Gateway."
            };
        }

        var textContent = gatewayResponse.Content.FirstOrDefault(c => c.Type == "text")?.Text ?? string.Empty;

        var usage = new AiReviewUsage
        {
            InputTokens = gatewayResponse.Usage?.InputTokens ?? 0,
            OutputTokens = gatewayResponse.Usage?.OutputTokens ?? 0,
            Model = gatewayResponse.Model
        };

        return new DataResult<(string, AiReviewUsage)>
        {
            StatusCode = 200,
            Data = (textContent, usage)
        };
    }

    // ============================================
    // Response Parsing
    // ============================================

    private ClaudeReviewResult ParseReviewResult(string textContent)
    {
        textContent = StripMarkdownFences(textContent);

        try
        {
            return JsonSerializer.Deserialize<ClaudeReviewResult>(textContent, JsonOpts)
                ?? new ClaudeReviewResult { Summary = textContent[..Math.Min(2000, textContent.Length)] };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse review JSON: {Text}", textContent[..Math.Min(500, textContent.Length)]);
            return new ClaudeReviewResult
            {
                OverallAssessment = "Acceptable",
                Summary = textContent[..Math.Min(2000, textContent.Length)],
                Issues = new List<AiReviewIssue>()
            };
        }
    }

    private ClaudeAnalysisResult ParseAnalysisResult(string textContent)
    {
        textContent = StripMarkdownFences(textContent);

        try
        {
            return JsonSerializer.Deserialize<ClaudeAnalysisResult>(textContent, JsonOpts)
                ?? new ClaudeAnalysisResult { Summary = textContent[..Math.Min(2000, textContent.Length)] };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse analysis JSON: {Text}", textContent[..Math.Min(500, textContent.Length)]);
            return new ClaudeAnalysisResult
            {
                IngestionReadiness = "MEDIUM",
                Summary = textContent[..Math.Min(2000, textContent.Length)]
            };
        }
    }

    private static string StripMarkdownFences(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```json"))
            text = text[7..];
        else if (text.StartsWith("```"))
            text = text[3..];
        if (text.EndsWith("```"))
            text = text[..^3];
        return text.Trim();
    }

    // ============================================
    // Analysis Prompt Construction
    // ============================================

    /// <summary>
    /// Load instruction content for a file class: DB first, then default file.
    /// </summary>
    private async Task<string?> LoadInstructionContentAsync(string fileClassCode)
    {
        // 1. Try database (user-created or seeded default)
        var dbResult = await _repository.GetInstructionFileAsync(fileClassCode);
        if (dbResult.IsSuccess && dbResult.Data != null)
            return dbResult.Data.InstructionContent;

        // 2. Fall back to shipped default file
        return LoadDefaultInstructionFile(fileClassCode);
    }

    /// <summary>
    /// Load default instruction markdown from the Instructions/ directory.
    /// </summary>
    private static string? LoadDefaultInstructionFile(string fileClassCode)
    {
        var fileName = $"{fileClassCode.Trim().ToUpperInvariant()}.md";

        // Check published location first, then dev location
        var basePath = Path.Combine(AppContext.BaseDirectory, "Instructions", fileName);
        if (File.Exists(basePath))
            return File.ReadAllText(basePath);

        var devPath = Path.Combine(Directory.GetCurrentDirectory(), "Instructions", fileName);
        if (File.Exists(devPath))
            return File.ReadAllText(devPath);

        return null;
    }

    private static string BuildAnalysisSystemPrompt(string? fileClassCode, string? instructionContent)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"You are a data analyst specialising in billing file ingestion for a telecom/IT billing platform.
Your task is to analyse example file(s) and produce a structured analysis that will be used to configure an automated file parser.

Return your analysis as a JSON object with this exact structure:
{
  ""IngestionReadiness"": ""HIGH|MEDIUM|LOW"",
  ""Summary"": ""A narrative paragraph summarizing your findings"",
  ""DetectedFormat"": {
    ""FileFormat"": ""CSV|XLSX|Delimited"",
    ""Delimiter"": "","" or ""\\t"" or ""|"",
    ""HasHeaderRow"": true/false,
    ""Encoding"": ""UTF-8"",
    ""HeaderRowCount"": 1,
    ""TrailerRowCount"": 0,
    ""DataRowCount"": 100
  },
  ""Columns"": [
    {
      ""Index"": 0,
      ""Name"": ""column header name"",
      ""DataType"": ""String|Integer|Decimal|Date|DateTime|GUID"",
      ""SampleValues"": [""val1"", ""val2"", ""val3""],
      ""SuggestedTargetField"": ""AccountCode|ServiceId|ChargeType|CostAmount|TaxAmount|Quantity|UOM|FromDate|ToDate|Description|ExternalRef|{snake_cased_header_name}|null""
    }
  ],
  ""BillingConceptMappings"": [
    {
      ""BillingConcept"": ""Customer Name"",
      ""SourceColumn"": ""CustomerName"",
      ""ColumnIndex"": 15,
      ""Confidence"": ""HIGH|MEDIUM|LOW""
    }
  ],
  ""DataQualityIssues"": [
    {
      ""Severity"": ""Critical|Warning|Info"",
      ""Category"": ""Format|DataQuality|Structure|Anomaly"",
      ""Description"": ""description"",
      ""AffectedField"": ""field name or null"",
      ""Examples"": [""example values""],
      ""Suggestion"": ""how to handle""
    }
  ],
  ""Observations"": [""key finding 1"", ""key finding 2""],
  ""SuggestedParserConfig"": {
    ""FileFormat"": ""CSV"",
    ""Delimiter"": "","",
    ""HasHeaderRow"": true,
    ""SkipRowsTop"": 0,
    ""SkipRowsBottom"": 0,
    ""RowIdMode"": ""POSITION"",
    ""DateFormat"": ""yyyy-MM-dd"",
    ""ColumnMappings"": [
      {
        ""ColumnIndex"": 0,
        ""SourceColumnName"": ""header name"",
        ""TargetField"": ""AccountCode"",
        ""DataType"": ""String"",
        ""DateFormat"": null,
        ""IsRequired"": true
      }
    ]
  }
}

IMPORTANT — Target Field Naming:
- For columns that map to well-known billing concepts, use these exact names: AccountCode, ServiceId, ChargeType, CostAmount, TaxAmount, Quantity, UOM, FromDate, ToDate, Description, ExternalRef, ProrateRatio
- For ALL OTHER columns, use the source column header name converted to snake_case (e.g. ResellerName -> reseller_name, BillableRatio -> billable_ratio, SubTotalRrp -> sub_total_rrp, UnitPriceRrp -> unit_price_rrp)
- Do NOT use Generic01, Generic02, etc. Always use meaningful names derived from the header.
- This applies to both SuggestedTargetField in Columns[] and TargetField in SuggestedParserConfig.ColumnMappings[]

Return ONLY the JSON object, no markdown fences or other text.");

        // Append file-class-specific instructions (from DB or default file)
        if (!string.IsNullOrEmpty(instructionContent))
        {
            sb.AppendLine();
            sb.AppendLine($"## File Class: {fileClassCode}");
            sb.AppendLine(instructionContent);
        }

        return sb.ToString();
    }

    private static string BuildAnalysisUserPrompt(string fileTypeCode, string exampleContent, List<string>? focusAreas)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"## File Type: {fileTypeCode}");
        sb.AppendLine();
        sb.AppendLine("## Example File Content");
        sb.AppendLine("Analyse the following example file(s) and produce the structured analysis:");
        sb.AppendLine();
        sb.AppendLine(exampleContent);

        if (focusAreas?.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Focus Areas");
            sb.AppendLine("Pay special attention to:");
            foreach (var area in focusAreas)
                sb.AppendLine($"  - {area}");
        }

        return sb.ToString();
    }

    // ============================================
    // Helpers
    // ============================================

    private static string MapFileClassCodeToCategory(string? fileClassCode)
    {
        return fileClassCode?.ToUpperInvariant() switch
        {
            "CHG" => "Charge",
            "CDR" => "Usage",
            "PAY" or "EBL" => "Payment",
            _ => fileClassCode ?? "Unknown"
        };
    }

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 12)
            return "***";
        return apiKey[..8] + "..." + apiKey[^4..];
    }

    /// <summary>
    /// Sanitize text for Informix storage — replace Unicode characters that cause
    /// "Code-set conversion function failed" errors.
    /// </summary>
    private static string SanitizeForInformix(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '\u2018' or '\u2019' => '\'',  // smart single quotes
                '\u201C' or '\u201D' => '"',    // smart double quotes
                '\u2013' => '-',                // en dash
                '\u2014' => '-',                // em dash
                '\u2026' => '.',                // ellipsis
                '\u00A0' => ' ',                // non-breaking space
                '\u2022' => '-',                // bullet
                '\u00B7' => '-',                // middle dot
                _ => c > 127 ? '?' : c         // replace any other non-ASCII
            });
        }
        return sb.ToString();
    }

    private static DataResult<T> Forward<T>(StoredProcedureResult source)
    {
        return new DataResult<T>
        {
            StatusCode = source.StatusCode,
            ErrorCode = source.ErrorCode,
            ErrorMessage = source.ErrorMessage
        };
    }
}
