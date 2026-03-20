using Microsoft.Extensions.Logging;
using Renci.SshNet;
using FileLoading.Models;

namespace FileLoading.Transfer;

/// <summary>
/// SFTP transfer client implementation using SSH.NET.
/// </summary>
public class SftpTransferClient : ITransferClient
{
    private readonly TransferSourceConfig _config;
    private readonly ILogger<SftpTransferClient> _logger;
    private SftpClient? _client;
    private bool _disposed;

    public SftpTransferClient(TransferSourceConfig config, ILogger<SftpTransferClient> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsConnected => _client?.IsConnected ?? false;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client?.IsConnected == true)
            return;

        _logger.LogInformation("Connecting to SFTP server {Host}:{Port}", _config.Host, _config.Port);

        var connectionInfo = CreateConnectionInfo();
        _client = new SftpClient(connectionInfo);

        await Task.Run(() => _client.Connect(), cancellationToken);

        _logger.LogInformation("Connected to SFTP server {Host}", _config.Host);
    }

    public Task DisconnectAsync()
    {
        if (_client?.IsConnected == true)
        {
            _logger.LogInformation("Disconnecting from SFTP server {Host}", _config.Host);
            _client.Disconnect();
        }
        return Task.CompletedTask;
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

        var items = await Task.Run(() => _client!.ListDirectory(remotePath), cancellationToken);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.IsDirectory || item.Name == "." || item.Name == "..")
                continue;

            if (System.Text.RegularExpressions.Regex.IsMatch(item.Name, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                files.Add(new RemoteFileInfo
                {
                    Name = item.Name,
                    FullPath = CombinePath(remotePath, item.Name),
                    Size = item.Length,
                    LastModified = item.LastWriteTime,
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

            using var fileStream = File.Create(localPath);
            await Task.Run(() => _client!.DownloadFile(remotePath, fileStream), cancellationToken);

            _logger.LogInformation("Downloaded {RemotePath} successfully ({Size} bytes)",
                remotePath, new FileInfo(localPath).Length);
            return true;
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
            await Task.Run(() => _client!.DeleteFile(remotePath), cancellationToken);
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

        return await Task.Run(() => _client!.Exists(remotePath), cancellationToken);
    }

    public async Task<RemoteFileInfo?> GetFileInfoAsync(
        string remotePath,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        try
        {
            var attrs = await Task.Run(() => _client!.GetAttributes(remotePath), cancellationToken);
            return new RemoteFileInfo
            {
                Name = Path.GetFileName(remotePath),
                FullPath = remotePath,
                Size = attrs.Size,
                LastModified = attrs.LastWriteTime,
                IsDirectory = attrs.IsDirectory
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
            var exists = await Task.Run(() => _client!.Exists(remotePath), cancellationToken);
            if (!exists)
            {
                // Create directories recursively
                var parts = remotePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var current = "";
                foreach (var part in parts)
                {
                    current += "/" + part;
                    var partExists = await Task.Run(() => _client!.Exists(current), cancellationToken);
                    if (!partExists)
                    {
                        await Task.Run(() => _client!.CreateDirectory(current), cancellationToken);
                    }
                }
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
            var remoteDir = remotePath.Contains('/') ? remotePath[..remotePath.LastIndexOf('/')] : "/";
            if (!string.IsNullOrEmpty(remoteDir))
            {
                await CreateDirectoryAsync(remoteDir, cancellationToken);
            }

            using var fileStream = File.OpenRead(localPath);
            await Task.Run(() => _client!.UploadFile(fileStream, remotePath, true), cancellationToken);

            _logger.LogInformation("Uploaded {LocalPath} to {RemotePath} successfully", localPath, remotePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload {LocalPath} to {RemotePath}", localPath, remotePath);
            throw;
        }
    }

    private Renci.SshNet.ConnectionInfo CreateConnectionInfo()
    {
        var authMethods = new List<AuthenticationMethod>();

        switch (_config.AuthType)
        {
            case AuthenticationType.Password:
                if (!string.IsNullOrEmpty(_config.Password))
                {
                    authMethods.Add(new PasswordAuthenticationMethod(_config.Username, _config.Password));
                }
                break;

            case AuthenticationType.PrivateKey:
                if (!string.IsNullOrEmpty(_config.PrivateKeyPath) && File.Exists(_config.PrivateKeyPath))
                {
                    var keyFile = new PrivateKeyFile(_config.PrivateKeyPath);
                    authMethods.Add(new PrivateKeyAuthenticationMethod(_config.Username, keyFile));
                }
                break;

            case AuthenticationType.Certificate:
                // Certificate auth typically uses key-based auth with the cert as the key
                if (!string.IsNullOrEmpty(_config.CertificatePath) && File.Exists(_config.CertificatePath))
                {
                    var keyFile = new PrivateKeyFile(_config.CertificatePath);
                    authMethods.Add(new PrivateKeyAuthenticationMethod(_config.Username, keyFile));
                }
                break;
        }

        if (authMethods.Count == 0)
        {
            throw new InvalidOperationException($"No valid authentication method configured for source {_config.SourceId}");
        }

        return new Renci.SshNet.ConnectionInfo(_config.Host, _config.Port, _config.Username, authMethods.ToArray());
    }

    private void EnsureConnected()
    {
        if (_client == null || !_client.IsConnected)
        {
            throw new InvalidOperationException("Not connected to SFTP server. Call ConnectAsync first.");
        }
    }

    private static string ConvertGlobToRegex(string pattern)
    {
        // Convert glob pattern to regex
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return regex;
    }

    private static string CombinePath(string basePath, string fileName)
    {
        // Use forward slash for SFTP paths
        basePath = basePath.TrimEnd('/');
        return $"{basePath}/{fileName}";
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
                    try { _client.Disconnect(); } catch { }
                }
                _client.Dispose();
                _client = null;
            }
        }

        _disposed = true;
    }
}
