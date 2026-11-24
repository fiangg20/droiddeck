using System.Diagnostics;
using System.Text;

namespace Droideck.Core;

public record AdbResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;

    public string All => string.IsNullOrWhiteSpace(Error) ? Output : Output + Environment.NewLine + Error;
}

public static class Adb
{
    public static string DefaultDir => Path.Combine(Util.AppData, "platform-tools");

    public static string AdbPath
    {
        get
        {
            var o = Store.Settings.AdbPath;
            if (!string.IsNullOrWhiteSpace(o) && File.Exists(o)) return o;
            return Path.Combine(DefaultDir, "adb.exe");
        }
    }

    public static string FastbootPath => Path.Combine(Path.GetDirectoryName(AdbPath) ?? DefaultDir, "fastboot.exe");

    public static bool Installed => File.Exists(AdbPath);

    public static ProcessStartInfo Make(string exe, IEnumerable<string> args, bool redirectInput = false)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    public static Task<AdbResult> RunAsync(IEnumerable<string> args, int timeoutMs = 60000, CancellationToken ct = default)
        => RunExeAsync(AdbPath, args, timeoutMs, ct);

    public static async Task<AdbResult> RunExeAsync(string exe, IEnumerable<string> args, int timeoutMs = 60000, CancellationToken ct = default)
    {
        try
        {
            using var p = new Process { StartInfo = Make(exe, args) };
            p.Start();
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            try
            {
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    p.Kill(true);
                }
                catch
                {
                }
                return new AdbResult(-1, "", ct.IsCancellationRequested ? "Cancelled" : "Timed out");
            }
            return new AdbResult(p.ExitCode, await outTask, await errTask);
        }
        catch (Exception ex)
        {
            return new AdbResult(-1, "", ex.Message);
        }
    }

    public static Task<AdbResult> ShellAsync(string serial, string command, int timeoutMs = 60000, CancellationToken ct = default)
        => RunAsync(new[] { "-s", serial, "shell", command }, timeoutMs, ct);

    public static Task<AdbResult> ForDeviceAsync(string serial, IEnumerable<string> args, int timeoutMs = 60000, CancellationToken ct = default)
    {
        var list = new List<string> { "-s", serial };
        list.AddRange(args);
        return RunAsync(list, timeoutMs, ct);
    }

    public static async Task<byte[]?> ExecOutBytesAsync(string serial, string command, int timeoutMs = 15000)
    {
        try
        {
            using var p = new Process { StartInfo = Make(AdbPath, new[] { "-s", serial, "exec-out", command }) };
            p.Start();
            var ms = new MemoryStream();
            var copy = p.StandardOutput.BaseStream.CopyToAsync(ms);
            var err = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await copy.WaitAsync(cts.Token);
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    p.Kill(true);
                }
                catch
                {
                }
                return null;
            }
            return p.ExitCode == 0 ? ms.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }

    public static List<DeviceInfo> ParseDevices(string text)
    {
        var list = new List<DeviceInfo>();
        foreach (var line in Util.NonEmptyLines(text))
        {
            if (line.StartsWith("List of devices") || line.StartsWith("*")) continue;
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var d = new DeviceInfo { Serial = parts[0], State = parts[1] };
            foreach (var kv in parts.Skip(2))
            {
                var i = kv.IndexOf(':');
                if (i <= 0) continue;
                var k = kv.Substring(0, i);
                var v = kv.Substring(i + 1);
                if (k == "model") d.Model = v.Replace('_', ' ');
                else if (k == "product") d.Product = v;
            }
            list.Add(d);
        }
        return list;
    }
}
