using Posit.Web.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<QaGuiService>();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
var app = builder.Build();
app.UseStaticFiles();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// ── Trial listing ──────────────────────────────────────────────────
app.MapGet("/api/trials", async (QaGuiService svc, CancellationToken ct) =>
    Results.Ok(await svc.ListTrialsAsync(ct)));

// ── Trial detail ────────────────────────────────────────────────────
app.MapGet("/api/trials/{sessionId}", async (string sessionId, QaGuiService svc, CancellationToken ct) =>
{
    var detail = await svc.LoadTrialAsync(sessionId, ct);
    return detail == null ? Results.NotFound() : Results.Ok(detail);
});

// ── Run one test case ────────────────────────────────────────────────
app.MapPost("/api/run", async (RunRequest req, QaGuiService svc, CancellationToken ct) =>
{
    var result = await svc.RunTestAsync(req.SessionId, req.Input, req.ExpectedOutput,
        req.ExpectedExitCode, req.ExpectedBehavior, req.SystemContext, req.IsStdin, ct);
    return Results.Ok(result);
});

// ── Search ──────────────────────────────────────────────────────────
app.MapPost("/api/search", async (SearchRequest req, QaGuiService svc, CancellationToken ct) =>
    Results.Ok(await svc.SearchAsync(req.Query, ct)));

// ── DB browser ──────────────────────────────────────────────────────
app.MapGet("/api/db/{schema}/{table}", async (string schema, string table, QaGuiService svc, int? page, int? pageSize, string? filter, CancellationToken ct) =>
{
    try
    {
        var data = await svc.GetTableAsync(schema, table, page ?? 1, pageSize ?? 50, filter, ct);
        return Results.Ok(data);
    }
    catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
    catch (Exception ex) { return Results.Problem(detail: ex.Message, statusCode: 500); }
});

app.Run();

public sealed class RunRequest
{
    public string SessionId { get; set; } = "";
    public string Input { get; set; } = "";
    public string ExpectedOutput { get; set; } = "";
    public int ExpectedExitCode { get; set; }
    public string ExpectedBehavior { get; set; } = "";
    public string SystemContext { get; set; } = "";
    public bool IsStdin { get; set; }
}

public sealed class SearchRequest
{
    public string Query { get; set; } = "";
}