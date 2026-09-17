using System.CommandLine;
using System.Diagnostics.CodeAnalysis;

namespace ReturnColors.Commands;

internal static class RestoreCommand
{
    [field: AllowNull, MaybeNull]
    public static Command Command
    {
        get
        {
            if (field is not null)
                return field;

            field = new Command(
                "restore",
                "Restore the original Affinity files from their .bak backup copies."
            );
            field.Arguments.Add(Global.DirectoryArgument);
            field.Options.Add(Options.BackupOption);
            field.SetAction((ParseResult pr, CancellationToken _) =>
            {
                Run(pr.GetValue(Global.DirectoryArgument)!, pr.GetValue(Options.BackupOption)!);
                return Task.CompletedTask;
            });
            return field;
        }
    }

    internal static void Run(DirectoryInfo installDirectory, DirectoryInfo backupDirectory)
    {
        if (!CheckCommand.Execute(installDirectory))
            return;

        if (!backupDirectory.Exists)
        {
            Log.Error($"Backup directory \"{backupDirectory.FullName}\" does not exist.");
            return;
        }

        string[] fileNames = ["Serif.Affinity.dll", "Affinity.exe"];
        var restoredAny = false;

        foreach (var name in fileNames)
        {
            var backupPath = Path.Combine(backupDirectory.FullName, $"{name}.bak");
            var targetPath = Path.Combine(installDirectory.FullName, name);

            if (!File.Exists(backupPath))
            {
                Log.Warning($"No backup found for \"{name}\" in \"{backupDirectory.FullName}\"; skipping.");
                continue;
            }

            if (!File.Exists(targetPath))
            {
                Log.Warning($"Target \"{targetPath}\" not found; skipping.");
                continue;
            }

            if (FileOperations.ReplaceWithUpdatedFile(targetPath, temp => File.Copy(backupPath, temp, true)))
            {
                Log.Success($"Restored original \"{name}\".");
                restoredAny = true;
            }
        }

        if (restoredAny)
            Log.Success("Finished — original files were restored. Restart Affinity to use them.");
    }

    private static class Options
    {
        public static readonly Option<DirectoryInfo> BackupOption = new("--backup", "-b")
        {
            DefaultValueFactory = _ => new DirectoryInfo(AppContext.BaseDirectory),
            Description =
                "The directory containing the .bak backup files (defaults to the folder this tool runs from).",
            Validators =
            {
                result =>
                {
                    var directory = result.GetValueOrDefault<DirectoryInfo>();

                    if (!directory.Exists)
                        result.AddError($"\"{directory.FullName}\" does not exist.");
                },
            },
        };
    }
}
