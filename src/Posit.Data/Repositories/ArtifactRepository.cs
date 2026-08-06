using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Posit.Data.Configuration;

namespace Posit.Data.Repositories;

/// <summary>
/// Stores and retrieves artifacts in the posit_artifacts schema.
/// Every phase output is persisted here so artifacts survive process exit
/// and can be loaded for session resume.
/// </summary>
public sealed class ArtifactRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public ArtifactRepository(NpgsqlDataSource? dataSource = null)
    {
        _dataSource = dataSource ?? DbConnectionProvider.CreateDataSource();
    }

    public async Task StageAsync(ArtifactBundle bundle, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO posit_artifacts.artifacts
                (id, session_id, source_phase, schema_version, kind, payload_json, produced_at)
            VALUES (@id, @sid, @phase, @sv, @kind, @payload, @at)
            ON CONFLICT (id) DO UPDATE SET payload_json = EXCLUDED.payload_json",
            conn);

        cmd.Parameters.AddWithValue("id", bundle.Id.Value);
        cmd.Parameters.AddWithValue("sid", bundle.SessionId.Value);
        cmd.Parameters.AddWithValue("phase", bundle.SourcePhase.Value);
        cmd.Parameters.AddWithValue("sv", bundle.SchemaVersion);
        cmd.Parameters.AddWithValue("kind", bundle.Kind.ToString());
        cmd.Parameters.AddWithValue("payload", bundle.PayloadJson).NpgsqlDbType = NpgsqlDbType.Jsonb;
        cmd.Parameters.AddWithValue("at", bundle.ProducedAt);

        await cmd.ExecuteNonQueryAsync(ct);

        // Store lineage
        foreach (var ref_ in bundle.References)
        {
            await using var lineageCmd = new NpgsqlCommand(@"
                INSERT INTO posit_artifacts.artifact_lineage (artifact_id, parent_artifact_id)
                VALUES (@child, @parent) ON CONFLICT DO NOTHING",
                conn);
            lineageCmd.Parameters.AddWithValue("child", bundle.Id.Value);
            lineageCmd.Parameters.AddWithValue("parent", ref_.Id.Value);
            await lineageCmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<ArtifactBundle?> GetAsync(ArtifactId id, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT id, session_id, source_phase, schema_version, kind, payload_json, produced_at FROM posit_artifacts.artifacts WHERE id = @id",
            conn);
        cmd.Parameters.AddWithValue("id", id.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return ReadArtifactBundle(reader);
    }

    public async Task<ArtifactBundle[]> GetByPhaseAsync(SessionId sessionId, PhaseId phaseId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT id, session_id, source_phase, schema_version, kind, payload_json, produced_at FROM posit_artifacts.artifacts WHERE session_id = @sid AND source_phase = @phase ORDER BY produced_at",
            conn);
        cmd.Parameters.AddWithValue("sid", sessionId.Value);
        cmd.Parameters.AddWithValue("phase", phaseId.Value);

        var results = new List<ArtifactBundle>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(ReadArtifactBundle(reader));

        return [.. results];
    }

    public async Task<ArtifactBundle[]> ListBySessionAsync(SessionId sessionId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT id, session_id, source_phase, schema_version, kind, payload_json, produced_at FROM posit_artifacts.artifacts WHERE session_id = @sid ORDER BY produced_at",
            conn);
        cmd.Parameters.AddWithValue("sid", sessionId.Value);

        var results = new List<ArtifactBundle>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(ReadArtifactBundle(reader));

        return [.. results];
    }

    private static ArtifactBundle ReadArtifactBundle(NpgsqlDataReader reader)
    {
        var payloadBytes = reader.GetFieldValue<byte[]>(5);
        return new ArtifactBundle
        {
            Id = new ArtifactId(reader.GetString(0)),
            SessionId = new SessionId(reader.GetString(1)),
            SourcePhase = new PhaseId(reader.GetString(2)),
            SchemaVersion = reader.GetString(3),
            Kind = Enum.Parse<ArtifactKind>(reader.GetString(4)),
            PayloadJson = payloadBytes,
            ProducedAt = reader.GetDateTime(6)
        };
    }
}