using System.CommandLine;
using System.Runtime.InteropServices;
using Avalonia;
using ReturnColors;
using ReturnColors.Commands;
using ReturnColors.UI;

if (args.Length > 0)
{
    var elevatedIndex = Array.IndexOf(args, "--elevated");
    if (elevatedIndex >= 0 && elevatedIndex + 1 < args.Length)
    {
        AppState.AutoAction = args[elevatedIndex + 1];
        args = args.Where((_, i) => i != elevatedIndex && i != elevatedIndex + 1).ToArray();
    }

    if (args.Length > 0)
    {
        var rootCommand = new RootCommand("Application for customizing Affinity.");
        rootCommand.Subcommands.Add(CheckCommand.Command);
        rootCommand.Subcommands.Add(ColorizeIconsCommand.Command);
        rootCommand.Subcommands.Add(DumpCommand.Command);
        rootCommand.Subcommands.Add(ImportCommand.Command);
        rootCommand.Subcommands.Add(RestoreCommand.Command);
        rootCommand.Subcommands.Add(ReplaceSplashImageCommand.Command);
        await rootCommand.Parse(args).InvokeAsync();
        return;
    }
}

if (OperatingSystem.IsWindows())
    NativeMethods.HideConsoleWindow();

BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();

internal static partial class NativeMethods
{
    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    private const int SW_HIDE = 0;

    public static void HideConsoleWindow()
    {
        var handle = GetConsoleWindow();
        if (handle != 0)
            ShowWindow(handle, SW_HIDE);
    }
}
