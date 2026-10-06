using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexAccountMonitor.Core;

namespace CodexAccountMonitor;

public sealed class SourceWindow : Window
{
    private readonly TextBox name = new();
    private readonly ComboBox kind = new();
    private readonly ComboBox host = new() { IsEditable = true };
    private readonly TextBox binary = new();
    private readonly TextBox home = new();
    private readonly TextBlock loginStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = MainBrush("#52DCC4"), Margin = new Thickness(0, 10, 0, 10) };
    private readonly Button login = new() { Content = "이 계정으로 로그인", Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button save = new() { Content = "저장", IsDefault = true };
    private readonly string id;
    private readonly string profilesRoot;
    private CancellationTokenSource? loginCancellation;
    public AccountSource? Source { get; private set; }
    public bool Deleted { get; private set; }

    public SourceWindow(AccountSource? existing, string dataDirectory)
    {
        id = existing?.Id ?? Guid.NewGuid().ToString("N");
        profilesRoot = Path.Combine(dataDirectory, "profiles");
        Title = existing is null ? "계정 연결 추가" : "계정 연결 편집";
        Width = 460; Height = 650; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = MainBrush("#0C1016"); Foreground = MainBrush("#EDF4FA"); FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
        var stack = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = stack };
        stack.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        Field(stack, "표시 이름", name); name.Text = existing?.Name ?? "";
        kind.ItemsSource = new[] { "로컬 · 현재 Codex 로그인", "로컬 · 별도 계정으로 로그인", "SSH · 원격 서버의 Codex" };
        Field(stack, "연결 방식", kind);
        Field(stack, "SSH 별명 또는 user@host", host);
        var sshConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");
        try
        {
            if (File.Exists(sshConfig)) host.ItemsSource = File.ReadLines(sshConfig).Select(x => x.Trim()).Where(x => x.StartsWith("Host ", StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => x[5..].Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(x => !x.Contains('*') && !x.Contains('?') && !x.StartsWith('!')).Distinct().ToArray();
        }
        catch (IOException) { }
        host.Text = existing?.SshHost ?? "";
        Field(stack, "Codex 실행파일 · 비워두면 자동 탐색", binary); binary.Text = existing?.CodexPath ?? "";
        Field(stack, "CODEX_HOME · 기본 경로는 비워두기", home); home.Text = existing?.CodexHome ?? "";
        stack.Children.Add(new TextBlock { Text = "별도 계정은 프로필을 분리해 현재 Codex 로그인을 유지합니다. SSH 연결은 기존 SSH 키와 서버의 로그인을 사용합니다.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = MainBrush("#93A4B8"), Margin = new Thickness(0, 12, 0, 6) });
        stack.Children.Add(login); stack.Children.Add(loginStatus);
        login.Click += LoginClick;
        kind.SelectionChanged += (_, _) =>
        {
            var remote = kind.SelectedIndex == 2;
            host.IsEnabled = remote;
            login.Visibility = kind.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (kind.SelectedIndex == 1 && string.IsNullOrWhiteSpace(home.Text)) home.Text = Path.Combine(profilesRoot, id);
        };
        kind.SelectedIndex = existing?.Kind == "ssh" ? 2 : !string.IsNullOrWhiteSpace(existing?.CodexHome) ? 1 : 0;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        if (existing is not null)
        {
            var remove = new Button { Content = "연결 삭제", Margin = new Thickness(0, 0, 8, 0) };
            remove.Click += (_, _) => { Deleted = true; DialogResult = true; }; buttons.Children.Add(remove);
        }
        var cancel = new Button { Content = "취소", Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        buttons.Children.Add(cancel);
        save.Click += (_, _) => { try { Source = BuildSource(); DialogResult = true; } catch (ArgumentException error) { loginStatus.Text = error.Message; } };
        buttons.Children.Add(save); stack.Children.Add(buttons);
        Closed += (_, _) => loginCancellation?.Cancel();
    }

    private AccountSource BuildSource()
    {
        if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("표시 이름을 입력하세요.");
        var source = new AccountSource { Id = id, Name = name.Text.Trim(), Kind = kind.SelectedIndex == 2 ? "ssh" : "local", SshHost = Empty(host.Text), CodexPath = Empty(binary.Text),
            CodexHome = Empty(home.Text) };
        if (kind.SelectedIndex == 1 && source.CodexHome is null) source.CodexHome = Path.Combine(profilesRoot, id);
        if (source.Kind == "ssh" && source.CodexHome is not null && !source.CodexHome.StartsWith('/')) throw new ArgumentException("서버 CODEX_HOME은 절대 경로(/home/...)로 입력하세요.");
        _ = SourceLauncher.Create(source);
        return source;
    }
    private async void LoginClick(object sender, RoutedEventArgs e)
    {
        if (loginCancellation is not null) return;
        try
        {
            var source = BuildSource();
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(source.CodexHome!));
            var currentCodexHome = Path.GetFullPath(Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"));
            if (string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), currentCodexHome.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("현재 Codex의 프로필에는 앱에서 로그인할 수 없습니다. 별도 폴더를 선택하세요.");
            Directory.CreateDirectory(path);
            var config = Path.Combine(path, "config.toml");
            if (!File.Exists(config)) File.WriteAllText(config, "cli_auth_credentials_store = \"keyring\"\n");
            loginCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            login.IsEnabled = false;
            save.IsEnabled = name.IsEnabled = kind.IsEnabled = binary.IsEnabled = home.IsEnabled = false;
            loginStatus.Text = "로그인 준비 중…";
            await using var connection = new AccountConnection(source);
            await connection.LoginAsync((url, code) =>
            {
                loginStatus.Text = code is null ? "브라우저에서 원하는 계정으로 로그인하세요." : $"브라우저에서 로그인한 뒤 코드 {code}를 입력하세요.";
                if (Uri.TryCreate(url, UriKind.Absolute, out var target) && target.Scheme == "https" && target.Host == "auth.openai.com")
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }, loginCancellation.Token);
            loginStatus.Text = "로그인 완료 · 저장을 누르면 계정 조회를 시작합니다.";
        }
        catch (ArgumentException error) { loginStatus.Text = error.Message; }
        catch (OperationCanceledException) { loginStatus.Text = "로그인이 취소되었거나 시간이 초과되었습니다."; }
        catch (Exception) { loginStatus.Text = "로그인을 완료하지 못했습니다. Codex 버전과 브라우저 로그인을 확인하세요."; }
        finally { loginCancellation?.Dispose(); loginCancellation = null; login.IsEnabled = save.IsEnabled = name.IsEnabled = kind.IsEnabled = binary.IsEnabled = home.IsEnabled = true; }
    }
    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Field(StackPanel stack, string label, Control control)
    { stack.Children.Add(new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 12, 0, 5), Foreground = MainBrush("#93A4B8") }); stack.Children.Add(control); }
    private static SolidColorBrush MainBrush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
}
