namespace FileLoading.Models;

/// <summary>
/// Base class for file detail records.
/// Maps to actual database tables (cl_detail, ntfl_chgdtl).
/// </summary>
public abstract class FileDetailRecord
{
    /// <summary>Reference to parent file (nt_file_num).</summary>
    public int NtFileNum { get; set; }

    /// <summary>Record number within file (nt_file_rec_num).</summary>
    public int NtFileRecNum { get; set; }
}

/// <summary>
/// Call/Usage detail record - maps to cl_detail table.
/// Used for CDR file types (TEL_GSM, TEL_CDMA, OPTUS, AAPT, VODA, etc.)
/// </summary>
public class ClDetailRecord : FileDetailRecord
{
    /// <summary>Invoice reference (inv_ref).</summary>
    public int? InvRef { get; set; }

    /// <summary>Service provider connection reference (sp_cn_ref).</summary>
    public int? SpCnRef { get; set; }

    /// <summary>Service provider plan reference (sp_plan_ref).</summary>
    public int? SpPlanRef { get; set; }

    /// <summary>Number called/dialed (num_called).</summary>
    public string? NumCalled { get; set; }

    /// <summary>Tariff class code (tar_class_code).</summary>
    public short? TarClassCode { get; set; }

    /// <summary>Call start date/time (cl_start_dt).</summary>
    public DateTime? ClStartDt { get; set; }

    /// <summary>Unit type (unit) - e.g., 'S' for seconds.</summary>
    public string? Unit { get; set; }

    /// <summary>Unit quantity (unitquantity).</summary>
    public decimal? UnitQuantity { get; set; }

    /// <summary>Call duration as TimeSpan (cl_duration).</summary>
    public TimeSpan? ClDuration { get; set; }

    /// <summary>Network cost (nt_cost).</summary>
    public decimal? NtCost { get; set; }

    /// <summary>Network cost excluding tax (nt_cost_ex).</summary>
    public decimal? NtCostEx { get; set; }

    /// <summary>Network cost tax (nt_cost_tax).</summary>
    public decimal? NtCostTax { get; set; }

    /// <summary>Retail non-discountable excluding tax (rtl_non_disc_ex).</summary>
    public decimal? RtlNonDiscEx { get; set; }

    /// <summary>Retail non-discountable tax (rtl_non_disc_tax).</summary>
    public decimal? RtlNonDiscTax { get; set; }

    /// <summary>Retail discountable excluding tax (rtl_disc_ex).</summary>
    public decimal? RtlDiscEx { get; set; }

    /// <summary>Retail discountable tax (rtl_disc_tax).</summary>
    public decimal? RtlDiscTax { get; set; }

    /// <summary>Timeband code (timebandcode).</summary>
    public string? TimebandCode { get; set; }

    /// <summary>B-party destination description (bparty_destn).</summary>
    public string? BpartyDestn { get; set; }

    /// <summary>Call start date/time on server (cl_start_dt_srvr).</summary>
    public DateTime? ClStartDtSrvr { get; set; }

    /// <summary>Network call SVIN (nt_cl_svin).</summary>
    public string? NtClSvin { get; set; }

    /// <summary>Call status (cl_status).</summary>
    public short? ClStatus { get; set; }

    /// <summary>Tariff number (tariff_num).</summary>
    public int? TariffNum { get; set; }

    /// <summary>Process reference (process_ref).</summary>
    public int? ProcessRef { get; set; }

    /// <summary>Call detail table code (cl_dt_tabcd).</summary>
    public string? ClDtTabcd { get; set; }
}

/// <summary>
/// Charge detail record - maps to ntfl_chgdtl table.
/// Used for charge file types (CHG).
/// </summary>
public class NtflChgdtlRecord : FileDetailRecord
{
    /// <summary>Status ID (status_id).</summary>
    public int? StatusId { get; set; }

    /// <summary>Phone number (phone_num).</summary>
    public string? PhoneNum { get; set; }

    /// <summary>Service provider connection reference (sp_cn_ref).</summary>
    public int? SpCnRef { get; set; }

    /// <summary>Service provider plan reference (sp_plan_ref).</summary>
    public int? SpPlanRef { get; set; }

    /// <summary>Service provider charge reference (sp_cn_chg_ref).</summary>
    public int? SpCnChgRef { get; set; }

    /// <summary>Charge code (chg_code).</summary>
    public string? ChgCode { get; set; }

    /// <summary>Start date (start_date).</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>End date (end_date).</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>Calculated amount (calc_amount).</summary>
    public decimal? CalcAmount { get; set; }

    /// <summary>Calculated GST (calc_gst).</summary>
    public decimal? CalcGst { get; set; }

    /// <summary>Manual amount (man_amount).</summary>
    public decimal? ManAmount { get; set; }

    /// <summary>Manual GST (man_gst).</summary>
    public decimal? ManGst { get; set; }

    /// <summary>Cost amount (cost_amount).</summary>
    public decimal? CostAmount { get; set; }

    /// <summary>Cost GST (cost_gst).</summary>
    public decimal? CostGst { get; set; }

    /// <summary>Unit quantity (unitquantity).</summary>
    public decimal? UnitQuantity { get; set; }

    /// <summary>Destination database (dest_db).</summary>
    public string? DestDb { get; set; }

    /// <summary>Charge narrative (chg_narr).</summary>
    public string? ChgNarr { get; set; }

    /// <summary>Use net price flag (use_net_price).</summary>
    public string? UseNetPrice { get; set; }

    /// <summary>Net price prorated flag (net_prc_prorated).</summary>
    public string? NetPrcProrated { get; set; }

    /// <summary>Frequency (frequency).</summary>
    public string? Frequency { get; set; }

    /// <summary>Uplift percentage (uplift_perc).</summary>
    public decimal? UpliftPerc { get; set; }

    /// <summary>Uplift amount (uplift_amt).</summary>
    public decimal? UpliftAmt { get; set; }

    /// <summary>Prorate ratio (prorate_ratio).</summary>
    public decimal? ProrateRatio { get; set; }

    /// <summary>Charge detail table code (ch_dt_tabcd).</summary>
    public string? ChDtTabcd { get; set; }

    /// <summary>Network charge reason code (nt_chg_reas_code).</summary>
    public string? NtChgReasCode { get; set; }

    /// <summary>Note (note).</summary>
    public string? Note { get; set; }

    /// <summary>Case number (case_no).</summary>
    public string? CaseNo { get; set; }

    /// <summary>Network reference (nt_ref).</summary>
    public string? NtRef { get; set; }
}

/// <summary>
/// Record that failed to load - maps to nt_cl_not_load table.
/// Used for tracking errors during file processing.
/// </summary>
public class NtClNotLoadRecord : FileDetailRecord
{
    /// <summary>Service provider connection reference (sp_cn_ref).</summary>
    public int? SpCnRef { get; set; }

    /// <summary>Call detail table code (cl_dt_tabcd).</summary>
    public string? ClDtTabcd { get; set; }

    /// <summary>Phone number (phone_num).</summary>
    public string? PhoneNum { get; set; }

    /// <summary>Call start date/time (cl_start_dt).</summary>
    public DateTime? ClStartDt { get; set; }

    /// <summary>Number called (num_called).</summary>
    public string? NumCalled { get; set; }

    /// <summary>Unit type (unit).</summary>
    public string? Unit { get; set; }

    /// <summary>Unit quantity (unitquantity).</summary>
    public decimal? UnitQuantity { get; set; }

    /// <summary>Call duration (cl_duration).</summary>
    public TimeSpan? ClDuration { get; set; }

    /// <summary>Tariff class code (tar_class_code).</summary>
    public short? TarClassCode { get; set; }

    /// <summary>Network cost (nt_cost).</summary>
    public decimal? NtCost { get; set; }

    /// <summary>Network cost excluding tax (nt_cost_ex).</summary>
    public decimal? NtCostEx { get; set; }

    /// <summary>Network cost tax (nt_cost_tax).</summary>
    public decimal? NtCostTax { get; set; }

    /// <summary>Wholesale cost (ws_cost).</summary>
    public decimal? WsCost { get; set; }

    /// <summary>Wholesale cost excluding tax (ws_cost_ex).</summary>
    public decimal? WsCostEx { get; set; }

    /// <summary>Wholesale cost tax (ws_cost_tax).</summary>
    public decimal? WsCostTax { get; set; }

    /// <summary>Network free call code (nt_free_cl_code).</summary>
    public string? NtFreeClCode { get; set; }

    /// <summary>Network call SVIN (nt_cl_svin).</summary>
    public string? NtClSvin { get; set; }

    /// <summary>IMEI (imei).</summary>
    public string? Imei { get; set; }

    /// <summary>Call start date/time on server (cl_start_dt_srvr).</summary>
    public DateTime? ClStartDtSrvr { get; set; }

    /// <summary>B-party destination (bparty_destn).</summary>
    public string? BpartyDestn { get; set; }

    /// <summary>Network load code (nl_code).</summary>
    public string? NlCode { get; set; }

    /// <summary>Business unit code (bus_unit_code).</summary>
    public string? BusUnitCode { get; set; }

    /// <summary>Error code (err_code).</summary>
    public string? ErrCode { get; set; }

    /// <summary>Status ID (status_id).</summary>
    public string? StatusId { get; set; }

    /// <summary>Status description (status_desc).</summary>
    public string? StatusDesc { get; set; }

    /// <summary>Rating A-party (rating_a_party).</summary>
    public string? RatingAParty { get; set; }

    /// <summary>Rating B-party (rating_b_party).</summary>
    public string? RatingBParty { get; set; }

    /// <summary>Rating rule (rating_rule).</summary>
    public short? RatingRule { get; set; }

    /// <summary>Retail non-discountable excluding tax (rtl_non_disc_ex).</summary>
    public decimal? RtlNonDiscEx { get; set; }

    /// <summary>Network call reason code (nt_cl_reas_code).</summary>
    public string? NtClReasCode { get; set; }

    /// <summary>Note (note).</summary>
    public string? Note { get; set; }

    /// <summary>Case number (case_no).</summary>
    public string? CaseNo { get; set; }
}

/// <summary>
/// File trailer record - maps to nt_fl_trailer table.
/// </summary>
public class NtFlTrailerRecord
{
    /// <summary>File number (nt_file_num).</summary>
    public int NtFileNum { get; set; }

    /// <summary>Earliest call date (nt_earliest_call).</summary>
    public DateTime? NtEarliestCall { get; set; }

    /// <summary>Latest call date (nt_latest_call).</summary>
    public DateTime? NtLatestCall { get; set; }

    /// <summary>Processing date (nt_proc_date).</summary>
    public DateTime? NtProcDate { get; set; }

    /// <summary>Processing time (nt_proc_time).</summary>
    public DateTime? NtProcTime { get; set; }

    /// <summary>Total quantity (nt_tot_quantity).</summary>
    public decimal? NtTotQuantity { get; set; }

    /// <summary>Total cost (nt_tot_cost).</summary>
    public decimal? NtTotCost { get; set; }

    /// <summary>Total records (nt_tot_rec).</summary>
    public int? NtTotRec { get; set; }

    /// <summary>Total non-discountable (nt_tot_non_disc).</summary>
    public decimal? NtTotNonDisc { get; set; }
}

/// <summary>
/// Error log record - maps to ntfl_error_log table.
/// Used for tracking file-level and record-level errors during processing.
/// </summary>
public class NtflErrorLogRecord
{
    /// <summary>File number (nt_file_num).</summary>
    public int NtFileNum { get; set; }

    /// <summary>Error sequence within file (error_seq).</summary>
    public int ErrorSeq { get; set; }

    /// <summary>Error code (error_code) - e.g., 'HDR_MISSING', 'TRL_MISMATCH', 'PARSE_ERR'.</summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>Error message (error_message).</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Line number in file where error occurred (line_number). Null for file-level errors.</summary>
    public int? LineNumber { get; set; }

    /// <summary>Raw data from the line that caused the error (raw_data).</summary>
    public string? RawData { get; set; }

    /// <summary>Timestamp when error was logged (created_dt).</summary>
    public DateTime CreatedDt { get; set; } = DateTime.Now;
}

/// <summary>
/// Error codes for file processing errors.
/// </summary>
public static class FileErrorCodes
{
    // File-level errors
    public const string HeaderMissing = "HDR_MISS";
    public const string HeaderWrongPlace = "HDR_POS";
    public const string HeaderMultiple = "HDR_MULT";
    public const string TrailerMissing = "TRL_MISS";
    public const string TrailerMultiple = "TRL_MULT";
    public const string TrailerCountMismatch = "TRL_CNT";
    public const string FileEmpty = "FILE_EMPTY";

    // Record-level errors
    public const string ParseError = "PARSE_ERR";
    public const string InvalidFields = "INV_FIELD";
    public const string MissingRequired = "REQ_MISS";
    public const string InvalidFormat = "INV_FMT";
}

/// <summary>
/// File status constants matching nt_file_stat table.
/// </summary>
public static class FileStatus
{
    /// <summary>Initial loading in progress.</summary>
    public const int InitialLoading = 1;

    /// <summary>Transactions loaded.</summary>
    public const int TransactionsLoaded = 2;

    /// <summary>Processing Errors.</summary>
    public const int ProcessingErrors = 3;

    /// <summary>Processing Completed.</summary>
    public const int ProcessingCompleted = 4;

    /// <summary>File Discarded.</summary>
    public const int FileDiscarded = 5;

    /// <summary>File generation in progress.</summary>
    public const int FileGenerationInProgress = 10;

    /// <summary>File generation complete.</summary>
    public const int FileGenerationComplete = 11;

    /// <summary>Response received - Some errors.</summary>
    public const int ResponseSomeErrors = 12;

    /// <summary>Response received - No errors.</summary>
    public const int ResponseNoErrors = 13;

    /// <summary>Get status description.</summary>
    public static string GetDescription(int statusId) => statusId switch
    {
        InitialLoading => "Initial loading in progress",
        TransactionsLoaded => "Transactions loaded",
        ProcessingErrors => "Processing Errors",
        ProcessingCompleted => "Processing Completed",
        FileDiscarded => "File Discarded",
        FileGenerationInProgress => "File generation in progress",
        FileGenerationComplete => "File generation complete",
        ResponseSomeErrors => "Response received - Some errors",
        ResponseNoErrors => "Response received - No errors",
        _ => "Unknown"
    };
}
