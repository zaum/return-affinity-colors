using System.Collections.ObjectModel;
using System.CommandLine;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using ReturnColors;
using ReturnColors.Commands;

namespace ReturnColors.UI;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<LogEntry> _log = [];
    private AffinityInstallation? _installation;
    private bool _busy;
    private string? _pendingAction;

    public MainWindow()
    {
        InitializeComponent();
        LogList.ItemsSource = _log;
        Log.EntryReceived += OnLogEntry;

        BrowseButton.Click += (_, _) => _ = BrowseAsync();
        RunButton.Click += (_, _) => _ = RunActionAsync("colorize");
        SplashButton.Click += (_, _) => _ = RunActionAsync("splash");
        RestoreButton.Click += (_, _) => _ = RunRestoreAsync();
        ImportButton.Click += (_, _) => _ = RunImportAsync();
        DumpButton.Click += (_, _) => _ = RunDumpAsync();

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await SearchAsync();

        if (AppState.AutoAction is not null)
        {
            var action = AppState.AutoAction;
            AppState.AutoAction = null;
            await Task.Delay(500);
            try
            {
                await RunActionAsync(action);
            }
            catch (Exception ex)
            {
                Log.Error($"Auto-action '{action}' failed: {ex.Message}");
            }
        }
    }

    private void OnLogEntry(LogEntry entry)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            AppendLog(entry);
        }
        else
        {
            Dispatcher.UIThread.Post(() => AppendLog(entry));
        }
    }

    private void AppendLog(LogEntry entry)
    {
        _log.Add(entry);
        LogList.ScrollIntoView(_log.Last());
    }

    private void SetStatus(string status) => StatusText.Text = status;

    private void RefreshStatusPanel()
    {
        if (PlatformService.IsAdmin())
        {
            AdminText.Inlines?.Clear();
            AdminText.Text = "yes (elevated)";
            AdminText.Foreground = Brushes.Green;
        }
        else
        {
            AdminText.Text = null;
            AdminText.Foreground = Brushes.Green;
            AdminText.Inlines?.Clear();
            AdminText.Inlines?.Add(new Run("no ") { Foreground = Brushes.Red });
            AdminText.Inlines?.Add(new Run("— don't worry, admin permission will be requested automatically when you start an operation") { Foreground = Brushes.Green });
        }

        if (_installation is null)
        {
            PathText.Text = "-";
            WriteText.Text = "-";
            DetectedText.Text = "-";
            RunButton.IsEnabled = false;
            return;
        }

        PathText.Text = _installation.Directory.FullName;
        RunButton.IsEnabled = !_busy && _installation.IconLibrary is not null;

        var parts = new List<string>();
        if (_installation.IconLibrary is not null)
            parts.Add($"icon library: {_installation.IconLibrary.Name}");
        if (_installation.SplashExecutable is not null)
            parts.Add($"splash exe: {_installation.SplashExecutable.Name}");
        if (_installation.IconResourceName is not null)
            parts.Add($"icon resource: {_installation.IconResourceName}");
        if (_installation.SplashResourceName is not null)
            parts.Add($"splash resource: {_installation.SplashResourceName}");
        DetectedText.Text = parts.Count > 0 ? string.Join(Environment.NewLine, parts) : "no Affinity artifacts detected";

        var hasWrite = _installation.Directory.Exists && HasWriteAccess(_installation.Directory);
        WriteText.Text = hasWrite ? "yes" : "no (admin may be required)";
        WriteText.Foreground = hasWrite ? Brushes.Green : Brushes.OrangeRed;
    }

    private static bool HasWriteAccess(DirectoryInfo directory)
    {
        try
        {
            var temp = Path.Combine(directory.FullName, Path.GetRandomFileName());
            using var _ = File.Create(temp, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private async Task SearchAsync()
    {
        if (_busy)
            return;

        SetStatus("Searching for Affinity...");
        Log.Info("Searching for an existing Affinity installation...");

        await Task.Run(() =>
        {
            try
            {
                _installation = DiscoveryService.FindInstallation();
            }
            catch (Exception ex)
            {
                Log.Error($"Search failed: {ex.Message}");
                _installation = null;
            }
        });

        if (_installation is null)
        {
            SetStatus("Affinity not found.");
            Log.Warning("Could not automatically locate Affinity. Use 'Select Affinity folder...' to select the installation folder.");
        }
        else
        {
            SetStatus("Affinity found.");
            Log.Info($"Found Affinity at \"{_installation.Directory.FullName}\".");
            if (!PlatformService.IsAdmin())
                Log.Error("This application is not running with administrator privileges. Patching may fail; the app will attempt to elevate when you start an operation.");
        }

        RefreshStatusPanel();
    }

    private async Task BrowseAsync()
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new()
        {
            Title = "Select Affinity installation folder",
            AllowMultiple = false,
        });
        if (result.Count == 0)
            return;

        var directory = new DirectoryInfo(result[0].Path.LocalPath);
        SetStatus("Manual path selected.");
        Log.Info($"Using manually selected path: \"{directory.FullName}\".");

        AffinityInstallation? found = null;
        await Task.Run(() =>
        {
            found = DiscoveryService.FindInstallation(directory);
        });

        _installation = found ?? new AffinityInstallation(
            directory,
            null,
            null,
            null,
            null,
            null
        );

        if (_installation.IconLibrary is null && _installation.SplashExecutable is null)
        {
            Log.Warning("No Affinity artifacts detected in the selected folder, but the path will be used as-is.");
        }

        RefreshStatusPanel();
    }

    private async Task RunActionAsync(string action)
    {
        if (_busy || _installation is null || _pendingAction is not null)
            return;

        if (!PlatformService.IsAdmin())
        {
            Log.Warning("Requesting administrator privileges...");
            _pendingAction = action;
            var elevated = PlatformService.ElevateIfNeeded(action);
            if (elevated)
                Environment.Exit(0);
            _pendingAction = null;
            return;
        }

        SetBusy(true);

        if (action == "colorize")
        {
            try
            {
                Log.Info("Starting icon colorization...");
                await Task.Run(() => ColorizeIconsCommand.Run(CreateParseResult(_installation.Directory)));
                Log.Info("Icon colorization finished.");
            }
            catch (IOException ex) when (ex.Message.Contains("used by another process", StringComparison.OrdinalIgnoreCase))
            {
                Log.Error("A different process is using the files.");
                Log.Error("Close Affinity and try again.");
                var terminate = await DisplayAlert(
                    "Affinity is running",
                    "Affinity has the files locked. Close Affinity automatically and retry?",
                    "Close Affinity & Retry",
                    "Cancel"
                );
                if (terminate)
                {
                    Global.TerminateAffinity();
                    await Task.Delay(500);
                    try
                    {
                        await Task.Run(() => ColorizeIconsCommand.Run(CreateParseResult(_installation.Directory)));
                        Log.Info("Icon colorization finished.");
                    }
                    catch (Exception ex2)
                    {
                        Log.Error($"Icon colorization failed: {ex2.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Icon colorization failed: {ex.Message}");
            }
        }
        else if (action == "splash")
        {
            var picker = await StorageProvider.OpenFilePickerAsync(new()
            {
                Title = "Select splash image (587x450px)",
                AllowMultiple = false,
                FileTypeFilter = [new("PNG images") { Patterns = ["*.png"] }],
            });
            if (picker.Count == 0)
            {
                SetBusy(false);
                return;
            }

            var splashPath = picker[0].Path.LocalPath;
            try
            {
                Log.Info("Replacing splash image...");
                await Task.Run(() =>
                    ReplaceSplashImageCommand.Run(CreateSplashParseResult(_installation.Directory, splashPath))
                );
                Log.Info("Splash image replacement finished.");
            }
            catch (Exception ex)
            {
                Log.Error($"Splash replacement failed: {ex.Message}");
            }
        }
        else if (action == "restore")
        {
            await ExecuteRestoreAsync();
        }

        SetBusy(false);
    }

    private async Task RunRestoreAsync()
    {
        if (_busy || _installation is null || _pendingAction is not null)
            return;

        if (!PlatformService.IsAdmin())
        {
            Log.Warning("Requesting administrator privileges...");
            _pendingAction = "restore";
            var elevated = PlatformService.ElevateIfNeeded("restore");
            if (elevated)
                Environment.Exit(0);
            _pendingAction = null;
            return;
        }

        await ExecuteRestoreAsync();
    }

    private async Task ExecuteRestoreAsync()
    {
        if (_installation is null)
            return;

        SetBusy(true);
        try
        {
            Log.Info("Restoring original files from backup...");
            var installDirectory = _installation.Directory;
            var backupDirectory = new DirectoryInfo(AppContext.BaseDirectory);
            await Task.Run(() => RestoreCommand.Run(installDirectory, backupDirectory));
        }
        catch (Exception ex)
        {
            Log.Error($"Restore failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunImportAsync()
    {
        if (_busy || _installation is null)
            return;

        var picker = await StorageProvider.OpenFolderPickerAsync(new()
        {
            Title = "Select folder with custom icons",
            AllowMultiple = false,
        });
        if (picker.Count == 0)
            return;

        var folder = picker[0].Path.LocalPath;

        SetBusy(true);
        try
        {
            Log.Info("Importing custom icons...");
            await Task.Run(() =>
                ImportCommand.Command.Parse(
                    ["icons", _installation.Directory.FullName, "-i", folder]
                ).InvokeAsync()
            );
            Log.Info("Custom icon import finished.");
        }
        catch (Exception ex)
        {
            Log.Error($"Import failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RunDumpAsync()
    {
        if (_busy || _installation is null)
            return;

        var picker = await StorageProvider.OpenFolderPickerAsync(new()
        {
            Title = "Select output folder for dumped icons",
            AllowMultiple = false,
        });
        if (picker.Count == 0)
            return;

        var folder = picker[0].Path.LocalPath;

        SetBusy(true);
        try
        {
            Log.Info("Dumping icons...");
            await Task.Run(() =>
                DumpCommand.Command.Parse(
                    ["dump", "icons", _installation.Directory.FullName, "-o", folder]
                ).InvokeAsync()
            );
            Log.Info("Icon dump finished.");
        }
        catch (Exception ex)
        {
            Log.Error($"Dump failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        SetStatus(busy ? "Working..." : (_installation is null ? "Affinity not found." : "Ready."));
        BrowseButton.IsEnabled = !busy;
        RunButton.IsEnabled = !busy && _installation?.IconLibrary is not null;
        SplashButton.IsEnabled = !busy && _installation?.SplashExecutable is not null;
        RestoreButton.IsEnabled = !busy;
        ImportButton.IsEnabled = !busy;
        DumpButton.IsEnabled = !busy;
    }

    private async Task<bool> DisplayAlert(string title, string message, string yes, string no)
    {
        var yesBtn = new Button { Content = yes, MinWidth = 100 };
        var noBtn = new Button { Content = no, MinWidth = 100 };
        var tcs = new TaskCompletionSource<bool>();

        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14 },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Children = { yesBtn, noBtn },
                    }
                }
            }
        };

        dialog.Closed += (_, _) => tcs.TrySetResult(false);
        yesBtn.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        noBtn.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };

        await dialog.ShowDialog(this);
        return await tcs.Task;
    }

    private static ParseResult CreateParseResult(DirectoryInfo directory) =>
        ColorizeIconsCommand.Command.Parse([directory.FullName]);

    private static ParseResult CreateSplashParseResult(DirectoryInfo directory, string image) =>
        ReplaceSplashImageCommand.Command.Parse([directory.FullName, "--img", image]);
}
