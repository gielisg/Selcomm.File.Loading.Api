using System.Globalization;
using System.Text.RegularExpressions;
using FileLoading.Models;

namespace FileLoading.Validation;

/// <summary>
/// Generic validation engine that applies configured rules to files and fields.
/// Supports field-level type parsing, constraint validation, and error aggregation.
/// </summary>
public class ValidationEngine
{
    private readonly FileValidationConfig _config;
    private readonly ErrorAggregator _errorAggregator;
    private readonly Dictionary<string, FieldValidationRule> _fieldRulesByName;
    private readonly Dictionary<int, FieldValidationRule> _fieldRulesByIndex;
    private int _totalRecords;
    private int _validRecords;

    /// <summary>
    /// Creates a new validation engine with the specified configuration.
    /// </summary>
    public ValidationEngine(FileValidationConfig config)
    {
        _config = config;
        _errorAggregator = new ErrorAggregator(config.ErrorLogging);
        _errorAggregator.SetFileType(config.FileType);

        // Build lookup dictionaries for field rules
        _fieldRulesByName = config.FieldRules
            .Where(r => !string.IsNullOrEmpty(r.FieldName))
            .ToDictionary(r => r.FieldName, StringComparer.OrdinalIgnoreCase);

        _fieldRulesByIndex = config.FieldRules
            .Where(r => r.FieldIndex >= 0)
            .ToDictionary(r => r.FieldIndex);
    }

    /// <summary>
    /// Gets the underlying error aggregator for direct access.
    /// </summary>
    public ErrorAggregator ErrorAggregator => _errorAggregator;

    /// <summary>
    /// Gets the file rules configuration.
    /// </summary>
    public FileRules FileRules => _config.FileRules;

    /// <summary>
    /// Validates a single field value against its configured rules.
    /// Returns the parsed/validated value or null if validation fails.
    /// </summary>
    /// <param name="rawValue">The raw string value to validate.</param>
    /// <param name="fieldName">The field name to look up rules.</param>
    /// <param name="lineNumber">Line number for error reporting.</param>
    /// <param name="recordNumber">Record number for error reporting.</param>
    /// <param name="rawLine">Optional raw line for error context.</param>
    /// <returns>The parsed value if valid, null if invalid or empty.</returns>
    public object? ValidateField(string? rawValue, string fieldName, int lineNumber, int recordNumber, string? rawLine = null)
    {
        if (!_fieldRulesByName.TryGetValue(fieldName, out var rule))
        {
            // No validation rule configured - return value as-is
            return rawValue;
        }

        return ValidateFieldWithRule(rawValue, rule, lineNumber, recordNumber, rawLine);
    }

    /// <summary>
    /// Validates a field value by its index position.
    /// </summary>
    public object? ValidateFieldByIndex(string? rawValue, int fieldIndex, int lineNumber, int recordNumber, string? rawLine = null)
    {
        if (!_fieldRulesByIndex.TryGetValue(fieldIndex, out var rule))
        {
            // No validation rule configured - return value as-is
            return rawValue;
        }

        return ValidateFieldWithRule(rawValue, rule, lineNumber, recordNumber, rawLine);
    }

    /// <summary>
    /// Validates a field value against a specific rule.
    /// </summary>
    public object? ValidateFieldWithRule(string? rawValue, FieldValidationRule rule, int lineNumber, int recordNumber, string? rawLine = null)
    {
        // 1. Check required
        if (rule.Required && string.IsNullOrWhiteSpace(rawValue))
        {
            AddFieldError(ValidationErrorCodes.FieldRequired, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Required field '{rule.EffectiveLabel}' is empty",
                $"The {rule.EffectiveLabel} field is required but was not provided",
                $"Please provide a value for {rule.EffectiveLabel}");
            return null;
        }

        // If empty and not required, return null
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        // 2. Parse and validate by type
        return rule.Type switch
        {
            FieldType.Integer => ValidateInteger(rawValue, rule, lineNumber, recordNumber, rawLine),
            FieldType.Long => ValidateLong(rawValue, rule, lineNumber, recordNumber, rawLine),
            FieldType.Decimal => ValidateDecimal(rawValue, rule, lineNumber, recordNumber, rawLine),
            FieldType.DateTime => ValidateDateTime(rawValue, rule, lineNumber, recordNumber, rawLine),
            FieldType.Boolean => ValidateBoolean(rawValue, rule, lineNumber, recordNumber, rawLine),
            FieldType.String => ValidateString(rawValue, rule, lineNumber, recordNumber, rawLine),
            _ => rawValue
        };
    }

    /// <summary>
    /// Validates an integer field.
    /// </summary>
    private int? ValidateInteger(string rawValue, FieldValidationRule rule, int lineNumber, int recordNumber, string? rawLine)
    {
        if (!int.TryParse(rawValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            AddFieldError(ValidationErrorCodes.FieldParseInteger, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Cannot parse '{rawValue}' as integer",
                $"The {rule.EffectiveLabel} field contains '{rawValue}' which is not a valid whole number",
                $"Ensure {rule.EffectiveLabel} contains only digits (e.g., 123)");
            return null;
        }

        // Range checks
        if (rule.MustBeNonNegative == true && value < 0)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintNegative, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Value {value} is negative",
                $"The {rule.EffectiveLabel} field contains {value} but must not be negative",
                $"Ensure {rule.EffectiveLabel} is zero or greater");
            return null;
        }

        if (rule.MustBePositive == true && value <= 0)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintNotPositive, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Value {value} is not positive",
                $"The {rule.EffectiveLabel} field contains {value} but must be greater than zero",
                $"Ensure {rule.EffectiveLabel} is a positive number");
            return null;
        }

        if (rule.MinValue.HasValue && value < rule.MinValue)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintMin, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Value {value} is below minimum {rule.MinValue}",
                $"The {rule.EffectiveLabel} field contains {value} but must be at least {rule.MinValue}",
                $"Ensure {rule.EffectiveLabel} is {rule.MinValue} or greater");
            return null;
        }

        if (rule.MaxValue.HasValue && value > rule.MaxValue)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintMax, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Value {value} exceeds maximum {rule.MaxValue}",
                $"The {rule.EffectiveLabel} field contains {value} but must not exceed {rule.MaxValue}",
                $"Ensure {rule.EffectiveLabel} is {rule.MaxValue} or less");
            return null;
        }

        return value;
    }

    /// <summary>
    /// Validates a long integer field.
    /// </summary>
    private long? ValidateLong(string rawValue, FieldValidationRule rule, int lineNumber, int recordNumber, string? rawLine)
    {
        if (!long.TryParse(rawValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            AddFieldError(ValidationErrorCodes.FieldParseLong, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Cannot parse '{rawValue}' as long integer",
                $"The {rule.EffectiveLabel} field contains '{rawValue}' which is not a valid number",
                $"Ensure {rule.EffectiveLabel} contains only digits (e.g., 123456789)");
            return null;
        }

        // Range checks
        if (rule.MustBeNonNegative == true && value < 0)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintNegative, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Value {value} is negative",
                $"The {rule.EffectiveLabel} field contains {value} but must not be negative",
                $"Ensure {rule.EffectiveLabel} is zero or greater");
            return null;
        }

        if (rule.MustBePositive == true && value <= 0)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintNotPositive, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Value {value} is not positive",
                $"The {rule.EffectiveLabel} field contains {value} but must be greater than zero",
                $"Ensure {rule.EffectiveLabel} is a positive number");
            return null;
        }

        if (rule.MinValue.HasValue && value < (long)rule.MinValue)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintMin, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Value {value} is below minimum {rule.MinValue}",
                $"The {rule.EffectiveLabel} field contains {value} but must be at least {rule.MinValue}",
                $"Ensure {rule.EffectiveLabel} is {rule.MinValue} or greater");
            return null;
        }

        if (rule.MaxValue.HasValue && value > (long)rule.MaxValue)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintMax, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Value {value} exceeds maximum {rule.MaxValue}",
                $"The {rule.EffectiveLabel} field contains {value} but must not exceed {rule.MaxValue}",
                $"Ensure {rule.EffectiveLabel} is {rule.MaxValue} or less");
            return null;
        }

        return value;
    }

    /// <summary>
    /// Validates a decimal field.
    /// </summary>
    private decimal? ValidateDecimal(string rawValue, FieldValidationRule rule, int lineNumber, int recordNumber, string? rawLine)
    {
        if (!decimal.TryParse(rawValue.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            AddFieldError(ValidationErrorCodes.FieldParseDecimal, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Cannot parse '{rawValue}' as decimal",
                $"The {rule.EffectiveLabel} field contains '{rawValue}' which is not a valid number",
                $"Ensure {rule.EffectiveLabel} contains a valid number (e.g., 123.45)");
            return null;
        }

        // Range checks
        if (rule.MustBeNonNegative == true && value < 0)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintNegative, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Value {value} is negative",
                $"The {rule.EffectiveLabel} field contains {value} but must not be negative",
                $"Ensure {rule.EffectiveLabel} is zero or greater");
            return null;
        }

        if (rule.MustBePositive == true && value <= 0)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintNotPositive, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Value {value} is not positive",
                $"The {rule.EffectiveLabel} field contains {value} but must be greater than zero",
                $"Ensure {rule.EffectiveLabel} is a positive number");
            return null;
        }

        if (rule.MinValue.HasValue && value < rule.MinValue)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintMin, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Value {value} is below minimum {rule.MinValue}",
                $"The {rule.EffectiveLabel} field contains {value} but must be at least {rule.MinValue}",
                $"Ensure {rule.EffectiveLabel} is {rule.MinValue} or greater");
            return null;
        }

        if (rule.MaxValue.HasValue && value > rule.MaxValue)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintMax, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Value {value} exceeds maximum {rule.MaxValue}",
                $"The {rule.EffectiveLabel} field contains {value} but must not exceed {rule.MaxValue}",
                $"Ensure {rule.EffectiveLabel} is {rule.MaxValue} or less");
            return null;
        }

        return value;
    }

    /// <summary>
    /// Validates a datetime field.
    /// </summary>
    private DateTime? ValidateDateTime(string rawValue, FieldValidationRule rule, int lineNumber, int recordNumber, string? rawLine)
    {
        DateTime? value = null;
        var trimmedValue = rawValue.Trim();

        // Try specific format first
        if (!string.IsNullOrEmpty(rule.DateFormat))
        {
            if (DateTime.TryParseExact(trimmedValue, rule.DateFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactResult))
            {
                value = exactResult;
            }
        }

        // Try common formats if specific format didn't work
        if (!value.HasValue)
        {
            string[] formats =
            {
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd",
                "dd/MM/yyyy HH:mm:ss",
                "dd/MM/yyyy",
                "MM/dd/yyyy HH:mm:ss",
                "MM/dd/yyyy",
                "yyyy/MM/dd HH:mm:ss",
                "yyyy/MM/dd",
                "yyyyMMdd",
                "yyyyMMddHHmmss"
            };

            foreach (var fmt in formats)
            {
                if (DateTime.TryParseExact(trimmedValue, fmt,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
                {
                    value = result;
                    break;
                }
            }
        }

        // Final fallback to generic parse
        if (!value.HasValue)
        {
            if (DateTime.TryParse(trimmedValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var genericResult))
            {
                value = genericResult;
            }
        }

        if (!value.HasValue)
        {
            var expectedFormat = rule.DateFormat ?? "yyyy-MM-dd HH:mm:ss";
            AddFieldError(ValidationErrorCodes.FieldParseDateTime, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Cannot parse '{rawValue}' as datetime",
                $"The {rule.EffectiveLabel} field contains '{rawValue}' which is not a valid date/time",
                $"Use format {expectedFormat} (e.g., 2024-01-15 14:30:00)");
            return null;
        }

        // Date constraint checks
        if (rule.DateMustBeInPast == true && value > DateTime.Now)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintDateFuture, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Date {value:yyyy-MM-dd} is in the future",
                $"The {rule.EffectiveLabel} field contains a future date but must be in the past",
                $"Ensure {rule.EffectiveLabel} is a past date");
            return null;
        }

        if (rule.DateMustBeInFuture == true && value < DateTime.Now)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintDatePast, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Date {value:yyyy-MM-dd} is in the past",
                $"The {rule.EffectiveLabel} field contains a past date but must be in the future",
                $"Ensure {rule.EffectiveLabel} is a future date");
            return null;
        }

        if (rule.DateMinValue.HasValue && value < rule.DateMinValue)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintDateMin, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Date {value:yyyy-MM-dd} is before minimum {rule.DateMinValue:yyyy-MM-dd}",
                $"The {rule.EffectiveLabel} field contains a date before the allowed minimum",
                $"Ensure {rule.EffectiveLabel} is on or after {rule.DateMinValue:yyyy-MM-dd}");
            return null;
        }

        if (rule.DateMaxValue.HasValue && value > rule.DateMaxValue)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintDateMax, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Date {value:yyyy-MM-dd} is after maximum {rule.DateMaxValue:yyyy-MM-dd}",
                $"The {rule.EffectiveLabel} field contains a date after the allowed maximum",
                $"Ensure {rule.EffectiveLabel} is on or before {rule.DateMaxValue:yyyy-MM-dd}");
            return null;
        }

        return value;
    }

    /// <summary>
    /// Validates a boolean field.
    /// </summary>
    private bool? ValidateBoolean(string rawValue, FieldValidationRule rule, int lineNumber, int recordNumber, string? rawLine)
    {
        var trimmedValue = rawValue.Trim().ToLowerInvariant();

        // Common boolean representations
        if (trimmedValue == "true" || trimmedValue == "1" || trimmedValue == "yes" || trimmedValue == "y")
        {
            return true;
        }

        if (trimmedValue == "false" || trimmedValue == "0" || trimmedValue == "no" || trimmedValue == "n")
        {
            return false;
        }

        AddFieldError(ValidationErrorCodes.FieldParseBoolean, rule, lineNumber, recordNumber, rawValue, rawLine,
            $"Cannot parse '{rawValue}' as boolean",
            $"The {rule.EffectiveLabel} field contains '{rawValue}' which is not a valid true/false value",
            $"Use 'true', 'false', '1', '0', 'yes', or 'no' for {rule.EffectiveLabel}");
        return null;
    }

    /// <summary>
    /// Validates a string field.
    /// </summary>
    private string? ValidateString(string rawValue, FieldValidationRule rule, int lineNumber, int recordNumber, string? rawLine)
    {
        var value = rawValue; // Don't trim - preserve whitespace unless configured otherwise

        // Length checks
        if (rule.MinLength.HasValue && value.Length < rule.MinLength)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintMinLength, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Value length {value.Length} is below minimum {rule.MinLength}",
                $"The {rule.EffectiveLabel} field must be at least {rule.MinLength} characters",
                $"Ensure {rule.EffectiveLabel} has at least {rule.MinLength} characters");
            return null;
        }

        if (rule.MaxLength.HasValue && value.Length > rule.MaxLength)
        {
            AddFieldError(ValidationErrorCodes.FieldConstraintMaxLength, rule, lineNumber, recordNumber, rawValue, rawLine,
                $"Value length {value.Length} exceeds maximum {rule.MaxLength}",
                $"The {rule.EffectiveLabel} field must not exceed {rule.MaxLength} characters",
                $"Ensure {rule.EffectiveLabel} has at most {rule.MaxLength} characters");
            return null;
        }

        // Regex pattern check
        if (!string.IsNullOrEmpty(rule.RegexPattern))
        {
            try
            {
                if (!Regex.IsMatch(value, rule.RegexPattern))
                {
                    AddFieldError(ValidationErrorCodes.FieldConstraintPattern, rule, lineNumber, recordNumber, rawValue, rawLine,
                        $"Value '{value}' does not match pattern '{rule.RegexPattern}'",
                        $"The {rule.EffectiveLabel} field value does not match the required format",
                        $"Ensure {rule.EffectiveLabel} matches the expected pattern");
                    return null;
                }
            }
            catch (ArgumentException)
            {
                // Invalid regex pattern - skip this validation
            }
        }

        // Allowed values check
        if (rule.AllowedValues != null && rule.AllowedValues.Count > 0)
        {
            if (!rule.AllowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                var allowedList = string.Join(", ", rule.AllowedValues.Take(5));
                if (rule.AllowedValues.Count > 5)
                {
                    allowedList += $", ... ({rule.AllowedValues.Count - 5} more)";
                }

                AddFieldError(ValidationErrorCodes.FieldConstraintEnum, rule, lineNumber, recordNumber, rawValue, rawLine,
                    $"Value '{value}' is not in allowed list",
                    $"The {rule.EffectiveLabel} field contains '{value}' which is not a valid option",
                    $"Allowed values for {rule.EffectiveLabel}: {allowedList}");
                return null;
            }
        }

        return value;
    }

    /// <summary>
    /// Adds a file-level error.
    /// </summary>
    public void AddFileLevelError(string errorCode, string message, string userMessage, string? suggestion = null)
    {
        _errorAggregator.AddError(new ValidationError
        {
            ErrorCode = errorCode,
            ErrorCategory = ValidationErrorCategory.FileStructure,
            Message = message,
            UserMessage = userMessage,
            Suggestion = suggestion
        });
    }

    /// <summary>
    /// Adds a field-level error to the aggregator.
    /// </summary>
    private void AddFieldError(
        string errorCode,
        FieldValidationRule rule,
        int lineNumber,
        int recordNumber,
        string? rawValue,
        string? rawLine,
        string message,
        string userMessage,
        string suggestion)
    {
        _errorAggregator.AddError(new ValidationError
        {
            ErrorCode = errorCode,
            ErrorCategory = rule.Type switch
            {
                FieldType.String => ValidationErrorCategory.FieldConstraint,
                _ when errorCode.StartsWith("FIELD_PARSE_") => ValidationErrorCategory.FieldParse,
                _ => ValidationErrorCategory.FieldConstraint
            },
            LineNumber = lineNumber,
            RecordNumber = recordNumber,
            FieldName = rule.FieldName,
            FieldIndex = rule.FieldIndex,
            FieldLabel = rule.EffectiveLabel,
            Message = message,
            UserMessage = userMessage,
            Suggestion = suggestion,
            RawValue = rawValue,
            RawLine = rawLine,
            ExpectedType = rule.Type.ToString(),
            ExpectedFormat = rule.DateFormat,
            ConstraintDescription = BuildConstraintDescription(rule)
        });
    }

    /// <summary>
    /// Builds a human-readable constraint description for a field rule.
    /// </summary>
    private static string BuildConstraintDescription(FieldValidationRule rule)
    {
        var parts = new List<string>();

        parts.Add(rule.Type.ToString().ToLower());

        if (rule.Required)
            parts.Add("required");

        if (rule.MustBeNonNegative == true)
            parts.Add("non-negative");

        if (rule.MustBePositive == true)
            parts.Add("positive");

        if (rule.MinValue.HasValue)
            parts.Add($"min {rule.MinValue}");

        if (rule.MaxValue.HasValue)
            parts.Add($"max {rule.MaxValue}");

        if (rule.DateMustBeInPast == true)
            parts.Add("must be in past");

        if (rule.DateMustBeInFuture == true)
            parts.Add("must be in future");

        if (!string.IsNullOrEmpty(rule.DateFormat))
            parts.Add($"format: {rule.DateFormat}");

        if (rule.MinLength.HasValue)
            parts.Add($"min length {rule.MinLength}");

        if (rule.MaxLength.HasValue)
            parts.Add($"max length {rule.MaxLength}");

        if (rule.AllowedValues != null && rule.AllowedValues.Count > 0)
            parts.Add($"allowed values: {string.Join(", ", rule.AllowedValues.Take(3))}...");

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Increments the total record count.
    /// </summary>
    public void IncrementTotalRecords()
    {
        _totalRecords++;
    }

    /// <summary>
    /// Increments the valid record count.
    /// </summary>
    public void IncrementValidRecords()
    {
        _validRecords++;
    }

    /// <summary>
    /// Gets the final validation result.
    /// </summary>
    public FileValidationResult GetResult()
    {
        _errorAggregator.SetRecordCounts(_totalRecords, _validRecords);
        return _errorAggregator.BuildResult();
    }

    /// <summary>
    /// Checks if there are any file-level errors.
    /// </summary>
    public bool HasFileLevelErrors => _errorAggregator.HasFileLevelErrors;

    /// <summary>
    /// Gets the current total error count.
    /// </summary>
    public int TotalErrorCount => _errorAggregator.TotalErrorCount;

    /// <summary>
    /// Gets a validation rule by field name.
    /// </summary>
    public FieldValidationRule? GetRuleByName(string fieldName)
    {
        return _fieldRulesByName.TryGetValue(fieldName, out var rule) ? rule : null;
    }

    /// <summary>
    /// Gets a validation rule by field index.
    /// </summary>
    public FieldValidationRule? GetRuleByIndex(int fieldIndex)
    {
        return _fieldRulesByIndex.TryGetValue(fieldIndex, out var rule) ? rule : null;
    }

    /// <summary>
    /// Gets all configured field rules.
    /// </summary>
    public IReadOnlyList<FieldValidationRule> FieldRules => _config.FieldRules;
}
