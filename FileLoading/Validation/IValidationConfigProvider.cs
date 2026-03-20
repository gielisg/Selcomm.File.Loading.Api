using FileLoading.Models;

namespace FileLoading.Validation;

/// <summary>
/// Interface for providing validation configurations.
/// Implementations can load configs from appsettings, database, or other sources.
/// </summary>
public interface IValidationConfigProvider
{
    /// <summary>
    /// Gets the validation configuration for a specific file type.
    /// Returns null if no configuration is defined for the file type.
    /// </summary>
    /// <param name="fileType">The file type code (e.g., "SSSWHLSCDR", "SSSWHLSCHG").</param>
    /// <returns>The validation configuration or null if not found.</returns>
    FileValidationConfig? GetConfig(string fileType);

    /// <summary>
    /// Gets all available validation configurations.
    /// </summary>
    /// <returns>Dictionary of file type to configuration.</returns>
    IDictionary<string, FileValidationConfig> GetAllConfigs();

    /// <summary>
    /// Checks if a validation configuration exists for a file type.
    /// </summary>
    /// <param name="fileType">The file type code.</param>
    /// <returns>True if configuration exists.</returns>
    bool HasConfig(string fileType);
}
