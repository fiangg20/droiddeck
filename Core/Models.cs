using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Droideck.Core;

public class DeviceInfo
{
    public string Serial { get; set; } = "";
    public string State { get; set; } = "";
    public string Model { get; set; } = "";
    public string Product { get; set; } = "";

    public bool IsWireless => Serial.Contains(':') || Serial.Contains("_adb-tls");
    public bool Ready => State == "device";
    public string Title => string.IsNullOrWhiteSpace(Model) ? "Android device" : Model;
    public string Display => Title + "  ·  " + Serial;
    public string Badge => !Ready ? State.ToUpperInvariant() : IsWireless ? "WI-FI" : "USB";
    public Brush BadgeBrush => Ready ? Theme.Ok : State == "unauthorized" ? Theme.Warn : Theme.Danger;

    public override string ToString() => Display;
}

public class FileEntry
{
    public string Name { get; set; } = "";
    public bool IsDir { get; set; }
    public long Size { get; set; }
    public string Date { get; set; } = "";

    public string Glyph => IsDir ? "\uE838" : "\uE8A5";
    public Brush GlyphBrush => IsDir ? Theme.Folder : Theme.Muted;
    public string SizeText => IsDir ? "" : Util.Size(Size);

    static readonly Regex Rx = new(
        @"^([\-dlbcps])([rwxsStT\-]{9})[.+]?\s+(?:\d+\s+)?\S+\s+\S+\s+(?:(\d+)\s+)?(\d{4}-\d\d-\d\d)\s+(\d\d:\d\d)\s+(.+)$",
        RegexOptions.Compiled);

    public static FileEntry? Parse(string line)
    {
        var m = Rx.Match(line.TrimEnd());
        if (!m.Success) return null;
        var type = m.Groups[1].Value;
        var name = m.Groups[6].Value;
        bool link = type == "l";
        if (link)
        {
            var i = name.IndexOf(" -> ", StringComparison.Ordinal);
            if (i > 0) name = name.Substring(0, i);
        }
        if (name == "." || name == "..") return null;
        long.TryParse(m.Groups[3].Value, out var size);
        return new FileEntry
        {
            Name = name,
            IsDir = type == "d" || link,
            Size = size,
            Date = m.Groups[4].Value + " " + m.Groups[5].Value
        };
    }
}

public class AppEntry
{
    public string Package { get; set; } = "";
    public string Apk { get; set; } = "";
    public bool IsSystem { get; set; }
    public bool IsDisabled { get; set; }

    public string Tag => (IsSystem ? "SYSTEM" : "USER") + (IsDisabled ? " · DISABLED" : "");
    public Brush TagBrush => IsDisabled ? Theme.Danger : IsSystem ? Theme.Warn : Theme.Ok;
}

public class LogLine
{
    public string Raw { get; set; } = "";
    public string Display { get; set; } = "";
    public string Pid { get; set; } = "";
    public string Level { get; set; } = "I";
    public Brush Brush { get; set; } = Theme.Fg;

    static readonly Regex Rx = new(
        @"^(\d\d-\d\d)\s+(\d\d:\d\d:\d\d\.\d+)\s+(\d+)\s+(\d+)\s+([VDIWEF])\s+(.*?)\s*:\s(.*)$",
        RegexOptions.Compiled);

    public static int Rank(string level) => level switch
    {
        "V" => 0,
        "D" => 1,
        "I" => 2,
        "W" => 3,
        "E" => 4,
        "F" => 5,
        _ => 2
    };

    static Brush ForLevel(string level) => level switch
    {
        "V" => Theme.LogVerbose,
        "D" => Theme.LogDebug,
        "I" => Theme.LogInfo,
        "W" => Theme.LogWarn,
        "E" => Theme.LogError,
        "F" => Theme.LogError,
        _ => Theme.Fg
    };

    public static LogLine Parse(string raw)
    {
        var m = Rx.Match(raw);
        if (!m.Success)
            return new LogLine { Raw = raw, Display = raw, Level = "I", Brush = Theme.Muted };
        var level = m.Groups[5].Value;
        var tag = m.Groups[6].Value;
        if (tag.Length > 22) tag = tag.Substring(0, 22);
        return new LogLine
        {
            Raw = raw,
            Pid = m.Groups[3].Value,
            Level = level,
            Brush = ForLevel(level),
            Display = m.Groups[2].Value + "  " + m.Groups[3].Value.PadLeft(5) + "  " + level + "  " + tag.PadRight(22) + "  " + m.Groups[7].Value
        };
    }
}

public class MacroAction
{
    public string Kind { get; set; } = "tap";
    public int X1 { get; set; }
    public int Y1 { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }
    public int Duration { get; set; }
    public int KeyCode { get; set; }
    public string Text { get; set; } = "";
    public int DelayMs { get; set; }

    [JsonIgnore]
    public string Describe => Kind switch
    {
        "tap" => "Tap  (" + X1 + ", " + Y1 + ")",
        "swipe" => "Swipe  (" + X1 + ", " + Y1 + ") → (" + X2 + ", " + Y2 + ")  " + Duration + " ms",
        "key" => "Key  " + KeyName(KeyCode),
        "text" => "Type  \"" + Text + "\"",
        "wait" => "Wait",
        _ => Kind
    };

    [JsonIgnore]
    public string DelayText => "+" + DelayMs + " ms";

    public static string KeyName(int code) => code switch
    {
        3 => "Home",
        4 => "Back",
        187 => "Recents",
        26 => "Power",
        24 => "Volume up",
        25 => "Volume down",
        66 => "Enter",
        67 => "Backspace",
        112 => "Delete",
        61 => "Tab",
        19 => "Up",
        20 => "Down",
        21 => "Left",
        22 => "Right",
        82 => "Menu",
        224 => "Wake",
        _ => code.ToString()
    };
}

public static class MacroRecorder
{
    public static ObservableCollection<MacroAction> Actions { get; } = new();
    public static bool IsRecording { get; private set; }
    static DateTime last;

    public static void Begin()
    {
        IsRecording = true;
        last = DateTime.UtcNow;
    }

    public static void End() => IsRecording = false;

    public static void Record(MacroAction a)
    {
        if (!IsRecording) return;
        var now = DateTime.UtcNow;
        a.DelayMs = (int)Math.Clamp((now - last).TotalMilliseconds, 0, 10000);
        last = now;
        Actions.Add(a);
    }
}
