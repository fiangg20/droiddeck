using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Droideck.Core;
using Microsoft.Win32;

namespace Droideck.Views;

public partial class LogcatView : UserControl
{
    const int Cap = 20000;
    const int TrimTo = 16000;

    readonly LineProcess proc = new();
    readonly ConcurrentQueue<string> queue = new();
    readonly DispatcherTimer flush = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    List<LogLine> all = new();
    ObservableCollection<LogLine> shown = new();
    bool running;
    bool paused;
    bool ready;
    int minLevel;
    string filterText = "";
    Regex? filterRegex;
    string? pidFilter;

    public LogcatView()
    {
        InitializeComponent();
        ready = true;
        LogList.ItemsSource = shown;
        proc.Line += l => queue.Enqueue(l);
        flush.Tick += (_, _) => Flush();
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            ReadFilter();
            Rebuild();
        };
        AppState.DeviceChanged += () =>
        {
            StopLogcat();
            all.Clear();
            shown = new ObservableCollection<LogLine>();
            LogList.ItemsSource = shown;
            pidFilter = null;
            AppBox.Text = "";
            UpdateCount();
        };
    }

    void Start_Click(object sender, RoutedEventArgs e)
    {
        if (running)
        {
            StopLogcat();
            return;
        }
        var serial = AppState.Serial;
        if (serial == null)
        {
            AppState.Need();
            return;
        }
        proc.Start(Adb.AdbPath, new[] { "-s", serial, "logcat", "-v", "threadtime" });
        running = true;
        StartBtn.Content = "Stop";
        flush.Start();
    }

    void StopLogcat()
    {
        proc.Stop();
        running = false;
        flush.Stop();
        StartBtn.Content = "Start";
    }

    void Pause_Click(object sender, RoutedEventArgs e)
    {
        paused = !paused;
        PauseBtn.Content = paused ? "▶  Resume" : "Pause";
        if (!paused) Rebuild();
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        all.Clear();
        shown = new ObservableCollection<LogLine>();
        LogList.ItemsSource = shown;
        UpdateCount();
        var serial = AppState.Serial;
        if (serial != null) _ = Adb.RunAsync(new[] { "-s", serial, "logcat", "-c" }, 10000);
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Text file (*.txt)|*.txt|Log file (*.log)|*.log", FileName = "logcat_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt" };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllLines(dlg.FileName, all.Select(l => l.Raw));
        AppState.SetStatus("Saved " + all.Count + " lines to " + dlg.FileName);
    }

    void Level_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        minLevel = LevelBox.SelectedIndex;
        Rebuild();
    }

    void Regex_Click(object sender, RoutedEventArgs e)
    {
        ReadFilter();
        Rebuild();
    }

    void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (!ready) return;
        debounce.Stop();
        debounce.Start();
    }

    void ReadFilter()
    {
        filterText = FilterBox.Text.Trim();
        filterRegex = null;
        if (filterText.Length > 0 && RegexCheck.IsChecked == true)
        {
            try
            {
                filterRegex = new Regex(filterText, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch
            {
                filterRegex = null;
            }
        }
    }

    async void AppBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var pkg = AppBox.Text.Trim();
        if (pkg.Length == 0)
        {
            pidFilter = null;
            Rebuild();
            return;
        }
        var serial = AppState.Serial;
        if (serial == null) return;
        var r = await Adb.ShellAsync(serial, "pidof -s " + pkg, 10000);
        var pid = r.Output.Trim();
        if (pid.Length == 0 || !pid.All(char.IsDigit))
        {
            AppState.SetStatus(pkg + " is not running right now.");
            return;
        }
        pidFilter = pid;
        AppState.SetStatus("Showing only " + pkg + " (PID " + pid + ")");
        Rebuild();
    }

    bool Match(LogLine l)
    {
        if (LogLine.Rank(l.Level) < minLevel) return false;
        if (pidFilter != null && l.Pid != pidFilter) return false;
        if (filterRegex != null) return filterRegex.IsMatch(l.Raw);
        if (filterText.Length > 0 && filterRegex == null && RegexCheck.IsChecked != true)
            return l.Raw.Contains(filterText, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    void Rebuild()
    {
        shown = new ObservableCollection<LogLine>(all.Where(Match));
        LogList.ItemsSource = shown;
        UpdateCount();
        ScrollEnd();
    }

    void Flush()
    {
        int n = 0;
        bool trimmed = false;
        var added = new List<LogLine>();
        while (n < 3000 && queue.TryDequeue(out var line))
        {
            n++;
            var l = LogLine.Parse(line);
            all.Add(l);
            added.Add(l);
        }
        if (n == 0) return;

        if (all.Count > Cap)
        {
            all.RemoveRange(0, all.Count - TrimTo);
            trimmed = true;
        }
        if (trimmed)
        {
            if (!paused) Rebuild();
            return;
        }
        if (!paused)
        {
            foreach (var l in added)
                if (Match(l)) shown.Add(l);
            UpdateCount();
            ScrollEnd();
        }
    }

    void ScrollEnd()
    {
        if (AutoScroll.IsChecked == true && shown.Count > 0 && !paused)
            LogList.ScrollIntoView(shown[shown.Count - 1]);
    }

    void UpdateCount()
    {
        CountText.Text = shown.Count + " shown  ·  " + all.Count + " captured" + (paused ? "  ·  paused" : "");
    }

    void LogList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var text = string.Join("\n", LogList.SelectedItems.Cast<LogLine>().Select(l => l.Raw));
            try
            {
                if (text.Length > 0) Clipboard.SetText(text);
            }
            catch
            {
            }
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            LogList.SelectAll();
            e.Handled = true;
        }
    }
}
