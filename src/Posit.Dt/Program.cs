using Posit.Dt.Components;
using Posit.Dt.Data;
using Npgsql;
using Posit.Data.Configuration;
using Posit.Data.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<NpgsqlDataSource>(_ => DbConnectionProvider.CreateDataSource());
builder.Services.AddSingleton<ArtifactRepository>(sp => new ArtifactRepository(sp.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddScoped<PositDashboardRepository>(sp => new PositDashboardRepository(sp.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddScoped<PositPromptRepository>(sp => new PositPromptRepository(sp.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddScoped<PositImplTraceRepository>(sp => new PositImplTraceRepository(sp.GetRequiredService<NpgsqlDataSource>()));
builder.Services.AddScoped<PositTrialRepository>(sp => new PositTrialRepository(sp.GetRequiredService<NpgsqlDataSource>()));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
