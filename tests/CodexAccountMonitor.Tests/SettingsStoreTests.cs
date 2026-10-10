using System.Text.Json;
using CodexAccountMonitor.Core;

internal static class SettingsStoreTests
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodexAccountMonitorSettings-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            var native = Path.Combine(root, "native", "CodexAccountMonitor");
            var packaged = Path.Combine(root, "package", "LocalCache", "Local", "CodexAccountMonitor");
            var shared = Path.Combine(root, "profile", ".codex-account-monitor");
            MonitorSettings Accounts(params string[] names) => new() { StartWithWindows = true,
                Sources = names.Select(name => new AccountSource { Id = name, Name = name, Kind = "ssh", SshHost = "host-" + name }).ToList() };
            void Write(string directory, string file, object data)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, file), JsonSerializer.Serialize(data));
            }
            var configured = Accounts("hrk", "nmail2", "twkim");
            Write(packaged, "settings.json", configured);
            Write(native, "settings.json", new MonitorSettings { Sources = [new AccountSource()] });
            File.SetLastWriteTimeUtc(Path.Combine(packaged, "settings.json"), DateTime.UtcNow.AddDays(-1));
            var original = File.ReadAllText(Path.Combine(packaged, "settings.json"));
            var snapshot = new AccountSnapshot { SourceId = "twkim", Plan = "pro", ResetCredits = 2 };
            Write(packaged, "cache.json", new[] { snapshot });
            var noticeTime = DateTimeOffset.UtcNow;
            Write(packaged, "reset-state.json", new ResetMonitorState { Delivered = new() { ["credit-announcement"] = noticeTime } });
            var store = new SettingsStore(dataDirectory: shared, legacyDirectories: [native, packaged]);
            var loaded = store.Load();
            check(loaded.Sources.Select(s => s.Name).SequenceEqual(new[] { "hrk", "nmail2", "twkim" }),
                "a newer first-run this-PC store cannot hide configured accounts in the Codex package");
            check(loaded.StartWithWindows && loaded.Sources.All(s => s.Kind == "ssh"), "migration preserves startup and SSH connection settings");
            check(File.ReadAllText(Path.Combine(packaged, "settings.json")) == original && File.Exists(Path.Combine(native, "settings.json")),
                "migration preserves both legacy originals");
            check(store.LoadCache().Single().SourceId == "twkim" && store.LoadCache().Single().ResetCredits == 2,
                "migration takes account cache from the selected configuration");
            check(store.LoadResetState().Delivered["credit-announcement"] == noticeTime, "migration retains notice deduplication state");
            var restarted = new SettingsStore(dataDirectory: shared, legacyDirectories: [native]);
            check(restarted.Load().Sources.Select(s => s.Id).SequenceEqual(loaded.Sources.Select(s => s.Id)),
                "a restart without the package view reads the same shared accounts");
            Write(packaged, "settings.json", Accounts("obsolete"));
            loaded.Sources.RemoveAt(0); store.Save(loaded);
            check(new SettingsStore(dataDirectory: shared, legacyDirectories: [packaged]).Load().Sources.Count == 2,
                "later legacy edits cannot replace the authoritative shared settings");
            store.Save(new MonitorSettings());
            check(new SettingsStore(dataDirectory: shared, legacyDirectories: [packaged]).Load().Sources.Count == 0,
                "deliberately deleting all connections does not restore old accounts on restart");
            File.WriteAllText(store.SettingsPath, "{");
            var rejected = false;
            try { restarted.Load(); } catch (InvalidDataException) { rejected = true; }
            check(rejected && File.ReadAllText(store.SettingsPath) == "{", "invalid shared settings remain preserved instead of being silently replaced");

            var other = Path.Combine(root, "invalid"); Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(other, "settings.json"), "{bad json");
            Write(packaged, "settings.json", configured);
            var recovered = new SettingsStore(dataDirectory: Path.Combine(root, "recovered"), legacyDirectories: [other, packaged]).Load();
            check(recovered.Sources.Count == 3, "an invalid legacy store does not prevent recovery from the other valid store");

            Write(native, "settings.json", Accounts("recent"));
            File.SetLastWriteTimeUtc(Path.Combine(native, "settings.json"), DateTime.UtcNow.AddHours(1));
            var mostRecent = new SettingsStore(dataDirectory: Path.Combine(root, "recent"), legacyDirectories: [packaged, native]).Load();
            check(mostRecent.Sources.Single().Id == "recent", "when both legacy stores were configured, the most recent settings win");

            var fresh = new SettingsStore(dataDirectory: Path.Combine(root, "fresh"), legacyDirectories: []);
            check(fresh.Load().Sources.Single().Name == "이 PC" && !File.Exists(fresh.SettingsPath), "a fresh installation still offers this PC without fabricating saved settings");
            var readOnly = new SettingsStore(dataDirectory: Path.Combine(root, "readonly"), legacyDirectories: [packaged]);
            check(readOnly.Load(migrateLegacy: false).Sources.Single().Name == "이 PC" && !Directory.Exists(readOnly.DirectoryPath),
                "demo reads cannot migrate or write the user's configuration");

            var customPath = Path.Combine(root, "custom", "settings.json");
            var isolated = new SettingsStore(customPath, legacyDirectories: [packaged]);
            check(isolated.Load().Sources.Single().Name == "이 PC" && isolated.DirectoryPath == Path.GetDirectoryName(customPath),
                "explicit custom settings isolate state and do not import unrelated real accounts");
            isolated.Save(Accounts("isolated")); isolated.SaveCache([snapshot]);
            check(File.Exists(Path.Combine(isolated.DirectoryPath, "cache.json")) && isolated.Load().Sources.Single().Id == "isolated",
                "custom connection and cache writes stay in the same isolated directory");
            check(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "atomic writes leave no partial temporary settings behind");
            var executable = Path.Combine(root, "path with spaces", "Monitor.exe");
            check(MonitorDataPaths.StartupCommand(executable, customPath) == $"\"{executable}\" --minimized --settings \"{customPath}\"",
                "Windows startup quotes the executable and pins the shared settings path");
            check(MonitorDataPaths.DefaultDirectory == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-account-monitor"),
                "the shared default directory is outside package-virtualized AppData");
        }
        finally
        {
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Path.GetDirectoryName(root) != temporaryRoot) throw new InvalidOperationException("Unexpected test cleanup directory");
            Directory.Delete(root, recursive: true);
        }
    }
}
