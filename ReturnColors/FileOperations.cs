using System.Diagnostics;
using ReturnColors.Commands;

namespace ReturnColors;

internal static class FileOperations
{
    public static bool BackUp(this FileInfo file, DirectoryInfo backupTo, bool pause = false)
    {
        if (!CheckCommand.Execute(backupTo))
            return false;

        var backupFile = file.CopyTo(Path.Combine(backupTo.FullName, $"{file.Name}.bak"), true);
        Log.Info($"Backed up \"{file.FullName}\" to \"{backupFile.FullName}\".");

        if (pause)
            Global.Pause();

        return true;
    }

    /// <summary>
    /// Writes the updated assembly to a temp file next to the original and
    /// then atomically swaps it in. Unlike deleting the original first, a
    /// failed write never leaves the Affinity installation without a DLL/EXE.
    /// </summary>
    /// <returns>True when the original file was replaced.</returns>
    public static bool ReplaceWithUpdatedFile(string originalPath, Action<string> writeUpdated)
    {
        try
        {
            if (Process.GetProcessesByName("Affinity").Length > 0)
            {
                Log.Warning(
                    "Affinity appears to be running. Close it before patching, "
                    + "otherwise the file may stay locked or the running app may keep using the old icons. "
                    + "You can also pass --terminate to close it automatically."
                );
            }
        }
        catch
        {
            // Process enumeration is best-effort only.
        }

        var directory = Path.GetDirectoryName(originalPath) ?? AppContext.BaseDirectory;
        var tempPath = Path.Combine(directory, Path.GetRandomFileName());

        try
        {
            writeUpdated(tempPath);

            try
            {
                File.SetAttributes(originalPath, FileAttributes.Normal);
            }
            catch
            {
                // Read-only flag is best-effort; the replace below reports real failures.
            }

            File.Replace(tempPath, originalPath, null);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            Log.Error(
                $"Access denied writing \"{originalPath}\". Run this tool as administrator, "
                + "make sure Affinity is closed, and (for MSIX installs) that the Administrators "
                + "group has full control over the Affinity App folder."
            );
        }
        catch (IOException exception)
        {
            Log.Error($"Failed to replace \"{originalPath}\": {exception.Message}");
        }

        try
        {
            File.Delete(tempPath);
        }
        catch
        {
            // Best-effort cleanup of the temp file.
        }

        return false;
    }
}
