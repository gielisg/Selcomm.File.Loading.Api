using FileLoading.Models;

namespace FileLoading.Parsers;

/// <summary>
/// Interface for parsers that need sub-type table inserts alongside standard detail records.
/// Parsers implement this when a file type requires dual-insert (e.g., cl_detail + ssswhls_cdr).
/// </summary>
public interface ISubTypeRecordProvider
{
    /// <summary>
    /// The sub-type table name (e.g., "ssswhls_cdr", "ssswhlschg").
    /// </summary>
    string SubTypeTableName { get; }

    /// <summary>
    /// Creates a sub-type record from the parsed data.
    /// Called after the standard detail record is created.
    /// </summary>
    FileDetailRecord CreateSubTypeRecord(ParsedRecord parsed, int ntFileNum, int recordNum);
}
