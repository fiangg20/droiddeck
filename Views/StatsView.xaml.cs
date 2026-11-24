using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Droideck.Core;

namespace Droideck.Views;

public partial class StatsView : UserControl
{
    const string Cmd = "head -n 1 /proc/stat; echo ===; head -n 3 /proc/meminfo; echo ===; dumpsys battery; echo ===; cat /proc/net/dev";

    CancellationTokenSource? cts;

    public StatsView()
    {
        InitializeComponent();
        CpuGraph.Stroke = Theme.Accent;
        MemGraph.Stroke = Theme.Violet;
        NetGraph.Stroke = Theme.Ok;
        NetGraph.MaxValue = 0;
        BatGraph.Stroke = Theme.Warn;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) StartLoop();
            else StopLoop();
        };
        AppState.DeviceChanged += () =>
        {
            ResetGraphs();
            if (IsVisible) StartLoop();
        };
    }

    void ResetGraphs()
    {
        CpuGraph.Clear();
        MemGraph.Clear();
        NetGraph.Clear();
        BatGraph.Clear();
        CpuText.Text = MemText.Text = NetText.Text = BatText.Text = "–";
        CpuSub.Text = MemSub.Text = NetSub.Text = BatSub.Text = "";
    }

    void StartLoop()
    {
        StopLoop();
        if (AppState.Serial == null) return;
        cts = new CancellationTokenSource();
        _ = LoopAsync(AppState.Serial, cts.Token);
    }

    void StopLoop()
    {
        cts?.Cancel();
        cts = null;
    }

    static long Num(string s) => long.TryParse(s, out var v) ? v : 0;

    static string BatteryStatus(int code) => code switch
    {
        2 => "charging",
        3 => "discharging",
        4 => "not charging",
        5 => "full",
        _ => "unknown"
    };

    async Task LoopAsync(string serial, CancellationToken ct)
    {
        long prevTotal = 0, prevIdle = 0, prevRx = 0, prevTx = 0;
        DateTime prevTime = DateTime.MinValue;
        bool havePrev = false;

        while (!ct.IsCancellationRequested)
        {
            var r = await Adb.ShellAsync(serial, Cmd, 12000);
            if (ct.IsCancellationRequested) break;
            if (r.Ok || r.Output.Length > 20)
            {
                var now = DateTime.UtcNow;
                var sec = r.Output.Replace("\r", "").Split("===");
                if (sec.Length >= 4)
                {
                    var cpu = sec[0].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (cpu.Length > 5)
                    {
                        long total = 0;
                        for (int i = 1; i < Math.Min(cpu.Length, 9); i++) total += Num(cpu[i]);
                        long idle = Num(cpu[4]) + Num(cpu[5]);
                        if (havePrev && total > prevTotal)
                        {
                            double usage = 100.0 * (1.0 - (double)(idle - prevIdle) / (total - prevTotal));
                            usage = Math.Clamp(usage, 0, 100);
                            CpuText.Text = usage.ToString("0") + "%";
                            CpuGraph.Add(usage);
                        }
                        prevTotal = total;
                        prevIdle = idle;
                    }

                    long memTotal = 0, memAvail = 0;
                    foreach (var line in Util.NonEmptyLines(sec[1]))
                    {
                        var m = Regex.Match(line, @"^(\w+):\s+(\d+)");
                        if (!m.Success) continue;
                        if (m.Groups[1].Value == "MemTotal") memTotal = Num(m.Groups[2].Value);
                        else if (m.Groups[1].Value == "MemAvailable") memAvail = Num(m.Groups[2].Value);
                    }
                    if (memTotal > 0)
                    {
                        long used = memTotal - memAvail;
                        double pct = 100.0 * used / memTotal;
                        MemText.Text = pct.ToString("0") + "%";
                        MemSub.Text = Util.Size(used * 1024) + " of " + Util.Size(memTotal * 1024);
                        MemGraph.Add(pct);
                    }

                    var bat = sec[2];
                    var lvl = Regex.Match(bat, @"level:\s*(\d+)");
                    if (lvl.Success)
                    {
                        double level = Num(lvl.Groups[1].Value);
                        BatText.Text = level.ToString("0") + "%";
                        BatGraph.Add(level);
                        var temp = Regex.Match(bat, @"temperature:\s*(\d+)");
                        var volt = Regex.Match(bat, @"voltage:\s*(\d+)");
                        var stat = Regex.Match(bat, @"status:\s*(\d+)");
                        var parts = new List<string>();
                        if (stat.Success) parts.Add(BatteryStatus((int)Num(stat.Groups[1].Value)));
                        if (temp.Success) parts.Add((Num(temp.Groups[1].Value) / 10.0).ToString("0.0") + " °C");
                        if (volt.Success) parts.Add((Num(volt.Groups[1].Value) / 1000.0).ToString("0.00") + " V");
                        BatSub.Text = string.Join("  ·  ", parts);
                    }

                    long rx = 0, tx = 0;
                    foreach (var line in Util.NonEmptyLines(sec[3]))
                    {
                        var colon = line.IndexOf(':');
                        if (colon <= 0) continue;
                        var iface = line.Substring(0, colon).Trim();
                        if (iface == "lo") continue;
                        var f = line.Substring(colon + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (f.Length < 9) continue;
                        rx += Num(f[0]);
                        tx += Num(f[8]);
                    }
                    if (havePrev && prevTime != DateTime.MinValue)
                    {
                        double dt = Math.Max(0.2, (now - prevTime).TotalSeconds);
                        double down = Math.Max(0, (rx - prevRx) / dt);
                        double up = Math.Max(0, (tx - prevTx) / dt);
                        NetText.Text = Util.Size((long)down) + "/s";
                        NetSub.Text = "Upload " + Util.Size((long)up) + "/s";
                        NetGraph.Add(down);
                    }
                    prevRx = rx;
                    prevTx = tx;
                    prevTime = now;
                    havePrev = true;
                }
            }
            try
            {
                await Task.Delay(1200, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
