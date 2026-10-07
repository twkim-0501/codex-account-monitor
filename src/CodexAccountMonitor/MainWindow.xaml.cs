using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using CodexAccountMonitor.Core;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace CodexAccountMonitor;

public partial class MainWindow : Window
{
    private readonly SettingsStore store;
    private readonly MonitorSettings settings;
    private readonly Dictionary<string, AccountConnection> connections = [];
    private readonly Dictionary<string, AccountSnapshot> snapshots = [];
    private readonly Dictionary<string, string> errors = [];
    private readonly HashSet<string> healthy = [];
    private readonly HashSet<string> alertStates = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer = new();
    private readonly Forms.NotifyIcon? tray;
    private readonly TaskbarWidget? miniWidget;
    private bool refreshing;
    private bool exiting;
    private readonly bool demo;
    private readonly string? screenshotPath;
    private readonly bool firstRun;
    public bool StartsCollapsed => screenshotPath is null && settings.ShowMiniWidget && !firstRun;

    public MainWindow(bool demo, string? screenshotPath, string? customSettings, int demoAccounts = 3, bool layoutCheck = false)
    {
        InitializeComponent();
        this.demo = demo;
        this.screenshotPath = screenshotPath;
        store = new(customSettings);
        firstRun = !File.Exists(store.SettingsPath) && !demo;
        try { settings = store.Load(); }
        catch (InvalidDataException error) { MessageBox.Show(error.Message, Title); settings = new(); }
        if (firstRun && screenshotPath is null) store.Save(settings);
        Topmost = settings.AlwaysOnTop;
        var area = SystemParameters.WorkArea;
        Height = Math.Min(Height, area.Height - 24);
        Left = area.Left + 12;
        Top = Math.Max(area.Top + 12, area.Bottom - Height - 12);
        if (demo) LoadDemo(demoAccounts);
        else foreach (var snapshot in store.LoadCache()) snapshots[snapshot.SourceId] = snapshot;
        InitializeResetMonitor();
        if (screenshotPath is null && !layoutCheck)
        {
            tray = new Forms.NotifyIcon { Icon = CreateIcon(), Text = "Codex Account Monitor", Visible = true };
            tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ToggleWindow); };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("열기 / 숨기기", null, (_, _) => Dispatcher.Invoke(ToggleWindow));
            menu.Items.Add("새로고침", null, (_, _) => Dispatcher.Invoke(() => RefreshClick(this, new RoutedEventArgs())));
            menu.Items.Add("설정", null, (_, _) => Dispatcher.Invoke(() => SettingsClick(this, new RoutedEventArgs())));
            var miniMenu = new Forms.ToolStripMenuItem("왼쪽 미니 위젯 표시") { Checked = settings.ShowMiniWidget, CheckOnClick = true };
            miniMenu.Click += (_, _) => Dispatcher.Invoke(() =>
            {
                settings.ShowMiniWidget = miniMenu.Checked;
                if (!demo) store.Save(settings);
                miniWidget?.Configure(settings.ShowMiniWidget, settings.DockMiniWidget);
            });
            menu.Items.Add(miniMenu);
            menu.Opening += (_, _) => miniMenu.Checked = settings.ShowMiniWidget;
            menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(async () => await ExitAsync()));
            tray.ContextMenuStrip = menu;
            try
            {
                miniWidget = new TaskbarWidget();
                miniWidget.Click += ToggleWindow;
                miniWidget.RightClick += () => { miniMenu.Checked = settings.ShowMiniWidget; menu.Show(Forms.Cursor.Position); };
                miniWidget.Configure(settings.ShowMiniWidget, settings.DockMiniWidget);
            }
            catch (Win32Exception) { /* The notification icon remains available if native widget creation fails. */ }
        }
        timer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds);
        timer.Tick += async (_, _) => await RefreshAsync();
        RenderCards();
        if (screenshotPath is not null)
        {
            Loaded += async (_, _) =>
            {
                if (!demo) await RefreshAsync();
                await Dispatcher.InvokeAsync(() => { UpdateLayout(); Capture(screenshotPath); }, DispatcherPriority.ApplicationIdle);
                await ExitAsync();
            };
        }
        else if (!layoutCheck) { timer.Start(); resetTimer.Start(); _ = RefreshAsync(); }
    }

    private void LoadDemo(int count)
    {
        settings.Sources = [];
        for (var i = 0; i < Math.Clamp(count, 1, 12); i++)
        {
            var name = i switch { 0 => "Personal", 1 => "Research", 2 => "Backup", _ => $"Account {i + 1}" };
            var source = new AccountSource { Id = $"demo-{i}", Name = name, ShortName = i switch { 0 => "My", 1 => "Lab", 2 => "Alt", _ => $"A{i + 1}" }, Kind = i == 1 ? "ssh" : "local", SshHost = i == 1 ? "research-server" : null };
            settings.Sources.Add(source);
            snapshots[source.Id] = DemoSnapshot(source.Id, $"{name.Replace(" ", "").ToLowerInvariant()}@example.com", $"account-{i}", 33 + i * 9 % 60, 25 + i * 12 % 70, 487_000_000 / (i + 1), 33_065_000_000 / (i + 1));
            healthy.Add(source.Id);
        }
    }
    private static AccountSnapshot DemoSnapshot(string id, string email, string accountId, double weekly, double hourly, long latest, long lifetimeTokens) => new()
    {
        SourceId = id, Email = email, AccountId = accountId, Plan = "pro", AuthType = "chatgpt", UpdatedAt = DateTimeOffset.UtcNow,
        LifetimeTokens = lifetimeTokens, ResetCredits = 2, OrdinaryUsageAllowed = true,
        ResetCreditDetails = [new("demo-credit-a", "codexRateLimits", "available", DateTimeOffset.UtcNow.AddDays(-28), DateTimeOffset.UtcNow.AddHours(18), "초기화권"),
            new("demo-credit-b", "codexRateLimits", "available", DateTimeOffset.UtcNow.AddDays(-12), DateTimeOffset.UtcNow.AddDays(18), "초기화권")],
        Windows = [new("codex", "Codex", hourly, 300, DateTimeOffset.UtcNow.AddHours(3)), new("codex", "Codex", weekly, 10080, DateTimeOffset.UtcNow.AddDays(5))],
        Daily = Enumerable.Range(0, 7).Select(i => new DailyTokens(DateTime.UtcNow.Date.AddDays(i - 6).ToString("yyyy-MM-dd"), latest * (i + 2) / 8)).ToList()
    };

    private async Task RefreshAsync()
    {
        if (refreshing || exiting || demo) return;
        refreshing = true;
        FooterText.Text = "조회 중…";
        try
        {
            var publicTask = RefreshPublicResetsAsync();
            var enabled = settings.Sources.Where(x => x.Enabled).ToArray();
            using var parallelism = new SemaphoreSlim(3);
            var tasks = enabled.Select(async source =>
            {
                await parallelism.WaitAsync(lifetime.Token);
                try
                {
                    if (!connections.TryGetValue(source.Id, out var connection)) connections[source.Id] = connection = new(source);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(35));
                    var snapshot = await connection.ReadAsync(timeout.Token);
                    var previous = healthy.Contains(source.Id) ? snapshots.GetValueOrDefault(source.Id) : null;
                    snapshots[source.Id] = snapshot;
                    healthy.Add(source.Id);
                    errors.Remove(source.Id);
                    CheckAlert(source, snapshot);
                    CheckResetNotices(source, previous, snapshot);
                }
                catch (OperationCanceledException) { healthy.Remove(source.Id); errors[source.Id] = "조회 시간 초과 · 연결 확인"; }
                catch (Exception error) { healthy.Remove(source.Id); errors[source.Id] = SafeError(error); }
                finally { parallelism.Release(); RenderCards(); }
            });
            await Task.WhenAll(tasks);
            await publicTask;
            if (screenshotPath is null)
                try { store.SaveCache(snapshots.Values.Where(x => settings.Sources.Any(s => s.Id == x.SourceId))); } catch (IOException) { FooterText.Text = "캐시 저장 실패 · 현재 조회는 정상"; }
        }
        catch (OperationCanceledException) { }
        finally
        {
            refreshing = false;
            if (!exiting) { RenderCards(); FooterText.Text = demo ? "예시 데이터" : $"{settings.RefreshSeconds}초마다 갱신 · {DateTime.Now:HH:mm}"; }
        }
    }

    private static string SafeError(Exception error) => error switch
    {
        System.ComponentModel.Win32Exception => "프로그램을 실행할 수 없습니다 · Codex/SSH 경로 확인",
        FileNotFoundException => "Codex 실행파일을 찾지 못했습니다 · 경로 지정 필요",
        ArgumentException => "연결 설정을 확인하세요 · SSH 별명/실행파일 경로",
        InvalidOperationException => "로그인이 필요하거나 연결 설정이 올바르지 않습니다",
        RpcException => "계정 조회 실패 · Codex 로그인/버전 확인",
        _ => "연결 종료 · SSH 연결/실행파일/로그인 확인"
    };

    private void CheckAlert(AccountSource source, AccountSnapshot snapshot)
    {
        if (!settings.AlertsEnabled || tray is null) return;
        foreach (var window in snapshot.Windows)
        {
            var key = $"{source.Id}|{window.Bucket}|{window.DurationMinutes}|{window.ResetsAt}";
            if (window.RemainingPercent > 10) { alertStates.Remove(key); continue; }
            if (alertStates.Add(key)) tray.ShowBalloonTip(7000, source.Name + " 사용 한도", $"{window.DurationLabel} {window.RemainingPercent:0}% 남음", Forms.ToolTipIcon.Warning);
        }
    }

    private void RenderCards()
    {
        if (exiting) return;
        Cards.Children.Clear();
        var sources = settings.Sources.Where(x => x.Enabled).ToArray();
        SummaryText.Text = demo ? $"{sources.Length}개 계정 · 예시 데이터" : $"{sources.Length}개 계정 · {healthy.Count(x => sources.Any(s => s.Id == x))}개 연결됨";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            snapshots.TryGetValue(source.Id, out var snapshot);
            var duplicate = snapshot?.IdentityKey is { } identity && !seen.Add(identity);
            Cards.Children.Add(BuildCard(source, snapshot, duplicate));
        }
        if (sources.Length == 0) Cards.Children.Add(Text("+ 계정을 눌러 로컬 또는 SSH 계정을 추가하세요.", 12, "#90919B"));
        RenderResetPanel();
        SizePanel();
        QueuePanelSize();
        if (tray is not null)
        {
            var tooltip = string.Join(" | ", sources.Take(3).Select(source => snapshots.TryGetValue(source.Id, out var data) && data.Windows.Count > 0
                ? $"{source.Name}: {data.Windows.Min(w => w.RemainingPercent):0}%" : $"{source.Name}: —"));
            tray.Text = ("Codex · " + tooltip)[..Math.Min(63, 8 + tooltip.Length)];
        }
        miniWidget?.Update(sources.Select(source =>
        {
            snapshots.TryGetValue(source.Id, out var data);
            return new MiniAccount(source.Name, data?.Windows.Count > 0 ? data.Windows.Min(x => x.RemainingPercent) : null,
                healthy.Contains(source.Id), data?.OrdinaryUsageAllowed == false, source.ShortName);
        }).ToArray());
    }

    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    private static TextBlock Text(string value, double size, string color, FontWeight? weight = null, Thickness? margin = null) => new()
    { Text = value, FontSize = size, Foreground = Brush(color), FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };

    private void AddClick(object sender, RoutedEventArgs e) => EditSource(null);
    private async void EditSource(AccountSource? source)
    {
        if (refreshing || demo) { MessageBox.Show(demo ? "데모 모드에서는 연결 설정을 저장하지 않습니다." : "조회가 완료된 뒤 연결을 편집할 수 있습니다.", Title); return; }
        if (source is null && settings.Sources.Count >= 50) { MessageBox.Show("최대 50개 연결을 등록할 수 있습니다. 기존 연결을 정리한 뒤 추가하세요.", Title); return; }
        var editor = new SourceWindow(source, store.DirectoryPath) { Owner = this };
        if (editor.ShowDialog() != true) return;
        if (source is not null)
        {
            settings.Sources.Remove(source); snapshots.Remove(source.Id); errors.Remove(source.Id); healthy.Remove(source.Id);
            if (connections.Remove(source.Id, out var old)) await old.DisposeAsync();
        }
        if (!editor.Deleted && editor.Source is { } changed) settings.Sources.Add(changed);
        store.Save(settings); RenderCards(); await RefreshAsync();
    }
    private async void RefreshClick(object sender, RoutedEventArgs e)
    {
        if (resetState.Outlook.CheckedAt is not { } checkedAt || DateTimeOffset.UtcNow - checkedAt >= TimeSpan.FromMinutes(1)) nextPublicCheck = DateTimeOffset.MinValue;
        await RefreshAsync();
    }
    private void HideClick(object sender, RoutedEventArgs e) => Hide();
    private void HelpClick(object sender, RoutedEventArgs e) => new HelpWindow { Owner = this }.ShowDialog();
    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (!IsVisible) ShowDetails();
        var previouslyWatched = settings.WatchPublicResets;
        var editor = new PreferencesWindow(settings) { Owner = this };
        if (editor.ShowDialog() != true || demo) return;
        Topmost = settings.AlwaysOnTop;
        timer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds);
        store.Save(settings);
        ApplyStartup();
        miniWidget?.Configure(settings.ShowMiniWidget, settings.DockMiniWidget);
        if (settings.WatchPublicResets && !previouslyWatched) nextPublicCheck = DateTimeOffset.MinValue;
        RenderCards();
        _ = RefreshAsync();
    }
    private void ApplyStartup()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (settings.StartWithWindows && Environment.ProcessPath is { } path) key.SetValue("CodexAccountMonitor", "\"" + path + "\" --minimized");
        else key.DeleteValue("CodexAccountMonitor", throwOnMissingValue: false);
    }
    private void ToggleWindow() { if (IsVisible) Hide(); else ShowDetails(); }
    internal void ShowDetails()
    {
        WindowState = WindowState.Normal;
        SizePanel();
        if (miniWidget is { Handle: not 0 } widget)
        {
            var pixelBounds = widget.ScreenBounds;
            var dpi = GetDpiForWidget(widget.Handle) / 96d;
            var area = SystemParameters.WorkArea;
            Left = Math.Clamp(pixelBounds.Left / dpi, area.Left, Math.Max(area.Left, area.Right - Width));
            Top = Math.Max(area.Top + 4, pixelBounds.Top / dpi - Height - 6);
        }
        Show(); Activate();
    }
    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")] private static extern uint GetDpiForWidget(nint window);
    private void OnClosing(object? sender, CancelEventArgs e) { if (exiting) return; e.Cancel = true; Hide(); }
    internal async Task ExitAsync(int exitCode = 0)
    {
        if (exiting) return;
        exiting = true; timer.Stop(); resetTimer.Stop(); noticeTimer.Stop(); lifetime.Cancel();
        while (refreshing || publicRefreshing) await Task.Delay(50);
        foreach (var connection in connections.Values) await connection.DisposeAsync();
        resetFeed.Dispose();
        miniWidget?.Dispose(); tray?.Dispose(); lifetime.Dispose(); Close(); Application.Current.Shutdown(exitCode);
    }
    internal async Task<bool> RunWidgetCheckAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new Dictionary<string, bool>();
        var panelLayouts = new Dictionary<string, object>();
        if (miniWidget is null) { File.WriteAllText(System.IO.Path.Combine(directory, "widget-check.json"), "{\"nativeWidgetAvailable\":false}"); return false; }
        while (refreshing) await Task.Delay(50);
        var widget = miniWidget;
        Hide();
        widget.Configure(true, true);
        await Task.Delay(700);
        var docked = widget.Inspect();
        checks["nativeWidgetVisible"] = widget.CaptureVisible(System.IO.Path.Combine(directory, "mini-taskbar.png"));
        checks["nativeWidgetReceivesPointer"] = widget.HitTestCenter();
        widget.SendTestClick(rightButton: true); await Task.Delay(300);
        checks["miniRightClickOpensMenu"] = tray?.ContextMenuStrip?.Visible == true;
        tray?.ContextMenuStrip?.Close();
        widget.SendTestClick(); await Task.Delay(500);
        checks["miniClickOpensDetails"] = IsVisible;
        checks["allAccountsListed"] = Cards.Children.Count == settings.Sources.Count(x => x.Enabled);
        checks["threeAccountsAndOverflowBounded"] = widget.VisibleAccounts == Math.Min(3, settings.Sources.Count(x => x.Enabled)) && widget.OverflowAccounts == Math.Max(0, settings.Sources.Count(x => x.Enabled) - 3) && widget.ScreenBounds.Width / (GetDpiForWidget(widget.Handle) / 96d) <= 340.5;
        UpdateLayout(); Capture(System.IO.Path.Combine(directory, "details.png"));
        if (ResetPanel.Children.Count > 0)
        {
            CaptureForecastCard(System.IO.Path.Combine(directory, "forecast-card.png"));
            var forecastDetails = (Expander)((Border)ResetPanel.Children[0]).Child;
            forecastDetails.IsExpanded = true;
            await SettlePanelLayoutAsync();
            CaptureForecastCard(System.IO.Path.Combine(directory, "forecast-reasons.png"));
            forecastDetails.IsExpanded = false;
            await SettlePanelLayoutAsync();
        }
        checks["collapsedPanelFitsOrScreenLimited"] = ScrollOnlyAtHeightLimit();
        panelLayouts["collapsed"] = PanelLayoutState();
        if (Cards.Children.Count > 0 && ((Border)Cards.Children[0]).Child is Expander first)
        {
            first.IsExpanded = true;
            await SettlePanelLayoutAsync();
            Capture(System.IO.Path.Combine(directory, "account-expanded.png"));
            RenderCards();
            checks["expandedAccountSurvivesRefresh"] = ((Border)Cards.Children[0]).Child is Expander { IsExpanded: true };
            ((Expander)((Border)Cards.Children[0]).Child).IsExpanded = false;
        }
        foreach (var card in Cards.Children.OfType<Border>()) if (card.Child is Expander expander) expander.IsExpanded = true;
        await SettlePanelLayoutAsync();
        checks["allExpandedPanelFitsOrScreenLimited"] = ScrollOnlyAtHeightLimit();
        panelLayouts["allExpanded"] = PanelLayoutState();
        Capture(System.IO.Path.Combine(directory, "all-expanded.png"));
        foreach (var card in Cards.Children.OfType<Border>()) if (card.Child is Expander expander) expander.IsExpanded = false;
        HideClick(this, new RoutedEventArgs()); await Task.Delay(200);
        checks["collapsePreservesMiniWidget"] = !IsVisible && widget.Handle != 0;
        if (widget.OverflowAccounts > 0)
        {
            checks["overflowBadgeReceivesPointer"] = widget.HitTestOverflow();
            widget.SendTestClick(overflowBadge: true); await Task.Delay(300);
            checks["overflowBadgeOpensAllAccounts"] = IsVisible && Cards.Children.Count == settings.Sources.Count(x => x.Enabled);
            Hide();
        }
        widget.SendTestClick(); await Task.Delay(300);
        widget.SendTestClick(); await Task.Delay(300);
        checks["miniClickTogglesDetails"] = !IsVisible;
        widget.Configure(true, false); await Task.Delay(300);
        checks["overlayModeVisible"] = !widget.IsDocked && widget.CaptureVisible(System.IO.Path.Combine(directory, "mini-overlay.png"));
        widget.Configure(false, true);
        checks["miniCanBeDisabled"] = widget.Handle == 0;
        widget.Configure(true, true); await Task.Delay(300);
        checks["miniCanBeRestored"] = widget.Handle != 0;
        ShowDetails(); Close(); await Task.Delay(200);
        checks["closeCollapsesWithoutExiting"] = !IsVisible && widget.Handle != 0;
        if (!demo && settings.Sources.LastOrDefault(x => x.Enabled && x.Kind == "ssh") is { } source)
        {
            var editor = new SourceWindow(source, store.DirectoryPath);
            checks["connectionCheckReadsSelectedAccount"] = await editor.CheckConnectionAsync();
            checks["connectionCheckShowsAccountIdentity"] = snapshots.TryGetValue(source.Id, out var account) && editor.StatusText.Contains(MetricFormatting.MaskEmail(account.Email));
            editor.SaveSnapshot(System.IO.Path.Combine(directory, "connection-checked.png"));
            editor.Close();
            var invalid = new SourceWindow(new AccountSource { Kind = "ssh", SshHost = null }, store.DirectoryPath);
            checks["emptySshTargetShowsGuidance"] = !await invalid.CheckConnectionAsync() && invalid.StatusText.Contains("SSH");
            invalid.Close();
        }
        File.WriteAllText(System.IO.Path.Combine(directory, "widget-check.json"), JsonSerializer.Serialize(new { checks, docked, panelLayouts }, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Values.All(x => x);
    }
    private void Capture(string path)
    {
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var stream = File.Create(path); encoder.Save(stream);
        var miniAccounts = settings.Sources.Where(x => x.Enabled).Select(source =>
        {
            snapshots.TryGetValue(source.Id, out var data);
            return new MiniAccount(source.Name, data?.Windows.Count > 0 ? data.Windows.Min(x => x.RemainingPercent) : null,
                healthy.Contains(source.Id), data?.OrdinaryUsageAllowed == false, source.ShortName);
        }).ToArray();
        TaskbarWidget.SavePreview(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "mini-preview.png"), miniAccounts);
        TaskbarWidget.SavePreview(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "mini-preview-dark.png"), miniAccounts, lightTheme: false);
        if (demo) SourceWindow.SavePreview(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "add-ssh-preview.png"), store.DirectoryPath);
    }
    private static Drawing.Icon CreateIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        if (resource is not null) { using var stream = resource.Stream; return new Drawing.Icon(stream); }
        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }
}
