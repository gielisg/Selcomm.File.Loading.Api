using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
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
    private readonly IConfiguration _configuration;
    private readonly ILogger<FileTransferService> _logger;

    // Encryption key for password storage (should be from config in production)
    private const string EncryptionKey = "Selcomm-FileLoader-Key-32Chars!!";

    public FileTransferService(
        IFileLoaderRepository repository,
        ITransferClientFactory clientFactory,
        CompressionHelper compressionHelper,
        IConfiguration configuration,
        ILogger<FileTransferService> logger)
    {
        _repository = repository;
        _clientFactory = clientFactory;
        _compressionHelper = compressionHelper;
        _configuration = configuration;
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
            var folderResult = await _repository.GetFolderConfigAsync(sourceConfig.FileTypeCode);
            if (!folderResult.IsSuccess || folderResult.Data == null)
            {
                return new DataResult<TransferFetchResponse>
                {
                    StatusCode = 400,
                    ErrorCode = "FileLoading.FolderConfigNotFound",
                    ErrorMessage = "No folder configuration found"
                };
            }

            var folderConfig = folderResult.Data;

            // Check for active FTP server
            var activeServerResult = await _repository.GetActiveFtpServerAsync();
            var activeFtpServer = activeServerResult.IsSuccess ? activeServerResult.Data : null;
            var isFtpStorage = activeFtpServer != null;

            // Determine local download path
            string localDownloadDir;
            if (isFtpStorage && !string.IsNullOrEmpty(activeFtpServer!.TempLocalPath))
            {
                localDownloadDir = activeFtpServer.TempLocalPath;
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
                var ftpPassword = activeFtpServer!.Password;
                if (!string.IsNullOrEmpty(ftpPassword))
                {
                    ftpPassword = DecryptPassword(ftpPassword);
                }
                var ftpConfig = BuildFtpConfigFromServer(activeFtpServer, ftpPassword);
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
                            FtpServerId = activeFtpServer?.ServerId,
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
                ErrorCode = "FileLoading.TransferError",
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
        var fileType = sourceResult.Data?.FileTypeCode;

        var folderResult = await _repository.GetFolderConfigAsync(fileType);
        if (!folderResult.IsSuccess || folderResult.Data == null)
        {
            return new DataResult<FileTransferRecord>
            {
                StatusCode = 400,
                ErrorCode = "FileLoading.FolderConfigNotFound",
                ErrorMessage = "Folder configuration not found"
            };
        }

        var folderConfig = folderResult.Data;

        // Check for active FTP server
        var activeServerResult = await _repository.GetActiveFtpServerAsync();
        var activeFtpServer = activeServerResult.IsSuccess ? activeServerResult.Data : null;
        var isFtpStorage = activeFtpServer != null;

        try
        {
            if (isFtpStorage)
            {
                // FTP mode: download from FTP transfer folder to local temp path
                var ftpPassword = activeFtpServer!.Password;
                if (!string.IsNullOrEmpty(ftpPassword))
                {
                    ftpPassword = DecryptPassword(ftpPassword);
                }
                var ftpConfig = BuildFtpConfigFromServer(activeFtpServer, ftpPassword);

                var tempDir = activeFtpServer.TempLocalPath ?? $"/var/www/{context.Domain ?? "default"}/files/_temp";
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
                ErrorCode = "FileLoading.MoveFailed",
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
        var fileType = sourceResult.Data?.FileTypeCode;

        var folderResult = await _repository.GetFolderConfigAsync(fileType);
        if (!folderResult.IsSuccess || folderResult.Data == null)
        {
            return new DataResult<FileTransferRecord>
            {
                StatusCode = 400,
                ErrorCode = "FileLoading.FolderConfigNotFound",
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

        // Check for active FTP server
        var activeServerResult = await _repository.GetActiveFtpServerAsync();
        var activeFtpServer = activeServerResult.IsSuccess ? activeServerResult.Data : null;
        var isFtpStorage = activeFtpServer != null;

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
                var ftpPassword = activeFtpServer!.Password;
                if (!string.IsNullOrEmpty(ftpPassword))
                {
                    ftpPassword = DecryptPassword(ftpPassword);
                }
                var ftpConfig = BuildFtpConfigFromServer(activeFtpServer, ftpPassword);

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
                ErrorCode = "FileLoading.MoveFailed",
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
                ErrorCode = "FileLoading.CompressionError",
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
                ErrorCode = "FileLoading.DecompressionError",
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

    public async Task<DataResult<List<TransferSourceConfig>>> GetSourceConfigsAsync(SecurityContext context)
    {
        var result = await _repository.GetTransferSourcesAsync();

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
            IsEnabled = request.IsEnabled,
            CreatedBy = context.UserCode ?? "SYSTEM",
            UpdatedBy = context.UserCode ?? "SYSTEM"
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
        string? fileTypeCode, SecurityContext context)
    {
        return await _repository.GetFolderConfigAsync(fileTypeCode);
    }

    public async Task<DataResult<FolderWorkflowConfig>> SaveFolderConfigAsync(
        string? fileTypeCode, SecurityContext context)
    {
        // Determine storage mode from active FTP server
        var activeServerResult = await _repository.GetActiveFtpServerAsync();
        var activeFtpServer = activeServerResult.IsSuccess ? activeServerResult.Data : null;
        var isFtpMode = activeFtpServer != null;

        var typePath = string.IsNullOrEmpty(fileTypeCode) ? "default" : fileTypeCode;
        var rootBase = _configuration["LocalStorage:BasePath"] ?? "/var/www";
        var domain = context.Domain ?? "default";
        var localBasePath = $"{rootBase}/{domain}/files";

        FolderWorkflowConfig config;

        if (!isFtpMode)
        {
            // Local: all paths derived from config
            config = new FolderWorkflowConfig
            {
                FileTypeCode = fileTypeCode,
                TransferFolder = $"{localBasePath}/{typePath}/transfer",
                ProcessingFolder = $"{localBasePath}/{typePath}/processing",
                ProcessedFolder = $"{localBasePath}/{typePath}/processed",
                ErrorsFolder = $"{localBasePath}/{typePath}/errors",
                SkippedFolder = $"{localBasePath}/{typePath}/skipped",
                ExampleFolder = $"{localBasePath}/{typePath}/example"
            };
        }
        else
        {
            // FTP: 5 workflow folders on FTP server, Example always local
            var ftpBase = $"{activeFtpServer!.RootPath}/files";
            config = new FolderWorkflowConfig
            {
                FileTypeCode = fileTypeCode,
                TransferFolder = $"{ftpBase}/{typePath}/transfer",
                ProcessingFolder = $"{ftpBase}/{typePath}/processing",
                ProcessedFolder = $"{ftpBase}/{typePath}/processed",
                ErrorsFolder = $"{ftpBase}/{typePath}/errors",
                SkippedFolder = $"{ftpBase}/{typePath}/skipped",
                ExampleFolder = $"{localBasePath}/{typePath}/example"
            };
        }

        config.CreatedBy = context.UserCode ?? "SYSTEM";
        config.UpdatedBy = context.UserCode ?? "SYSTEM";

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

        return await _repository.GetFolderConfigAsync(config.FileTypeCode);
    }

    // ============================================
    // FTP Server Configuration
    // ============================================

    public async Task<DataResult<List<FtpServer>>> GetFtpServersAsync(SecurityContext context)
    {
        var result = await _repository.GetFtpServersAsync();

        // Mask passwords in response
        if (result.IsSuccess && result.Data != null)
        {
            foreach (var server in result.Data)
                server.Password = string.IsNullOrEmpty(server.Password) ? null : "********";
        }

        return result;
    }

    public async Task<DataResult<FtpServer>> GetFtpServerAsync(int serverId, SecurityContext context)
    {
        var result = await _repository.GetFtpServerAsync(serverId);

        if (result.IsSuccess && result.Data != null)
            result.Data.Password = string.IsNullOrEmpty(result.Data.Password) ? null : "********";

        return result;
    }

    public async Task<DataResult<FtpServer>> CreateFtpServerAsync(FtpServerRequest request, SecurityContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Host))
            return new DataResult<FtpServer> { StatusCode = 400, ErrorCode = "FileLoading.ValidationError", ErrorMessage = "Host is required" };

        if (string.IsNullOrWhiteSpace(request.ServerName))
            return new DataResult<FtpServer> { StatusCode = 400, ErrorCode = "FileLoading.ValidationError", ErrorMessage = "ServerName is required" };

        var server = new FtpServer
        {
            ServerName = request.ServerName,
            Protocol = request.Protocol,
            Host = request.Host,
            Port = request.Port,
            AuthType = request.AuthType,
            Username = request.Username,
            Password = string.IsNullOrEmpty(request.Password) ? null : EncryptPassword(request.Password),
            CertificatePath = request.CertificatePath,
            PrivateKeyPath = request.PrivateKeyPath,
            RootPath = request.RootPath,
            TempLocalPath = request.TempLocalPath,
            IsActive = false,
            CreatedBy = context.UserCode ?? "SYSTEM",
            UpdatedBy = context.UserCode ?? "SYSTEM"
        };

        var insertResult = await _repository.InsertFtpServerAsync(server);
        if (!insertResult.IsSuccess)
            return new DataResult<FtpServer> { StatusCode = 500, ErrorCode = insertResult.ErrorCode ?? "DATABASE_ERROR", ErrorMessage = insertResult.ErrorMessage };

        var saved = await _repository.GetFtpServerAsync(insertResult.Value);
        if (saved.IsSuccess && saved.Data != null)
            saved.Data.Password = string.IsNullOrEmpty(saved.Data.Password) ? null : "********";

        return new DataResult<FtpServer> { StatusCode = 201, Data = saved.Data };
    }

    public async Task<DataResult<FtpServer>> UpdateFtpServerAsync(int serverId, FtpServerRequest request, SecurityContext context)
    {
        var existing = await _repository.GetFtpServerAsync(serverId);
        if (!existing.IsSuccess || existing.Data == null)
            return new DataResult<FtpServer> { StatusCode = 404, ErrorCode = "FileLoading.FtpServerNotFound", ErrorMessage = $"FTP server {serverId} not found" };

        var server = existing.Data;
        var isLocked = await _repository.IsFtpServerLockedAsync(serverId);

        // Immutability check: if locked, only ServerName and TempLocalPath can change
        if (isLocked)
        {
            if (request.Host != server.Host || request.RootPath != server.RootPath ||
                request.Protocol != server.Protocol || request.Port != server.Port ||
                request.AuthType != server.AuthType)
            {
                return new DataResult<FtpServer>
                {
                    StatusCode = 409,
                    ErrorCode = "FileLoading.ServerLocked",
                    ErrorMessage = "This FTP server is referenced by transfer records. Only ServerName and TempLocalPath can be updated. Create a new server for different connection settings."
                };
            }
        }

        server.ServerName = request.ServerName;
        server.Protocol = request.Protocol;
        server.Host = request.Host;
        server.Port = request.Port;
        server.AuthType = request.AuthType;
        server.Username = request.Username;
        server.CertificatePath = request.CertificatePath;
        server.PrivateKeyPath = request.PrivateKeyPath;
        server.RootPath = request.RootPath;
        server.TempLocalPath = request.TempLocalPath;
        server.UpdatedBy = context.UserCode ?? "SYSTEM";

        // Handle password: keep existing if masked
        if (!string.IsNullOrEmpty(request.Password) && request.Password != "********")
        {
            server.Password = EncryptPassword(request.Password);
        }
        // else: keep existing encrypted password from DB

        var result = await _repository.UpdateFtpServerAsync(server);
        if (!result.IsSuccess)
            return new DataResult<FtpServer> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "DATABASE_ERROR", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetFtpServerAsync(serverId);
        if (saved.IsSuccess && saved.Data != null)
            saved.Data.Password = string.IsNullOrEmpty(saved.Data.Password) ? null : "********";

        return saved;
    }

    public async Task<DataResult<bool>> DeleteFtpServerAsync(int serverId, SecurityContext context)
    {
        var isLocked = await _repository.IsFtpServerLockedAsync(serverId);
        if (isLocked)
        {
            return new DataResult<bool>
            {
                StatusCode = 409,
                Data = false,
                ErrorCode = "FileLoading.ServerLocked",
                ErrorMessage = "Cannot delete FTP server — it is referenced by transfer records"
            };
        }

        var result = await _repository.DeleteFtpServerAsync(serverId);
        return new DataResult<bool>
        {
            StatusCode = result.IsSuccess ? 200 : 500,
            Data = result.IsSuccess,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    public async Task<DataResult<FtpServer>> ActivateFtpServerAsync(int serverId, SecurityContext context)
    {
        var existing = await _repository.GetFtpServerAsync(serverId);
        if (!existing.IsSuccess || existing.Data == null)
            return new DataResult<FtpServer> { StatusCode = 404, ErrorCode = "FileLoading.FtpServerNotFound", ErrorMessage = $"FTP server {serverId} not found" };

        var result = await _repository.ActivateFtpServerAsync(serverId);
        if (!result.IsSuccess)
            return new DataResult<FtpServer> { StatusCode = 500, ErrorCode = result.ErrorCode ?? "DATABASE_ERROR", ErrorMessage = result.ErrorMessage };

        var saved = await _repository.GetFtpServerAsync(serverId);
        if (saved.IsSuccess && saved.Data != null)
            saved.Data.Password = string.IsNullOrEmpty(saved.Data.Password) ? null : "********";

        return saved;
    }

    public async Task<DataResult<bool>> DeactivateFtpServerAsync(int serverId, SecurityContext context)
    {
        var result = await _repository.DeactivateAllFtpServersAsync();
        return new DataResult<bool>
        {
            StatusCode = result.IsSuccess ? 200 : 500,
            Data = result.IsSuccess,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    public async Task<DataResult<bool>> TestFtpConnectionAsync(FtpServerRequest request, SecurityContext context)
    {
        var config = BuildFtpConfigFromRequest(request);
        return await TestConnectionInternalAsync(config);
    }

    public async Task<DataResult<FtpServer?>> GetActiveFtpServerAsync(SecurityContext context)
    {
        return await _repository.GetActiveFtpServerAsync();
    }

    public async Task<DataResult<FolderDefaultsResponse>> GetDefaultFolderPathsAsync(
        string? fileType, SecurityContext context)
    {
        var domain = context.Domain ?? "default";
        var rootBase = _configuration["LocalStorage:BasePath"] ?? "/var/www";
        var localBasePath = $"{rootBase}/{domain}/files";
        var typePath = string.IsNullOrEmpty(fileType) ? "default" : fileType;

        // Check for active FTP server
        var activeServerResult = await _repository.GetActiveFtpServerAsync();
        var activeFtpServer = activeServerResult.IsSuccess ? activeServerResult.Data : null;
        var isFtpMode = activeFtpServer != null;

        var defaults = new FolderDefaultsResponse
        {
            FileTypeCode = fileType,
            IsFtpMode = isFtpMode,
            BasePath = localBasePath,
            IsExampleAlwaysLocal = isFtpMode
        };

        if (isFtpMode)
        {
            var ftpBase = $"{activeFtpServer!.RootPath}/files";
            defaults.TransferFolder = $"{ftpBase}/{typePath}/transfer";
            defaults.ProcessingFolder = $"{ftpBase}/{typePath}/processing";
            defaults.ProcessedFolder = $"{ftpBase}/{typePath}/processed";
            defaults.ErrorsFolder = $"{ftpBase}/{typePath}/errors";
            defaults.SkippedFolder = $"{ftpBase}/{typePath}/skipped";
            defaults.ExampleFolder = $"{localBasePath}/{typePath}/example";
        }
        else
        {
            defaults.TransferFolder = $"{localBasePath}/{typePath}/transfer";
            defaults.ProcessingFolder = $"{localBasePath}/{typePath}/processing";
            defaults.ProcessedFolder = $"{localBasePath}/{typePath}/processed";
            defaults.ErrorsFolder = $"{localBasePath}/{typePath}/errors";
            defaults.SkippedFolder = $"{localBasePath}/{typePath}/skipped";
            defaults.ExampleFolder = $"{localBasePath}/{typePath}/example";
        }

        return new DataResult<FolderDefaultsResponse> { StatusCode = 200, Data = defaults };
    }

    public async Task<DataResult<FolderCreateResult>> CreateFoldersAsync(
        string? fileType, SecurityContext context)
    {
        _logger.LogInformation("Creating folders for fileType {FileType}", fileType);

        var result = new FolderCreateResult();

        // Get folder config for the fileType
        var folderResult = await _repository.GetFolderConfigAsync(fileType);
        if (!folderResult.IsSuccess || folderResult.Data == null)
        {
            return new DataResult<FolderCreateResult>
            {
                StatusCode = 400,
                ErrorCode = "FileLoading.FolderConfigNotFound",
                ErrorMessage = "No folder configuration found"
            };
        }

        var folderConfig = folderResult.Data;
        var folders = new Dictionary<string, string>
        {
            ["Transfer"] = folderConfig.TransferFolder,
            ["Processing"] = folderConfig.ProcessingFolder,
            ["Processed"] = folderConfig.ProcessedFolder,
            ["Errors"] = folderConfig.ErrorsFolder,
            ["Skipped"] = folderConfig.SkippedFolder,
            ["Example"] = folderConfig.ExampleFolder
        };

        // Check for active FTP server
        var activeServerResult = await _repository.GetActiveFtpServerAsync();
        var activeFtpServer = activeServerResult.IsSuccess ? activeServerResult.Data : null;
        var isFtpMode = activeFtpServer != null;

        if (!isFtpMode)
        {
            // Create all local directories
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
            // FTP mode: create 5 workflow folders on FTP + Example locally
            var ftpServer = activeFtpServer!;
            var password = ftpServer.Password;
            if (!string.IsNullOrEmpty(password))
            {
                password = DecryptPassword(password);
            }

            var ftpConfig = BuildFtpConfigFromServer(ftpServer, password);

            try
            {
                using var client = _clientFactory.CreateClient(ftpConfig);
                await client.ConnectAsync();

                foreach (var (name, path) in folders)
                {
                    var status = new FolderCreateStatus { FolderName = name, Path = path };
                    try
                    {
                        if (name == "Example")
                        {
                            // Example always created locally
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
                        else
                        {
                            await client.CreateDirectoryAsync(path);
                            status.Created = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        status.Error = ex.Message;
                    }
                    result.Folders.Add(status);
                }

                await client.DisconnectAsync();

                // Also create temp local path if configured
                if (!string.IsNullOrEmpty(ftpServer.TempLocalPath) && !Directory.Exists(ftpServer.TempLocalPath))
                {
                    Directory.CreateDirectory(ftpServer.TempLocalPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to FTP for folder creation");
                return new DataResult<FolderCreateResult>
                {
                    StatusCode = 500,
                    ErrorCode = "FileLoading.FtpError",
                    ErrorMessage = $"Failed to connect to FTP server: {ex.Message}"
                };
            }
        }

        result.AllCreated = result.Folders.All(f => f.Created);

        return new DataResult<FolderCreateResult> { StatusCode = 200, Data = result };
    }

    /// <summary>
    /// Ensure a single folder exists on the FTP server. Idempotent — safe to call repeatedly.
    /// </summary>
    public async Task EnsureFtpFolderExistsAsync(FtpServer ftpServer, string folderPath)
    {
        var password = ftpServer.Password;
        if (!string.IsNullOrEmpty(password))
        {
            password = DecryptPassword(password);
        }

        var ftpConfig = BuildFtpConfigFromServer(ftpServer, password);

        using var client = _clientFactory.CreateClient(ftpConfig);
        await client.ConnectAsync();
        try
        {
            await client.CreateDirectoryAsync(folderPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Folder may already exist: {Path}", folderPath);
        }
        await client.DisconnectAsync();
    }

    private static TransferSourceConfig BuildFtpConfigFromRequest(FtpServerRequest request) => new()
    {
        Protocol = request.Protocol,
        Host = request.Host,
        Port = request.Port,
        AuthType = request.AuthType,
        Username = request.Username ?? "",
        Password = request.Password,
        CertificatePath = request.CertificatePath,
        PrivateKeyPath = request.PrivateKeyPath,
        RemotePath = request.RootPath
    };

    private static TransferSourceConfig BuildFtpConfigFromServer(FtpServer server, string? decryptedPassword = null) => new()
    {
        Protocol = server.Protocol,
        Host = server.Host,
        Port = server.Port,
        AuthType = server.AuthType,
        Username = server.Username ?? "",
        Password = decryptedPassword ?? server.Password,
        CertificatePath = server.CertificatePath,
        PrivateKeyPath = server.PrivateKeyPath,
        RemotePath = server.RootPath
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
                ErrorCode = "FileLoading.ConnectionFailed",
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
