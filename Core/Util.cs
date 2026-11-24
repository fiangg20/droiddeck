using System.Text;

namespace Droideck.Core;

public static class Util
{
    public static string AppData
    {
        get
        {
            var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Droideck");
            Directory.CreateDirectory(p);
            return p;
        }
    }

    public static string CapturesDir
    {
        get
        {
            var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Droideck");
            Directory.CreateDirectory(p);
            return p;
        }
    }

    public static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    public static string[] Lines(string s) => (s ?? "").Replace("\r", "").Split('\n');

    public static IEnumerable<string> NonEmptyLines(string s) => Lines(s).Where(l => !string.IsNullOrWhiteSpace(l));

    public static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < units.Length - 1)
        {
            v /= 1024;
            i++;
        }
        return i == 0 ? bytes + " B" : v.ToString("0.#") + " " + units[i];
    }

    public static string[] SplitArgs(string s)
    {
        var res = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;
        char quoteChar = '\0';
        bool any = false;
        foreach (var c in s)
        {
            if (inQuote)
            {
                if (c == quoteChar) inQuote = false;
                else sb.Append(c);
            }
            else if (c == '"' || c == '\'')
            {
                inQuote = true;
                quoteChar = c;
                any = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0 || any)
                {
                    res.Add(sb.ToString());
                    sb.Clear();
                    any = false;
                }
            }
            else sb.Append(c);
        }
        if (sb.Length > 0 || any) res.Add(sb.ToString());
        return res.ToArray();
    }

    public static string SafeName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    public static void OpenFolder(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
        catch
        {
        }
    }

    public static void Reveal(string file)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
