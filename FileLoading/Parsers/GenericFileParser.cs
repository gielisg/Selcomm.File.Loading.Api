using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using FileLoading.Interfaces;
using FileLoading.Models;
using FileLoading.Repositories;

namespace FileLoading.Parsers;

/// <summary>
/// Generic configurable file parser driven by database configuration.
/// Supports CSV, delimited text, and Excel files with configurable
/// row identification and column mapping.
/// </summary>
public class GenericFileParser : BaseFileParser
{
    private readonly IFileLoaderRepository _repository;
    private GenericFileFormatConfig? _config;

    public GenericFileParser(ILogger<GenericFileParser> logger, IFileLoaderRepository repository)
        : base(logger)
    {
        _repository = repository;
    }

    /// <summary>Sentinel — never matches directly in parser lookup.</summary>
    public override string FileType => "GENERIC";

    /// <summary>File class code for generic files.</summary>
    public override string FileClassCode => "GEN";

    /// <summary>The loaded configuration (available after InitializeForFileTypeAsync).</summary>
    public GenericFileFormatConfig? Config => _config;

    /// <summary>
    /// Initialize the parser for a specific file type by loading config from the database.
    /// Must be called before parsing.
    /// </summary>
    public async Task InitializeForFileTypeAsync(string fileTypeCode)
    {
        _config = await _repository.GetGenericFileFormatConfigAsync(fileTypeCode);
        if (_config == null)
        {
            throw new InvalidOperationException($"No generic file format configuration found for file type: {fileTypeCode}");
        }

        Logger.LogInformation("Generic parser initialized for file type {FileType}: format={Format}, delimiter={Delimiter}, headerRow={HasHeader}, rowIdMode={RowIdMode}, mappings={MappingCount}",
            fileTypeCode, _config.FileFormat, _config.Delimiter, _config.HasHeaderRow, _config.RowIdMode, _config.ColumnMappings.Count);
    }

    /// <summary>
    /// Create appropriate row reader based on file format config.
    /// </summary>
    private IFileRowReader CreateRowReader(Stream stream)
    {
        if (_config == null) throw new InvalidOperationException("Parser not initialized. Call InitializeForFileTypeAsync first.");

        if (_config.FileFormat == FileFormatType.XLSX)
        {
            return new ExcelRowReader(stream, _config.SheetName, _config.SheetIndex);
        }

        return new DelimitedTextRowReader(stream, _config.GetDelimiterChar());
    }

    // ============================================
    // Streaming Validation Override
    // ============================================

    public override async Task<StreamingValidationResult> ValidateFileStreamingAsync(
        Stream fileStream,
        ParseContext context,
        CancellationToken cancellationToken = default)
    {
        if (_config == null) throw new InvalidOperationException("Parser not initialized.");

        var result = new StreamingValidationResult { IsValid = true };
        var detailRecordCount = 0;
        var rowNumber = 0;
        var totalRows = new List<string[]>(); // Buffer all rows for skip_rows_bottom
        var rawLines = new List<string?>();

        using var reader = CreateRowReader(fileStream);

        // Read all rows to support skip_rows_bottom
        while (!reader.IsComplete && !cancellationToken.IsCancellationRequested)
        {
            var fields = await reader.ReadNextRowAsync();
            if (fields == null) break;

            totalRows.Add(fields);
            rawLines.Add(reader.GetRawLine());
        }

        // Determine effective range after skipping top/bottom
        var startIndex = _config.SkipRowsTop;
        var endIndex = totalRows.Count - _config.SkipRowsBottom;

        for (var i = startIndex; i < endIndex; i++)
        {
            var fields = totalRows[i];
            var rawLine = rawLines[i];
            rowNumber = i + 1;

            // Skip blank rows
            if (fields.All(string.IsNullOrWhiteSpace))
                continue;

            var recordType = DetermineRecordTypeFromFields(fields, rawLine, i - startIndex);

            switch (recordType)
            {
                case RecordCategory.Header:
                    // Header row in generic files is informational only
                    break;
                case RecordCategory.Trailer:
                    // Validate trailer total if configured
                    ValidateTrailerTotal(fields, result, detailRecordCount);
                    break;
                case RecordCategory.Detail:
                    detailRecordCount++;
                    break;
                case RecordCategory.Skip:
                    break;
            }
        }

        if (totalRows.Count == 0)
        {
            result.IsValid = false;
            result.Errors.Add(new ParseError
            {
                ErrorCode = FileErrorCodes.FileEmpty,
                Message = "File is empty",
                IsFileLevelError = true
            });
        }

        result.RecordCount = detailRecordCount;
        if (!result.IsValid)
        {
            result.ErrorMessage = result.Errors.FirstOrDefault()?.Message ?? "File validation failed";
        }

        Logger.LogInformation("Generic streaming validation for {FileRef}: {RecordCount} detail records, IsValid={IsValid}",
            context.FileRef, detailRecordCount, result.IsValid);

        return result;
    }

    /// <summary>
    /// Validate trailer total against accumulated running total or record count.
    /// </summary>
    private void ValidateTrailerTotal(string[] fields, StreamingValidationResult result, int detailRecordCount)
    {
        if (_config?.TotalColumnIndex == null || string.IsNullOrEmpty(_config.TotalType))
            return;

        var colIndex = _config.TotalColumnIndex.Value;
        if (colIndex >= fields.Length)
            return;

        var totalStr = fields[colIndex].Trim();
        if (string.IsNullOrEmpty(totalStr))
            return;

        if (_config.TotalType == "COUNT")
        {
            if (int.TryParse(totalStr, out var trailerCount) && trailerCount != detailRecordCount)
            {
                result.IsValid = false;
                result.Errors.Add(new ParseError
                {
                    ErrorCode = FileErrorCodes.TrailerCountMismatch,
                    Message = $"Error: Trailer count {trailerCount} does not match number of records {detailRecordCount}",
                    IsFileLevelError = true
                });
            }
        }
        // SUM reconciliation is checked after insert pass (needs actual cost values)
    }

    // ============================================
    // Streaming Parse Override
    // ============================================

    public override async IAsyncEnumerable<ParsedRecord> ParseRecordsStreamingAsync(
        Stream fileStream,
        ParseContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_config == null) throw new InvalidOperationException("Parser not initialized.");

        var allRows = new List<string[]>();
        var allRawLines = new List<string?>();

        using var reader = CreateRowReader(fileStream);

        // Read all rows to support skip_rows_bottom
        while (!reader.IsComplete && !cancellationToken.IsCancellationRequested)
        {
            var fields = await reader.ReadNextRowAsync();
            if (fields == null) break;

            allRows.Add(fields);
            allRawLines.Add(reader.GetRawLine());
        }

        var startIndex = _config.SkipRowsTop;
        var endIndex = allRows.Count - _config.SkipRowsBottom;
        var detailRecordCount = 0;

        for (var i = startIndex; i < endIndex; i++)
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            var fields = allRows[i];
            var rawLine = allRawLines[i];

            // Skip blank rows
            if (fields.All(string.IsNullOrWhiteSpace))
                continue;

            var recordType = DetermineRecordTypeFromFields(fields, rawLine, i - startIndex);

            if (recordType == RecordCategory.Detail)
            {
                detailRecordCount++;
                ParsedRecord? record;

                try
                {
                    record = ParseDetailFromFields(fields, rawLine, context, detailRecordCount);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Error parsing generic record at row {RowNumber}", i + 1);
                    record = new ParsedRecord
                    {
                        RecordNumber = detailRecordCount,
                        RecordType = "ERROR",
                        IsValid = false,
                        ValidationError = ex.Message,
                        Fields = new Dictionary<string, object> { { "RawData", rawLine ?? string.Empty } }
                    };
                }

                if (record != null)
                    yield return record;
            }
        }

        Logger.LogDebug("Generic streaming parse for {FileRef}: yielded {Count} records", context.FileRef, detailRecordCount);
    }

    // ============================================
    // Row Identification
    // ============================================

    /// <summary>
    /// Determine record type based on config mode (Position, Indicator, Pattern).
    /// </summary>
    private RecordCategory DetermineRecordTypeFromFields(string[] fields, string? rawLine, int dataRowIndex)
    {
        if (_config == null) return RecordCategory.Detail;

        return _config.RowIdMode switch
        {
            RowIdMode.Position => DetermineByPosition(fields, rawLine, dataRowIndex),
            RowIdMode.Indicator => DetermineByIndicator(fields),
            RowIdMode.Pattern => DetermineByPattern(rawLine ?? string.Empty),
            _ => RecordCategory.Detail
        };
    }

    private RecordCategory DetermineByPosition(string[] fields, string? rawLine, int dataRowIndex)
    {
        // First non-skipped row is header if HasHeaderRow is set
        if (_config!.HasHeaderRow && dataRowIndex == 0)
            return RecordCategory.Header;

        // Check for trailer indicator match (if configured)
        if (!string.IsNullOrEmpty(_config.TrailerIndicator) && rawLine != null)
        {
            if (rawLine.Contains(_config.TrailerIndicator, StringComparison.OrdinalIgnoreCase))
                return RecordCategory.Trailer;
        }

        return RecordCategory.Detail;
    }

    private RecordCategory DetermineByIndicator(string[] fields)
    {
        if (_config == null || _config.RowIdColumn >= fields.Length)
            return RecordCategory.Detail;

        var value = fields[_config.RowIdColumn].Trim();

        if (!string.IsNullOrEmpty(_config.HeaderIndicator) &&
            string.Equals(value, _config.HeaderIndicator, StringComparison.OrdinalIgnoreCase))
            return RecordCategory.Header;

        if (!string.IsNullOrEmpty(_config.TrailerIndicator) &&
            string.Equals(value, _config.TrailerIndicator, StringComparison.OrdinalIgnoreCase))
            return RecordCategory.Trailer;

        if (!string.IsNullOrEmpty(_config.SkipIndicator) &&
            string.Equals(value, _config.SkipIndicator, StringComparison.OrdinalIgnoreCase))
            return RecordCategory.Skip;

        if (!string.IsNullOrEmpty(_config.DetailIndicator))
        {
            return string.Equals(value, _config.DetailIndicator, StringComparison.OrdinalIgnoreCase)
                ? RecordCategory.Detail
                : RecordCategory.Detail; // Default to detail even if no match
        }

        return RecordCategory.Detail;
    }

    private RecordCategory DetermineByPattern(string rawLine)
    {
        if (_config == null) return RecordCategory.Detail;

        var headerPattern = _config.GetHeaderPattern();
        if (headerPattern?.IsMatch(rawLine) == true)
            return RecordCategory.Header;

        var trailerPattern = _config.GetTrailerPattern();
        if (trailerPattern?.IsMatch(rawLine) == true)
            return RecordCategory.Trailer;

        var skipPattern = _config.GetSkipPattern();
        if (skipPattern?.IsMatch(rawLine) == true)
            return RecordCategory.Skip;

        return RecordCategory.Detail;
    }

    // ============================================
    // Detail Record Parsing
    // ============================================

    /// <summary>
    /// Parse a detail record from field array using column mappings.
    /// </summary>
    private ParsedRecord? ParseDetailFromFields(string[] fields, string? rawLine, ParseContext context, int recordNumber)
    {
        if (_config == null) return null;

        var record = new ParsedRecord
        {
            RecordNumber = recordNumber,
            RecordType = "D",
            IsValid = true
        };

        record.Fields["RawData"] = rawLine ?? string.Join(_config.Delimiter ?? ",", fields);

        foreach (var mapping in _config.ColumnMappings)
        {
            var rawValue = mapping.ColumnIndex < fields.Length
                ? fields[mapping.ColumnIndex]
                : null;

            // Apply default value if source is empty
            if (string.IsNullOrWhiteSpace(rawValue) && !string.IsNullOrEmpty(mapping.DefaultValue))
            {
                rawValue = mapping.DefaultValue;
            }

            // Check required
            if (mapping.IsRequired && string.IsNullOrWhiteSpace(rawValue))
            {
                record.IsValid = false;
                record.ValidationError = $"Required field '{mapping.TargetField}' (column {mapping.ColumnIndex}) is missing";
                continue;
            }

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                record.Fields[mapping.TargetField] = null!;
                continue;
            }

            // Validate regex pattern
            if (!string.IsNullOrEmpty(mapping.RegexPattern))
            {
                var regex = mapping.GetCompiledRegex();
                if (regex != null && !regex.IsMatch(rawValue))
                {
                    record.IsValid = false;
                    record.ValidationError = $"Field '{mapping.TargetField}' value '{rawValue}' does not match pattern '{mapping.RegexPattern}'";
                    continue;
                }
            }

            // Validate max length
            if (mapping.MaxLength.HasValue && rawValue.Length > mapping.MaxLength.Value)
            {
                rawValue = rawValue[..mapping.MaxLength.Value];
            }

            // Parse by data type
            object? parsedValue = ParseFieldValue(rawValue, mapping, record);
            if (parsedValue != null)
            {
                record.Fields[mapping.TargetField] = parsedValue;
            }
        }

        return record;
    }

    /// <summary>
    /// Parse a field value based on the mapping's data type.
    /// </summary>
    private object? ParseFieldValue(string rawValue, GenericColumnMapping mapping, ParsedRecord record)
    {
        switch (mapping.DataType.ToUpperInvariant())
        {
            case "STRING":
                return rawValue;

            case "INTEGER":
                if (int.TryParse(rawValue, out var intVal))
                    return intVal;
                record.IsValid = false;
                record.ValidationError = $"Field '{mapping.TargetField}' value '{rawValue}' is not a valid integer";
                return null;

            case "DECIMAL":
                if (decimal.TryParse(rawValue, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var decVal))
                    return decVal;
                record.IsValid = false;
                record.ValidationError = $"Field '{mapping.TargetField}' value '{rawValue}' is not a valid decimal";
                return null;

            case "DATETIME":
                var dateFormat = mapping.DateFormat ?? _config?.DateFormat;
                DateTime? dateVal;
                if (!string.IsNullOrEmpty(dateFormat))
                {
                    dateVal = ParseDate(rawValue, dateFormat);
                }
                else
                {
                    dateVal = ParseDate(rawValue, "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "dd-MM-yyyy");
                }

                if (dateVal.HasValue)
                    return dateVal.Value;

                record.IsValid = false;
                record.ValidationError = $"Field '{mapping.TargetField}' value '{rawValue}' is not a valid date";
                return null;

            case "BOOLEAN":
                var boolStr = rawValue.Trim().ToUpperInvariant();
                return boolStr is "Y" or "YES" or "TRUE" or "1";

            default:
                return rawValue;
        }
    }

    // ============================================
    // Base class overrides (used by legacy path)
    // ============================================

    protected override RecordCategory DetermineRecordType(string line)
    {
        // For text-based files using legacy path
        if (_config == null) return RecordCategory.Detail;

        var fields = SplitDelimited(line, _config.GetDelimiterChar());
        return DetermineRecordTypeFromFields(fields, line, -1);
    }

    protected override ParsedRecord? ParseDetailRecord(string line, ParseContext context, int recordNumber)
    {
        if (_config == null) return null;

        var fields = SplitDelimited(line, _config.GetDelimiterChar());
        return ParseDetailFromFields(fields, line, context, recordNumber);
    }
}
