using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CodexAccountMonitor.Core;

public static partial class SourceLauncher
{
    public static ProcessStartInfo Create(AccountSource source)
    {
        var info = new ProcessStartInfo { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8 };
        if (source.Kind == "ssh")
        {
            if (string.IsNullOrWhiteSpace(source.SshHost) || !HostPattern().IsMatch(source.SshHost) || source.SshHost.StartsWith('-'))
                throw new ArgumentException("SSH 별명 또는 user@host를 입력하세요. 공백과 옵션은 허용되지 않습니다.");
            info.FileName = "ssh.exe";
            foreach (var arg in new[] { "-T", "-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "-o", "ServerAliveInterval=20", "-o", "ServerAliveCountMax=2", source.SshHost }) info.ArgumentList.Add(arg);
            var executable = string.IsNullOrWhiteSpace(source.CodexPath) ? "codex" : QuotePosix(source.CodexPath);
            var command = $"exec {executable} app-server --listen stdio://";
            if (!string.IsNullOrWhiteSpace(source.CodexHome))
                command = $"export CODEX_HOME={QuotePosix(source.CodexHome)}; " + command;
            info.ArgumentList.Add("bash -lc " + QuotePosix(command));
        }
        else if (source.Kind == "local")
        {
            info.FileName = FindLocalCodex(source.CodexPath);
            info.ArgumentList.Add("app-server");
            info.ArgumentList.Add("--listen");
            info.ArgumentList.Add("stdio://");
            if (!string.IsNullOrWhiteSpace(source.CodexHome)) info.Environment["CODEX_HOME"] = Environment.ExpandEnvironmentVariables(source.CodexHome);
            info.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else throw new ArgumentException("지원하지 않는 연결 방식입니다.");
        return info;
    }

    public static string FindLocalCodex(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var path = Environment.ExpandEnvironmentVariables(configured);
            if (path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("스크립트 대신 Codex의 실제 codex.exe 경로를 지정하세요.");
            return path;
        }
        var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(basePath))
        {
            var candidate = Directory.EnumerateFiles(basePath, "codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (candidate is not null) return candidate;
        }
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var file = Path.Combine(folder.Trim('"'), "codex.exe");
            if (File.Exists(file)) return file;
        }
        var npmPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai");
        if (Directory.Exists(npmPath))
        {
            var candidate = Directory.EnumerateFiles(npmPath, "codex.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (candidate is not null) return candidate;
        }
        throw new FileNotFoundException("Codex 실행파일을 찾지 못했습니다. Codex 앱을 설치하거나 연결 설정에 codex.exe 경로를 지정하세요.");
    }

    public static string QuotePosix(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.@:\-]*$")]
    private static partial Regex HostPattern();
}
