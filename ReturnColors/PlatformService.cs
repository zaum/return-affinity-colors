using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace ReturnColors;
#pragma warning disable CA1416

internal interface IPlatformService
{
    bool IsAdmin();
    bool ElevateIfNeeded(string? actionArg = null);
}

internal static class PlatformService
{
    private static readonly IPlatformService Implementation = OperatingSystem.IsWindows()
        ? new WindowsPlatformService()
        : new UnixPlatformService();

    public static bool IsAdmin() => Implementation.IsAdmin();

    public static bool ElevateIfNeeded(string? actionArg = null) => Implementation.ElevateIfNeeded(actionArg);
}

internal sealed class WindowsPlatformService : IPlatformService
{
    public bool IsAdmin()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public bool ElevateIfNeeded(string? actionArg = null)
    {
        if (IsAdmin())
            return true;

        try
        {
            var process = Process.GetCurrentProcess();
            var baseArgs = string.Join(" ", Environment.GetCommandLineArgs().Skip(1));
            var extraArg = string.IsNullOrEmpty(actionArg) ? "" : $" --elevated {actionArg}";
            var startInfo = new ProcessStartInfo
            {
                FileName = process.MainModule?.FileName,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"{baseArgs}{extraArg}".Trim(),
            };

            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception)
        {
            Log.Error("Administrator elevation was cancelled or failed. Write access to the Affinity installation may be denied.");
            return false;
        }
    }
}

internal sealed class UnixPlatformService : IPlatformService
{
    public bool IsAdmin() => GetEuid() == 0;

    public bool ElevateIfNeeded(string? actionArg = null)
    {
        if (IsAdmin())
            return true;

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return false;

        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var command = $"\"{exe}\" {string.Join(" ", args.Select(a => $"\"{a}\""))}".Trim();

        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var applescript = $"do shell script \"{command}\" with administrator privileges";
                using var osa = Process.Start("/usr/bin/osascript", new[] { "-e", applescript });
                osa?.WaitForExit();
            }
            else
            {
                using var pkexec = Process.Start("pkexec", [exe, .. args]);
                if (pkexec is not null)
                {
                    pkexec.WaitForExit();
                }
                else
                {
                    using var sudo = Process.Start("sudo", [exe, .. args]);
                    sudo?.WaitForExit();
                }
            }

            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Log.Error("Privilege elevation failed. Run the application with administrator/sudo privileges.");
            return false;
        }
    }

    private static int GetEuid()
    {
        try
        {
            using var id = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "id",
                    Arguments = "-u",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                }
            );
            id?.WaitForExit();
            var output = id?.StandardOutput.ReadToEnd().Trim() ?? "0";
            return int.TryParse(output, out var value) ? value : 0;
        }
        catch
        {
            return 0;
        }
    }
}
