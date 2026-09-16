# Class & Interface Design

Complete class hierarchy, interface contracts, and property/method signatures for Scarab.

---

## Project Structure

```
Scarab/
├── Scarab.csproj
├── Program.cs                          # Entry point + Minimal API routes
├── appsettings.json                    # Configuration
│
├── Models/
│   ├── ApiResponse.cs                  # Standard HTTP envelope
│   ├── JobReq.cs                       # Inbound job request
│   ├── JobResp.cs                      # Job creation response
│   ├── JobStatusResp.cs                # Job status poll response
│   ├── GroupReq.cs                     # Inbound group request
│   ├── GroupResp.cs                    # Group creation response
│   └── GroupStatusResp.cs              # Group status poll response
│
├── Core/
│   ├── ScarabCore.cs                   # Central orchestrator
│   ├── TaskLoader.cs                   # SQL file parser
│   ├── SqlTask.cs                      # Single task definition
│   └── TaskCollection.cs              # Named task registry
│
├── DbPool/
│   ├── DbType.cs                       # Database type enum
│   ├── DbConfig.cs                     # Per-database configuration
│   ├── DbConnectionFactory.cs          # Creates typed connections
│   ├── DbPool.cs                       # Named pool of connection factories
│   └── DbPoolManager.cs               # Pool lifecycle manager
│
├── ResultBackends/
│   ├── IResultBackend.cs               # Result backend interface
│   ├── IResultSet.cs                   # Per-job result writer interface
│   ├── SqlResultBackend.cs             # MySQL/PG result backend
│   ├── SqlResultSet.cs                 # Per-job result writer impl
│   └── ResultBackendCollection.cs      # Named result backend registry
│
├── Queue/
│   ├── IJobBroker.cs                   # Job queue interface
│   ├── IJobStateStore.cs               # Job state interface
│   ├── RedisJobBroker.cs               # Redis List-based queue
│   ├── RedisJobStateStore.cs           # Redis Hash-based state
│   ├── JobMessage.cs                   # Serialized job on the wire
│   └── WorkerService.cs               # Background worker host
│
├── Config/
│   ├── AppConfig.cs                    # App section binding
│   ├── BrokerConfig.cs                 # JobQueue.Broker binding
│   ├── StateConfig.cs                  # JobQueue.State binding
│   ├── DbPoolConfig.cs                # Db section binding
│   └── ResultDbConfig.cs              # Results section binding
│
└── Sql/                                # SQL task files directory
    └── queries.sql                     # Example tasks
```

---

## Models

### `ApiResponse`

```csharp
public class ApiResponse
{
    public string Status { get; set; }        // "success" or "error"
    public string? Message { get; set; }       // Error message (null on success)
    public object? Data { get; set; }          // Response payload

    public static ApiResponse Success(object data);
    public static ApiResponse Error(string message);
}
```

### `JobReq`

```csharp
public class JobReq
{
    public string? TaskName { get; set; }      // "task" — used in group jobs
    public string? JobId { get; set; }         // "job_id"
    public string? Queue { get; set; }
    public string? Eta { get; set; }           // "yyyy-MM-dd HH:mm:ss"
    public int Retries { get; set; }
    public int Ttl { get; set; }
    public string[]? Args { get; set; }
    public string? Db { get; set; }            // Specific source DB name
}
```

### `JobResp`

```csharp
public class JobResp
{
    public string JobId { get; set; }          // "job_id"
    public string TaskName { get; set; }       // "task"
    public string Queue { get; set; }
    public DateTime? Eta { get; set; }
    public int Retries { get; set; }
}
```

### `JobStatusResp`

```csharp
public class JobStatusResp
{
    public string JobId { get; set; }          // "job_id"
    public string State { get; set; }          // PENDING, STARTED, SUCCESS, FAILURE, RETRY
    public int Count { get; set; }             // Number of result rows
    public string Error { get; set; }          // Error message (empty on success)
}
```

### `GroupReq`

```csharp
public class GroupReq
{
    public string? GroupId { get; set; }       // "group_id"
    public int Concurrency { get; set; }
    public List<JobReq> Jobs { get; set; }
}
```

### `GroupResp`

```csharp
public class GroupResp
{
    public string GroupId { get; set; }        // "group_id"
    public List<JobResp> Jobs { get; set; }
}
```

### `GroupStatusResp`

```csharp
public class GroupStatusResp
{
    public string GroupId { get; set; }        // "group_id"
    public string State { get; set; }          // Aggregate state
    public List<JobStatusResp> Jobs { get; set; }
}
```

---

## Configuration Binding Classes

### `AppConfig`

```csharp
public class AppConfig
{
    public string LogLevel { get; set; } = "Information";
    public int DefaultJobTtlSeconds { get; set; } = 60;
    public string Server { get; set; } = "0.0.0.0:6060";
    public string[] SqlDirectories { get; set; } = ["./Sql"];
    public string Queue { get; set; } = "default";
    public string WorkerName { get; set; } = "default";
    public int WorkerConcurrency { get; set; } = 10;
    public bool WorkerOnly { get; set; } = false;
}
```

### `BrokerConfig`

```csharp
public class BrokerConfig
{
    public string Type { get; set; } = "redis";
    public string[] Addresses { get; set; } = ["localhost:6379"];
    public string Password { get; set; } = "";
    public int Db { get; set; } = 0;
    public int MaxActive { get; set; } = 50;
    public int MaxIdle { get; set; } = 20;
    public int DialTimeoutSeconds { get; set; } = 1;
    public int ReadTimeoutSeconds { get; set; } = 1;
    public int WriteTimeoutSeconds { get; set; } = 1;
}
```

### `StateConfig`

```csharp
public class StateConfig : BrokerConfig
{
    public int ExpirySeconds { get; set; } = 3000;
    public int MetaExpirySeconds { get; set; } = 3600;
}
```

### `DbPoolConfig`

```csharp
public class DbPoolConfig
{
    public string Type { get; set; }           // "mssql", "mysql", "postgres"
    public string Dsn { get; set; }            // ADO.NET connection string
    public int MaxIdle { get; set; } = 10;
    public int MaxActive { get; set; } = 100;
    public int ConnectTimeoutSeconds { get; set; } = 10;
}
```

### `ResultDbConfig`

```csharp
public class ResultDbConfig : DbPoolConfig
{
    public string ResultsTable { get; set; } = "results_{0}";
    public bool Unlogged { get; set; } = false;
}
```

---

## Core

### `ScarabCore`

The central orchestrator holding all subsystem references.

```csharp
public class ScarabCore
{
    // Dependencies
    private readonly TaskCollection _tasks;
    private readonly DbPool _srcDbs;
    private readonly ResultBackendCollection _resultBackends;
    private readonly IJobBroker _broker;
    private readonly IJobStateStore _stateStore;
    private readonly AppConfig _appConfig;
    private readonly ILogger<ScarabCore> _logger;

    // Cancellation tracking
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _jobCtx;

    // Constructor
    public ScarabCore(AppConfig appConfig, DbPool srcDbs, ResultBackendCollection resultBackends, IJobBroker broker, IJobStateStore stateStore, ILogger<ScarabCore> logger);

    // ── Public API (called by HTTP handlers) ──

    public TaskCollection GetTasks();

    public Task<JobResp> NewJob(JobReq request, string taskName);

    public Task<GroupResp> NewJobGroup(GroupReq request);

    public Task<JobStatusResp> GetJobStatus(string jobId);

    public Task<GroupStatusResp> GetJobGroupStatus(string groupId);

    public Task<List<JobMessage>> GetPendingJobs(string queue);

    public Task CancelJob(string jobId, bool purge);

    public Task CancelJobGroup(string groupId, bool purge);

    // ── Internal (called by WorkerService) ──

    public Task<long> ExecJob(string jobId, string taskName, string? dbName, TimeSpan ttl, object[] args, SqlTask task, CancellationToken cancellationToken);

    // ── Private ──

    private Task<JobMessage> MakeJob(JobReq request, string taskName);

    private Task<long> WriteResults(string jobId, SqlTask task, TimeSpan ttl, DbDataReader reader);
}
```

### `TaskLoader`

```csharp
public class TaskLoader
{
    private readonly ILogger<TaskLoader> _logger;

    public TaskLoader(ILogger<TaskLoader> logger);

    /// <summary>
    /// Loads all .sql files from the given directories and returns a TaskCollection.
    /// Validates queries by preparing them against source DBs.
    /// </summary>
    public TaskCollection LoadTasks(string[] directories, DbPool srcDbs, ResultBackendCollection resultBackends, AppConfig appConfig);

    /// <summary>
    /// Parses a single .sql file into named queries with tags.
    /// </summary>
    private Dictionary<string, ParsedQuery> ParseSqlFile(string filePath);
}

/// <summary>
/// Intermediate representation of a parsed query before it becomes an SqlTask.
/// </summary>
internal class ParsedQuery
{
    public string Name { get; set; }
    public string Query { get; set; }
    public Dictionary<string, string> Tags { get; set; }   // "db" → "db1, db2", "queue" → "myqueue", etc.
}
```

### `SqlTask`

```csharp
public class SqlTask
{
    public string Name { get; set; }
    public string Queue { get; set; }
    public int Concurrency { get; set; }
    public string RawSql { get; set; }                      // The SQL query text
    public bool IsPrepared { get; set; }                    // false if raw:1
    public DbPool SourceDbs { get; set; }                   // Tagged source DBs (or all)
    public ResultBackendCollection ResultBackends { get; set; } // Tagged result backends (or all)
}
```

### `TaskCollection`

```csharp
public class TaskCollection : Dictionary<string, SqlTask>
{
    // Inherits Dictionary<string, SqlTask>
    // No additional methods needed — standard dictionary operations suffice.
}
```

---

## DbPool

### `DbType` (Enum)

```csharp
public enum DbType
{
    Mssql,
    MySql,
    PostgreSql
}
```

### `DbConnectionFactory`

```csharp
public class DbConnectionFactory
{
    public string Name { get; }
    public DbType Type { get; }
    private readonly string _connectionString;

    public DbConnectionFactory(string name, DbPoolConfig config);

    /// <summary>
    /// Creates and opens a new DbConnection of the appropriate type.
    /// Connection pooling is handled by the underlying ADO.NET driver.
    /// </summary>
    public Task<DbConnection> CreateConnectionAsync();

    /// <summary>
    /// Validates connectivity at startup.
    /// </summary>
    public Task PingAsync();
}
```

### `DbPool`

```csharp
public class DbPool : Dictionary<string, DbConnectionFactory>
{
    /// <summary>
    /// Returns a specific connection factory by name.
    /// Throws if not found.
    /// </summary>
    public DbConnectionFactory Get(string name);

    /// <summary>
    /// Returns a random connection factory from the pool.
    /// </summary>
    public (string name, DbConnectionFactory factory) GetRandom();

    /// <summary>
    /// Returns all factory names.
    /// </summary>
    public string[] GetNames();

    /// <summary>
    /// Returns a subset of the pool filtered by the given names.
    /// Throws if any name is not found.
    /// </summary>
    public DbPool FilterByNames(string[] names);
}
```

### `DbPoolManager`

```csharp
public class DbPoolManager
{
    private readonly ILogger<DbPoolManager> _logger;

    public DbPoolManager(ILogger<DbPoolManager> logger);

    /// <summary>
    /// Creates connection factories for all configured databases and validates connectivity.
    /// </summary>
    public Task<DbPool> InitializeAsync(Dictionary<string, DbPoolConfig> configs);
}
```

---

## Result Backends

### `IResultBackend`

```csharp
public interface IResultBackend
{
    /// <summary>
    /// Creates a new result set writer for a specific job.
    /// Each job gets its own dedicated result table.
    /// </summary>
    Task<IResultSet> NewResultSet(string jobId, string taskName, TimeSpan ttl);
}
```

### `IResultSet`

```csharp
public interface IResultSet : IAsyncDisposable
{
    /// <summary>
    /// Registers column types for a task. Generates and caches the CREATE TABLE schema.
    /// Should only be called once per unique task name.
    /// </summary>
    Task RegisterColTypes(string[] columnNames, DbColumn[] columnTypes);

    /// <summary>
    /// Returns true if column types have already been registered for this task.
    /// </summary>
    bool IsColTypesRegistered();

    /// <summary>
    /// Creates the result table with the registered schema.
    /// Drops existing table first. Should be called once per job.
    /// </summary>
    Task WriteCols(string[] columnNames);

    /// <summary>
    /// Inserts a single row into the result table.
    /// Called once per result row.
    /// </summary>
    Task WriteRow(object[] values);

    /// <summary>
    /// Commits the transaction, finalizing all written rows.
    /// </summary>
    Task Flush();
}
```

### `SqlResultBackend`

```csharp
public class SqlResultBackend : IResultBackend
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly ResultDbConfig _config;
    private readonly ILogger _logger;

    // Schema cache: taskName → InsertSchema
    private readonly ConcurrentDictionary<string, InsertSchema> _schemaCache;

    public SqlResultBackend(DbConnectionFactory connectionFactory, ResultDbConfig config, ILogger logger);

    public Task<IResultSet> NewResultSet(string jobId, string taskName, TimeSpan ttl);
}
```

### `SqlResultSet`

```csharp
public class SqlResultSet : IResultSet
{
    private readonly string _jobId;
    private readonly string _taskName;
    private readonly string _tableName;            // e.g., "results_myjob"
    private readonly SqlResultBackend _backend;
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;

    public SqlResultSet(string jobId, string taskName, SqlResultBackend backend, DbConnection connection, DbTransaction transaction);

    public Task RegisterColTypes(string[] columnNames, DbColumn[] columnTypes);
    public bool IsColTypesRegistered();
    public Task WriteCols(string[] columnNames);
    public Task WriteRow(object[] values);
    public Task Flush();
    public ValueTask DisposeAsync();
}
```

### `InsertSchema` (Internal)

```csharp
internal class InsertSchema
{
    public string DropTable { get; set; }       // "DROP TABLE IF EXISTS {0};"
    public string CreateTable { get; set; }     // "CREATE TABLE {0} (col1 BIGINT, ...);"
    public string InsertRow { get; set; }       // "INSERT INTO {0} (col1,...) VALUES ($1,...);"
}
```

### `ResultBackendCollection`

```csharp
public class ResultBackendCollection : Dictionary<string, IResultBackend>
{
    /// <summary>
    /// Returns a random result backend.
    /// </summary>
    public (string name, IResultBackend backend) GetRandom();

    /// <summary>
    /// Returns all backend names.
    /// </summary>
    public string[] GetNames();

    /// <summary>
    /// Filters to a subset matching the given names.
    /// </summary>
    public ResultBackendCollection FilterByNames(string[] names);
}
```

---

## Queue

### `IJobBroker`

```csharp
public interface IJobBroker
{
    /// <summary>
    /// Pushes a job onto a named queue.
    /// </summary>
    Task Enqueue(string queue, JobMessage message);

    /// <summary>
    /// Blocking dequeue from a named queue. Returns null on timeout/cancellation.
    /// </summary>
    Task<JobMessage?> Dequeue(string queue, CancellationToken cancellationToken);

    /// <summary>
    /// Returns all pending jobs in a queue without removing them.
    /// </summary>
    Task<List<JobMessage>> GetPending(string queue);

    /// <summary>
    /// Adds a delayed job (ETA scheduling).
    /// </summary>
    Task EnqueueDelayed(string queue, JobMessage message, DateTime eta);

    /// <summary>
    /// Moves due delayed jobs to the active queue. Called periodically by workers.
    /// </summary>
    Task PromoteDelayedJobs(string queue);
}
```

### `IJobStateStore`

```csharp
public interface IJobStateStore
{
    // ── Job State ──

    Task SetJobState(string jobId, string status, string? taskName = null, string? queue = null, string? error = null, string? groupId = null);

    Task<JobState?> GetJobState(string jobId);

    Task SetJobResult(string jobId, int rowCount);

    Task DeleteJob(string jobId);

    // ── Group State ──

    Task CreateGroup(string groupId, string[] jobIds);

    Task<GroupState?> GetGroupState(string groupId);

    Task DeleteGroup(string groupId);
}

/// <summary>
/// Internal representation of job state stored in Redis.
/// </summary>
public class JobState
{
    public string JobId { get; set; }
    public string Status { get; set; }          // Internal status (started, processing, done, failed, retrying)
    public string? TaskName { get; set; }
    public string? Queue { get; set; }
    public string? Error { get; set; }
    public int Result { get; set; }             // Row count
    public string? GroupId { get; set; }
}

/// <summary>
/// Internal representation of group state.
/// </summary>
public class GroupState
{
    public string GroupId { get; set; }
    public string Status { get; set; }
    public string[] JobIds { get; set; }
}
```

### `RedisJobBroker`

```csharp
public class RedisJobBroker : IJobBroker
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisJobBroker> _logger;

    public RedisJobBroker(BrokerConfig config, ILogger<RedisJobBroker> logger);

    // Key patterns:
    // Queue:    "queue:{queueName}"          (Redis List)
    // Delayed:  "queue:{queueName}:delayed"  (Redis Sorted Set, score = UTC timestamp)

    public Task Enqueue(string queue, JobMessage message);
    public Task<JobMessage?> Dequeue(string queue, CancellationToken cancellationToken);
    public Task<List<JobMessage>> GetPending(string queue);
    public Task EnqueueDelayed(string queue, JobMessage message, DateTime eta);
    public Task PromoteDelayedJobs(string queue);
}
```

### `RedisJobStateStore`

```csharp
public class RedisJobStateStore : IJobStateStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly StateConfig _config;
    private readonly ILogger<RedisJobStateStore> _logger;

    public RedisJobStateStore(StateConfig config, ILogger<RedisJobStateStore> logger);

    // Key patterns:
    // Job:      "job:{jobId}"                (Redis Hash)
    // Group:    "group:{groupId}"            (Redis Hash)
    // GroupJobs: "group:{groupId}:jobs"       (Redis Set)

    public Task SetJobState(string jobId, string status, string? taskName = null, string? queue = null, string? error = null, string? groupId = null);
    public Task<JobState?> GetJobState(string jobId);
    public Task SetJobResult(string jobId, int rowCount);
    public Task DeleteJob(string jobId);
    public Task CreateGroup(string groupId, string[] jobIds);
    public Task<GroupState?> GetGroupState(string groupId);
    public Task DeleteGroup(string groupId);
}
```

### `JobMessage`

```csharp
public class JobMessage
{
    public string JobId { get; set; }
    public string TaskName { get; set; }
    public string Queue { get; set; }
    public object[]? Args { get; set; }
    public string? Db { get; set; }             // Specific source DB
    public int TtlSeconds { get; set; }
    public int Retries { get; set; }
    public int MaxRetries { get; set; }
    public DateTime? Eta { get; set; }
    public string? GroupId { get; set; }
}
```

### `WorkerService`

```csharp
public class WorkerService : BackgroundService
{
    private readonly ScarabCore _core;
    private readonly IJobBroker _broker;
    private readonly IJobStateStore _stateStore;
    private readonly AppConfig _appConfig;
    private readonly ILogger<WorkerService> _logger;

    public WorkerService(ScarabCore core, IJobBroker broker, IJobStateStore stateStore, AppConfig appConfig, ILogger<WorkerService> logger);

    protected override Task ExecuteAsync(CancellationToken stoppingToken);

    /// <summary>
    /// Individual worker loop: dequeue → execute → update state.
    /// Multiple instances run concurrently (WorkerConcurrency count).
    /// </summary>
    private Task WorkerLoop(int workerId, string queue, CancellationToken stoppingToken);

    /// <summary>
    /// Processes a single dequeued job.
    /// </summary>
    private Task ProcessJob(JobMessage message, CancellationToken stoppingToken);

    /// <summary>
    /// Periodically promotes delayed jobs (ETA) to the active queue.
    /// </summary>
    private Task DelayedJobPromoter(string queue, CancellationToken stoppingToken);
}
```

---

## Program.cs (Entry Point)

```csharp
// Pseudocode structure — not full implementation

var builder = WebApplication.CreateBuilder(args);

// 1. Bind configuration sections
var appConfig = builder.Configuration.GetSection("App").Get<AppConfig>();
var brokerConfig = builder.Configuration.GetSection("JobQueue:Broker").Get<BrokerConfig>();
var stateConfig = builder.Configuration.GetSection("JobQueue:State").Get<StateConfig>();
var dbConfigs = builder.Configuration.GetSection("Db").Get<Dictionary<string, DbPoolConfig>>();
var resultConfigs = builder.Configuration.GetSection("Results").Get<Dictionary<string, ResultDbConfig>>();

// 2. Initialize DB pools
var dbPoolManager = new DbPoolManager(logger);
var srcPool = await dbPoolManager.InitializeAsync(dbConfigs);
var resPool = await dbPoolManager.InitializeResultsAsync(resultConfigs);

// 3. Initialize result backends
var resultBackends = new ResultBackendCollection();
foreach (var (name, config) in resultConfigs)
    resultBackends[name] = new SqlResultBackend(resPool.Get(name), config, logger);

// 4. Initialize Redis
var broker = new RedisJobBroker(brokerConfig, logger);
var stateStore = new RedisJobStateStore(stateConfig, logger);

// 5. Create core and load tasks
var core = new ScarabCore(appConfig, srcPool, resultBackends, broker, stateStore, logger);
var taskLoader = new TaskLoader(logger);
core.Tasks = taskLoader.LoadTasks(appConfig.SqlDirectories, srcPool, resultBackends, appConfig);

// 6. Register WorkerService
builder.Services.AddHostedService<WorkerService>(sp => new WorkerService(core, broker, stateStore, appConfig, logger));

var app = builder.Build();

// 7. Map HTTP routes (only if not worker-only)
if (!appConfig.WorkerOnly)
{
    app.MapGet("/", () => ApiResponse.Success($"Scarab v1.0.0"));
    app.MapGet("/tasks", (HttpContext ctx) => { ... });
    app.MapPost("/tasks/{taskName}/jobs", async (string taskName, HttpContext ctx) => { ... });
    app.MapGet("/jobs/{jobId}", async (string jobId) => { ... });
    app.MapGet("/jobs/queue/{queue}", async (string queue) => { ... });
    app.MapDelete("/jobs/{jobId}", async (string jobId, HttpContext ctx) => { ... });
    app.MapPost("/groups", async (HttpContext ctx) => { ... });
    app.MapGet("/groups/{groupId}", async (string groupId) => { ... });
    app.MapDelete("/groups/{groupId}", async (string groupId, HttpContext ctx) => { ... });
}

await app.RunAsync();
```

---

## Redis Key Schema

| Key Pattern | Redis Type | Description | TTL |
|-------------|-----------|-------------|-----|
| `queue:{name}` | List | Active job queue | None |
| `queue:{name}:delayed` | Sorted Set | Delayed jobs (score = UTC epoch) | None |
| `job:{jobId}` | Hash | Job state (status, task, queue, error, result) | ExpirySeconds |
| `group:{groupId}` | Hash | Group state (status) | MetaExpirySeconds |
| `group:{groupId}:jobs` | Set | Job IDs belonging to a group | MetaExpirySeconds |

---

## NuGet Package Dependencies

```xml
<ItemGroup>
    <PackageReference Include="Microsoft.Data.SqlClient" Version="5.*" />
    <PackageReference Include="MySqlConnector" Version="2.*" />
    <PackageReference Include="Npgsql" Version="8.*" />
    <PackageReference Include="StackExchange.Redis" Version="2.*" />
</ItemGroup>
```

No additional frameworks — the solution uses only ASP.NET Core built-in DI, logging, and configuration.

---

## Type Mapping Reference (Source → Result Table)

Used by `SqlResultSet.RegisterColTypes()` to map source DB column types to result table types:

```csharp
private static string MapColumnType(string sourceType, DbType resultDbType)
{
    return sourceType.ToUpperInvariant() switch
    {
        "INT" or "INT2" or "INT4" or "INT8" or "TINYINT" or "SMALLINT"
            or "MEDIUMINT" or "BIGINT"
            => "BIGINT",

        "FLOAT" or "FLOAT4" or "FLOAT8" or "DOUBLE" or "DECIMAL"
            or "NUMERIC" or "REAL" or "MONEY" or "SMALLMONEY"
            => "DECIMAL",

        "DATETIME" or "DATETIME2" or "DATETIMEOFFSET"
            or "SMALLDATETIME" or "TIMESTAMP"
            => "TIMESTAMP",

        "DATE"
            => "DATE",

        "BIT" or "BOOLEAN"
            => "BOOLEAN",

        "JSON" or "JSONB"
            => resultDbType == DbType.PostgreSql ? "JSONB" : "JSON",

        "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR"
            => "VARCHAR(255)",

        _ => "TEXT"
    };
}
```

---

## Identifier Quoting

Result table and column names are quoted based on the result database type:

| DB Type | Quote Character | Example |
|---------|----------------|---------|
| PostgreSQL | `"double quotes"` | `"results_myjob"` |
| MySQL | `` `backticks` `` | `` `results_myjob` `` |
