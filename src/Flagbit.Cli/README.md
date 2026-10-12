# Flagbit CLI

The Flagbit CLI manages and evaluates flags through a running Flagbit HTTP API. It does not require the .NET SDK package in the server application.

Install the tool from your configured NuGet source:

```powershell
dotnet tool install --global Flagbit.Cli
```

Set the API address and an API key before running a command:

```powershell
$env:FLAGBIT_API_URL = "https://flags.example"
$env:FLAGBIT_API_KEY = "<your-management-or-evaluation-api-key>"
flagbit evaluate new-checkout --user user-123 --environment production --attribute plan=enterprise
```

Management keys can create, list, retrieve, enable, disable, and delete flags. Management and evaluation keys can evaluate flags. The command exits with a nonzero status for invalid configuration, request failures, malformed responses, timeouts, and cancellation.

The server owns flag definitions and evaluation. The CLI sends one HTTP request per command and does not cache results or provide an outage fallback.
