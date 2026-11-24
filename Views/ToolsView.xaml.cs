using System.Windows;
using System.Windows.Controls;
using Droideck.Core;
using Microsoft.Win32;

namespace Droideck.Views;

public partial class ToolsView : UserControl
{
    public ToolsView()
    {
        InitializeComponent();
        Log("Output from the tools shows up here.");
    }

    void Log(string text)
    {
        Output.AppendText(text.TrimEnd() + "\n");
        Output.ScrollToEnd();
    }

    async void Power_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        if (sender is not Button b || b.Tag is not string mode) return;
        var serial = AppState.Serial!;
        if (mode != "" && !Dialogs.Confirm(Window.GetWindow(this), "Really send the phone to '" + (mode == "poweroff" ? "power off" : mode) + "'?")) return;

        AdbResult r = mode == "poweroff"
            ? await Adb.ShellAsync(serial, "reboot -p", 15000)
            : await Adb.RunAsync(mode == "" ? new[] { "-s", serial, "reboot" } : new[] { "-s", serial, "reboot", mode }, 15000);
        Log("> reboot " + mode + "\n" + r.All.Trim());
    }

    async void RestartAdb_Click(object sender, RoutedEventArgs e)
    {
        Log("> adb kill-server / start-server");
        await Adb.RunAsync(new[] { "kill-server" }, 15000);
        var r = await Adb.RunAsync(new[] { "start-server" }, 20000);
        Log(r.All.Trim().Length > 0 ? r.All.Trim() : "ADB restarted.");
    }

    async void Fastboot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string cmd) return;
        if (!File.Exists(Adb.FastbootPath))
        {
            Log("fastboot.exe not found next to adb.exe.");
            return;
        }
        Log("> fastboot " + cmd);
        var r = await Adb.RunExeAsync(Adb.FastbootPath, cmd.Split(' '), 60000);
        var text = r.All.Trim();
        Log(text.Length > 0 ? text : "(no output. Is a phone in bootloader mode?)");
    }

    async void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        var r = await Adb.RunAsync(new[] { "-s", AppState.Serial!, "forward", "tcp:" + PortLocal.Text.Trim(), "tcp:" + PortRemote.Text.Trim() }, 15000);
        Log("> forward tcp:" + PortLocal.Text.Trim() + " -> phone tcp:" + PortRemote.Text.Trim() + "\n" + (r.Ok ? "OK" : r.All.Trim()));
    }

    async void Reverse_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        var r = await Adb.RunAsync(new[] { "-s", AppState.Serial!, "reverse", "tcp:" + PortRemote.Text.Trim(), "tcp:" + PortLocal.Text.Trim() }, 15000);
        Log("> reverse phone tcp:" + PortRemote.Text.Trim() + " -> PC tcp:" + PortLocal.Text.Trim() + "\n" + (r.Ok ? "OK" : r.All.Trim()));
    }

    async void ListPorts_Click(object sender, RoutedEventArgs e)
    {
        var f = await Adb.RunAsync(new[] { "forward", "--list" }, 10000);
        Log("> forward --list\n" + (f.Output.Trim().Length > 0 ? f.Output.Trim() : "(none)"));
        if (AppState.Serial != null)
        {
            var r = await Adb.RunAsync(new[] { "-s", AppState.Serial, "reverse", "--list" }, 10000);
            Log("> reverse --list\n" + (r.Output.Trim().Length > 0 ? r.Output.Trim() : "(none)"));
        }
    }

    async void RemovePorts_Click(object sender, RoutedEventArgs e)
    {
        await Adb.RunAsync(new[] { "forward", "--remove-all" }, 10000);
        if (AppState.Serial != null) await Adb.RunAsync(new[] { "-s", AppState.Serial, "reverse", "--remove-all" }, 10000);
        Log("All port forwards removed.");
    }

    async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        var serial = AppState.Serial!;
        var dlg = new OpenFolderDialog { Title = "Choose where to save the backup" };
        if (dlg.ShowDialog() != true) return;

        var root = Path.Combine(dlg.FolderName, "Droideck-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmm"));
        var media = Path.Combine(root, "media");
        Directory.CreateDirectory(media);
        Log("Backup started: " + root);

        var folders = new List<string>();
        if (BkDcim.IsChecked == true) folders.Add("DCIM");
        if (BkPictures.IsChecked == true) folders.Add("Pictures");
        if (BkDownload.IsChecked == true) folders.Add("Download");
        if (BkDocuments.IsChecked == true) folders.Add("Documents");
        if (BkMusic.IsChecked == true) folders.Add("Music");
        if (BkMovies.IsChecked == true) folders.Add("Movies");

        foreach (var f in folders)
        {
            AppState.SetStatus("Backing up " + f + "...");
            var r = await Adb.RunAsync(new[] { "-s", serial, "pull", "/sdcard/" + f, media }, 7200000);
            Log(f + ": " + (r.Ok ? "done" : r.All.Trim().Split('\n')[0]));
        }

        if (BkApks.IsChecked == true)
        {
            var list = await Adb.ShellAsync(serial, "pm list packages -3", 30000);
            var pkgs = Util.NonEmptyLines(list.Output).Where(l => l.StartsWith("package:")).Select(l => l.Substring(8).Trim()).ToList();
            int n = 0;
            foreach (var pkg in pkgs)
            {
                AppState.SetStatus("Backing up APK " + (++n) + "/" + pkgs.Count + ": " + pkg);
                var dir = Path.Combine(root, "apks", Util.SafeName(pkg));
                Directory.CreateDirectory(dir);
                var pm = await Adb.ShellAsync(serial, "pm path " + pkg, 20000);
                foreach (var line in Util.NonEmptyLines(pm.Output).Where(l => l.StartsWith("package:")))
                    await Adb.RunAsync(new[] { "-s", serial, "pull", line.Substring(8).Trim(), dir }, 600000);
            }
            Log("APKs: " + pkgs.Count + " app(s) saved");
        }

        Log("Backup finished.");
        AppState.SetStatus("Backup finished: " + root);
        Util.OpenFolder(root);
    }

    async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        var serial = AppState.Serial!;
        var dlg = new OpenFolderDialog { Title = "Choose a Droideck backup folder" };
        if (dlg.ShowDialog() != true) return;
        var root = dlg.FolderName;
        var media = Path.Combine(root, "media");
        var apks = Path.Combine(root, "apks");
        if (!Directory.Exists(media) && !Directory.Exists(apks))
        {
            Log("That folder has no 'media' or 'apks' folder, so it is not a Droideck backup.");
            return;
        }
        if (!Dialogs.Confirm(Window.GetWindow(this), "Restore this backup to the phone? Existing files with the same name are overwritten.")) return;

        Log("Restore started from " + root);
        if (Directory.Exists(media))
        {
            foreach (var d in Directory.GetDirectories(media))
            {
                AppState.SetStatus("Restoring " + Path.GetFileName(d) + "...");
                var r = await Adb.RunAsync(new[] { "-s", serial, "push", d, "/sdcard/" }, 7200000);
                Log(Path.GetFileName(d) + ": " + (r.Ok ? "done" : r.All.Trim().Split('\n')[0]));
            }
        }
        if (Directory.Exists(apks))
        {
            foreach (var d in Directory.GetDirectories(apks))
            {
                var files = Directory.GetFiles(d, "*.apk");
                if (files.Length == 0) continue;
                AppState.SetStatus("Installing " + Path.GetFileName(d) + "...");
                var args = new List<string> { "-s", serial, "install-multiple", "-r" };
                args.AddRange(files);
                var r = await Adb.RunAsync(args, 600000);
                Log(Path.GetFileName(d) + ": " + (r.Output.Contains("Success") ? "installed" : r.All.Trim().Split('\n')[0]));
            }
        }
        Log("Restore finished.");
        AppState.SetStatus("Restore finished.");
    }
}
