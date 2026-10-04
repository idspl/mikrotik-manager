namespace MikroTikManager;

public sealed partial class MainForm
{
    private readonly ComboBox _themeChoice = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _themeToggle = new() { Dock = DockStyle.Bottom, AutoSize = true,
        Padding = new Padding(4, 8, 4, 8), FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(23, 39, 62), ForeColor = Color.White };
    private bool _changingTheme;

    private void ChangeTheme(string theme)
    {
        if (_changingTheme) return;
        _changingTheme = true;
        try
        {
            var settings = _store.LoadSettings();
            settings.Theme = theme == "Dark" ? "Dark" : "Light";
            _store.SaveSettings(settings);
            AppTheme.Set(settings.Theme);
            _themeChoice.SelectedItem = settings.Theme;
            _themeToggle.Text = AppTheme.IsDark ? "Switch to light mode" : "Switch to dark mode";
            foreach (Form form in Application.OpenForms.Cast<Form>().ToArray()) AppTheme.Apply(form);
            // Theme changes repaint only; no rebinding, sorting or router operations.
        }
        catch (Exception ex)
        {
            _themeChoice.SelectedItem = AppTheme.IsDark ? "Dark" : "Light";
            ShowError(ex);
        }
        finally { _changingTheme = false; }
    }
}
