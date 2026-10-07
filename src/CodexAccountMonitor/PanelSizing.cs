using System;
using System.Windows;
using System.Windows.Threading;

namespace CodexAccountMonitor;

public partial class MainWindow
{
    private bool panelSizeQueued;

    private void OnPanelLoaded(object sender, RoutedEventArgs e) => QueuePanelSize();
    private void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) QueuePanelSize();
    }

    private void QueuePanelSize()
    {
        if (panelSizeQueued || exiting) return;
        panelSizeQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            panelSizeQueued = false;
            if (!exiting) SizePanel();
        }, DispatcherPriority.Loaded);
    }

    private double PanelHeightLimit => Math.Min(MaxHeight, SystemParameters.WorkArea.Height - 16);

    private void SizePanel()
    {
        if (exiting) return;
        var area = SystemParameters.WorkArea;
        var bottom = IsVisible && ActualHeight > 0 ? Top + ActualHeight : area.Bottom - 12;
        var width = ActualWidth > 0 ? ActualWidth : Width;

        // An unconstrained vertical measure includes every expanded row and wrapped line.
        // The normal window layout then gives the ScrollViewer a finite viewport.
        PanelContent.Measure(new Size(width, double.PositiveInfinity));
        var height = Math.Clamp(Math.Ceiling(PanelContent.DesiredSize.Height) + 1,
            Math.Min(MinHeight, PanelHeightLimit), PanelHeightLimit);
        if (Math.Abs(Height - height) > 0.1) Height = height;
        Top = Math.Clamp(bottom - height, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - height - 8));
    }
}
