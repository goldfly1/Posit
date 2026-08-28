using Posit.Web.Data;
using Posit.Data.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<QaDashboardRepository>();
builder.Services.AddSingleton<QaTerminalService>();

// DB browser — connection string from QaDashboardRepository's config
var connStr = builder.Configuration.GetConnectionString("PositDb")
    ?? "Host=localhost;Port=5434;Database=shepherd;Username=shepherd;Password=shepherd";
builder.Services.AddSingleton(new DbBrowserService(connStr));

var app = builder.Build();

app.UseStaticFiles();
app.MapBlazorHub();

// === DB Browser API ===
app.MapGet("/api/db/schema", async (DbBrowserService svc) =>
{
    return Results.Ok(await svc.GetSchemaAsync());
});

app.MapGet("/api/db/tables/{schema}/{table}", async (string schema, string table, DbBrowserService svc, int? page, int? pageSize, string? filter) =>
{
    try
    {
        var data = await svc.GetTableRowsAsync(schema, table, page ?? 1, pageSize ?? 50, filter);
        return Results.Ok(data);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

// === QA Terminal API ===
app.MapGet("/api/qa/sessions", async (QaDashboardRepository repo) =>
{
    return Results.Ok(await repo.GetSessionsAsync());
});

app.MapGet("/api/qa/load/{sessionId}", async (string sessionId, QaTerminalService svc) =>
{
    var session = await svc.LoadSessionAsync(sessionId);
    return session == null ? Results.NotFound() : Results.Ok(session);
});

app.MapGet("/api/qa/artifacts/{sessionId}", async (string sessionId, QaDashboardRepository repo) =>
{
    return Results.Ok(await repo.GetArtifactsAsync(sessionId));
});

app.MapPost("/api/qa/run", async (QaRunRequest req, QaTerminalService svc, CancellationToken ct) =>
{
    var result = await svc.RunTestCaseAsync(
        req.SessionId, req.Input, req.ExpectedOutput, req.ExpectedExitCode,
        req.ExpectedBehavior, req.SystemContext, req.IsStdin, ct);
    return Results.Ok(result);
});

app.MapFallbackToPage("/_Host");

app.Run();

public sealed class QaRunRequest
{
    public string SessionId { get; set; } = "";
    public string Input { get; set; } = "";
    public string ExpectedOutput { get; set; } = "";
    public int ExpectedExitCode { get; set; }
    public string ExpectedBehavior { get; set; } = "";
    public string SystemContext { get; set; } = "";
    public bool IsStdin { get; set; }
}