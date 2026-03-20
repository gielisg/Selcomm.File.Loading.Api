using Microsoft.Extensions.Logging;
using FileLoading.Models;

namespace FileLoading.Parsers;

/// <summary>
/// CHG (Charge) file parser for Selcomm format.
/// Handles pipe-delimited charge files with H/D/T record structure.
/// </summary>
public class ChgFileParser : BaseFileParser
{
    public ChgFileParser(ILogger<ChgFileParser> logger) : base(logger) { }

    public override string FileType => "CHG";
    public override string FileClassCode => "CHG";

    protected override ParsedRecord? ParseDetailRecord(string line, ParseContext context, int recordNumber)
    {
        var fields = SplitDelimited(line, '|');

        // Need at least: D|Ref|AcctCode|ServiceId|ChgCode|Desc|From|To|Amount|Tax
        if (fields.Length < 9)
        {
            return new ParsedRecord
            {
                RecordNumber = recordNumber,
                RecordType = "D",
                IsValid = false,
                ValidationError = $"Insufficient fields: expected at least 9, got {fields.Length}",
                Fields = new Dictionary<string, object> { { "RawData", line } }
            };
        }

        var record = new ParsedRecord
        {
            RecordNumber = recordNumber,
            RecordType = "D",
            IsValid = true
        };

        // Position 0 is "D" (record type indicator)
        // Field mapping based on Selcomm CHG format:
        // D|NtRef|AccountCode|PhoneNum|ChgCode|ChgNarr|StartDate|EndDate|CostAmount|CostGst

        record.Fields["NtRef"] = GetField(fields, 1);                       // Reference number
        record.Fields["AccountCode"] = GetField(fields, 2);                 // Account code (optional)
        record.Fields["PhoneNum"] = GetField(fields, 3);                    // Service ID / phone number
        record.Fields["ChgCode"] = GetField(fields, 4);                     // Charge code
        record.Fields["ChgNarr"] = GetField(fields, 5);                     // Charge description/narrative
        record.Fields["StartDate"] = ParseDate(GetField(fields, 6),
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd");                           // Period from
        record.Fields["EndDate"] = ParseDate(GetField(fields, 7),
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd");                           // Period to
        record.Fields["CostAmount"] = ParseDecimal(GetField(fields, 8));    // Charge amount (ex tax)
        record.Fields["CostGst"] = ParseDecimal(GetField(fields, 9));       // Tax amount

        record.Fields["RawData"] = line;

        // Validate: charge amount is required
        var chargeAmount = record.Fields["CostAmount"];
        if (chargeAmount == null)
        {
            record.IsValid = false;
            record.ValidationError = "Charge amount is required";
        }

        return record;
    }

    protected override void ParseHeader(string line, ParseContext context)
    {
        var fields = SplitDelimited(line, '|');
        // H|DateTime|SequenceNum|FileName
        // H|2026-01-01 17:49:46|02285982|60000943_00000305_20260101_7927.chg

        context.Metadata["Header"] = line;
        if (fields.Length > 1)
            context.Metadata["HeaderDateTime"] = ParseDate(fields[1], "yyyy-MM-dd HH:mm:ss");
        if (fields.Length > 2)
            context.Metadata["SequenceNumber"] = ParseInt(fields[2]);
        if (fields.Length > 3)
            context.Metadata["FileName"] = fields[3];
    }

    protected override int? ExtractSequenceNumber(string headerLine)
    {
        var fields = SplitDelimited(headerLine, '|');
        // Sequence number is in position 2
        if (fields.Length > 2)
        {
            return ParseInt(fields[2]);
        }
        return null;
    }

    protected override int? ExtractTrailerRecordCount(string trailerLine)
    {
        var fields = SplitDelimited(trailerLine, '|');
        // T|RecordCount|TotalValue
        // T|115| 2992.425200
        if (fields.Length > 1)
        {
            return ParseInt(fields[1]);
        }
        return null;
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
