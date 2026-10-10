using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CodexAccountMonitor.Core;

public sealed class SettingsStore
{
    public string DirectoryPath { get; }
    public string SettingsPath { get; }
    private string CachePath => Path.Combine(DirectoryPath, "cache.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly IEnumerable<string> legacyDirectories;
    private bool migrationAttempted;

    public SettingsStore(string? customPath = null, string? dataDirectory = null, IEnumerable<string>? legacyDirectories = null)
    {
        DirectoryPath = Path.GetFullPath(dataDirectory ?? (customPath is null ? MonitorDataPaths.DefaultDirectory : Path.GetDirectoryName(Path.GetFullPath(customPath))!));
        SettingsPath = customPath is null ? Path.Combine(DirectoryPath, "settings.json") : Path.GetFullPath(customPath);
        this.legacyDirectories = customPath is null ? legacyDirectories ?? MonitorDataPaths.LegacyDirectories() : [];
    }

    public MonitorSettings Load(bool migrateLegacy = true)
    {
        if (migrateLegacy) MigrateLegacyState();
        if (!File.Exists(SettingsPath)) return new() { Sources = [new AccountSource()] };
        return ReadSettings(SettingsPath);
    }

    private static MonitorSettings ReadSettings(string path)
    {
        try
        {
            var result = JsonSerializer.Deserialize<MonitorSettings>(File.ReadAllText(path), Options) ?? throw new JsonException();
            result.RefreshSeconds = Math.Clamp(result.RefreshSeconds, 30, 3600);
            if (result.Sources is null || result.Sources.Count > 50) throw new JsonException();
            var ids = new HashSet<string>();
            foreach (var source in result.Sources) if (source is null || string.IsNullOrWhiteSpace(source.Id) || !ids.Add(source.Id)) throw new JsonException();
            return result;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        { throw new InvalidDataException("설정 파일을 읽지 못했습니다. settings.json을 확인하세요. 기존 파일은 보존됩니다.", error); }
    }
    public void Save(MonitorSettings settings) => Write(SettingsPath, settings);
    public List<AccountSnapshot> LoadCache()
    {
        MigrateLegacyState();
        try { return File.Exists(CachePath) ? JsonSerializer.Deserialize<List<AccountSnapshot>>(File.ReadAllText(CachePath), Options) ?? [] : []; }
        catch (Exception error) when (error is JsonException or IOException) { return []; }
    }
    public void SaveCache(IEnumerable<AccountSnapshot> data) => Write(CachePath, data);
    public ResetMonitorState LoadResetState()
    {
        MigrateLegacyState();
        try
        {
            var path = Path.Combine(DirectoryPath, "reset-state.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 2_000_000) return new();
            var state = JsonSerializer.Deserialize<ResetMonitorState>(File.ReadAllText(path), Options);
            if (state?.Outlook is null || state.Outlook.Signals is null || state.Outlook.Completions is null || state.Delivered is null) return new();
            state.Outlook.Signals = state.Outlook.Signals.Take(4).ToList();
            state.Outlook.Completions = state.Outlook.Completions.Take(2).ToList();
            state.Outlook.CurrentContext = (state.Outlook.CurrentContext ?? []).Take(3).ToList();
            return state;
        }
        catch (Exception error) when (error is JsonException or IOException) { return new(); }
    }
    public void SaveResetState(ResetMonitorState state) => Write(Path.Combine(DirectoryPath, "reset-state.json"), state);

    private void MigrateLegacyState()
    {
        if (migrationAttempted || File.Exists(SettingsPath)) return;
        migrationAttempted = true;
        var candidates = new List<(string Directory, MonitorSettings Settings, DateTime Written)>();
        foreach (var directory in legacyDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(directory, "settings.json");
            if (!File.Exists(path)) continue;
            try { candidates.Add((directory, ReadSettings(path), File.GetLastWriteTimeUtc(path))); }
            catch (InvalidDataException) { /* Preserve unreadable originals and try the other legacy store. */ }
        }
        var selected = candidates.OrderByDescending(c => HasConfiguredConnections(c.Settings)).ThenByDescending(c => c.Written).FirstOrDefault();
        if (selected.Settings is null) return;
        Directory.CreateDirectory(DirectoryPath);
        if (!WriteNew(SettingsPath, selected.Settings)) return;
        foreach (var name in new[] { "cache.json", "reset-state.json" })
        {
            var original = Path.Combine(selected.Directory, name);
            var destination = Path.Combine(DirectoryPath, name);
            if (!File.Exists(original) || File.Exists(destination)) continue;
            try { File.Copy(original, destination); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Cache is optional; live reads recover it. */ }
        }
    }

    private static bool HasConfiguredConnections(MonitorSettings settings) => settings.Sources.Count != 1 ||
        settings.Sources[0] is not { Name: "이 PC", Kind: "local", Enabled: true, CodexHome: null, CodexPath: null, SshHost: null, ShortName: null };

    private static bool WriteNew<T>(string path, T data)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, Options));
            try { File.Move(temporary, path); return true; }
            catch (IOException) when (File.Exists(path)) { return false; }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Write<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, Options));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
