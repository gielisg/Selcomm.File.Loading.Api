using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Selcomm.Data.Common;
using FileLoading.Interfaces;
using FileLoading.Models;
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
    private readonly FileLoaderDbContext _dbContext;
    private readonly ILogger<FileLoaderController> _logger;

    public FileLoaderController(
        IFileLoaderService fileLoaderService,
        FileLoaderDbContext dbContext,
        ILogger<FileLoaderController> logger)
    {
        _fileLoaderService = fileLoaderService;
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
    /// <returns>Load job details</returns>
    [HttpPost("load")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_load")]
    [Tags("File Loading")]
    [ProducesResponseType(typeof(FileLoadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LoadFile([FromBody] LoadFileRequest request)
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

    /// <summary>
    /// Upload and load a file.
    /// </summary>
    /// <param name="file">File to upload</param>
    /// <param name="fileType">File type code</param>
    /// <returns>Load job details</returns>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_upload")]
    [Tags("File Loading")]
    [ProducesResponseType(typeof(FileLoadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadFile(
        IFormFile file,
        [FromQuery(Name = "fileType")] string fileType)
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

    /// <summary>
    /// Get file load status by nt-file-num (the database record key assigned at load time).
    /// </summary>
    /// <param name="ntFileNum">File number (nt_file_num)</param>
    /// <returns>File status</returns>
    [HttpGet("files/{nt-file-num}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_files_nt_file_num")]
    [Tags("File Loading")]
    [ProducesResponseType(typeof(FileStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFileStatus([FromRoute(Name = "nt-file-num")] int ntFileNum)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_files_nt_file_num");
        var result = await _fileLoaderService.GetFileStatusAsync(ntFileNum, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// List loaded files with filtering.
    /// </summary>
    /// <param name="fileTypeCode">Filter by file type code (e.g. CDR, CHG, SSSWHLSCDR)</param>
    /// <param name="ntCustNum">Filter by customer number</param>
    /// <param name="maxRecords">Maximum records to return (default 100)</param>
    /// <returns>List of files</returns>
    [HttpGet("files")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_files")]
    [Tags("File Loading")]
    [ProducesResponseType(typeof(FileListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ListFiles(
        [FromQuery(Name = "fileTypeCode")] string? fileTypeCode = null,
        [FromQuery(Name = "ntCustNum")] string? ntCustNum = null,
        [FromQuery(Name = "maxRecords")] int maxRecords = 100)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_files");
        var result = await _fileLoaderService.ListFilesAsync(fileTypeCode, ntCustNum, maxRecords, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Get supported file types for loading.
    /// </summary>
    /// <returns>List of file types</returns>
    [HttpGet("file-types")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_file_types")]
    [Tags("File Loading")]
    [ProducesResponseType(typeof(FileTypeListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFileTypes()
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_file_types");
        var result = await _fileLoaderService.ListFileTypesAsync(securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Reprocess a previously loaded file.
    /// </summary>
    /// <param name="ntFileNum">File number (nt_file_num)</param>
    /// <returns>Reprocess result</returns>
    [HttpPost("files/{nt-file-num}/reprocess")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_files_nt_file_num_reprocess")]
    [Tags("File Loading")]
    [ProducesResponseType(typeof(FileLoadResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReprocessFile([FromRoute(Name = "nt-file-num")] int ntFileNum)
    {
        _logger.LogInformation("Reprocessing file {NtFileNum}", ntFileNum);

        var securityContext = CreateSecurityContext("post_api_v4_file_loading_files_nt_file_num_reprocess");
        var result = await _fileLoaderService.ReprocessFileAsync(ntFileNum, securityContext);

        return HandleDataResult(result);
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
