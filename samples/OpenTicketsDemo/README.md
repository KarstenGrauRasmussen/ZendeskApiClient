# OpenTicketsDemo

Minimal console sample showing how to use `ZendeskApi.Client` to list **open tickets**.

It reads credentials from environment variables (nothing hard-coded):

| Variable           | Example                              |
|--------------------|--------------------------------------|
| `ZENDESK_URL`      | `https://yoursubdomain.zendesk.com`  |
| `ZENDESK_USERNAME` | `you@company.com/token`              |
| `ZENDESK_TOKEN`    | your API token                       |

> Note the `/token` suffix on the username — Zendesk requires it when authenticating with an API token.

## Run

```bash
export ZENDESK_URL="https://yoursubdomain.zendesk.com"
export ZENDESK_USERNAME="you@company.com/token"
export ZENDESK_TOKEN="your_api_token"

dotnet run --project samples/OpenTicketsDemo
```

The sample uses the server-side Search API (`type:ticket status:open`). A commented-out
alternative in `Program.cs` shows listing tickets and filtering client-side instead.
