using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Droideck.Core;
using Microsoft.Win32;

namespace Droideck.Setup;

public partial class InstallWizard : Window
{
    int step;
    bool busy;
    string target = "";
    string lastLog = "";

    public InstallWizard()
    {
        InitializeComponent();
        DirBox.Text = Installer.DefaultDir;
        DirNote.Text = "Droideck installs for your Windows account only. You can remove it any time from Windows Settings, Installed apps.";
        SourceInitialized += (_, _) => Native.DarkTitleBar(this);
        Closing += (_, e) =>
        {
            if (busy) e.Cancel = true;
        };
        ShowStep(0);
    }

    void ShowStep(int s)
    {
        step = s;
        Page0.Visibility = s == 0 ? Visibility.Visible : Visibility.Collapsed;
        Page1.Visibility = s == 1 ? Visibility.Visible : Visibility.Collapsed;
        Page2.Visibility = s == 2 ? Visibility.Visible : Visibility.Collapsed;
        Page3.Visibility = s == 3 ? Visibility.Visible : Visibility.Collapsed;

        BackBtn.Visibility = s == 1 ? Visibility.Visible : Visibility.Collapsed;
        CancelBtn.Visibility = s >= 2 ? Visibility.Collapsed : Visibility.Visible;
        PortablePanel.Visibility = s == 0 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.IsEnabled = s != 2;
        NextBtn.Content = s == 1 ? "Install" : s == 3 ? "Finish" : "Next";

        var texts = new[] { Step0, Step1, Step2, Step3 };
        for (int i = 0; i < texts.Length; i++)
        {
            texts[i].Foreground = i == s ? Theme.Fg : Theme.Muted;
            texts[i].FontWeight = i == s ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose where to install Droideck" };
        if (dlg.ShowDialog(this) == true) DirBox.Text = Path.Combine(dlg.FolderName, "Droideck");
    }

    void Back_Click(object sender, RoutedEventArgs e)
    {
        if (step == 1) ShowStep(0);
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    void Portable_Click(object sender, RoutedEventArgs e)
    {
        if (DontAsk.IsChecked == true)
        {
            Store.Settings.SkipSetup = true;
            Store.Save();
        }
        var main = new MainWindow();
        Application.Current.MainWindow = main;
        main.Show();
        Close();
    }

    async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (step == 0)
        {
            ShowStep(1);
            return;
        }
        if (step == 1)
        {
            await RunInstallAsync();
            return;
        }
        if (step == 3)
        {
            if (LaunchCheck.IsChecked == true && File.Exists(target))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                }
                catch
                {
                }
                Application.Current.Shutdown();
                return;
            }
            Close();
        }
    }

    void AddLog(string text)
    {
        if (text == lastLog) return;
        lastLog = text;
        LogBox.AppendText(text + "\n");
        LogBox.ScrollToEnd();
    }

    async Task RunInstallAsync()
    {
        var dir = DirBox.Text.Trim();
        if (dir.Length == 0)
        {
            MessageBox.Show(this, "Choose an install folder first.", "Droideck Setup", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ShowStep(2);
        busy = true;
        LogBox.Clear();
        lastLog = "";
        Bar.Value = 0;
        try
        {
            target = await Installer.InstallAsync(dir, DesktopCheck.IsChecked == true, StartCheck.IsChecked == true, AdbCheck.IsChecked == true, (text, pct) =>
            {
                StatusText.Text = text;
                Bar.Value = Math.Clamp(pct, 0, 100);
                if (!text.StartsWith("Downloading platform-tools")) AddLog(text);
            });
            busy = false;
            DoneText.Text = "Installed to " + Path.GetDirectoryName(target) + ".\nUse the Start menu or desktop shortcut to open Droideck. On first launch, plug in your phone and allow USB debugging.";
            ShowStep(3);
        }
        catch (Exception ex)
        {
            busy = false;
            ShowStep(1);
            MessageBox.Show(this, "Setup could not finish:\n\n" + ex.Message, "Droideck Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
