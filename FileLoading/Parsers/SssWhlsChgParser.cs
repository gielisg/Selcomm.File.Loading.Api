using Microsoft.Extensions.Logging;
using FileLoading.Models;

namespace FileLoading.Parsers;

/// <summary>
/// SSSWHLSCHG file parser - tilde-delimited CHG with sub-type table (ssswhlschg).
/// Inserts into both ntfl_chgdtl (standard CHG) and ssswhlschg (sub-type extension).
/// Based on legacy 4GL ntfileload_chg.4gl.
/// </summary>
public class SssWhlsChgParser : BaseFileParser, ISubTypeRecordProvider
{
    private int _fileVersion;

    public SssWhlsChgParser(ILogger<SssWhlsChgParser> logger) : base(logger) { }

    public override string FileType => "SSSWHLSCHG";
    public override string FileClassCode => "CHG";

    public string SubTypeTableName => "ssswhlschg";

    protected override RecordCategory DetermineRecordType(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return RecordCategory.Skip;

        return line[0] switch
        {
            'H' => RecordCategory.Header,
            'T' => RecordCategory.Trailer,
            '#' => RecordCategory.Skip,
            _ => RecordCategory.Detail
        };
    }

    protected override void ParseHeader(string line, ParseContext context)
    {
        var fields = SplitDelimited(line, '~');
        // H~version~filename~record_count
        context.Metadata["Header"] = line;

        if (fields.Length > 1)
        {
            _fileVersion = ParseInt(fields[1]) ?? 1;
            context.Metadata["FileVersion"] = _fileVersion;
        }
        if (fields.Length > 2)
            context.Metadata["FileName"] = fields[2];
        if (fields.Length > 3)
            context.Metadata["HeaderRecordCount"] = ParseInt(fields[3]);
    }

    protected override int? ExtractSequenceNumber(string headerLine)
    {
        // No sequence number for CHG files
        return null;
    }

    protected override int? ExtractTrailerRecordCount(string trailerLine)
    {
        var fields = SplitDelimited(trailerLine, '~');
        // T~record_count~total_value
        if (fields.Length > 1)
            return ParseInt(fields[1]);
        return null;
    }

    protected override ParsedRecord? ParseDetailRecord(string line, ParseContext context, int recordNumber)
    {
        var fields = SplitDelimited(line, '~');

        // v1: 10 fields, v2: 14 fields
        var minFields = _fileVersion >= 2 ? 14 : 10;
        if (fields.Length < minFields)
        {
            return new ParsedRecord
            {
                RecordNumber = recordNumber,
                RecordType = "D",
                IsValid = false,
                ValidationError = $"Insufficient fields: expected at least {minFields}, got {fields.Length}",
                Fields = new Dictionary<string, object> { { "RawData", line } }
            };
        }

        var record = new ParsedRecord
        {
            RecordNumber = recordNumber,
            RecordType = "D",
            IsValid = true
        };

        // Field positions (0-based):
        // 0: record_number
        // 1: service_no
        // 2: charge_code
        // 3: charge_narr
        // 4: charge_start_dt
        // 5: charge_end_dt
        // 6: charge_amt_ex
        // 7: charge_amt_tax
        // 8: orig_sp_cn_ref
        // 9: orig_sp_chgdtl_ref
        // v2+:
        // 10: currency_code
        // 11: unit_quantity
        // 12: charge_category
        // 13: (reserved)

        var recNum = GetField(fields, 0);
        var serviceNo = GetField(fields, 1);
        var chargeCode = GetField(fields, 2);
        var chargeNarr = GetField(fields, 3);
        var chargeStartDtStr = GetField(fields, 4);
        var chargeEndDtStr = GetField(fields, 5);
        var chargeAmtExStr = GetField(fields, 6);
        var chargeAmtTaxStr = GetField(fields, 7);
        var origSpCnRef = GetField(fields, 8);
        var origSpChgdtlRef = GetField(fields, 9);

        var chargeStartDt = ParseDate(chargeStartDtStr, "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd");
        var chargeEndDt = ParseDate(chargeEndDtStr, "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd");
        var chargeAmtEx = ParseDecimal(chargeAmtExStr);
        var chargeAmtTax = ParseDecimal(chargeAmtTaxStr);

        // Default charge code if blank
        if (string.IsNullOrWhiteSpace(chargeCode))
            chargeCode = "NONE";

        // v2 fields
        decimal? unitQuantity = 1m; // default for v1
        string? currencyCode = null;
        string? chargeCategory = null;

        if (_fileVersion >= 2 && fields.Length > 11)
        {
            currencyCode = GetField(fields, 10);
            unitQuantity = ParseDecimal(GetField(fields, 11)) ?? 1m;
            if (fields.Length > 12)
                chargeCategory = GetField(fields, 12);
        }

        // Standard ntfl_chgdtl fields
        record.Fields["PhoneNum"] = serviceNo;
        record.Fields["ChgCode"] = chargeCode;
        record.Fields["ChgNarr"] = chargeNarr;
        record.Fields["StartDate"] = chargeStartDt;
        record.Fields["EndDate"] = chargeEndDt;
        record.Fields["CostAmount"] = chargeAmtEx;
        record.Fields["CostGst"] = chargeAmtTax;
        record.Fields["ChDtTabcd"] = "SSWC";
        record.Fields["NtRef"] = origSpChgdtlRef;
        record.Fields["UnitQuantity"] = unitQuantity;

        // Store all raw fields for sub-type record creation
        record.Fields["_raw_file_version"] = _fileVersion;
        record.Fields["_raw_record_number"] = recNum;
        record.Fields["_raw_service_no"] = serviceNo;
        record.Fields["_raw_charge_code"] = chargeCode;
        record.Fields["_raw_charge_narr"] = chargeNarr;
        record.Fields["_raw_charge_start_dt"] = chargeStartDt;
        record.Fields["_raw_charge_end_dt"] = chargeEndDt;
        record.Fields["_raw_charge_amt_ex"] = chargeAmtEx;
        record.Fields["_raw_charge_amt_tax"] = chargeAmtTax;
        record.Fields["_raw_orig_sp_cn_ref"] = origSpCnRef;
        record.Fields["_raw_orig_sp_chgdtl_ref"] = origSpChgdtlRef;
        record.Fields["_raw_currency_code"] = currencyCode;
        record.Fields["_raw_unit_quantity"] = unitQuantity;
        record.Fields["_raw_charge_category"] = chargeCategory;

        record.Fields["RawData"] = line;
        return record;
    }

    public FileDetailRecord CreateSubTypeRecord(ParsedRecord parsed, int ntFileNum, int recordNum)
    {
        return new SssWhlsChgRecord
        {
            NtFileNum = ntFileNum,
            NtFileRecNum = recordNum,
            FileVersion = GetRawInt(parsed, "_raw_file_version"),
            RecordNumber = GetRawString(parsed, "_raw_record_number"),
            ServiceNo = GetRawString(parsed, "_raw_service_no"),
            ChargeCode = GetRawString(parsed, "_raw_charge_code"),
            ChargeNarr = GetRawString(parsed, "_raw_charge_narr"),
            ChargeStartDt = GetRawDateTime(parsed, "_raw_charge_start_dt"),
            ChargeEndDt = GetRawDateTime(parsed, "_raw_charge_end_dt"),
            ChargeAmtEx = GetRawDecimal(parsed, "_raw_charge_amt_ex"),
            ChargeAmtTax = GetRawDecimal(parsed, "_raw_charge_amt_tax"),
            OrigSpCnRef = GetRawString(parsed, "_raw_orig_sp_cn_ref"),
            OrigSpChgdtlRef = GetRawString(parsed, "_raw_orig_sp_chgdtl_ref"),
            CurrencyCode = GetRawString(parsed, "_raw_currency_code"),
            UnitQuantity = GetRawDecimal(parsed, "_raw_unit_quantity"),
            ChargeCategory = GetRawString(parsed, "_raw_charge_category")
        };
    }

    private static string GetField(string[] fields, int position)
    {
        return position < fields.Length ? fields[position] : string.Empty;
    }

    private static string? GetRawString(ParsedRecord parsed, string key)
    {
        return parsed.Fields.TryGetValue(key, out var val) ? val?.ToString() : null;
    }

    private static int? GetRawInt(ParsedRecord parsed, string key)
    {
        if (!parsed.Fields.TryGetValue(key, out var val)) return null;
        if (val is int i) return i;
        if (val is string s && int.TryParse(s, out var result)) return result;
        return null;
    }

    private static decimal? GetRawDecimal(ParsedRecord parsed, string key)
    {
        if (!parsed.Fields.TryGetValue(key, out var val)) return null;
        if (val is decimal d) return d;
        if (val is string s && decimal.TryParse(s, out var result)) return result;
        return null;
    }

    private static DateTime? GetRawDateTime(ParsedRecord parsed, string key)
    {
        if (!parsed.Fields.TryGetValue(key, out var val)) return null;
        if (val is DateTime dt) return dt;
        return null;
    }
}
