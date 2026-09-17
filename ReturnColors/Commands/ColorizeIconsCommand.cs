using System.Collections;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Resources;
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using Microsoft.Extensions.FileProviders;

namespace ReturnColors.Commands;

internal static class ColorizeIconsCommand
{
    [field: AllowNull, MaybeNull]
    public static Command Command
    {
        get
        {
            if (field is not null)
                return field;

            field = new Command(
                "colorize",
                "Replace the Affinity monochrome icons with the v2 colored icons."
            );
            field.Arguments.Add(Global.DirectoryArgument);
            field.Options.Add(Global.TerminateOption);
            field.Options.Add(Options.BackupOption);
            field.Options.Add(Options.PauseOption);
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

        var pause = parseResult.GetValue(Options.PauseOption);
        var directory = parseResult.GetValue(Global.DirectoryArgument)!;
        var installation = DiscoveryService.FindInstallationPreferring(directory) ?? new AffinityInstallation(
            directory,
            new FileInfo(Path.Combine(directory.FullName, "Serif.Affinity.dll")),
            null,
            "Serif.Affinity.g.resources",
            null,
            null
        );
        var dllPath = installation.IconLibrary?.FullName
            ?? Path.Combine(directory.FullName, "Serif.Affinity.dll");
        var resourceName = installation.IconResourceName ?? "Serif.Affinity.g.resources";
        Log.Info($"Patching \"{dllPath}\" (resource \"{resourceName}\").");
        var backup = parseResult.GetValue(Options.BackupOption);

        if (backup is not null && !new FileInfo(dllPath).BackUp(backup, pause))
        {
            Log.Error("Failed to back up current Serif.Affinity.dll.");
            return;
        }

        var dllBytes = await File.ReadAllBytesAsync(dllPath, cancellationToken);
        using var module = ModuleDefMD.Load(
            dllBytes,
            new ModuleCreationOptions(ModuleDef.CreateModuleContext())
        );
        var resourcesTempFile = Path.Combine(AppContext.BaseDirectory, Path.GetRandomFileName());
        MergeResources(module, resourcesTempFile, resourceName);
        await ReplaceResources(module, resourcesTempFile, resourceName);

        if (pause)
            Global.Pause();

        var saved = SaveDll(module, dllPath, resourceName);
        File.Delete(resourcesTempFile);

        if (saved)
            Log.Success("Finished — colored icons were written. Restart Affinity to see them.");
    }

    private static ResourceReader GetV2ResourceReader(Disposables disposables)
    {
        var assembly = typeof(ColorizeIconsCommand).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(n =>
                n.Replace('\\', '/').EndsWith("Serif.Affinity.v2.g.resources", StringComparison.OrdinalIgnoreCase)
            )
            ?? throw new InvalidOperationException("Embedded v2 resources file was not found.");

        return new ResourceReader(
            assembly
                .GetManifestResourceStream(resourceName)!
                .DisposeWith(disposables)
        );
    }

    private static ResourceReader GetV3ResourceReader(
        ModuleDefMD module,
        Disposables disposables,
        string resourceName
    ) =>
        new(
            module
                .Resources.FindEmbeddedResource(resourceName)
                .CreateReader()
                .AsStream()
                .DisposeWith(disposables)
        );

    private static void MergeResources(ModuleDefMD module, string resourcesFile, string resourceName)
    {
        File.Delete(resourcesFile);
        Disposables disposables = [];
        using var v2ResourceReader = GetV2ResourceReader(disposables);
        using var v3ResourceReader = GetV3ResourceReader(module, disposables, resourceName);
        // Build the v2 lookup once instead of once per icon (there are
        // thousands of icons, so rebuilding it per key is very slow).
        var v2Index = IconKeyMatcher.BuildIndex(v2ResourceReader);
        var mergedResourcesWriter = new ResourceWriter(resourcesFile);
        var mergedCount = 0;
        var keptCount = 0;

        foreach (DictionaryEntry v3Entry in v3ResourceReader)
        {
            var key = v3Entry.Key.ToString() ?? "";

            if (
                !key.EndsWith(".png")
                || !key.StartsWith("resources/icons/")
            )
            {
                mergedResourcesWriter.AddResource(key, v3Entry.Value);
                continue;
            }

            try
            {
                var v2Resource = IconKeyMatcher.FindV2Resource(key, v2Index);
                mergedResourcesWriter.AddResource(key, v2Resource ?? v3Entry.Value);
                if (v2Resource is not null)
                {
                    mergedCount++;
                    Log.Warning($"Merged v2 resource \"{key}\".");
                }
                else
                {
                    keptCount++;
                }
            }
            catch (Exception exception)
            {
                Log.Error($"Failed to merge v2 resource \"{key}\".");
                Log.Error(exception.Message);
                mergedResourcesWriter.AddResource(key, v3Entry.Value);
                keptCount++;
            }
        }

        Log.Info($"Replaced {mergedCount} icons with v2 versions, kept {keptCount} originals.");
        if (mergedCount == 0)
        {
            Log.Warning(
                "No icons were replaced. The installed Affinity version probably renamed its "
                + "icon resources; run \"dump icons\" and compare the key names, then update "
                + "IconKeyMatcher with the new mappings."
            );
        }

        mergedResourcesWriter.Dispose();
        disposables.Dispose();
    }

    private static async ValueTask ReplaceResources(ModuleDefMD module, string resourcesFile, string resourceName)
    {
        var resourceIndex = module.Resources.IndexOf(resourceName);
        if (resourceIndex < 0)
        {
            Log.Error($"Could not find embedded resource \"{resourceName}\" in the library.");
            return;
        }

        await using var mergedResourcesFs = new FileStream(resourcesFile, FileMode.Open);
        var buffer = new Memory<byte>(new byte[mergedResourcesFs.Length]);
        await mergedResourcesFs.ReadExactlyAsync(buffer);
        var newResource = new EmbeddedResource(
            resourceName,
            buffer.ToArray(),
            ManifestResourceAttributes.Public
        );
        module.Resources[resourceIndex] = newResource;
    }

    private static bool SaveDll(ModuleDefMD module, string path, string resourceName)
    {
        var replaced = FileOperations.ReplaceWithUpdatedFile(
            path,
            tempPath =>
            {
                if (module.IsILOnly)
                    module.Write(tempPath);
                else
                {
                    var writerOptions = new NativeModuleWriterOptions(module, false);
                    module.NativeWrite(tempPath, writerOptions);
                }
            }
        );

        if (replaced)
            Log.Info($"Updated \"{path}\", replacing monochrome icons with colored icons.");

        return replaced;
    }

    private static class Options
    {
        public static readonly Option<DirectoryInfo> BackupOption = new("--backup", "-b")
        {
            DefaultValueFactory = _ => new DirectoryInfo(AppContext.BaseDirectory),
            Description =
                "The directory into which to back up the current Serif.Affinity.dll file before modifying it.",
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
        public static readonly Option<bool> PauseOption = new("--pause", "-p")
        {
            DefaultValueFactory = _ => false,
            Description = "Pause between each step of the process and wait for user input.",
        };
    }
}
