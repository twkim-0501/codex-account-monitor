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
        var forecastCard = ForecastExpander!;
        var visibleHeader = (StackPanel)forecastCard.Header;
        checks["twoHorizonProbabilitiesVisibleWithoutOpeningLinks"] = RenderedProbabilityAvailable &&
            VisualDescendants<TextBlock>(visibleHeader).Count(t => t.Text.EndsWith('%')) == 2 && !forecastCard.IsExpanded;
        checks["defaultForecastCardIsCompact"] = visibleHeader.ActualHeight <= 235 &&
            !VisualDescendants<TextBlock>(visibleHeader).Any(t => t.Text == RenderedForecast?.Explanation);
        Capture(System.IO.Path.Combine(directory, "forecast-visible.png"));
        CaptureForecastCard(System.IO.Path.Combine(directory, "forecast-card.png"));

        var first = (Expander)((Border)Cards.Children[0]).Child;
        var originalBottom = Top + ActualHeight;
        first.IsExpanded = true;
        await SettlePanelLayoutAsync();
        checks["expandingContentGrowsWindow"] = ActualHeight > collapsedHeight || ActualHeight >= PanelHeightLimit - 1.5;
        var area = SystemParameters.WorkArea;
        var expectedTop = Math.Clamp(originalBottom - ActualHeight, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - ActualHeight - 8));
        checks["expansionKeepsBottomAnchorUnlessScreenClamped"] = Math.Abs(Top - expectedTop) <= 1.5;
        checks["expandedContentFitsOrReachesScreenLimit"] = ScrollOnlyAtHeightLimit();
        layouts["firstExpanded"] = PanelLayoutState();

        var expandedHeight = ActualHeight;
        Hide(); Height = MinHeight; ShowDetails();
        await SettlePanelLayoutAsync();
        checks["reopeningRepairsManuallyShortenedWindow"] = Math.Abs(ActualHeight - expandedHeight) <= 1.5 && ScrollOnlyAtHeightLimit();

        var source = settings.Sources[0];
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
        var resetExpander = ForecastExpander!;
        resetExpander.IsExpanded = true;
        await SettlePanelLayoutAsync();
        checks["resetEvidenceFitsOrReachesScreenLimit"] = ScrollOnlyAtHeightLimit();
        RenderCards();
        await SettlePanelLayoutAsync();
        checks["resetExpansionSurvivesRefresh"] = ForecastExpander!.IsExpanded;
        var localEvidence = (StackPanel)ForecastExpander.Content;
        checks["briefReasonsAndOptionalLinksInsideApp"] = VisualDescendants<TextBlock>(localEvidence).Any(t => t.Text.Contains("리셋 선택지")) &&
            VisualDescendants<Button>(localEvidence).Any(b => (string)b.Content == "원문 ↗") && !VisualDescendants<TextBox>(localEvidence).Any();
        var originalCommunity = resetState.Outlook.CommunityForecast;
        resetState.Outlook.CommunityForecast = originalCommunity! with { FetchedAt = DateTimeOffset.UtcNow.AddHours(-1) };
        RenderCards(); await SettlePanelLayoutAsync();
        var staleHeader = (StackPanel)ForecastExpander!.Header;
        checks["staleProbabilitiesAreSuppressedInUi"] = !RenderedProbabilityAvailable && !VisualDescendants<TextBlock>(staleHeader).Any(t => t.Text.EndsWith('%'));
        resetState.Outlook.CommunityForecast = originalCommunity;
        var originalSignals = resetState.Outlook.Signals;
        resetState.Outlook.Signals = originalSignals.Select(s => s.Poll is not null ? s with { Poll = s.Poll with { Choices = [new("good day", 76), new("needs a reset", 24)] } } : s).ToList();
        RenderCards(); await SettlePanelLayoutAsync();
        checks["reversedPollUpdatesForecastInUi"] = RenderedForecast?.State == ResetForecastState.Weak;
        var creditNow = DateTimeOffset.UtcNow;
        var creditReply = new ResetPost("0000000000000000005", "thsottiaux", "Will be available by EOD PST.", creditNow.AddMinutes(-10),
            new("0000000000000000004", "thsottiaux", "Loading a banked reset for paid Codex accounts. (fictional example)"),
            Schedule: new(ResetSignalKind.CreditGrant, creditNow.AddHours(6)));
        resetState.Outlook.Signals = ResetJudgment.Evaluate([creditReply], [], creditNow);
        outlookExpanded = false;
        RenderCards(); await SettlePanelLayoutAsync();
        var creditHeader = (StackPanel)CreditNewsExpander!.Header;
        checks["creditDeadlineAndSeparateQuotaProbabilitiesVisible"] = RenderedCreditNews?.State == CreditGrantState.Distributing &&
            RenderedForecast?.Kind == ResetSignalKind.UsageReset && ResetPanel.Children.Count == 2 &&
            VisualDescendants<TextBlock>(creditHeader).Any(t => t.Text.Contains("까지 예상")) &&
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
        creditHeader = (StackPanel)CreditNewsExpander!.Header;
        checks["accountGrantConfirmationReachesSeparateNewsCard"] = RenderedCreditNews is { State: CreditGrantState.AccountConfirmed, ConfirmedAccounts: 2, CreditsPerAccount: 1 } &&
            VisualDescendants<TextBlock>(creditHeader).Any(t => t.Text == "+1");
        CaptureForecastCard(System.IO.Path.Combine(directory, "credit-news-and-forecast.png"));
        CreditNewsExpander.IsExpanded = true;
        await SettlePanelLayoutAsync();
        checks["creditEvidenceExpansionFitsOrReachesScreenLimit"] = ScrollOnlyAtHeightLimit();
        RenderCards(); await SettlePanelLayoutAsync();
        checks["creditNewsExpansionSurvivesRefresh"] = CreditNewsExpander!.IsExpanded && !ForecastExpander!.IsExpanded;
        CapturePanelElement((Border)CreditNewsExpander.Parent, System.IO.Path.Combine(directory, "credit-news-reasons.png"));
        CreditNewsExpander.IsExpanded = false; await SettlePanelLayoutAsync();
        CapturePanelElement((Border)CreditNewsExpander.Parent, System.IO.Path.Combine(directory, "credit-news-card.png"));
        foreach (var saved in originalCredits) { saved.Snapshot.ResetCreditDetails = saved.Details; saved.Snapshot.ResetCredits = saved.Count; }
        outlookExpanded = true;
        resetState.Outlook.Signals = originalSignals;
        RenderCards(); await SettlePanelLayoutAsync();
        Capture(System.IO.Path.Combine(directory, "reset-outlook.png"));
        CaptureForecastCard(System.IO.Path.Combine(directory, "forecast-reasons.png"));
        ((Expander)((Border)Cards.Children[0]).Child).IsExpanded = true;
        await SettlePanelLayoutAsync();
        Capture(System.IO.Path.Combine(directory, "reset-and-credit.png"));
        ForecastExpander!.IsExpanded = false;
        foreach (Border card in Cards.Children) ((Expander)card.Child).IsExpanded = true;

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
