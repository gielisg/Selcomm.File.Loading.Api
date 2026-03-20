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

            // Check storage mode for destination folders
            var storageResult = await _repository.GetFolderStorageAsync(sourceConfig.Domain);
            var isFtpStorage = storageResult.IsSuccess && storageResult.Data != null &&
                               storageResult.Data.StorageMode == StorageMode.Ftp;
            var storageConfig = isFtpStorage ? storageResult.Data : null;

            // Determine local download path
            string localDownloadDir;
            if (isFtpStorage && !string.IsNullOrEmpty(storageConfig!.TempLocalPath))
            {
                localDownloadDir = storageConfig.TempLocalPath;
            }
            else
            {
                localDownloadDir = folderConfig.TransferFolder;
            }

            // Ensure local download directory exists
            if (!Directory.Exists(localDownloadDir))
            {
                Directory.CreateDirectory(localDownloadDir);
            }

            // Create transfer client for vendor source
            using var client = _clientFactory.CreateClient(sourceConfig);
            await client.ConnectAsync();

            // List remote files
            var remoteFiles = await client.ListFilesAsync(
                sourceConfig.RemotePath,
                sourceConfig.FileNamePattern);

            response.FilesFound = remoteFiles.Count;
            _logger.LogInformation("Found {Count} files on remote", remoteFiles.Count);

            // If FTP storage, prepare FTP client for uploading to workflow folders
            ITransferClient? ftpStorageClient = null;
            if (isFtpStorage)
            {
                if (!string.IsNullOrEmpty(storageConfig!.Password))
                {
                    storageConfig.Password = DecryptPassword(storageConfig.Password);
                }
                var ftpConfig = BuildFtpConfigFromStorage(storageConfig);
                ftpStorageClient = _clientFactory.CreateClient(ftpConfig);
                await ftpStorageClient.ConnectAsync();
            }

            try
            {
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
                        var localPath = Path.Combine(localDownloadDir, remoteFile.Name);
                        var transferRecord = new FileTransferRecord
                        {
                            SourceId = sourceId,
                            FileName = remoteFile.Name,
                            Status = TransferStatus.Downloading,
                            SourcePath = remoteFile.FullPath,
                            DestinationPath = isFtpStorage
                                ? $"{folderConfig.TransferFolder}/{remoteFile.Name}"
                                : localPath,
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

                        // Download file from vendor source to local
                        var downloaded = await client.DownloadFileAsync(remoteFile.FullPath, localPath);

                        if (downloaded)
                        {
                            // If FTP storage, upload to FTP transfer folder then clean up local temp
                            if (isFtpStorage && ftpStorageClient != null)
                            {
                                var ftpTransferPath = $"{folderConfig.TransferFolder}/{remoteFile.Name}";
                                await ftpStorageClient.UploadFileAsync(localPath, ftpTransferPath);

                                // Delete local temp file
                                try { File.Delete(localPath); } catch { }
                            }

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
            }
            finally
            {
                if (ftpStorageClient != null)
                {
                    await ftpStorageClient.DisconnectAsync();
                    ftpStorageClient.Dispose();
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

        // Check storage mode
        var storageResult = await _repository.GetFolderStorageAsync(domain);
        var isFtpStorage = storageResult.IsSuccess && storageResult.Data != null &&
                           storageResult.Data.StorageMode == StorageMode.Ftp;

        try
        {
            if (isFtpStorage)
            {
                // FTP mode: download from FTP transfer folder to local temp path
                var storage = storageResult.Data!;
                if (!string.IsNullOrEmpty(storage.Password))
                {
                    storage.Password = DecryptPassword(storage.Password);
                }
                var ftpConfig = BuildFtpConfigFromStorage(storage);

                var tempDir = storage.TempLocalPath ?? $"/var/www/{domain}/files/_temp";
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                var localPath = Path.Combine(tempDir, transfer.FileName);
                var remotePath = transfer.DestinationPath!; // FTP path in transfer folder

                using var client = _clientFactory.CreateClient(ftpConfig);
                await client.ConnectAsync();
                await client.DownloadFileAsync(remotePath, localPath);
                await client.DisconnectAsync();

                // Update record with local temp path for processing
                await _repository.UpdateTransferFolderAsync(transferId, "Processing", localPath);
                await _repository.UpdateTransferStatusAsync(transferId, TransferStatus.Processing, null);

                transfer.DestinationPath = localPath;
                transfer.CurrentFolder = "Processing";
                transfer.Status = TransferStatus.Processing;
            }
            else
            {
                // LOCAL mode: move file on disk
                if (!Directory.Exists(folderConfig.ProcessingFolder))
                {
                    Directory.CreateDirectory(folderConfig.ProcessingFolder);
                }

                var sourcePath = transfer.DestinationPath!;
                var destPath = Path.Combine(folderConfig.ProcessingFolder, transfer.FileName);

                File.Move(sourcePath, destPath, overwrite: true);

                await _repository.UpdateTransferFolderAsync(transferId, "Processing", destPath);
                await _repository.UpdateTransferStatusAsync(transferId, TransferStatus.Processing, null);

                transfer.DestinationPath = destPath;
                transfer.CurrentFolder = "Processing";
                transfer.Status = TransferStatus.Processing;
            }

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

        // Check storage mode
        var storageResult = await _repository.GetFolderStorageAsync(domain);
        var isFtpStorage = storageResult.IsSuccess && storageResult.Data != null &&
                           storageResult.Data.StorageMode == StorageMode.Ftp;

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

            string destPath;

            if (isFtpStorage)
            {
                // FTP mode: upload to FTP target folder, delete local temp
                var storage = storageResult.Data!;
                if (!string.IsNullOrEmpty(storage.Password))
                {
                    storage.Password = DecryptPassword(storage.Password);
                }
                var ftpConfig = BuildFtpConfigFromStorage(storage);

                destPath = $"{targetPath}/{destFileName}";

                using var client = _clientFactory.CreateClient(ftpConfig);
                await client.ConnectAsync();
                await client.UploadFileAsync(sourcePath, destPath);
                await client.DisconnectAsync();

                // Delete local temp file
                if (File.Exists(sourcePath))
                {
                    try { File.Delete(sourcePath); } catch { }
                }
            }
            else
            {
                // LOCAL mode: move file on disk
                if (!Directory.Exists(targetPath))
                {
                    Directory.CreateDirectory(targetPath);
                }

                destPath = Path.Combine(targetPath, destFileName);
                File.Move(sourcePath, destPath, overwrite: true);
            }

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
    // Folder Storage Configuration
    // ============================================

    public async Task<DataResult<FolderStorageConfig>> GetFolderStorageAsync(
        string domain, SecurityContext context)
    {
        var result = await _repository.GetFolderStorageAsync(domain);

        // Mask password in response
        if (result.IsSuccess && result.Data != null)
        {
            result.Data.Password = string.IsNullOrEmpty(result.Data.Password) ? null : "********";
        }

        return result;
    }

    public async Task<DataResult<FolderStorageConfig>> SaveFolderStorageAsync(
        FolderStorageRequest request, SecurityContext context)
    {
        // Validate FTP fields when mode is FTP
        if (request.StorageMode == StorageMode.Ftp)
        {
            if (string.IsNullOrWhiteSpace(request.Host))
            {
                return new DataResult<FolderStorageConfig>
                {
                    StatusCode = 400,
                    ErrorCode = "VALIDATION_ERROR",
                    ErrorMessage = "Host is required for FTP storage mode"
                };
            }
        }

        var config = new FolderStorageConfig
        {
            Domain = request.Domain,
            StorageMode = request.StorageMode,
            Protocol = request.Protocol,
            Host = request.Host,
            Port = request.Port,
            AuthType = request.AuthType,
            Username = request.Username,
            Password = string.IsNullOrEmpty(request.Password) || request.Password == "********"
                ? null
                : EncryptPassword(request.Password),
            CertificatePath = request.CertificatePath,
            PrivateKeyPath = request.PrivateKeyPath,
            BasePath = request.BasePath,
            TempLocalPath = request.TempLocalPath
        };

        // If password is masked, keep existing encrypted password
        if (request.Password == "********")
        {
            var existingResult = await _repository.GetFolderStorageAsync(request.Domain);
            if (existingResult.IsSuccess && existingResult.Data != null)
            {
                config.Password = existingResult.Data.Password;
            }
        }

        // Clear FTP fields when mode is Local
        if (request.StorageMode == StorageMode.Local)
        {
            config.Protocol = null;
            config.Host = null;
            config.Port = null;
            config.AuthType = null;
            config.Username = null;
            config.Password = null;
            config.CertificatePath = null;
            config.PrivateKeyPath = null;
            config.BasePath = "/";
            config.TempLocalPath = null;
        }

        var result = await _repository.UpsertFolderStorageAsync(config);

        if (!result.IsSuccess)
        {
            return new DataResult<FolderStorageConfig>
            {
                StatusCode = 500,
                ErrorCode = result.ErrorCode ?? "DATABASE_ERROR",
                ErrorMessage = result.ErrorMessage
            };
        }

        // Return the saved config
        var saved = await _repository.GetFolderStorageAsync(request.Domain);
        if (saved.IsSuccess && saved.Data != null)
        {
            saved.Data.Password = string.IsNullOrEmpty(saved.Data.Password) ? null : "********";
        }
        return saved;
    }

    public async Task<DataResult<bool>> DeleteFolderStorageAsync(string domain, SecurityContext context)
    {
        var result = await _repository.DeleteFolderStorageAsync(domain);
        return new DataResult<bool>
        {
            StatusCode = result.IsSuccess ? 200 : 500,
            Data = result.IsSuccess,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    public async Task<DataResult<bool>> TestFolderStorageAsync(
        FolderStorageRequest request, SecurityContext context)
    {
        if (request.StorageMode == StorageMode.Local)
        {
            return new DataResult<bool>
            {
                StatusCode = 400,
                Data = false,
                ErrorCode = "INVALID_MODE",
                ErrorMessage = "Connection test is only applicable for FTP storage mode"
            };
        }

        var config = BuildFtpConfig(request);
        return await TestConnectionInternalAsync(config);
    }

    public Task<DataResult<FolderDefaultsResponse>> GetDefaultFolderPathsAsync(
        string domain, string? fileType, SecurityContext context)
    {
        var basePath = $"/var/www/{domain}/files";
        var typePath = string.IsNullOrEmpty(fileType) ? "default" : fileType;

        var defaults = new FolderDefaultsResponse
        {
            Domain = domain,
            FileTypeCode = fileType,
            TransferFolder = $"{basePath}/{typePath}/transfer",
            ProcessingFolder = $"{basePath}/{typePath}/processing",
            ProcessedFolder = $"{basePath}/{typePath}/processed",
            ErrorsFolder = $"{basePath}/{typePath}/errors",
            SkippedFolder = $"{basePath}/{typePath}/skipped"
        };

        return Task.FromResult(new DataResult<FolderDefaultsResponse>
        {
            StatusCode = 200,
            Data = defaults
        });
    }

    public async Task<DataResult<FolderCreateResult>> CreateFoldersAsync(
        string domain, string? fileType, SecurityContext context)
    {
        _logger.LogInformation("Creating folders for domain {Domain}, fileType {FileType}", domain, fileType);

        var result = new FolderCreateResult();

        // Get folder config for the domain/fileType
        var folderResult = await _repository.GetFolderConfigAsync(domain, fileType);
        if (!folderResult.IsSuccess || folderResult.Data == null)
        {
            return new DataResult<FolderCreateResult>
            {
                StatusCode = 400,
                ErrorCode = "NO_FOLDER_CONFIG",
                ErrorMessage = $"No folder configuration found for domain '{domain}'"
            };
        }

        var folderConfig = folderResult.Data;
        var folders = new Dictionary<string, string>
        {
            ["Transfer"] = folderConfig.TransferFolder,
            ["Processing"] = folderConfig.ProcessingFolder,
            ["Processed"] = folderConfig.ProcessedFolder,
            ["Errors"] = folderConfig.ErrorsFolder,
            ["Skipped"] = folderConfig.SkippedFolder
        };

        // Check storage mode
        var storageResult = await _repository.GetFolderStorageAsync(domain);
        var isLocal = !storageResult.IsSuccess || storageResult.Data == null ||
                      storageResult.Data.StorageMode == StorageMode.Local;

        if (isLocal)
        {
            // Create local directories
            foreach (var (name, path) in folders)
            {
                var status = new FolderCreateStatus { FolderName = name, Path = path };
                try
                {
                    if (Directory.Exists(path))
                    {
                        status.AlreadyExisted = true;
                        status.Created = true;
                    }
                    else
                    {
                        Directory.CreateDirectory(path);
                        status.Created = true;
                    }
                }
                catch (Exception ex)
                {
                    status.Error = ex.Message;
                }
                result.Folders.Add(status);
            }
        }
        else
        {
            // Create FTP directories
            var storage = storageResult.Data!;
            if (!string.IsNullOrEmpty(storage.Password))
            {
                storage.Password = DecryptPassword(storage.Password);
            }

            var ftpConfig = BuildFtpConfigFromStorage(storage);

            try
            {
                using var client = _clientFactory.CreateClient(ftpConfig);
                await client.ConnectAsync();

                foreach (var (name, path) in folders)
                {
                    var status = new FolderCreateStatus { FolderName = name, Path = path };
                    try
                    {
                        await client.CreateDirectoryAsync(path);
                        status.Created = true;
                    }
                    catch (Exception ex)
                    {
                        status.Error = ex.Message;
                    }
                    result.Folders.Add(status);
                }

                await client.DisconnectAsync();

                // Also create temp local path if configured
                if (!string.IsNullOrEmpty(storage.TempLocalPath) && !Directory.Exists(storage.TempLocalPath))
                {
                    Directory.CreateDirectory(storage.TempLocalPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to FTP for folder creation");
                return new DataResult<FolderCreateResult>
                {
                    StatusCode = 500,
                    ErrorCode = "FTP_ERROR",
                    ErrorMessage = $"Failed to connect to FTP server: {ex.Message}"
                };
            }
        }

        result.AllCreated = result.Folders.All(f => f.Created);

        return new DataResult<FolderCreateResult>
        {
            StatusCode = 200,
            Data = result
        };
    }

    private TransferSourceConfig BuildFtpConfig(FolderStorageRequest request) => new()
    {
        Protocol = request.Protocol ?? TransferProtocol.Sftp,
        Host = request.Host ?? "",
        Port = request.Port ?? (request.Protocol == TransferProtocol.Ftp ? 21 : 22),
        AuthType = request.AuthType ?? AuthenticationType.Password,
        Username = request.Username ?? "",
        Password = request.Password,
        CertificatePath = request.CertificatePath,
        PrivateKeyPath = request.PrivateKeyPath,
        RemotePath = request.BasePath
    };

    private static TransferSourceConfig BuildFtpConfigFromStorage(FolderStorageConfig storage) => new()
    {
        Protocol = storage.Protocol ?? TransferProtocol.Sftp,
        Host = storage.Host ?? "",
        Port = storage.Port ?? (storage.Protocol == TransferProtocol.Ftp ? 21 : 22),
        AuthType = storage.AuthType ?? AuthenticationType.Password,
        Username = storage.Username ?? "",
        Password = storage.Password,
        CertificatePath = storage.CertificatePath,
        PrivateKeyPath = storage.PrivateKeyPath,
        RemotePath = storage.BasePath
    };

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
