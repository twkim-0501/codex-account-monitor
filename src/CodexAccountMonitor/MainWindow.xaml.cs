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
    public bool StartsCollapsed => screenshotPath is null && settings.ShowMiniWidget;

    public MainWindow(bool demo, string? screenshotPath, string? customSettings)
    {
        InitializeComponent();
        this.demo = demo;
        this.screenshotPath = screenshotPath;
        if (demo) Height = 900;
        store = new(customSettings);
        try { settings = store.Load(); }
        catch (InvalidDataException error) { MessageBox.Show(error.Message, Title); settings = new(); }
        Topmost = settings.AlwaysOnTop;
        var area = SystemParameters.WorkArea;
        Height = Math.Min(Height, area.Height - 24);
        Left = area.Left + 12;
        Top = Math.Max(area.Top + 12, area.Bottom - Height - 12);
        if (demo) LoadDemo();
        else foreach (var snapshot in store.LoadCache()) snapshots[snapshot.SourceId] = snapshot;
        if (screenshotPath is null)
        {
            tray = new Forms.NotifyIcon { Icon = CreateIcon(), Text = "Codex Account Monitor", Visible = true };
            tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ToggleWindow); };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("열기 / 숨기기", null, (_, _) => Dispatcher.Invoke(ToggleWindow));
            menu.Items.Add("새로고침", null, (_, _) => Dispatcher.Invoke(async () => await RefreshAsync()));
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
        else { timer.Start(); _ = RefreshAsync(); }
    }

    private void LoadDemo()
    {
        settings.Sources = [new() { Id = "demo-local", Name = "Personal", Kind = "local" }, new() { Id = "demo-ssh", Name = "Research", Kind = "ssh", SshHost = "research-server" }];
        snapshots["demo-local"] = DemoSnapshot("demo-local", "personal@example.com", "account-a", 33, 54, 487_000_000, 33_065_000_000);
        snapshots["demo-ssh"] = DemoSnapshot("demo-ssh", "research@example.com", "account-b", 72, 40, 124_500_000, 12_800_000_000);
        foreach (var source in settings.Sources) healthy.Add(source.Id);
    }
    private static AccountSnapshot DemoSnapshot(string id, string email, string accountId, double weekly, double hourly, long latest, long lifetimeTokens) => new()
    {
        SourceId = id, Email = email, AccountId = accountId, Plan = "pro", AuthType = "chatgpt", UpdatedAt = DateTimeOffset.UtcNow,
        LifetimeTokens = lifetimeTokens, ResetCredits = 2, OrdinaryUsageAllowed = true,
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
                    snapshots[source.Id] = snapshot;
                    healthy.Add(source.Id);
                    errors.Remove(source.Id);
                    CheckAlert(source, snapshot);
                }
                catch (OperationCanceledException) { healthy.Remove(source.Id); errors[source.Id] = "조회 시간 초과 · 연결 확인"; }
                catch (Exception error) { healthy.Remove(source.Id); errors[source.Id] = SafeError(error); }
                finally { parallelism.Release(); RenderCards(); }
            });
            await Task.WhenAll(tasks);
            if (screenshotPath is null)
                try { store.SaveCache(snapshots.Values.Where(x => settings.Sources.Any(s => s.Id == x.SourceId))); } catch (IOException) { FooterText.Text = "캐시 저장 실패 · 현재 조회는 정상"; }
        }
        catch (OperationCanceledException) { }
        finally
        {
            refreshing = false;
            if (!exiting) { RenderCards(); FooterText.Text = demo ? "DEMO · 예시 데이터" : $"{settings.RefreshSeconds}초 갱신 · {miniWidget?.Status ?? "알림 영역"}"; }
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
        SummaryText.Text = demo ? "예시 데이터 · 로컬과 원격 계정을 한 화면에" : $"연결 {healthy.Count(x => sources.Any(s => s.Id == x))}/{sources.Length} · 계정별 한도와 토큰";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            snapshots.TryGetValue(source.Id, out var snapshot);
            var duplicate = snapshot?.IdentityKey is { } identity && !seen.Add(identity);
            Cards.Children.Add(BuildCard(source, snapshot, duplicate));
        }
        if (sources.Length == 0) Cards.Children.Add(Text("+ 연결을 눌러 로컬 또는 SSH 계정을 추가하세요.", 13, "#93A4B8"));
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
                healthy.Contains(source.Id), data?.OrdinaryUsageAllowed == false);
        }).ToArray());
    }

    private Border BuildCard(AccountSource source, AccountSnapshot? data, bool duplicate)
    {
        var stack = new StackPanel();
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(Text(source.Name, 15, "#EDF4FA", FontWeights.SemiBold));
        var identity = Text(MetricFormatting.MaskEmail(data?.Email), 11, "#93A4B8");
        identity.ToolTip = data?.Email;
        titles.Children.Add(identity);
        header.Children.Add(titles);
        var menu = new Button { Content = "···", Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Top, ToolTip = "연결 편집 / 삭제" };
        menu.Click += (_, _) => EditSource(source);
        Grid.SetColumn(menu, 1); header.Children.Add(menu); stack.Children.Add(header);
        stack.Children.Add(Text($"{(data?.Plan ?? "CODEX").ToUpperInvariant()}  ·  {source.Location}", 10, "#52DCC4", margin: new Thickness(0, 9, 0, 10)));
        if (errors.TryGetValue(source.Id, out var error)) stack.Children.Add(Text(error, 11, "#F5B86D", margin: new Thickness(0, 0, 0, 8)));
        else if (data is not null && !healthy.Contains(source.Id)) stack.Children.Add(Text("이전 조회값 · 최신 값 확인 중", 11, "#F5B86D"));
        if (duplicate) stack.Children.Add(Text("같은 계정의 다른 연결 · 중복 합산하지 않음", 10, "#A798F5"));
        if (data?.OrdinaryUsageAllowed == false) stack.Children.Add(Text("현재 일반 사용 제한 · 계정 상태 확인", 11, "#F5B86D"));
        if (data is null) stack.Children.Add(Text("사용량을 조회하고 있습니다…", 12, "#93A4B8", margin: new Thickness(0, 7, 0, 12)));
        else
        {
            foreach (var window in data.Windows)
            {
                var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
                var percent = Text($"{window.RemainingPercent:0}% 남음", 14, window.RemainingPercent <= 10 ? "#F5B86D" : "#52DCC4", FontWeights.SemiBold);
                DockPanel.SetDock(percent, Dock.Right); row.Children.Add(percent);
                var quotaLabel = window.DurationLabel + (data.Windows.Select(x => x.Bucket).Distinct().Count() > 1 ? " · " + window.Label : "");
                row.Children.Add(Text(quotaLabel, 12, "#EDF4FA")); stack.Children.Add(row);
                var track = new Grid();
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(window.RemainingPercent, GridUnitType.Star) });
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - window.RemainingPercent, GridUnitType.Star) });
                track.Children.Add(new Border { Background = Brush(window.RemainingPercent <= 10 ? "#F5B86D" : "#52DCC4"), CornerRadius = new CornerRadius(3) });
                stack.Children.Add(new Border { Height = 5, Background = Brush("#263341"), CornerRadius = new CornerRadius(3), Child = track });
                stack.Children.Add(Text(MetricFormatting.Reset(window, DateTimeOffset.Now), 10, "#93A4B8", margin: new Thickness(0, 5, 0, 8)));
            }
            if (data.Windows.Count == 0) stack.Children.Add(Text(data.LimitsNote ?? "사용 한도 미제공", 11, "#93A4B8"));
            stack.Children.Add(new Border { Height = 1, Background = Brush("#293343"), Margin = new Thickness(0, 5, 0, 10) });
            var latest = data.Daily?.LastOrDefault();
            stack.Children.Add(MetricRow(latest is null ? "일별 토큰" : $"일별 토큰 · {(latest.Date.Length >= 10 ? latest.Date[5..10] : latest.Date)}", MetricFormatting.Tokens(latest?.Tokens)));
            stack.Children.Add(MetricRow("누적 토큰", MetricFormatting.Tokens(data.LifetimeTokens)));
            if (data.UsageNote is not null) stack.Children.Add(Text(data.UsageNote, 10, "#93A4B8"));
            if (data.Daily is { Count: > 1 }) stack.Children.Add(Sparkline(data.Daily.TakeLast(14).ToArray()));
            var footer = $"갱신 {data.UpdatedAt.ToLocalTime():MM/dd HH:mm:ss}";
            if (data.ResetCredits is { } credits) footer += $"  ·  리셋 {credits}회";
            stack.Children.Add(Text(footer, 9, "#93A4B8", margin: new Thickness(0, 8, 0, 0)));
        }
        return new Border { Background = Brush("#141B25"), BorderBrush = Brush("#293343"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12), Child = stack };
    }

    private static Grid MetricRow(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.Children.Add(Text(label, 11, "#93A4B8"));
        var right = Text(value, 13, "#EDF4FA", FontWeights.SemiBold); right.HorizontalAlignment = HorizontalAlignment.Right; grid.Children.Add(right);
        return grid;
    }
    private FrameworkElement Sparkline(DailyTokens[] daily)
    {
        var canvas = new Canvas { Height = 35, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 9, 0, 0), ClipToBounds = true, ToolTip = "최근 일별 토큰 추이 · 서버가 제공한 날짜 기준" };
        var max = Math.Max(1, daily.Max(x => x.Tokens));
        var line = new Polyline { Stroke = Brush("#A798F5"), StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round };
        canvas.SizeChanged += (_, _) =>
        {
            line.Points.Clear();
            for (var i = 0; i < daily.Length; i++) line.Points.Add(new Point(i * Math.Max(1, canvas.ActualWidth) / (daily.Length - 1), 32 - Math.Clamp(daily[i].Tokens / (double)max, 0, 1) * 27));
        };
        canvas.Children.Add(line); return canvas;
    }
    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    private static TextBlock Text(string value, double size, string color, FontWeight? weight = null, Thickness? margin = null) => new()
    { Text = value, FontSize = size, Foreground = Brush(color), FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };

    private void AddClick(object sender, RoutedEventArgs e) => EditSource(null);
    private async void EditSource(AccountSource? source)
    {
        if (refreshing || demo) { MessageBox.Show(demo ? "데모 모드에서는 연결 설정을 저장하지 않습니다." : "조회가 완료된 뒤 연결을 편집할 수 있습니다.", Title); return; }
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
    private async void RefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void HideClick(object sender, RoutedEventArgs e) => Hide();
    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (!IsVisible) ShowDetails();
        var editor = new PreferencesWindow(settings) { Owner = this };
        if (editor.ShowDialog() != true || demo) return;
        Topmost = settings.AlwaysOnTop;
        timer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds);
        store.Save(settings);
        ApplyStartup();
        miniWidget?.Configure(settings.ShowMiniWidget, settings.DockMiniWidget);
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
        if (miniWidget is { Handle: not 0 } widget)
        {
            var pixelBounds = widget.ScreenBounds;
            var dpi = GetDpiForWidget(widget.Handle) / 96d;
            var area = SystemParameters.WorkArea;
            Left = Math.Clamp(pixelBounds.Left / dpi, area.Left, Math.Max(area.Left, area.Right - Width));
            Top = Math.Max(area.Top + 4, pixelBounds.Top / dpi - Height - 6);
        }
        Show(); WindowState = WindowState.Normal; Activate();
    }
    [DllImport("user32.dll", EntryPoint = "GetDpiForWindow")] private static extern uint GetDpiForWidget(nint window);
    private void OnClosing(object? sender, CancelEventArgs e) { if (exiting) return; e.Cancel = true; Hide(); }
    internal async Task ExitAsync(int exitCode = 0)
    {
        if (exiting) return;
        exiting = true; timer.Stop(); lifetime.Cancel();
        while (refreshing) await Task.Delay(50);
        foreach (var connection in connections.Values) await connection.DisposeAsync();
        miniWidget?.Dispose(); tray?.Dispose(); lifetime.Dispose(); Close(); Application.Current.Shutdown(exitCode);
    }
    internal async Task<bool> RunWidgetCheckAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new Dictionary<string, bool>();
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
        UpdateLayout(); Capture(System.IO.Path.Combine(directory, "details.png"));
        HideClick(this, new RoutedEventArgs()); await Task.Delay(200);
        checks["collapsePreservesMiniWidget"] = !IsVisible && widget.Handle != 0;
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
        File.WriteAllText(System.IO.Path.Combine(directory, "widget-check.json"), JsonSerializer.Serialize(new { checks, docked }, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Values.All(x => x);
    }
    private void Capture(string path)
    {
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var stream = File.Create(path); encoder.Save(stream);
        TaskbarWidget.SavePreview(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "mini-preview.png"), settings.Sources.Where(x => x.Enabled).Select(source =>
        {
            snapshots.TryGetValue(source.Id, out var data);
            return new MiniAccount(source.Name, data?.Windows.Count > 0 ? data.Windows.Min(x => x.RemainingPercent) : null,
                healthy.Contains(source.Id), data?.OrdinaryUsageAllowed == false);
        }).ToArray());
    }
    private static Drawing.Icon CreateIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        if (resource is not null) { using var stream = resource.Stream; return new Drawing.Icon(stream); }
        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }
}
