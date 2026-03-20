using Microsoft.Extensions.Logging;
using FileLoading.Models;

namespace FileLoading.Parsers;

/// <summary>
/// SVC (Service) file parser.
/// Handles service provisioning and update file formats.
/// </summary>
public class SvcFileParser : BaseFileParser
{
    public SvcFileParser(ILogger<SvcFileParser> logger) : base(logger) { }

    public override string FileType => "SVC";
    public override string FileClassCode => "SVC";

    /// <summary>
    /// Expected field positions for service files.
    /// </summary>
    protected virtual int AccountCodePosition => 0;
    protected virtual int ServiceIdPosition => 1;
    protected virtual int ServiceTypePosition => 2;
    protected virtual int ActionCodePosition => 3;
    protected virtual int EffectiveDatePosition => 4;
    protected virtual int ExpiryDatePosition => 5;
    protected virtual int StatusPosition => 6;
    protected virtual int PlanCodePosition => 7;
    protected virtual int Attribute1Position => 8;
    protected virtual int Attribute2Position => 9;
    protected virtual int Attribute3Position => 10;

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

        record.Fields["AccountCode"] = GetField(fields, AccountCodePosition);
        record.Fields["ServiceId"] = GetField(fields, ServiceIdPosition);
        record.Fields["ServiceType"] = GetField(fields, ServiceTypePosition);
        record.Fields["ActionCode"] = GetField(fields, ActionCodePosition);
        record.Fields["EffectiveDate"] = ParseDate(GetField(fields, EffectiveDatePosition),
            "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy");
        record.Fields["ExpiryDate"] = ParseDate(GetField(fields, ExpiryDatePosition),
            "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy");
        record.Fields["Status"] = GetField(fields, StatusPosition);
        record.Fields["PlanCode"] = GetField(fields, PlanCodePosition);
        record.Fields["Attribute1"] = GetField(fields, Attribute1Position);
        record.Fields["Attribute2"] = GetField(fields, Attribute2Position);
        record.Fields["Attribute3"] = GetField(fields, Attribute3Position);
        record.Fields["RawData"] = line;

        // Validate required fields
        var serviceId = record.Fields["ServiceId"]?.ToString();
        var actionCode = record.Fields["ActionCode"]?.ToString();

        if (string.IsNullOrWhiteSpace(serviceId))
        {
            record.IsValid = false;
            record.ValidationError = "Service ID is required";
        }
        else if (string.IsNullOrWhiteSpace(actionCode))
        {
            record.IsValid = false;
            record.ValidationError = "Action code is required";
        }
        else if (!IsValidActionCode(actionCode))
        {
            record.IsValid = false;
            record.ValidationError = $"Invalid action code: {actionCode}. Expected A, M, or D";
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

    private static bool IsValidActionCode(string? actionCode)
    {
        if (string.IsNullOrWhiteSpace(actionCode))
            return false;

        return actionCode.ToUpperInvariant() switch
        {
            "A" or "ADD" => true,      // Add
            "M" or "MOD" or "MODIFY" => true,  // Modify
            "D" or "DEL" or "DELETE" => true,  // Delete
            "U" or "UPD" or "UPDATE" => true,  // Update
            _ => false
        };
    }

    private static string GetField(string[] fields, int position)
    {
        return position < fields.Length ? fields[position] : string.Empty;
    }
}
