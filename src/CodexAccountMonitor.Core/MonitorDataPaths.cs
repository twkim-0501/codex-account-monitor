namespace CodexAccountMonitor.Core;

public static class MonitorDataPaths
{
    // AppData writes inherited from a packaged launcher can be invisible to Windows startup.
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-account-monitor");

    public static IEnumerable<string> LegacyDirectories()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localData, "CodexAccountMonitor");
        var packages = Path.Combine(localData, "Packages");
        string[] directories;
        try { directories = Directory.Exists(packages) ? Directory.GetDirectories(packages, "OpenAI.Codex_*") : []; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { directories = []; }
        foreach (var package in directories) yield return Path.Combine(package, "LocalCache", "Local", "CodexAccountMonitor");
    }

    public static string StartupCommand(string executable, string settingsPath) => $"\"{executable}\" --minimized --settings \"{settingsPath}\"";
}
