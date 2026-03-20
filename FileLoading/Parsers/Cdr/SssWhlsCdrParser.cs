using Microsoft.Extensions.Logging;
using FileLoading.Models;

namespace FileLoading.Parsers.Cdr;

/// <summary>
/// SSSWHLSCDR file parser - tilde-delimited CDR with sub-type table (ssswhls_cdr).
/// Inserts into both cl_detail (standard CDR) and ssswhls_cdr (sub-type extension).
/// Based on legacy 4GL ntfileload_cd2.4gl.
/// </summary>
public class SssWhlsCdrParser : BaseFileParser, ISubTypeRecordProvider
{
    private int _fileVersion;
    private string? _batchNumber;

    public SssWhlsCdrParser(ILogger<SssWhlsCdrParser> logger) : base(logger) { }

    public override string FileType => "SSSWHLSCDR";
    public override string FileClassCode => "CDR";

    public string SubTypeTableName => "ssswhls_cdr";

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
        // H~version~batch_number~filename~record_count
        context.Metadata["Header"] = line;

        if (fields.Length > 1)
        {
            _fileVersion = ParseInt(fields[1]) ?? 1;
            context.Metadata["FileVersion"] = _fileVersion;
        }
        if (fields.Length > 2)
        {
            _batchNumber = fields[2];
            context.Metadata["BatchNumber"] = _batchNumber;
        }
        if (fields.Length > 3)
            context.Metadata["FileName"] = fields[3];
        if (fields.Length > 4)
            context.Metadata["HeaderRecordCount"] = ParseInt(fields[4]);
    }

    protected override int? ExtractSequenceNumber(string headerLine)
    {
        var fields = SplitDelimited(headerLine, '~');
        // Batch number as sequence
        if (fields.Length > 2)
            return ParseInt(fields[2]);
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

        // Minimum fields: record_number, transaction_type, originating_srvc, terminating_srvc,
        //                 cl_start_dt, cl_start_dt_utc, unitquantity, uom, nt_cost_ex, nt_cost_tax
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

        // Field positions (0-based):
        // 0: record_number
        // 1: transaction_type (C=Call, U=Usage)
        // 2: originating_srvc (A-party)
        // 3: terminating_srvc (B-party)
        // 4: cl_start_dt
        // 5: cl_start_dt_utc
        // 6: unitquantity
        // 7: uom
        // 8: nt_cost_ex
        // 9: nt_cost_tax
        // 10: time_code
        // 11: call_destination
        // 12: orig_sp_cn_ref
        // 13: orig_sp_chgdtl_ref
        // 14: rating_band
        // 15: rating_plan
        // 16: rating_category
        // 17: currency_code
        // 18: imsi
        // 19: imei
        // 20: cell_id
        // 21: carrier
        // v3+:
        // 22: nt_disc_cost_ex
        // 23: nt_disc_cost_tax
        // v5+:
        // 24: service_type
        // 25: original_network
        // v7+:
        // 26: content_type

        var recNum = GetField(fields, 0);
        var transType = GetField(fields, 1);
        var originatingSrvc = GetField(fields, 2);
        var terminatingSrvc = GetField(fields, 3);
        var clStartDtStr = GetField(fields, 4);
        var clStartDtUtcStr = GetField(fields, 5);
        var unitQtyStr = GetField(fields, 6);
        var uom = GetField(fields, 7);
        var ntCostExStr = GetField(fields, 8);
        var ntCostTaxStr = GetField(fields, 9);
        var timeCode = GetField(fields, 10);
        var callDestination = GetField(fields, 11);

        // Validate required fields
        if (string.IsNullOrWhiteSpace(recNum))
        {
            record.IsValid = false;
            record.ValidationError = "Record number is required";
            record.Fields["RawData"] = line;
            return record;
        }
        if (transType != "C" && transType != "U")
        {
            record.IsValid = false;
            record.ValidationError = $"Invalid transaction type '{transType}' - must be C or U";
            record.Fields["RawData"] = line;
            return record;
        }
        if (string.IsNullOrWhiteSpace(originatingSrvc))
        {
            record.IsValid = false;
            record.ValidationError = "Originating service is required";
            record.Fields["RawData"] = line;
            return record;
        }

        var clStartDt = ParseDate(clStartDtStr, "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd");
        if (clStartDt == null && !string.IsNullOrWhiteSpace(clStartDtStr))
        {
            record.IsValid = false;
            record.ValidationError = $"Invalid call start date: {clStartDtStr}";
            record.Fields["RawData"] = line;
            return record;
        }

        var unitQuantity = ParseDecimal(unitQtyStr);
        if (unitQuantity == null && !string.IsNullOrWhiteSpace(unitQtyStr))
        {
            record.IsValid = false;
            record.ValidationError = $"Invalid unit quantity: {unitQtyStr}";
            record.Fields["RawData"] = line;
            return record;
        }

        if (string.IsNullOrWhiteSpace(uom))
        {
            record.IsValid = false;
            record.ValidationError = "Unit of measure is required";
            record.Fields["RawData"] = line;
            return record;
        }

        var ntCostEx = ParseDecimal(ntCostExStr) ?? 0m;
        var ntCostTax = ParseDecimal(ntCostTaxStr) ?? 0m;
        var ntCost = ntCostEx + ntCostTax;
        var clStartDtUtc = ParseDate(clStartDtUtcStr, "yyyy-MM-dd HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd");

        // Standard cl_detail fields
        record.Fields["NtClSvin"] = originatingSrvc;
        record.Fields["NumCalled"] = transType == "U" ? "Usage" : terminatingSrvc;
        record.Fields["ClStartDt"] = clStartDt;
        record.Fields["ClStartDtSrvr"] = clStartDtUtc;
        record.Fields["UnitQuantity"] = unitQuantity;
        record.Fields["Unit"] = uom;
        record.Fields["NtCost"] = ntCost;
        record.Fields["NtCostEx"] = ntCostEx;
        record.Fields["NtCostTax"] = ntCostTax;
        record.Fields["TimebandCode"] = timeCode;
        record.Fields["BpartyDestn"] = callDestination;
        record.Fields["ClDtTabcd"] = "SS";

        // Duration: for calls (type C) convert seconds, for usage 0
        if (transType == "C" && unitQuantity.HasValue)
        {
            record.Fields["ClDuration"] = TimeSpan.FromSeconds((double)unitQuantity.Value);
        }
        else
        {
            record.Fields["ClDuration"] = TimeSpan.Zero;
        }

        // Non-discountable cost (v3+)
        if (_fileVersion >= 3 && fields.Length > 22)
        {
            record.Fields["RtlNonDiscEx"] = ParseDecimal(GetField(fields, 22));
        }

        // Store all raw fields for sub-type record creation
        record.Fields["_raw_record_number"] = recNum;
        record.Fields["_raw_transaction_type"] = transType;
        record.Fields["_raw_originating_srvc"] = originatingSrvc;
        record.Fields["_raw_terminating_srvc"] = terminatingSrvc;
        record.Fields["_raw_cl_start_dt"] = clStartDt;
        record.Fields["_raw_cl_start_dt_utc"] = clStartDtUtc;
        record.Fields["_raw_unitquantity"] = unitQuantity;
        record.Fields["_raw_uom"] = uom;
        record.Fields["_raw_nt_cost_ex"] = ntCostEx;
        record.Fields["_raw_nt_cost_tax"] = ntCostTax;
        record.Fields["_raw_time_code"] = timeCode;
        record.Fields["_raw_call_destination"] = callDestination;
        record.Fields["_raw_orig_sp_cn_ref"] = GetField(fields, 12);
        record.Fields["_raw_orig_sp_chgdtl_ref"] = GetField(fields, 13);
        record.Fields["_raw_rating_band"] = GetField(fields, 14);
        record.Fields["_raw_rating_plan"] = GetField(fields, 15);
        record.Fields["_raw_rating_category"] = GetField(fields, 16);
        record.Fields["_raw_currency_code"] = GetField(fields, 17);
        record.Fields["_raw_imsi"] = GetField(fields, 18);
        record.Fields["_raw_imei"] = GetField(fields, 19);
        record.Fields["_raw_cell_id"] = GetField(fields, 20);
        record.Fields["_raw_carrier"] = GetField(fields, 21);
        record.Fields["_raw_batch_number"] = _batchNumber;
        record.Fields["_raw_file_version"] = _fileVersion;

        // Version-dependent fields
        if (_fileVersion >= 3 && fields.Length > 23)
        {
            record.Fields["_raw_nt_disc_cost_ex"] = ParseDecimal(GetField(fields, 22));
            record.Fields["_raw_nt_disc_cost_tax"] = ParseDecimal(GetField(fields, 23));
        }
        if (_fileVersion >= 5 && fields.Length > 25)
        {
            record.Fields["_raw_service_type"] = GetField(fields, 24);
            record.Fields["_raw_original_network"] = GetField(fields, 25);
        }
        if (_fileVersion >= 7 && fields.Length > 26)
        {
            record.Fields["_raw_content_type"] = GetField(fields, 26);
        }

        record.Fields["RawData"] = line;
        return record;
    }

    public FileDetailRecord CreateSubTypeRecord(ParsedRecord parsed, int ntFileNum, int recordNum)
    {
        return new SssWhlsCdrRecord
        {
            NtFileNum = ntFileNum,
            NtFileRecNum = recordNum,
            FileVersion = GetRawInt(parsed, "_raw_file_version"),
            RecordNumber = GetRawString(parsed, "_raw_record_number"),
            TransactionType = GetRawString(parsed, "_raw_transaction_type"),
            OriginatingSrvc = GetRawString(parsed, "_raw_originating_srvc"),
            TerminatingSrvc = GetRawString(parsed, "_raw_terminating_srvc"),
            ClStartDt = GetRawDateTime(parsed, "_raw_cl_start_dt"),
            ClStartDtUtc = GetRawDateTime(parsed, "_raw_cl_start_dt_utc"),
            UnitQuantity = GetRawDecimal(parsed, "_raw_unitquantity"),
            Uom = GetRawString(parsed, "_raw_uom"),
            NtCostEx = GetRawDecimal(parsed, "_raw_nt_cost_ex"),
            NtCostTax = GetRawDecimal(parsed, "_raw_nt_cost_tax"),
            TimeCode = GetRawString(parsed, "_raw_time_code"),
            CallDestination = GetRawString(parsed, "_raw_call_destination"),
            OrigSpCnRef = GetRawString(parsed, "_raw_orig_sp_cn_ref"),
            OrigSpChgdtlRef = GetRawString(parsed, "_raw_orig_sp_chgdtl_ref"),
            RatingBand = GetRawString(parsed, "_raw_rating_band"),
            RatingPlan = GetRawString(parsed, "_raw_rating_plan"),
            RatingCategory = GetRawString(parsed, "_raw_rating_category"),
            CurrencyCode = GetRawString(parsed, "_raw_currency_code"),
            Imsi = GetRawString(parsed, "_raw_imsi"),
            Imei = GetRawString(parsed, "_raw_imei"),
            CellId = GetRawString(parsed, "_raw_cell_id"),
            Carrier = GetRawString(parsed, "_raw_carrier"),
            BatchNumber = GetRawString(parsed, "_raw_batch_number"),
            NtDiscCostEx = GetRawDecimal(parsed, "_raw_nt_disc_cost_ex"),
            NtDiscCostTax = GetRawDecimal(parsed, "_raw_nt_disc_cost_tax"),
            ServiceType = GetRawString(parsed, "_raw_service_type"),
            OriginalNetwork = GetRawString(parsed, "_raw_original_network"),
            ContentType = GetRawString(parsed, "_raw_content_type")
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
