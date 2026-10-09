using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexAccountMonitor.Core;
using Forms = System.Windows.Forms;

namespace CodexAccountMonitor;

public sealed class ResetMonitorState
{
    public ResetOutlook Outlook { get; set; } = new();
    public Dictionary<string, DateTimeOffset> Delivered { get; set; } = [];
}

public partial class MainWindow
{
    private readonly ResetFeed resetFeed = new();
    private ResetMonitorState resetState = new();
    private DateTimeOffset nextPublicCheck;
    private bool publicRefreshing;
    private readonly Queue<MonitorNotice> noticeQueue = new();
    private readonly DispatcherTimer noticeTimer = new() { Interval = TimeSpan.FromSeconds(9) };
    private readonly DispatcherTimer resetTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    internal ResetForecast? RenderedForecast { get; private set; }

    private void InitializeResetMonitor()
    {
        if (demo)
        {
            var now = DateTimeOffset.UtcNow;
            var promise = new ResetSignal("0000000000000000002", ResetSignalKind.UsageReset, ResetSignalLevel.Conditional, now.AddDays(-2), now.AddDays(26), null,
                "개선 출시 또는 리셋", "Tibo가 매일 Codex를 개선하거나 리셋하겠다고 약속했습니다. 매일 리셋한다는 뜻은 아닙니다.", "조건부 약속 · 일별 시각 미정", "Each day, we'll ship an improvement or a full reset. (fictional example)");
            resetState.Outlook = new() { CheckedAt = now, CommunityForecast = new(68, 84, now, now, "fictional", "fictional example"), Note = "예시 데이터 · 실제 공지가 아닙니다", Signals =
                [new("0000000000000000001", ResetSignalKind.UsageReset, ResetSignalLevel.Hint, now.AddHours(-2), now.AddHours(46), null,
                    "리셋을 묻는 투표", "투표 선택지에 리셋이 있고, 진행 중인 조건부 약속과 연결됩니다. 실행은 미확정입니다.", "투표 진행 중 · 실행 시각 미정",
                    "Vote (fictional example)", "0000000000000000003", "좋은 업데이트 24표 · 리셋 필요 76표 · 투표 종료 · 예시", null,
                    new([new("good day", 24), new("needs a reset", 76)], now.AddHours(-1), true),
                    [new("0000000000000000003", "thsottiaux", "Roundup of Day 2: Codex improvements shipped. (fictional example)"), new(promise.Id, "thsottiaux", promise.Evidence)]), promise,
                    new("0000000000000000006", ResetSignalKind.CreditGrant, ResetSignalLevel.Announced, now.AddMinutes(-30), now.AddDays(1), now.AddHours(6),
                        "가상 초기화권 지급", "가상 예시: Tibo가 초기화권을 지급하겠다고 알린 상황입니다.", "가상 지급 시각 · " + ResetJudgment.KoreanTime(now.AddHours(6)),
                        "Loading a banked reset for paid Codex accounts. (fictional example)")] };
        }
        else resetState = store.LoadResetState();
        noticeTimer.Tick += (_, _) => ShowNextNotice();
        resetTimer.Tick += async (_, _) => { await RefreshPublicResetsAsync(); if (!exiting) RenderCards(); };
    }

    private async Task RefreshPublicResetsAsync()
    {
        if (demo || publicRefreshing || !settings.WatchPublicResets || DateTimeOffset.UtcNow < nextPublicCheck || exiting) return;
        publicRefreshing = true;
        // Public collection is bounded and independent of the account refresh interval.
        nextPublicCheck = DateTimeOffset.UtcNow.AddMinutes(10);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(50));
            var previous = resetState.Outlook;
            var current = await resetFeed.ReadAsync(DateTimeOffset.UtcNow, timeout.Token);
            if (current.PartialCoverage)
            {
                // An unavailable context source cannot revoke an existing ongoing promise.
                current.Signals = ResetJudgment.MergePartial(previous, current, DateTimeOffset.UtcNow);
                current.CurrentContext = current.CurrentContext.Concat((previous.CurrentContext ?? []).Where(c => current.CurrentContext.All(n => n.Id != c.Id)))
                    .Where(c => DateTimeOffset.UtcNow - c.At <= TimeSpan.FromHours(36)).OrderByDescending(c => c.At).Take(3).ToList();
            }
            foreach (var signal in current.Signals)
            {
                var notice = new MonitorNotice($"public:{signal.Id}:{signal.Level}", signal.LevelLabel + " · " + signal.KindLabel,
                    signal.Timing + "\n" + signal.Reason);
                if (previous.CheckedAt is null) RememberNotice(notice.Key);
                else if (settings.ResetAlertsEnabled && (signal.Level != ResetSignalLevel.Hint || settings.HintAlertsEnabled)) QueueNotice(notice);
            }
            resetState.Outlook = current;
            SaveResetState();
        }
        catch (Exception error) when (error is System.IO.InvalidDataException or OperationCanceledException or System.Text.Json.JsonException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            if (!exiting)
            {
                resetState.Outlook.PartialCoverage = true;
                resetState.Outlook.Note = "공개 소식을 확인하지 못해 이전 내용을 표시합니다. 10분 후에 다시 확인합니다.";
                SaveResetState();
            }
        }
        finally { publicRefreshing = false; if (!exiting) RenderCards(); }
    }

    private void CheckResetNotices(AccountSource source, AccountSnapshot? previous, AccountSnapshot current)
    {
        if (settings.ResetAlertsEnabled)
            foreach (var notice in ResetNotices.AccountChanges(source.Name, previous, current, DateTimeOffset.UtcNow)) QueueNotice(notice);
        if (settings.CreditExpiryAlertsEnabled)
            foreach (var notice in ResetNotices.CreditExpiry(source.Name, current, DateTimeOffset.UtcNow)) QueueNotice(notice);
        SaveResetState();
    }

    private bool RememberNotice(string key)
    {
        var now = DateTimeOffset.UtcNow;
        return NoticeLedger.Remember(resetState.Delivered, key, now);
    }
    private void QueueNotice(MonitorNotice notice)
    {
        if (tray is null || !RememberNotice(notice.Key)) return;
        noticeQueue.Enqueue(notice);
        if (!noticeTimer.IsEnabled) { ShowNextNotice(); noticeTimer.Start(); }
    }
    private void ShowNextNotice()
    {
        if (!noticeQueue.TryDequeue(out var notice)) { noticeTimer.Stop(); return; }
        tray?.ShowBalloonTip(7500, notice.Title, notice.Body, Forms.ToolTipIcon.Info);
    }
    private void SaveResetState()
    {
        if (demo || screenshotPath is not null) return;
        resetState.Outlook.Signals = ResetJudgment.Active(resetState.Outlook, DateTimeOffset.UtcNow);
        resetState.Outlook.CurrentContext = (resetState.Outlook.CurrentContext ?? []).Where(c => DateTimeOffset.UtcNow - c.At <= TimeSpan.FromHours(36)).Take(3).ToList();
        resetState.Delivered = resetState.Delivered.Where(x => DateTimeOffset.UtcNow - x.Value < TimeSpan.FromDays(33))
            .OrderByDescending(x => x.Value).Take(256).ToDictionary();
        try { store.SaveResetState(resetState); } catch (System.IO.IOException) { /* Live data remains usable. */ }
    }

    private void RenderResetPanel()
    {
        ResetPanel.Children.Clear();
        RenderedCreditNews = null; CreditNewsCard = null; ForecastCard = null;
        ResetPanel.Visibility = settings.WatchPublicResets ? Visibility.Visible : Visibility.Collapsed;
        if (!settings.WatchPublicResets) return;
        var now = DateTimeOffset.UtcNow;
        RenderedCreditNews = CreditGrantNewsBuilder.Build(resetState.Outlook, DisplayOrder().Sources
            .Select(s => new NamedAccountSnapshot(s.Name, snapshots.GetValueOrDefault(s.Id))), now);
        if (RenderedCreditNews is { } creditNews) ResetPanel.Children.Add(BuildCreditGrantCard(creditNews));
        var quotaOutlook = new ResetOutlook
        {
            CheckedAt = resetState.Outlook.CheckedAt, PartialCoverage = resetState.Outlook.PartialCoverage, Note = resetState.Outlook.Note,
            Signals = resetState.Outlook.Signals.Where(s => s.Kind == ResetSignalKind.UsageReset).ToList(),
            CurrentContext = resetState.Outlook.CurrentContext, Completions = resetState.Outlook.Completions,
            CommunityForecast = resetState.Outlook.CommunityForecast
        };
        var forecast = ResetForecasting.Build(quotaOutlook, now);
        RenderedForecast = forecast;
        ResetPanel.Children.Add(BuildForecastCard(forecast, now));
    }

    private static Button SourceLink(string label, string url)
    {
        var button = new Button { Content = label, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(2, 4, 2, 4), Margin = new Thickness(0, 4, 0, 0) };
        button.Click += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return button;
    }
}
