using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CodexAccountMonitor.Core;

namespace CodexAccountMonitor;

public sealed class SettingsStore
{
    public string DirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexAccountMonitor");
    public string SettingsPath { get; }
    private string CachePath => Path.Combine(DirectoryPath, "cache.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public SettingsStore(string? customPath = null) { SettingsPath = customPath ?? Path.Combine(DirectoryPath, "settings.json"); }
    public MonitorSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new() { Sources = [new AccountSource()] };
        try
        {
            var result = JsonSerializer.Deserialize<MonitorSettings>(File.ReadAllText(SettingsPath), Options) ?? throw new JsonException();
            result.RefreshSeconds = Math.Clamp(result.RefreshSeconds, 30, 3600);
            if (result.Sources.Count > 50) throw new JsonException();
            var ids = new HashSet<string>();
            foreach (var source in result.Sources) if (string.IsNullOrWhiteSpace(source.Id) || !ids.Add(source.Id)) throw new JsonException();
            return result;
        }
        catch (Exception error) when (error is JsonException or IOException)
        { throw new InvalidDataException("설정 파일을 읽지 못했습니다. settings.json을 확인하세요. 기존 파일은 보존됩니다.", error); }
    }
    public void Save(MonitorSettings settings) => Write(SettingsPath, settings);
    public List<AccountSnapshot> LoadCache()
    {
        try { return File.Exists(CachePath) ? JsonSerializer.Deserialize<List<AccountSnapshot>>(File.ReadAllText(CachePath), Options) ?? [] : []; }
        catch (Exception error) when (error is JsonException or IOException) { return []; }
    }
    public void SaveCache(IEnumerable<AccountSnapshot> data) => Write(CachePath, data);
    public ResetMonitorState LoadResetState()
    {
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
    private static void Write<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(data, Options));
        File.Move(temporary, path, overwrite: true);
    }
}
