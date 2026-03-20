using Selcomm.Data.Common;
using FileLoading.Models;
using FileLoading.Validation;

namespace FileLoading.Interfaces;

/// <summary>
/// Service interface for file management operations.
/// Handles user-initiated file actions, dashboard data, and activity logging.
/// </summary>
public interface IFileManagementService
{
    // ============================================
    // User File Operations
    // ============================================

    /// <summary>
    /// Process a file from the Transfer folder.
    /// Moves to Processing, runs file loader, then moves to Processed/Errors.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <param name="context">Security context</param>
    /// <returns>Processing result</returns>
    Task<DataResult<FileLoadResponse>> ProcessFileAsync(
        int transferId,
        SecurityContext context);

    /// <summary>
    /// Unload a previously loaded file (reverse the load operation).
    /// Deletes inserted records from database.
    /// </summary>
    /// <param name="ntFileNum">NT file number</param>
    /// <param name="context">Security context</param>
    /// <returns>True if successful</returns>
    Task<DataResult<bool>> UnloadFileAsync(
        int ntFileNum,
        SecurityContext context);

    /// <summary>
    /// Force skip a sequence number to allow loading out-of-sequence files.
    /// </summary>
    /// <param name="ntFileNum">NT file number</param>
    /// <param name="skipToSequence">Sequence number to skip to</param>
    /// <param name="reason">Reason for skipping</param>
    /// <param name="context">Security context</param>
    /// <returns>True if successful</returns>
    Task<DataResult<bool>> ForceSequenceSkipAsync(
        int ntFileNum,
        int skipToSequence,
        string? reason,
        SecurityContext context);

    /// <summary>
    /// Move a file to a specific workflow folder.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <param name="targetFolder">Target folder name</param>
    /// <param name="reason">Optional reason for the move</param>
    /// <param name="context">Security context</param>
    /// <returns>True if successful</returns>
    Task<DataResult<bool>> MoveFileAsync(
        int transferId,
        string targetFolder,
        string? reason,
        SecurityContext context);

    /// <summary>
    /// Delete a file from the workflow.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <param name="context">Security context</param>
    /// <returns>True if successful</returns>
    Task<DataResult<bool>> DeleteFileAsync(
        int transferId,
        SecurityContext context);

    /// <summary>
    /// Get a file stream for download to browser.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <param name="context">Security context</param>
    /// <returns>File stream and content type</returns>
    Task<DataResult<FileDownloadResult>> DownloadFileAsync(
        int transferId,
        SecurityContext context);

    /// <summary>
    /// Retry processing a failed file.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <param name="context">Security context</param>
    /// <returns>Processing result</returns>
    Task<DataResult<FileLoadResponse>> RetryProcessingAsync(
        int transferId,
        SecurityContext context);

    // ============================================
    // Dashboard Queries
    // ============================================

    /// <summary>
    /// Get dashboard summary data.
    /// </summary>
    /// <param name="domain">Optional filter by domain</param>
    /// <param name="fileTypeCode">Optional filter by file type</param>
    /// <param name="context">Security context</param>
    /// <returns>Dashboard summary</returns>
    Task<DataResult<FileManagementDashboard>> GetDashboardAsync(
        string? domain,
        string? fileTypeCode,
        SecurityContext context);

    /// <summary>
    /// List files with filtering and pagination.
    /// </summary>
    /// <param name="filter">Filter criteria</param>
    /// <param name="context">Security context</param>
    /// <returns>List of files with status</returns>
    Task<DataResult<FileListWithStatusResponse>> ListFilesAsync(
        FileListFilter filter,
        SecurityContext context);

    /// <summary>
    /// Get detailed file information.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <param name="context">Security context</param>
    /// <returns>File details</returns>
    Task<DataResult<FileWithStatus>> GetFileDetailsAsync(
        int transferId,
        SecurityContext context);

    // ============================================
    // Activity Logging
    // ============================================

    /// <summary>
    /// Get activity log entries.
    /// </summary>
    /// <param name="ntFileNum">Optional filter by file number</param>
    /// <param name="transferId">Optional filter by transfer ID</param>
    /// <param name="maxRecords">Maximum records to return</param>
    /// <param name="context">Security context</param>
    /// <returns>List of activity log entries</returns>
    Task<DataResult<List<FileActivityLog>>> GetActivityLogAsync(
        int? ntFileNum,
        int? transferId,
        int maxRecords,
        SecurityContext context);

    /// <summary>
    /// Log a file activity.
    /// </summary>
    /// <param name="activity">Activity to log</param>
    /// <param name="context">Security context</param>
    Task LogActivityAsync(
        FileActivityLog activity,
        SecurityContext context);

    // ============================================
    // Validation Summary (AI Error Analysis)
    // ============================================

    /// <summary>
    /// Get AI-friendly validation summary for a file.
    /// </summary>
    /// <param name="ntFileNum">NT file number</param>
    /// <param name="context">Security context</param>
    /// <returns>Validation summary</returns>
    Task<DataResult<ValidationSummaryForAI>> GetValidationSummaryAsync(
        int ntFileNum,
        SecurityContext context);

    // ============================================
    // Exception/Error Management
    // ============================================

    /// <summary>
    /// Get files with errors for the exceptions view.
    /// </summary>
    /// <param name="domain">Optional filter by domain</param>
    /// <param name="fileTypeCode">Optional filter by file type</param>
    /// <param name="maxRecords">Maximum records to return</param>
    /// <param name="context">Security context</param>
    /// <returns>List of files with errors</returns>
    Task<DataResult<List<FileWithStatus>>> GetFilesWithErrorsAsync(
        string? domain,
        string? fileTypeCode,
        int maxRecords,
        SecurityContext context);

    /// <summary>
    /// Get files in the Skipped folder.
    /// </summary>
    /// <param name="domain">Optional filter by domain</param>
    /// <param name="fileTypeCode">Optional filter by file type</param>
    /// <param name="maxRecords">Maximum records to return</param>
    /// <param name="context">Security context</param>
    /// <returns>List of skipped files</returns>
    Task<DataResult<List<FileWithStatus>>> GetSkippedFilesAsync(
        string? domain,
        string? fileTypeCode,
        int maxRecords,
        SecurityContext context);

    // ============================================
    // Parser Configuration
    // ============================================

    /// <summary>
    /// Get all generic parser configurations.
    /// </summary>
    /// <param name="activeOnly">If true, only return active configs</param>
    /// <param name="context">Security context</param>
    Task<DataResult<List<GenericFileFormatConfig>>> GetParserConfigsAsync(bool? activeOnly, SecurityContext context);

    /// <summary>
    /// Get a single generic parser configuration with column mappings.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="context">Security context</param>
    Task<DataResult<GenericFileFormatConfig>> GetParserConfigAsync(string fileTypeCode, SecurityContext context);

    /// <summary>
    /// Create or update a generic parser configuration (upsert).
    /// </summary>
    /// <param name="request">Parser config request</param>
    /// <param name="context">Security context</param>
    Task<DataResult<GenericFileFormatConfig>> SaveParserConfigAsync(GenericParserConfigRequest request, SecurityContext context);

    /// <summary>
    /// Delete a generic parser configuration and its column mappings.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="context">Security context</param>
    Task<DataResult<bool>> DeleteParserConfigAsync(string fileTypeCode, SecurityContext context);

    // ============================================
    // Lookup Tables: Vendors
    // ============================================

    Task<DataResult<List<VendorRecord>>> GetVendorsAsync(SecurityContext context);
    Task<DataResult<VendorRecord>> GetVendorAsync(string networkId, SecurityContext context);
    Task<DataResult<VendorRecord>> SaveVendorAsync(VendorRecord record, SecurityContext context);
    Task<DataResult<bool>> DeleteVendorAsync(string networkId, SecurityContext context);

    // ============================================
    // Lookup Tables: File Classes
    // ============================================

    Task<DataResult<List<FileClassRecord>>> GetFileClassesAsync(SecurityContext context);
    Task<DataResult<FileClassRecord>> GetFileClassAsync(string fileClassCode, SecurityContext context);
    Task<DataResult<FileClassRecord>> SaveFileClassAsync(FileClassRecord record, SecurityContext context);
    Task<DataResult<bool>> DeleteFileClassAsync(string fileClassCode, SecurityContext context);

    // ============================================
    // Lookup Tables: File Types
    // ============================================

    Task<DataResult<List<FileTypeRecord>>> GetFileTypesAsync(SecurityContext context);
    Task<DataResult<FileTypeRecord>> GetFileTypeAsync(string fileTypeCode, SecurityContext context);
    Task<DataResult<FileTypeRecord>> SaveFileTypeAsync(FileTypeRecord record, SecurityContext context);
    Task<DataResult<bool>> DeleteFileTypeAsync(string fileTypeCode, SecurityContext context);

    // ============================================
    // Lookup Tables: File Type NT
    // ============================================

    Task<DataResult<List<FileTypeNtRecord>>> GetFileTypeNtRecordsAsync(string? fileTypeCode, SecurityContext context);
    Task<DataResult<FileTypeNtRecord>> GetFileTypeNtAsync(string fileTypeCode, SecurityContext context);
    Task<DataResult<FileTypeNtRecord>> SaveFileTypeNtAsync(FileTypeNtRecord record, SecurityContext context);
    Task<DataResult<bool>> DeleteFileTypeNtAsync(string fileTypeCode, SecurityContext context);
}

/// <summary>
/// Result for file download operation.
/// </summary>
public class FileDownloadResult
{
    /// <summary>File stream.</summary>
    public Stream Stream { get; set; } = Stream.Null;

    /// <summary>Content type (MIME type).</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>File name for download.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long FileSize { get; set; }
}
