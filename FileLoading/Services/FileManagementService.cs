using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Selcomm.Data.Common;
using FileLoading.Interfaces;
using FileLoading.Models;
using FileLoading.Repositories;
using FileLoading.Transfer;
using FileLoading.Validation;

namespace FileLoading.Services;

/// <summary>
/// Service implementation for file management operations.
/// </summary>
public class FileManagementService : IFileManagementService
{
    private readonly IFileLoaderRepository _repository;
    private readonly IFileLoaderService _fileLoaderService;
    private readonly IFileTransferService _transferService;
    private readonly IConfiguration _configuration;
    private readonly CompressionHelper _compressionHelper;
    private readonly ILogger<FileManagementService> _logger;

    public FileManagementService(
        IFileLoaderRepository repository,
        IFileLoaderService fileLoaderService,
        IFileTransferService transferService,
        IConfiguration configuration,
        CompressionHelper compressionHelper,
        ILogger<FileManagementService> logger)
    {
        _repository = repository;
        _fileLoaderService = fileLoaderService;
        _transferService = transferService;
        _configuration = configuration;
        _compressionHelper = compressionHelper;
        _logger = logger;
    }

    // ============================================
    // User File Operations
    // ============================================

    public async Task<DataResult<FileWithStatus>> UploadToTransferAsync(IFormFile file, string fileTypeCode, SecurityContext context)
    {
        _logger.LogInformation("Uploading file to transfer workflow: {FileName}, Type: {FileTypeCode}", file.FileName, fileTypeCode);

        // Get folder config for this file type
        var folderResult = await _transferService.GetFolderConfigAsync(fileTypeCode, context);
        if (!folderResult.IsSuccess || folderResult.Data == null)
            return new DataResult<FileWithStatus> { StatusCode = 400, ErrorCode = "FileLoading.NoFolderConfig", ErrorMessage = $"No folder configuration found for file type '{fileTypeCode}'" };

        var folderConfig = folderResult.Data;

        // Save file to Transfer folder first
        var transferFolder = folderConfig.TransferFolder;
        if (!Directory.Exists(transferFolder))
            Directory.CreateDirectory(transferFolder);

        var destPath = Path.Combine(transferFolder, file.FileName);
        using (var fs = new FileStream(destPath, FileMode.Create))
        {
            await file.CopyToAsync(fs);
        }

        // Create transfer record in Transfer folder
        var transferRecord = new FileTransferRecord
        {
            FileName = file.FileName,
            Status = TransferStatus.Downloaded,
            DestinationPath = destPath,
            CurrentFolder = "Transfer",
            FileSize = file.Length,
            StartedAt = DateTime.Now,
            CreatedBy = context.UserCode ?? "SYSTEM"
        };

        var insertResult = await _repository.InsertTransferRecordAsync(transferRecord);
        if (!insertResult.IsSuccess)
            return new DataResult<FileWithStatus> { StatusCode = 500, ErrorCode = "FileLoading.TransferRecordFailed", ErrorMessage = "Failed to create transfer record" };

        transferRecord.TransferId = insertResult.Value;

        _logger.LogInformation("Created transfer record {TransferId} for {FileName} in Transfer folder", transferRecord.TransferId, file.FileName);

        return new DataResult<FileWithStatus>
        {
            StatusCode = 201,
            Data = new FileWithStatus
            {
                TransferId = transferRecord.TransferId,
                FileName = file.FileName,
                CurrentFolder = "Transfer",
                StatusId = TransferStatus.Downloaded,
                FileTypeCode = fileTypeCode
            }
        };
    }

    public async Task<DataResult<FileLoadResponse>> ProcessFileAsync(int transferId, SecurityContext context, string? fileTypeCode = null)
    {
        _logger.LogInformation("Processing file transfer {TransferId}", transferId);

        // Get transfer record
        var transferResult = await _repository.GetTransferRecordAsync(transferId);
        if (!transferResult.IsSuccess || transferResult.Data == null)
        {
            return new DataResult<FileLoadResponse>
            {
                StatusCode = transferResult.StatusCode,
                ErrorCode = transferResult.ErrorCode,
                ErrorMessage = transferResult.ErrorMessage
            };
        }

        var transfer = transferResult.Data;

        // Determine file type: explicit parameter > source config > filename inference
        var sourceResult = await _repository.GetTransferSourceAsync(transfer.SourceId ?? 0);
        var fileType = fileTypeCode ?? sourceResult.Data?.FileTypeCode ?? DetermineFileType(transfer.FileName);

        // Move to processing folder if not already there
        if (transfer.CurrentFolder?.ToUpper() != "PROCESSING")
        {
            // Try standard source-based move first; fall back to file-type folder config
            var moveResult = await _transferService.TransferToProcessingAsync(transferId, context);
            if (!moveResult.IsSuccess && !string.IsNullOrEmpty(fileType))
            {
                // Source not found — try moving via file type folder config directly
                moveResult = await _transferService.MoveToFolderAsync(transferId, "Processing", true, context, fileType);
            }
            if (!moveResult.IsSuccess)
            {
                return new DataResult<FileLoadResponse>
                {
                    StatusCode = moveResult.StatusCode,
                    ErrorCode = moveResult.ErrorCode,
                    ErrorMessage = moveResult.ErrorMessage
                };
            }
            transfer = moveResult.Data!;
        }

        // Update status to processing
        await _repository.UpdateTransferStatusAsync(transferId, TransferStatus.Processing, null);

        // Log activity
        await LogActivityAsync(new FileActivityLog
        {
            TransferId = transferId,
            FileName = transfer.FileName,
            ActivityType = FileActivityType.ProcessingStarted,
            Description = $"Started processing file {transfer.FileName}",
            UserId = context.UserCode ?? "SYSTEM",
        }, context);

        try
        {

            if (string.IsNullOrEmpty(fileType))
            {
                var errorMsg = $"Cannot determine file type for {transfer.FileName}";
                await _repository.UpdateTransferStatusAsync(transferId, TransferStatus.Error, errorMsg, DateTime.Now);
                await MoveToErrorsFolder(transfer, context, fileType);
                return new DataResult<FileLoadResponse>
                {
                    StatusCode = 400,
                    ErrorCode = "FileLoading.FileTypeNotFound",
                    ErrorMessage = errorMsg
                };
            }

            // Look up the file_type_nt record to get the correct NtCustNum
            var fileTypeNt = await _repository.GetFileTypeNtRecordAsync(fileType);
            var ntCustNum = fileTypeNt.Data?.NtCustNum;

            // Call file loader service
            var loadRequest = new LoadFileRequest
            {
                FileName = transfer.DestinationPath!,
                FileType = fileType,
                NtCustNum = ntCustNum,
                DisplayFileName = transfer.FileName
            };

            var loadResult = await _fileLoaderService.LoadFileAsync(loadRequest, context);

            if (loadResult.IsSuccess && loadResult.Data != null)
            {
                // Update transfer with nt_file_num
                await _repository.UpdateTransferNtFileNumAsync(transferId, loadResult.Data.NtFileNum);
                await _repository.UpdateTransferStatusAsync(transferId, TransferStatus.Processed, null, DateTime.Now);

                // Move to processed folder
                await _transferService.MoveToFolderAsync(transferId, "Processed", true, context, fileType);

                // Log success
                await LogActivityAsync(new FileActivityLog
                {
                    NtFileNum = loadResult.Data.NtFileNum,
                    TransferId = transferId,
                    FileName = transfer.FileName,
                    ActivityType = FileActivityType.ProcessingCompleted,
                    Description = $"Successfully processed file {transfer.FileName}. Loaded {loadResult.Data.RecordsLoaded} records.",
                    UserId = context.UserCode ?? "SYSTEM",
                        }, context);

                return loadResult;
            }
            else
            {
                // Processing failed
                var errorMsg = loadResult.ErrorMessage ?? "Processing failed";
                await _repository.UpdateTransferStatusAsync(transferId, TransferStatus.Error, errorMsg, DateTime.Now);
                await MoveToErrorsFolder(transfer, context, fileType);

                // Log failure
                await LogActivityAsync(new FileActivityLog
                {
                    TransferId = transferId,
                    FileName = transfer.FileName,
                    ActivityType = FileActivityType.ProcessingFailed,
                    Description = $"Failed to process file {transfer.FileName}: {errorMsg}",
                    UserId = context.UserCode ?? "SYSTEM",
                        }, context);

                return loadResult;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file {FileName}", transfer.FileName);
            await _repository.UpdateTransferStatusAsync(transferId, TransferStatus.Error, ex.Message, DateTime.Now);
            await MoveToErrorsFolder(transfer, context, fileType);

            return new DataResult<FileLoadResponse>
            {
                StatusCode = 500,
                ErrorCode = "FileLoading.ProcessingError",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<DataResult<bool>> UnloadFileAsync(int ntFileNum, SecurityContext context)
    {
        _logger.LogInformation("Unloading file {NtFileNum}", ntFileNum);

        try
        {
            // Get file info
            var fileStatus = await _repository.GetFileStatusAsync(ntFileNum, context);
            if (!fileStatus.IsSuccess)
            {
                return new DataResult<bool>
                {
                    StatusCode = fileStatus.StatusCode,
                    Data = false,
                    ErrorCode = fileStatus.ErrorCode,
                    ErrorMessage = fileStatus.ErrorMessage
                };
            }

            // Unload records and reset status to Transferred
            var result = await _repository.UnloadFileRecordsAsync(ntFileNum, context);
            await _repository.UpdateFileStatusAsync(ntFileNum, FileStatus.Transferred, context);

            // Log activity
            await LogActivityAsync(new FileActivityLog
            {
                NtFileNum = ntFileNum,
                FileName = fileStatus.Data?.FileName ?? "Unknown",
                ActivityType = FileActivityType.FileUnloaded,
                Description = $"Unloaded file {fileStatus.Data?.FileName}. Deleted {result.RowsAffected} records.",
                UserId = context.UserCode ?? "SYSTEM",
                }, context);

            return new DataResult<bool>
            {
                StatusCode = 200,
                Data = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unloading file {NtFileNum}", ntFileNum);
            return new DataResult<bool>
            {
                StatusCode = 500,
                Data = false,
                ErrorCode = "FileLoading.UnloadError",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<DataResult<bool>> ForceSequenceSkipAsync(
        int ntFileNum, int skipToSequence, string? reason, SecurityContext context)
    {
        _logger.LogInformation("Force skipping sequence for file {NtFileNum} to {Sequence}", ntFileNum, skipToSequence);

        // This would update the expected sequence in the file_type or nt_file tables
        // Implementation depends on how sequence validation is handled

        // Log activity
        await LogActivityAsync(new FileActivityLog
        {
            NtFileNum = ntFileNum,
            ActivityType = FileActivityType.SequenceSkipped,
            Description = $"Sequence skipped to {skipToSequence}. Reason: {reason ?? "Not specified"}",
            Details = JsonSerializer.Serialize(new { SkipToSequence = skipToSequence, Reason = reason }),
            UserId = context.UserCode ?? "SYSTEM",
        }, context);

        return new DataResult<bool>
        {
            StatusCode = 200,
            Data = true
        };
    }

    public async Task<DataResult<bool>> MoveFileAsync(
        int transferId, string targetFolder, string? reason, SecurityContext context)
    {
        _logger.LogInformation("Moving file {TransferId} to {Folder}", transferId, targetFolder);

        var transferResult = await _repository.GetTransferRecordAsync(transferId);
        if (!transferResult.IsSuccess || transferResult.Data == null)
        {
            return new DataResult<bool>
            {
                StatusCode = transferResult.StatusCode,
                Data = false,
                ErrorCode = transferResult.ErrorCode,
                ErrorMessage = transferResult.ErrorMessage
            };
        }

        var transfer = transferResult.Data;

        // Move file
        var compress = targetFolder.ToUpper() == "PROCESSED" || targetFolder.ToUpper() == "SKIPPED";
        var moveResult = await _transferService.MoveToFolderAsync(transferId, targetFolder, compress, context);

        if (!moveResult.IsSuccess)
        {
            return new DataResult<bool>
            {
                StatusCode = moveResult.StatusCode,
                Data = false,
                ErrorCode = moveResult.ErrorCode,
                ErrorMessage = moveResult.ErrorMessage
            };
        }

        // Determine activity type
        var activityType = targetFolder.ToUpper() switch
        {
            "PROCESSING" => FileActivityType.MovedToProcessing,
            "PROCESSED" => FileActivityType.MovedToProcessed,
            "ERRORS" => FileActivityType.MovedToErrors,
            "SKIPPED" => FileActivityType.MovedToSkipped,
            _ => FileActivityType.MovedToProcessing
        };

        // Log activity
        await LogActivityAsync(new FileActivityLog
        {
            TransferId = transferId,
            NtFileNum = transfer.NtFileNum,
            FileName = transfer.FileName,
            ActivityType = activityType,
            Description = $"Moved file {transfer.FileName} to {targetFolder}. {reason ?? ""}",
            UserId = context.UserCode ?? "SYSTEM",
        }, context);

        return new DataResult<bool>
        {
            StatusCode = 200,
            Data = true
        };
    }

    public async Task<DataResult<bool>> DeleteFileAsync(int transferId, SecurityContext context)
    {
        _logger.LogInformation("Deleting file {TransferId}", transferId);

        var transferResult = await _repository.GetTransferRecordAsync(transferId);
        if (!transferResult.IsSuccess || transferResult.Data == null)
        {
            return new DataResult<bool>
            {
                StatusCode = transferResult.StatusCode,
                Data = false,
                ErrorCode = transferResult.ErrorCode,
                ErrorMessage = transferResult.ErrorMessage
            };
        }

        var transfer = transferResult.Data;

        try
        {
            // Delete physical file
            if (!string.IsNullOrEmpty(transfer.DestinationPath) && File.Exists(transfer.DestinationPath))
            {
                File.Delete(transfer.DestinationPath);
            }

            // Delete transfer record
            await _repository.DeleteTransferRecordAsync(transferId);

            // Log activity
            await LogActivityAsync(new FileActivityLog
            {
                FileName = transfer.FileName,
                ActivityType = FileActivityType.FileDeleted,
                Description = $"Deleted file {transfer.FileName}",
                UserId = context.UserCode ?? "SYSTEM",
                }, context);

            return new DataResult<bool>
            {
                StatusCode = 200,
                Data = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FileName}", transfer.FileName);
            return new DataResult<bool>
            {
                StatusCode = 500,
                Data = false,
                ErrorCode = "FileLoading.DeleteError",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<DataResult<FileDownloadResult>> DownloadFileAsync(int transferId, SecurityContext context)
    {
        _logger.LogInformation("Downloading file {TransferId} for user", transferId);

        var transferResult = await _repository.GetTransferRecordAsync(transferId);
        if (!transferResult.IsSuccess || transferResult.Data == null)
        {
            return new DataResult<FileDownloadResult>
            {
                StatusCode = transferResult.StatusCode,
                ErrorCode = transferResult.ErrorCode,
                ErrorMessage = transferResult.ErrorMessage
            };
        }

        var transfer = transferResult.Data;

        if (string.IsNullOrEmpty(transfer.DestinationPath) || !File.Exists(transfer.DestinationPath))
        {
            return new DataResult<FileDownloadResult>
            {
                StatusCode = 404,
                ErrorCode = "FileLoading.FileNotFound",
                ErrorMessage = "Physical file not found"
            };
        }

        try
        {
            var fileInfo = new FileInfo(transfer.DestinationPath);
            var stream = File.OpenRead(transfer.DestinationPath);

            // Determine content type
            var contentType = GetContentType(transfer.FileName);

            // Log activity
            await LogActivityAsync(new FileActivityLog
            {
                TransferId = transferId,
                NtFileNum = transfer.NtFileNum,
                FileName = transfer.FileName,
                ActivityType = FileActivityType.BrowserDownload,
                Description = $"User downloaded file {transfer.FileName}",
                UserId = context.UserCode ?? "SYSTEM",
                }, context);

            return new DataResult<FileDownloadResult>
            {
                StatusCode = 200,
                Data = new FileDownloadResult
                {
                    Stream = stream,
                    ContentType = contentType,
                    FileName = transfer.FileName,
                    FileSize = fileInfo.Length
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {FileName}", transfer.FileName);
            return new DataResult<FileDownloadResult>
            {
                StatusCode = 500,
                ErrorCode = "FileLoading.DownloadError",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<DataResult<FileLoadResponse>> RetryProcessingAsync(int transferId, SecurityContext context)
    {
        _logger.LogInformation("Retrying processing for file {TransferId}", transferId);

        var transferResult = await _repository.GetTransferRecordAsync(transferId);
        if (!transferResult.IsSuccess || transferResult.Data == null)
        {
            return new DataResult<FileLoadResponse>
            {
                StatusCode = transferResult.StatusCode,
                ErrorCode = transferResult.ErrorCode,
                ErrorMessage = transferResult.ErrorMessage
            };
        }

        var transfer = transferResult.Data;

        // Increment retry count
        // Move back to transfer folder first
        await _transferService.MoveToFolderAsync(transferId, "Transfer", false, context);

        // Then process
        return await ProcessFileAsync(transferId, context);
    }

    // ============================================
    // Dashboard Queries
    // ============================================

    public async Task<DataResult<FileManagementDashboard>> GetDashboardAsync(
        string? fileTypeCode, SecurityContext context)
    {
        var dashboardResult = await _repository.GetDashboardSummaryAsync(fileTypeCode);

        if (dashboardResult.IsSuccess && dashboardResult.Data != null)
        {
            // Get source statuses
            var statusesResult = await _repository.GetSourceStatusesAsync();
            if (statusesResult.IsSuccess && statusesResult.Data != null)
            {
                dashboardResult.Data.SourceStatuses = statusesResult.Data;
            }
        }

        return dashboardResult;
    }

    public async Task<DataResult<List<NtFileSearchResult>>> SearchNtFilesAsync(string search, SecurityContext context)
    {
        return await _repository.SearchNtFilesAsync(search);
    }

    public async Task<DataResult<FileWithStatusResponse>> ListFilesAsync(
        FileListFilter filter, SecurityContext context)
    {
        return await _repository.ListFilesWithStatusAsync(filter);
    }

    public async Task<DataResult<FileWithStatus>> GetFileDetailsAsync(int transferId, SecurityContext context)
    {
        var transferResult = await _repository.GetTransferRecordAsync(transferId);
        if (!transferResult.IsSuccess || transferResult.Data == null)
        {
            return new DataResult<FileWithStatus>
            {
                StatusCode = transferResult.StatusCode,
                ErrorCode = transferResult.ErrorCode,
                ErrorMessage = transferResult.ErrorMessage
            };
        }

        var transfer = transferResult.Data;

        // Get source info for domain
        var sourceResult = await _repository.GetTransferSourceAsync(transfer.SourceId ?? 0);

        return new DataResult<FileWithStatus>
        {
            StatusCode = 200,
            Data = new FileWithStatus
            {
                TransferId = transfer.TransferId,
                NtFileNum = transfer.NtFileNum,
                FileName = transfer.FileName,
                FileTypeCode = sourceResult.Data?.FileTypeCode,
                CurrentFolder = transfer.CurrentFolder ?? "",
                StatusId = transfer.Status,
                Status = GetStatus(transfer.Status),
                FileSize = transfer.FileSize,
                CreatedAt = transfer.CreatedAt,
                CompletedAt = transfer.CompletedAt,
                ErrorMessage = transfer.ErrorMessage,
                SourceId = transfer.SourceId
            }
        };
    }

    // ============================================
    // Activity Logging
    // ============================================

    public async Task<DataResult<ActivityLogResponse>> GetActivityLogAsync(
        int? ntFileNum, int? transferId, int skipRecords, int takeRecords, string countRecords, SecurityContext context)
    {
        return await _repository.GetActivityLogsAsync(ntFileNum, transferId, skipRecords, takeRecords, countRecords ?? "F");
    }

    public async Task LogActivityAsync(FileActivityLog activity, SecurityContext context)
    {
        activity.ActivityAt = DateTime.Now;
        if (string.IsNullOrEmpty(activity.UserId))
        {
            activity.UserId = context.UserCode ?? "SYSTEM";
        }

        await _repository.InsertActivityLogAsync(activity);
    }

    // ============================================
    // Validation Summary
    // ============================================

    public async Task<DataResult<ValidationSummaryForAI>> GetValidationSummaryAsync(
        int ntFileNum, SecurityContext context)
    {
        var result = await _repository.GetValidationSummaryAsync(ntFileNum);

        if (result.IsSuccess && result.Data != null)
        {
            return new DataResult<ValidationSummaryForAI>
            {
                StatusCode = 200,
                Data = result.Data
            };
        }

        return new DataResult<ValidationSummaryForAI>
        {
            StatusCode = result.StatusCode,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    // ============================================
    // Exception/Error Management
    // ============================================

    public async Task<DataResult<FileWithStatusResponse>> GetFilesWithErrorsAsync(
        string? fileTypeCode, int skipRecords, int takeRecords, string countRecords, SecurityContext context)
    {
        var filter = new FileListFilter
        {
            FileTypeCode = fileTypeCode,
            Status = TransferStatus.Error,
            SkipRecords = skipRecords,
            TakeRecords = takeRecords,
            CountRecords = countRecords ?? "F"
        };

        return await _repository.ListFilesWithStatusAsync(filter);
    }

    public async Task<DataResult<FileWithStatusResponse>> GetSkippedFilesAsync(
        string? fileTypeCode, int skipRecords, int takeRecords, string countRecords, SecurityContext context)
    {
        var filter = new FileListFilter
        {
            FileTypeCode = fileTypeCode,
            Status = TransferStatus.Skipped,
            SkipRecords = skipRecords,
            TakeRecords = takeRecords,
            CountRecords = countRecords ?? "F"
        };

        return await _repository.ListFilesWithStatusAsync(filter);
    }

    // ============================================
    // Duplicate Detection
    // ============================================

    public async Task<DataResult<DuplicateFilesResponse>> GetDuplicateFilesAsync(
        string? fileTypeCode, bool includeIgnored, int skipRecords, int takeRecords, string countRecords, SecurityContext context)
    {
        return await _repository.GetDuplicateFilesAsync(fileTypeCode, includeIgnored, skipRecords, takeRecords, countRecords ?? "F");
    }

    public async Task<RawCommandResult> IgnoreDuplicateAsync(string fileHash, int ntFileNum, string? reason, SecurityContext context)
    {
        return await _repository.IgnoreDuplicateAsync(fileHash, ntFileNum, context.UserCode, reason);
    }

    public async Task<RawCommandResult> UnignoreDuplicateAsync(string fileHash, SecurityContext context)
    {
        return await _repository.UnignoreDuplicateAsync(fileHash);
    }

    // ============================================
    // Helper Methods
    // ============================================

    private async Task MoveToErrorsFolder(FileTransferRecord transfer, SecurityContext context, string? fileTypeCode = null)
    {
        try
        {
            await _transferService.MoveToFolderAsync(transfer.TransferId, "Errors", false, context, fileTypeCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move file to errors folder: {FileName}", transfer.FileName);
        }
    }

    private static string? DetermineFileType(string fileName)
    {
        // Try to determine file type from filename patterns
        var name = Path.GetFileNameWithoutExtension(fileName).ToUpper();

        if (name.Contains("CDR")) return "CDR";
        if (name.Contains("CHG")) return "CHG";
        if (name.Contains("SVC")) return "SVC";
        if (name.Contains("ORD")) return "ORD";
        if (name.Contains("EBL")) return "EBL";

        return null;
    }

    private static string GetStatus(TransferStatus status) => status switch
    {
        TransferStatus.Pending => "Pending",
        TransferStatus.Downloading => "Downloading",
        TransferStatus.Downloaded => "Downloaded",
        TransferStatus.Processing => "Processing",
        TransferStatus.Processed => "Processed",
        TransferStatus.Error => "Error",
        TransferStatus.Skipped => "Skipped",
        _ => "Unknown"
    };

    // ============================================
    // Parser Configuration
    // ============================================

    public async Task<DataResult<List<GenericFileFormatConfig>>> GetParserConfigsAsync(bool? activeOnly, SecurityContext context)
    {
        return await _repository.GetAllGenericFileFormatConfigsAsync(activeOnly);
    }

    public async Task<DataResult<GenericFileFormatConfig>> GetParserConfigAsync(string fileTypeCode, SecurityContext context)
    {
        var config = await _repository.GetGenericFileFormatConfigAsync(fileTypeCode);
        if (config == null)
        {
            return new DataResult<GenericFileFormatConfig>
            {
                StatusCode = 404,
                ErrorCode = "FileLoading.ParserConfigNotFound",
                ErrorMessage = $"Parser config not found: {fileTypeCode}"
            };
        }

        return new DataResult<GenericFileFormatConfig>
        {
            StatusCode = 200,
            Data = config
        };
    }

    public async Task<DataResult<GenericFileFormatConfig>> CreateParserConfigAsync(GenericParserConfigRequest request, SecurityContext context)
    {
        var existing = await _repository.GetGenericFileFormatConfigAsync(request.FileTypeCode);

        int newVersion = 1;
        if (existing != null)
        {
            return new DataResult<GenericFileFormatConfig> { StatusCode = 409, ErrorCode = "FileLoading.AlreadyExists", ErrorMessage = $"Parser config for '{request.FileTypeCode}' already exists (v{existing.ConfigVersion}). Use PATCH to update non-structural fields, or create a new custom table version to change the table structure." };
        }

        var (config, columnMappings) = MapParserRequest(request, context);
        config.ConfigVersion = newVersion;
        foreach (var m in columnMappings) m.ConfigVersion = newVersion;

        var result = await _repository.InsertGenericFileFormatConfigAsync(config);
        if (!result.IsSuccess)
            return new DataResult<GenericFileFormatConfig> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "FileLoading.DatabaseError", ErrorMessage = result.ErrorMessage };

        if (columnMappings.Count > 0)
            await _repository.InsertColumnMappingsBatchAsync(columnMappings);

        var saved = await _repository.GetGenericFileFormatConfigAsync(request.FileTypeCode);
        return new DataResult<GenericFileFormatConfig> { StatusCode = 201, Data = saved };
    }

    public async Task<DataResult<GenericFileFormatConfig>> UpdateParserConfigAsync(string fileTypeCode, GenericParserConfigRequest request, SecurityContext context)
    {
        var existing = await _repository.GetGenericFileFormatConfigAsync(fileTypeCode);
        if (existing == null)
            return new DataResult<GenericFileFormatConfig> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"Parser config for '{fileTypeCode}' not found" };

        // Smart freeze: only block DDL-affecting column mapping changes on frozen configs
        var isFrozen = await _repository.IsParserConfigFrozenAsync(fileTypeCode, existing.ConfigVersion);
        if (isFrozen)
        {
            var ddlChanges = DetectDdlChanges(existing.ColumnMappings, request.ColumnMappings);
            if (ddlChanges.Count > 0)
                return new DataResult<GenericFileFormatConfig>
                {
                    StatusCode = 409,
                    ErrorCode = "FileLoading.DdlChangesFrozen",
                    ErrorMessage = $"Parser config v{existing.ConfigVersion} is linked to a custom table. " +
                        $"The following DDL-affecting changes are not allowed: {string.Join("; ", ddlChanges)}. " +
                        $"Non-structural fields (regex, defaults, delimiters, etc.) can still be edited. " +
                        $"Create a new custom table version to change the table structure."
                };
        }

        request.FileTypeCode = fileTypeCode;
        var (config, columnMappings) = MapParserRequest(request, context);
        config.ConfigVersion = existing.ConfigVersion; // Preserve version

        var result = await _repository.UpdateGenericFileFormatConfigAsync(config);
        if (!result.IsSuccess)
            return new DataResult<GenericFileFormatConfig> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "FileLoading.DatabaseError", ErrorMessage = result.ErrorMessage };

        await _repository.DeleteColumnMappingsAsync(fileTypeCode, existing.ConfigVersion);
        foreach (var m in columnMappings) m.ConfigVersion = existing.ConfigVersion;
        if (columnMappings.Count > 0)
            await _repository.InsertColumnMappingsBatchAsync(columnMappings);

        var saved = await _repository.GetGenericFileFormatConfigAsync(fileTypeCode);
        return new DataResult<GenericFileFormatConfig> { StatusCode = 200, Data = saved };
    }

    private (GenericFileFormatConfig config, List<GenericColumnMapping> mappings) MapParserRequest(GenericParserConfigRequest request, SecurityContext context)
    {
        var config = new GenericFileFormatConfig
        {
            FileTypeCode = request.FileTypeCode,
            FileFormat = ParseFileFormatString(request.FileFormat),
            Delimiter = request.Delimiter,
            HasHeaderRow = request.HasHeaderRow,
            SkipRowsTop = request.SkipRowsTop,
            SkipRowsBottom = request.SkipRowsBottom,
            RowIdMode = ParseRowIdModeString(request.RowIdMode),
            RowIdColumn = request.RowIdColumn,
            HeaderIndicator = request.HeaderIndicator,
            TrailerIndicator = request.TrailerIndicator,
            DetailIndicator = request.DetailIndicator,
            SkipIndicator = request.SkipIndicator,
            TotalColumnIndex = request.TotalColumnIndex,
            TotalType = request.TotalType,
            SheetName = request.SheetName,
            SheetIndex = request.SheetIndex,
            DateFormat = request.DateFormat,
            CustomSpName = request.CustomSpName,
            Active = request.Active,
            CreatedBy = context.UserCode ?? "SYSTEM",
            UpdatedBy = context.UserCode ?? "SYSTEM"
        };

        var mappings = request.ColumnMappings.Select(m => new GenericColumnMapping
        {
            FileTypeCode = request.FileTypeCode,
            ColumnIndex = m.ColumnIndex,
            SourceColumnName = m.SourceColumnName,
            TargetField = m.TargetField,
            DataType = m.DataType,
            DateFormat = m.DateFormat,
            IsRequired = m.IsRequired,
            DefaultValue = m.DefaultValue,
            RegexPattern = m.RegexPattern,
            MaxLength = m.MaxLength
        }).ToList();

        return (config, mappings);
    }

    public async Task<DataResult<bool>> DeleteParserConfigAsync(string fileTypeCode, SecurityContext context)
    {
        var result = await _repository.DeleteGenericFileFormatConfigAsync(fileTypeCode);
        return new DataResult<bool>
        {
            StatusCode = result.IsSuccess ? 200 : 500,
            Data = result.IsSuccess,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    private static FileFormatType ParseFileFormatString(string value) => value.ToUpperInvariant() switch
    {
        "CSV" => FileFormatType.CSV,
        "XLSX" => FileFormatType.XLSX,
        "DELIMITED" => FileFormatType.Delimited,
        _ => FileFormatType.CSV
    };

    private static RowIdMode ParseRowIdModeString(string value) => value.ToUpperInvariant() switch
    {
        "POSITION" => RowIdMode.Position,
        "INDICATOR" => RowIdMode.Indicator,
        "PATTERN" => RowIdMode.Pattern,
        _ => RowIdMode.Position
    };

    /// <summary>
    /// Detect DDL-affecting changes between existing column mappings and a new request.
    /// DDL-affecting fields: target_field, data_type, max_length, is_required, and column additions/removals.
    /// Non-DDL fields (source_column_name, default_value, regex_pattern, date_format) are allowed to change freely.
    /// </summary>
    private static List<string> DetectDdlChanges(List<GenericColumnMapping> existing, List<GenericColumnMappingRequest> proposed)
    {
        var changes = new List<string>();

        // Build lookup of existing mappings by column index
        var existingByIndex = existing.ToDictionary(m => m.ColumnIndex);
        var proposedByIndex = proposed.ToDictionary(m => m.ColumnIndex);

        // Check for removed columns
        foreach (var ex in existing)
        {
            if (!proposedByIndex.ContainsKey(ex.ColumnIndex))
                changes.Add($"Column {ex.ColumnIndex} ({ex.TargetField}) removed");
        }

        // Check for added columns
        foreach (var pr in proposed)
        {
            if (!existingByIndex.ContainsKey(pr.ColumnIndex))
                changes.Add($"Column {pr.ColumnIndex} ({pr.TargetField}) added");
        }

        // Check DDL-affecting field changes on existing columns
        foreach (var pr in proposed)
        {
            if (!existingByIndex.TryGetValue(pr.ColumnIndex, out var ex))
                continue; // Already flagged as added

            if (!string.Equals(ex.TargetField, pr.TargetField, StringComparison.OrdinalIgnoreCase))
                changes.Add($"Column {pr.ColumnIndex}: target_field changed from '{ex.TargetField}' to '{pr.TargetField}'");

            if (!string.Equals(ex.DataType, pr.DataType, StringComparison.OrdinalIgnoreCase))
                changes.Add($"Column {pr.ColumnIndex}: data_type changed from '{ex.DataType}' to '{pr.DataType}'");

            if (ex.MaxLength != pr.MaxLength)
                changes.Add($"Column {pr.ColumnIndex}: max_length changed from {ex.MaxLength?.ToString() ?? "null"} to {pr.MaxLength?.ToString() ?? "null"}");

            if (ex.IsRequired != pr.IsRequired)
                changes.Add($"Column {pr.ColumnIndex}: is_required changed from {ex.IsRequired} to {pr.IsRequired}");
        }

        return changes;
    }

    private static string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLower();
        return ext switch
        {
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".xml" => "application/xml",
            ".json" => "application/json",
            ".gz" => "application/gzip",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }

    // ============================================
    // Lookup Tables: Vendors
    // ============================================

    public async Task<DataResult<List<VendorRecord>>> GetVendorsAsync(SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "VENDOR", "*");
        if (!auth.IsSuccess)
            return new DataResult<List<VendorRecord>> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetVendorsAsync();
    }

    public async Task<DataResult<VendorRecord>> GetVendorAsync(string networkId, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "VENDOR", networkId);
        if (!auth.IsSuccess)
            return new DataResult<VendorRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetVendorAsync(networkId);
    }

    public async Task<DataResult<VendorRecord>> CreateVendorAsync(VendorRecord record, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "VENDOR", record.NetworkId);
        if (!auth.IsSuccess)
            return new DataResult<VendorRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var existing = await _repository.GetVendorAsync(record.NetworkId);
        if (existing.IsSuccess && existing.Data != null)
            return new DataResult<VendorRecord> { StatusCode = 409, ErrorCode = "FileLoading.AlreadyExists", ErrorMessage = $"Vendor '{record.NetworkId}' already exists" };

        var result = await _repository.InsertVendorAsync(record);
        if (!result.IsSuccess)
            return new DataResult<VendorRecord> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "FileLoading.DatabaseError", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetVendorAsync(record.NetworkId);
        return new DataResult<VendorRecord> { StatusCode = 201, Data = saved.Data };
    }

    public async Task<DataResult<VendorRecord>> UpdateVendorAsync(string networkId, VendorRecord record, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "VENDOR", networkId);
        if (!auth.IsSuccess)
            return new DataResult<VendorRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var existing = await _repository.GetVendorAsync(networkId);
        if (!existing.IsSuccess || existing.Data == null)
            return new DataResult<VendorRecord> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"Vendor '{networkId}' not found" };

        record.NetworkId = networkId;
        var result = await _repository.UpdateVendorAsync(record);
        if (!result.IsSuccess)
            return new DataResult<VendorRecord> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "FileLoading.DatabaseError", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetVendorAsync(networkId);
        return new DataResult<VendorRecord> { StatusCode = 200, Data = saved.Data };
    }

    public async Task<DataResult<bool>> DeleteVendorAsync(string networkId, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "VENDOR", networkId);
        if (!auth.IsSuccess)
            return new DataResult<bool> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var result = await _repository.DeleteVendorAsync(networkId);
        return new DataResult<bool> { StatusCode = result.IsSuccess ? 200 : 500, Data = result.IsSuccess, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };
    }

    // ============================================
    // Lookup Tables: File Classes
    // ============================================

    public async Task<DataResult<List<FileClassRecord>>> GetFileClassesAsync(SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_CLASS", "*");
        if (!auth.IsSuccess)
            return new DataResult<List<FileClassRecord>> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetFileClassesAsync();
    }

    public async Task<DataResult<FileClassRecord>> GetFileClassAsync(string fileClassCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_CLASS", fileClassCode);
        if (!auth.IsSuccess)
            return new DataResult<FileClassRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetFileClassAsync(fileClassCode);
    }

    public async Task<DataResult<FileClassRecord>> CreateFileClassAsync(FileClassRecord record, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_CLASS", record.FileClassCode);
        if (!auth.IsSuccess)
            return new DataResult<FileClassRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var existing = await _repository.GetFileClassAsync(record.FileClassCode);
        if (existing.IsSuccess && existing.Data != null)
            return new DataResult<FileClassRecord> { StatusCode = 409, ErrorCode = "FileLoading.AlreadyExists", ErrorMessage = $"File class '{record.FileClassCode}' already exists" };

        var result = await _repository.InsertFileClassAsync(record);
        if (!result.IsSuccess)
            return new DataResult<FileClassRecord> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "FileLoading.DatabaseError", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetFileClassAsync(record.FileClassCode);
        return new DataResult<FileClassRecord> { StatusCode = 201, Data = saved.Data };
    }

    public async Task<DataResult<FileClassRecord>> UpdateFileClassAsync(string fileClassCode, FileClassRecord record, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_CLASS", fileClassCode);
        if (!auth.IsSuccess)
            return new DataResult<FileClassRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var existing = await _repository.GetFileClassAsync(fileClassCode);
        if (!existing.IsSuccess || existing.Data == null)
            return new DataResult<FileClassRecord> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"File class '{fileClassCode}' not found" };

        record.FileClassCode = fileClassCode;
        var result = await _repository.UpdateFileClassAsync(record);
        if (!result.IsSuccess)
            return new DataResult<FileClassRecord> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "FileLoading.DatabaseError", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetFileClassAsync(fileClassCode);
        return new DataResult<FileClassRecord> { StatusCode = 200, Data = saved.Data };
    }

    public async Task<DataResult<bool>> DeleteFileClassAsync(string fileClassCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_CLASS", fileClassCode);
        if (!auth.IsSuccess)
            return new DataResult<bool> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var result = await _repository.DeleteFileClassAsync(fileClassCode);
        return new DataResult<bool> { StatusCode = result.IsSuccess ? 200 : 500, Data = result.IsSuccess, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };
    }

    // ============================================
    // Lookup Tables: File Types
    // ============================================

    public async Task<DataResult<List<FileTypeRecord>>> GetFileTypesAsync(SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE", "*");
        if (!auth.IsSuccess)
            return new DataResult<List<FileTypeRecord>> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetFileTypeRecordsAsync();
    }

    public async Task<DataResult<FileTypeRecord>> GetFileTypeAsync(string fileTypeCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE", fileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<FileTypeRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetFileTypeRecordAsync(fileTypeCode);
    }

    public async Task<DataResult<FileTypeRecord>> CreateFileTypeAsync(FileTypeRecord record, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE", record.FileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<FileTypeRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        if (string.IsNullOrWhiteSpace(record.NetworkId))
            return new DataResult<FileTypeRecord> { StatusCode = 400, ErrorCode = "FileLoading.ValidationError", ErrorMessage = "Vendor is required" };

        var existing = await _repository.GetFileTypeRecordAsync(record.FileTypeCode);
        if (existing.IsSuccess && existing.Data != null)
            return new DataResult<FileTypeRecord> { StatusCode = 409, ErrorCode = "FileLoading.AlreadyExists", ErrorMessage = $"File type '{record.FileTypeCode}' already exists" };

        var result = await _repository.InsertFileTypeAsync(record);
        if (!result.IsSuccess)
            return new DataResult<FileTypeRecord> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "FileLoading.DatabaseError", ErrorMessage = result.ErrorMessage };

        // Auto-create folder config and local folders for new file types
        var folderSaveResult = await _transferService.SaveFolderConfigAsync(record.FileTypeCode, context);
        if (folderSaveResult.IsSuccess)
        {
            await _transferService.CreateFoldersAsync(record.FileTypeCode, context);
            _logger.LogInformation("Auto-created folder config and directories for new file type {FileTypeCode}", record.FileTypeCode);
        }
        else
        {
            _logger.LogWarning("Failed to auto-create folder config for file type {FileTypeCode}: {Error}", record.FileTypeCode, folderSaveResult.ErrorMessage);
        }

        var saved = await _repository.GetFileTypeRecordAsync(record.FileTypeCode);
        return new DataResult<FileTypeRecord> { StatusCode = 201, Data = saved.Data };
    }

    public async Task<DataResult<FileTypeRecord>> UpdateFileTypeAsync(string fileTypeCode, FileTypeRecord record, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE", fileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<FileTypeRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        if (string.IsNullOrWhiteSpace(record.NetworkId))
            return new DataResult<FileTypeRecord> { StatusCode = 400, ErrorCode = "FileLoading.ValidationError", ErrorMessage = "Vendor is required" };

        var existing = await _repository.GetFileTypeRecordAsync(fileTypeCode);
        if (!existing.IsSuccess || existing.Data == null)
            return new DataResult<FileTypeRecord> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"File type '{fileTypeCode}' not found" };

        record.FileTypeCode = fileTypeCode;
        var result = await _repository.UpdateFileTypeAsync(record);
        if (!result.IsSuccess)
            return new DataResult<FileTypeRecord> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "FileLoading.DatabaseError", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetFileTypeRecordAsync(fileTypeCode);
        return new DataResult<FileTypeRecord> { StatusCode = 200, Data = saved.Data };
    }

    public async Task<DataResult<bool>> DeleteFileTypeAsync(string fileTypeCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE", fileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<bool> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        // Check if local folders contain files and warn
        var rootBase = _configuration["LocalStorage:BasePath"] ?? "/var/www";
        var domain = context.Domain ?? "default";
        var basePath = $"{rootBase}/{domain}/files/{fileTypeCode}";
        var foldersWithFiles = new List<string>();

        try
        {
            if (Directory.Exists(basePath))
            {
                var folderNames = new[] { "transfer", "processing", "processed", "errors", "skipped", "example" };
                foreach (var folder in folderNames)
                {
                    var folderPath = $"{basePath}/{folder}";
                    if (Directory.Exists(folderPath) && Directory.EnumerateFileSystemEntries(folderPath).Any())
                    {
                        foldersWithFiles.Add(folder);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check folders for file type {FileTypeCode}", fileTypeCode);
        }

        var result = await _repository.DeleteFileTypeAsync(fileTypeCode);
        if (!result.IsSuccess)
            return new DataResult<bool> { StatusCode = 500, Data = false, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };

        // Return success with warning about non-empty folders
        if (foldersWithFiles.Count > 0)
        {
            return new DataResult<bool>
            {
                StatusCode = 200,
                Data = true,
                ErrorCode = "FileLoading.FoldersNotEmpty",
                ErrorMessage = $"File type deleted but the following folders still contain files and were not removed: {string.Join(", ", foldersWithFiles)}. Path: {basePath}"
            };
        }

        return new DataResult<bool> { StatusCode = 200, Data = true };
    }

    // ============================================
    // Lookup Tables: File Type NT
    // ============================================

    public async Task<DataResult<List<FileTypeNtRecord>>> GetFileTypeNtRecordsAsync(string? fileTypeCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE_NT", fileTypeCode ?? "*");
        if (!auth.IsSuccess)
            return new DataResult<List<FileTypeNtRecord>> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetFileTypeNtRecordsAsync(fileTypeCode);
    }

    public async Task<DataResult<FileTypeNtRecord>> GetFileTypeNtAsync(string fileTypeCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE_NT", fileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<FileTypeNtRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetFileTypeNtRecordAsync(fileTypeCode);
    }

    public async Task<DataResult<FileTypeNtRecord>> CreateFileTypeNtAsync(FileTypeNtRecord record, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE_NT", record.FileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<FileTypeNtRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        record.CreatedBy = context.UserCode ?? "SYSTEM";
        record.UpdatedBy = context.UserCode ?? "SYSTEM";

        var existing = await _repository.GetFileTypeNtRecordAsync(record.FileTypeCode);
        if (existing.IsSuccess && existing.Data != null)
            return new DataResult<FileTypeNtRecord> { StatusCode = 409, ErrorCode = "FileLoading.AlreadyExists", ErrorMessage = $"File type NT '{record.FileTypeCode}' already exists" };

        var result = await _repository.InsertFileTypeNtAsync(record);
        if (!result.IsSuccess)
            return new DataResult<FileTypeNtRecord> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "FileLoading.DatabaseError", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetFileTypeNtRecordAsync(record.FileTypeCode);
        return new DataResult<FileTypeNtRecord> { StatusCode = 201, Data = saved.Data };
    }

    public async Task<DataResult<FileTypeNtRecord>> UpdateFileTypeNtAsync(string fileTypeCode, FileTypeNtRecord record, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE_NT", fileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<FileTypeNtRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        record.CreatedBy = context.UserCode ?? "SYSTEM";
        record.UpdatedBy = context.UserCode ?? "SYSTEM";

        var existing = await _repository.GetFileTypeNtRecordAsync(fileTypeCode);
        if (!existing.IsSuccess || existing.Data == null)
            return new DataResult<FileTypeNtRecord> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"File type NT '{fileTypeCode}' not found" };

        record.FileTypeCode = fileTypeCode;
        var result = await _repository.UpdateFileTypeNtAsync(record);
        if (!result.IsSuccess)
            return new DataResult<FileTypeNtRecord> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "FileLoading.DatabaseError", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetFileTypeNtRecordAsync(fileTypeCode);
        return new DataResult<FileTypeNtRecord> { StatusCode = 200, Data = saved.Data };
    }

    public async Task<DataResult<bool>> DeleteFileTypeNtAsync(string fileTypeCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE_NT", fileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<bool> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var result = await _repository.DeleteFileTypeNtAsync(fileTypeCode);
        return new DataResult<bool> { StatusCode = result.IsSuccess ? 200 : 500, Data = result.IsSuccess, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };
    }

    // ============================================
    // Parser Config Versioning
    // ============================================

    public async Task<DataResult<List<GenericFileFormatConfig>>> GetParserConfigVersionsAsync(string fileTypeCode, SecurityContext context)
    {
        return await _repository.GetParserConfigVersionsAsync(fileTypeCode);
    }

    public async Task<DataResult<GenericFileFormatConfig>> GetParserConfigByVersionAsync(string fileTypeCode, int configVersion, SecurityContext context)
    {
        var config = await _repository.GetGenericFileFormatConfigAsync(fileTypeCode, configVersion);
        if (config == null)
            return new DataResult<GenericFileFormatConfig> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"Parser config v{configVersion} not found for '{fileTypeCode}'" };

        return new DataResult<GenericFileFormatConfig> { StatusCode = 200, Data = config };
    }

    // ============================================
    // Custom Table Management
    // ============================================

    public async Task<DataResult<CustomTableInfo>> GetCustomTableInfoAsync(string fileTypeCode, SecurityContext context)
    {
        var tablesResult = await _repository.GetCustomTablesAsync(fileTypeCode);
        if (!tablesResult.IsSuccess)
            return new DataResult<CustomTableInfo> { StatusCode = 500, ErrorCode = "FileLoading.DatabaseError", ErrorMessage = tablesResult.ErrorMessage };

        var allVersions = tablesResult.Data ?? new List<CustomTableMetadata>();
        if (allVersions.Count == 0)
            return new DataResult<CustomTableInfo> { StatusCode = 204 };

        var info = new CustomTableInfo
        {
            FileTypeCode = fileTypeCode.Trim(),
            ActiveVersion = allVersions.FirstOrDefault(v => v.Status == "ACTIVE"),
            AllVersions = allVersions
        };

        return new DataResult<CustomTableInfo> { StatusCode = 200, Data = info };
    }

    public async Task<DataResult<CustomTableProposal>> ProposeCustomTableAsync(string fileTypeCode, SecurityContext context)
    {
        // Get parser config with column mappings
        var config = await _repository.GetGenericFileFormatConfigAsync(fileTypeCode);
        if (config == null)
            return new DataResult<CustomTableProposal> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"No parser configuration found for '{fileTypeCode}'" };

        if (config.ColumnMappings.Count == 0)
            return new DataResult<CustomTableProposal> { StatusCode = 400, ErrorCode = "FileLoading.NoMappings", ErrorMessage = "Parser configuration has no column mappings defined" };

        // Determine version
        var existing = await _repository.GetActiveCustomTableAsync(fileTypeCode);
        int proposedVersion;

        if (existing == null)
        {
            // Check if there are any versions at all (all retired/dropped)
            var allResult = await _repository.GetCustomTablesAsync(fileTypeCode);
            var allVersions = allResult.Data ?? new List<CustomTableMetadata>();
            proposedVersion = allVersions.Count > 0 ? allVersions.Max(v => v.Version) + 1 : 1;
        }
        else
        {
            // New version — check records exist in current
            var recordCount = await _repository.GetLiveRecordCountAsync(existing.TableName);
            if (recordCount == 0)
                return new DataResult<CustomTableProposal> { StatusCode = 409, ErrorCode = "FileLoading.ActiveTableEmpty", ErrorMessage = "Active table has no records. Use the existing table or drop it first." };

            proposedVersion = existing.Version + 1;
        }

        var tableName = CustomTableHelper.DeriveTableName(fileTypeCode, proposedVersion);
        var ddl = CustomTableHelper.GenerateCreateTableDdl(tableName, config.ColumnMappings);
        var columns = CustomTableHelper.BuildColumnDefs(config.ColumnMappings);

        var proposal = new CustomTableProposal
        {
            FileTypeCode = fileTypeCode.Trim(),
            TableName = tableName,
            ProposedVersion = proposedVersion,
            Ddl = ddl,
            Columns = columns
        };

        return new DataResult<CustomTableProposal> { StatusCode = 200, Data = proposal };
    }

    public async Task<DataResult<CustomTableMetadata>> CreateCustomTableAsync(string fileTypeCode, SecurityContext context)
    {
        // Get parser config
        var config = await _repository.GetGenericFileFormatConfigAsync(fileTypeCode);
        if (config == null)
            return new DataResult<CustomTableMetadata> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"No parser configuration found for '{fileTypeCode}'" };

        if (config.ColumnMappings.Count == 0)
            return new DataResult<CustomTableMetadata> { StatusCode = 400, ErrorCode = "FileLoading.NoMappings", ErrorMessage = "Parser configuration has no column mappings defined" };

        // Check no active table exists
        var existing = await _repository.GetActiveCustomTableAsync(fileTypeCode);
        if (existing != null)
            return new DataResult<CustomTableMetadata> { StatusCode = 409, ErrorCode = "FileLoading.ActiveTableExists", ErrorMessage = $"Active custom table '{existing.TableName}' already exists. Use new-version endpoint instead." };

        // Determine version
        var allResult = await _repository.GetCustomTablesAsync(fileTypeCode);
        var allVersions = allResult.Data ?? new List<CustomTableMetadata>();
        var version = allVersions.Count > 0 ? allVersions.Max(v => v.Version) + 1 : 1;

        var tableName = CustomTableHelper.DeriveTableName(fileTypeCode, version);
        var ddl = CustomTableHelper.GenerateCreateTableDdl(tableName, config.ColumnMappings);
        var columns = CustomTableHelper.BuildColumnDefs(config.ColumnMappings);
        var columnDefJson = JsonSerializer.Serialize(columns);

        // Execute DDL
        var ddlResult = await _repository.ExecuteCreateTableAsync(ddl);
        if (!ddlResult.IsSuccess)
            return new DataResult<CustomTableMetadata> { StatusCode = 500, ErrorCode = "FileLoading.DdlFailed", ErrorMessage = $"Failed to create table: {ddlResult.ErrorMessage}" };

        // Insert metadata with config_version link
        var metadata = new CustomTableMetadata
        {
            FileTypeCode = fileTypeCode.Trim(),
            TableName = tableName,
            Version = version,
            Status = "ACTIVE",
            ColumnCount = config.ColumnMappings.Count,
            ColumnDefinition = columnDefJson,
            ConfigVersion = config.ConfigVersion,
            CreatedBy = context.UserCode
        };

        var insertResult = await _repository.InsertCustomTableMetadataAsync(metadata);
        metadata.CustomTableId = insertResult.Value;

        _logger.LogInformation("Created custom table {TableName} (v{Version}) for file type {FileTypeCode} with config v{ConfigVersion}",
            tableName, version, fileTypeCode, config.ConfigVersion);

        return new DataResult<CustomTableMetadata> { StatusCode = 201, Data = metadata };
    }

    public async Task<DataResult<CustomTableMetadata>> CreateCustomTableNewVersionAsync(string fileTypeCode, SecurityContext context)
    {
        // Check current active version exists and has records
        var existing = await _repository.GetActiveCustomTableAsync(fileTypeCode);
        if (existing == null)
            return new DataResult<CustomTableMetadata> { StatusCode = 404, ErrorCode = "FileLoading.NoActiveTable", ErrorMessage = "No active custom table exists. Use create endpoint instead." };

        var recordCount = await _repository.GetLiveRecordCountAsync(existing.TableName);
        if (recordCount == 0)
            return new DataResult<CustomTableMetadata> { StatusCode = 400, ErrorCode = "FileLoading.ActiveTableEmpty", ErrorMessage = "Current active table has no records. Cannot create new version until records exist." };

        // Get current column mappings (latest parser config version)
        var config = await _repository.GetGenericFileFormatConfigAsync(fileTypeCode);
        if (config == null || config.ColumnMappings.Count == 0)
            return new DataResult<CustomTableMetadata> { StatusCode = 400, ErrorCode = "FileLoading.NoMappings", ErrorMessage = "Parser configuration has no column mappings" };

        // Copy current parser config to a new version (freezes the current one)
        var newConfigVersion = config.ConfigVersion + 1;
        var copyResult = await _repository.CopyParserConfigVersionAsync(fileTypeCode, config.ConfigVersion, newConfigVersion);
        if (!copyResult.IsSuccess)
            return new DataResult<CustomTableMetadata> { StatusCode = 500, ErrorCode = "FileLoading.CopyConfigFailed", ErrorMessage = $"Failed to copy parser config: {copyResult.ErrorMessage}" };

        // Get the new config version's column mappings for DDL generation
        var newConfig = await _repository.GetGenericFileFormatConfigAsync(fileTypeCode, newConfigVersion);

        var newVersion = existing.Version + 1;
        var tableName = CustomTableHelper.DeriveTableName(fileTypeCode, newVersion);
        var ddl = CustomTableHelper.GenerateCreateTableDdl(tableName, newConfig!.ColumnMappings);
        var columns = CustomTableHelper.BuildColumnDefs(newConfig.ColumnMappings);
        var columnDefJson = JsonSerializer.Serialize(columns);

        // Execute DDL for new table
        var ddlResult = await _repository.ExecuteCreateTableAsync(ddl);
        if (!ddlResult.IsSuccess)
            return new DataResult<CustomTableMetadata> { StatusCode = 500, ErrorCode = "FileLoading.DdlFailed", ErrorMessage = $"Failed to create table: {ddlResult.ErrorMessage}" };

        // Retire current version
        await _repository.UpdateCustomTableStatusAsync(existing.CustomTableId, "RETIRED");

        // Insert new metadata with config_version link
        var metadata = new CustomTableMetadata
        {
            FileTypeCode = fileTypeCode.Trim(),
            TableName = tableName,
            Version = newVersion,
            Status = "ACTIVE",
            ColumnCount = newConfig.ColumnMappings.Count,
            ColumnDefinition = columnDefJson,
            ConfigVersion = newConfigVersion,
            CreatedBy = context.UserCode
        };

        var insertResult = await _repository.InsertCustomTableMetadataAsync(metadata);
        metadata.CustomTableId = insertResult.Value;

        _logger.LogInformation("Created new version v{Version} of custom table for {FileTypeCode} with config v{ConfigVersion}. Previous v{OldVersion} retired.",
            newVersion, fileTypeCode, newConfigVersion, existing.Version);

        return new DataResult<CustomTableMetadata> { StatusCode = 201, Data = metadata };
    }

    public async Task<DataResult<bool>> DropCustomTableVersionAsync(string fileTypeCode, int version, SecurityContext context)
    {
        var tableMetadata = await _repository.GetCustomTableByVersionAsync(fileTypeCode, version);
        if (tableMetadata == null)
            return new DataResult<bool> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"Custom table version {version} not found for '{fileTypeCode}'" };

        if (tableMetadata.Status == "DROPPED")
            return new DataResult<bool> { StatusCode = 400, ErrorCode = "FileLoading.AlreadyDropped", ErrorMessage = "Table has already been dropped" };

        // Check table is empty (skip if physical table doesn't exist — just update metadata)
        var recordCount = await _repository.GetLiveRecordCountAsync(tableMetadata.TableName);
        if (recordCount > 0)
            return new DataResult<bool> { StatusCode = 400, ErrorCode = "FileLoading.TableNotEmpty", ErrorMessage = $"Table has {recordCount} records. Cannot drop a non-empty table." };

        // Drop the physical table (skip if it doesn't exist)
        if (recordCount >= 0)
        {
            var dropResult = await _repository.DropTableAsync(tableMetadata.TableName);
            if (!dropResult.IsSuccess)
                return new DataResult<bool> { StatusCode = 500, ErrorCode = "FileLoading.DropFailed", ErrorMessage = $"Failed to drop table: {dropResult.ErrorMessage}" };
        }

        // Update metadata
        await _repository.UpdateCustomTableStatusAsync(tableMetadata.CustomTableId, "DROPPED", DateTime.Now);

        _logger.LogInformation("Dropped custom table {TableName} (v{Version}) for {FileTypeCode}",
            tableMetadata.TableName, version, fileTypeCode);

        return new DataResult<bool> { StatusCode = 200, Data = true };
    }

    public async Task<DataResult<int>> GetCustomTableRecordCountAsync(string fileTypeCode, int version, SecurityContext context)
    {
        var tableMetadata = await _repository.GetCustomTableByVersionAsync(fileTypeCode, version);
        if (tableMetadata == null)
            return new DataResult<int> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"Custom table version {version} not found for '{fileTypeCode}'" };

        if (tableMetadata.Status == "DROPPED")
            return new DataResult<int> { StatusCode = 400, ErrorCode = "FileLoading.TableDropped", ErrorMessage = "Table has been dropped" };

        var count = await _repository.GetLiveRecordCountAsync(tableMetadata.TableName);
        if (count < 0)
            return new DataResult<int> { StatusCode = 404, ErrorCode = "FileLoading.TableNotFound", ErrorMessage = $"Physical table '{tableMetadata.TableName}' does not exist in the database" };

        return new DataResult<int> { StatusCode = 200, Data = count };
    }

    public async Task<DataResult<TestLoadResult>> TestLoadCustomTableAsync(string fileTypeCode, Stream fileStream, string fileName, SecurityContext context)
    {
        // Check custom table exists
        var customTable = await _repository.GetActiveCustomTableAsync(fileTypeCode);
        if (customTable == null)
            return new DataResult<TestLoadResult> { StatusCode = 404, ErrorCode = "FileLoading.NoActiveTable", ErrorMessage = "No active custom table exists for this file type" };

        // Look up the file_type_nt record to get the correct NtCustNum
        var fileTypeNt = await _repository.GetFileTypeNtRecordAsync(fileTypeCode);
        var ntCustNum = fileTypeNt.Data?.NtCustNum;

        // Save uploaded file to temp location
        var tempPath = Path.Combine(Path.GetTempPath(), $"testload_{Guid.NewGuid():N}_{fileName}");
        try
        {
            using (var fs = new FileStream(tempPath, FileMode.Create))
            {
                await fileStream.CopyToAsync(fs);
            }

            // Use the existing file loader service to process the file
            var loadResult = await _fileLoaderService.LoadFileAsync(
                new LoadFileRequest
                {
                    FileName = tempPath,
                    FileType = fileTypeCode,
                    NtCustNum = ntCustNum,
                    DisplayFileName = fileName
                },
                context);

            var testResult = new TestLoadResult
            {
                NtFileNum = loadResult.Data?.NtFileNum ?? 0,
                RecordsLoaded = loadResult.Data?.RecordsLoaded ?? 0,
                RecordsFailed = loadResult.Data?.RecordsFailed ?? 0
            };

            // If the load failed, include the error message
            if (!loadResult.IsSuccess && !string.IsNullOrEmpty(loadResult.ErrorMessage))
                testResult.Errors.Add(loadResult.ErrorMessage);

            return new DataResult<TestLoadResult> { StatusCode = 201, Data = testResult };
        }
        finally
        {
            // Clean up temp file
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public async Task<DataResult<bool>> DeleteTestLoadAsync(string fileTypeCode, int ntFileNum, SecurityContext context)
    {
        // Verify this is a test file
        var fileTypeCode2 = await _repository.GetFileTypeCodeForFileAsync(ntFileNum);
        if (fileTypeCode2 == null)
            return new DataResult<bool> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = "File not found" };

        // Delete records from custom table if one exists
        var customTable = await _repository.GetActiveCustomTableAsync(fileTypeCode);
        if (customTable != null)
        {
            await _repository.DeleteCustomTableRecordsAsync(customTable.TableName, ntFileNum);
            _logger.LogInformation("Deleted test load {NtFileNum} from custom table {TableName}", ntFileNum, customTable.TableName);
        }

        // Also delete from generic detail and other detail tables
        await _repository.UnloadFileRecordsAsync(ntFileNum, context);

        // Delete the nt_file record itself
        await _repository.DeleteNtFileAsync(ntFileNum);

        _logger.LogInformation("Deleted test load file record {NtFileNum}", ntFileNum);

        return new DataResult<bool> { StatusCode = 200, Data = true };
    }

    // ============================================
    // Charge Mappings (ntfl_chg_map)
    // ============================================

    public async Task<DataResult<List<NtflChgMapRecord>>> GetChargeMapsAsync(string fileTypeCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "CHARGE_MAP", fileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<List<NtflChgMapRecord>> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetChargeMapsAsync(fileTypeCode);
    }

    public async Task<DataResult<NtflChgMapRecord>> GetChargeMapAsync(int id, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "CHARGE_MAP", "*");
        if (!auth.IsSuccess)
            return new DataResult<NtflChgMapRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetChargeMapAsync(id);
    }

    public async Task<DataResult<NtflChgMapRecord>> CreateChargeMapAsync(NtflChgMapRequest request, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "CHARGE_MAP", request.FileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<NtflChgMapRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var record = new NtflChgMapRecord
        {
            FileTypeCode = request.FileTypeCode,
            FileChgDesc = request.FileChgDesc,
            SeqNo = request.SeqNo,
            ChgCode = request.ChgCode,
            AutoExclude = request.AutoExclude,
            UseNetPrice = request.UseNetPrice,
            NetPrcProrated = request.NetPrcProrated,
            UpliftPerc = request.UpliftPerc,
            UpliftAmt = request.UpliftAmt,
            UseNetDesc = request.UseNetDesc,
            UpdatedBy = context.UserCode ?? "SYSTEM"
        };

        var result = await _repository.InsertChargeMapAsync(record);
        if (!result.IsSuccess)
            return new DataResult<NtflChgMapRecord> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "FileLoading.DatabaseError", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetChargeMapAsync(result.Value);
        return new DataResult<NtflChgMapRecord> { StatusCode = 201, Data = saved.Data };
    }

    public async Task<DataResult<NtflChgMapRecord>> UpdateChargeMapAsync(int id, NtflChgMapRequest request, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "CHARGE_MAP", request.FileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<NtflChgMapRecord> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var existing = await _repository.GetChargeMapAsync(id);
        if (!existing.IsSuccess || existing.Data == null)
            return new DataResult<NtflChgMapRecord> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"Charge mapping {id} not found" };

        var record = new NtflChgMapRecord
        {
            Id = id,
            FileTypeCode = request.FileTypeCode,
            FileChgDesc = request.FileChgDesc,
            SeqNo = request.SeqNo,
            ChgCode = request.ChgCode,
            AutoExclude = request.AutoExclude,
            UseNetPrice = request.UseNetPrice,
            NetPrcProrated = request.NetPrcProrated,
            UpliftPerc = request.UpliftPerc,
            UpliftAmt = request.UpliftAmt,
            UseNetDesc = request.UseNetDesc,
            UpdatedBy = context.UserCode ?? "SYSTEM"
        };

        var result = await _repository.UpdateChargeMapAsync(record);
        if (!result.IsSuccess)
            return new DataResult<NtflChgMapRecord> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "FileLoading.DatabaseError", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetChargeMapAsync(id);
        return new DataResult<NtflChgMapRecord> { StatusCode = 200, Data = saved.Data };
    }

    public async Task<DataResult<bool>> DeleteChargeMapAsync(int id, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "CHARGE_MAP", "*");
        if (!auth.IsSuccess)
            return new DataResult<bool> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var result = await _repository.DeleteChargeMapAsync(id);
        return new DataResult<bool> { StatusCode = result.IsSuccess ? 200 : 500, Data = result.IsSuccess, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };
    }

    // ============================================
    // Configuration Readiness
    // ============================================

    public async Task<DataResult<FileTypeReadinessResponse>> GetReadinessAsync(string fileTypeCode, SecurityContext context)
    {
        // Load file type record first — if not found, 404
        var ftResult = await _repository.GetFileTypeRecordAsync(fileTypeCode);
        if (!ftResult.IsSuccess || ftResult.Data == null)
            return new DataResult<FileTypeReadinessResponse> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"File type '{fileTypeCode}' not found" };

        var ft = ftResult.Data;
        var fileClassCode = ft.FileClassCode?.Trim() ?? "";
        var isChgClass = fileClassCode.Equals("CHG", StringComparison.OrdinalIgnoreCase);

        // Run all lookups in parallel
        var ntTask = _repository.GetFileTypeNtRecordAsync(fileTypeCode);
        var parserTask = _repository.GetGenericFileFormatConfigAsync(fileTypeCode);
        var customTableTask = _repository.GetActiveCustomTableAsync(fileTypeCode);
        var chargeMapsTask = _repository.GetChargeMapsAsync(fileTypeCode);
        var exampleFilesTask = _repository.GetExampleFilesByTypeAsync(fileTypeCode);
        var analysisTask = _repository.GetAnalysisResultsAsync(fileTypeCode);
        var promptTask = _repository.GetCurrentFileTypePromptAsync(fileTypeCode);

        await Task.WhenAll(ntTask, parserTask, customTableTask, chargeMapsTask, exampleFilesTask, analysisTask, promptTask);

        var ntResult = await ntTask;
        var parserResult = await parserTask;
        var customTableResult = await customTableTask;
        var chargeMapsResult = await chargeMapsTask;
        var exampleFilesResult = await exampleFilesTask;
        var analysisResult = await analysisTask;
        var promptResult = await promptTask;

        var tiers = new List<ReadinessTier>();
        var missing = new List<string>();
        var completed = new List<string>();

        // Tier 1: Core Identity
        var tier1Checks = new List<ReadinessCheck>();
        tier1Checks.Add(new ReadinessCheck { Item = "File Type Record", IsConfigured = true, IsRequired = true, Detail = $"{ft.FileTypeCode?.Trim()} - {ft.FileType?.Trim()}" });
        completed.Add("File type record exists");

        var hasNt = ntResult.IsSuccess && ntResult.Data != null;
        tier1Checks.Add(new ReadinessCheck { Item = "File Type NT Record", IsConfigured = hasNt, IsRequired = true, Detail = hasNt ? "Configured" : "No file_type_nt record" });
        if (hasNt) completed.Add("File type NT record configured");
        else missing.Add("Configure file_type_nt record (Tier 1)");

        tiers.Add(new ReadinessTier { Tier = 1, Name = "Core Identity", Status = tier1Checks.All(c => !c.IsRequired || c.IsConfigured) ? "READY" : "PARTIAL", Checks = tier1Checks });

        // Tier 2: Parser Configuration
        var tier2Checks = new List<ReadinessCheck>();
        var hasParser = parserResult != null && parserResult.ColumnMappings?.Count > 0;
        var parserDetail = hasParser ? $"{parserResult!.FileFormat}, {parserResult.ColumnMappings!.Count} column mappings, {(parserResult.Active ? "active" : "inactive")}" : "No parser configuration";
        tier2Checks.Add(new ReadinessCheck { Item = "Parser Config", IsConfigured = hasParser, IsRequired = true, Detail = parserDetail });
        if (hasParser) completed.Add($"Parser configuration active with {parserResult!.ColumnMappings!.Count} column mappings");
        else missing.Add("Set up parser configuration with column mappings (Tier 2)");

        var hasCustomTable = customTableResult != null;
        var ctDetail = hasCustomTable ? $"{customTableResult!.TableName} (ACTIVE, {customTableResult.ColumnCount} columns)" : "Using generic detail table";
        tier2Checks.Add(new ReadinessCheck { Item = "Custom Staging Table", IsConfigured = hasCustomTable, IsRequired = false, Detail = ctDetail });
        if (hasCustomTable) completed.Add($"Custom staging table {customTableResult!.TableName} active");

        tiers.Add(new ReadinessTier { Tier = 2, Name = "Parser Configuration", Status = tier2Checks.All(c => !c.IsRequired || c.IsConfigured) ? "READY" : "NOT_CONFIGURED", Checks = tier2Checks });

        // Tier 3: Transfer & Folders — simplified (folder config always has a default)
        var tier3Checks = new List<ReadinessCheck>();
        tier3Checks.Add(new ReadinessCheck { Item = "Folder Configuration", IsConfigured = true, IsRequired = true, Detail = "Available (default or file-type specific)" });
        completed.Add("Folder configuration available");
        tiers.Add(new ReadinessTier { Tier = 3, Name = "Transfer & Folders", Status = "READY", Checks = tier3Checks });

        // Tier 4: Charge Mappings
        var tier4Checks = new List<ReadinessCheck>();
        var chargeMapCount = chargeMapsResult.IsSuccess && chargeMapsResult.Data != null ? chargeMapsResult.Data.Count : 0;
        var hasMaps = chargeMapCount > 0;
        var mapDetail = isChgClass
            ? (hasMaps ? $"{chargeMapCount} charge mapping(s) configured" : "No charge mappings configured (required for CHG file class)")
            : "Not applicable for this file class";
        tier4Checks.Add(new ReadinessCheck { Item = "Charge Maps", IsConfigured = hasMaps, IsRequired = isChgClass, Detail = mapDetail });
        if (isChgClass && hasMaps) completed.Add($"{chargeMapCount} charge mapping(s) configured");
        else if (isChgClass && !hasMaps) missing.Add("Add charge mappings to map vendor charges to Selcomm charge codes (Tier 4)");

        var tier4Status = !isChgClass ? "NOT_APPLICABLE" : (hasMaps ? "READY" : "NOT_CONFIGURED");
        tiers.Add(new ReadinessTier { Tier = 4, Name = "Charge Mappings", Status = tier4Status, Checks = tier4Checks });

        // Tier 5: AI Configuration (optional)
        var tier5Checks = new List<ReadinessCheck>();
        var exampleCount = exampleFilesResult.IsSuccess && exampleFilesResult.Data != null ? exampleFilesResult.Data.Count : 0;
        tier5Checks.Add(new ReadinessCheck { Item = "Example Files", IsConfigured = exampleCount > 0, IsRequired = false, Detail = exampleCount > 0 ? $"{exampleCount} example file(s) uploaded" : "No example files" });
        if (exampleCount > 0) completed.Add($"{exampleCount} example file(s) uploaded");

        var analysisCount = analysisResult.IsSuccess && analysisResult.Data != null ? analysisResult.Data.Count : 0;
        var latestAnalysis = analysisResult.Data?.FirstOrDefault();
        tier5Checks.Add(new ReadinessCheck { Item = "Analysis Result", IsConfigured = analysisCount > 0, IsRequired = false, Detail = latestAnalysis != null ? $"{latestAnalysis.IngestionReadiness} readiness ({latestAnalysis.CreatedTm:yyyy-MM-dd})" : "No analysis results" });
        if (analysisCount > 0) completed.Add($"AI analysis completed ({latestAnalysis!.IngestionReadiness} readiness)");

        var hasPrompt = promptResult.IsSuccess && promptResult.Data != null;
        tier5Checks.Add(new ReadinessCheck { Item = "Active Prompt", IsConfigured = hasPrompt, IsRequired = false, Detail = hasPrompt ? $"v{promptResult.Data!.Version} ({promptResult.Data.Source})" : "No active file-type prompt" });
        if (hasPrompt) completed.Add("Active file-type prompt configured");

        var tier5Configured = tier5Checks.Count(c => c.IsConfigured);
        tiers.Add(new ReadinessTier { Tier = 5, Name = "AI Configuration", Status = tier5Configured == tier5Checks.Count ? "READY" : tier5Configured > 0 ? "PARTIAL" : "NOT_CONFIGURED", Checks = tier5Checks });

        // Calculate score from required items only
        var requiredChecks = tiers.SelectMany(t => t.Checks).Where(c => c.IsRequired).ToList();
        var configuredRequired = requiredChecks.Count(c => c.IsConfigured);
        var totalRequired = requiredChecks.Count;
        var score = totalRequired > 0 ? (int)Math.Round((double)configuredRequired / totalRequired * 100) : 0;
        var level = score >= 100 ? "READY" : score > 0 ? "PARTIAL" : "NOT_CONFIGURED";

        return new DataResult<FileTypeReadinessResponse>
        {
            StatusCode = 200,
            Data = new FileTypeReadinessResponse
            {
                FileTypeCode = fileTypeCode,
                FileType = ft.FileType?.Trim(),
                FileClassCode = fileClassCode,
                ReadinessLevel = level,
                ReadinessScore = score,
                Tiers = tiers,
                MissingSteps = missing,
                CompletedSteps = completed
            }
        };
    }

    public async Task<DataResult<ChargeMapMatch?>> ResolveChargeMapAsync(string fileTypeCode, string chargeDescription, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "CHARGE_MAP", fileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<ChargeMapMatch?> { StatusCode = 403, ErrorCode = "FileLoading.Unauthorised", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var mapsResult = await _repository.GetChargeMapsAsync(fileTypeCode);
        if (!mapsResult.IsSuccess || mapsResult.Data == null || mapsResult.Data.Count == 0)
            return new DataResult<ChargeMapMatch?> { StatusCode = 200, Data = null };

        // Match in seq_no order (already sorted by repository)
        foreach (var map in mapsResult.Data)
        {
            if (MatchesLikePattern(chargeDescription, map.FileChgDesc))
            {
                return new DataResult<ChargeMapMatch?>
                {
                    StatusCode = 200,
                    Data = new ChargeMapMatch
                    {
                        Id = map.Id,
                        FileChgDesc = map.FileChgDesc,
                        ChgCode = map.ChgCode,
                        AutoExclude = map.AutoExclude,
                        UseNetPrice = map.UseNetPrice,
                        NetPrcProrated = map.NetPrcProrated,
                        UpliftPerc = map.UpliftPerc,
                        UpliftAmt = map.UpliftAmt,
                        UseNetDesc = map.UseNetDesc
                    }
                };
            }
        }

        return new DataResult<ChargeMapMatch?> { StatusCode = 200, Data = null };
    }

    /// <summary>
    /// Matches a value against a SQL LIKE pattern (supports % and _ wildcards).
    /// Case-insensitive.
    /// </summary>
    private static bool MatchesLikePattern(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return string.IsNullOrEmpty(input);
        if (string.IsNullOrEmpty(input)) return pattern == "%";

        // Convert SQL LIKE pattern to regex
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("%", ".*")
            .Replace("_", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(input, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
