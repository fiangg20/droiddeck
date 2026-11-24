using System.Diagnostics;
using Microsoft.Win32;

namespace Droideck.Core;

public static class Installer
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Droideck";

    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Droideck");

    public static string ExePath => Environment.ProcessPath ?? "";

    static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Droideck.lnk");

    static string StartLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Droideck.lnk");

    public static string? InstalledDir()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return k?.GetValue("InstallLocation") as string;
        }
        catch
        {
            return null;
        }
    }

    public static bool RunningInstalled()
    {
        try
        {
            var d = InstalledDir();
            if (string.IsNullOrEmpty(d)) return false;
            var here = Path.GetDirectoryName(ExePath) ?? "";
            return string.Equals(Path.GetFullPath(here).TrimEnd('\\'), Path.GetFullPath(d).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool ShouldOfferSetup(string[] args)
    {
        if (args.Contains("--portable")) return false;
        if (Store.Settings.SkipSetup) return false;
        return !RunningInstalled();
    }

    static void MakeShortcut(string link, string target, string dir)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Shortcuts are not available on this PC.");
        dynamic shell = Activator.CreateInstance(t)!;
        try
        {
            dynamic lnk = shell.CreateShortcut(link);
            lnk.TargetPath = target;
            lnk.WorkingDirectory = dir;
            lnk.IconLocation = target + ",0";
            lnk.Description = "Droideck - ADB command center";
            lnk.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject((object)shell);
        }
    }

    static void Register(string dir, string exe)
    {
        using var k = Registry.CurrentUser.CreateSubKey(UninstallKey);
        k.SetValue("DisplayName", "Droideck");
        k.SetValue("DisplayIcon", exe);
        k.SetValue("InstallLocation", dir);
        k.SetValue("Publisher", "Droideck");
        k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
    }

    public static async Task<string> InstallAsync(string dir, bool desktop, bool startMenu, bool adb, Action<string, double> report)
    {
        dir = Path.GetFullPath(dir.Trim());
        var target = Path.Combine(dir, "Droideck.exe");
        var source = ExePath;

        report("Copying files...", 5);
        Directory.CreateDirectory(dir);
        await Task.Run(() =>
        {
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(source, target, true);
        });

        report("Creating shortcuts...", 25);
        if (desktop) MakeShortcut(DesktopLink, target, dir);
        if (startMenu) MakeShortcut(StartLink, target, dir);

        report("Registering with Windows...", 40);
        Register(dir, target);

        if (adb && !Adb.Installed)
            await AdbSetup.DownloadAsync((text, pct) => report(text, pct < 0 ? 90 : 40 + pct * 0.55));

        report("Finished", 100);
        return target;
    }

    public static async Task UninstallAsync(bool removeData)
    {
        TryDelete(DesktopLink);
        TryDelete(StartLink);
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        }
        catch
        {
        }
        if (removeData)
        {
            await Adb.RunAsync(new[] { "kill-server" }, 8000);
            try
            {
                Directory.Delete(Util.AppData, true);
            }
            catch
            {
            }
        }
    }

    public static void ScheduleCleanup(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + dir + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch
        {
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }
}
