using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexAccountMonitor.Core;

public sealed class ResetFeed : IDisposable
{
    private readonly HttpClient http;
    public ResetFeed(HttpMessageHandler? handler = null)
    {
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CodexAccountMonitor/1.4.5 (+https://github.com/twkim-0501/codex-account-monitor)");
    }

    public async Task<ResetOutlook> ReadAsync(DateTimeOffset now, CancellationToken token)
    {
        // Independent public requests; no account identifiers, cookies or Codex credentials leave this process.
        var statusTask = Attempt("https://codex-resets.com/api/v1/status", 256_000, token);
        var contextTask = Attempt("https://codexreset.org/", 8_000_000, token);
        await Task.WhenAll(statusTask, contextTask);
        var statusText = statusTask.Result;
        var contextText = contextTask.Result;
        List<ResetPost> posts = [];
        List<ResetCompletion> completions = [];
        var notes = new List<string>();
        DateTimeOffset? sourceUpdated = null;
        var statusOk = false;
        if (statusText is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(statusText);
                var parsed = ResetFeedParser.Status(doc.RootElement);
                posts.AddRange(parsed.Posts); completions.AddRange(parsed.Completions);
                sourceUpdated = parsed.GeneratedAt; statusOk = true;
            }
            catch (Exception error) when (error is JsonException or InvalidDataException) { notes.Add("공지 API 형식 변경"); }
        }
        else notes.Add("공지 API 연결 실패");
        var contextOk = false;
        if (contextText is not null)
        {
            var parsed = ResetFeedParser.CommunityPosts(contextText);
            posts.AddRange(parsed);
            contextOk = parsed.Count > 0;
            if (!contextOk) notes.Add("답글 맥락 수집 형식 변경");
        }
        else notes.Add("답글 맥락 수집 실패");
        if (!statusOk && !contextOk) throw new InvalidDataException("공개 소식을 확인할 수 없습니다");
        posts = posts.Where(p => p.CreatedAt >= now.AddDays(-32)).GroupBy(p => p.Id)
            .Select(g => g.Last() with { Schedule = g.Select(p => p.Schedule).FirstOrDefault(s => s is not null) }).ToList();

        // Enrich reset candidates, short Vote replies and scheduled timing replies that omit the reset keyword.
        var interesting = posts.Where(p => p.Schedule is not null || Regex.IsMatch(p.Text, @"\breset|^vote[.!]?$", RegexOptions.IgnoreCase))
            .OrderByDescending(p => p.CreatedAt).Take(6).ToArray();
        var missingContext = false;
        foreach (var candidate in interesting)
        {
            token.ThrowIfCancellationRequested();
            if (!Regex.IsMatch(candidate.Id, @"^\d{10,25}$")) continue;
            var text = await Attempt($"https://cdn.syndication.twimg.com/tweet-result?id={candidate.Id}&lang=en&token=0", 256_000, token);
            ResetPost? direct = null;
            if (text is not null)
                try { using var doc = JsonDocument.Parse(text); direct = ResetFeedParser.SyndicatedPost(doc.RootElement); }
                catch (JsonException) { }
            if (direct is null || direct.Id != candidate.Id || !direct.Author.Equals("thsottiaux", StringComparison.OrdinalIgnoreCase)) { missingContext = true; continue; }
            // Some widget bodies are truncated; keep the longer collected body and verified poll/context.
            var richer = direct with { Text = direct.Text.Length > candidate.Text.Length ? direct.Text : candidate.Text,
                Parent = direct.Parent ?? candidate.Parent, Quote = direct.Quote ?? candidate.Quote, Schedule = candidate.Schedule };
            posts[posts.FindIndex(p => p.Id == candidate.Id)] = richer;
        }
        if (missingContext) notes.Add("일부 투표·원문 맥락 미수집");
        if (sourceUpdated is { } updated && now - updated > TimeSpan.FromHours(2)) notes.Add("공지 원본 갱신 지연");
        if (posts.Count == 0 || now - posts.Max(p => p.CreatedAt) > TimeSpan.FromDays(2)) notes.Add("최근 게시물 수집 범위 확인 필요");
        var boundaries = completions.Concat(posts.Select(ResetJudgment.Completion).OfType<ResetCompletion>())
            .GroupBy(c => c.Kind).Select(g => g.MaxBy(c => c.At)!).ToList();
        return new ResetOutlook
        {
            CheckedAt = now, SourceUpdatedAt = sourceUpdated, PartialCoverage = notes.Count > 0,
            Note = notes.Count == 0 ? "공개 수집본·투표 확인 · 새 글 반영에 지연 가능" : string.Join(" · ", notes),
            Signals = ResetJudgment.Evaluate(posts, completions, now),
            Completions = boundaries,
            CurrentContext = ResetForecasting.CurrentContext(posts, boundaries, now),
            CommunityForecast = contextText is null ? null : ResetFeedParser.CommunityForecast(contextText, now)
        };
    }

    private async Task<string?> Attempt(string url, int maxBytes, CancellationToken token)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes) return null;
            await using var input = await response.Content.ReadAsStreamAsync(token);
            using var output = new MemoryStream();
            var buffer = new byte[16_384];
            int size;
            while ((size = await input.ReadAsync(buffer, token)) > 0)
            {
                if (output.Length + size > maxBytes) return null;
                output.Write(buffer, 0, size);
            }
            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException)
        { token.ThrowIfCancellationRequested(); return null; }
    }
    public void Dispose() => http.Dispose();
}

public static class ResetFeedParser
{
    private static JsonElement Get(JsonElement obj, string name) => UsageParser.Get(obj, name);
    private static string? Str(JsonElement obj, string name) => UsageParser.String(obj, name);
    private static DateTimeOffset? Date(JsonElement obj, string name) => DateTimeOffset.TryParse(Str(obj, name), System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.AssumeUniversal, out var value) ? value : null;
    public sealed record StatusData(List<ResetPost> Posts, List<ResetCompletion> Completions, DateTimeOffset GeneratedAt);
    public static StatusData Status(JsonElement root)
    {
        var data = Get(root, "data");
        var generated = Date(Get(root, "meta"), "generated_at");
        if (data.ValueKind != JsonValueKind.Object || generated is null || !data.TryGetProperty("scheduled_reset", out _) || !data.TryGetProperty("active_watch", out _))
            throw new InvalidDataException("Unknown public status schema");
        List<ResetPost> posts = []; List<ResetCompletion> completed = [];
        foreach (var name in new[] { "latest_reset", "scheduled_reset", "active_watch" })
        {
            var item = Get(data, name); var source = Get(item, "source");
            if (Str(source, "type") != "x_post" || Str(source, "author") != "thsottiaux") continue;
            var id = Str(item, "id") ?? Regex.Match(Str(source, "url") ?? "", @"/status/(\d+)").Groups[1].Value;
            var at = Date(item, "announced_at") ?? Date(item, "observed_at");
            if (!Regex.IsMatch(id, @"^\d{10,25}$") || at is null || Str(item, "text") is not { } text) continue;
            ResetSchedule? schedule = null;
            if (name == "scheduled_reset" && Str(item, "status") == "scheduled" && Str(item, "reset_type") is "regular" or "banked")
            {
                var by = Date(item, "scheduled_for");
                if (by < at || by > at.Value.AddDays(32)) by = null;
                schedule = new(Str(item, "reset_type") == "banked" ? ResetSignalKind.CreditGrant : ResetSignalKind.UsageReset, by);
            }
            posts.Add(new(id, "thsottiaux", text, at.Value, Schedule: schedule));
            if (name == "latest_reset") completed.Add(new(id, Str(item, "reset_type") == "banked" ? ResetSignalKind.CreditGrant : ResetSignalKind.UsageReset, at.Value));
            // Provider probabilities/averages are deliberately ignored; raw evidence is judged locally.
        }
        return new(posts, completed, generated.Value);
    }

    public static ResetPost? SyndicatedPost(JsonElement root)
    {
        var id = Str(root, "id_str"); var author = Str(Get(root, "user"), "screen_name"); var at = Date(root, "created_at"); var text = Str(root, "text");
        if (id is null || author is null || at is null || text is null) return null;
        var values = Get(Get(root, "card"), "binding_values");
        var choices = new List<PollChoice>();
        for (var i = 1; i <= 4; i++)
            if (Str(Get(values, $"choice{i}_label"), "string_value") is { } label)
                choices.Add(new(label, long.TryParse(Str(Get(values, $"choice{i}_count"), "string_value"), out var votes) ? votes : null));
        ResetPoll? poll = choices.Count == 0 ? null : new(choices, Date(Get(values, "end_datetime_utc"), "string_value"), Get(Get(values, "counts_are_final"), "boolean_value").ValueKind == JsonValueKind.True);
        return new(id, author, text, at.Value, WidgetContext(Get(root, "parent")), WidgetContext(Get(root, "quoted_tweet")), poll);
    }
    private static PostContext? WidgetContext(JsonElement root) => Str(root, "id_str") is { } id && Str(root, "text") is { } text
        ? new(id, Str(Get(root, "user"), "screen_name") ?? "unknown", text) : null;

    public static List<ResetPost> CommunityPosts(string html)
    {
        var result = new List<ResetPost>();
        // Read bounded literal objects from the public server-rendered page. Never execute page JavaScript.
        foreach (Match marker in Regex.Matches(html, @"\{assessmentConfidence:", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            var literal = BalancedObject(html, marker.Index);
            if (literal is null) continue;
            try
            {
                using var doc = JsonDocument.Parse(LiteralToJson(literal)); var root = doc.RootElement;
                if (Str(root, "handle") is not { } author || Str(root, "id") is not { } id || !Regex.IsMatch(id, @"^\d{10,25}$") || Date(root, "createdAt") is not { } at || Str(root, "text") is not { } text) continue;
                result.Add(new(id, author.TrimStart('@'), text, at, CommunityContext(Get(root, "replyTo")), CommunityContext(Get(root, "quotedPost"))));
            }
            catch (JsonException) { }
            if (result.Count >= 600) break;
        }
        return result;
    }
    private static PostContext? CommunityContext(JsonElement root) => Str(root, "id") is { } id && Str(root, "text") is { } text
        ? new(id, (Str(root, "handle") ?? "unknown").TrimStart('@'), text) : null;

    public static CommunityResetForecast? CommunityForecast(string html, DateTimeOffset now)
    {
        // Match only the current snapshot and its forecast, never a history entry or animated "0%" text.
        var snapshot = Regex.Match(html, @"snapshot:(?:\$R\[\d+\]=)?\{status:""[^""]*"",updatedAt:""([^""]+)"",forecastStatus:""current""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!snapshot.Success || !DateTimeOffset.TryParse(snapshot.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var updated)) return null;
        var marker = Regex.Match(html, @"\bforecast:(?:\$R\[\d+\]=)?\{", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!marker.Success || marker.Index < snapshot.Index) return null;
        var literal = BalancedObject(html, marker.Index + marker.Length - 1);
        if (literal is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(LiteralToJson(literal));
            var root = doc.RootElement;
            if (Get(root, "score24h").ValueKind != JsonValueKind.Number || Get(root, "score48h").ValueKind != JsonValueKind.Number
                || !Get(root, "score24h").TryGetDouble(out var score24) || !Get(root, "score48h").TryGetDouble(out var score48)) return null;
            var forecast = new CommunityResetForecast(score24, score48, updated, now,
                Str(Get(root, "calculationBreakdown"), "method") ?? "unknown", Str(root, "semanticSummary") ?? "");
            return forecast.IsCurrent(now) ? forecast : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? BalancedObject(string text, int start)
    {
        var depth = 0; var quoted = false; var escaped = false;
        for (var i = start; i < text.Length && i - start < 40_000; i++)
        {
            var c = text[i];
            if (quoted) { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; continue; }
            if (c == '"') quoted = true;
            else if (c == '{') depth++;
            else if (c == '}' && --depth == 0) return text[start..(i + 1)];
        }
        return null;
    }
    private static string LiteralToJson(string input)
    {
        var output = new StringBuilder(); var quoted = false; var escaped = false;
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (quoted) { output.Append(c); if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; continue; }
            if (c == '"') { quoted = true; output.Append(c); continue; }
            if (c == '$' && input.AsSpan(i).StartsWith("$R["))
            {
                var end = input.IndexOf("]=" , i, StringComparison.Ordinal);
                if (end >= 0 && end - i < 12) { i = end + 1; continue; }
            }
            if (c == '!' && i + 1 < input.Length && input[i + 1] is '0' or '1') { output.Append(input[++i] == '0' ? "true" : "false"); continue; }
            if (char.IsLetter(c) || c == '_')
            {
                var end = i + 1;
                while (end < input.Length && (char.IsLetterOrDigit(input[end]) || input[end] == '_')) end++;
                var word = input[i..end];
                if (end < input.Length && input[end] == ':') output.Append('"').Append(word).Append('"'); else output.Append(word);
                i = end - 1; continue;
            }
            output.Append(c);
        }
        return output.ToString();
    }
}
