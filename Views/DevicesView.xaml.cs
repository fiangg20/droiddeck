using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Droideck.Core;

namespace Droideck.Views;

public partial class DevicesView : UserControl
{
    readonly ObservableCollection<string> saved = new();
    int infoVersion;

    public DevicesView()
    {
        InitializeComponent();
        foreach (var s in Store.Settings.Saved) saved.Add(s);
        SavedList.ItemsSource = saved;

        AppState.DevicesUpdated += () =>
        {
            DeviceList.ItemsSource = null;
            DeviceList.ItemsSource = AppState.Devices;
            EmptyText.Visibility = AppState.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SelectCurrent();
        };
        AppState.DeviceChanged += () =>
        {
            SelectCurrent();
            _ = LoadInfoAsync();
        };
    }

    void SelectCurrent()
    {
        DeviceList.SelectedItem = AppState.Devices.FirstOrDefault(d => d.Serial == AppState.Serial);
    }

    void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceList.SelectedItem is not DeviceInfo d) return;
        if (d.Ready) AppState.Select(d.Serial);
        else AppState.SetStatus(d.Title + " is " + d.State + ". Unlock the phone and accept the USB debugging prompt.");
    }

    void Row(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var a = new TextBlock { Text = key, Foreground = Theme.Muted };
        var b = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(b, 1);
        g.Children.Add(a);
        g.Children.Add(b);
        InfoPanel.Children.Add(g);
    }

    async Task LoadInfoAsync()
    {
        var serial = AppState.Serial;
        int ver = ++infoVersion;
        InfoPanel.Children.Clear();
        if (serial == null)
        {
            InfoTitle.Text = "No device selected";
            InfoSub.Text = "";
            return;
        }
        InfoTitle.Text = "Loading...";
        InfoSub.Text = serial;

        const string cmd = "echo mf=$(getprop ro.product.manufacturer); echo model=$(getprop ro.product.model); " +
                           "echo android=$(getprop ro.build.version.release); echo sdk=$(getprop ro.build.version.sdk); " +
                           "echo abi=$(getprop ro.product.cpu.abi); echo build=$(getprop ro.build.display.id); " +
                           "echo patch=$(getprop ro.build.version.security_patch); echo size=$(wm size | tr '\\n' ' '); " +
                           "echo dens=$(wm density | tr '\\n' ' '); echo df=$(df /data | tail -n 1); " +
                           "echo kernel=$(uname -r)";
        var r = await Adb.ShellAsync(serial, cmd, 20000);
        if (ver != infoVersion) return;

        var kv = new Dictionary<string, string>();
        foreach (var line in Util.NonEmptyLines(r.Output))
        {
            var i = line.IndexOf('=');
            if (i > 0) kv[line.Substring(0, i)] = line.Substring(i + 1).Trim();
        }
        string Get(string k) => kv.TryGetValue(k, out var v) ? v : "";

        InfoPanel.Children.Clear();
        var name = (Get("mf") + " " + Get("model")).Trim();
        InfoTitle.Text = name.Length == 0 ? "Android device" : name;
        var dev = AppState.Devices.FirstOrDefault(d => d.Serial == serial);
        InfoSub.Text = serial + (dev != null ? "  ·  " + dev.Badge : "");

        Row("Android", Get("android") + (Get("sdk").Length > 0 ? "  (API " + Get("sdk") + ")" : ""));
        Row("Build", Get("build"));
        Row("Security patch", Get("patch"));
        Row("CPU", Get("abi"));
        Row("Kernel", Get("kernel"));
        Row("Screen", (Get("size").Replace("Physical size:", "").Trim() + "   " + Get("dens").Replace("Physical density:", "").Trim()).Trim());

        var parts = Get("df").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 5 && long.TryParse(parts[1], out var total) && long.TryParse(parts[2], out var used))
            Row("Storage", Util.Size(used * 1024) + " used of " + Util.Size(total * 1024) + "  (" + parts[4] + ")");
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadInfoAsync();
        AppState.SetStatus("Device info refreshed");
    }

    void RememberDevice(string endpoint)
    {
        if (saved.Contains(endpoint)) return;
        saved.Add(endpoint);
        Store.Settings.Saved = saved.ToList();
        Store.Save();
    }

    async void GoWireless_Click(object sender, RoutedEventArgs e)
    {
        var s = AppState.Serial;
        if (s == null)
        {
            AppState.SetStatus("Plug the phone in with USB and select it first.");
            return;
        }
        if (s.Contains(':'))
        {
            AppState.SetStatus("This device is already wireless.");
            return;
        }
        AppState.SetStatus("Looking for the phone's Wi-Fi address...");
        var ipRes = await Adb.ShellAsync(s, "ip -f inet addr show wlan0", 10000);
        var m = Regex.Match(ipRes.Output, @"inet (\d+\.\d+\.\d+\.\d+)");
        if (!m.Success)
        {
            AppState.SetStatus("Could not find the phone's Wi-Fi IP. Make sure it is connected to Wi-Fi.");
            return;
        }
        var endpoint = m.Groups[1].Value + ":5555";
        AppState.SetStatus("Switching adb to network mode...");
        await Adb.RunAsync(new[] { "-s", s, "tcpip", "5555" }, 15000);
        await Task.Delay(2000);
        var c = await Adb.RunAsync(new[] { "connect", endpoint }, 15000);
        if (c.Output.Contains("connected"))
        {
            RememberDevice(endpoint);
            AppState.SetStatus("Connected over Wi-Fi: " + endpoint + ". You can unplug the cable now.");
        }
        else AppState.SetStatus("Wireless connect failed: " + c.All.Trim());
    }

    async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        var s = AppState.Serial;
        if (s == null || !s.Contains(':'))
        {
            AppState.SetStatus("Select a wireless device to disconnect.");
            return;
        }
        var r = await Adb.RunAsync(new[] { "disconnect", s }, 10000);
        AppState.SetStatus(r.Output.Trim().Length > 0 ? r.Output.Trim() : "Disconnected");
    }

    async void Pair_Click(object sender, RoutedEventArgs e)
    {
        var host = PairHost.Text.Trim();
        var code = PairCode.Text.Trim();
        if (host.Length == 0 || code.Length == 0)
        {
            AppState.SetStatus("Enter the pairing address and the pairing code.");
            return;
        }
        AppState.SetStatus("Pairing...");
        var r = await Adb.RunAsync(new[] { "pair", host, code }, 30000);
        var text = r.All.Trim();
        AppState.SetStatus(text.Length > 0 ? text : "Pairing finished");
        if (r.Output.Contains("Successfully")) PairCode.Text = "";
    }

    async Task ConnectAsync(string endpoint)
    {
        AppState.SetStatus("Connecting to " + endpoint + "...");
        var r = await Adb.RunAsync(new[] { "connect", endpoint }, 15000);
        var text = r.All.Trim();
        if (r.Output.Contains("connected to") || r.Output.Contains("already connected"))
        {
            RememberDevice(endpoint);
            AppState.SetStatus(text);
        }
        else AppState.SetStatus(text.Length > 0 ? text : "Could not connect");
    }

    async void Connect_Click(object sender, RoutedEventArgs e)
    {
        var ep = ConnectHost.Text.Trim();
        if (ep.Length == 0)
        {
            AppState.SetStatus("Enter the device address, like 192.168.1.20:5555");
            return;
        }
        if (!ep.Contains(':')) ep += ":5555";
        await ConnectAsync(ep);
    }

    async void SavedList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SavedList.SelectedItem is string ep) await ConnectAsync(ep);
    }

    void RemoveSaved_Click(object sender, RoutedEventArgs e)
    {
        if (SavedList.SelectedItem is not string ep) return;
        saved.Remove(ep);
        Store.Settings.Saved = saved.ToList();
        Store.Save();
    }
}
