namespace FileLoading.Models;

// ============================================
// Transfer Configuration
// ============================================

/// <summary>
/// Configuration for a file transfer source.
/// Supports SFTP, FTP, and local file system sources.
/// </summary>
public class TransferSourceConfig
{
    /// <summary>Unique identifier for this source (auto-generated).</summary>
    public int SourceId { get; set; }

    /// <summary>Friendly name for the vendor/source (e.g., "Telstra CDR Feed").</summary>
    public string VendorName { get; set; } = string.Empty;

    /// <summary>File type code (from file_type table).</summary>
    public string? FileTypeCode { get; set; }

    // Connection settings
    /// <summary>Transfer protocol (SFTP, FTP, FileSystem).</summary>
    public TransferProtocol Protocol { get; set; } = TransferProtocol.Sftp;

    /// <summary>Remote host address.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Remote port (default 22 for SFTP, 21 for FTP).</summary>
    public int Port { get; set; } = 22;

    /// <summary>Remote path to monitor.</summary>
    public string RemotePath { get; set; } = "/";

    // Authentication
    /// <summary>Authentication type.</summary>
    public AuthenticationType AuthType { get; set; } = AuthenticationType.Password;

    /// <summary>Username for authentication.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Password (encrypted in database).</summary>
    public string? Password { get; set; }

    /// <summary>Path to certificate file for certificate authentication.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Path to private key file for key-based authentication.</summary>
    public string? PrivateKeyPath { get; set; }

    // File patterns
    /// <summary>Pattern to match files for download (e.g., "*.csv", "CDR_*.txt").</summary>
    public string FileNamePattern { get; set; } = "*.*";

    /// <summary>Pattern to match files that should be skipped.</summary>
    public string? SkipFilePattern { get; set; }

    // Behavior
    /// <summary>Whether to delete files from source after successful download.</summary>
    public bool DeleteAfterDownload { get; set; } = true;

    /// <summary>Whether to compress files when archiving to Processed folder.</summary>
    public bool CompressOnArchive { get; set; } = true;

    /// <summary>Compression method for archiving.</summary>
    public CompressionMethod Compression { get; set; } = CompressionMethod.GZip;

    // Schedule
    /// <summary>CRON schedule expression (e.g., "0 */15 * * * *" for every 15 min).</summary>
    public string? CronSchedule { get; set; }

    /// <summary>Whether this source is enabled for scheduled transfers.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Created timestamp.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Created by user.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Last updated timestamp.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Updated by user.</summary>
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Gets the computed connection URL for display purposes.
    /// Format: protocol://username@host:port/path
    /// </summary>
    public string ConnectionUrl
    {
        get
        {
            var proto = Protocol switch
            {
                TransferProtocol.Sftp => "sftp",
                TransferProtocol.Ftp => "ftp",
                TransferProtocol.FileSystem => "file",
                _ => "unknown"
            };

            if (Protocol == TransferProtocol.FileSystem)
                return $"file://{RemotePath}";

            var userPart = string.IsNullOrEmpty(Username) ? "" : $"{Username}@";
            var portPart = (Protocol == TransferProtocol.Sftp && Port == 22) ||
                          (Protocol == TransferProtocol.Ftp && Port == 21)
                          ? "" : $":{Port}";
            return $"{proto}://{userPart}{Host}{portPart}{RemotePath}";
        }
    }
}

/// <summary>
/// Transfer protocol types.
/// </summary>
public enum TransferProtocol
{
    /// <summary>SSH File Transfer Protocol (secure).</summary>
    Sftp = 1,

    /// <summary>File Transfer Protocol.</summary>
    Ftp = 2,

    /// <summary>Local or network file system.</summary>
    FileSystem = 3
}

/// <summary>
/// Authentication types for remote connections.
/// </summary>
public enum AuthenticationType
{
    /// <summary>Username and password authentication.</summary>
    Password = 1,

    /// <summary>Certificate-based authentication.</summary>
    Certificate = 2,

    /// <summary>Private key authentication.</summary>
    PrivateKey = 3
}

/// <summary>
/// Compression methods for file archiving.
/// </summary>
public enum CompressionMethod
{
    /// <summary>No compression.</summary>
    None = 0,

    /// <summary>GZip compression (.gz).</summary>
    GZip = 1,

    /// <summary>Zip compression (.zip).</summary>
    Zip = 2
}

// ============================================
// Folder Storage Configuration
// ============================================

/// <summary>
/// Storage mode for folder workflow (local disk or FTP server).
/// </summary>
public enum StorageMode
{
    /// <summary>Local file system storage.</summary>
    Local = 1,

    /// <summary>FTP/SFTP remote storage.</summary>
    Ftp = 2
}

/// <summary>
/// Domain-level storage configuration for folder workflow.
/// Determines whether files are stored on local disk or an FTP server.
/// </summary>
public class FolderStorageConfig
{
    /// <summary>Storage configuration ID.</summary>
    public int StorageId { get; set; }

    /// <summary>Storage mode (Local or Ftp).</summary>
    public StorageMode StorageMode { get; set; } = StorageMode.Local;

    /// <summary>Transfer protocol (null when Local).</summary>
    public TransferProtocol? Protocol { get; set; }

    /// <summary>FTP host address (null when Local).</summary>
    public string? Host { get; set; }

    /// <summary>FTP port (null when Local).</summary>
    public int? Port { get; set; }

    /// <summary>Authentication type (null when Local).</summary>
    public AuthenticationType? AuthType { get; set; }

    /// <summary>FTP username (null when Local).</summary>
    public string? Username { get; set; }

    /// <summary>FTP password — masked in API responses (null when Local).</summary>
    public string? Password { get; set; }

    /// <summary>Path to certificate file (null when Local or not using cert auth).</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Path to private key file (null when Local or not using key auth).</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>Base path on FTP server.</summary>
    public string BasePath { get; set; } = "/";

    /// <summary>Local temp path for FTP mode processing.</summary>
    public string? TempLocalPath { get; set; }

    /// <summary>Created timestamp.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Created by user.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Last updated timestamp.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Updated by user.</summary>
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Request to create or update folder storage configuration.
/// </summary>
public class FolderStorageRequest
{
    /// <summary>Storage mode (Local or Ftp).</summary>
    public StorageMode StorageMode { get; set; } = StorageMode.Local;

    /// <summary>Transfer protocol (required when Ftp).</summary>
    public TransferProtocol? Protocol { get; set; }

    /// <summary>FTP host address (required when Ftp).</summary>
    public string? Host { get; set; }

    /// <summary>FTP port.</summary>
    public int? Port { get; set; }

    /// <summary>Authentication type.</summary>
    public AuthenticationType? AuthType { get; set; }

    /// <summary>FTP username.</summary>
    public string? Username { get; set; }

    /// <summary>FTP password (will be encrypted).</summary>
    public string? Password { get; set; }

    /// <summary>Path to certificate file.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Path to private key file.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>Base path on FTP server.</summary>
    public string BasePath { get; set; } = "/";

    /// <summary>Local temp path for FTP mode processing.</summary>
    public string? TempLocalPath { get; set; }
}

/// <summary>
/// Result of folder creation operation.
/// </summary>
public class FolderCreateResult
{
    /// <summary>Whether all folders were created successfully.</summary>
    public bool AllCreated { get; set; }

    /// <summary>Status of each folder creation.</summary>
    public List<FolderCreateStatus> Folders { get; set; } = new();
}

/// <summary>
/// Status of a single folder creation.
/// </summary>
public class FolderCreateStatus
{
    /// <summary>Folder name (Transfer, Processing, etc.).</summary>
    public string FolderName { get; set; } = string.Empty;

    /// <summary>Full path of the folder.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Whether the folder was created.</summary>
    public bool Created { get; set; }

    /// <summary>Whether the folder already existed.</summary>
    public bool AlreadyExisted { get; set; }

    /// <summary>Error message if creation failed.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Default folder paths for a domain/file-type combination.
/// </summary>
public class FolderDefaultsResponse
{
    /// <summary>File type code.</summary>
    public string? FileTypeCode { get; set; }

    /// <summary>Base path prefix (non-editable by users).</summary>
    public string BasePath { get; set; } = string.Empty;

    /// <summary>Default transfer folder path.</summary>
    public string TransferFolder { get; set; } = string.Empty;

    /// <summary>Default processing folder path.</summary>
    public string ProcessingFolder { get; set; } = string.Empty;

    /// <summary>Default processed folder path.</summary>
    public string ProcessedFolder { get; set; } = string.Empty;

    /// <summary>Default errors folder path.</summary>
    public string ErrorsFolder { get; set; } = string.Empty;

    /// <summary>Default skipped folder path.</summary>
    public string SkippedFolder { get; set; } = string.Empty;

    /// <summary>Default example folder path.</summary>
    public string ExampleFolder { get; set; } = string.Empty;
}

/// <summary>
/// Request to create or update folder workflow configuration.
/// For Local storage: only FileTypeCode is needed — paths are auto-generated from config.
/// For FTP storage: full folder paths on the FTP server are accepted.
/// </summary>
public class FolderWorkflowRequest
{
    /// <summary>File type code (null = default for domain).</summary>
    public string? FileTypeCode { get; set; }

    /// <summary>Transfer folder path on FTP server (ignored for Local storage).</summary>
    public string? TransferFolder { get; set; }

    /// <summary>Processing folder path on FTP server (ignored for Local storage).</summary>
    public string? ProcessingFolder { get; set; }

    /// <summary>Processed folder path on FTP server (ignored for Local storage).</summary>
    public string? ProcessedFolder { get; set; }

    /// <summary>Errors folder path on FTP server (ignored for Local storage).</summary>
    public string? ErrorsFolder { get; set; }

    /// <summary>Skipped folder path on FTP server (ignored for Local storage).</summary>
    public string? SkippedFolder { get; set; }

    /// <summary>Example folder path on FTP server (ignored for Local storage).</summary>
    public string? ExampleFolder { get; set; }
}

// ============================================
// Folder Workflow Configuration
// ============================================

/// <summary>
/// Folder workflow configuration per domain/file-type.
/// Defines the folder structure for the file processing workflow.
/// </summary>
public class FolderWorkflowConfig
{
    /// <summary>Configuration ID.</summary>
    public int ConfigId { get; set; }

    /// <summary>File type code (null = default for domain).</summary>
    public string? FileTypeCode { get; set; }

    /// <summary>Folder where files are initially transferred to.</summary>
    public string TransferFolder { get; set; } = string.Empty;

    /// <summary>Folder where files are moved during processing.</summary>
    public string ProcessingFolder { get; set; } = string.Empty;

    /// <summary>Folder where successfully processed files are archived.</summary>
    public string ProcessedFolder { get; set; } = string.Empty;

    /// <summary>Folder where files with errors are moved.</summary>
    public string ErrorsFolder { get; set; } = string.Empty;

    /// <summary>Folder where skipped files are moved.</summary>
    public string SkippedFolder { get; set; } = string.Empty;

    /// <summary>Folder where example files are stored.</summary>
    public string ExampleFolder { get; set; } = string.Empty;

    /// <summary>Created timestamp.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Created by user.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Last updated timestamp.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Updated by user.</summary>
    public string UpdatedBy { get; set; } = string.Empty;
}

// ============================================
// Downloaded File Tracking
// ============================================

/// <summary>
/// Record of a downloaded file (for vendors where we can't delete after download).
/// Used to prevent re-downloading the same file.
/// </summary>
public class DownloadedFileRecord
{
    /// <summary>Download record ID.</summary>
    public int DownloadId { get; set; }

    /// <summary>Source ID the file was downloaded from.</summary>
    public int SourceId { get; set; }

    /// <summary>Original remote file name.</summary>
    public string RemoteFileName { get; set; } = string.Empty;

    /// <summary>Full remote file path.</summary>
    public string? RemoteFilePath { get; set; }

    /// <summary>File size in bytes.</summary>
    public long FileSize { get; set; }

    /// <summary>Remote file's last modified date.</summary>
    public DateTime RemoteModifiedDate { get; set; }

    /// <summary>File hash (MD5/SHA256) for duplicate detection.</summary>
    public string? FileHash { get; set; }

    /// <summary>When the file was downloaded.</summary>
    public DateTime DownloadedAt { get; set; }
}

// ============================================
// File Transfer Records
// ============================================

/// <summary>
/// Transfer operation record for tracking file progress through the workflow.
/// </summary>
public class FileTransferRecord
{
    /// <summary>Transfer record ID.</summary>
    public int TransferId { get; set; }

    /// <summary>Source ID this transfer originated from.</summary>
    public int? SourceId { get; set; }

    /// <summary>FK to nt_file after file creation.</summary>
    public int? NtFileNum { get; set; }

    /// <summary>File name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Current transfer status.</summary>
    public TransferStatus Status { get; set; }

    /// <summary>Source path (remote or local).</summary>
    public string? SourcePath { get; set; }

    /// <summary>Destination path (local).</summary>
    public string? DestinationPath { get; set; }

    /// <summary>Current folder in workflow (Transfer, Processing, Processed, etc.).</summary>
    public string? CurrentFolder { get; set; }

    /// <summary>File size in bytes.</summary>
    public long? FileSize { get; set; }

    /// <summary>When transfer started.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>When transfer completed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Error message if transfer failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Number of retry attempts.</summary>
    public int RetryCount { get; set; }

    /// <summary>User who created this transfer.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>When this record was created.</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Transfer status enumeration.
/// </summary>
public enum TransferStatus
{
    /// <summary>Transfer is pending.</summary>
    Pending = 0,

    /// <summary>File is being downloaded.</summary>
    Downloading = 1,

    /// <summary>File has been downloaded to Transfer folder.</summary>
    Downloaded = 2,

    /// <summary>File is being processed.</summary>
    Processing = 3,

    /// <summary>File has been successfully processed.</summary>
    Processed = 4,

    /// <summary>Transfer or processing encountered an error.</summary>
    Error = 5,

    /// <summary>File was skipped (matched skip pattern or manually skipped).</summary>
    Skipped = 6
}

// ============================================
// Activity Logging
// ============================================

/// <summary>
/// User activity log entry for audit trail.
/// </summary>
public class FileActivityLog
{
    /// <summary>Activity log ID.</summary>
    public long ActivityId { get; set; }

    /// <summary>FK to nt_file if applicable.</summary>
    public int? NtFileNum { get; set; }

    /// <summary>FK to transfer record if applicable.</summary>
    public int? TransferId { get; set; }

    /// <summary>File name for reference.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Type of activity performed.</summary>
    public FileActivityType ActivityType { get; set; }

    /// <summary>Human-readable description of the activity.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Additional details as JSON.</summary>
    public string? Details { get; set; }

    /// <summary>User who performed the activity.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>When the activity occurred.</summary>
    public DateTime ActivityAt { get; set; }
}

/// <summary>
/// Types of file activities that can be logged.
/// </summary>
public enum FileActivityType
{
    /// <summary>File downloaded from remote source.</summary>
    Downloaded = 1,

    /// <summary>File moved to Processing folder.</summary>
    MovedToProcessing = 2,

    /// <summary>File processing started.</summary>
    ProcessingStarted = 3,

    /// <summary>File processing completed successfully.</summary>
    ProcessingCompleted = 4,

    /// <summary>File processing failed.</summary>
    ProcessingFailed = 5,

    /// <summary>File moved to Skipped folder.</summary>
    MovedToSkipped = 6,

    /// <summary>File moved to Errors folder.</summary>
    MovedToErrors = 7,

    /// <summary>File moved to Processed folder.</summary>
    MovedToProcessed = 8,

    /// <summary>File deleted.</summary>
    FileDeleted = 9,

    /// <summary>Loaded file data unloaded/reversed.</summary>
    FileUnloaded = 10,

    /// <summary>Sequence number skipped.</summary>
    SequenceSkipped = 11,

    /// <summary>File manually triggered for download.</summary>
    ManualDownload = 12,

    /// <summary>File downloaded to user's browser.</summary>
    BrowserDownload = 13,

    /// <summary>Transfer source configuration created.</summary>
    SourceCreated = 14,

    /// <summary>Transfer source configuration modified.</summary>
    SourceModified = 15,

    /// <summary>Transfer source configuration deleted.</summary>
    SourceDeleted = 16
}

// ============================================
// Dashboard and Query Models
// ============================================

/// <summary>
/// Dashboard summary for file management UI.
/// </summary>
public class FileManagementDashboard
{
    /// <summary>Count of files in Transfer folder.</summary>
    public int FilesInTransfer { get; set; }

    /// <summary>Count of files in Processing folder.</summary>
    public int FilesInProcessing { get; set; }

    /// <summary>Count of files processed today.</summary>
    public int FilesProcessedToday { get; set; }

    /// <summary>Count of files with errors.</summary>
    public int FilesWithErrors { get; set; }

    /// <summary>Count of files skipped.</summary>
    public int FilesSkipped { get; set; }

    /// <summary>Status of each transfer source.</summary>
    public List<TransferSourceStatus> SourceStatuses { get; set; } = new();
}

/// <summary>
/// Status summary for a transfer source.
/// </summary>
public class TransferSourceStatus
{
    /// <summary>Source ID.</summary>
    public int SourceId { get; set; }

    /// <summary>Friendly name for the vendor/source.</summary>
    public string VendorName { get; set; } = string.Empty;

    /// <summary>File type code.</summary>
    public string? FileTypeCode { get; set; }

    /// <summary>Whether the source is enabled.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Last successful transfer time.</summary>
    public DateTime? LastTransferAt { get; set; }

    /// <summary>Files transferred today.</summary>
    public int FilesTransferredToday { get; set; }

    /// <summary>Last error message if any.</summary>
    public string? LastError { get; set; }
}

/// <summary>
/// Filter criteria for listing files.
/// </summary>
public class FileListFilter
{
    /// <summary>Filter by file type code.</summary>
    public string? FileTypeCode { get; set; }

    /// <summary>Filter by current folder (Transfer, Processing, etc.).</summary>
    public string? CurrentFolder { get; set; }

    /// <summary>Filter by transfer status.</summary>
    public TransferStatus? Status { get; set; }

    /// <summary>Filter by date range start.</summary>
    public DateTime? FromDate { get; set; }

    /// <summary>Filter by date range end.</summary>
    public DateTime? ToDate { get; set; }

    /// <summary>Search by file name.</summary>
    public string? FileNameSearch { get; set; }

    /// <summary>Maximum records to return.</summary>
    public int MaxRecords { get; set; } = 100;
}

/// <summary>
/// File with combined status information for UI display.
/// </summary>
public class FileWithStatus
{
    /// <summary>Transfer record ID.</summary>
    public int TransferId { get; set; }

    /// <summary>NT file number (if file has been created).</summary>
    public int? NtFileNum { get; set; }

    /// <summary>File name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>File type code.</summary>
    public string? FileTypeCode { get; set; }

    /// <summary>Current folder.</summary>
    public string CurrentFolder { get; set; } = string.Empty;

    /// <summary>Transfer status.</summary>
    public TransferStatus Status { get; set; }

    /// <summary>Status description.</summary>
    public string StatusDescription { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long? FileSize { get; set; }

    /// <summary>When the file was created/downloaded.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When processing completed (if applicable).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Error message if any.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Source ID if transferred from remote.</summary>
    public int? SourceId { get; set; }
}

/// <summary>
/// Response for file list with pagination info.
/// </summary>
public class FileListWithStatusResponse
{
    /// <summary>List of files.</summary>
    public List<FileWithStatus> Items { get; set; } = new();

    /// <summary>Total count (for pagination).</summary>
    public int TotalCount { get; set; }
}

// ============================================
// Request/Response Models for API
// ============================================

/// <summary>
/// Request to create or update a transfer source.
/// </summary>
public class TransferSourceRequest
{
    /// <summary>Source ID (set from route for updates, ignored for creates).</summary>
    public int SourceId { get; set; }

    /// <summary>Friendly name for the vendor/source (e.g., "Telstra CDR Feed").</summary>
    public string VendorName { get; set; } = string.Empty;

    /// <summary>File type code.</summary>
    public string? FileTypeCode { get; set; }

    /// <summary>Transfer protocol.</summary>
    public TransferProtocol Protocol { get; set; } = TransferProtocol.Sftp;

    /// <summary>Remote host address.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Remote port.</summary>
    public int Port { get; set; } = 22;

    /// <summary>Remote path to monitor.</summary>
    public string RemotePath { get; set; } = "/";

    /// <summary>Authentication type.</summary>
    public AuthenticationType AuthType { get; set; } = AuthenticationType.Password;

    /// <summary>Username.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Password (will be encrypted).</summary>
    public string? Password { get; set; }

    /// <summary>Path to certificate file.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Path to private key file.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>File name pattern.</summary>
    public string FileNamePattern { get; set; } = "*.*";

    /// <summary>Skip file pattern.</summary>
    public string? SkipFilePattern { get; set; }

    /// <summary>Delete after download.</summary>
    public bool DeleteAfterDownload { get; set; } = true;

    /// <summary>Compress on archive.</summary>
    public bool CompressOnArchive { get; set; } = true;

    /// <summary>Compression method.</summary>
    public CompressionMethod Compression { get; set; } = CompressionMethod.GZip;

    /// <summary>CRON schedule.</summary>
    public string? CronSchedule { get; set; }

    /// <summary>Whether enabled.</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Request to move a file to a specific folder.
/// </summary>
public class MoveFileRequest
{
    /// <summary>Target folder (Transfer, Processing, Processed, Errors, Skipped).</summary>
    public string TargetFolder { get; set; } = string.Empty;

    /// <summary>Optional reason for the move.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Request to skip a sequence number.
/// </summary>
public class SequenceSkipRequest
{
    /// <summary>Sequence number to skip to.</summary>
    public int SkipToSequence { get; set; }

    /// <summary>Reason for skipping.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Response for transfer fetch operation.
/// </summary>
public class TransferFetchResponse
{
    /// <summary>Number of files found on remote.</summary>
    public int FilesFound { get; set; }

    /// <summary>Number of files downloaded.</summary>
    public int FilesDownloaded { get; set; }

    /// <summary>Number of files skipped (already downloaded or matched skip pattern).</summary>
    public int FilesSkipped { get; set; }

    /// <summary>Number of files that failed to download.</summary>
    public int FilesFailed { get; set; }

    /// <summary>Transfer records for downloaded files.</summary>
    public List<FileTransferRecord> TransferRecords { get; set; } = new();

    /// <summary>Errors encountered during fetch.</summary>
    public List<string> Errors { get; set; } = new();
}
