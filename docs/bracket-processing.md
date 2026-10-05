# Resumable bracket processing

Apply `database/migrations/001_bracket_processing.sql` to the Alexandria database
before deploying the updated API and worker. There is no automatic schema creation.
Both processes need access to the two new tables. Deploy both updated processes
together so that the worker recognizes the new resume command.

Use the existing `ServiceBusSettings:LegendReceivedQueue` and
`ServiceBusSettings:PlayerReceivedQueue`. No additional queue is required. Keep the
worker running: `BracketOutboxWorker` publishes committed commands and performs
recovery and cleanup. The API alone cannot progress pending operations.

`POST /api/players/OnboardBracketRunnerByBracketSlug` returns HTTP 202 when legends
are missing, with `Status = Pending`, `OperationId`, and a Location header pointing
to `GET /api/players/BracketProcessing/{operationId}`. Completion returns HTTP 200.
The status endpoint returns Pending, Completed, Failed, or Expired; unknown or
cleaned-up operation IDs return HTTP 404. Completed requests include the saved set
identifiers in `Successful`. A bracket with no opponents completes without inserting
an empty path. Singles brackets are supported; team entrants and incomplete/cyclic
bracket graphs are rejected instead of silently producing partial results.

The checkpoint records the original opponent snapshot, player card, metadata, and
finished matchup cards. Resumes query missing legends and build unfinished cards;
they do not refetch the bracket or traverse it again. Repeating the initial request
for the same tournament, bracket, and player reuses its operation, including its
terminal result, during retention. This deliberately freezes that request's bracket
snapshot. Refreshing changed live brackets or explicitly restarting an expired
operation is a separate future API capability.

Checkpoint changes and outgoing commands commit together in PostgreSQL. Final
matchup/path writes use that same transaction as checkpoint completion. Advisory
locks serialize requests/resumes across replicas. The publisher retries failed
sends with backoff. A crash after send but before marking an outbox row sent can
deliver duplicates; both onboarding and completion therefore tolerate replay.
MessageId is the outbox row ID; CorrelationId is the operation ID. Broker duplicate
detection is optional and is not needed for correctness.

Legends onboarding emits an immediate resume notification after persistence. A
fallback resume runs every two minutes while dependencies remain missing. If a
missing legend also lacks standings, tournament/player intake is requested. A
recovery sweep wakes stalled operations after five minutes if no outgoing commands
remain unsent, covering lost/dead-lettered resume messages. Operations expire after
24 hours; expired/completed operations stop scheduling intake. Checkpoints are
removed seven days after their expiration timestamp, and sent outbox rows are
removed after seven days. These fixed intervals live in the checkpoint model,
PlayerOperations, and BracketOutboxWorker.

Player queries treat HTTP 429 separately from ordinary failures: after three rate
limit responses on a page, they await the throttler cooldown, select a bearer not
yet exhausted for that page, and retry the same page with completed pages retained.
Each bearer gets three rate-limit attempts. An exhausted bearer pool stops the
invocation; other errors still stop after three attempts on the failed page.

Inspect `bracket_processing_checkpoints.status` and its JSON payload for progress;
inspect unsent `bracket_processing_outbox` rows (`attempts`, `last_error`,
`available_at`) for publish failures. Service Bus delivery failures still appear in
its dead-letter queues. The five-minute recovery sweep can retry pending operations
after fixing their underlying dependency without repeating bracket preparation.

Candidate matchup IDs have the format
`br:{bracketId}:{pathSetId}:{playerEntrantId}:{opponentEntrantId}`. These are synthetic
IDs for possible matchups, not upstream start.gg set IDs. Opponents that can appear
in multiple rounds get a separate card for each round.

Run the dependency-free regression executable:

```powershell
dotnet run --project tests/BracketWorkflow.Tests/BracketWorkflow.Tests.csproj
```

The default checks use fake data services and a transaction-modeling in-memory
checkpoint store. PostgreSQL integration checks are opt-in: set
`BRACKET_TEST_POSTGRES` to a dedicated disposable test database connection string.
The runner creates its own randomly named schema, applies the migration there,
tests real transaction rollback/concurrency/idempotency, and removes that schema.
It never connects to application settings or sends Service Bus messages.
