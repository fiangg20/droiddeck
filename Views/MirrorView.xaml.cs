using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Droideck.Core;

namespace Droideck.Views;

public partial class MirrorView : UserControl
{
    CancellationTokenSource? cts;
    Point downPt;
    DateTime downAt;
    bool dragging;
    int rotation;
    Process? recProc;
    string recSerial = "";
    const string RemoteRec = "/sdcard/droideck_rec.mp4";

    public MirrorView()
    {
        InitializeComponent();
        AppState.DeviceChanged += () =>
        {
            Stop();
            Screen.Source = null;
            Hint.Visibility = Visibility.Visible;
        };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false) Stop();
        };
    }

    void Start_Click(object sender, RoutedEventArgs e)
    {
        if (cts != null)
        {
            Stop();
            return;
        }
        var serial = AppState.Serial;
        if (serial == null)
        {
            AppState.Need();
            return;
        }
        cts = new CancellationTokenSource();
        Hint.Visibility = Visibility.Collapsed;
        StartBtn.Content = "Stop mirroring";
        _ = LoopAsync(serial, cts.Token);
        ScreenHost.Focus();
    }

    void Stop()
    {
        cts?.Cancel();
        cts = null;
        StartBtn.Content = "Start mirroring";
        FpsText.Text = "";
    }

    static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    async Task LoopAsync(string serial, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        int frames = 0;
        int failures = 0;
        var pending = Adb.ExecOutBytesAsync(serial, "screencap -p", 12000);
        while (!ct.IsCancellationRequested)
        {
            var bytes = await pending;
            if (ct.IsCancellationRequested) break;
            pending = Adb.ExecOutBytesAsync(serial, "screencap -p", 12000);

            if (bytes == null || bytes.Length < 100)
            {
                failures++;
                if (failures >= 6)
                {
                    AppState.SetStatus("Mirroring stopped: could not read the screen. Is the phone unlocked and connected?");
                    Stop();
                    return;
                }
                await Task.Delay(400);
                continue;
            }
            failures = 0;
            var bmp = await Task.Run(() => Decode(bytes));
            if (ct.IsCancellationRequested) break;
            if (bmp != null)
            {
                Screen.Source = bmp;
                frames++;
            }
            if (sw.ElapsedMilliseconds >= 1000)
            {
                FpsText.Text = frames + " fps";
                frames = 0;
                sw.Restart();
            }
        }
    }

    bool Map(Point p, bool clamp, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (Screen.Source is not BitmapSource s) return false;
        double aw = Screen.ActualWidth;
        double ah = Screen.ActualHeight;
        if (aw <= 0 || ah <= 0) return false;
        double scale = Math.Min(aw / s.PixelWidth, ah / s.PixelHeight);
        double rw = s.PixelWidth * scale;
        double rh = s.PixelHeight * scale;
        double ox = (aw - rw) / 2;
        double oy = (ah - rh) / 2;
        double px = (p.X - ox) / scale;
        double py = (p.Y - oy) / scale;
        if (!clamp && (px < 0 || py < 0 || px > s.PixelWidth || py > s.PixelHeight)) return false;
        x = (int)Math.Clamp(px, 0, s.PixelWidth - 1);
        y = (int)Math.Clamp(py, 0, s.PixelHeight - 1);
        return true;
    }

    void Host_MouseDown(object sender, MouseButtonEventArgs e)
    {
        ScreenHost.Focus();
        if (Screen.Source == null || AppState.Serial == null) return;
        downPt = e.GetPosition(Screen);
        downAt = DateTime.UtcNow;
        dragging = true;
        ScreenHost.CaptureMouse();
    }

    void Host_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        ScreenHost.ReleaseMouseCapture();
        var serial = AppState.Serial;
        if (serial == null) return;
        if (!Map(downPt, false, out var x1, out var y1)) return;
        Map(e.GetPosition(Screen), true, out var x2, out var y2);
        double dist = Math.Sqrt((x2 - x1) * (double)(x2 - x1) + (y2 - y1) * (double)(y2 - y1));
        int ms = (int)Math.Clamp((DateTime.UtcNow - downAt).TotalMilliseconds, 80, 2500);

        if (dist < 24 && ms < 450)
        {
            Remote.Tap(serial, x1, y1);
            MacroRecorder.Record(new MacroAction { Kind = "tap", X1 = x1, Y1 = y1 });
        }
        else if (dist < 24)
        {
            Remote.Swipe(serial, x1, y1, x1, y1, ms);
            MacroRecorder.Record(new MacroAction { Kind = "swipe", X1 = x1, Y1 = y1, X2 = x1, Y2 = y1, Duration = ms });
        }
        else
        {
            Remote.Swipe(serial, x1, y1, x2, y2, ms);
            MacroRecorder.Record(new MacroAction { Kind = "swipe", X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Duration = ms });
        }
    }

    void Host_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var serial = AppState.Serial;
        if (serial == null || Screen.Source is not BitmapSource s) return;
        if (!Map(e.GetPosition(Screen), true, out var x, out var y)) return;
        int d = (int)(s.PixelHeight * 0.25) * (e.Delta > 0 ? 1 : -1);
        int y1 = Math.Clamp(y - d, 0, s.PixelHeight - 1);
        int y2 = Math.Clamp(y + d, 0, s.PixelHeight - 1);
        Remote.Swipe(serial, x, y1, x, y2, 220);
        MacroRecorder.Record(new MacroAction { Kind = "swipe", X1 = x, Y1 = y1, X2 = x, Y2 = y2, Duration = 220 });
    }

    void Host_KeyDown(object sender, KeyEventArgs e)
    {
        var serial = AppState.Serial;
        if (serial == null) return;
        int code = e.Key switch
        {
            Key.Enter => 66,
            Key.Back => 67,
            Key.Delete => 112,
            Key.Escape => 4,
            Key.Tab => 61,
            Key.Up => 19,
            Key.Down => 20,
            Key.Left => 21,
            Key.Right => 22,
            Key.Home => 3,
            _ => 0
        };
        if (code == 0) return;
        Remote.PressKey(serial, code);
        MacroRecorder.Record(new MacroAction { Kind = "key", KeyCode = code });
        e.Handled = true;
    }

    void Host_TextInput(object sender, TextCompositionEventArgs e)
    {
        var serial = AppState.Serial;
        if (serial == null) return;
        var t = Remote.EscapeText(e.Text);
        if (t.Length == 0) return;
        Remote.TypeText(serial, e.Text);
        MacroRecorder.Record(new MacroAction { Kind = "text", Text = e.Text });
        e.Handled = true;
    }

    void Key_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        if (sender is not Button b || b.Tag is not string tag) return;
        int code = int.Parse(tag);
        Remote.PressKey(AppState.Serial!, code);
        MacroRecorder.Record(new MacroAction { Kind = "key", KeyCode = code });
    }

    async void Notif_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        await Adb.ShellAsync(AppState.Serial!, "cmd statusbar expand-notifications", 10000);
    }

    async void Rotate_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        rotation = (rotation + 1) % 4;
        await Adb.ShellAsync(AppState.Serial!, "settings put system accelerometer_rotation 0; settings put system user_rotation " + rotation, 10000);
    }

    void SendText_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        var t = TypeBox.Text;
        if (t.Length == 0) return;
        Remote.TypeText(AppState.Serial!, t);
        MacroRecorder.Record(new MacroAction { Kind = "text", Text = t });
        TypeBox.Clear();
    }

    void TypeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        SendText_Click(sender, e);
        e.Handled = true;
    }

    async void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        if (!AppState.Need()) return;
        var bytes = await Adb.ExecOutBytesAsync(AppState.Serial!, "screencap -p", 20000);
        if (bytes == null)
        {
            AppState.SetStatus("Screenshot failed.");
            return;
        }
        var file = Path.Combine(Util.CapturesDir, "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
        await File.WriteAllBytesAsync(file, bytes);
        AppState.SetStatus("Saved " + file);
    }

    async void Record_Click(object sender, RoutedEventArgs e)
    {
        if (recProc == null)
        {
            if (!AppState.Need()) return;
            recSerial = AppState.Serial!;
            var p = new Process { StartInfo = Adb.Make(Adb.AdbPath, new[] { "-s", recSerial, "shell", "screenrecord --time-limit 180 " + RemoteRec }), EnableRaisingEvents = true };
            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                AppState.SetStatus("Could not start recording: " + ex.Message);
                return;
            }
            recProc = p;
            RecBtn.Content = "Stop recording";
            RecBtn.Style = Theme.Sty("DangerButton");
            AppState.SetStatus("Recording... (max 3 minutes)");
            return;
        }

        var proc = recProc;
        recProc = null;
        RecBtn.Content = "Record";
        RecBtn.ClearValue(StyleProperty);
        AppState.SetStatus("Finishing recording...");
        await Adb.ShellAsync(recSerial, "kill -2 $(pidof screenrecord)", 10000);
        try
        {
            await proc.WaitForExitAsync(new CancellationTokenSource(8000).Token);
        }
        catch
        {
        }
        await Task.Delay(500);
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Droideck");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "recording_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
        var pull = await Adb.RunAsync(new[] { "-s", recSerial, "pull", RemoteRec, file }, 300000);
        await Adb.ShellAsync(recSerial, "rm -f " + RemoteRec, 10000);
        AppState.SetStatus(pull.Ok ? "Saved " + file : "Could not copy the recording: " + pull.All.Trim());
    }

    void OpenCaptures_Click(object sender, RoutedEventArgs e) => Util.OpenFolder(Util.CapturesDir);

    void MacroCheck_Click(object sender, RoutedEventArgs e)
    {
        if (MacroCheck.IsChecked == true)
        {
            MacroRecorder.Begin();
            AppState.SetStatus("Recording taps, swipes and typing into the Macros tab.");
        }
        else MacroRecorder.End();
    }
}
