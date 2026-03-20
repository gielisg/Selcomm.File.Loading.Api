using System.Text;
using FileLoading.Models;

namespace FileLoading.Validation;

/// <summary>
/// Aggregates validation errors with smart thresholding.
/// Stores detailed errors up to MaxDetailedErrors, then aggregates by type.
/// Produces AI-friendly summaries for conversational explanations.
/// </summary>
public class ErrorAggregator
{
    private readonly ErrorLoggingConfig _config;
    private readonly List<ValidationError> _detailedErrors = new();
    private readonly Dictionary<string, AggregatedError> _aggregatedErrors = new();
    private readonly List<ValidationError> _fileLevelErrors = new();
    private int _totalErrorCount = 0;
    private int _totalRecords = 0;
    private int _validRecords = 0;
    private string _fileType = string.Empty;

    public ErrorAggregator(ErrorLoggingConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Sets the file type for result reporting.
    /// </summary>
    public void SetFileType(string fileType)
    {
        _fileType = fileType;
    }

    /// <summary>
    /// Updates the record counts for result reporting.
    /// </summary>
    public void SetRecordCounts(int totalRecords, int validRecords)
    {
        _totalRecords = totalRecords;
        _validRecords = validRecords;
    }

    /// <summary>
    /// Adds a validation error, applying aggregation rules.
    /// </summary>
    public void AddError(ValidationError error)
    {
        _totalErrorCount++;

        // Truncate raw data if configured
        if (error.RawLine != null && error.RawLine.Length > _config.MaxRawDataLength)
        {
            error.RawLine = error.RawLine.Substring(0, _config.MaxRawDataLength) + "...";
        }
        if (error.RawValue != null && error.RawValue.Length > _config.MaxRawDataLength)
        {
            error.RawValue = error.RawValue.Substring(0, _config.MaxRawDataLength) + "...";
        }

        // File-level errors always stored with full detail
        if (error.IsFileLevelError)
        {
            _fileLevelErrors.Add(error);
            return;
        }

        // Under threshold: store full detail
        if (_detailedErrors.Count < _config.MaxDetailedErrors)
        {
            _detailedErrors.Add(error);
        }

        // Always aggregate for summary (if aggregation enabled)
        if (_config.AggregateAfterMax)
        {
            AggregateError(error);
        }
    }

    /// <summary>
    /// Adds a batch of validation errors.
    /// </summary>
    public void AddErrors(IEnumerable<ValidationError> errors)
    {
        foreach (var error in errors)
        {
            AddError(error);
        }
    }

    /// <summary>
    /// Aggregates an error by type/field combination.
    /// </summary>
    private void AggregateError(ValidationError error)
    {
        var key = error.AggregationKey;

        if (!_aggregatedErrors.TryGetValue(key, out var agg))
        {
            agg = new AggregatedError
            {
                ErrorCode = error.ErrorCode,
                FieldName = error.FieldName,
                FieldLabel = error.FieldLabel,
                Message = error.UserMessage
            };
            _aggregatedErrors[key] = agg;
        }

        agg.Count++;

        // Store sample values (up to max)
        if (agg.SampleLineNumbers.Count < _config.MaxSampleValues)
        {
            if (error.LineNumber.HasValue)
            {
                agg.SampleLineNumbers.Add(error.LineNumber.Value);
            }
            agg.SampleValues.Add(error.RawValue);
        }
    }

    /// <summary>
    /// Builds the final validation result with all errors and summaries.
    /// </summary>
    public FileValidationResult BuildResult()
    {
        var result = new FileValidationResult
        {
            FileType = _fileType,
            TotalRecords = _totalRecords,
            ValidRecords = _validRecords,
            InvalidRecords = _totalRecords - _validRecords,
            TotalErrors = _totalErrorCount,
            DetailedErrors = _detailedErrors.ToList(),
            FileLevelErrors = _fileLevelErrors.ToList(),
            AggregatedErrors = _aggregatedErrors.Values
                .OrderByDescending(e => e.Count)
                .ToList()
        };

        // Determine overall validity
        result.IsValid = _totalErrorCount == 0 && _fileLevelErrors.Count == 0;

        // Build summary string
        result.Summary = BuildSummaryString();

        // Build AI summary
        result.AISummary = BuildAISummary(result);

        return result;
    }

    /// <summary>
    /// Builds a human-readable summary string for logging.
    /// </summary>
    private string BuildSummaryString()
    {
        if (_totalErrorCount == 0 && _fileLevelErrors.Count == 0)
        {
            return "No validation errors";
        }

        var sb = new StringBuilder();

        // File-level errors first
        if (_fileLevelErrors.Count > 0)
        {
            sb.Append($"{_fileLevelErrors.Count} file-level error(s): ");
            sb.Append(string.Join("; ", _fileLevelErrors.Select(e => e.Message)));
            if (_totalErrorCount > _fileLevelErrors.Count)
            {
                sb.Append(". ");
            }
        }

        // Record-level errors
        var recordErrorCount = _totalErrorCount - _fileLevelErrors.Count;
        if (recordErrorCount > 0)
        {
            sb.Append($"{recordErrorCount} record validation error(s): ");

            var topErrors = _aggregatedErrors.Values
                .OrderByDescending(e => e.Count)
                .Take(5)
                .Select(e =>
                {
                    var fieldPart = !string.IsNullOrEmpty(e.FieldLabel) ? e.FieldLabel : e.FieldName;
                    return !string.IsNullOrEmpty(fieldPart)
                        ? $"{e.Count} {fieldPart} errors"
                        : $"{e.Count} {e.ErrorCode} errors";
                });

            sb.Append(string.Join(", ", topErrors));

            if (_aggregatedErrors.Count > 5)
            {
                sb.Append($", and {_aggregatedErrors.Count - 5} more error types");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds an AI-consumable summary for conversational explanations.
    /// </summary>
    private ValidationSummaryForAI BuildAISummary(FileValidationResult result)
    {
        var summary = new ValidationSummaryForAI();

        if (_totalErrorCount == 0 && _fileLevelErrors.Count == 0)
        {
            summary.OverallStatus = "File validation passed successfully";
            summary.CanPartiallyProcess = true;
            return summary;
        }

        // Build overall status
        if (_fileLevelErrors.Count > 0)
        {
            summary.OverallStatus = $"File rejected due to {_fileLevelErrors.Count} file structure error(s)";
            summary.CanPartiallyProcess = false;
        }
        else
        {
            var recordErrorCount = _totalErrorCount - _fileLevelErrors.Count;
            summary.OverallStatus = $"File validation completed with {recordErrorCount} record error(s) across {_aggregatedErrors.Count} different issue(s)";
            summary.CanPartiallyProcess = result.ValidRecords > 0;
        }

        // Build main issues in plain English
        summary.MainIssues = new List<string>();

        // File-level issues first
        foreach (var error in _fileLevelErrors.Take(3))
        {
            summary.MainIssues.Add(error.UserMessage);
        }

        // Top record-level issues
        foreach (var agg in _aggregatedErrors.Values.OrderByDescending(e => e.Count).Take(5))
        {
            var fieldPart = !string.IsNullOrEmpty(agg.FieldLabel) ? agg.FieldLabel : agg.FieldName;
            if (!string.IsNullOrEmpty(fieldPart))
            {
                summary.MainIssues.Add($"{agg.Count} records have invalid {fieldPart}: {agg.Message}");
            }
            else
            {
                summary.MainIssues.Add($"{agg.Count} records failed: {agg.Message}");
            }
        }

        // Error counts by field
        summary.ErrorCountsByField = _aggregatedErrors.Values
            .Where(e => !string.IsNullOrEmpty(e.FieldName))
            .GroupBy(e => e.FieldName!)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Count));

        // Error counts by type
        summary.ErrorCountsByType = _aggregatedErrors.Values
            .GroupBy(e => e.ErrorCode)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Count));

        // Suggested actions
        summary.SuggestedActions = new List<string>();

        // File-level suggestions
        foreach (var error in _fileLevelErrors.Where(e => !string.IsNullOrEmpty(e.Suggestion)).Take(2))
        {
            summary.SuggestedActions.Add(error.Suggestion!);
        }

        // Record-level suggestions (based on most common errors)
        foreach (var agg in _aggregatedErrors.Values.OrderByDescending(e => e.Count).Take(3))
        {
            var fieldPart = !string.IsNullOrEmpty(agg.FieldLabel) ? agg.FieldLabel : agg.FieldName;
            if (!string.IsNullOrEmpty(fieldPart))
            {
                summary.SuggestedActions.Add($"Review and correct {fieldPart} values - {agg.Count} records affected");
            }
        }

        return summary;
    }

    /// <summary>
    /// Gets the current count of total errors.
    /// </summary>
    public int TotalErrorCount => _totalErrorCount;

    /// <summary>
    /// Gets the current count of file-level errors.
    /// </summary>
    public int FileLevelErrorCount => _fileLevelErrors.Count;

    /// <summary>
    /// Gets the current count of detailed errors.
    /// </summary>
    public int DetailedErrorCount => _detailedErrors.Count;

    /// <summary>
    /// Returns true if the maximum detailed error count has been reached.
    /// </summary>
    public bool IsAtDetailedErrorLimit => _detailedErrors.Count >= _config.MaxDetailedErrors;

    /// <summary>
    /// Returns true if there are any file-level errors.
    /// </summary>
    public bool HasFileLevelErrors => _fileLevelErrors.Count > 0;

    /// <summary>
    /// Clears all accumulated errors and resets the aggregator.
    /// </summary>
    public void Clear()
    {
        _detailedErrors.Clear();
        _aggregatedErrors.Clear();
        _fileLevelErrors.Clear();
        _totalErrorCount = 0;
        _totalRecords = 0;
        _validRecords = 0;
    }
}
