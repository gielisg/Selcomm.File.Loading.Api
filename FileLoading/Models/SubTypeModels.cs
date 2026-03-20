namespace FileLoading.Models;

/// <summary>
/// CDR sub-type record - maps to ssswhls_cdr table.
/// Used alongside cl_detail for SSSWHLSCDR file type.
/// </summary>
public class SssWhlsCdrRecord : FileDetailRecord
{
    public int? FileVersion { get; set; }
    public string? RecordNumber { get; set; }
    public string? TransactionType { get; set; }
    public string? OriginatingSrvc { get; set; }
    public string? TerminatingSrvc { get; set; }
    public DateTime? ClStartDt { get; set; }
    public DateTime? ClStartDtUtc { get; set; }
    public decimal? UnitQuantity { get; set; }
    public string? Uom { get; set; }
    public decimal? NtCostEx { get; set; }
    public decimal? NtCostTax { get; set; }
    public string? TimeCode { get; set; }
    public string? CallDestination { get; set; }
    public string? OrigSpCnRef { get; set; }
    public string? OrigSpChgdtlRef { get; set; }
    public string? RatingBand { get; set; }
    public string? RatingPlan { get; set; }
    public string? RatingCategory { get; set; }
    public string? CurrencyCode { get; set; }
    public string? Imsi { get; set; }
    public string? Imei { get; set; }
    public string? CellId { get; set; }
    public string? Carrier { get; set; }
    public string? BatchNumber { get; set; }
    public decimal? NtDiscCostEx { get; set; }
    public decimal? NtDiscCostTax { get; set; }
    public string? ServiceType { get; set; }
    public string? OriginalNetwork { get; set; }
    public string? ContentType { get; set; }
}

/// <summary>
/// CHG sub-type record - maps to ssswhlschg table.
/// Used alongside ntfl_chgdtl for SSSWHLSCHG file type.
/// </summary>
public class SssWhlsChgRecord : FileDetailRecord
{
    public int? FileVersion { get; set; }
    public string? RecordNumber { get; set; }
    public string? ServiceNo { get; set; }
    public string? ChargeCode { get; set; }
    public string? ChargeNarr { get; set; }
    public DateTime? ChargeStartDt { get; set; }
    public DateTime? ChargeEndDt { get; set; }
    public decimal? ChargeAmtEx { get; set; }
    public decimal? ChargeAmtTax { get; set; }
    public string? OrigSpCnRef { get; set; }
    public string? OrigSpChgdtlRef { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? UnitQuantity { get; set; }
    public string? ChargeCategory { get; set; }
}
