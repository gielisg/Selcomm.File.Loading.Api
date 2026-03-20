using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using FileLoading.Interfaces;
using FileLoading.Models;
using FileLoading.Validation;

namespace FileLoading.Parsers;

/// <summary>
/// Abstract base class for file parsers.
/// Implements template method pattern for streaming file parsing.
/// Applies same business rules as legacy 4GL ntfileload.4gl.
/// </summary>
public abstract class BaseFileParser : IFileParser
{
    protected readonly ILogger Logger;

    /// <summary>
    /// Validation engine for field-level validation.
    /// Set via SetValidationConfig if validation is required.
    /// </summary>
    protected ValidationEngine? ValidationEngine { get; private set; }

    protected BaseFileParser(ILogger logger)
    {
        Logger = logger;
    }

    /// <summary>
    /// Sets the validation configuration for this parser.
    /// When set, field-level validation will be applied during parsing.
    /// </summary>
    /// <param name="config">The validation configuration to use.</param>
    public void SetValidationConfig(FileValidationConfig config)
    {
        ValidationEngine = new ValidationEngine(config);
        Logger.LogDebug("Validation engine configured for file type {FileType} with {RuleCount} field rules",
            config.FileType, config.FieldRules.Count);
    }

    /// <summary>
    /// Gets the validation result if validation was configured.
    /// Returns null if no validation engine was set.
    /// </summary>
    public FileValidationResult? GetValidationResult()
    {
        return ValidationEngine?.GetResult();
    }

    /// <summary>
    /// Checks if the validation engine has any file-level errors.
    /// </summary>
    public bool HasFileLevelValidationErrors => ValidationEngine?.HasFileLevelErrors ?? false;

    /// <summary>File type code this parser handles.</summary>
    public abstract string FileType { get; }

    /// <summary>File class code (CDR, CHG, EBL, SVC, ORD).</summary>
    public abstract string FileClassCode { get; }

    /// <summary>
    /// Parse file stream and return staging records.
    /// Collects all errors for later processing - does not stop on first error.
    /// Applies strict 4GL validation rules: header required, trailer required, counts must match.
    /// </summary>
    public virtual async Task<ParseResult> ParseAsync(Stream fileStream, ParseContext context)
    {
        var result = new ParseResult { Success = true };
        var detailRecordCount = 0;
        var lineNumber = 0;
        var foundHeader = false;
        var foundTrailer = false;
        int? trailerRecordCount = null;
        var fileLevelErrorFound = false;

        using var reader = new StreamReader(fileStream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            lineNumber++;

            try
            {
                var recordType = DetermineRecordType(line);

                switch (recordType)
                {
                    case RecordCategory.Header:
                        // 4GL Rule: Header must be first record
                        if (detailRecordCount > 0)
                        {
                            AddFileLevelError(result, FileErrorCodes.HeaderWrongPlace,
                                "Error: Header in wrong place", lineNumber, line);
                            fileLevelErrorFound = true;
                            Logger.LogError("File {FileRef}: Header found after detail records at line {Line}",
                                context.FileRef, lineNumber);
                        }
                        // 4GL Rule: No multiple headers
                        else if (foundHeader)
                        {
                            AddFileLevelError(result, FileErrorCodes.HeaderMultiple,
                                "Error: Multiple headers found", lineNumber, line);
                            fileLevelErrorFound = true;
                            Logger.LogError("File {FileRef}: Multiple headers found at line {Line}",
                                context.FileRef, lineNumber);
                        }
                        else
                        {
                            ParseHeader(line, context);
                            foundHeader = true;
                        }
                        break;

                    case RecordCategory.Trailer:
                        // 4GL Rule: No multiple trailers
                        if (foundTrailer)
                        {
                            AddFileLevelError(result, FileErrorCodes.TrailerMultiple,
                                "Error: Multiple trailers found", lineNumber, line);
                            fileLevelErrorFound = true;
                            Logger.LogError("File {FileRef}: Multiple trailers found at line {Line}",
                                context.FileRef, lineNumber);
                        }
                        else
                        {
                            trailerRecordCount = ExtractTrailerRecordCount(line);
                            ParseTrailer(line, context, result);
                            foundTrailer = true;
                        }
                        break;

                    case RecordCategory.Detail:
                        // 4GL Rule: Header must come before details
                        if (!foundHeader)
                        {
                            AddFileLevelError(result, FileErrorCodes.HeaderMissing,
                                "Error: Header missing - detail record found before header", lineNumber, line);
                            fileLevelErrorFound = true;
                            Logger.LogError("File {FileRef}: Detail record found before header at line {Line}",
                                context.FileRef, lineNumber);
                            // Still process the record to collect all errors
                        }

                        detailRecordCount++;
                        var record = ParseDetailRecord(line, context, detailRecordCount);
                        if (record != null)
                        {
                            result.Records.Add(record);
                            if (record.IsValid)
                            {
                                result.RecordsParsed++;
                            }
                            else
                            {
                                result.RecordsFailed++;
                                // Add record-level error
                                result.Errors.Add(new ParseError
                                {
                                    ErrorCode = FileErrorCodes.ParseError,
                                    Message = record.ValidationError ?? "Record validation failed",
                                    LineNumber = lineNumber,
                                    RawData = line,
                                    IsFileLevelError = false
                                });
                            }
                        }
                        break;

                    case RecordCategory.Skip:
                        // Ignored record (comments, blank lines, etc.)
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error parsing record at line {LineNumber} in file {FileRef}",
                    lineNumber, context.FileRef);

                detailRecordCount++;
                result.Records.Add(new ParsedRecord
                {
                    RecordNumber = detailRecordCount,
                    RecordType = "ERROR",
                    IsValid = false,
                    ValidationError = ex.Message,
                    Fields = new Dictionary<string, object> { { "RawData", line } }
                });
                result.RecordsFailed++;

                result.Errors.Add(new ParseError
                {
                    ErrorCode = FileErrorCodes.ParseError,
                    Message = $"Exception: {ex.Message}",
                    LineNumber = lineNumber,
                    RawData = line,
                    IsFileLevelError = false
                });
            }
        }

        // 4GL Rule: Header is required
        if (!foundHeader)
        {
            AddFileLevelError(result, FileErrorCodes.HeaderMissing, "Error: Did not find header", null, null);
            fileLevelErrorFound = true;
            Logger.LogError("File {FileRef}: No header record found", context.FileRef);
        }

        // 4GL Rule: Trailer is required
        if (!foundTrailer)
        {
            AddFileLevelError(result, FileErrorCodes.TrailerMissing, "Error: Did not find trailer", null, null);
            fileLevelErrorFound = true;
            Logger.LogError("File {FileRef}: No trailer record found", context.FileRef);
        }

        // 4GL Rule: Trailer count must match detail count
        if (trailerRecordCount.HasValue && trailerRecordCount.Value != detailRecordCount)
        {
            var errorMsg = $"Error: Trailer count {trailerRecordCount.Value} does not match number of records {detailRecordCount}";
            AddFileLevelError(result, FileErrorCodes.TrailerCountMismatch, errorMsg, null, null);
            fileLevelErrorFound = true;
            Logger.LogError("File {FileRef}: Trailer count {TrailerCount} does not reconcile to detail count {DetailCount}",
                context.FileRef, trailerRecordCount.Value, detailRecordCount);
        }

        // Set overall success based on file-level errors only
        // Record-level errors don't fail the file (matching 4GL behavior where bad records go to nt_cl_not_load)
        result.Success = !fileLevelErrorFound;
        if (!result.Success)
        {
            result.ErrorMessage = result.Errors.FirstOrDefault(e => e.IsFileLevelError)?.Message ?? "File validation failed";
        }

        Logger.LogInformation("Parsed {FileRef}: {Parsed} records, {Failed} failed, {Errors} total errors",
            context.FileRef, result.RecordsParsed, result.RecordsFailed, result.Errors.Count);

        return result;
    }

    /// <summary>
    /// Validate file structure (header/trailer match, sequence numbers, etc.)
    /// Applies strict 4GL validation rules.
    /// </summary>
    public virtual ValidationResult ValidateFile(Stream fileStream)
    {
        try
        {
            using var reader = new StreamReader(fileStream);
            string? line;
            var lineNumber = 0;
            var detailCount = 0;
            var foundHeader = false;
            var foundTrailer = false;
            int? sequenceNumber = null;
            int? trailerRecordCount = null;

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                lineNumber++;
                var recordType = DetermineRecordType(line);

                switch (recordType)
                {
                    case RecordCategory.Header:
                        // 4GL Rule: Header must be first
                        if (detailCount > 0)
                        {
                            return new ValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = "Error: Header in wrong place"
                            };
                        }

                        // 4GL Rule: No multiple headers
                        if (foundHeader)
                        {
                            return new ValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = "Error: Multiple headers found"
                            };
                        }

                        sequenceNumber = ExtractSequenceNumber(line);
                        foundHeader = true;
                        break;

                    case RecordCategory.Trailer:
                        // 4GL Rule: No multiple trailers
                        if (foundTrailer)
                        {
                            return new ValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = "Error: Multiple trailers found"
                            };
                        }

                        trailerRecordCount = ExtractTrailerRecordCount(line);
                        foundTrailer = true;
                        break;

                    case RecordCategory.Detail:
                        // 4GL Rule: Must have header first
                        if (!foundHeader)
                        {
                            return new ValidationResult
                            {
                                IsValid = false,
                                ErrorMessage = "Error: Header missing"
                            };
                        }
                        detailCount++;
                        break;
                }
            }

            // 4GL Rule: File cannot be empty
            if (lineNumber == 0)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "File is empty"
                };
            }

            // 4GL Rule: Header is required
            if (!foundHeader)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Error: Did not find header"
                };
            }

            // 4GL Rule: Trailer is required
            if (!foundTrailer)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Error: Did not find trailer"
                };
            }

            // 4GL Rule: Trailer count must match
            if (trailerRecordCount.HasValue && trailerRecordCount.Value != detailCount)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    SequenceNumber = sequenceNumber,
                    RecordCount = detailCount,
                    ErrorMessage = $"Error: Trailer count {trailerRecordCount.Value} does not match number of records {detailCount}"
                };
            }

            return new ValidationResult
            {
                IsValid = true,
                SequenceNumber = sequenceNumber,
                RecordCount = detailCount
            };
        }
        catch (Exception ex)
        {
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Validation error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Streaming validation pass - validates file structure without accumulating records in memory.
    /// Only checks header/trailer rules, counts records, and collects file-level errors.
    /// Does NOT parse or store detail records.
    /// </summary>
    public virtual async Task<StreamingValidationResult> ValidateFileStreamingAsync(
        Stream fileStream,
        ParseContext context,
        CancellationToken cancellationToken = default)
    {
        var result = new StreamingValidationResult { IsValid = true };
        var detailRecordCount = 0;
        var lineNumber = 0;
        var foundHeader = false;
        var foundTrailer = false;
        int? trailerRecordCount = null;

        using var reader = new StreamReader(fileStream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            lineNumber++;

            try
            {
                var recordType = DetermineRecordType(line);

                switch (recordType)
                {
                    case RecordCategory.Header:
                        // 4GL Rule: Header must be first record
                        if (detailRecordCount > 0)
                        {
                            result.IsValid = false;
                            result.Errors.Add(new ParseError
                            {
                                ErrorCode = FileErrorCodes.HeaderWrongPlace,
                                Message = "Error: Header in wrong place",
                                LineNumber = lineNumber,
                                RawData = line,
                                IsFileLevelError = true
                            });
                            Logger.LogError("File {FileRef}: Header found after detail records at line {Line}",
                                context.FileRef, lineNumber);
                        }
                        // 4GL Rule: No multiple headers
                        else if (foundHeader)
                        {
                            result.IsValid = false;
                            result.Errors.Add(new ParseError
                            {
                                ErrorCode = FileErrorCodes.HeaderMultiple,
                                Message = "Error: Multiple headers found",
                                LineNumber = lineNumber,
                                RawData = line,
                                IsFileLevelError = true
                            });
                            Logger.LogError("File {FileRef}: Multiple headers found at line {Line}",
                                context.FileRef, lineNumber);
                        }
                        else
                        {
                            result.SequenceNumber = ExtractSequenceNumber(line);
                            foundHeader = true;
                        }
                        break;

                    case RecordCategory.Trailer:
                        // 4GL Rule: No multiple trailers
                        if (foundTrailer)
                        {
                            result.IsValid = false;
                            result.Errors.Add(new ParseError
                            {
                                ErrorCode = FileErrorCodes.TrailerMultiple,
                                Message = "Error: Multiple trailers found",
                                LineNumber = lineNumber,
                                RawData = line,
                                IsFileLevelError = true
                            });
                            Logger.LogError("File {FileRef}: Multiple trailers found at line {Line}",
                                context.FileRef, lineNumber);
                        }
                        else
                        {
                            trailerRecordCount = ExtractTrailerRecordCount(line);
                            foundTrailer = true;
                        }
                        break;

                    case RecordCategory.Detail:
                        // 4GL Rule: Header must come before details
                        if (!foundHeader)
                        {
                            result.IsValid = false;
                            result.Errors.Add(new ParseError
                            {
                                ErrorCode = FileErrorCodes.HeaderMissing,
                                Message = "Error: Header missing - detail record found before header",
                                LineNumber = lineNumber,
                                RawData = line,
                                IsFileLevelError = true
                            });
                            Logger.LogError("File {FileRef}: Detail record found before header at line {Line}",
                                context.FileRef, lineNumber);
                        }
                        detailRecordCount++;
                        break;

                    case RecordCategory.Skip:
                        // Ignored record (comments, blank lines, etc.)
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error during streaming validation at line {LineNumber} in file {FileRef}",
                    lineNumber, context.FileRef);
                result.IsValid = false;
                result.Errors.Add(new ParseError
                {
                    ErrorCode = FileErrorCodes.ParseError,
                    Message = $"Exception during validation: {ex.Message}",
                    LineNumber = lineNumber,
                    IsFileLevelError = true
                });
            }
        }

        // Check for cancellation
        if (cancellationToken.IsCancellationRequested)
        {
            result.IsValid = false;
            result.ErrorMessage = "Validation cancelled";
            return result;
        }

        // 4GL Rule: Header is required
        if (!foundHeader)
        {
            result.IsValid = false;
            result.Errors.Add(new ParseError
            {
                ErrorCode = FileErrorCodes.HeaderMissing,
                Message = "Error: Did not find header",
                IsFileLevelError = true
            });
            Logger.LogError("File {FileRef}: No header record found", context.FileRef);
        }

        // 4GL Rule: Trailer is required
        if (!foundTrailer)
        {
            result.IsValid = false;
            result.Errors.Add(new ParseError
            {
                ErrorCode = FileErrorCodes.TrailerMissing,
                Message = "Error: Did not find trailer",
                IsFileLevelError = true
            });
            Logger.LogError("File {FileRef}: No trailer record found", context.FileRef);
        }

        // 4GL Rule: Trailer count must match detail count
        if (trailerRecordCount.HasValue && trailerRecordCount.Value != detailRecordCount)
        {
            var errorMsg = $"Error: Trailer count {trailerRecordCount.Value} does not match number of records {detailRecordCount}";
            result.IsValid = false;
            result.Errors.Add(new ParseError
            {
                ErrorCode = FileErrorCodes.TrailerCountMismatch,
                Message = errorMsg,
                IsFileLevelError = true
            });
            Logger.LogError("File {FileRef}: Trailer count {TrailerCount} does not reconcile to detail count {DetailCount}",
                context.FileRef, trailerRecordCount.Value, detailRecordCount);
        }

        result.RecordCount = detailRecordCount;
        if (!result.IsValid)
        {
            result.ErrorMessage = result.Errors.FirstOrDefault()?.Message ?? "File validation failed";
        }

        Logger.LogInformation("Streaming validation for {FileRef}: {RecordCount} records, IsValid={IsValid}",
            context.FileRef, detailRecordCount, result.IsValid);

        return result;
    }

    /// <summary>
    /// Streaming parse pass - parses and yields records one at a time without accumulating in memory.
    /// Call this AFTER ValidateFileStreamingAsync passes validation.
    /// </summary>
    public virtual async IAsyncEnumerable<ParsedRecord> ParseRecordsStreamingAsync(
        Stream fileStream,
        ParseContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var detailRecordCount = 0;
        var lineNumber = 0;

        using var reader = new StreamReader(fileStream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            lineNumber++;

            var recordType = DetermineRecordType(line);

            switch (recordType)
            {
                case RecordCategory.Header:
                    // Parse header to populate context metadata
                    ParseHeader(line, context);
                    break;

                case RecordCategory.Trailer:
                    // Parse trailer (optional - for metadata)
                    // We don't yield trailer records
                    break;

                case RecordCategory.Detail:
                    detailRecordCount++;
                    ParsedRecord? record = null;

                    try
                    {
                        record = ParseDetailRecord(line, context, detailRecordCount);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Error parsing record at line {LineNumber} in file {FileRef}",
                            lineNumber, context.FileRef);

                        record = new ParsedRecord
                        {
                            RecordNumber = detailRecordCount,
                            RecordType = "ERROR",
                            IsValid = false,
                            ValidationError = ex.Message,
                            Fields = new Dictionary<string, object> { { "RawData", line } }
                        };
                    }

                    if (record != null)
                    {
                        yield return record;
                    }
                    break;

                case RecordCategory.Skip:
                    // Ignored record
                    break;
            }
        }

        Logger.LogDebug("Streaming parse for {FileRef}: yielded {Count} records", context.FileRef, detailRecordCount);
    }

    /// <summary>
    /// Add a file-level error to the parse result.
    /// </summary>
    private static void AddFileLevelError(ParseResult result, string errorCode, string message, int? lineNumber, string? rawData)
    {
        result.Errors.Add(new ParseError
        {
            ErrorCode = errorCode,
            Message = message,
            LineNumber = lineNumber,
            RawData = rawData,
            IsFileLevelError = true
        });
    }

    /// <summary>
    /// Determine the category of a record line.
    /// Override for specific file formats.
    /// </summary>
    protected virtual RecordCategory DetermineRecordType(string line)
    {
        // Default implementation: first char indicator
        if (string.IsNullOrWhiteSpace(line))
            return RecordCategory.Skip;

        var indicator = line.Length > 0 ? line[0] : ' ';

        return indicator switch
        {
            'H' or '0' => RecordCategory.Header,
            'T' or '9' => RecordCategory.Trailer,
            'D' or '1' or '2' => RecordCategory.Detail,
            '#' => RecordCategory.Skip, // Comment
            _ => RecordCategory.Detail  // Default to detail
        };
    }

    /// <summary>
    /// Parse file header record.
    /// Override to extract header-specific information.
    /// </summary>
    protected virtual void ParseHeader(string line, ParseContext context)
    {
        // Default: store header in metadata
        context.Metadata["Header"] = line;
    }

    /// <summary>
    /// Parse file trailer record.
    /// Override to validate and store trailer information.
    /// </summary>
    protected virtual void ParseTrailer(string line, ParseContext context, ParseResult result)
    {
        // Default: store trailer in metadata
        context.Metadata["Trailer"] = line;
    }

    /// <summary>
    /// Parse a detail record into a ParsedRecord.
    /// MUST be overridden by derived classes.
    /// </summary>
    protected abstract ParsedRecord? ParseDetailRecord(string line, ParseContext context, int recordNumber);

    /// <summary>
    /// Extract sequence number from header line.
    /// Override for specific formats.
    /// </summary>
    protected virtual int? ExtractSequenceNumber(string headerLine)
    {
        // Default: no sequence number
        return null;
    }

    /// <summary>
    /// Extract record count from trailer line.
    /// Override for specific formats.
    /// </summary>
    protected virtual int? ExtractTrailerRecordCount(string trailerLine)
    {
        // Default: no record count
        return null;
    }

    /// <summary>
    /// Split a delimited line into fields.
    /// Handles quoted fields.
    /// </summary>
    protected static string[] SplitDelimited(string line, char delimiter = ',')
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == delimiter && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields.ToArray();
    }

    /// <summary>
    /// Extract fixed-width field from line.
    /// </summary>
    protected static string ExtractField(string line, int start, int length)
    {
        if (start >= line.Length)
            return string.Empty;

        var actualLength = Math.Min(length, line.Length - start);
        return line.Substring(start, actualLength).Trim();
    }

    /// <summary>
    /// Parse a date string with multiple format support.
    /// </summary>
    protected static DateTime? ParseDate(string? value, params string[] formats)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(value.Trim(), format,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var result))
            {
                return result;
            }
        }

        // Try generic parse as fallback
        if (DateTime.TryParse(value, out var genericResult))
            return genericResult;

        return null;
    }

    /// <summary>
    /// Parse a decimal value.
    /// </summary>
    protected static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (decimal.TryParse(value.Trim(), out var result))
            return result;

        return null;
    }

    /// <summary>
    /// Parse an integer value.
    /// </summary>
    protected static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (int.TryParse(value.Trim(), out var result))
            return result;

        return null;
    }

    /// <summary>
    /// Parse a long value.
    /// </summary>
    protected static long? ParseLong(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (long.TryParse(value.Trim(), out var result))
            return result;

        return null;
    }

    // ============================================
    // VALIDATION HELPER METHODS
    // ============================================

    /// <summary>
    /// Validates a field using the validation engine and returns the parsed value.
    /// If no validation engine is configured, returns the raw value as-is.
    /// </summary>
    /// <param name="rawValue">The raw string value from the file.</param>
    /// <param name="fieldName">The field name for rule lookup.</param>
    /// <param name="lineNumber">Line number for error reporting.</param>
    /// <param name="recordNumber">Record number for error reporting.</param>
    /// <param name="rawLine">Optional complete raw line for error context.</param>
    /// <returns>The validated/parsed value or null if validation failed.</returns>
    protected object? ValidateAndParseField(string? rawValue, string fieldName, int lineNumber, int recordNumber, string? rawLine = null)
    {
        if (ValidationEngine == null)
        {
            // No validation engine - return raw value
            return rawValue;
        }

        return ValidationEngine.ValidateField(rawValue, fieldName, lineNumber, recordNumber, rawLine);
    }

    /// <summary>
    /// Validates a field by index using the validation engine and returns the parsed value.
    /// </summary>
    protected object? ValidateAndParseFieldByIndex(string? rawValue, int fieldIndex, int lineNumber, int recordNumber, string? rawLine = null)
    {
        if (ValidationEngine == null)
        {
            return rawValue;
        }

        return ValidationEngine.ValidateFieldByIndex(rawValue, fieldIndex, lineNumber, recordNumber, rawLine);
    }

    /// <summary>
    /// Increments the validation engine's record counters.
    /// Call this after processing each detail record.
    /// </summary>
    protected void UpdateValidationCounts(bool isValid)
    {
        if (ValidationEngine == null)
            return;

        ValidationEngine.IncrementTotalRecords();
        if (isValid)
        {
            ValidationEngine.IncrementValidRecords();
        }
    }

    /// <summary>
    /// Adds a file-level validation error.
    /// </summary>
    protected void AddFileLevelValidationError(string errorCode, string message, string userMessage, string? suggestion = null)
    {
        ValidationEngine?.AddFileLevelError(errorCode, message, userMessage, suggestion);
    }
}

/// <summary>
/// Record category for parsing.
/// </summary>
public enum RecordCategory
{
    Header,
    Detail,
    Trailer,
    Skip
}
