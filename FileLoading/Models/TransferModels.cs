namespace FileLoading.Models;

// ============================================
// Transfer Configuration
// ============================================

/// <summary>
/// Configuration for a file transfer source.
/// Supports SFTP, FTP, and local file system sources for automated or manual file retrieval.
/// </summary>
public class TransferSourceConfig
{
    /// <summary>Unique identifier for this source (auto-generated).</summary>
    /// <example>1</example>
    public int SourceId { get; set; }

    /// <summary>Friendly name for the vendor/source.</summary>
    /// <example>Telstra CDR Feed</example>
    public string VendorName { get; set; } = string.Empty;

    /// <summary>File type code (from file_type table).</summary>
    /// <example>TEL_GSM</example>
    public string? FileTypeCode { get; set; }

    // Connection settings
    /// <summary>Transfer protocol (SFTP, FTP, or FileSystem).</summary>
    /// <example>Sftp</example>
    public TransferProtocol Protocol { get; set; } = TransferProtocol.Sftp;

    /// <summary>Remote host address.</summary>
    /// <example>sftp.telstra.com.au</example>
    public string Host { get; set; } = string.Empty;

    /// <summary>Remote port (default 22 for SFTP, 21 for FTP).</summary>
    /// <example>22</example>
    public int Port { get; set; } = 22;

    /// <summary>Remote path to monitor for new files.</summary>
    /// <example>/outgoing/cdr</example>
    public string RemotePath { get; set; } = "/";

    // Authentication
    /// <summary>Authentication type for the remote connection.</summary>
    /// <example>Password</example>
    public AuthenticationType AuthType { get; set; } = AuthenticationType.Password;

    /// <summary>Username for authentication.</summary>
    /// <example>selcomm_user</example>
    public string Username { get; set; } = string.Empty;

    /// <summary>Password (encrypted in database, masked in API responses).</summary>
    public string? Password { get; set; }

    /// <summary>Path to certificate file for certificate-based authentication.</summary>
    /// <example>/etc/ssl/certs/telstra.pem</example>
    public string? CertificatePath { get; set; }

    /// <summary>Path to private key file for key-based authentication.</summary>
    /// <example>/etc/ssl/private/telstra.key</example>
    public string? PrivateKeyPath { get; set; }

    // File patterns
    /// <summary>Glob pattern to match files for download.</summary>
    /// <example>CDR_*.csv</example>
    public string FileNamePattern { get; set; } = "*.*";

    /// <summary>Glob pattern to match files that should be skipped.</summary>
    /// <example>*.tmp</example>
    public string? SkipFilePattern { get; set; }

    // Behavior
    /// <summary>Whether to delete files from the remote source after successful download.</summary>
    /// <example>true</example>
    public bool DeleteAfterDownload { get; set; } = true;

    /// <summary>Whether to compress files when archiving to the Processed folder.</summary>
    /// <example>true</example>
    public bool CompressOnArchive { get; set; } = true;

    /// <summary>Compression method for archiving processed files.</summary>
    /// <example>GZip</example>
    public CompressionMethod Compression { get; set; } = CompressionMethod.GZip;

    // Schedule
    /// <summary>CRON schedule expression for automated file retrieval.</summary>
    /// <example>0 */15 * * * *</example>
    public string? CronSchedule { get; set; }

    /// <summary>Whether this source is enabled for scheduled transfers.</summary>
    /// <example>true</example>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Timestamp when this record was created.</summary>
    /// <example>2025-01-15T10:30:00Z</example>
    public DateTime CreatedAt { get; set; }

    /// <summary>User who created this record.</summary>
    /// <example>admin</example>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Timestamp when this record was last updated.</summary>
    /// <example>2025-03-20T14:45:00Z</example>
    public DateTime UpdatedAt { get; set; }

    /// <summary>User who last updated this record.</summary>
    /// <example>admin</example>
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// Computed connection URL for display purposes.
    /// Format: protocol://username@host:port/path
    /// </summary>
    /// <example>sftp://selcomm_user@sftp.telstra.com.au/outgoing/cdr</example>
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
/// Transfer protocol types supported for remote file retrieval.
/// </summary>
public enum TransferProtocol
{
    /// <summary>SSH File Transfer Protocol (secure, recommended).</summary>
    Sftp = 1,

    /// <summary>File Transfer Protocol (legacy, use SFTP where possible).</summary>
    Ftp = 2,

    /// <summary>Local or network file system path.</summary>
    FileSystem = 3
}

/// <summary>
/// Authentication types for remote file transfer connections.
/// </summary>
public enum AuthenticationType
{
    /// <summary>Username and password authentication.</summary>
    Password = 1,

    /// <summary>Certificate-based authentication.</summary>
    Certificate = 2,

    /// <summary>Private key authentication (SSH key pair).</summary>
    PrivateKey = 3
}

/// <summary>
/// Compression methods for archiving processed files.
/// </summary>
public enum CompressionMethod
{
    /// <summary>No compression — files are stored as-is.</summary>
    None = 0,

    /// <summary>GZip compression (.gz).</summary>
    GZip = 1,

    /// <summary>Zip compression (.zip).</summary>
    Zip = 2
}

// ============================================
// FTP Server Entity
// ============================================

/// <summary>
/// FTP server entity for file storage. Each server is a distinct storage destination
/// that is linked to transfer records via FK. Immutable once referenced by transfers.
/// </summary>
public class FtpServer
{
    /// <summary>Unique server identifier (auto-generated).</summary>
    /// <example>1</example>
    public int ServerId { get; set; }

    /// <summary>Friendly label for this server.</summary>
    /// <example>Primary SFTP</example>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>Transfer protocol (SFTP or FTP).</summary>
    /// <example>Sftp</example>
    public TransferProtocol Protocol { get; set; }

    /// <summary>FTP host address.</summary>
    /// <example>ftp.internal.local</example>
    public string Host { get; set; } = string.Empty;

    /// <summary>FTP port.</summary>
    /// <example>22</example>
    public int Port { get; set; }

    /// <summary>Authentication type for the connection.</summary>
    /// <example>Password</example>
    public AuthenticationType AuthType { get; set; }

    /// <summary>Username for authentication.</summary>
    /// <example>file_user</example>
    public string? Username { get; set; }

    /// <summary>Password — masked in API responses.</summary>
    public string? Password { get; set; }

    /// <summary>Path to certificate file for cert-based auth.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Path to private key file for key-based auth.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>Root path on the FTP server for file storage.</summary>
    /// <example>/var/data/fileloading</example>
    public string RootPath { get; set; } = "/";

    /// <summary>Local temp path used during FTP mode processing for staging files.</summary>
    /// <example>/tmp/fileloading</example>
    public string? TempLocalPath { get; set; }

    /// <summary>Whether this server is the currently active storage destination.</summary>
    /// <example>false</example>
    public bool IsActive { get; set; }

    /// <summary>Computed: true if any transfer record references this server (immutable fields locked).</summary>
    /// <example>false</example>
    public bool IsLocked { get; set; }

    /// <summary>Timestamp when this record was created.</summary>
    /// <example>2025-01-15T10:30:00Z</example>
    public DateTime CreatedAt { get; set; }

    /// <summary>User who created this record.</summary>
    /// <example>admin</example>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Timestamp when this record was last updated.</summary>
    /// <example>2025-03-20T14:45:00Z</example>
    public DateTime UpdatedAt { get; set; }

    /// <summary>User who last updated this record.</summary>
    /// <example>admin</example>
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Request to create or update an FTP server entity.
/// </summary>
public class FtpServerRequest
{
    /// <summary>Friendly label for this server.</summary>
    /// <example>Primary SFTP</example>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>Transfer protocol (SFTP or FTP).</summary>
    /// <example>Sftp</example>
    public TransferProtocol Protocol { get; set; } = TransferProtocol.Sftp;

    /// <summary>FTP host address.</summary>
    /// <example>ftp.internal.local</example>
    public string Host { get; set; } = string.Empty;

    /// <summary>FTP port (default 22 for SFTP).</summary>
    /// <example>22</example>
    public int Port { get; set; } = 22;

    /// <summary>Authentication type for the connection.</summary>
    /// <example>Password</example>
    public AuthenticationType AuthType { get; set; } = AuthenticationType.Password;

    /// <summary>Username for authentication.</summary>
    /// <example>file_user</example>
    public string? Username { get; set; }

    /// <summary>Password (will be encrypted before storage).</summary>
    public string? Password { get; set; }

    /// <summary>Path to certificate file for cert-based auth.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Path to private key file for key-based auth.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>Root path on the FTP server for file storage.</summary>
    /// <example>/var/data/fileloading</example>
    public string RootPath { get; set; } = "/";

    /// <summary>Local temp path for FTP mode processing.</summary>
    /// <example>/tmp/fileloading</example>
    public string? TempLocalPath { get; set; }
}

/// <summary>
/// Result of a bulk folder creation operation, indicating success/failure per folder.
/// </summary>
public class FolderCreateResult
{
    /// <summary>Whether all folders were created successfully.</summary>
    /// <example>true</example>
    public bool AllCreated { get; set; }

    /// <summary>Status of each individual folder creation attempt.</summary>
    public List<FolderCreateStatus> Folders { get; set; } = new();
}

/// <summary>
/// Status of a single folder creation attempt within a bulk operation.
/// </summary>
public class FolderCreateStatus
{
    /// <summary>Workflow stage name of the folder (Transfer, Processing, Processed, Errors, Skipped, Example).</summary>
    /// <example>Transfer</example>
    public string FolderName { get; set; } = string.Empty;

    /// <summary>Full absolute path of the folder.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Transfer</example>
    public string Path { get; set; } = string.Empty;

    /// <summary>Whether the folder was newly created.</summary>
    /// <example>true</example>
    public bool Created { get; set; }

    /// <summary>Whether the folder already existed prior to this operation.</summary>
    /// <example>false</example>
    public bool AlreadyExisted { get; set; }

    /// <summary>Error message if the folder creation failed, null on success.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Default folder paths for a domain/file-type combination.
/// Used by the UI to display auto-generated folder paths before saving.
/// </summary>
public class FolderDefaultsResponse
{
    /// <summary>File type code (null for domain-level defaults).</summary>
    /// <example>TEL_GSM</example>
    public string? FileTypeCode { get; set; }

    /// <summary>Whether the domain is currently in FTP storage mode.</summary>
    /// <example>false</example>
    public bool IsFtpMode { get; set; }

    /// <summary>Non-editable local storage base path prefix.</summary>
    /// <example>/var/data/fileloading</example>
    public string BasePath { get; set; } = string.Empty;

    /// <summary>Whether the Example folder is always local (true when FTP mode is active).</summary>
    /// <example>true</example>
    public bool IsExampleAlwaysLocal { get; set; }

    /// <summary>Default transfer folder path where incoming files land.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Transfer</example>
    public string TransferFolder { get; set; } = string.Empty;

    /// <summary>Default processing folder path where files are moved during loading.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Processing</example>
    public string ProcessingFolder { get; set; } = string.Empty;

    /// <summary>Default processed folder path where successfully loaded files are archived.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Processed</example>
    public string ProcessedFolder { get; set; } = string.Empty;

    /// <summary>Default errors folder path where files with loading errors are moved.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Errors</example>
    public string ErrorsFolder { get; set; } = string.Empty;

    /// <summary>Default skipped folder path where intentionally skipped files are moved.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Skipped</example>
    public string SkippedFolder { get; set; } = string.Empty;

    /// <summary>Default example folder path where reference/sample files are stored.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Example</example>
    public string ExampleFolder { get; set; } = string.Empty;
}

// ============================================
// Folder Workflow Configuration
// ============================================

/// <summary>
/// Folder workflow configuration per domain/file-type.
/// Defines the folder structure used by the file processing pipeline — Transfer, Processing, Processed, Errors, Skipped, and Example.
/// </summary>
public class FolderWorkflowConfig
{
    /// <summary>Configuration ID (auto-generated).</summary>
    /// <example>1</example>
    public int ConfigId { get; set; }

    /// <summary>File type code (null for domain-level default configuration).</summary>
    /// <example>TEL_GSM</example>
    public string? FileTypeCode { get; set; }

    /// <summary>Folder where files are initially transferred to from the source.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Transfer</example>
    public string TransferFolder { get; set; } = string.Empty;

    /// <summary>Folder where files are moved while being actively processed.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Processing</example>
    public string ProcessingFolder { get; set; } = string.Empty;

    /// <summary>Folder where successfully processed files are archived.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Processed</example>
    public string ProcessedFolder { get; set; } = string.Empty;

    /// <summary>Folder where files that encountered errors during processing are moved.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Errors</example>
    public string ErrorsFolder { get; set; } = string.Empty;

    /// <summary>Folder where intentionally skipped files are moved.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Skipped</example>
    public string SkippedFolder { get; set; } = string.Empty;

    /// <summary>Folder where example/reference files are stored for AI review comparison.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Example</example>
    public string ExampleFolder { get; set; } = string.Empty;

    /// <summary>Timestamp when this record was created.</summary>
    /// <example>2025-01-15T10:30:00Z</example>
    public DateTime CreatedAt { get; set; }

    /// <summary>User who created this record.</summary>
    /// <example>admin</example>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Timestamp when this record was last updated.</summary>
    /// <example>2025-03-20T14:45:00Z</example>
    public DateTime UpdatedAt { get; set; }

    /// <summary>User who last updated this record.</summary>
    /// <example>admin</example>
    public string UpdatedBy { get; set; } = string.Empty;
}

// ============================================
// Downloaded File Tracking
// ============================================

/// <summary>
/// Record of a previously downloaded file, used to prevent re-downloading.
/// Tracks files from vendors where deletion after download is not possible.
/// </summary>
public class DownloadedFileRecord
{
    /// <summary>Download record ID (auto-generated).</summary>
    /// <example>1001</example>
    public int DownloadId { get; set; }

    /// <summary>Source ID the file was downloaded from.</summary>
    /// <example>1</example>
    public int SourceId { get; set; }

    /// <summary>Original file name on the remote server.</summary>
    /// <example>CDR_20250315_001.csv</example>
    public string RemoteFileName { get; set; } = string.Empty;

    /// <summary>Full remote file path.</summary>
    /// <example>/outgoing/cdr/CDR_20250315_001.csv</example>
    public string? RemoteFilePath { get; set; }

    /// <summary>File size in bytes.</summary>
    /// <example>1048576</example>
    public long FileSize { get; set; }

    /// <summary>Remote file's last modified date.</summary>
    /// <example>2025-03-15T08:00:00Z</example>
    public DateTime RemoteModifiedDate { get; set; }

    /// <summary>File hash (MD5 or SHA256) for duplicate detection.</summary>
    /// <example>d41d8cd98f00b204e9800998ecf8427e</example>
    public string? FileHash { get; set; }

    /// <summary>Timestamp when the file was downloaded.</summary>
    /// <example>2025-03-15T08:05:30Z</example>
    public DateTime DownloadedAt { get; set; }
}

// ============================================
// File Transfer Records
// ============================================

/// <summary>
/// Transfer operation record for tracking a file's progress through the processing workflow.
/// Each file that enters the system gets a transfer record from download through to completion.
/// </summary>
public class FileTransferRecord
{
    /// <summary>Transfer record ID (auto-generated).</summary>
    /// <example>5001</example>
    public int TransferId { get; set; }

    /// <summary>Source ID this transfer originated from (null for manual uploads).</summary>
    /// <example>1</example>
    public int? SourceId { get; set; }

    /// <summary>NT file number (foreign key to nt_file) assigned after file creation in the database.</summary>
    /// <example>12345</example>
    public int? NtFileNum { get; set; }

    /// <summary>File name.</summary>
    /// <example>CDR_20250315_001.csv</example>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Current transfer status in the workflow.</summary>
    /// <example>Downloaded</example>
    public TransferStatus Status { get; set; }

    /// <summary>Source path (remote or local) where the file was retrieved from.</summary>
    /// <example>/outgoing/cdr/CDR_20250315_001.csv</example>
    public string? SourcePath { get; set; }

    /// <summary>Local destination path where the file was saved.</summary>
    /// <example>/var/data/fileloading/TEL_GSM/Transfer/CDR_20250315_001.csv</example>
    public string? DestinationPath { get; set; }

    /// <summary>Current workflow folder (Transfer, Processing, Processed, Errors, Skipped).</summary>
    /// <example>Transfer</example>
    public string? CurrentFolder { get; set; }

    /// <summary>File size in bytes.</summary>
    /// <example>1048576</example>
    public long? FileSize { get; set; }

    /// <summary>Timestamp when the transfer/download started.</summary>
    /// <example>2025-03-15T08:05:00Z</example>
    public DateTime? StartedAt { get; set; }

    /// <summary>Timestamp when the transfer/processing completed.</summary>
    /// <example>2025-03-15T08:05:30Z</example>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Error message if the transfer or processing failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>FTP server ID at the time of transfer (null = local storage).</summary>
    /// <example>1</example>
    public int? FtpServerId { get; set; }

    /// <summary>Number of retry attempts made for this transfer.</summary>
    /// <example>0</example>
    public int RetryCount { get; set; }

    /// <summary>User who created or initiated this transfer.</summary>
    /// <example>admin</example>
    public string? CreatedBy { get; set; }

    /// <summary>Timestamp when this transfer record was created.</summary>
    /// <example>2025-03-15T08:05:00Z</example>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Transfer status enumeration representing the stages of file processing.
/// </summary>
public enum TransferStatus
{
    /// <summary>Transfer is pending — file has been queued but not yet started.</summary>
    Pending = 0,

    /// <summary>File is currently being downloaded from the remote source.</summary>
    Downloading = 1,

    /// <summary>File has been successfully downloaded to the Transfer folder.</summary>
    Downloaded = 2,

    /// <summary>File is currently being parsed and loaded into the database.</summary>
    Processing = 3,

    /// <summary>File has been successfully processed and archived.</summary>
    Processed = 4,

    /// <summary>Transfer or processing encountered an error.</summary>
    Error = 5,

    /// <summary>File was skipped (matched skip pattern or manually skipped by user).</summary>
    Skipped = 6
}

// ============================================
// Activity Logging
// ============================================

/// <summary>
/// User activity log entry for the file management audit trail.
/// Records all significant actions performed on files and transfer sources.
/// </summary>
public class FileActivityLog
{
    /// <summary>Activity log ID (auto-generated).</summary>
    /// <example>10001</example>
    public long ActivityId { get; set; }

    /// <summary>NT file number if this activity relates to a specific file.</summary>
    /// <example>12345</example>
    public int? NtFileNum { get; set; }

    /// <summary>Transfer record ID if this activity relates to a transfer operation.</summary>
    /// <example>5001</example>
    public int? TransferId { get; set; }

    /// <summary>File name for reference and display.</summary>
    /// <example>CDR_20250315_001.csv</example>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Type of activity that was performed.</summary>
    /// <example>Downloaded</example>
    public FileActivityType ActivityType { get; set; }

    /// <summary>Human-readable description of the activity.</summary>
    /// <example>File downloaded from sftp.telstra.com.au</example>
    public string Description { get; set; } = string.Empty;

    /// <summary>Additional details as a JSON string for structured metadata.</summary>
    /// <example>{"sourceId": 1, "fileSize": 1048576}</example>
    public string? Details { get; set; }

    /// <summary>User who performed the activity.</summary>
    /// <example>admin</example>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Timestamp when the activity occurred.</summary>
    /// <example>2025-03-15T08:05:30Z</example>
    public DateTime ActivityAt { get; set; }
}

/// <summary>
/// Types of file activities that can be logged in the audit trail.
/// </summary>
public enum FileActivityType
{
    /// <summary>File downloaded from a remote source.</summary>
    Downloaded = 1,

    /// <summary>File moved to the Processing folder.</summary>
    MovedToProcessing = 2,

    /// <summary>File processing/loading started.</summary>
    ProcessingStarted = 3,

    /// <summary>File processing completed successfully.</summary>
    ProcessingCompleted = 4,

    /// <summary>File processing failed with errors.</summary>
    ProcessingFailed = 5,

    /// <summary>File moved to the Skipped folder.</summary>
    MovedToSkipped = 6,

    /// <summary>File moved to the Errors folder.</summary>
    MovedToErrors = 7,

    /// <summary>File moved to the Processed folder.</summary>
    MovedToProcessed = 8,

    /// <summary>File was permanently deleted.</summary>
    FileDeleted = 9,

    /// <summary>Loaded file data was unloaded/reversed from the database.</summary>
    FileUnloaded = 10,

    /// <summary>File sequence number was manually skipped.</summary>
    SequenceSkipped = 11,

    /// <summary>File was manually triggered for download by a user.</summary>
    ManualDownload = 12,

    /// <summary>File was downloaded to a user's browser.</summary>
    BrowserDownload = 13,

    /// <summary>Transfer source configuration was created.</summary>
    SourceCreated = 14,

    /// <summary>Transfer source configuration was modified.</summary>
    SourceModified = 15,

    /// <summary>Transfer source configuration was deleted.</summary>
    SourceDeleted = 16
}

// ============================================
// Dashboard and Query Models
// ============================================

/// <summary>
/// Dashboard summary for the file management UI, providing at-a-glance counts of files across workflow stages.
/// </summary>
public class FileManagementDashboard
{
    /// <summary>Count of files currently in the Transfer folder awaiting processing.</summary>
    /// <example>5</example>
    public int FilesInTransfer { get; set; }

    /// <summary>Count of files currently being processed.</summary>
    /// <example>2</example>
    public int FilesInProcessing { get; set; }

    /// <summary>Count of files successfully processed today.</summary>
    /// <example>47</example>
    public int FilesProcessedToday { get; set; }

    /// <summary>Count of files that encountered errors.</summary>
    /// <example>1</example>
    public int FilesWithErrors { get; set; }

    /// <summary>Count of files that were skipped.</summary>
    /// <example>3</example>
    public int FilesSkipped { get; set; }

    /// <summary>Status summary for each configured transfer source.</summary>
    public List<TransferSourceStatus> SourceStatuses { get; set; } = new();
}

/// <summary>
/// Status summary for a single transfer source, used in the dashboard view.
/// </summary>
public class TransferSourceStatus
{
    /// <summary>Source ID.</summary>
    /// <example>1</example>
    public int SourceId { get; set; }

    /// <summary>Friendly name for the vendor/source.</summary>
    /// <example>Telstra CDR Feed</example>
    public string VendorName { get; set; } = string.Empty;

    /// <summary>File type code.</summary>
    /// <example>TEL_GSM</example>
    public string? FileTypeCode { get; set; }

    /// <summary>Whether the source is currently enabled for scheduled transfers.</summary>
    /// <example>true</example>
    public bool IsEnabled { get; set; }

    /// <summary>Timestamp of the last successful transfer from this source.</summary>
    /// <example>2025-03-15T08:05:30Z</example>
    public DateTime? LastTransferAt { get; set; }

    /// <summary>Number of files transferred from this source today.</summary>
    /// <example>12</example>
    public int FilesTransferredToday { get; set; }

    /// <summary>Last error message from this source, null if no recent errors.</summary>
    public string? LastError { get; set; }
}

/// <summary>
/// Filter criteria for querying the file list. All fields are optional and combined with AND logic.
/// </summary>
public class FileListFilter
{
    /// <summary>Filter by file type code.</summary>
    /// <example>TEL_GSM</example>
    public string? FileTypeCode { get; set; }

    /// <summary>Filter by current workflow folder (Transfer, Processing, Processed, Errors, Skipped).</summary>
    /// <example>Transfer</example>
    public string? CurrentFolder { get; set; }

    /// <summary>Filter by transfer status.</summary>
    /// <example>Downloaded</example>
    public TransferStatus? Status { get; set; }

    /// <summary>Filter by date range start (inclusive).</summary>
    /// <example>2025-03-01T00:00:00Z</example>
    public DateTime? FromDate { get; set; }

    /// <summary>Filter by date range end (inclusive).</summary>
    /// <example>2025-03-31T23:59:59Z</example>
    public DateTime? ToDate { get; set; }

    /// <summary>Search by file name (partial match).</summary>
    /// <example>CDR_2025</example>
    public string? FileNameSearch { get; set; }

    /// <summary>Maximum number of records to return.</summary>
    /// <example>100</example>
    public int MaxRecords { get; set; } = 100;
}

/// <summary>
/// File record with combined status information for UI display.
/// Merges transfer record data with file status for a unified view.
/// </summary>
public class FileWithStatus
{
    /// <summary>Transfer record ID.</summary>
    /// <example>5001</example>
    public int TransferId { get; set; }

    /// <summary>NT file number (null if the file has not yet been created in nt_file).</summary>
    /// <example>12345</example>
    public int? NtFileNum { get; set; }

    /// <summary>File name.</summary>
    /// <example>CDR_20250315_001.csv</example>
    public string FileName { get; set; } = string.Empty;

    /// <summary>File type code.</summary>
    /// <example>TEL_GSM</example>
    public string? FileTypeCode { get; set; }

    /// <summary>Current workflow folder.</summary>
    /// <example>Transfer</example>
    public string CurrentFolder { get; set; } = string.Empty;

    /// <summary>Transfer status as an enum/integer value.</summary>
    /// <example>2</example>
    public TransferStatus StatusId { get; set; }

    /// <summary>Human-readable status description.</summary>
    /// <example>Downloaded</example>
    public string Status { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    /// <example>1048576</example>
    public long? FileSize { get; set; }

    /// <summary>Timestamp when the file was created or downloaded.</summary>
    /// <example>2025-03-15T08:05:00Z</example>
    public DateTime CreatedAt { get; set; }

    /// <summary>Timestamp when processing completed (null if not yet completed).</summary>
    /// <example>2025-03-15T08:06:15Z</example>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Error message if the file encountered errors, null otherwise.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Source ID if the file was transferred from a remote source (null for manual uploads).</summary>
    /// <example>1</example>
    public int? SourceId { get; set; }

    /// <summary>FTP server ID at the time of transfer (null = local storage).</summary>
    /// <example>1</example>
    public int? FtpServerId { get; set; }

    /// <summary>FTP hostname at the time of transfer (joined from ntfl_ftp_server, null for local storage).</summary>
    /// <example>ftp.internal.local</example>
    public string? StorageHost { get; set; }
}

/// <summary>
/// Paginated response for file list queries, containing items and total count for pagination.
/// </summary>
public class FileListWithStatusResponse
{
    /// <summary>List of files matching the query criteria.</summary>
    public List<FileWithStatus> Items { get; set; } = new();

    /// <summary>Total count of matching records (for pagination controls).</summary>
    /// <example>150</example>
    public int TotalCount { get; set; }
}

// ============================================
// Request/Response Models for API
// ============================================

/// <summary>
/// Request to create or update a transfer source configuration.
/// </summary>
public class TransferSourceRequest
{
    /// <summary>Source ID (populated from route for updates, ignored for creates).</summary>
    /// <example>1</example>
    public int SourceId { get; set; }

    /// <summary>Friendly name for the vendor/source.</summary>
    /// <example>Telstra CDR Feed</example>
    public string VendorName { get; set; } = string.Empty;

    /// <summary>File type code to associate with this source.</summary>
    /// <example>TEL_GSM</example>
    public string? FileTypeCode { get; set; }

    /// <summary>Transfer protocol for the connection.</summary>
    /// <example>Sftp</example>
    public TransferProtocol Protocol { get; set; } = TransferProtocol.Sftp;

    /// <summary>Remote host address to connect to.</summary>
    /// <example>sftp.telstra.com.au</example>
    public string Host { get; set; } = string.Empty;

    /// <summary>Remote port number.</summary>
    /// <example>22</example>
    public int Port { get; set; } = 22;

    /// <summary>Remote path to monitor for new files.</summary>
    /// <example>/outgoing/cdr</example>
    public string RemotePath { get; set; } = "/";

    /// <summary>Authentication type for the connection.</summary>
    /// <example>Password</example>
    public AuthenticationType AuthType { get; set; } = AuthenticationType.Password;

    /// <summary>Username for authentication.</summary>
    /// <example>selcomm_user</example>
    public string Username { get; set; } = string.Empty;

    /// <summary>Password (will be encrypted before storage).</summary>
    public string? Password { get; set; }

    /// <summary>Path to certificate file for cert-based authentication.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Path to private key file for key-based authentication.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>Glob pattern to match files for download.</summary>
    /// <example>CDR_*.csv</example>
    public string FileNamePattern { get; set; } = "*.*";

    /// <summary>Glob pattern to match files that should be skipped.</summary>
    /// <example>*.tmp</example>
    public string? SkipFilePattern { get; set; }

    /// <summary>Whether to delete files from the source after successful download.</summary>
    /// <example>true</example>
    public bool DeleteAfterDownload { get; set; } = true;

    /// <summary>Whether to compress files when archiving to the Processed folder.</summary>
    /// <example>true</example>
    public bool CompressOnArchive { get; set; } = true;

    /// <summary>Compression method for archiving.</summary>
    /// <example>GZip</example>
    public CompressionMethod Compression { get; set; } = CompressionMethod.GZip;

    /// <summary>CRON schedule expression for automated retrieval.</summary>
    /// <example>0 */15 * * * *</example>
    public string? CronSchedule { get; set; }

    /// <summary>Whether the source should be enabled for scheduled transfers.</summary>
    /// <example>true</example>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Request to move a file to a specific workflow folder.
/// </summary>
public class MoveFileRequest
{
    /// <summary>Target folder name (Transfer, Processing, Processed, Errors, Skipped).</summary>
    /// <example>Processed</example>
    public string TargetFolder { get; set; } = string.Empty;

    /// <summary>Optional reason for the move (recorded in activity log).</summary>
    /// <example>Manually verified and approved</example>
    public string? Reason { get; set; }
}

/// <summary>
/// Request to skip to a specific sequence number for a file type.
/// </summary>
public class SequenceSkipRequest
{
    /// <summary>Sequence number to skip to (next expected sequence will be this value + 1).</summary>
    /// <example>50</example>
    public int SkipToSequence { get; set; }

    /// <summary>Reason for skipping sequences (recorded in activity log).</summary>
    /// <example>Sequences 43-50 were empty test files from vendor</example>
    public string? Reason { get; set; }
}

/// <summary>
/// Response for a transfer fetch (download) operation, summarizing what was retrieved.
/// </summary>
public class TransferFetchResponse
{
    /// <summary>Total number of files found on the remote source.</summary>
    /// <example>10</example>
    public int FilesFound { get; set; }

    /// <summary>Number of files successfully downloaded.</summary>
    /// <example>8</example>
    public int FilesDownloaded { get; set; }

    /// <summary>Number of files skipped (already downloaded or matched skip pattern).</summary>
    /// <example>1</example>
    public int FilesSkipped { get; set; }

    /// <summary>Number of files that failed to download.</summary>
    /// <example>1</example>
    public int FilesFailed { get; set; }

    /// <summary>Transfer records created for the downloaded files.</summary>
    public List<FileTransferRecord> TransferRecords { get; set; } = new();

    /// <summary>Error messages encountered during the fetch operation.</summary>
    public List<string> Errors { get; set; } = new();
}
