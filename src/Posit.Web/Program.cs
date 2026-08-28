using Posit.Web.Data;
using Posit.Data.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<QaDashboardRepository>();
builder.Services.AddSingleton<QaTerminalService>();

var app = builder.Build();

app.UseStaticFiles();
app.MapBlazorHub();

// API endpoints for the QA terminal
app.MapGet("/api/qa/sessions", async (QaDashboardRepository repo) =>
{
    return Results.Ok(await repo.GetSessionsAsync());
});

app.MapGet("/api/qa/load/{sessionId}", async (string sessionId, QaTerminalService svc) =>
{
    var session = await svc.LoadSessionAsync(sessionId);
    return session == null ? Results.NotFound() : Results.Ok(session);
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