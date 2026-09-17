using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ReturnColors;
#pragma warning disable CA1416

internal sealed record AffinityInstallation(
    DirectoryInfo Directory,
    FileInfo? IconLibrary,         // e.g. Serif.Affinity.dll (icons live here)
    FileInfo? SplashExecutable,    // e.g. Affinity.exe (splash lives here)
    string? IconResourceName,      // embedded resource name inside IconLibrary
    string? SplashResourceName,    // embedded resource name inside SplashExecutable
    string? SplashImageKey         // resource key of the splash png inside the exe resources
);

internal static partial class DiscoveryService
{
    [GeneratedRegex(@"Serif\.Affinity.*\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex IconLibraryRegex();

    [GeneratedRegex(@"Affinity.*\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex SplashExecutableRegex();

    [GeneratedRegex(@"Serif\.Affinity.*\.resources$")]
    private static partial Regex IconResourcesRegex();

    [GeneratedRegex(@"Affinity.*\.resources$")]
    private static partial Regex SplashResourcesRegex();

    [GeneratedRegex(@"resources/images/splash\.imageset/.*\.png$", RegexOptions.IgnoreCase)]
    private static partial Regex SplashImageKeyRegex();

    public static AffinityInstallation? FindInstallation() => FindInstallation(FindInstallationDirectory());

    /// <summary>
    /// Resolves the installation to patch. A directory given on the command
    /// line always wins over auto-detection, so after an Affinity update the
    /// tool patches the folder the user pointed at instead of a stale
    /// auto-detected one.
    /// </summary>
    public static AffinityInstallation? FindInstallationPreferring(DirectoryInfo? explicitDirectory) =>
        FindInstallation(explicitDirectory) ?? FindInstallation();

    public static AffinityInstallation? FindInstallation(DirectoryInfo? directory)
    {
        if (directory is null || !directory.Exists)
            return null;

        var iconLibrary = FindFile(directory, IconLibraryRegex());
        var splashExecutable = FindFile(directory, SplashExecutableRegex());
        var iconResourceName = iconLibrary is not null ? FindIconResourceName(iconLibrary) : null;
        var splashResourceName = splashExecutable is not null ? FindSplashResourceName(splashExecutable) : null;
        var splashImageKey = splashExecutable is not null ? FindSplashImageKey(splashExecutable) : null;

        return new AffinityInstallation(
            directory,
            iconLibrary,
            splashExecutable,
            iconResourceName,
            splashResourceName,
            splashImageKey
        );
    }

    private static DirectoryInfo? FindInstallationDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var fromRegistry = FindWindowsInstallationDirectoryFromRegistry();
            if (fromRegistry is not null)
                return fromRegistry;
        }

        DirectoryInfo? best = null;
        Version? bestVersion = null;
        foreach (var candidate in EnumerateInstallationDirectoryCandidates())
        {
            if (!candidate.Exists || !HasAffinityArtifacts(candidate))
                continue;

            // After an MSIX update several version folders can linger side by
            // side (e.g. Canva.Affinity_3.0.0.3791_... next to
            // Canva.Affinity_3.3.0.4850_...). Prefer the newest version so a
            // stale folder is never patched by accident.
            var version = TryParsePackageVersion(candidate.FullName);
            if (best is null || (version is not null && (bestVersion is null || version > bestVersion)))
            {
                best = candidate;
                bestVersion = version;
            }
        }

        return best;
    }

    private static Version? TryParsePackageVersion(string path)
    {
        var match = Regex.Match(path, @"_(\d+\.\d+\.\d+\.\d+)_");
        return match.Success && Version.TryParse(match.Groups[1].Value, out var version)
            ? version
            : null;
    }

    private static DirectoryInfo? FindWindowsInstallationDirectoryFromRegistry()
    {
        // Affinity v2 registers under Serif, newer Canva builds under Canva.
        // Check both vendors, machine-wide first, then per-user.
        string[] keyPaths = [@"Software\Serif\Affinity", @"Software\Canva\Affinity"];
        foreach (var keyPath in keyPaths)
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                using var affinityKey = hive.OpenSubKey(keyPath);
                if (affinityKey is null)
                    continue;

                foreach (var subKeyName in affinityKey.GetSubKeyNames())
                {
                    using var subKey = affinityKey.OpenSubKey(subKeyName);
                    var path = subKey?.GetValue("Affinity Install Path") as string;
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        return new DirectoryInfo(path);
                }
            }
        }

        return null;
    }

    private static IEnumerable<DirectoryInfo> EnumerateInstallationDirectoryCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
            if (!string.IsNullOrEmpty(programFiles))
            {
                yield return new DirectoryInfo(Path.Combine(programFiles, "Affinity", "Affinity"));
                yield return new DirectoryInfo(Path.Combine(programFiles, "Affinity"));
                yield return new DirectoryInfo(Path.Combine(programFiles, "Serif", "Affinity"));
            }

            var windowsApps = Environment.GetEnvironmentVariable("ProgramFiles") is { } pf
                ? Path.Combine(pf, "WindowsApps")
                : null;
            if (windowsApps is not null && Directory.Exists(windowsApps))
            {
                foreach (var dir in new DirectoryInfo(windowsApps).EnumerateDirectories("*Affinity*"))
                    yield return new DirectoryInfo(Path.Combine(dir.FullName, "App"));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return new DirectoryInfo("/Applications");
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                yield return new DirectoryInfo(Path.Combine(home, "Applications"));
        }
        else if (OperatingSystem.IsLinux())
        {
            yield return new DirectoryInfo("/opt");
            yield return new DirectoryInfo("/usr/share");
            yield return new DirectoryInfo("/usr/local");
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                yield return new DirectoryInfo(home);
        }
    }

    private static bool HasAffinityArtifacts(DirectoryInfo directory)
    {
        try
        {
            return directory
                .EnumerateFiles()
                .Any(f => IconLibraryRegex().IsMatch(f.Name) || SplashExecutableRegex().IsMatch(f.Name));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static FileInfo? FindFile(DirectoryInfo directory, Regex regex)
    {
        try
        {
            return directory
                .EnumerateFiles()
                .Where(f => regex.IsMatch(f.Name))
                .OrderByDescending(f => f.Length)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FindIconResourceName(FileInfo library)
    {
        try
        {
            using var module = dnlib.DotNet.ModuleDefMD.Load(library.FullName);
            return module
                .Resources.OfType<dnlib.DotNet.EmbeddedResource>()
                .Select(r => r.Name)
                .FirstOrDefault(n => IconResourcesRegex().IsMatch(n));
        }
        catch
        {
            return null;
        }
    }

    private static string? FindSplashResourceName(FileInfo executable)
    {
        try
        {
            using var module = dnlib.DotNet.ModuleDefMD.Load(executable.FullName);
            return module
                .Resources.OfType<dnlib.DotNet.EmbeddedResource>()
                .Select(r => r.Name)
                .FirstOrDefault(n => SplashResourcesRegex().IsMatch(n));
        }
        catch
        {
            return null;
        }
    }

    private static string? FindSplashImageKey(FileInfo executable)
    {
        try
        {
            using var module = dnlib.DotNet.ModuleDefMD.Load(executable.FullName);
            var resource = module
                .Resources.OfType<dnlib.DotNet.EmbeddedResource>()
                .FirstOrDefault(r => SplashResourcesRegex().IsMatch(r.Name));
            if (resource is null)
                return null;

            using var reader = new System.Resources.ResourceReader(resource.CreateReader().AsStream());
            foreach (System.Collections.DictionaryEntry entry in reader)
            {
                var key = entry.Key.ToString() ?? string.Empty;
                if (SplashImageKeyRegex().IsMatch(key))
                    return key;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static FileInfo? FindEmbeddedV2ResourcesFile()
    {
        var assembly = typeof(DiscoveryService).Assembly;
        var prefix = assembly.GetName().Name ?? string.Empty;
        var resourceNames = assembly.GetManifestResourceNames();
        var match = resourceNames.FirstOrDefault(n =>
            n.Replace('\\', '/').EndsWith("Serif.Affinity.v2.g.resources", StringComparison.OrdinalIgnoreCase)
            || n.Replace('\\', '/').Contains("Serif.Affinity", StringComparison.OrdinalIgnoreCase)
                && n.Replace('\\', '/').Contains(".v2.", StringComparison.OrdinalIgnoreCase)
                && n.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)
        );
        return match is null ? null : new FileInfo(match);
    }
}
