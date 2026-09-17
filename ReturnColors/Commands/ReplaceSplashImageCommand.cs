using System.Collections;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Resources;
using dnlib.DotNet;

namespace ReturnColors.Commands;

internal static class ReplaceSplashImageCommand
{
    [field: AllowNull, MaybeNull]
    public static Command Command
    {
        get
        {
            if (field is not null)
                return field;

            field = new Command("splash", "Replace the Affinity startup splash image.");
            field.Arguments.Add(Global.DirectoryArgument);
            field.Options.Add(Global.TerminateOption);
            field.Options.Add(Options.BackupOption);
            field.Options.Add(Options.SplashImageOption);
            field.SetAction((ParseResult pr, CancellationToken ct) => ParseAndExecute(pr, ct));
            return field;
        }
    }

    internal static async Task Run(ParseResult parseResult) =>
        await ParseAndExecute(parseResult, CancellationToken.None);

    private static async Task ParseAndExecute(ParseResult parseResult, CancellationToken cancellationToken)
    {
        if (!CheckCommand.Execute(parseResult))
            return;

        if (parseResult.GetValue(Global.TerminateOption))
            Global.TerminateAffinity();

        var directory = parseResult.GetValue(Global.DirectoryArgument)!;
        var installation = DiscoveryService.FindInstallationPreferring(directory) ?? new AffinityInstallation(
            directory,
            null,
            new FileInfo(Path.Combine(directory.FullName, "Affinity.exe")),
            null,
            "Affinity.g.resources",
            "resources/images/splash.imageset/studioprosplash.png"
        );
        var exePath = installation.SplashExecutable?.FullName
            ?? Path.Combine(directory.FullName, "Affinity.exe");
        var resourceName = installation.SplashResourceName ?? "Affinity.g.resources";
        var splashImageKey = installation.SplashImageKey
            ?? "resources/images/splash.imageset/studioprosplash.png";
        Log.Info($"Patching \"{exePath}\" (resource \"{resourceName}\").");
        var backup = parseResult.GetValue(Options.BackupOption);

        if (backup is not null && !new FileInfo(exePath).BackUp(backup))
        {
            Log.Error("Failed to back up current Affinity executable.");
            return;
        }

        var splashImage = parseResult.GetRequiredValue(Options.SplashImageOption);
        var exeBytes = await File.ReadAllBytesAsync(exePath, cancellationToken);
        using var module = ModuleDefMD.Load(
            exeBytes,
            new ModuleCreationOptions(ModuleDef.CreateModuleContext())
        );
        using var resourceReader = new ResourceReader(
            module.Resources.FindEmbeddedResource(resourceName).CreateReader().AsStream()
        );
        var resourcesTempFile = Path.Combine(AppContext.BaseDirectory, Path.GetRandomFileName());
        var resourceWriter = new ResourceWriter(resourcesTempFile);

        foreach (DictionaryEntry resource in resourceReader)
        {
            var key = resource.Key.ToString() ?? "";

            if (key != splashImageKey)
            {
                resourceWriter.AddResource(key, resource.Value);
                continue;
            }

            await using var fs = new FileStream(splashImage.FullName, FileMode.Open);
            var buffer = new byte[fs.Length];
            await fs.ReadExactlyAsync(buffer, 0, (int)fs.Length, cancellationToken);
            var ms = new MemoryStream(buffer, false);
            resourceWriter.AddResource(key, ms, true);
        }

        resourceWriter.Dispose();
        var resourceIndex = module.Resources.IndexOf(resourceName);
        if (resourceIndex < 0)
        {
            Log.Error($"Could not find embedded resource \"{resourceName}\" in the executable.");
            File.Delete(resourcesTempFile);
            return;
        }

        var mergedResourcesFs = new FileStream(resourcesTempFile, FileMode.Open);
        var resBuffer = new Memory<byte>(new byte[mergedResourcesFs.Length]);
        _ = await mergedResourcesFs.ReadAsync(resBuffer, cancellationToken);
        var newResource = new EmbeddedResource(
            resourceName,
            resBuffer.ToArray(),
            ManifestResourceAttributes.Public
        );
        module.Resources[resourceIndex] = newResource;
        var replaced = FileOperations.ReplaceWithUpdatedFile(exePath, module.Write);
        await mergedResourcesFs.DisposeAsync();
        File.Delete(resourcesTempFile);

        if (replaced)
        {
            Log.Info(
                $"Updated \"{exePath}\", replacing the startup splash image with \"{splashImage.FullName}\"."
            );
            Log.Success("Finished — splash image was written. Restart Affinity to see it.");
        }
    }

    private static class Options
    {
        public static readonly Option<DirectoryInfo> BackupOption = new("--backup", "-b")
        {
            DefaultValueFactory = _ => new DirectoryInfo(AppContext.BaseDirectory),
            Description =
                "The directory into which to back up the current Affinity.exe file before modifying it.",
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
        public static readonly Option<FileInfo> SplashImageOption = new("--img", "-i")
        {
            Description = "The image to use as the Affinity startup splash image.",
            Required = true,
            Validators =
            {
                result =>
                {
                    var file = result.GetValueOrDefault<FileInfo>();

                    if (!file.Exists)
                        result.AddError($"\"{file.FullName}\" does not exist.");
                },
            },
        };
    }
}
