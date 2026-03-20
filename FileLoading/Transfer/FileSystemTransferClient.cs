using Microsoft.Extensions.Logging;
using FileLoading.Models;

namespace FileLoading.Transfer;

/// <summary>
/// File system transfer client for local and network paths.
/// </summary>
public class FileSystemTransferClient : ITransferClient
{
    private readonly TransferSourceConfig _config;
    private readonly ILogger<FileSystemTransferClient> _logger;
    private bool _connected;
    private bool _disposed;

    public FileSystemTransferClient(TransferSourceConfig config, ILogger<FileSystemTransferClient> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsConnected => _connected;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Connecting to file system path {Path}", _config.RemotePath);

        // Verify the path exists
        if (!Directory.Exists(_config.RemotePath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {_config.RemotePath}");
        }

        _connected = true;
        _logger.LogInformation("Connected to file system path {Path}", _config.RemotePath);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _connected = false;
        return Task.CompletedTask;
    }

    public Task<List<RemoteFileInfo>> ListFilesAsync(
        string remotePath,
        string filePattern,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        _logger.LogDebug("Listing files in {RemotePath} with pattern {Pattern}", remotePath, filePattern);

        var files = new List<RemoteFileInfo>();

        if (!Directory.Exists(remotePath))
        {
            _logger.LogWarning("Directory not found: {RemotePath}", remotePath);
            return Task.FromResult(files);
        }

        var searchPattern = string.IsNullOrEmpty(filePattern) ? "*.*" : filePattern;
        var fileInfos = new DirectoryInfo(remotePath).GetFiles(searchPattern);

        foreach (var fileInfo in fileInfos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            files.Add(new RemoteFileInfo
            {
                Name = fileInfo.Name,
                FullPath = fileInfo.FullName,
                Size = fileInfo.Length,
                LastModified = fileInfo.LastWriteTime,
                IsDirectory = false
            });
        }

        _logger.LogInformation("Found {Count} files matching pattern in {RemotePath}", files.Count, remotePath);
        return Task.FromResult(files);
    }

    public Task<bool> DownloadFileAsync(
        string remotePath,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        _logger.LogInformation("Copying {RemotePath} to {LocalPath}", remotePath, localPath);

        try
        {
            // Ensure local directory exists
            var localDir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
            {
                Directory.CreateDirectory(localDir);
            }

            // Copy file (move would be more efficient but we want to mirror FTP behavior)
            File.Copy(remotePath, localPath, overwrite: true);

            _logger.LogInformation("Copied {RemotePath} successfully ({Size} bytes)",
                remotePath, new FileInfo(localPath).Length);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy {RemotePath}", remotePath);

            // Clean up partial copy
            if (File.Exists(localPath))
            {
                try { File.Delete(localPath); } catch { }
            }
            throw;
        }
    }

    public Task<bool> DeleteFileAsync(
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        _logger.LogInformation("Deleting file {RemotePath}", remotePath);

        try
        {
            if (File.Exists(remotePath))
            {
                File.Delete(remotePath);
                _logger.LogInformation("Deleted file {RemotePath}", remotePath);
                return Task.FromResult(true);
            }
            else
            {
                _logger.LogWarning("File not found for deletion: {RemotePath}", remotePath);
                return Task.FromResult(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file {RemotePath}", remotePath);
            throw;
        }
    }

    public Task<bool> FileExistsAsync(
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        return Task.FromResult(File.Exists(remotePath));
    }

    public Task<RemoteFileInfo?> GetFileInfoAsync(
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (!File.Exists(remotePath))
            return Task.FromResult<RemoteFileInfo?>(null);

        var fileInfo = new FileInfo(remotePath);
        return Task.FromResult<RemoteFileInfo?>(new RemoteFileInfo
        {
            Name = fileInfo.Name,
            FullPath = fileInfo.FullName,
            Size = fileInfo.Length,
            LastModified = fileInfo.LastWriteTime,
            IsDirectory = false
        });
    }

    private void EnsureConnected()
    {
        if (!_connected)
        {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }
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
            _connected = false;
        }

        _disposed = true;
    }
}
