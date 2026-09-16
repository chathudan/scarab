# API Specification

Scarab exposes a REST HTTP API on `http://127.0.0.1:6060` by default.

All responses use a standard JSON envelope:

```json
// Success
{ "status": "success", "data": { ... } }

// Error
{ "status": "error", "message": "description of what went wrong" }
```

---

## Endpoints Summary

| Method   | Route                      | Description                                    |
|----------|----------------------------|------------------------------------------------|
| `GET`    | `/`                        | Health check / version info                    |
| `GET`    | `/tasks`                   | List all registered SQL tasks                  |
| `POST`   | `/tasks/{taskName}/jobs`   | Schedule a job for a given task                |
| `GET`    | `/jobs/{jobId}`            | Get the status of a job                        |
| `GET`    | `/jobs/{jobId}/data`       | Get job result rows (default limit, all rows, or paginated) |
| `GET`    | `/jobs/queue/{queue}`      | List all pending jobs in a queue               |
| `DELETE` | `/jobs/{jobId}`            | Cancel/delete a job                            |
| `POST`   | `/groups`                  | Schedule a group of jobs                       |
| `GET`    | `/groups/{groupId}`        | Get the status of a group and its jobs         |
| `DELETE` | `/groups/{groupId}`        | Cancel/delete a group and all its jobs         |

---

## `GET /`

Returns server version information.

### Response

```json
{
  "status": "success",
  "data": "Scarab v1.0.0"
}
```

---

## `GET /tasks`

Returns the list of all registered SQL tasks loaded from `.sql` files at startup.

### Query Parameters

| Parameter | Type   | Required | Description                                |
|-----------|--------|----------|--------------------------------------------|
| `sql`     | string | No       | If omitted, returns full task objects including raw SQL. If set to any value (e.g., `?sql=1`), returns only task names as a string array. |

### Response — Full tasks (default)

```json
{
  "status": "success",
  "data": {
    "get_profit_summary": {
      "name": "get_profit_summary",
      "queue": "default",
      "concurrency": 10,
      "raw": "SELECT SUM(amount) AS total, entry_date FROM entries WHERE user_id = @p1 GROUP BY entry_date"
    },
    "get_profit_entries": {
      "name": "get_profit_entries",
      "queue": "myqueue",
      "concurrency": 10,
      "raw": "SELECT * FROM entries WHERE user_id = @p1"
    }
  }
}
```

### Response — Names only (`?sql=1`)

```json
{
  "status": "success",
  "data": ["get_profit_summary", "get_profit_entries"]
}
```

---

## `POST /tasks/{taskName}/jobs`

Schedules a new job for execution against the specified task.

### Path Parameters

| Parameter  | Type   | Required | Description                     |
|------------|--------|----------|---------------------------------|
| `taskName` | string | Yes      | Name of a registered SQL task   |

### Request Body

```json
{
  "job_id": "myjob",
  "queue": "high_priority",
  "eta": "2026-01-15 10:00:00",
  "retries": 3,
  "ttl": 3600,
  "args": ["USER1", "2024-01-01", "2024-12-31"],
  "db": "mssql_db"
}
```

### Request Body Parameters

| Parameter | Type     | Required | Default         | Description |
|-----------|----------|----------|-----------------|-------------|
| `job_id`  | string   | No       | Auto-generated  | Alphanumeric ID for the job (`a-z`, `0-9`, `-`, `_`, `:`). Can be non-unique, but only one job with the same ID can run at a time. |
| `queue`   | string   | No       | Task's queue or server default | Queue to send the job to. Only workers listening on this queue will receive it. |
| `eta`     | string   | No       | Immediate       | Timestamp (`yyyy-MM-dd HH:mm:ss`) at which the job should start. |
| `retries` | int      | No       | `0`             | Number of times a failed job should be retried. |
| `ttl`     | int      | No       | Server default  | TTL in seconds for results in the results backend. |
| `args`    | string[] | No       | `[]`            | Positional arguments to pass to the SQL query placeholders. |
| `db`      | string   | No       | Random from task's DBs | Specific source database name to execute against. |

### Response — Success

```json
{
  "status": "success",
  "data": {
    "job_id": "myjob",
    "task": "get_profit_entries",
    "queue": "high_priority",
    "eta": "2026-01-15T10:00:00",
    "retries": 3
  }
}
```

### Response — Errors

| HTTP Code | Condition |
|-----------|-----------|
| `400`     | Empty request body |
| `400`     | Invalid JSON body |
| `400`     | Invalid characters in `job_id` (only `a-z`, `0-9`, `-`, `_`, `:` allowed) |
| `500`     | Unrecognized task name |
| `500`     | Job with same ID is already running |

---

## `GET /jobs/{jobId}`

Returns the current status of a job.

### Path Parameters

| Parameter | Type   | Required | Description       |
|-----------|--------|----------|-------------------|
| `jobId`   | string | Yes      | The job identifier |

### Response

```json
{
  "status": "success",
  "data": {
    "job_id": "myjob",
    "state": "SUCCESS",
    "count": 42,
    "error": ""
  }
}
```

### Job States

| State     | Description |
|-----------|-------------|
| `PENDING` | Job is queued and waiting to be picked up by a worker |
| `STARTED` | Job is currently being executed by a worker |
| `SUCCESS` | Job completed successfully. `count` contains the number of result rows written. |
| `FAILURE` | Job failed. `error` contains the error message. |
| `RETRY`   | Job failed and is being retried. |

### Response — Errors

| HTTP Code | Condition |
|-----------|-----------|
| `404`     | Job ID not found |

---

## `GET /jobs/{jobId}/data`

Returns rows generated by a completed job.

### Path Parameters

| Parameter | Type   | Required | Description       |
|-----------|--------|----------|-------------------|
| `jobId`   | string | Yes      | The job identifier |

### Query Parameters

| Parameter   | Type | Required | Description |
|-------------|------|----------|-------------|
| `limit`     | int  | No       | Legacy/simple mode. Returns first N rows. Default `100`, max `5000`. |
| `all`       | bool | No       | If `true`, returns all rows. Cannot be combined with `page`/`page_size`. |
| `page`      | int  | No       | 1-based page number. Must be used with `page_size`. |
| `page_size` | int  | No       | Number of rows per page. Must be used with `page`. Max `5000`. |

### Modes

1. Default/limit mode: `/jobs/{jobId}/data` or `/jobs/{jobId}/data?limit=100`
2. All rows mode: `/jobs/{jobId}/data?all=true`
3. Paginated mode: `/jobs/{jobId}/data?page=2&page_size=50`

### Response — Default/limit mode and all mode

```json
{
  "status": "success",
  "data": [
    { "id": 1, "name": "Course A" },
    { "id": 2, "name": "Course B" }
  ]
}
```

### Response — Paginated mode

```json
{
  "status": "success",
  "data": {
    "job_id": "myjob",
    "page": 2,
    "page_size": 50,
    "total": 150,
    "total_pages": 3,
    "count": 50,
    "data": [
      { "id": 51, "name": "Course 51" }
    ]
  }
}
```

### Response — Errors

| HTTP Code | Condition |
|-----------|-----------|
| `400`     | Invalid pagination input (`page <= 0`, `page_size <= 0`, or `page_size > 5000`) |
| `400`     | Invalid mode combination (`all=true` with pagination, or only one of `page`/`page_size`) |
| `400`     | Job exists but is not yet completed |
| `404`     | Job ID not found or result table missing |

---

## `GET /jobs/queue/{queue}`

Returns all pending jobs in the specified queue.

### Path Parameters

| Parameter | Type   | Required | Description   |
|-----------|--------|----------|---------------|
| `queue`   | string | Yes      | Queue name    |

### Response

```json
{
  "status": "success",
  "data": [
    {
      "id": "myjob",
      "task": "get_profit_entries",
      "queue": "high_priority",
      "status": "started",
      "max_retries": 3,
      "retries": 0
    }
  ]
}
```

---

## `DELETE /jobs/{jobId}`

Cancels a pending or running job and removes it from the queue.

### Path Parameters

| Parameter | Type   | Required | Description       |
|-----------|--------|----------|-------------------|
| `jobId`   | string | Yes      | The job identifier |

### Query Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `purge`   | bool | No       | `false` | If `true`, also deletes completed (SUCCESS/FAILURE) jobs. If `false`, completed jobs cannot be deleted. |

### Behavior

1. If the job is currently running, the `CancellationToken` is triggered to cancel the SQL query execution.
2. The job is deleted from the Redis state store.
3. **MSSQL & PostgreSQL**: Query execution is cancelled mid-flight via `CancellationToken`.
4. **MySQL**: The MySQL server will continue executing the query after cancellation. It is important to set `max_execution_time` on the MySQL server.

### Response — Success

```json
{
  "status": "success",
  "data": true
}
```

### Response — Errors

| HTTP Code | Condition |
|-----------|-----------|
| `500`     | Job not found |
| `500`     | Cannot cancel completed job (when `purge=false`) |

---

## `POST /groups`

Schedules a group of jobs to be executed concurrently. Group state can be polled to check if all jobs have completed.

### Request Body

```json
{
  "group_id": "mygroup",
  "concurrency": 3,
  "jobs": [
    {
      "job_id": "job1",
      "task": "get_profit_entries",
      "args": ["USER1", "2024-01-01", "2024-12-31"]
    },
    {
      "job_id": "job2",
      "task": "get_profit_summary",
      "args": ["USER1"]
    }
  ]
}
```

### Request Body Parameters

| Parameter     | Type    | Required | Default        | Description |
|---------------|---------|----------|----------------|-------------|
| `group_id`    | string  | No       | Auto-generated | Alphanumeric ID for the group. |
| `concurrency` | int     | No       | Server default | Number of jobs to run concurrently within this group. |
| `jobs`        | array   | Yes      |                | Array of job objects (same schema as POST job, but with an additional required `task` field). |

### Job Object within Group

| Parameter | Type     | Required | Description |
|-----------|----------|----------|-------------|
| `task`    | string   | Yes      | Name of the SQL task |
| `job_id`  | string   | No       | Job identifier |
| `queue`   | string   | No       | Target queue |
| `eta`     | string   | No       | Scheduled execution time |
| `retries` | int      | No       | Retry count |
| `args`    | string[] | No       | SQL query arguments |
| `db`      | string   | No       | Specific source DB |

### Response

```json
{
  "status": "success",
  "data": {
    "group_id": "mygroup",
    "jobs": [
      {
        "job_id": "job1",
        "task": "get_profit_entries",
        "queue": "default",
        "eta": null,
        "retries": 0
      },
      {
        "job_id": "job2",
        "task": "get_profit_summary",
        "queue": "default",
        "eta": null,
        "retries": 0
      }
    ]
  }
}
```

---

## `GET /groups/{groupId}`

Returns the status of a group and all its individual jobs.

### Path Parameters

| Parameter | Type   | Required | Description         |
|-----------|--------|----------|---------------------|
| `groupId` | string | Yes      | The group identifier |

### Response

```json
{
  "status": "success",
  "data": {
    "group_id": "mygroup",
    "state": "SUCCESS",
    "jobs": [
      {
        "job_id": "job1",
        "state": "SUCCESS",
        "count": 42,
        "error": ""
      },
      {
        "job_id": "job2",
        "state": "SUCCESS",
        "count": 1,
        "error": ""
      }
    ]
  }
}
```

### Group States

| State     | Description |
|-----------|-------------|
| `PENDING` | Some jobs are still queued |
| `STARTED` | Some jobs are currently executing |
| `SUCCESS` | All jobs completed successfully |
| `FAILURE` | One or more jobs failed |

### Response — Errors

| HTTP Code | Condition |
|-----------|-----------|
| `404`     | Group ID not found |

---

## `DELETE /groups/{groupId}`

Cancels all jobs in a group and removes the group.

### Path Parameters

| Parameter | Type   | Required | Description         |
|-----------|--------|----------|---------------------|
| `groupId` | string | Yes      | The group identifier |

### Query Parameters

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `purge`   | bool | No       | `false` | If `true`, also deletes completed groups. If `false`, completed groups cannot be deleted. |

### Behavior

1. Iterates through every job in the group.
2. Cancels each running job's `CancellationToken`.
3. Deletes each job from Redis.
4. Deletes the group from Redis.

### Response — Success

```json
{
  "status": "success",
  "data": true
}
```

### Response — Errors

| HTTP Code | Condition |
|-----------|-----------|
| `500`     | Group not found |
| `500`     | Cannot delete completed group (when `purge=false`) |

---

## Error Response Format

All error responses follow this structure:

```json
{
  "status": "error",
  "message": "human-readable error description"
}
```

### Standard HTTP Status Codes Used

| Code | Usage |
|------|-------|
| `200` | Successful operation |
| `400` | Bad request (invalid input, missing body, bad JSON) |
| `404` | Resource not found (job/group ID not found) |
| `500` | Internal server error (execution failure, queue error) |

---

## Job ID Validation

Job IDs and Group IDs must match the pattern: `^[a-zA-Z0-9\-_:]+$`

Valid: `my-job_1`, `report:2024:q1`, `user123_monthly`
Invalid: `my job`, `report@2024`, `job#1`

---

## Content Type

All POST requests must include the header:
```
Content-Type: application/json
```

POST request bodies must be raw JSON.
