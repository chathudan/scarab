# Configuration Reference

Scarab uses `appsettings.json` for all configuration. Environment variables can override any setting using the prefix `SCARAB__` with `__` as the section separator (standard ASP.NET Core convention).

---

## Complete Configuration Example

```json
{
  "App": {
    "LogLevel": "Debug",
    "DefaultJobTtlSeconds": 60,
    "Server": "0.0.0.0:6060",
    "SqlDirectories": ["./Sql"],
    "Queue": "default",
    "WorkerName": "default",
    "WorkerConcurrency": 10,
    "WorkerOnly": false
  },

  "JobQueue": {
    "Broker": {
      "Type": "redis",
      "Addresses": ["localhost:6379"],
      "Password": "",
      "Db": 1,
      "MaxActive": 50,
      "MaxIdle": 20,
      "DialTimeoutSeconds": 1,
      "ReadTimeoutSeconds": 1,
      "WriteTimeoutSeconds": 1
    },
    "State": {
      "Type": "redis",
      "Addresses": ["localhost:6379"],
      "Password": "",
      "Db": 1,
      "MaxActive": 50,
      "MaxIdle": 20,
      "DialTimeoutSeconds": 1,
      "ReadTimeoutSeconds": 1,
      "WriteTimeoutSeconds": 1,
      "ExpirySeconds": 3000,
      "MetaExpirySeconds": 3600
    }
  },

  "Db": {
    "mssql_db": {
      "Type": "mssql",
      "Dsn": "Server=localhost;Database=mydb;User Id=sa;Password=YourPassword;TrustServerCertificate=True;",
      "MaxIdle": 10,
      "MaxActive": 100,
      "ConnectTimeoutSeconds": 10
    },
    "mysql_db": {
      "Type": "mysql",
      "Dsn": "Server=localhost;Port=3306;Database=mydb;Uid=root;Pwd=password;",
      "MaxIdle": 10,
      "MaxActive": 100,
      "ConnectTimeoutSeconds": 10
    },
    "pg_db": {
      "Type": "postgres",
      "Dsn": "Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=password;",
      "MaxIdle": 10,
      "MaxActive": 100,
      "ConnectTimeoutSeconds": 10
    }
  },

  "Results": {
    "my_pg_results": {
      "Type": "postgres",
      "Dsn": "Host=localhost;Port=5432;Database=results_db;Username=postgres;Password=password;",
      "MaxIdle": 10,
      "MaxActive": 100,
      "ConnectTimeoutSeconds": 10,
      "ResultsTable": "results_{0}",
      "Unlogged": true
    },
    "my_mysql_results": {
      "Type": "mysql",
      "Dsn": "Server=localhost;Port=3306;Database=results_db;Uid=root;Pwd=password;",
      "MaxIdle": 10,
      "MaxActive": 100,
      "ConnectTimeoutSeconds": 10,
      "ResultsTable": "results_{0}",
      "Unlogged": false
    }
  }
}
```

---

## Section: `App`

Application-level settings.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `LogLevel` | string | `"Information"` | Log level: `Debug`, `Information`, `Warning`, `Error` |
| `DefaultJobTtlSeconds` | int | `60` | Default TTL in seconds for job results in the results backend |
| `Server` | string | `"0.0.0.0:6060"` | HTTP server bind address and port |
| `SqlDirectories` | string[] | `["./Sql"]` | Paths to directories containing `.sql` task files. Multiple directories supported. |
| `Queue` | string | `"default"` | Default queue name for jobs that don't specify one |
| `WorkerName` | string | `"default"` | Name identifier for this worker instance (used in logging) |
| `WorkerConcurrency` | int | `10` | Number of concurrent worker threads processing jobs from the queue |
| `WorkerOnly` | bool | `false` | If `true`, only runs workers (no HTTP API). Useful for dedicated processing nodes. |

---

## Section: `JobQueue.Broker`

Redis broker configuration for the job queue.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Type` | string | `"redis"` | Broker type. Only `"redis"` is supported. |
| `Addresses` | string[] | `["localhost:6379"]` | Redis server address(es). First address is used. |
| `Password` | string | `""` | Redis authentication password |
| `Db` | int | `0` | Redis database number |
| `MaxActive` | int | `50` | Maximum number of active Redis connections |
| `MaxIdle` | int | `20` | Maximum number of idle Redis connections |
| `DialTimeoutSeconds` | int | `1` | Connection timeout in seconds |
| `ReadTimeoutSeconds` | int | `1` | Read operation timeout in seconds |
| `WriteTimeoutSeconds` | int | `1` | Write operation timeout in seconds |

---

## Section: `JobQueue.State`

Redis state store for tracking job statuses and results metadata.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Type` | string | `"redis"` | State store type. Only `"redis"` is supported. |
| `Addresses` | string[] | `["localhost:6379"]` | Redis server address(es) |
| `Password` | string | `""` | Redis authentication password |
| `Db` | int | `0` | Redis database number |
| `MaxActive` | int | `50` | Maximum active connections |
| `MaxIdle` | int | `20` | Maximum idle connections |
| `DialTimeoutSeconds` | int | `1` | Connection timeout |
| `ReadTimeoutSeconds` | int | `1` | Read timeout |
| `WriteTimeoutSeconds` | int | `1` | Write timeout |
| `ExpirySeconds` | int | `3000` | TTL for job state entries in Redis |
| `MetaExpirySeconds` | int | `3600` | TTL for job metadata entries in Redis |

---

## Section: `Db`

Source database connections. Each key is a named database identifier referenced in SQL task files and API requests.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Type` | string | — | Database type: `"mssql"`, `"mysql"`, or `"postgres"` |
| `Dsn` | string | — | ADO.NET connection string for the database |
| `MaxIdle` | int | `10` | Minimum pool size (idle connections kept open) |
| `MaxActive` | int | `100` | Maximum pool size (total connections allowed) |
| `ConnectTimeoutSeconds` | int | `10` | Connection lifetime in seconds |

### DSN Format Examples

**MSSQL:**
```
Server=localhost;Database=mydb;User Id=sa;Password=YourPass;TrustServerCertificate=True;
Server=localhost,1433;Database=mydb;Integrated Security=True;
```

**MySQL:**
```
Server=localhost;Port=3306;Database=mydb;Uid=root;Pwd=password;
Server=localhost;Port=3306;Database=mydb;Uid=root;Pwd=password;SslMode=Required;
```

**PostgreSQL:**
```
Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=password;
Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=password;SSL Mode=Require;
```

---

## Section: `Results`

Result database backends where job output is written. Each key is a named result backend referenced in SQL task files.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Type` | string | — | Database type: `"mysql"` or `"postgres"` (MSSQL not supported as result backend) |
| `Dsn` | string | — | ADO.NET connection string |
| `MaxIdle` | int | `10` | Minimum pool size |
| `MaxActive` | int | `100` | Maximum pool size |
| `ConnectTimeoutSeconds` | int | `10` | Connection lifetime |
| `ResultsTable` | string | `"results_{0}"` | Table name template. `{0}` is replaced with the job ID. |
| `Unlogged` | bool | `false` | **PostgreSQL only.** If `true`, creates `UNLOGGED` tables which are faster but not crash-safe. Ideal for ephemeral cache tables. |

### Results Table Naming

The `ResultsTable` pattern uses `{0}` as a placeholder for the job ID. Examples:

- `"results_{0}"` → `results_myjob`
- `"cache_{0}"` → `cache_myjob`
- `"rpt_{0}"` → `rpt_myjob`

---

## Environment Variable Overrides

Any configuration key can be overridden via environment variables using the `SCARAB__` prefix with `__` as the section separator:

```bash
# Override the server address
SCARAB__App__Server=0.0.0.0:8080

# Override worker concurrency
SCARAB__App__WorkerConcurrency=20

# Override Redis broker address
SCARAB__JobQueue__Broker__Addresses__0=redis-server:6379

# Override a source database DSN
SCARAB__Db__mssql_db__Dsn="Server=prod-sql;Database=proddb;..."

# Enable worker-only mode
SCARAB__App__WorkerOnly=true
```

---

## Command Line Arguments

| Argument | Description | Default |
|----------|-------------|---------|
| `--config` | Path to configuration file | `appsettings.json` |
| `--server` | HTTP bind address | `0.0.0.0:6060` |
| `--sql-directory` | Path to SQL task files (can be specified multiple times) | `./Sql` |
| `--queue` | Default queue name | `default` |
| `--worker-name` | Worker instance name | `default` |
| `--worker-concurrency` | Number of concurrent workers | `10` |
| `--worker-only` | Run without HTTP API | `false` |

---

## Multiple Instances Configuration

### High-priority worker + API

```bash
dotnet run -- --queue "high_priority" --worker-name "hp_worker" --worker-concurrency 30
```

### Low-priority worker only (no API)

```bash
dotnet run -- --queue "low_priority" --worker-name "lp_worker" --worker-concurrency 5 --worker-only
```

Both instances connect to the same Redis broker, so jobs are routed correctly based on the `queue` parameter regardless of which instance received the HTTP request.
