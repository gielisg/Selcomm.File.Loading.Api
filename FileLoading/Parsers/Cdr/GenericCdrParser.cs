using Microsoft.Extensions.Logging;
using FileLoading.Models;

namespace FileLoading.Parsers.Cdr;

/// <summary>
/// Generic CDR (Call Detail Record) parser for Selcomm format.
/// Handles pipe-delimited CDR files with H/D/T record structure.
/// </summary>
public class GenericCdrParser : BaseFileParser
{
    public GenericCdrParser(ILogger<GenericCdrParser> logger) : base(logger) { }

    public override string FileType => "CDR";
    public override string FileClassCode => "CDR";

    protected override ParsedRecord? ParseDetailRecord(string line, ParseContext context, int recordNumber)
    {
        var fields = SplitDelimited(line, '|');

        // Need at least: D|RecNum|Type|SpCnRef|StartDt|EndDt|Volume|Unit|Phone|Called
        if (fields.Length < 10)
        {
            return new ParsedRecord
            {
                RecordNumber = recordNumber,
                RecordType = "D",
                IsValid = false,
                ValidationError = $"Insufficient fields: expected at least 10, got {fields.Length}",
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
        // Field mapping based on Selcomm CDR format:
        // D|RecNum|Type|SpCnRef|StartDt|EndDt|Volume|Unit|Phone|Called|Extra|RateType|PlanCode|SvcCode|Cost1|Cost2|Cost3|Cost4|...|Flag|Val

        record.Fields["NtFileRecNum"] = ParseInt(GetField(fields, 1));      // Record number
        var callType = GetField(fields, 2);                                  // U=Usage, C=Call
        record.Fields["CallType"] = callType;
        record.Fields["SpCnRef"] = GetField(fields, 3);                     // SP connection reference
        record.Fields["ClStartDt"] = ParseDate(GetField(fields, 4),
            "yyyy-MM-dd HH:mm:ss");                                          // Start datetime
        record.Fields["ClStartDtSrvr"] = ParseDate(GetField(fields, 5),
            "yyyy-MM-dd HH:mm:ss");                                          // Server datetime

        var volumeStr = GetField(fields, 6);
        var unitStr = GetField(fields, 7);

        // Handle unit based on call type
        if (callType == "U")
        {
            // Usage record - volume is in megabytes
            record.Fields["UnitQuantity"] = ParseDecimal(volumeStr);
            record.Fields["Unit"] = "M";
        }
        else if (callType == "C")
        {
            // Call record - duration is in seconds
            var durationSeconds = ParseDecimal(volumeStr);
            record.Fields["UnitQuantity"] = durationSeconds;
            record.Fields["Unit"] = "S";

            // Also store as TimeSpan for convenience
            if (durationSeconds.HasValue)
            {
                record.Fields["Duration"] = TimeSpan.FromSeconds((double)durationSeconds.Value);
            }
        }
        else
        {
            record.Fields["UnitQuantity"] = ParseDecimal(volumeStr);
            record.Fields["Unit"] = unitStr;
        }

        record.Fields["NtClSvin"] = GetField(fields, 8);                    // Phone number (service)
        record.Fields["NumCalled"] = GetField(fields, 9);                   // Called number / "data usage"
        record.Fields["BpartyDestn"] = GetField(fields, 10);                // B-party destination
        record.Fields["TimebandCode"] = GetField(fields, 11);               // Rate type (FLAT, etc.)
        record.Fields["SpPlanRef"] = GetField(fields, 12);                  // Plan code
        record.Fields["TarClassCode"] = GetField(fields, 13);               // Service/tariff class code

        // Cost fields (positions 14-17)
        record.Fields["NtCost"] = ParseDecimal(GetField(fields, 14));
        record.Fields["NtCostEx"] = ParseDecimal(GetField(fields, 15));
        record.Fields["NtCostTax"] = ParseDecimal(GetField(fields, 16));
        record.Fields["RtlNonDiscEx"] = ParseDecimal(GetField(fields, 17));

        record.Fields["RawData"] = line;

        return record;
    }

    protected override void ParseHeader(string line, ParseContext context)
    {
        var fields = SplitDelimited(line, '|');
        // H|DateTime|SequenceNum|FileName|Count
        // H|2026-01-19 05:24:12|02297525|60000943_00000304_20260119_8259.cdr|4

        context.Metadata["Header"] = line;
        if (fields.Length > 1)
            context.Metadata["HeaderDateTime"] = ParseDate(fields[1], "yyyy-MM-dd HH:mm:ss");
        if (fields.Length > 2)
            context.Metadata["SequenceNumber"] = ParseInt(fields[2]);
        if (fields.Length > 3)
            context.Metadata["FileName"] = fields[3];
        if (fields.Length > 4)
            context.Metadata["HeaderRecordCount"] = ParseInt(fields[4]);
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
        // T|10| 241.01
        if (fields.Length > 1)
        {
            return ParseInt(fields[1]);
        }
        return null;
    }

    private static string GetField(string[] fields, int position)
    {
        return position < fields.Length ? fields[position] : string.Empty;
    }
}

/// <summary>
/// Telstra GSM CDR parser.
/// Extends generic CDR with Telstra-specific field positions.
/// </summary>
public class TelstraGsmCdrParser : GenericCdrParser
{
    public TelstraGsmCdrParser(ILogger<TelstraGsmCdrParser> logger) : base(logger) { }

    public override string FileType => "TEL_GSM";
}

/// <summary>
/// Telstra CDMA CDR parser.
/// </summary>
public class TelstraCdmaCdrParser : GenericCdrParser
{
    public TelstraCdmaCdrParser(ILogger<TelstraCdmaCdrParser> logger) : base(logger) { }

    public override string FileType => "TEL_CDMA";
}

/// <summary>
/// Optus CDR parser.
/// </summary>
public class OptusCdrParser : GenericCdrParser
{
    public OptusCdrParser(ILogger<OptusCdrParser> logger) : base(logger) { }

    public override string FileType => "OPTUS";
}

/// <summary>
/// AAPT CDR parser.
/// </summary>
public class AaptCdrParser : GenericCdrParser
{
    public AaptCdrParser(ILogger<AaptCdrParser> logger) : base(logger) { }

    public override string FileType => "AAPT";
}

/// <summary>
/// Vodafone CDR parser.
/// </summary>
public class VodafoneCdrParser : GenericCdrParser
{
    public VodafoneCdrParser(ILogger<VodafoneCdrParser> logger) : base(logger) { }

    public override string FileType => "VODA";
}
