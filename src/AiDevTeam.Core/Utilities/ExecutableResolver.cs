namespace AiDevTeam.Core.Utilities;

public static class ExecutableResolver
{
    /// <summary>
    /// Resolve a bare executable name to its full path by searching PATH
    /// and common npm/user-local directories.
    /// </summary>
    public static string? Resolve(string name)
    {
        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".cmd", ".exe", ".bat", "" }
            : new[] { "" };

        // Search PATH
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        // Also search common locations not always on PATH when launched from a service
        var extraDirs = new List<string>();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
            extraDirs.Add(Path.Combine(appData, "npm"));
        if (!string.IsNullOrEmpty(userProfile))
        {
            extraDirs.Add(Path.Combine(userProfile, ".local", "bin"));
            extraDirs.Add(Path.Combine(userProfile, "AppData", "Roaming", "npm"));
            extraDirs.Add(Path.Combine(userProfile, "AppData", "Local", "Programs", "copilot-cli"));
        }

        foreach (var dir in pathDirs.Concat(extraDirs))
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
