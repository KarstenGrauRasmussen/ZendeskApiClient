using Microsoft.Extensions.DependencyInjection;
using ZendeskApi.Client;
using ZendeskApi.Client.Extensions;
using ZendeskApi.Client.Models;

// ---- Credentials (read from environment so nothing is hard-coded) ----
//   ZENDESK_URL      e.g. https://yoursubdomain.zendesk.com
//   ZENDESK_USERNAME e.g. you@company.com/token   (Zendesk wants the "/token" suffix for API tokens)
//   ZENDESK_TOKEN    your API token
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
var provider = services.BuildServiceProvider();
var client = provider.GetRequiredService<IZendeskClient>();

// ---- Approach A: server-side Search (efficient; lets Zendesk do the filtering) ----
// Produces the query  type:ticket status:open
var search = await client.Search.SearchAsync<Ticket>(q =>
    q.WithFilter("status", "open"));

Console.WriteLine($"Open tickets (via Search API): {search.Count}");
foreach (var t in search)
{
    Console.WriteLine($"  #{t.Id}  [{t.Status}]  {t.Subject}");
}

// ---- Approach B: list tickets and filter client-side (handy if you can't use Search) ----
// var page = await client.Tickets.GetAllAsync();
// foreach (var t in page.Where(t => t.Status == TicketStatus.Open))
//     Console.WriteLine($"  #{t.Id}  {t.Subject}");

return 0;
