using System.Text;

namespace MikroTikManager;

/// <summary>Parser for the legacy WinBox 3 managed-address export (.wbx).</summary>
public static class WinboxWbxParser
{
    private static readonly HashSet<string> KnownFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "group", "host", "keep-pwd", "login", "note", "pwd", "secure-mode", "type"
    };

    public static IReadOnlyList<RouterRecord> Parse(string path, int defaultApiPort)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 6)
            throw new InvalidDataException("The WBX file is empty or incomplete.");

        var records = new List<RouterRecord>();
        var fields = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        int offset = 4; // Legacy WBX files begin with a four-byte header.
        int recognizedPairs = 0;

        while (offset < data.Length)
        {
            if (data[offset] == 0)
            {
                AddRecord(fields, records, defaultApiPort);
                fields.Clear();
                offset += Math.Min(2, data.Length - offset);
                if (offset < data.Length && IsAllZero(data.AsSpan(offset))) break;
                continue;
            }

            if (offset + 3 > data.Length)
                throw new InvalidDataException($"WBX field header is truncated at offset {offset}.");

            int recordLength = data[offset] - 1;
            int nameLength = data[offset + 2];
            int valueLength = recordLength - nameLength;
            if (recordLength < 1 || nameLength < 1 || valueLength < 0 || offset + 3 + nameLength + valueLength > data.Length)
                throw new InvalidDataException($"Invalid WBX field length at offset {offset}.");

            string name = Encoding.UTF8.GetString(data, offset + 3, nameLength);
            byte[] value = data.AsSpan(offset + 3 + nameLength, valueLength).ToArray();
            if (KnownFields.Contains(name))
            {
                fields[name] = value;
                recognizedPairs++;
            }
            offset += 3 + nameLength + valueLength;
        }

        AddRecord(fields, records, defaultApiPort);
        if (recognizedPairs == 0 || records.Count == 0)
            throw new InvalidDataException("No managed router records were found in this legacy WBX file.");
        return records;
    }

    private static void AddRecord(Dictionary<string, byte[]> fields, List<RouterRecord> records, int defaultApiPort)
    {
        string address = Text(fields, "host").Trim();
        if (string.IsNullOrEmpty(address)) return;
        ParsedRouterAddress endpoint = RouterAddressParser.Parse(address);
        string host = endpoint.Host;
        string password = Text(fields, "pwd");
        string note = Text(fields, "note").Trim();
        records.Add(new RouterRecord
        {
            Name = string.IsNullOrEmpty(note) ? host : note,
            Host = host,
            ApiPort = defaultApiPort,
            Username = Text(fields, "login"),
            Password = password,
            BackupPassword = string.IsNullOrEmpty(password)
                ? Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18))
                : password
        });
    }

    private static string Text(Dictionary<string, byte[]> fields, string key)
        => fields.TryGetValue(key, out byte[]? value) ? Encoding.UTF8.GetString(value).TrimEnd('\0') : "";

    private static bool IsAllZero(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes) if (value != 0) return false;
        return true;
    }

}
