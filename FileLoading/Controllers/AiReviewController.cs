using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Selcomm.Data.Common;
using FileLoading.Data;
using FileLoading.Interfaces;
using FileLoading.Models;

namespace FileLoading.Controllers;

/// <summary>
/// AI Review API - AI-powered file review using Claude, with example file and domain config management.
/// </summary>
[ApiController]
[Route("api/v4/file-loading/ai-review")]
[Authorize(Policy = "MultiAuth")]
[Produces("application/json")]
public class AiReviewController : DbControllerBase<FileLoaderDbContext>
{
    private readonly IAiReviewService _aiReviewService;
    private readonly FileLoaderDbContext _dbContext;
    private readonly ILogger<AiReviewController> _logger;

    public AiReviewController(
        IAiReviewService aiReviewService,
        FileLoaderDbContext dbContext,
        ILogger<AiReviewController> logger)
    {
        _aiReviewService = aiReviewService;
        _dbContext = dbContext;
        _logger = logger;
    }

    protected override FileLoaderDbContext DbContext => _dbContext;

    // ============================================
    // AI Review Endpoints
    // ============================================

    /// <summary>
    /// Trigger an AI review of a file.
    /// </summary>
    /// <param name="ntFileNum">NT file number to review</param>
    /// <param name="request">Optional review parameters</param>
    /// <response code="200">AI review completed successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">File not found</response>
    /// <response code="429">Rate limit exceeded</response>
    /// <response code="500">Internal server error</response>
    /// <response code="502">AI service unavailable</response>
    /// <response code="504">AI service timeout</response>
    [HttpPost("files/{nt-file-num}")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_file")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> TriggerAiReview(
        [FromRoute(Name = "nt-file-num")] int ntFileNum,
        [FromBody] AiReviewRequest? request = null)
    {
        try
        {
            _logger.LogInformation("AI review requested for file {NtFileNum}", ntFileNum);

            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ai_review_file");
            var result = await _aiReviewService.ReviewFileAsync(ntFileNum, request, securityContext);

            if (result.IsSuccess)
                return Ok(result.Data);

            return StatusCode(result.StatusCode,
                new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering AI review for file {NtFileNum}", ntFileNum);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get the cached AI review for a file.
    /// </summary>
    /// <param name="ntFileNum">NT file number</param>
    /// <response code="200">Cached AI review returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">No cached review found for this file</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("files/{nt-file-num}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_file")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCachedAiReview([FromRoute(Name = "nt-file-num")] int ntFileNum)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_ai_review_file");
            var result = await _aiReviewService.GetCachedReviewAsync(ntFileNum);

            if (result.IsSuccess)
                return Ok(result.Data);

            return StatusCode(result.StatusCode,
                new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached AI review for file {NtFileNum}", ntFileNum);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Content / Upload Review Endpoints
    // ============================================

    /// <summary>
    /// Review pasted file content directly without a file number.
    /// </summary>
    /// <param name="request">File content and optional parameters</param>
    /// <response code="200">AI content review completed successfully</response>
    /// <response code="400">Invalid request data</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="429">Rate limit exceeded</response>
    /// <response code="500">Internal server error</response>
    /// <response code="502">AI service unavailable</response>
    /// <response code="504">AI service timeout</response>
    [HttpPost("content")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_content")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> ReviewContent([FromBody] AiContentReviewRequest request)
    {
        try
        {
            _logger.LogInformation("AI content review requested, type={FileType}, length={Length}",
                request.FileTypeCode, request.FileContent?.Length ?? 0);

            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ai_review_content");
            var result = await _aiReviewService.ReviewContentAsync(request, securityContext);

            if (result.IsSuccess)
                return Ok(result.Data);

            return StatusCode(result.StatusCode,
                new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reviewing content for file type {FileTypeCode}", request.FileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Upload a file from the browser and review it with AI.
    /// </summary>
    /// <param name="file">The file to review</param>
    /// <param name="fileTypeCode">Optional file type code for spec lookup</param>
    /// <param name="focusAreas">Optional comma-separated focus areas</param>
    /// <response code="200">AI upload review completed successfully</response>
    /// <response code="400">Invalid request data or no file provided</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="429">Rate limit exceeded</response>
    /// <response code="500">Internal server error</response>
    /// <response code="502">AI service unavailable</response>
    /// <response code="504">AI service timeout</response>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_upload")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> UploadAndReview(
        IFormFile file,
        [FromQuery(Name = "fileTypeCode")] string? fileTypeCode = null,
        [FromQuery(Name = "focusAreas")] string? focusAreas = null)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new ErrorResponse("No file provided.", "VALIDATION_ERROR"));
            }

            _logger.LogInformation("AI upload review requested: {FileName}, size={Size}, type={FileType}",
                file.FileName, file.Length, fileTypeCode);

            // Read file content into string
            string fileContent;
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                fileContent = await reader.ReadToEndAsync();
            }

            var request = new AiContentReviewRequest
            {
                FileContent = fileContent,
                FileTypeCode = fileTypeCode,
                FileName = file.FileName,
                FocusAreas = string.IsNullOrWhiteSpace(focusAreas)
                    ? null
                    : focusAreas.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList()
            };

            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ai_review_upload");
            var result = await _aiReviewService.ReviewContentAsync(request, securityContext);

            if (result.IsSuccess)
                return Ok(result.Data);

            return StatusCode(result.StatusCode,
                new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading and reviewing file {FileName}", file?.FileName);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // AI File Analysis (discovery/configuration)
    // ============================================

    /// <summary>
    /// Analyse example files for a file type to discover structure, map billing concepts,
    /// and generate a suggested parser configuration.
    /// </summary>
    /// <param name="fileTypeCode">File type code to analyse</param>
    /// <param name="request">Optional analysis parameters (file class, focus areas)</param>
    /// <response code="200">Analysis completed successfully</response>
    /// <response code="400">Invalid request</response>
    /// <response code="401">Unauthorized</response>
    /// <response code="404">No example files found for this file type</response>
    /// <response code="500">Internal server error</response>
    /// <response code="502">AI service unavailable</response>
    /// <response code="504">AI service timeout</response>
    [HttpPost("analyse/{file-type-code}")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_analyse")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiFileAnalysisResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> AnalyseExampleFile(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromBody] AiFileAnalysisRequest? request = null)
    {
        try
        {
            _logger.LogInformation("AI file analysis requested for type {FileTypeCode}", fileTypeCode);

            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ai_review_analyse");
            var result = await _aiReviewService.AnalyseExampleFileAsync(fileTypeCode, request, securityContext);

            if (result.IsSuccess)
                return Ok(result.Data);

            return StatusCode(result.StatusCode,
                new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analysing example files for type {FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Example File CRUD
    // ============================================

    /// <summary>
    /// List all configured example files.
    /// </summary>
    /// <response code="200">Example files returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("example-files")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_example_files")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(List<ExampleFileRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListExampleFiles()
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_ai_review_example_files");
            var result = await _aiReviewService.ListExampleFilesAsync();
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing example files");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get example files for a file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="200">Example files returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("example-files/{file-type-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_example_files_by_type")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(List<ExampleFileRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetExampleFilesByType([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var result = await _aiReviewService.GetExampleFilesByTypeAsync(fileTypeCode);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting example files for type {FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Upload an example file for a file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="file">The example file to upload</param>
    /// <param name="description">Optional description of the example file</param>
    /// <response code="200">Example file uploaded successfully</response>
    /// <response code="400">Invalid request data or no file provided</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("example-files/{file-type-code}")]
    [Consumes("multipart/form-data")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_example_file")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(ExampleFileRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UploadExampleFile(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        IFormFile file,
        [FromQuery(Name = "description")] string? description = null)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new ErrorResponse("No file provided.", "VALIDATION_ERROR"));
            }

            _logger.LogInformation("Uploading example file for type {FileTypeCode}: {FileName}, size={Size}",
                fileTypeCode, file.FileName, file.Length);

            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ai_review_example_file");
            var result = await _aiReviewService.UploadExampleFileAsync(fileTypeCode, file, description, securityContext);

            if (result.IsSuccess)
                return Ok(result.Data);

            return StatusCode(result.StatusCode,
                new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading example file for type {FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Remove an example file by ID (also deletes the file from disk).
    /// </summary>
    /// <param name="exampleFileId">Example file ID</param>
    /// <response code="200">Example file deleted successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">Example file not found</response>
    /// <response code="500">Internal server error</response>
    [HttpDelete("example-files/{example-file-id:int}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_ai_review_example_file")]
    [Tags("AI Review")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteExampleFile([FromRoute(Name = "example-file-id")] int exampleFileId)
    {
        try
        {
            var result = await _aiReviewService.DeleteExampleFileAsync(exampleFileId);

            if (result.IsSuccess)
                return Ok(new { Message = $"Example file {exampleFileId} deleted." });

            return StatusCode(result.StatusCode,
                new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting example file {ExampleFileId}", exampleFileId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // Domain AI Config CRUD
    // ============================================

    /// <summary>
    /// Get AI config for the caller's domain (from JWT).
    /// </summary>
    /// <response code="200">AI config returned successfully</response>
    /// <response code="401">Unauthorized - invalid or missing authentication</response>
    /// <response code="404">No AI config found for this domain</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("config")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_config")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiDomainConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDomainConfig()
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_ai_review_config");

            var result = await _aiReviewService.GetDomainConfigAsync();

            if (result.IsSuccess)
                return Ok(result.Data);

            return StatusCode(result.StatusCode,
                new ErrorResponse(result.ErrorMessage ?? "Not found", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting domain AI config");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Create or update AI config for the caller's domain.
    /// </summary>
    /// <param name="request">AI config details including API key</param>
    [HttpPut("config")]
    [SwaggerOperation(OperationId = "put_api_v4_file_loading_ai_review_config")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiDomainConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveDomainConfig([FromBody] AiDomainConfigRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("put_api_v4_file_loading_ai_review_config");

            var result = await _aiReviewService.SaveDomainConfigAsync(request, securityContext);

            if (result.IsSuccess)
                return Ok(result.Data);

            return StatusCode(result.StatusCode,
                new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving domain AI config");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Remove AI config for the caller's domain.
    /// </summary>
    [HttpDelete("config")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_ai_review_config")]
    [Tags("AI Review")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteDomainConfig()
    {
        try
        {
            var securityContext = CreateSecurityContext("delete_api_v4_file_loading_ai_review_config");

            var result = await _aiReviewService.DeleteDomainConfigAsync();

            if (result.IsSuccess)
                return Ok(new { Message = "AI config deleted." });

            return StatusCode(result.StatusCode,
                new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting domain AI config");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Check if AI review is available (config valid, API key set).
    /// </summary>
    [HttpGet("config/status")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_config_status")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiConfigStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConfigStatus()
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_ai_review_config_status");

            var result = await _aiReviewService.GetConfigStatusAsync();

            if (result.IsSuccess)
                return Ok(result.Data);

            return StatusCode(result.StatusCode,
                new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting AI config status");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }
}
