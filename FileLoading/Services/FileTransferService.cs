using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Selcomm.Data.Common;
using FileLoading.Interfaces;
using FileLoading.Models;
using FileLoading.Repositories;
using FileLoading.Transfer;

namespace FileLoading.Services;

/// <summary>
/// Service implementation for file transfer operations.
/// </summary>
public class FileTransferService : IFileTransferService
{
    private readonly IFileLoaderRepository _repository;
    private readonly ITransferClientFactory _clientFactory;
    private readonly CompressionHelper _compressionHelper;
    private readonly ILogger<FileTransferService> _logger;

    // Encryption key for password storage (should be from config in production)
    private const string EncryptionKey = "Selcomm-FileLoader-Key-32Chars!!";

    public FileTransferService(
        IFileLoaderRepository repository,
        ITransferClientFactory clientFactory,
        CompressionHelper compressionHelper,
        ILogger<FileTransferService> logger)
    {
        _repository = repository;
        _clientFactory = clientFactory;
        _compressionHelper = compressionHelper;
        _logger = logger;
    }

    // ============================================
    // Transfer Operations
    // ============================================

    public async Task<DataResult<TransferFetchResponse>> FetchFilesFromSourceAsync(
        int sourceId, SecurityContext context)
    {
        _logger.LogInformation("Fetching files from source: {SourceId}", sourceId);

        var response = new TransferFetchResponse();

        try
        {
            // Get source configuration
            var sourceResult = await _repository.GetTransferSourceAsync(sourceId);
            if (!sourceResult.IsSuccess || sourceResult.Data == null)
            {
                return new DataResult<TransferFetchResponse>
                {
                    StatusCode = sourceResult.StatusCode,
                    ErrorCode = sourceResult.ErrorCode,
                    ErrorMessage = sourceResult.ErrorMessage
                };
            }

            var sourceConfig = sourceResult.Data;

            // Decrypt password if present
            if (!string.IsNullOrEmpty(sourceConfig.Password))
            {
                sourceConfig.Password = DecryptPassword(sourceConfig.Password);
            }

            // Get folder configuration
            var folderResult = await _repository.GetFolderConfigAsync(sourceConfig.Domain, sourceConfig.FileTypeCode);
            if (!folderResult.IsSuccess || folderResult.Data == null)
            {
                return new DataResult<TransferFetchResponse>
                {
                    StatusCode = 400,
                    ErrorCode = "NO_FOLDER_CONFIG",
                    ErrorMessage = $"No folder configuration found for domain '{sourceConfig.Domain}'"
                };
            }

            var folderConfig = folderResult.Data;

            // Ensure transfer folder exists
            if (!Directory.Exists(folderConfig.TransferFolder))
            {
                Directory.CreateDirectory(folderConfig.TransferFolder);
            }

            // Create transfer client
            using var client = _clientFactory.CreateClient(sourceConfig);
            await client.ConnectAsync();

            // List remote files
            var remoteFiles = await client.ListFilesAsync(
                sourceConfig.RemotePath,
                sourceConfig.FileNamePattern);

            response.FilesFound = remoteFiles.Count;
            _logger.LogInformation("Found {Count} files on remote", remoteFiles.Count);

            foreach (var remoteFile in remoteFiles)
            {
                try
                {
                    // Check skip pattern
                    if (!string.IsNullOrEmpty(sourceConfig.SkipFilePattern) &&
                        MatchesPattern(remoteFile.Name, sourceConfig.SkipFilePattern))
                    {
                        _logger.LogDebug("Skipping file matching skip pattern: {FileName}", remoteFile.Name);
                        response.FilesSkipped++;
                        continue;
                    }

                    // Check if already downloaded (for sources where we can't delete)
                    if (!sourceConfig.DeleteAfterDownload)
                    {
                        var alreadyDownloaded = await _repository.IsFileDownloadedAsync(
                            sourceId, remoteFile.Name, remoteFile.LastModified, remoteFile.Size);

                        if (alreadyDownloaded)
                        {
                            _logger.LogDebug("File already downloaded: {FileName}", remoteFile.Name);
                            response.FilesSkipped++;
                            continue;
                        }
                    }

                    // Create transfer record
                    var localPath = Path.Combine(folderConfig.TransferFolder, remoteFile.Name);
                    var transferRecord = new FileTransferRecord
                    {
                        SourceId = sourceId,
                        FileName = remoteFile.Name,
                        Status = TransferStatus.Downloading,
                        SourcePath = remoteFile.FullPath,
                        DestinationPath = localPath,
                        CurrentFolder = "Transfer",
                        FileSize = remoteFile.Size,
                        StartedAt = DateTime.Now,
                        CreatedBy = context.UserCode ?? "SYSTEM"
                    };

                    var insertResult = await _repository.InsertTransferRecordAsync(transferRecord);
                    if (!insertResult.IsSuccess)
                    {
                        _logger.LogError("Failed to create transfer record for {FileName}", remoteFile.Name);
                        response.FilesFailed++;
                        response.Errors.Add($"Failed to create transfer record for {remoteFile.Name}");
                        continue;
                    }

                    transferRecord.TransferId = insertResult.Value;

                    // Download file
                    var downloaded = await client.DownloadFileAsync(remoteFile.FullPath, localPath);

                    if (downloaded)
                    {
                        // Update transfer record
                        await _repository.UpdateTransferStatusAsync(
                            transferRecord.TransferId, TransferStatus.Downloaded, null, DateTime.Now);

                        // Record download (for no-delete sources)
                        if (!sourceConfig.DeleteAfterDownload)
                        {
                            await _repository.InsertDownloadedFileAsync(new DownloadedFileRecord
                            {
                                SourceId = sourceId,
                                RemoteFileName = remoteFile.Name,
                                RemoteFilePath = remoteFile.FullPath,
                                FileSize = remoteFile.Size,
                                RemoteModifiedDate = remoteFile.LastModified
                            });
                        }
                        else
                        {
                            // Delete from remote
                            try
                            {
                                await client.DeleteFileAsync(remoteFile.FullPath);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to delete remote file: {FileName}", remoteFile.Name);
                            }
                        }

                        response.FilesDownloaded++;
                        response.TransferRecords.Add(transferRecord);

                        _logger.LogInformation("Downloaded file: {FileName}", remoteFile.Name);
                    }
                    else
                    {
                        await _repository.UpdateTransferStatusAsync(
                            transferRecord.TransferId, TransferStatus.Error, "Download failed");
                        response.FilesFailed++;
                        response.Errors.Add($"Failed to download {remoteFile.Name}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file: {FileName}", remoteFile.Name);
                    response.FilesFailed++;
                    response.Errors.Add($"Error processing {remoteFile.Name}: {ex.Message}");
                }
            }

            await client.DisconnectAsync();

            return new DataResult<TransferFetchResponse>
            {
                StatusCode = 200,
                Data = response
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching files from source {SourceId}", sourceId);
            return new DataResult<TransferFetchResponse>
            {
                StatusCode = 500,
                ErrorCode = "TRANSFER_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<DataResult<FileTransferRecord>> TransferToProcessingAsync(
        int transferId, SecurityContext context)
    {
        _logger.LogInformation("Transferring file {TransferId} to Processing", transferId);

        var transferResult = await _repository.GetTransferRecordAsync(transferId);
        if (!transferResult.IsSuccess || transferResult.Data == null)
        {
            return transferResult;
        }

        var transfer = transferResult.Data;

        // Get folder config
        var sourceResult = await _repository.GetTransferSourceAsync(transfer.SourceId ?? 0);
        var domain = sourceResult.Data?.Domain ?? "default";
        var fileType = sourceResult.Data?.FileTypeCode;

        var folderResult = await _repository.GetFolderConfigAsync(domain, fileType);
        if (!folderResult.IsSuccess || folderResult.Data == null)
        {
            return new DataResult<FileTransferRecord>
            {
                StatusCode = 400,
                ErrorCode = "NO_FOLDER_CONFIG",
                ErrorMessage = "Folder configuration not found"
            };
        }

        var folderConfig = folderResult.Data;

        // Ensure processing folder exists
        if (!Directory.Exists(folderConfig.ProcessingFolder))
        {
            Directory.CreateDirectory(folderConfig.ProcessingFolder);
        }

        // Move file
        var sourcePath = transfer.DestinationPath!;
        var destPath = Path.Combine(folderConfig.ProcessingFolder, transfer.FileName);

        try
        {
            File.Move(sourcePath, destPath, overwrite: true);

            await _repository.UpdateTransferFolderAsync(transferId, "Processing", destPath);
            await _repository.UpdateTransferStatusAsync(transferId, TransferStatus.Processing, null);

            transfer.DestinationPath = destPath;
            transfer.CurrentFolder = "Processing";
            transfer.Status = TransferStatus.Processing;

            return new DataResult<FileTransferRecord>
            {
                StatusCode = 200,
                Data = transfer
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move file to processing: {FileName}", transfer.FileName);
            return new DataResult<FileTransferRecord>
            {
                StatusCode = 500,
                ErrorCode = "MOVE_FAILED",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<DataResult<FileTransferRecord>> MoveToFolderAsync(
        int transferId, string targetFolder, bool compress, SecurityContext context)
    {
        _logger.LogInformation("Moving file {TransferId} to {Folder}", transferId, targetFolder);

        var transferResult = await _repository.GetTransferRecordAsync(transferId);
        if (!transferResult.IsSuccess || transferResult.Data == null)
        {
            return transferResult;
        }

        var transfer = transferResult.Data;

        // Get folder config
        var sourceResult = await _repository.GetTransferSourceAsync(transfer.SourceId ?? 0);
        var domain = sourceResult.Data?.Domain ?? "default";
        var fileType = sourceResult.Data?.FileTypeCode;

        var folderResult = await _repository.GetFolderConfigAsync(domain, fileType);
        if (!folderResult.IsSuccess || folderResult.Data == null)
        {
            return new DataResult<FileTransferRecord>
            {
                StatusCode = 400,
                ErrorCode = "NO_FOLDER_CONFIG",
                ErrorMessage = "Folder configuration not found"
            };
        }

        var folderConfig = folderResult.Data;

        // Determine target path
        var targetPath = targetFolder.ToUpper() switch
        {
            "TRANSFER" => folderConfig.TransferFolder,
            "PROCESSING" => folderConfig.ProcessingFolder,
            "PROCESSED" => folderConfig.ProcessedFolder,
            "ERRORS" => folderConfig.ErrorsFolder,
            "SKIPPED" => folderConfig.SkippedFolder,
            _ => throw new ArgumentException($"Unknown folder: {targetFolder}")
        };

        // Ensure folder exists
        if (!Directory.Exists(targetPath))
        {
            Directory.CreateDirectory(targetPath);
        }

        var sourcePath = transfer.DestinationPath!;
        var destFileName = transfer.FileName;

        try
        {
            // Compress if requested (typically for Processed/Skipped)
            if (compress && sourceResult.Data?.CompressOnArchive == true)
            {
                var compressedPath = await _compressionHelper.CompressFileAsync(
                    sourcePath, sourceResult.Data.Compression, deleteOriginal: true);
                sourcePath = compressedPath;
                destFileName = Path.GetFileName(compressedPath);
            }

            var destPath = Path.Combine(targetPath, destFileName);
            File.Move(sourcePath, destPath, overwrite: true);

            // Update transfer status based on folder
            var newStatus = targetFolder.ToUpper() switch
            {
                "PROCESSED" => TransferStatus.Processed,
                "ERRORS" => TransferStatus.Error,
                "SKIPPED" => TransferStatus.Skipped,
                _ => transfer.Status
            };

            await _repository.UpdateTransferFolderAsync(transferId, targetFolder, destPath);
            if (newStatus != transfer.Status)
            {
                await _repository.UpdateTransferStatusAsync(transferId, newStatus, null, DateTime.Now);
            }

            transfer.DestinationPath = destPath;
            transfer.CurrentFolder = targetFolder;
            transfer.Status = newStatus;

            return new DataResult<FileTransferRecord>
            {
                StatusCode = 200,
                Data = transfer
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move file to {Folder}: {FileName}", targetFolder, transfer.FileName);
            return new DataResult<FileTransferRecord>
            {
                StatusCode = 500,
                ErrorCode = "MOVE_FAILED",
                ErrorMessage = ex.Message
            };
        }
    }

    // ============================================
    // Compression Operations
    // ============================================

    public async Task<DataResult<string>> CompressFileAsync(string filePath, CompressionMethod method)
    {
        try
        {
            var compressedPath = await _compressionHelper.CompressFileAsync(filePath, method);
            return new DataResult<string> { StatusCode = 200, Data = compressedPath };
        }
        catch (Exception ex)
        {
            return new DataResult<string>
            {
                StatusCode = 500,
                ErrorCode = "COMPRESSION_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<DataResult<string>> DecompressFileAsync(string compressedFilePath, string destinationFolder)
    {
        try
        {
            var decompressedPath = await _compressionHelper.DecompressFileAsync(compressedFilePath, destinationFolder);
            return new DataResult<string> { StatusCode = 200, Data = decompressedPath };
        }
        catch (Exception ex)
        {
            return new DataResult<string>
            {
                StatusCode = 500,
                ErrorCode = "DECOMPRESSION_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }

    // ============================================
    // Query Operations
    // ============================================

    public async Task<DataResult<List<FileTransferRecord>>> ListTransfersAsync(
        int? sourceId, TransferStatus? status, string? currentFolder, int maxRecords, SecurityContext context)
    {
        return await _repository.ListTransferRecordsAsync(sourceId, status.HasValue ? (int)status.Value : null, currentFolder, maxRecords);
    }

    public async Task<DataResult<FileTransferRecord>> GetTransferAsync(int transferId, SecurityContext context)
    {
        return await _repository.GetTransferRecordAsync(transferId);
    }

    // ============================================
    // Source Configuration
    // ============================================

    public async Task<DataResult<List<TransferSourceConfig>>> GetSourceConfigsAsync(string? domain, SecurityContext context)
    {
        var result = await _repository.GetTransferSourcesAsync(domain);

        // Don't return passwords
        if (result.IsSuccess && result.Data != null)
        {
            foreach (var source in result.Data)
            {
                source.Password = string.IsNullOrEmpty(source.Password) ? null : "********";
            }
        }

        return result;
    }

    public async Task<DataResult<TransferSourceConfig>> GetSourceConfigAsync(int sourceId, SecurityContext context)
    {
        var result = await _repository.GetTransferSourceAsync(sourceId);

        if (result.IsSuccess && result.Data != null)
        {
            result.Data.Password = string.IsNullOrEmpty(result.Data.Password) ? null : "********";
        }

        return result;
    }

    public async Task<DataResult<TransferSourceConfig>> SaveSourceConfigAsync(TransferSourceRequest request, SecurityContext context)
    {
        var config = new TransferSourceConfig
        {
            SourceId = request.SourceId,
            VendorName = request.VendorName,
            Domain = request.Domain,
            FileTypeCode = request.FileTypeCode,
            Protocol = request.Protocol,
            Host = request.Host,
            Port = request.Port,
            RemotePath = request.RemotePath,
            AuthType = request.AuthType,
            Username = request.Username,
            Password = string.IsNullOrEmpty(request.Password) ? null : EncryptPassword(request.Password),
            CertificatePath = request.CertificatePath,
            PrivateKeyPath = request.PrivateKeyPath,
            FileNamePattern = request.FileNamePattern,
            SkipFilePattern = request.SkipFilePattern,
            DeleteAfterDownload = request.DeleteAfterDownload,
            CompressOnArchive = request.CompressOnArchive,
            Compression = request.Compression,
            CronSchedule = request.CronSchedule,
            IsEnabled = request.IsEnabled
        };

        // Check if exists (SourceId > 0 means update)
        RawCommandResult result;

        if (request.SourceId > 0)
        {
            var existingResult = await _repository.GetTransferSourceAsync(request.SourceId);
            if (existingResult.IsSuccess && existingResult.Data != null)
            {
                // If password is masked, keep existing
                if (request.Password == "********")
                {
                    config.Password = existingResult.Data.Password;
                }
            }
            result = await _repository.UpdateTransferSourceAsync(config);
        }
        else
        {
            result = await _repository.InsertTransferSourceAsync(config);
        }

        if (!result.IsSuccess)
        {
            return new DataResult<TransferSourceConfig>
            {
                StatusCode = 500,
                ErrorCode = result.ErrorCode ?? "DATABASE_ERROR",
                ErrorMessage = result.ErrorMessage
            };
        }

        config.Password = string.IsNullOrEmpty(config.Password) ? null : "********";
        return new DataResult<TransferSourceConfig>
        {
            StatusCode = 200,
            Data = config
        };
    }

    public async Task<DataResult<bool>> DeleteSourceConfigAsync(int sourceId, SecurityContext context)
    {
        var result = await _repository.DeleteTransferSourceAsync(sourceId);
        return new DataResult<bool>
        {
            StatusCode = result.IsSuccess ? 200 : 500,
            Data = result.IsSuccess,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    // ============================================
    // Folder Configuration
    // ============================================

    public async Task<DataResult<FolderWorkflowConfig>> GetFolderConfigAsync(
        string domain, string? fileTypeCode, SecurityContext context)
    {
        return await _repository.GetFolderConfigAsync(domain, fileTypeCode);
    }

    public async Task<DataResult<FolderWorkflowConfig>> SaveFolderConfigAsync(
        FolderWorkflowConfig config, SecurityContext context)
    {
        var result = await _repository.SaveFolderConfigAsync(config);

        if (!result.IsSuccess)
        {
            return new DataResult<FolderWorkflowConfig>
            {
                StatusCode = 500,
                ErrorCode = result.ErrorCode ?? "DATABASE_ERROR",
                ErrorMessage = result.ErrorMessage
            };
        }

        return await _repository.GetFolderConfigAsync(config.Domain, config.FileTypeCode);
    }

    // ============================================
    // Downloaded File Tracking
    // ============================================

    public async Task<bool> IsFileAlreadyDownloadedAsync(
        int sourceId, string fileName, DateTime modifiedDate, long fileSize)
    {
        return await _repository.IsFileDownloadedAsync(sourceId, fileName, modifiedDate, fileSize);
    }

    public async Task RecordDownloadedFileAsync(DownloadedFileRecord record)
    {
        await _repository.InsertDownloadedFileAsync(record);
    }

    // ============================================
    // Connection Testing
    // ============================================

    public async Task<DataResult<bool>> TestConnectionAsync(int sourceId, SecurityContext context)
    {
        var sourceResult = await _repository.GetTransferSourceAsync(sourceId);
        if (!sourceResult.IsSuccess || sourceResult.Data == null)
        {
            return new DataResult<bool>
            {
                StatusCode = sourceResult.StatusCode,
                Data = false,
                ErrorCode = sourceResult.ErrorCode,
                ErrorMessage = sourceResult.ErrorMessage
            };
        }

        var config = sourceResult.Data;
        if (!string.IsNullOrEmpty(config.Password))
        {
            config.Password = DecryptPassword(config.Password);
        }

        return await TestConnectionInternalAsync(config);
    }

    public async Task<DataResult<bool>> TestConnectionAsync(TransferSourceRequest request, SecurityContext context)
    {
        var config = new TransferSourceConfig
        {
            SourceId = request.SourceId,
            Protocol = request.Protocol,
            Host = request.Host,
            Port = request.Port,
            RemotePath = request.RemotePath,
            AuthType = request.AuthType,
            Username = request.Username,
            Password = request.Password,
            CertificatePath = request.CertificatePath,
            PrivateKeyPath = request.PrivateKeyPath
        };

        return await TestConnectionInternalAsync(config);
    }

    private async Task<DataResult<bool>> TestConnectionInternalAsync(TransferSourceConfig config)
    {
        try
        {
            using var client = _clientFactory.CreateClient(config);
            await client.ConnectAsync();

            // Try to list files to verify access
            await client.ListFilesAsync(config.RemotePath, "*.*");

            await client.DisconnectAsync();

            return new DataResult<bool>
            {
                StatusCode = 200,
                Data = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test failed for {Host}", config.Host);
            return new DataResult<bool>
            {
                StatusCode = 400,
                Data = false,
                ErrorCode = "CONNECTION_FAILED",
                ErrorMessage = ex.Message
            };
        }
    }

    // ============================================
    // Helper Methods
    // ============================================

    private static bool MatchesPattern(string fileName, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase);
    }

    private static string EncryptPassword(string password)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(EncryptionKey);
        aes.IV = new byte[16];

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(password);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        return Convert.ToBase64String(encryptedBytes);
    }

    private static string DecryptPassword(string encryptedPassword)
    {
        try
        {
            using var aes = Aes.Create();
            aes.Key = Encoding.UTF8.GetBytes(EncryptionKey);
            aes.IV = new byte[16];

            using var decryptor = aes.CreateDecryptor();
            var encryptedBytes = Convert.FromBase64String(encryptedPassword);
            var plainBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            // If decryption fails, return as-is (might be unencrypted)
            return encryptedPassword;
        }
    }
}
