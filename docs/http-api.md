# HTTP API

Flagbit can be managed and evaluated through HTTP without the .NET SDK. The default local address is `http://localhost:5070`. PostgreSQL must be available and migrations applied before using flag endpoints. Configure and start the API as described in the [README](../README.md#api-keys).

Send `X-Api-Key` with every `/api/flags` and `/api/keys` request. Management keys can use all endpoints; configured evaluation keys and generated application keys can use only the two evaluation endpoints. `/health` and the Development-only `/openapi/v1.json` document do not require a key. `/health` checks database connectivity and returns `503` when PostgreSQL is unavailable. It does not verify or apply migrations: a reachable database without tables is healthy. Apply migrations explicitly before serving flag or application-key requests.

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
| `POST /api/keys` | `201`, application-key metadata and the generated secret, with `Cache-Control: no-store` |
| `GET /api/keys` | `200`, array of application-key metadata without secrets or hashes |
| `DELETE /api/keys/{id}` | `204`, no body; revokes the application key |

Application-key creation accepts `{ "name": "sample-app" }`. Names are trimmed and must contain 1–100 characters after trimming. Metadata contains `id`, `name`, and `createdAt`; only the creation response also contains `key`. Store that secret when it is returned. PostgreSQL stores its SHA-256 hash, and listing cannot retrieve the secret. Revocation takes effect on subsequent requests across API instances and survives restarts. An unknown or already revoked ID returns `404` Problem Details.

Keys are looked up case-insensitively. URL-encode keys used in request paths. Flag definitions include `key`, `isEnabled`, `targetedUserIds`, `rolloutPercentage`, `environments`, `rules`, `startsAt`, `endsAt`, and `dependencyKeys`.

Identifier matching follows .NET ordinal case-insensitive comparison. PostgreSQL stores application-normalized lookup values so its locale does not change key, target-user, environment, or dependency identity. Dotless `\u0131` remains distinct from `i`, and long `\u017f` remains distinct from `s`. Original spelling and whitespace are preserved, including the stored key used for rollout hashing.

New flag keys must be nonblank and cannot be exactly `.` or `..`, contain `/`, or contain a null character. These values cannot round-trip through the existing HTTP paths. Other punctuation, spaces, and Unicode are not restricted to a slug format. Encode the entire key as one path segment; a literal percent sign must also be encoded. Keys are not trimmed or renamed, and this creation validation does not rewrite existing definitions.

Creation accepts all these fields. Only `key` is required; `isEnabled` defaults to `false`. Updating `/evaluation` replaces all evaluation settings: omitted collections become empty and omitted rollout/schedule values become `null`. It does not rename the flag or change its enabled state. Send every setting you want to retain.

Enable/disable writes change only enabled state. Evaluation-setting writes replace settings atomically and preserve enabled state, including when the two operations overlap. Concurrent updates to evaluation settings still use full replacement semantics; the last completed settings write wins.

Required request bodies must be JSON objects with JSON-compatible field types. Missing bodies, JSON `null`, malformed JSON, and incorrect field types return `400`. Rollout percentages must be integer JSON numbers from 0 through 100, or `null`; numeric strings are rejected. Rule operators accept the documented names case-insensitively, with surrounding whitespace ignored; numeric enum aliases are rejected. Null rule entries, invalid operators, reversed schedules, and self-dependencies return `400`. Rejected updates preserve the existing flag and its settings.

Request cancellation flows through flag management, both evaluation endpoints, and application-key operations into persistence. Cancelled evaluation does not produce an ordinary disabled result, and request aborts are not reported as internal-server errors. Cancellation cannot undo a write that already committed.

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

The repository also includes [HTTP editor requests](../src/Flagbit.Api/Flagbit.Api.http). OpenAPI is available only when the API runs in Development. It describes flag and application-key request/response schemas, error responses, and the `X-Api-Key` header security scheme on protected operations. Operation descriptions distinguish management access from evaluation access. The document can be generated without a database connection.

## Evaluation behavior

An enabled flag must pass every configured restriction:

- `targetedUserIds`: requires a case-insensitive matching user when the list is nonempty.
- `rolloutPercentage`: accepts 0–100; a configured percentage requires a nonblank user ID, including at 100%. The bucket is the first four SHA-256 bytes, interpreted as an unsigned big-endian integer, modulo 100. The UTF-8 hash input is the stored flag key, a colon, and the exact supplied user ID. Evaluation succeeds when the bucket is less than the percentage. User-ID casing and whitespace affect assignment; caller key casing does not change the stored key used for hashing. Targeting does not bypass rollout.
- `environments`: requires a case-insensitive matching environment when the list is nonempty.
- `rules`: every rule must match an attribute. Operators are `Equals`, `NotEquals`, `Contains`, `StartsWith`, and `EndsWith`. A missing or null attribute fails every operator, including `NotEquals`. Attribute names and string comparisons are case-insensitive. Attribute names that differ only by case are rejected with `400`, even for disabled or missing flags, rather than selecting the first value.
- `startsAt` / `endsAt`: inclusive schedule bounds; either bound may be omitted. Valid offset timestamps are converted to UTC without changing the represented instant, and ordering is checked by instant. PostgreSQL persists microsecond precision; finer fractions are truncated by the provider, and evaluation uses the persisted bounds. Equal bounds match only that instant. Evaluation captures server UTC time once and passes it through the entire dependency traversal. The request has no client-time field.
- `dependencyKeys`: every dependency must evaluate to true with the same user, environment, attributes, and time. Dependency identity and self-dependency validation ignore case. Missing or disabled dependencies and dependency cycles return false; a dependency shared by separate branches is not a cycle.

Matching does not trim supplied user IDs, environment names, attribute names, or values. Configuration ignores blank target, environment, and dependency entries and removes case-insensitive duplicates, while preserving the spelling and whitespace of retained entries. Only the configured rule attribute name is trimmed; its value is preserved.

Missing flags evaluate to `200` with `isEnabled: false`. The legacy GET endpoint supplies only `userId`; use POST when environments or attributes are required.

## Error responses

Flag and application-key validation and application errors use `application/problem+json` in Development and non-Development environments. Handled exceptions contain `status`, `title`, `detail`, `instance`, and `traceId`, along with the standard `type` field. Creation can return either ordinary Problem Details or validation Problem Details with an `errors` dictionary; OpenAPI describes both shapes. For example, creating a duplicate key returns:

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
| `400` | Invalid body, field type, key, application-key name, rollout, rule, schedule, dependency, or ambiguous evaluation attributes. Invalid create keys and application-key names return validation Problem Details with `errors.key` or `errors.name`. |
| `401` | Missing, invalid, or multiple API keys; includes `WWW-Authenticate: ApiKey`. |
| `403` | An evaluation key attempted a management operation. |
| `404` | A management operation addressed an unknown flag or application-key ID. |
| `409` | A flag with the same case-insensitive key already exists. |
| `415` | A body endpoint received an unsupported media type; send `Content-Type: application/json`. |
| `500` | Unexpected failure, with a generic error message. |

Framework request exceptions retain their HTTP status, including `408` and `413`, instead of becoming `400`. Unexpected-error logs record the exception type, method, path, and trace identifier without raw exception messages. Configured and generated API-key secrets are not included in error responses or normal application logs.

Error results retain their Problem Details JSON body even when the caller sends a non-JSON `Accept` header.

Authentication responses (`401`/`403`) have no body. `/health` returns a health status rather than Problem Details. Inspect HTTP status before parsing a response body.
