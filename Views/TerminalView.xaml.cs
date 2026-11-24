using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Droideck.Core;

namespace Droideck.Views;

public partial class TerminalView : UserControl
{
    static readonly string[] Common =
    {
        "ls -la", "pwd", "cat ", "ps -A", "top -n 1", "df -h", "du -sh ", "free -m", "uptime", "date", "id", "whoami",
        "getprop ", "getprop ro.product.model", "setprop ", "settings get ", "settings put ", "dumpsys battery", "dumpsys activity",
        "dumpsys package ", "dumpsys meminfo ", "dumpsys wifi", "pm list packages", "pm list packages -3", "pm path ", "pm clear ",
        "am start -n ", "am start -a android.intent.action.VIEW -d ", "am force-stop ", "am broadcast -a ", "input tap ", "input swipe ",
        "input keyevent ", "input text ", "screencap -p /sdcard/s.png", "screenrecord /sdcard/r.mp4", "logcat -d", "logcat -c", "wm size",
        "wm density", "ip addr", "ifconfig", "netstat -tulpn", "ping -c 4 ", "svc wifi enable", "svc wifi disable", "svc data enable",
        "svc data disable", "cmd statusbar expand-notifications", "monkey -p ", "reboot", "pidof ", "kill ", "echo ", "grep ", "tail ",
        "head ", "rm ", "mkdir ", "mv ", "cp ", "touch ", "chmod ", "find ", "uname -a", "clear"
    };

    readonly List<string> history = new();
    int histIndex;
    string cwd = "/";
    CancellationTokenSource? runCts;
    string tabPrefix = "";
    int tabIndex = -1;

    public TerminalView()
    {
        InitializeComponent();
        UpdatePrompt();
        Write("Droideck terminal\nType a device shell command and press Enter.\n" +
              "Prefix a line with 'adb ' to run a raw adb command (adb devices, adb install app.apk).\n" +
              "Up/Down browse history, Tab completes, 'clear' wipes the screen.\n\n");
        AppState.DeviceChanged += () =>
        {
            cwd = "/";
            UpdatePrompt();
            if (AppState.Serial != null) Write("-- switched to " + AppState.Serial + " --\n");
        };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) In.Focus();
        };
    }

    void Write(string text)
    {
        Out.AppendText(text);
        Out.ScrollToEnd();
    }

    void UpdatePrompt()
    {
        Prompt.Text = (AppState.Serial ?? "no-device") + ":" + cwd + " $";
    }

    async void In_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var line = In.Text.Trim();
            In.Clear();
            tabIndex = -1;
            if (line.Length == 0) return;
            if (history.Count == 0 || history[history.Count - 1] != line) history.Add(line);
            histIndex = history.Count;
            await RunAsync(line);
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            if (history.Count == 0) return;
            histIndex = Math.Max(0, histIndex - 1);
            In.Text = history[histIndex];
            In.CaretIndex = In.Text.Length;
        }
        else if (e.Key == Key.Down)
        {
            e.Handled = true;
            if (history.Count == 0) return;
            histIndex = Math.Min(history.Count, histIndex + 1);
            In.Text = histIndex >= history.Count ? "" : history[histIndex];
            In.CaretIndex = In.Text.Length;
        }
        else if (e.Key == Key.Tab)
        {
            e.Handled = true;
            Complete();
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && In.SelectionLength == 0)
        {
            e.Handled = true;
            runCts?.Cancel();
        }
        else if (e.Key != Key.LeftShift && e.Key != Key.RightShift)
        {
            tabIndex = -1;
        }
    }

    void Complete()
    {
        if (tabIndex < 0) tabPrefix = In.Text;
        var matches = history.AsEnumerable().Reverse().Concat(Common)
            .Where(c => c.StartsWith(tabPrefix, StringComparison.OrdinalIgnoreCase) && c != tabPrefix)
            .Distinct().ToList();
        if (matches.Count == 0) return;
        tabIndex = (tabIndex + 1) % matches.Count;
        In.Text = matches[tabIndex];
        In.CaretIndex = In.Text.Length;
    }

    void Stop_Click(object sender, RoutedEventArgs e) => runCts?.Cancel();

    async Task RunAsync(string line)
    {
        if (line == "clear" || line == "cls")
        {
            Out.Clear();
            return;
        }
        Write(Prompt.Text + " " + line + "\n");

        runCts = new CancellationTokenSource();
        var ct = runCts.Token;
        StopBtn.IsEnabled = true;
        try
        {
            AdbResult r;
            if (line.StartsWith("adb ", StringComparison.OrdinalIgnoreCase))
            {
                var args = Util.SplitArgs(line.Substring(4));
                if (AppState.Serial != null && !args.Contains("-s") && args.Length > 0 && args[0] != "devices" && args[0] != "connect" && args[0] != "disconnect" && args[0] != "pair")
                    args = new[] { "-s", AppState.Serial }.Concat(args).ToArray();
                r = await Adb.RunAsync(args, 600000, ct);
            }
            else
            {
                var serial = AppState.Serial;
                if (serial == null)
                {
                    Write("No device selected.\n\n");
                    return;
                }
                if (line == "cd" || line.StartsWith("cd "))
                {
                    var target = line.Length > 3 ? line.Substring(3).Trim() : "/sdcard";
                    var cd = await Adb.ShellAsync(serial, "cd " + Util.Quote(cwd) + " && cd " + Util.Quote(target) + " && pwd", 15000, ct);
                    var res = cd.Output.Trim();
                    if (cd.Ok && res.StartsWith("/"))
                    {
                        cwd = res.Split('\n')[0].Trim();
                        UpdatePrompt();
                    }
                    else Write((cd.All.Trim().Length > 0 ? cd.All.Trim() : "cd: failed") + "\n");
                    Write("\n");
                    return;
                }
                r = await Adb.ShellAsync(serial, "cd " + Util.Quote(cwd) + " && " + line, 600000, ct);
            }

            var text = r.Output.Replace("\r", "");
            if (!string.IsNullOrEmpty(r.Error)) text += (text.EndsWith("\n") || text.Length == 0 ? "" : "\n") + r.Error.Replace("\r", "");
            if (text.Length > 0) Write(text.EndsWith("\n") ? text : text + "\n");
            Write("\n");
        }
        finally
        {
            StopBtn.IsEnabled = false;
        }
    }
}
