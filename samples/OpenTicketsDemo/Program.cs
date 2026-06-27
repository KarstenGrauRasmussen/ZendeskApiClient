using Microsoft.Extensions.DependencyInjection;
using ZendeskApi.Client;
using ZendeskApi.Client.Extensions;
using ZendeskApi.Client.Models;

// ---- Credentials (read from environment so nothing is hard-coded) ----
//   ZENDESK_URL      e.g. https://yoursubdomain.zendesk.com
//   ZENDESK_USERNAME e.g. you@company.com/token   (Zendesk wants the "/token" suffix for API tokens)
//   ZENDESK_TOKEN    your API token
// Note: the Search/Tickets APIs require an agent or admin account; end-users get HTTP 403.
var url = Environment.GetEnvironmentVariable("ZENDESK_URL");
var username = Environment.GetEnvironmentVariable("ZENDESK_USERNAME");
var token = Environment.GetEnvironmentVariable("ZENDESK_TOKEN");

if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("Set ZENDESK_URL, ZENDESK_USERNAME and ZENDESK_TOKEN environment variables first.");
    return 1;
}

var services = new ServiceCollection();
services.AddZendeskClientWithHttpClientFactory(url, username, token);
var client = services.BuildServiceProvider().GetRequiredService<IZendeskClient>();

// ---- Page through ALL open tickets ----
// The Search API uses offset pagination: each response carries the grand total in
// Count and a non-null NextPage while more pages remain. We loop until NextPage is null.
const int pageSize = 100;
var openTickets = new List<Ticket>();
var page = 1;
var total = 0;

while (true)
{
    var response = await client.Search.SearchAsync<Ticket>(
        q => q.WithFilter("status", "open"),
        new PagerParameters { Page = page, PageSize = pageSize });

    total = response.Count;
    openTickets.AddRange(response);

    Console.Write($"\rFetched {openTickets.Count}/{total}...");

    if (response.NextPage == null)
        break;

    page++;
}

Console.WriteLine();
Console.WriteLine($"\nOpen tickets retrieved: {openTickets.Count} (Zendesk reports {total} total)\n");

// ---- Breakdown by priority ----
Console.WriteLine("By priority:");
foreach (var group in openTickets
             .GroupBy(t => t.Priority)
             .OrderByDescending(g => g.Count()))
{
    var label = group.Key?.ToString() ?? "(none)";
    Console.WriteLine($"  {label,-8} {group.Count()}");
}

// ---- Breakdown by assignee (resolve ids -> names in one batch call) ----
var assigneeIds = openTickets
    .Where(t => t.AssigneeId.HasValue)
    .Select(t => t.AssigneeId!.Value)
    .Distinct()
    .ToArray();

var names = new Dictionary<long, string>();
if (assigneeIds.Length > 0)
{
    var users = await client.Users.GetAllAsync(assigneeIds);
    foreach (var u in users)
        names[u.Id] = u.Name;
}

Console.WriteLine("\nTop assignees:");
foreach (var group in openTickets
             .GroupBy(t => t.AssigneeId)
             .OrderByDescending(g => g.Count())
             .Take(10))
{
    var label = group.Key.HasValue
        ? names.TryGetValue(group.Key.Value, out var n) ? n : $"user {group.Key}"
        : "(unassigned)";
    Console.WriteLine($"  {label,-30} {group.Count()}");
}

return 0;
