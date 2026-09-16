using System.Net;
using Scalar.AspNetCore;
using Scarab.Config;
using Scarab.Core;
using Scarab.DbPool;
using Scarab.Models;
using Scarab.Queue;
using Scarab.ResultBackends;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Support prefixed environment overrides used in docker-compose (SCARAB__Section__Key).
builder.Configuration.AddEnvironmentVariables(prefix: "SCARAB__");

// Support the shorthand CLI flags documented for multi-instance deployment
// (e.g. `dotnet run -- --queue high_priority --worker-only`). The default command-line
// provider only understands the full `--App:Queue=value` config path, so map the
// friendly flags explicitly.
var cliSwitchMappings = new Dictionary<string, string>
{
    ["--queue"] = "App:Queue",
    ["--worker-name"] = "App:WorkerName",
    ["--worker-concurrency"] = "App:WorkerConcurrency",
    ["--worker-only"] = "App:WorkerOnly",
};

// --worker-only is a bare boolean flag with no value; the command-line provider requires
// an explicit value for every switch, so inject "true" when none follows it.
var cliArgs = new List<string>(args);
for (var i = 0; i < cliArgs.Count; i++)
{
    if (!string.Equals(cliArgs[i], "--worker-only", StringComparison.OrdinalIgnoreCase))
        continue;

    var hasValue = i + 1 < cliArgs.Count && !cliArgs[i + 1].StartsWith('-');
    if (!hasValue)
        cliArgs.Insert(i + 1, "true");
}

builder.Configuration.AddCommandLine(cliArgs.ToArray(), cliSwitchMappings);

// 1. Bind configuration
var appConfig = builder.Configuration.GetSection("App").Get<AppConfig>() ?? new AppConfig();
var brokerConfig = builder.Configuration.GetSection("JobQueue:Broker").Get<BrokerConfig>() ?? new BrokerConfig();
var stateConfig = builder.Configuration.GetSection("JobQueue:State").Get<StateConfig>() ?? new StateConfig();
var dbConfigs = builder.Configuration.GetSection("Db").Get<Dictionary<string, DbPoolConfig>>() ?? new();
var resultConfigs = builder.Configuration.GetSection("Results").Get<Dictionary<string, ResultDbConfig>>() ?? new();

// Docker compose sets prefixed env vars; enforce address overrides explicitly to avoid array binding surprises.
var brokerAddrOverride = Environment.GetEnvironmentVariable("SCARAB__JobQueue__Broker__Addresses__0");
if (!string.IsNullOrWhiteSpace(brokerAddrOverride))
    brokerConfig.Addresses = [brokerAddrOverride.Trim()];

var stateAddrOverride = Environment.GetEnvironmentVariable("SCARAB__JobQueue__State__Addresses__0");
if (!string.IsNullOrWhiteSpace(stateAddrOverride))
    stateConfig.Addresses = [stateAddrOverride.Trim()];

// When launched via the Aspire AppHost, resource connection strings arrive through
// ConnectionStrings:<resourceName> instead of the App/Db/Results sections above -
// apply them on top when present. A plain `dotnet run` or docker-compose (neither of
// which set these) leaves all of this a no-op and behaves exactly as before.
var aspireRedis = builder.Configuration.GetConnectionString("redis");
if (!string.IsNullOrWhiteSpace(aspireRedis))
{
    var redisOptions = ConfigurationOptions.Parse(aspireRedis);
    // EndPoint.ToString() isn't a clean "host:port" (e.g. "Unspecified/localhost:6379" for a
    // DnsEndPoint) - extract host/port explicitly so RedisJobBroker/RedisJobStateStore can
    // re-parse each address correctly.
    var addresses = redisOptions.EndPoints.Select(ep => ep switch
    {
        DnsEndPoint dns => $"{dns.Host}:{dns.Port}",
        IPEndPoint ip => $"{ip.Address}:{ip.Port}",
        _ => ep.ToString()!
    }).ToArray();
    brokerConfig.Addresses = addresses;
    brokerConfig.Password = redisOptions.Password ?? "";
    brokerConfig.Ssl = redisOptions.Ssl;
    stateConfig.Addresses = addresses;
    stateConfig.Password = redisOptions.Password ?? "";
    stateConfig.Ssl = redisOptions.Ssl;

    // Aspire's local Redis resource TLS-terminates with a self-signed dev certificate.
    // Only trust it unconditionally in Development - a real deployment's Redis still
    // gets full certificate validation.
    if (redisOptions.Ssl && builder.Environment.IsDevelopment())
    {
        brokerConfig.SslAllowSelfSigned = true;
        stateConfig.SslAllowSelfSigned = true;
    }
}

var aspireResultsDb = builder.Configuration.GetConnectionString("results-db");
if (!string.IsNullOrWhiteSpace(aspireResultsDb))
{
    resultConfigs["pg_results"] = new ResultDbConfig { Type = "postgres", Dsn = aspireResultsDb, MaxActive = 50, ResultsTable = "results_{0}" };
}

var aspireDemoDb = builder.Configuration.GetConnectionString("demo-db");
if (!string.IsNullOrWhiteSpace(aspireDemoDb))
{
    dbConfigs["mysql_demo"] = new DbPoolConfig { Type = "mysql", Dsn = aspireDemoDb, Optional = true };
}

// Configuration binding can merge arrays with defaults and introduce duplicates.
appConfig.SqlDirectories = appConfig.SqlDirectories
    .Where(d => !string.IsNullOrWhiteSpace(d))
    .Select(d => d.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
if (appConfig.SqlDirectories.Length == 0)
    appConfig.SqlDirectories = ["./Sql"];

// Configure Kestrel binding
var serverParts = appConfig.Server.Split(':');
if (serverParts.Length == 2 && int.TryParse(serverParts[1], out var port))
    builder.WebHost.UseUrls($"http://{serverParts[0]}:{port}");

builder.Logging.SetMinimumLevel(Enum.TryParse<LogLevel>(appConfig.LogLevel, true, out var ll) ? ll : LogLevel.Information);

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(ll));

// 2. Initialize DB pools
var dbPoolMgr = new DbPoolManager(loggerFactory.CreateLogger<DbPoolManager>());
var srcPool = await dbPoolMgr.InitializeAsync(dbConfigs);
var resPool = await dbPoolMgr.InitializeResultsAsync(resultConfigs);

// 3. Initialize result backends
var resultBackends = new ResultBackendCollection();
foreach (var (name, config) in resultConfigs)
    resultBackends[name] = new SqlResultBackend(resPool.Get(name), config, loggerFactory.CreateLogger<SqlResultBackend>());

// 4. Initialize Redis
var broker = new RedisJobBroker(brokerConfig, loggerFactory.CreateLogger<RedisJobBroker>());
var stateStore = new RedisJobStateStore(stateConfig, loggerFactory.CreateLogger<RedisJobStateStore>());

// 5. Create core engine and load tasks
var core = new ScarabCore(appConfig, srcPool, resultBackends, broker, stateStore, loggerFactory.CreateLogger<ScarabCore>());
var taskLoader = new TaskLoader(loggerFactory.CreateLogger<TaskLoader>());
core.Tasks = taskLoader.LoadTasks(appConfig.SqlDirectories, srcPool, resultBackends, appConfig);

// 6. Register worker service
builder.Services.AddOpenApi();
builder.Services.AddSingleton(core);
builder.Services.AddSingleton<IJobBroker>(broker);
builder.Services.AddSingleton<IJobStateStore>(stateStore);
builder.Services.AddSingleton(appConfig);
builder.Services.AddHostedService(sp => new WorkerService(core, broker, stateStore, appConfig, sp.GetRequiredService<ILogger<WorkerService>>()));

var app = builder.Build();
app.MapDefaultEndpoints();

// 7. Map HTTP routes (skip if worker-only mode)
if (!appConfig.WorkerOnly)
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Scarab API"));

    app.MapGet("/", () => Results.Ok(ApiResponse.Success("Scarab v1.0.0")))
        .WithName("GetInfo")
        .WithTags("Meta")
        .WithSummary("Service info")
        .WithDescription("Returns the running Scarab version.");

    app.MapGet("/tasks", (HttpContext ctx) =>
    {
        var tasks = core.GetTasks();
        if (ctx.Request.Query.ContainsKey("sql"))
            return Results.Ok(ApiResponse.Success(tasks.Keys.ToArray()));

        var taskList = tasks.ToDictionary(t => t.Key, t => new { name = t.Value.Name, queue = t.Value.Queue, concurrency = t.Value.Concurrency, @params = t.Value.Params, raw = t.Value.RawSql });
        return Results.Ok(ApiResponse.Success(taskList));
    })
        .WithName("ListTasks")
        .WithTags("Tasks")
        .WithSummary("List registered tasks")
        .WithDescription("Lists the SQL tasks loaded from the configured SQL directories at startup. Pass ?sql to get just the task names.");

    app.MapPost("/tasks/{taskName}/jobs", async (string taskName, HttpContext ctx) =>
    {
        // A task that needs no args/overrides shouldn't require a client to send "{}" -
        // no body (or a literal `null`) just means "use all the defaults".
        JobReq request;
        if (ctx.Request.ContentLength is null or 0)
        {
            request = new JobReq();
        }
        else
        {
            try { request = await ctx.Request.ReadFromJsonAsync<JobReq>() ?? new JobReq(); }
            catch { return Results.BadRequest(ApiResponse.Error("Invalid JSON body")); }
        }

        try
        {
            var resp = await core.NewJob(request, taskName);
            return Results.Ok(ApiResponse.Success(resp));
        }
        catch (ArgumentException ex) { return Results.BadRequest(ApiResponse.Error(ex.Message)); }
        catch (Exception ex) { return Results.Json(ApiResponse.Error(ex.Message), statusCode: 500); }
    })
        .Accepts<JobReq>("application/json")
        .WithName("SubmitJob")
        .WithTags("Jobs")
        .WithSummary("Submit a job for a task")
        .WithDescription("Queues a new job that executes the named task's SQL, optionally with args, a target db, queue, eta, retries and ttl.");

    app.MapGet("/jobs/{jobId}", async (string jobId) =>
    {
        try
        {
            var resp = await core.GetJobStatus(jobId);
            return Results.Ok(ApiResponse.Success(resp));
        }
        catch (KeyNotFoundException) { return Results.NotFound(ApiResponse.Error($"Job '{jobId}' not found")); }
    })
        .WithName("GetJobStatus")
        .WithTags("Jobs")
        .WithSummary("Get job status")
        .WithDescription("Returns a job's current state (PENDING/STARTED/SUCCESS/FAILURE/RETRY), row count and error, if any.");

    app.MapGet("/jobs/{jobId}/data", async (string jobId, HttpContext ctx) =>
    {
        var query = ctx.Request.Query;
        var limit = 100;
        if (query.TryGetValue("limit", out var limitValues) && int.TryParse(limitValues, out var parsedLimit))
            limit = parsedLimit;

        var all = query.TryGetValue("all", out var allValues) && bool.TryParse(allValues, out var parsedAll) && parsedAll;
        int? page = null;
        int? pageSize = null;
        var parsedPage = 0;
        var parsedPageSize = 0;

        var hasPage = query.TryGetValue("page", out var pageValues) && int.TryParse(pageValues, out parsedPage);
        if (hasPage) page = parsedPage;

        var hasPageSize = query.TryGetValue("page_size", out var pageSizeValues) && int.TryParse(pageSizeValues, out parsedPageSize);
        if (!hasPageSize)
            hasPageSize = query.TryGetValue("pageSize", out pageSizeValues) && int.TryParse(pageSizeValues, out parsedPageSize);
        if (hasPageSize) pageSize = parsedPageSize;

        if (all && (hasPage || hasPageSize))
            return Results.BadRequest(ApiResponse.Error("Use either all=true or page/page_size, not both"));

        if (hasPage != hasPageSize)
            return Results.BadRequest(ApiResponse.Error("Both page and page_size are required for pagination"));

        try
        {
            if (all)
            {
                var allRows = await core.GetAllJobData(jobId);
                return Results.Ok(ApiResponse.Success(allRows));
            }

            if (hasPage && hasPageSize)
            {
                var (total, pageRows) = await core.GetJobDataPage(jobId, page!.Value, pageSize!.Value);
                var totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize.Value);
                return Results.Ok(ApiResponse.Success(new
                {
                    job_id = jobId,
                    page = page.Value,
                    page_size = pageSize.Value,
                    total,
                    total_pages = totalPages,
                    count = pageRows.Count,
                    data = pageRows
                }));
            }

            var rows = await core.GetJobData(jobId, limit);
            return Results.Ok(ApiResponse.Success(rows));
        }
        catch (ArgumentOutOfRangeException ex) { return Results.BadRequest(ApiResponse.Error(ex.Message)); }
        catch (InvalidOperationException ex) { return Results.BadRequest(ApiResponse.Error(ex.Message)); }
        catch (KeyNotFoundException ex) { return Results.NotFound(ApiResponse.Error(ex.Message)); }
    })
        .WithName("GetJobData")
        .WithTags("Jobs")
        .WithSummary("Get a completed job's result rows")
        .WithDescription("Reads rows from a completed job's result table. Use limit, or all=true, or page/page_size for pagination.");

    app.MapGet("/jobs/queue/{queue}", async (string queue) =>
    {
        var pending = await core.GetPendingJobs(queue);
        return Results.Ok(ApiResponse.Success(pending));
    })
        .WithName("ListPendingJobs")
        .WithTags("Jobs")
        .WithSummary("List pending jobs in a queue")
        .WithDescription("Returns all jobs currently waiting in the named Redis queue.");

    app.MapDelete("/jobs/{jobId}", async (string jobId, HttpContext ctx) =>
    {
        var purge = ctx.Request.Query.ContainsKey("purge") && ctx.Request.Query["purge"] != "false";
        try
        {
            await core.CancelJob(jobId, purge);
            return Results.Ok(ApiResponse.Success(true));
        }
        catch (KeyNotFoundException ex) { return Results.Json(ApiResponse.Error(ex.Message), statusCode: 500); }
        catch (InvalidOperationException ex) { return Results.Json(ApiResponse.Error(ex.Message), statusCode: 500); }
    })
        .WithName("CancelJob")
        .WithTags("Jobs")
        .WithSummary("Cancel or delete a job")
        .WithDescription("Cancels a pending/running job. Completed jobs require purge=true to delete.");

    app.MapPost("/groups", async (HttpContext ctx) =>
    {
        GroupReq? request;
        try { request = await ctx.Request.ReadFromJsonAsync<GroupReq>(); }
        catch { return Results.BadRequest(ApiResponse.Error("Invalid JSON body")); }
        if (request == null || request.Jobs.Count == 0) return Results.BadRequest(ApiResponse.Error("Empty or invalid group request"));

        try
        {
            var resp = await core.NewJobGroup(request);
            return Results.Ok(ApiResponse.Success(resp));
        }
        catch (Exception ex) { return Results.Json(ApiResponse.Error(ex.Message), statusCode: 500); }
    })
        .Accepts<GroupReq>("application/json")
        .WithName("SubmitJobGroup")
        .WithTags("Groups")
        .WithSummary("Submit a group of jobs")
        .WithDescription("Queues multiple jobs together under one group id, so their collective status can be tracked.");

    app.MapGet("/groups/{groupId}", async (string groupId) =>
    {
        try
        {
            var resp = await core.GetJobGroupStatus(groupId);
            return Results.Ok(ApiResponse.Success(resp));
        }
        catch (KeyNotFoundException) { return Results.NotFound(ApiResponse.Error($"Group '{groupId}' not found")); }
    })
        .WithName("GetJobGroupStatus")
        .WithTags("Groups")
        .WithSummary("Get group status")
        .WithDescription("Returns the collective state (STARTED/SUCCESS/FAILURE) of every job in the group.");

    app.MapDelete("/groups/{groupId}", async (string groupId, HttpContext ctx) =>
    {
        var purge = ctx.Request.Query.ContainsKey("purge") && ctx.Request.Query["purge"] != "false";
        try
        {
            await core.CancelJobGroup(groupId, purge);
            return Results.Ok(ApiResponse.Success(true));
        }
        catch (KeyNotFoundException ex) { return Results.Json(ApiResponse.Error(ex.Message), statusCode: 500); }
        catch (InvalidOperationException ex) { return Results.Json(ApiResponse.Error(ex.Message), statusCode: 500); }
    })
        .WithName("CancelJobGroup")
        .WithTags("Groups")
        .WithSummary("Cancel or delete a group")
        .WithDescription("Cancels every job in the group. Completed groups require purge=true to delete.");
}

await app.RunAsync();
