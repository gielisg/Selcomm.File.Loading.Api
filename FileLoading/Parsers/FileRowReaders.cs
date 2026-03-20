using ClosedXML.Excel;

namespace FileLoading.Parsers;

/// <summary>
/// Abstraction for reading rows from different file formats (text, Excel).
/// Decouples file format from parsing logic.
/// </summary>
internal interface IFileRowReader : IDisposable
{
    /// <summary>Read the next row as a string array. Returns null at end of file.</summary>
    Task<string[]?> ReadNextRowAsync();

    /// <summary>Get the raw text of the last read line (for error reporting).</summary>
    string? GetRawLine();

    /// <summary>Whether all rows have been read.</summary>
    bool IsComplete { get; }

    /// <summary>Rewind for a second pass.</summary>
    void Reset();
}

/// <summary>
/// Reads rows from delimited text files (CSV, pipe, tab, etc.).
/// </summary>
internal class DelimitedTextRowReader : IFileRowReader
{
    private readonly Stream _stream;
    private StreamReader _reader;
    private readonly char _delimiter;
    private string? _lastLine;

    public DelimitedTextRowReader(Stream stream, char delimiter)
    {
        _stream = stream;
        _delimiter = delimiter;
        _reader = new StreamReader(stream, leaveOpen: true);
    }

    public bool IsComplete => _reader.EndOfStream;

    public async Task<string[]?> ReadNextRowAsync()
    {
        if (_reader.EndOfStream)
            return null;

        _lastLine = await _reader.ReadLineAsync();
        if (_lastLine == null)
            return null;

        return SplitDelimited(_lastLine, _delimiter);
    }

    public string? GetRawLine() => _lastLine;

    public void Reset()
    {
        _stream.Position = 0;
        _reader.Dispose();
        _reader = new StreamReader(_stream, leaveOpen: true);
        _lastLine = null;
    }

    public void Dispose()
    {
        _reader.Dispose();
    }

    /// <summary>
    /// Split a delimited line into fields, handling quoted fields.
    /// Matches BaseFileParser.SplitDelimited behavior.
    /// </summary>
    private static string[] SplitDelimited(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == delimiter && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields.ToArray();
    }
}

/// <summary>
/// Reads rows from Excel (.xlsx) files using ClosedXML.
/// </summary>
internal class ExcelRowReader : IFileRowReader
{
    private readonly XLWorkbook _workbook;
    private readonly IXLWorksheet _worksheet;
    private readonly int _lastRow;
    private readonly int _lastColumn;
    private int _currentRow;
    private string? _lastLine;

    public ExcelRowReader(Stream stream, string? sheetName, int sheetIndex)
    {
        _workbook = new XLWorkbook(stream);

        // Select worksheet by name or index
        if (!string.IsNullOrEmpty(sheetName) && _workbook.TryGetWorksheet(sheetName, out var namedSheet))
        {
            _worksheet = namedSheet;
        }
        else if (sheetIndex >= 0 && sheetIndex < _workbook.Worksheets.Count)
        {
            _worksheet = _workbook.Worksheets.Skip(sheetIndex).First();
        }
        else
        {
            _worksheet = _workbook.Worksheets.First();
        }

        var rangeUsed = _worksheet.RangeUsed();
        if (rangeUsed != null)
        {
            _lastRow = rangeUsed.LastRow().RowNumber();
            _lastColumn = rangeUsed.LastColumn().ColumnNumber();
        }
        _currentRow = 0;
    }

    public bool IsComplete => _currentRow >= _lastRow;

    public Task<string[]?> ReadNextRowAsync()
    {
        _currentRow++;
        if (_currentRow > _lastRow)
            return Task.FromResult<string[]?>(null);

        var row = _worksheet.Row(_currentRow);
        var fields = new string[_lastColumn];
        var rawParts = new List<string>();

        for (int col = 1; col <= _lastColumn; col++)
        {
            var cell = row.Cell(col);
            var value = cell.IsEmpty() ? string.Empty : cell.GetFormattedString();
            fields[col - 1] = value;
            rawParts.Add(value);
        }

        _lastLine = string.Join("\t", rawParts);
        return Task.FromResult<string[]?>(fields);
    }

    public string? GetRawLine() => _lastLine;

    public void Reset()
    {
        _currentRow = 0;
        _lastLine = null;
    }

    public void Dispose()
    {
        _workbook.Dispose();
    }
}
