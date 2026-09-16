namespace IndigoRouterScheduler;

public sealed class ImportPathDialog : Form
{
    private readonly TextBox _path = new() { Dock = DockStyle.Top, Height = 28 };
    public string SelectedPath => _path.Text.Trim().Trim('"');

    public ImportPathDialog()
    {
        Icon = AppIcon.Current;
        Text = "Import WinBox Address File";
        Width = 620;
        Height = 225;
        MinimumSize = new Size(520, 225);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AllowDrop = true;

        var instructions = new Label
        {
            Text = "Paste the full path of a .cdb or .wbx file, or drag and drop the file into this window.",
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(0, 6, 0, 8),
            AutoEllipsis = true
        };
        var paste = new Button { Text = "Paste from Clipboard", AutoSize = true };
        paste.Click += (_, _) => PasteClipboard();
        var import = new Button { Text = "Import", AutoSize = true, DialogResult = DialogResult.None };
        import.Click += (_, _) => ValidateAndAccept();
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(import);
        buttons.Controls.Add(paste);
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        panel.Controls.Add(_path);
        panel.Controls.Add(instructions);
        panel.Controls.Add(buttons);
        Controls.Add(panel);
        AcceptButton = import;
        CancelButton = cancel;

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        Shown += (_, _) => _path.Focus();
    }

    private void PasteClipboard()
    {
        try
        {
            if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Count > 0)
                _path.Text = Clipboard.GetFileDropList()[0] ?? "";
            else if (Clipboard.ContainsText())
                _path.Text = Clipboard.GetText().Trim().Trim('"');
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not read the clipboard: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ValidateAndAccept()
    {
        string path = SelectedPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show("Paste or drop a WinBox .cdb or .wbx file path.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".cdb", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".wbx", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Only WinBox .cdb and .wbx files are supported.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = GetDroppedPath(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        string? path = GetDroppedPath(e);
        if (path is null) return;
        _path.Text = path;
        ValidateAndAccept();
    }

    private static string? GetDroppedPath(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1) return null;
        string extension = Path.GetExtension(files[0]);
        return extension.Equals(".cdb", StringComparison.OrdinalIgnoreCase) || extension.Equals(".wbx", StringComparison.OrdinalIgnoreCase)
            ? files[0]
            : null;
    }
}
