using System.Windows;
using Droideck.Core;
using Droideck.Setup;

namespace Droideck;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            AppState.SetStatus("Error: " + e.Exception.Message);
            e.Handled = true;
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--uninstall")) new UninstallWindow().Show();
        else if (Installer.ShouldOfferSetup(e.Args)) new InstallWizard().Show();
        else new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Remote.CloseAll();
        base.OnExit(e);
    }
}
