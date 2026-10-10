using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexAccountMonitor.Core;

namespace CodexAccountMonitor;

public partial class MainWindow
{
    private static Border BuildDailyTokenChart(IReadOnlyList<DailyTokenDay> days)
    {
        var stack = new StackPanel();
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(Text("일별 토큰 · 7일", 11, Ink, FontWeights.SemiBold));
        var range = Text($"{days[0].DateLabel} – {days[^1].DateLabel}", 10, Soft);
        range.ToolTip = "서버가 제공한 마지막 날짜까지 최근 7일입니다. PC의 오늘 날짜로 바꾸지 않습니다.";
        Grid.SetColumn(range, 1); heading.Children.Add(range); stack.Children.Add(heading);

        var bars = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        var maximum = Math.Max(1, days.Max(d => d.Tokens ?? 0));
        for (var index = 0; index < days.Count; index++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition());
            var day = days[index];
            var latest = index == days.Count - 1;
            var height = day.Tokens is > 0 ? Math.Max(1.5, day.Tokens.Value / (double)maximum * 70) : 0;
            var column = new StackPanel { Tag = "daily-token-day", Margin = new Thickness(1, 0, 1, 0),
                ToolTip = day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " (" + day.Weekday + ")\n" +
                    (day.Tokens is { } tokens ? tokens.ToString("N0", CultureInfo.InvariantCulture) + " 토큰" : "일별 정보 미제공 · 사용량 0으로 계산하지 않습니다.") +
                    (latest ? "\n가장 최근에 제공된 날짜" : "") };
            var plot = new Grid { Height = 90 };
            var value = Text(DailyTokenHistory.Amount(day.Tokens), 10.5, day.Tokens is null ? Soft : latest ? "#315D91" : Ink, FontWeights.SemiBold);
            value.Tag = "daily-token-value";
            value.TextWrapping = TextWrapping.NoWrap; value.TextAlignment = TextAlignment.Center;
            value.VerticalAlignment = VerticalAlignment.Bottom; value.Margin = new Thickness(0, 0, 0, height + 5);
            plot.Children.Add(new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = value.Margin, Child = value });
            value.Margin = new Thickness(0);
            if (height > 0)
                plot.Children.Add(new Border { Tag = "daily-token-bar", Height = height, Width = 22,
                    Background = Brush(latest ? "#5B87C4" : "#B7C9E2"), CornerRadius = new CornerRadius(4, 4, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom });
            else
                plot.Children.Add(new Border { Height = 1, Width = 14, Background = Brush("#D3D9E4"),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom });
            column.Children.Add(plot);
            var date = Text(day.DateLabel, 10, latest ? "#315D91" : Ink, latest ? FontWeights.SemiBold : FontWeights.Normal, new Thickness(0, 7, 0, 0));
            date.Tag = "daily-token-date"; date.TextWrapping = TextWrapping.NoWrap; date.TextAlignment = TextAlignment.Center;
            column.Children.Add(date);
            var weekday = Text(day.Weekday, 9, Soft, margin: new Thickness(0, 2, 0, 0)); weekday.TextAlignment = TextAlignment.Center;
            column.Children.Add(weekday);
            Grid.SetColumn(column, index); bars.Children.Add(column);
        }
        stack.Children.Add(bars);
        var footnote = Text("서버 날짜 기준" + (days.Any(d => d.Tokens is null) ? " · — 미제공" : ""), 9, Soft, margin: new Thickness(0, 9, 0, 0));
        footnote.ToolTip = "숫자는 반올림한 토큰 수입니다. 각 날짜에 마우스를 올리면 정확한 수치를 볼 수 있습니다. 막대 높이는 이 계정의 7일 최댓값을 기준으로 비교합니다.";
        stack.Children.Add(footnote);
        return new Border { Tag = "daily-token-chart", Background = Brush("#F6F8FC"), BorderBrush = Brush("#E5EAF2"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10),
            Margin = new Thickness(0, 12, 0, 0), Child = stack };
    }
}
