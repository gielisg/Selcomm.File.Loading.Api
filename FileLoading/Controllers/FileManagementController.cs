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
    /// <param name="domain">Filter by domain</param>
    /// <param name="fileType">Filter by file type code</param>
    [HttpGet("dashboard")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_dashboard")]
    [Tags("File Management")]
    [ProducesResponseType(typeof(FileManagementDashboard), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] string? domain = null,
        [FromQuery(Name = "fileType")] string? fileType = null)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_dashboard");
        var result = await _managementService.GetDashboardAsync(domain, fileType, securityContext);

        return HandleDataResult(result);
    }

    // ============================================
    // Files (workflow)
    // ============================================

    /// <summary>
    /// List files in the transfer workflow with filtering. Supports domain, file-type, folder, status, date range, and filename search filters.
    /// </summary>
    [HttpGet("manager/files")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_manager_files")]
    [Tags("File Management")]
    [ProducesResponseType(typeof(FileListWithStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFiles([FromQuery] FileListFilterRequest request)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_manager_files");

        var filter = new FileListFilter
        {
            Domain = request.Domain,
            FileTypeCode = request.FileType,
            CurrentFolder = request.Folder,
            Status = request.Status,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            FileNameSearch = request.Search,
            MaxRecords = request.MaxRecords
        };

        var result = await _managementService.ListFilesAsync(filter, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Get file details by transfer-id (the workflow tracking key).
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    [HttpGet("manager/files/{transfer-id}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_manager_files_transfer_id")]
    [Tags("File Management")]
    [ProducesResponseType(typeof(FileWithStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFileDetails([FromRoute(Name = "transfer-id")] int transferId)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_manager_files_transfer_id");
        var result = await _managementService.GetFileDetailsAsync(transferId, securityContext);

        return HandleDataResult(result);
    }

    // ============================================
    // Transfer Operations
    // ============================================

    /// <summary>
    /// Fetch files from a transfer source (SFTP/FTP/FileSystem).
    /// </summary>
    /// <param name="sourceId">Transfer source ID (auto-generated integer)</param>
    [HttpPost("transfers/{source-id}/fetch")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_transfers_source_id_fetch")]
    [Tags("Transfer Operations")]
    [ProducesResponseType(typeof(TransferFetchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> FetchFromSource([FromRoute(Name = "source-id")] int sourceId)
    {
        _logger.LogInformation("Fetching files from source: {SourceId}", sourceId);

        var securityContext = CreateSecurityContext("post_api_v4_file_loading_transfers_source_id_fetch");
        var result = await _transferService.FetchFilesFromSourceAsync(sourceId, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Process a file from the transfer workflow.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    [HttpPost("manager/files/{transfer-id}/process")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_manager_files_transfer_id_process")]
    [Tags("File Management")]
    [ProducesResponseType(typeof(FileLoadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ProcessFile([FromRoute(Name = "transfer-id")] int transferId)
    {
        _logger.LogInformation("Processing file: {TransferId}", transferId);

        var securityContext = CreateSecurityContext("post_api_v4_file_loading_manager_files_transfer_id_process");
        var result = await _managementService.ProcessFileAsync(transferId, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Retry processing a failed file.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    [HttpPost("manager/files/{transfer-id}/retry")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_manager_files_transfer_id_retry")]
    [Tags("File Management")]
    [ProducesResponseType(typeof(FileLoadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RetryProcessing([FromRoute(Name = "transfer-id")] int transferId)
    {
        _logger.LogInformation("Retrying file: {TransferId}", transferId);

        var securityContext = CreateSecurityContext("post_api_v4_file_loading_manager_files_transfer_id_retry");
        var result = await _managementService.RetryProcessingAsync(transferId, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Move a file to a specific workflow folder (Transfer, Processing, Processed, Errors, Skipped).
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    /// <param name="folder">Target folder name</param>
    /// <param name="reason">Optional reason for the move</param>
    [HttpPost("manager/files/{transfer-id}/move")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_manager_files_transfer_id_move")]
    [Tags("File Management")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MoveFile(
        [FromRoute(Name = "transfer-id")] int transferId,
        [FromQuery] string folder,
        [FromQuery] string? reason = null)
    {
        _logger.LogInformation("Moving file {TransferId} to {Folder}", transferId, folder);

        var securityContext = CreateSecurityContext("post_api_v4_file_loading_manager_files_transfer_id_move");
        var result = await _managementService.MoveFileAsync(transferId, folder, reason, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Unload a loaded file (reverse the load operation). Uses nt-file-num because it operates on the database record created at load time.
    /// </summary>
    /// <param name="ntFileNum">File number (nt_file_num) — the database record key</param>
    [HttpPost("manager/files/{nt-file-num}/unload")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_manager_files_nt_file_num_unload")]
    [Tags("File Management")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnloadFile([FromRoute(Name = "nt-file-num")] int ntFileNum)
    {
        _logger.LogInformation("Unloading file: {NtFileNum}", ntFileNum);

        var securityContext = CreateSecurityContext("post_api_v4_file_loading_manager_files_nt_file_num_unload");
        var result = await _managementService.UnloadFileAsync(ntFileNum, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Force skip a sequence number. Uses nt-file-num because it operates on the database record created at load time.
    /// </summary>
    /// <param name="ntFileNum">File number (nt_file_num) — the database record key</param>
    /// <param name="skipToSeq">Sequence number to skip to</param>
    /// <param name="reason">Optional reason for skipping</param>
    [HttpPost("manager/files/{nt-file-num}/skip-sequence")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_manager_files_nt_file_num_skip_sequence")]
    [Tags("File Management")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForceSequenceSkip(
        [FromRoute(Name = "nt-file-num")] int ntFileNum,
        [FromQuery(Name = "skipTo")] int skipToSeq,
        [FromQuery] string? reason = null)
    {
        _logger.LogInformation("Skipping sequence for file {NtFileNum} to {Seq}", ntFileNum, skipToSeq);

        var securityContext = CreateSecurityContext("post_api_v4_file_loading_manager_files_nt_file_num_skip_sequence");
        var result = await _managementService.ForceSequenceSkipAsync(ntFileNum, skipToSeq, reason, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Download a file to browser.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    [HttpGet("manager/files/{transfer-id}/download")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_manager_files_transfer_id_download")]
    [Tags("File Management")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile([FromRoute(Name = "transfer-id")] int transferId)
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

    /// <summary>
    /// Delete a file from the workflow.
    /// </summary>
    /// <param name="transferId">Transfer record ID</param>
    [HttpDelete("manager/files/{transfer-id}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_manager_files_transfer_id")]
    [Tags("File Management")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFile([FromRoute(Name = "transfer-id")] int transferId)
    {
        _logger.LogInformation("Deleting file: {TransferId}", transferId);

        var securityContext = CreateSecurityContext("delete_api_v4_file_loading_manager_files_transfer_id");
        var result = await _managementService.DeleteFileAsync(transferId, securityContext);

        return HandleDataResult(result);
    }

    // ============================================
    // Activity Log
    // ============================================

    /// <summary>
    /// Get activity log entries. Filter by nt-file-num, transfer-id, or both.
    /// </summary>
    /// <param name="ntFileNum">Filter by file number</param>
    /// <param name="transferId">Filter by transfer record ID</param>
    /// <param name="maxRecords">Maximum records to return (default 100)</param>
    [HttpGet("activity")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_activity")]
    [Tags("Activity Log")]
    [ProducesResponseType(typeof(List<FileActivityLog>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActivityLog(
        [FromQuery(Name = "ntFileNum")] int? ntFileNum = null,
        [FromQuery(Name = "transferId")] int? transferId = null,
        [FromQuery(Name = "maxRecords")] int maxRecords = 100)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_activity");
        var result = await _managementService.GetActivityLogAsync(ntFileNum, transferId, maxRecords, securityContext);

        return HandleDataResult(result);
    }

    // ============================================
    // Validation Summary
    // ============================================

    /// <summary>
    /// Get AI-friendly validation summary for a loaded file.
    /// </summary>
    /// <param name="ntFileNum">File number (nt_file_num)</param>
    [HttpGet("files/{nt-file-num}/validation-summary")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_files_nt_file_num_validation_summary")]
    [Tags("Validation")]
    [ProducesResponseType(typeof(ValidationSummaryForAI), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetValidationSummary([FromRoute(Name = "nt-file-num")] int ntFileNum)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_files_nt_file_num_validation_summary");
        var result = await _managementService.GetValidationSummaryAsync(ntFileNum, securityContext);

        return HandleDataResult(result);
    }

    // ============================================
    // Exceptions View
    // ============================================

    /// <summary>
    /// Get files with processing errors.
    /// </summary>
    /// <param name="domain">Filter by domain</param>
    /// <param name="fileType">Filter by file type code</param>
    /// <param name="maxRecords">Maximum records to return (default 100)</param>
    [HttpGet("exceptions/errors")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_exceptions_errors")]
    [Tags("Exceptions")]
    [ProducesResponseType(typeof(List<FileWithStatus>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFilesWithErrors(
        [FromQuery] string? domain = null,
        [FromQuery(Name = "fileType")] string? fileType = null,
        [FromQuery(Name = "maxRecords")] int maxRecords = 100)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_exceptions_errors");
        var result = await _managementService.GetFilesWithErrorsAsync(domain, fileType, maxRecords, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Get skipped files.
    /// </summary>
    /// <param name="domain">Filter by domain</param>
    /// <param name="fileType">Filter by file type code</param>
    /// <param name="maxRecords">Maximum records to return (default 100)</param>
    [HttpGet("exceptions/skipped")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_exceptions_skipped")]
    [Tags("Exceptions")]
    [ProducesResponseType(typeof(List<FileWithStatus>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSkippedFiles(
        [FromQuery] string? domain = null,
        [FromQuery(Name = "fileType")] string? fileType = null,
        [FromQuery(Name = "maxRecords")] int maxRecords = 100)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_exceptions_skipped");
        var result = await _managementService.GetSkippedFilesAsync(domain, fileType, maxRecords, securityContext);

        return HandleDataResult(result);
    }

    // ============================================
    // Transfer Sources (CRUD)
    // ============================================

    /// <summary>
    /// List all transfer source configurations.
    /// </summary>
    /// <param name="domain">Filter by domain</param>
    [HttpGet("sources")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_sources")]
    [Tags("Transfer Sources")]
    [ProducesResponseType(typeof(List<TransferSourceConfig>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTransferSources([FromQuery] string? domain = null)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_sources");
        var result = await _transferService.GetSourceConfigsAsync(domain, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Get a specific transfer source configuration.
    /// </summary>
    /// <param name="sourceId">Source ID (auto-generated integer)</param>
    [HttpGet("sources/{source-id}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_sources_source_id")]
    [Tags("Transfer Sources")]
    [ProducesResponseType(typeof(TransferSourceConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransferSource([FromRoute(Name = "source-id")] int sourceId)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_sources_source_id");
        var result = await _transferService.GetSourceConfigAsync(sourceId, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Create or update a transfer source. Use source-id=0 to create (ID is auto-generated).
    /// </summary>
    /// <param name="sourceId">Source ID (0 for create, existing ID for update)</param>
    /// <param name="request">Transfer source configuration</param>
    [HttpPut("sources/{source-id}")]
    [SwaggerOperation(OperationId = "put_api_v4_file_loading_sources_source_id")]
    [Tags("Transfer Sources")]
    [ProducesResponseType(typeof(TransferSourceConfig), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveTransferSource(
        [FromRoute(Name = "source-id")] int sourceId,
        [FromBody] TransferSourceRequest request)
    {
        request.SourceId = sourceId;

        var securityContext = CreateSecurityContext("put_api_v4_file_loading_sources_source_id");
        var result = await _transferService.SaveSourceConfigAsync(request, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Delete a transfer source configuration.
    /// </summary>
    /// <param name="sourceId">Source ID</param>
    [HttpDelete("sources/{source-id}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_sources_source_id")]
    [Tags("Transfer Sources")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTransferSource([FromRoute(Name = "source-id")] int sourceId)
    {
        var securityContext = CreateSecurityContext("delete_api_v4_file_loading_sources_source_id");
        var result = await _transferService.DeleteSourceConfigAsync(sourceId, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Test connection to a saved transfer source.
    /// </summary>
    /// <param name="sourceId">Source ID</param>
    [HttpPost("sources/{source-id}/test")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_sources_source_id_test")]
    [Tags("Transfer Sources")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TestSourceConnection([FromRoute(Name = "source-id")] int sourceId)
    {
        var securityContext = CreateSecurityContext("post_api_v4_file_loading_sources_source_id_test");
        var result = await _transferService.TestConnectionAsync(sourceId, securityContext);

        if (!result.IsSuccess || !result.Data)
        {
            return BadRequest(new ErrorResponse(result.ErrorMessage ?? "Connection test failed", result.ErrorCode));
        }

        return Ok(new { Success = true, Message = "Connection successful" });
    }

    /// <summary>
    /// Test connection with provided configuration (without saving).
    /// </summary>
    /// <param name="request">Transfer source configuration to test</param>
    [HttpPost("sources/test")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_sources_test")]
    [Tags("Transfer Sources")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TestSourceConnectionWithConfig([FromBody] TransferSourceRequest request)
    {
        var securityContext = CreateSecurityContext("post_api_v4_file_loading_sources_test");
        var result = await _transferService.TestConnectionAsync(request, securityContext);

        if (!result.IsSuccess || !result.Data)
        {
            return BadRequest(new ErrorResponse(result.ErrorMessage ?? "Connection test failed", result.ErrorCode));
        }

        return Ok(new { Success = true, Message = "Connection successful" });
    }

    // ============================================
    // Parser Configuration (CRUD)
    // ============================================

    /// <summary>
    /// List all generic parser configurations.
    /// </summary>
    /// <param name="active">Filter by active status</param>
    [HttpGet("parsers")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_parsers")]
    [Tags("Parser Configuration")]
    [ProducesResponseType(typeof(List<GenericFileFormatConfig>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetParserConfigs([FromQuery] bool? active = null)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_parsers");
        var result = await _managementService.GetParserConfigsAsync(active, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Get a specific generic parser configuration with column mappings.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    [HttpGet("parsers/{file-type-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_parsers_file_type_code")]
    [Tags("Parser Configuration")]
    [ProducesResponseType(typeof(GenericFileFormatConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetParserConfig([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_parsers_file_type_code");
        var result = await _managementService.GetParserConfigAsync(fileTypeCode, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Create or update a generic parser configuration.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="request">Parser configuration with column mappings</param>
    [HttpPut("parsers/{file-type-code}")]
    [SwaggerOperation(OperationId = "put_api_v4_file_loading_parsers_file_type_code")]
    [Tags("Parser Configuration")]
    [ProducesResponseType(typeof(GenericFileFormatConfig), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveParserConfig(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromBody] GenericParserConfigRequest request)
    {
        request.FileTypeCode = fileTypeCode;

        var securityContext = CreateSecurityContext("put_api_v4_file_loading_parsers_file_type_code");
        var result = await _managementService.SaveParserConfigAsync(request, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Delete a generic parser configuration and its column mappings.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    [HttpDelete("parsers/{file-type-code}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_parsers_file_type_code")]
    [Tags("Parser Configuration")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteParserConfig([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        var securityContext = CreateSecurityContext("delete_api_v4_file_loading_parsers_file_type_code");
        var result = await _managementService.DeleteParserConfigAsync(fileTypeCode, securityContext);

        return HandleDataResult(result);
    }

    // ============================================
    // Folder Configuration
    // ============================================

    /// <summary>
    /// Get folder workflow configuration for a domain. Falls back to domain default if file-type specific config not found. No DELETE — folders always exist per domain.
    /// </summary>
    /// <param name="domain">Domain name (required)</param>
    /// <param name="fileType">File type code (optional — falls back to domain default)</param>
    [HttpGet("folders")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_folders")]
    [Tags("Folder Configuration")]
    [ProducesResponseType(typeof(FolderWorkflowConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFolderConfig(
        [FromQuery] string domain,
        [FromQuery(Name = "fileType")] string? fileType = null)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_folders");
        var result = await _transferService.GetFolderConfigAsync(domain, fileType, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Create or update folder workflow configuration. Auto-creates folders on save.
    /// </summary>
    /// <param name="config">Folder configuration</param>
    [HttpPut("folders")]
    [SwaggerOperation(OperationId = "put_api_v4_file_loading_folders")]
    [Tags("Folder Configuration")]
    [ProducesResponseType(typeof(FolderWorkflowConfig), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveFolderConfig([FromBody] FolderWorkflowConfig config)
    {
        var securityContext = CreateSecurityContext("put_api_v4_file_loading_folders");
        var result = await _transferService.SaveFolderConfigAsync(config, securityContext);

        if (result.IsSuccess)
        {
            // Auto-create folders on save
            await _transferService.CreateFoldersAsync(config.Domain, config.FileTypeCode, securityContext);
        }

        return HandleDataResult(result);
    }

    /// <summary>
    /// Get default folder paths for a domain/file-type combination.
    /// </summary>
    /// <param name="domain">Domain name</param>
    /// <param name="fileType">File type code</param>
    [HttpGet("folders/defaults")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_folders_defaults")]
    [Tags("Folder Configuration")]
    [ProducesResponseType(typeof(FolderDefaultsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFolderDefaults(
        [FromQuery] string domain,
        [FromQuery(Name = "fileType")] string? fileType = null)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_folders_defaults");
        var result = await _transferService.GetDefaultFolderPathsAsync(domain, fileType, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Create all 5 workflow folders for a domain/file-type (local or FTP based on storage config).
    /// </summary>
    /// <param name="domain">Domain name</param>
    /// <param name="fileType">File type code</param>
    [HttpPost("folders/create")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_folders_create")]
    [Tags("Folder Configuration")]
    [ProducesResponseType(typeof(FolderCreateResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateFolders(
        [FromQuery] string domain,
        [FromQuery(Name = "fileType")] string? fileType = null)
    {
        var securityContext = CreateSecurityContext("post_api_v4_file_loading_folders_create");
        var result = await _transferService.CreateFoldersAsync(domain, fileType, securityContext);

        return HandleDataResult(result);
    }

    // ============================================
    // Folder Storage Configuration
    // ============================================

    /// <summary>
    /// Get folder storage configuration for a domain (from JWT). Returns 404 if no config exists (interpreted as LOCAL mode).
    /// </summary>
    /// <param name="domain">Domain name</param>
    [HttpGet("folder-storage")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_folder_storage")]
    [Tags("Folder Configuration")]
    [ProducesResponseType(typeof(FolderStorageConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFolderStorage([FromQuery] string domain)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_folder_storage");
        var result = await _transferService.GetFolderStorageAsync(domain, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Save folder storage configuration (local/FTP mode + FTP details).
    /// </summary>
    /// <param name="request">Storage configuration</param>
    [HttpPut("folder-storage")]
    [SwaggerOperation(OperationId = "put_api_v4_file_loading_folder_storage")]
    [Tags("Folder Configuration")]
    [ProducesResponseType(typeof(FolderStorageConfig), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveFolderStorage([FromBody] FolderStorageRequest request)
    {
        var securityContext = CreateSecurityContext("put_api_v4_file_loading_folder_storage");
        var result = await _transferService.SaveFolderStorageAsync(request, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Delete folder storage configuration (revert to local defaults).
    /// </summary>
    /// <param name="domain">Domain name</param>
    [HttpDelete("folder-storage")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_folder_storage")]
    [Tags("Folder Configuration")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFolderStorage([FromQuery] string domain)
    {
        var securityContext = CreateSecurityContext("delete_api_v4_file_loading_folder_storage");
        var result = await _transferService.DeleteFolderStorageAsync(domain, securityContext);

        return HandleDataResult(result);
    }

    /// <summary>
    /// Test FTP connection with provided storage configuration (without saving).
    /// </summary>
    /// <param name="request">Storage configuration to test</param>
    [HttpPost("folder-storage/test")]
    [SwaggerOperation(OperationId = "post_api_v4_file_loading_folder_storage_test")]
    [Tags("Folder Configuration")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TestFolderStorage([FromBody] FolderStorageRequest request)
    {
        var securityContext = CreateSecurityContext("post_api_v4_file_loading_folder_storage_test");
        var result = await _transferService.TestFolderStorageAsync(request, securityContext);

        if (!result.IsSuccess || !result.Data)
        {
            return BadRequest(new ErrorResponse(result.ErrorMessage ?? "Connection test failed", result.ErrorCode));
        }

        return Ok(new { Success = true, Message = "Connection successful" });
    }

    // ============================================
    // Vendors (CRUD)
    // ============================================

    /// <summary>
    /// List all vendors (networks table).
    /// </summary>
    [HttpGet("vendors")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_vendors")]
    [Tags("Vendors")]
    [ProducesResponseType(typeof(List<VendorRecord>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVendors()
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_vendors");
        var result = await _managementService.GetVendorsAsync(securityContext);
        return HandleDataResult(result);
    }

    /// <summary>
    /// Get a specific vendor.
    /// </summary>
    /// <param name="networkId">Network ID (CHAR(2))</param>
    [HttpGet("vendors/{network-id}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_vendors_network_id")]
    [Tags("Vendors")]
    [ProducesResponseType(typeof(VendorRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVendor([FromRoute(Name = "network-id")] string networkId)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_vendors_network_id");
        var result = await _managementService.GetVendorAsync(networkId, securityContext);
        return HandleDataResult(result);
    }

    /// <summary>
    /// Create or update a vendor.
    /// </summary>
    /// <param name="networkId">Network ID (CHAR(2))</param>
    /// <param name="record">Vendor record</param>
    [HttpPut("vendors/{network-id}")]
    [SwaggerOperation(OperationId = "put_api_v4_file_loading_vendors_network_id")]
    [Tags("Vendors")]
    [ProducesResponseType(typeof(VendorRecord), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveVendor(
        [FromRoute(Name = "network-id")] string networkId,
        [FromBody] VendorRecord record)
    {
        record.NetworkId = networkId;
        var securityContext = CreateSecurityContext("put_api_v4_file_loading_vendors_network_id");
        var result = await _managementService.SaveVendorAsync(record, securityContext);
        return HandleDataResult(result);
    }

    /// <summary>
    /// Delete a vendor.
    /// </summary>
    /// <param name="networkId">Network ID (CHAR(2))</param>
    [HttpDelete("vendors/{network-id}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_vendors_network_id")]
    [Tags("Vendors")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteVendor([FromRoute(Name = "network-id")] string networkId)
    {
        var securityContext = CreateSecurityContext("delete_api_v4_file_loading_vendors_network_id");
        var result = await _managementService.DeleteVendorAsync(networkId, securityContext);
        return HandleDataResult(result);
    }

    // ============================================
    // File Classes (CRUD)
    // ============================================

    /// <summary>
    /// List all file classes (e.g. CDR, CHG). A file class groups related file types.
    /// </summary>
    [HttpGet("file-classes")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_file_classes")]
    [Tags("File Classes")]
    [ProducesResponseType(typeof(List<FileClassRecord>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFileClasses()
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_file_classes");
        var result = await _managementService.GetFileClassesAsync(securityContext);
        return HandleDataResult(result);
    }

    /// <summary>
    /// Get a specific file class.
    /// </summary>
    /// <param name="fileClassCode">File class code (e.g. CDR, CHG)</param>
    [HttpGet("file-classes/{file-class-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_file_classes_file_class_code")]
    [Tags("File Classes")]
    [ProducesResponseType(typeof(FileClassRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFileClass([FromRoute(Name = "file-class-code")] string fileClassCode)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_file_classes_file_class_code");
        var result = await _managementService.GetFileClassAsync(fileClassCode, securityContext);
        return HandleDataResult(result);
    }

    /// <summary>
    /// Create or update a file class.
    /// </summary>
    /// <param name="fileClassCode">File class code</param>
    /// <param name="record">File class record</param>
    [HttpPut("file-classes/{file-class-code}")]
    [SwaggerOperation(OperationId = "put_api_v4_file_loading_file_classes_file_class_code")]
    [Tags("File Classes")]
    [ProducesResponseType(typeof(FileClassRecord), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveFileClass(
        [FromRoute(Name = "file-class-code")] string fileClassCode,
        [FromBody] FileClassRecord record)
    {
        record.FileClassCode = fileClassCode;
        var securityContext = CreateSecurityContext("put_api_v4_file_loading_file_classes_file_class_code");
        var result = await _managementService.SaveFileClassAsync(record, securityContext);
        return HandleDataResult(result);
    }

    /// <summary>
    /// Delete a file class.
    /// </summary>
    /// <param name="fileClassCode">File class code</param>
    [HttpDelete("file-classes/{file-class-code}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_file_classes_file_class_code")]
    [Tags("File Classes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFileClass([FromRoute(Name = "file-class-code")] string fileClassCode)
    {
        var securityContext = CreateSecurityContext("delete_api_v4_file_loading_file_classes_file_class_code");
        var result = await _managementService.DeleteFileClassAsync(fileClassCode, securityContext);
        return HandleDataResult(result);
    }

    // ============================================
    // File Types (CRUD)
    // ============================================

    /// <summary>
    /// List all file types. Each file type belongs to a file class and optionally a vendor.
    /// </summary>
    [HttpGet("manager/file-types")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_manager_file_types")]
    [Tags("File Types")]
    [ProducesResponseType(typeof(List<FileTypeRecord>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFileTypes()
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_manager_file_types");
        var result = await _managementService.GetFileTypesAsync(securityContext);
        return HandleDataResult(result);
    }

    /// <summary>
    /// Get a specific file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code (e.g. TEL_GSM, SSSWHLSCDR)</param>
    [HttpGet("file-types/{file-type-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_file_types_file_type_code")]
    [Tags("File Types")]
    [ProducesResponseType(typeof(FileTypeRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFileType([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_file_types_file_type_code");
        var result = await _managementService.GetFileTypeAsync(fileTypeCode, securityContext);
        return HandleDataResult(result);
    }

    /// <summary>
    /// Create or update a file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="record">File type record</param>
    [HttpPut("file-types/{file-type-code}")]
    [SwaggerOperation(OperationId = "put_api_v4_file_loading_file_types_file_type_code")]
    [Tags("File Types")]
    [ProducesResponseType(typeof(FileTypeRecord), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveFileType(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromBody] FileTypeRecord record)
    {
        record.FileTypeCode = fileTypeCode;
        var securityContext = CreateSecurityContext("put_api_v4_file_loading_file_types_file_type_code");
        var result = await _managementService.SaveFileTypeAsync(record, securityContext);
        return HandleDataResult(result);
    }

    /// <summary>
    /// Delete a file type.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    [HttpDelete("file-types/{file-type-code}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_file_types_file_type_code")]
    [Tags("File Types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFileType([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        var securityContext = CreateSecurityContext("delete_api_v4_file_loading_file_types_file_type_code");
        var result = await _managementService.DeleteFileTypeAsync(fileTypeCode, securityContext);
        return HandleDataResult(result);
    }

    // ============================================
    // File Types NT (CRUD)
    // ============================================

    /// <summary>
    /// List file type NT records. NT records map a file type to a customer number, filename pattern, and header/trailer skip counts used during loading.
    /// </summary>
    /// <param name="fileTypeCode">Filter by file type code</param>
    [HttpGet("file-types-nt")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_file_types_nt")]
    [Tags("File Types NT")]
    [ProducesResponseType(typeof(List<FileTypeNtRecord>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFileTypeNtRecords(
        [FromQuery(Name = "fileType")] string? fileTypeCode = null)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_file_types_nt");
        var result = await _managementService.GetFileTypeNtRecordsAsync(fileTypeCode, securityContext);
        return HandleDataResult(result);
    }

    /// <summary>
    /// Get a specific file type NT record.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    [HttpGet("file-types-nt/{file-type-code}")]
    [SwaggerOperation(OperationId = "get_api_v4_file_loading_file_types_nt_file_type_code")]
    [Tags("File Types NT")]
    [ProducesResponseType(typeof(FileTypeNtRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFileTypeNt([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        var securityContext = CreateSecurityContext("get_api_v4_file_loading_file_types_nt_file_type_code");
        var result = await _managementService.GetFileTypeNtAsync(fileTypeCode, securityContext);
        return HandleDataResult(result);
    }

    /// <summary>
    /// Create or update a file type NT record.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    /// <param name="record">File type NT record</param>
    [HttpPut("file-types-nt/{file-type-code}")]
    [SwaggerOperation(OperationId = "put_api_v4_file_loading_file_types_nt_file_type_code")]
    [Tags("File Types NT")]
    [ProducesResponseType(typeof(FileTypeNtRecord), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveFileTypeNt(
        [FromRoute(Name = "file-type-code")] string fileTypeCode,
        [FromBody] FileTypeNtRecord record)
    {
        record.FileTypeCode = fileTypeCode;
        var securityContext = CreateSecurityContext("put_api_v4_file_loading_file_types_nt_file_type_code");
        var result = await _managementService.SaveFileTypeNtAsync(record, securityContext);
        return HandleDataResult(result);
    }

    /// <summary>
    /// Delete a file type NT record.
    /// </summary>
    /// <param name="fileTypeCode">File type code</param>
    [HttpDelete("file-types-nt/{file-type-code}")]
    [SwaggerOperation(OperationId = "delete_api_v4_file_loading_file_types_nt_file_type_code")]
    [Tags("File Types NT")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFileTypeNt([FromRoute(Name = "file-type-code")] string fileTypeCode)
    {
        var securityContext = CreateSecurityContext("delete_api_v4_file_loading_file_types_nt_file_type_code");
        var result = await _managementService.DeleteFileTypeNtAsync(fileTypeCode, securityContext);
        return HandleDataResult(result);
    }

    // ============================================
    // Helpers
    // ============================================

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
