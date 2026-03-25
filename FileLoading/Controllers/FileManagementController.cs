using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Selcomm.Data.Common;
using FileLoading.Interfaces;
using FileLoading.Models;
using FileLoading.Validation;
using FileLoading.Data;

namespace FileLoading.Controllers;

/// <summary>
/// File Management API - manages file transfers, workflow, and monitoring.
/// </summary>
[ApiController]
[Route("api/v4/file-loading")]
[Authorize(Policy = "MultiAuth")]
[Produces("application/json")]
public class FileManagementController : DbControllerBase<FileLoaderDbContext>
{
    private readonly IFileManagementService _managementService;
    private readonly IFileTransferService _transferService;
    private readonly FileLoaderDbContext _dbContext;
    private readonly ILogger<FileManagementController> _logger;

    public FileManagementController(
        IFileManagementService managementService,
        IFileTransferService transferService,
        FileLoaderDbContext dbContext,
        ILogger<FileManagementController> logger)
    {
        _managementService = managementService;
        _transferService = transferService;
        _dbContext = dbContext;
        _logger = logger;
    }

    protected override FileLoaderDbContext DbContext => _dbContext;

    // ============================================
    // Dashboard
    // ============================================

    /// <summary>
    /// Get dashboard summary data including file counts by folder and transfer source statuses.
    /// </summary>
    /// <param name="fileType">Filter by file type code</param>
    /// <response code="200">Dashboard data returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("dashboard")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_dashboard")]
    [Tags("File Management")]
    [ProducesResponseType(typeof(FileManagementDashboard), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery(Name = "fileType")] string? fileType = null)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_dashboard");
            var result = await _managementService.GetDashboardAsync(fileType, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard for fileType={FileType}", fileType);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Files (workflow)
    // ============================================

    /// <summary>
    /// Search NT files by file number or name for autocomplete.
    /// Returns up to 10 matching results.
    /// </summary>
    /// <param name="search">Search term (minimum 3 characters)</param>
    /// <response code="200">Matching files returned successfully</response>
    /// <response code="400">Search term too short</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("files/search")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_files_search")]
    [Tags("File Management")]
    [ProducesResponseType(typeof(List<NtFileSearchResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SearchFiles(
        [FromQuery(Name = "search")] string search)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 3)
            {
                return BadRequest(new ErrorResponse("Search term must be at least 3 characters", "VALIDATION_ERROR"));
            }

            var securityContext = CreateSecurityContext("get_api_v4_file_loading_files_search");
            var result = await _managementService.SearchNtFilesAsync(search.Trim(), securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching files for search={Search}", search);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// List files in the transfer workflow with filtering.
    /// </summary>
    /// <param name="request">Filter parameters including file type, folder, status, date range, and filename search</param>
    /// <response code="200">File list returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("manager/files")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_manager_files")]
    [Tags("File Management")]
    [ProducesResponseType(typeof(FileWithStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListFiles([FromQuery] FileListFilterRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_manager_files");

            var filter = new FileListFilter
            {
                FileTypeCode = request.FileType,
                CurrentFolder = request.Folder,
                Status = request.Status,
                FromDate = request.FromDate,
                ToDate = request.ToDate,
                FileNameSearch = request.Search,
                SkipRecords = request.SkipRecords,
                TakeRecords = request.TakeRecords,
                CountRecords = request.CountRecords ?? "F"
            };

            var result = await _managementService.ListFilesAsync(filter, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get file details by transfer-id (the workflow tracking key).
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <response code="200">File details returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Transfer record not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("manager/files/{transfer-id}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_manager_files_transfer_id")]
    [Tags("File Management")]
    [ProducesResponseType(typeof(FileWithStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFileDetails([FromRoute(Name = "transfer-id")] int transferId)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_manager_files_transfer_id");
            var result = await _managementService.GetFileDetailsAsync(transferId, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file details for transferId={TransferId}", transferId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Transfer Operations
    // ============================================

    /// <summary>
    /// Fetch files from a transfer source (SFTP/FTP/FileSystem).
    /// </summary>
    /// <param name="sourceId">Transfer source ID (auto-generated integer)</param>
    /// <response code="200">Files fetched successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Transfer source not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("transfers/{source-id}/fetch")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_transfers_source_id_fetch")]
    [Tags("Transfer Operations")]
    [ProducesResponseType(typeof(TransferFetchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> FetchFromSource([FromRoute(Name = "source-id")] int sourceId)
    {
        try
        {
            _logger.LogInformation("Fetching files from source: {SourceId}", sourceId);

            var securityContext = CreateSecurityContext("post_api_v4_file_loading_transfers_source_id_fetch");
            var result = await _transferService.FetchFilesFromSourceAsync(sourceId, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching files from source sourceId={SourceId}", sourceId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Process a file from the transfer workflow.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <response code="200">File processed successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Transfer record not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("manager/files/{transfer-id}/process")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_manager_files_transfer_id_process")]
    [Tags("File Management")]
    [ProducesResponseType(typeof(FileLoadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ProcessFile([FromRoute(Name = "transfer-id")] int transferId)
    {
        try
        {
            _logger.LogInformation("Processing file: {TransferId}", transferId);

            var securityContext = CreateSecurityContext("post_api_v4_file_loading_manager_files_transfer_id_process");
            var result = await _managementService.ProcessFileAsync(transferId, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file transferId={TransferId}", transferId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Retry processing a failed file.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <response code="200">File reprocessed successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Transfer record not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("manager/files/{transfer-id}/retry")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_manager_files_transfer_id_retry")]
    [Tags("File Management")]
    [ProducesResponseType(typeof(FileLoadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RetryProcessing([FromRoute(Name = "transfer-id")] int transferId)
    {
        try
        {
            _logger.LogInformation("Retrying file: {TransferId}", transferId);

            var securityContext = CreateSecurityContext("post_api_v4_file_loading_manager_files_transfer_id_retry");
            var result = await _managementService.RetryProcessingAsync(transferId, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrying processing for transferId={TransferId}", transferId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Move a file to a specific workflow folder (Transfer, Processing, Processed, Errors, Skipped).
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <param name="folder">Target folder name</param>
    /// <param name="reason">Optional reason for the move</param>
    /// <response code="200">File moved successfully</response>
    /// <response code="400">Invalid folder name</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Transfer record not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("manager/files/{transfer-id}/move")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_manager_files_transfer_id_move")]
    [Tags("File Management")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> MoveFile(
        [FromRoute(Name = "transfer-id")] int transferId,
        [FromQuery] string folder,
        [FromQuery] string? reason = null)
    {
        try
        {
            _logger.LogInformation("Moving file {TransferId} to {Folder}", transferId, folder);

            var securityContext = CreateSecurityContext("post_api_v4_file_loading_manager_files_transfer_id_move");
            var result = await _managementService.MoveFileAsync(transferId, folder, reason, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error moving file transferId={TransferId} to folder={Folder}", transferId, folder);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Unload a loaded file (reverse the load operation).
    /// </summary>
    /// <param name="ntFileNum">File number (nt_file_num) — the database record key</param>
    /// <response code="200">File unloaded successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File record not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("manager/files/{nt-file-num}/unload")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_manager_files_nt_file_num_unload")]
    [Tags("File Management")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UnloadFile([FromRoute(Name = "nt-file-num")] int ntFileNum)
    {
        try
        {
            _logger.LogInformation("Unloading file: {NtFileNum}", ntFileNum);

            var securityContext = CreateSecurityContext("post_api_v4_file_loading_manager_files_nt_file_num_unload");
            var result = await _managementService.UnloadFileAsync(ntFileNum, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unloading file ntFileNum={NtFileNum}", ntFileNum);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Force skip a sequence number.
    /// </summary>
    /// <param name="ntFileNum">File number (nt_file_num) — the database record key</param>
    /// <param name="skipToSeq">Sequence number to skip to</param>
    /// <param name="reason">Optional reason for skipping</param>
    /// <response code="200">Sequence skipped successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File record not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("manager/files/{nt-file-num}/skip-sequence")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_manager_files_nt_file_num_skip_sequence")]
    [Tags("File Management")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ForceSequenceSkip(
        [FromRoute(Name = "nt-file-num")] int ntFileNum,
        [FromQuery(Name = "skipTo")] int skipToSeq,
        [FromQuery] string? reason = null)
    {
        try
        {
            _logger.LogInformation("Skipping sequence for file {NtFileNum} to {Seq}", ntFileNum, skipToSeq);

            var securityContext = CreateSecurityContext("post_api_v4_file_loading_manager_files_nt_file_num_skip_sequence");
            var result = await _managementService.ForceSequenceSkipAsync(ntFileNum, skipToSeq, reason, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error skipping sequence for ntFileNum={NtFileNum} to seq={SkipToSeq}", ntFileNum, skipToSeq);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Download a file to browser.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <response code="200">File content returned as download</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Transfer record not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("manager/files/{transfer-id}/download")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_manager_files_transfer_id_download")]
    [Tags("File Management")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadFile([FromRoute(Name = "transfer-id")] int transferId)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_manager_files_transfer_id_download");
            var result = await _managementService.DownloadFileAsync(transferId, securityContext);

            if (!result.IsSuccess || result.Data == null)
            {
                return StatusCode(result.StatusCode,
                    new ErrorResponse(result.ErrorMessage ?? "Download failed", result.ErrorCode));
            }

            return File(result.Data.Stream, result.Data.ContentType, result.Data.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file transferId={TransferId}", transferId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Delete a file from the workflow.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <response code="200">File deleted successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Transfer record not found</response>
    /// <response code="500">Internal server error</response>
    [HttpDelete("manager/files/{transfer-id}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_manager_files_transfer_id")]
    [Tags("File Management")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteFile([FromRoute(Name = "transfer-id")] int transferId)
    {
        try
        {
            _logger.LogInformation("Deleting file: {TransferId}", transferId);

            var securityContext = CreateSecurityContext("delete_api_v4_file_loading_manager_files_transfer_id");
            var result = await _managementService.DeleteFileAsync(transferId, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file transferId={TransferId}", transferId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Activity Log
    // ============================================

    /// <summary>
    /// Get activity log entries. Filter by nt-file-num, transfer-id, or both.
    /// </summary>
    /// <param name="ntFileNum">Filter by file number</param>
    /// <param name="transferId">Filter by transfer record ID</param>
    /// <param name="skipRecords">Number of records to skip (default 0)</param>
    /// <param name="takeRecords">Number of records to return (default 20)</param>
    /// <param name="countRecords">Include total count: Y=yes, N=no, F=first page only (default F)</param>
    /// <response code="200">Activity log entries returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("activity")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_activity")]
    [Tags("Activity Log")]
    [ProducesResponseType(typeof(ActivityLogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetActivityLog(
        [FromQuery(Name = "ntFileNum")] int? ntFileNum = null,
        [FromQuery(Name = "transferId")] int? transferId = null,
        [FromQuery(Name = "skipRecords")] int skipRecords = 0,
        [FromQuery(Name = "takeRecords")] int takeRecords = 20,
        [FromQuery(Name = "countRecords")] string countRecords = "F")
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_activity");
            var result = await _managementService.GetActivityLogAsync(ntFileNum, transferId, skipRecords, takeRecords, countRecords, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting activity log for ntFileNum={NtFileNum}, transferId={TransferId}", ntFileNum, transferId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Validation Summary
    // ============================================

    /// <summary>
    /// Get AI-friendly validation summary for a loaded file.
    /// </summary>
    /// <param name="ntFileNum">File number (nt_file_num)</param>
    /// <response code="200">Validation summary returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File record not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("files/{nt-file-num}/validation-summary")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_files_nt_file_num_validation_summary")]
    [Tags("Validation")]
    [ProducesResponseType(typeof(ValidationSummaryForAI), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetValidationSummary([FromRoute(Name = "nt-file-num")] int ntFileNum)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_files_nt_file_num_validation_summary");
            var result = await _managementService.GetValidationSummaryAsync(ntFileNum, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting validation summary for ntFileNum={NtFileNum}", ntFileNum);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Exceptions View
    // ============================================

    /// <summary>
    /// Get files with processing errors.
    /// </summary>
    /// <param name="fileType">Filter by file type code</param>
    /// <param name="skipRecords">Number of records to skip (default 0)</param>
    /// <param name="takeRecords">Number of records to return (default 20)</param>
    /// <param name="countRecords">Include total count: Y=yes, N=no, F=first page only (default F)</param>
    /// <response code="200">Error files returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("exceptions/errors")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_exceptions_errors")]
    [Tags("Exceptions")]
    [ProducesResponseType(typeof(FileWithStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFilesWithErrors(
        [FromQuery(Name = "fileType")] string? fileType = null,
        [FromQuery(Name = "skipRecords")] int skipRecords = 0,
        [FromQuery(Name = "takeRecords")] int takeRecords = 20,
        [FromQuery(Name = "countRecords")] string countRecords = "F")
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_exceptions_errors");
            var result = await _managementService.GetFilesWithErrorsAsync(fileType, skipRecords, takeRecords, countRecords, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting files with errors for fileType={FileType}", fileType);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get skipped files.
    /// </summary>
    /// <param name="fileType">Filter by file type code</param>
    /// <param name="skipRecords">Number of records to skip (default 0)</param>
    /// <param name="takeRecords">Number of records to return (default 20)</param>
    /// <param name="countRecords">Include total count: Y=yes, N=no, F=first page only (default F)</param>
    /// <response code="200">Skipped files returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("exceptions/skipped")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_exceptions_skipped")]
    [Tags("Exceptions")]
    [ProducesResponseType(typeof(FileWithStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSkippedFiles(
        [FromQuery(Name = "fileType")] string? fileType = null,
        [FromQuery(Name = "skipRecords")] int skipRecords = 0,
        [FromQuery(Name = "takeRecords")] int takeRecords = 20,
        [FromQuery(Name = "countRecords")] string countRecords = "F")
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_exceptions_skipped");
            var result = await _managementService.GetSkippedFilesAsync(fileType, skipRecords, takeRecords, countRecords, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting skipped files for fileType={FileType}", fileType);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Transfer Sources (CRUD)
    // ============================================

    /// <summary>
    /// List all transfer source configurations.
    /// </summary>
    /// <response code="200">Transfer sources returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("sources")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_sources")]
    [Tags("Transfer Sources")]
    [ProducesResponseType(typeof(List<TransferSourceConfig>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetTransferSources()
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_sources");
            var result = await _transferService.GetSourceConfigsAsync(securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transfer sources");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get a specific transfer source configuration.
    /// </summary>
    /// <param name="sourceId">Source ID (auto-generated integer)</param>
    /// <response code="200">Transfer source returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Transfer source not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("sources/{source-id}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_sources_source_id")]
    [Tags("Transfer Sources")]
    [ProducesResponseType(typeof(TransferSourceConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetTransferSource([FromRoute(Name = "source-id")] int sourceId)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_sources_source_id");
            var result = await _transferService.GetSourceConfigAsync(sourceId, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transfer source sourceId={SourceId}", sourceId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Create or update a transfer source. Use source-id=0 to create (ID is auto-generated).
    /// </summary>
    /// <param name="sourceId">Source ID (0 for create, existing ID for update)</param>
    /// <param name="request">Transfer source configuration</param>
    /// <response code="200">Transfer source saved successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpPut("sources/{source-id}")]
    [SwaggerOperation(OperationId = "put_api_v4_file_loading_sources_source_id")]
    [Tags("Transfer Sources")]
    [ProducesResponseType(typeof(TransferSourceConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SaveTransferSource(
        [FromRoute(Name = "source-id")] int sourceId,
        [FromBody] TransferSourceRequest request)
    {
        try
        {
            request.SourceId = sourceId;

            var securityContext = CreateSecurityContext("put_api_v4_file_loading_sources_source_id");
            var result = await _transferService.SaveSourceConfigAsync(request, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving transfer source sourceId={SourceId}", sourceId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Delete a transfer source configuration.
    /// </summary>
    /// <param name="sourceId">Source ID</param>
    /// <response code="200">Transfer source deleted successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Transfer source not found</response>
    /// <response code="500">Internal server error</response>
    [HttpDelete("sources/{source-id}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_sources_source_id")]
    [Tags("Transfer Sources")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteTransferSource([FromRoute(Name = "source-id")] int sourceId)
    {
        try
        {
            var securityContext = CreateSecurityContext("delete_api_v4_file_loading_sources_source_id");
            var result = await _transferService.DeleteSourceConfigAsync(sourceId, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting transfer source sourceId={SourceId}", sourceId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Test connection to a saved transfer source.
    /// </summary>
    /// <param name="sourceId">Source ID</param>
    /// <response code="200">Connection test successful</response>
    /// <response code="400">Connection test failed</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("sources/{source-id}/test")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_sources_source_id_test")]
    [Tags("Transfer Sources")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TestSourceConnection([FromRoute(Name = "source-id")] int sourceId)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_sources_source_id_test");
            var result = await _transferService.TestConnectionAsync(sourceId, securityContext);

            if (!result.IsSuccess || !result.Data)
            {
                return BadRequest(new ErrorResponse(result.ErrorMessage ?? "Connection test failed", result.ErrorCode));
            }

            return Ok(new { Success = true, Message = "Connection successful" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing source connection sourceId={SourceId}", sourceId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Test connection with provided configuration (without saving).
    /// </summary>
    /// <param name="request">Transfer source configuration to test</param>
    /// <response code="200">Connection test successful</response>
    /// <response code="400">Connection test failed</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("sources/test")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_sources_test")]
    [Tags("Transfer Sources")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TestSourceConnectionWithConfig([FromBody] TransferSourceRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_sources_test");
            var result = await _transferService.TestConnectionAsync(request, securityContext);

            if (!result.IsSuccess || !result.Data)
            {
                return BadRequest(new ErrorResponse(result.ErrorMessage ?? "Connection test failed", result.ErrorCode));
            }

            return Ok(new { Success = true, Message = "Connection successful" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing source connection with config");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Parser Configuration (CRUD)
    // ============================================

    /// <summary>
    /// List all generic parser configurations.
    /// </summary>
    /// <param name="active">Filter by active status</param>
    /// <response code="200">Parser configurations returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("parsers")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_parsers")]
    [Tags("Parser Configuration")]
    [ProducesResponseType(typeof(List<GenericFileFormatConfig>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetParserConfigs([FromQuery] bool? active = null)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_parsers");
            var result = await _managementService.GetParserConfigsAsync(active, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parser configs");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get a specific generic parser configuration with column mappings.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="200">Parser configuration returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Parser configuration not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("parsers/{file-type-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_parsers_file_type_code")]
    [Tags("Parser Configuration")]
    [ProducesResponseType(typeof(GenericFileFormatConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetParserConfig([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_parsers_file_type_code");
            var result = await _managementService.GetParserConfigAsync(fileTypeCode, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parser config for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Create a new generic parser configuration.
    /// </summary>
    /// <param name="request">Parser configuration with column mappings</param>
    /// <response code="201">Parser configuration created successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="409">Parser configuration already exists for this file type code</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("parsers")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_parsers")]
    [Tags("Parser Configuration")]
    [ProducesResponseType(typeof(GenericFileFormatConfig), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateParserConfig(
        [FromBody] GenericParserConfigRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_parsers");
            var result = await _managementService.CreateParserConfigAsync(request, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating parser config for fileTypeCode={FileTypeCode}", request.FileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Update an existing parser configuration.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="request">Parser configuration with column mappings</param>
    /// <response code="200">Parser configuration updated successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Parser configuration not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPatch("parsers/{file-type-code}")]
    [SwaggerOperation(OperationId = "patch_api_v4_file_loading_parsers_file_type_code")]
    [Tags("Parser Configuration")]
    [ProducesResponseType(typeof(GenericFileFormatConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateParserConfig(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromBody] GenericParserConfigRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("patch_api_v4_file_loading_parsers_file_type_code");
            var result = await _managementService.UpdateParserConfigAsync(fileTypeCode, request, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating parser config for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Delete a generic parser configuration and its column mappings.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="200">Parser configuration deleted successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Parser configuration not found</response>
    /// <response code="500">Internal server error</response>
    [HttpDelete("parsers/{file-type-code}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_parsers_file_type_code")]
    [Tags("Parser Configuration")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteParserConfig([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("delete_api_v4_file_loading_parsers_file_type_code");
            var result = await _managementService.DeleteParserConfigAsync(fileTypeCode, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting parser config for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Parser Config Versioning
    // ============================================

    /// <summary>
    /// List all parser configuration versions for a file type.
    /// Returns all versions including frozen ones linked to custom tables.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="200">Version list returned</response>
    /// <response code="204">No configs found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("parsers/{file-type-code}/versions")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_parsers_file_type_code_versions")]
    [Tags("Parser Configuration")]
    [ProducesResponseType(typeof(List<GenericFileFormatConfig>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetParserConfigVersions([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_parsers_file_type_code_versions");
            var result = await _managementService.GetParserConfigVersionsAsync(fileTypeCode, securityContext);
            if (result.IsSuccess && (result.Data == null || result.Data.Count == 0))
                return NoContent();
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parser config versions for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get a specific parser configuration version.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="version">Config version number</param>
    /// <response code="200">Config version returned</response>
    /// <response code="404">Version not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("parsers/{file-type-code}/versions/{version}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_parsers_file_type_code_versions_version")]
    [Tags("Parser Configuration")]
    [ProducesResponseType(typeof(GenericFileFormatConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetParserConfigByVersion(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromRoute] int version)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_parsers_file_type_code_versions_version");
            var result = await _managementService.GetParserConfigByVersionAsync(fileTypeCode, version, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parser config v{Version} for fileTypeCode={FileTypeCode}", version, fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Folder Configuration
    // ============================================

    /// <summary>
    /// Get folder workflow configuration. Falls back to default if file-type specific config not found.
    /// </summary>
    /// <param name="fileType">File type code (optional — falls back to default)</param>
    /// <response code="200">Folder configuration returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Folder configuration not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("folders")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_folders")]
    [Tags("Folder Configuration")]
    [ProducesResponseType(typeof(FolderWorkflowConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFolderConfig(
        [FromQuery(Name = "fileType")] string? fileType = null)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_folders");
            var result = await _transferService.GetFolderConfigAsync(fileType, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting folder config for fileType={FileType}", fileType);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Create or update folder workflow configuration. Paths are derived from file type and current storage mode. Auto-creates folders on save.
    /// </summary>
    /// <param name="fileType">File type code</param>
    /// <response code="200">Folder configuration saved successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpPatch("folders")]
    [SwaggerOperation(OperationId = "patch_api_v4_file_loading_folders")]
    [Tags("Folder Configuration")]
    [ProducesResponseType(typeof(FolderWorkflowConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SaveFolderConfig(
        [FromQuery(Name = "fileType")] string? fileType = null)
    {
        try
        {
            var securityContext = CreateSecurityContext("patch_api_v4_file_loading_folders");
            var result = await _transferService.SaveFolderConfigAsync(fileType, securityContext);

            if (result.IsSuccess)
            {
                // Auto-create folders on save
                await _transferService.CreateFoldersAsync(fileType, securityContext);
            }

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving folder config for fileTypeCode={FileTypeCode}", fileType);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get default folder paths for a file-type combination.
    /// </summary>
    /// <param name="fileType">File type code</param>
    /// <response code="200">Folder defaults returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("folders/defaults")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_folders_defaults")]
    [Tags("Folder Configuration")]
    [ProducesResponseType(typeof(FolderDefaultsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFolderDefaults(
        [FromQuery(Name = "fileType")] string? fileType = null)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_folders_defaults");
            var result = await _transferService.GetDefaultFolderPathsAsync(fileType, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting folder defaults for fileType={FileType}", fileType);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Create all 6 workflow folders for a file-type (local or FTP based on storage config).
    /// </summary>
    /// <param name="fileType">File type code</param>
    /// <response code="200">Folders created successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("folders/create")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_folders_create")]
    [Tags("Folder Configuration")]
    [ProducesResponseType(typeof(FolderCreateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateFolders(
        [FromQuery(Name = "fileType")] string? fileType = null)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_folders_create");
            var result = await _transferService.CreateFoldersAsync(fileType, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating folders for fileType={FileType}", fileType);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // FTP Servers
    // ============================================

    /// <summary>
    /// List all FTP server entities.
    /// </summary>
    /// <response code="200">FTP servers returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("ftp-servers")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ftp_servers")]
    [Tags("FTP Servers")]
    [ProducesResponseType(typeof(List<FtpServer>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFtpServers()
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_ftp_servers");
            var result = await _transferService.GetFtpServersAsync(securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting FTP servers");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get a specific FTP server entity.
    /// </summary>
    /// <param name="serverId">FTP server ID</param>
    /// <response code="200">FTP server returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">FTP server not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("ftp-servers/{serverId}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ftp_servers_by_id")]
    [Tags("FTP Servers")]
    [ProducesResponseType(typeof(FtpServer), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFtpServer([FromRoute] int serverId)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_ftp_servers_by_id");
            var result = await _transferService.GetFtpServerAsync(serverId, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting FTP server {ServerId}", serverId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Create a new FTP server entity. Not activated automatically.
    /// </summary>
    /// <param name="request">FTP server configuration</param>
    /// <response code="201">FTP server created successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("ftp-servers")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ftp_servers")]
    [Tags("FTP Servers")]
    [ProducesResponseType(typeof(FtpServer), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateFtpServer([FromBody] FtpServerRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ftp_servers");
            var result = await _transferService.CreateFtpServerAsync(request, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating FTP server");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Update an FTP server entity. Immutable fields blocked if server is locked (referenced by transfers).
    /// </summary>
    /// <param name="serverId">FTP server ID</param>
    /// <param name="request">Updated FTP server configuration</param>
    /// <response code="200">FTP server updated successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">FTP server not found</response>
    /// <response code="409">Server is locked — immutable fields cannot be changed</response>
    /// <response code="500">Internal server error</response>
    [HttpPatch("ftp-servers/{serverId}")]
    [SwaggerOperation(OperationId = "patch_api_v4_file_loading_ftp_servers")]
    [Tags("FTP Servers")]
    [ProducesResponseType(typeof(FtpServer), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateFtpServer([FromRoute] int serverId, [FromBody] FtpServerRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("patch_api_v4_file_loading_ftp_servers");
            var result = await _transferService.UpdateFtpServerAsync(serverId, request, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating FTP server {ServerId}", serverId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Delete an FTP server entity. Blocked if server is locked (referenced by transfers).
    /// </summary>
    /// <param name="serverId">FTP server ID</param>
    /// <response code="200">FTP server deleted successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">FTP server not found</response>
    /// <response code="409">Server is locked — cannot delete</response>
    /// <response code="500">Internal server error</response>
    [HttpDelete("ftp-servers/{serverId}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_ftp_servers")]
    [Tags("FTP Servers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteFtpServer([FromRoute] int serverId)
    {
        try
        {
            var securityContext = CreateSecurityContext("delete_api_v4_file_loading_ftp_servers");
            var result = await _transferService.DeleteFtpServerAsync(serverId, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting FTP server {ServerId}", serverId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Activate an FTP server as the current storage destination. Deactivates all other servers.
    /// </summary>
    /// <param name="serverId">FTP server ID</param>
    /// <response code="200">FTP server activated successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">FTP server not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("ftp-servers/{serverId}/activate")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ftp_servers_activate")]
    [Tags("FTP Servers")]
    [ProducesResponseType(typeof(FtpServer), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ActivateFtpServer([FromRoute] int serverId)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ftp_servers_activate");
            var result = await _transferService.ActivateFtpServerAsync(serverId, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating FTP server {ServerId}", serverId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Deactivate an FTP server, reverting the domain to local storage mode.
    /// </summary>
    /// <param name="serverId">FTP server ID</param>
    /// <response code="200">FTP server deactivated successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("ftp-servers/{serverId}/deactivate")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ftp_servers_deactivate")]
    [Tags("FTP Servers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeactivateFtpServer([FromRoute] int serverId)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ftp_servers_deactivate");
            var result = await _transferService.DeactivateFtpServerAsync(serverId, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating FTP server {ServerId}", serverId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Test FTP connection with provided configuration (without saving).
    /// </summary>
    /// <param name="request">FTP server configuration to test</param>
    /// <response code="200">Connection test successful</response>
    /// <response code="400">Connection test failed</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("ftp-servers/test")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ftp_servers_test")]
    [Tags("FTP Servers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> TestFtpConnection([FromBody] FtpServerRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ftp_servers_test");
            var result = await _transferService.TestFtpConnectionAsync(request, securityContext);

            if (!result.IsSuccess || !result.Data)
            {
                return BadRequest(new ErrorResponse(result.ErrorMessage ?? "Connection test failed", result.ErrorCode));
            }

            return Ok(new { Success = true, Message = "Connection successful" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing FTP connection");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Vendors (CRUD)
    // ============================================

    /// <summary>
    /// List all vendors (networks table).
    /// </summary>
    /// <response code="200">Vendors returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("vendors")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_vendors")]
    [Tags("Vendors")]
    [ProducesResponseType(typeof(List<VendorRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetVendors()
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_vendors");
            var result = await _managementService.GetVendorsAsync(securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting vendors");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get a specific vendor.
    /// </summary>
    /// <param name="networkId">Network ID (CHAR(2))</param>
    /// <response code="200">Vendor returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Vendor not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("vendors/{network-id}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_vendors_network_id")]
    [Tags("Vendors")]
    [ProducesResponseType(typeof(VendorRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetVendor([FromRoute(Name = "network-id")] string networkId)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_vendors_network_id");
            var result = await _managementService.GetVendorAsync(networkId, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting vendor networkId={NetworkId}", networkId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Create a new vendor.
    /// </summary>
    /// <param name="record">Vendor record</param>
    /// <response code="201">Vendor created successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="409">Vendor already exists with this network ID</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("vendors")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_vendors")]
    [Tags("Vendors")]
    [ProducesResponseType(typeof(VendorRecord), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateVendor(
        [FromBody] VendorRecord record)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_vendors");
            var result = await _managementService.CreateVendorAsync(record, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating vendor networkId={NetworkId}", record.NetworkId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Update an existing vendor.
    /// </summary>
    /// <param name="networkId">Network ID</param>
    /// <param name="record">Vendor record</param>
    /// <response code="200">Vendor updated successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Vendor not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPatch("vendors/{network-id}")]
    [SwaggerOperation(OperationId = "patch_api_v4_file_loading_vendors_network_id")]
    [Tags("Vendors")]
    [ProducesResponseType(typeof(VendorRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateVendor(
        [FromRoute(Name = "network-id")] string networkId,
        [FromBody] VendorRecord record)
    {
        try
        {
            var securityContext = CreateSecurityContext("patch_api_v4_file_loading_vendors_network_id");
            var result = await _managementService.UpdateVendorAsync(networkId, record, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating vendor networkId={NetworkId}", networkId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Delete a vendor.
    /// </summary>
    /// <param name="networkId">Network ID (CHAR(2))</param>
    /// <response code="200">Vendor deleted successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Vendor not found</response>
    /// <response code="500">Internal server error</response>
    [HttpDelete("vendors/{network-id}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_vendors_network_id")]
    [Tags("Vendors")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteVendor([FromRoute(Name = "network-id")] string networkId)
    {
        try
        {
            var securityContext = CreateSecurityContext("delete_api_v4_file_loading_vendors_network_id");
            var result = await _managementService.DeleteVendorAsync(networkId, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting vendor networkId={NetworkId}", networkId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // File Classes (CRUD)
    // ============================================

    /// <summary>
    /// List all file classes (e.g. CDR, CHG). A file class groups related file types.
    /// </summary>
    /// <response code="200">File classes returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("file-classes")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_file_classes")]
    [Tags("File Classes")]
    [ProducesResponseType(typeof(List<FileClassRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFileClasses()
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_file_classes");
            var result = await _managementService.GetFileClassesAsync(securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file classes");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get a specific file class.
    /// </summary>
    /// <param name="fileClassCode">File class code (e.g. CDR, CHG)</param>
    /// <response code="200">File class returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File class not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("file-classes/{file-class-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_file_classes_file_class_code")]
    [Tags("File Classes")]
    [ProducesResponseType(typeof(FileClassRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFileClass([FromRoute(Name = "file-class-code")] string fileClassCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_file_classes_file_class_code");
            var result = await _managementService.GetFileClassAsync(fileClassCode, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file class fileClassCode={FileClassCode}", fileClassCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Create a new file class.
    /// </summary>
    /// <param name="record">File class record</param>
    /// <response code="201">File class created successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="409">File class already exists with this code</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("file-classes")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_file_classes")]
    [Tags("File Classes")]
    [ProducesResponseType(typeof(FileClassRecord), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateFileClass(
        [FromBody] FileClassRecord record)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_file_classes");
            var result = await _managementService.CreateFileClassAsync(record, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating file class fileClassCode={FileClassCode}", record.FileClassCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Update an existing file class.
    /// </summary>
    /// <param name="fileClassCode">File class code</param>
    /// <param name="record">File class record</param>
    /// <response code="200">File class updated successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File class not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPatch("file-classes/{file-class-code}")]
    [SwaggerOperation(OperationId = "patch_api_v4_file_loading_file_classes_file_class_code")]
    [Tags("File Classes")]
    [ProducesResponseType(typeof(FileClassRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateFileClass(
        [FromRoute(Name = "file-class-code")] string fileClassCode,
        [FromBody] FileClassRecord record)
    {
        try
        {
            var securityContext = CreateSecurityContext("patch_api_v4_file_loading_file_classes_file_class_code");
            var result = await _managementService.UpdateFileClassAsync(fileClassCode, record, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating file class fileClassCode={FileClassCode}", fileClassCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Delete a file class.
    /// </summary>
    /// <param name="fileClassCode">File class code</param>
    /// <response code="200">File class deleted successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File class not found</response>
    /// <response code="500">Internal server error</response>
    [HttpDelete("file-classes/{file-class-code}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_file_classes_file_class_code")]
    [Tags("File Classes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteFileClass([FromRoute(Name = "file-class-code")] string fileClassCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("delete_api_v4_file_loading_file_classes_file_class_code");
            var result = await _managementService.DeleteFileClassAsync(fileClassCode, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file class fileClassCode={FileClassCode}", fileClassCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // File Types (CRUD)
    // ============================================

    /// <summary>
    /// List all file types. Each file type belongs to a file class and optionally a vendor.
    /// </summary>
    /// <response code="200">File types returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("manager/file-types")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_manager_file_types")]
    [Tags("File Types")]
    [ProducesResponseType(typeof(List<FileTypeRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFileTypes()
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_manager_file_types");
            var result = await _managementService.GetFileTypesAsync(securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file types");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get a specific file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code (e.g. TEL_GSM, SSSWHLSCDR)</param>
    /// <response code="200">File type returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File type not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("file-types/{file-type-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_file_types_file_type_code")]
    [Tags("File Types")]
    [ProducesResponseType(typeof(FileTypeRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFileType([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_file_types_file_type_code");
            var result = await _managementService.GetFileTypeAsync(fileTypeCode, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file type fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Create a new file type.
    /// </summary>
    /// <param name="record">File type record</param>
    /// <response code="201">File type created successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="409">File type already exists with this code</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("file-types")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_file_types")]
    [Tags("File Types")]
    [ProducesResponseType(typeof(FileTypeRecord), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateFileType(
        [FromBody] FileTypeRecord record)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_file_types");
            var result = await _managementService.CreateFileTypeAsync(record, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating file type fileTypeCode={FileTypeCode}", record.FileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Update an existing file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="record">File type record</param>
    /// <response code="200">File type updated successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File type not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPatch("file-types/{file-type-code}")]
    [SwaggerOperation(OperationId = "patch_api_v4_file_loading_file_types_file_type_code")]
    [Tags("File Types")]
    [ProducesResponseType(typeof(FileTypeRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateFileType(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromBody] FileTypeRecord record)
    {
        try
        {
            var securityContext = CreateSecurityContext("patch_api_v4_file_loading_file_types_file_type_code");
            var result = await _managementService.UpdateFileTypeAsync(fileTypeCode, record, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating file type fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Delete a file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="200">File type deleted successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File type not found</response>
    /// <response code="500">Internal server error</response>
    [HttpDelete("file-types/{file-type-code}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_file_types_file_type_code")]
    [Tags("File Types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteFileType([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("delete_api_v4_file_loading_file_types_file_type_code");
            var result = await _managementService.DeleteFileTypeAsync(fileTypeCode, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file type fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // File Types NT (CRUD)
    // ============================================

    /// <summary>
    /// List file type NT records.
    /// </summary>
    /// <param name="fileTypeCode">Filter by file type code</param>
    /// <response code="200">File type NT records returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("file-types-nt")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_file_types_nt")]
    [Tags("File Types NT")]
    [ProducesResponseType(typeof(List<FileTypeNtRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFileTypeNtRecords(
        [FromQuery(Name = "fileType")] string? fileTypeCode = null)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_file_types_nt");
            var result = await _managementService.GetFileTypeNtRecordsAsync(fileTypeCode, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file type NT records for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get a specific file type NT record.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="200">File type NT record returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File type NT record not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("file-types-nt/{file-type-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_file_types_nt_file_type_code")]
    [Tags("File Types NT")]
    [ProducesResponseType(typeof(FileTypeNtRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFileTypeNt([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_file_types_nt_file_type_code");
            var result = await _managementService.GetFileTypeNtAsync(fileTypeCode, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file type NT for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Create a new file type NT record.
    /// </summary>
    /// <param name="record">File type NT record</param>
    /// <response code="201">File type NT record created successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="409">File type NT record already exists for this file type code</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("file-types-nt")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_file_types_nt")]
    [Tags("File Types NT")]
    [ProducesResponseType(typeof(FileTypeNtRecord), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateFileTypeNt(
        [FromBody] FileTypeNtRecord record)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_file_types_nt");
            var result = await _managementService.CreateFileTypeNtAsync(record, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating file type NT for fileTypeCode={FileTypeCode}", record.FileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Update an existing file type NT record.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="record">File type NT record</param>
    /// <response code="200">File type NT record updated successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File type NT record not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPatch("file-types-nt/{file-type-code}")]
    [SwaggerOperation(OperationId = "patch_api_v4_file_loading_file_types_nt_file_type_code")]
    [Tags("File Types NT")]
    [ProducesResponseType(typeof(FileTypeNtRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateFileTypeNt(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromBody] FileTypeNtRecord record)
    {
        try
        {
            var securityContext = CreateSecurityContext("patch_api_v4_file_loading_file_types_nt_file_type_code");
            var result = await _managementService.UpdateFileTypeNtAsync(fileTypeCode, record, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating file type NT for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Delete a file type NT record.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="200">File type NT record deleted successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File type NT record not found</response>
    /// <response code="500">Internal server error</response>
    [HttpDelete("file-types-nt/{file-type-code}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_file_types_nt_file_type_code")]
    [Tags("File Types NT")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteFileTypeNt([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("delete_api_v4_file_loading_file_types_nt_file_type_code");
            var result = await _managementService.DeleteFileTypeNtAsync(fileTypeCode, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file type NT for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Helpers
    // ============================================

    private IActionResult HandleDataResult<T>(DataResult<T> result)
    {
        return result.StatusCode switch
        {
            200 => Ok(result.Data),
            201 => StatusCode(201, result.Data),
            204 => NoContent(),
            400 => BadRequest(new ErrorResponse(result.ErrorMessage ?? "Bad request", result.ErrorCode)),
            404 => NotFound(new ErrorResponse(result.ErrorMessage ?? "Not found", result.ErrorCode)),
            409 => Conflict(new ErrorResponse(result.ErrorMessage ?? "Conflict", result.ErrorCode)),
            _ => StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode))
        };
    }

    // ============================================
    // Custom Table Management
    // ============================================

    /// <summary>
    /// Get all custom table versions for a file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="200">Custom table info returned successfully</response>
    /// <response code="204">No custom table exists for this file type</response>
    /// <response code="401">Unauthorized</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("parsers/{file-type-code}/custom-table")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_parsers_file_type_code_custom_table")]
    [Tags("Custom Tables")]
    [ProducesResponseType(typeof(CustomTableInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCustomTable([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_parsers_file_type_code_custom_table");
            var result = await _managementService.GetCustomTableInfoAsync(fileTypeCode, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting custom table for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Propose a new custom table based on current column mappings. No database changes are made.
    /// Returns the proposed DDL, table name, and column definitions.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="200">Proposed DDL returned successfully</response>
    /// <response code="400">No column mappings configured</response>
    /// <response code="404">Parser configuration not found</response>
    /// <response code="409">Active table exists with no records (use existing or drop it)</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("parsers/{file-type-code}/custom-table/propose")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_parsers_file_type_code_custom_table_propose")]
    [Tags("Custom Tables")]
    [ProducesResponseType(typeof(CustomTableProposal), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ProposeCustomTable([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_parsers_file_type_code_custom_table_propose");
            var result = await _managementService.ProposeCustomTableAsync(fileTypeCode, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proposing custom table for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Create the custom table in the database for this file type.
    /// Generates and executes the CREATE TABLE DDL based on current column mappings.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="201">Custom table created successfully</response>
    /// <response code="400">No column mappings configured</response>
    /// <response code="404">Parser configuration not found</response>
    /// <response code="409">Active custom table already exists</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("parsers/{file-type-code}/custom-table")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_parsers_file_type_code_custom_table")]
    [Tags("Custom Tables")]
    [ProducesResponseType(typeof(CustomTableMetadata), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateCustomTable([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_parsers_file_type_code_custom_table");
            var result = await _managementService.CreateCustomTableAsync(fileTypeCode, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating custom table for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Create a new version of the custom table. Retires the current active version.
    /// Only allowed when the current active version has records.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="201">New version created successfully</response>
    /// <response code="400">Current version has no records or no column mappings</response>
    /// <response code="404">No active custom table exists</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("parsers/{file-type-code}/custom-table/new-version")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_parsers_file_type_code_custom_table_new_version")]
    [Tags("Custom Tables")]
    [ProducesResponseType(typeof(CustomTableMetadata), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateCustomTableNewVersion([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_parsers_file_type_code_custom_table_new_version");
            var result = await _managementService.CreateCustomTableNewVersionAsync(fileTypeCode, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new custom table version for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Drop a specific version of a custom table. Only allowed if the table is empty.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="version">Table version number</param>
    /// <response code="200">Table dropped successfully</response>
    /// <response code="400">Table has records and cannot be dropped</response>
    /// <response code="404">Version not found</response>
    /// <response code="500">Internal server error</response>
    [HttpDelete("parsers/{file-type-code}/custom-table/{version}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_parsers_file_type_code_custom_table_version")]
    [Tags("Custom Tables")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DropCustomTableVersion(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromRoute] int version)
    {
        try
        {
            var securityContext = CreateSecurityContext("delete_api_v4_file_loading_parsers_file_type_code_custom_table_version");
            var result = await _managementService.DropCustomTableVersionAsync(fileTypeCode, version, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dropping custom table v{Version} for fileTypeCode={FileTypeCode}", version, fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get the live record count for a specific custom table version.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="version">Table version number</param>
    /// <response code="200">Record count returned</response>
    /// <response code="400">Table has been dropped</response>
    /// <response code="404">Version not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("parsers/{file-type-code}/custom-table/{version}/count")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_parsers_file_type_code_custom_table_version_count")]
    [Tags("Custom Tables")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCustomTableRecordCount(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromRoute] int version)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_parsers_file_type_code_custom_table_version_count");
            var result = await _managementService.GetCustomTableRecordCountAsync(fileTypeCode, version, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting record count for custom table v{Version}, fileTypeCode={FileTypeCode}", version, fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Test load a file into the custom table. Creates a temporary file record that can be cleaned up.
    /// Use this to verify the table structure works with real data before going live.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="file">File to test load</param>
    /// <response code="201">Test load completed</response>
    /// <response code="404">No active custom table or parser config found</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("parsers/{file-type-code}/custom-table/test-load")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_parsers_file_type_code_custom_table_test_load")]
    [Tags("Custom Tables")]
    [ProducesResponseType(typeof(TestLoadResult), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> TestLoadCustomTable(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        IFormFile file)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_parsers_file_type_code_custom_table_test_load");
            using var stream = file.OpenReadStream();
            var result = await _managementService.TestLoadCustomTableAsync(fileTypeCode, stream, file.FileName, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error test loading file into custom table for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Delete a test-loaded file's records from the custom table and remove the file record.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="ntFileNum">NT file number from the test load response</param>
    /// <response code="200">Test load records deleted successfully</response>
    /// <response code="404">File or custom table not found</response>
    /// <response code="500">Internal server error</response>
    [HttpDelete("parsers/{file-type-code}/custom-table/test-load/{nt-file-num}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_parsers_file_type_code_custom_table_test_load_nt_file_num")]
    [Tags("Custom Tables")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteTestLoad(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromRoute(Name = "nt-file-num")] int ntFileNum)
    {
        try
        {
            var securityContext = CreateSecurityContext("delete_api_v4_file_loading_parsers_file_type_code_custom_table_test_load_nt_file_num");
            var result = await _managementService.DeleteTestLoadAsync(fileTypeCode, ntFileNum, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting test load {NtFileNum} for fileTypeCode={FileTypeCode}", ntFileNum, fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Configuration Readiness
    // ============================================

    /// <summary>
    /// Get holistic configuration readiness status for a file type.
    /// Returns a tiered breakdown of what's configured vs what's missing.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="200">Readiness status returned</response>
    /// <response code="404">File type not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("readiness/{file-type-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_readiness")]
    [Tags("File Management")]
    [ProducesResponseType(typeof(FileTypeReadinessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetReadiness(
        [FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_readiness");
            var result = await _managementService.GetReadinessAsync(fileTypeCode, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting readiness for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Charge Mappings (CRUD)
    // ============================================

    /// <summary>
    /// List all charge mappings for a file type, ordered by sequence number.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="200">Charge mappings returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("charge-maps/{file-type-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_charge_maps_file_type_code")]
    [Tags("Charge Mappings")]
    [ProducesResponseType(typeof(List<NtflChgMapRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetChargeMaps(
        [FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_charge_maps_file_type_code");
            var result = await _managementService.GetChargeMapsAsync(fileTypeCode, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting charge maps for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get a specific charge mapping by ID.
    /// </summary>
    /// <param name="id">Charge mapping ID</param>
    /// <response code="200">Charge mapping returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Charge mapping not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("charge-maps/by-id/{id:int}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_charge_maps_id")]
    [Tags("Charge Mappings")]
    [ProducesResponseType(typeof(NtflChgMapRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetChargeMap([FromRoute] int id)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_charge_maps_id");
            var result = await _managementService.GetChargeMapAsync(id, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting charge map id={Id}", id);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Create a new charge mapping.
    /// </summary>
    /// <param name="request">Charge mapping request</param>
    /// <response code="201">Charge mapping created successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("charge-maps")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_charge_maps")]
    [Tags("Charge Mappings")]
    [ProducesResponseType(typeof(NtflChgMapRecord), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateChargeMap(
        [FromBody] NtflChgMapRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_charge_maps");
            var result = await _managementService.CreateChargeMapAsync(request, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating charge map for fileTypeCode={FileTypeCode}", request.FileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Update an existing charge mapping.
    /// </summary>
    /// <param name="id">Charge mapping ID</param>
    /// <param name="request">Charge mapping request</param>
    /// <response code="200">Charge mapping updated successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Charge mapping not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPatch("charge-maps/{id:int}")]
    [SwaggerOperation(OperationId = "patch_api_v4_file_loading_charge_maps_id")]
    [Tags("Charge Mappings")]
    [ProducesResponseType(typeof(NtflChgMapRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UpdateChargeMap(
        [FromRoute] int id,
        [FromBody] NtflChgMapRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("patch_api_v4_file_loading_charge_maps_id");
            var result = await _managementService.UpdateChargeMapAsync(id, request, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating charge map id={Id}", id);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Delete a charge mapping.
    /// </summary>
    /// <param name="id">Charge mapping ID</param>
    /// <response code="200">Charge mapping deleted successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Charge mapping not found</response>
    /// <response code="500">Internal server error</response>
    [HttpDelete("charge-maps/{id:int}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_charge_maps_id")]
    [Tags("Charge Mappings")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteChargeMap([FromRoute] int id)
    {
        try
        {
            var securityContext = CreateSecurityContext("delete_api_v4_file_loading_charge_maps_id");
            var result = await _managementService.DeleteChargeMapAsync(id, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting charge map id={Id}", id);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Resolve a charge description to a charge code using the mapping table.
    /// Tests the description against stored patterns in sequence order and returns the first match.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="description">Charge description text to match</param>
    /// <response code="200">Resolution result (null data if no match found)</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("charge-maps/{file-type-code}/resolve")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_charge_maps_file_type_code_resolve")]
    [Tags("Charge Mappings")]
    [ProducesResponseType(typeof(ChargeMapMatch), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ResolveChargeMap(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromQuery(Name = "description")] string description)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_charge_maps_file_type_code_resolve");
            var result = await _managementService.ResolveChargeMapAsync(fileTypeCode, description, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving charge map for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }
}
