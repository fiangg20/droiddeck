using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Droideck.Core;
using Microsoft.Win32;

namespace Droideck.Views;

public partial class FilesView : UserControl
{
    string cur = "/sdcard";
    List<FileEntry> all = new();
    bool loaded;
    bool ready;

    public FilesView()
    {
        InitializeComponent();
        ready = true;
        PathBox.Text = cur;
        AppState.DeviceChanged += () =>
        {
            all = new List<FileEntry>();
            FileList.ItemsSource = null;
            loaded = false;
            if (IsVisible) _ = GoAsync("/sdcard");
        };
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true && !loaded && AppState.Serial != null) await GoAsync(cur);
        };
    }

    void SetBusy(bool on, string? text = null)
    {
        Busy.Visibility = on ? Visibility.Visible : Visibility.Hidden;
        if (text != null) InfoText.Text = text;
    }

    static string Normalize(string p)
    {
        p = p.Trim().Replace('\\', '/');
        if (p.Length == 0) p = "/";
        while (p.Length > 1 && p.EndsWith("/")) p = p.Substring(0, p.Length - 1);
        return p.StartsWith("/") ? p : "/" + p;
    }

    static string Join(string dir, string name) => dir == "/" ? "/" + name : dir + "/" + name;

    async Task GoAsync(string path)
    {
        var serial = AppState.Serial;
        if (serial == null) return;
        path = Normalize(path);
        SetBusy(true, "Loading " + path + "...");
        var r = await Adb.ShellAsync(serial, "ls -la " + Util.Quote(path == "/" ? "/" : path + "/"), 30000);
        SetBusy(false);
        var entries = new List<FileEntry>();
        foreach (var line in Util.Lines(r.Output))
        {
            var fe = FileEntry.Parse(line);
            if (fe != null) entries.Add(fe);
        }
        if (entries.Count == 0)
        {
            var text = (r.Output + " " + r.Error).Trim();
            if (text.Contains("denied") || text.Contains("No such") || text.Contains("Not a directory"))
            {
                InfoText.Text = "Can't open " + path + ": " + text.Split('\n')[0].Trim();
                return;
            }
        }
        cur = path;
        loaded = true;
        PathBox.Text = cur;
        all = entries.OrderByDescending(f => f.IsDir).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        ApplyFilter();
    }

    void ApplyFilter()
    {
        var f = FilterBox.Text.Trim();
        var list = f.Length == 0 ? all : all.Where(x => x.Name.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        FileList.ItemsSource = list;
        InfoText.Text = cur + "   ·   " + list.Count + " item(s)";
    }

    void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (ready) ApplyFilter();
    }

    async void Up_Click(object sender, RoutedEventArgs e)
    {
        if (cur == "/") return;
        var i = cur.LastIndexOf('/');
        await GoAsync(i <= 0 ? "/" : cur.Substring(0, i));
    }

    async void Home_Click(object sender, RoutedEventArgs e) => await GoAsync("/sdcard");

    async void Reload_Click(object sender, RoutedEventArgs e) => await GoAsync(cur);

    async void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await GoAsync(PathBox.Text);
    }

    List<FileEntry> Selected() => FileList.SelectedItems.Cast<FileEntry>().ToList();

    async Task PushAsync(IEnumerable<string> paths)
    {
        var serial = AppState.Serial;
        if (serial == null) return;
        int ok = 0;
        foreach (var p in paths)
        {
            SetBusy(true, "Pushing " + Path.GetFileName(p) + "...");
            var r = await Adb.RunAsync(new[] { "-s", serial, "push", p, cur + "/" }, 3600000);
            if (r.Ok) ok++;
            else AppState.SetStatus("Push failed: " + r.All.Trim());
        }
        SetBusy(false);
        await GoAsync(cur);
        InfoText.Text = "Pushed " + ok + " item(s) to " + cur;
    }

    async void FileList_Drop(object sender, DragEventArgs e)
    {
        if (!AppState.Need()) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        await PushAsync(paths);
    }

    async void PushFiles_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        var dlg = new OpenFileDialog { Multiselect = true, Title = "Choose files to push" };
        if (dlg.ShowDialog() == true) await PushAsync(dlg.FileNames);
    }

    async void PushFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        var dlg = new OpenFolderDialog { Title = "Choose a folder to push" };
        if (dlg.ShowDialog() == true) await PushAsync(new[] { dlg.FolderName });
    }

    async Task<int> PullAsync(IEnumerable<FileEntry> items, string localDir)
    {
        var serial = AppState.Serial;
        if (serial == null) return 0;
        int ok = 0;
        foreach (var it in items)
        {
            SetBusy(true, "Pulling " + it.Name + "...");
            var r = await Adb.RunAsync(new[] { "-s", serial, "pull", Join(cur, it.Name), localDir }, 3600000);
            if (r.Ok) ok++;
            else AppState.SetStatus("Pull failed: " + r.All.Trim());
        }
        SetBusy(false);
        return ok;
    }

    async void Pull_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        var sel = Selected();
        if (sel.Count == 0)
        {
            InfoText.Text = "Select one or more files or folders first.";
            return;
        }
        var dlg = new OpenFolderDialog { Title = "Save to folder" };
        if (dlg.ShowDialog() != true) return;
        int ok = await PullAsync(sel, dlg.FolderName);
        InfoText.Text = "Pulled " + ok + " of " + sel.Count + " item(s) to " + dlg.FolderName;
    }

    async void FileList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is not FileEntry fe) return;
        if (fe.IsDir)
        {
            await GoAsync(Join(cur, fe.Name));
            return;
        }
        var tmp = Path.Combine(Path.GetTempPath(), "Droideck-open");
        Directory.CreateDirectory(tmp);
        int ok = await PullAsync(new[] { fe }, tmp);
        if (ok == 1)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(tmp, fe.Name)) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                InfoText.Text = "Could not open the file: " + ex.Message;
            }
        }
    }

    async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var serial = AppState.Serial;
        if (serial == null) return;
        var name = Dialogs.Ask(Window.GetWindow(this), "New folder", "Folder name");
        if (string.IsNullOrWhiteSpace(name)) return;
        await Adb.ShellAsync(serial, "mkdir -p " + Util.Quote(Join(cur, name.Trim())));
        await GoAsync(cur);
    }

    async void Rename_Click(object sender, RoutedEventArgs e)
    {
        var serial = AppState.Serial;
        if (serial == null || FileList.SelectedItem is not FileEntry fe) return;
        var name = Dialogs.Ask(Window.GetWindow(this), "Rename", "New name", fe.Name);
        if (string.IsNullOrWhiteSpace(name) || name == fe.Name) return;
        await Adb.ShellAsync(serial, "mv " + Util.Quote(Join(cur, fe.Name)) + " " + Util.Quote(Join(cur, name.Trim())));
        await GoAsync(cur);
    }

    async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var serial = AppState.Serial;
        var sel = Selected();
        if (serial == null || sel.Count == 0) return;
        if (!Dialogs.Confirm(Window.GetWindow(this), "Delete " + sel.Count + " item(s) from the phone? This cannot be undone.")) return;
        SetBusy(true, "Deleting...");
        foreach (var it in sel)
            await Adb.ShellAsync(serial, "rm -rf " + Util.Quote(Join(cur, it.Name)));
        SetBusy(false);
        await GoAsync(cur);
    }
}
