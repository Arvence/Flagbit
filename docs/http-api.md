# HTTP API

Flagbit can be managed and evaluated through HTTP without the .NET SDK. The default local address is `http://localhost:5070`. PostgreSQL must be available and migrations applied before using flag endpoints. Configure and start the API as described in the [README](../README.md#api-keys).

Send `X-Api-Key` with every `/api/flags` request. Management keys can use all endpoints; evaluation keys can use only the two evaluation endpoints. `/health` and the Development-only `/openapi/v1.json` document do not require a key. `/health` checks database connectivity and returns `503` when PostgreSQL is unavailable.

## Endpoints

| Method and path | Success response |
| --- | --- |
| `GET /api/flags` | `200`, array of complete flag definitions |
| `GET /api/flags/{key}` | `200`, complete flag definition |
| `POST /api/flags` | `201`, complete flag definition and `Location` header |
| `PUT /api/flags/{key}/evaluation` | `200`, updated complete flag definition |
| `PUT /api/flags/{key}/enable` | `200`, enabled flag definition |
| `PUT /api/flags/{key}/disable` | `200`, disabled flag definition |
| `DELETE /api/flags/{key}` | `204`, no body |
| `POST /api/flags/{key}/evaluate` | `200`, `{ "key": "new-checkout", "isEnabled": true }` |
| `GET /api/flags/{key}/enabled?userId=user-123` | `200`, the same evaluation response with user-only context |

Keys are looked up case-insensitively. URL-encode keys used in request paths. Flag definitions include `key`, `isEnabled`, `targetedUserIds`, `rolloutPercentage`, `environments`, `rules`, `startsAt`, `endsAt`, and `dependencyKeys`.

Creation accepts all these fields. Only `key` is required; `isEnabled` defaults to `false`. Updating `/evaluation` replaces all evaluation settings: omitted collections become empty and omitted rollout/schedule values become `null`. It does not rename the flag or change its enabled state. Send every setting you want to retain.

## PowerShell walkthrough

With the API running, execute this in a second terminal. Replace the key placeholders with your configured keys. Use unused flag names, or delete flags from a previous run first.

```powershell
$baseUrl = "http://localhost:5070"
$managementHeaders = @{ "X-Api-Key" = "<your-management-api-key>" }
$evaluationHeaders = @{ "X-Api-Key" = "<your-evaluation-api-key>" }

Invoke-RestMethod -Uri "$baseUrl/health"
Invoke-RestMethod -Uri "$baseUrl/openapi/v1.json"

$dependency = @{ key = "http-demo-accounts"; isEnabled = $true } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$baseUrl/api/flags" -Headers $managementHeaders -ContentType "application/json" -Body $dependency

$settings = @{
    targetedUserIds = @("user-123")
    rolloutPercentage = 100
    environments = @("production")
    rules = @(@{ attribute = "plan"; operator = "Equals"; value = "enterprise" })
    startsAt = [DateTimeOffset]::UtcNow.AddDays(-1).ToString("o")
    endsAt = [DateTimeOffset]::UtcNow.AddDays(1).ToString("o")
    dependencyKeys = @("http-demo-accounts")
}
$flag = @{ key = "http-demo-checkout"; isEnabled = $true } + $settings
Invoke-RestMethod -Method Post -Uri "$baseUrl/api/flags" -Headers $managementHeaders -ContentType "application/json" -Body ($flag | ConvertTo-Json -Depth 5)
Invoke-RestMethod -Uri "$baseUrl/api/flags/http-demo-checkout" -Headers $managementHeaders

$context = @{
    userId = "user-123"
    environment = "production"
    attributes = @{ plan = "enterprise" }
} | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$baseUrl/api/flags/http-demo-checkout/evaluate" -Headers $evaluationHeaders -ContentType "application/json" -Body $context
# isEnabled is true.

$settings.rolloutPercentage = 0
Invoke-RestMethod -Method Put -Uri "$baseUrl/api/flags/http-demo-checkout/evaluation" -Headers $managementHeaders -ContentType "application/json" -Body ($settings | ConvertTo-Json -Depth 5)
Invoke-RestMethod -Method Post -Uri "$baseUrl/api/flags/http-demo-checkout/evaluate" -Headers $evaluationHeaders -ContentType "application/json" -Body $context
# isEnabled is now false.

Invoke-RestMethod -Method Delete -Uri "$baseUrl/api/flags/http-demo-checkout" -Headers $managementHeaders
Invoke-RestMethod -Method Delete -Uri "$baseUrl/api/flags/http-demo-accounts" -Headers $managementHeaders
```

The repository also includes [HTTP editor requests](../src/Flagbit.Api/Flagbit.Api.http). OpenAPI is available only when the API runs in Development. It describes the management/evaluation request and response schemas and documented error responses.

## Evaluation behavior

An enabled flag must pass every configured restriction:

- `targetedUserIds`: requires a matching user when the list is nonempty.
- `rolloutPercentage`: accepts 0–100; a configured percentage requires a user ID, including at 100%. Assignment is deterministic for a flag key and user ID. Targeting does not bypass rollout.
- `environments`: requires a matching environment when the list is nonempty.
- `rules`: every rule must match an attribute. Operators are `Equals`, `NotEquals`, `Contains`, `StartsWith`, and `EndsWith`. A missing attribute fails the rule. Attribute names and string comparisons are case-insensitive.
- `startsAt` / `endsAt`: inclusive schedule bounds expressed as timestamps with an offset; either bound may be omitted. Evaluation uses server UTC time. The request has no client-time field.
- `dependencyKeys`: every dependency must evaluate to true with the same context. Missing or disabled dependencies and dependency cycles return false.

Missing flags evaluate to `200` with `isEnabled: false`. The legacy GET endpoint supplies only `userId`; use POST when environments or attributes are required.

## Error responses

Flag validation and application errors use `application/problem+json`. Handled exceptions contain `status`, `title`, `detail`, `instance`, and `traceId`, along with the standard `type` field. For example, creating a duplicate key returns:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Feature flag already exists",
  "status": 409,
  "detail": "A feature flag with the key 'new-checkout' already exists.",
  "instance": "/api/flags",
  "traceId": "<request-trace-id>"
}
```

| Status | Meaning |
| --- | --- |
| `400` | Invalid key, rollout outside 0–100, invalid rule, reversed schedule, or self-dependency. A missing/blank create key returns validation Problem Details with an `errors.key` array. |
| `401` | Missing, invalid, or multiple API keys; includes `WWW-Authenticate: ApiKey`. |
| `403` | An evaluation key attempted a management operation. |
| `404` | A management operation addressed an unknown flag. |
| `409` | A flag with the same case-insensitive key already exists. |
| `500` | Unexpected failure, with a generic error message. |

Authentication responses (`401`/`403`) currently have no Problem Details body. `/health` returns a health status rather than Problem Details. Inspect HTTP status before parsing a response body.
