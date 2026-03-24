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
    // AI Analysis Results (persisted per file-type)
    // ============================================

    /// <summary>List all analysis results for a file type.</summary>
    [HttpGet("analysis-results/{file-type-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_analysis_results")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(List<AiAnalysisResultRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAnalysisResults([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var result = await _aiReviewService.GetAnalysisResultsAsync(fileTypeCode);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting analysis results for {FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>Get a specific analysis result by ID.</summary>
    [HttpGet("analysis-results/{file-type-code}/{analysis-id:int}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_analysis_results_id")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiAnalysisResultRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAnalysisResult(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromRoute(Name = "analysis-id")] int analysisId)
    {
        try
        {
            var result = await _aiReviewService.GetAnalysisResultAsync(analysisId);
            if (result.IsSuccess) return Ok(result.Data);
            return StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "Not found", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting analysis result {AnalysisId}", analysisId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>Update/edit a stored analysis result.</summary>
    [HttpPatch("analysis-results/{file-type-code}/{analysis-id:int}")]
    [SwaggerOperation(OperationId = "patch_api_v4_file_loading_ai_review_analysis_results_id")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiAnalysisResultRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAnalysisResult(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromRoute(Name = "analysis-id")] int analysisId,
        [FromBody] AiAnalysisResultUpdateRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("patch_api_v4_file_loading_ai_review_analysis_results_id");
            var result = await _aiReviewService.UpdateAnalysisResultAsync(analysisId, request, securityContext);
            if (result.IsSuccess) return Ok(result.Data);
            return StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating analysis result {AnalysisId}", analysisId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>Delete an analysis result.</summary>
    [HttpDelete("analysis-results/{file-type-code}/{analysis-id:int}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_ai_review_analysis_results_id")]
    [Tags("AI Review")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAnalysisResult(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromRoute(Name = "analysis-id")] int analysisId)
    {
        try
        {
            var result = await _aiReviewService.DeleteAnalysisResultAsync(analysisId);
            if (result.IsSuccess) return Ok(new { Message = $"Analysis result {analysisId} deleted." });
            return StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting analysis result {AnalysisId}", analysisId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // AI File-Type Prompts (versioned per file-type)
    // ============================================

    /// <summary>List all prompts for a file type.</summary>
    [HttpGet("prompts/{file-type-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_prompts")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(List<AiFileTypePromptRecord>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFileTypePrompts([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var result = await _aiReviewService.GetFileTypePromptsAsync(fileTypeCode);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting prompts for {FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>Get the current active prompt for a file type.</summary>
    [HttpGet("prompts/{file-type-code}/current")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_prompts_current")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiFileTypePromptRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentFileTypePrompt([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var result = await _aiReviewService.GetCurrentFileTypePromptAsync(fileTypeCode);
            if (result.IsSuccess) return Ok(result.Data);
            return StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "Not found", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current prompt for {FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>Create a new prompt version manually.</summary>
    [HttpPost("prompts/{file-type-code}")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_prompts")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiFileTypePromptRecord), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateFileTypePrompt(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromBody] AiFileTypePromptCreateRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ai_review_prompts");
            var result = await _aiReviewService.CreateFileTypePromptAsync(fileTypeCode, request, securityContext);
            if (result.IsSuccess) return StatusCode(201, result.Data);
            return StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating prompt for {FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>Auto-generate a file-type prompt from a stored analysis result.</summary>
    [HttpPost("prompts/{file-type-code}/generate")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_prompts_generate")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiFileTypePromptRecord), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GenerateFileTypePrompt(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromBody] AiFileTypePromptGenerateRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ai_review_prompts_generate");
            var result = await _aiReviewService.GenerateFileTypePromptAsync(fileTypeCode, request, securityContext);
            if (result.IsSuccess) return StatusCode(201, result.Data);
            return StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating prompt for {FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>Edit an existing prompt.</summary>
    [HttpPatch("prompts/{file-type-code}/{prompt-id:int}")]
    [SwaggerOperation(OperationId = "patch_api_v4_file_loading_ai_review_prompts_id")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiFileTypePromptRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateFileTypePrompt(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromRoute(Name = "prompt-id")] int promptId,
        [FromBody] AiFileTypePromptUpdateRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("patch_api_v4_file_loading_ai_review_prompts_id");
            var result = await _aiReviewService.UpdateFileTypePromptAsync(fileTypeCode, promptId, request, securityContext);
            if (result.IsSuccess) return Ok(result.Data);
            return StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating prompt {PromptId}", promptId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>Set a prompt as the current active prompt for this file type.</summary>
    [HttpPatch("prompts/{file-type-code}/{prompt-id:int}/activate")]
    [SwaggerOperation(OperationId = "patch_api_v4_file_loading_ai_review_prompts_id_activate")]
    [Tags("AI Review")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateFileTypePrompt(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromRoute(Name = "prompt-id")] int promptId)
    {
        try
        {
            var result = await _aiReviewService.ActivateFileTypePromptAsync(fileTypeCode, promptId);
            if (result.IsSuccess) return Ok(new { Message = $"Prompt {promptId} is now the current prompt for {fileTypeCode}." });
            return StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating prompt {PromptId}", promptId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>Delete a prompt version.</summary>
    [HttpDelete("prompts/{file-type-code}/{prompt-id:int}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_ai_review_prompts_id")]
    [Tags("AI Review")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteFileTypePrompt(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromRoute(Name = "prompt-id")] int promptId)
    {
        try
        {
            var result = await _aiReviewService.DeleteFileTypePromptAsync(promptId);
            if (result.IsSuccess) return Ok(new { Message = $"Prompt {promptId} deleted." });
            return StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting prompt {PromptId}", promptId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    // ============================================
    // AI Instruction File CRUD
    // ============================================

    /// <summary>
    /// List all AI instruction files for analysis.
    /// </summary>
    [HttpGet("instructions")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_instructions")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(List<AiInstructionFileRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ListInstructionFiles()
    {
        try
        {
            var result = await _aiReviewService.ListInstructionFilesAsync();
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing instruction files");
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get the AI instruction file for a file class. Returns DB record if exists, otherwise the shipped default.
    /// </summary>
    /// <param name="fileClassCode">File class code (e.g., CHG, CDR, PAY)</param>
    [HttpGet("instructions/{file-class-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_instructions_file_class_code")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiInstructionFileRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetInstructionFile([FromRoute(Name = "file-class-code")] string fileClassCode)
    {
        try
        {
            var result = await _aiReviewService.GetInstructionFileAsync(fileClassCode);

            if (result.IsSuccess)
                return Ok(result.Data);

            return StatusCode(result.StatusCode,
                new ErrorResponse(result.ErrorMessage ?? "Not found", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting instruction file for {FileClassCode}", fileClassCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Create or update a custom AI instruction file for a file class.
    /// </summary>
    /// <param name="fileClassCode">File class code (e.g., CHG, CDR, PAY)</param>
    /// <param name="request">Instruction content and description</param>
    [HttpPost("instructions/{file-class-code}")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_instructions_file_class_code")]
    [Tags("AI Review")]
    [ProducesResponseType(typeof(AiInstructionFileRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SaveInstructionFile(
        [FromRoute(Name = "file-class-code")] string fileClassCode,
        [FromBody] AiInstructionFileRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ai_review_instructions_file_class_code");
            var result = await _aiReviewService.SaveInstructionFileAsync(fileClassCode, request, securityContext);

            if (result.IsSuccess)
                return Ok(result.Data);

            return StatusCode(result.StatusCode,
                new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving instruction file for {FileClassCode}", fileClassCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Delete a custom AI instruction file for a file class. Reverts to the shipped default.
    /// </summary>
    /// <param name="fileClassCode">File class code</param>
    [HttpDelete("instructions/{file-class-code}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_ai_review_instructions_file_class_code")]
    [Tags("AI Review")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeleteInstructionFile([FromRoute(Name = "file-class-code")] string fileClassCode)
    {
        try
        {
            var result = await _aiReviewService.DeleteInstructionFileAsync(fileClassCode);

            if (result.IsSuccess)
                return Ok(new { Message = $"Custom instruction for '{fileClassCode}' deleted. Default will be used." });

            return StatusCode(result.StatusCode,
                new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting instruction file for {FileClassCode}", fileClassCode);
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

    // ============================================
    // AI Charge Map Seeding
    // ============================================

    /// <summary>
    /// Trigger AI charge map seeding for a file type. Analyses charge descriptions from the AI analysis
    /// and suggests mappings to Selcomm charge codes.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="request">Optional seeding parameters</param>
    /// <response code="201">Suggestions created successfully</response>
    /// <response code="400">No ChargeType column found in analysis</response>
    /// <response code="404">File type or analysis not found</response>
    /// <response code="502">AI gateway error</response>
    [HttpPost("charge-map-seed/{file-type-code}")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_charge_map_seed")]
    [Tags("AI Charge Map Seeding")]
    [ProducesResponseType(typeof(AiChargeMapSeedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SeedChargeMaps(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromBody] AiChargeMapSeedRequest? request = null)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ai_review_charge_map_seed");
            var result = await _aiReviewService.SeedChargeMapsAsync(fileTypeCode, request, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding charge maps for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// List pending AI charge map suggestions with reasoning for a file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="200">Pending suggestions returned</response>
    [HttpGet("charge-map-seed/{file-type-code}/suggestions")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_charge_map_seed_suggestions")]
    [Tags("AI Charge Map Seeding")]
    [ProducesResponseType(typeof(List<AiChargeMapSuggestion>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPendingSuggestions(
        [FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_ai_review_charge_map_seed_suggestions");
            var result = await _aiReviewService.GetPendingSuggestionsAsync(fileTypeCode, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending suggestions for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Accept, reject, or modify a single AI charge map suggestion.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="chgMapId">Charge mapping ID</param>
    /// <param name="request">Review action (ACCEPT, REJECT, MODIFY)</param>
    /// <response code="200">Suggestion reviewed successfully</response>
    /// <response code="404">Charge mapping not found</response>
    /// <response code="409">Already reviewed</response>
    [HttpPost("charge-map-seed/{file-type-code}/review/{chg-map-id:int}")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_charge_map_seed_review")]
    [Tags("AI Charge Map Seeding")]
    [ProducesResponseType(typeof(NtflChgMapRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReviewSuggestion(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromRoute(Name = "chg-map-id")] int chgMapId,
        [FromBody] AiChargeMapReviewRequest request)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ai_review_charge_map_seed_review");
            var result = await _aiReviewService.ReviewSuggestionAsync(fileTypeCode, chgMapId, request, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reviewing suggestion chgMapId={ChgMapId}", chgMapId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Bulk accept all pending AI charge map suggestions for a file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="200">All suggestions accepted</response>
    [HttpPost("charge-map-seed/{file-type-code}/accept-all")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_charge_map_seed_accept_all")]
    [Tags("AI Charge Map Seeding")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AcceptAllSuggestions(
        [FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ai_review_charge_map_seed_accept_all");
            var result = await _aiReviewService.AcceptAllSuggestionsAsync(fileTypeCode, securityContext);
            if (result.IsSuccess) return Ok(new { Accepted = result.Data });
            return StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting all suggestions for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Bulk reject all pending AI charge map suggestions for a file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <response code="200">All suggestions rejected</response>
    [HttpPost("charge-map-seed/{file-type-code}/reject-all")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_ai_review_charge_map_seed_reject_all")]
    [Tags("AI Charge Map Seeding")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RejectAllSuggestions(
        [FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        try
        {
            var securityContext = CreateSecurityContext("post_api_v4_file_loading_ai_review_charge_map_seed_reject_all");
            var result = await _aiReviewService.RejectAllSuggestionsAsync(fileTypeCode, securityContext);
            if (result.IsSuccess) return Ok(new { Rejected = result.Data });
            return StatusCode(result.StatusCode, new ErrorResponse(result.ErrorMessage ?? "An error occurred", result.ErrorCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting all suggestions for fileTypeCode={FileTypeCode}", fileTypeCode);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }

    /// <summary>
    /// Get AI reasoning records for a specific charge mapping.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="chgMapId">Charge mapping ID</param>
    /// <response code="200">Reasoning records returned</response>
    [HttpGet("charge-map-seed/{file-type-code}/reasons/{chg-map-id:int}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_ai_review_charge_map_seed_reasons")]
    [Tags("AI Charge Map Seeding")]
    [ProducesResponseType(typeof(List<ChgMapAiReasonRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAiReasons(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromRoute(Name = "chg-map-id")] int chgMapId)
    {
        try
        {
            var securityContext = CreateSecurityContext("get_api_v4_file_loading_ai_review_charge_map_seed_reasons");
            var result = await _aiReviewService.GetAiReasonsAsync(chgMapId, securityContext);
            return HandleDataResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting AI reasons for chgMapId={ChgMapId}", chgMapId);
            return StatusCode(500, new ErrorResponse("An error occurred", "INTERNAL_ERROR"));
        }
    }
}
