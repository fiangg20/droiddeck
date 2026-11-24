using System.Windows;
using System.Windows.Controls;

namespace Droideck.Core;

public static class Dialogs
{
    static Window Make(Window? owner, string title, double width)
    {
        var win = new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner,
            Background = Theme.Res("PanelBrush"),
            Foreground = Theme.Fg,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13,
            ShowInTaskbar = false
        };
        win.SourceInitialized += (_, _) => Native.DarkTitleBar(win);
        return win;
    }

    public static string? Ask(Window? owner, string title, string label, string initial = "")
    {
        var w = Make(owner, title, 440);
        w.ResizeMode = ResizeMode.NoResize;
        var tb = new TextBox { Text = initial, Margin = new Thickness(0, 10, 0, 16) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 84, Style = Theme.Sty("AccentButton") };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        string? result = null;
        ok.Click += (_, _) =>
        {
            result = tb.Text;
            w.DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(tb);
        sp.Children.Add(buttons);
        w.Content = sp;
        w.Loaded += (_, _) =>
        {
            tb.Focus();
            tb.SelectAll();
        };
        w.ShowDialog();
        return result;
    }

    public static bool Confirm(Window? owner, string text)
    {
        return MessageBox.Show(owner ?? Application.Current.MainWindow, text, "Droideck", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public static void Permissions(Window? owner, string serial, string package)
    {
        var w = Make(owner, "Permissions · " + package, 560);
        w.Height = 560;
        w.SizeToContent = SizeToContent.Manual;
        var list = new StackPanel();
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var note = new TextBlock { Text = "Loading...", Margin = new Thickness(18, 14, 18, 8), Foreground = Theme.Muted };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(note);
        Grid.SetRow(scroll, 1);
        scroll.Margin = new Thickness(18, 0, 18, 18);
        grid.Children.Add(scroll);
        w.Content = grid;
        w.Loaded += async (_, _) =>
        {
            var r = await Adb.ShellAsync(serial, "dumpsys package " + package, 30000);
            var perms = new SortedDictionary<string, bool>();
            bool inRuntime = false;
            int indent = 0;
            foreach (var raw in Util.Lines(r.Output))
            {
                var trimmed = raw.Trim();
                int ind = raw.Length - raw.TrimStart().Length;
                if (trimmed == "runtime permissions:")
                {
                    inRuntime = true;
                    indent = ind;
                    continue;
                }
                if (!inRuntime) continue;
                if (trimmed.Length == 0 || ind <= indent)
                {
                    inRuntime = false;
                    continue;
                }
                var colon = trimmed.IndexOf(':');
                if (colon <= 0) continue;
                var name = trimmed.Substring(0, colon);
                perms[name] = trimmed.Contains("granted=true");
            }
            if (perms.Count == 0)
            {
                note.Text = "This app has no runtime permissions to manage.";
                return;
            }
            note.Text = "Tick to grant, untick to revoke. Changes apply immediately.";
            foreach (var kv in perms)
            {
                var cb = new CheckBox { Content = kv.Key, IsChecked = kv.Value, Margin = new Thickness(0, 5, 0, 5) };
                var perm = kv.Key;
                cb.Click += async (_, _) =>
                {
                    bool grant = cb.IsChecked == true;
                    var res = await Adb.ShellAsync(serial, (grant ? "pm grant " : "pm revoke ") + package + " " + perm);
                    if (!res.Ok || res.Output.Contains("Exception") || res.Error.Contains("Exception"))
                    {
                        cb.IsChecked = !grant;
                        AppState.SetStatus("Could not change " + perm + ": " + res.All.Trim());
                    }
                };
                list.Children.Add(cb);
            }
        };
        w.Show();
    }

    static readonly (string Pkg, string Label)[] Presets =
    {
        ("com.facebook.appmanager", "Facebook App Manager"),
        ("com.facebook.services", "Facebook Services"),
        ("com.facebook.system", "Facebook System"),
        ("com.facebook.katana", "Facebook"),
        ("com.facebook.orca", "Messenger"),
        ("com.netflix.mediaclient", "Netflix"),
        ("com.netflix.partner.activation", "Netflix activation"),
        ("com.amazon.mShop.android.shopping", "Amazon Shopping"),
        ("com.spotify.music", "Spotify (preinstalled)"),
        ("com.linkedin.android", "LinkedIn"),
        ("com.microsoft.office.officehubrow", "Microsoft Office Hub"),
        ("com.google.android.apps.tachyon", "Google Meet / Duo"),
        ("com.google.android.videos", "Google TV / Play Movies"),
        ("com.google.android.music", "Play Music"),
        ("com.google.android.apps.magazines", "Google News"),
        ("com.google.android.apps.youtube.music", "YouTube Music"),
        ("com.miui.analytics", "Xiaomi Analytics"),
        ("com.miui.msa.global", "Xiaomi MSA (ads)"),
        ("com.xiaomi.mipicks", "Xiaomi GetApps"),
        ("com.miui.bugreport", "Xiaomi Bug Report"),
        ("com.miui.hybrid", "Xiaomi Quick Apps"),
        ("com.miui.videoplayer", "Mi Video"),
        ("com.miui.player", "Mi Music"),
        ("com.samsung.android.bixby.agent", "Bixby"),
        ("com.samsung.android.bixby.wakeup", "Bixby Voice Wake-up"),
        ("com.samsung.android.game.gamehome", "Samsung Game Launcher"),
        ("com.samsung.android.app.spage", "Samsung Free"),
        ("com.samsung.android.arzone", "AR Zone")
    };

    public static void Debloat(Window? owner, string serial, HashSet<string> installed)
    {
        var w = Make(owner, "Debloater", 560);
        w.Height = 640;
        w.SizeToContent = SizeToContent.Manual;
        var list = new StackPanel();
        var boxes = new List<(CheckBox Box, string Pkg)>();
        foreach (var (pkg, label) in Presets)
        {
            if (!installed.Contains(pkg)) continue;
            var cb = new CheckBox { Content = label + "   (" + pkg + ")", Margin = new Thickness(0, 5, 0, 5) };
            boxes.Add((cb, pkg));
            list.Children.Add(cb);
        }
        var head = new TextBlock
        {
            Margin = new Thickness(18, 16, 18, 8),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Theme.Muted,
            Text = boxes.Count == 0
                ? "None of the known bloatware packages are installed on this phone."
                : "Common preinstalled apps found on this phone. Removal only affects the current user and can be undone with Restore."
        };
        var remove = new Button { Content = "Remove selected", Style = Theme.Sty("DangerButton") };
        var restore = new Button { Content = "Restore selected" };
        var output = new TextBlock { Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        remove.Click += async (_, _) =>
        {
            int ok = 0;
            foreach (var (box, pkg) in boxes)
            {
                if (box.IsChecked != true) continue;
                var r = await Adb.ShellAsync(serial, "pm uninstall -k --user 0 " + pkg);
                if (r.Output.Contains("Success")) ok++;
            }
            output.Text = "Removed " + ok + " app(s).";
        };
        restore.Click += async (_, _) =>
        {
            int ok = 0;
            foreach (var (box, pkg) in boxes)
            {
                if (box.IsChecked != true) continue;
                var r = await Adb.ShellAsync(serial, "cmd package install-existing " + pkg);
                if (r.Output.Contains("installed")) ok++;
            }
            output.Text = "Restored " + ok + " app(s).";
        };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 8, 18, 16) };
        bar.Children.Add(remove);
        bar.Children.Add(restore);
        bar.Children.Add(output);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(head);
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(18, 0, 18, 0) };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);
        Grid.SetRow(bar, 2);
        grid.Children.Add(bar);
        w.Content = grid;
        w.Show();
    }
}
