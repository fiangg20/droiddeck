using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Droideck.Core;
using Microsoft.Win32;

namespace Droideck.Views;

public partial class AppsView : UserControl
{
    List<AppEntry> all = new();
    bool loaded;
    bool ready;

    public AppsView()
    {
        InitializeComponent();
        ready = true;
        AppState.DeviceChanged += () =>
        {
            all = new List<AppEntry>();
            loaded = false;
            AppList.ItemsSource = null;
            EmptyText.Visibility = Visibility.Visible;
            if (IsVisible && AppState.Serial != null) _ = LoadAsync();
        };
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true && !loaded && AppState.Serial != null) await LoadAsync();
        };
    }

    async Task LoadAsync()
    {
        var serial = AppState.Serial;
        if (serial == null) return;
        loaded = true;
        EmptyText.Text = "Loading apps...";
        EmptyText.Visibility = Visibility.Visible;
        var r = await Adb.ShellAsync(serial, "pm list packages -f; echo ===U; pm list packages -3; echo ===D; pm list packages -d", 60000);
        if (serial != AppState.Serial) return;
        if (!r.Ok && string.IsNullOrWhiteSpace(r.Output))
        {
            EmptyText.Text = "Could not list apps: " + r.All.Trim();
            loaded = false;
            return;
        }

        var user = new HashSet<string>();
        var disabled = new HashSet<string>();
        var list = new List<AppEntry>();
        int section = 0;
        foreach (var line in Util.NonEmptyLines(r.Output))
        {
            var t = line.Trim();
            if (t == "===U")
            {
                section = 1;
                continue;
            }
            if (t == "===D")
            {
                section = 2;
                continue;
            }
            if (!t.StartsWith("package:")) continue;
            var body = t.Substring(8);
            if (section == 0)
            {
                var eq = body.LastIndexOf('=');
                if (eq <= 0) continue;
                list.Add(new AppEntry { Apk = body.Substring(0, eq), Package = body.Substring(eq + 1) });
            }
            else if (section == 1) user.Add(body);
            else disabled.Add(body);
        }
        foreach (var a in list)
        {
            a.IsSystem = !user.Contains(a.Package);
            a.IsDisabled = disabled.Contains(a.Package);
        }
        all = list.OrderBy(a => a.Package, StringComparer.OrdinalIgnoreCase).ToList();
        ApplyFilter();
    }

    void ApplyFilter()
    {
        if (!ready) return;
        var q = SearchBox.Text.Trim();
        IEnumerable<AppEntry> src = all;
        src = FilterBox.SelectedIndex switch
        {
            0 => src.Where(a => !a.IsSystem),
            1 => src.Where(a => a.IsSystem),
            3 => src.Where(a => a.IsDisabled),
            _ => src
        };
        if (q.Length > 0) src = src.Where(a => a.Package.Contains(q, StringComparison.OrdinalIgnoreCase));
        var list = src.ToList();
        AppList.ItemsSource = list;
        EmptyText.Text = all.Count == 0 ? "Select a device to list its apps." : "No apps match.";
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SelText.Text = list.Count + " app(s)";
    }

    void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    void Search_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();

    async void Reload_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    List<AppEntry> Selected() => AppList.SelectedItems.Cast<AppEntry>().ToList();

    void AppList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var sel = Selected();
        SelText.Text = sel.Count == 0 ? AppList.Items.Count + " app(s)" : sel.Count == 1 ? sel[0].Package : sel.Count + " apps selected";
    }

    bool Pick(out List<AppEntry> sel)
    {
        sel = Selected();
        if (!AppState.Need()) return false;
        if (sel.Count == 0)
        {
            AppState.SetStatus("Select an app first.");
            return false;
        }
        return true;
    }

    async void AppList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AppList.SelectedItem is not AppEntry a || AppState.Serial == null) return;
        await Adb.ShellAsync(AppState.Serial, "monkey -p " + a.Package + " -c android.intent.category.LAUNCHER 1", 15000);
    }

    async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (!Pick(out var sel)) return;
        var r = await Adb.ShellAsync(AppState.Serial!, "monkey -p " + sel[0].Package + " -c android.intent.category.LAUNCHER 1", 15000);
        AppState.SetStatus(r.Output.Contains("No activities found") ? sel[0].Package + " has no launcher screen." : "Launched " + sel[0].Package);
    }

    async void Info_Click(object sender, RoutedEventArgs e)
    {
        if (!Pick(out var sel)) return;
        await Adb.ShellAsync(AppState.Serial!, "am start -a android.settings.APPLICATION_DETAILS_SETTINGS -d package:" + sel[0].Package, 15000);
    }

    async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (!Pick(out var sel)) return;
        foreach (var a in sel) await Adb.ShellAsync(AppState.Serial!, "am force-stop " + a.Package, 15000);
        AppState.SetStatus("Force stopped " + sel.Count + " app(s).");
    }

    async void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (!Pick(out var sel)) return;
        if (!Dialogs.Confirm(Window.GetWindow(this), "Clear all data of " + sel.Count + " app(s)? Logins and settings inside the app are lost.")) return;
        foreach (var a in sel) await Adb.ShellAsync(AppState.Serial!, "pm clear " + a.Package, 30000);
        AppState.SetStatus("Cleared data of " + sel.Count + " app(s).");
    }

    void Perms_Click(object sender, RoutedEventArgs e)
    {
        if (!Pick(out var sel)) return;
        Dialogs.Permissions(Window.GetWindow(this), AppState.Serial!, sel[0].Package);
    }

    async void Extract_Click(object sender, RoutedEventArgs e)
    {
        if (!Pick(out var sel)) return;
        var dlg = new OpenFolderDialog { Title = "Save APKs to folder" };
        if (dlg.ShowDialog() != true) return;
        var serial = AppState.Serial!;
        int count = 0;
        foreach (var a in sel)
        {
            var dir = Path.Combine(dlg.FolderName, Util.SafeName(a.Package));
            Directory.CreateDirectory(dir);
            var pm = await Adb.ShellAsync(serial, "pm path " + a.Package, 20000);
            foreach (var line in Util.NonEmptyLines(pm.Output))
            {
                if (!line.StartsWith("package:")) continue;
                AppState.SetStatus("Extracting " + a.Package + "...");
                var r = await Adb.RunAsync(new[] { "-s", serial, "pull", line.Substring(8).Trim(), dir }, 600000);
                if (r.Ok) count++;
            }
        }
        AppState.SetStatus("Extracted " + count + " APK file(s) to " + dlg.FolderName);
    }

    async void Disable_Click(object sender, RoutedEventArgs e)
    {
        if (!Pick(out var sel)) return;
        foreach (var a in sel) await Adb.ShellAsync(AppState.Serial!, "pm disable-user --user 0 " + a.Package, 15000);
        await LoadAsync();
    }

    async void Enable_Click(object sender, RoutedEventArgs e)
    {
        if (!Pick(out var sel)) return;
        foreach (var a in sel) await Adb.ShellAsync(AppState.Serial!, "pm enable " + a.Package, 15000);
        await LoadAsync();
    }

    async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (!Pick(out var sel)) return;
        if (!Dialogs.Confirm(Window.GetWindow(this), "Uninstall " + sel.Count + " app(s)?" + (sel.Any(a => a.IsSystem) ? "\nSystem apps are removed for the current user only and can be restored in the Debloater." : ""))) return;
        var serial = AppState.Serial!;
        int ok = 0;
        foreach (var a in sel)
        {
            AdbResult r = a.IsSystem
                ? await Adb.ShellAsync(serial, "pm uninstall -k --user 0 " + a.Package, 60000)
                : await Adb.RunAsync(new[] { "-s", serial, "uninstall", a.Package }, 60000);
            if (r.Output.Contains("Success")) ok++;
        }
        AppState.SetStatus("Uninstalled " + ok + " of " + sel.Count + " app(s).");
        await LoadAsync();
    }

    async Task InstallAsync(IEnumerable<string> files)
    {
        var serial = AppState.Serial;
        if (serial == null) return;
        int ok = 0;
        foreach (var f in files.Where(x => x.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)))
        {
            AppState.SetStatus("Installing " + Path.GetFileName(f) + "...");
            var r = await Adb.RunAsync(new[] { "-s", serial, "install", "-r", f }, 600000);
            if (r.Output.Contains("Success")) ok++;
            else AppState.SetStatus("Install failed: " + r.All.Trim());
        }
        if (ok > 0) AppState.SetStatus("Installed " + ok + " APK(s).");
        await LoadAsync();
    }

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        var dlg = new OpenFileDialog { Multiselect = true, Filter = "Android packages (*.apk)|*.apk", Title = "Choose APK files" };
        if (dlg.ShowDialog() == true) await InstallAsync(dlg.FileNames);
    }

    async void AppList_Drop(object sender, DragEventArgs e)
    {
        if (!AppState.Need()) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        await InstallAsync(paths);
    }

    void Debloat_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        var installed = all.Select(a => a.Package).ToHashSet();
        Dialogs.Debloat(Window.GetWindow(this), AppState.Serial!, installed);
    }
}
