using FileLoading.Models;

namespace FileLoading.Transfer;

/// <summary>
/// Interface for file transfer clients (SFTP, FTP, FileSystem).
/// </summary>
public interface ITransferClient : IDisposable
{
    /// <summary>
    /// Connect to the remote source.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the remote source.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Check if connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// List files in the remote directory matching the pattern.
    /// </summary>
    /// <param name="remotePath">Remote directory path</param>
    /// <param name="filePattern">File name pattern (e.g., "*.csv")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of remote files</returns>
    Task<List<RemoteFileInfo>> ListFilesAsync(
        string remotePath,
        string filePattern,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Download a file from the remote source.
    /// </summary>
    /// <param name="remotePath">Full remote file path</param>
    /// <param name="localPath">Local destination path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successful</returns>
    Task<bool> DownloadFileAsync(
        string remotePath,
        string localPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a file from the remote source.
    /// </summary>
    /// <param name="remotePath">Full remote file path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successful</returns>
    Task<bool> DeleteFileAsync(
        string remotePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a remote file exists.
    /// </summary>
    /// <param name="remotePath">Full remote file path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if file exists</returns>
    Task<bool> FileExistsAsync(
        string remotePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get file information.
    /// </summary>
    /// <param name="remotePath">Full remote file path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>File information</returns>
    Task<RemoteFileInfo?> GetFileInfoAsync(
        string remotePath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a remote file.
/// </summary>
public class RemoteFileInfo
{
    /// <summary>File name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Full path on remote.</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>Last modified date.</summary>
    public DateTime LastModified { get; set; }

    /// <summary>Whether this is a directory.</summary>
    public bool IsDirectory { get; set; }
}

/// <summary>
/// Factory for creating transfer clients.
/// </summary>
public interface ITransferClientFactory
{
    /// <summary>
    /// Create a transfer client for the given source configuration.
    /// </summary>
    /// <param name="config">Transfer source configuration</param>
    /// <returns>Transfer client instance</returns>
    ITransferClient CreateClient(TransferSourceConfig config);
}
