using Npgsql;

namespace Posit.Data.Configuration;

/// <summary>
/// Provides the database connection string for Posit.
/// Checks POSIT_DB env var first, then falls back to the shared Shepherd DB
/// on localhost:5434 (Posit shares the wiki chunks with Shepherd).
/// </summary>
public static class DbConnectionProvider
{
    public static string GetConnectionString()
    {
        var env = Environment.GetEnvironmentVariable("POSIT_DB");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        // Fall back to shared Shepherd DB (same PG18 instance, same wiki chunks)
        return "Host=localhost;Port=5434;Database=shepherd;Username=shepherd;Password=shepherd";
    }

    public static NpgsqlDataSource CreateDataSource()
    {
        var connString = GetConnectionString();
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connString);
        dataSourceBuilder.EnableDynamicJson();
        return dataSourceBuilder.Build();
    }
}