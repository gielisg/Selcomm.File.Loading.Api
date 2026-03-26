using Selcomm.Data.Common;
using FileLoading.Models;

namespace FileLoading.Interfaces;

/// <summary>
/// Service interface for file transfer operations (FTP/SFTP/FileSystem).
/// Handles downloading files from remote sources and managing transfer records.
/// </summary>
public interface IFileTransferService
{
    // ============================================
    // Transfer Operations
    // ============================================

    /// <summary>
    /// Fetch files from a configured transfer source.
    /// Downloads matching files to the Transfer folder.
    /// </summary>
    /// <param name="sourceId">Transfer source identifier</param>
    /// <param name="context">Security context</param>
    /// <returns>List of transfer records for downloaded files</returns>
    Task<DataResult<TransferFetchResponse>> FetchFilesFromSourceAsync(
        int sourceId,
        SecurityContext context);

    /// <summary>
    /// Transfer a single file from the Transfer folder to the Processing folder.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <param name="context">Security context</param>
    /// <returns>Updated transfer record</returns>
    Task<DataResult<FileTransferRecord>> TransferToProcessingAsync(
        int transferId,
        SecurityContext context);

    /// <summary>
    /// Move a file to a specific workflow folder.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <param name="targetFolder">Target folder name (Transfer, Processing, Processed, Errors, Skipped)</param>
    /// <param name="compress">Whether to compress the file (only for Processed/Skipped)</param>
    /// <param name="context">Security context</param>
    /// <returns>Updated transfer record</returns>
    Task<DataResult<FileTransferRecord>> MoveToFolderAsync(
        int transferId,
        string targetFolder,
        bool compress,
        SecurityContext context,
        string? fileTypeCode = null);

    // ============================================
    // Compression Operations
    // ============================================

    /// <summary>
    /// Compress a file using the configured compression method.
    /// </summary>
    /// <param name="filePath">Path to the file to compress</param>
    /// <param name="method">Compression method to use</param>
    /// <returns>Path to the compressed file</returns>
    Task<DataResult<string>> CompressFileAsync(string filePath, CompressionMethod method);

    /// <summary>
    /// Decompress a file.
    /// </summary>
    /// <param name="compressedFilePath">Path to the compressed file</param>
    /// <param name="destinationFolder">Folder to extract to</param>
    /// <returns>Path to the decompressed file</returns>
    Task<DataResult<string>> DecompressFileAsync(string compressedFilePath, string destinationFolder);

    // ============================================
    // Query Operations
    // ============================================

    /// <summary>
    /// List transfer records with filtering.
    /// </summary>
    /// <param name="sourceId">Optional filter by source ID</param>
    /// <param name="status">Optional filter by status</param>
    /// <param name="currentFolder">Optional filter by current folder</param>
    /// <param name="maxRecords">Maximum records to return</param>
    /// <param name="context">Security context</param>
    /// <returns>List of transfer records</returns>
    Task<DataResult<List<FileTransferRecord>>> ListTransfersAsync(
        int? sourceId,
        TransferStatus? status,
        string? currentFolder,
        int maxRecords,
        SecurityContext context);

    /// <summary>
    /// Get a specific transfer record.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <param name="context">Security context</param>
    /// <returns>Transfer record</returns>
    Task<DataResult<FileTransferRecord>> GetTransferAsync(
        int transferId,
        SecurityContext context);

    // ============================================
    // Source Configuration
    // ============================================

    /// <summary>
    /// Get all transfer source configurations.
    /// </summary>
    /// <param name="context">Security context</param>
    /// <returns>List of transfer source configurations</returns>
    Task<DataResult<List<TransferSourceConfig>>> GetSourceConfigsAsync(
        SecurityContext context);

    /// <summary>
    /// Get a specific transfer source configuration.
    /// </summary>
    /// <param name="sourceId">Source identifier</param>
    /// <param name="context">Security context</param>
    /// <returns>Transfer source configuration</returns>
    Task<DataResult<TransferSourceConfig>> GetSourceConfigAsync(
        int sourceId,
        SecurityContext context);

    /// <summary>
    /// Create or update a transfer source configuration.
    /// </summary>
    /// <param name="request">Source configuration request</param>
    /// <param name="context">Security context</param>
    /// <returns>Created/updated source configuration</returns>
    Task<DataResult<TransferSourceConfig>> SaveSourceConfigAsync(
        TransferSourceRequest request,
        SecurityContext context);

    /// <summary>
    /// Delete a transfer source configuration.
    /// </summary>
    /// <param name="sourceId">Source identifier</param>
    /// <param name="context">Security context</param>
    Task<DataResult<bool>> DeleteSourceConfigAsync(
        int sourceId,
        SecurityContext context);

    // ============================================
    // Folder Configuration
    // ============================================

    /// <summary>
    /// Get folder workflow configuration for a file-type.
    /// </summary>
    /// <param name="fileTypeCode">Optional file type code</param>
    /// <param name="context">Security context</param>
    /// <returns>Folder configuration</returns>
    Task<DataResult<FolderWorkflowConfig>> GetFolderConfigAsync(
        string? fileTypeCode,
        SecurityContext context);

    /// <summary>
    /// Save folder workflow configuration. Paths are always derived from the file type code
    /// and current storage mode — no user-supplied paths.
    /// </summary>
    /// <param name="fileTypeCode">File type code (null for domain-level default)</param>
    /// <param name="context">Security context</param>
    /// <returns>Saved folder configuration</returns>
    Task<DataResult<FolderWorkflowConfig>> SaveFolderConfigAsync(
        string? fileTypeCode,
        SecurityContext context);

    // ============================================
    // FTP Server Configuration
    // ============================================

    /// <summary>
    /// Get all FTP server entities.
    /// </summary>
    Task<DataResult<List<FtpServer>>> GetFtpServersAsync(SecurityContext context);

    /// <summary>
    /// Get a specific FTP server entity.
    /// </summary>
    Task<DataResult<FtpServer>> GetFtpServerAsync(int serverId, SecurityContext context);

    /// <summary>
    /// Create a new FTP server entity (not activated automatically).
    /// </summary>
    Task<DataResult<FtpServer>> CreateFtpServerAsync(FtpServerRequest request, SecurityContext context);

    /// <summary>
    /// Update an FTP server entity. Immutable fields blocked if server is locked.
    /// </summary>
    Task<DataResult<FtpServer>> UpdateFtpServerAsync(int serverId, FtpServerRequest request, SecurityContext context);

    /// <summary>
    /// Delete an FTP server entity. Blocked if locked (referenced by transfers).
    /// </summary>
    Task<DataResult<bool>> DeleteFtpServerAsync(int serverId, SecurityContext context);

    /// <summary>
    /// Activate an FTP server as the current storage destination (deactivates all others).
    /// </summary>
    Task<DataResult<FtpServer>> ActivateFtpServerAsync(int serverId, SecurityContext context);

    /// <summary>
    /// Deactivate an FTP server, reverting the domain to local storage mode.
    /// </summary>
    Task<DataResult<bool>> DeactivateFtpServerAsync(int serverId, SecurityContext context);

    /// <summary>
    /// Test FTP connection with provided configuration (without saving).
    /// </summary>
    Task<DataResult<bool>> TestFtpConnectionAsync(FtpServerRequest request, SecurityContext context);

    /// <summary>
    /// Get the currently active FTP server, or null if in local mode.
    /// </summary>
    Task<DataResult<FtpServer?>> GetActiveFtpServerAsync(SecurityContext context);

    /// <summary>
    /// Get default folder paths for a file-type combination.
    /// Uses SecurityContext.Domain for path construction.
    /// </summary>
    /// <param name="fileType">File type code</param>
    /// <param name="context">Security context</param>
    /// <returns>Default folder paths</returns>
    Task<DataResult<FolderDefaultsResponse>> GetDefaultFolderPathsAsync(
        string? fileType,
        SecurityContext context);

    /// <summary>
    /// Create all 5 workflow folders for a file-type (local or FTP).
    /// Uses SecurityContext.Domain for path construction.
    /// </summary>
    /// <param name="fileType">File type code</param>
    /// <param name="context">Security context</param>
    /// <returns>Folder creation result</returns>
    Task<DataResult<FolderCreateResult>> CreateFoldersAsync(
        string? fileType,
        SecurityContext context);

    // ============================================
    // Downloaded File Tracking
    // ============================================

    /// <summary>
    /// Check if a file has already been downloaded.
    /// Used for sources where we can't delete after download.
    /// </summary>
    /// <param name="sourceId">Source identifier</param>
    /// <param name="fileName">Remote file name</param>
    /// <param name="modifiedDate">Remote file modified date</param>
    /// <param name="fileSize">Remote file size</param>
    /// <returns>True if file has been downloaded</returns>
    Task<bool> IsFileAlreadyDownloadedAsync(
        int sourceId,
        string fileName,
        DateTime modifiedDate,
        long fileSize);

    /// <summary>
    /// Record a downloaded file.
    /// </summary>
    /// <param name="record">Downloaded file record</param>
    Task RecordDownloadedFileAsync(DownloadedFileRecord record);

    // ============================================
    // Connection Testing
    // ============================================

    /// <summary>
    /// Test connection to a transfer source.
    /// </summary>
    /// <param name="sourceId">Source identifier</param>
    /// <param name="context">Security context</param>
    /// <returns>True if connection successful, error message otherwise</returns>
    Task<DataResult<bool>> TestConnectionAsync(
        int sourceId,
        SecurityContext context);

    /// <summary>
    /// Test connection with specific parameters (without saving configuration).
    /// </summary>
    /// <param name="request">Source configuration to test</param>
    /// <param name="context">Security context</param>
    /// <returns>True if connection successful, error message otherwise</returns>
    Task<DataResult<bool>> TestConnectionAsync(
        TransferSourceRequest request,
        SecurityContext context);
}
