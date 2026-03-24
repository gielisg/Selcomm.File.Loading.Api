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
                    ErrorCode = "FileLoading.FileTypeNotFound",
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
        string? fileTypeCode, int maxRecords, SecurityContext context)
    {
        var filter = new FileListFilter
        {
            FileTypeCode = fileTypeCode,
            Status = TransferStatus.Error,
            MaxRecords = maxRecords
        };

        return await _repository.ListFilesWithStatusAsync(filter);
    }

    public async Task<DataResult<List<FileWithStatus>>> GetSkippedFilesAsync(
        string? fileTypeCode, int maxRecords, SecurityContext context)
    {
        var filter = new FileListFilter
        {
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
        if (existing != null)
            return new DataResult<GenericFileFormatConfig> { StatusCode = 409, ErrorCode = "FileLoading.AlreadyExists", ErrorMessage = $"Parser config for '{request.FileTypeCode}' already exists" };

        var (config, columnMappings) = MapParserRequest(request, context);

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

        request.FileTypeCode = fileTypeCode;
        var (config, columnMappings) = MapParserRequest(request, context);

        var result = await _repository.UpdateGenericFileFormatConfigAsync(config);
        if (!result.IsSuccess)
            return new DataResult<GenericFileFormatConfig> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "FileLoading.DatabaseError", ErrorMessage = result.ErrorMessage };

        await _repository.DeleteColumnMappingsAsync(fileTypeCode);
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

        // Insert metadata
        var metadata = new CustomTableMetadata
        {
            FileTypeCode = fileTypeCode.Trim(),
            TableName = tableName,
            Version = version,
            Status = "ACTIVE",
            ColumnCount = config.ColumnMappings.Count,
            ColumnDefinition = columnDefJson,
            CreatedBy = context.UserCode
        };

        var insertResult = await _repository.InsertCustomTableMetadataAsync(metadata);
        metadata.CustomTableId = insertResult.Value;

        _logger.LogInformation("Created custom table {TableName} (v{Version}) for file type {FileTypeCode}",
            tableName, version, fileTypeCode);

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

        // Get current column mappings
        var config = await _repository.GetGenericFileFormatConfigAsync(fileTypeCode);
        if (config == null || config.ColumnMappings.Count == 0)
            return new DataResult<CustomTableMetadata> { StatusCode = 400, ErrorCode = "FileLoading.NoMappings", ErrorMessage = "Parser configuration has no column mappings" };

        var newVersion = existing.Version + 1;
        var tableName = CustomTableHelper.DeriveTableName(fileTypeCode, newVersion);
        var ddl = CustomTableHelper.GenerateCreateTableDdl(tableName, config.ColumnMappings);
        var columns = CustomTableHelper.BuildColumnDefs(config.ColumnMappings);
        var columnDefJson = JsonSerializer.Serialize(columns);

        // Execute DDL for new table
        var ddlResult = await _repository.ExecuteCreateTableAsync(ddl);
        if (!ddlResult.IsSuccess)
            return new DataResult<CustomTableMetadata> { StatusCode = 500, ErrorCode = "FileLoading.DdlFailed", ErrorMessage = $"Failed to create table: {ddlResult.ErrorMessage}" };

        // Retire current version
        await _repository.UpdateCustomTableStatusAsync(existing.CustomTableId, "RETIRED");

        // Insert new metadata
        var metadata = new CustomTableMetadata
        {
            FileTypeCode = fileTypeCode.Trim(),
            TableName = tableName,
            Version = newVersion,
            Status = "ACTIVE",
            ColumnCount = config.ColumnMappings.Count,
            ColumnDefinition = columnDefJson,
            CreatedBy = context.UserCode
        };

        var insertResult = await _repository.InsertCustomTableMetadataAsync(metadata);
        metadata.CustomTableId = insertResult.Value;

        _logger.LogInformation("Created new version v{Version} of custom table for {FileTypeCode}. Previous v{OldVersion} retired.",
            newVersion, fileTypeCode, existing.Version);

        return new DataResult<CustomTableMetadata> { StatusCode = 201, Data = metadata };
    }

    public async Task<DataResult<bool>> DropCustomTableVersionAsync(string fileTypeCode, int version, SecurityContext context)
    {
        var tableMetadata = await _repository.GetCustomTableByVersionAsync(fileTypeCode, version);
        if (tableMetadata == null)
            return new DataResult<bool> { StatusCode = 404, ErrorCode = "FileLoading.NotFound", ErrorMessage = $"Custom table version {version} not found for '{fileTypeCode}'" };

        if (tableMetadata.Status == "DROPPED")
            return new DataResult<bool> { StatusCode = 400, ErrorCode = "FileLoading.AlreadyDropped", ErrorMessage = "Table has already been dropped" };

        // Check table is empty
        var recordCount = await _repository.GetLiveRecordCountAsync(tableMetadata.TableName);
        if (recordCount > 0)
            return new DataResult<bool> { StatusCode = 400, ErrorCode = "FileLoading.TableNotEmpty", ErrorMessage = $"Table has {recordCount} records. Cannot drop a non-empty table." };

        // Drop the physical table
        var dropResult = await _repository.DropTableAsync(tableMetadata.TableName);
        if (!dropResult.IsSuccess)
            return new DataResult<bool> { StatusCode = 500, ErrorCode = "FileLoading.DropFailed", ErrorMessage = $"Failed to drop table: {dropResult.ErrorMessage}" };

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
        return new DataResult<int> { StatusCode = 200, Data = count };
    }

    public async Task<DataResult<TestLoadResult>> TestLoadCustomTableAsync(string fileTypeCode, Stream fileStream, string fileName, SecurityContext context)
    {
        // Check custom table exists
        var customTable = await _repository.GetActiveCustomTableAsync(fileTypeCode);
        if (customTable == null)
            return new DataResult<TestLoadResult> { StatusCode = 404, ErrorCode = "FileLoading.NoActiveTable", ErrorMessage = "No active custom table exists for this file type" };

        // Save uploaded file to temp location
        var tempPath = Path.Combine(Path.GetTempPath(), $"testload_{Guid.NewGuid():N}_{fileName}");
        try
        {
            using (var fs = new FileStream(tempPath, FileMode.Create))
            {
                await fileStream.CopyToAsync(fs);
            }

            // Use the existing file loader service to process the file
            // It will automatically route to the custom table (once parser integration is done)
            var loadResult = await _fileLoaderService.LoadFileAsync(
                new LoadFileRequest
                {
                    FileName = tempPath,
                    FileType = fileTypeCode
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

        // Get active custom table
        var customTable = await _repository.GetActiveCustomTableAsync(fileTypeCode);
        if (customTable == null)
            return new DataResult<bool> { StatusCode = 404, ErrorCode = "FileLoading.NoActiveTable", ErrorMessage = "No active custom table found" };

        // Delete records from custom table
        await _repository.DeleteCustomTableRecordsAsync(customTable.TableName, ntFileNum);

        // Also delete from generic detail in case it went there
        await _repository.UnloadFileRecordsAsync(ntFileNum, context);

        // Delete the nt_file record
        await _repository.DeleteNtFileAsync(ntFileNum);

        _logger.LogInformation("Deleted test load {NtFileNum} from custom table {TableName}", ntFileNum, customTable.TableName);

        return new DataResult<bool> { StatusCode = 200, Data = true };
    }
}
