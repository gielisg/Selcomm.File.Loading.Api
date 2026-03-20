using System.Text.RegularExpressions;

namespace FileLoading.Models;

/// <summary>
/// File format types supported by the generic parser.
/// </summary>
public enum FileFormatType
{
    CSV,
    XLSX,
    Delimited
}

/// <summary>
/// Row identification modes for the generic parser.
/// </summary>
public enum RowIdMode
{
    /// <summary>Row type determined by position (skip N top/bottom, header row flag).</summary>
    Position,
    /// <summary>Row type determined by a column value matching indicator strings.</summary>
    Indicator,
    /// <summary>Row type determined by regex pattern matching on the full raw line.</summary>
    Pattern
}

/// <summary>
/// Standard target field names for generic column mapping.
/// </summary>
public enum GenericTargetField
{
    AccountCode,
    ServiceId,
    ChargeType,
    CostAmount,
    TaxAmount,
    Quantity,
    UOM,
    FromDate,
    ToDate,
    Description,
    ExternalRef,
    Generic01, Generic02, Generic03, Generic04, Generic05,
    Generic06, Generic07, Generic08, Generic09, Generic10,
    Generic11, Generic12, Generic13, Generic14, Generic15,
    Generic16, Generic17, Generic18, Generic19, Generic20
}

/// <summary>
/// Configuration for a generic file format - mirrors ntfl_file_format_config table.
/// </summary>
public class GenericFileFormatConfig
{
    public string FileTypeCode { get; set; } = string.Empty;
    public FileFormatType FileFormat { get; set; } = FileFormatType.CSV;
    public string? Delimiter { get; set; } = ",";
    public bool HasHeaderRow { get; set; }
    public int SkipRowsTop { get; set; }
    public int SkipRowsBottom { get; set; }
    public RowIdMode RowIdMode { get; set; } = RowIdMode.Position;
    public int RowIdColumn { get; set; }
    public string? HeaderIndicator { get; set; }
    public string? TrailerIndicator { get; set; }
    public string? DetailIndicator { get; set; }
    public string? SkipIndicator { get; set; }
    public int? TotalColumnIndex { get; set; }
    public string? TotalType { get; set; }
    public string? SheetName { get; set; }
    public int SheetIndex { get; set; }
    public string? DateFormat { get; set; }
    public string? CustomSpName { get; set; }
    public bool Active { get; set; } = true;

    /// <summary>Column mappings for this file type.</summary>
    public List<GenericColumnMapping> ColumnMappings { get; set; } = new();

    /// <summary>Get the delimiter character (defaults to comma).</summary>
    public char GetDelimiterChar()
    {
        if (string.IsNullOrEmpty(Delimiter)) return ',';
        return Delimiter.ToLowerInvariant() switch
        {
            "tab" or "\t" => '\t',
            "pipe" or "|" => '|',
            "semicolon" or ";" => ';',
            _ => Delimiter[0]
        };
    }

    // Pre-compiled regex patterns for Pattern mode (lazily initialized)
    private Regex? _headerPattern;
    private Regex? _trailerPattern;
    private Regex? _detailPattern;
    private Regex? _skipPattern;

    public Regex? GetHeaderPattern() => GetOrCompile(ref _headerPattern, HeaderIndicator);
    public Regex? GetTrailerPattern() => GetOrCompile(ref _trailerPattern, TrailerIndicator);
    public Regex? GetDetailPattern() => GetOrCompile(ref _detailPattern, DetailIndicator);
    public Regex? GetSkipPattern() => GetOrCompile(ref _skipPattern, SkipIndicator);

    private static Regex? GetOrCompile(ref Regex? field, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return null;
        return field ??= new Regex(pattern, RegexOptions.Compiled);
    }
}

/// <summary>
/// Column mapping configuration - mirrors ntfl_column_mapping table.
/// </summary>
public class GenericColumnMapping
{
    public string FileTypeCode { get; set; } = string.Empty;
    public int ColumnIndex { get; set; }
    public string? SourceColumnName { get; set; }
    public string TargetField { get; set; } = string.Empty;
    public string DataType { get; set; } = "String";
    public string? DateFormat { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? RegexPattern { get; set; }
    public int? MaxLength { get; set; }

    // Pre-compiled regex (lazily initialized)
    private Regex? _compiledRegex;

    public Regex? GetCompiledRegex()
    {
        if (string.IsNullOrEmpty(RegexPattern)) return null;
        return _compiledRegex ??= new Regex(RegexPattern, RegexOptions.Compiled);
    }
}

/// <summary>
/// Generic detail record - maps to ntfl_generic_detail table.
/// Used for generic/configurable file types.
/// </summary>
public class GenericDetailRecord : FileDetailRecord
{
    public string? AccountCode { get; set; }
    public string? ServiceId { get; set; }
    public string? ChargeType { get; set; }
    public decimal? CostAmount { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? Quantity { get; set; }
    public string? UOM { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Description { get; set; }
    public string? ExternalRef { get; set; }

    // Generic overflow columns
    public string? Generic01 { get; set; }
    public string? Generic02 { get; set; }
    public string? Generic03 { get; set; }
    public string? Generic04 { get; set; }
    public string? Generic05 { get; set; }
    public string? Generic06 { get; set; }
    public string? Generic07 { get; set; }
    public string? Generic08 { get; set; }
    public string? Generic09 { get; set; }
    public string? Generic10 { get; set; }
    public string? Generic11 { get; set; }
    public string? Generic12 { get; set; }
    public string? Generic13 { get; set; }
    public string? Generic14 { get; set; }
    public string? Generic15 { get; set; }
    public string? Generic16 { get; set; }
    public string? Generic17 { get; set; }
    public string? Generic18 { get; set; }
    public string? Generic19 { get; set; }
    public string? Generic20 { get; set; }

    /// <summary>Original row for debugging.</summary>
    public string? RawData { get; set; }

    /// <summary>Status ID (default 1 = initial).</summary>
    public int StatusId { get; set; } = 1;

    /// <summary>Set a generic field by number (1-20).</summary>
    public void SetGenericField(int number, string? value)
    {
        switch (number)
        {
            case 1: Generic01 = value; break;
            case 2: Generic02 = value; break;
            case 3: Generic03 = value; break;
            case 4: Generic04 = value; break;
            case 5: Generic05 = value; break;
            case 6: Generic06 = value; break;
            case 7: Generic07 = value; break;
            case 8: Generic08 = value; break;
            case 9: Generic09 = value; break;
            case 10: Generic10 = value; break;
            case 11: Generic11 = value; break;
            case 12: Generic12 = value; break;
            case 13: Generic13 = value; break;
            case 14: Generic14 = value; break;
            case 15: Generic15 = value; break;
            case 16: Generic16 = value; break;
            case 17: Generic17 = value; break;
            case 18: Generic18 = value; break;
            case 19: Generic19 = value; break;
            case 20: Generic20 = value; break;
        }
    }

    /// <summary>Get a generic field by number (1-20).</summary>
    public string? GetGenericField(int number) => number switch
    {
        1 => Generic01, 2 => Generic02, 3 => Generic03, 4 => Generic04, 5 => Generic05,
        6 => Generic06, 7 => Generic07, 8 => Generic08, 9 => Generic09, 10 => Generic10,
        11 => Generic11, 12 => Generic12, 13 => Generic13, 14 => Generic14, 15 => Generic15,
        16 => Generic16, 17 => Generic17, 18 => Generic18, 19 => Generic19, 20 => Generic20,
        _ => null
    };
}

/// <summary>
/// Request DTO for creating or updating a generic parser configuration.
/// </summary>
public class GenericParserConfigRequest
{
    public string FileTypeCode { get; set; } = string.Empty;
    public string FileFormat { get; set; } = "CSV";
    public string? Delimiter { get; set; }
    public bool HasHeaderRow { get; set; }
    public int SkipRowsTop { get; set; }
    public int SkipRowsBottom { get; set; }
    public string RowIdMode { get; set; } = "POSITION";
    public int RowIdColumn { get; set; }
    public string? HeaderIndicator { get; set; }
    public string? TrailerIndicator { get; set; }
    public string? DetailIndicator { get; set; }
    public string? SkipIndicator { get; set; }
    public int? TotalColumnIndex { get; set; }
    public string? TotalType { get; set; }
    public string? SheetName { get; set; }
    public int SheetIndex { get; set; }
    public string? DateFormat { get; set; }
    public string? CustomSpName { get; set; }
    public bool Active { get; set; } = true;
    public List<GenericColumnMappingRequest> ColumnMappings { get; set; } = new();
}

/// <summary>
/// Request DTO for a column mapping within a parser configuration.
/// </summary>
public class GenericColumnMappingRequest
{
    public int ColumnIndex { get; set; }
    public string? SourceColumnName { get; set; }
    public string TargetField { get; set; } = string.Empty;
    public string DataType { get; set; } = "String";
    public string? DateFormat { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? RegexPattern { get; set; }
    public int? MaxLength { get; set; }
}
