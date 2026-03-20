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
    /// Trigger an AI review of a file. Samples file content, calls the Claude API, and returns structured issues.
    /// </summary>
    /// <param name="ntFileNum">NT file number to review</param>
    /// <param name="request">Optional review parameters</param>
    [HttpPost("files/{nt-file-num}")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_file")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> TriggerAiReview(
        [FromRoute(Name = "nt-file-num")] int ntFileNum,
        [FromBody] AiReviewRequest? request = null)
    {
        _logger.LogInformation("AI review requested for file {NtFileNum}", ntFileNum);

        var securityContext = CreateSecurityContext("post_api_v4_file_loading_ai_review_file");
        var result = await _aiReviewService.ReviewFileAsync(ntFileNum, request, securityContext);

        if (result.IsSuccess)
            return Ok(result.Data);

        return StatusCode(result.StatusCode,
            new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
    }

    /// <summary>
    /// Get the cached AI review for a file.
    /// </summary>
    /// <param name="ntFileNum">NT file number</param>
    [HttpGet("files/{nt-file-num}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_file")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCachedAiReview([FromRoute(Name = "nt-file-num")] int ntFileNum)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_ai_review_file");
        var result = await _aiReviewService.GetCachedReviewAsync(ntFileNum);

        if (result.IsSuccess)
            return Ok(result.Data);

        return StatusCode(result.StatusCode,
            new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
    }

    // ============================================
    // Content / Upload Review Endpoints
    // ============================================

    /// <summary>
    /// Review pasted file content directly. No file number required — content is provided in the request body.
    /// </summary>
    /// <param name="request">File content and optional parameters</param>
    [HttpPost("content")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_content")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> ReviewContent([FromBody] AiContentReviewRequest request)
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

    /// <summary>
    /// Upload a file from the browser and review it with AI. The file is read in memory — not stored on the server.
    /// </summary>
    /// <param name="file">The file to review</param>
    /// <param name="fileTypeCode">Optional file type code for spec lookup</param>
    /// <param name="focusAreas">Optional comma-separated focus areas</param>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_upload")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> UploadAndReview(
        IFormFile file,
        [FromQuery(Name = "fileTypeCode")] string? fileTypeCode = null,
        [FromQuery(Name = "focusAreas")] string? focusAreas = null)
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

    // ============================================
    // Example File CRUD
    // ============================================

    /// <summary>
    /// List all configured example files.
    /// </summary>
    [HttpGet("example-files")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_example_files")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(List<ExampleFileRecord>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListExampleFiles()
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_ai_review_example_files");
        var result = await _aiReviewService.ListExampleFilesAsync();
        return HandleDataResult(result);
    }

    /// <summary>
    /// Get the example file for a file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    [HttpGet("example-files/{file-type-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_example_file")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(ExampleFileRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExampleFile([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        var result = await _aiReviewService.GetExampleFileAsync(fileTypeCode);

        if (result.IsSuccess)
            return Ok(result.Data);

        return StatusCode(result.StatusCode,
            new ErrorResponse(result.ErrorMessage ?? "Not found", result.ErrorCode));
    }

    /// <summary>
    /// Create or update the example file for a file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="request">Example file details</param>
    [HttpPut("example-files/{file-type-code}")]
    [SwaggerOperation(OperationId = "put_api_v4_file_loading_ai_review_example_file")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(ExampleFileRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveExampleFile(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromBody] ExampleFileRequest request)
    {
        var securityContext = CreateSecurityContext("put_api_v4_file_loading_ai_review_example_file");
        var result = await _aiReviewService.SaveExampleFileAsync(fileTypeCode, request, securityContext);

        if (result.IsSuccess)
            return Ok(result.Data);

        return StatusCode(result.StatusCode,
            new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
    }

    /// <summary>
    /// Remove the example file for a file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    [HttpDelete("example-files/{file-type-code}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_ai_review_example_file")]
    [Tags("AI Review")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteExampleFile([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        var result = await _aiReviewService.DeleteExampleFileAsync(fileTypeCode);

        if (result.IsSuccess)
            return Ok(new { Message = $"Example file for '{fileTypeCode}' deleted." });

        return StatusCode(result.StatusCode,
            new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
    }

    // ============================================
    // Domain AI Config CRUD
    // ============================================

    /// <summary>
    /// Get AI config for the caller's domain (from JWT).
    /// </summary>
    [HttpGet("config")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_config")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiDomainConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDomainConfig()
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_ai_review_config");
        var domain = securityContext.Domain ?? string.Empty;

        var result = await _aiReviewService.GetDomainConfigAsync(domain);

        if (result.IsSuccess)
            return Ok(result.Data);

        return StatusCode(result.StatusCode,
            new ErrorResponse(result.ErrorMessage ?? "Not found", result.ErrorCode));
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
        var securityContext = CreateSecurityContext("put_api_v4_file_loading_ai_review_config");
        var domain = securityContext.Domain ?? string.Empty;

        var result = await _aiReviewService.SaveDomainConfigAsync(domain, request, securityContext);

        if (result.IsSuccess)
            return Ok(result.Data);

        return StatusCode(result.StatusCode,
            new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
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
        var securityContext = CreateSecurityContext("delete_api_v4_file_loading_ai_review_config");
        var domain = securityContext.Domain ?? string.Empty;

        var result = await _aiReviewService.DeleteDomainConfigAsync(domain);

        if (result.IsSuccess)
            return Ok(new { Message = "AI config deleted." });

        return StatusCode(result.StatusCode,
            new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
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
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_ai_review_config_status");
        var domain = securityContext.Domain ?? string.Empty;

        var result = await _aiReviewService.GetConfigStatusAsync(domain);

        if (result.IsSuccess)
            return Ok(result.Data);

        return StatusCode(result.StatusCode,
            new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
    }
}
