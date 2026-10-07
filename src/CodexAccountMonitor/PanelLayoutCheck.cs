using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

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

        var first = (Expander)((Border)Cards.Children[0]).Child;
        var originalBottom = Top + ActualHeight;
        first.IsExpanded = true;
        await SettlePanelLayoutAsync();
        checks["expandingContentGrowsWindow"] = ActualHeight > collapsedHeight || ActualHeight >= PanelHeightLimit - 1.5;
        checks["expansionKeepsBottomAnchor"] = Math.Abs(Top + ActualHeight - originalBottom) <= 1.5;
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
    private bool ScrollOnlyAtHeightLimit() => AccountScroll.ScrollableHeight <= 1 || ActualHeight >= PanelHeightLimit - 1.5;
    private object PanelLayoutState() => new
    {
        width = ActualWidth, height = ActualHeight, top = Top,
        contentHeight = AccountScroll.ExtentHeight, viewportHeight = AccountScroll.ViewportHeight,
        scrollableHeight = AccountScroll.ScrollableHeight, heightLimit = PanelHeightLimit
    };
}
