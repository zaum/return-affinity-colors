using System.CommandLine;
using System.Diagnostics;
using Microsoft.Win32;

namespace ReturnColors;

internal static class Global
{
    public static readonly Argument<DirectoryInfo> DirectoryArgument = new("dir")
    {
        DefaultValueFactory = result =>
        {
            var affinityPath = GetAffinityInstallationPath();

            if (affinityPath is not null)
                return new DirectoryInfo(affinityPath);

            result.AddError(
                "No existing Affinity installation path was found.\nYou must manually specify the installation path."
            );
            return null!;
        },
        Description = "The directory where Affinity is installed.",
        Validators =
        {
            result =>
            {
                try
                {
                    var directory = result.GetValueOrDefault<DirectoryInfo>();

                    if (!directory.Exists)
                        result.AddError($"\"{directory.FullName}\" does not exist.");
                }
                catch (InvalidOperationException) { }
            },
        },
    };

    public static readonly Option<bool> TerminateOption = new("--terminate", "-t")
    {
        Arity = ArgumentArity.ZeroOrOne,
        DefaultValueFactory = _ => false,
        Description = "Force closes Affinity if it is running.",
    };

    private static string? GetAffinityInstallationPath()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        // v2 installs register under Serif, newer Canva builds under Canva.
        string[] keyPaths = [@"Software\Serif\Affinity\Affinity", @"Software\Canva\Affinity\Affinity"];
        foreach (var keyPath in keyPaths)
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                var path = (string?)hive.OpenSubKey(keyPath)?.GetValue("Affinity Install Path");
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    return path;
            }
        }

        return null;
    }

    public static void Pause()
    {
        Console.WriteLine("Press any key to continue...");
        _ = Console.ReadKey();
    }

    public static void TerminateAffinity()
    {
        var affinityProcesses = Process.GetProcessesByName("Affinity");

        foreach (var process in affinityProcesses)
            process.Kill();
    }
}
