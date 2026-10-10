# SDK consumer utility audit

## Outcome

The TODO 5 SDK already supports the useful consumer utilities: an evaluation context with optional named constructor arguments and caller-owned variation selection for both evaluation paths. No additional public utility is justified by TODO 5.5. This decision was recorded before any utility implementation; none was added.

The interrupted TODO 5 work was completed first, including cancellation during sending and response reading, pre-cancelled calls, HTTP timeouts, and integration with the real API using migrated PostgreSQL and generated-key revocation. The distinction matters: those changes complete the evaluation contract, rather than add consumer utilities.

## Reviewed public surface

The completed TODO 5 surface, retained unchanged by the utility audit, is:

```csharp
new FlagbitClient(HttpClient httpClient)
new FeatureFlagEvaluationContext(string? UserId = null, string? Environment = null, IReadOnlyDictionary<string, string>? Attributes = null)
Task<bool> IsEnabledAsync(string key, string? userId = null, CancellationToken cancellationToken = default)
Task<bool> EvaluateAsync(string key, FeatureFlagEvaluationContext context, CancellationToken cancellationToken = default)
Task<T> GetVariationAsync<T>(string key, T enabledVariation, T disabledVariation, string? userId = null, CancellationToken cancellationToken = default)
Task<T> GetContextualVariationAsync<T>(string key, T enabledVariation, T disabledVariation, FeatureFlagEvaluationContext context, CancellationToken cancellationToken = default)
```

The contextual methods have separate names so existing calls such as `IsEnabledAsync("checkout", null)` and `GetVariationAsync("checkout", "modern", "classic", null)` remain unambiguous. The HTTP response model and parsing details remain internal. No public type references API, Core, or Infrastructure types.

## Candidate decisions

Expected frequency below is an assessment of the existing consumer examples and SDK/API integration tests, not usage telemetry.

| Candidate | Consumer problem solved | Expected frequency | Implementation complexity | Hidden semantic or network cost | Decision |
| --- | --- | --- | --- | --- | --- |
| Context factories or builders | Construct user, environment, and attributes together | Common | Existing optional constructor is sufficient; factories would overlap it | Construction has no I/O, but more construction paths increase API choices | KEEP EXISTING |
| Contextual variation selection | Select caller-owned enabled/disabled values using full context | Common | Already implemented through `EvaluateAsync` | Exactly one evaluation request; values are never sent to the server | KEEP EXISTING |
| `IsDisabledAsync` | Invert an enabled result | Occasional | One trivial wrapper | Still performs a remote request; adds no value over `!await` | REJECT |
| Conditional callback execution | Run an application action when enabled | Application-specific | Requires decisions about callback cancellation, async results, and failures | Blurs remote evaluation and application execution; failure ordering becomes part of the SDK contract | REJECT |
| Any/all enabled checks | Compose several flag evaluations | No demonstrated repeated need | Requires ordering, concurrency, short-circuit, and partial-failure rules | Up to N requests for N flags; sequential latency accumulates, parallel work changes short-circuit behavior, and results are not a server snapshot | REJECT |
| Reusable context or attribute helpers | Reuse common context and vary selected fields | Common | Existing record and `with` expressions suffice | Construction has no I/O; attribute ownership must remain explicit | KEEP EXISTING |

No candidate is classified ADD. Permanent API surface is not warranted for an alias, an application control-flow policy, or an unproven multi-request abstraction.

## Consumer semantics

All four evaluation methods perform exactly one request per call: GET for user-only calls, POST for full context. Variation methods reuse the appropriate evaluation method and select a caller-owned value after a successful boolean result. They do not catch or convert authentication, HTTP, transport, invalid-response, timeout, or cancellation failures into a disabled result. Already-cancelled calls stop before invoking the transport.

The context record is reusable, but its attribute dictionary is a caller-owned reference. Record copies are shallow. Consumers can replace attributes in a `with` expression or pass a standard immutable/frozen dictionary when needed; there is no SDK-specific immutable collection, ambient context, or builder. This keeps allocation and ownership choices visible to the application.

Boolean inversion, conditional business actions, and deliberate multi-flag composition remain in application code. Applications choose whether several checks run sequentially or concurrently and how to handle failures; the SDK does not introduce those policies. No additional HTTP requests are introduced by this audit.

## Cleanup review

- `SendEvaluationAsync` provides one send/status/parse/validation path for both endpoints.
- GET URI construction and POST JSON construction remain distinct because they encode different contracts; another request abstraction would obscure that distinction.
- Each variation method delegates to its corresponding boolean method; there is no duplicate response parsing.
- Existing names and signatures are retained. There are no redundant contextual overloads, obsolete utility methods, public transport DTOs, client base classes, service locators, or DI dependencies to remove.
- The SDK runtime project remains independently referenceable. Only the existing API test project references both the server and SDK for boundary tests.

## Verification and remaining scope

The SDK tests cover both outcomes, complete context serialization, escaping, authentication headers, caller-owned `HttpClient` lifetime, source-compatible null calls, variation identity, request counts, invalid inputs/responses, HTTP and network failures, cancellation, timeout, and response disposal.

`SdkEvaluationTests` runs the SDK against a real Kestrel listener and the existing migrated PostgreSQL fixture. It verifies user targeting, rollout, environment and attribute restrictions, schedules, dependencies, current-state evaluation without caching, user-only GET/POST equivalence, caller-owned variations, generated-key permissions, and revocation across all SDK evaluation methods.

Exact executed counts and build results are reported with the task completion. No utility-specific test is needed because no additional utility was introduced.

CLI changes, SampleApi integration, broader documentation, and final end-to-end review remain in TODOs 6–9 according to their respective scopes. SampleApi should exercise the full context, demonstrate one evaluation per decision, and keep application behavior and error policy outside the SDK. The later documentation pass should preserve the explicit per-call network cost, context ownership, and failure distinctions documented here.

No server changes, migrations, batch endpoint, local evaluation, caching, retries, polling, streaming, telemetry, generated flag wrappers, callback framework, or registration framework were added.
