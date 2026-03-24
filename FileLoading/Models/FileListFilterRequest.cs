using Microsoft.AspNetCore.Mvc;
using FileLoading.Models;

namespace FileLoading.Models;

/// <summary>
/// Request model for file list filtering (query parameters).
/// </summary>
public class FileListFilterRequest
{
    /// <summary>Filter by file type code (e.g. CDR, CHG, SSSWHLSCDR).</summary>
    [FromQuery(Name = "fileType")]
    public string? FileType { get; set; }

    /// <summary>Filter by current folder (Transfer, Processing, Processed, Errors, Skipped).</summary>
    [FromQuery]
    public string? Folder { get; set; }

    /// <summary>Filter by transfer status (0=Pending, 1=Downloading, 2=Downloaded, 3=Processing, 4=Processed, 5=Error, 6=Skipped).</summary>
    [FromQuery]
    public TransferStatus? Status { get; set; }

    /// <summary>Filter by date range start (inclusive).</summary>
    [FromQuery(Name = "fromDate")]
    public DateTime? FromDate { get; set; }

    /// <summary>Filter by date range end (inclusive).</summary>
    [FromQuery(Name = "toDate")]
    public DateTime? ToDate { get; set; }

    /// <summary>Search by file name (partial match).</summary>
    [FromQuery]
    public string? Search { get; set; }

    /// <summary>Number of records to skip (default 0).</summary>
    [FromQuery(Name = "skipRecords")]
    public int SkipRecords { get; set; } = 0;

    /// <summary>Number of records to return (default 20).</summary>
    [FromQuery(Name = "takeRecords")]
    public int TakeRecords { get; set; } = 20;

    /// <summary>Include total count: Y=yes, N=no, F=first page only (default F).</summary>
    [FromQuery(Name = "countRecords")]
    public string CountRecords { get; set; } = "F";
}
