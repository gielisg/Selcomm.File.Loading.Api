using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FileLoading.Models;

namespace FileLoading.Validation;

/// <summary>
/// Root configuration class for validation settings.
/// Maps to "FileValidationConfig" section in appsettings.json.
/// </summary>
public class FileValidationConfigRoot
{
    /// <summary>
    /// Validation configurations keyed by file type code.
    /// </summary>
    public Dictionary<string, FileValidationConfig> Configs { get; set; } = new();
}

/// <summary>
/// Provides validation configurations loaded from appsettings.json.
///
/// Example appsettings.json configuration:
/// {
///   "FileValidationConfig": {
///     "Configs": {
///       "SSSWHLSCDR": {
///         "FileType": "SSSWHLSCDR",
///         "FileRules": {
///           "RequireHeader": true,
///           "RequireFooter": true,
///           "FooterCountMustMatch": true
///         },
///         "ErrorLogging": {
///           "MaxDetailedErrors": 100,
///           "AggregateAfterMax": true
///         },
///         "FieldRules": [
///           {
///             "FieldName": "NtCost",
///             "FieldLabel": "Cost Amount",
///             "FieldIndex": 8,
///             "Type": "Decimal",
///             "Required": true,
///             "MustBeNonNegative": true,
///             "MaxValue": 999999.99
///           }
///         ]
///       }
///     }
///   }
/// }
/// </summary>
public class ValidationConfigProvider : IValidationConfigProvider
{
    private readonly Dictionary<string, FileValidationConfig> _configs;
    private readonly ILogger<ValidationConfigProvider>? _logger;

    /// <summary>
    /// Creates a new provider with configuration from IOptions.
    /// </summary>
    public ValidationConfigProvider(
        IOptions<FileValidationConfigRoot>? options = null,
        ILogger<ValidationConfigProvider>? logger = null)
    {
        _logger = logger;
        _configs = new Dictionary<string, FileValidationConfig>(StringComparer.OrdinalIgnoreCase);

        if (options?.Value?.Configs != null)
        {
            foreach (var kvp in options.Value.Configs)
            {
                var config = kvp.Value;
                // Ensure FileType is set
                if (string.IsNullOrEmpty(config.FileType))
                {
                    config.FileType = kvp.Key;
                }
                _configs[kvp.Key] = config;
            }

            _logger?.LogInformation("Loaded {Count} validation configurations from settings", _configs.Count);
        }
        else
        {
            _logger?.LogDebug("No validation configurations found in settings");
        }
    }

    /// <summary>
    /// Creates a provider with predefined configurations (for testing or programmatic setup).
    /// </summary>
    public ValidationConfigProvider(IDictionary<string, FileValidationConfig> configs)
    {
        _configs = new Dictionary<string, FileValidationConfig>(configs, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public FileValidationConfig? GetConfig(string fileType)
    {
        if (string.IsNullOrEmpty(fileType))
            return null;

        if (_configs.TryGetValue(fileType, out var config))
        {
            _logger?.LogDebug("Found validation config for file type {FileType}", fileType);
            return config;
        }

        _logger?.LogDebug("No validation config found for file type {FileType}", fileType);
        return null;
    }

    /// <inheritdoc/>
    public IDictionary<string, FileValidationConfig> GetAllConfigs()
    {
        return new Dictionary<string, FileValidationConfig>(_configs, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public bool HasConfig(string fileType)
    {
        return !string.IsNullOrEmpty(fileType) && _configs.ContainsKey(fileType);
    }

    /// <summary>
    /// Adds or updates a configuration (for programmatic setup).
    /// </summary>
    public void AddConfig(string fileType, FileValidationConfig config)
    {
        if (string.IsNullOrEmpty(config.FileType))
        {
            config.FileType = fileType;
        }
        _configs[fileType] = config;
    }

    /// <summary>
    /// Creates a default configuration with common file rules.
    /// </summary>
    public static FileValidationConfig CreateDefaultConfig(string fileType)
    {
        return new FileValidationConfig
        {
            FileType = fileType,
            FileRules = new FileRules
            {
                RequireHeader = true,
                RequireFooter = true,
                FooterCountMustMatch = true,
                HeaderMustBeFirstLine = true,
                FooterMustBeLastLine = true
            },
            ErrorLogging = new ErrorLoggingConfig
            {
                MaxDetailedErrors = 100,
                AggregateAfterMax = true,
                IncludeRawData = true,
                MaxRawDataLength = 500
            },
            FieldRules = new List<FieldValidationRule>()
        };
    }

    /// <summary>
    /// Creates a CDR-specific validation configuration with common field rules.
    /// </summary>
    public static FileValidationConfig CreateCdrConfig(string fileType)
    {
        var config = CreateDefaultConfig(fileType);

        config.FieldRules.AddRange(new[]
        {
            new FieldValidationRule
            {
                FieldName = "NtFileRecNum",
                FieldLabel = "Record Number",
                FieldIndex = 1,
                Type = FieldType.Integer,
                Required = false,
                MustBeNonNegative = true
            },
            new FieldValidationRule
            {
                FieldName = "SpCnRef",
                FieldLabel = "Service Connection Reference",
                Type = FieldType.Integer,
                Required = false
            },
            new FieldValidationRule
            {
                FieldName = "ClStartDt",
                FieldLabel = "Call Start Date",
                Type = FieldType.DateTime,
                Required = true,
                DateFormat = "yyyy-MM-dd HH:mm:ss",
                DateMustBeInPast = true
            },
            new FieldValidationRule
            {
                FieldName = "NtCost",
                FieldLabel = "Cost Amount",
                Type = FieldType.Decimal,
                Required = true,
                MustBeNonNegative = true,
                MaxValue = 999999.99m
            },
            new FieldValidationRule
            {
                FieldName = "NumCalled",
                FieldLabel = "Number Called",
                Type = FieldType.String,
                Required = false,
                MaxLength = 64
            }
        });

        return config;
    }

    /// <summary>
    /// Creates a CHG (charge) file validation configuration with common field rules.
    /// </summary>
    public static FileValidationConfig CreateChgConfig(string fileType)
    {
        var config = CreateDefaultConfig(fileType);

        config.FieldRules.AddRange(new[]
        {
            new FieldValidationRule
            {
                FieldName = "NtFileRecNum",
                FieldLabel = "Record Number",
                FieldIndex = 1,
                Type = FieldType.Integer,
                Required = false,
                MustBeNonNegative = true
            },
            new FieldValidationRule
            {
                FieldName = "PhoneNum",
                FieldLabel = "Phone Number",
                Type = FieldType.String,
                Required = false,
                MaxLength = 32
            },
            new FieldValidationRule
            {
                FieldName = "ChgCode",
                FieldLabel = "Charge Code",
                Type = FieldType.String,
                Required = false,
                MaxLength = 4
            },
            new FieldValidationRule
            {
                FieldName = "CostAmount",
                FieldLabel = "Charge Amount",
                Type = FieldType.Decimal,
                Required = true,
                MustBeNonNegative = true
            },
            new FieldValidationRule
            {
                FieldName = "StartDate",
                FieldLabel = "Start Date",
                Type = FieldType.DateTime,
                Required = false,
                DateFormat = "yyyy-MM-dd"
            },
            new FieldValidationRule
            {
                FieldName = "EndDate",
                FieldLabel = "End Date",
                Type = FieldType.DateTime,
                Required = false,
                DateFormat = "yyyy-MM-dd"
            }
        });

        return config;
    }
}
