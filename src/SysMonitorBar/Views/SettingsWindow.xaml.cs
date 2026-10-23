using System.Windows;
using SysMonitorBar.Core.Settings;
using SysMonitorBar.ViewModels;

namespace SysMonitorBar.Views;

/// <summary>
/// Edits a copy of the settings. Edits preview live on the bar; Apply/OK persist them;
/// closing without applying (Cancel, Esc, the close box) reverts the bar to the applied settings.
/// </summary>
internal partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;
    private readonly Func<AppSettings, bool> _apply;

    /// <param name="apply">Persists the settings; returns false when saving failed.</param>
    public SettingsWindow(SettingsViewModel viewModel, Func<AppSettings, bool> apply)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _apply = apply;
        DataContext = viewModel;
    }

    private bool ApplyEdits()
    {
        var settings = _viewModel.ToSettings();
        if (!_apply(settings))
        {
            return false;
        }
        _viewModel.MarkApplied(settings);
        return true;
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e) => _viewModel.RestoreDefaults();

    private void Apply_Click(object sender, RoutedEventArgs e) => ApplyEdits();

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (ApplyEdits())
        {
            Close();
        }
    }
}
