using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexAccountMonitor.Core;

namespace CodexAccountMonitor;

public sealed class SourceWindow : Window
{
    private readonly TextBox name = new();
    private readonly TextBox shortName = new() { MaxLength = 6 };
    private readonly ComboBox kind = new();
    private readonly ComboBox host = new() { IsEditable = true };
    private readonly TextBox binary = new();
    private readonly TextBox home = new();
    private readonly TextBlock guidance = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = MainBrush("#777BAA"), Margin = new Thickness(0, 14, 0, 10) };
    private readonly TextBlock loginStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = MainBrush("#777BAA"), Margin = new Thickness(0, 10, 0, 10) };
    private readonly Button login = new() { Content = "이 계정으로 로그인", Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button save = new() { Content = "저장", IsDefault = true };
    private readonly Button check = new() { Content = "연결 확인", Background = MainBrush("#EEEEF4") };
    private readonly StackPanel sshFields = new();
    private readonly string id;
    private readonly string profilesRoot;
    private Button? remove;
    private CancellationTokenSource? loginCancellation;
    public AccountSource? Source { get; private set; }
    public bool Deleted { get; private set; }
    internal string StatusText => loginStatus.Text;

    public SourceWindow(AccountSource? existing, string dataDirectory, bool preview = false)
    {
        id = existing?.Id ?? Guid.NewGuid().ToString("N");
        profilesRoot = Path.Combine(dataDirectory, "profiles");
        Title = existing is null ? "계정 추가" : "계정 연결 편집";
        Width = 480; Height = Math.Min(610, SystemParameters.WorkArea.Height - 32); MinWidth = 420; MinHeight = Math.Min(430, Height); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = MainBrush("#FAFAFC"); Foreground = MainBrush("#30323B"); FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(new TextBlock { Text = "마지막 버튼을 눌러야 모니터 목록에 반영됩니다.", FontSize = 10, Foreground = MainBrush("#90919B"), Margin = new Thickness(0, 0, 0, 8) });
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var stack = new StackPanel(); root.Children.Add(new ScrollViewer { Content = stack });
        stack.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        Field(stack, "표시 이름 · 원하는 이름을 입력하세요", name); name.Text = existing?.Name ?? "이 PC";
        shortName.Text = existing?.ShortName ?? "";
        kind.ItemsSource = new[] { "로컬 · 현재 Codex 로그인", "로컬 · 별도 계정으로 로그인", "SSH · 원격 서버의 Codex" };
        Field(stack, "연결 방식", kind);
        Field(sshFields, "SSH 별명 또는 user@host", host); stack.Children.Add(sshFields);
        var sshConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");
        try
        {
            if (!preview && File.Exists(sshConfig)) host.ItemsSource = File.ReadLines(sshConfig).Select(x => x.Trim()).Where(x => x.StartsWith("Host ", StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => x[5..].Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(x => !x.Contains('*') && !x.Contains('?') && !x.StartsWith('!')).Distinct().ToArray();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        host.Text = existing?.SshHost ?? "";
        binary.Text = existing?.CodexPath ?? ""; home.Text = existing?.CodexHome ?? "";
        stack.Children.Add(guidance);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        login.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(login); actions.Children.Add(check); stack.Children.Add(actions); stack.Children.Add(loginStatus);
        check.Click += async (_, _) => await CheckConnectionAsync();
        var advanced = new StackPanel();
        Field(advanced, "작업표시줄 이름 · 선택, 최대 6글자", shortName);
        Field(advanced, "Codex 실행파일 · 기본값은 비워두세요", binary);
        Field(advanced, "Codex 프로필 폴더 (CODEX_HOME) · 기본값은 비워두세요", home);
        advanced.Children.Add(new TextBlock { Text = "SSH 경로는 서버의 절대 경로(/home/...)를 사용합니다. 프로젝트 폴더와 Codex 로그인 프로필은 서로 다릅니다.", FontSize = 10, Foreground = MainBrush("#90919B"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        stack.Children.Add(new Expander { Header = "고급 설정 · 대부분 변경하지 않아도 됩니다", Content = advanced, IsExpanded = existing?.CodexPath is not null || existing?.CodexHome is not null });
        login.Click += LoginClick;
        kind.SelectionChanged += (_, _) =>
        {
            var remote = kind.SelectedIndex == 2;
            sshFields.Visibility = remote ? Visibility.Visible : Visibility.Collapsed;
            host.IsEnabled = remote;
            login.Visibility = kind.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            var automaticHome = Path.Combine(profilesRoot, id);
            if (kind.SelectedIndex != 1 && home.Text == automaticHome) home.Text = "";
            if (kind.SelectedIndex == 1 && string.IsNullOrWhiteSpace(home.Text)) home.Text = automaticHome;
            if (name.Text is "이 PC" or "추가 계정" or "서버 계정") name.Text = kind.SelectedIndex switch { 1 => "추가 계정", 2 => "서버 계정", _ => "이 PC" };
            guidance.Text = kind.SelectedIndex switch
            {
                1 => "현재 Codex 로그인을 유지하면서 다른 계정으로 로그인합니다. 브라우저 로그인 후에도 마지막 ‘모니터에 추가’가 필요합니다.",
                2 => "VSCode나 Codex 앱에서 SSH를 연결했어도 이 모니터에 따로 추가해야 합니다. SSH 별명은 서버 접속 이름이며, 실제 ChatGPT 계정은 ‘연결 확인’에서 확인하세요.",
                _ => "이 PC의 Codex에 현재 로그인된 계정을 조회합니다. 다른 계정은 ‘로컬 · 별도 계정으로 로그인’을 선택하세요."
            };
            loginStatus.Text = "";
        };
        kind.SelectedIndex = existing?.Kind == "ssh" ? 2 : !string.IsNullOrWhiteSpace(existing?.CodexHome) ? 1 : 0;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        if (existing is not null)
        {
            remove = new Button { Content = "연결 삭제", Margin = new Thickness(0, 0, 8, 0) };
            remove.Click += (_, _) => { Deleted = true; DialogResult = true; }; buttons.Children.Add(remove);
        }
        var cancel = new Button { Content = "취소", Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        buttons.Children.Add(cancel);
        save.Content = existing is null ? "모니터에 추가" : "변경 저장"; save.Background = MainBrush("#EEEEF4");
        save.Click += (_, _) => { try { Source = BuildSource(); DialogResult = true; } catch (ArgumentException error) { loginStatus.Text = error.Message; } catch (FileNotFoundException error) { loginStatus.Text = error.Message; } };
        buttons.Children.Add(save); footer.Children.Add(buttons);
        Closed += (_, _) => loginCancellation?.Cancel();
    }

    private AccountSource BuildSource()
    {
        if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("표시 이름을 입력하세요.");
        var source = new AccountSource { Id = id, Name = name.Text.Trim(), ShortName = Empty(shortName.Text), Kind = kind.SelectedIndex == 2 ? "ssh" : "local", SshHost = kind.SelectedIndex == 2 ? Empty(host.Text) : null, CodexPath = Empty(binary.Text),
            CodexHome = Empty(home.Text) };
        if (kind.SelectedIndex == 1 && source.CodexHome is null) source.CodexHome = Path.Combine(profilesRoot, id);
        if (source.Kind == "ssh" && source.CodexHome is not null && !source.CodexHome.StartsWith('/')) throw new ArgumentException("서버 CODEX_HOME은 절대 경로(/home/...)로 입력하세요.");
        _ = SourceLauncher.Create(source);
        return source;
    }
    internal async Task<bool> CheckConnectionAsync()
    {
        if (loginCancellation is not null) return false;
        try
        {
            var source = BuildSource();
            loginCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            SetBusy(true); loginStatus.ToolTip = null; loginStatus.Text = "로그인된 계정을 확인하고 있습니다…";
            await using var connection = new AccountConnection(source);
            var account = await connection.ReadAsync(loginCancellation.Token);
            var quota = account.Windows.Count == 0 ? "계정은 연결됐지만 한도 정보는 제공되지 않았습니다."
                : string.Join(" · ", account.Windows.Select(x => $"{x.DurationLabel} {x.RemainingPercent:0}% 남음"));
            if (account.OrdinaryUsageAllowed == false) quota = "현재 일반 사용 제한 · " + quota;
            loginStatus.Text = $"계정 확인됨 · {MetricFormatting.MaskEmail(account.Email)} · {(account.Plan ?? account.AuthType).ToUpperInvariant()}\n{quota}\n마지막 ‘{save.Content}’를 눌러 목록에 반영하세요.";
            loginStatus.ToolTip = account.Email;
            return true;
        }
        catch (ArgumentException error) { loginStatus.Text = error.Message; }
        catch (FileNotFoundException error) { loginStatus.Text = error.Message; }
        catch (InvalidOperationException error) { loginStatus.Text = error.Message; }
        catch (OperationCanceledException) { loginStatus.Text = "연결 확인 시간이 초과되었거나 취소되었습니다. SSH 키 인증과 네트워크를 확인하세요."; }
        catch (Exception) { loginStatus.Text = kind.SelectedIndex == 2 ? "서버를 조회하지 못했습니다. 터미널에서 SSH 접속과 서버의 Codex 로그인 상태를 확인하세요." : "계정을 조회하지 못했습니다. 이 PC의 Codex 설치와 로그인 상태를 확인하세요."; }
        finally { loginCancellation?.Dispose(); loginCancellation = null; SetBusy(false); }
        return false;
    }
    private void SetBusy(bool busy)
    {
        login.IsEnabled = check.IsEnabled = save.IsEnabled = name.IsEnabled = shortName.IsEnabled = kind.IsEnabled = binary.IsEnabled = home.IsEnabled = !busy;
        host.IsEnabled = !busy && kind.SelectedIndex == 2;
        if (remove is not null) remove.IsEnabled = !busy;
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
            SetBusy(true);
            loginStatus.Text = "로그인 준비 중…";
            await using var connection = new AccountConnection(source);
            await connection.LoginAsync((url, code) =>
            {
                loginStatus.Text = code is null ? "브라우저에서 원하는 계정으로 로그인하세요." : $"브라우저에서 로그인한 뒤 코드 {code}를 입력하세요.";
                if (Uri.TryCreate(url, UriKind.Absolute, out var target) && target.Scheme == "https" && target.Host == "auth.openai.com")
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }, loginCancellation.Token);
            loginStatus.Text = $"로그인 완료 · 마지막 ‘{save.Content}’를 눌러 계정 조회를 시작하세요.";
        }
        catch (ArgumentException error) { loginStatus.Text = error.Message; }
        catch (OperationCanceledException) { loginStatus.Text = "로그인이 취소되었거나 시간이 초과되었습니다."; }
        catch (FileNotFoundException error) { loginStatus.Text = error.Message; }
        catch (Exception) { loginStatus.Text = "로그인을 완료하지 못했습니다. Codex 버전과 브라우저 로그인을 확인하세요."; }
        finally { loginCancellation?.Dispose(); loginCancellation = null; SetBusy(false); }
    }
    internal static void SavePreview(string path, string dataDirectory)
    {
        var window = new SourceWindow(null, dataDirectory, preview: true) { ShowActivated = false };
        window.kind.SelectedIndex = 2; window.name.Text = "Research"; window.host.Text = "research-server";
        window.SaveSnapshot(path);
        window.Close();
    }
    internal void SaveSnapshot(string path)
    {
        ShowActivated = false; Show(); UpdateLayout();
        var client = (FrameworkElement)Content;
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)(client.ActualHeight + client.Margin.Top + client.Margin.Bottom), 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
    }
    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Field(StackPanel stack, string label, Control control)
    { stack.Children.Add(new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 12, 0, 5), Foreground = MainBrush("#90919B") }); stack.Children.Add(control); }
    private static SolidColorBrush MainBrush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
}
