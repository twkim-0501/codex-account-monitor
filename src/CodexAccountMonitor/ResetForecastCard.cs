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
        var community = outlook.CommunityForecast;
        RenderedProbabilityAvailable = community?.IsCurrent(now) == true;
        var header = new StackPanel();
        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition());
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(Text("다음 특별 리셋 확률", 12, Ink, FontWeights.SemiBold));
        var (status, color) = ForecastBadge(forecast);
        var badge = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 3, 7, 3),
            Child = Text(status, 9, color, FontWeights.SemiBold), ToolTip = forecast.Explanation };
        badge.Background = new SolidColorBrush(Color.FromArgb(22, Brush(color).Color.R, Brush(color).Color.G, Brush(color).Color.B));
        Grid.SetColumn(badge, 1); title.Children.Add(badge); header.Children.Add(title);

        var gauges = new Grid { Margin = new Thickness(0, 12, 0, 5) };
        gauges.ColumnDefinitions.Add(new ColumnDefinition()); gauges.ColumnDefinitions.Add(new ColumnDefinition());
        var day = ProbabilityGauge("24시간 내 리셋 확률", RenderedProbabilityAvailable ? community!.Within24Hours : null);
        var twoDays = ProbabilityGauge("48시간 내 리셋 확률", RenderedProbabilityAvailable ? community!.Within48Hours : null);
        gauges.Children.Add(day); Grid.SetColumn(twoDays, 1); gauges.Children.Add(twoDays); header.Children.Add(gauges);
        var attribution = Text(RenderedProbabilityAvailable ? forecast.Kind == ResetSignalKind.CreditGrant
            ? "한도 리셋 확률 · 초기화권 지급과 별도" : "커뮤니티 확률 · 실험적 추정" : "확률 데이터 확인 중", 9, Soft);
        attribution.HorizontalAlignment = HorizontalAlignment.Center;
        attribution.ToolTip = "확률 출처: codexreset.org. 게시물에 대한 앱의 판단은 오른쪽 상태와 근거에서 확인합니다.";
        header.Children.Add(attribution);

        var timing = new Grid { Margin = new Thickness(0, 11, 0, 0) };
        timing.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(57) }); timing.ColumnDefinitions.Add(new ColumnDefinition());
        timing.Children.Add(Text(forecast.Kind == ResetSignalKind.CreditGrant ? "지급 예정" : "예상 시각", 9, Soft, margin: new Thickness(0, 2, 0, 0)));
        var timingValue = Text(CompactTiming(forecast), 10, Ink, FontWeights.Medium);
        timingValue.ToolTip = forecast.Timing; Grid.SetColumn(timingValue, 1); timing.Children.Add(timingValue); header.Children.Add(timing);

        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var completion = outlook.Completions.Where(c => c.Kind == ResetSignalKind.UsageReset && c.At <= now).MaxBy(c => c.At);
        var latest = Text(demo ? "가상 예시" : completion is not null ? "최근 리셋 · " + TimeAgo(completion.At, now) : "최근 완료 시각 미확인", 9, Soft);
        latest.ToolTip = completion is not null ? ResetJudgment.KoreanTime(completion.At) : outlook.Note;
        footer.Children.Add(latest);
        var disclosure = Text(outlookExpanded ? "근거 닫기 ⌃" : "근거 보기 ⌄", 10, ForecastGreen, FontWeights.Medium);
        Grid.SetColumn(disclosure, 1); footer.Children.Add(disclosure); header.Children.Add(footer);
        if (forecast.DataWarning is not null || !RenderedProbabilityAvailable)
        {
            var warning = Text(forecast.DataWarning is not null ? "● 일부 근거 확인 필요" : "● 확률 갱신 대기", 9, Warning, margin: new Thickness(0, 7, 0, 0));
            warning.ToolTip = forecast.DataWarning ?? "확률 데이터가 없거나 오래되어 수치를 표시하지 않습니다.";
            header.Children.Add(warning);
        }

        var details = BuildForecastReasons(forecast, community, now);
        var expander = new Expander { Header = header, Content = details, IsExpanded = outlookExpanded,
            ToolTip = "클릭하면 짧은 판단 근거와 선택적으로 열 수 있는 원문 링크를 표시합니다" };
        expander.Expanded += (_, _) => { outlookExpanded = true; disclosure.Text = "근거 닫기 ⌃"; QueuePanelSize(); };
        expander.Collapsed += (_, _) => { outlookExpanded = false; disclosure.Text = "근거 보기 ⌄"; QueuePanelSize(); };
        ForecastExpander = expander;
        return new Border { Background = Brush("#FFFFFF"), BorderBrush = Brush("#E1E8E5"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(6, 1, 6, 0), Child = expander };
    }

    private StackPanel BuildForecastReasons(ResetForecast forecast, CommunityResetForecast? community, DateTimeOffset now)
    {
        var details = new StackPanel();
        details.Children.Add(new Border { Height = 1, Background = Brush("#E7ECE9"), Margin = new Thickness(0, 0, 0, 10) });
        details.Children.Add(Text("예측 근거", 11, Ink, FontWeights.SemiBold));
        if (RenderedProbabilityAvailable && community is not null)
        {
            details.Children.Add(ReasonRow("확률", community.Reason, CommunityResetForecast.SourceUrl, demo));
        }
        else details.Children.Add(Text("확률은 현재 확인할 수 없습니다. 게시물 판단은 아래에 표시합니다.", 10, Soft, margin: new Thickness(0, 7, 0, 0)));

        foreach (var basis in forecast.Evidence.Take(3))
        {
            var summary = CompactEvidence(basis, resetState.Outlook, now);
            details.Children.Add(ReasonRow(basis.RoleLabel, summary, "https://x.com/thsottiaux/status/" + basis.Id, demo));
        }
        if (forecast.Evidence.Count == 0) details.Children.Add(Text("현재 수집 범위에서 다음 리셋을 가리키는 게시물이 없습니다.", 10, Soft, margin: new Thickness(0, 8, 0, 0)));
        details.Children.Add(Text(CompactChange(forecast), 10, Soft, margin: new Thickness(0, 10, 0, 0)));
        var updated = community is not null && RenderedProbabilityAvailable ? "확률 갱신 " + ResetJudgment.KoreanTime(community.UpdatedAt) : "확률 수집 대기";
        details.Children.Add(Text(demo ? "가상 예시 · 실제 예측이 아닙니다" : updated + " · 소식 10분 간격", 9, Soft, margin: new Thickness(0, 10, 0, 0)));
        if (forecast.DataWarning is not null)
            details.Children.Add(Text(forecast.DataWarning + " " + resetState.Outlook.Note, 9, Warning, margin: new Thickness(0, 5, 0, 0)));
        if (!demo)
        {
            var sources = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
            sources.Children.Add(SourceLink("CodexReset ↗", CommunityResetForecast.SourceUrl));
            sources.Children.Add(SourceLink("Data from Codex Resets ↗", "https://codex-resets.com"));
            details.Children.Add(sources);
        }
        return details;
    }

    private static StackPanel ProbabilityGauge(string label, double? value)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var heading = Text(label, 10, ForecastGreen, FontWeights.Medium); heading.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(heading);
        var ring = new Grid { Width = 86, Height = 86, Margin = new Thickness(0, 6, 0, 0) };
        ring.Children.Add(new Ellipse { Stroke = Brush("#EDF2EF"), StrokeThickness = 7, Margin = new Thickness(5) });
        if (value is > 0)
        {
            // Clockwise from twelve o'clock. The value is a source estimate, never a poll vote share.
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

    private static Grid ReasonRow(string role, string summary, string url, bool fictional)
    {
        var row = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var body = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        body.Children.Add(Text(role, 9, ForecastGreen, FontWeights.Medium)); body.Children.Add(Text(summary, 10, Ink, margin: new Thickness(0, 3, 0, 0)));
        row.Children.Add(body);
        var link = SourceLink(role == "확률" ? "계산 ↗" : "원문 ↗", url); link.VerticalAlignment = VerticalAlignment.Top;
        link.IsEnabled = !fictional; link.ToolTip = fictional ? "가상 예시이므로 실제 원문이 없습니다" : url;
        Grid.SetColumn(link, 1); row.Children.Add(link); return row;
    }

    private static (string, string) ForecastBadge(ResetForecast forecast) => forecast.State switch
    {
        ResetForecastState.Announced => (forecast.Kind == ResetSignalKind.CreditGrant ? "초기화권 예고" : "리셋 예고", ForecastGreen),
        ResetForecastState.Elevated => ("가능성 ↑", ForecastGreen),
        ResetForecastState.Watching => ("단서 관찰", "#857140"),
        ResetForecastState.WaitingForCompletion => ("완료 확인 중", "#857140"),
        ResetForecastState.Gathering => ("수집 중", "#82858A"),
        _ => (forecast.DataWarning is null ? "단서 대기" : "판단 보류", "#82858A")
    };
    private static string CompactTiming(ResetForecast forecast) => forecast.State switch
    {
        ResetForecastState.Elevated => forecast.Timing.Replace(" · 추정 관찰 범위", " · 추정"),
        ResetForecastState.Announced or ResetForecastState.WaitingForCompletion => forecast.Timing.Replace(" · 완료 별도 확인", "").Replace(" · PST/PT 표기 차이 1시간", " · PST/PT 1시간 차이"),
        ResetForecastState.Gathering => "확인 중",
        _ => "새 예고 대기"
    };
    private static string CompactEvidence(ForecastEvidence basis, ResetOutlook outlook, DateTimeOffset now)
    {
        var signal = outlook.Signals.FirstOrDefault(s => s.Id == basis.Id);
        if (signal?.Poll is { } poll)
        {
            var total = poll.Choices.Sum(c => Math.Max(0d, c.Votes ?? 0));
            if (total > 0 && poll.Choices.All(c => c.Votes is >= 0))
                return basis.Summary.Split('.')[0] + ". " + (poll.IsClosed ? "투표 종료." : "투표 진행 중.");
            return "리셋 선택지는 있으나 득표 수를 확인하지 못했습니다.";
        }
        if (signal?.Level == ResetSignalLevel.Conditional) return "개선 출시 또는 리셋의 조건부 약속. 매일 리셋을 보장하진 않습니다.";
        var context = outlook.CurrentContext.FirstOrDefault(c => c.Id == basis.Id);
        if (context?.Kind == ResetCurrentContextKind.Completion) return "최근 리셋 완료. 이전 일회성 투표와 힌트는 제외했습니다.";
        if (context?.Kind == ResetCurrentContextKind.Improvement) return "최근 개선 출시 확인. 리셋 취소 여부는 아직 알 수 없습니다.";
        return basis.Summary;
    }
    private static string CompactChange(ResetForecast forecast) => forecast.State switch
    {
        ResetForecastState.Announced or ResetForecastState.WaitingForCompletion => "다음 확인: 완료 공지 또는 실제 계정 한도 변화",
        ResetForecastState.Elevated => "투표가 역전되거나 새 예고가 나오면 판단이 바뀝니다.",
        _ => "새 투표·답글·실행 예고가 나오면 다시 판단합니다."
    };
    private static string TimeAgo(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;
        return elapsed.TotalMinutes < 1 ? "방금" : elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes}분 전"
            : elapsed.TotalDays < 1 ? $"{(int)elapsed.TotalHours}시간 전" : $"{(int)elapsed.TotalDays}일 전";
    }
}
