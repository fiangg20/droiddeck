# Droideck

An ADB command center for Windows: devices (USB and Wi-Fi), live screen mirror with touch and keyboard control, file manager, app manager, logcat, live stats, terminal, macros, and power/fastboot tools. On first launch it downloads Google's platform-tools by itself.

## Get the .exe (no Visual Studio needed)

1. Unzip the file. Open the unzipped folder until you see `Droideck.csproj`, `app.ico` and the `Core` and `Views` folders (if you see another folder with the same name inside, go into it).
2. Go to github.com/new, type a name, click **Create repository**.
3. Click the **uploading an existing file** link. Select everything INSIDE the folder (not the folder itself), drag it into the page, wait until it finishes, then click **Commit changes**.
4. Click the **Actions** tab. Wait 2 to 5 minutes for a green check. (If nothing starts, click the workflow name, then **Run workflow**.)
5. When the check is green, open the repo's **Releases** page (`github.com/<user>/<repo>/releases/latest`) and download `Droideck.exe`. Double-click it. If Windows shows "Windows protected your PC", click **More info**, then **Run anyway**.

If the build fails, open the failed step, type `error` in **Search logs**, and send those lines back.

## Setup wizard

The first time you run `Droideck.exe` from your Downloads folder, a setup wizard opens: it asks for an install folder, adds desktop and Start menu shortcuts, downloads Google's ADB tools, and registers Droideck in Windows Settings > Installed apps so you can uninstall it later. No administrator rights are needed. Choose **Run without installing** to skip it (tick **Don't ask again** to hide it for good).

## Using it

- On the phone: Settings > About phone > tap Build number 7 times, then Developer options > turn on USB debugging. Plug in USB and tap Allow on the phone.
- Wireless (Android 11+): Developer options > Wireless debugging > Pair device with pairing code, then use the Devices tab.
