using Microsoft.AspNetCore.Http;
using FileLoading.Models;
using Selcomm.Data.Common;

namespace FileLoading.Interfaces;

/// <summary>
/// Service interface for AI-powered file review using the Claude API.
/// </summary>
public interface IAiReviewService
{
    // ============================================
    // AI Review
    // ============================================

    /// <summary>
    /// Trigger an AI review of a file. Samples content, builds a prompt, calls AI gateway.
    /// </summary>
    Task<DataResult<AiReviewResponse>> ReviewFileAsync(int ntFileNum, AiReviewRequest? request, SecurityContext securityContext);

    /// <summary>
    /// Review raw file content (pasted or uploaded). No file number required.
    /// </summary>
    Task<DataResult<AiReviewResponse>> ReviewContentAsync(AiContentReviewRequest request, SecurityContext securityContext);

    /// <summary>
    /// Get a cached AI review for a file.
    /// </summary>
    Task<DataResult<AiReviewResponse>> GetCachedReviewAsync(int ntFileNum);

    // ============================================
    // AI File Analysis (discovery/configuration)
    // ============================================

    /// <summary>
    /// Analyse example files for a file type to discover structure, map billing concepts,
    /// and generate a suggested parser configuration.
    /// </summary>
    Task<DataResult<AiFileAnalysisResponse>> AnalyseExampleFileAsync(string fileTypeCode, AiFileAnalysisRequest? request, SecurityContext securityContext);

    // ============================================
    // Example File CRUD
    // ============================================

    Task<DataResult<List<ExampleFileRecord>>> ListExampleFilesAsync();
    Task<DataResult<List<ExampleFileRecord>>> GetExampleFilesByTypeAsync(string fileTypeCode);
    Task<DataResult<ExampleFileRecord>> UploadExampleFileAsync(string fileTypeCode, IFormFile file, string? description, SecurityContext securityContext);
    Task<DataResult<ExampleFileRecord>> DeleteExampleFileAsync(int exampleFileId);

    // ============================================
    // Domain AI Config CRUD
    // ============================================

    Task<DataResult<AiDomainConfig>> GetDomainConfigAsync();
    Task<DataResult<AiDomainConfig>> SaveDomainConfigAsync(AiDomainConfigRequest request, SecurityContext securityContext);
    Task<RawCommandResult> DeleteDomainConfigAsync();
    Task<DataResult<AiConfigStatusResponse>> GetConfigStatusAsync();
}
