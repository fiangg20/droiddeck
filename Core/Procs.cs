using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace Droideck.Core;

public sealed class LineProcess : IDisposable
{
    Process? p;

    public event Action<string>? Line;
    public event Action? Exited;

    public bool Running
    {
        get
        {
            try
            {
                return p != null && !p.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Start(string exe, IEnumerable<string> args)
    {
        Stop();
        var proc = new Process { StartInfo = Adb.Make(exe, args), EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Line?.Invoke(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Line?.Invoke(e.Data);
        };
        proc.Exited += (_, _) => Exited?.Invoke();
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        p = proc;
    }

    public void Stop()
    {
        var proc = p;
        p = null;
        try
        {
            if (proc != null && !proc.HasExited) proc.Kill(true);
        }
        catch
        {
        }
    }

    public void Dispose() => Stop();
}

public sealed class InputShell
{
    readonly string serial;
    readonly Channel<string> channel = Channel.CreateUnbounded<string>();
    Process? p;

    public InputShell(string serial)
    {
        this.serial = serial;
        _ = Task.Run(PumpAsync);
    }

    public void Send(string command) => channel.Writer.TryWrite(command);

    async Task PumpAsync()
    {
        await foreach (var cmd in channel.Reader.ReadAllAsync())
        {
            try
            {
                if (p == null || p.HasExited) Open();
                if (p == null) continue;
                await p.StandardInput.WriteLineAsync(cmd);
                await p.StandardInput.FlushAsync();
            }
            catch
            {
                Close();
            }
        }
        Close();
    }

    void Open()
    {
        Close();
        var psi = Adb.Make(Adb.AdbPath, new[] { "-s", serial, "shell" }, true);
        var proc = Process.Start(psi);
        if (proc == null) return;
        proc.StandardInput.NewLine = "\n";
        proc.OutputDataReceived += (_, _) => { };
        proc.ErrorDataReceived += (_, _) => { };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        p = proc;
    }

    void Close()
    {
        var proc = p;
        p = null;
        try
        {
            if (proc != null && !proc.HasExited) proc.Kill(true);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        channel.Writer.TryComplete();
        Close();
    }
}

public static class Remote
{
    static readonly Dictionary<string, InputShell> shells = new();

    public static void Send(string serial, string command)
    {
        InputShell? s;
        lock (shells)
        {
            if (!shells.TryGetValue(serial, out s))
            {
                s = new InputShell(serial);
                shells[serial] = s;
            }
        }
        s!.Send(command);
    }

    public static void Tap(string serial, int x, int y) => Send(serial, "input tap " + x + " " + y);

    public static void Swipe(string serial, int x1, int y1, int x2, int y2, int ms)
        => Send(serial, "input swipe " + x1 + " " + y1 + " " + x2 + " " + y2 + " " + ms);

    public static void PressKey(string serial, int code) => Send(serial, "input keyevent " + code);

    public static void TypeText(string serial, string text)
    {
        var e = EscapeText(text);
        if (e.Length > 0) Send(serial, "input text '" + e + "'");
    }

    public static string EscapeText(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (c < ' ' || c > '~') continue;
            if (c == ' ') sb.Append("%s");
            else if (c == '\'') sb.Append("'\\''");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public static void CloseAll()
    {
        lock (shells)
        {
            foreach (var s in shells.Values) s.Dispose();
            shells.Clear();
        }
    }
}
