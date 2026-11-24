using System.Windows;
using System.Windows.Controls;
using Droideck.Core;
using Droideck.Views;
using Microsoft.Win32;

namespace Droideck;

public partial class MainWindow : Window
{
    static readonly string[] Names = { "Devices", "Mirror", "Files", "Apps", "Logcat", "Stats", "Terminal", "Macros", "Tools" };

    readonly List<UserControl> views = new();
    bool ready;
    bool syncing;
    bool started;
    string lastSig = "\u0001";

    public MainWindow()
    {
        InitializeComponent();

        views.Add(new DevicesView());
        views.Add(new MirrorView());
        views.Add(new FilesView());
        views.Add(new AppsView());
        views.Add(new LogcatView());
        views.Add(new StatsView());
        views.Add(new TerminalView());
        views.Add(new MacroView());
        views.Add(new ToolsView());

        foreach (var v in views)
        {
            v.Visibility = Visibility.Collapsed;
            Host.Children.Add(v);
        }

        ready = true;
        NavDevices.IsChecked = true;

        AppState.DeviceChanged += OnDeviceChanged;
        AppState.StatusChanged += s => StatusText.Text = s;

        SourceInitialized += (_, _) => Native.DarkTitleBar(this);
        Loaded += async (_, _) => await StartAsync();
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!ready || sender is not RadioButton rb || rb.Tag is not string tag) return;
        int idx = int.Parse(tag);
        CrumbText.Text = "›   " + Names[idx];
        for (int i = 0; i < views.Count; i++)
            views[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;
    }

    async Task StartAsync()
    {
        if (!Adb.Installed) await DownloadAsync();
        if (!Adb.Installed) return;

        Overlay.Visibility = Visibility.Collapsed;
        if (started) return;
        started = true;
        await Adb.RunAsync(new[] { "start-server" }, 20000);
        _ = PollAsync();
    }

    async Task DownloadAsync()
    {
        Overlay.Visibility = Visibility.Visible;
        OverlayButtons.Visibility = Visibility.Collapsed;
        OverlayTitle.Text = "Setting up ADB";
        OverlayText.Text = "Downloading Android platform-tools from Google. This only happens once.";
        OverlayBar.IsIndeterminate = true;
        try
        {
            await AdbSetup.DownloadAsync((text, pct) =>
            {
                OverlayText.Text = text;
                if (pct >= 0)
                {
                    OverlayBar.IsIndeterminate = false;
                    OverlayBar.Value = pct;
                }
                else OverlayBar.IsIndeterminate = true;
            });
        }
        catch (Exception ex)
        {
            OverlayTitle.Text = "Download failed";
            OverlayText.Text = ex.Message + "\n\nCheck your internet connection and retry, or point Droideck to an adb.exe you already have.";
            OverlayBar.IsIndeterminate = false;
            OverlayBar.Value = 0;
            OverlayButtons.Visibility = Visibility.Visible;
        }
    }

    async void Retry_Click(object sender, RoutedEventArgs e) => await StartAsync();

    async void BrowseAdb_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "adb.exe|adb.exe", Title = "Select adb.exe" };
        if (dlg.ShowDialog(this) != true) return;
        Store.Settings.AdbPath = dlg.FileName;
        Store.Save();
        await StartAsync();
    }

    async Task PollAsync()
    {
        while (true)
        {
            await PollOnceAsync();
            await Task.Delay(2500);
        }
    }

    async Task PollOnceAsync()
    {
        if (!Adb.Installed) return;
        var r = await Adb.RunAsync(new[] { "devices", "-l" }, 8000);
        if (r.Ok) ApplyDevices(Adb.ParseDevices(r.Output));
    }

    void ApplyDevices(List<DeviceInfo> list)
    {
        var sig = string.Join("|", list.Select(d => d.Serial + ":" + d.State + ":" + d.Model));
        if (sig == lastSig) return;
        lastSig = sig;

        var prev = AppState.Serial;
        syncing = true;
        AppState.SetDevices(list);
        DeviceBox.ItemsSource = list;
        var pick = list.FirstOrDefault(d => d.Serial == prev && d.Ready) ?? list.FirstOrDefault(d => d.Ready);
        DeviceBox.SelectedItem = pick;
        syncing = false;

        AppState.Select(pick?.Serial);
        UpdateStateLabel();
    }

    void UpdateStateLabel()
    {
        var sel = AppState.Devices.FirstOrDefault(d => d.Serial == AppState.Serial);
        SerialText.Text = sel != null ? sel.Display : "";
        if (sel != null)
        {
            StateDot.Fill = Theme.Ok;
            StateText.Text = "Connected";
            return;
        }
        var blocked = AppState.Devices.FirstOrDefault();
        if (blocked != null)
        {
            StateDot.Fill = Theme.Warn;
            StateText.Text = blocked.State == "unauthorized" ? "Allow USB debugging on the phone" : "Device " + blocked.State;
            return;
        }
        StateDot.Fill = Theme.Danger;
        StateText.Text = "No device";
    }

    void OnDeviceChanged()
    {
        syncing = true;
        DeviceBox.SelectedItem = AppState.Devices.FirstOrDefault(d => d.Serial == AppState.Serial);
        syncing = false;
        UpdateStateLabel();
    }

    void DeviceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncing) return;
        if (DeviceBox.SelectedItem is not DeviceInfo d) return;
        if (d.Ready) AppState.Select(d.Serial);
        else AppState.SetStatus(d.Title + " is " + d.State + ". Unlock the phone and accept the USB debugging prompt.");
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        lastSig = "\u0001";
        AppState.SetStatus("Refreshing devices...");
        await PollOnceAsync();
        AppState.SetStatus("Ready");
    }
}
