# Data Flow & Sequence Diagrams

This document details every major operation flow in Scarab with sequence diagrams.

---

## 1. Application Startup

```mermaid
sequenceDiagram
    participant Main as Program.cs
    participant Config as Configuration
    participant DbPool as DbPoolManager
    participant ResPool as ResultBackendManager
    participant Loader as TaskLoader
    participant Redis as Redis
    participant Worker as WorkerService
    participant HTTP as HTTP API

    Main->>Config: Load appsettings.json
    Main->>Config: Apply environment variable overrides
    Main->>Config: Parse command-line arguments

    Main->>DbPool: Initialize source DB connections
    loop For each Db.* config entry
        DbPool->>DbPool: Create connection factory (MSSQL/MySQL/PG)
        DbPool->>DbPool: Set pool size (MinPool=MaxIdle, MaxPool=MaxActive)
        DbPool->>DbPool: Ping() to validate connection
    end

    Main->>ResPool: Initialize result backends
    loop For each Results.* config entry
        ResPool->>ResPool: Create connection factory (MySQL/PG only)
        ResPool->>ResPool: Ping() to validate connection
        ResPool->>ResPool: Create SqlResultBackend with ResultsTable pattern
    end

    Main->>Redis: Connect to broker Redis
    Main->>Redis: Connect to state Redis

    Main->>Loader: LoadTasks(SqlDirectories)
    loop For each *.sql file
        Loader->>Loader: Parse file (goyesql format)
        loop For each named query
            Loader->>Loader: Extract tags (db, queue, results, raw, conc)
            Loader->>DbPool: Resolve tagged source DBs
            Loader->>ResPool: Resolve tagged result backends
            alt raw != 1
                Loader->>DbPool: Prepare statement on each tagged DB
            end
            Loader->>Loader: Register task in TaskCollection
        end
    end

    alt WorkerOnly == false
        Main->>HTTP: Start HTTP listener on configured address
    end

    Main->>Worker: Start WorkerService (IHostedService)
    Worker->>Worker: Spawn N concurrent dequeue loops
    Note over Worker: N = WorkerConcurrency
```

---

## 2. Schedule a Job (POST /tasks/{taskName}/jobs)

```mermaid
sequenceDiagram
    participant Client as HTTP Client
    participant API as HTTP API
    participant Core as ScarabCore
    participant Tasks as TaskCollection
    participant Redis as Redis (State)
    participant Broker as Redis (Broker)

    Client->>API: POST /tasks/get_report/jobs<br/>{"job_id":"rpt1","args":["USER1"]}
    API->>API: Validate Content-Length > 0
    API->>API: Deserialize JSON to JobReq
    API->>API: Validate job_id matches ^[a-zA-Z0-9\-_:]+$

    API->>Core: NewJob(jobReq, "get_report")
    Core->>Tasks: Lookup task "get_report"
    alt Task not found
        Core-->>API: Error: "unrecognized task: get_report"
        API-->>Client: 500 {"status":"error","message":"unrecognized task: get_report"}
    end

    Core->>Redis: GetJob(job_id) — check if already running
    alt Job already running (STARTED/PROCESSING)
        Core-->>API: Error: "job 'rpt1' is already running"
        API-->>Client: 500 {"status":"error","message":"job 'rpt1' is already running"}
    end

    alt job_id is empty
        Core->>Core: Generate UUID: "job_xxxxxxxx-xxxx-..."
    end

    Core->>Core: Resolve queue (request → task → default)
    Core->>Core: Resolve TTL (request → default)
    Core->>Core: Parse ETA if provided
    Core->>Core: Serialize job payload (taskName, args, db, ttl)

    Core->>Broker: LPUSH queue:{queueName} {serialized_job}
    Core->>Redis: HSET job:{jobId} status=PENDING, task=get_report, queue=...

    Core-->>API: JobResp{job_id, task, queue, eta, retries}
    API-->>Client: 200 {"status":"success","data":{"job_id":"rpt1","task":"get_report",...}}
```

---

## 3. Job Execution (Worker Dequeue & Execute)

```mermaid
sequenceDiagram
    participant Worker as Worker Thread
    participant Broker as Redis (Broker)
    participant State as Redis (State)
    participant Core as ScarabCore
    participant SrcDB as Source DB
    participant ResBackend as Result Backend
    participant ResDB as Result DB

    Worker->>Broker: BRPOP queue:{queueName} (blocking)
    Broker-->>Worker: Serialized job payload

    Worker->>Worker: Deserialize: {taskName, jobId, args, db, ttl}
    Worker->>State: HSET job:{jobId} status=STARTED

    Worker->>Core: ExecJob(jobId, taskName, db, ttl, args, task)

    Core->>Core: Create CancellationTokenSource
    Core->>Core: Register CTS in jobCtx[jobId]

    Core->>State: GetJobStatus(jobId) — verify not cancelled
    alt Job was cancelled
        Core-->>Worker: Error: "the job was canceled"
    end

    alt Specific DB requested
        Core->>SrcDB: Get connection to named DB
    else Random DB
        Core->>SrcDB: Get random connection from task's DB pool
    end

    alt Prepared statement
        Core->>SrcDB: ExecuteReaderAsync(preparedStmt, args, cancellationToken)
    else Raw query
        Core->>SrcDB: ExecuteReaderAsync(rawSql, args, cancellationToken)
    end

    SrcDB-->>Core: DbDataReader (streaming rows)

    Core->>Core: WriteResults(jobId, task, ttl, reader)

    Core->>ResBackend: GetRandom() — pick a result backend
    Core->>ResBackend: NewResultSet(jobId, taskName, ttl)
    ResBackend-->>Core: IResultSet (with transaction)

    Core->>Core: reader.GetColumnSchema()
    alt Column types not yet registered for this task
        Core->>ResBackend: RegisterColTypes(columns, columnTypes)
        Note over ResBackend: Generate CREATE TABLE schema<br/>Cache for future jobs of same task
    end

    Core->>ResDB: DROP TABLE IF EXISTS results_{jobId}
    Core->>ResDB: CREATE TABLE results_{jobId} (auto-schema)

    loop For each row in reader
        Core->>Core: reader.Read()
        Core->>ResDB: INSERT INTO results_{jobId} VALUES (...)
    end

    Core->>ResDB: COMMIT transaction (Flush)
    Core->>Core: Remove CTS from jobCtx[jobId]

    Core-->>Worker: rowCount = N

    Worker->>State: HSET job:{jobId} status=SUCCESS result=N

    alt Execution failed
        Worker->>State: HSET job:{jobId} status=FAILURE error="..."
        alt Retries remaining > 0
            Worker->>State: HSET job:{jobId} status=RETRY
            Worker->>Broker: LPUSH queue:{queueName} {job with retries-1}
        end
    end
```

---

## 4. Check Job Status (GET /jobs/{jobId})

```mermaid
sequenceDiagram
    participant Client as HTTP Client
    participant API as HTTP API
    participant Core as ScarabCore
    participant State as Redis (State)

    Client->>API: GET /jobs/rpt1
    API->>Core: GetJobStatus("rpt1")

    Core->>State: HGETALL job:rpt1
    alt Job not found
        Core-->>API: Error: "job not found"
        API-->>Client: 404 {"status":"error","message":"job not found"}
    end

    State-->>Core: {status, task, error, ...}

    Core->>State: HGET job:rpt1 result
    State-->>Core: "42" (row count, may be empty if still running)

    Core->>Core: Map internal status to public state
    Note over Core: StatusStarted → PENDING<br/>StatusProcessing → STARTED<br/>StatusDone → SUCCESS<br/>StatusFailed → FAILURE<br/>StatusRetrying → RETRY

    Core-->>API: JobStatusResp{job_id, state, count, error}
    API-->>Client: 200 {"status":"success","data":{"job_id":"rpt1","state":"SUCCESS","count":42,"error":""}}
```

---

## 5. Schedule a Job Group (POST /groups)

```mermaid
sequenceDiagram
    participant Client as HTTP Client
    participant API as HTTP API
    participant Core as ScarabCore
    participant Broker as Redis (Broker)
    participant State as Redis (State)

    Client->>API: POST /groups<br/>{"group_id":"grp1","concurrency":3,"jobs":[...]}
    API->>API: Deserialize GroupReq

    API->>Core: NewJobGroup(groupReq)

    loop For each job in groupReq.Jobs
        Core->>Core: makeJob(job, job.TaskName)
        Note over Core: Same validation as single job:<br/>task exists, ID format, not already running
    end

    Core->>State: HSET group:grp1 status=PENDING
    Core->>State: SADD group:grp1:jobs job1 job2 ...

    loop For each job
        Core->>Broker: LPUSH queue:{queueName} {serialized_job}
        Core->>State: HSET job:{jobId} status=PENDING group=grp1
    end

    Core-->>API: GroupResp{group_id, jobs:[JobResp,...]}
    API-->>Client: 200 {"status":"success","data":{"group_id":"grp1","jobs":[...]}}
```

---

## 6. Check Group Status (GET /groups/{groupId})

```mermaid
sequenceDiagram
    participant Client as HTTP Client
    participant API as HTTP API
    participant Core as ScarabCore
    participant State as Redis (State)

    Client->>API: GET /groups/grp1
    API->>Core: GetJobGroupStatus("grp1")

    Core->>State: HGETALL group:grp1
    alt Group not found
        Core-->>API: Error: "group not found"
        API-->>Client: 404 {"status":"error","message":"group not found"}
    end

    Core->>State: SMEMBERS group:grp1:jobs
    State-->>Core: ["job1", "job2", "job3"]

    loop For each job ID
        Core->>Core: GetJobStatus(jobId)
        Core->>State: HGETALL job:{jobId}
    end

    Core->>Core: Derive group state from job states
    Note over Core: All SUCCESS → SUCCESS<br/>Any FAILURE → FAILURE<br/>Any STARTED → STARTED<br/>Otherwise → PENDING

    Core-->>API: GroupStatusResp{group_id, state, jobs:[...]}
    API-->>Client: 200 {"status":"success","data":{"group_id":"grp1","state":"SUCCESS","jobs":[...]}}
```

---

## 7. Cancel a Job (DELETE /jobs/{jobId})

```mermaid
sequenceDiagram
    participant Client as HTTP Client
    participant API as HTTP API
    participant Core as ScarabCore
    participant State as Redis (State)
    participant Worker as Worker Thread
    participant SrcDB as Source DB

    Client->>API: DELETE /jobs/rpt1?purge=false
    API->>Core: CancelJob("rpt1", purge=false)

    Core->>Core: GetJobStatus("rpt1")
    alt purge=false AND state is SUCCESS or FAILURE
        Core-->>API: Error: "can't cancel completed job"
        API-->>Client: 500 {"status":"error","message":"can't cancel completed job"}
    end

    Core->>Core: Lookup jobCtx["rpt1"]
    alt Job is currently running
        Core->>Core: CancellationTokenSource.Cancel()
        Note over Worker,SrcDB: CancellationToken triggers<br/>DbCommand.Cancel()<br/>(MSSQL/PG cancel mid-flight,<br/>MySQL continues server-side)
    end

    Core->>State: DEL job:rpt1
    Core-->>API: true
    API-->>Client: 200 {"status":"success","data":true}
```

---

## 8. Cancel a Group (DELETE /groups/{groupId})

```mermaid
sequenceDiagram
    participant Client as HTTP Client
    participant API as HTTP API
    participant Core as ScarabCore
    participant State as Redis (State)

    Client->>API: DELETE /groups/grp1?purge=false
    API->>Core: CancelJobGroup("grp1", purge=false)

    Core->>Core: GetJobGroupStatus("grp1")
    alt purge=false AND group is completed
        Core-->>API: Error: "can't delete group as it's already complete"
        API-->>Client: 500 {"status":"error","message":"..."}
    end

    Core->>State: SMEMBERS group:grp1:jobs
    State-->>Core: ["job1", "job2"]

    loop For each job in group
        Core->>Core: Cancel CTS if running
        Core->>State: DEL job:{jobId}
    end

    Core->>State: DEL group:grp1
    Core->>State: DEL group:grp1:jobs

    Core-->>API: true
    API-->>Client: 200 {"status":"success","data":true}
```

---

## 9. Result Table Lifecycle

```mermaid
sequenceDiagram
    participant Worker as Worker
    participant ResBackend as SqlResultBackend
    participant ResDB as Result DB

    Note over Worker: Job "rpt1" executing task "get_report"

    Worker->>ResBackend: NewResultSet("rpt1", "get_report", ttl=60s)
    ResBackend->>ResDB: BEGIN TRANSACTION
    ResBackend-->>Worker: IResultSet

    Worker->>ResBackend: RegisterColTypes(["region","total","date"], [VARCHAR,DECIMAL,DATE])
    Note over ResBackend: Generate & cache schema:<br/>DROP TABLE IF EXISTS "results_rpt1";<br/>CREATE TABLE "results_rpt1" (<br/>  "region" VARCHAR(255),<br/>  "total" DECIMAL,<br/>  "date" DATE<br/>);

    Worker->>ResBackend: WriteCols(["region","total","date"])
    ResBackend->>ResDB: DROP TABLE IF EXISTS "results_rpt1"
    ResBackend->>ResDB: CREATE TABLE "results_rpt1" (...)

    loop For each result row
        Worker->>ResBackend: WriteRow(["North", 15000.50, "2024-01-15"])
        ResBackend->>ResDB: INSERT INTO "results_rpt1" ("region","total","date") VALUES ($1,$2,$3)
    end

    Worker->>ResBackend: Flush()
    ResBackend->>ResDB: COMMIT

    Worker->>ResBackend: Close()

    Note over ResDB: Table "results_rpt1" now available<br/>for direct querying by the application
```

---

## 10. Worker Retry Flow

```mermaid
sequenceDiagram
    participant Worker as Worker Thread
    participant State as Redis (State)
    participant Broker as Redis (Broker)
    participant SrcDB as Source DB

    Worker->>Broker: BRPOP queue:default
    Broker-->>Worker: Job{id="rpt1", retries=2, task="get_report", args=[...]}

    Worker->>State: HSET job:rpt1 status=STARTED
    Worker->>SrcDB: Execute query
    SrcDB-->>Worker: ERROR: connection timeout

    alt retries > 0
        Worker->>State: HSET job:rpt1 status=RETRY error="connection timeout"
        Worker->>Worker: Decrement retries: 2 → 1
        Worker->>Broker: LPUSH queue:default Job{id="rpt1", retries=1, ...}
        Note over Broker: Job re-queued for retry
    else retries == 0
        Worker->>State: HSET job:rpt1 status=FAILURE error="connection timeout"
        Note over State: Job permanently failed
    end
```

---

## 11. ETA (Scheduled) Job Flow

```mermaid
sequenceDiagram
    participant Client as HTTP Client
    participant API as HTTP API
    participant Core as ScarabCore
    participant Broker as Redis (Broker)
    participant Worker as Worker Thread

    Client->>API: POST /tasks/get_report/jobs<br/>{"job_id":"rpt1","eta":"2026-06-03 09:00:00","args":["U1"]}
    API->>Core: NewJob(req, "get_report")

    Core->>Core: Parse ETA: 2026-06-03 09:00:00
    Core->>Broker: ZADD queue:default:delayed {score=timestamp} {job_payload}
    Note over Broker: Job stored in sorted set with ETA as score

    Core-->>API: JobResp{job_id:"rpt1", eta:"2026-06-03T09:00:00"}
    API-->>Client: 200 {"status":"success","data":{...}}

    Note over Worker: Worker periodically checks delayed queue

    Worker->>Broker: ZRANGEBYSCORE queue:default:delayed -inf {now}
    alt ETA has passed
        Broker-->>Worker: Job payload
        Worker->>Broker: ZREM queue:default:delayed {job}
        Worker->>Broker: LPUSH queue:default {job}
        Note over Worker: Job now in regular queue,<br/>picked up by next BRPOP
    end
```
