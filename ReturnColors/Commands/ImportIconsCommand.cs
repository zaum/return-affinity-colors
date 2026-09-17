using System.Collections;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Resources;
using dnlib.DotNet;
using dnlib.DotNet.Writer;

namespace ReturnColors.Commands;

internal static class ImportIconsCommand
{
    [field: AllowNull, MaybeNull]
    public static Command Command
    {
        get
        {
            if (field is not null)
                return field;

            field = new Command("icons", "Import Affinity icons from a folder.");
            field.Arguments.Add(Global.DirectoryArgument);
            field.SetAction(Execute);
            return field;
        }
    }

    private static async Task Execute(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var installDirectory = parseResult.GetValue(Global.DirectoryArgument)!;

        if (!CheckCommand.Execute(installDirectory))
            return;

        var installation = DiscoveryService.FindInstallationPreferring(installDirectory) ?? new AffinityInstallation(
            installDirectory,
            new FileInfo(Path.Combine(installDirectory.FullName, "Serif.Affinity.dll")),
            null,
            "Serif.Affinity.g.resources",
            null,
            null
        );
        var dllPath = installation.IconLibrary?.FullName
            ?? Path.Combine(installDirectory.FullName, "Serif.Affinity.dll");
        var resourceName = installation.IconResourceName ?? "Serif.Affinity.g.resources";
        var dllBytes = await File.ReadAllBytesAsync(dllPath, cancellationToken);
        using var module = ModuleDefMD.Load(
            dllBytes,
            new ModuleCreationOptions(ModuleDef.CreateModuleContext())
        );
        var inputDirectory = parseResult.GetRequiredValue(
            ImportCommand.Options.ResourcesDirectoryOption
        );
        var mergedResourcesFile = MergeResources(module, inputDirectory, resourceName);
        var resourceIndex = module.Resources.IndexOf(resourceName);
        var mergedResourcesFs = new FileStream(
            mergedResourcesFile.FullName,
            FileMode.Open,
            FileAccess.Read
        );
        var buffer = new Memory<byte>(new byte[mergedResourcesFs.Length]);
        await mergedResourcesFs.ReadExactlyAsync(buffer, cancellationToken);
        await mergedResourcesFs.DisposeAsync();
        var newResource = new EmbeddedResource(
            resourceName,
            buffer.ToArray(),
            ManifestResourceAttributes.Public
        );
        module.Resources[resourceIndex] = newResource;
        var saved = SaveDll(module, dllPath, resourceName);
        mergedResourcesFile.Delete();

        if (saved)
            Log.Success("Finished — custom icons were written. Restart Affinity to see them.");
    }

    private static FileStream? FindCustomResource(string resourceKey, DirectoryInfo inputDirectory)
    {
        var splits = resourceKey.Split('/')[2..];
        var fileName = Path.Combine(inputDirectory.FullName, Path.Combine(splits));
        var file = new FileInfo(fileName);
        return !file.Exists ? null : file.OpenRead();
    }

    private static FileInfo MergeResources(ModuleDefMD module, DirectoryInfo inputDirectory, string resourceName)
    {
        var disposables = new Disposables();
        using var affinityResourceReader = new ResourceReader(
            module
                .Resources.FindEmbeddedResource(resourceName)
                .CreateReader()
                .AsStream()
                .DisposeWith(disposables)
        );
        var resourcesTempFile = Path.Combine(AppContext.BaseDirectory, Path.GetRandomFileName());
        var mergedResourcesWriter = new ResourceWriter(resourcesTempFile);

        foreach (DictionaryEntry entry in affinityResourceReader)
        {
            var key = entry.Key.ToString() ?? "";

            if (!key.EndsWith(".png") || !key.StartsWith("resources/icons/"))
            {
                mergedResourcesWriter.AddResource(key, entry.Value);
                continue;
            }

            var customResource = FindCustomResource(key, inputDirectory);

            if (customResource is null)
            {
                mergedResourcesWriter.AddResource(key, entry.Value);
                continue;
            }

            mergedResourcesWriter.AddResource(key, customResource, true);
        }

        mergedResourcesWriter.Dispose();
        disposables.Dispose();
        return new FileInfo(resourcesTempFile);
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
            Log.Info($"Updated \"{path}\", importing custom icons.");

        return replaced;
    }
}
