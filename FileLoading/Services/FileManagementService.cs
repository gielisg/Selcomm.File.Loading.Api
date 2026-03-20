using System.Text.Json;
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
    private readonly CompressionHelper _compressionHelper;
    private readonly ILogger<FileManagementService> _logger;

    public FileManagementService(
        IFileLoaderRepository repository,
        IFileLoaderService fileLoaderService,
        IFileTransferService transferService,
        CompressionHelper compressionHelper,
        ILogger<FileManagementService> logger)
    {
        _repository = repository;
        _fileLoaderService = fileLoaderService;
        _transferService = transferService;
        _compressionHelper = compressionHelper;
        _logger = logger;
    }

    // ============================================
    // User File Operations
    // ============================================

    public async Task<DataResult<FileLoadResponse>> ProcessFileAsync(int transferId, SecurityContext context)
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

        // Move to processing folder if not already there
        if (transfer.CurrentFolder?.ToUpper() != "PROCESSING")
        {
            var moveResult = await _transferService.TransferToProcessingAsync(transferId, context);
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
            Domain = context.Domain ?? "default"
        }, context);

        try
        {
            // Determine file type from source config or filename
            var sourceResult = await _repository.GetTransferSourceAsync(transfer.SourceId ?? 0);
            var fileType = sourceResult.Data?.FileTypeCode ?? DetermineFileType(transfer.FileName);

            if (string.IsNullOrEmpty(fileType))
            {
                var errorMsg = $"Cannot determine file type for {transfer.FileName}";
                await _repository.UpdateTransferStatusAsync(transferId, TransferStatus.Error, errorMsg, DateTime.Now);
                await MoveToErrorsFolder(transfer, context);
                return new DataResult<FileLoadResponse>
                {
                    StatusCode = 400,
                    ErrorCode = "UNKNOWN_FILE_TYPE",
                    ErrorMessage = errorMsg
                };
            }

            // Call file loader service
            var loadRequest = new LoadFileRequest
            {
                FileName = transfer.DestinationPath!,
                FileType = fileType
            };

            var loadResult = await _fileLoaderService.LoadFileAsync(loadRequest, context);

            if (loadResult.IsSuccess && loadResult.Data != null)
            {
                // Update transfer with nt_file_num
                await _repository.UpdateTransferNtFileNumAsync(transferId, loadResult.Data.NtFileNum);
                await _repository.UpdateTransferStatusAsync(transferId, TransferStatus.Processed, null, DateTime.Now);

                // Move to processed folder
                await _transferService.MoveToFolderAsync(transferId, "Processed", true, context);

                // Log success
                await LogActivityAsync(new FileActivityLog
                {
                    NtFileNum = loadResult.Data.NtFileNum,
                    TransferId = transferId,
                    FileName = transfer.FileName,
                    ActivityType = FileActivityType.ProcessingCompleted,
                    Description = $"Successfully processed file {transfer.FileName}. Loaded {loadResult.Data.RecordsLoaded} records.",
                    UserId = context.UserCode ?? "SYSTEM",
                    Domain = context.Domain ?? "default"
                }, context);

                return loadResult;
            }
            else
            {
                // Processing failed
                var errorMsg = loadResult.ErrorMessage ?? "Processing failed";
                await _repository.UpdateTransferStatusAsync(transferId, TransferStatus.Error, errorMsg, DateTime.Now);
                await MoveToErrorsFolder(transfer, context);

                // Log failure
                await LogActivityAsync(new FileActivityLog
                {
                    TransferId = transferId,
                    FileName = transfer.FileName,
                    ActivityType = FileActivityType.ProcessingFailed,
                    Description = $"Failed to process file {transfer.FileName}: {errorMsg}",
                    UserId = context.UserCode ?? "SYSTEM",
                    Domain = context.Domain ?? "default"
                }, context);

                return loadResult;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file {FileName}", transfer.FileName);
            await _repository.UpdateTransferStatusAsync(transferId, TransferStatus.Error, ex.Message, DateTime.Now);
            await MoveToErrorsFolder(transfer, context);

            return new DataResult<FileLoadResponse>
            {
                StatusCode = 500,
                ErrorCode = "PROCESSING_ERROR",
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

            // Unload records
            var result = await _repository.UnloadFileRecordsAsync(ntFileNum, context);

            // Log activity
            await LogActivityAsync(new FileActivityLog
            {
                NtFileNum = ntFileNum,
                FileName = fileStatus.Data?.FileName ?? "Unknown",
                ActivityType = FileActivityType.FileUnloaded,
                Description = $"Unloaded file {fileStatus.Data?.FileName}. Deleted {result.RowsAffected} records.",
                UserId = context.UserCode ?? "SYSTEM",
                Domain = context.Domain ?? "default"
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
                ErrorCode = "UNLOAD_ERROR",
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
            Domain = context.Domain ?? "default"
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
            Domain = context.Domain ?? "default"
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
                Domain = context.Domain ?? "default"
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
                ErrorCode = "DELETE_ERROR",
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
                ErrorCode = "FILE_NOT_FOUND",
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
                Domain = context.Domain ?? "default"
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
                ErrorCode = "DOWNLOAD_ERROR",
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
        string? domain, string? fileTypeCode, SecurityContext context)
    {
        var dashboardResult = await _repository.GetDashboardSummaryAsync(domain, fileTypeCode);

        if (dashboardResult.IsSuccess && dashboardResult.Data != null)
        {
            // Get source statuses
            var statusesResult = await _repository.GetSourceStatusesAsync(domain);
            if (statusesResult.IsSuccess && statusesResult.Data != null)
            {
                dashboardResult.Data.SourceStatuses = statusesResult.Data;
            }
        }

        return dashboardResult;
    }

    public async Task<DataResult<FileListWithStatusResponse>> ListFilesAsync(
        FileListFilter filter, SecurityContext context)
    {
        var result = await _repository.ListFilesWithStatusAsync(filter);

        return new DataResult<FileListWithStatusResponse>
        {
            StatusCode = result.StatusCode,
            Data = new FileListWithStatusResponse
            {
                Items = result.Data ?? new List<FileWithStatus>(),
                TotalCount = result.Data?.Count ?? 0
            },
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
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
                Domain = sourceResult.Data?.Domain ?? "unknown",
                CurrentFolder = transfer.CurrentFolder ?? "",
                Status = transfer.Status,
                StatusDescription = GetStatusDescription(transfer.Status),
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

    public async Task<DataResult<List<FileActivityLog>>> GetActivityLogAsync(
        int? ntFileNum, int? transferId, int maxRecords, SecurityContext context)
    {
        return await _repository.GetActivityLogsAsync(ntFileNum, transferId, maxRecords);
    }

    public async Task LogActivityAsync(FileActivityLog activity, SecurityContext context)
    {
        activity.ActivityAt = DateTime.Now;
        if (string.IsNullOrEmpty(activity.UserId))
        {
            activity.UserId = context.UserCode ?? "SYSTEM";
        }
        if (string.IsNullOrEmpty(activity.Domain))
        {
            activity.Domain = context.Domain ?? "default";
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

    public async Task<DataResult<List<FileWithStatus>>> GetFilesWithErrorsAsync(
        string? domain, string? fileTypeCode, int maxRecords, SecurityContext context)
    {
        var filter = new FileListFilter
        {
            Domain = domain,
            FileTypeCode = fileTypeCode,
            Status = TransferStatus.Error,
            MaxRecords = maxRecords
        };

        return await _repository.ListFilesWithStatusAsync(filter);
    }

    public async Task<DataResult<List<FileWithStatus>>> GetSkippedFilesAsync(
        string? domain, string? fileTypeCode, int maxRecords, SecurityContext context)
    {
        var filter = new FileListFilter
        {
            Domain = domain,
            FileTypeCode = fileTypeCode,
            Status = TransferStatus.Skipped,
            MaxRecords = maxRecords
        };

        return await _repository.ListFilesWithStatusAsync(filter);
    }

    // ============================================
    // Helper Methods
    // ============================================

    private async Task MoveToErrorsFolder(FileTransferRecord transfer, SecurityContext context)
    {
        try
        {
            await _transferService.MoveToFolderAsync(transfer.TransferId, "Errors", false, context);
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

    private static string GetStatusDescription(TransferStatus status) => status switch
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
                ErrorCode = "NOT_FOUND",
                ErrorMessage = $"Parser config not found: {fileTypeCode}"
            };
        }

        return new DataResult<GenericFileFormatConfig>
        {
            StatusCode = 200,
            Data = config
        };
    }

    public async Task<DataResult<GenericFileFormatConfig>> SaveParserConfigAsync(GenericParserConfigRequest request, SecurityContext context)
    {
        // Map request to domain model
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
            Active = request.Active
        };

        var columnMappings = request.ColumnMappings.Select(m => new GenericColumnMapping
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

        // Check if exists
        var existing = await _repository.GetGenericFileFormatConfigAsync(request.FileTypeCode);
        RawCommandResult result;

        if (existing != null)
        {
            result = await _repository.UpdateGenericFileFormatConfigAsync(config);
            if (result.IsSuccess)
            {
                await _repository.DeleteColumnMappingsAsync(request.FileTypeCode);
                if (columnMappings.Count > 0)
                    await _repository.InsertColumnMappingsBatchAsync(columnMappings);
            }
        }
        else
        {
            result = await _repository.InsertGenericFileFormatConfigAsync(config);
            if (result.IsSuccess && columnMappings.Count > 0)
            {
                await _repository.InsertColumnMappingsBatchAsync(columnMappings);
            }
        }

        if (!result.IsSuccess)
        {
            return new DataResult<GenericFileFormatConfig>
            {
                StatusCode = 500,
                ErrorCode = result.ErrorCode ?? "DATABASE_ERROR",
                ErrorMessage = result.ErrorMessage
            };
        }

        // Re-read from DB to confirm
        var saved = await _repository.GetGenericFileFormatConfigAsync(request.FileTypeCode);
        return new DataResult<GenericFileFormatConfig>
        {
            StatusCode = 200,
            Data = saved
        };
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
            return new DataResult<List<VendorRecord>> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetVendorsAsync();
    }

    public async Task<DataResult<VendorRecord>> GetVendorAsync(string networkId, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "VENDOR", networkId);
        if (!auth.IsSuccess)
            return new DataResult<VendorRecord> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetVendorAsync(networkId);
    }

    public async Task<DataResult<VendorRecord>> SaveVendorAsync(VendorRecord record, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "VENDOR", record.NetworkId);
        if (!auth.IsSuccess)
            return new DataResult<VendorRecord> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var existing = await _repository.GetVendorAsync(record.NetworkId);
        RawCommandResult result;

        if (existing.IsSuccess && existing.Data != null)
            result = await _repository.UpdateVendorAsync(record);
        else
            result = await _repository.InsertVendorAsync(record);

        if (!result.IsSuccess)
            return new DataResult<VendorRecord> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "DATABASE_ERROR", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetVendorAsync(record.NetworkId);
        return new DataResult<VendorRecord> { StatusCode = 200, Data = saved.Data };
    }

    public async Task<DataResult<bool>> DeleteVendorAsync(string networkId, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "VENDOR", networkId);
        if (!auth.IsSuccess)
            return new DataResult<bool> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

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
            return new DataResult<List<FileClassRecord>> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetFileClassesAsync();
    }

    public async Task<DataResult<FileClassRecord>> GetFileClassAsync(string fileClassCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_CLASS", fileClassCode);
        if (!auth.IsSuccess)
            return new DataResult<FileClassRecord> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetFileClassAsync(fileClassCode);
    }

    public async Task<DataResult<FileClassRecord>> SaveFileClassAsync(FileClassRecord record, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_CLASS", record.FileClassCode);
        if (!auth.IsSuccess)
            return new DataResult<FileClassRecord> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var existing = await _repository.GetFileClassAsync(record.FileClassCode);
        RawCommandResult result;

        if (existing.IsSuccess && existing.Data != null)
            result = await _repository.UpdateFileClassAsync(record);
        else
            result = await _repository.InsertFileClassAsync(record);

        if (!result.IsSuccess)
            return new DataResult<FileClassRecord> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "DATABASE_ERROR", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetFileClassAsync(record.FileClassCode);
        return new DataResult<FileClassRecord> { StatusCode = 200, Data = saved.Data };
    }

    public async Task<DataResult<bool>> DeleteFileClassAsync(string fileClassCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_CLASS", fileClassCode);
        if (!auth.IsSuccess)
            return new DataResult<bool> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

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
            return new DataResult<List<FileTypeRecord>> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetFileTypeRecordsAsync();
    }

    public async Task<DataResult<FileTypeRecord>> GetFileTypeAsync(string fileTypeCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE", fileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<FileTypeRecord> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetFileTypeRecordAsync(fileTypeCode);
    }

    public async Task<DataResult<FileTypeRecord>> SaveFileTypeAsync(FileTypeRecord record, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE", record.FileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<FileTypeRecord> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var existing = await _repository.GetFileTypeRecordAsync(record.FileTypeCode);
        RawCommandResult result;

        if (existing.IsSuccess && existing.Data != null)
            result = await _repository.UpdateFileTypeAsync(record);
        else
            result = await _repository.InsertFileTypeAsync(record);

        if (!result.IsSuccess)
            return new DataResult<FileTypeRecord> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "DATABASE_ERROR", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetFileTypeRecordAsync(record.FileTypeCode);
        return new DataResult<FileTypeRecord> { StatusCode = 200, Data = saved.Data };
    }

    public async Task<DataResult<bool>> DeleteFileTypeAsync(string fileTypeCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE", fileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<bool> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var result = await _repository.DeleteFileTypeAsync(fileTypeCode);
        return new DataResult<bool> { StatusCode = result.IsSuccess ? 200 : 500, Data = result.IsSuccess, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };
    }

    // ============================================
    // Lookup Tables: File Type NT
    // ============================================

    public async Task<DataResult<List<FileTypeNtRecord>>> GetFileTypeNtRecordsAsync(string? fileTypeCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE_NT", fileTypeCode ?? "*");
        if (!auth.IsSuccess)
            return new DataResult<List<FileTypeNtRecord>> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetFileTypeNtRecordsAsync(fileTypeCode);
    }

    public async Task<DataResult<FileTypeNtRecord>> GetFileTypeNtAsync(string fileTypeCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE_NT", fileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<FileTypeNtRecord> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        return await _repository.GetFileTypeNtRecordAsync(fileTypeCode);
    }

    public async Task<DataResult<FileTypeNtRecord>> SaveFileTypeNtAsync(FileTypeNtRecord record, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE_NT", record.FileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<FileTypeNtRecord> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var existing = await _repository.GetFileTypeNtRecordAsync(record.FileTypeCode);
        RawCommandResult result;

        if (existing.IsSuccess && existing.Data != null)
            result = await _repository.UpdateFileTypeNtAsync(record);
        else
            result = await _repository.InsertFileTypeNtAsync(record);

        if (!result.IsSuccess)
            return new DataResult<FileTypeNtRecord> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "DATABASE_ERROR", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetFileTypeNtRecordAsync(record.FileTypeCode);
        return new DataResult<FileTypeNtRecord> { StatusCode = 200, Data = saved.Data };
    }

    public async Task<DataResult<bool>> DeleteFileTypeNtAsync(string fileTypeCode, SecurityContext context)
    {
        var auth = await _repository.AuthoriseAsync(context, "FILE_TYPE_NT", fileTypeCode);
        if (!auth.IsSuccess)
            return new DataResult<bool> { StatusCode = 403, ErrorCode = "UNAUTHORISED", ErrorMessage = auth.ErrorMessage ?? "Not authorised" };

        var result = await _repository.DeleteFileTypeNtAsync(fileTypeCode);
        return new DataResult<bool> { StatusCode = result.IsSuccess ? 200 : 500, Data = result.IsSuccess, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };
    }
}
