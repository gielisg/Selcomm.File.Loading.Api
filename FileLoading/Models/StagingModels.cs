namespace FileLoading.Models;

/// <summary>
/// Base class for file detail records.
/// Maps to actual database tables (cl_detail, ntfl_chgdtl, ntfl_generic_detail).
/// All detail record types inherit NtFileNum and NtFileRecNum from this class.
/// </summary>
public abstract class FileDetailRecord
{
    /// <summary>Reference to the parent file (nt_file_num foreign key).</summary>
    /// <example>12345</example>
    public int NtFileNum { get; set; }

    /// <summary>Record number within the file (nt_file_rec_num), 1-based.</summary>
    /// <example>1</example>
    public int NtFileRecNum { get; set; }
}

/// <summary>
/// Call/usage detail record — maps to the cl_detail table.
/// Used for CDR file types (TEL_GSM, TEL_CDMA, OPTUS, AAPT, VODA, etc.).
/// Each record represents a single call or usage event.
/// </summary>
public class ClDetailRecord : FileDetailRecord
{
    /// <summary>Invoice reference number (inv_ref).</summary>
    /// <example>50001</example>
    public int? InvRef { get; set; }

    /// <summary>Service provider connection reference (sp_cn_ref).</summary>
    /// <example>100234</example>
    public int? SpCnRef { get; set; }

    /// <summary>Service provider plan reference (sp_plan_ref).</summary>
    /// <example>500</example>
    public int? SpPlanRef { get; set; }

    /// <summary>Number called/dialed (num_called).</summary>
    /// <example>0298765432</example>
    public string? NumCalled { get; set; }

    /// <summary>Tariff class code (tar_class_code).</summary>
    /// <example>1</example>
    public short? TarClassCode { get; set; }

    /// <summary>Call start date/time in the originating timezone (cl_start_dt).</summary>
    /// <example>2025-03-15T08:30:00</example>
    public DateTime? ClStartDt { get; set; }

    /// <summary>Unit type (unit) — e.g., S for seconds, B for bytes.</summary>
    /// <example>S</example>
    public string? Unit { get; set; }

    /// <summary>Unit quantity (unitquantity) — e.g., duration in seconds or data in bytes.</summary>
    /// <example>300</example>
    public decimal? UnitQuantity { get; set; }

    /// <summary>Call duration as a TimeSpan (cl_duration).</summary>
    /// <example>00:05:00</example>
    public TimeSpan? ClDuration { get; set; }

    /// <summary>Network cost including tax (nt_cost).</summary>
    /// <example>1.65</example>
    public decimal? NtCost { get; set; }

    /// <summary>Network cost excluding tax (nt_cost_ex).</summary>
    /// <example>1.50</example>
    public decimal? NtCostEx { get; set; }

    /// <summary>Network cost tax component (nt_cost_tax).</summary>
    /// <example>0.15</example>
    public decimal? NtCostTax { get; set; }

    /// <summary>Retail non-discountable amount excluding tax (rtl_non_disc_ex).</summary>
    /// <example>0.00</example>
    public decimal? RtlNonDiscEx { get; set; }

    /// <summary>Retail non-discountable tax amount (rtl_non_disc_tax).</summary>
    /// <example>0.00</example>
    public decimal? RtlNonDiscTax { get; set; }

    /// <summary>Retail discountable amount excluding tax (rtl_disc_ex).</summary>
    /// <example>2.00</example>
    public decimal? RtlDiscEx { get; set; }

    /// <summary>Retail discountable tax amount (rtl_disc_tax).</summary>
    /// <example>0.20</example>
    public decimal? RtlDiscTax { get; set; }

    /// <summary>Timeband code indicating peak/off-peak classification (timebandcode).</summary>
    /// <example>PEAK</example>
    public string? TimebandCode { get; set; }

    /// <summary>B-party destination description (bparty_destn).</summary>
    /// <example>Sydney Local</example>
    public string? BpartyDestn { get; set; }

    /// <summary>Call start date/time on the server timezone (cl_start_dt_srvr).</summary>
    /// <example>2025-03-14T21:30:00</example>
    public DateTime? ClStartDtSrvr { get; set; }

    /// <summary>Network call service identification number (nt_cl_svin).</summary>
    /// <example>0412345678</example>
    public string? NtClSvin { get; set; }

    /// <summary>Call status code (cl_status).</summary>
    /// <example>1</example>
    public short? ClStatus { get; set; }

    /// <summary>Tariff number used for rating (tariff_num).</summary>
    /// <example>1001</example>
    public int? TariffNum { get; set; }

    /// <summary>Process reference for batch tracking (process_ref).</summary>
    /// <example>0</example>
    public int? ProcessRef { get; set; }

    /// <summary>Call detail table code for routing to the correct detail table (cl_dt_tabcd).</summary>
    /// <example>CL</example>
    public string? ClDtTabcd { get; set; }
}

/// <summary>
/// Charge detail record — maps to the ntfl_chgdtl table.
/// Used for charge file types (CHG). Each record represents a recurring or one-off charge.
/// </summary>
public class NtflChgdtlRecord : FileDetailRecord
{
    /// <summary>Record status ID (status_id).</summary>
    /// <example>1</example>
    public int? StatusId { get; set; }

    /// <summary>Phone number or service identifier (phone_num).</summary>
    /// <example>0412345678</example>
    public string? PhoneNum { get; set; }

    /// <summary>Service provider connection reference (sp_cn_ref).</summary>
    /// <example>100234</example>
    public int? SpCnRef { get; set; }

    /// <summary>Service provider plan reference (sp_plan_ref).</summary>
    /// <example>500</example>
    public int? SpPlanRef { get; set; }

    /// <summary>Service provider charge reference (sp_cn_chg_ref).</summary>
    /// <example>7001</example>
    public int? SpCnChgRef { get; set; }

    /// <summary>Charge code identifying the type of charge (chg_code).</summary>
    /// <example>MRC</example>
    public string? ChgCode { get; set; }

    /// <summary>Charge period start date (start_date).</summary>
    /// <example>2025-03-01</example>
    public DateTime? StartDate { get; set; }

    /// <summary>Charge period end date (end_date).</summary>
    /// <example>2025-03-31</example>
    public DateTime? EndDate { get; set; }

    /// <summary>Calculated charge amount excluding tax (calc_amount).</summary>
    /// <example>49.95</example>
    public decimal? CalcAmount { get; set; }

    /// <summary>Calculated GST/tax amount (calc_gst).</summary>
    /// <example>5.00</example>
    public decimal? CalcGst { get; set; }

    /// <summary>Manual override amount excluding tax (man_amount).</summary>
    /// <example>0.00</example>
    public decimal? ManAmount { get; set; }

    /// <summary>Manual override GST/tax amount (man_gst).</summary>
    /// <example>0.00</example>
    public decimal? ManGst { get; set; }

    /// <summary>Network cost amount (cost_amount).</summary>
    /// <example>35.00</example>
    public decimal? CostAmount { get; set; }

    /// <summary>Network cost GST/tax (cost_gst).</summary>
    /// <example>3.50</example>
    public decimal? CostGst { get; set; }

    /// <summary>Unit quantity for the charge (unitquantity).</summary>
    /// <example>1</example>
    public decimal? UnitQuantity { get; set; }

    /// <summary>Destination database for multi-database environments (dest_db).</summary>
    /// <example>PROD</example>
    public string? DestDb { get; set; }

    /// <summary>Charge narrative/description (chg_narr).</summary>
    /// <example>Monthly service fee - Mobile plan</example>
    public string? ChgNarr { get; set; }

    /// <summary>Whether to use net price for rating (use_net_price).</summary>
    /// <example>N</example>
    public string? UseNetPrice { get; set; }

    /// <summary>Whether the net price is prorated (net_prc_prorated).</summary>
    /// <example>N</example>
    public string? NetPrcProrated { get; set; }

    /// <summary>Charge frequency (M = Monthly, Q = Quarterly, A = Annual) (frequency).</summary>
    /// <example>M</example>
    public string? Frequency { get; set; }

    /// <summary>Uplift percentage applied to the charge (uplift_perc).</summary>
    /// <example>0.00</example>
    public decimal? UpliftPerc { get; set; }

    /// <summary>Uplift amount applied to the charge (uplift_amt).</summary>
    /// <example>0.00</example>
    public decimal? UpliftAmt { get; set; }

    /// <summary>Prorate ratio for partial-period charges (prorate_ratio).</summary>
    /// <example>1.00</example>
    public decimal? ProrateRatio { get; set; }

    /// <summary>Charge detail table code (ch_dt_tabcd).</summary>
    /// <example>CH</example>
    public string? ChDtTabcd { get; set; }

    /// <summary>Network charge reason code for adjustments (nt_chg_reas_code).</summary>
    /// <example>NEW</example>
    public string? NtChgReasCode { get; set; }

    /// <summary>Free-text note associated with the charge (note).</summary>
    public string? Note { get; set; }

    /// <summary>Related case/ticket number (case_no).</summary>
    /// <example>CASE-2025-001</example>
    public string? CaseNo { get; set; }

    /// <summary>Network reference identifier (nt_ref).</summary>
    /// <example>NTR-12345</example>
    public string? NtRef { get; set; }
}

/// <summary>
/// Record that failed to load — maps to the nt_cl_not_load table.
/// Used for tracking individual records that could not be processed during file loading.
/// Contains the original record data plus error information.
/// </summary>
public class NtClNotLoadRecord : FileDetailRecord
{
    /// <summary>Service provider connection reference (sp_cn_ref).</summary>
    /// <example>100234</example>
    public int? SpCnRef { get; set; }

    /// <summary>Call detail table code (cl_dt_tabcd).</summary>
    /// <example>CL</example>
    public string? ClDtTabcd { get; set; }

    /// <summary>Phone number or service identifier (phone_num).</summary>
    /// <example>0412345678</example>
    public string? PhoneNum { get; set; }

    /// <summary>Call start date/time (cl_start_dt).</summary>
    /// <example>2025-03-15T08:30:00</example>
    public DateTime? ClStartDt { get; set; }

    /// <summary>Number called/dialed (num_called).</summary>
    /// <example>0298765432</example>
    public string? NumCalled { get; set; }

    /// <summary>Unit type (unit).</summary>
    /// <example>S</example>
    public string? Unit { get; set; }

    /// <summary>Unit quantity (unitquantity).</summary>
    /// <example>300</example>
    public decimal? UnitQuantity { get; set; }

    /// <summary>Call duration (cl_duration).</summary>
    /// <example>00:05:00</example>
    public TimeSpan? ClDuration { get; set; }

    /// <summary>Tariff class code (tar_class_code).</summary>
    /// <example>1</example>
    public short? TarClassCode { get; set; }

    /// <summary>Network cost including tax (nt_cost).</summary>
    /// <example>1.65</example>
    public decimal? NtCost { get; set; }

    /// <summary>Network cost excluding tax (nt_cost_ex).</summary>
    /// <example>1.50</example>
    public decimal? NtCostEx { get; set; }

    /// <summary>Network cost tax component (nt_cost_tax).</summary>
    /// <example>0.15</example>
    public decimal? NtCostTax { get; set; }

    /// <summary>Wholesale cost including tax (ws_cost).</summary>
    /// <example>1.10</example>
    public decimal? WsCost { get; set; }

    /// <summary>Wholesale cost excluding tax (ws_cost_ex).</summary>
    /// <example>1.00</example>
    public decimal? WsCostEx { get; set; }

    /// <summary>Wholesale cost tax component (ws_cost_tax).</summary>
    /// <example>0.10</example>
    public decimal? WsCostTax { get; set; }

    /// <summary>Network free call code (nt_free_cl_code).</summary>
    public string? NtFreeClCode { get; set; }

    /// <summary>Network call service identification number (nt_cl_svin).</summary>
    /// <example>0412345678</example>
    public string? NtClSvin { get; set; }

    /// <summary>IMEI device identifier (imei).</summary>
    /// <example>353456789012345</example>
    public string? Imei { get; set; }

    /// <summary>Call start date/time on the server timezone (cl_start_dt_srvr).</summary>
    /// <example>2025-03-14T21:30:00</example>
    public DateTime? ClStartDtSrvr { get; set; }

    /// <summary>B-party destination description (bparty_destn).</summary>
    /// <example>Sydney Local</example>
    public string? BpartyDestn { get; set; }

    /// <summary>Network load code indicating the error category (nl_code).</summary>
    /// <example>ERR01</example>
    public string? NlCode { get; set; }

    /// <summary>Business unit code (bus_unit_code).</summary>
    /// <example>BU01</example>
    public string? BusUnitCode { get; set; }

    /// <summary>Error code describing why the record failed to load (err_code).</summary>
    /// <example>NO_MATCH</example>
    public string? ErrCode { get; set; }

    /// <summary>Status ID for the not-loaded record (status_id).</summary>
    /// <example>1</example>
    public string? StatusId { get; set; }

    /// <summary>Human-readable status description (status_desc).</summary>
    /// <example>Unmatched service</example>
    public string? StatusDesc { get; set; }

    /// <summary>Rating A-party (originating number) used in rating attempts (rating_a_party).</summary>
    /// <example>0412345678</example>
    public string? RatingAParty { get; set; }

    /// <summary>Rating B-party (terminating number) used in rating attempts (rating_b_party).</summary>
    /// <example>0298765432</example>
    public string? RatingBParty { get; set; }

    /// <summary>Rating rule that was applied or attempted (rating_rule).</summary>
    /// <example>1</example>
    public short? RatingRule { get; set; }

    /// <summary>Retail non-discountable amount excluding tax (rtl_non_disc_ex).</summary>
    /// <example>0.00</example>
    public decimal? RtlNonDiscEx { get; set; }

    /// <summary>Network call reason code (nt_cl_reas_code).</summary>
    public string? NtClReasCode { get; set; }

    /// <summary>Free-text note associated with the failed record (note).</summary>
    public string? Note { get; set; }

    /// <summary>Related case/ticket number (case_no).</summary>
    /// <example>CASE-2025-001</example>
    public string? CaseNo { get; set; }
}

/// <summary>
/// File trailer record — maps to the nt_fl_trailer table.
/// Contains summary totals from the file trailer for validation against loaded data.
/// </summary>
public class NtFlTrailerRecord
{
    /// <summary>File number (nt_file_num) this trailer belongs to.</summary>
    /// <example>12345</example>
    public int NtFileNum { get; set; }

    /// <summary>Earliest call/transaction date found in the file (nt_earliest_call).</summary>
    /// <example>2025-03-01T00:00:12Z</example>
    public DateTime? NtEarliestCall { get; set; }

    /// <summary>Latest call/transaction date found in the file (nt_latest_call).</summary>
    /// <example>2025-03-15T23:59:48Z</example>
    public DateTime? NtLatestCall { get; set; }

    /// <summary>Processing date (nt_proc_date).</summary>
    /// <example>2025-03-15</example>
    public DateTime? NtProcDate { get; set; }

    /// <summary>Processing time (nt_proc_time).</summary>
    /// <example>2025-03-15T08:06:15Z</example>
    public DateTime? NtProcTime { get; set; }

    /// <summary>Total unit quantity across all records (nt_tot_quantity).</summary>
    /// <example>4500000</example>
    public decimal? NtTotQuantity { get; set; }

    /// <summary>Total cost across all records (nt_tot_cost).</summary>
    /// <example>12500.75</example>
    public decimal? NtTotCost { get; set; }

    /// <summary>Total number of detail records in the file (nt_tot_rec).</summary>
    /// <example>15000</example>
    public int? NtTotRec { get; set; }

    /// <summary>Total non-discountable amount (nt_tot_non_disc).</summary>
    /// <example>250.00</example>
    public decimal? NtTotNonDisc { get; set; }
}

/// <summary>
/// Error log record — maps to the ntfl_error_log table.
/// Used for tracking file-level and record-level errors encountered during file processing.
/// </summary>
public class NtflErrorLogRecord
{
    /// <summary>File number this error belongs to (nt_file_num).</summary>
    /// <example>12345</example>
    public int NtFileNum { get; set; }

    /// <summary>Error sequence number within the file (error_seq), 1-based.</summary>
    /// <example>1</example>
    public int ErrorSeq { get; set; }

    /// <summary>Error code identifying the type of error (e.g., HDR_MISS, TRL_MISMATCH, PARSE_ERR).</summary>
    /// <example>PARSE_ERR</example>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>Human-readable error message describing what went wrong.</summary>
    /// <example>Invalid date format in column 5 at line 142</example>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Line number in the file where the error occurred. Null for file-level errors.</summary>
    /// <example>142</example>
    public int? LineNumber { get; set; }

    /// <summary>Raw data from the line that caused the error (for debugging).</summary>
    /// <example>D,0412345678,INVALID_DATE,300,1.50</example>
    public string? RawData { get; set; }

    /// <summary>Timestamp when the error was logged.</summary>
    /// <example>2025-03-15T08:05:45Z</example>
    public DateTime CreatedDt { get; set; } = DateTime.Now;
}

/// <summary>
/// Error codes for file processing errors.
/// Constants used in NtflErrorLogRecord.ErrorCode and ParseError.ErrorCode fields.
/// </summary>
public static class FileErrorCodes
{
    // File-level errors

    /// <summary>Required header row is missing from the file.</summary>
    public const string HeaderMissing = "HDR_MISS";

    /// <summary>Header row found in wrong position (not at the start of the file).</summary>
    public const string HeaderWrongPlace = "HDR_POS";

    /// <summary>Multiple header rows found in the file.</summary>
    public const string HeaderMultiple = "HDR_MULT";

    /// <summary>Required trailer row is missing from the file.</summary>
    public const string TrailerMissing = "TRL_MISS";

    /// <summary>Multiple trailer rows found in the file.</summary>
    public const string TrailerMultiple = "TRL_MULT";

    /// <summary>Trailer record count does not match the actual number of detail records.</summary>
    public const string TrailerCountMismatch = "TRL_CNT";

    /// <summary>File is empty (zero bytes or no data rows).</summary>
    public const string FileEmpty = "FILE_EMPTY";

    // Record-level errors

    /// <summary>Generic parse error — could not parse the record.</summary>
    public const string ParseError = "PARSE_ERR";

    /// <summary>One or more field values are invalid.</summary>
    public const string InvalidFields = "INV_FIELD";

    /// <summary>One or more required fields are missing.</summary>
    public const string MissingRequired = "REQ_MISS";

    /// <summary>Field value does not match the expected format.</summary>
    public const string InvalidFormat = "INV_FMT";
}

/// <summary>
/// File status constants matching the nt_file_stat lookup table.
/// Used in FileStatusResponse.StatusId and FileLoadResponse.StatusId fields.
/// </summary>
public static class FileStatus
{
    /// <summary>Initial loading in progress (status 1).</summary>
    public const int InitialLoading = 1;

    /// <summary>Transactions loaded successfully (status 2).</summary>
    public const int TransactionsLoaded = 2;

    /// <summary>Processing completed with errors (status 3).</summary>
    public const int ProcessingErrors = 3;

    /// <summary>Processing completed successfully (status 4).</summary>
    public const int ProcessingCompleted = 4;

    /// <summary>File has been discarded/rejected (status 5).</summary>
    public const int FileDiscarded = 5;

    /// <summary>Output file generation in progress (status 10).</summary>
    public const int FileGenerationInProgress = 10;

    /// <summary>Output file generation complete (status 11).</summary>
    public const int FileGenerationComplete = 11;

    /// <summary>Response received from downstream system with some errors (status 12).</summary>
    public const int ResponseSomeErrors = 12;

    /// <summary>Response received from downstream system with no errors (status 13).</summary>
    public const int ResponseNoErrors = 13;

    /// <summary>Get the human-readable description for a status ID.</summary>
    /// <param name="statusId">The numeric status ID.</param>
    /// <returns>Human-readable status description.</returns>
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
