using System.Globalization;
using System.Text;

namespace MikroTikManager;

public sealed record MaintenanceReportFiles(string CsvPath, string PdfPath);

public sealed class MaintenanceReportService(SecureStore store)
{
    private readonly SecureStore _store = store;

    public async Task<MaintenanceReportFiles> WriteAsync(MaintenanceRunResult run, CancellationToken ct = default)
    {
        string folder = Path.Combine(_store.RootDirectory, "Reports");
        Directory.CreateDirectory(folder);
        string stem = $"maintenance-{run.StartedAt:yyyyMMdd-HHmmss}";
        string csvPath = Path.Combine(folder, stem + ".csv");
        string pdfPath = Path.Combine(folder, stem + ".pdf");

        var csv = new StringBuilder("Router,Address,Result,Attempts,RouterOS,Firmware,Started,Completed,Message\r\n");
        foreach (RouterRunResult row in run.Routers)
        {
            csv.AppendLine(string.Join(',', Csv(row.Router), Csv(row.Address), Csv(row.Result), row.Attempts,
                Csv(row.RouterOsVersion), Csv(row.FirmwareVersion), Csv(row.StartedAt.ToString("s", CultureInfo.InvariantCulture)),
                Csv(row.CompletedAt.ToString("s", CultureInfo.InvariantCulture)), Csv(row.Message)));
        }
        await File.WriteAllTextAsync(csvPath, csv.ToString(), new UTF8Encoding(true), ct);

        var lines = new List<string>
        {
            "MikroTik Manager - Maintenance Report",
            $"Started:   {run.StartedAt:yyyy-MM-dd HH:mm:ss}",
            $"Completed: {run.CompletedAt:yyyy-MM-dd HH:mm:ss}",
            $"Completed: {run.Successful}    Failed: {run.Failed}    Cancelled: {run.Cancelled}",
            "",
            "Router | Address | Result | Attempts | RouterOS | Firmware",
            new string('-', 105)
        };
        foreach (RouterRunResult row in run.Routers)
        {
            lines.Add(Trim($"{row.Router} | {row.Address} | {row.Result} | {row.Attempts} | {row.RouterOsVersion} | {row.FirmwareVersion}", 112));
            if (!string.IsNullOrWhiteSpace(row.Message) && row.Result != "Completed") lines.Add("  " + Trim(row.Message, 108));
        }
        lines.Add("");
        lines.Add("MikroTik, RouterOS and RouterBOARD are trademarks of MikroTikls SIA.");
        WritePdf(pdfPath, lines);
        return new MaintenanceReportFiles(csvPath, pdfPath);
    }

    private static void WritePdf(string path, IReadOnlyList<string> lines)
    {
        const int linesPerPage = 58;
        List<List<string>> pages = lines.Chunk(linesPerPage).Select(chunk => chunk.ToList()).ToList();
        if (pages.Count == 0) pages.Add([]);
        int objectCount = 3 + pages.Count * 2;
        var objects = new string[objectCount + 1];
        objects[1] = "<< /Type /Catalog /Pages 2 0 R >>";
        string kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => $"{4 + i * 2} 0 R"));
        objects[2] = $"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>";
        objects[3] = "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>";
        for (int i = 0; i < pages.Count; i++)
        {
            int pageObject = 4 + i * 2;
            int contentObject = pageObject + 1;
            string body = "BT\n/F1 8 Tf\n36 756 Td\n11 TL\n" +
                string.Join("\n", pages[i].Select(line => $"({Pdf(line)}) Tj T*")) + "\nET";
            objects[pageObject] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentObject} 0 R >>";
            objects[contentObject] = $"<< /Length {Encoding.ASCII.GetByteCount(body)} >>\nstream\n{body}\nendstream";
        }

        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        Write(output, "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        var offsets = new long[objects.Length];
        for (int i = 1; i < objects.Length; i++)
        {
            offsets[i] = output.Position;
            Write(output, $"{i} 0 obj\n{objects[i]}\nendobj\n");
        }
        long xref = output.Position;
        Write(output, $"xref\n0 {objects.Length}\n0000000000 65535 f \n");
        for (int i = 1; i < objects.Length; i++) Write(output, $"{offsets[i]:0000000000} 00000 n \n");
        Write(output, $"trailer\n<< /Size {objects.Length} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    private static void Write(Stream stream, string value)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string Pdf(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';
    private static string Trim(string value, int maximum) => value.Length <= maximum ? value : value[..(maximum - 3)] + "...";
}
