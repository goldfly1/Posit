using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Posit.Data.Configuration;

namespace Posit.Data.Migrations;

public sealed record MigrationRecord(string Id, DateTimeOffset AppliedAt, string Checksum, string AppliedBy);

public sealed class MigrationRunner
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly string _migrationsDirectory;

    public MigrationRunner(NpgsqlDataSource dataSource, string migrationsDirectory)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _migrationsDirectory = migrationsDirectory ?? throw new ArgumentNullException(nameof(migrationsDirectory));
    }

    public async Task<IReadOnlyList<MigrationRecord>> ApplyAsync(CancellationToken ct = default)
    {
        var files = Directory.EnumerateFiles(_migrationsDirectory, "*.sql")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

        var records = new List<MigrationRecord>();

        await using var connection = await _dataSource.OpenConnectionAsync(ct);

        // Bootstrap migration tracking table
        await using (var bootstrap = connection.CreateCommand())
        {
            bootstrap.CommandText = @"
CREATE SCHEMA IF NOT EXISTS posit_meta;
CREATE TABLE IF NOT EXISTS posit_meta.migrations (
    id          TEXT NOT NULL PRIMARY KEY,
    applied_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    checksum    TEXT NOT NULL,
    applied_by  TEXT NOT NULL
);";
            await bootstrap.ExecuteNonQueryAsync(ct);
        }

        await using var transaction = await connection.BeginTransactionAsync(ct);

        try
        {
            foreach (var file in files)
            {
                var content = await File.ReadAllTextAsync(file, ct);
                var id = Path.GetFileNameWithoutExtension(file);
                var checksum = ComputeChecksum(content);

                // Check if already applied
                var existing = await LoadRecordAsync(connection, transaction, id, ct);
                if (existing is not null)
                {
                    if (existing.Checksum != checksum)
                        throw new InvalidOperationException($"Migration checksum mismatch for {id}.");
                    records.Add(existing);
                    continue;
                }

                // Execute migration
                await using var cmd = new NpgsqlCommand(content, connection, transaction);
                await cmd.ExecuteNonQueryAsync(ct);

                // Record
                var record = new MigrationRecord(id, DateTimeOffset.UtcNow, checksum, Environment.MachineName);
                await using var insert = new NpgsqlCommand(
                    "INSERT INTO posit_meta.migrations (id, applied_at, checksum, applied_by) VALUES (@id, @at, @cs, @by)",
                    connection, transaction);
                insert.Parameters.AddWithValue("id", record.Id);
                insert.Parameters.AddWithValue("at", record.AppliedAt);
                insert.Parameters.AddWithValue("cs", record.Checksum);
                insert.Parameters.AddWithValue("by", record.AppliedBy);
                await insert.ExecuteNonQueryAsync(ct);

                records.Add(record);
                Console.Error.WriteLine($"[Posit] Migration {id} applied");
            }

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        await connection.ReloadTypesAsync(ct);
        return records;
    }

    public async Task<IReadOnlyList<MigrationRecord>> GetAppliedAsync(CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT id, applied_at, checksum, applied_by FROM posit_meta.migrations ORDER BY id");

        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            var records = new List<MigrationRecord>();
            while (await reader.ReadAsync(ct))
            {
                records.Add(new MigrationRecord(
                    reader.GetString(0),
                    reader.GetDateTime(1),
                    reader.GetString(2),
                    reader.GetString(3)));
            }
            return records;
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
        {
            return [];
        }
    }

    private static async Task<MigrationRecord?> LoadRecordAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string id, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT id, applied_at, checksum, applied_by FROM posit_meta.migrations WHERE id = @id",
            connection, transaction);
        command.Parameters.AddWithValue("id", id);

        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;
            return new MigrationRecord(
                reader.GetString(0),
                reader.GetDateTime(1),
                reader.GetString(2),
                reader.GetString(3));
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
        {
            return null;
        }
    }

    private static string ComputeChecksum(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}