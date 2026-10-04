namespace MikroTikManager;

public sealed partial class MainForm
{
    private readonly ComboBox _themeChoice = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
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
