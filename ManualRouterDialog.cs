namespace MikroTikManager;

public sealed class ManualRouterDialog : Form
{
    private readonly TextBox _name = new() { Width = 280 };
    private readonly TextBox _group = new() { Width = 220, MaxLength = 80, PlaceholderText = "Optional upgrade group" };
    private readonly TextBox _address = new() { Width = 280, PlaceholderText = "Example: 172.21.1.207 or router.example.com" };
    private readonly NumericUpDown _apiPort = new() { Minimum = 1, Maximum = 65535, Width = 120 };
    private readonly TextBox _username = new() { Width = 220 };
    private readonly TextBox _password = new() { Width = 220, UseSystemPasswordChar = true };
    public RouterRecord? Router { get; private set; }

    public ManualRouterDialog(int defaultApiPort)
    {
        Icon = AppIcon.Current;
        Text = "Add Router Manually";
        Width = 570;
        Height = 395;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        _apiPort.Value = Math.Clamp(defaultApiPort, 1, 65535);
        _username.Text = "admin";

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 8 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(table, 0, "Router name", _name);
        AddRow(table, 1, "IP address / hostname", _address);
        AddRow(table, 2, "Upgrade group", _group);
        AddRow(table, 3, "RouterOS API port", _apiPort);
        AddRow(table, 4, "Username", _username);
        AddRow(table, 5, "Password", _password);

        var note = new Label
        {
            Text = "If the address contains :port, it overrides the API port above.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Padding = new Padding(0, 6, 0, 8)
        };
        table.Controls.Add(note, 1, 6);

        var add = new Button { Text = "Add Router", AutoSize = true };
        add.Click += (_, _) => AcceptRouter();
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(add);
        table.Controls.Add(buttons, 1, 7);
        Controls.Add(table);
        AcceptButton = add;
        CancelButton = cancel;
    }

    private void AcceptRouter()
    {
        try
        {
            ParsedRouterAddress endpoint = RouterAddressParser.Parse(_address.Text);
            if (string.IsNullOrWhiteSpace(_username.Text))
                throw new ArgumentException("Username is required.");
            string password = _password.Text;
            Router = new RouterRecord
            {
                Name = string.IsNullOrWhiteSpace(_name.Text) ? endpoint.Host : _name.Text.Trim(),
                Group = _group.Text.Trim(),
                Host = endpoint.Host,
                ApiPort = endpoint.ExplicitPort ?? (int)_apiPort.Value,
                Username = _username.Text.Trim(),
                Password = password,
                BackupPassword = string.IsNullOrEmpty(password)
                    ? Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18))
                    : password
            };
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 7, 0, 7) }, 0, row);
        table.Controls.Add(control, 1, row);
    }
}
