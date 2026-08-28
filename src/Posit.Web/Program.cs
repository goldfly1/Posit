using Npgsql;
using Posit.Web.Data;
using Posit.Data.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server
builder.Services.AddRazorPages(options =>
{
    options.RootDirectory = "/Pages";
});
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<QaDashboardRepository>();

var app = builder.Build();

app.UseStaticFiles();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();