using Selcomm.Data.Common;
using FileLoading.Models;
using FileLoading.Validation;

namespace FileLoading.Repositories;

/// <summary>
/// Repository interface for FileLoader database operations.
/// Uses V4 stored procedures: sp_file_loading_nt_file_api, ss_file_loading_nt_file_api, su_file_loading_nt_file_api.
/// Inserts records into: cl_detail, ntfl_chgdtl, nt_cl_not_load.
/// </summary>
public interface IFileLoaderRepository
{
    /// <summary>
    /// Call sf_authorise to verify the user has permission for the operation.
    /// Raises an exception (returns failure) if not authorised.
    /// </summary>
    /// <param name="context">Security context with user, role, and operation ID</param>
    /// <param name="entityType">Entity type (e.g., "FILEMANAGER", "VENDOR")</param>
    /// <param name="entityId">Entity identifier</param>
    Task<RawCommandResult> AuthoriseAsync(SecurityContext context, string entityType, string entityId);

    /// <summary>
    /// Create a new file load record in nt_file table.
    /// Calls sp_file_loading_nt_file_api stored procedure.
    /// </summary>
    /// <param name="fileTypeCode">File type code (from file_type table)</param>
    /// <param name="ntCustNum">Network customer number</param>
    /// <param name="ntFileName">File name (can include placeholders like &lt;SEQUENCE&gt;)</param>
    /// <param name="statusId">Initial status ID (typically 1 = Initial loading)</param>
    /// <param name="ntFileDate">File date (defaults to today)</param>
    /// <param name="securityContext">Security context</param>
    /// <returns>ValueResult containing the new nt_file_num and resolved filename</returns>
    Task<ValueResult<NtFileCreateResult>> CreateNtFileAsync(
        string fileTypeCode,
        string ntCustNum,
        string ntFileName,
        int statusId,
        DateTime? ntFileDate,
        SecurityContext securityContext);

    /// <summary>
    /// Update file status using su_file_loading_nt_file_api stored procedure.
    /// </summary>
    /// <param name="ntFileNum">File number</param>
    /// <param name="statusId">New status ID</param>
    /// <param name="securityContext">Security context</param>
    Task<StoredProcedureResult> UpdateFileStatusAsync(
        int ntFileNum,
        int statusId,
        SecurityContext securityContext);

    /// <summary>
    /// Get file status by nt_file_num.
    /// Uses direct SQL query on nt_file with joins.
    /// </summary>
    /// <param name="ntFileNum">File number</param>
    /// <param name="securityContext">Security context</param>
    Task<DataResult<FileStatusResponse>> GetFileStatusAsync(
        int ntFileNum,
        SecurityContext securityContext);

    /// <summary>
    /// List files with pagination and filtering.
    /// Uses ss_file_loading_nt_file_api stored procedure.
    /// </summary>
    /// <param name="fileTypeCode">Optional file type filter</param>
    /// <param name="ntCustNum">Optional customer number filter</param>
    /// <param name="maxRecords">Maximum records to return</param>
    /// <param name="securityContext">Security context</param>
    Task<DataResult<FileListResponse>> ListFilesAsync(
        string? fileTypeCode,
        string? ntCustNum,
        int maxRecords,
        SecurityContext securityContext);

    /// <summary>
    /// Insert a batch of call detail records into cl_detail table.
    /// </summary>
    /// <param name="records">Records to insert</param>
    Task<RawCommandResult> InsertClDetailBatchAsync(IEnumerable<ClDetailRecord> records);

    /// <summary>
    /// Optimized batch insert for cl_detail records using transaction batching.
    /// Wraps each batch in an explicit transaction to reduce commit overhead.
    /// </summary>
    /// <param name="records">Records to insert</param>
    /// <param name="transactionBatchSize">Number of records per transaction batch</param>
    Task<RawCommandResult> InsertClDetailBatchOptimizedAsync(IEnumerable<ClDetailRecord> records, int transactionBatchSize = 1000);

    /// <summary>
    /// Insert a batch of charge detail records into ntfl_chgdtl table.
    /// </summary>
    /// <param name="records">Records to insert</param>
    Task<RawCommandResult> InsertChargeBatchAsync(IEnumerable<NtflChgdtlRecord> records);

    /// <summary>
    /// Optimized batch insert for charge records using transaction batching.
    /// Wraps each batch in an explicit transaction to reduce commit overhead.
    /// </summary>
    /// <param name="records">Records to insert</param>
    /// <param name="transactionBatchSize">Number of records per transaction batch</param>
    Task<RawCommandResult> InsertChargeBatchOptimizedAsync(IEnumerable<NtflChgdtlRecord> records, int transactionBatchSize = 1000);

    /// <summary>
    /// Batch insert for ssswhls_cdr sub-type records using transaction batching.
    /// </summary>
    Task<RawCommandResult> InsertSssWhlsCdrBatchAsync(IEnumerable<SssWhlsCdrRecord> records, int transactionBatchSize = 1000);

    /// <summary>
    /// Batch insert for ssswhlschg sub-type records using transaction batching.
    /// </summary>
    Task<RawCommandResult> InsertSssWhlsChgBatchAsync(IEnumerable<SssWhlsChgRecord> records, int transactionBatchSize = 1000);

    /// <summary>
    /// Insert a failed record into nt_cl_not_load table.
    /// </summary>
    /// <param name="record">Failed record with error details</param>
    Task<RawCommandResult> InsertNotLoadRecordAsync(NtClNotLoadRecord record);

    /// <summary>
    /// Update file trailer totals in nt_fl_trailer table.
    /// </summary>
    /// <param name="ntFileNum">File number</param>
    /// <param name="totalRecords">Total records processed</param>
    /// <param name="totalCost">Total cost amount</param>
    /// <param name="earliestCall">Earliest call date</param>
    /// <param name="latestCall">Latest call date</param>
    Task<RawCommandResult> UpdateTrailerAsync(
        int ntFileNum,
        int totalRecords,
        decimal totalCost,
        DateTime? earliestCall,
        DateTime? latestCall);

    /// <summary>
    /// Get file types from file_type table.
    /// </summary>
    /// <param name="securityContext">Security context</param>
    Task<DataResult<FileTypeListResponse>> GetFileTypesAsync(SecurityContext securityContext);

    /// <summary>
    /// Get next record number for a file.
    /// Uses nt_fl_trailer.nt_tot_rec + 1.
    /// </summary>
    /// <param name="ntFileNum">File number</param>
    Task<int> GetNextRecordNumberAsync(int ntFileNum);

    /// <summary>
    /// Insert error log records into ntfl_error_log table.
    /// </summary>
    /// <param name="errors">Error records to insert</param>
    Task<RawCommandResult> InsertErrorLogBatchAsync(IEnumerable<NtflErrorLogRecord> errors);

    /// <summary>
    /// Get error logs for a file.
    /// </summary>
    /// <param name="ntFileNum">File number</param>
    Task<DataResult<List<NtflErrorLogRecord>>> GetErrorLogsAsync(int ntFileNum);

    /// <summary>
    /// Stores a validation summary for later retrieval (e.g., by AI agents).
    /// The summary is serialized as JSON and stored in the database.
    /// </summary>
    /// <param name="ntFileNum">File number</param>
    /// <param name="summary">The AI-friendly validation summary</param>
    Task<RawCommandResult> StoreValidationSummaryAsync(int ntFileNum, ValidationSummaryForAI summary);

    /// <summary>
    /// Retrieves a stored validation summary for a file.
    /// </summary>
    /// <param name="ntFileNum">File number</param>
    Task<DataResult<ValidationSummaryForAI?>> GetValidationSummaryAsync(int ntFileNum);

    /// <summary>
    /// Inserts validation errors in batch (from FileValidationResult).
    /// Uses the AI-friendly error format.
    /// </summary>
    /// <param name="ntFileNum">File number</param>
    /// <param name="errors">Validation errors to insert</param>
    Task<RawCommandResult> InsertValidationErrorsBatchAsync(int ntFileNum, IEnumerable<ValidationError> errors);

    // ============================================
    // Process Tracking (Legacy Compatibility)
    // ============================================

    /// <summary>
    /// Insert a process tracking record into nt_fl_process.
    /// Returns the auto-generated process_ref.
    /// </summary>
    /// <param name="ntFileNum">File number</param>
    Task<ValueResult<int>> InsertProcessRecordAsync(int ntFileNum);

    /// <summary>
    /// Update process tracking record with end timestamp.
    /// </summary>
    /// <param name="processRef">Process reference</param>
    Task<RawCommandResult> UpdateProcessRecordAsync(int processRef);

    /// <summary>
    /// Insert a file header record into nt_fl_header.
    /// </summary>
    /// <param name="ntFileNum">File number</param>
    /// <param name="ntCustNum">Customer number</param>
    /// <param name="earliestCall">Earliest call/record date</param>
    /// <param name="latestCall">Latest call/record date</param>
    Task<RawCommandResult> InsertFileHeaderAsync(int ntFileNum, string ntCustNum, DateTime? earliestCall, DateTime? latestCall);

    // ============================================
    // Transfer Source Configuration
    // ============================================

    /// <summary>
    /// Get all transfer source configurations.
    /// </summary>
    /// <param name="domain">Optional filter by domain</param>
    Task<DataResult<List<TransferSourceConfig>>> GetTransferSourcesAsync(string? domain);

    /// <summary>
    /// Get a specific transfer source configuration.
    /// </summary>
    /// <param name="sourceId">Source identifier</param>
    Task<DataResult<TransferSourceConfig>> GetTransferSourceAsync(int sourceId);

    /// <summary>
    /// Insert a new transfer source configuration.
    /// </summary>
    /// <param name="config">Transfer source configuration</param>
    Task<RawCommandResult> InsertTransferSourceAsync(TransferSourceConfig config);

    /// <summary>
    /// Update a transfer source configuration.
    /// </summary>
    /// <param name="config">Transfer source configuration</param>
    Task<RawCommandResult> UpdateTransferSourceAsync(TransferSourceConfig config);

    /// <summary>
    /// Delete a transfer source configuration.
    /// </summary>
    /// <param name="sourceId">Source identifier</param>
    Task<RawCommandResult> DeleteTransferSourceAsync(int sourceId);

    // ============================================
    // Folder Configuration
    // ============================================

    /// <summary>
    /// Get folder workflow configuration for a domain/file-type.
    /// Falls back to domain default if file-type specific config not found.
    /// </summary>
    /// <param name="domain">Domain name</param>
    /// <param name="fileTypeCode">Optional file type code</param>
    Task<DataResult<FolderWorkflowConfig>> GetFolderConfigAsync(string domain, string? fileTypeCode);

    /// <summary>
    /// Insert or update folder workflow configuration.
    /// </summary>
    /// <param name="config">Folder configuration</param>
    Task<RawCommandResult> SaveFolderConfigAsync(FolderWorkflowConfig config);

    // ============================================
    // Transfer Records
    // ============================================

    /// <summary>
    /// Insert a new transfer record.
    /// </summary>
    /// <param name="record">Transfer record</param>
    Task<ValueResult<int>> InsertTransferRecordAsync(FileTransferRecord record);

    /// <summary>
    /// Update transfer record status.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <param name="status">New status</param>
    /// <param name="error">Optional error message</param>
    /// <param name="completedAt">Optional completion timestamp</param>
    Task<RawCommandResult> UpdateTransferStatusAsync(int transferId, TransferStatus status, string? error, DateTime? completedAt = null);

    /// <summary>
    /// Update transfer record with nt_file_num after file creation.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <param name="ntFileNum">NT file number</param>
    Task<RawCommandResult> UpdateTransferNtFileNumAsync(int transferId, int ntFileNum);

    /// <summary>
    /// Update transfer record current folder.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <param name="currentFolder">Current folder name</param>
    /// <param name="destinationPath">New destination path</param>
    Task<RawCommandResult> UpdateTransferFolderAsync(int transferId, string currentFolder, string? destinationPath);

    /// <summary>
    /// Get a specific transfer record.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    Task<DataResult<FileTransferRecord>> GetTransferRecordAsync(int transferId);

    /// <summary>
    /// List transfer records with filtering.
    /// </summary>
    /// <param name="sourceId">Optional filter by source ID</param>
    /// <param name="status">Optional filter by status</param>
    /// <param name="currentFolder">Optional filter by current folder</param>
    /// <param name="maxRecords">Maximum records to return</param>
    Task<DataResult<List<FileTransferRecord>>> ListTransferRecordsAsync(
        int? sourceId, int? status, string? currentFolder, int maxRecords);

    /// <summary>
    /// Get transfer records with combined status for UI display.
    /// </summary>
    /// <param name="filter">Filter criteria</param>
    Task<DataResult<List<FileWithStatus>>> ListFilesWithStatusAsync(FileListFilter filter);

    /// <summary>
    /// Delete a transfer record.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    Task<RawCommandResult> DeleteTransferRecordAsync(int transferId);

    // ============================================
    // Downloaded Files Tracking
    // ============================================

    /// <summary>
    /// Check if a file has already been downloaded from a source.
    /// </summary>
    /// <param name="sourceId">Source identifier</param>
    /// <param name="fileName">Remote file name</param>
    /// <param name="modifiedDate">Remote file modified date</param>
    /// <param name="fileSize">File size</param>
    Task<bool> IsFileDownloadedAsync(int sourceId, string fileName, DateTime modifiedDate, long fileSize);

    /// <summary>
    /// Record a downloaded file.
    /// </summary>
    /// <param name="record">Downloaded file record</param>
    Task<RawCommandResult> InsertDownloadedFileAsync(DownloadedFileRecord record);

    // ============================================
    // Activity Logging
    // ============================================

    /// <summary>
    /// Insert an activity log entry.
    /// </summary>
    /// <param name="log">Activity log entry</param>
    Task<RawCommandResult> InsertActivityLogAsync(FileActivityLog log);

    /// <summary>
    /// Get activity log entries.
    /// </summary>
    /// <param name="ntFileNum">Optional filter by file number</param>
    /// <param name="transferId">Optional filter by transfer ID</param>
    /// <param name="maxRecords">Maximum records to return</param>
    Task<DataResult<List<FileActivityLog>>> GetActivityLogsAsync(int? ntFileNum, int? transferId, int maxRecords);

    // ============================================
    // Dashboard Queries
    // ============================================

    /// <summary>
    /// Get dashboard summary counts.
    /// </summary>
    /// <param name="domain">Optional filter by domain</param>
    /// <param name="fileTypeCode">Optional filter by file type</param>
    Task<DataResult<FileManagementDashboard>> GetDashboardSummaryAsync(string? domain, string? fileTypeCode);

    /// <summary>
    /// Get transfer source status summaries.
    /// </summary>
    /// <param name="domain">Optional filter by domain</param>
    Task<DataResult<List<TransferSourceStatus>>> GetSourceStatusesAsync(string? domain);

    // ============================================
    // File Unload Operations
    // ============================================

    /// <summary>
    /// Unload (reverse) a loaded file by deleting its records.
    /// </summary>
    /// <param name="ntFileNum">NT file number</param>
    /// <param name="securityContext">Security context</param>
    Task<RawCommandResult> UnloadFileRecordsAsync(int ntFileNum, SecurityContext securityContext);

    // ============================================
    // Generic Parser Configuration
    // ============================================

    /// <summary>
    /// Get generic file format configuration for a file type.
    /// Loads ntfl_file_format_config + ntfl_column_mapping.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    Task<GenericFileFormatConfig?> GetGenericFileFormatConfigAsync(string fileTypeCode);

    /// <summary>
    /// Optimized batch insert for generic detail records using transaction batching.
    /// Inserts into ntfl_generic_detail table.
    /// </summary>
    /// <param name="records">Records to insert</param>
    /// <param name="transactionBatchSize">Number of records per transaction batch</param>
    Task<RawCommandResult> InsertGenericDetailBatchOptimizedAsync(
        IEnumerable<GenericDetailRecord> records, int transactionBatchSize = 1000);

    /// <summary>
    /// Execute a custom validation stored procedure for a generic file.
    /// </summary>
    /// <param name="spName">Stored procedure name</param>
    /// <param name="ntFileNum">NT file number</param>
    Task<RawCommandResult> ExecuteCustomValidationSpAsync(string spName, int ntFileNum);

    /// <summary>
    /// Get all generic file format configurations.
    /// </summary>
    /// <param name="activeOnly">If true, only return active configs</param>
    Task<DataResult<List<GenericFileFormatConfig>>> GetAllGenericFileFormatConfigsAsync(bool? activeOnly);

    /// <summary>
    /// Insert a new generic file format configuration.
    /// </summary>
    /// <param name="config">Configuration to insert</param>
    Task<RawCommandResult> InsertGenericFileFormatConfigAsync(GenericFileFormatConfig config);

    /// <summary>
    /// Update an existing generic file format configuration.
    /// </summary>
    /// <param name="config">Configuration to update</param>
    Task<RawCommandResult> UpdateGenericFileFormatConfigAsync(GenericFileFormatConfig config);

    /// <summary>
    /// Delete a generic file format configuration and its column mappings.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    Task<RawCommandResult> DeleteGenericFileFormatConfigAsync(string fileTypeCode);

    /// <summary>
    /// Delete all column mappings for a file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    Task<RawCommandResult> DeleteColumnMappingsAsync(string fileTypeCode);

    /// <summary>
    /// Insert column mappings in batch.
    /// </summary>
    /// <param name="mappings">Column mappings to insert</param>
    Task<RawCommandResult> InsertColumnMappingsBatchAsync(IEnumerable<GenericColumnMapping> mappings);

    // ============================================
    // Lookup Tables: Vendors (networks)
    // ============================================

    Task<DataResult<List<VendorRecord>>> GetVendorsAsync();
    Task<DataResult<VendorRecord>> GetVendorAsync(string networkId);
    Task<RawCommandResult> InsertVendorAsync(VendorRecord record);
    Task<RawCommandResult> UpdateVendorAsync(VendorRecord record);
    Task<RawCommandResult> DeleteVendorAsync(string networkId);

    // ============================================
    // Lookup Tables: File Classes
    // ============================================

    Task<DataResult<List<FileClassRecord>>> GetFileClassesAsync();
    Task<DataResult<FileClassRecord>> GetFileClassAsync(string fileClassCode);
    Task<RawCommandResult> InsertFileClassAsync(FileClassRecord record);
    Task<RawCommandResult> UpdateFileClassAsync(FileClassRecord record);
    Task<RawCommandResult> DeleteFileClassAsync(string fileClassCode);

    // ============================================
    // Lookup Tables: File Types
    // ============================================

    Task<DataResult<List<FileTypeRecord>>> GetFileTypeRecordsAsync();
    Task<DataResult<FileTypeRecord>> GetFileTypeRecordAsync(string fileTypeCode);
    Task<RawCommandResult> InsertFileTypeAsync(FileTypeRecord record);
    Task<RawCommandResult> UpdateFileTypeAsync(FileTypeRecord record);
    Task<RawCommandResult> DeleteFileTypeAsync(string fileTypeCode);

    // ============================================
    // Lookup Tables: File Type NT
    // ============================================

    Task<DataResult<List<FileTypeNtRecord>>> GetFileTypeNtRecordsAsync(string? fileTypeCode = null);
    Task<DataResult<FileTypeNtRecord>> GetFileTypeNtRecordAsync(string fileTypeCode);
    Task<RawCommandResult> InsertFileTypeNtAsync(FileTypeNtRecord record);
    Task<RawCommandResult> UpdateFileTypeNtAsync(FileTypeNtRecord record);
    Task<RawCommandResult> DeleteFileTypeNtAsync(string fileTypeCode);

    // ============================================
    // Folder Storage Configuration
    // ============================================

    /// <summary>
    /// Get folder storage configuration for a domain.
    /// </summary>
    /// <param name="domain">Domain name</param>
    Task<DataResult<FolderStorageConfig>> GetFolderStorageAsync(string domain);

    /// <summary>
    /// Insert or update folder storage configuration.
    /// </summary>
    /// <param name="config">Storage configuration</param>
    Task<RawCommandResult> UpsertFolderStorageAsync(FolderStorageConfig config);

    /// <summary>
    /// Delete folder storage configuration for a domain.
    /// </summary>
    /// <param name="domain">Domain name</param>
    Task<RawCommandResult> DeleteFolderStorageAsync(string domain);

    // ============================================
    // AI Review
    // ============================================

    /// <summary>
    /// Get a cached AI review for a file.
    /// </summary>
    Task<DataResult<AiReviewResponse>> GetCachedAiReviewAsync(int ntFileNum);

    /// <summary>
    /// Store an AI review result.
    /// </summary>
    Task<RawCommandResult> StoreAiReviewAsync(AiReviewResponse review, string reviewedBy, DateTime expiresAt);

    /// <summary>
    /// Delete cached AI reviews for a file.
    /// </summary>
    Task<RawCommandResult> DeleteAiReviewAsync(int ntFileNum);

    // ============================================
    // AI Example Files
    // ============================================

    Task<DataResult<List<ExampleFileRecord>>> GetAllExampleFilesAsync();
    Task<DataResult<ExampleFileRecord>> GetExampleFileAsync(string fileTypeCode);
    Task<RawCommandResult> UpsertExampleFileAsync(ExampleFileRecord record);
    Task<RawCommandResult> DeleteExampleFileAsync(string fileTypeCode);

    // ============================================
    // AI Domain Config
    // ============================================

    Task<DataResult<AiDomainConfig>> GetAiDomainConfigAsync(string domain);
    Task<RawCommandResult> UpsertAiDomainConfigAsync(AiDomainConfig config);
    Task<RawCommandResult> DeleteAiDomainConfigAsync(string domain);
    Task<RawCommandResult> IncrementAiReviewCountAsync(string domain);
    Task<RawCommandResult> ResetAiReviewCountAsync(string domain);
}

/// <summary>
/// Result from creating a new nt_file record.
/// </summary>
public class NtFileCreateResult
{
    /// <summary>The assigned file number.</summary>
    public int NtFileNum { get; set; }

    /// <summary>The resolved file name (with placeholders replaced).</summary>
    public string NtFileName { get; set; } = string.Empty;
}
