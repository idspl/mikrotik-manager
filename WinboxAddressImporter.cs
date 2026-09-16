namespace IndigoRouterScheduler;

public static class WinboxAddressImporter
{
    private static readonly byte[] CdbMagic = [0x0d, 0xf0, 0x1d, 0xc0];

    public static IReadOnlyList<RouterRecord> Parse(string path, int defaultApiPort)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        Span<byte> header = stackalloc byte[4];
        if (stream.Read(header) != header.Length)
            throw new InvalidDataException("The selected WinBox address file is empty or incomplete.");

        if (header.SequenceEqual(CdbMagic))
            return WinboxCdbParser.Parse(path, defaultApiPort);

        string extension = Path.GetExtension(path);
        if (extension.Equals(".wbx", StringComparison.OrdinalIgnoreCase))
            return WinboxWbxParser.Parse(path, defaultApiPort);

        throw new InvalidDataException("Unknown WinBox address format. Select a WinBox .cdb or legacy .wbx file.");
    }
}
