using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CodexAccountMonitor.Core;

namespace CodexAccountMonitor;

public partial class MainWindow
{
    private const string ForecastGreen = "#245E52";
    internal bool RenderedProbabilityAvailable { get; private set; }

    private Border BuildForecastCard(ResetForecast forecast, DateTimeOffset now)
    {
        var outlook = resetState.Outlook;
        var completion = outlook.Completions.Where(c => c.Kind == ResetSignalKind.UsageReset && c.At <= now).MaxBy(c => c.At);
        var community = outlook.CommunityForecast;
        RenderedProbabilityAvailable = community?.IsCurrent(now, completion?.At) == true;
        var content = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Text("다음 특별 리셋", 12, Ink, FontWeights.SemiBold));
        var (status, color) = ForecastBadge(forecast);
        var badge = Text(status, 9, color, FontWeights.SemiBold);
        Grid.SetColumn(badge, 1); top.Children.Add(badge); content.Children.Add(top);

        content.Children.Add(Text(ForecastTime(forecast), 18, Ink, FontWeights.SemiBold, new Thickness(0, 10, 0, 0)));
        var gauges = new Grid { Margin = new Thickness(0, 11, 0, 0) };
        gauges.ColumnDefinitions.Add(new ColumnDefinition()); gauges.ColumnDefinitions.Add(new ColumnDefinition());
        gauges.Children.Add(ProbabilityGauge("24시간 내 리셋 확률", RenderedProbabilityAvailable ? community!.Within24Hours : null));
        var twoDays = ProbabilityGauge("48시간 내 리셋 확률", RenderedProbabilityAvailable ? community!.Within48Hours : null);
        Grid.SetColumn(twoDays, 1); gauges.Children.Add(twoDays); content.Children.Add(gauges);

        var links = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        links.Children.Add(EvidenceLink("확률 출처 ↗", CommunityResetForecast.SourceUrl));
        var active = ResetJudgment.Active(outlook, now);
        foreach (var basis in forecast.Evidence.Where(e => active.Any(s => s.Id == e.Id && s.Kind == ResetSignalKind.UsageReset)).Take(3))
        {
            var signal = active.First(s => s.Id == basis.Id);
            var label = signal.Poll is not null ? "투표" : signal.Level == ResetSignalLevel.Conditional ? "Tibo 약속" : "Tibo 예고";
            links.Children.Add(EvidenceLink(label + " ↗", signal.SourceUrl));
        }
        content.Children.Add(links);
        if (completion is not null && now - completion.At <= TimeSpan.FromHours(48))
            content.Children.Add(Text("최근 완료 · " + ResetJudgment.KoreanTime(completion.At), 9, Soft, margin: new Thickness(0, 5, 0, 0)));
        if (forecast.DataWarning is not null || !RenderedProbabilityAvailable)
            content.Children.Add(Text("최신 정보 확인 중", 9, Warning, margin: new Thickness(0, 4, 0, 0)));
        else if (demo) content.Children.Add(Text("가상 예시", 9, Soft, margin: new Thickness(0, 4, 0, 0)));
        if (forecast.Timing.Contains("1시간"))
            content.Children.Add(Text("원문 시각에 1시간 차이 가능", 9, Warning, margin: new Thickness(0, 3, 0, 0)));
        ForecastCard = new Border { Background = Brush("#FFFFFF"), BorderBrush = Brush("#E1E8E5"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(15, 12, 15, 10), Child = content };
        return ForecastCard;
    }

    private Button EvidenceLink(string label, string url)
    {
        var link = SourceLink(label, url);
        link.Margin = new Thickness(0, 0, 10, 0);
        link.IsEnabled = !demo;
        link.ToolTip = demo ? "가상 예시" : url;
        return link;
    }

    private static (string, string) ForecastBadge(ResetForecast forecast) => forecast.State switch
    {
        ResetForecastState.Announced => ("직접 예고", ForecastGreen),
        ResetForecastState.Elevated => ("앱 추정", ForecastGreen),
        ResetForecastState.Watching => ("미확정", "#857140"),
        ResetForecastState.WaitingForCompletion => ("확인 대기", "#857140"),
        ResetForecastState.Gathering => ("확인 중", "#82858A"),
        _ => ("새 예고 대기", "#82858A")
    };

    private static string ForecastTime(ResetForecast forecast) => forecast.State switch
    {
        ResetForecastState.Elevated or ResetForecastState.Announced => forecast.Timing.Split(" · ")[0],
        ResetForecastState.WaitingForCompletion => "예정 시각 지남 · 완료 확인 중",
        ResetForecastState.Gathering => "예상 시각 확인 중",
        _ => "다음 리셋 시각 미정"
    };

    private static StackPanel ProbabilityGauge(string label, double? value)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var heading = Text(label, 10, ForecastGreen, FontWeights.Medium); heading.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(heading);
        var ring = new Grid { Width = 86, Height = 86, Margin = new Thickness(0, 6, 0, 0) };
        ring.Children.Add(new Ellipse { Stroke = Brush("#EDF2EF"), StrokeThickness = 7, Margin = new Thickness(5) });
        if (value is > 0)
        {
            const double radius = 34.5;
            var angle = Math.Min(value.Value, 99.9999) / 100 * 2 * Math.PI;
            var geometry = new PathGeometry([new PathFigure(new Point(43, 8.5),
                [new ArcSegment(new Point(43 + radius * Math.Sin(angle), 43 - radius * Math.Cos(angle)), new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true)], false)]);
            ring.Children.Add(new System.Windows.Shapes.Path { Data = geometry, Stroke = Brush(ForecastGreen), StrokeThickness = 7,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }
        var number = Text(value is { } percent ? $"{percent:0}%" : "—", 25, value.HasValue ? ForecastGreen : Soft, FontWeights.SemiBold);
        number.HorizontalAlignment = HorizontalAlignment.Center; number.VerticalAlignment = VerticalAlignment.Center;
        ring.Children.Add(number); panel.Children.Add(ring);
        System.Windows.Automation.AutomationProperties.SetName(panel, label + (value is { } v ? $" 리셋 확률 {v:0}%" : " 확률 미확인"));
        return panel;
    }
}
