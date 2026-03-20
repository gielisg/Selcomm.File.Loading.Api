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
    /// Trigger an AI review of a file. Samples content, builds a prompt, calls Claude API.
    /// </summary>
    Task<DataResult<AiReviewResponse>> ReviewFileAsync(int ntFileNum, AiReviewRequest? request, SecurityContext securityContext);

    /// <summary>
    /// Get a cached AI review for a file.
    /// </summary>
    Task<DataResult<AiReviewResponse>> GetCachedReviewAsync(int ntFileNum);

    // ============================================
    // Example File CRUD
    // ============================================

    Task<DataResult<List<ExampleFileRecord>>> ListExampleFilesAsync();
    Task<DataResult<ExampleFileRecord>> GetExampleFileAsync(string fileTypeCode);
    Task<DataResult<ExampleFileRecord>> SaveExampleFileAsync(string fileTypeCode, ExampleFileRequest request, SecurityContext securityContext);
    Task<RawCommandResult> DeleteExampleFileAsync(string fileTypeCode);

    // ============================================
    // Domain AI Config CRUD
    // ============================================

    Task<DataResult<AiDomainConfig>> GetDomainConfigAsync(string domain);
    Task<DataResult<AiDomainConfig>> SaveDomainConfigAsync(string domain, AiDomainConfigRequest request, SecurityContext securityContext);
    Task<RawCommandResult> DeleteDomainConfigAsync(string domain);
    Task<DataResult<AiConfigStatusResponse>> GetConfigStatusAsync(string domain);
}
