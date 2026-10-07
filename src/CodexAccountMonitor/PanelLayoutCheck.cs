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
        var forecastCard = (Expander)((Border)ResetPanel.Children[0]).Child;
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
        var resetExpander = (Expander)((Border)ResetPanel.Children[0]).Child;
        resetExpander.IsExpanded = true;
        await SettlePanelLayoutAsync();
        checks["resetEvidenceFitsOrReachesScreenLimit"] = ScrollOnlyAtHeightLimit();
        RenderCards();
        await SettlePanelLayoutAsync();
        checks["resetExpansionSurvivesRefresh"] = ((Expander)((Border)ResetPanel.Children[0]).Child).IsExpanded;
        var localEvidence = (StackPanel)((Expander)((Border)ResetPanel.Children[0]).Child).Content;
        checks["briefReasonsAndOptionalLinksInsideApp"] = VisualDescendants<TextBlock>(localEvidence).Any(t => t.Text.Contains("리셋 선택지")) &&
            VisualDescendants<Button>(localEvidence).Any(b => (string)b.Content == "원문 ↗") && !VisualDescendants<TextBox>(localEvidence).Any();
        var originalCommunity = resetState.Outlook.CommunityForecast;
        resetState.Outlook.CommunityForecast = originalCommunity! with { FetchedAt = DateTimeOffset.UtcNow.AddHours(-1) };
        RenderCards(); await SettlePanelLayoutAsync();
        var staleHeader = (StackPanel)((Expander)((Border)ResetPanel.Children[0]).Child).Header;
        checks["staleProbabilitiesAreSuppressedInUi"] = !RenderedProbabilityAvailable && !VisualDescendants<TextBlock>(staleHeader).Any(t => t.Text.EndsWith('%'));
        resetState.Outlook.CommunityForecast = originalCommunity;
        var originalSignals = resetState.Outlook.Signals;
        resetState.Outlook.Signals = originalSignals.Select(s => s.Poll is not null ? s with { Poll = s.Poll with { Choices = [new("good day", 76), new("needs a reset", 24)] } } : s).ToList();
        RenderCards(); await SettlePanelLayoutAsync();
        checks["reversedPollUpdatesForecastInUi"] = RenderedForecast?.State == ResetForecastState.Weak;
        resetState.Outlook.Signals = originalSignals;
        RenderCards(); await SettlePanelLayoutAsync();
        Capture(System.IO.Path.Combine(directory, "reset-outlook.png"));
        CaptureForecastCard(System.IO.Path.Combine(directory, "forecast-reasons.png"));
        ((Expander)((Border)Cards.Children[0]).Child).IsExpanded = true;
        await SettlePanelLayoutAsync();
        Capture(System.IO.Path.Combine(directory, "reset-and-credit.png"));
        ((Expander)((Border)ResetPanel.Children[0]).Child).IsExpanded = false;
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
        var width = Math.Max(1, (int)Math.Ceiling(ResetPanel.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ResetPanel.ActualHeight));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(ResetPanel), null, new Rect(0, 0, width, height));
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
