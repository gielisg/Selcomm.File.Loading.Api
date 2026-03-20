using Microsoft.Extensions.Logging;
using FileLoading.Models;

namespace FileLoading.Parsers;

/// <summary>
/// ORD (Order) file parser.
/// Handles order and provisioning file formats.
/// </summary>
public class OrdFileParser : BaseFileParser
{
    public OrdFileParser(ILogger<OrdFileParser> logger) : base(logger) { }

    public override string FileType => "ORD";
    public override string FileClassCode => "ORD";

    /// <summary>
    /// Expected field positions for order files.
    /// </summary>
    protected virtual int OrderNumberPosition => 0;
    protected virtual int AccountCodePosition => 1;
    protected virtual int ServiceIdPosition => 2;
    protected virtual int OrderTypePosition => 3;
    protected virtual int OrderStatusPosition => 4;
    protected virtual int OrderDatePosition => 5;
    protected virtual int RequiredDatePosition => 6;
    protected virtual int CompletedDatePosition => 7;
    protected virtual int ProductCodePosition => 8;
    protected virtual int QuantityPosition => 9;
    protected virtual int NotesPosition => 10;

    protected override ParsedRecord? ParseDetailRecord(string line, ParseContext context, int recordNumber)
    {
        var fields = SplitDelimited(line, ',');

        if (fields.Length < 4)
        {
            return new ParsedRecord
            {
                RecordNumber = recordNumber,
                RecordType = "D",
                IsValid = false,
                ValidationError = $"Insufficient fields: expected at least 4, got {fields.Length}",
                Fields = new Dictionary<string, object> { { "RawData", line } }
            };
        }

        var record = new ParsedRecord
        {
            RecordNumber = recordNumber,
            RecordType = "D",
            IsValid = true
        };

        record.Fields["OrderNumber"] = GetField(fields, OrderNumberPosition);
        record.Fields["AccountCode"] = GetField(fields, AccountCodePosition);
        record.Fields["ServiceId"] = GetField(fields, ServiceIdPosition);
        record.Fields["OrderType"] = GetField(fields, OrderTypePosition);
        record.Fields["OrderStatus"] = GetField(fields, OrderStatusPosition);
        record.Fields["OrderDate"] = ParseDate(GetField(fields, OrderDatePosition),
            "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy");
        record.Fields["RequiredDate"] = ParseDate(GetField(fields, RequiredDatePosition),
            "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy");
        record.Fields["CompletedDate"] = ParseDate(GetField(fields, CompletedDatePosition),
            "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy");
        record.Fields["ProductCode"] = GetField(fields, ProductCodePosition);
        record.Fields["Quantity"] = ParseInt(GetField(fields, QuantityPosition));
        record.Fields["Notes"] = GetField(fields, NotesPosition);
        record.Fields["RawData"] = line;

        // Validate required fields
        var orderNumber = record.Fields["OrderNumber"]?.ToString();

        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            record.IsValid = false;
            record.ValidationError = "Order number is required";
        }

        return record;
    }

    protected override RecordCategory DetermineRecordType(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return RecordCategory.Skip;

        var indicator = line.Length > 0 ? line[0] : ' ';

        return indicator switch
        {
            'H' => RecordCategory.Header,
            'T' => RecordCategory.Trailer,
            '#' => RecordCategory.Skip,
            _ => RecordCategory.Detail
        };
    }

    private static string GetField(string[] fields, int position)
    {
        return position < fields.Length ? fields[position] : string.Empty;
    }
}
