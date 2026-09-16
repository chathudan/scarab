# Architecture & Design

## System Overview

Scarab is a distributed SQL job server. It sits between your application and your databases, accepting report generation requests via HTTP, queuing them, executing SQL queries asynchronously via background workers, and writing results to ephemeral result databases.

High-level component view:

![Scarab high-level architecture](image-1.png)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Scarab Instance                            │
│                                                                             │
│  ┌──────────────────────┐    ┌──────────────────────────────────────────┐   │
│  │    HTTP API Layer     │    │         Worker Service                   │   │
│  │  (ASP.NET Minimal)    │    │  (IHostedService / BackgroundService)    │   │
│  │                       │    │                                          │   │
│  │  GET  /tasks          │    │  ┌─────────┐ ┌─────────┐ ┌─────────┐   │   │
│  │  POST /tasks/x/jobs   │    │  │Worker #1│ │Worker #2│ │Worker #N│   │   │
│  │  GET  /jobs/x         │    │  └────┬────┘ └────┬────┘ └────┬────┘   │   │
│  │  DELETE /jobs/x       │    │       │           │           │         │   │
│  │  POST /groups         │    │       └─────────┬─┘───────────┘         │   │
│  │  GET  /groups/x       │    │                 │                        │   │
│  │  DELETE /groups/x     │    │          ┌──────▼──────┐                 │   │
│  │  GET  /jobs/queue/x   │    │          │ JobExecutor │                 │   │
│  └──────────┬────────────┘    │          └──────┬──────┘                 │   │
│             │                 │                 │                         │   │
│  ┌──────────▼─────────────────▼─────────────────▼──────────────────────┐ │   │
│  │                        ScarabCore                                   │ │   │
│  │                                                                     │ │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌───────────────────────────┐ │ │   │
│  │  │ TaskRegistry  │  │ DbPoolManager│  │ ResultBackendManager     │ │ │   │
│  │  │ (SQL Tasks)   │  │ (Source DBs) │  │ (Result DBs)             │ │ │   │
│  │  └──────────────┘  └──────────────┘  └───────────────────────────┘ │ │   │
│  └─────────────────────────────────────────────────────────────────────┘ │   │
│                                                                             │
└──────────┬──────────────────────────────────┬───────────────────────────────┘
           │                                  │
     ┌─────▼─────┐                     ┌──────▼──────┐
     │   Redis    │                     │ Source DBs  │
     │ (Broker +  │                     │ MSSQL/MySQL │
     │  State)    │                     │  /Postgres  │
     └───────────┘                     └──────┬──────┘
                                              │ (query results)
                                       ┌──────▼──────┐
                                       │ Result DBs  │
                                       │ MySQL /     │
                                       │ Postgres    │
                                       └─────────────┘
```

---

## Component Breakdown

### 1. HTTP API Layer (`Program.cs`)

Thin ASP.NET Core Minimal API layer that:
- Maps HTTP routes to `ScarabCore` methods
- Validates input (job ID format, request body presence)
- Serializes responses using the standard `ApiResponse` envelope
- Started conditionally — skipped in `--worker-only` mode

**No business logic in this layer.** All operations delegate to `ScarabCore`.

Current routes include:
- `GET /tasks`
- `POST /tasks/{taskName}/jobs`
- `GET /jobs/{jobId}`
- `GET /jobs/{jobId}/data` (supports `limit`, `all=true`, and `page/page_size`)
- `GET /jobs/queue/{queue}`
- `DELETE /jobs/{jobId}`
- `POST /groups`
- `GET /groups/{groupId}`
- `DELETE /groups/{groupId}`

### 2. ScarabCore (`Core/ScarabCore.cs`)

The central orchestrator. Holds references to:
- `TaskCollection` — all registered SQL tasks
- `DbPoolManager` — source database connections
- `ResultBackendManager` — result database backends
- `RedisJobBroker` — the job queue
- `RedisJobStateStore` — job/group state persistence
- `ConcurrentDictionary<string, CancellationTokenSource>` — active job cancellation tokens

**Public methods** (called by HTTP API):
- `GetTasks()` — return registered tasks
- `NewJob(JobReq, taskName)` — validate task, create job, enqueue to Redis
- `NewJobGroup(GroupReq)` — create multiple jobs, enqueue as a group
- `GetJobStatus(jobId)` — read status from Redis state store
- `GetJobData(jobId, limit)` — return first N rows for a completed job
- `GetAllJobData(jobId)` — return all rows for a completed job
- `GetJobDataPage(jobId, page, pageSize)` — return paginated rows and total count
- `GetJobGroupStatus(groupId)` — aggregate status of all jobs in a group
- `GetPendingJobs(queue)` — list pending jobs in a queue
- `CancelJob(jobId, purge)` — cancel running job, delete from queue
- `CancelJobGroup(groupId, purge)` — cancel all jobs in group

**Internal methods** (called by workers):
- `ExecJob(jobId, taskName, dbName, ttl, args, task)` — execute SQL, write results
- `WriteResults(jobId, task, ttl, rows)` — stream result rows to result backend

### 3. Task Loader (`Core/TaskLoader.cs`)

Parses `.sql` files using a custom goyesql-compatible parser:
1. Scans all `.sql` files in configured directories
2. Extracts named queries with metadata tags (`-- name:`, `-- db:`, `-- queue:`, `-- results:`, `-- raw:`, `-- conc:`)
3. Validates queries against source databases (prepares statements unless `raw: 1`)
4. Registers tasks in `TaskCollection`

### 4. Database Pool Manager (`DbPool/DbPoolManager.cs`)

Manages named `DbConnection` factories for source databases:
- Creates connection pools per named DB config from `appsettings.json`
- Supports MSSQL (`Microsoft.Data.SqlClient`), MySQL (`MySqlConnector`), PostgreSQL (`Npgsql`)
- Provides `Get(name)` for specific DB and `GetRandom()` for random selection
- Validates connections at startup via `Ping()`

### 5. Result Backend (`ResultBackends/`)

Writes job results to MySQL or PostgreSQL result databases:
- **`IResultBackend`** — creates new result sets (one per job)
- **`IResultSet`** — writes columns and rows to a dedicated result table
- Supports reading result rows (`ReadJobData`) and row counting (`CountJobData`) for API data retrieval and pagination
- Auto-generates `CREATE TABLE` schemas from query column metadata
- Caches table schemas per task name to avoid regeneration
- Supports PostgreSQL `UNLOGGED` tables for performance

### 6. Redis Job Queue (`Queue/`)

Two Redis-backed components:

**`RedisJobBroker`** — Job queuing via Redis Lists:
- `Enqueue(job)` → `LPUSH queue:{queueName} serialized_job`
- `Dequeue(queueName)` → `BRPOP queue:{queueName}` (blocking pop)
- `GetPending(queue)` → `LRANGE queue:{queueName} 0 -1`

**`RedisJobStateStore`** — Job state via Redis Hashes:
- `SetStatus(jobId, status)` → `HSET job:{jobId} status {status}`
- `GetStatus(jobId)` → `HGET job:{jobId} status`
- `SetResult(jobId, rowCount)` → `HSET job:{jobId} result {count}`
- `Delete(jobId)` → `DEL job:{jobId}`
- State entries have configurable TTL via `ExpirySeconds`

**Group state:**
- `HSET group:{groupId} status {status}`
- `SADD group:{groupId}:jobs {jobId1} {jobId2} ...`
- Group status derived from aggregate of individual job statuses

### 7. Worker Service (`Queue/WorkerService.cs`)

`BackgroundService` (IHostedService) that:
1. Spawns `WorkerConcurrency` number of concurrent Task loops
2. Each loop: `BRPOP` from the configured queue → deserialize → call `ScarabCore.ExecJob()`
3. On success: set state to `SUCCESS`, store row count
4. On failure: set state to `FAILURE` (or `RETRY` if retries remain), re-enqueue
5. Supports `CancellationToken` for graceful shutdown
6. Each running job registers its `CancellationTokenSource` in `ScarabCore.jobCtx` for cancellation support

---

## Design Decisions

## Request And Execution Sequence

This sequence shows how a job is submitted, processed by workers, stored in result DBs, and then retrieved through status/data endpoints.

![Scarab request and execution sequence](image.png)

---

### Why No External Job Queue Library?

DungBeetle uses `tasqueue` (a lightweight Redis-based Go library). Instead of pulling in a heavy C# job framework like Hangfire or MassTransit, we implement the same lightweight Redis-based pattern directly using `StackExchange.Redis`. This keeps the solution:
- Single project / single binary
- Minimal dependencies
- Exact behavioral parity with DungBeetle

### Why ADO.NET Instead of Entity Framework?

We execute arbitrary user-defined SQL queries and need:
- Raw `DbDataReader` access for streaming large result sets
- `ColumnType` metadata for auto-schema generation
- No ORM mapping — results are dynamic and schema-free
- Support for prepared statements per DB type

### Why Minimal API Instead of Controllers?

- Lighter weight, matches DungBeetle's simple handler structure
- Single `Program.cs` file for all route definitions
- No ceremony — just map routes to core methods

### Why Connection Factories Instead of a Single Connection?

Each named DB config creates a connection factory with pooling:
- `MinPoolSize` = `MaxIdle` from config
- `MaxPoolSize` = `MaxActive` from config
- ADO.NET handles pool lifecycle automatically
- `GetRandom()` picks a random DB from the pool (load distribution for read replicas)

### Thread Safety

- `TaskCollection` is read-only after startup — no locking needed
- `ConcurrentDictionary<string, CancellationTokenSource>` for job cancellation tokens
- Result table schema cache uses `ReaderWriterLockSlim`
- Redis operations are inherently thread-safe via `StackExchange.Redis` multiplexer

---

## Deployment Topologies

### Single Instance (Simple)

```
[HTTP API + Workers] ──── [Redis] ──── [Source DBs] ──── [Result DBs]
```

### Multi-Instance with Priority Queues

```
[Instance 1: HTTP API + 30 workers on "high_priority"]
[Instance 2: Workers-only, 5 workers on "low_priority"]
[Instance 3: Workers-only, 10 workers on "default"]
          │
          └──── All connect to same [Redis] ────
```

### Scaling Strategy

- **Horizontal**: Add more worker-only instances connected to the same Redis
- **Queue-based priority**: Different queues with different concurrency
- **DB-level**: Tag specific queries to specific source DB replicas
- **Result isolation**: Multiple result backends for different workload types

---

## Error Handling Strategy

| Layer | Approach |
|-------|----------|
| HTTP API | Return structured error JSON with appropriate HTTP status codes |
| Job Execution | Catch exceptions, set job state to FAILURE with error message |
| Retries | Re-enqueue job with decremented retry count; state = RETRY |
| DB Connection | Connection pool handles transient failures; startup validation via Ping() |
| Redis | StackExchange.Redis handles reconnection automatically |
| Cancellation | CancellationToken propagation to DB commands; MySQL caveat documented |

---

## Security Considerations

- **No authentication** on the HTTP API by default (same as DungBeetle). Deploy behind a reverse proxy with auth.
- **SQL injection prevention**: Queries are pre-defined in `.sql` files; user input only fills parameterized placeholders.
- **Connection strings**: Stored in `appsettings.json`, overridable via environment variables. Never logged.
- **Result table names**: Derived from job IDs which are validated against `^[a-zA-Z0-9\-_:]+$` to prevent SQL injection in table names.