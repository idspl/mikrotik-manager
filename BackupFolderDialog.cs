namespace MikroTikManager;

public sealed class BackupFolderDialog : Form
{
    private readonly TextBox _path = new() { Dock = DockStyle.Fill };
    public string SelectedPath { get; private set; } = "";

    public BackupFolderDialog()
    {
        Icon = AppIcon.Current;
        Text = "Backup Selected Routers";
        Width = 680;
        Height = 230;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        _path.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Indigo Router Backups");

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4 };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.Controls.Add(new Label { Text = "Local parent folder", AutoSize = true, Padding = new Padding(0, 0, 0, 6) }, 0, 0);
        table.Controls.Add(_path, 0, 1);
        table.Controls.Add(new Label
        {
            Text = "A dated subfolder will contain encrypted .backup files, show-sensitive .rsc exports and the backup-password manifest. Protect this folder.",
            AutoSize = true,
            ForeColor = Color.DarkRed,
            MaximumSize = new Size(620, 0),
            Padding = new Padding(0, 8, 0, 8)
        }, 0, 2);

        var start = new Button { Text = "Start Backup", AutoSize = true };
        start.Click += (_, _) => AcceptPath();
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(start);
        table.Controls.Add(buttons, 0, 3);
        Controls.Add(table);
        AcceptButton = start;
        CancelButton = cancel;
    }

    private void AcceptPath()
    {
        try
        {
            string path = Environment.ExpandEnvironmentVariables(_path.Text.Trim());
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose a local backup folder.");
            Directory.CreateDirectory(path);
            SelectedPath = Path.GetFullPath(path);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
