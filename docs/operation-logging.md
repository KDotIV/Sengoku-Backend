# Operation logging

Both active .NET 8 hosts use `Sengoku.Observability`: Provider API and Dispatcher API.
The retired Provider.Worker and EventTournamentScheduler executables remain retired.

## App Service configuration

Set these application settings on **both** App Services, then deploy the code:

| Setting | Value |
| --- | --- |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Connection string of the intended Application Insights resource |
| `OperationLogging__ServiceName` | `SengokuProvider.API` or `SengokuDispatcher.API` (defaults to the entry assembly name) |
| `OperationLogging__StorageDirectory` | Optional writable directory for the exporter's offline retry storage; use a separate directory for each host |

Do not commit connection strings or other credentials. With no connection string the application runs locally without exporting; startup logs a configuration warning. Tests inject an in-memory sink.

The private Azure Monitor OpenTelemetry logger sends `Sengoku.Operation` custom events. It does not attach the Azure exporter to the application's general `ILogger` pipeline. Application Insights sampling is not enabled in this package pipeline. Check resource-level ingestion sampling/daily caps separately if complete tracking is required.

App Service's automatic Application Insights agent is a **separate** telemetry path. Disable its automatic instrumentation for these hosts, or review its own collection/redaction rules before enabling it alongside this exporter. Otherwise it can duplicate requests and collect raw dependency URLs or exception messages independently of this policy. Existing console/file logging is also separate; this change does not forward that text to the new exporter.

The exporter provides batching and offline retries. This is operational telemetry, not a transactional audit store: process termination, exhausted SDK queues/storage, ingestion limits, network failures, or host disposal deadlines can still lose records. A synchronous sink failure increments `TelemetryHealth.FailedWrites` without failing the business operation; asynchronous exporter failures must be monitored through Azure Monitor exporter diagnostics. Application shutdown disposes the exporter through DI. There is no per-request flush.

References: [Azure Monitor exporter configuration and delivery behavior](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/monitor.opentelemetry.exporter-readme), [custom events](https://learn.microsoft.com/en-us/azure/azure-monitor/app/opentelemetry-add-modify), [Azure Monitor limits](https://learn.microsoft.com/en-us/azure/azure-monitor/service-limits).

## Package contract

| Field | Meaning |
| --- | --- |
| `SchemaVersion` | Version of the package contract, currently 1 |
| `OperationId` | Unique package ID for one request, message delivery, scheduled job, feed/outbox cycle, or standalone decorated service call |
| `TraceId` | W3C correlation across Provider, Dispatcher, and Service Bus executions |
| `SpanId`, `ParentSpanId` | Execution relationship within the distributed trace |
| `Title` | Server-owned controller/action, service/method, or job name |
| `StartTime`, `EndTime`, `DurationMs` | UTC timestamps and monotonic elapsed duration; end time exists on the final part |
| `Status` | `Succeeded`, `Failed`, `Cancelled`, `Incomplete`, `Pending`, or `CompletedWithFailures`; intermediate parts use `InProgress` |
| `PartIndex`, `IsFinal` | Zero-based record number and final summary marker |
| `StageCount`, `FailedStageCount` | Cumulative counts, authoritative on the final part |
| `Data` | JSON object of approved operation metadata |
| `Stages` | JSON array with stage ID, parent ID, completion sequence, title, timestamps, duration, status, and approved metadata |

Nested calls share the enclosing execution's package; they do not publish independent completed operation events. Concurrent children have separate stage IDs and the same enclosing package. `Sequence` is completion order; use `StartTime` to inspect overlapping work. A stage's parent is another stage ID or the package's `SpanId`.

Across process boundaries, each execution has its own package/final status and shares a `TraceId`. This distinguishes the Provider accepting work, the Dispatcher publishing it, and a worker completing it. Each broker delivery has its own `OperationId`, `MessageId`, and `DeliveryCount`. An outbox publish includes its persisted business `WorkflowId`; delayed/recovered outbox work can be joined by that ID even when the original HTTP trace has ended.

Small operations emit one final record. Large operations stream numbered parts, each with a bounded `Stages` property under Application Insights' 8 KB property limit and 64 KB item limit. Stages are not dropped to fit one record. Reconstruct by `(OperationId, PartIndex)`; deduplicate retransmissions on that pair. Only `IsFinal=true` is an operation's authoritative final status. Missing final parts indicate incomplete telemetry, not success. A process killed before its first part cannot emit an incomplete record.

`CompletedWithFailures` means execution returned normally but one or more recorded stages failed, were cancelled, or never completed. This includes recovered dependency failures and handled exceptions. `Pending` captures deferred bracket processing or retriable delivery. Empty queries and false predicates are not automatically failures. Explicit adapters classify business command results; a false send/update command is a failure.

## Coverage

- Middleware starts before authentication/authorization and captures controller responses, authentication failures, validation short circuits, exceptions, and cancellations. It never reads request/response bodies, URL query values, route parameter values, or authorization headers.
- `InstrumentOperations` wraps registered business interfaces in both libraries after registration, preserving lifetimes, disposal ownership, and sync/Task/ValueTask/async-stream results. New closed interfaces in the selected assemblies are picked up automatically. Factory interfaces are excluded; the services they return are decorated.
- The ambient context reaches all awaited services and PostgreSQL/HTTP/Service Bus activities. Dependency records contain type, timing, and outcome, never SQL text, parameters, connection strings, URLs, or response bodies.
- Explicit scopes cover account sessions, Start.gg OAuth, command validation, tournament construction/query/region resolution, worker message handlers, scheduled jobs, and Discord callbacks. Database commit/rollback calls and existing handled exception paths have safe stage markers.
- Tournament intake records build counts, inserts/updates, commit completion, region lookup results, and explicit region creation. **Current behavior:** normal event intake calls `GetRegionId` to look up a region; it does not automatically call `IntakeNewRegion`. Logging reports that behavior without changing the workflow.
- User packages include user ID/username when known, player ID where available, whether the request had an authenticated session, and the approved domain-separated one-way session fingerprint. Registration/login can enrich identity before an authenticated principal exists. Session creation/revocation is recorded separately from the incoming request's active-session state. Passwords, password hashes, email addresses, raw session cookies, and the database's session hash are excluded.
- Feed delivery results and bracket/league/tournament onboarding results have explicit adapters so failures returned as data are visible.

Concrete classes called outside a request, decorated interface, or explicit root do not invent a global logging destination. Start a root scope for those entry points. Direct `new` bypasses DI decoration. Unawaited work must create its own root boundary (`forceNew: true`); work that outlives a finished package is not appended to it. A root that returns while recorded children remain active is marked `Incomplete` with `Data.ActiveStageCount`. Open generic DI registrations and lazy synchronous enumerables need an explicit scope around actual execution/enumeration.

## New operations and fields

Use an interface registered in either business library for automatic method stages. Keep these registrations before the single `InstrumentOperations(...)` call in the host. Do not cast decorated interfaces to concrete implementations or another interface; register aliases against the shared concrete service, as with `EventQueryService`.

For a new concrete/background entry point, inject `OperationLogger`:

```csharp
await operations.RunAsync("Tournament.Rebuild", async () =>
{
    using var stage = OperationLog.Step("BuildTournamentData",
        OperationFields.Of(("TournamentId", tournamentId)));
    await BuildAsync();
    stage.Complete();
});
```

`Complete()` is explicit: disposing an unfinished scope records `Incomplete`. `Fail(exception)` captures only type and a code call site. `Reject("StableReasonCode")` represents validation/business failure; `Pending()` represents deferred completion. Log handled failures with `OperationLog.Failure(ex)` and known milestones with `OperationLog.Event(...)`. Mark a transaction committed only **after** a successful commit. Register an explicit typed result adapter in `OperationResultConfiguration` when a new result type expresses failure without throwing.

Metadata uses an allowlist rather than a denylist alone. Only approved scalar arguments and approved scalar DTO properties are projected; arbitrary objects and unknown fields are ignored. Do not put secrets into an approved field under a different name. New fields require an explicit review of relevance/privacy and a scalar formatter:

```csharp
services.AddOperationLogging(policy =>
    policy.Allow("TotalRounds", value => value is int rounds
        ? rounds.ToString(System.Globalization.CultureInfo.InvariantCulture) : null));
```

Register custom configuration on the first `AddOperationLogging` call. Sensitive names (including punctuation-separated aliases such as `api_key`) cannot be allowed. Metadata is bounded to 24 fields, 100 characters per scalar, and approximately 2 KB per stage/operation; `MetadataTruncated=true` flags capacity truncation. No full exception message, stack string, SQL payload, GraphQL payload, webhook URL, or serialized command/result is exported. When a new field's relevance is unclear, confirm it with the product owner before allowing it.

## Application Insights queries

Completed executions:

```kusto
customEvents
| where name == "Sengoku.Operation"
| extend OperationId=tostring(customDimensions.OperationId),
         Title=tostring(customDimensions.Title), Status=tostring(customDimensions.Status),
         Data=parse_json(tostring(customDimensions.Data))
| where tobool(customDimensions.IsFinal)
| summarize arg_max(timestamp, *) by OperationId
| project timestamp, OperationId, Title, Status,
          UserId=Data.UserId, Username=Data.Username, SessionActive=Data.SessionActive,
          DurationMs=todouble(customDimensions.DurationMs),
          FailedStages=tolong(customDimensions.FailedStageCount)
| order by timestamp desc
```

Every stage in one package, including oversized packages:

```kusto
customEvents
| where name == "Sengoku.Operation"
| extend OperationId=tostring(customDimensions.OperationId), Part=toint(customDimensions.PartIndex)
| where OperationId == "REPLACE_WITH_PACKAGE_ID"
| summarize arg_max(timestamp, *) by OperationId, Part
| mv-expand Stage=parse_json(tostring(customDimensions.Stages))
| project Sequence=tolong(Stage.Sequence), Title=tostring(Stage.Title),
          StartTime=todatetime(Stage.StartTime), DurationMs=todouble(Stage.DurationMs),
          Status=tostring(Stage.Status), Data=Stage.Data
| order by Sequence asc
```

To follow work across hosts, filter on `customDimensions.TraceId` instead. To follow sequential authenticated API calls, filter parsed `Data.SessionFingerprint` on final records. Do not paste raw session tokens into queries.

## Verification

Build the solution, then run `tests/Run-RegressionTests.ps1`. `OperationLogging.Tests` runs without Azure credentials or a database and checks isolation, concurrency, privacy, size bounds, correlation, middleware outcomes, dependency sanitization, interface semantics, and actual host service composition. Its exporter contract check sends through the real Azure SDK to a local fake ingestion endpoint and requires loopback networking. The existing regression harnesses run their database scenarios only when explicitly given an isolated database configuration. Live App Service ingestion must be verified after deployment by exercising a request and checking the final `Sengoku.Operation` event and any numbered parts.
