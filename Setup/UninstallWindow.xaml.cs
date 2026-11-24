using System.Windows;
using Droideck.Core;

namespace Droideck.Setup;

public partial class UninstallWindow : Window
{
    string dir = "";
    bool removed;
    bool busy;

    public UninstallWindow()
    {
        InitializeComponent();
        dir = Installer.InstalledDir() ?? Path.GetDirectoryName(Installer.ExePath) ?? "";
        SourceInitialized += (_, _) => Native.DarkTitleBar(this);
        Closing += (_, e) =>
        {
            if (busy) e.Cancel = true;
        };
        Closed += (_, _) =>
        {
            if (removed) Installer.ScheduleCleanup(dir);
        };
    }

    async void Go_Click(object sender, RoutedEventArgs e)
    {
        busy = true;
        GoBtn.IsEnabled = false;
        CloseBtn.IsEnabled = false;
        DataCheck.IsEnabled = false;
        Bar.Visibility = Visibility.Visible;
        try
        {
            await Installer.UninstallAsync(DataCheck.IsChecked == true);
            removed = true;
            TitleText.Text = "Droideck was removed";
            Body.Text = "Shortcuts and the Windows entry are gone. The program folder is deleted a moment after you close this window.";
        }
        catch (Exception ex)
        {
            TitleText.Text = "Could not finish";
            Body.Text = ex.Message;
        }
        busy = false;
        Bar.Visibility = Visibility.Collapsed;
        GoBtn.Visibility = Visibility.Collapsed;
        CloseBtn.IsEnabled = true;
        CloseBtn.Content = "Close";
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
