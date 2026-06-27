using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using ZendeskApi.Client;
using ZendeskApi.Client.Extensions;
using ZendeskApi.Client.Models;
using ZendeskApi.Client.Queries;

// Migration-progress tracker
// -------------------------------------------------------------------------
// Tracks the share of support tickets still attributed to the Legacy product,
// broken down by country and month, so you can measure whether the Tier-1
// "migrate customers off Legacy" effort is actually reducing ticket volume.
//
// It only READS from Zendesk (Search API, count-only requests).
//
// Usage:
//   export ZENDESK_URL="https://yoursubdomain.zendesk.com"
//   export ZENDESK_USERNAME="you@company.com/token"
//   export ZENDESK_TOKEN="..."
//   dotnet run --project samples/MigrationTracker [months]
// (Requires an agent/admin account; end-users get HTTP 403 from Search.)

var url = Environment.GetEnvironmentVariable("ZENDESK_URL");
var username = Environment.GetEnvironmentVariable("ZENDESK_USERNAME");
var token = Environment.GetEnvironmentVariable("ZENDESK_TOKEN");

if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("Set ZENDESK_URL, ZENDESK_USERNAME and ZENDESK_TOKEN environment variables first.");
    return 1;
}

var months = args.Length > 0 && int.TryParse(args[0], out var m) ? m : 6;
// Country tags used in this Zendesk instance.
var countries = (Environment.GetEnvironmentVariable("ZENDESK_COUNTRIES") ?? "dk,se,no,de,ch,uk")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
const string legacyTag = "legacy";

var services = new ServiceCollection();
services.AddZendeskClientWithHttpClientFactory(url, username, token);
var client = services.BuildServiceProvider().GetRequiredService<IZendeskClient>();

// Count tickets created in [from, to) for one country, and how many of those carry the legacy tag.
// NOTE: Zendesk search ORs repeated same-field keywords, so "tags:dk tags:legacy" does NOT AND.
// We therefore page the country's tickets (volume is well under the 1000-result cap) and count
// the legacy tag locally, which is exact.
async Task<(int Total, int Legacy)> CountForCountry(DateTime from, DateTime to, string country)
{
    int total = 0, legacy = 0, page = 1;
    while (true)
    {
        var response = await client.Search.SearchAsync<Ticket>(q =>
        {
            q.WithFilter("created", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), FilterOperator.GreaterThanOrEqual);
            q.WithFilter("created", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), FilterOperator.LessThan);
            q.WithFilter("tags", country);
        }, new PagerParameters { Page = page, PageSize = 100 });

        foreach (var t in response)
        {
            total++;
            if (t.Tags != null && t.Tags.Contains(legacyTag))
                legacy++;
        }

        if (response.NextPage == null)
            break;
        page++;
    }
    return (total, legacy);
}

// Build the list of month windows, oldest first, ending with the current (partial) month.
var firstOfThisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
var windows = Enumerable.Range(0, months)
    .Select(i => firstOfThisMonth.AddMonths(-(months - 1 - i)))
    .Select(start => (Label: start.ToString("yyyy-MM"), Start: start, End: start.AddMonths(1)))
    .ToList();

Console.WriteLine($"Legacy ticket share by country, last {months} months (created date)\n");
Console.Write($"{"month",-9}");
foreach (var c in countries) Console.Write(c.ToUpperInvariant().PadLeft(12));
Console.WriteLine("ALL".PadLeft(12));

using var csv = new StreamWriter("legacy_share_by_country.csv");
csv.WriteLine("month," + string.Join(",", countries.Select(c => $"{c}_total,{c}_legacy,{c}_legacy_pct")) + ",all_total,all_legacy,all_legacy_pct");

foreach (var w in windows)
{
    var line = new System.Text.StringBuilder($"{w.Label,-9}");
    var csvCells = new List<string> { w.Label };
    int allTotal = 0, allLegacy = 0;

    foreach (var c in countries)
    {
        var (total, legacy) = await CountForCountry(w.Start, w.End, c);
        allTotal += total;
        allLegacy += legacy;

        var pct = total > 0 ? 100.0 * legacy / total : 0;
        line.Append((total > 0 ? $"{pct,3:0}%/{total}" : "-").PadLeft(12));
        csvCells.Add($"{total},{legacy},{pct:0.0}");
    }

    var allPct = allTotal > 0 ? 100.0 * allLegacy / allTotal : 0;
    line.Append($"{allPct,3:0}%/{allTotal}".PadLeft(12));
    csvCells.Add($"{allTotal},{allLegacy},{allPct:0.0}");

    Console.WriteLine(line.ToString());
    csv.WriteLine(string.Join(",", csvCells));
}

Console.WriteLine("\nEach cell: legacy% (total tickets created). Lower legacy% over time = migration working.");
Console.WriteLine("Wrote legacy_share_by_country.csv");
return 0;
