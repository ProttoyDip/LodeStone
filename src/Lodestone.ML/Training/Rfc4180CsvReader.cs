using System.Text;

namespace Lodestone.ML.Training;

internal sealed class CsvSchemaException : IOException
{
    public CsvSchemaException(string message) : base(message) { }
}

internal readonly struct CsvRecord
{
    private readonly IReadOnlyDictionary<string, int> _columns;
    private readonly string[] _values;

    public CsvRecord(IReadOnlyDictionary<string, int> columns, string[] values, long rowNumber)
    {
        _columns = columns;
        _values = values;
        RowNumber = rowNumber;
    }

    public long RowNumber { get; }

    public string this[string column]
    {
        get
        {
            var index = _columns[column];
            return index < _values.Length ? _values[index].Trim() : string.Empty;
        }
    }
}

/// <summary>Small streaming RFC 4180 reader used to avoid loading the large studentVle table.</summary>
internal static class Rfc4180CsvReader
{
    public static void Read(
        string path,
        IReadOnlyCollection<string> requiredColumns,
        Action<CsvRecord> consume)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(requiredColumns);
        ArgumentNullException.ThrowIfNull(consume);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Required OULAD table was not found: {path}", path);

        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var rows = ReadRows(reader, path).GetEnumerator();

        if (!rows.MoveNext())
            throw new CsvSchemaException($"OULAD table '{Path.GetFileName(path)}' is empty.");

        var headers = rows.Current;
        if (headers.Length > 0)
            headers[0] = headers[0].TrimStart('\uFEFF');

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Length; index++)
        {
            var header = headers[index].Trim();
            if (header.Length == 0)
                throw new CsvSchemaException($"OULAD table '{Path.GetFileName(path)}' contains an empty header at column {index + 1}.");
            if (!columns.TryAdd(header, index))
                throw new CsvSchemaException($"OULAD table '{Path.GetFileName(path)}' contains duplicate header '{header}'.");
        }

        var missing = requiredColumns.Where(column => !columns.ContainsKey(column)).ToArray();
        if (missing.Length > 0)
        {
            throw new CsvSchemaException(
                $"OULAD table '{Path.GetFileName(path)}' is missing required column(s): {string.Join(", ", missing)}.");
        }

        long rowNumber = 1;
        while (rows.MoveNext())
        {
            rowNumber++;
            var values = rows.Current;
            if (values.All(string.IsNullOrWhiteSpace))
                continue;
            if (values.Length > headers.Length)
            {
                throw new CsvSchemaException(
                    $"OULAD table '{Path.GetFileName(path)}' row {rowNumber} has {values.Length} fields but the header has {headers.Length}.");
            }

            consume(new CsvRecord(columns, values, rowNumber));
        }
    }

    private static IEnumerable<string[]> ReadRows(TextReader reader, string path)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var quotedFieldClosed = false;
        var atFieldStart = true;
        var hasRecordContent = false;

        while (true)
        {
            var next = reader.Read();
            if (next < 0)
            {
                if (inQuotes)
                    throw new CsvSchemaException($"CSV file '{Path.GetFileName(path)}' ends inside a quoted field.");

                if (hasRecordContent || fields.Count > 0 || field.Length > 0)
                {
                    fields.Add(field.ToString());
                    yield return fields.ToArray();
                }

                yield break;
            }

            var character = (char)next;
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                        quotedFieldClosed = true;
                    }
                }
                else
                {
                    field.Append(character);
                }

                hasRecordContent = true;
                continue;
            }

            if (character == '"')
            {
                if (!atFieldStart)
                    throw new CsvSchemaException($"CSV file '{Path.GetFileName(path)}' contains an unexpected quote.");
                inQuotes = true;
                atFieldStart = false;
                hasRecordContent = true;
                continue;
            }

            if (quotedFieldClosed && character is not ',' and not '\r' and not '\n')
            {
                throw new CsvSchemaException(
                    $"CSV file '{Path.GetFileName(path)}' contains characters after a closing quote.");
            }

            if (character == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
                atFieldStart = true;
                quotedFieldClosed = false;
                hasRecordContent = true;
                continue;
            }

            if (character is '\r' or '\n')
            {
                if (character == '\r' && reader.Peek() == '\n')
                    reader.Read();

                fields.Add(field.ToString());
                yield return fields.ToArray();
                fields.Clear();
                field.Clear();
                atFieldStart = true;
                quotedFieldClosed = false;
                hasRecordContent = false;
                continue;
            }

            field.Append(character);
            atFieldStart = false;
            hasRecordContent = true;
        }
    }
}
