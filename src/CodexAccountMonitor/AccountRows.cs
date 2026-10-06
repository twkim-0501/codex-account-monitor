using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CodexAccountMonitor.Core;

namespace CodexAccountMonitor;

public partial class MainWindow
{
    private readonly HashSet<string> expandedAccounts = [];
    private int rowCount = -1;
    private const string Ink = "#30323B", Soft = "#90919B", Warning = "#AE713B";

    private Border BuildCard(AccountSource source, AccountSnapshot? data, bool duplicate)
    {
        var worst = data?.Windows.OrderBy(x => x.RemainingPercent).FirstOrDefault();
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        identity.Children.Add(Text(source.Name, 14, Ink, FontWeights.SemiBold));
        identity.Children.Add(Text($"{(data?.Plan ?? "Codex").ToUpperInvariant()} · {(source.Kind == "ssh" ? "서버" : "로컬")}", 10, Soft, margin: new Thickness(0, 4, 0, 0)));
        var state = errors.TryGetValue(source.Id, out var error) ? error
            : data is not null && !healthy.Contains(source.Id) ? "이전 조회값 · 갱신 확인 중"
            : data?.OrdinaryUsageAllowed == false ? "현재 사용 제한" : null;
        if (state is not null) identity.Children.Add(Text(state, 10, Warning, margin: new Thickness(0, 4, 6, 0)));
        if (duplicate) identity.Children.Add(Text("같은 계정의 다른 연결", 10, Soft));
        header.Children.Add(identity);
        var percentage = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        percentage.Children.Add(Text(worst is null ? "—" : $"{worst.RemainingPercent:0}%", 25, worst?.RemainingPercent <= 10 ? Warning : Ink, FontWeights.Medium));
        var caption = Text(worst?.DurationLabel ?? "잔여 한도", 9, Soft);
        caption.HorizontalAlignment = HorizontalAlignment.Right;
        percentage.Children.Add(caption);
        Grid.SetColumn(percentage, 1); header.Children.Add(percentage);
        var edit = new Button { Content = "···", ToolTip = "계정 연결 편집", Width = 26, Height = 28, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        edit.Click += (_, e) => { e.Handled = true; EditSource(source); };
        Grid.SetColumn(edit, 2); header.Children.Add(edit);

        var details = new StackPanel();
        details.Children.Add(Text(MetricFormatting.MaskEmail(data?.Email) + " · " + source.Location, 10, Soft, margin: new Thickness(0, 0, 0, 10)));
        if (data is null) details.Children.Add(Text("사용량을 조회하고 있습니다…", 11, Soft));
        else
        {
            foreach (var quota in data.Windows)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                row.Children.Add(Text(quota.DurationLabel + (data.Windows.Select(x => x.Bucket).Distinct().Count() > 1 ? " · " + quota.Label : ""), 11, Ink));
                var value = Text($"{quota.RemainingPercent:0}% 남음", 11, quota.RemainingPercent <= 10 ? Warning : Ink, FontWeights.Medium);
                value.HorizontalAlignment = HorizontalAlignment.Right; row.Children.Add(value); details.Children.Add(row);
                details.Children.Add(Text(MetricFormatting.Reset(quota, DateTimeOffset.Now), 10, Soft, margin: new Thickness(0, 3, 0, 8)));
            }
            if (data.Windows.Count == 0) details.Children.Add(Text(data.LimitsNote ?? "사용 한도 미제공", 11, Soft));
            var tokens = new Grid { Margin = new Thickness(0, 10, 0, 4) };
            tokens.ColumnDefinitions.Add(new ColumnDefinition()); tokens.ColumnDefinitions.Add(new ColumnDefinition());
            var latest = data.Daily?.LastOrDefault();
            var daily = TokenMetric(latest is null ? "일별 토큰" : $"일별 토큰 · {(latest.Date.Length >= 10 ? latest.Date[5..10] : latest.Date)}", MetricFormatting.Tokens(latest?.Tokens));
            var lifetimeTokens = TokenMetric("누적 토큰", MetricFormatting.Tokens(data.LifetimeTokens));
            Grid.SetColumn(lifetimeTokens, 1); tokens.Children.Add(daily); tokens.Children.Add(lifetimeTokens); details.Children.Add(tokens);
            if (data.UsageNote is not null) details.Children.Add(Text(data.UsageNote, 10, Soft));
            if (data.Daily is { Count: > 1 }) details.Children.Add(Sparkline(data.Daily.TakeLast(14).ToArray()));
            var updated = $"갱신 {data.UpdatedAt.ToLocalTime():MM/dd HH:mm}";
            if (data.ResetCredits is { } credits) updated += $" · 리셋 {credits}회";
            details.Children.Add(Text(updated, 9, Soft, margin: new Thickness(0, 10, 0, 0)));
        }
        var expander = new Expander { Header = header, Content = details, IsExpanded = expandedAccounts.Contains(source.Id), ToolTip = "계정을 클릭하면 토큰과 초기화 시각을 펼칩니다" };
        expander.Expanded += (_, _) => { expandedAccounts.Add(source.Id); SizePanel(); };
        expander.Collapsed += (_, _) => { expandedAccounts.Remove(source.Id); SizePanel(); };
        return new Border { BorderBrush = Brush("#E8E8EE"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 3, 0, 3), Child = expander };
    }

    private void SizePanel()
    {
        var count = settings.Sources.Count(x => x.Enabled);
        var height = Math.Min(SystemParameters.WorkArea.Height - 24, Math.Clamp(164 + count * 86, 340, 520) + Math.Min(2, expandedAccounts.Count) * 180);
        height = Math.Min(height, 680);
        var delta = height - Height;
        Height = height;
        if (IsVisible) Top = Math.Max(SystemParameters.WorkArea.Top + 4, Top - delta);
        rowCount = count;
    }

    private static StackPanel TokenMetric(string label, string value)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text(label, 10, Soft));
        stack.Children.Add(Text(value, 16, Ink, FontWeights.Medium, new Thickness(0, 4, 0, 0)));
        return stack;
    }

    private FrameworkElement Sparkline(DailyTokens[] daily)
    {
        var canvas = new Canvas { Height = 23, Margin = new Thickness(0, 10, 0, 0), ClipToBounds = true, ToolTip = "최근 일별 토큰 추이 · 서버가 제공한 날짜 기준" };
        var max = Math.Max(1, daily.Max(x => x.Tokens));
        var line = new Polyline { Stroke = Brush("#9CA0C4"), StrokeThickness = 1.2, StrokeLineJoin = PenLineJoin.Round };
        canvas.SizeChanged += (_, _) =>
        {
            line.Points.Clear();
            for (var i = 0; i < daily.Length; i++) line.Points.Add(new Point(i * Math.Max(1, canvas.ActualWidth) / (daily.Length - 1), 21 - Math.Clamp(daily[i].Tokens / (double)max, 0, 1) * 18));
        };
        canvas.Children.Add(line); return canvas;
    }
}
