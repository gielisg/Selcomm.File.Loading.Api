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
    // AI Analysis Results (persisted per file-type)
    // ============================================

    Task<DataResult<List<AiAnalysisResultRecord>>> GetAnalysisResultsAsync(string fileTypeCode);
    Task<DataResult<AiAnalysisResultRecord>> GetAnalysisResultAsync(int analysisId);
    Task<DataResult<AiAnalysisResultRecord>> UpdateAnalysisResultAsync(int analysisId, AiAnalysisResultUpdateRequest request, SecurityContext securityContext);
    Task<RawCommandResult> DeleteAnalysisResultAsync(int analysisId);

    // ============================================
    // AI File-Type Prompts (versioned per file-type)
    // ============================================

    Task<DataResult<List<AiFileTypePromptRecord>>> GetFileTypePromptsAsync(string fileTypeCode);
    Task<DataResult<AiFileTypePromptRecord>> GetCurrentFileTypePromptAsync(string fileTypeCode);
    Task<DataResult<AiFileTypePromptRecord>> CreateFileTypePromptAsync(string fileTypeCode, AiFileTypePromptCreateRequest request, SecurityContext securityContext);
    Task<DataResult<AiFileTypePromptRecord>> GenerateFileTypePromptAsync(string fileTypeCode, AiFileTypePromptGenerateRequest request, SecurityContext securityContext);
    Task<DataResult<AiFileTypePromptRecord>> UpdateFileTypePromptAsync(string fileTypeCode, int promptId, AiFileTypePromptUpdateRequest request, SecurityContext securityContext);
    Task<RawCommandResult> ActivateFileTypePromptAsync(string fileTypeCode, int promptId);
    Task<RawCommandResult> DeleteFileTypePromptAsync(int promptId);

    // ============================================
    // AI Instruction File CRUD
    // ============================================

    Task<DataResult<List<AiInstructionFileRecord>>> ListInstructionFilesAsync();
    Task<DataResult<AiInstructionFileRecord>> GetInstructionFileAsync(string fileClassCode);
    DataResult<AiInstructionFileRecord> GetDefaultInstructionFile(string fileClassCode);
    Task<DataResult<AiInstructionFileRecord>> SaveInstructionFileAsync(string fileClassCode, AiInstructionFileRequest request, SecurityContext securityContext);
    Task<RawCommandResult> DeleteInstructionFileAsync(string fileClassCode);

    // ============================================
    // Example File CRUD
    // ============================================

    Task<DataResult<List<ExampleFileRecord>>> ListExampleFilesAsync();
    Task<DataResult<List<ExampleFileRecord>>> GetExampleFilesByTypeAsync(string fileTypeCode);
    Task<DataResult<ExampleFileRecord>> UploadExampleFileAsync(string fileTypeCode, IFormFile file, string? description, SecurityContext securityContext);
    Task<DataResult<ExampleFileRecord>> DeleteExampleFileAsync(int exampleFileId);

    // ============================================
    // AI Charge Map Seeding
    // ============================================

    /// <summary>Trigger AI charge map seeding for a file type.</summary>
    Task<DataResult<AiChargeMapSeedResponse>> SeedChargeMapsAsync(string fileTypeCode, AiChargeMapSeedRequest? request, SecurityContext securityContext);

    /// <summary>List pending AI charge map suggestions with reasoning.</summary>
    Task<DataResult<List<AiChargeMapSuggestion>>> GetPendingSuggestionsAsync(string fileTypeCode, SecurityContext securityContext);

    /// <summary>Accept/reject/modify a single AI charge map suggestion.</summary>
    Task<DataResult<NtflChgMapRecord>> ReviewSuggestionAsync(string fileTypeCode, int chgMapId, AiChargeMapReviewRequest request, SecurityContext securityContext);

    /// <summary>Bulk accept all pending suggestions for a file type.</summary>
    Task<DataResult<int>> AcceptAllSuggestionsAsync(string fileTypeCode, SecurityContext securityContext);

    /// <summary>Bulk reject all pending suggestions for a file type.</summary>
    Task<DataResult<int>> RejectAllSuggestionsAsync(string fileTypeCode, SecurityContext securityContext);

    /// <summary>Get AI reasoning for a specific charge mapping.</summary>
    Task<DataResult<List<ChgMapAiReasonRecord>>> GetAiReasonsAsync(int chgMapId, SecurityContext securityContext);

    // ============================================
    // Domain AI Config CRUD
    // ============================================

    Task<DataResult<AiDomainConfig>> GetDomainConfigAsync();
    Task<DataResult<AiDomainConfig>> SaveDomainConfigAsync(AiDomainConfigRequest request, SecurityContext securityContext);
    Task<RawCommandResult> DeleteDomainConfigAsync();
    Task<DataResult<AiConfigStatusResponse>> GetConfigStatusAsync();
}
