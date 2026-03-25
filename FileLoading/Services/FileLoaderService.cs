using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FileLoading.Interfaces;
using FileLoading.Models;
using FileLoading.Parsers;
using FileLoading.Repositories;
using FileLoading.Validation;
using Selcomm.Data.Common;

namespace FileLoading.Services;

/// <summary>
/// File Loader Service implementation (from ntfileload.4gl).
/// Loads and processes network files of various types.
/// Uses database persistence via IFileLoaderRepository.
///
/// Processing Flow (optimized streaming two-pass approach):
/// 1. Create nt_file record (status = Initial Loading)
/// 2. PASS 1: Streaming validation - validate file structure without loading records into memory
/// 3. If file-level errors: update status, log errors, reject file
/// 4. PASS 2: Streaming insert - re-read file and insert records in batches with transaction batching
/// 5. Update trailer totals at end
///
/// This approach minimizes memory usage (~10-50MB instead of ~280MB for 400K records)
/// and significantly improves performance through batch transaction commits.
/// </summary>
public class FileLoaderService : IFileLoaderService
{
    private readonly ILogger<FileLoaderService> _logger;
    private readonly IFileLoaderRepository _repository;
    private readonly IEnumerable<IFileParser> _parsers;
    private readonly FileLoaderOptionsRoot _optionsRoot;
    private readonly IValidationConfigProvider? _validationConfigProvider;

    public FileLoaderService(
        IFileLoaderRepository repository,
        ILogger<FileLoaderService> logger,
        IEnumerable<IFileParser> parsers,
        IOptions<FileLoaderOptionsRoot>? options = null,
        IValidationConfigProvider? validationConfigProvider = null)
    {
        _repository = repository;
        _logger = logger;
        _parsers = parsers;
        _optionsRoot = options?.Value ?? new FileLoaderOptionsRoot();
        _validationConfigProvider = validationConfigProvider;
    }

    public async Task<DataResult<FileLoadResponse>> LoadFileAsync(LoadFileRequest request, SecurityContext securityContext)
    {
        try
        {
            _logger.LogInformation("Loading file: {FileName}, Type: {FileType}", request.FileName, request.FileType);

            // Validate file exists
            if (!File.Exists(request.FileName))
            {
                return new DataResult<FileLoadResponse>
                {
                    StatusCode = 400,
                    ErrorCode = "FILE_NOT_FOUND",
                    ErrorMessage = $"File not found: {request.FileName}"
                };
            }

            // Get customer number from request or use default
            var ntCustNum = request.NtCustNum ?? "DEFAULT";

            // Step 1: Create file record in database using sp_file_loading_nt_file_api
            var displayName = request.DisplayFileName ?? Path.GetFileName(request.FileName);
            var createResult = await _repository.CreateNtFileAsync(
                request.FileType,
                ntCustNum,
                displayName,
                FileStatus.Transferred,
                request.FileDate,
                securityContext);

            if (!createResult.IsSuccess || createResult.Value == null)
            {
                return new DataResult<FileLoadResponse>
                {
                    StatusCode = createResult.StatusCode,
                    ErrorCode = createResult.ErrorCode ?? "CREATE_FAILED",
                    ErrorMessage = createResult.ErrorMessage ?? "Failed to create file record"
                };
            }

            var ntFileNum = createResult.Value.NtFileNum;
            var resolvedFileName = createResult.Value.NtFileName;

            if (ntFileNum == 0)
            {
                return new DataResult<FileLoadResponse>
                {
                    StatusCode = 500,
                    ErrorCode = "CREATE_FAILED",
                    ErrorMessage = "Failed to create file record - no ID returned"
                };
            }

            // Get file class code for determining which table to insert into
            var fileTypesResult = await _repository.GetFileTypesAsync(securityContext);
            var fileTypeInfo = fileTypesResult.Data?.Items.FirstOrDefault(t => t.FileTypeCode == request.FileType);
            var fileClassCode = fileTypeInfo?.FileClassCode ?? "CDR";

            // Step 2-4: Process file synchronously (parse all, then insert if valid)
            var processResult = await ProcessFileAsync(ntFileNum, request.FileName, request.FileType, fileClassCode, ntCustNum, securityContext);

            return new DataResult<FileLoadResponse>
            {
                StatusCode = processResult.Success ? 200 : 400,
                ErrorCode = processResult.Success ? null : "PROCESSING_ERRORS",
                ErrorMessage = processResult.Success ? null : processResult.ErrorMessage,
                Data = new FileLoadResponse
                {
                    NtFileNum = ntFileNum,
                    FileName = resolvedFileName,
                    FileType = request.FileType,
                    Status = FileStatus.GetDescription(processResult.StatusId),
                    StatusId = processResult.StatusId,
                    RecordsLoaded = processResult.RecordsLoaded,
                    RecordsFailed = processResult.RecordsFailed,
                    StartedAt = processResult.StartedAt,
                    CompletedAt = processResult.CompletedAt
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading file");
            return new DataResult<FileLoadResponse>
            {
                StatusCode = 500,
                ErrorCode = "INTERNAL_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<DataResult<FileLoadResponse>> UploadFileAsync(IFormFile file, string fileType, SecurityContext securityContext)
    {
        try
        {
            // Save uploaded file to temp location
            var tempPath = Path.Combine(Path.GetTempPath(), $"upload_{Guid.NewGuid():N}_{file.FileName}");
            using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Load the saved file
            return await LoadFileAsync(new LoadFileRequest
            {
                FileName = tempPath,
                FileType = fileType
            }, securityContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file");
            return new DataResult<FileLoadResponse>
            {
                StatusCode = 500,
                ErrorCode = "INTERNAL_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Process file using streaming two-pass approach:
    /// Pass 1: Validate file structure without loading records into memory
    /// Pass 2: Re-read file and insert records in batches with transaction batching
    ///
    /// This approach minimizes memory usage and maximizes performance for large files.
    /// </summary>
    private async Task<ProcessingResult> ProcessFileAsync(
        int ntFileNum,
        string filePath,
        string fileType,
        string fileClassCode,
        string ntCustNum,
        SecurityContext securityContext)
    {
        var processingResult = new ProcessingResult
        {
            StartedAt = DateTime.Now,
            StatusId = FileStatus.Transferred
        };

        try
        {
            // Get options based on domain and file class code
            var options = _optionsRoot.GetOptions(securityContext.Domain, fileClassCode);

            _logger.LogInformation("Processing file {NtFileNum}: {FilePath} (domain: {Domain}, fileClass: {FileClass}, streaming: {StreamingMode}, batchSize: {BatchSize})",
                ntFileNum, filePath, securityContext.Domain ?? "Default", fileClassCode, options.EffectiveUseStreamingMode, options.EffectiveBatchSize);

            // Get parser for file type
            var parser = _parsers.FirstOrDefault(p => p.FileType == fileType)
                      ?? _parsers.FirstOrDefault(p => p.FileClassCode == fileClassCode);

            // Fallback: check for generic parser configuration
            if (parser == null)
            {
                var genericParser = _parsers.OfType<GenericFileParser>().FirstOrDefault();
                if (genericParser != null)
                {
                    try
                    {
                        await genericParser.InitializeForFileTypeAsync(fileType);
                        parser = genericParser;
                        fileClassCode = "GEN";
                        _logger.LogInformation("Using generic parser for file type {FileType}", fileType);
                    }
                    catch (InvalidOperationException)
                    {
                        // No generic config found — fall through to error
                    }
                }
            }

            if (parser == null)
            {
                _logger.LogError("No parser found for file type {FileType}", fileType);
                processingResult.Success = false;
                processingResult.StatusId = FileStatus.ValidationError;
                processingResult.ErrorMessage = $"No parser found for file type: {fileType}";
                await _repository.UpdateFileStatusAsync(ntFileNum, FileStatus.ValidationError, securityContext);
                return processingResult;
            }

            // Configure validation if available
            if (_validationConfigProvider != null && parser is BaseFileParser baseParser)
            {
                var validationConfig = _validationConfigProvider.GetConfig(fileType);
                if (validationConfig != null)
                {
                    baseParser.SetValidationConfig(validationConfig);
                    _logger.LogInformation("Validation config applied for file type {FileType} with {RuleCount} field rules",
                        fileType, validationConfig.FieldRules.Count);
                }
            }

            // Insert nt_fl_process record (legacy compatibility)
            int processRef = 0;
            var processResult = await _repository.InsertProcessRecordAsync(ntFileNum);
            if (processResult.IsSuccess)
            {
                processRef = processResult.Value;
                _logger.LogDebug("Created process record: process_ref={ProcessRef} for file {NtFileNum}", processRef, ntFileNum);
            }
            else
            {
                _logger.LogWarning("Failed to create nt_fl_process record for file {NtFileNum}: {Error}", ntFileNum, processResult.ErrorMessage);
            }

            var context = new ParseContext
            {
                FileRef = ntFileNum,
                FileType = fileType
            };

            // Use streaming mode for optimized processing
            ProcessingResult result;
            if (options.EffectiveUseStreamingMode)
            {
                result = await ProcessFileStreamingAsync(ntFileNum, filePath, fileClassCode, parser, context, securityContext, options);
            }
            else
            {
                // Fallback to legacy memory-based processing
                result = await ProcessFileLegacyAsync(ntFileNum, filePath, parser, context, securityContext, fileClassCode, options);
            }

            // Update nt_fl_process end time (legacy compatibility)
            if (processRef > 0)
            {
                await _repository.UpdateProcessRecordAsync(processRef);
            }

            // Insert nt_fl_header record (legacy compatibility)
            await _repository.InsertFileHeaderAsync(ntFileNum, ntCustNum, result.EarliestCall, result.LatestCall);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file {NtFileNum}", ntFileNum);

            // Log the exception as an error
            await LogErrorsAsync(ntFileNum, new List<ParseError>
            {
                new ParseError
                {
                    ErrorCode = FileErrorCodes.ParseError,
                    Message = $"Exception: {ex.Message}",
                    IsFileLevelError = true
                }
            });

            await _repository.UpdateFileStatusAsync(ntFileNum, FileStatus.LoadError, securityContext);

            processingResult.Success = false;
            processingResult.StatusId = FileStatus.LoadError;
            processingResult.ErrorMessage = ex.Message;
            processingResult.CompletedAt = DateTime.Now;
            return processingResult;
        }
    }

    /// <summary>
    /// Streaming two-pass processing for large files.
    /// Pass 1: Validate file structure without loading records into memory
    /// Pass 2: Re-read file and insert records in batches
    /// </summary>
    private async Task<ProcessingResult> ProcessFileStreamingAsync(
        int ntFileNum,
        string filePath,
        string fileClassCode,
        IFileParser parser,
        ParseContext context,
        SecurityContext securityContext,
        FileTypeOptions options)
    {
        var processingResult = new ProcessingResult
        {
            StartedAt = DateTime.Now,
            StatusId = FileStatus.Transferred
        };

        // ============================================
        // PASS 1: STREAMING VALIDATION
        // ============================================
        _logger.LogInformation("File {NtFileNum}: Starting validation pass", ntFileNum);

        StreamingValidationResult validationResult;
        using (var stream = File.OpenRead(filePath))
        {
            validationResult = await parser.ValidateFileStreamingAsync(stream, context);
        }

        // If file-level errors, reject the file
        if (!validationResult.IsValid)
        {
            _logger.LogError("File {NtFileNum} validation failed: {Error}", ntFileNum, validationResult.ErrorMessage);

            // Log all errors to database
            await LogErrorsAsync(ntFileNum, validationResult.Errors);

            // Update status to validation error
            await _repository.UpdateFileStatusAsync(ntFileNum, FileStatus.ValidationError, securityContext);

            processingResult.Success = false;
            processingResult.StatusId = FileStatus.ValidationError;
            processingResult.ErrorMessage = validationResult.ErrorMessage;
            processingResult.RecordsFailed = validationResult.Errors.Count;
            processingResult.CompletedAt = DateTime.Now;
            return processingResult;
        }

        // Validation passed — update status to Validated
        await _repository.UpdateFileStatusAsync(ntFileNum, FileStatus.Validated, securityContext);

        _logger.LogInformation("File {NtFileNum}: Validation passed, {RecordCount} records to process",
            ntFileNum, validationResult.RecordCount);

        // ============================================
        // PASS 2: STREAMING INSERT
        // ============================================
        _logger.LogInformation("File {NtFileNum}: Starting streaming insert pass", ntFileNum);

        var recordNum = await _repository.GetNextRecordNumberAsync(ntFileNum);
        int recordsLoaded;
        int recordsFailed;
        decimal totalCost;
        DateTime? earliestCall = null;
        DateTime? latestCall = null;
        var recordErrors = new List<ParseError>();

        using (var stream = File.OpenRead(filePath))
        {
            if (fileClassCode == "GEN")
            {
                (recordsLoaded, recordsFailed, totalCost, recordErrors) = await ProcessGenericRecordsStreamingAsync(
                    ntFileNum, context.FileType, parser, stream, context, recordNum, securityContext, options);
            }
            else if (fileClassCode == "CHG")
            {
                (recordsLoaded, recordsFailed, totalCost, recordErrors) = await ProcessChargeRecordsStreamingAsync(
                    ntFileNum, parser, stream, context, recordNum, securityContext, options);
            }
            else
            {
                // Default to CDR (call detail) processing
                (recordsLoaded, recordsFailed, totalCost, earliestCall, latestCall, recordErrors) = await ProcessCdrRecordsStreamingAsync(
                    ntFileNum, parser, stream, context, recordNum, securityContext, options);
            }
        }

        // Log any record-level errors
        if (recordErrors.Count > 0)
        {
            await LogErrorsAsync(ntFileNum, recordErrors);
        }

        // Store validation result if available
        if (parser is BaseFileParser baseParser)
        {
            var fieldValidationResult = baseParser.GetValidationResult();
            if (fieldValidationResult != null)
            {
                // Log detailed errors (up to configured max)
                if (fieldValidationResult.DetailedErrors.Count > 0)
                {
                    await _repository.InsertValidationErrorsBatchAsync(ntFileNum, fieldValidationResult.DetailedErrors);
                }

                // Store AI-friendly summary for later retrieval
                if (fieldValidationResult.TotalErrors > 0)
                {
                    await _repository.StoreValidationSummaryAsync(ntFileNum, fieldValidationResult.AISummary);
                    _logger.LogWarning("Validation summary for file {NtFileNum}: {Summary}",
                        ntFileNum, fieldValidationResult.Summary);
                }
            }
        }

        // Execute custom validation SP if configured (generic parser)
        if (parser is GenericFileParser genParser && genParser.Config?.CustomSpName != null)
        {
            _logger.LogInformation("Executing custom validation SP {SpName} for file {NtFileNum}",
                genParser.Config.CustomSpName, ntFileNum);
            var spResult = await _repository.ExecuteCustomValidationSpAsync(genParser.Config.CustomSpName, ntFileNum);
            if (!spResult.IsSuccess)
            {
                _logger.LogWarning("Custom validation SP {SpName} returned error: {Error}",
                    genParser.Config.CustomSpName, spResult.ErrorMessage);
            }
        }

        // Update trailer with totals
        await _repository.UpdateTrailerAsync(ntFileNum, recordsLoaded, totalCost, earliestCall, latestCall);

        // All-or-nothing: if any records failed, the entire file is rejected
        if (recordsFailed > 0)
        {
            await _repository.UpdateFileStatusAsync(ntFileNum, FileStatus.LoadError, securityContext);

            processingResult.Success = false;
            processingResult.StatusId = FileStatus.LoadError;
            processingResult.ErrorMessage = $"{recordsFailed} record(s) failed to load";
            processingResult.RecordsLoaded = 0;
            processingResult.RecordsFailed = recordsFailed;
            processingResult.CompletedAt = DateTime.Now;
            return processingResult;
        }

        // All records loaded successfully
        await _repository.UpdateFileStatusAsync(ntFileNum, FileStatus.Loaded, securityContext);

        processingResult.Success = true;
        processingResult.StatusId = FileStatus.Loaded;
        processingResult.RecordsLoaded = recordsLoaded;
        processingResult.RecordsFailed = 0;
        processingResult.TotalCost = totalCost;
        processingResult.EarliestCall = earliestCall;
        processingResult.LatestCall = latestCall;
        processingResult.CompletedAt = DateTime.Now;

        _logger.LogInformation("File {NtFileNum} loaded: {Loaded} records", ntFileNum, recordsLoaded);

        return processingResult;
    }

    /// <summary>
    /// Legacy memory-based processing (kept for backward compatibility).
    /// </summary>
    private async Task<ProcessingResult> ProcessFileLegacyAsync(
        int ntFileNum,
        string filePath,
        IFileParser parser,
        ParseContext context,
        SecurityContext securityContext,
        string fileClassCode,
        FileTypeOptions options)
    {
        var processingResult = new ProcessingResult
        {
            StartedAt = DateTime.Now,
            StatusId = FileStatus.Transferred
        };

        // Parse entire file in memory
        ParseResult parseResult;
        using (var stream = File.OpenRead(filePath))
        {
            parseResult = await parser.ParseAsync(stream, context);
        }

        // If file-level errors, reject the file
        if (!parseResult.Success)
        {
            _logger.LogError("File {NtFileNum} validation failed: {Error}", ntFileNum, parseResult.ErrorMessage);

            await LogErrorsAsync(ntFileNum, parseResult.Errors);
            await _repository.UpdateFileStatusAsync(ntFileNum, FileStatus.ValidationError, securityContext);

            processingResult.Success = false;
            processingResult.StatusId = FileStatus.ValidationError;
            processingResult.ErrorMessage = parseResult.ErrorMessage;
            processingResult.RecordsFailed = parseResult.Errors.Count;
            processingResult.CompletedAt = DateTime.Now;
            return processingResult;
        }

        // Validation passed — update status to Validated
        await _repository.UpdateFileStatusAsync(ntFileNum, FileStatus.Validated, securityContext);

        // Insert all valid records
        var recordNum = await _repository.GetNextRecordNumberAsync(ntFileNum);
        int recordsLoaded;
        int recordsFailed;
        decimal totalCost;
        DateTime? earliestCall = null;
        DateTime? latestCall = null;

        if (fileClassCode == "GEN")
        {
            (recordsLoaded, recordsFailed, totalCost) = await ProcessGenericRecordsAsync(
                ntFileNum, context.FileType, parseResult.Records, recordNum, securityContext, options);
        }
        else if (fileClassCode == "CHG")
        {
            (recordsLoaded, recordsFailed, totalCost) = await ProcessChargeRecordsAsync(
                ntFileNum, parseResult.Records, recordNum, securityContext, options);
        }
        else
        {
            (recordsLoaded, recordsFailed, totalCost, earliestCall, latestCall) = await ProcessCdrRecordsAsync(
                ntFileNum, parseResult.Records, recordNum, securityContext, options);
        }

        // Log any record-level errors
        var recordErrors = parseResult.Errors.Where(e => !e.IsFileLevelError).ToList();
        if (recordErrors.Count > 0)
        {
            await LogErrorsAsync(ntFileNum, recordErrors);
        }

        // Store validation result if available
        if (parser is BaseFileParser baseParser)
        {
            var validationResult = baseParser.GetValidationResult();
            if (validationResult != null)
            {
                // Log detailed errors (up to configured max)
                if (validationResult.DetailedErrors.Count > 0)
                {
                    await _repository.InsertValidationErrorsBatchAsync(ntFileNum, validationResult.DetailedErrors);
                }

                // Store AI-friendly summary for later retrieval
                if (validationResult.TotalErrors > 0)
                {
                    await _repository.StoreValidationSummaryAsync(ntFileNum, validationResult.AISummary);
                    _logger.LogWarning("Validation summary for file {NtFileNum}: {Summary}",
                        ntFileNum, validationResult.Summary);
                }
            }
        }

        // Update trailer with totals
        await _repository.UpdateTrailerAsync(ntFileNum, recordsLoaded, totalCost, earliestCall, latestCall);

        // All-or-nothing: if any records failed, the entire file is rejected
        if (recordsFailed > 0)
        {
            await _repository.UpdateFileStatusAsync(ntFileNum, FileStatus.LoadError, securityContext);

            processingResult.Success = false;
            processingResult.StatusId = FileStatus.LoadError;
            processingResult.ErrorMessage = $"{recordsFailed} record(s) failed to load";
            processingResult.RecordsLoaded = 0;
            processingResult.RecordsFailed = recordsFailed;
            processingResult.CompletedAt = DateTime.Now;
            return processingResult;
        }

        // All records loaded successfully
        await _repository.UpdateFileStatusAsync(ntFileNum, FileStatus.Loaded, securityContext);

        processingResult.Success = true;
        processingResult.StatusId = FileStatus.Loaded;
        processingResult.RecordsLoaded = recordsLoaded;
        processingResult.RecordsFailed = 0;
        processingResult.TotalCost = totalCost;
        processingResult.EarliestCall = earliestCall;
        processingResult.LatestCall = latestCall;
        processingResult.CompletedAt = DateTime.Now;

        _logger.LogInformation("File {NtFileNum} loaded (legacy): {Loaded} records", ntFileNum, recordsLoaded);

        return processingResult;
    }

    /// <summary>
    /// Process CDR records using streaming with batch inserts.
    /// Memory-efficient: only holds current batch in memory.
    /// </summary>
    private async Task<(int loaded, int failed, decimal totalCost, DateTime? earliest, DateTime? latest, List<ParseError> errors)> ProcessCdrRecordsStreamingAsync(
        int ntFileNum,
        IFileParser parser,
        Stream fileStream,
        ParseContext context,
        int startRecordNum,
        SecurityContext securityContext,
        FileTypeOptions options)
    {
        var recordsLoaded = 0;
        var recordsFailed = 0;
        var totalCost = 0m;
        DateTime? earliestCall = null;
        DateTime? latestCall = null;
        var recordNum = startRecordNum;
        var errors = new List<ParseError>();

        // Sub-type support: check if parser needs dual-insert
        var subTypeProvider = parser is ISubTypeRecordProvider stp ? stp : null;
        var subTypeBatch = subTypeProvider != null ? new List<FileDetailRecord>(options.EffectiveBatchSize) : null;

        var batch = new List<ClDetailRecord>(options.EffectiveBatchSize);

        await foreach (var parsed in parser.ParseRecordsStreamingAsync(fileStream, context))
        {
            if (!parsed.IsValid)
            {
                // Log failed record to nt_cl_not_load
                var notLoadRecord = CreateNotLoadRecord(parsed, ntFileNum, recordNum);
                await _repository.InsertNotLoadRecordAsync(notLoadRecord);
                recordsFailed++;

                errors.Add(new ParseError
                {
                    ErrorCode = FileErrorCodes.ParseError,
                    Message = parsed.ValidationError ?? "Record validation failed",
                    LineNumber = recordNum,
                    IsFileLevelError = false
                });

                recordNum++;
                continue;
            }

            var clDetail = CreateClDetailRecord(parsed, ntFileNum, recordNum);
            batch.Add(clDetail);

            // Build sub-type record if applicable
            if (subTypeProvider != null)
                subTypeBatch!.Add(subTypeProvider.CreateSubTypeRecord(parsed, ntFileNum, recordNum));

            // Track totals
            if (clDetail.NtCost.HasValue)
                totalCost += clDetail.NtCost.Value;

            if (clDetail.ClStartDt.HasValue)
            {
                if (!earliestCall.HasValue || clDetail.ClStartDt < earliestCall)
                    earliestCall = clDetail.ClStartDt;
                if (!latestCall.HasValue || clDetail.ClStartDt > latestCall)
                    latestCall = clDetail.ClStartDt;
            }

            recordsLoaded++;
            recordNum++;

            // Flush batch when full - use optimized batch insert with transaction batching
            if (batch.Count >= options.EffectiveBatchSize)
            {
                var insertResult = await _repository.InsertClDetailBatchOptimizedAsync(batch, options.EffectiveTransactionBatchSize);
                if (!insertResult.IsSuccess)
                {
                    _logger.LogError("Batch insert failed: {Error}", insertResult.ErrorMessage);
                }
                batch.Clear();  // Release memory!

                // Flush sub-type batch
                if (subTypeBatch?.Count > 0)
                {
                    await FlushSubTypeBatchAsync(subTypeProvider!, subTypeBatch, options.EffectiveTransactionBatchSize);
                    subTypeBatch.Clear();
                }
            }
        }

        // Flush remaining records
        if (batch.Count > 0)
        {
            var insertResult = await _repository.InsertClDetailBatchOptimizedAsync(batch, options.EffectiveTransactionBatchSize);
            if (!insertResult.IsSuccess)
            {
                _logger.LogError("Final batch insert failed: {Error}", insertResult.ErrorMessage);
            }
        }

        // Flush remaining sub-type records
        if (subTypeBatch?.Count > 0)
        {
            await FlushSubTypeBatchAsync(subTypeProvider!, subTypeBatch, options.EffectiveTransactionBatchSize);
        }

        return (recordsLoaded, recordsFailed, totalCost, earliestCall, latestCall, errors);
    }

    /// <summary>
    /// Process charge records using streaming with batch inserts.
    /// Memory-efficient: only holds current batch in memory.
    /// </summary>
    private async Task<(int loaded, int failed, decimal totalCost, List<ParseError> errors)> ProcessChargeRecordsStreamingAsync(
        int ntFileNum,
        IFileParser parser,
        Stream fileStream,
        ParseContext context,
        int startRecordNum,
        SecurityContext securityContext,
        FileTypeOptions options)
    {
        var recordsLoaded = 0;
        var recordsFailed = 0;
        var totalCost = 0m;
        var recordNum = startRecordNum;
        var errors = new List<ParseError>();

        // Sub-type support: check if parser needs dual-insert
        var subTypeProvider = parser is ISubTypeRecordProvider stp ? stp : null;
        var subTypeBatch = subTypeProvider != null ? new List<FileDetailRecord>(options.EffectiveBatchSize) : null;

        var batch = new List<NtflChgdtlRecord>(options.EffectiveBatchSize);

        await foreach (var parsed in parser.ParseRecordsStreamingAsync(fileStream, context))
        {
            if (!parsed.IsValid)
            {
                // Log failed record
                var notLoadRecord = CreateNotLoadRecord(parsed, ntFileNum, recordNum);
                await _repository.InsertNotLoadRecordAsync(notLoadRecord);
                recordsFailed++;

                errors.Add(new ParseError
                {
                    ErrorCode = FileErrorCodes.ParseError,
                    Message = parsed.ValidationError ?? "Record validation failed",
                    LineNumber = recordNum,
                    IsFileLevelError = false
                });

                recordNum++;
                continue;
            }

            var chgDetail = CreateChgDetailRecord(parsed, ntFileNum, recordNum);
            batch.Add(chgDetail);

            // Build sub-type record if applicable
            if (subTypeProvider != null)
                subTypeBatch!.Add(subTypeProvider.CreateSubTypeRecord(parsed, ntFileNum, recordNum));

            // Track totals
            if (chgDetail.CostAmount.HasValue)
                totalCost += chgDetail.CostAmount.Value;

            recordsLoaded++;
            recordNum++;

            // Flush batch when full - use optimized batch insert with transaction batching
            if (batch.Count >= options.EffectiveBatchSize)
            {
                var insertResult = await _repository.InsertChargeBatchOptimizedAsync(batch, options.EffectiveTransactionBatchSize);
                if (!insertResult.IsSuccess)
                {
                    _logger.LogError("Batch insert failed: {Error}", insertResult.ErrorMessage);
                }
                batch.Clear();  // Release memory!

                // Flush sub-type batch
                if (subTypeBatch?.Count > 0)
                {
                    await FlushSubTypeBatchAsync(subTypeProvider!, subTypeBatch, options.EffectiveTransactionBatchSize);
                    subTypeBatch.Clear();
                }
            }
        }

        // Flush remaining records
        if (batch.Count > 0)
        {
            var insertResult = await _repository.InsertChargeBatchOptimizedAsync(batch, options.EffectiveTransactionBatchSize);
            if (!insertResult.IsSuccess)
            {
                _logger.LogError("Final batch insert failed: {Error}", insertResult.ErrorMessage);
            }
        }

        // Flush remaining sub-type records
        if (subTypeBatch?.Count > 0)
        {
            await FlushSubTypeBatchAsync(subTypeProvider!, subTypeBatch, options.EffectiveTransactionBatchSize);
        }

        return (recordsLoaded, recordsFailed, totalCost, errors);
    }

    /// <summary>
    /// Process generic records using streaming with batch inserts.
    /// Memory-efficient: only holds current batch in memory.
    /// </summary>
    private async Task<(int loaded, int failed, decimal totalCost, List<ParseError> errors)> ProcessGenericRecordsStreamingAsync(
        int ntFileNum,
        string fileType,
        IFileParser parser,
        Stream fileStream,
        ParseContext context,
        int startRecordNum,
        SecurityContext securityContext,
        FileTypeOptions options)
    {
        var recordsLoaded = 0;
        var recordsFailed = 0;
        var totalCost = 0m;
        var recordNum = startRecordNum;
        var errors = new List<ParseError>();

        // Check if a custom table exists for this file type
        var customTable = await _repository.GetActiveCustomTableAsync(fileType);
        List<GenericColumnMapping>? customMappings = null;
        if (customTable != null)
        {
            var config = await _repository.GetGenericFileFormatConfigAsync(fileType);
            customMappings = config?.ColumnMappings;
            _logger.LogInformation("Using custom table {TableName} for file type {FileType}", customTable.TableName, fileType);
        }

        var batch = new List<GenericDetailRecord>(options.EffectiveBatchSize);

        await foreach (var parsed in parser.ParseRecordsStreamingAsync(fileStream, context))
        {
            if (!parsed.IsValid)
            {
                // Log failed record
                var notLoadRecord = CreateNotLoadRecord(parsed, ntFileNum, recordNum);
                await _repository.InsertNotLoadRecordAsync(notLoadRecord);
                recordsFailed++;

                errors.Add(new ParseError
                {
                    ErrorCode = FileErrorCodes.ParseError,
                    Message = parsed.ValidationError ?? "Record validation failed",
                    LineNumber = recordNum,
                    IsFileLevelError = false
                });

                recordNum++;
                continue;
            }

            var genDetail = CreateGenericDetailRecord(parsed, ntFileNum, recordNum);
            batch.Add(genDetail);

            // Track totals
            if (genDetail.CostAmount.HasValue)
                totalCost += genDetail.CostAmount.Value;

            recordsLoaded++;
            recordNum++;

            // Flush batch when full
            if (batch.Count >= options.EffectiveBatchSize)
            {
                var insertResult = customTable != null && customMappings != null
                    ? await _repository.InsertCustomTableBatchAsync(customTable.TableName, customMappings, batch, options.EffectiveTransactionBatchSize)
                    : await _repository.InsertGenericDetailBatchOptimizedAsync(batch, options.EffectiveTransactionBatchSize);
                if (!insertResult.IsSuccess)
                {
                    _logger.LogError("Batch insert failed: {Error}", insertResult.ErrorMessage);
                }
                batch.Clear();
            }
        }

        // Flush remaining records
        if (batch.Count > 0)
        {
            var insertResult = customTable != null && customMappings != null
                ? await _repository.InsertCustomTableBatchAsync(customTable.TableName, customMappings, batch, options.EffectiveTransactionBatchSize)
                : await _repository.InsertGenericDetailBatchOptimizedAsync(batch, options.EffectiveTransactionBatchSize);
            if (!insertResult.IsSuccess)
            {
                _logger.LogError("Final batch insert failed: {Error}", insertResult.ErrorMessage);
            }
        }

        return (recordsLoaded, recordsFailed, totalCost, errors);
    }

    /// <summary>
    /// Process generic records using legacy memory-based approach.
    /// </summary>
    private async Task<(int loaded, int failed, decimal totalCost)> ProcessGenericRecordsAsync(
        int ntFileNum,
        string fileType,
        List<ParsedRecord> parsedRecords,
        int startRecordNum,
        SecurityContext securityContext,
        FileTypeOptions options)
    {
        var recordsLoaded = 0;
        var recordsFailed = 0;
        var totalCost = 0m;
        var recordNum = startRecordNum;

        // Check if a custom table exists for this file type
        var customTable = await _repository.GetActiveCustomTableAsync(fileType);
        List<GenericColumnMapping>? customMappings = null;
        if (customTable != null)
        {
            var config = await _repository.GetGenericFileFormatConfigAsync(fileType);
            customMappings = config?.ColumnMappings;
            _logger.LogInformation("Using custom table {TableName} for file type {FileType}", customTable.TableName, fileType);
        }

        var batch = new List<GenericDetailRecord>(options.EffectiveBatchSize);

        foreach (var parsed in parsedRecords)
        {
            if (!parsed.IsValid)
            {
                var notLoadRecord = CreateNotLoadRecord(parsed, ntFileNum, recordNum);
                await _repository.InsertNotLoadRecordAsync(notLoadRecord);
                recordsFailed++;
                recordNum++;
                continue;
            }

            var genDetail = CreateGenericDetailRecord(parsed, ntFileNum, recordNum);
            batch.Add(genDetail);

            if (genDetail.CostAmount.HasValue)
                totalCost += genDetail.CostAmount.Value;

            recordsLoaded++;
            recordNum++;

            if (batch.Count >= options.EffectiveBatchSize)
            {
                var insertResult = customTable != null && customMappings != null
                    ? await _repository.InsertCustomTableBatchAsync(customTable.TableName, customMappings, batch)
                    : await _repository.InsertGenericDetailBatchOptimizedAsync(batch);
                if (!insertResult.IsSuccess)
                {
                    _logger.LogError("Batch insert failed: {Error}", insertResult.ErrorMessage);
                }
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            var insertResult = customTable != null && customMappings != null
                ? await _repository.InsertCustomTableBatchAsync(customTable.TableName, customMappings, batch)
                : await _repository.InsertGenericDetailBatchOptimizedAsync(batch);
            if (!insertResult.IsSuccess)
            {
                _logger.LogError("Final batch insert failed: {Error}", insertResult.ErrorMessage);
            }
        }

        return (recordsLoaded, recordsFailed, totalCost);
    }

    /// <summary>
    /// Log parsing errors to ntfl_error_log table.
    /// </summary>
    private async Task LogErrorsAsync(int ntFileNum, List<ParseError> errors)
    {
        if (errors.Count == 0) return;

        var errorRecords = errors.Select((e, index) => new NtflErrorLogRecord
        {
            NtFileNum = ntFileNum,
            ErrorSeq = index + 1,
            ErrorCode = e.ErrorCode,
            ErrorMessage = e.Message,
            LineNumber = e.LineNumber,
            RawData = e.RawData
        }).ToList();

        var result = await _repository.InsertErrorLogBatchAsync(errorRecords);
        if (!result.IsSuccess)
        {
            _logger.LogError("Failed to log errors for file {NtFileNum}: {Error}", ntFileNum, result.ErrorMessage);
        }
        else
        {
            _logger.LogInformation("Logged {Count} errors for file {NtFileNum}", errors.Count, ntFileNum);
        }
    }

    private async Task<(int loaded, int failed, decimal totalCost, DateTime? earliest, DateTime? latest)> ProcessCdrRecordsAsync(
        int ntFileNum,
        List<ParsedRecord> parsedRecords,
        int startRecordNum,
        SecurityContext securityContext,
        FileTypeOptions options)
    {
        var recordsLoaded = 0;
        var recordsFailed = 0;
        var totalCost = 0m;
        DateTime? earliestCall = null;
        DateTime? latestCall = null;
        var recordNum = startRecordNum;

        var batch = new List<ClDetailRecord>(options.EffectiveBatchSize);

        foreach (var parsed in parsedRecords)
        {
            if (!parsed.IsValid)
            {
                // Log failed record to nt_cl_not_load
                var notLoadRecord = CreateNotLoadRecord(parsed, ntFileNum, recordNum);
                await _repository.InsertNotLoadRecordAsync(notLoadRecord);
                recordsFailed++;
                recordNum++;
                continue;
            }

            var clDetail = CreateClDetailRecord(parsed, ntFileNum, recordNum);
            batch.Add(clDetail);

            // Track totals
            if (clDetail.NtCost.HasValue)
                totalCost += clDetail.NtCost.Value;

            if (clDetail.ClStartDt.HasValue)
            {
                if (!earliestCall.HasValue || clDetail.ClStartDt < earliestCall)
                    earliestCall = clDetail.ClStartDt;
                if (!latestCall.HasValue || clDetail.ClStartDt > latestCall)
                    latestCall = clDetail.ClStartDt;
            }

            recordsLoaded++;
            recordNum++;

            // Flush batch when full
            if (batch.Count >= options.EffectiveBatchSize)
            {
                var insertResult = await _repository.InsertClDetailBatchAsync(batch);
                if (!insertResult.IsSuccess)
                {
                    _logger.LogError("Batch insert failed: {Error}", insertResult.ErrorMessage);
                }
                batch.Clear();
            }
        }

        // Flush remaining records
        if (batch.Count > 0)
        {
            var insertResult = await _repository.InsertClDetailBatchAsync(batch);
            if (!insertResult.IsSuccess)
            {
                _logger.LogError("Final batch insert failed: {Error}", insertResult.ErrorMessage);
            }
        }

        return (recordsLoaded, recordsFailed, totalCost, earliestCall, latestCall);
    }

    private async Task<(int loaded, int failed, decimal totalCost)> ProcessChargeRecordsAsync(
        int ntFileNum,
        List<ParsedRecord> parsedRecords,
        int startRecordNum,
        SecurityContext securityContext,
        FileTypeOptions options)
    {
        var recordsLoaded = 0;
        var recordsFailed = 0;
        var totalCost = 0m;
        var recordNum = startRecordNum;

        var batch = new List<NtflChgdtlRecord>(options.EffectiveBatchSize);

        foreach (var parsed in parsedRecords)
        {
            if (!parsed.IsValid)
            {
                // Log failed record
                var notLoadRecord = CreateNotLoadRecord(parsed, ntFileNum, recordNum);
                await _repository.InsertNotLoadRecordAsync(notLoadRecord);
                recordsFailed++;
                recordNum++;
                continue;
            }

            var chgDetail = CreateChgDetailRecord(parsed, ntFileNum, recordNum);
            batch.Add(chgDetail);

            // Track totals
            if (chgDetail.CostAmount.HasValue)
                totalCost += chgDetail.CostAmount.Value;

            recordsLoaded++;
            recordNum++;

            // Flush batch when full
            if (batch.Count >= options.EffectiveBatchSize)
            {
                var insertResult = await _repository.InsertChargeBatchAsync(batch);
                if (!insertResult.IsSuccess)
                {
                    _logger.LogError("Batch insert failed: {Error}", insertResult.ErrorMessage);
                }
                batch.Clear();
            }
        }

        // Flush remaining records
        if (batch.Count > 0)
        {
            var insertResult = await _repository.InsertChargeBatchAsync(batch);
            if (!insertResult.IsSuccess)
            {
                _logger.LogError("Final batch insert failed: {Error}", insertResult.ErrorMessage);
            }
        }

        return (recordsLoaded, recordsFailed, totalCost);
    }

    private ClDetailRecord CreateClDetailRecord(ParsedRecord parsed, int ntFileNum, int recordNum)
    {
        return new ClDetailRecord
        {
            NtFileNum = ntFileNum,
            NtFileRecNum = recordNum,
            ServiceReference = GetFieldInt(parsed, "SpCnRef"),
            SpPlanRef = GetFieldInt(parsed, "SpPlanRef"),
            NumCalled = GetFieldString(parsed, "NumCalled") ?? GetFieldString(parsed, "CalledNumber"),
            TarClassCode = GetFieldShort(parsed, "TarClassCode"),
            ClStartDt = GetFieldDateTime(parsed, "ClStartDt") ?? GetFieldDateTime(parsed, "CallDateTime"),
            Unit = GetFieldString(parsed, "Unit") ?? "S",
            UnitQuantity = GetFieldDecimal(parsed, "UnitQuantity"),
            ClDuration = GetFieldTimeSpan(parsed, "ClDuration") ?? GetFieldTimeSpan(parsed, "Duration") ?? TimeSpan.FromSeconds(GetFieldInt(parsed, "DurationSeconds") ?? 0),
            NtCost = GetFieldDecimal(parsed, "NtCost") ?? GetFieldDecimal(parsed, "ChargeAmount"),
            NtCostEx = GetFieldDecimal(parsed, "NtCostEx"),
            NtCostTax = GetFieldDecimal(parsed, "NtCostTax"),
            RtlNonDiscEx = GetFieldDecimal(parsed, "RtlNonDiscEx"),
            RtlNonDiscTax = GetFieldDecimal(parsed, "RtlNonDiscTax"),
            RtlDiscEx = GetFieldDecimal(parsed, "RtlDiscEx"),
            RtlDiscTax = GetFieldDecimal(parsed, "RtlDiscTax"),
            TimebandCode = GetFieldString(parsed, "TimebandCode"),
            BpartyDestn = GetFieldString(parsed, "BpartyDestn"),
            ClStartDtSrvr = GetFieldDateTime(parsed, "ClStartDtSrvr"),
            NtClSvin = GetFieldString(parsed, "NtClSvin") ?? GetFieldString(parsed, "ServiceId"),
            ClStatus = GetFieldShort(parsed, "ClStatus"),
            TariffNum = GetFieldInt(parsed, "TariffNum"),
            ProcessRef = GetFieldInt(parsed, "ProcessRef"),
            ClDtTabcd = GetFieldString(parsed, "ClDtTabcd")
        };
    }

    private NtflChgdtlRecord CreateChgDetailRecord(ParsedRecord parsed, int ntFileNum, int recordNum)
    {
        return new NtflChgdtlRecord
        {
            NtFileNum = ntFileNum,
            NtFileRecNum = recordNum,
            PhoneNum = GetFieldString(parsed, "PhoneNum") ?? GetFieldString(parsed, "ServiceId"),
            ServiceReference = GetFieldInt(parsed, "SpCnRef"),
            SpPlanRef = GetFieldInt(parsed, "SpPlanRef"),
            SpCnChgRef = GetFieldInt(parsed, "SpCnChgRef"),
            ChgCode = GetFieldString(parsed, "ChgCode") ?? GetFieldString(parsed, "ChargeCode"),
            StartDate = GetFieldDateTime(parsed, "StartDate") ?? GetFieldDateTime(parsed, "PeriodFrom"),
            EndDate = GetFieldDateTime(parsed, "EndDate") ?? GetFieldDateTime(parsed, "PeriodTo"),
            CalcAmount = GetFieldDecimal(parsed, "CalcAmount"),
            CalcGst = GetFieldDecimal(parsed, "CalcGst"),
            ManAmount = GetFieldDecimal(parsed, "ManAmount"),
            ManGst = GetFieldDecimal(parsed, "ManGst"),
            CostAmount = GetFieldDecimal(parsed, "CostAmount") ?? GetFieldDecimal(parsed, "ChargeAmount"),
            CostGst = GetFieldDecimal(parsed, "CostGst") ?? GetFieldDecimal(parsed, "TaxAmount"),
            UnitQuantity = GetFieldDecimal(parsed, "UnitQuantity") ?? GetFieldDecimal(parsed, "Quantity"),
            DestDb = GetFieldString(parsed, "DestDb"),
            ChgNarr = GetFieldString(parsed, "ChgNarr") ?? GetFieldString(parsed, "ChargeDescription"),
            UseNetPrice = GetFieldString(parsed, "UseNetPrice"),
            NetPrcProrated = GetFieldString(parsed, "NetPrcProrated"),
            Frequency = GetFieldString(parsed, "Frequency"),
            UpliftPerc = GetFieldDecimal(parsed, "UpliftPerc"),
            UpliftAmt = GetFieldDecimal(parsed, "UpliftAmt"),
            ProrateRatio = GetFieldDecimal(parsed, "ProrateRatio"),
            ChDtTabcd = GetFieldString(parsed, "ChDtTabcd"),
            NtChgReasCode = GetFieldString(parsed, "NtChgReasCode"),
            Note = GetFieldString(parsed, "Note"),
            CaseNo = GetFieldString(parsed, "CaseNo"),
            NtRef = GetFieldString(parsed, "NtRef") ?? GetFieldString(parsed, "ExternalRef")
        };
    }

    private NtClNotLoadRecord CreateNotLoadRecord(ParsedRecord parsed, int ntFileNum, int recordNum)
    {
        return new NtClNotLoadRecord
        {
            NtFileNum = ntFileNum,
            NtFileRecNum = recordNum,
            ServiceReference = GetFieldInt(parsed, "SpCnRef"),
            ClDtTabcd = GetFieldString(parsed, "ClDtTabcd"),
            PhoneNum = GetFieldString(parsed, "PhoneNum") ?? GetFieldString(parsed, "ServiceId"),
            ClStartDt = GetFieldDateTime(parsed, "ClStartDt") ?? GetFieldDateTime(parsed, "CallDateTime"),
            NumCalled = GetFieldString(parsed, "NumCalled") ?? GetFieldString(parsed, "CalledNumber"),
            Unit = GetFieldString(parsed, "Unit"),
            UnitQuantity = GetFieldDecimal(parsed, "UnitQuantity"),
            ClDuration = GetFieldTimeSpan(parsed, "ClDuration"),
            TarClassCode = GetFieldShort(parsed, "TarClassCode"),
            NtCost = GetFieldDecimal(parsed, "NtCost") ?? GetFieldDecimal(parsed, "ChargeAmount"),
            NtCostEx = GetFieldDecimal(parsed, "NtCostEx"),
            NtCostTax = GetFieldDecimal(parsed, "NtCostTax"),
            ErrCode = "PARSE_ERROR",
            StatusId = "E",
            StatusDesc = parsed.ValidationError ?? "Validation failed"
        };
    }

    private GenericDetailRecord CreateGenericDetailRecord(ParsedRecord parsed, int ntFileNum, int recordNum)
    {
        var record = new GenericDetailRecord
        {
            NtFileNum = ntFileNum,
            NtFileRecNum = recordNum,
            AccountCode = GetFieldString(parsed, "AccountCode"),
            ServiceId = GetFieldString(parsed, "ServiceId"),
            ChargeType = GetFieldString(parsed, "ChargeType"),
            CostAmount = GetFieldDecimal(parsed, "CostAmount"),
            TaxAmount = GetFieldDecimal(parsed, "TaxAmount"),
            Quantity = GetFieldDecimal(parsed, "Quantity"),
            UOM = GetFieldString(parsed, "UOM"),
            FromDate = GetFieldDateTime(parsed, "FromDate"),
            ToDate = GetFieldDateTime(parsed, "ToDate"),
            Description = GetFieldString(parsed, "Description"),
            ExternalRef = GetFieldString(parsed, "ExternalRef"),
            RawData = GetFieldString(parsed, "RawData"),
            StatusId = TransactionStatus.New
        };

        // Map Generic01..Generic20
        for (int i = 1; i <= 20; i++)
        {
            var fieldName = $"Generic{i:D2}";
            var value = GetFieldString(parsed, fieldName);
            if (value != null)
                record.SetGenericField(i, value);
        }

        // Copy all parsed fields for custom table inserts
        foreach (var kvp in parsed.Fields)
        {
            record.ParsedFields[kvp.Key] = kvp.Value;
        }

        return record;
    }

    private static string? GetFieldString(ParsedRecord record, string fieldName)
    {
        return record.Fields.TryGetValue(fieldName, out var value) ? value?.ToString() : null;
    }

    private static DateTime? GetFieldDateTime(ParsedRecord record, string fieldName)
    {
        if (!record.Fields.TryGetValue(fieldName, out var value) || value == null)
            return null;

        return value is DateTime dt ? dt : null;
    }

    private static TimeSpan? GetFieldTimeSpan(ParsedRecord record, string fieldName)
    {
        if (!record.Fields.TryGetValue(fieldName, out var value) || value == null)
            return null;

        return value is TimeSpan ts ? ts : null;
    }

    private static int? GetFieldInt(ParsedRecord record, string fieldName)
    {
        if (!record.Fields.TryGetValue(fieldName, out var value) || value == null)
            return null;

        return value is int i ? i : (int.TryParse(value.ToString(), out var parsed) ? parsed : null);
    }

    private static short? GetFieldShort(ParsedRecord record, string fieldName)
    {
        if (!record.Fields.TryGetValue(fieldName, out var value) || value == null)
            return null;

        return value is short s ? s : (short.TryParse(value.ToString(), out var parsed) ? parsed : null);
    }

    private static decimal? GetFieldDecimal(ParsedRecord record, string fieldName)
    {
        if (!record.Fields.TryGetValue(fieldName, out var value) || value == null)
            return null;

        return value is decimal d ? d : (decimal.TryParse(value.ToString(), out var parsed) ? parsed : null);
    }

    /// <summary>
    /// Flush a batch of sub-type records to the appropriate table.
    /// </summary>
    private async Task FlushSubTypeBatchAsync(ISubTypeRecordProvider provider, List<FileDetailRecord> batch, int transactionBatchSize)
    {
        if (batch.Count == 0) return;

        RawCommandResult insertResult;
        if (provider.SubTypeTableName == "ssswhls_cdr")
        {
            insertResult = await _repository.InsertSssWhlsCdrBatchAsync(batch.Cast<SssWhlsCdrRecord>(), transactionBatchSize);
        }
        else if (provider.SubTypeTableName == "ssswhlschg")
        {
            insertResult = await _repository.InsertSssWhlsChgBatchAsync(batch.Cast<SssWhlsChgRecord>(), transactionBatchSize);
        }
        else
        {
            _logger.LogWarning("Unknown sub-type table: {TableName}", provider.SubTypeTableName);
            return;
        }

        if (!insertResult.IsSuccess)
        {
            _logger.LogError("Sub-type batch insert to {Table} failed: {Error}", provider.SubTypeTableName, insertResult.ErrorMessage);
        }
    }

    public async Task<DataResult<FileStatusResponse>> GetFileStatusAsync(int ntFileNum, SecurityContext securityContext)
    {
        return await _repository.GetFileStatusAsync(ntFileNum, securityContext);
    }

    public async Task<DataResult<FileListResponse>> ListFilesAsync(
        string? fileTypeCode,
        string? ntCustNum,
        int skipRecords,
        int takeRecords,
        string countRecords,
        SecurityContext securityContext,
        int? statusId = null)
    {
        return await _repository.ListFilesAsync(fileTypeCode, ntCustNum, skipRecords, takeRecords, countRecords, securityContext, statusId);
    }

    public async Task<DataResult<FileTypeListResponse>> ListFileTypesAsync(SecurityContext securityContext)
    {
        return await _repository.GetFileTypesAsync(securityContext);
    }

    public async Task<DataResult<FileLoadResponse>> ReprocessFileAsync(int ntFileNum, SecurityContext securityContext)
    {
        // Get current file status
        var statusResult = await _repository.GetFileStatusAsync(ntFileNum, securityContext);
        if (!statusResult.IsSuccess || statusResult.Data == null)
        {
            return new DataResult<FileLoadResponse>
            {
                StatusCode = statusResult.StatusCode,
                ErrorCode = statusResult.ErrorCode ?? "NOT_FOUND",
                ErrorMessage = statusResult.ErrorMessage ?? $"File {ntFileNum} not found"
            };
        }

        var file = statusResult.Data;

        // Reset status to loading
        await _repository.UpdateFileStatusAsync(ntFileNum, FileStatus.Transferred, securityContext);

        return new DataResult<FileLoadResponse>
        {
            StatusCode = 202,
            Data = new FileLoadResponse
            {
                NtFileNum = ntFileNum,
                FileName = file.FileName,
                FileType = file.FileType,
                Status = FileStatus.GetDescription(FileStatus.Transferred),
                StatusId = FileStatus.Transferred,
                StartedAt = DateTime.Now
            }
        };
    }

    /// <summary>
    /// Get error logs for a file.
    /// </summary>
    public async Task<DataResult<List<NtflErrorLogRecord>>> GetFileErrorsAsync(int ntFileNum, SecurityContext securityContext)
    {
        return await _repository.GetErrorLogsAsync(ntFileNum);
    }
}

/// <summary>
/// Internal result from file processing.
/// </summary>
internal class ProcessingResult
{
    public bool Success { get; set; }
    public int StatusId { get; set; }
    public string? ErrorMessage { get; set; }
    public int RecordsLoaded { get; set; }
    public int RecordsFailed { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? EarliestCall { get; set; }
    public DateTime? LatestCall { get; set; }
}
