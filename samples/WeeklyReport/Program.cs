using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using ZendeskApi.Client;
using ZendeskApi.Client.Extensions;
using ZendeskApi.Client.Models;
using ZendeskApi.Client.Queries;

// Weekly support-ticket report -> Microsoft Teams
// -------------------------------------------------------------------------
// Computes last week's ticket inflow (volume, week-over-week delta, Legacy
// share and per-country breakdown, plus the top recurring themes) from the
// Zendesk Search API (read-only) and posts an Adaptive Card to a Teams
// incoming webhook. Designed to run unattended from GitHub Actions.
//
// Environment:
//   ZENDESK_URL, ZENDESK_USERNAME, ZENDESK_TOKEN   (required; agent/admin token)
//   TEAMS_WEBHOOK_URL                              (optional; if unset, prints the card JSON)
//   ZENDESK_COUNTRIES                              (optional; default "dk,se,no,de,ch,uk")

var url = Environment.GetEnvironmentVariable("ZENDESK_URL");
var username = Environment.GetEnvironmentVariable("ZENDESK_USERNAME");
var token = Environment.GetEnvironmentVariable("ZENDESK_TOKEN");
var webhook = Environment.GetEnvironmentVariable("TEAMS_WEBHOOK_URL");

if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("Set ZENDESK_URL, ZENDESK_USERNAME and ZENDESK_TOKEN.");
    return 1;
}

var countries = (Environment.GetEnvironmentVariable("ZENDESK_COUNTRIES") ?? "dk,se,no,de,ch,uk")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

var services = new ServiceCollection();
services.AddZendeskClientWithHttpClientFactory(url, username, token);
var client = services.BuildServiceProvider().GetRequiredService<IZendeskClient>();

string D(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
var today = DateTime.UtcNow.Date;
var weekStart = today.AddDays(-7);
var prevStart = today.AddDays(-14);

// Total ticket count created in [from,to) (single count request; type:ticket implied).
async Task<int> Total(DateTime from, DateTime to)
{
    var r = await client.Search.SearchAsync<Ticket>(q =>
    {
        q.WithFilter("created", D(from), FilterOperator.GreaterThanOrEqual);
        q.WithFilter("created", D(to), FilterOperator.LessThan);
    }, new PagerParameters { Page = 1, PageSize = 1 });
    return r.Count;
}

// Page one country's tickets for the window and return them (well under the 1000 cap weekly).
async Task<List<Ticket>> ByCountry(DateTime from, DateTime to, string country)
{
    var list = new List<Ticket>();
    var page = 1;
    while (true)
    {
        var r = await client.Search.SearchAsync<Ticket>(q =>
        {
            q.WithFilter("created", D(from), FilterOperator.GreaterThanOrEqual);
            q.WithFilter("created", D(to), FilterOperator.LessThan);
            q.WithFilter("tags", country);
        }, new PagerParameters { Page = page, PageSize = 100 });
        list.AddRange(r);
        if (r.NextPage == null) break;
        page++;
    }
    return list;
}

bool Has(Ticket t, string tag) => t.Tags != null && t.Tags.Contains(tag);

var weekTotal = await Total(weekStart, today);
var prevTotal = await Total(prevStart, weekStart);
var delta = prevTotal > 0 ? 100.0 * (weekTotal - prevTotal) / prevTotal : 0;

// Per-country breakdown
var rows = new List<(string C, int N, int Leg, int Err, int Esc)>();
var allTickets = new List<Ticket>();
foreach (var c in countries)
{
    var ts = await ByCountry(weekStart, today, c);
    allTickets.AddRange(ts);
    rows.Add((c.ToUpperInvariant(), ts.Count,
        ts.Count(t => Has(t, "legacy")),
        ts.Count(t => Has(t, "fejl") || Has(t, "bugserrors")),
        ts.Count(t => Has(t, "forwarded_to_2nd"))));
}
rows = rows.OrderByDescending(r => r.N).ToList();
var legAll = allTickets.Count > 0 ? 100.0 * allTickets.Count(t => Has(t, "legacy")) / allTickets.Count : 0;

// Top themes by subject keyword (multilingual; conservative)
var themes = new (string Name, Regex Rx)[]
{
    ("Year/season rollover", new Regex(@"l[äa]s[åa]r|sesong|semester|skole[åa]r|schuljahr|school year|re-?registr|reregistr|übertrag|flytta elev|semesterskifte", RegexOptions.IgnoreCase)),
    ("Invoicing / payments", new Regex(@"faktur|invoice|rechnung|gebühr|betaling|payment|worldpay|debit|opkr[æa]v|avgift", RegexOptions.IgnoreCase)),
    ("Reports / statistics", new Regex(@"statistik|statistics|rapport|report|lister?", RegexOptions.IgnoreCase)),
    ("Login / access", new Regex(@"unlock|locked|login|log in|password|kodeord|adgang", RegexOptions.IgnoreCase)),
};
var themeCounts = themes
    .Select(th => (th.Name, Count: allTickets.Count(t => th.Rx.IsMatch(t.Subject ?? ""))))
    .Where(x => x.Count > 0)
    .OrderByDescending(x => x.Count)
    .ToList();

// ---- Build Adaptive Card ----
var body = new List<object>
{
    new { type = "TextBlock", size = "Large", weight = "Bolder", text = "📊 Weekly Support Ticket Report" },
    new { type = "TextBlock", isSubtle = true, spacing = "None", text = $"{D(weekStart)} → {D(today)} (last 7 days)" },
    new
    {
        type = "ColumnSet",
        spacing = "Medium",
        columns = new object[]
        {
            StatColumn(weekTotal.ToString(), "new tickets"),
            StatColumn($"{(delta >= 0 ? "+" : "")}{delta:0}%", "vs prev week", delta > 5 ? "Attention" : delta < -5 ? "Good" : "Default"),
            StatColumn($"{legAll:0}%", "on Legacy", "Warning"),
        }
    },
    new { type = "TextBlock", weight = "Bolder", spacing = "Medium", text = "By country" },
    new
    {
        type = "FactSet",
        facts = rows.Where(r => r.N > 0).Select(r => new
        {
            title = r.C,
            value = $"{r.N}  ·  {(r.N > 0 ? 100 * r.Leg / r.N : 0)}% legacy  ·  {(r.N > 0 ? 100 * r.Err / r.N : 0)}% bug  ·  {(r.N > 0 ? 100 * r.Esc / r.N : 0)}% esc"
        }).ToArray()
    },
};
if (themeCounts.Count > 0)
{
    body.Add(new { type = "TextBlock", weight = "Bolder", spacing = "Medium", text = "Top recurring themes" });
    body.Add(new
    {
        type = "FactSet",
        facts = themeCounts.Select(x => new { title = x.Name, value = x.Count.ToString() }).ToArray()
    });
}
body.Add(new { type = "TextBlock", isSubtle = true, wrap = true, spacing = "Medium",
    text = "Source: Zendesk Search API (read-only). Theme counts are subject-keyword based (conservative)." });

var card = new
{
    type = "message",
    attachments = new object[]
    {
        new
        {
            contentType = "application/vnd.microsoft.card.adaptive",
            content = new
            {
                schema = "http://adaptivecards.io/schemas/adaptive-card.json",
                type = "AdaptiveCard",
                version = "1.4",
                body = body.ToArray()
            }
        }
    }
};

var json = JsonSerializer.Serialize(card, new JsonSerializerOptions { WriteIndented = true });
// The "$schema" property can't be a C# identifier; emit it with a rename.
json = json.Replace("\"schema\":", "\"$schema\":");
File.WriteAllText("teams_card.json", json);

Console.WriteLine($"Week {D(weekStart)}..{D(today)}: {weekTotal} tickets ({delta:+0;-0;0}% WoW), {legAll:0}% legacy");
foreach (var r in rows.Where(r => r.N > 0))
    Console.WriteLine($"  {r.C}: {r.N}  ({(r.N>0?100*r.Leg/r.N:0)}% legacy)");

if (string.IsNullOrWhiteSpace(webhook))
{
    Console.WriteLine("\nTEAMS_WEBHOOK_URL not set — wrote teams_card.json (not posted).");
    return 0;
}

using var http = new HttpClient();
var resp = await http.PostAsync(webhook, new StringContent(json, Encoding.UTF8, "application/json"));
Console.WriteLine($"\nPosted to Teams: HTTP {(int)resp.StatusCode} {resp.StatusCode}");
return resp.IsSuccessStatusCode ? 0 : 2;

static object StatColumn(string big, string label, string color = "Default") => new
{
    type = "Column",
    width = "stretch",
    items = new object[]
    {
        new { type = "TextBlock", size = "ExtraLarge", weight = "Bolder", color, text = big, horizontalAlignment = "Center" },
        new { type = "TextBlock", isSubtle = true, spacing = "None", text = label, horizontalAlignment = "Center", wrap = true },
    }
};
