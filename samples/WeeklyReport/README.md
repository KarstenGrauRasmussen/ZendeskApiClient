# Weekly ticket report → Microsoft Teams

A read-only tool (built on `ZendeskApi.Client`) that summarises the last 7 days
of ticket inflow — volume, week-over-week change, Legacy share, a per-country
breakdown and the top recurring themes — and posts it to a Microsoft Teams
channel as an Adaptive Card. Intended to run weekly from GitHub Actions
(`.github/workflows/weekly-ticket-report.yml`).

## Run locally

```bash
export ZENDESK_URL="https://yoursubdomain.zendesk.com"
export ZENDESK_USERNAME="you@company.com/token"   # agent/admin account; end-users get HTTP 403
export ZENDESK_TOKEN="<api-token>"
export TEAMS_WEBHOOK_URL="<incoming-webhook-url>" # optional; if unset, writes teams_card.json instead
dotnet run --project samples/WeeklyReport
```

## Scheduled run (GitHub Actions)

The workflow runs every Monday 07:00 UTC (and on manual dispatch). Add these
repository **Secrets** (Settings → Secrets and variables → Actions):

| Secret | Value |
| --- | --- |
| `ZENDESK_URL` | `https://yoursubdomain.zendesk.com` |
| `ZENDESK_USERNAME` | `you@company.com/token` |
| `ZENDESK_TOKEN` | a fresh Zendesk API token (create a new one — don't reuse a pasted one) |
| `TEAMS_WEBHOOK_URL` | the incoming webhook URL for the target Teams channel |

### Getting the Teams webhook URL

Teams now uses **Workflows** for incoming webhooks (the older Office 365
Connector is being retired):

1. In the target channel: **⋯ → Workflows**.
2. Choose the template **"Post to a channel when a webhook request is received."**
3. Complete the steps; copy the generated **HTTP POST URL** → store it as `TEAMS_WEBHOOK_URL`.

The card is posted as `{"type":"message","attachments":[{ adaptive card }]}`,
which the Workflows trigger renders directly.

Optional: set `ZENDESK_COUNTRIES` (default `dk,se,no,de,ch,uk`) to change the
country tags broken out in the report.
