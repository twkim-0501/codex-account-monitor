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
    private const string Ink = "#30323B", Soft = "#90919B", Warning = "#AE713B";

    private Border BuildCard(AccountSource source, AccountSnapshot? data, bool duplicate, bool primary = false)
    {
        var worst = data?.Windows.OrderBy(x => x.RemainingPercent).FirstOrDefault();
        var budgets = WeeklyUsageBudget.Build(data, healthy.Contains(source.Id), DateTimeOffset.UtcNow);
        var primaryBudget = budgets.FirstOrDefault(b => !b.IsAvailable) ?? budgets.OrderBy(b => b.DailyPercent).First();
        var header = new Grid();
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition());
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(Text(source.Name, 14, Ink, FontWeights.SemiBold));
        if (primary)
        {
            var badge = new Border { Background = Brush("#DDEEF8"), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "현재 데스크톱 앱에 로그인된 계정", Child = Text("PRIMARY", 8, "#39708F", FontWeights.SemiBold) };
            Grid.SetColumn(badge, 1); title.Children.Add(badge);
        }
        identity.Children.Add(title);
        identity.Children.Add(Text($"{(data?.Plan ?? "Codex").ToUpperInvariant()} · {(source.Kind == "ssh" ? "서버" : "로컬")}", 10, Soft, margin: new Thickness(0, 4, 0, 0)));
        if (data?.ResetCredits is { } available)
        {
            var nearest = data.ResetCreditDetails?.Where(c => c.IsAvailable && c.ExpiresAt > DateTimeOffset.UtcNow).OrderBy(c => c.ExpiresAt).FirstOrDefault();
            identity.Children.Add(Text($"초기화권 {available}개" + (available > 0 && nearest?.ExpiresAt is { } expiry && expiry - DateTimeOffset.UtcNow <= TimeSpan.FromDays(3) ? $" · {ResetJudgment.KoreanTime(expiry)} 만료" : ""), 10,
                nearest?.ExpiresAt - DateTimeOffset.UtcNow <= TimeSpan.FromDays(3) ? Warning : Soft, margin: new Thickness(0, 4, 4, 0)));
        }
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
        if (source.Id == currentDesktop.Id) edit.Visibility = Visibility.Collapsed;
        Grid.SetColumn(edit, 2); header.Children.Add(edit);
        var budgetSummary = BuildWeeklyBudgetSummary(primaryBudget, budgets.Count > 1);
        Grid.SetRow(budgetSummary, 1); Grid.SetColumnSpan(budgetSummary, 3); header.Children.Add(budgetSummary);

        var details = new StackPanel();
        details.Children.Add(Text(MetricFormatting.MaskEmail(data?.Email) + " · " + source.Location, 10, Soft, margin: new Thickness(0, 0, 0, 10)));
        if (data is null) details.Children.Add(Text("사용량을 조회하고 있습니다…", 11, Soft));
        else
        {
            foreach (var budget in budgets.Where(b => b.IsAvailable)) details.Children.Add(BuildWeeklyBudgetDetail(budget, budgets.Count > 1));
            foreach (var quota in data.Windows)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                row.Children.Add(Text(quota.DurationLabel + (data.Windows.Select(x => x.Bucket).Distinct().Count() > 1 ? " · " + quota.Label : ""), 11, Ink));
                var value = Text($"{quota.RemainingPercent:0}% 남음", 11, quota.RemainingPercent <= 10 ? Warning : Ink, FontWeights.Medium);
                value.HorizontalAlignment = HorizontalAlignment.Right; row.Children.Add(value); details.Children.Add(row);
                details.Children.Add(Text(MetricFormatting.Reset(quota, DateTimeOffset.Now), 10, Soft, margin: new Thickness(0, 3, 0, 8)));
            }
            if (data.Windows.Count == 0) details.Children.Add(Text(data.LimitsNote ?? "사용 한도 미제공", 11, Soft));
            AddCreditDetails(details, data);
            var tokens = new Grid { Margin = new Thickness(0, 10, 0, 4) };
            tokens.ColumnDefinitions.Add(new ColumnDefinition()); tokens.ColumnDefinitions.Add(new ColumnDefinition());
            var latest = data.Daily?.LastOrDefault();
            var daily = TokenMetric(latest is null ? "일별 토큰" : $"일별 토큰 · {(latest.Date.Length >= 10 ? latest.Date[5..10] : latest.Date)}", MetricFormatting.Tokens(latest?.Tokens));
            var lifetimeTokens = TokenMetric("누적 토큰", MetricFormatting.Tokens(data.LifetimeTokens));
            Grid.SetColumn(lifetimeTokens, 1); tokens.Children.Add(daily); tokens.Children.Add(lifetimeTokens); details.Children.Add(tokens);
            if (data.UsageNote is not null) details.Children.Add(Text(data.UsageNote, 10, Soft));
            if (data.Daily is { Count: > 1 }) details.Children.Add(Sparkline(data.Daily.TakeLast(14).ToArray()));
            var updated = $"갱신 {data.UpdatedAt.ToLocalTime():MM/dd HH:mm}";
            details.Children.Add(Text(updated, 9, Soft, margin: new Thickness(0, 10, 0, 0)));
        }
        var expander = new Expander { Header = header, Content = details, IsExpanded = expandedAccounts.Contains(source.Id), ToolTip = "계정을 클릭하면 주간 사용 속도와 상세 정보를 펼칩니다" };
        expander.Expanded += (_, _) => { expandedAccounts.Add(source.Id); QueuePanelSize(); };
        expander.Collapsed += (_, _) => { expandedAccounts.Remove(source.Id); QueuePanelSize(); };
        return new Border { Tag = primary ? "primary-account" : "account",
            Background = primary ? Brush("#F0F7FC") : null, BorderBrush = Brush(primary ? "#D0E4F0" : "#E8E8EE"),
            BorderThickness = primary ? new Thickness(1) : new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(primary ? 9 : 0),
            Margin = primary ? new Thickness(0, 3, 0, 7) : new Thickness(0),
            Padding = primary ? new Thickness(8, 8, 8, 7) : new Thickness(0, 3, 0, 3), Child = expander };
    }

    private static void AddCreditDetails(StackPanel details, AccountSnapshot data)
    {
        var summary = Text(data.ResetCredits is { } count ? $"초기화권 {count}개" : "초기화권 개수 미제공", 11, Ink, FontWeights.Medium, new Thickness(0, 10, 0, 4));
        summary.ToolTip = "보유 개수는 서버 조회값입니다. 초기화권 사용 뒤의 정기 리셋 시각은 위 한도에 표시된 새 조회값을 따릅니다.";
        details.Children.Add(summary);
        if (data.ResetCreditDetails is null)
        {
            if (data.ResetCredits != 0) details.Children.Add(Text("개별 만료일 미제공 · 유효기간을 추정하지 않습니다.", 10, Soft));
            return;
        }
        var credits = data.ResetCreditDetails.Where(c => c.IsAvailable).OrderBy(c => c.ExpiresAt ?? DateTimeOffset.MaxValue).ToArray();
        foreach (var credit in credits)
        {
            var expiry = credit.ExpiresAt;
            var label = expiry is null ? "만료일 미제공" : $"{ResetJudgment.KoreanTime(expiry.Value)} 만료";
            if (expiry <= DateTimeOffset.UtcNow) label += " · 만료 시각 지남, 갱신 확인";
            var row = Text(label, 10, expiry - DateTimeOffset.UtcNow <= TimeSpan.FromDays(3) ? Warning : Soft, margin: new Thickness(0, 3, 0, 0));
            row.ToolTip = credit.GrantedAt is { } granted ? "지급 " + ResetJudgment.KoreanTime(granted) : "지급 시각 미제공";
            details.Children.Add(row);
        }
        if (data.ResetCredits > credits.Length) details.Children.Add(Text("개별 상세는 일부만 제공됐습니다. 표시 개수가 전체 보유 개수입니다.", 10, Soft));
        else if (credits.Length == 0 && data.ResetCredits != 0) details.Children.Add(Text("현재 응답에는 사용 가능한 초기화권 상세가 없습니다.", 10, Soft));
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
