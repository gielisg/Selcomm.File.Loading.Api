using Microsoft.Extensions.Logging;
using FileLoading.Models;

namespace FileLoading.Parsers;

/// <summary>
/// EBL (E-Billing) file parser.
/// Handles electronic billing and invoice file formats.
/// </summary>
public class EblFileParser : BaseFileParser
{
    public EblFileParser(ILogger<EblFileParser> logger) : base(logger) { }

    public override string FileType => "EBL";
    public override string FileClassCode => "EBL";

    /// <summary>
    /// Expected field positions for e-billing files.
    /// </summary>
    protected virtual int AccountCodePosition => 0;
    protected virtual int InvoiceNumberPosition => 1;
    protected virtual int InvoiceDatePosition => 2;
    protected virtual int DueDatePosition => 3;
    protected virtual int InvoiceAmountPosition => 4;
    protected virtual int TaxAmountPosition => 5;
    protected virtual int TotalAmountPosition => 6;
    protected virtual int CurrencyCodePosition => 7;
    protected virtual int PeriodFromPosition => 8;
    protected virtual int PeriodToPosition => 9;
    protected virtual int DocumentPathPosition => 10;

    protected override ParsedRecord? ParseDetailRecord(string line, ParseContext context, int recordNumber)
    {
        var fields = SplitDelimited(line, ',');

        if (fields.Length < 5)
        {
            return new ParsedRecord
            {
                RecordNumber = recordNumber,
                RecordType = "D",
                IsValid = false,
                ValidationError = $"Insufficient fields: expected at least 5, got {fields.Length}",
                Fields = new Dictionary<string, object> { { "RawData", line } }
            };
        }

        var record = new ParsedRecord
        {
            RecordNumber = recordNumber,
            RecordType = "D",
            IsValid = true
        };

        record.Fields["AccountCode"] = GetField(fields, AccountCodePosition);
        record.Fields["InvoiceNumber"] = GetField(fields, InvoiceNumberPosition);
        record.Fields["InvoiceDate"] = ParseDate(GetField(fields, InvoiceDatePosition),
            "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy");
        record.Fields["DueDate"] = ParseDate(GetField(fields, DueDatePosition),
            "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy");
        record.Fields["InvoiceAmount"] = ParseDecimal(GetField(fields, InvoiceAmountPosition));
        record.Fields["TaxAmount"] = ParseDecimal(GetField(fields, TaxAmountPosition));
        record.Fields["TotalAmount"] = ParseDecimal(GetField(fields, TotalAmountPosition));
        record.Fields["CurrencyCode"] = GetField(fields, CurrencyCodePosition);
        record.Fields["PeriodFrom"] = ParseDate(GetField(fields, PeriodFromPosition),
            "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy");
        record.Fields["PeriodTo"] = ParseDate(GetField(fields, PeriodToPosition),
            "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy");
        record.Fields["DocumentPath"] = GetField(fields, DocumentPathPosition);
        record.Fields["RawData"] = line;

        // Validate required fields
        var accountCode = record.Fields["AccountCode"]?.ToString();
        var invoiceNumber = record.Fields["InvoiceNumber"]?.ToString();

        if (string.IsNullOrWhiteSpace(accountCode))
        {
            record.IsValid = false;
            record.ValidationError = "Account code is required";
        }
        else if (string.IsNullOrWhiteSpace(invoiceNumber))
        {
            record.IsValid = false;
            record.ValidationError = "Invoice number is required";
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
