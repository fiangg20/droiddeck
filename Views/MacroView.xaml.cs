using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Droideck.Core;
using Microsoft.Win32;

namespace Droideck.Views;

public partial class MacroView : UserControl
{
    CancellationTokenSource? playCts;

    public MacroView()
    {
        InitializeComponent();
        ActionList.ItemsSource = MacroRecorder.Actions;
        MacroRecorder.Actions.CollectionChanged += (_, _) => UpdateInfo();
        UpdateInfo();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is not true) return;
            if (MacroRecorder.IsRecording)
            {
                RecBtn.Content = "Stop recording";
                RecBtn.Style = Theme.Sty("DangerButton");
            }
            else
            {
                RecBtn.Content = "Record";
                RecBtn.ClearValue(StyleProperty);
            }
            UpdateInfo();
        };
    }

    void UpdateInfo()
    {
        InfoText.Text = MacroRecorder.Actions.Count + " step(s)" + (MacroRecorder.IsRecording ? "  ·  recording..." : "");
    }

    void Rec_Click(object sender, RoutedEventArgs e)
    {
        if (!MacroRecorder.IsRecording)
        {
            MacroRecorder.Begin();
            RecBtn.Content = "Stop recording";
            RecBtn.Style = Theme.Sty("DangerButton");
            AppState.SetStatus("Recording. Use the Mirror tab to tap, swipe and type on the phone.");
        }
        else
        {
            MacroRecorder.End();
            RecBtn.Content = "Record";
            RecBtn.ClearValue(StyleProperty);
        }
        UpdateInfo();
    }

    static void Run(string serial, MacroAction a)
    {
        switch (a.Kind)
        {
            case "tap":
                Remote.Tap(serial, a.X1, a.Y1);
                break;
            case "swipe":
                Remote.Swipe(serial, a.X1, a.Y1, a.X2, a.Y2, Math.Max(50, a.Duration));
                break;
            case "key":
                Remote.PressKey(serial, a.KeyCode);
                break;
            case "text":
                Remote.TypeText(serial, a.Text);
                break;
        }
    }

    async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (MacroRecorder.IsRecording) Rec_Click(sender, e);
        if (MacroRecorder.Actions.Count == 0)
        {
            AppState.SetStatus("Nothing to play yet. Record some steps first.");
            return;
        }
        List<string> targets = AllDevices.IsChecked == true
            ? AppState.Devices.Where(d => d.Ready).Select(d => d.Serial).ToList()
            : (AppState.Serial != null ? new List<string> { AppState.Serial } : new List<string>());
        if (targets.Count == 0)
        {
            AppState.Need();
            return;
        }

        int loops = int.TryParse(LoopsBox.Text.Trim(), out var n) ? Math.Max(0, n) : 1;
        double speed = SpeedBox.SelectedIndex switch
        {
            0 => 0.5,
            2 => 2.0,
            3 => 4.0,
            _ => 1.0
        };

        playCts = new CancellationTokenSource();
        var ct = playCts.Token;
        PlayBtn.IsEnabled = false;
        StopBtn.IsEnabled = true;
        var actions = MacroRecorder.Actions.ToList();
        try
        {
            for (int i = 0; loops == 0 || i < loops; i++)
            {
                AppState.SetStatus("Playing macro on " + targets.Count + " device(s)... loop " + (i + 1) + (loops == 0 ? "" : " of " + loops));
                foreach (var a in actions)
                {
                    int delay = (int)(a.DelayMs / speed);
                    if (delay > 0) await Task.Delay(delay, ct);
                    foreach (var t in targets) Run(t, a);
                }
                await Task.Delay(80, ct);
            }
            AppState.SetStatus("Macro finished.");
        }
        catch (OperationCanceledException)
        {
            AppState.SetStatus("Macro stopped.");
        }
        finally
        {
            PlayBtn.IsEnabled = true;
            StopBtn.IsEnabled = false;
        }
    }

    void StopPlay_Click(object sender, RoutedEventArgs e) => playCts?.Cancel();

    void AddWait_Click(object sender, RoutedEventArgs e)
    {
        var s = Dialogs.Ask(Window.GetWindow(this), "Add wait", "Wait how many milliseconds?", "1000");
        if (s == null || !int.TryParse(s.Trim(), out var ms) || ms < 0) return;
        Insert(new MacroAction { Kind = "wait", DelayMs = ms });
    }

    void AddKey_Click(object sender, RoutedEventArgs e)
    {
        var s = Dialogs.Ask(Window.GetWindow(this), "Add key", "Key name or Android key code\n(home, back, recents, power, enter, menu, wake, or a number like 66)", "home");
        if (s == null) return;
        int code = s.Trim().ToLowerInvariant() switch
        {
            "home" => 3,
            "back" => 4,
            "recents" or "recent" => 187,
            "power" => 26,
            "enter" => 66,
            "menu" => 82,
            "wake" => 224,
            "volup" or "volume up" => 24,
            "voldown" or "volume down" => 25,
            var other => int.TryParse(other, out var c) ? c : 0
        };
        if (code == 0) return;
        Insert(new MacroAction { Kind = "key", KeyCode = code, DelayMs = 300 });
    }

    void AddText_Click(object sender, RoutedEventArgs e)
    {
        var s = Dialogs.Ask(Window.GetWindow(this), "Add text", "Text to type on the phone");
        if (string.IsNullOrEmpty(s)) return;
        Insert(new MacroAction { Kind = "text", Text = s, DelayMs = 300 });
    }

    void Insert(MacroAction a)
    {
        int idx = ActionList.SelectedIndex;
        if (idx >= 0 && idx < MacroRecorder.Actions.Count) MacroRecorder.Actions.Insert(idx + 1, a);
        else MacroRecorder.Actions.Add(a);
    }

    void EditDelay_Click(object sender, RoutedEventArgs e)
    {
        if (ActionList.SelectedItem is not MacroAction a) return;
        var s = Dialogs.Ask(Window.GetWindow(this), "Edit delay", "Delay before this step (ms)", a.DelayMs.ToString());
        if (s == null || !int.TryParse(s.Trim(), out var ms) || ms < 0) return;
        a.DelayMs = ms;
        ActionList.Items.Refresh();
    }

    void Up_Click(object sender, RoutedEventArgs e)
    {
        int i = ActionList.SelectedIndex;
        if (i <= 0) return;
        MacroRecorder.Actions.Move(i, i - 1);
        ActionList.SelectedIndex = i - 1;
    }

    void Down_Click(object sender, RoutedEventArgs e)
    {
        int i = ActionList.SelectedIndex;
        if (i < 0 || i >= MacroRecorder.Actions.Count - 1) return;
        MacroRecorder.Actions.Move(i, i + 1);
        ActionList.SelectedIndex = i + 1;
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        int i = ActionList.SelectedIndex;
        if (i < 0) return;
        MacroRecorder.Actions.RemoveAt(i);
        if (MacroRecorder.Actions.Count > 0) ActionList.SelectedIndex = Math.Min(i, MacroRecorder.Actions.Count - 1);
    }

    void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (MacroRecorder.Actions.Count == 0) return;
        if (Dialogs.Confirm(Window.GetWindow(this), "Remove all " + MacroRecorder.Actions.Count + " steps?")) MacroRecorder.Actions.Clear();
    }

    static string MacroDir
    {
        get
        {
            var d = Path.Combine(Util.AppData, "macros");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "Droideck macro (*.json)|*.json", InitialDirectory = MacroDir, FileName = "macro.json" };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(MacroRecorder.Actions.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        AppState.SetStatus("Saved macro to " + dlg.FileName);
    }

    void Load_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Droideck macro (*.json)|*.json", InitialDirectory = MacroDir };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<MacroAction>>(File.ReadAllText(dlg.FileName)) ?? new List<MacroAction>();
            MacroRecorder.Actions.Clear();
            foreach (var a in list) MacroRecorder.Actions.Add(a);
            AppState.SetStatus("Loaded " + list.Count + " step(s).");
        }
        catch (Exception ex)
        {
            AppState.SetStatus("Could not load macro: " + ex.Message);
        }
    }
}
