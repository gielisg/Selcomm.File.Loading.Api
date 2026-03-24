using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Selcomm.Data.Common;
using FileLoading.Data;
using FileLoading.Models;
using FileLoading.Validation;

namespace FileLoading.Repositories;

/// <summary>
/// Repository implementation for FileLoader database operations.
/// Uses V4 stored procedures: sp_file_loading_nt_file_api, ss_file_loading_nt_file_api, su_file_loading_nt_file_api.
/// Inserts records into: cl_detail, ntfl_chgdtl, nt_cl_not_load.
/// </summary>
public class FileLoaderRepository : IFileLoaderRepository
{
    private readonly FileLoaderDbContext _dbContext;
    private readonly ILogger<FileLoaderRepository> _logger;

    private const int BatchSize = 1000;

    public FileLoaderRepository(FileLoaderDbContext dbContext, ILogger<FileLoaderRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<RawCommandResult> AuthoriseAsync(SecurityContext context, string entityType, string entityId)
    {
        _logger.LogDebug("Calling sf_authorise: User={User}, Operation={Op}",
            context.UserCode, context.OperationId);

        return _dbContext.ExecuteRawCommand(
            "EXECUTE PROCEDURE sf_authorise(?, ?, ?)",
            ("@p1", context.UserCode, DbType.String, 64),
            ("@p2", context.RoleNarr, DbType.String, 64),
            ("@p3", context.OperationId, DbType.String, 254)
        );
    }

    public async Task<ValueResult<NtFileCreateResult>> CreateNtFileAsync(
        string fileTypeCode,
        string ntCustNum,
        string ntFileName,
        int statusId,
        DateTime? ntFileDate,
        SecurityContext securityContext)
    {
        _logger.LogDebug("Creating nt_file: Type={FileType}, Cust={CustNum}, Name={FileName}",
            fileTypeCode, ntCustNum, ntFileName);

        // Call sp_file_loading_nt_file_api via standard ExecuteValueQuery pattern
        // Returns: StatusCode (201), Id (nt_file_num), ErrorCode, ErrorMessage
        var spResult = await _dbContext.ExecuteValueQueryAsync<int>(
            "sp_file_loading_nt_file_api",
            securityContext,
            ("@p_file_type_code", fileTypeCode, DbType.String, 10),
            ("@p_nt_cust_num", ntCustNum, DbType.String, 10),
            ("@p_nt_file_name", ntFileName, DbType.String, 80),
            ("@p_status_id", statusId, DbType.Int32, null),
            ("@p_nt_file_date", ntFileDate ?? DateTime.Today, DbType.Date, null)
        );

        if (!spResult.IsSuccess)
        {
            return new ValueResult<NtFileCreateResult>
            {
                StatusCode = spResult.StatusCode,
                ErrorCode = spResult.ErrorCode,
                ErrorMessage = spResult.ErrorMessage
            };
        }

        return new ValueResult<NtFileCreateResult>
        {
            StatusCode = 201,
            Value = new NtFileCreateResult
            {
                NtFileNum = spResult.Value,
                NtFileName = ntFileName
            }
        };
    }

    public async Task<StoredProcedureResult> UpdateFileStatusAsync(
        int ntFileNum,
        int statusId,
        SecurityContext securityContext)
    {
        _logger.LogDebug("Updating file {NtFileNum} status to {StatusId}", ntFileNum, statusId);

        // Call su_file_loading_nt_file_api via standard ExecuteCommand pattern
        // Returns: StatusCode (200), ErrorCode, ErrorMessage
        return await _dbContext.ExecuteCommandAsync(
            "su_file_loading_nt_file_api",
            securityContext,
            ("@p_nt_file_num", ntFileNum, DbType.Int32, null),
            ("@p_status_id", statusId, DbType.Int32, null)
        );
    }

    public async Task<DataResult<FileStatusResponse>> GetFileStatusAsync(
        int ntFileNum,
        SecurityContext securityContext)
    {
        _logger.LogDebug("Getting file status for {NtFileNum}", ntFileNum);

        var sql = @"
            SELECT
                f.nt_file_num, f.file_type_code, f.nt_cust_num, f.nt_file_name,
                f.nt_file_date, f.nt_file_seq, f.status_id,
                ft.file_type_narr, ft.file_class_code,
                s.status_narr,
                t.nt_tot_rec, t.nt_tot_cost, t.nt_earliest_call, t.nt_latest_call,
                f.created_tm, f.created_by, f.last_updated, f.updated_by
            FROM nt_file f
            JOIN file_type ft ON f.file_type_code = ft.file_type_code
            JOIN nt_file_stat s ON f.status_id = s.status_id
            LEFT OUTER JOIN nt_fl_trailer t ON f.nt_file_num = t.nt_file_num
            WHERE f.nt_file_num = ?";

        var result = _dbContext.ExecuteRawQuery<FileStatusResponse>(
            sql,
            reader => new FileStatusResponse
            {
                NtFileNum = reader.GetInt32(0),
                FileType = reader.GetString(1).Trim(),
                NtCustNum = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim(),
                FileName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim(),
                NtFileDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                NtFileSeq = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                StatusId = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                Status = reader.IsDBNull(9) ? string.Empty : reader.GetString(9).Trim(),
                TotalRecords = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                TotalCost = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                EarliestCall = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                LatestCall = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
                CreatedTm = reader.IsDBNull(14) ? null : reader.GetDateTime(14)
            },
            ("@p1", ntFileNum, DbType.Int32, null)
        );

        if (!result.IsSuccess)
        {
            return new DataResult<FileStatusResponse>
            {
                StatusCode = result.StatusCode,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };
        }

        if (result.Data.Count == 0)
        {
            return new DataResult<FileStatusResponse>
            {
                StatusCode = 404,
                ErrorCode = "FileLoading.FileNotFound",
                ErrorMessage = $"File {ntFileNum} not found"
            };
        }

        return new DataResult<FileStatusResponse>
        {
            StatusCode = 200,
            Data = result.Data[0]
        };
    }

    public async Task<DataResult<FileListResponse>> ListFilesAsync(
        string? fileTypeCode,
        string? ntCustNum,
        int skipRecords,
        int takeRecords,
        string countRecords,
        SecurityContext securityContext)
    {
        _logger.LogDebug("Listing files: Type={FileType}, Cust={CustNum}, Skip={Skip}, Take={Take}, Count={Count}",
            fileTypeCode, ntCustNum, skipRecords, takeRecords, countRecords);

        // Use ss_file_loading_nt_file_api via standard ExecuteJsonQueryAsync pattern
        // Returns: StatusCode, Json ({"Count": N|null, "Items": [...]}), ErrorCode, ErrorMessage
        var jsonResult = await _dbContext.ExecuteJsonQueryAsync(
            "ss_file_loading_nt_file_api",
            securityContext,
            ("@p_file_type_code", (object?)fileTypeCode ?? DBNull.Value, DbType.String, 10),
            ("@p_nt_cust_num", (object?)ntCustNum ?? DBNull.Value, DbType.String, 10),
            ("@p_skip_records", skipRecords, DbType.Int32, null),
            ("@p_take_records", takeRecords, DbType.Int32, null),
            ("@p_count_records", countRecords, DbType.StringFixedLength, 1)
        );

        if (!jsonResult.IsSuccess || string.IsNullOrEmpty(jsonResult.Json))
        {
            return new DataResult<FileListResponse>
            {
                StatusCode = jsonResult.StatusCode == 204 ? 204 : jsonResult.StatusCode,
                ErrorCode = jsonResult.ErrorCode,
                ErrorMessage = jsonResult.ErrorMessage
            };
        }

        var jsonDoc = JsonSerializer.Deserialize<JsonElement>(jsonResult.Json);

        int? count = null;
        if (jsonDoc.TryGetProperty("Count", out var countProp) && countProp.ValueKind != JsonValueKind.Null)
        {
            count = countProp.GetInt32();
        }

        var itemsArray = jsonDoc.GetProperty("Items");

        var items = new List<FileStatusResponse>();
        foreach (var item in itemsArray.EnumerateArray())
        {
            items.Add(new FileStatusResponse
            {
                NtFileNum = item.TryGetProperty("NtFileNum", out var v1) ? v1.GetInt32() : 0,
                FileType = item.TryGetProperty("FileTypeCode", out var v2) ? v2.GetString()?.Trim() ?? string.Empty : string.Empty,
                NtCustNum = item.TryGetProperty("NtCustNum", out var v3) ? v3.GetString()?.Trim() ?? string.Empty : string.Empty,
                FileName = item.TryGetProperty("NtFileName", out var v4) ? v4.GetString()?.Trim() ?? string.Empty : string.Empty,
                NtFileDate = item.TryGetProperty("NtFileDate", out var v5) && v5.ValueKind != JsonValueKind.Null ? DateTime.Parse(v5.GetString()!) : null,
                NtFileSeq = item.TryGetProperty("NtFileSeq", out var v6) ? v6.GetInt32() : 0,
                StatusId = item.TryGetProperty("StatusId", out var v7) ? v7.GetInt32() : 0,
                Status = item.TryGetProperty("StatusNarr", out var v8) ? v8.GetString()?.Trim() ?? string.Empty : string.Empty,
                TotalRecords = item.TryGetProperty("NtTotRec", out var v9) && v9.ValueKind != JsonValueKind.Null ? v9.GetInt32() : null,
                TotalCost = item.TryGetProperty("NtTotCost", out var v10) && v10.ValueKind != JsonValueKind.Null ? v10.GetDecimal() : null,
                CreatedTm = item.TryGetProperty("CreatedTm", out var v11) && v11.ValueKind != JsonValueKind.Null ? DateTime.Parse(v11.GetString()!) : null
            });
        }

        return new DataResult<FileListResponse>
        {
            StatusCode = 200,
            Data = new FileListResponse
            {
                Items = items,
                Count = count
            }
        };
    }

    public async Task<RawCommandResult> InsertClDetailBatchAsync(IEnumerable<ClDetailRecord> records)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0)
            return new RawCommandResult { RowsAffected = 0 };

        _logger.LogDebug("Inserting {Count} cl_detail records", recordList.Count);

        var totalRows = 0;

        foreach (var batch in recordList.Chunk(BatchSize))
        {
            foreach (var record in batch)
            {
                var sql = @"INSERT INTO cl_detail (
                    nt_file_num, nt_file_rec_num, sp_cn_ref, sp_plan_ref,
                    num_called, tar_class_code, cl_start_dt, unit, unitquantity,
                    cl_duration, nt_cost, nt_cost_ex, nt_cost_tax,
                    rtl_non_disc_ex, rtl_non_disc_tax, rtl_disc_ex, rtl_disc_tax,
                    timebandcode, bparty_destn, cl_status, cl_dt_tabcd, process_ref
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

                var result = _dbContext.ExecuteRawCommand(sql,
                    ("@p1", record.NtFileNum, DbType.Int32, null),
                    ("@p2", record.NtFileRecNum, DbType.Int32, null),
                    ("@p3", record.SpCnRef, DbType.Int32, null),
                    ("@p4", record.SpPlanRef, DbType.Int32, null),
                    ("@p5", record.NumCalled, DbType.String, 64),
                    ("@p6", record.TarClassCode, DbType.Int16, null),
                    ("@p7", record.ClStartDt, DbType.DateTime, null),
                    ("@p8", record.Unit, DbType.String, 1),
                    ("@p9", record.UnitQuantity, DbType.Decimal, null),
                    ("@p10", record.ClDuration?.ToString(), DbType.String, 20),
                    ("@p11", record.NtCost, DbType.Decimal, null),
                    ("@p12", record.NtCostEx, DbType.Decimal, null),
                    ("@p13", record.NtCostTax, DbType.Decimal, null),
                    ("@p14", record.RtlNonDiscEx, DbType.Decimal, null),
                    ("@p15", record.RtlNonDiscTax, DbType.Decimal, null),
                    ("@p16", record.RtlDiscEx, DbType.Decimal, null),
                    ("@p17", record.RtlDiscTax, DbType.Decimal, null),
                    ("@p18", record.TimebandCode, DbType.String, 4),
                    ("@p19", record.BpartyDestn, DbType.String, 32),
                    ("@p20", record.ClStatus, DbType.Int16, null),
                    ("@p21", record.ClDtTabcd, DbType.String, 2),
                    ("@p22", record.ProcessRef, DbType.Int32, null)
                );

                if (!result.IsSuccess)
                {
                    _logger.LogError("Failed to insert cl_detail: {Error}", result.ErrorMessage);
                    return result;
                }
                totalRows += result.RowsAffected;
            }
        }

        return new RawCommandResult { RowsAffected = totalRows };
    }

    /// <summary>
    /// Optimized batch insert for cl_detail records using transaction batching.
    /// Uses generic DbConnection transactions for cross-database support (Informix, PostgreSQL).
    /// Wraps each batch in an explicit transaction to reduce commit overhead.
    /// </summary>
    public async Task<RawCommandResult> InsertClDetailBatchOptimizedAsync(IEnumerable<ClDetailRecord> records, int transactionBatchSize = 1000)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0)
            return new RawCommandResult { RowsAffected = 0 };

        _logger.LogDebug("Inserting {Count} cl_detail records with transaction batching (batch size: {BatchSize})",
            recordList.Count, transactionBatchSize);

        var totalRows = 0;
        var connection = _dbContext.GetConnection();

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        foreach (var batch in recordList.Chunk(transactionBatchSize))
        {
            // Begin transaction for this batch using generic DbConnection
            DbTransaction? transaction = null;
            try
            {
                transaction = await connection.BeginTransactionAsync();

                foreach (var record in batch)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO cl_detail (
                        nt_file_num, nt_file_rec_num, sp_cn_ref, sp_plan_ref,
                        num_called, tar_class_code, cl_start_dt, unit, unitquantity,
                        cl_duration, nt_cost, nt_cost_ex, nt_cost_tax,
                        rtl_non_disc_ex, rtl_non_disc_tax, rtl_disc_ex, rtl_disc_tax,
                        timebandcode, bparty_destn, cl_status, cl_dt_tabcd, process_ref
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
                    command.CommandType = CommandType.Text;

                    AddParameter(command, "@p1", record.NtFileNum, DbType.Int32);
                    AddParameter(command, "@p2", record.NtFileRecNum, DbType.Int32);
                    AddParameter(command, "@p3", record.SpCnRef, DbType.Int32);
                    AddParameter(command, "@p4", record.SpPlanRef, DbType.Int32);
                    AddParameter(command, "@p5", record.NumCalled, DbType.String, 64);
                    AddParameter(command, "@p6", record.TarClassCode, DbType.Int16);
                    AddParameter(command, "@p7", record.ClStartDt, DbType.DateTime);
                    AddParameter(command, "@p8", record.Unit, DbType.String, 1);
                    AddParameter(command, "@p9", record.UnitQuantity, DbType.Decimal);
                    AddParameter(command, "@p10", record.ClDuration?.ToString(), DbType.String, 20);
                    AddParameter(command, "@p11", record.NtCost, DbType.Decimal);
                    AddParameter(command, "@p12", record.NtCostEx, DbType.Decimal);
                    AddParameter(command, "@p13", record.NtCostTax, DbType.Decimal);
                    AddParameter(command, "@p14", record.RtlNonDiscEx, DbType.Decimal);
                    AddParameter(command, "@p15", record.RtlNonDiscTax, DbType.Decimal);
                    AddParameter(command, "@p16", record.RtlDiscEx, DbType.Decimal);
                    AddParameter(command, "@p17", record.RtlDiscTax, DbType.Decimal);
                    AddParameter(command, "@p18", record.TimebandCode, DbType.String, 4);
                    AddParameter(command, "@p19", record.BpartyDestn, DbType.String, 32);
                    AddParameter(command, "@p20", record.ClStatus, DbType.Int16);
                    AddParameter(command, "@p21", record.ClDtTabcd, DbType.String, 2);
                    AddParameter(command, "@p22", record.ProcessRef, DbType.Int32);

                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected < 0)
                    {
                        _logger.LogError("Failed to insert cl_detail record");
                        await transaction.RollbackAsync();
                        return new RawCommandResult { RowsAffected = 0, ErrorMessage = "Insert failed" };
                    }
                    totalRows += rowsAffected;
                }

                // Commit transaction for this batch
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during batch insert, rolling back");
                if (transaction != null)
                    await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        _logger.LogDebug("Completed inserting {TotalRows} cl_detail records", totalRows);
        return new RawCommandResult { RowsAffected = totalRows };
    }

    /// <summary>
    /// Helper method to add a parameter to a DbCommand.
    /// </summary>
    private static void AddParameter(DbCommand command, string name, object? value, DbType dbType, int? size = null)
    {
        var param = command.CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        param.DbType = dbType;
        if (size.HasValue)
            param.Size = size.Value;
        command.Parameters.Add(param);
    }

    public async Task<RawCommandResult> InsertChargeBatchAsync(IEnumerable<NtflChgdtlRecord> records)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0)
            return new RawCommandResult { RowsAffected = 0 };

        _logger.LogDebug("Inserting {Count} ntfl_chgdtl records", recordList.Count);

        var totalRows = 0;

        foreach (var record in recordList)
        {
            var sql = @"INSERT INTO ntfl_chgdtl (
                nt_file_num, nt_file_rec_num, status_id, phone_num,
                sp_cn_ref, sp_plan_ref, chg_code, start_date, end_date,
                cost_amount, cost_gst, unitquantity, chg_narr, ch_dt_tabcd
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

            var result = _dbContext.ExecuteRawCommand(sql,
                ("@p1", record.NtFileNum, DbType.Int32, null),
                ("@p2", record.NtFileRecNum, DbType.Int32, null),
                ("@p3", record.StatusId ?? 1, DbType.Int32, null),
                ("@p4", record.PhoneNum, DbType.String, 32),
                ("@p5", record.SpCnRef, DbType.Int32, null),
                ("@p6", record.SpPlanRef, DbType.Int32, null),
                ("@p7", record.ChgCode, DbType.String, 4),
                ("@p8", record.StartDate, DbType.DateTime, null),
                ("@p9", record.EndDate, DbType.DateTime, null),
                ("@p10", record.CostAmount, DbType.Decimal, null),
                ("@p11", record.CostGst, DbType.Decimal, null),
                ("@p12", record.UnitQuantity, DbType.Decimal, null),
                ("@p13", record.ChgNarr, DbType.String, 64),
                ("@p14", record.ChDtTabcd, DbType.String, 4)
            );

            if (!result.IsSuccess)
            {
                _logger.LogError("Failed to insert ntfl_chgdtl: {Error}", result.ErrorMessage);
                return result;
            }
            totalRows += result.RowsAffected;
        }

        return new RawCommandResult { RowsAffected = totalRows };
    }

    /// <summary>
    /// Optimized batch insert for charge records using transaction batching.
    /// Uses generic DbConnection transactions for cross-database support (Informix, PostgreSQL).
    /// Wraps each batch in an explicit transaction to reduce commit overhead.
    /// </summary>
    public async Task<RawCommandResult> InsertChargeBatchOptimizedAsync(IEnumerable<NtflChgdtlRecord> records, int transactionBatchSize = 1000)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0)
            return new RawCommandResult { RowsAffected = 0 };

        _logger.LogDebug("Inserting {Count} ntfl_chgdtl records with transaction batching (batch size: {BatchSize})",
            recordList.Count, transactionBatchSize);

        var totalRows = 0;
        var connection = _dbContext.GetConnection();

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        foreach (var batch in recordList.Chunk(transactionBatchSize))
        {
            // Begin transaction for this batch using generic DbConnection
            DbTransaction? transaction = null;
            try
            {
                transaction = await connection.BeginTransactionAsync();

                foreach (var record in batch)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO ntfl_chgdtl (
                        nt_file_num, nt_file_rec_num, status_id, phone_num,
                        sp_cn_ref, sp_plan_ref, chg_code, start_date, end_date,
                        cost_amount, cost_gst, unitquantity, chg_narr, ch_dt_tabcd
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
                    command.CommandType = CommandType.Text;

                    AddParameter(command, "@p1", record.NtFileNum, DbType.Int32);
                    AddParameter(command, "@p2", record.NtFileRecNum, DbType.Int32);
                    AddParameter(command, "@p3", record.StatusId ?? 1, DbType.Int32);
                    AddParameter(command, "@p4", record.PhoneNum, DbType.String, 32);
                    AddParameter(command, "@p5", record.SpCnRef, DbType.Int32);
                    AddParameter(command, "@p6", record.SpPlanRef, DbType.Int32);
                    AddParameter(command, "@p7", record.ChgCode, DbType.String, 4);
                    AddParameter(command, "@p8", record.StartDate, DbType.DateTime);
                    AddParameter(command, "@p9", record.EndDate, DbType.DateTime);
                    AddParameter(command, "@p10", record.CostAmount, DbType.Decimal);
                    AddParameter(command, "@p11", record.CostGst, DbType.Decimal);
                    AddParameter(command, "@p12", record.UnitQuantity, DbType.Decimal);
                    AddParameter(command, "@p13", record.ChgNarr, DbType.String, 64);
                    AddParameter(command, "@p14", record.ChDtTabcd, DbType.String, 4);

                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected < 0)
                    {
                        _logger.LogError("Failed to insert ntfl_chgdtl record");
                        await transaction.RollbackAsync();
                        return new RawCommandResult { RowsAffected = 0, ErrorMessage = "Insert failed" };
                    }
                    totalRows += rowsAffected;
                }

                // Commit transaction for this batch
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during batch insert, rolling back");
                if (transaction != null)
                    await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        _logger.LogDebug("Completed inserting {TotalRows} ntfl_chgdtl records", totalRows);
        return new RawCommandResult { RowsAffected = totalRows };
    }

    /// <summary>
    /// Batch insert for ssswhls_cdr sub-type records using transaction batching.
    /// </summary>
    public async Task<RawCommandResult> InsertSssWhlsCdrBatchAsync(IEnumerable<SssWhlsCdrRecord> records, int transactionBatchSize = 1000)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0)
            return new RawCommandResult { RowsAffected = 0 };

        _logger.LogDebug("Inserting {Count} ssswhls_cdr records with transaction batching (batch size: {BatchSize})",
            recordList.Count, transactionBatchSize);

        var totalRows = 0;
        var connection = _dbContext.GetConnection();

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        foreach (var batch in recordList.Chunk(transactionBatchSize))
        {
            DbTransaction? transaction = null;
            try
            {
                transaction = await connection.BeginTransactionAsync();

                foreach (var record in batch)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO ssswhls_cdr (
                        nt_file_num, nt_file_rec_num, file_version, record_number,
                        transaction_type, originating_srvc, terminating_srvc,
                        cl_start_dt, cl_start_dt_utc, unitquantity, uom,
                        nt_cost_ex, nt_cost_tax, time_code, call_destination,
                        orig_sp_cn_ref, orig_sp_chgdtl_ref, rating_band, rating_plan,
                        rating_category, currency_code, imsi, imei, cell_id, carrier,
                        batch_number, nt_disc_cost_ex, nt_disc_cost_tax,
                        service_type, original_network, content_type
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
                    command.CommandType = CommandType.Text;

                    AddParameter(command, "@p1", record.NtFileNum, DbType.Int32);
                    AddParameter(command, "@p2", record.NtFileRecNum, DbType.Int32);
                    AddParameter(command, "@p3", record.FileVersion, DbType.Int32);
                    AddParameter(command, "@p4", record.RecordNumber, DbType.String, 20);
                    AddParameter(command, "@p5", record.TransactionType, DbType.String, 1);
                    AddParameter(command, "@p6", record.OriginatingSrvc, DbType.String, 64);
                    AddParameter(command, "@p7", record.TerminatingSrvc, DbType.String, 64);
                    AddParameter(command, "@p8", record.ClStartDt, DbType.DateTime);
                    AddParameter(command, "@p9", record.ClStartDtUtc, DbType.DateTime);
                    AddParameter(command, "@p10", record.UnitQuantity, DbType.Decimal);
                    AddParameter(command, "@p11", record.Uom, DbType.String, 4);
                    AddParameter(command, "@p12", record.NtCostEx, DbType.Decimal);
                    AddParameter(command, "@p13", record.NtCostTax, DbType.Decimal);
                    AddParameter(command, "@p14", record.TimeCode, DbType.String, 16);
                    AddParameter(command, "@p15", record.CallDestination, DbType.String, 64);
                    AddParameter(command, "@p16", record.OrigSpCnRef, DbType.String, 32);
                    AddParameter(command, "@p17", record.OrigSpChgdtlRef, DbType.String, 32);
                    AddParameter(command, "@p18", record.RatingBand, DbType.String, 32);
                    AddParameter(command, "@p19", record.RatingPlan, DbType.String, 32);
                    AddParameter(command, "@p20", record.RatingCategory, DbType.String, 32);
                    AddParameter(command, "@p21", record.CurrencyCode, DbType.String, 4);
                    AddParameter(command, "@p22", record.Imsi, DbType.String, 32);
                    AddParameter(command, "@p23", record.Imei, DbType.String, 32);
                    AddParameter(command, "@p24", record.CellId, DbType.String, 32);
                    AddParameter(command, "@p25", record.Carrier, DbType.String, 32);
                    AddParameter(command, "@p26", record.BatchNumber, DbType.String, 32);
                    AddParameter(command, "@p27", record.NtDiscCostEx, DbType.Decimal);
                    AddParameter(command, "@p28", record.NtDiscCostTax, DbType.Decimal);
                    AddParameter(command, "@p29", record.ServiceType, DbType.String, 32);
                    AddParameter(command, "@p30", record.OriginalNetwork, DbType.String, 32);
                    AddParameter(command, "@p31", record.ContentType, DbType.String, 32);

                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected < 0)
                    {
                        _logger.LogError("Failed to insert ssswhls_cdr record");
                        await transaction.RollbackAsync();
                        return new RawCommandResult { RowsAffected = 0, ErrorMessage = "Insert failed" };
                    }
                    totalRows += rowsAffected;
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during ssswhls_cdr batch insert, rolling back");
                if (transaction != null)
                    await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        _logger.LogDebug("Completed inserting {TotalRows} ssswhls_cdr records", totalRows);
        return new RawCommandResult { RowsAffected = totalRows };
    }

    /// <summary>
    /// Batch insert for ssswhlschg sub-type records using transaction batching.
    /// </summary>
    public async Task<RawCommandResult> InsertSssWhlsChgBatchAsync(IEnumerable<SssWhlsChgRecord> records, int transactionBatchSize = 1000)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0)
            return new RawCommandResult { RowsAffected = 0 };

        _logger.LogDebug("Inserting {Count} ssswhlschg records with transaction batching (batch size: {BatchSize})",
            recordList.Count, transactionBatchSize);

        var totalRows = 0;
        var connection = _dbContext.GetConnection();

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        foreach (var batch in recordList.Chunk(transactionBatchSize))
        {
            DbTransaction? transaction = null;
            try
            {
                transaction = await connection.BeginTransactionAsync();

                foreach (var record in batch)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO ssswhlschg (
                        nt_file_num, nt_file_rec_num, file_version, record_number,
                        service_no, charge_code, charge_narr,
                        charge_start_dt, charge_end_dt, charge_amt_ex, charge_amt_tax,
                        orig_sp_cn_ref, orig_sp_chgdtl_ref, currency_code,
                        unit_quantity, charge_category
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
                    command.CommandType = CommandType.Text;

                    AddParameter(command, "@p1", record.NtFileNum, DbType.Int32);
                    AddParameter(command, "@p2", record.NtFileRecNum, DbType.Int32);
                    AddParameter(command, "@p3", record.FileVersion, DbType.Int32);
                    AddParameter(command, "@p4", record.RecordNumber, DbType.String, 20);
                    AddParameter(command, "@p5", record.ServiceNo, DbType.String, 64);
                    AddParameter(command, "@p6", record.ChargeCode, DbType.String, 16);
                    AddParameter(command, "@p7", record.ChargeNarr, DbType.String, 128);
                    AddParameter(command, "@p8", record.ChargeStartDt, DbType.DateTime);
                    AddParameter(command, "@p9", record.ChargeEndDt, DbType.DateTime);
                    AddParameter(command, "@p10", record.ChargeAmtEx, DbType.Decimal);
                    AddParameter(command, "@p11", record.ChargeAmtTax, DbType.Decimal);
                    AddParameter(command, "@p12", record.OrigSpCnRef, DbType.String, 32);
                    AddParameter(command, "@p13", record.OrigSpChgdtlRef, DbType.String, 32);
                    AddParameter(command, "@p14", record.CurrencyCode, DbType.String, 4);
                    AddParameter(command, "@p15", record.UnitQuantity, DbType.Decimal);
                    AddParameter(command, "@p16", record.ChargeCategory, DbType.String, 32);

                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected < 0)
                    {
                        _logger.LogError("Failed to insert ssswhlschg record");
                        await transaction.RollbackAsync();
                        return new RawCommandResult { RowsAffected = 0, ErrorMessage = "Insert failed" };
                    }
                    totalRows += rowsAffected;
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during ssswhlschg batch insert, rolling back");
                if (transaction != null)
                    await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        _logger.LogDebug("Completed inserting {TotalRows} ssswhlschg records", totalRows);
        return new RawCommandResult { RowsAffected = totalRows };
    }

    public async Task<RawCommandResult> InsertNotLoadRecordAsync(NtClNotLoadRecord record)
    {
        _logger.LogDebug("Inserting nt_cl_not_load: File={NtFileNum}, Rec={RecNum}, Err={ErrCode}",
            record.NtFileNum, record.NtFileRecNum, record.ErrCode);

        var sql = @"INSERT INTO nt_cl_not_load (
            nt_file_num, nt_file_rec_num, phone_num, cl_start_dt, num_called,
            unit, unitquantity, nt_cost, err_code, status_id, status_desc
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

        return _dbContext.ExecuteRawCommand(sql,
            ("@p1", record.NtFileNum, DbType.Int32, null),
            ("@p2", record.NtFileRecNum, DbType.Int32, null),
            ("@p3", record.PhoneNum, DbType.String, 32),
            ("@p4", record.ClStartDt, DbType.DateTime, null),
            ("@p5", record.NumCalled, DbType.String, 64),
            ("@p6", record.Unit, DbType.String, 1),
            ("@p7", record.UnitQuantity, DbType.Decimal, null),
            ("@p8", record.NtCost, DbType.Decimal, null),
            ("@p9", record.ErrCode, DbType.String, 3),
            ("@p10", record.StatusId, DbType.String, 4),
            ("@p11", record.StatusDesc, DbType.String, 64)
        );
    }

    public async Task<RawCommandResult> UpdateTrailerAsync(
        int ntFileNum,
        int totalRecords,
        decimal totalCost,
        DateTime? earliestCall,
        DateTime? latestCall)
    {
        _logger.LogDebug("Updating trailer for file {NtFileNum}: Records={Records}, Cost={Cost}",
            ntFileNum, totalRecords, totalCost);

        var sql = @"UPDATE nt_fl_trailer SET
            nt_tot_rec = ?,
            nt_tot_cost = ?,
            nt_earliest_call = ?,
            nt_latest_call = ?,
            nt_proc_date = TODAY,
            nt_proc_time = CURRENT
        WHERE nt_file_num = ?";

        return _dbContext.ExecuteRawCommand(sql,
            ("@p1", totalRecords, DbType.Int32, null),
            ("@p2", totalCost, DbType.Decimal, null),
            ("@p3", earliestCall, DbType.Date, null),
            ("@p4", latestCall, DbType.Date, null),
            ("@p5", ntFileNum, DbType.Int32, null)
        );
    }

    public async Task<DataResult<FileTypeListResponse>> GetFileTypesAsync(SecurityContext securityContext)
    {
        var sql = @"SELECT ft.file_type_code, ft.file_class_code, ft.file_type_narr,
                           fc.file_class_narr, ft.network_id, n.network_narr
                    FROM file_type ft
                    LEFT OUTER JOIN file_class fc ON ft.file_class_code = fc.file_class_code
                    LEFT OUTER JOIN networks n ON ft.network_id = n.network_id
                    ORDER BY ft.file_type_code";

        var result = _dbContext.ExecuteRawQuery<FileTypeInfo>(
            sql,
            reader => new FileTypeInfo
            {
                FileTypeCode = reader.GetString(0).Trim(),
                FileClassCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim(),
                FileType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim(),
                FileClass = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim(),
                NetworkId = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                Network = reader.IsDBNull(5) ? null : reader.GetString(5).Trim()
            }
        );

        if (!result.IsSuccess)
        {
            return new DataResult<FileTypeListResponse>
            {
                StatusCode = result.StatusCode,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };
        }

        return new DataResult<FileTypeListResponse>
        {
            StatusCode = 200,
            Data = new FileTypeListResponse { Items = result.Data }
        };
    }

    public async Task<int> GetNextRecordNumberAsync(int ntFileNum)
    {
        var result = _dbContext.ExecuteRawScalar<int>(
            "SELECT NVL(nt_tot_rec, 0) + 1 FROM nt_fl_trailer WHERE nt_file_num = ?",
            ("@p1", ntFileNum, DbType.Int32, null)
        );

        return result.IsSuccess ? result.Value : 1;
    }

    public async Task<RawCommandResult> InsertErrorLogBatchAsync(IEnumerable<NtflErrorLogRecord> errors)
    {
        var errorList = errors.ToList();
        if (errorList.Count == 0)
            return new RawCommandResult { RowsAffected = 0 };

        _logger.LogDebug("Inserting {Count} error log records", errorList.Count);

        var totalRows = 0;

        foreach (var error in errorList)
        {
            var sql = @"INSERT INTO ntfl_error_log (
                nt_file_num, error_seq, error_code, error_message,
                line_number, raw_data, created_dt
            ) VALUES (?, ?, ?, ?, ?, ?, CURRENT)";

            var result = _dbContext.ExecuteRawCommand(sql,
                ("@p1", error.NtFileNum, DbType.Int32, null),
                ("@p2", error.ErrorSeq, DbType.Int32, null),
                ("@p3", error.ErrorCode, DbType.String, 10),
                ("@p4", error.ErrorMessage, DbType.String, 256),
                ("@p5", error.LineNumber, DbType.Int32, null),
                ("@p6", error.RawData?.Length > 500 ? error.RawData.Substring(0, 500) : error.RawData, DbType.String, 500)
            );

            if (!result.IsSuccess)
            {
                _logger.LogError("Failed to insert error log: {Error}", result.ErrorMessage);
                return result;
            }
            totalRows += result.RowsAffected;
        }

        return new RawCommandResult { RowsAffected = totalRows };
    }

    public async Task<DataResult<List<NtflErrorLogRecord>>> GetErrorLogsAsync(int ntFileNum)
    {
        _logger.LogDebug("Getting error logs for file {NtFileNum}", ntFileNum);

        var sql = @"SELECT nt_file_num, error_seq, error_code, error_message,
                           line_number, raw_data, created_dt
                    FROM ntfl_error_log
                    WHERE nt_file_num = ?
                    ORDER BY error_seq";

        var result = _dbContext.ExecuteRawQuery<NtflErrorLogRecord>(
            sql,
            reader => new NtflErrorLogRecord
            {
                NtFileNum = reader.GetInt32(0),
                ErrorSeq = reader.GetInt32(1),
                ErrorCode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim(),
                ErrorMessage = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim(),
                LineNumber = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                RawData = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedDt = reader.IsDBNull(6) ? DateTime.Now : reader.GetDateTime(6)
            },
            ("@p1", ntFileNum, DbType.Int32, null)
        );

        if (!result.IsSuccess)
        {
            return new DataResult<List<NtflErrorLogRecord>>
            {
                StatusCode = result.StatusCode,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };
        }

        return new DataResult<List<NtflErrorLogRecord>>
        {
            StatusCode = 200,
            Data = result.Data
        };
    }

    /// <summary>
    /// Stores a validation summary for a file as JSON.
    /// The summary can be retrieved later by AI agents for conversational error explanations.
    /// </summary>
    public async Task<RawCommandResult> StoreValidationSummaryAsync(int ntFileNum, ValidationSummaryForAI summary)
    {
        _logger.LogDebug("Storing validation summary for file {NtFileNum}", ntFileNum);

        try
        {
            var summaryJson = JsonSerializer.Serialize(summary, new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                WriteIndented = false
            });

            // Check if record exists first
            var existsResult = _dbContext.ExecuteRawScalar<int>(
                "SELECT COUNT(*) FROM ntfl_validation_summary WHERE nt_file_num = ?",
                ("@p1", ntFileNum, DbType.Int32, null)
            );

            if (existsResult.IsSuccess && existsResult.Value > 0)
            {
                // Update existing
                var sql = @"UPDATE ntfl_validation_summary SET
                    summary_json = ?,
                    overall_status = ?,
                    total_errors = ?,
                    can_partially_process = ?,
                    last_updated = CURRENT, updated_by = ?
                WHERE nt_file_num = ?";

                return _dbContext.ExecuteRawCommand(sql,
                    ("@p1", summaryJson, DbType.String, null),
                    ("@p2", summary.OverallStatus?.Substring(0, Math.Min(summary.OverallStatus.Length, 256)) ?? string.Empty, DbType.String, 256),
                    ("@p3", summary.ErrorCountsByType.Values.Sum(), DbType.Int32, null),
                    ("@p4", summary.CanPartiallyProcess ? "Y" : "N", DbType.String, 1),
                    ("@p5", "SYSTEM", DbType.String, 18),
                    ("@p6", ntFileNum, DbType.Int32, null)
                );
            }
            else
            {
                // Insert new
                var sql = @"INSERT INTO ntfl_validation_summary (
                    nt_file_num, summary_json, overall_status, total_errors, can_partially_process,
                    created_tm, created_by, last_updated, updated_by
                ) VALUES (?, ?, ?, ?, ?, CURRENT, ?, CURRENT, ?)";

                return _dbContext.ExecuteRawCommand(sql,
                    ("@p1", ntFileNum, DbType.Int32, null),
                    ("@p2", summaryJson, DbType.String, null),
                    ("@p3", summary.OverallStatus?.Substring(0, Math.Min(summary.OverallStatus.Length, 256)) ?? string.Empty, DbType.String, 256),
                    ("@p4", summary.ErrorCountsByType.Values.Sum(), DbType.Int32, null),
                    ("@p5", summary.CanPartiallyProcess ? "Y" : "N", DbType.String, 1),
                    ("@p6", "SYSTEM", DbType.String, 18),
                    ("@p7", "SYSTEM", DbType.String, 18)
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing validation summary for file {NtFileNum}", ntFileNum);
            return new RawCommandResult
            {
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Retrieves a stored validation summary for a file.
    /// </summary>
    public async Task<DataResult<ValidationSummaryForAI?>> GetValidationSummaryAsync(int ntFileNum)
    {
        _logger.LogDebug("Getting validation summary for file {NtFileNum}", ntFileNum);

        try
        {
            var result = _dbContext.ExecuteRawQuery<string>(
                "SELECT summary_json FROM ntfl_validation_summary WHERE nt_file_num = ?",
                reader => reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                ("@p1", ntFileNum, DbType.Int32, null)
            );

            if (!result.IsSuccess)
            {
                return new DataResult<ValidationSummaryForAI?>
                {
                    StatusCode = result.StatusCode,
                    ErrorCode = result.ErrorCode,
                    ErrorMessage = result.ErrorMessage
                };
            }

            if (result.Data.Count == 0 || string.IsNullOrEmpty(result.Data[0]))
            {
                return new DataResult<ValidationSummaryForAI?>
                {
                    StatusCode = 404,
                    ErrorCode = "FileLoading.FileNotFound",
                    ErrorMessage = $"No validation summary found for file {ntFileNum}"
                };
            }

            var summary = JsonSerializer.Deserialize<ValidationSummaryForAI>(result.Data[0], new JsonSerializerOptions
            {
                PropertyNamingPolicy = null
            });

            return new DataResult<ValidationSummaryForAI?>
            {
                StatusCode = 200,
                Data = summary
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting validation summary for file {NtFileNum}", ntFileNum);
            return new DataResult<ValidationSummaryForAI?>
            {
                StatusCode = 500,
                ErrorCode = "FileLoading.DatabaseError",
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Inserts validation errors in batch using the AI-friendly error format.
    /// Maps ValidationError to ntfl_error_log table.
    /// </summary>
    public async Task<RawCommandResult> InsertValidationErrorsBatchAsync(int ntFileNum, IEnumerable<ValidationError> errors)
    {
        var errorList = errors.ToList();
        if (errorList.Count == 0)
            return new RawCommandResult { RowsAffected = 0 };

        _logger.LogDebug("Inserting {Count} validation errors for file {NtFileNum}", errorList.Count, ntFileNum);

        var totalRows = 0;
        var errorSeq = 1;

        foreach (var error in errorList)
        {
            // Build combined error message with AI-friendly details
            var errorMessage = error.UserMessage;
            if (!string.IsNullOrEmpty(error.Suggestion))
            {
                errorMessage += $" Suggestion: {error.Suggestion}";
            }

            var rawData = error.RawLine ?? error.RawValue;
            if (rawData != null && rawData.Length > 500)
            {
                rawData = rawData.Substring(0, 497) + "...";
            }

            var sql = @"INSERT INTO ntfl_error_log (
                nt_file_num, error_seq, error_code, error_message,
                line_number, raw_data, created_dt
            ) VALUES (?, ?, ?, ?, ?, ?, CURRENT)";

            var result = _dbContext.ExecuteRawCommand(sql,
                ("@p1", ntFileNum, DbType.Int32, null),
                ("@p2", errorSeq, DbType.Int32, null),
                ("@p3", error.ErrorCode?.Substring(0, Math.Min(error.ErrorCode.Length, 10)) ?? "UNKNOWN", DbType.String, 10),
                ("@p4", errorMessage?.Substring(0, Math.Min(errorMessage.Length, 256)) ?? "Validation error", DbType.String, 256),
                ("@p5", error.LineNumber, DbType.Int32, null),
                ("@p6", rawData, DbType.String, 500)
            );

            if (!result.IsSuccess)
            {
                _logger.LogError("Failed to insert validation error: {Error}", result.ErrorMessage);
                return result;
            }

            totalRows += result.RowsAffected;
            errorSeq++;
        }

        _logger.LogDebug("Inserted {TotalRows} validation errors for file {NtFileNum}", totalRows, ntFileNum);
        return new RawCommandResult { RowsAffected = totalRows };
    }

    // Internal helper classes
    private class SpNewNtFileResult
    {
        public int Status { get; set; }
        public int NtFileNum { get; set; }
        public string NtFileName { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }

    private class NtFileRecord
    {
        public string NtCustNum { get; set; } = string.Empty;
        public string FileTypeCode { get; set; } = string.Empty;
        public int NtFileSeq { get; set; }
        public string NtFileName { get; set; } = string.Empty;
        public DateTime NtFileDate { get; set; }
    }

    // ============================================
    // Process Tracking (Legacy Compatibility)
    // ============================================

    public async Task<ValueResult<int>> InsertProcessRecordAsync(int ntFileNum)
    {
        _logger.LogDebug("Inserting nt_fl_process for file {NtFileNum}", ntFileNum);

        try
        {
            var connection = _dbContext.GetConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO nt_fl_process (process_ref, nt_file_num, proc_start_dt)
                VALUES (0, ?, CURRENT YEAR TO SECOND)";
            command.CommandType = CommandType.Text;
            AddParameter(command, "@p1", ntFileNum, DbType.Int32);

            await command.ExecuteNonQueryAsync();

            // Retrieve the auto-generated serial process_ref
            using var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT DBINFO('sqlca.sqlerrd1') FROM systables WHERE tabid = 1";
            selectCmd.CommandType = CommandType.Text;
            var processRef = Convert.ToInt32(await selectCmd.ExecuteScalarAsync());

            _logger.LogDebug("Created nt_fl_process: process_ref={ProcessRef} for file {NtFileNum}", processRef, ntFileNum);

            return new ValueResult<int>
            {
                StatusCode = 201,
                Value = processRef
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting nt_fl_process for file {NtFileNum}", ntFileNum);
            return new ValueResult<int>
            {
                StatusCode = 500,
                ErrorCode = "FileLoading.DatabaseError",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<RawCommandResult> UpdateProcessRecordAsync(int processRef)
    {
        _logger.LogDebug("Updating nt_fl_process: process_ref={ProcessRef}", processRef);

        return _dbContext.ExecuteRawCommand(
            "UPDATE nt_fl_process SET proc_end_dt = CURRENT YEAR TO SECOND WHERE process_ref = ?",
            ("@p1", processRef, DbType.Int32, null)
        );
    }

    public async Task<RawCommandResult> InsertFileHeaderAsync(int ntFileNum, string ntCustNum, DateTime? earliestCall, DateTime? latestCall)
    {
        _logger.LogDebug("Inserting nt_fl_header for file {NtFileNum}", ntFileNum);

        var sql = @"INSERT INTO nt_fl_header (
            nt_file_num, nt_cust_num, nt_earliest_call, nt_latest_call,
            nt_proc_date, nt_proc_time, nt_batch_num, nt_tot_in_batch,
            nt_file_in_batch, nt_invoice_num, nt_inv_month
        ) VALUES (?, ?, ?, ?, TODAY, CURRENT HOUR TO SECOND, 0, 1, 1, NULL, 0)";

        return _dbContext.ExecuteRawCommand(sql,
            ("@p1", ntFileNum, DbType.Int32, null),
            ("@p2", ntCustNum, DbType.String, 10),
            ("@p3", earliestCall, DbType.Date, null),
            ("@p4", latestCall, DbType.Date, null)
        );
    }

    // ============================================
    // Transfer Source Configuration
    // ============================================

    public async Task<DataResult<List<TransferSourceConfig>>> GetTransferSourcesAsync()
    {
        _logger.LogDebug("Getting transfer sources");

        var sql = @"SELECT source_id, vendor_name, file_type_code, protocol, host, port,
                           remote_path, auth_type, username, password_encrypted,
                           certificate_path, private_key_path, file_name_pattern,
                           skip_file_pattern, delete_after_download, compress_on_archive,
                           compression_method, cron_schedule, is_enabled,
                           created_tm, created_by, last_updated, updated_by
                    FROM ntfl_transfer_source
                    ORDER BY source_id";

        var result = _dbContext.ExecuteRawQuery(
            sql,
            reader => new TransferSourceConfig
            {
                SourceId = reader.GetInt32(0),
                VendorName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim(),
                FileTypeCode = reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                Protocol = ParseProtocol(reader.IsDBNull(3) ? "SFTP" : reader.GetString(3).Trim()),
                Host = reader.IsDBNull(4) ? string.Empty : reader.GetString(4).Trim(),
                Port = reader.IsDBNull(5) ? 22 : reader.GetInt32(5),
                RemotePath = reader.IsDBNull(6) ? "/" : reader.GetString(6).Trim(),
                AuthType = ParseAuthType(reader.IsDBNull(7) ? "PASSWORD" : reader.GetString(7).Trim()),
                Username = reader.IsDBNull(8) ? string.Empty : reader.GetString(8).Trim(),
                Password = reader.IsDBNull(9) ? null : reader.GetString(9), // Encrypted
                CertificatePath = reader.IsDBNull(10) ? null : reader.GetString(10).Trim(),
                PrivateKeyPath = reader.IsDBNull(11) ? null : reader.GetString(11).Trim(),
                FileNamePattern = reader.IsDBNull(12) ? "*.*" : reader.GetString(12).Trim(),
                SkipFilePattern = reader.IsDBNull(13) ? null : reader.GetString(13).Trim(),
                DeleteAfterDownload = reader.IsDBNull(14) || reader.GetString(14).Trim().ToUpper() == "Y",
                CompressOnArchive = reader.IsDBNull(15) || reader.GetString(15).Trim().ToUpper() == "Y",
                Compression = ParseCompression(reader.IsDBNull(16) ? "GZIP" : reader.GetString(16).Trim()),
                CronSchedule = reader.IsDBNull(17) ? null : reader.GetString(17).Trim(),
                IsEnabled = reader.IsDBNull(18) || reader.GetString(18).Trim().ToUpper() == "Y",
                CreatedAt = reader.GetDateTime(19),
                CreatedBy = reader.GetString(20).Trim(),
                UpdatedAt = reader.GetDateTime(21),
                UpdatedBy = reader.GetString(22).Trim()
            }
        );

        return new DataResult<List<TransferSourceConfig>>
        {
            StatusCode = result.IsSuccess ? 200 : result.StatusCode,
            Data = result.Data,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    public async Task<DataResult<TransferSourceConfig>> GetTransferSourceAsync(int sourceId)
    {
        _logger.LogDebug("Getting transfer source: {SourceId}", sourceId);

        var result = await GetTransferSourcesAsync();
        if (!result.IsSuccess)
        {
            return new DataResult<TransferSourceConfig>
            {
                StatusCode = result.StatusCode,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };
        }

        var source = result.Data?.FirstOrDefault(s => s.SourceId == sourceId);
        if (source == null)
        {
            return new DataResult<TransferSourceConfig>
            {
                StatusCode = 404,
                ErrorCode = "FileLoading.TransferSourceNotFound",
                ErrorMessage = $"Transfer source '{sourceId}' not found"
            };
        }

        return new DataResult<TransferSourceConfig>
        {
            StatusCode = 200,
            Data = source
        };
    }

    public async Task<RawCommandResult> InsertTransferSourceAsync(TransferSourceConfig config)
    {
        _logger.LogDebug("Inserting transfer source: {VendorName}", config.VendorName);

        var sql = @"INSERT INTO ntfl_transfer_source (
            vendor_name, file_type_code, protocol, host, port,
            remote_path, auth_type, username, password_encrypted,
            certificate_path, private_key_path, file_name_pattern,
            skip_file_pattern, delete_after_download, compress_on_archive,
            compression_method, cron_schedule, is_enabled,
            created_tm, created_by, last_updated, updated_by
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, CURRENT, ?, CURRENT, ?)";

        return _dbContext.ExecuteRawCommand(sql,
            ("@p1", config.VendorName, DbType.String, 128),
            ("@p2", config.FileTypeCode, DbType.String, 10),
            ("@p3", config.Protocol.ToString().ToUpper(), DbType.String, 16),
            ("@p4", config.Host, DbType.String, 255),
            ("@p5", config.Port, DbType.Int32, null),
            ("@p6", config.RemotePath, DbType.String, 255),
            ("@p7", config.AuthType.ToString().ToUpper(), DbType.String, 16),
            ("@p8", config.Username, DbType.String, 64),
            ("@p9", config.Password, DbType.String, 512),
            ("@p10", config.CertificatePath, DbType.String, 255),
            ("@p11", config.PrivateKeyPath, DbType.String, 255),
            ("@p12", config.FileNamePattern, DbType.String, 128),
            ("@p13", config.SkipFilePattern, DbType.String, 128),
            ("@p14", config.DeleteAfterDownload ? "Y" : "N", DbType.String, 1),
            ("@p15", config.CompressOnArchive ? "Y" : "N", DbType.String, 1),
            ("@p16", config.Compression.ToString().ToUpper(), DbType.String, 16),
            ("@p17", config.CronSchedule, DbType.String, 64),
            ("@p18", config.IsEnabled ? "Y" : "N", DbType.String, 1),
            ("@p19", config.CreatedBy, DbType.String, 18),
            ("@p20", config.UpdatedBy, DbType.String, 18)
        );
    }

    public async Task<RawCommandResult> UpdateTransferSourceAsync(TransferSourceConfig config)
    {
        _logger.LogDebug("Updating transfer source: {SourceId}", config.SourceId);

        var sql = @"UPDATE ntfl_transfer_source SET
            vendor_name = ?, file_type_code = ?, protocol = ?, host = ?, port = ?,
            remote_path = ?, auth_type = ?, username = ?, password_encrypted = ?,
            certificate_path = ?, private_key_path = ?, file_name_pattern = ?,
            skip_file_pattern = ?, delete_after_download = ?, compress_on_archive = ?,
            compression_method = ?, cron_schedule = ?, is_enabled = ?,
            last_updated = CURRENT, updated_by = ?
        WHERE source_id = ?";

        return _dbContext.ExecuteRawCommand(sql,
            ("@p1", config.VendorName, DbType.String, 128),
            ("@p2", config.FileTypeCode, DbType.String, 10),
            ("@p3", config.Protocol.ToString().ToUpper(), DbType.String, 16),
            ("@p4", config.Host, DbType.String, 255),
            ("@p5", config.Port, DbType.Int32, null),
            ("@p6", config.RemotePath, DbType.String, 255),
            ("@p7", config.AuthType.ToString().ToUpper(), DbType.String, 16),
            ("@p8", config.Username, DbType.String, 64),
            ("@p9", config.Password, DbType.String, 512),
            ("@p10", config.CertificatePath, DbType.String, 255),
            ("@p11", config.PrivateKeyPath, DbType.String, 255),
            ("@p12", config.FileNamePattern, DbType.String, 128),
            ("@p13", config.SkipFilePattern, DbType.String, 128),
            ("@p14", config.DeleteAfterDownload ? "Y" : "N", DbType.String, 1),
            ("@p15", config.CompressOnArchive ? "Y" : "N", DbType.String, 1),
            ("@p16", config.Compression.ToString().ToUpper(), DbType.String, 16),
            ("@p17", config.CronSchedule, DbType.String, 64),
            ("@p18", config.IsEnabled ? "Y" : "N", DbType.String, 1),
            ("@p19", config.UpdatedBy, DbType.String, 18),
            ("@p20", config.SourceId, DbType.Int32, null)
        );
    }

    public async Task<RawCommandResult> DeleteTransferSourceAsync(int sourceId)
    {
        _logger.LogDebug("Deleting transfer source: {SourceId}", sourceId);

        return _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_transfer_source WHERE source_id = ?",
            ("@p1", sourceId, DbType.Int32, null)
        );
    }

    // ============================================
    // Folder Configuration
    // ============================================

    public async Task<DataResult<FolderWorkflowConfig>> GetFolderConfigAsync(string? fileTypeCode)
    {
        _logger.LogDebug("Getting folder config: FileType={FileType}", fileTypeCode);

        // Try to find specific config first, then fall back to default
        var sql = @"SELECT config_id, file_type_code, transfer_folder,
                           processing_folder, processed_folder, errors_folder, skipped_folder,
                           example_folder, created_tm, last_updated
                    FROM ntfl_folder_config
                    WHERE file_type_code = ? OR file_type_code IS NULL
                    ORDER BY file_type_code DESC";

        var result = _dbContext.ExecuteRawQuery(
            sql,
            reader => new FolderWorkflowConfig
            {
                ConfigId = reader.GetInt32(0),
                FileTypeCode = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                TransferFolder = reader.GetString(2).Trim(),
                ProcessingFolder = reader.GetString(3).Trim(),
                ProcessedFolder = reader.GetString(4).Trim(),
                ErrorsFolder = reader.GetString(5).Trim(),
                SkippedFolder = reader.GetString(6).Trim(),
                ExampleFolder = reader.IsDBNull(7) ? string.Empty : reader.GetString(7).Trim(),
                CreatedAt = reader.GetDateTime(8),
                UpdatedAt = reader.GetDateTime(9)
            },
            ("@p1", fileTypeCode, DbType.String, 10)
        );

        if (!result.IsSuccess)
        {
            return new DataResult<FolderWorkflowConfig>
            {
                StatusCode = result.StatusCode,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };
        }

        if (result.Data.Count == 0)
        {
            return new DataResult<FolderWorkflowConfig>
            {
                StatusCode = 404,
                ErrorCode = "FileLoading.FolderConfigNotFound",
                ErrorMessage = "Folder configuration not found"
            };
        }

        return new DataResult<FolderWorkflowConfig>
        {
            StatusCode = 200,
            Data = result.Data[0]
        };
    }

    public async Task<RawCommandResult> SaveFolderConfigAsync(FolderWorkflowConfig config)
    {
        _logger.LogDebug("Saving folder config: FileType={FileType}", config.FileTypeCode);

        // Check if exists
        var existsResult = _dbContext.ExecuteRawScalar<int>(
            "SELECT COUNT(*) FROM ntfl_folder_config WHERE file_type_code = ? OR (file_type_code IS NULL AND ? IS NULL)",
            ("@p1", config.FileTypeCode, DbType.String, 10),
            ("@p2", config.FileTypeCode, DbType.String, 10)
        );

        if (existsResult.IsSuccess && existsResult.Value > 0)
        {
            // Update
            var sql = @"UPDATE ntfl_folder_config SET
                transfer_folder = ?, processing_folder = ?, processed_folder = ?,
                errors_folder = ?, skipped_folder = ?, example_folder = ?,
                last_updated = CURRENT, updated_by = ?
            WHERE file_type_code = ? OR (file_type_code IS NULL AND ? IS NULL)";

            return _dbContext.ExecuteRawCommand(sql,
                ("@p1", config.TransferFolder, DbType.String, 255),
                ("@p2", config.ProcessingFolder, DbType.String, 255),
                ("@p3", config.ProcessedFolder, DbType.String, 255),
                ("@p4", config.ErrorsFolder, DbType.String, 255),
                ("@p5", config.SkippedFolder, DbType.String, 255),
                ("@p6", config.ExampleFolder, DbType.String, 255),
                ("@p7", config.UpdatedBy, DbType.String, 18),
                ("@p8", config.FileTypeCode, DbType.String, 10),
                ("@p9", config.FileTypeCode, DbType.String, 10)
            );
        }
        else
        {
            // Insert
            var sql = @"INSERT INTO ntfl_folder_config (
                file_type_code, transfer_folder, processing_folder,
                processed_folder, errors_folder, skipped_folder, example_folder,
                created_tm, created_by, last_updated, updated_by
            ) VALUES (?, ?, ?, ?, ?, ?, ?, CURRENT, ?, CURRENT, ?)";

            return _dbContext.ExecuteRawCommand(sql,
                ("@p1", config.FileTypeCode, DbType.String, 10),
                ("@p2", config.TransferFolder, DbType.String, 255),
                ("@p3", config.ProcessingFolder, DbType.String, 255),
                ("@p4", config.ProcessedFolder, DbType.String, 255),
                ("@p5", config.ErrorsFolder, DbType.String, 255),
                ("@p6", config.SkippedFolder, DbType.String, 255),
                ("@p7", config.ExampleFolder, DbType.String, 255),
                ("@p8", config.CreatedBy, DbType.String, 18),
                ("@p9", config.UpdatedBy, DbType.String, 18)
            );
        }
    }

    // ============================================
    // Transfer Records
    // ============================================

    public async Task<ValueResult<int>> InsertTransferRecordAsync(FileTransferRecord record)
    {
        _logger.LogDebug("Inserting transfer record: {FileName}", record.FileName);

        // First get max transfer_id + 1 (since SERIAL might not be available)
        var sql = @"INSERT INTO ntfl_transfer (
            source_id, nt_file_num, file_name, status_id, source_path,
            destination_path, current_folder, file_size, started_dt,
            completed_dt, error_message, retry_count, ftp_server_id,
            created_by, created_dt
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, CURRENT)";

        var result = _dbContext.ExecuteRawCommand(sql,
            ("@p1", record.SourceId, DbType.Int32, null),
            ("@p2", record.NtFileNum, DbType.Int32, null),
            ("@p3", record.FileName, DbType.String, 255),
            ("@p4", (int)record.Status, DbType.Int32, null),
            ("@p5", record.SourcePath, DbType.String, 512),
            ("@p6", record.DestinationPath, DbType.String, 512),
            ("@p7", record.CurrentFolder, DbType.String, 32),
            ("@p8", record.FileSize, DbType.Int64, null),
            ("@p9", record.StartedAt, DbType.DateTime, null),
            ("@p10", record.CompletedAt, DbType.DateTime, null),
            ("@p11", record.ErrorMessage, DbType.String, 512),
            ("@p12", record.RetryCount, DbType.Int32, null),
            ("@p13", record.FtpServerId, DbType.Int32, null),
            ("@p14", record.CreatedBy, DbType.String, 32)
        );

        if (!result.IsSuccess)
        {
            return new ValueResult<int>
            {
                StatusCode = 500,
                ErrorCode = "FileLoading.DatabaseError",
                ErrorMessage = result.ErrorMessage
            };
        }

        // Get the inserted ID
        var idResult = _dbContext.ExecuteRawScalar<int>(
            "SELECT MAX(transfer_id) FROM ntfl_transfer WHERE file_name = ? AND created_by = ?",
            ("@p1", record.FileName, DbType.String, 255),
            ("@p2", record.CreatedBy, DbType.String, 32)
        );

        return new ValueResult<int>
        {
            StatusCode = 201,
            Value = idResult.IsSuccess ? idResult.Value : 0
        };
    }

    public async Task<RawCommandResult> UpdateTransferStatusAsync(int transferId, TransferStatus status, string? error, DateTime? completedAt = null)
    {
        _logger.LogDebug("Updating transfer {TransferId} status to {Status}", transferId, status);

        var sql = @"UPDATE ntfl_transfer SET
            status_id = ?, error_message = ?, completed_dt = ?
        WHERE transfer_id = ?";

        return _dbContext.ExecuteRawCommand(sql,
            ("@p1", (int)status, DbType.Int32, null),
            ("@p2", error?.Substring(0, Math.Min(error.Length, 512)), DbType.String, 512),
            ("@p3", completedAt, DbType.DateTime, null),
            ("@p4", transferId, DbType.Int32, null)
        );
    }

    public async Task<RawCommandResult> UpdateTransferNtFileNumAsync(int transferId, int ntFileNum)
    {
        _logger.LogDebug("Updating transfer {TransferId} with nt_file_num {NtFileNum}", transferId, ntFileNum);

        return _dbContext.ExecuteRawCommand(
            "UPDATE ntfl_transfer SET nt_file_num = ? WHERE transfer_id = ?",
            ("@p1", ntFileNum, DbType.Int32, null),
            ("@p2", transferId, DbType.Int32, null)
        );
    }

    public async Task<RawCommandResult> UpdateTransferFolderAsync(int transferId, string currentFolder, string? destinationPath)
    {
        _logger.LogDebug("Updating transfer {TransferId} folder to {Folder}", transferId, currentFolder);

        return _dbContext.ExecuteRawCommand(
            "UPDATE ntfl_transfer SET current_folder = ?, destination_path = ? WHERE transfer_id = ?",
            ("@p1", currentFolder, DbType.String, 32),
            ("@p2", destinationPath, DbType.String, 512),
            ("@p3", transferId, DbType.Int32, null)
        );
    }

    public async Task<DataResult<FileTransferRecord>> GetTransferRecordAsync(int transferId)
    {
        _logger.LogDebug("Getting transfer record: {TransferId}", transferId);

        var sql = @"SELECT transfer_id, source_id, nt_file_num, file_name, status_id,
                           source_path, destination_path, current_folder, file_size,
                           started_dt, completed_dt, error_message, retry_count,
                           created_by, created_dt, ftp_server_id
                    FROM ntfl_transfer WHERE transfer_id = ?";

        var result = _dbContext.ExecuteRawQuery(
            sql,
            reader => new FileTransferRecord
            {
                TransferId = reader.GetInt32(0),
                SourceId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                NtFileNum = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                FileName = reader.GetString(3).Trim(),
                Status = (TransferStatus)reader.GetInt32(4),
                SourcePath = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                DestinationPath = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                CurrentFolder = reader.IsDBNull(7) ? null : reader.GetString(7).Trim(),
                FileSize = reader.IsDBNull(8) ? null : reader.GetInt64(8),
                StartedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                CompletedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                ErrorMessage = reader.IsDBNull(11) ? null : reader.GetString(11).Trim(),
                RetryCount = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                CreatedBy = reader.IsDBNull(13) ? null : reader.GetString(13).Trim(),
                CreatedAt = reader.IsDBNull(14) ? DateTime.Now : reader.GetDateTime(14),
                FtpServerId = reader.IsDBNull(15) ? null : reader.GetInt32(15)
            },
            ("@p1", transferId, DbType.Int32, null)
        );

        if (!result.IsSuccess)
        {
            return new DataResult<FileTransferRecord>
            {
                StatusCode = result.StatusCode,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };
        }

        if (result.Data.Count == 0)
        {
            return new DataResult<FileTransferRecord>
            {
                StatusCode = 404,
                ErrorCode = "FileLoading.TransferRecordNotFound",
                ErrorMessage = $"Transfer record {transferId} not found"
            };
        }

        return new DataResult<FileTransferRecord>
        {
            StatusCode = 200,
            Data = result.Data[0]
        };
    }

    public async Task<DataResult<List<FileTransferRecord>>> ListTransferRecordsAsync(
        int? sourceId, int? status, string? currentFolder, int maxRecords)
    {
        _logger.LogDebug("Listing transfer records: Source={Source}, Status={Status}, Folder={Folder}",
            sourceId, status, currentFolder);

        var sql = new StringBuilder(@"SELECT FIRST ");
        sql.Append(maxRecords);
        sql.Append(@" transfer_id, source_id, nt_file_num, file_name, status_id,
                     source_path, destination_path, current_folder, file_size,
                     started_dt, completed_dt, error_message, retry_count,
                     created_by, created_dt, ftp_server_id
                FROM ntfl_transfer WHERE 1=1");

        var parameters = new List<(string, object?, DbType, int?)>();
        var paramIndex = 1;

        if (sourceId.HasValue)
        {
            sql.Append($" AND source_id = ?");
            parameters.Add(($"@p{paramIndex++}", sourceId.Value, DbType.Int32, null));
        }

        if (status.HasValue)
        {
            sql.Append($" AND status_id = ?");
            parameters.Add(($"@p{paramIndex++}", status.Value, DbType.Int32, null));
        }

        if (!string.IsNullOrEmpty(currentFolder))
        {
            sql.Append($" AND current_folder = ?");
            parameters.Add(($"@p{paramIndex++}", currentFolder, DbType.String, 32));
        }

        sql.Append(" ORDER BY created_dt DESC");

        var result = _dbContext.ExecuteRawQuery(
            sql.ToString(),
            reader => new FileTransferRecord
            {
                TransferId = reader.GetInt32(0),
                SourceId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                NtFileNum = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                FileName = reader.GetString(3).Trim(),
                Status = (TransferStatus)reader.GetInt32(4),
                SourcePath = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                DestinationPath = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                CurrentFolder = reader.IsDBNull(7) ? null : reader.GetString(7).Trim(),
                FileSize = reader.IsDBNull(8) ? null : reader.GetInt64(8),
                StartedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                CompletedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                ErrorMessage = reader.IsDBNull(11) ? null : reader.GetString(11).Trim(),
                RetryCount = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                CreatedBy = reader.IsDBNull(13) ? null : reader.GetString(13).Trim(),
                CreatedAt = reader.IsDBNull(14) ? DateTime.Now : reader.GetDateTime(14),
                FtpServerId = reader.IsDBNull(15) ? null : reader.GetInt32(15)
            },
            parameters.ToArray()
        );

        return new DataResult<List<FileTransferRecord>>
        {
            StatusCode = result.IsSuccess ? 200 : result.StatusCode,
            Data = result.Data,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    public async Task<DataResult<List<FileWithStatus>>> ListFilesWithStatusAsync(FileListFilter filter)
    {
        _logger.LogDebug("Listing files with status: Filter={Filter}", filter);

        var sql = new StringBuilder(@"SELECT FIRST ");
        sql.Append(filter.MaxRecords);
        sql.Append(@" t.transfer_id, t.nt_file_num, t.file_name, t.status_id,
                     t.current_folder, t.file_size, t.created_dt, t.completed_dt,
                     t.error_message, t.source_id, s.file_type_code,
                     t.ftp_server_id, fs.host
                FROM ntfl_transfer t
                LEFT OUTER JOIN ntfl_transfer_source s ON t.source_id = s.source_id
                LEFT OUTER JOIN ntfl_ftp_server fs ON t.ftp_server_id = fs.server_id
                WHERE 1=1");

        var parameters = new List<(string, object?, DbType, int?)>();
        var paramIndex = 1;

        if (!string.IsNullOrEmpty(filter.FileTypeCode))
        {
            sql.Append($" AND s.file_type_code = ?");
            parameters.Add(($"@p{paramIndex++}", filter.FileTypeCode, DbType.String, 10));
        }

        if (!string.IsNullOrEmpty(filter.CurrentFolder))
        {
            sql.Append($" AND t.current_folder = ?");
            parameters.Add(($"@p{paramIndex++}", filter.CurrentFolder, DbType.String, 32));
        }

        if (filter.Status.HasValue)
        {
            sql.Append($" AND t.status_id = ?");
            parameters.Add(($"@p{paramIndex++}", (int)filter.Status.Value, DbType.Int32, null));
        }

        if (filter.FromDate.HasValue)
        {
            sql.Append($" AND t.created_dt >= ?");
            parameters.Add(($"@p{paramIndex++}", filter.FromDate.Value, DbType.DateTime, null));
        }

        if (filter.ToDate.HasValue)
        {
            sql.Append($" AND t.created_dt <= ?");
            parameters.Add(($"@p{paramIndex++}", filter.ToDate.Value, DbType.DateTime, null));
        }

        if (!string.IsNullOrEmpty(filter.FileNameSearch))
        {
            sql.Append($" AND t.file_name LIKE ?");
            parameters.Add(($"@p{paramIndex++}", $"%{filter.FileNameSearch}%", DbType.String, 255));
        }

        sql.Append(" ORDER BY t.created_dt DESC");

        var result = _dbContext.ExecuteRawQuery(
            sql.ToString(),
            reader => new FileWithStatus
            {
                TransferId = reader.GetInt32(0),
                NtFileNum = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                FileName = reader.GetString(2).Trim(),
                StatusId = (TransferStatus)reader.GetInt32(3),
                Status = GetStatus((TransferStatus)reader.GetInt32(3)),
                CurrentFolder = reader.IsDBNull(4) ? string.Empty : reader.GetString(4).Trim(),
                FileSize = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                CreatedAt = reader.IsDBNull(6) ? DateTime.Now : reader.GetDateTime(6),
                CompletedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
                SourceId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                FileTypeCode = reader.IsDBNull(10) ? null : reader.GetString(10).Trim(),
                FtpServerId = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                StorageHost = reader.IsDBNull(12) ? null : reader.GetString(12).Trim()
            },
            parameters.ToArray()
        );

        return new DataResult<List<FileWithStatus>>
        {
            StatusCode = result.IsSuccess ? 200 : result.StatusCode,
            Data = result.Data,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    public async Task<RawCommandResult> DeleteTransferRecordAsync(int transferId)
    {
        _logger.LogDebug("Deleting transfer record: {TransferId}", transferId);

        return _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_transfer WHERE transfer_id = ?",
            ("@p1", transferId, DbType.Int32, null)
        );
    }

    // ============================================
    // Downloaded Files Tracking
    // ============================================

    public async Task<bool> IsFileDownloadedAsync(int sourceId, string fileName, DateTime modifiedDate, long fileSize)
    {
        _logger.LogDebug("Checking if file downloaded: {SourceId}/{FileName}", sourceId, fileName);

        var result = _dbContext.ExecuteRawScalar<int>(
            @"SELECT COUNT(*) FROM ntfl_downloaded_files
              WHERE source_id = ? AND remote_file_name = ?
              AND remote_modified_dt = ? AND file_size = ?",
            ("@p1", sourceId, DbType.Int32, null),
            ("@p2", fileName, DbType.String, 255),
            ("@p3", modifiedDate, DbType.DateTime, null),
            ("@p4", fileSize, DbType.Int64, null)
        );

        return result.IsSuccess && result.Value > 0;
    }

    public async Task<RawCommandResult> InsertDownloadedFileAsync(DownloadedFileRecord record)
    {
        _logger.LogDebug("Recording downloaded file: {SourceId}/{FileName}", record.SourceId, record.RemoteFileName);

        var sql = @"INSERT INTO ntfl_downloaded_files (
            source_id, remote_file_name, remote_file_path, file_size,
            remote_modified_dt, file_hash, downloaded_dt
        ) VALUES (?, ?, ?, ?, ?, ?, CURRENT)";

        return _dbContext.ExecuteRawCommand(sql,
            ("@p1", record.SourceId, DbType.Int32, null),
            ("@p2", record.RemoteFileName, DbType.String, 255),
            ("@p3", record.RemoteFilePath, DbType.String, 512),
            ("@p4", record.FileSize, DbType.Int64, null),
            ("@p5", record.RemoteModifiedDate, DbType.DateTime, null),
            ("@p6", record.FileHash, DbType.String, 64)
        );
    }

    // ============================================
    // Activity Logging
    // ============================================

    public async Task<RawCommandResult> InsertActivityLogAsync(FileActivityLog log)
    {
        _logger.LogDebug("Inserting activity log: {ActivityType} for {FileName}", log.ActivityType, log.FileName);

        var sql = @"INSERT INTO ntfl_activity_log (
            nt_file_num, transfer_id, file_name, activity_type,
            description, details_json, user_id, activity_dt
        ) VALUES (?, ?, ?, ?, ?, ?, ?, CURRENT)";

        return _dbContext.ExecuteRawCommand(sql,
            ("@p1", log.NtFileNum, DbType.Int32, null),
            ("@p2", log.TransferId, DbType.Int32, null),
            ("@p3", log.FileName, DbType.String, 255),
            ("@p4", (int)log.ActivityType, DbType.Int32, null),
            ("@p5", log.Description?.Substring(0, Math.Min(log.Description.Length, 512)), DbType.String, 512),
            ("@p6", log.Details, DbType.String, null),
            ("@p7", log.UserId, DbType.String, 32)
        );
    }

    public async Task<DataResult<List<FileActivityLog>>> GetActivityLogsAsync(int? ntFileNum, int? transferId, int maxRecords)
    {
        _logger.LogDebug("Getting activity logs: NtFileNum={NtFileNum}, TransferId={TransferId}", ntFileNum, transferId);

        var sql = new StringBuilder(@"SELECT FIRST ");
        sql.Append(maxRecords);
        sql.Append(@" activity_id, nt_file_num, transfer_id, file_name,
                     activity_type, description, details_json, user_id, activity_dt
                FROM ntfl_activity_log WHERE 1=1");

        var parameters = new List<(string, object?, DbType, int?)>();
        var paramIndex = 1;

        if (ntFileNum.HasValue)
        {
            sql.Append($" AND nt_file_num = ?");
            parameters.Add(($"@p{paramIndex++}", ntFileNum.Value, DbType.Int32, null));
        }

        if (transferId.HasValue)
        {
            sql.Append($" AND transfer_id = ?");
            parameters.Add(($"@p{paramIndex++}", transferId.Value, DbType.Int32, null));
        }

        sql.Append(" ORDER BY activity_dt DESC");

        var result = _dbContext.ExecuteRawQuery(
            sql.ToString(),
            reader => new FileActivityLog
            {
                ActivityId = reader.GetInt64(0),
                NtFileNum = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                TransferId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                FileName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim(),
                ActivityType = (FileActivityType)reader.GetInt32(4),
                Description = reader.IsDBNull(5) ? string.Empty : reader.GetString(5).Trim(),
                Details = reader.IsDBNull(6) ? null : reader.GetString(6),
                UserId = reader.IsDBNull(7) ? string.Empty : reader.GetString(7).Trim(),
                ActivityAt = reader.IsDBNull(8) ? DateTime.Now : reader.GetDateTime(8)
            },
            parameters.ToArray()
        );

        return new DataResult<List<FileActivityLog>>
        {
            StatusCode = result.IsSuccess ? 200 : result.StatusCode,
            Data = result.Data,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    // ============================================
    // Dashboard Queries
    // ============================================

    public async Task<DataResult<FileManagementDashboard>> GetDashboardSummaryAsync(string? fileTypeCode)
    {
        _logger.LogDebug("Getting dashboard summary: FileType={FileType}", fileTypeCode);

        var dashboard = new FileManagementDashboard();

        // Get counts by status/folder
        var countsSql = @"SELECT current_folder, status_id, COUNT(*)
                          FROM ntfl_transfer
                          GROUP BY current_folder, status_id";

        var countsResult = _dbContext.ExecuteRawQuery(
            countsSql,
            reader => new
            {
                Folder = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                Status = reader.GetInt32(1),
                Count = reader.GetInt32(2)
            }
        );

        if (countsResult.IsSuccess)
        {
            foreach (var item in countsResult.Data)
            {
                switch (item.Folder.ToUpper())
                {
                    case "TRANSFER":
                        dashboard.FilesInTransfer += item.Count;
                        break;
                    case "PROCESSING":
                        dashboard.FilesInProcessing += item.Count;
                        break;
                    case "ERRORS":
                        dashboard.FilesWithErrors += item.Count;
                        break;
                    case "SKIPPED":
                        dashboard.FilesSkipped += item.Count;
                        break;
                }
            }
        }

        // Get files processed today
        var todayResult = _dbContext.ExecuteRawScalar<int>(
            @"SELECT COUNT(*) FROM ntfl_transfer
              WHERE status_id = 4 AND completed_dt >= TODAY",
            Array.Empty<(string, object?, DbType, int?)>()
        );

        if (todayResult.IsSuccess)
        {
            dashboard.FilesProcessedToday = todayResult.Value;
        }

        return new DataResult<FileManagementDashboard>
        {
            StatusCode = 200,
            Data = dashboard
        };
    }

    public async Task<DataResult<List<TransferSourceStatus>>> GetSourceStatusesAsync()
    {
        _logger.LogDebug("Getting source statuses");

        var sql = @"SELECT s.source_id, s.vendor_name, s.file_type_code, s.is_enabled,
                           MAX(t.completed_dt) as last_transfer,
                           COUNT(CASE WHEN t.created_dt >= TODAY THEN 1 END) as today_count,
                           MAX(CASE WHEN t.status_id = 5 THEN t.error_message END) as last_error
                    FROM ntfl_transfer_source s
                    LEFT OUTER JOIN ntfl_transfer t ON s.source_id = t.source_id
                    GROUP BY s.source_id, s.vendor_name, s.file_type_code, s.is_enabled";

        var result = _dbContext.ExecuteRawQuery(
            sql,
            reader => new TransferSourceStatus
            {
                SourceId = reader.GetInt32(0),
                VendorName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim(),
                FileTypeCode = reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                IsEnabled = reader.IsDBNull(3) || reader.GetString(3).Trim().ToUpper() == "Y",
                LastTransferAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                FilesTransferredToday = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                LastError = reader.IsDBNull(6) ? null : reader.GetString(6).Trim()
            }
        );

        return new DataResult<List<TransferSourceStatus>>
        {
            StatusCode = result.IsSuccess ? 200 : result.StatusCode,
            Data = result.Data,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    // ============================================
    // File Unload Operations
    // ============================================

    public async Task<RawCommandResult> UnloadFileRecordsAsync(int ntFileNum, SecurityContext securityContext)
    {
        _logger.LogInformation("Unloading file records for {NtFileNum}", ntFileNum);

        var totalDeleted = 0;

        // Delete cl_detail records
        var clResult = _dbContext.ExecuteRawCommand(
            "DELETE FROM cl_detail WHERE nt_file_num = ?",
            ("@p1", ntFileNum, DbType.Int32, null)
        );
        if (clResult.IsSuccess) totalDeleted += clResult.RowsAffected;

        // Delete ntfl_chgdtl records
        var chgResult = _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_chgdtl WHERE nt_file_num = ?",
            ("@p1", ntFileNum, DbType.Int32, null)
        );
        if (chgResult.IsSuccess) totalDeleted += chgResult.RowsAffected;

        // Delete ntfl_generic_detail records
        var genResult = _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_generic_detail WHERE nt_file_num = ?",
            ("@p1", ntFileNum, DbType.Int32, null)
        );
        if (genResult.IsSuccess) totalDeleted += genResult.RowsAffected;

        // Delete from custom table if one exists for this file type
        var fileTypeCode = await GetFileTypeCodeForFileAsync(ntFileNum);
        if (!string.IsNullOrEmpty(fileTypeCode))
        {
            var customTable = await GetActiveCustomTableAsync(fileTypeCode);
            if (customTable != null)
            {
                var customResult = await DeleteCustomTableRecordsAsync(customTable.TableName, ntFileNum);
                if (customResult.IsSuccess) totalDeleted += customResult.RowsAffected;
            }
        }

        // Delete nt_cl_not_load records
        var notLoadResult = _dbContext.ExecuteRawCommand(
            "DELETE FROM nt_cl_not_load WHERE nt_file_num = ?",
            ("@p1", ntFileNum, DbType.Int32, null)
        );
        if (notLoadResult.IsSuccess) totalDeleted += notLoadResult.RowsAffected;

        // Reset trailer
        var trailerResult = _dbContext.ExecuteRawCommand(
            @"UPDATE nt_fl_trailer SET
                nt_tot_rec = 0, nt_tot_cost = 0, nt_earliest_call = NULL, nt_latest_call = NULL
              WHERE nt_file_num = ?",
            ("@p1", ntFileNum, DbType.Int32, null)
        );

        // Update file status back to initial
        await UpdateFileStatusAsync(ntFileNum, 1, securityContext);

        _logger.LogInformation("Unloaded {TotalDeleted} records for file {NtFileNum}", totalDeleted, ntFileNum);

        return new RawCommandResult { RowsAffected = totalDeleted };
    }

    // ============================================
    // Generic Parser Configuration
    // ============================================

    public async Task<GenericFileFormatConfig?> GetGenericFileFormatConfigAsync(string fileTypeCode)
    {
        _logger.LogDebug("Loading generic file format config for {FileTypeCode}", fileTypeCode);

        // Query file format config
        var configSql = @"SELECT file_type_code, file_format, delimiter, has_header_row,
            skip_rows_top, skip_rows_bottom, row_id_mode, row_id_column,
            header_indicator, trailer_indicator, detail_indicator, skip_indicator,
            total_column_index, total_type, sheet_name, sheet_index,
            date_format, custom_sp_name, active
            FROM ntfl_file_format_config
            WHERE file_type_code = ? AND active = 'Y'";

        GenericFileFormatConfig? config = null;

        var connection = _dbContext.GetConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = configSql;
            command.CommandType = CommandType.Text;
            AddParameter(command, "@p1", fileTypeCode, DbType.String, 10);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                config = new GenericFileFormatConfig
                {
                    FileTypeCode = reader.GetString(0).Trim(),
                    FileFormat = ParseFileFormat(reader.GetString(1).Trim()),
                    Delimiter = reader.IsDBNull(2) ? "," : reader.GetString(2).Trim(),
                    HasHeaderRow = !reader.IsDBNull(3) && reader.GetString(3).Trim() == "Y",
                    SkipRowsTop = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    SkipRowsBottom = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    RowIdMode = ParseRowIdMode(reader.IsDBNull(6) ? "POSITION" : reader.GetString(6).Trim()),
                    RowIdColumn = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    HeaderIndicator = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
                    TrailerIndicator = reader.IsDBNull(9) ? null : reader.GetString(9).Trim(),
                    DetailIndicator = reader.IsDBNull(10) ? null : reader.GetString(10).Trim(),
                    SkipIndicator = reader.IsDBNull(11) ? null : reader.GetString(11).Trim(),
                    TotalColumnIndex = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                    TotalType = reader.IsDBNull(13) ? null : reader.GetString(13).Trim(),
                    SheetName = reader.IsDBNull(14) ? null : reader.GetString(14).Trim(),
                    SheetIndex = reader.IsDBNull(15) ? 0 : reader.GetInt32(15),
                    DateFormat = reader.IsDBNull(16) ? null : reader.GetString(16).Trim(),
                    CustomSpName = reader.IsDBNull(17) ? null : reader.GetString(17).Trim(),
                    Active = !reader.IsDBNull(18) && reader.GetString(18).Trim() == "Y"
                };
            }
        }

        if (config == null)
            return null;

        // Query column mappings
        var mappingSql = @"SELECT file_type_code, column_index, source_column_name, target_field,
            data_type, date_format, is_required, default_value, regex_pattern, max_length
            FROM ntfl_column_mapping
            WHERE file_type_code = ?
            ORDER BY column_index";

        using (var command = connection.CreateCommand())
        {
            command.CommandText = mappingSql;
            command.CommandType = CommandType.Text;
            AddParameter(command, "@p1", fileTypeCode, DbType.String, 10);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                config.ColumnMappings.Add(new GenericColumnMapping
                {
                    FileTypeCode = reader.GetString(0).Trim(),
                    ColumnIndex = reader.GetInt32(1),
                    SourceColumnName = reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                    TargetField = reader.GetString(3).Trim(),
                    DataType = reader.IsDBNull(4) ? "String" : reader.GetString(4).Trim(),
                    DateFormat = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                    IsRequired = !reader.IsDBNull(6) && reader.GetString(6).Trim() == "Y",
                    DefaultValue = reader.IsDBNull(7) ? null : reader.GetString(7).Trim(),
                    RegexPattern = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
                    MaxLength = reader.IsDBNull(9) ? null : reader.GetInt32(9)
                });
            }
        }

        _logger.LogDebug("Loaded generic config for {FileTypeCode}: {MappingCount} column mappings",
            fileTypeCode, config.ColumnMappings.Count);

        return config;
    }

    public async Task<RawCommandResult> InsertGenericDetailBatchOptimizedAsync(
        IEnumerable<GenericDetailRecord> records, int transactionBatchSize = 1000)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0)
            return new RawCommandResult { RowsAffected = 0 };

        _logger.LogDebug("Inserting {Count} ntfl_generic_detail records with transaction batching (batch size: {BatchSize})",
            recordList.Count, transactionBatchSize);

        var totalRows = 0;
        var connection = _dbContext.GetConnection();

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        foreach (var batch in recordList.Chunk(transactionBatchSize))
        {
            DbTransaction? transaction = null;
            try
            {
                transaction = await connection.BeginTransactionAsync();

                foreach (var record in batch)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO ntfl_generic_detail (
                        nt_file_num, nt_file_rec_num, account_code, service_id,
                        charge_type, cost_amount, tax_amount, quantity, uom,
                        from_date, to_date, description, external_ref,
                        generic_01, generic_02, generic_03, generic_04, generic_05,
                        generic_06, generic_07, generic_08, generic_09, generic_10,
                        generic_11, generic_12, generic_13, generic_14, generic_15,
                        generic_16, generic_17, generic_18, generic_19, generic_20,
                        raw_data, status_id
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
                    command.CommandType = CommandType.Text;

                    AddParameter(command, "@p1", record.NtFileNum, DbType.Int32);
                    AddParameter(command, "@p2", record.NtFileRecNum, DbType.Int32);
                    AddParameter(command, "@p3", record.AccountCode, DbType.String, 64);
                    AddParameter(command, "@p4", record.ServiceId, DbType.String, 64);
                    AddParameter(command, "@p5", record.ChargeType, DbType.String, 30);
                    AddParameter(command, "@p6", record.CostAmount, DbType.Decimal);
                    AddParameter(command, "@p7", record.TaxAmount, DbType.Decimal);
                    AddParameter(command, "@p8", record.Quantity, DbType.Decimal);
                    AddParameter(command, "@p9", record.UOM, DbType.String, 10);
                    AddParameter(command, "@p10", record.FromDate, DbType.DateTime);
                    AddParameter(command, "@p11", record.ToDate, DbType.DateTime);
                    AddParameter(command, "@p12", record.Description, DbType.String, 256);
                    AddParameter(command, "@p13", record.ExternalRef, DbType.String, 64);
                    AddParameter(command, "@p14", record.Generic01, DbType.String, 128);
                    AddParameter(command, "@p15", record.Generic02, DbType.String, 128);
                    AddParameter(command, "@p16", record.Generic03, DbType.String, 128);
                    AddParameter(command, "@p17", record.Generic04, DbType.String, 128);
                    AddParameter(command, "@p18", record.Generic05, DbType.String, 128);
                    AddParameter(command, "@p19", record.Generic06, DbType.String, 128);
                    AddParameter(command, "@p20", record.Generic07, DbType.String, 128);
                    AddParameter(command, "@p21", record.Generic08, DbType.String, 128);
                    AddParameter(command, "@p22", record.Generic09, DbType.String, 128);
                    AddParameter(command, "@p23", record.Generic10, DbType.String, 128);
                    AddParameter(command, "@p24", record.Generic11, DbType.String, 256);
                    AddParameter(command, "@p25", record.Generic12, DbType.String, 256);
                    AddParameter(command, "@p26", record.Generic13, DbType.String, 256);
                    AddParameter(command, "@p27", record.Generic14, DbType.String, 256);
                    AddParameter(command, "@p28", record.Generic15, DbType.String, 256);
                    AddParameter(command, "@p29", record.Generic16, DbType.String, 256);
                    AddParameter(command, "@p30", record.Generic17, DbType.String, 256);
                    AddParameter(command, "@p31", record.Generic18, DbType.String, 256);
                    AddParameter(command, "@p32", record.Generic19, DbType.String, 256);
                    AddParameter(command, "@p33", record.Generic20, DbType.String, 256);
                    AddParameter(command, "@p34", record.RawData, DbType.String, 2000);
                    AddParameter(command, "@p35", record.StatusId, DbType.Int32);

                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected < 0)
                    {
                        _logger.LogError("Failed to insert ntfl_generic_detail record");
                        await transaction.RollbackAsync();
                        return new RawCommandResult { RowsAffected = 0, ErrorMessage = "Insert failed" };
                    }
                    totalRows += rowsAffected;
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during generic detail batch insert, rolling back");
                if (transaction != null)
                    await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        _logger.LogDebug("Completed inserting {TotalRows} ntfl_generic_detail records", totalRows);
        return new RawCommandResult { RowsAffected = totalRows };
    }

    public async Task<RawCommandResult> ExecuteCustomValidationSpAsync(string spName, int ntFileNum)
    {
        _logger.LogInformation("Executing custom validation SP {SpName} for file {NtFileNum}", spName, ntFileNum);

        var connection = _dbContext.GetConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = spName;
        command.CommandType = CommandType.StoredProcedure;
        AddParameter(command, "@p1", ntFileNum, DbType.Int32);

        try
        {
            var rowsAffected = await command.ExecuteNonQueryAsync();
            return new RawCommandResult { RowsAffected = rowsAffected };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Custom validation SP {SpName} failed for file {NtFileNum}", spName, ntFileNum);
            return new RawCommandResult { RowsAffected = 0, ErrorMessage = ex.Message };
        }
    }

    public async Task<DataResult<List<GenericFileFormatConfig>>> GetAllGenericFileFormatConfigsAsync(bool? activeOnly)
    {
        _logger.LogDebug("Loading all generic file format configs (activeOnly={ActiveOnly})", activeOnly);

        var configSql = @"SELECT file_type_code, file_format, delimiter, has_header_row,
            skip_rows_top, skip_rows_bottom, row_id_mode, row_id_column,
            header_indicator, trailer_indicator, detail_indicator, skip_indicator,
            total_column_index, total_type, sheet_name, sheet_index,
            date_format, custom_sp_name, active
            FROM ntfl_file_format_config";

        if (activeOnly == true)
            configSql += " WHERE active = 'Y'";

        configSql += " ORDER BY file_type_code";

        var configs = new List<GenericFileFormatConfig>();

        var connection = _dbContext.GetConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = configSql;
            command.CommandType = CommandType.Text;

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                configs.Add(new GenericFileFormatConfig
                {
                    FileTypeCode = reader.GetString(0).Trim(),
                    FileFormat = ParseFileFormat(reader.GetString(1).Trim()),
                    Delimiter = reader.IsDBNull(2) ? "," : reader.GetString(2).Trim(),
                    HasHeaderRow = !reader.IsDBNull(3) && reader.GetString(3).Trim() == "Y",
                    SkipRowsTop = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    SkipRowsBottom = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    RowIdMode = ParseRowIdMode(reader.IsDBNull(6) ? "POSITION" : reader.GetString(6).Trim()),
                    RowIdColumn = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    HeaderIndicator = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
                    TrailerIndicator = reader.IsDBNull(9) ? null : reader.GetString(9).Trim(),
                    DetailIndicator = reader.IsDBNull(10) ? null : reader.GetString(10).Trim(),
                    SkipIndicator = reader.IsDBNull(11) ? null : reader.GetString(11).Trim(),
                    TotalColumnIndex = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                    TotalType = reader.IsDBNull(13) ? null : reader.GetString(13).Trim(),
                    SheetName = reader.IsDBNull(14) ? null : reader.GetString(14).Trim(),
                    SheetIndex = reader.IsDBNull(15) ? 0 : reader.GetInt32(15),
                    DateFormat = reader.IsDBNull(16) ? null : reader.GetString(16).Trim(),
                    CustomSpName = reader.IsDBNull(17) ? null : reader.GetString(17).Trim(),
                    Active = !reader.IsDBNull(18) && reader.GetString(18).Trim() == "Y"
                });
            }
        }

        // Load column mappings for each config
        foreach (var config in configs)
        {
            var mappingSql = @"SELECT file_type_code, column_index, source_column_name, target_field,
                data_type, date_format, is_required, default_value, regex_pattern, max_length
                FROM ntfl_column_mapping
                WHERE file_type_code = ?
                ORDER BY column_index";

            using var command = connection.CreateCommand();
            command.CommandText = mappingSql;
            command.CommandType = CommandType.Text;
            AddParameter(command, "@p1", config.FileTypeCode, DbType.String, 10);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                config.ColumnMappings.Add(new GenericColumnMapping
                {
                    FileTypeCode = reader.GetString(0).Trim(),
                    ColumnIndex = reader.GetInt32(1),
                    SourceColumnName = reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                    TargetField = reader.GetString(3).Trim(),
                    DataType = reader.IsDBNull(4) ? "String" : reader.GetString(4).Trim(),
                    DateFormat = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                    IsRequired = !reader.IsDBNull(6) && reader.GetString(6).Trim() == "Y",
                    DefaultValue = reader.IsDBNull(7) ? null : reader.GetString(7).Trim(),
                    RegexPattern = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
                    MaxLength = reader.IsDBNull(9) ? null : reader.GetInt32(9)
                });
            }
        }

        _logger.LogDebug("Loaded {Count} generic file format configs", configs.Count);

        return new DataResult<List<GenericFileFormatConfig>>
        {
            StatusCode = 200,
            Data = configs
        };
    }

    public async Task<RawCommandResult> InsertGenericFileFormatConfigAsync(GenericFileFormatConfig config)
    {
        _logger.LogDebug("Inserting generic file format config: {FileTypeCode}", config.FileTypeCode);

        var sql = @"INSERT INTO ntfl_file_format_config (
            file_type_code, file_format, delimiter, has_header_row,
            skip_rows_top, skip_rows_bottom, row_id_mode, row_id_column,
            header_indicator, trailer_indicator, detail_indicator, skip_indicator,
            total_column_index, total_type, sheet_name, sheet_index,
            date_format, custom_sp_name, active,
            created_tm, created_by, last_updated, updated_by
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, CURRENT, ?, CURRENT, ?)";

        return _dbContext.ExecuteRawCommand(sql,
            ("@p1", config.FileTypeCode, DbType.String, 10),
            ("@p2", config.FileFormat.ToString().ToUpper(), DbType.String, 16),
            ("@p3", config.Delimiter, DbType.String, 8),
            ("@p4", config.HasHeaderRow ? "Y" : "N", DbType.String, 1),
            ("@p5", config.SkipRowsTop, DbType.Int32, null),
            ("@p6", config.SkipRowsBottom, DbType.Int32, null),
            ("@p7", config.RowIdMode.ToString().ToUpper(), DbType.String, 16),
            ("@p8", config.RowIdColumn, DbType.Int32, null),
            ("@p9", config.HeaderIndicator, DbType.String, 64),
            ("@p10", config.TrailerIndicator, DbType.String, 64),
            ("@p11", config.DetailIndicator, DbType.String, 64),
            ("@p12", config.SkipIndicator, DbType.String, 64),
            ("@p13", config.TotalColumnIndex, DbType.Int32, null),
            ("@p14", config.TotalType, DbType.String, 16),
            ("@p15", config.SheetName, DbType.String, 64),
            ("@p16", config.SheetIndex, DbType.Int32, null),
            ("@p17", config.DateFormat, DbType.String, 32),
            ("@p18", config.CustomSpName, DbType.String, 64),
            ("@p19", config.Active ? "Y" : "N", DbType.String, 1),
            ("@p20", config.CreatedBy, DbType.String, 18),
            ("@p21", config.UpdatedBy, DbType.String, 18)
        );
    }

    public async Task<RawCommandResult> UpdateGenericFileFormatConfigAsync(GenericFileFormatConfig config)
    {
        _logger.LogDebug("Updating generic file format config: {FileTypeCode}", config.FileTypeCode);

        var sql = @"UPDATE ntfl_file_format_config SET
            file_format = ?, delimiter = ?, has_header_row = ?,
            skip_rows_top = ?, skip_rows_bottom = ?, row_id_mode = ?, row_id_column = ?,
            header_indicator = ?, trailer_indicator = ?, detail_indicator = ?, skip_indicator = ?,
            total_column_index = ?, total_type = ?, sheet_name = ?, sheet_index = ?,
            date_format = ?, custom_sp_name = ?, active = ?,
            last_updated = CURRENT, updated_by = ?
        WHERE file_type_code = ?";

        return _dbContext.ExecuteRawCommand(sql,
            ("@p1", config.FileFormat.ToString().ToUpper(), DbType.String, 16),
            ("@p2", config.Delimiter, DbType.String, 8),
            ("@p3", config.HasHeaderRow ? "Y" : "N", DbType.String, 1),
            ("@p4", config.SkipRowsTop, DbType.Int32, null),
            ("@p5", config.SkipRowsBottom, DbType.Int32, null),
            ("@p6", config.RowIdMode.ToString().ToUpper(), DbType.String, 16),
            ("@p7", config.RowIdColumn, DbType.Int32, null),
            ("@p8", config.HeaderIndicator, DbType.String, 64),
            ("@p9", config.TrailerIndicator, DbType.String, 64),
            ("@p10", config.DetailIndicator, DbType.String, 64),
            ("@p11", config.SkipIndicator, DbType.String, 64),
            ("@p12", config.TotalColumnIndex, DbType.Int32, null),
            ("@p13", config.TotalType, DbType.String, 16),
            ("@p14", config.SheetName, DbType.String, 64),
            ("@p15", config.SheetIndex, DbType.Int32, null),
            ("@p16", config.DateFormat, DbType.String, 32),
            ("@p17", config.CustomSpName, DbType.String, 64),
            ("@p18", config.Active ? "Y" : "N", DbType.String, 1),
            ("@p19", config.UpdatedBy, DbType.String, 18),
            ("@p20", config.FileTypeCode, DbType.String, 10)
        );
    }

    public async Task<RawCommandResult> DeleteGenericFileFormatConfigAsync(string fileTypeCode)
    {
        _logger.LogDebug("Deleting generic file format config: {FileTypeCode}", fileTypeCode);

        // Delete column mappings first (FK constraint)
        var mappingResult = await DeleteColumnMappingsAsync(fileTypeCode);
        if (!mappingResult.IsSuccess)
            return mappingResult;

        return _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_file_format_config WHERE file_type_code = ?",
            ("@p1", fileTypeCode, DbType.String, 10)
        );
    }

    public async Task<RawCommandResult> DeleteColumnMappingsAsync(string fileTypeCode)
    {
        _logger.LogDebug("Deleting column mappings for: {FileTypeCode}", fileTypeCode);

        return _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_column_mapping WHERE file_type_code = ?",
            ("@p1", fileTypeCode, DbType.String, 10)
        );
    }

    public async Task<RawCommandResult> InsertColumnMappingsBatchAsync(IEnumerable<GenericColumnMapping> mappings)
    {
        var mappingList = mappings.ToList();
        if (mappingList.Count == 0)
            return new RawCommandResult { RowsAffected = 0 };

        _logger.LogDebug("Inserting {Count} column mappings", mappingList.Count);

        var totalRows = 0;
        var connection = _dbContext.GetConnection();

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        DbTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync();

            foreach (var mapping in mappingList)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO ntfl_column_mapping (
                    file_type_code, column_index, source_column_name, target_field,
                    data_type, date_format, is_required, default_value, regex_pattern, max_length
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
                command.CommandType = CommandType.Text;

                AddParameter(command, "@p1", mapping.FileTypeCode, DbType.String, 10);
                AddParameter(command, "@p2", mapping.ColumnIndex, DbType.Int32);
                AddParameter(command, "@p3", mapping.SourceColumnName, DbType.String, 64);
                AddParameter(command, "@p4", mapping.TargetField, DbType.String, 32);
                AddParameter(command, "@p5", mapping.DataType, DbType.String, 16);
                AddParameter(command, "@p6", mapping.DateFormat, DbType.String, 32);
                AddParameter(command, "@p7", mapping.IsRequired ? "Y" : "N", DbType.String, 1);
                AddParameter(command, "@p8", mapping.DefaultValue, DbType.String, 128);
                AddParameter(command, "@p9", mapping.RegexPattern, DbType.String, 255);
                AddParameter(command, "@p10", mapping.MaxLength, DbType.Int32);

                var rowsAffected = await command.ExecuteNonQueryAsync();
                if (rowsAffected < 0)
                {
                    _logger.LogError("Failed to insert column mapping");
                    await transaction.RollbackAsync();
                    return new RawCommandResult { RowsAffected = 0, ErrorMessage = "Insert failed" };
                }
                totalRows += rowsAffected;
            }

            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting column mappings batch");
            if (transaction != null)
                await transaction.RollbackAsync();
            return new RawCommandResult { RowsAffected = 0, ErrorMessage = ex.Message };
        }

        _logger.LogDebug("Inserted {TotalRows} column mappings", totalRows);
        return new RawCommandResult { RowsAffected = totalRows };
    }

    private static FileFormatType ParseFileFormat(string value) => value.ToUpperInvariant() switch
    {
        "CSV" => FileFormatType.CSV,
        "XLSX" => FileFormatType.XLSX,
        "DELIMITED" => FileFormatType.Delimited,
        _ => FileFormatType.CSV
    };

    private static RowIdMode ParseRowIdMode(string value) => value.ToUpperInvariant() switch
    {
        "POSITION" => RowIdMode.Position,
        "INDICATOR" => RowIdMode.Indicator,
        "PATTERN" => RowIdMode.Pattern,
        _ => RowIdMode.Position
    };

    // ============================================
    // Helper Methods
    // ============================================

    private static TransferProtocol ParseProtocol(string value) => value.ToUpper() switch
    {
        "SFTP" => TransferProtocol.Sftp,
        "FTP" => TransferProtocol.Ftp,
        "FILESYSTEM" => TransferProtocol.FileSystem,
        _ => TransferProtocol.Sftp
    };

    private static AuthenticationType ParseAuthType(string value) => value.ToUpper() switch
    {
        "PASSWORD" => AuthenticationType.Password,
        "CERTIFICATE" => AuthenticationType.Certificate,
        "PRIVATEKEY" => AuthenticationType.PrivateKey,
        _ => AuthenticationType.Password
    };

    private static CompressionMethod ParseCompression(string value) => value.ToUpper() switch
    {
        "NONE" => CompressionMethod.None,
        "GZIP" => CompressionMethod.GZip,
        "ZIP" => CompressionMethod.Zip,
        _ => CompressionMethod.GZip
    };

    private static string GetStatus(TransferStatus status) => status switch
    {
        TransferStatus.Pending => "Pending",
        TransferStatus.Downloading => "Downloading",
        TransferStatus.Downloaded => "Downloaded",
        TransferStatus.Processing => "Processing",
        TransferStatus.Processed => "Processed",
        TransferStatus.Error => "Error",
        TransferStatus.Skipped => "Skipped",
        _ => "Unknown"
    };

    // ============================================
    // Lookup Tables: Vendors (networks)
    // ============================================

    public async Task<DataResult<List<VendorRecord>>> GetVendorsAsync()
    {
        var sql = "SELECT network_id, network_narr FROM networks ORDER BY network_id";

        var result = _dbContext.ExecuteRawQuery(
            sql,
            reader => new VendorRecord
            {
                NetworkId = reader.GetString(0).Trim(),
                Network = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim()
            }
        );

        return new DataResult<List<VendorRecord>>
        {
            StatusCode = result.IsSuccess ? 200 : result.StatusCode,
            Data = result.Data,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    public async Task<DataResult<VendorRecord>> GetVendorAsync(string networkId)
    {
        var result = await GetVendorsAsync();
        if (!result.IsSuccess)
            return new DataResult<VendorRecord> { StatusCode = result.StatusCode, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };

        var record = result.Data?.FirstOrDefault(v => v.NetworkId == networkId);
        if (record == null)
            return new DataResult<VendorRecord> { StatusCode = 404, ErrorCode = "FileLoading.VendorNotFound", ErrorMessage = $"Vendor '{networkId}' not found" };

        return new DataResult<VendorRecord> { StatusCode = 200, Data = record };
    }

    public async Task<RawCommandResult> InsertVendorAsync(VendorRecord record)
    {
        return _dbContext.ExecuteRawCommand(
            "INSERT INTO networks (network_id, network_narr) VALUES (?, ?)",
            ("@p1", record.NetworkId, DbType.String, 2),
            ("@p2", record.Network, DbType.String, 64)
        );
    }

    public async Task<RawCommandResult> UpdateVendorAsync(VendorRecord record)
    {
        return _dbContext.ExecuteRawCommand(
            "UPDATE networks SET network_narr = ? WHERE network_id = ?",
            ("@p1", record.Network, DbType.String, 64),
            ("@p2", record.NetworkId, DbType.String, 2)
        );
    }

    public async Task<RawCommandResult> DeleteVendorAsync(string networkId)
    {
        return _dbContext.ExecuteRawCommand(
            "DELETE FROM networks WHERE network_id = ?",
            ("@p1", networkId, DbType.String, 2)
        );
    }

    // ============================================
    // Lookup Tables: File Classes
    // ============================================

    public async Task<DataResult<List<FileClassRecord>>> GetFileClassesAsync()
    {
        var sql = "SELECT file_class_code, file_class_narr FROM file_class ORDER BY file_class_code";

        var result = _dbContext.ExecuteRawQuery(
            sql,
            reader => new FileClassRecord
            {
                FileClassCode = reader.GetString(0).Trim(),
                FileClass = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim()
            }
        );

        return new DataResult<List<FileClassRecord>>
        {
            StatusCode = result.IsSuccess ? 200 : result.StatusCode,
            Data = result.Data,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    public async Task<DataResult<FileClassRecord>> GetFileClassAsync(string fileClassCode)
    {
        var result = await GetFileClassesAsync();
        if (!result.IsSuccess)
            return new DataResult<FileClassRecord> { StatusCode = result.StatusCode, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };

        var record = result.Data?.FirstOrDefault(c => c.FileClassCode == fileClassCode);
        if (record == null)
            return new DataResult<FileClassRecord> { StatusCode = 404, ErrorCode = "FileLoading.FileClassNotFound", ErrorMessage = $"File class '{fileClassCode}' not found" };

        return new DataResult<FileClassRecord> { StatusCode = 200, Data = record };
    }

    public async Task<RawCommandResult> InsertFileClassAsync(FileClassRecord record)
    {
        return _dbContext.ExecuteRawCommand(
            "INSERT INTO file_class (file_class_code, file_class_narr) VALUES (?, ?)",
            ("@p1", record.FileClassCode, DbType.String, 10),
            ("@p2", record.FileClass, DbType.String, 64)
        );
    }

    public async Task<RawCommandResult> UpdateFileClassAsync(FileClassRecord record)
    {
        return _dbContext.ExecuteRawCommand(
            "UPDATE file_class SET file_class_narr = ? WHERE file_class_code = ?",
            ("@p1", record.FileClass, DbType.String, 64),
            ("@p2", record.FileClassCode, DbType.String, 10)
        );
    }

    public async Task<RawCommandResult> DeleteFileClassAsync(string fileClassCode)
    {
        return _dbContext.ExecuteRawCommand(
            "DELETE FROM file_class WHERE file_class_code = ?",
            ("@p1", fileClassCode, DbType.String, 10)
        );
    }

    // ============================================
    // Lookup Tables: File Types
    // ============================================

    public async Task<DataResult<List<FileTypeRecord>>> GetFileTypeRecordsAsync()
    {
        var sql = @"SELECT ft.file_type_code, ft.file_type_narr, ft.file_class_code, ft.network_id,
                           fc.file_class_narr, n.network_narr
                    FROM file_type ft
                    LEFT OUTER JOIN file_class fc ON ft.file_class_code = fc.file_class_code
                    LEFT OUTER JOIN networks n ON ft.network_id = n.network_id
                    ORDER BY ft.file_type_code";

        var result = _dbContext.ExecuteRawQuery(
            sql,
            reader => new FileTypeRecord
            {
                FileTypeCode = reader.GetString(0).Trim(),
                FileType = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim(),
                FileClassCode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim(),
                NetworkId = reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),
                FileClass = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                Network = reader.IsDBNull(5) ? null : reader.GetString(5).Trim()
            }
        );

        return new DataResult<List<FileTypeRecord>>
        {
            StatusCode = result.IsSuccess ? 200 : result.StatusCode,
            Data = result.Data,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    public async Task<DataResult<FileTypeRecord>> GetFileTypeRecordAsync(string fileTypeCode)
    {
        var result = await GetFileTypeRecordsAsync();
        if (!result.IsSuccess)
            return new DataResult<FileTypeRecord> { StatusCode = result.StatusCode, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };

        var record = result.Data?.FirstOrDefault(t => t.FileTypeCode == fileTypeCode);
        if (record == null)
            return new DataResult<FileTypeRecord> { StatusCode = 404, ErrorCode = "FileLoading.FileTypeRecordNotFound", ErrorMessage = $"File type '{fileTypeCode}' not found" };

        return new DataResult<FileTypeRecord> { StatusCode = 200, Data = record };
    }

    public async Task<RawCommandResult> InsertFileTypeAsync(FileTypeRecord record)
    {
        return _dbContext.ExecuteRawCommand(
            @"INSERT INTO file_type (file_type_code, file_type_narr, file_class_code, network_id, comp_dll)
              VALUES (?, ?, ?, ?, 'none')",
            ("@p1", record.FileTypeCode, DbType.String, 10),
            ("@p2", record.FileType, DbType.String, 32),
            ("@p3", record.FileClassCode, DbType.String, 3),
            ("@p4", record.NetworkId, DbType.String, 2)
        );
    }

    public async Task<RawCommandResult> UpdateFileTypeAsync(FileTypeRecord record)
    {
        return _dbContext.ExecuteRawCommand(
            "UPDATE file_type SET file_type_narr = ?, file_class_code = ?, network_id = ? WHERE file_type_code = ?",
            ("@p1", record.FileType, DbType.String, 32),
            ("@p2", record.FileClassCode, DbType.String, 3),
            ("@p3", record.NetworkId, DbType.String, 2),
            ("@p4", record.FileTypeCode, DbType.String, 10)
        );
    }

    public async Task<RawCommandResult> DeleteFileTypeAsync(string fileTypeCode)
    {
        return _dbContext.ExecuteRawCommand(
            "DELETE FROM file_type WHERE file_type_code = ?",
            ("@p1", fileTypeCode, DbType.String, 10)
        );
    }

    // ============================================
    // Lookup Tables: File Type NT
    // ============================================

    public async Task<DataResult<List<FileTypeNtRecord>>> GetFileTypeNtRecordsAsync(string? fileTypeCode = null)
    {
        var sql = @"SELECT ftn.file_type_code, ftn.nt_cust_num, ftn.last_seq,
                           ftn.default_bus_unit, ftn.plan_code, ftn.expected_freq,
                           ftn.freq_files, ft.file_type_narr
                    FROM file_type_nt ftn
                    LEFT OUTER JOIN file_type ft ON ftn.file_type_code = ft.file_type_code";

        if (!string.IsNullOrEmpty(fileTypeCode))
            sql += " WHERE ftn.file_type_code = ?";

        sql += " ORDER BY ftn.file_type_code";

        var parameters = string.IsNullOrEmpty(fileTypeCode)
            ? Array.Empty<(string, object?, DbType, int?)>()
            : new[] { ("@p1", (object?)fileTypeCode, DbType.String, (int?)10) };

        var result = _dbContext.ExecuteRawQuery(
            sql,
            reader => new FileTypeNtRecord
            {
                FileTypeCode = reader.GetString(0).Trim(),
                NtCustNum = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim(),
                LastSeq = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                DefaultBusUnit = reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),
                PlanCode = reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4),
                ExpectedFreq = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                FreqFiles = reader.IsDBNull(6) ? null : (int?)reader.GetInt32(6),
                FileType = reader.IsDBNull(7) ? null : reader.GetString(7).Trim()
            },
            parameters
        );

        return new DataResult<List<FileTypeNtRecord>>
        {
            StatusCode = result.IsSuccess ? 200 : result.StatusCode,
            Data = result.Data,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    public async Task<DataResult<FileTypeNtRecord>> GetFileTypeNtRecordAsync(string fileTypeCode)
    {
        var result = await GetFileTypeNtRecordsAsync(fileTypeCode);
        if (!result.IsSuccess)
            return new DataResult<FileTypeNtRecord> { StatusCode = result.StatusCode, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };

        var record = result.Data?.FirstOrDefault();
        if (record == null)
            return new DataResult<FileTypeNtRecord> { StatusCode = 404, ErrorCode = "FileLoading.FileTypeNtNotFound", ErrorMessage = $"File type NT '{fileTypeCode}' not found" };

        return new DataResult<FileTypeNtRecord> { StatusCode = 200, Data = record };
    }

    public async Task<RawCommandResult> InsertFileTypeNtAsync(FileTypeNtRecord record)
    {
        return _dbContext.ExecuteRawCommand(
            @"INSERT INTO file_type_nt (file_type_code, nt_cust_num, last_seq,
                default_bus_unit, plan_code, expected_freq, freq_files,
                created_tm, created_by, last_updated, updated_by)
              VALUES (?, ?, ?, ?, ?, ?, ?, CURRENT, ?, CURRENT, ?)",
            ("@p1", record.FileTypeCode, DbType.String, 10),
            ("@p2", record.NtCustNum, DbType.String, 10),
            ("@p3", record.LastSeq, DbType.Int32, null),
            ("@p4", record.DefaultBusUnit, DbType.String, 2),
            ("@p5", record.PlanCode, DbType.Int32, null),
            ("@p6", record.ExpectedFreq, DbType.String, 1),
            ("@p7", record.FreqFiles, DbType.Int32, null),
            ("@p8", record.CreatedBy, DbType.String, 18),
            ("@p9", record.UpdatedBy, DbType.String, 18)
        );
    }

    public async Task<RawCommandResult> UpdateFileTypeNtAsync(FileTypeNtRecord record)
    {
        return _dbContext.ExecuteRawCommand(
            @"UPDATE file_type_nt SET nt_cust_num = ?, last_seq = ?,
                default_bus_unit = ?, plan_code = ?, expected_freq = ?, freq_files = ?,
                last_updated = CURRENT, updated_by = ?
              WHERE file_type_code = ?",
            ("@p1", record.NtCustNum, DbType.String, 10),
            ("@p2", record.LastSeq, DbType.Int32, null),
            ("@p3", record.DefaultBusUnit, DbType.String, 2),
            ("@p4", record.PlanCode, DbType.Int32, null),
            ("@p5", record.ExpectedFreq, DbType.String, 1),
            ("@p6", record.FreqFiles, DbType.Int32, null),
            ("@p7", record.UpdatedBy, DbType.String, 18),
            ("@p8", record.FileTypeCode, DbType.String, 10)
        );
    }

    public async Task<RawCommandResult> DeleteFileTypeNtAsync(string fileTypeCode)
    {
        return _dbContext.ExecuteRawCommand(
            "DELETE FROM file_type_nt WHERE file_type_code = ?",
            ("@p1", fileTypeCode, DbType.String, 10)
        );
    }

    // ============================================
    // AI Review
    // ============================================

    public async Task<DataResult<AiReviewResponse>> GetCachedAiReviewAsync(int ntFileNum)
    {
        _logger.LogDebug("Getting cached AI review for file {NtFileNum}", ntFileNum);

        var result = _dbContext.ExecuteRawQuery<AiReviewResponse>(
            @"SELECT review_id, nt_file_num, file_type_code, overall_assessment, summary,
                     issues_json, records_sampled, total_records, input_tokens, output_tokens,
                     model_used, reviewed_at, expires_at
              FROM ntfl_ai_review
              WHERE nt_file_num = ?
              ORDER BY reviewed_at DESC",
            reader =>
            {
                var issuesJson = reader.IsDBNull(5) ? "[]" : reader.GetString(5);
                var issues = new List<AiReviewIssue>();
                try
                {
                    issues = JsonSerializer.Deserialize<List<AiReviewIssue>>(issuesJson,
                        new JsonSerializerOptions { PropertyNamingPolicy = null }) ?? new();
                }
                catch { /* ignore deserialization errors */ }

                return new AiReviewResponse
                {
                    NtFileNum = reader.GetInt32(1),
                    FileType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim(),
                    OverallAssessment = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim(),
                    Summary = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Issues = issues,
                    RecordsSampled = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                    TotalRecords = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    ReviewedAt = reader.IsDBNull(11) ? DateTime.MinValue : reader.GetDateTime(11),
                    IsCached = true,
                    Usage = new AiReviewUsage
                    {
                        InputTokens = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                        OutputTokens = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                        Model = reader.IsDBNull(10) ? string.Empty : reader.GetString(10).Trim()
                    }
                };
            },
            ("@p1", ntFileNum, DbType.Int32, null)
        );

        if (!result.IsSuccess)
        {
            return new DataResult<AiReviewResponse>
            {
                StatusCode = result.StatusCode,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };
        }

        if (result.Data.Count == 0)
        {
            return new DataResult<AiReviewResponse>
            {
                StatusCode = 404,
                ErrorCode = "FileLoading.AiReviewNotFound",
                ErrorMessage = "No AI review found for this file. Use POST to trigger one."
            };
        }

        // Check if expired
        return new DataResult<AiReviewResponse>
        {
            StatusCode = 200,
            Data = result.Data[0]
        };
    }

    public async Task<RawCommandResult> StoreAiReviewAsync(AiReviewResponse review, string reviewedBy, DateTime expiresAt)
    {
        _logger.LogDebug("Storing AI review for file {NtFileNum}", review.NtFileNum);

        // Delete any existing review for this file
        _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_ai_review WHERE nt_file_num = ?",
            ("@p1", review.NtFileNum, DbType.Int32, null)
        );

        var issuesJson = JsonSerializer.Serialize(review.Issues, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false
        });

        return _dbContext.ExecuteRawCommand(
            @"INSERT INTO ntfl_ai_review
              (nt_file_num, file_type_code, overall_assessment, summary, issues_json,
               records_sampled, total_records, input_tokens, output_tokens, model_used,
               reviewed_at, reviewed_by, expires_at)
              VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            ("@p1", review.NtFileNum, DbType.Int32, null),
            ("@p2", review.FileType, DbType.String, 20),
            ("@p3", review.OverallAssessment, DbType.String, 20),
            ("@p4", review.Summary, DbType.String, 4000),
            ("@p5", issuesJson, DbType.String, 32000),
            ("@p6", review.RecordsSampled, DbType.Int32, null),
            ("@p7", review.TotalRecords, DbType.Int32, null),
            ("@p8", review.Usage?.InputTokens ?? 0, DbType.Int32, null),
            ("@p9", review.Usage?.OutputTokens ?? 0, DbType.Int32, null),
            ("@p10", review.Usage?.Model ?? string.Empty, DbType.String, 50),
            ("@p11", review.ReviewedAt, DbType.DateTime, null),
            ("@p12", reviewedBy, DbType.String, 50),
            ("@p13", expiresAt, DbType.DateTime, null)
        );
    }

    public async Task<RawCommandResult> DeleteAiReviewAsync(int ntFileNum)
    {
        return _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_ai_review WHERE nt_file_num = ?",
            ("@p1", ntFileNum, DbType.Int32, null)
        );
    }

    // ============================================
    // AI Example Files
    // ============================================

    public async Task<DataResult<List<ExampleFileRecord>>> GetAllExampleFilesAsync()
    {
        var result = _dbContext.ExecuteRawQuery<ExampleFileRecord>(
            "SELECT example_file_id, file_type_code, file_path, file_name, description, created_tm, created_by, last_updated, updated_by FROM ntfl_ai_example_file ORDER BY file_type_code, example_file_id",
            reader => new ExampleFileRecord
            {
                ExampleFileId = reader.GetInt32(0),
                FileTypeCode = reader.GetString(1).Trim(),
                FilePath = reader.GetString(2).Trim(),
                FileName = reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                CreatedAt = reader.GetDateTime(5),
                CreatedBy = reader.GetString(6).Trim(),
                UpdatedAt = reader.GetDateTime(7),
                UpdatedBy = reader.GetString(8).Trim()
            }
        );

        return new DataResult<List<ExampleFileRecord>>
        {
            StatusCode = result.IsSuccess ? 200 : result.StatusCode,
            Data = result.IsSuccess ? result.Data : null,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    public async Task<DataResult<List<ExampleFileRecord>>> GetExampleFilesByTypeAsync(string fileTypeCode)
    {
        var result = _dbContext.ExecuteRawQuery<ExampleFileRecord>(
            "SELECT example_file_id, file_type_code, file_path, file_name, description, created_tm, created_by, last_updated, updated_by FROM ntfl_ai_example_file WHERE file_type_code = ? ORDER BY example_file_id",
            reader => new ExampleFileRecord
            {
                ExampleFileId = reader.GetInt32(0),
                FileTypeCode = reader.GetString(1).Trim(),
                FilePath = reader.GetString(2).Trim(),
                FileName = reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                CreatedAt = reader.GetDateTime(5),
                CreatedBy = reader.GetString(6).Trim(),
                UpdatedAt = reader.GetDateTime(7),
                UpdatedBy = reader.GetString(8).Trim()
            },
            ("@p1", fileTypeCode, DbType.String, 20)
        );

        return new DataResult<List<ExampleFileRecord>>
        {
            StatusCode = result.IsSuccess ? 200 : result.StatusCode,
            Data = result.IsSuccess ? result.Data : null,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    public async Task<DataResult<ExampleFileRecord>> GetExampleFileByIdAsync(int exampleFileId)
    {
        var result = _dbContext.ExecuteRawQuery<ExampleFileRecord>(
            "SELECT example_file_id, file_type_code, file_path, file_name, description, created_tm, created_by, last_updated, updated_by FROM ntfl_ai_example_file WHERE example_file_id = ?",
            reader => new ExampleFileRecord
            {
                ExampleFileId = reader.GetInt32(0),
                FileTypeCode = reader.GetString(1).Trim(),
                FilePath = reader.GetString(2).Trim(),
                FileName = reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                CreatedAt = reader.GetDateTime(5),
                CreatedBy = reader.GetString(6).Trim(),
                UpdatedAt = reader.GetDateTime(7),
                UpdatedBy = reader.GetString(8).Trim()
            },
            ("@p1", exampleFileId, DbType.Int32, null)
        );

        if (!result.IsSuccess)
        {
            return new DataResult<ExampleFileRecord>
            {
                StatusCode = result.StatusCode,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };
        }

        if (result.Data.Count == 0)
        {
            return new DataResult<ExampleFileRecord>
            {
                StatusCode = 404,
                ErrorCode = "FileLoading.ExampleFileNotFound",
                ErrorMessage = $"No example file found with ID {exampleFileId}"
            };
        }

        return new DataResult<ExampleFileRecord>
        {
            StatusCode = 200,
            Data = result.Data[0]
        };
    }

    public async Task<RawCommandResult> InsertExampleFileAsync(ExampleFileRecord record)
    {
        return _dbContext.ExecuteRawCommand(
            @"INSERT INTO ntfl_ai_example_file (file_type_code, file_path, file_name, description,
              created_tm, created_by, last_updated, updated_by)
              VALUES (?, ?, ?, ?, CURRENT, ?, CURRENT, ?)",
            ("@p1", record.FileTypeCode, DbType.String, 20),
            ("@p2", record.FilePath, DbType.String, 500),
            ("@p3", (object?)record.FileName ?? DBNull.Value, DbType.String, 255),
            ("@p4", (object?)record.Description ?? DBNull.Value, DbType.String, 200),
            ("@p5", record.CreatedBy, DbType.String, 18),
            ("@p6", record.UpdatedBy, DbType.String, 18)
        );
    }

    public async Task<RawCommandResult> DeleteExampleFileAsync(int exampleFileId)
    {
        return _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_ai_example_file WHERE example_file_id = ?",
            ("@p1", exampleFileId, DbType.Int32, null)
        );
    }

    // ============================================
    // AI Domain Config
    // ============================================

    public async Task<DataResult<AiDomainConfig>> GetAiDomainConfigAsync()
    {
        var result = _dbContext.ExecuteRawQuery<AiDomainConfig>(
            @"SELECT config_id, api_key, model, enabled, max_reviews_day, max_output_tokens,
                     reviews_today, reviews_reset_dt, created_tm, created_by, last_updated, updated_by
              FROM ntfl_ai_domain_config",
            reader => new AiDomainConfig
            {
                ConfigId = reader.GetInt32(0),
                ApiKey = reader.GetString(1).Trim(),
                Model = reader.IsDBNull(2) ? "claude-sonnet-4-20250514" : reader.GetString(2).Trim(),
                Enabled = reader.IsDBNull(3) || reader.GetString(3).Trim() == "Y",
                MaxReviewsPerDay = reader.IsDBNull(4) ? 50 : reader.GetInt32(4),
                MaxOutputTokens = reader.IsDBNull(5) ? 4096 : reader.GetInt32(5),
                ReviewsToday = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                ReviewsResetDt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                CreatedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                CreatedBy = reader.IsDBNull(9) ? null : reader.GetString(9).Trim(),
                UpdatedAt = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                UpdatedBy = reader.IsDBNull(11) ? null : reader.GetString(11).Trim()
            }
        );

        if (!result.IsSuccess)
        {
            return new DataResult<AiDomainConfig>
            {
                StatusCode = result.StatusCode,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };
        }

        if (result.Data.Count == 0)
        {
            return new DataResult<AiDomainConfig>
            {
                StatusCode = 404,
                ErrorCode = "FileLoading.AiNotConfigured",
                ErrorMessage = "AI review has not been configured. Use PUT /ai-review/config to set up your API key."
            };
        }

        return new DataResult<AiDomainConfig>
        {
            StatusCode = 200,
            Data = result.Data[0]
        };
    }

    public async Task<RawCommandResult> UpsertAiDomainConfigAsync(AiDomainConfig config)
    {
        var existsResult = _dbContext.ExecuteRawScalar<int>(
            "SELECT COUNT(*) FROM ntfl_ai_domain_config"
        );

        if (existsResult.IsSuccess && existsResult.Value > 0)
        {
            return _dbContext.ExecuteRawCommand(
                @"UPDATE ntfl_ai_domain_config SET
                    api_key = ?, model = ?, enabled = ?, max_reviews_day = ?,
                    max_output_tokens = ?, last_updated = CURRENT, updated_by = ?
                  WHERE config_id = 1",
                ("@p1", config.ApiKey, DbType.String, 200),
                ("@p2", config.Model, DbType.String, 50),
                ("@p3", config.Enabled ? "Y" : "N", DbType.String, 1),
                ("@p4", config.MaxReviewsPerDay, DbType.Int32, null),
                ("@p5", config.MaxOutputTokens, DbType.Int32, null),
                ("@p6", config.UpdatedBy ?? "SYSTEM", DbType.String, 18)
            );
        }

        return _dbContext.ExecuteRawCommand(
            @"INSERT INTO ntfl_ai_domain_config
              (api_key, model, enabled, max_reviews_day, max_output_tokens,
               reviews_today, reviews_reset_dt, created_tm, created_by, last_updated, updated_by)
              VALUES (?, ?, ?, ?, ?, 0, TODAY, CURRENT, ?, CURRENT, ?)",
            ("@p1", config.ApiKey, DbType.String, 200),
            ("@p2", config.Model, DbType.String, 50),
            ("@p3", config.Enabled ? "Y" : "N", DbType.String, 1),
            ("@p4", config.MaxReviewsPerDay, DbType.Int32, null),
            ("@p5", config.MaxOutputTokens, DbType.Int32, null),
            ("@p6", config.CreatedBy ?? "SYSTEM", DbType.String, 18),
            ("@p7", config.UpdatedBy ?? "SYSTEM", DbType.String, 18)
        );
    }

    public async Task<RawCommandResult> DeleteAiDomainConfigAsync()
    {
        return _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_ai_domain_config"
        );
    }

    public async Task<RawCommandResult> IncrementAiReviewCountAsync()
    {
        return _dbContext.ExecuteRawCommand(
            "UPDATE ntfl_ai_domain_config SET reviews_today = reviews_today + 1 WHERE config_id = 1"
        );
    }

    public async Task<RawCommandResult> ResetAiReviewCountAsync()
    {
        return _dbContext.ExecuteRawCommand(
            "UPDATE ntfl_ai_domain_config SET reviews_today = 0, reviews_reset_dt = TODAY WHERE config_id = 1"
        );
    }

    // ============================================
    // FTP Server Configuration
    // ============================================

    private static FtpServer MapFtpServer(IDataReader reader) => new()
    {
        ServerId = reader.GetInt32(0),
        ServerName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim(),
        Protocol = ParseFtpProtocol(reader.IsDBNull(2) ? "SFTP" : reader.GetString(2).Trim()),
        Host = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim(),
        Port = reader.IsDBNull(4) ? 22 : reader.GetInt32(4),
        AuthType = ParseAuthType(reader.IsDBNull(5) ? "PASSWORD" : reader.GetString(5).Trim()),
        Username = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
        Password = reader.IsDBNull(7) ? null : reader.GetString(7).Trim(),
        CertificatePath = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
        PrivateKeyPath = reader.IsDBNull(9) ? null : reader.GetString(9).Trim(),
        RootPath = reader.IsDBNull(10) ? "/" : reader.GetString(10).Trim(),
        TempLocalPath = reader.IsDBNull(11) ? null : reader.GetString(11).Trim(),
        IsActive = !reader.IsDBNull(12) && reader.GetString(12).Trim() == "Y",
        CreatedAt = reader.GetDateTime(13),
        CreatedBy = reader.IsDBNull(14) ? string.Empty : reader.GetString(14).Trim(),
        UpdatedAt = reader.GetDateTime(15),
        UpdatedBy = reader.IsDBNull(16) ? string.Empty : reader.GetString(16).Trim()
    };

    private const string FtpServerSelectColumns = @"server_id, server_name, protocol, host, port,
        auth_type, username, password_enc, certificate_path, private_key_path,
        root_path, temp_local_path, is_active, created_tm, created_by, last_updated, updated_by";

    public async Task<DataResult<List<FtpServer>>> GetFtpServersAsync()
    {
        _logger.LogDebug("Getting all FTP servers");

        var sql = $"SELECT {FtpServerSelectColumns} FROM ntfl_ftp_server ORDER BY server_id";

        var result = _dbContext.ExecuteRawQuery(sql, MapFtpServer);

        if (!result.IsSuccess)
            return new DataResult<List<FtpServer>> { StatusCode = result.StatusCode, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };

        // Compute IsLocked for each server
        foreach (var server in result.Data)
        {
            server.IsLocked = await IsFtpServerLockedAsync(server.ServerId);
        }

        return new DataResult<List<FtpServer>> { StatusCode = 200, Data = result.Data };
    }

    public async Task<DataResult<FtpServer>> GetFtpServerAsync(int serverId)
    {
        _logger.LogDebug("Getting FTP server: {ServerId}", serverId);

        var sql = $"SELECT {FtpServerSelectColumns} FROM ntfl_ftp_server WHERE server_id = ?";

        var result = _dbContext.ExecuteRawQuery(sql, MapFtpServer,
            ("@p1", serverId, DbType.Int32, null));

        if (!result.IsSuccess)
            return new DataResult<FtpServer> { StatusCode = result.StatusCode, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };

        if (result.Data.Count == 0)
            return new DataResult<FtpServer> { StatusCode = 404, ErrorCode = "FileLoading.FtpServerNotFound", ErrorMessage = $"FTP server {serverId} not found" };

        var server = result.Data[0];
        server.IsLocked = await IsFtpServerLockedAsync(serverId);

        return new DataResult<FtpServer> { StatusCode = 200, Data = server };
    }

    public async Task<DataResult<FtpServer?>> GetActiveFtpServerAsync()
    {
        _logger.LogDebug("Getting active FTP server");

        var sql = $"SELECT {FtpServerSelectColumns} FROM ntfl_ftp_server WHERE is_active = 'Y'";

        var result = _dbContext.ExecuteRawQuery(sql, MapFtpServer);

        if (!result.IsSuccess)
            return new DataResult<FtpServer?> { StatusCode = result.StatusCode, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };

        if (result.Data.Count == 0)
            return new DataResult<FtpServer?> { StatusCode = 200, Data = null };

        var server = result.Data[0];
        server.IsLocked = await IsFtpServerLockedAsync(server.ServerId);

        return new DataResult<FtpServer?> { StatusCode = 200, Data = server };
    }

    public async Task<ValueResult<int>> InsertFtpServerAsync(FtpServer server)
    {
        _logger.LogDebug("Inserting FTP server: {ServerName}", server.ServerName);

        var protocol = server.Protocol.ToString().ToUpper();
        var authType = server.AuthType.ToString().ToUpper();
        var isActive = server.IsActive ? "Y" : "N";

        var sql = @"INSERT INTO ntfl_ftp_server (
            server_name, protocol, host, port, auth_type,
            username, password_enc, certificate_path, private_key_path,
            root_path, temp_local_path, is_active,
            created_tm, created_by, last_updated, updated_by
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, CURRENT, ?, CURRENT, ?)";

        var result = _dbContext.ExecuteRawCommand(sql,
            ("@p1", server.ServerName, DbType.String, 64),
            ("@p2", protocol, DbType.String, 16),
            ("@p3", server.Host, DbType.String, 255),
            ("@p4", server.Port, DbType.Int32, null),
            ("@p5", authType, DbType.String, 16),
            ("@p6", server.Username, DbType.String, 64),
            ("@p7", server.Password, DbType.String, 512),
            ("@p8", server.CertificatePath, DbType.String, 255),
            ("@p9", server.PrivateKeyPath, DbType.String, 255),
            ("@p10", server.RootPath, DbType.String, 255),
            ("@p11", server.TempLocalPath, DbType.String, 255),
            ("@p12", isActive, DbType.String, 1),
            ("@p13", server.CreatedBy, DbType.String, 18),
            ("@p14", server.UpdatedBy, DbType.String, 18)
        );

        if (!result.IsSuccess)
            return new ValueResult<int> { StatusCode = 500, ErrorCode = "FileLoading.DatabaseError", ErrorMessage = result.ErrorMessage };

        var idResult = _dbContext.ExecuteRawScalar<int>(
            "SELECT MAX(server_id) FROM ntfl_ftp_server WHERE server_name = ? AND created_by = ?",
            ("@p1", server.ServerName, DbType.String, 64),
            ("@p2", server.CreatedBy, DbType.String, 18)
        );

        return new ValueResult<int> { StatusCode = 201, Value = idResult.IsSuccess ? idResult.Value : 0 };
    }

    public async Task<RawCommandResult> UpdateFtpServerAsync(FtpServer server)
    {
        _logger.LogDebug("Updating FTP server: {ServerId}", server.ServerId);

        var protocol = server.Protocol.ToString().ToUpper();
        var authType = server.AuthType.ToString().ToUpper();

        var sql = @"UPDATE ntfl_ftp_server SET
            server_name = ?, protocol = ?, host = ?, port = ?, auth_type = ?,
            username = ?, password_enc = ?, certificate_path = ?, private_key_path = ?,
            root_path = ?, temp_local_path = ?,
            last_updated = CURRENT, updated_by = ?
        WHERE server_id = ?";

        return _dbContext.ExecuteRawCommand(sql,
            ("@p1", server.ServerName, DbType.String, 64),
            ("@p2", protocol, DbType.String, 16),
            ("@p3", server.Host, DbType.String, 255),
            ("@p4", server.Port, DbType.Int32, null),
            ("@p5", authType, DbType.String, 16),
            ("@p6", server.Username, DbType.String, 64),
            ("@p7", server.Password, DbType.String, 512),
            ("@p8", server.CertificatePath, DbType.String, 255),
            ("@p9", server.PrivateKeyPath, DbType.String, 255),
            ("@p10", server.RootPath, DbType.String, 255),
            ("@p11", server.TempLocalPath, DbType.String, 255),
            ("@p12", server.UpdatedBy, DbType.String, 18),
            ("@p13", server.ServerId, DbType.Int32, null)
        );
    }

    public async Task<RawCommandResult> DeleteFtpServerAsync(int serverId)
    {
        _logger.LogDebug("Deleting FTP server: {ServerId}", serverId);

        return _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_ftp_server WHERE server_id = ?",
            ("@p1", serverId, DbType.Int32, null)
        );
    }

    public async Task<RawCommandResult> ActivateFtpServerAsync(int serverId)
    {
        _logger.LogDebug("Activating FTP server: {ServerId}", serverId);

        // Deactivate all first
        _dbContext.ExecuteRawCommand("UPDATE ntfl_ftp_server SET is_active = 'N' WHERE is_active = 'Y'");

        // Activate target
        return _dbContext.ExecuteRawCommand(
            "UPDATE ntfl_ftp_server SET is_active = 'Y', last_updated = CURRENT WHERE server_id = ?",
            ("@p1", serverId, DbType.Int32, null)
        );
    }

    public async Task<RawCommandResult> DeactivateAllFtpServersAsync()
    {
        _logger.LogDebug("Deactivating all FTP servers");

        return _dbContext.ExecuteRawCommand("UPDATE ntfl_ftp_server SET is_active = 'N' WHERE is_active = 'Y'");
    }

    public async Task<bool> IsFtpServerLockedAsync(int serverId)
    {
        var result = _dbContext.ExecuteRawScalar<int>(
            "SELECT COUNT(*) FROM ntfl_transfer WHERE ftp_server_id = ?",
            ("@p1", serverId, DbType.Int32, null)
        );

        return result.IsSuccess && result.Value > 0;
    }

    private static TransferProtocol ParseFtpProtocol(string value)
    {
        return value.ToUpper() switch
        {
            "SFTP" => TransferProtocol.Sftp,
            "FTP" => TransferProtocol.Ftp,
            "FILESYSTEM" => TransferProtocol.FileSystem,
            _ => TransferProtocol.Sftp
        };
    }

    // ============================================
    // Custom Table Management
    // ============================================

    public async Task<DataResult<List<CustomTableMetadata>>> GetCustomTablesAsync(string fileTypeCode)
    {
        var sql = @"SELECT custom_table_id, file_type_code, table_name, version, status,
            column_count, column_definition, created_dt, created_by, dropped_dt
            FROM ntfl_custom_table
            WHERE file_type_code = ?
            ORDER BY version";

        var connection = _dbContext.GetConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        var results = new List<CustomTableMetadata>();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        AddParameter(command, "@p1", fileTypeCode, DbType.String, 10);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(ReadCustomTableMetadata(reader));
        }

        return new DataResult<List<CustomTableMetadata>> { Data = results };
    }

    public async Task<CustomTableMetadata?> GetActiveCustomTableAsync(string fileTypeCode)
    {
        var sql = @"SELECT custom_table_id, file_type_code, table_name, version, status,
            column_count, column_definition, created_dt, created_by, dropped_dt
            FROM ntfl_custom_table
            WHERE file_type_code = ? AND status = 'ACTIVE'";

        var connection = _dbContext.GetConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        AddParameter(command, "@p1", fileTypeCode, DbType.String, 10);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadCustomTableMetadata(reader);
        }

        return null;
    }

    public async Task<CustomTableMetadata?> GetCustomTableByVersionAsync(string fileTypeCode, int version)
    {
        var sql = @"SELECT custom_table_id, file_type_code, table_name, version, status,
            column_count, column_definition, created_dt, created_by, dropped_dt
            FROM ntfl_custom_table
            WHERE file_type_code = ? AND version = ?";

        var connection = _dbContext.GetConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        AddParameter(command, "@p1", fileTypeCode, DbType.String, 10);
        AddParameter(command, "@p2", version, DbType.Int32);

        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadCustomTableMetadata(reader);
        }

        return null;
    }

    public async Task<ValueResult<int>> InsertCustomTableMetadataAsync(CustomTableMetadata metadata)
    {
        var sql = @"INSERT INTO ntfl_custom_table (
            file_type_code, table_name, version, status, column_count,
            column_definition, created_by
        ) VALUES (?, ?, ?, ?, ?, ?, ?)";

        var connection = _dbContext.GetConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        AddParameter(command, "@p1", metadata.FileTypeCode, DbType.String, 10);
        AddParameter(command, "@p2", metadata.TableName, DbType.String, 64);
        AddParameter(command, "@p3", metadata.Version, DbType.Int32);
        AddParameter(command, "@p4", metadata.Status, DbType.String, 10);
        AddParameter(command, "@p5", metadata.ColumnCount, DbType.Int32);
        AddParameter(command, "@p6", metadata.ColumnDefinition, DbType.String, 4000);
        AddParameter(command, "@p7", metadata.CreatedBy, DbType.String, 30);

        await command.ExecuteNonQueryAsync();

        // Get the auto-generated ID
        using var idCommand = connection.CreateCommand();
        idCommand.CommandText = "SELECT DBINFO('sqlca.sqlerrd1') FROM systables WHERE tabid = 1";
        idCommand.CommandType = CommandType.Text;
        var id = Convert.ToInt32(await idCommand.ExecuteScalarAsync());

        return new ValueResult<int> { Value = id };
    }

    public async Task<RawCommandResult> UpdateCustomTableStatusAsync(int customTableId, string status, DateTime? droppedDt = null)
    {
        var sql = "UPDATE ntfl_custom_table SET status = ?, dropped_dt = ? WHERE custom_table_id = ?";

        var connection = _dbContext.GetConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        AddParameter(command, "@p1", status, DbType.String, 10);
        AddParameter(command, "@p2", droppedDt, DbType.DateTime);
        AddParameter(command, "@p3", customTableId, DbType.Int32);

        var rows = await command.ExecuteNonQueryAsync();
        return new RawCommandResult { RowsAffected = rows };
    }

    public async Task<int> GetLiveRecordCountAsync(string tableName)
    {
        // Validate table name to prevent SQL injection
        if (!IsValidTableName(tableName))
            throw new ArgumentException($"Invalid table name: {tableName}");

        var sql = $"SELECT COUNT(*) FROM {tableName}";

        var connection = _dbContext.GetConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<RawCommandResult> ExecuteCreateTableAsync(string ddl)
    {
        var connection = _dbContext.GetConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = ddl;
        command.CommandType = CommandType.Text;

        try
        {
            await command.ExecuteNonQueryAsync();
            _logger.LogInformation("Successfully executed CREATE TABLE DDL");
            return new RawCommandResult { RowsAffected = 0 };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute CREATE TABLE DDL");
            return new RawCommandResult { RowsAffected = 0, ErrorMessage = ex.Message };
        }
    }

    public async Task<RawCommandResult> DropTableAsync(string tableName)
    {
        if (!IsValidTableName(tableName))
            throw new ArgumentException($"Invalid table name: {tableName}");

        var connection = _dbContext.GetConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE {tableName}";
        command.CommandType = CommandType.Text;

        try
        {
            await command.ExecuteNonQueryAsync();
            _logger.LogInformation("Successfully dropped table {TableName}", tableName);
            return new RawCommandResult { RowsAffected = 0 };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to drop table {TableName}", tableName);
            return new RawCommandResult { RowsAffected = 0, ErrorMessage = ex.Message };
        }
    }

    public async Task<RawCommandResult> InsertCustomTableBatchAsync(
        string tableName,
        List<GenericColumnMapping> mappings,
        IEnumerable<GenericDetailRecord> records,
        int transactionBatchSize = 1000)
    {
        if (!IsValidTableName(tableName))
            throw new ArgumentException($"Invalid table name: {tableName}");

        var recordList = records.ToList();
        if (recordList.Count == 0)
            return new RawCommandResult { RowsAffected = 0 };

        // Build column names and placeholder list from mappings
        var columnNames = new List<string> { "nt_file_num", "nt_file_rec_num" };
        foreach (var mapping in mappings.OrderBy(m => m.ColumnIndex))
        {
            columnNames.Add(CustomTableHelper.ToSnakeCase(mapping.TargetField));
        }
        columnNames.Add("status_id");

        var placeholders = string.Join(", ", columnNames.Select(_ => "?"));
        var insertSql = $"INSERT INTO {tableName} ({string.Join(", ", columnNames)}) VALUES ({placeholders})";

        _logger.LogDebug("Inserting {Count} records into custom table {TableName} with transaction batching",
            recordList.Count, tableName);

        var totalRows = 0;
        var connection = _dbContext.GetConnection();

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        foreach (var batch in recordList.Chunk(transactionBatchSize))
        {
            DbTransaction? transaction = null;
            try
            {
                transaction = await connection.BeginTransactionAsync();

                foreach (var record in batch)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = insertSql;
                    command.CommandType = CommandType.Text;

                    // Fixed columns
                    AddParameter(command, "@p_ntfilenum", record.NtFileNum, DbType.Int32);
                    AddParameter(command, "@p_ntfilerecnum", record.NtFileRecNum, DbType.Int32);

                    // Dynamic columns from mappings
                    var paramIdx = 3;
                    foreach (var mapping in mappings.OrderBy(m => m.ColumnIndex))
                    {
                        var value = GetRecordValueByTargetField(record, mapping.TargetField);
                        var dbType = CustomTableHelper.MapToDbType(mapping.DataType);
                        var size = mapping.DataType.Equals("String", StringComparison.OrdinalIgnoreCase)
                            ? mapping.MaxLength ?? 128
                            : (int?)null;
                        AddParameter(command, $"@p{paramIdx}", value, dbType, size);
                        paramIdx++;
                    }

                    // Status
                    AddParameter(command, $"@p{paramIdx}", record.StatusId, DbType.Int32);

                    var rowsAffected = await command.ExecuteNonQueryAsync();
                    if (rowsAffected < 0)
                    {
                        _logger.LogError("Failed to insert record into custom table {TableName}", tableName);
                        await transaction.RollbackAsync();
                        return new RawCommandResult { RowsAffected = 0, ErrorMessage = "Insert failed" };
                    }
                    totalRows += rowsAffected;
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during custom table batch insert into {TableName}, rolling back", tableName);
                if (transaction != null)
                    await transaction.RollbackAsync();
                throw;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        _logger.LogDebug("Completed inserting {TotalRows} records into custom table {TableName}", totalRows, tableName);
        return new RawCommandResult { RowsAffected = totalRows };
    }

    public async Task<RawCommandResult> DeleteCustomTableRecordsAsync(string tableName, int ntFileNum)
    {
        if (!IsValidTableName(tableName))
            throw new ArgumentException($"Invalid table name: {tableName}");

        var sql = $"DELETE FROM {tableName} WHERE nt_file_num = ?";

        var connection = _dbContext.GetConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        AddParameter(command, "@p1", ntFileNum, DbType.Int32);

        var rows = await command.ExecuteNonQueryAsync();
        return new RawCommandResult { RowsAffected = rows };
    }

    public async Task<string?> GetFileTypeCodeForFileAsync(int ntFileNum)
    {
        var sql = "SELECT file_type_code FROM nt_file WHERE nt_file_num = ?";

        var connection = _dbContext.GetConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        AddParameter(command, "@p1", ntFileNum, DbType.Int32);

        var result = await command.ExecuteScalarAsync();
        return result?.ToString()?.Trim();
    }

    public async Task<RawCommandResult> DeleteNtFileAsync(int ntFileNum)
    {
        var connection = _dbContext.GetConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        // Delete trailer first
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM nt_fl_trailer WHERE nt_file_num = ?";
            cmd.CommandType = CommandType.Text;
            AddParameter(cmd, "@p1", ntFileNum, DbType.Int32);
            await cmd.ExecuteNonQueryAsync();
        }

        // Delete header
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM nt_fl_header WHERE nt_file_num = ?";
            cmd.CommandType = CommandType.Text;
            AddParameter(cmd, "@p1", ntFileNum, DbType.Int32);
            await cmd.ExecuteNonQueryAsync();
        }

        // Delete nt_file
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM nt_file WHERE nt_file_num = ?";
        command.CommandType = CommandType.Text;
        AddParameter(command, "@p1", ntFileNum, DbType.Int32);

        var rows = await command.ExecuteNonQueryAsync();
        return new RawCommandResult { RowsAffected = rows };
    }

    // ============================================
    // Custom Table Helpers
    // ============================================

    private static CustomTableMetadata ReadCustomTableMetadata(DbDataReader reader)
    {
        return new CustomTableMetadata
        {
            CustomTableId = reader.GetInt32(0),
            FileTypeCode = reader.GetString(1).Trim(),
            TableName = reader.GetString(2).Trim(),
            Version = reader.GetInt32(3),
            Status = reader.GetString(4).Trim(),
            ColumnCount = reader.GetInt32(5),
            ColumnDefinition = reader.IsDBNull(6) ? null : reader.GetString(6),
            CreatedDt = reader.GetDateTime(7),
            CreatedBy = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
            DroppedDt = reader.IsDBNull(9) ? null : reader.GetDateTime(9)
        };
    }

    private static object? GetRecordValueByTargetField(GenericDetailRecord record, string targetField)
    {
        return targetField switch
        {
            "AccountCode" => record.AccountCode,
            "ServiceId" => record.ServiceId,
            "ChargeType" => record.ChargeType,
            "CostAmount" => record.CostAmount,
            "TaxAmount" => record.TaxAmount,
            "Quantity" => record.Quantity,
            "UOM" => record.UOM,
            "FromDate" => record.FromDate,
            "ToDate" => record.ToDate,
            "Description" => record.Description,
            "ExternalRef" => record.ExternalRef,
            _ when targetField.StartsWith("Generic") && int.TryParse(targetField[7..], out var num) => record.GetGenericField(num),
            _ => null
        };
    }

    /// <summary>
    /// Validates that a table name contains only safe characters (alphanumeric and underscores).
    /// </summary>
    private static bool IsValidTableName(string tableName)
    {
        return !string.IsNullOrEmpty(tableName) &&
               System.Text.RegularExpressions.Regex.IsMatch(tableName, @"^[a-zA-Z_][a-zA-Z0-9_]*$");
    }

    // ============================================
    // AI Instruction Files
    // ============================================

    public async Task<DataResult<List<AiInstructionFileRecord>>> GetAllInstructionFilesAsync()
    {
        var result = _dbContext.ExecuteRawQuery<AiInstructionFileRecord>(
            "SELECT instruction_id, file_class_code, instruction_content, is_default, description, created_tm, created_by, last_updated, updated_by FROM ntfl_ai_instruction_file ORDER BY file_class_code",
            reader => new AiInstructionFileRecord
            {
                InstructionId = reader.GetInt32(0),
                FileClassCode = reader.GetString(1).Trim(),
                InstructionContent = reader.GetString(2).Trim(),
                IsDefault = !reader.IsDBNull(3) && reader.GetString(3).Trim().ToLower() == "t",
                Description = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                CreatedTm = reader.GetDateTime(5),
                CreatedBy = reader.GetString(6).Trim(),
                LastUpdated = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                UpdatedBy = reader.IsDBNull(8) ? null : reader.GetString(8).Trim()
            }
        );

        return new DataResult<List<AiInstructionFileRecord>>
        {
            StatusCode = result.IsSuccess ? 200 : result.StatusCode,
            Data = result.IsSuccess ? result.Data : null,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    public async Task<DataResult<AiInstructionFileRecord>> GetInstructionFileAsync(string fileClassCode)
    {
        var result = _dbContext.ExecuteRawQuery<AiInstructionFileRecord>(
            "SELECT instruction_id, file_class_code, instruction_content, is_default, description, created_tm, created_by, last_updated, updated_by FROM ntfl_ai_instruction_file WHERE file_class_code = ?",
            reader => new AiInstructionFileRecord
            {
                InstructionId = reader.GetInt32(0),
                FileClassCode = reader.GetString(1).Trim(),
                InstructionContent = reader.GetString(2).Trim(),
                IsDefault = !reader.IsDBNull(3) && reader.GetString(3).Trim().ToLower() == "t",
                Description = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                CreatedTm = reader.GetDateTime(5),
                CreatedBy = reader.GetString(6).Trim(),
                LastUpdated = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                UpdatedBy = reader.IsDBNull(8) ? null : reader.GetString(8).Trim()
            },
            ("@p1", (object)fileClassCode, DbType.String, (int?)null)
        );

        if (!result.IsSuccess)
            return new DataResult<AiInstructionFileRecord> { StatusCode = result.StatusCode, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };

        var record = result.Data?.FirstOrDefault();
        if (record == null)
            return new DataResult<AiInstructionFileRecord> { StatusCode = 404, ErrorCode = "FileLoading.InstructionNotFound", ErrorMessage = $"No instruction file found for file class '{fileClassCode}'" };

        return new DataResult<AiInstructionFileRecord> { StatusCode = 200, Data = record };
    }

    public async Task<RawCommandResult> UpsertInstructionFileAsync(AiInstructionFileRecord record)
    {
        var updateResult = _dbContext.ExecuteRawCommand(
            @"UPDATE ntfl_ai_instruction_file
              SET instruction_content = ?, is_default = ?, description = ?, updated_by = ?, last_updated = CURRENT YEAR TO SECOND
              WHERE file_class_code = ?",
            ("@p1", (object)record.InstructionContent, DbType.String, (int?)null),
            ("@p2", (object)(record.IsDefault ? "t" : "f"), DbType.String, (int?)null),
            ("@p3", (object?)record.Description ?? DBNull.Value, DbType.String, (int?)null),
            ("@p4", (object?)record.UpdatedBy ?? DBNull.Value, DbType.String, (int?)null),
            ("@p5", (object)record.FileClassCode, DbType.String, (int?)null)
        );

        if (updateResult.IsSuccess && updateResult.RowsAffected > 0)
            return updateResult;

        return _dbContext.ExecuteRawCommand(
            @"INSERT INTO ntfl_ai_instruction_file (file_class_code, instruction_content, is_default, description, created_by, created_tm)
              VALUES (?, ?, ?, ?, ?, CURRENT YEAR TO SECOND)",
            ("@p1", (object)record.FileClassCode, DbType.String, (int?)null),
            ("@p2", (object)record.InstructionContent, DbType.String, (int?)null),
            ("@p3", (object)(record.IsDefault ? "t" : "f"), DbType.String, (int?)null),
            ("@p4", (object?)record.Description ?? DBNull.Value, DbType.String, (int?)null),
            ("@p5", (object?)record.CreatedBy ?? DBNull.Value, DbType.String, (int?)null)
        );
    }

    public async Task<RawCommandResult> DeleteInstructionFileAsync(string fileClassCode)
    {
        return _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_ai_instruction_file WHERE file_class_code = ?",
            ("@p1", (object)fileClassCode, DbType.String, (int?)null)
        );
    }

    // ============================================
    // AI Analysis Results
    // ============================================

    public async Task<DataResult<List<AiAnalysisResultRecord>>> GetAnalysisResultsAsync(string fileTypeCode)
    {
        var result = _dbContext.ExecuteRawQuery<AiAnalysisResultRecord>(
            "SELECT analysis_id, file_type_code, ingestion_readiness, summary, analysis_json, created_by, created_tm, updated_by, last_updated FROM ntfl_ai_analysis_result WHERE file_type_code = ? ORDER BY created_tm DESC",
            reader => new AiAnalysisResultRecord
            {
                AnalysisId = reader.GetInt32(0),
                FileTypeCode = reader.GetString(1).Trim(),
                IngestionReadiness = reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                Summary = reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),
                AnalysisJson = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                CreatedBy = reader.IsDBNull(5) ? "" : reader.GetString(5).Trim(),
                CreatedTm = reader.GetDateTime(6),
                UpdatedBy = reader.IsDBNull(7) ? null : reader.GetString(7).Trim(),
                LastUpdated = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
            },
            ("@p1", (object)fileTypeCode, DbType.String, (int?)null)
        );

        if (result.IsSuccess && result.Data != null)
        {
            // Reconstitute overflow for each record
            foreach (var record in result.Data)
            {
                record.AnalysisJson = await ReconstituteJsonAsync(record.AnalysisId, record.AnalysisJson);
            }
        }

        return new DataResult<List<AiAnalysisResultRecord>>
        {
            StatusCode = result.IsSuccess ? 200 : result.StatusCode,
            Data = result.IsSuccess ? result.Data : null,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    private const int JsonChunkSize = 15000; // Leave margin below LVARCHAR(16000)

    public async Task<DataResult<AiAnalysisResultRecord>> GetAnalysisResultAsync(int analysisId)
    {
        var result = _dbContext.ExecuteRawQuery<AiAnalysisResultRecord>(
            "SELECT analysis_id, file_type_code, ingestion_readiness, summary, analysis_json, created_by, created_tm, updated_by, last_updated FROM ntfl_ai_analysis_result WHERE analysis_id = ?",
            reader => new AiAnalysisResultRecord
            {
                AnalysisId = reader.GetInt32(0),
                FileTypeCode = reader.GetString(1).Trim(),
                IngestionReadiness = reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                Summary = reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),
                AnalysisJson = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                CreatedBy = reader.IsDBNull(5) ? "" : reader.GetString(5).Trim(),
                CreatedTm = reader.GetDateTime(6),
                UpdatedBy = reader.IsDBNull(7) ? null : reader.GetString(7).Trim(),
                LastUpdated = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
            },
            ("@p1", (object)analysisId, DbType.Int32, (int?)null)
        );

        if (!result.IsSuccess)
            return new DataResult<AiAnalysisResultRecord> { StatusCode = result.StatusCode, ErrorCode = result.ErrorCode, ErrorMessage = result.ErrorMessage };

        var record = result.Data?.FirstOrDefault();
        if (record == null)
            return new DataResult<AiAnalysisResultRecord> { StatusCode = 404, ErrorCode = "FileLoading.AnalysisNotFound", ErrorMessage = $"Analysis result {analysisId} not found" };

        // Reconstitute overflow if present
        record.AnalysisJson = await ReconstituteJsonAsync(analysisId, record.AnalysisJson);

        return new DataResult<AiAnalysisResultRecord> { StatusCode = 200, Data = record };
    }

    public async Task<ValueResult<int>> InsertAnalysisResultAsync(AiAnalysisResultRecord record)
    {
        // Split JSON into chunks if needed
        var json = record.AnalysisJson ?? "";
        var parentJson = json.Length <= JsonChunkSize ? json : json[..JsonChunkSize];

        var cmd = _dbContext.ExecuteRawCommand(
            @"INSERT INTO ntfl_ai_analysis_result (file_type_code, ingestion_readiness, summary, analysis_json, created_by, created_tm)
              VALUES (?, ?, ?, ?, ?, CURRENT YEAR TO SECOND)",
            ("@p1", (object)record.FileTypeCode, DbType.String, (int?)null),
            ("@p2", (object?)record.IngestionReadiness ?? DBNull.Value, DbType.String, (int?)null),
            ("@p3", (object?)record.Summary ?? DBNull.Value, DbType.String, (int?)null),
            ("@p4", (object)parentJson, DbType.String, (int?)null),
            ("@p5", (object?)record.CreatedBy ?? DBNull.Value, DbType.String, (int?)null)
        );

        if (!cmd.IsSuccess)
            return new ValueResult<int> { StatusCode = cmd.StatusCode, ErrorCode = cmd.ErrorCode, ErrorMessage = cmd.ErrorMessage };

        // Get inserted ID
        var idResult = _dbContext.ExecuteRawScalar<int>("SELECT DBINFO('sqlca.sqlerrd1') FROM systables WHERE tabid = 1");
        var analysisId = idResult.IsSuccess ? idResult.Value : 0;

        // Insert overflow chunks if JSON was split
        if (json.Length > JsonChunkSize && analysisId > 0)
        {
            var remaining = json[JsonChunkSize..];
            var partNumber = 1;
            while (remaining.Length > 0)
            {
                var chunk = remaining.Length <= JsonChunkSize ? remaining : remaining[..JsonChunkSize];
                _dbContext.ExecuteRawCommand(
                    "INSERT INTO ntfl_ai_analysis_overflow (analysis_id, part_number, json_content) VALUES (?, ?, ?)",
                    ("@p1", (object)analysisId, DbType.Int32, (int?)null),
                    ("@p2", (object)partNumber, DbType.Int32, (int?)null),
                    ("@p3", (object)chunk, DbType.String, (int?)null)
                );
                remaining = remaining.Length <= JsonChunkSize ? "" : remaining[JsonChunkSize..];
                partNumber++;
            }
        }

        return new ValueResult<int> { StatusCode = 201, Value = analysisId };
    }

    public async Task<RawCommandResult> UpdateAnalysisResultAsync(AiAnalysisResultRecord record)
    {
        var json = record.AnalysisJson ?? "";
        var parentJson = json.Length <= JsonChunkSize ? json : json[..JsonChunkSize];

        var result = _dbContext.ExecuteRawCommand(
            @"UPDATE ntfl_ai_analysis_result SET ingestion_readiness = ?, summary = ?, analysis_json = ?, updated_by = ?, last_updated = CURRENT YEAR TO SECOND WHERE analysis_id = ?",
            ("@p1", (object?)record.IngestionReadiness ?? DBNull.Value, DbType.String, (int?)null),
            ("@p2", (object?)record.Summary ?? DBNull.Value, DbType.String, (int?)null),
            ("@p3", (object)parentJson, DbType.String, (int?)null),
            ("@p4", (object?)record.UpdatedBy ?? DBNull.Value, DbType.String, (int?)null),
            ("@p5", (object)record.AnalysisId, DbType.Int32, (int?)null)
        );

        // Replace overflow chunks
        _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_ai_analysis_overflow WHERE analysis_id = ?",
            ("@p1", (object)record.AnalysisId, DbType.Int32, (int?)null)
        );

        if (json.Length > JsonChunkSize)
        {
            var remaining = json[JsonChunkSize..];
            var partNumber = 1;
            while (remaining.Length > 0)
            {
                var chunk = remaining.Length <= JsonChunkSize ? remaining : remaining[..JsonChunkSize];
                _dbContext.ExecuteRawCommand(
                    "INSERT INTO ntfl_ai_analysis_overflow (analysis_id, part_number, json_content) VALUES (?, ?, ?)",
                    ("@p1", (object)record.AnalysisId, DbType.Int32, (int?)null),
                    ("@p2", (object)partNumber, DbType.Int32, (int?)null),
                    ("@p3", (object)chunk, DbType.String, (int?)null)
                );
                remaining = remaining.Length <= JsonChunkSize ? "" : remaining[JsonChunkSize..];
                partNumber++;
            }
        }

        return result;
    }

    public async Task<RawCommandResult> DeleteAnalysisResultAsync(int analysisId)
    {
        // Delete overflow first (FK constraint)
        _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_ai_analysis_overflow WHERE analysis_id = ?",
            ("@p1", (object)analysisId, DbType.Int32, (int?)null)
        );

        return _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_ai_analysis_result WHERE analysis_id = ?",
            ("@p1", (object)analysisId, DbType.Int32, (int?)null)
        );
    }

    /// <summary>Reconstitute JSON from parent + overflow chunks.</summary>
    private async Task<string?> ReconstituteJsonAsync(int analysisId, string? parentJson)
    {
        if (string.IsNullOrEmpty(parentJson)) return parentJson;

        var overflow = _dbContext.ExecuteRawQuery<string>(
            "SELECT json_content FROM ntfl_ai_analysis_overflow WHERE analysis_id = ? ORDER BY part_number",
            reader => reader.GetString(0).Trim(),
            ("@p1", (object)analysisId, DbType.Int32, (int?)null)
        );

        if (!overflow.IsSuccess || overflow.Data == null || overflow.Data.Count == 0)
            return parentJson;

        return parentJson + string.Concat(overflow.Data);
    }

    // ============================================
    // AI File-Type Prompts
    // ============================================

    public async Task<DataResult<List<AiFileTypePromptRecord>>> GetFileTypePromptsAsync(string fileTypeCode)
    {
        var result = _dbContext.ExecuteRawQuery<AiFileTypePromptRecord>(
            "SELECT prompt_id, file_type_code, prompt_content, is_current, version, description, source, created_by, created_tm, updated_by, last_updated FROM ntfl_ai_file_type_prompt WHERE file_type_code = ? ORDER BY version DESC",
            reader => new AiFileTypePromptRecord
            {
                PromptId = reader.GetInt32(0),
                FileTypeCode = reader.GetString(1).Trim(),
                PromptContent = reader.GetString(2).Trim(),
                IsCurrent = !reader.IsDBNull(3) && reader.GetString(3).Trim().ToLower() == "t",
                Version = reader.IsDBNull(4) ? 1 : reader.GetInt32(4),
                Description = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                Source = reader.IsDBNull(6) ? "AI" : reader.GetString(6).Trim(),
                CreatedBy = reader.IsDBNull(7) ? "" : reader.GetString(7).Trim(),
                CreatedTm = reader.GetDateTime(8),
                UpdatedBy = reader.IsDBNull(9) ? null : reader.GetString(9).Trim(),
                LastUpdated = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
            },
            ("@p1", (object)fileTypeCode, DbType.String, (int?)null)
        );

        return new DataResult<List<AiFileTypePromptRecord>>
        {
            StatusCode = result.IsSuccess ? 200 : result.StatusCode,
            Data = result.IsSuccess ? result.Data : null,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
    }

    public async Task<DataResult<AiFileTypePromptRecord>> GetFileTypePromptAsync(int promptId)
    {
        var result = _dbContext.ExecuteRawQuery<AiFileTypePromptRecord>(
            "SELECT prompt_id, file_type_code, prompt_content, is_current, version, description, source, created_by, created_tm, updated_by, last_updated FROM ntfl_ai_file_type_prompt WHERE prompt_id = ?",
            reader => new AiFileTypePromptRecord
            {
                PromptId = reader.GetInt32(0),
                FileTypeCode = reader.GetString(1).Trim(),
                PromptContent = reader.GetString(2).Trim(),
                IsCurrent = !reader.IsDBNull(3) && reader.GetString(3).Trim().ToLower() == "t",
                Version = reader.IsDBNull(4) ? 1 : reader.GetInt32(4),
                Description = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                Source = reader.IsDBNull(6) ? "AI" : reader.GetString(6).Trim(),
                CreatedBy = reader.IsDBNull(7) ? "" : reader.GetString(7).Trim(),
                CreatedTm = reader.GetDateTime(8),
                UpdatedBy = reader.IsDBNull(9) ? null : reader.GetString(9).Trim(),
                LastUpdated = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
            },
            ("@p1", (object)promptId, DbType.Int32, (int?)null)
        );

        var record = result.Data?.FirstOrDefault();
        if (record == null)
            return new DataResult<AiFileTypePromptRecord> { StatusCode = 404, ErrorCode = "FileLoading.PromptNotFound", ErrorMessage = $"Prompt {promptId} not found" };

        return new DataResult<AiFileTypePromptRecord> { StatusCode = 200, Data = record };
    }

    public async Task<DataResult<AiFileTypePromptRecord>> GetCurrentFileTypePromptAsync(string fileTypeCode)
    {
        var result = _dbContext.ExecuteRawQuery<AiFileTypePromptRecord>(
            "SELECT prompt_id, file_type_code, prompt_content, is_current, version, description, source, created_by, created_tm, updated_by, last_updated FROM ntfl_ai_file_type_prompt WHERE file_type_code = ? AND is_current = 't'",
            reader => new AiFileTypePromptRecord
            {
                PromptId = reader.GetInt32(0),
                FileTypeCode = reader.GetString(1).Trim(),
                PromptContent = reader.GetString(2).Trim(),
                IsCurrent = true,
                Version = reader.IsDBNull(4) ? 1 : reader.GetInt32(4),
                Description = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                Source = reader.IsDBNull(6) ? "AI" : reader.GetString(6).Trim(),
                CreatedBy = reader.IsDBNull(7) ? "" : reader.GetString(7).Trim(),
                CreatedTm = reader.GetDateTime(8),
                UpdatedBy = reader.IsDBNull(9) ? null : reader.GetString(9).Trim(),
                LastUpdated = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
            },
            ("@p1", (object)fileTypeCode, DbType.String, (int?)null)
        );

        var record = result.Data?.FirstOrDefault();
        if (record == null)
            return new DataResult<AiFileTypePromptRecord> { StatusCode = 404, ErrorCode = "FileLoading.NoCurrentPrompt", ErrorMessage = $"No current prompt found for file type '{fileTypeCode}'" };

        return new DataResult<AiFileTypePromptRecord> { StatusCode = 200, Data = record };
    }

    public async Task<ValueResult<int>> InsertFileTypePromptAsync(AiFileTypePromptRecord record)
    {
        var cmd = _dbContext.ExecuteRawCommand(
            @"INSERT INTO ntfl_ai_file_type_prompt (file_type_code, prompt_content, is_current, version, description, source, created_by, created_tm)
              VALUES (?, ?, ?, ?, ?, ?, ?, CURRENT YEAR TO SECOND)",
            ("@p1", (object)record.FileTypeCode, DbType.String, (int?)null),
            ("@p2", (object)record.PromptContent, DbType.String, (int?)null),
            ("@p3", (object)(record.IsCurrent ? "t" : "f"), DbType.String, (int?)null),
            ("@p4", (object)record.Version, DbType.Int32, (int?)null),
            ("@p5", (object?)record.Description ?? DBNull.Value, DbType.String, (int?)null),
            ("@p6", (object)record.Source, DbType.String, (int?)null),
            ("@p7", (object?)record.CreatedBy ?? DBNull.Value, DbType.String, (int?)null)
        );

        if (!cmd.IsSuccess)
            return new ValueResult<int> { StatusCode = cmd.StatusCode, ErrorCode = cmd.ErrorCode, ErrorMessage = cmd.ErrorMessage };

        var idResult = _dbContext.ExecuteRawScalar<int>("SELECT DBINFO('sqlca.sqlerrd1') FROM systables WHERE tabid = 1");
        return new ValueResult<int> { StatusCode = 201, Value = idResult.IsSuccess ? idResult.Value : 0 };
    }

    public async Task<RawCommandResult> UpdateFileTypePromptAsync(AiFileTypePromptRecord record)
    {
        return _dbContext.ExecuteRawCommand(
            @"UPDATE ntfl_ai_file_type_prompt SET prompt_content = ?, description = ?, source = ?, updated_by = ?, last_updated = CURRENT YEAR TO SECOND WHERE prompt_id = ?",
            ("@p1", (object)record.PromptContent, DbType.String, (int?)null),
            ("@p2", (object?)record.Description ?? DBNull.Value, DbType.String, (int?)null),
            ("@p3", (object)record.Source, DbType.String, (int?)null),
            ("@p4", (object?)record.UpdatedBy ?? DBNull.Value, DbType.String, (int?)null),
            ("@p5", (object)record.PromptId, DbType.Int32, (int?)null)
        );
    }

    public async Task<RawCommandResult> ActivateFileTypePromptAsync(string fileTypeCode, int promptId)
    {
        // Deactivate all prompts for this file type
        _dbContext.ExecuteRawCommand(
            "UPDATE ntfl_ai_file_type_prompt SET is_current = 'f' WHERE file_type_code = ?",
            ("@p1", (object)fileTypeCode, DbType.String, (int?)null)
        );

        // Activate the specified prompt
        return _dbContext.ExecuteRawCommand(
            "UPDATE ntfl_ai_file_type_prompt SET is_current = 't' WHERE prompt_id = ?",
            ("@p1", (object)promptId, DbType.Int32, (int?)null)
        );
    }

    public async Task<RawCommandResult> DeleteFileTypePromptAsync(int promptId)
    {
        return _dbContext.ExecuteRawCommand(
            "DELETE FROM ntfl_ai_file_type_prompt WHERE prompt_id = ?",
            ("@p1", (object)promptId, DbType.Int32, (int?)null)
        );
    }
}

/// <summary>
/// Static helper methods for custom table DDL generation.
/// </summary>
public static class CustomTableHelper
{
    /// <summary>Convert PascalCase to snake_case (e.g. AccountCode → account_code, Generic01 → generic_01).</summary>
    public static string ToSnakeCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase)) return pascalCase;

        var sb = new StringBuilder();
        for (int i = 0; i < pascalCase.Length; i++)
        {
            var c = pascalCase[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(pascalCase[i - 1]))
            {
                sb.Append('_');
            }
            else if (i > 0 && char.IsUpper(c) && char.IsUpper(pascalCase[i - 1]) &&
                     i + 1 < pascalCase.Length && char.IsLower(pascalCase[i + 1]))
            {
                sb.Append('_');
            }
            // Insert underscore between letters and digits
            else if (i > 0 && char.IsDigit(c) && char.IsLetter(pascalCase[i - 1]))
            {
                sb.Append('_');
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>Derive the physical table name from file type code and version.</summary>
    public static string DeriveTableName(string fileTypeCode, int version)
    {
        var baseName = fileTypeCode.Trim().ToLowerInvariant()
            .Replace(' ', '_')
            .Replace('-', '_');
        baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"[^a-z0-9_]", "");
        return $"ntfl_{baseName}_v{version}";
    }

    /// <summary>Map a column mapping DataType string to the corresponding SQL type string.</summary>
    public static string MapToSqlType(GenericColumnMapping mapping)
    {
        return mapping.DataType.ToLowerInvariant() switch
        {
            "int" or "integer" => "INTEGER",
            "decimal" => "DECIMAL(16,6)",
            "date" => "DATE",
            "datetime" => "DATETIME YEAR TO SECOND",
            _ => $"VARCHAR({mapping.MaxLength ?? 128})" // String and default
        };
    }

    /// <summary>Map a column mapping DataType string to the corresponding DbType.</summary>
    public static DbType MapToDbType(string dataType)
    {
        return dataType.ToLowerInvariant() switch
        {
            "int" or "integer" => DbType.Int32,
            "decimal" => DbType.Decimal,
            "date" or "datetime" => DbType.DateTime,
            _ => DbType.String
        };
    }

    /// <summary>Generate CREATE TABLE DDL from column mappings.</summary>
    public static string GenerateCreateTableDdl(string tableName, List<GenericColumnMapping> mappings)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE {tableName} (");
        sb.AppendLine("    nt_file_num         INTEGER NOT NULL,");
        sb.AppendLine("    nt_file_rec_num     INTEGER NOT NULL,");

        foreach (var mapping in mappings.OrderBy(m => m.ColumnIndex))
        {
            var colName = ToSnakeCase(mapping.TargetField);
            var sqlType = MapToSqlType(mapping);
            var nullable = mapping.IsRequired ? " NOT NULL" : "";
            sb.AppendLine($"    {colName,-20}{sqlType}{nullable},");
        }

        sb.AppendLine("    status_id           INTEGER DEFAULT 1,");
        sb.AppendLine();
        sb.AppendLine("    PRIMARY KEY (nt_file_num, nt_file_rec_num)");
        sb.AppendLine(");");

        return sb.ToString();
    }

    /// <summary>Build the list of column definitions for a proposal response.</summary>
    public static List<CustomTableColumnDef> BuildColumnDefs(List<GenericColumnMapping> mappings)
    {
        return mappings.OrderBy(m => m.ColumnIndex)
            .Select(m => new CustomTableColumnDef
            {
                ColumnName = ToSnakeCase(m.TargetField),
                SqlType = MapToSqlType(m),
                IsRequired = m.IsRequired,
                SourceField = m.TargetField,
                DataType = m.DataType
            })
            .ToList();
    }
}
