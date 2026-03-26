using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Selcomm.Data.Common;
using FileLoading.Interfaces;
using FileLoading.Models;
using FileLoading.Repositories;
using FileLoading.Data;

namespace FileLoading.Controllers;

/// <summary>
/// File Loader API - loads and processes network files (converted from ntfileload.4gl).
/// Supports multiple file classes: CDR, CHG, EBL, SVC, and more.
/// </summary>
[ApiController]
[Route("api/v4/file-loading")]
[Authorize(Policy = "MultiAuth")]
[Produces("application/json")]
public class FileLoaderController : DbControllerBase<FileLoaderDbContext>
{
    private readonly IFileLoaderService _fileLoaderService;
    private readonly IFileLoaderRepository _repository;
    private readonly FileLoaderDbContext _dbContext;
    private readonly ILogger<FileLoaderController> _logger;

    public FileLoaderController(
        IFileLoaderService fileLoaderService,
        IFileLoaderRepository repository,
        FileLoaderDbContext dbContext,
        ILogger<FileLoaderController> logger)
    {
        _fileLoaderService = fileLoaderService;
        _repository = repository;
        _dbContext = dbContext;
        _logger = logger;
    }

    protected override FileLoaderDbContext DbContext => _dbContext;

    // ============================================
    // Health Check
    // ============================================

    /// <summary>
    /// Health check endpoint - returns API health status and database connectivity.
    /// Uses ExecuteHealthCheckAsync with explicit domain parameter for DB routing.
    /// </summary>
    /// <param name="domain">Domain name for database connection routing</param>
    /// <returns>API health status with database connectivity result</returns>
    /// <response code="200">Service is healthy</response>
    /// <response code="400">Domain parameter is missing</response>
    /// <response code="503">Service is unhealthy (database unreachable)</response>
    [HttpGet("health-check")]
    [AllowAnonymous]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_health_check")]
    [Tags("Health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> HealthCheck([FromQuery] string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return BadRequest(new { Error = "Domain parameter is required", Parameter = "domain" });
        }

        var result = await _dbContext.ExecuteHealthCheckAsync(domain);

        if (result.IsHealthy)
        {
            return Ok(new
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                Service = "File Loading API",
                Version = "4.0.0",
                Database = new { Status = "Connected", ResponseTimeMs = result.ResponseTimeMs }
            });
        }

        return StatusCode(503, new
        {
            Status = "Unhealthy",
            Timestamp = DateTime.UtcNow,
            Service = "File Loading API",
            Version = "4.0.0",
            Database = new { Status = "Unreachable", Message = result.ErrorMessage }
        });
    }

    // ============================================
    // File Loading
    // ============================================

    /// <summary>
    /// Load a network file for processing.
    /// </summary>
    /// <param name="request">File load request</param>
    /// <response code="202">File load accepted for processing</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("load")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_load")]
    [Tags("File Loading")]
    [ProducesResponseType(typeof(FileLoadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> LoadFile([FromBody] LoadFileRequest request)
    {
        try
        {
            _logger.LogInformation("Loading file: {FileName}, Type: {FileType}",
                request.FileName, request.FileType);

            var securityContext = CreateSecurityContext("post_api_v4_file_loading_load");
            var result = await _fileLoaderService.LoadFileAsync(request, securityContext);

            if (result.StatusCode == 200 || result.StatusCode == 202)
            {
                return AcceptedAtAction(
                    nameof(GetFileStatus),
                    new { ntFileNum = result.Data?.NtFileNum },
                    result.Data);
            }

            return StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading file {FileName}, Type: {FileType}", request.FileName, request.FileType);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Upload and load a file.
    /// </summary>
    /// <param name="file">File to upload</param>
    /// <param name="fileType">File type code</param>
    /// <response code="202">File upload accepted for processing</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_upload")]
    [Tags("File Loading")]
    [ProducesResponseType(typeof(FileLoadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UploadFile(
        IFormFile file,
        [FromQuery(Name = "fileType")] string fileType)
    {
        try
        {
            _logger.LogInformation("Uploading file: {FileName}, Type: {FileType}",
                file.FileName, fileType);

            var securityContext = CreateSecurityContext("post_api_v4_file_loading_upload");
            var result = await _fileLoaderService.UploadFileAsync(file, fileType, securityContext);

            if (result.StatusCode == 200 || result.StatusCode == 202)
            {
                return AcceptedAtAction(
                    nameof(GetFileStatus),
                    new { ntFileNum = result.Data?.NtFileNum },
                    result.Data);
            }

            return StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {FileName}, Type: {FileType}", file.FileName, fileType);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get file load status by nt-file-num (the database record key assigned at load time).
    /// </summary>
    /// <param name="ntFileNum">File number (nt_file_num)</param>
    /// <response code="200">File status returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File record not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("files/{nt-file-num}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_files_nt_file_num")]
    [Tags("File Loading")]
    [ProducesResponseType(typeof(FileStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFileStatus([FromRoute(Name = "nt-file-num")] int ntFileNum)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_files_nt_file_num");
            var result = await _fileLoaderService.GetFileStatusAsync(ntFileNum, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file status for NtFileNum {NtFileNum}", ntFileNum);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// List loaded files with filtering and paging.
    /// </summary>
    /// <param name="fileTypeCode">Filter by file type code (e.g. CDR, CHG, SSSWHLSCDR)</param>
    /// <param name="ntCustNum">Filter by customer number</param>
    /// <param name="skipRecords">Number of records to skip (default 0)</param>
    /// <param name="takeRecords">Number of records to return (default 20, max 100)</param>
    /// <param name="countRecords">Include total count: Y=yes, N=no, F=first page only (default F)</param>
    /// <response code="200">Paged file list returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("files")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_files")]
    [Tags("File Loading")]
    [ProducesResponseType(typeof(FileListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListFiles(
        [FromQuery(Name = "fileTypeCode")] string? fileTypeCode = null,
        [FromQuery(Name = "ntCustNum")] string? ntCustNum = null,
        [FromQuery(Name = "skipRecords")] int skipRecords = 0,
        [FromQuery(Name = "takeRecords")] int takeRecords = 20,
        [FromQuery(Name = "countRecords")] string countRecords = "F",
        [FromQuery(Name = "folder")] string? folder = null,
        [FromQuery(Name = "search")] string? search = null)
    {
        try
        {
            // Map folder name to status_id filter(s)
            // Some folders span multiple statuses — pass as comma-separated string for SP
            int? statusId = null;
            string? statusIds = null;

            switch (folder?.ToUpperInvariant())
            {
                case "TRANSFER":
                    statusId = FileStatus.Transferred;
                    break;
                case "PROCESSING":
                    // Validated + Loaded (in-progress files)
                    statusIds = $"{FileStatus.Validated},{FileStatus.Loaded}";
                    break;
                case "PROCESSED":
                    statusId = FileStatus.ProcessingCompleted;
                    break;
                case "ERRORS":
                    // Validation errors + Load errors
                    statusIds = $"{FileStatus.ValidationError},{FileStatus.LoadError}";
                    break;
                case "SKIPPED":
                    statusId = FileStatus.FileDiscarded;
                    break;
                // Legacy single-status mappings
                case "VALIDATED":
                    statusId = FileStatus.Validated;
                    break;
                case "LOADED":
                    statusId = FileStatus.Loaded;
                    break;
                case "LOAD_ERRORS":
                    statusId = FileStatus.LoadError;
                    break;
                case "DISCARDED":
                    statusId = FileStatus.FileDiscarded;
                    break;
            }

            var securityContext = CreateSecurityContext("get_api_v4_file_loading_files");
            var result = await _fileLoaderService.ListFilesAsync(fileTypeCode, ntCustNum, skipRecords, takeRecords, countRecords, securityContext, statusId, search, statusIds);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files with FileTypeCode: {FileTypeCode}, NtCustNum: {NtCustNum}", fileTypeCode, ntCustNum);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get supported file types for loading.
    /// </summary>
    /// <response code="200">File types returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("file-types")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_file_types")]
    [Tags("File Loading")]
    [ProducesResponseType(typeof(FileTypeListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListFileTypes()
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_file_types");
            var result = await _fileLoaderService.ListFileTypesAsync(securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing file types");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Reprocess a previously loaded file.
    /// </summary>
    /// <param name="ntFileNum">File number (nt_file_num)</param>
    /// <response code="202">File reprocess accepted</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File record not found</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("files/{nt-file-num}/reprocess")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_files_nt_file_num_reprocess")]
    [Tags("File Loading")]
    [ProducesResponseType(typeof(FileLoadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ReprocessFile([FromRoute(Name = "nt-file-num")] int ntFileNum)
    {
        try
        {
            _logger.LogInformation("Reprocessing file {NtFileNum}", ntFileNum);

            var securityContext = CreateSecurityContext("post_api_v4_file_loading_files_nt_file_num_reprocess");
            var result = await _fileLoaderService.ReprocessFileAsync(ntFileNum, securityContext);

            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reprocessing file {NtFileNum}", ntFileNum);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get detailed error information for a file that failed to load.
    /// Returns individual error records and an AI-friendly summary.
    /// </summary>
    /// <param name="ntFileNum">File number (nt_file_num)</param>
    /// <response code="200">Error details returned</response>
    /// <response code="204">No errors found for this file</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("files/{nt-file-num}/errors")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_files_nt_file_num_errors")]
    [Tags("File Loading")]
    [ProducesResponseType(typeof(FileErrorsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFileErrors(
        [FromRoute(Name = "nt-file-num")] int ntFileNum)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_files_nt_file_num_errors");

            // Get detailed error log records
            var errorsResult = await _repository.GetErrorLogsAsync(ntFileNum);
            var errors = errorsResult.Data ?? new List<NtflErrorLogRecord>();

            // Get AI-friendly validation summary
            var summaryResult = await _repository.GetValidationSummaryAsync(ntFileNum);
            var summary = summaryResult.Data;

            if (errors.Count == 0 && summary == null)
                return NoContent();

            return Ok(new FileErrorsResponse
            {
                Errors = errors,
                Summary = summary,
                TotalErrors = errors.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting errors for file {NtFileNum}", ntFileNum);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    private IActionResult HandleDataResult<T>(DataResult<T> result)
    {
        return result.StatusCode switch
        {
            200 => Ok(result.Data),
            204 => NoContent(),
            404 => NotFound(new ErrorResponse(result.ErrorMessage ?? "Not found", result.ErrorCode)),
            _ => StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode))
        };
    }
}
