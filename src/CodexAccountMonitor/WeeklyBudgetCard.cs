using System;
using System.Windows;
using System.Windows.Controls;
using CodexAccountMonitor.Core;

namespace CodexAccountMonitor;

public partial class MainWindow
{
    private static string PaceLabel(WeeklyPace pace) => pace switch
    {
        WeeklyPace.Fast => "빠른 편", WeeklyPace.Roomy => "여유 있음", WeeklyPace.Steady => "적정 속도",
        WeeklyPace.Exhausted => "주간 한도 소진", _ => "확인 필요"
    };

    private static string PaceColor(WeeklyPace pace) => pace switch
    {
        WeeklyPace.Fast or WeeklyPace.Exhausted => Warning,
        WeeklyPace.Roomy => "#43816E", _ => "#737AA2"
    };

    private static string TimeLeft(double days) => days >= 1 ? $"{days:0.#}일"
        : days * 24 >= 1 ? $"{days * 24:0.#}시간" : $"{Math.Max(1, Math.Ceiling(days * 1440)):0}분";

    private static Border BuildWeeklyBudgetSummary(WeeklyUsageBudget budget, bool multiple)
    {
        var stack = new StackPanel();
        if (!budget.IsAvailable)
        {
            stack.Children.Add(Text("주간 배분 · 확인 필요", 11, Soft, FontWeights.Medium));
            stack.Children.Add(Text(budget.UnavailableReason!, 10, Soft, margin: new Thickness(0, 4, 0, 0)));
        }
        else
        {
            var metrics = new Grid();
            for (var i = 0; i < 3; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
            var values = new[]
            {
                BudgetMetric("주간 남음", $"{budget.RemainingPercent:0.#}%"),
                BudgetMetric("정기 리셋까지", TimeLeft(budget.RemainingDays)),
                BudgetMetric(budget.IsLastDay ? "리셋까지 배분" : "하루 배분", $"{budget.DailyPercent:0.#}%")
            };
            values[1].ToolTip = budget.Window!.ResetsAt!.Value.ToLocalTime().ToString("MM/dd HH:mm") + " 정기 리셋";
            values[2].ToolTip = budget.IsLastDay ? "하루 미만이 남아, 리셋까지 쓸 수 있는 주간 잔여 한도 전체입니다."
                : "전체 주간 한도의 비율입니다. 남은 주간 한도를 남은 일수로 나눕니다. 예: 40% 남음 ÷ 4일 = 하루 10%. 5시간 한도는 별도입니다.";
            for (var i = 0; i < values.Length; i++) { Grid.SetColumn(values[i], i); metrics.Children.Add(values[i]); }
            stack.Children.Add(metrics);
            var status = new Grid { Margin = new Thickness(0, 5, 0, 0) };
            status.ColumnDefinitions.Add(new ColumnDefinition());
            status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            status.Children.Add(Text(multiple ? $"주간 배분 · {budget.Window.Label}" : "7일 균등 사용 기준", 9, Soft));
            var badge = Text(PaceLabel(budget.Pace), 10, PaceColor(budget.Pace), FontWeights.Medium);
            badge.ToolTip = "7일 동안 고르게 쓰는 경우와 비교합니다. 사용 비율이 지난 기간보다 5%p 넘게 높으면 ‘빠른 편’, 5%p 넘게 낮으면 ‘여유 있음’입니다. 그 사이는 ‘적정 속도’입니다.";
            Grid.SetColumn(badge, 1); status.Children.Add(badge); stack.Children.Add(status);
        }
        return new Border
        {
            Tag = "weekly-budget-summary", Background = Brush("#F4F5F9"), CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 8, 0, 3), Child = stack
        };
    }

    private static StackPanel BudgetMetric(string label, string value)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text(label, 9, Soft));
        stack.Children.Add(Text(value, 15, Ink, FontWeights.SemiBold, new Thickness(0, 2, 0, 0)));
        return stack;
    }

    private static Border BuildWeeklyBudgetDetail(WeeklyUsageBudget budget, bool multiple)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text("주간 사용 속도" + (multiple ? " · " + budget.Window!.Label : ""), 11, Ink, FontWeights.SemiBold));
        stack.Children.Add(WeeklyBar("주간 한도 사용", budget.UsedPercent, PaceColor(budget.Pace)));
        stack.Children.Add(WeeklyBar("지난 기간", budget.ElapsedPercent, "#B5BBCD"));
        var difference = Math.Abs(budget.AheadPercentPoints);
        var explanation = budget.Pace switch
        {
            WeeklyPace.Fast => $"균등 사용보다 {difference:0.#}%p 더 사용",
            WeeklyPace.Roomy => $"균등 사용보다 {difference:0.#}%p 덜 사용",
            WeeklyPace.Exhausted => "주간 한도 소진 · 정기 리셋을 기다리는 중",
            _ => "지난 기간에 맞춰 고르게 사용 중"
        };
        stack.Children.Add(Text(explanation, 10, PaceColor(budget.Pace), margin: new Thickness(0, 10, 0, 0)));
        var reference = Text("7일 동안 고르게 쓰는 경우와 비교", 9, Soft, margin: new Thickness(0, 4, 0, 0));
        reference.ToolTip = "지난 기간은 서버가 알려준 정기 리셋 시각에서 7일을 거슬러 계산합니다. 특별 리셋 예측은 배분량 계산에 포함하지 않습니다. %p는 두 비율의 차이입니다. 이 기준은 별도의 사용 한도가 아닙니다.";
        stack.Children.Add(reference);
        return new Border
        {
            Tag = "weekly-budget-detail", BorderBrush = Brush("#E7E9F1"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12), Child = stack
        };
    }

    private static StackPanel WeeklyBar(string label, double percent, string color)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(Text(label, 10, Soft));
        var value = Text($"{percent:0}%", 10, Ink, FontWeights.Medium);
        Grid.SetColumn(value, 1); row.Children.Add(value); stack.Children.Add(row);
        var fill = new Grid();
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(percent, GridUnitType.Star) });
        fill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - percent, GridUnitType.Star) });
        fill.Children.Add(new Border { Background = Brush(color), CornerRadius = new CornerRadius(4) });
        stack.Children.Add(new Border { Tag = "weekly-budget-bar", Background = Brush("#EEF0F5"), Height = 8,
            CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 5, 0, 0), Child = fill });
        return stack;
    }
}
