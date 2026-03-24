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
    /// <param name="fileTypeCode">Optional filter by file type</param>
    /// <param name="context">Security context</param>
    /// <returns>Dashboard summary</returns>
    Task<DataResult<FileManagementDashboard>> GetDashboardAsync(
        string? fileTypeCode,
        SecurityContext context);

    /// <summary>
    /// Search NT files by file number or file name for autocomplete.
    /// </summary>
    /// <param name="search">Search term (min 3 characters)</param>
    /// <param name="context">Security context</param>
    Task<DataResult<List<NtFileSearchResult>>> SearchNtFilesAsync(string search, SecurityContext context);

    /// <summary>
    /// List files with filtering and pagination.
    /// </summary>
    /// <param name="filter">Filter criteria</param>
    /// <param name="context">Security context</param>
    /// <returns>List of files with status</returns>
    Task<DataResult<FileWithStatusResponse>> ListFilesAsync(
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
    /// <param name="skipRecords">Number of records to skip</param>
    /// <param name="takeRecords">Number of records to return</param>
    /// <param name="countRecords">Count flag: Y=yes, N=no, F=first page only</param>
    /// <param name="context">Security context</param>
    /// <returns>Paged activity log response</returns>
    Task<DataResult<ActivityLogResponse>> GetActivityLogAsync(
        int? ntFileNum,
        int? transferId,
        int skipRecords,
        int takeRecords,
        string countRecords,
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
    /// <param name="fileTypeCode">Optional filter by file type</param>
    /// <param name="skipRecords">Number of records to skip</param>
    /// <param name="takeRecords">Number of records to return</param>
    /// <param name="countRecords">Count flag: Y=yes, N=no, F=first page only</param>
    /// <param name="context">Security context</param>
    /// <returns>Paged file-with-status response</returns>
    Task<DataResult<FileWithStatusResponse>> GetFilesWithErrorsAsync(
        string? fileTypeCode,
        int skipRecords,
        int takeRecords,
        string countRecords,
        SecurityContext context);

    /// <summary>
    /// Get files in the Skipped folder.
    /// </summary>
    /// <param name="fileTypeCode">Optional filter by file type</param>
    /// <param name="skipRecords">Number of records to skip</param>
    /// <param name="takeRecords">Number of records to return</param>
    /// <param name="countRecords">Count flag: Y=yes, N=no, F=first page only</param>
    /// <param name="context">Security context</param>
    /// <returns>Paged file-with-status response</returns>
    Task<DataResult<FileWithStatusResponse>> GetSkippedFilesAsync(
        string? fileTypeCode,
        int skipRecords,
        int takeRecords,
        string countRecords,
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
    Task<DataResult<GenericFileFormatConfig>> CreateParserConfigAsync(GenericParserConfigRequest request, SecurityContext context);
    Task<DataResult<GenericFileFormatConfig>> UpdateParserConfigAsync(string fileTypeCode, GenericParserConfigRequest request, SecurityContext context);

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
    Task<DataResult<VendorRecord>> CreateVendorAsync(VendorRecord record, SecurityContext context);
    Task<DataResult<VendorRecord>> UpdateVendorAsync(string networkId, VendorRecord record, SecurityContext context);
    Task<DataResult<bool>> DeleteVendorAsync(string networkId, SecurityContext context);

    // ============================================
    // Lookup Tables: File Classes
    // ============================================

    Task<DataResult<List<FileClassRecord>>> GetFileClassesAsync(SecurityContext context);
    Task<DataResult<FileClassRecord>> GetFileClassAsync(string fileClassCode, SecurityContext context);
    Task<DataResult<FileClassRecord>> CreateFileClassAsync(FileClassRecord record, SecurityContext context);
    Task<DataResult<FileClassRecord>> UpdateFileClassAsync(string fileClassCode, FileClassRecord record, SecurityContext context);
    Task<DataResult<bool>> DeleteFileClassAsync(string fileClassCode, SecurityContext context);

    // ============================================
    // Lookup Tables: File Types
    // ============================================

    Task<DataResult<List<FileTypeRecord>>> GetFileTypesAsync(SecurityContext context);
    Task<DataResult<FileTypeRecord>> GetFileTypeAsync(string fileTypeCode, SecurityContext context);
    Task<DataResult<FileTypeRecord>> CreateFileTypeAsync(FileTypeRecord record, SecurityContext context);
    Task<DataResult<FileTypeRecord>> UpdateFileTypeAsync(string fileTypeCode, FileTypeRecord record, SecurityContext context);
    Task<DataResult<bool>> DeleteFileTypeAsync(string fileTypeCode, SecurityContext context);

    // ============================================
    // Lookup Tables: File Type NT
    // ============================================

    Task<DataResult<List<FileTypeNtRecord>>> GetFileTypeNtRecordsAsync(string? fileTypeCode, SecurityContext context);
    Task<DataResult<FileTypeNtRecord>> GetFileTypeNtAsync(string fileTypeCode, SecurityContext context);
    Task<DataResult<FileTypeNtRecord>> CreateFileTypeNtAsync(FileTypeNtRecord record, SecurityContext context);
    Task<DataResult<FileTypeNtRecord>> UpdateFileTypeNtAsync(string fileTypeCode, FileTypeNtRecord record, SecurityContext context);
    Task<DataResult<bool>> DeleteFileTypeNtAsync(string fileTypeCode, SecurityContext context);

    // ============================================
    // Charge Mappings (ntfl_chg_map)
    // ============================================

    Task<DataResult<List<NtflChgMapRecord>>> GetChargeMapsAsync(string fileTypeCode, SecurityContext context);
    Task<DataResult<NtflChgMapRecord>> GetChargeMapAsync(int id, SecurityContext context);
    Task<DataResult<NtflChgMapRecord>> CreateChargeMapAsync(NtflChgMapRequest request, SecurityContext context);
    Task<DataResult<NtflChgMapRecord>> UpdateChargeMapAsync(int id, NtflChgMapRequest request, SecurityContext context);
    Task<DataResult<bool>> DeleteChargeMapAsync(int id, SecurityContext context);
    Task<DataResult<ChargeMapMatch?>> ResolveChargeMapAsync(string fileTypeCode, string chargeDescription, SecurityContext context);

    // ============================================
    // Configuration Readiness
    // ============================================

    /// <summary>Get holistic configuration readiness status for a file type.</summary>
    Task<DataResult<FileTypeReadinessResponse>> GetReadinessAsync(string fileTypeCode, SecurityContext context);

    // ============================================
    // Custom Table Management
    // ============================================

    /// <summary>Get all custom table versions for a file type.</summary>
    Task<DataResult<CustomTableInfo>> GetCustomTableInfoAsync(string fileTypeCode, SecurityContext context);

    /// <summary>Propose a new custom table based on current column mappings (no DB changes).</summary>
    Task<DataResult<CustomTableProposal>> ProposeCustomTableAsync(string fileTypeCode, SecurityContext context);

    /// <summary>Create the custom table in the database.</summary>
    Task<DataResult<CustomTableMetadata>> CreateCustomTableAsync(string fileTypeCode, SecurityContext context);

    /// <summary>Create a new version of the custom table (retires current version).</summary>
    Task<DataResult<CustomTableMetadata>> CreateCustomTableNewVersionAsync(string fileTypeCode, SecurityContext context);

    /// <summary>Drop a specific version of a custom table (only if empty).</summary>
    Task<DataResult<bool>> DropCustomTableVersionAsync(string fileTypeCode, int version, SecurityContext context);

    /// <summary>Get live record count for a custom table version.</summary>
    Task<DataResult<int>> GetCustomTableRecordCountAsync(string fileTypeCode, int version, SecurityContext context);

    /// <summary>Test load a file into the custom table.</summary>
    Task<DataResult<TestLoadResult>> TestLoadCustomTableAsync(string fileTypeCode, Stream fileStream, string fileName, SecurityContext context);

    /// <summary>Delete a test-loaded file's records from the custom table.</summary>
    Task<DataResult<bool>> DeleteTestLoadAsync(string fileTypeCode, int ntFileNum, SecurityContext context);
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
