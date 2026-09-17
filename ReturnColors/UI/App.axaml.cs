using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ReturnColors;

namespace ReturnColors.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Copying text out of the window (Ctrl+C) can throw a transient
        // COMException when the Windows clipboard is momentarily busy.
        // Logging it instead of crashing keeps the app (and a long patch
        // run) alive; anything else still crashes loudly as before.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            if (e.Exception is System.Runtime.InteropServices.COMException)
            {
                Log.Warning($"Clipboard temporarily unavailable, nothing was copied: {e.Exception.Message}");
                e.Handled = true;
                return;
            }

            Log.Error($"Unhandled error: {e.Exception}");
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
