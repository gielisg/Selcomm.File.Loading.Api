using Microsoft.AspNetCore.Http;
using FileLoading.Models;
using Selcomm.Data.Common;

namespace FileLoading.Interfaces;

/// <summary>
/// File Loader Service interface (from ntfileload.4gl).
/// Loads and processes network files of various types.
/// </summary>
public interface IFileLoaderService
{
    /// <summary>
    /// Load a file for processing.
    /// </summary>
    Task<DataResult<FileLoadResponse>> LoadFileAsync(LoadFileRequest request, SecurityContext securityContext);

    /// <summary>
    /// Upload and load a file.
    /// </summary>
    Task<DataResult<FileLoadResponse>> UploadFileAsync(IFormFile file, string fileType, SecurityContext securityContext);

    /// <summary>
    /// Get file status by nt_file_num.
    /// </summary>
    Task<DataResult<FileStatusResponse>> GetFileStatusAsync(int ntFileNum, SecurityContext securityContext);

    /// <summary>
    /// List files with filtering.
    /// Uses ss_file_loading_nt_file_api stored procedure.
    /// </summary>
    /// <param name="fileTypeCode">Optional file type filter</param>
    /// <param name="ntCustNum">Optional customer number filter</param>
    /// <param name="skipRecords">Number of records to skip (default 0)</param>
    /// <param name="takeRecords">Number of records to return (default 20, max 100)</param>
    /// <param name="countRecords">Count flag: Y=yes, N=no, F=first page only</param>
    /// <param name="securityContext">Security context</param>
    Task<DataResult<FileListResponse>> ListFilesAsync(
        string? fileTypeCode,
        string? ntCustNum,
        int skipRecords,
        int takeRecords,
        string countRecords,
        SecurityContext securityContext,
        int? statusId = null);

    /// <summary>
    /// List supported file types.
    /// </summary>
    Task<DataResult<FileTypeListResponse>> ListFileTypesAsync(SecurityContext securityContext);

    /// <summary>
    /// Reprocess a file.
    /// </summary>
    Task<DataResult<FileLoadResponse>> ReprocessFileAsync(int ntFileNum, SecurityContext securityContext);
}

/// <summary>
/// File parser interface for different file formats.
/// </summary>
public interface IFileParser
{
    /// <summary>File type code.</summary>
    string FileType { get; }

    /// <summary>File class code (CDR, CHG, EBL, SVC, etc.).</summary>
    string FileClassCode { get; }

    /// <summary>Parse file and return records.</summary>
    Task<ParseResult> ParseAsync(Stream fileStream, ParseContext context);

    /// <summary>Validate file header/trailer.</summary>
    ValidationResult ValidateFile(Stream fileStream);

    /// <summary>
    /// Streaming validation pass - validates file structure without accumulating records in memory.
    /// Only checks header/trailer rules, counts records, and collects file-level errors.
    /// </summary>
    Task<StreamingValidationResult> ValidateFileStreamingAsync(
        Stream fileStream,
        ParseContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming parse pass - parses and yields records one at a time without accumulating in memory.
    /// Call this AFTER ValidateFileStreamingAsync passes validation.
    /// </summary>
    IAsyncEnumerable<ParsedRecord> ParseRecordsStreamingAsync(
        Stream fileStream,
        ParseContext context,
        CancellationToken cancellationToken = default);
}
