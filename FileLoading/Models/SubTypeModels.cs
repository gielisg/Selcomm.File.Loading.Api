namespace FileLoading.Models;

/// <summary>
/// CDR sub-type record — maps to the ssswhls_cdr table.
/// Used alongside cl_detail for the SSSWHLSCDR file type.
/// Contains extended fields specific to the SSS Wholesale CDR format.
/// </summary>
public class SssWhlsCdrRecord : FileDetailRecord
{
    /// <summary>File format version number.</summary>
    /// <example>2</example>
    public int? FileVersion { get; set; }

    /// <summary>Record number within the vendor's file.</summary>
    /// <example>00001</example>
    public string? RecordNumber { get; set; }

    /// <summary>Transaction type code (e.g., VOICE, DATA, SMS).</summary>
    /// <example>VOICE</example>
    public string? TransactionType { get; set; }

    /// <summary>Originating service identifier (A-party phone number or circuit ID).</summary>
    /// <example>0412345678</example>
    public string? OriginatingSrvc { get; set; }

    /// <summary>Terminating service identifier (B-party phone number).</summary>
    /// <example>0298765432</example>
    public string? TerminatingSrvc { get; set; }

    /// <summary>Call start date/time in the local timezone.</summary>
    /// <example>2025-03-15T08:30:00</example>
    public DateTime? ClStartDt { get; set; }

    /// <summary>Call start date/time in UTC.</summary>
    /// <example>2025-03-14T21:30:00Z</example>
    public DateTime? ClStartDtUtc { get; set; }

    /// <summary>Unit quantity (e.g., seconds, bytes, messages).</summary>
    /// <example>300</example>
    public decimal? UnitQuantity { get; set; }

    /// <summary>Unit of measure (SEC, MB, MSG, etc.).</summary>
    /// <example>SEC</example>
    public string? Uom { get; set; }

    /// <summary>Network cost excluding tax.</summary>
    /// <example>1.50</example>
    public decimal? NtCostEx { get; set; }

    /// <summary>Network cost tax component.</summary>
    /// <example>0.15</example>
    public decimal? NtCostTax { get; set; }

    /// <summary>Time code indicating peak/off-peak/weekend classification.</summary>
    /// <example>PEAK</example>
    public string? TimeCode { get; set; }

    /// <summary>Call destination description.</summary>
    /// <example>Sydney Local</example>
    public string? CallDestination { get; set; }

    /// <summary>Original service provider connection reference from the upstream system.</summary>
    /// <example>SP100234</example>
    public string? OrigSpCnRef { get; set; }

    /// <summary>Original service provider charge detail reference from the upstream system.</summary>
    /// <example>SPD50001</example>
    public string? OrigSpChgdtlRef { get; set; }

    /// <summary>Rating band applied to this call.</summary>
    /// <example>LOCAL</example>
    public string? RatingBand { get; set; }

    /// <summary>Rating plan used for costing this call.</summary>
    /// <example>STANDARD</example>
    public string? RatingPlan { get; set; }

    /// <summary>Rating category (e.g., VOICE, DATA, VALUE_ADDED).</summary>
    /// <example>VOICE</example>
    public string? RatingCategory { get; set; }

    /// <summary>Currency code for the cost amounts (ISO 4217).</summary>
    /// <example>AUD</example>
    public string? CurrencyCode { get; set; }

    /// <summary>International Mobile Subscriber Identity.</summary>
    /// <example>505012345678901</example>
    public string? Imsi { get; set; }

    /// <summary>International Mobile Equipment Identity (device identifier).</summary>
    /// <example>353456789012345</example>
    public string? Imei { get; set; }

    /// <summary>Cell tower ID where the call originated.</summary>
    /// <example>SYD-NTH-001</example>
    public string? CellId { get; set; }

    /// <summary>Carrier/network operator name.</summary>
    /// <example>Telstra</example>
    public string? Carrier { get; set; }

    /// <summary>Vendor batch number this record belongs to.</summary>
    /// <example>BATCH-20250315-001</example>
    public string? BatchNumber { get; set; }

    /// <summary>Network discounted cost excluding tax.</summary>
    /// <example>1.20</example>
    public decimal? NtDiscCostEx { get; set; }

    /// <summary>Network discounted cost tax component.</summary>
    /// <example>0.12</example>
    public decimal? NtDiscCostTax { get; set; }

    /// <summary>Service type classification (MOBILE, FIXED, BROADBAND).</summary>
    /// <example>MOBILE</example>
    public string? ServiceType { get; set; }

    /// <summary>Original network identifier for roaming or MVNO scenarios.</summary>
    /// <example>TL</example>
    public string? OriginalNetwork { get; set; }

    /// <summary>Content type for value-added services (e.g., PREMIUM_SMS, RINGTONE).</summary>
    public string? ContentType { get; set; }
}

/// <summary>
/// CHG sub-type record — maps to the ssswhlschg table.
/// Used alongside ntfl_chgdtl for the SSSWHLSCHG file type.
/// Contains extended fields specific to the SSS Wholesale charge format.
/// </summary>
public class SssWhlsChgRecord : FileDetailRecord
{
    /// <summary>File format version number.</summary>
    /// <example>2</example>
    public int? FileVersion { get; set; }

    /// <summary>Record number within the vendor's file.</summary>
    /// <example>00001</example>
    public string? RecordNumber { get; set; }

    /// <summary>Service number (phone number or circuit ID).</summary>
    /// <example>0412345678</example>
    public string? ServiceNo { get; set; }

    /// <summary>Charge code identifying the type of charge.</summary>
    /// <example>MRC</example>
    public string? ChargeCode { get; set; }

    /// <summary>Charge narrative/description.</summary>
    /// <example>Monthly service fee - Mobile plan</example>
    public string? ChargeNarr { get; set; }

    /// <summary>Charge period start date.</summary>
    /// <example>2025-03-01</example>
    public DateTime? ChargeStartDt { get; set; }

    /// <summary>Charge period end date.</summary>
    /// <example>2025-03-31</example>
    public DateTime? ChargeEndDt { get; set; }

    /// <summary>Charge amount excluding tax.</summary>
    /// <example>49.95</example>
    public decimal? ChargeAmtEx { get; set; }

    /// <summary>Charge tax amount.</summary>
    /// <example>5.00</example>
    public decimal? ChargeAmtTax { get; set; }

    /// <summary>Original service provider connection reference from the upstream system.</summary>
    /// <example>SP100234</example>
    public string? OrigSpCnRef { get; set; }

    /// <summary>Original service provider charge detail reference from the upstream system.</summary>
    /// <example>SPD50001</example>
    public string? OrigSpChgdtlRef { get; set; }

    /// <summary>Currency code for the charge amounts (ISO 4217).</summary>
    /// <example>AUD</example>
    public string? CurrencyCode { get; set; }

    /// <summary>Unit quantity for the charge.</summary>
    /// <example>1</example>
    public decimal? UnitQuantity { get; set; }

    /// <summary>Charge category classification (e.g., RECURRING, USAGE, ADJUSTMENT).</summary>
    /// <example>RECURRING</example>
    public string? ChargeCategory { get; set; }
}
