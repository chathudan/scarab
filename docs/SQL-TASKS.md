# SQL Task File Format

Scarab loads SQL queries from `.sql` files at startup, using a format compatible with [goyesql](https://github.com/knadh/goyesql). Each query becomes a named **task** that can be scheduled as a job via the API.

---

## File Discovery

At startup, Scarab scans all directories listed in `App.SqlDirectories` configuration for files matching `*.sql`. All discovered files are parsed and their queries registered.

```json
{
  "App": {
    "SqlDirectories": ["./Sql", "/etc/scarab/queries"]
  }
}
```

---

## Basic Format

Each query is preceded by a `-- name:` comment that defines its task name:

```sql
-- name: get_user_report
SELECT username, email, created_at FROM users WHERE department_id = @p1;
```

Multiple queries can exist in a single `.sql` file:

```sql
-- name: get_sales_summary
SELECT region, SUM(amount) AS total
FROM orders
WHERE customer_id = @p1
GROUP BY region;

-- name: get_order_details
SELECT order_id, product, quantity, price
FROM order_items
WHERE order_id = @p1;
```

---

## Metadata Tags

Tags are SQL comments on lines immediately following the `-- name:` line, before the SQL body begins.

### Supported Tags

| Tag | Type | Required | Description |
|-----|------|----------|-------------|
| `-- name:` | string | **Yes** | Unique task name. This becomes the URL path segment in `/tasks/{name}/jobs`. |
| `-- db:` | string | No | Comma-separated list of source database names (from config `Db` section). If omitted, all source DBs are available and one is picked randomly. |
| `-- queue:` | string | No | Default queue for this task. Can be overridden per-job via the API `queue` parameter. If omitted, uses the server's default queue. |
| `-- results:` | string | No | Comma-separated list of result backend names (from config `Results` section). If omitted, all result backends are available and one is picked randomly. |
| `-- raw:` | `1` | No | If set to `1`, the query is NOT prepared/validated at startup. Use for queries with complex syntax that don't prepare cleanly. |
| `-- conc:` | int | No | Task-level concurrency override. Limits how many concurrent instances of this specific task can run. |

---

## Complete Example

```sql
-- queries.sql

-- name: get_profit_summary
SELECT SUM(amount) AS total, entry_date
FROM entries
WHERE user_id = @p1
GROUP BY entry_date;

-- name: get_profit_entries
-- db: mssql_db, pg_db
-- queue: high_priority
-- results: my_pg_results
SELECT * FROM entries WHERE user_id = @p1;

-- name: get_entries_by_date
-- db: mysql_db
-- results: my_mysql_results
SELECT * FROM entries
WHERE user_id = @p1
  AND created_at > @p2
  AND created_at < @p3;

-- name: get_large_export
-- raw: 1
-- db: mssql_db
-- queue: low_priority
-- conc: 2
-- This raw query won't be prepared at startup
SELECT TOP 100000 *
FROM audit_log
WHERE category = @p1
ORDER BY created_at DESC;
```

---

## Query Placeholders

Placeholders vary by source database type. **You must write the query in the placeholder style of the target database.**

| Database | Placeholder Style | Example |
|----------|-------------------|---------|
| **MSSQL** | `@p1, @p2, @p3, ...` | `WHERE user_id = @p1 AND date > @p2` |
| **MySQL** | `?` (positional) | `WHERE user_id = ? AND date > ?` |
| **PostgreSQL** | `$1, $2, $3, ...` | `WHERE user_id = $1 AND date > $2` |

### Multi-DB Queries

When a query is tagged with multiple databases of **different types** (e.g., `-- db: mssql_db, pg_db`), you must ensure the SQL syntax is compatible with all target databases, or use `-- raw: 1` to skip preparation.

**Recommendation:** For multi-DB-type queries, prefer writing separate task files per DB type.

### Argument Mapping

Arguments passed via the API `args` array are mapped positionally:

```json
{ "args": ["USER1", "2024-01-01", "2024-12-31"] }
```

Maps to:
- MSSQL: `@p1 = "USER1"`, `@p2 = "2024-01-01"`, `@p3 = "2024-12-31"`
- MySQL: First `? = "USER1"`, second `? = "2024-01-01"`, third `? = "2024-12-31"`
- PostgreSQL: `$1 = "USER1"`, `$2 = "2024-01-01"`, `$3 = "2024-12-31"`

**All arguments are passed as strings.** The database driver handles type coercion based on the column type.

---

## Parsing Rules

1. Lines starting with `-- name:` begin a new task definition
2. Lines starting with `-- tag:` immediately after `-- name:` are parsed as metadata
3. Plain `--` comment lines between tags and SQL body are ignored
4. The SQL body is everything from the first non-tag, non-comment line until the next `-- name:` or end of file
5. Leading and trailing whitespace is trimmed from the SQL body
6. Empty lines within the SQL body are preserved
7. Task names must be unique across all files — duplicates cause a startup error

### Parser State Machine

```
START ──[-- name: X]──▶ TAGS ──[-- tag: val]──▶ TAGS
                                │
                                └──[SQL line]──▶ BODY ──[SQL line]──▶ BODY
                                                   │
                                                   └──[-- name: Y]──▶ TAGS (new task)
                                                   └──[EOF]──▶ DONE
```

---

## Validation at Startup

For each task loaded (unless `-- raw: 1`):

1. **Task name uniqueness** — duplicate names across any `.sql` file cause a fatal error
2. **Database existence** — all DBs listed in `-- db:` must exist in the `Db` config section
3. **Result backend existence** — all backends in `-- results:` must exist in the `Results` config section
4. **SQL preparation** — the query is prepared (compiled) against each tagged source DB. Preparation errors cause a fatal error, catching syntax issues early.

### Raw Queries (`-- raw: 1`)

When `raw: 1` is set:
- The query is stored as a raw string, not prepared
- No syntax validation is done at startup
- Useful for DB-specific syntax that doesn't prepare cleanly (e.g., CTEs, temp tables, dynamic SQL)
- The query is executed via `DbCommand.ExecuteReader()` at runtime

---

## Directory Structure Best Practices

```
Sql/
├── reports/
│   ├── sales_reports.sql       # All sales-related tasks
│   └── inventory_reports.sql   # All inventory tasks
├── analytics/
│   └── user_analytics.sql      # User behavior queries
└── exports/
    └── data_exports.sql        # Large data export tasks
```

**Note:** Subdirectories are NOT automatically scanned. Each directory must be explicitly listed in `SqlDirectories`:

```json
{
  "App": {
    "SqlDirectories": ["./Sql/reports", "./Sql/analytics", "./Sql/exports"]
  }
}
```

Or place all `.sql` files directly in a single directory:

```json
{
  "App": {
    "SqlDirectories": ["./Sql"]
  }
}
```

---

## Result Table Schema Mapping

When a task's query executes, the result columns are mapped to canonical types for the result table:

| Source Column Type(s) | Result Table Type |
|----------------------|-------------------|
| `INT`, `INT2`, `INT4`, `INT8`, `TINYINT`, `SMALLINT`, `MEDIUMINT`, `BIGINT` | `BIGINT` |
| `FLOAT`, `FLOAT4`, `FLOAT8`, `DOUBLE`, `DECIMAL`, `NUMERIC`, `REAL`, `MONEY`, `SMALLMONEY` | `DECIMAL` |
| `DATETIME`, `DATETIME2`, `DATETIMEOFFSET`, `SMALLDATETIME`, `TIMESTAMP` | `TIMESTAMP` |
| `DATE` | `DATE` |
| `BIT`, `BOOLEAN` | `BOOLEAN` |
| `JSON`, `JSONB` | `JSONB` (Postgres) / `JSON` (MySQL) |
| `VARCHAR`, `NVARCHAR`, `CHAR`, `NCHAR` | `VARCHAR(255)` |
| Everything else | `TEXT` |

Column nullability from the source query is preserved in the result table schema.
