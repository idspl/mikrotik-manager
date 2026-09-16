using System.Buffers.Binary;
using System.Text;

namespace MikroTikManager;

public static class WinboxCdbParser
{
    private static readonly byte[] Magic = [0x0d, 0xf0, 0x1d, 0xc0];

    public static IReadOnlyList<RouterRecord> Parse(string path, int defaultApiPort)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 8 || !bytes.AsSpan(0, 4).SequenceEqual(Magic))
            throw new InvalidDataException("This is not a supported WinBox v3 CDB file.");

        var result = new List<RouterRecord>();
        int position = 4;
        while (position < bytes.Length)
        {
            if (position + 4 > bytes.Length)
                throw new InvalidDataException("The CDB ends inside a record length.");
            int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position, 4));
            position += 4;
            if (length <= 0 || position + length > bytes.Length)
                throw new InvalidDataException($"Invalid CDB record length at offset {position - 4}.");

            var fields = ParseStringFields(bytes.AsSpan(position, length));
            position += length;
            if (!fields.TryGetValue(0x01, out string? address) || string.IsNullOrWhiteSpace(address))
                continue;

            ParsedRouterAddress endpoint = RouterAddressParser.Parse(address);
            string host = endpoint.Host;
            string displayName = fields.GetValueOrDefault(0x04)
                ?? fields.GetValueOrDefault(0xFE0009)
                ?? host;
            string password = fields.GetValueOrDefault(0x03) ?? "";
            result.Add(new RouterRecord
            {
                Name = displayName,
                Host = host,
                ApiPort = defaultApiPort,
                Username = fields.GetValueOrDefault(0x02) ?? "",
                Password = password,
                BackupPassword = string.IsNullOrEmpty(password)
                    ? Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18))
                    : password
            });
        }
        return result;
    }

    private static Dictionary<int, string> ParseStringFields(ReadOnlySpan<byte> record)
    {
        var result = new Dictionary<int, string>();
        for (int i = 0; i + 5 <= record.Length; i++)
        {
            if (record[i + 3] != 0x21) continue;
            int fieldId = record[i] | (record[i + 1] << 8) | (record[i + 2] << 16);
            int length = record[i + 4];
            if (i + 5 + length > record.Length) continue;
            ReadOnlySpan<byte> valueBytes = record.Slice(i + 5, length);
            try
            {
                string value = Encoding.UTF8.GetString(valueBytes);
                if (!value.Any(char.IsControl)) result[fieldId] = value;
            }
            catch { /* Skip malformed optional fields. */ }
        }
        return result;
    }

}
