using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexAccountMonitor.Core;

namespace CodexAccountMonitor;

public partial class MainWindow
{
    internal async Task<bool> RunLayoutCheckAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new Dictionary<string, bool>();
        var layouts = new Dictionary<string, object>();
        ShowDetails();
        await SettlePanelLayoutAsync();
        var collapsedHeight = ActualHeight;
        checks["collapsedRowsFitOrReachScreenLimit"] = ScrollOnlyAtHeightLimit();
        layouts["collapsed"] = PanelLayoutState();
        var forecastCard = ForecastCard!;
        var visibleHeader = (StackPanel)forecastCard.Child;
        checks["twoHorizonProbabilitiesVisibleWithoutOpeningLinks"] = RenderedProbabilityAvailable &&
            VisualDescendants<TextBlock>(visibleHeader).Count(t => t.Text.EndsWith('%')) == 2;
        checks["defaultForecastCardIsCompact"] = visibleHeader.ActualHeight <= 260 &&
            !VisualDescendants<TextBlock>(visibleHeader).Any(t => t.Text == RenderedForecast?.Explanation);
        checks["primaryAccountHasVisibleBadgeAndLeadsWidget"] = ((Border)Cards.Children[0]).Tag is "primary-account" &&
            VisualDescendants<TextBlock>((Border)Cards.Children[0]).Any(t => t.Text == "PRIMARY") && MiniAccounts()[0].Primary;
        Capture(System.IO.Path.Combine(directory, "forecast-visible.png"));
        CapturePanelElement((Border)Cards.Children[0], System.IO.Path.Combine(directory, "primary-card.png"));
        CaptureForecastCard(System.IO.Path.Combine(directory, "forecast-card.png"));

        var first = (Expander)((Border)Cards.Children[0]).Child;
        var summary = VisualDescendants<Border>((Grid)first.Header).Single(b => Equals(b.Tag, "weekly-budget-summary"));
        var summaryText = VisualDescendants<TextBlock>(summary).Select(t => t.Text).ToArray();
        checks["weeklyBudgetVisibleInCollapsedAccount"] = !first.IsExpanded && summaryText.Contains("주간 남음") &&
            summaryText.Contains("40%") && summaryText.Contains("4일") && summaryText.Contains("10%") && summaryText.Contains("빠른 편");
        var originalBottom = Top + ActualHeight;
        first.IsExpanded = true;
        await SettlePanelLayoutAsync();
        checks["expandingContentGrowsWindow"] = ActualHeight > collapsedHeight || ActualHeight >= PanelHeightLimit - 1.5;
        var area = SystemParameters.WorkArea;
        var expectedTop = Math.Clamp(originalBottom - ActualHeight, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - ActualHeight - 8));
        checks["expansionKeepsBottomAnchorUnlessScreenClamped"] = Math.Abs(Top - expectedTop) <= 1.5;
        checks["expandedContentFitsOrReachesScreenLimit"] = ScrollOnlyAtHeightLimit();
        layouts["firstExpanded"] = PanelLayoutState();
        checks["expandedWeeklyBudgetComparesQuotaWithTime"] = VisualDescendants<Border>(first).Count(b => Equals(b.Tag, "weekly-budget-bar")) == 2 &&
            VisualDescendants<TextBlock>(first).Any(t => t.Text == "주간 한도 사용") && VisualDescendants<TextBlock>(first).Any(t => t.Text == "지난 기간");
        CapturePanelElement(first, System.IO.Path.Combine(directory, "weekly-account.png"));
        var dailyChart = VisualDescendants<Border>(first).Single(b => Equals(b.Tag, "daily-token-chart"));
        checks["dailyTokensShowSevenDatesAndVisibleAmounts"] = VisualDescendants<TextBlock>(dailyChart).Count(t => Equals(t.Tag, "daily-token-date")) == 7 &&
            VisualDescendants<TextBlock>(dailyChart).Count(t => Equals(t.Tag, "daily-token-value")) == 7 &&
            VisualDescendants<Border>(dailyChart).Count(b => Equals(b.Tag, "daily-token-bar")) == 6;
        CapturePanelElement(dailyChart, System.IO.Path.Combine(directory, "daily-token-chart.png"));

        var originalDaily = snapshots[settings.Sources[0].Id].Daily;
        var day = DailyTokenHistory.Build(originalDaily)[^1].Date;
        snapshots[settings.Sources[0].Id].Daily = [new(day.AddDays(-30).ToString("yyyy-MM-dd"), 600_000_000),
            new(day.AddDays(-3).ToString("yyyy-MM-dd"), 0), new(day.ToString("yyyy-MM-dd"), 16_502_782)];
        RenderCards(); await SettlePanelLayoutAsync();
        dailyChart = VisualDescendants<Border>((Border)Cards.Children[0]).Single(b => Equals(b.Tag, "daily-token-chart"));
        var dailyAmounts = VisualDescendants<TextBlock>(dailyChart).Where(t => Equals(t.Tag, "daily-token-value")).Select(t => t.Text).ToArray();
        checks["sparseDailyUiDistinguishesMissingFromZero"] = dailyAmounts.Count(v => v == "—") == 5 && dailyAmounts.Contains("0") &&
            dailyAmounts.Contains("1650만") && !dailyAmounts.Contains("6억") && VisualDescendants<Border>(dailyChart).Count(b => Equals(b.Tag, "daily-token-bar")) == 1;
        CapturePanelElement(dailyChart, System.IO.Path.Combine(directory, "daily-token-sparse.png"));
        snapshots[settings.Sources[0].Id].Daily = originalDaily;
        RenderCards(); await SettlePanelLayoutAsync();
        first = (Expander)((Border)Cards.Children[0]).Child;
        var previewBudget = WeeklyUsageBudget.Build(snapshots[settings.Sources[0].Id], true, DateTimeOffset.UtcNow).Single();
        var preview = new StackPanel { Width = summary.ActualWidth, Background = Brush("#FFFFFF"), Margin = new Thickness(0) };
        preview.Children.Add(Text("Personal · 주간 배분", 14, Ink, FontWeights.SemiBold, new Thickness(10, 10, 10, 0)));
        preview.Children.Add(BuildWeeklyBudgetSummary(previewBudget, false));
        preview.Children.Add(BuildWeeklyBudgetDetail(previewBudget, false));
        preview.Measure(new Size(preview.Width, double.PositiveInfinity));
        preview.Arrange(new Rect(new Point(), preview.DesiredSize)); preview.UpdateLayout();
        CapturePanelElement(preview, System.IO.Path.Combine(directory, "weekly-budget.png"));

        var expandedHeight = ActualHeight;
        Hide(); Height = MinHeight; ShowDetails();
        await SettlePanelLayoutAsync();
        checks["reopeningRepairsManuallyShortenedWindow"] = Math.Abs(ActualHeight - expandedHeight) <= 1.5 && ScrollOnlyAtHeightLimit();

        var source = settings.Sources[0];
        healthy.Remove(source.Id); RenderCards(); await SettlePanelLayoutAsync();
        var staleAccount = (Expander)((Border)Cards.Children[0]).Child;
        checks["failedRefreshHidesDailyBudgetAndPaceBars"] = VisualDescendants<TextBlock>((Grid)staleAccount.Header).Any(t => t.Text == "주간 배분 · 확인 필요") &&
            !VisualDescendants<Border>(staleAccount).Any(b => Equals(b.Tag, "weekly-budget-bar"));
        healthy.Add(source.Id); RenderCards(); await SettlePanelLayoutAsync();
        var originalNote = snapshots[source.Id].UsageNote;
        snapshots[source.Id].UsageNote = string.Concat(Enumerable.Repeat("조회 안내가 길어지면 줄바꿈된 내용도 창 높이에 포함됩니다. ", 6));
        RenderCards();
        await SettlePanelLayoutAsync();
        checks["refreshWithSameAccountCountResizesContent"] = ActualHeight > expandedHeight || ActualHeight >= PanelHeightLimit - 1.5;
        checks["refreshPreservesExpandedRow"] = ((Expander)((Border)Cards.Children[0]).Child).IsExpanded;
        var wideExtent = AccountScroll.ExtentHeight;
        Width = 360;
        await SettlePanelLayoutAsync();
        checks["widthChangeIncludesWrappedText"] = AccountScroll.ExtentHeight >= wideExtent && ScrollOnlyAtHeightLimit();
        checks["primaryBadgeFitsNarrowAccountHeader"] = VisualDescendants<TextBlock>((Border)Cards.Children[0]).Single(t => t.Text == "PRIMARY").ActualWidth > 20;
        dailyChart = VisualDescendants<Border>((Border)Cards.Children[0]).Single(b => Equals(b.Tag, "daily-token-chart"));
        checks["dailyAmountsRemainReadableInNarrowPanel"] = VisualDescendants<Viewbox>(dailyChart).Where(v => v.Child is TextBlock t && Equals(t.Tag, "daily-token-value"))
            .All(v => v.ActualWidth > 0 && v.ActualHeight >= 10) && VisualDescendants<TextBlock>(dailyChart).Count(t => Equals(t.Tag, "daily-token-date")) == 7;
        CapturePanelElement(dailyChart, System.IO.Path.Combine(directory, "daily-token-narrow.png"));
        layouts["narrowWithNote"] = PanelLayoutState();

        snapshots[source.Id].UsageNote = originalNote;
        Width = 410; RenderCards();
        foreach (Border card in Cards.Children) ((Expander)card.Child).IsExpanded = false;
        await SettlePanelLayoutAsync();
        checks["collapseShrinksWindow"] = Math.Abs(ActualHeight - collapsedHeight) <= 1.5;
        foreach (Border card in Cards.Children) ((Expander)card.Child).IsExpanded = true;
        await SettlePanelLayoutAsync();
        checks["allExpandedRowsFitOrReachScreenLimit"] = ScrollOnlyAtHeightLimit();
        checks["windowRemainsInsideWorkArea"] = Top >= SystemParameters.WorkArea.Top && Top + ActualHeight <= SystemParameters.WorkArea.Bottom + 1;
        layouts["allExpanded"] = PanelLayoutState();
        Capture(System.IO.Path.Combine(directory, "all-expanded.png"));

        foreach (Border card in Cards.Children) ((Expander)card.Child).IsExpanded = false;
        checks["predictionCardsFitOrReachScreenLimit"] = ScrollOnlyAtHeightLimit();
        RenderCards();
        await SettlePanelLayoutAsync();
        var localEvidence = (StackPanel)ForecastCard!.Child;
        checks["predictionShowsTimeAndSourceLinksWithoutParagraphs"] = VisualDescendants<TextBlock>(localEvidence).Any(t => t.FontSize >= 18 && t.Text.Contains("KST")) &&
            VisualDescendants<Button>(localEvidence).Any(b => (string)b.Content == "투표 ↗") &&
            !VisualDescendants<Expander>(ResetPanel).Any() && !VisualDescendants<TextBlock>(localEvidence).Any(t => t.Text.Contains("습니다"));
        var originalCommunity = resetState.Outlook.CommunityForecast;
        resetState.Outlook.CommunityForecast = originalCommunity! with { FetchedAt = DateTimeOffset.UtcNow.AddHours(-1) };
        RenderCards(); await SettlePanelLayoutAsync();
        var staleHeader = (StackPanel)ForecastCard!.Child;
        checks["staleProbabilitiesAreSuppressedInUi"] = !RenderedProbabilityAvailable && !VisualDescendants<TextBlock>(staleHeader).Any(t => t.Text.EndsWith('%'));
        resetState.Outlook.CommunityForecast = originalCommunity;
        var originalSignals = resetState.Outlook.Signals;
        resetState.Outlook.Signals = originalSignals.Select(s => s.Poll is not null ? s with { Poll = s.Poll with { Choices = [new("good day", 76), new("needs a reset", 24)] } } : s).ToList();
        RenderCards(); await SettlePanelLayoutAsync();
        checks["reversedPollUpdatesForecastInUi"] = RenderedForecast?.State == ResetForecastState.Weak;
        var originalCompletions = resetState.Outlook.Completions;
        resetState.Outlook.Signals = originalSignals;
        var resetAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        resetState.Outlook.Completions = [new("fictional-reset-completed", ResetSignalKind.UsageReset, resetAt)];
        resetState.Outlook.CommunityForecast = originalCommunity! with { UpdatedAt = resetAt.AddMinutes(-1) };
        RenderCards(); await SettlePanelLayoutAsync();
        checks["completedResetExpiresOldTimeAndProbabilities"] = RenderedForecast?.State == ResetForecastState.Weak && !RenderedProbabilityAvailable &&
            VisualDescendants<TextBlock>(ForecastCard!).Any(t => t.Text == "다음 리셋 시각 미정");
        CaptureForecastCard(System.IO.Path.Combine(directory, "completed-reset.png"));
        resetState.Outlook.CommunityForecast = originalCommunity;
        RenderCards(); await SettlePanelLayoutAsync();
        checks["newProbabilitySnapshotAfterResetIsVisible"] = RenderedProbabilityAvailable;
        resetState.Outlook.Completions = originalCompletions;
        var creditNow = DateTimeOffset.UtcNow;
        var creditReply = new ResetPost("0000000000000000005", "thsottiaux", "Will be available by EOD PST.", creditNow.AddMinutes(-10),
            new("0000000000000000004", "thsottiaux", "Loading a banked reset for paid Codex accounts. (fictional example)"),
            Schedule: new(ResetSignalKind.CreditGrant, creditNow.AddHours(6)));
        resetState.Outlook.Signals = ResetJudgment.Evaluate([creditReply], [], creditNow);
        RenderCards(); await SettlePanelLayoutAsync();
        var creditHeader = (StackPanel)CreditNewsCard!.Child;
        checks["creditDeadlineAndSeparateQuotaProbabilitiesVisible"] = RenderedCreditNews?.State == CreditGrantState.Distributing &&
            RenderedForecast?.Kind == ResetSignalKind.UsageReset && ResetPanel.Children.Count == 2 &&
            VisualDescendants<TextBlock>(creditHeader).Any(t => t.Text.Contains("까지") && t.FontSize >= 18) &&
            !VisualDescendants<TextBlock>(creditHeader).Any(t => t.Text.EndsWith('%'));
        checks["creditAnnouncementRemainsCompactAndFits"] = creditHeader.ActualHeight <= 235 && ScrollOnlyAtHeightLimit();
        CaptureForecastCard(System.IO.Path.Combine(directory, "credit-announcement.png"));
        var receiptSources = settings.Sources.Take(2).ToArray();
        var originalCredits = receiptSources.Select(s => (Snapshot: snapshots[s.Id], Details: snapshots[s.Id].ResetCreditDetails, Count: snapshots[s.Id].ResetCredits)).ToArray();
        foreach (var saved in originalCredits)
        {
            saved.Snapshot.ResetCreditDetails = (saved.Details ?? []).Append(new("fictional-new-credit", "codexRateLimits", "available", creditNow.AddMinutes(-2), creditNow.AddDays(30), null)).ToList();
            saved.Snapshot.ResetCredits = 3;
        }
        RenderCards(); await SettlePanelLayoutAsync();
        creditHeader = (StackPanel)CreditNewsCard!.Child;
        checks["accountGrantConfirmationReachesSeparateNewsCard"] = RenderedCreditNews is { State: CreditGrantState.AccountConfirmed, ConfirmedAccounts: 2, CreditsPerAccount: 1 } &&
            VisualDescendants<TextBlock>(creditHeader).Any(t => t.Text.Contains("+1장"));
        CaptureForecastCard(System.IO.Path.Combine(directory, "credit-news-and-forecast.png"));
        checks["receivedCreditExpiresFutureDeadline"] = !VisualDescendants<TextBlock>(creditHeader).Any(t => t.Text.Contains("까지")) &&
            VisualDescendants<Button>(creditHeader).Any(b => (string)b.Content == "Tibo 공지 ↗");
        RenderCards(); await SettlePanelLayoutAsync();
        checks["compactReceiptSurvivesRefreshAndFits"] = RenderedCreditNews?.State == CreditGrantState.AccountConfirmed && ScrollOnlyAtHeightLimit();
        CapturePanelElement(CreditNewsCard!, System.IO.Path.Combine(directory, "credit-news-card.png"));
        foreach (var saved in originalCredits) { saved.Snapshot.ResetCreditDetails = saved.Details; saved.Snapshot.ResetCredits = saved.Count; }
        resetState.Outlook.Signals = originalSignals;
        RenderCards(); await SettlePanelLayoutAsync();
        Capture(System.IO.Path.Combine(directory, "reset-outlook.png"));
        ((Expander)((Border)Cards.Children[0]).Child).IsExpanded = true;
        await SettlePanelLayoutAsync();
        Capture(System.IO.Path.Combine(directory, "reset-and-credit.png"));
        foreach (Border card in Cards.Children) ((Expander)card.Child).IsExpanded = true;

        var originalIdentity = desktopIdentity;
        var alternative = settings.Sources[1];
        desktopIdentity = snapshots[alternative.Id]; RenderCards(); await SettlePanelLayoutAsync();
        checks["desktopSwitchMovesPrimaryAndPreservesExpansion"] = DisplayOrder().Sources[0].Id == alternative.Id && MiniAccounts()[0].Primary &&
            DisplayOrder().Sources.Zip(Cards.Children.OfType<Border>()).All(pair => ((Expander)pair.Second.Child).IsExpanded);
        Capture(System.IO.Path.Combine(directory, "primary-switched.png"));
        desktopIdentity = null; RenderCards(); await SettlePanelLayoutAsync();
        checks["unknownDesktopIdentityRemovesOldPrimary"] = !MiniAccounts().Any(a => a.Primary) &&
            !Cards.Children.OfType<Border>().Any(b => b.Tag is "primary-account");
        desktopIdentity = originalIdentity; RenderCards(); await SettlePanelLayoutAsync();

        // A short viewport exercises overflow even on a large development display.
        MaxHeight = Math.Max(MinHeight, Math.Min(460, SystemParameters.WorkArea.Height - 16));
        SizePanel();
        await SettlePanelLayoutAsync();
        checks["screenLimitedContentUsesScroll"] = AccountScroll.ScrollableHeight > 1 && Math.Abs(ActualHeight - MaxHeight) <= 1.5;
        layouts["limitedHeight"] = PanelLayoutState();
        Capture(System.IO.Path.Combine(directory, "limited-height.png"));
        File.WriteAllText(System.IO.Path.Combine(directory, "layout-check.json"),
            JsonSerializer.Serialize(new { checks, layouts }, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Values.All(x => x);
    }

    private async Task SettlePanelLayoutAsync() => await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in VisualDescendants<T>(child)) yield return nested;
        }
    }
    private void CaptureForecastCard(string path)
    {
        CapturePanelElement(ResetPanel, path);
    }
    private static void CapturePanelElement(FrameworkElement element, string path)
    {
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private bool ScrollOnlyAtHeightLimit() => AccountScroll.ScrollableHeight <= 1 || ActualHeight >= PanelHeightLimit - 1.5;
    private object PanelLayoutState() => new
    {
        width = ActualWidth, height = ActualHeight, top = Top,
        contentHeight = AccountScroll.ExtentHeight, viewportHeight = AccountScroll.ViewportHeight,
        scrollableHeight = AccountScroll.ScrollableHeight, heightLimit = PanelHeightLimit
    };
}
