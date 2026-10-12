# Request map

This map follows the implemented HTTP paths. Sequence-diagram arrows are straight and show the request and response order.

## Evaluation request

```mermaid
sequenceDiagram
    participant Consumer as SDK, CLI, or SampleApi
    participant Api as Flagbit.Api
    participant Auth as API key handler
    participant KeyStore as EvaluationApiKeyStore
    participant Evaluator as FeatureFlagEvaluator
    participant Store as FeatureFlagStore
    participant Database as PostgreSQL

    Consumer->>Api: GET /api/flags/{key}/enabled or POST /evaluate
    Api->>Auth: Read X-Api-Key
    opt Generated evaluation key
        Auth->>KeyStore: Look up active key hash
        KeyStore->>Database: Query active key hash
        Database-->>KeyStore: Key result
        KeyStore-->>Auth: Authentication result
    end
    Auth-->>Api: Evaluation or management role
    Api->>Evaluator: Evaluate key and complete context
    Evaluator->>Store: Load flag and settings
    Store->>Database: Query persisted definition
    Database-->>Store: Flag, rules, dependencies, and settings
    Store-->>Evaluator: Domain model
    Evaluator-->>Api: Boolean result
    Api-->>Consumer: 200 { key, isEnabled }
```

The legacy GET endpoint accepts an optional `userId`. The contextual POST endpoint accepts `userId`, `environment`, and string attributes. Configured management and evaluation keys are checked from configuration; generated evaluation keys are hashed, stored in PostgreSQL, and checked on every request. Missing or invalid keys return `401`; an evaluation key used for management returns `403`.

## Management request

```mermaid
sequenceDiagram
    participant Client as CLI or HTTP client
    participant Api as Flagbit.Api
    participant Auth as API key handler
    participant Endpoint as Management endpoint
    participant Manager as FeatureFlagManager
    participant Store as FeatureFlagStore
    participant Database as PostgreSQL

    Client->>Api: Authenticated management request
    Api->>Auth: Require management role
    Auth-->>Api: Authorized request
    Api->>Endpoint: Bind and validate HTTP contract
    Endpoint->>Manager: Create, update, enable, disable, or delete
    Manager->>Store: Persist intended change
    Store->>Database: Apply PostgreSQL transaction
    Database-->>Store: Persisted entity
    Store-->>Manager: Domain model
    Manager-->>Endpoint: Updated result
    Endpoint-->>Client: Status code and JSON or Problem Details
```

Management endpoints create, list, retrieve, enable, disable, replace evaluation settings, and delete flags. API-key management creates, lists, and revokes generated evaluation keys. Updating evaluation settings replaces the submitted child collections while unrelated flag fields remain preserved. Validation, conflicts, and not-found behavior use the documented status codes and Problem Details responses.

## Endpoint access map

| Path | Required access | Purpose |
| --- | --- | --- |
| `GET /`, `GET /health` | None | Service status and PostgreSQL connectivity. Health does not apply or validate migrations. |
| `GET /openapi/v1.json` | None in Development | Generated OpenAPI document. |
| `GET /api/flags/{key}/enabled` | Evaluation or management key | User-only boolean evaluation. |
| `POST /api/flags/{key}/evaluate` | Evaluation or management key | Complete-context boolean evaluation. |
| `GET /api/flags`, `GET /api/flags/{key}`, `POST /api/flags` | Management key | Flag retrieval and creation. |
| `PUT /api/flags/{key}/evaluation`, `/enable`, `/disable`; `DELETE /api/flags/{key}` | Management key | Evaluation-setting replacement and flag lifecycle changes. |
| `POST /api/keys`, `GET /api/keys`, `DELETE /api/keys/{id}` | Management key | Generated evaluation-key lifecycle. |

## Startup request

```mermaid
sequenceDiagram
    participant Terminal as Developer terminal
    participant Script as start-local.ps1
    participant Compose as Docker Compose
    participant Database as PostgreSQL
    participant Ef as dotnet-ef
    participant Api as Flagbit.Api

    Terminal->>Script: Set configured keys and start script
    Script->>Compose: Start PostgreSQL and wait
    Compose->>Database: Start container and health check
    Database-->>Compose: Healthy
    Compose-->>Script: Ready
    Script->>Ef: Apply migration chain
    Ef->>Database: Create or upgrade schema
    Database-->>Ef: Migration result
    Ef-->>Script: Complete
    Script->>Api: Start native API process
    Script->>Api: Probe GET /health
    Api-->>Script: 200 Healthy
```

The script stops on Docker, tool-restore, build, migration, API-start, or health failures. It does not treat a reachable database as proof that migrations are current; migrations are applied before the health probe.
