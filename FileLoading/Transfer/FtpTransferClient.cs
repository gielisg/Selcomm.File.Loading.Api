using FluentFTP;
using Microsoft.Extensions.Logging;
using FileLoading.Models;

namespace FileLoading.Transfer;

/// <summary>
/// FTP transfer client implementation using FluentFTP.
/// </summary>
public class FtpTransferClient : ITransferClient
{
    private readonly TransferSourceConfig _config;
    private readonly ILogger<FtpTransferClient> _logger;
    private AsyncFtpClient? _client;
    private bool _disposed;

    public FtpTransferClient(TransferSourceConfig config, ILogger<FtpTransferClient> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsConnected => _client?.IsConnected ?? false;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client?.IsConnected == true)
            return;

        _logger.LogInformation("Connecting to FTP server {Host}:{Port}", _config.Host, _config.Port);

        _client = new AsyncFtpClient(_config.Host, _config.Username, _config.Password, _config.Port);

        // Configure FTP options
        _client.Config.EncryptionMode = FtpEncryptionMode.Auto;
        _client.Config.ValidateAnyCertificate = true; // For testing - should be configurable

        await _client.Connect(cancellationToken);

        _logger.LogInformation("Connected to FTP server {Host}", _config.Host);
    }

    public async Task DisconnectAsync()
    {
        if (_client?.IsConnected == true)
        {
            _logger.LogInformation("Disconnecting from FTP server {Host}", _config.Host);
            await _client.Disconnect();
        }
    }

    public async Task<List<RemoteFileInfo>> ListFilesAsync(
        string remotePath,
        string filePattern,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        _logger.LogDebug("Listing files in {RemotePath} with pattern {Pattern}", remotePath, filePattern);

        var files = new List<RemoteFileInfo>();
        var pattern = ConvertGlobToRegex(filePattern);

        var items = await _client!.GetListing(remotePath, cancellationToken);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Type == FtpObjectType.Directory)
                continue;

            if (System.Text.RegularExpressions.Regex.IsMatch(item.Name, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                files.Add(new RemoteFileInfo
                {
                    Name = item.Name,
                    FullPath = item.FullName,
                    Size = item.Size,
                    LastModified = item.Modified,
                    IsDirectory = false
                });
            }
        }

        _logger.LogInformation("Found {Count} files matching pattern in {RemotePath}", files.Count, remotePath);
        return files;
    }

    public async Task<bool> DownloadFileAsync(
        string remotePath,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        _logger.LogInformation("Downloading {RemotePath} to {LocalPath}", remotePath, localPath);

        try
        {
            // Ensure local directory exists
            var localDir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
            {
                Directory.CreateDirectory(localDir);
            }

            var status = await _client!.DownloadFile(localPath, remotePath, FtpLocalExists.Overwrite, FtpVerify.None, null, cancellationToken);

            if (status == FtpStatus.Success)
            {
                _logger.LogInformation("Downloaded {RemotePath} successfully ({Size} bytes)",
                    remotePath, new FileInfo(localPath).Length);
                return true;
            }
            else
            {
                _logger.LogWarning("Download of {RemotePath} returned status {Status}", remotePath, status);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download {RemotePath}", remotePath);

            // Clean up partial download
            if (File.Exists(localPath))
            {
                try { File.Delete(localPath); } catch { }
            }
            throw;
        }
    }

    public async Task<bool> DeleteFileAsync(
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        _logger.LogInformation("Deleting remote file {RemotePath}", remotePath);

        try
        {
            await _client!.DeleteFile(remotePath, cancellationToken);
            _logger.LogInformation("Deleted remote file {RemotePath}", remotePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete remote file {RemotePath}", remotePath);
            throw;
        }
    }

    public async Task<bool> FileExistsAsync(
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        return await _client!.FileExists(remotePath, cancellationToken);
    }

    public async Task<RemoteFileInfo?> GetFileInfoAsync(
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        try
        {
            var info = await _client!.GetObjectInfo(remotePath, false, cancellationToken);
            if (info == null)
                return null;

            return new RemoteFileInfo
            {
                Name = info.Name,
                FullPath = info.FullName,
                Size = info.Size,
                LastModified = info.Modified,
                IsDirectory = info.Type == FtpObjectType.Directory
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> CreateDirectoryAsync(
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        _logger.LogInformation("Creating remote directory {RemotePath}", remotePath);

        try
        {
            if (!await _client!.DirectoryExists(remotePath, cancellationToken))
            {
                await _client.CreateDirectory(remotePath, true, cancellationToken);
                _logger.LogInformation("Created remote directory {RemotePath}", remotePath);
            }
            else
            {
                _logger.LogDebug("Remote directory already exists: {RemotePath}", remotePath);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create remote directory {RemotePath}", remotePath);
            throw;
        }
    }

    public async Task<bool> UploadFileAsync(
        string localPath,
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        _logger.LogInformation("Uploading {LocalPath} to {RemotePath}", localPath, remotePath);

        try
        {
            // Ensure remote directory exists
            var remoteDir = Path.GetDirectoryName(remotePath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(remoteDir))
            {
                await CreateDirectoryAsync(remoteDir, cancellationToken);
            }

            var status = await _client!.UploadFile(localPath, remotePath, FtpRemoteExists.Overwrite, true, FtpVerify.None, null, cancellationToken);

            if (status == FtpStatus.Success)
            {
                _logger.LogInformation("Uploaded {LocalPath} to {RemotePath} successfully", localPath, remotePath);
                return true;
            }
            else
            {
                _logger.LogWarning("Upload of {LocalPath} returned status {Status}", localPath, status);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload {LocalPath} to {RemotePath}", localPath, remotePath);
            throw;
        }
    }

    private void EnsureConnected()
    {
        if (_client == null || !_client.IsConnected)
        {
            throw new InvalidOperationException("Not connected to FTP server. Call ConnectAsync first.");
        }
    }

    private static string ConvertGlobToRegex(string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return regex;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            if (_client != null)
            {
                if (_client.IsConnected)
                {
                    try { _client.Disconnect().Wait(); } catch { }
                }
                _client.Dispose();
                _client = null;
            }
        }

        _disposed = true;
    }
}
