using System.IO.Compression;

namespace Droideck.Core;

public static class AdbSetup
{
    const string ToolsUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";

    public static async Task DownloadAsync(Action<string, double> report)
    {
        var zip = Path.Combine(Path.GetTempPath(), "droideck-platform-tools.zip");
        report("Downloading Android platform-tools from Google...", -1);
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
        using (var resp = await http.GetAsync(ToolsUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(zip);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, 0, buf.Length)) > 0)
            {
                await dst.WriteAsync(buf, 0, n);
                done += n;
                if (total > 0)
                    report("Downloading platform-tools... " + Util.Size(done) + " / " + Util.Size(total), done * 100.0 / total);
            }
        }

        report("Unpacking platform-tools...", -1);
        var root = Util.AppData;
        await Task.Run(() =>
        {
            if (Directory.Exists(Adb.DefaultDir)) Directory.Delete(Adb.DefaultDir, true);
            ZipFile.ExtractToDirectory(zip, root, true);
            File.Delete(zip);
        });
    }
}
