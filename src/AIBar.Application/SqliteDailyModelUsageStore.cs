using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Application;

public sealed record SourceContributionStatus(bool RebuildRequired);
public sealed record SourceReplacement(string Fingerprint, SessionCheckpoint? ExpectedCheckpoint, SessionCheckpoint Checkpoint, IReadOnlyList<DailyUsage> Usage);

public sealed class SqliteDailyModelUsageStore : IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly Action? _beforeCommit;

    public SqliteDailyModelUsageStore(string databasePath, Action? beforeCommit = null)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true, Pooling = false }.ToString();
        _beforeCommit = beforeCommit;
    }

    public async ValueTask SaveAsync(IReadOnlyList<DailyUsage> usage, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await SaveAsync(connection, transaction, usage, cancellationToken);
        await using var migration = connection.CreateCommand(); migration.Transaction = transaction;
        migration.CommandText = "UPDATE daily_model_usage_migration SET rebuild_required=1 WHERE EXISTS(SELECT 1 FROM daily_model_usage);";
        await migration.ExecuteNonQueryAsync(cancellationToken);
        _beforeCommit?.Invoke();
        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<SessionCheckpoint?> LoadCheckpointAsync(string sourceFingerprint, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT identity, length, last_write_utc_ticks, offset, parser_version, cumulative_input_tokens, cumulative_cached_input_tokens, cumulative_output_tokens FROM file_checkpoint WHERE source_fingerprint = $fingerprint;";
        command.Parameters.AddWithValue("$fingerprint", sourceFingerprint);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetString(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7)) : null;
    }

    public ValueTask SaveWithCheckpointAsync(IReadOnlyList<DailyUsage> usage, string sourceFingerprint, SessionCheckpoint? expectedCheckpoint, SessionCheckpoint checkpoint, CancellationToken cancellationToken) =>
        SaveWithCheckpointAsync(usage, sourceFingerprint, expectedCheckpoint, checkpoint, "legacy", cancellationToken);

    public async ValueTask SaveWithCheckpointAsync(IReadOnlyList<DailyUsage> usage, string sourceFingerprint, SessionCheckpoint? expectedCheckpoint, SessionCheckpoint checkpoint, string policyVersion, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = expectedCheckpoint is null
            ? "INSERT INTO file_checkpoint (source_fingerprint, identity, length, last_write_utc_ticks, offset, parser_version, cumulative_input_tokens, cumulative_cached_input_tokens, cumulative_output_tokens) VALUES ($fingerprint, $identity, $length, $mtime, $offset, $parser, $input, $cached, $output) ON CONFLICT(source_fingerprint) DO NOTHING;"
            : "UPDATE file_checkpoint SET identity=$identity, length=$length, last_write_utc_ticks=$mtime, offset=$offset, parser_version=$parser, cumulative_input_tokens=$input, cumulative_cached_input_tokens=$cached, cumulative_output_tokens=$output WHERE source_fingerprint=$fingerprint AND identity=$expectedIdentity AND length=$expectedLength AND last_write_utc_ticks=$expectedMtime AND offset=$expectedOffset AND parser_version=$expectedParser AND cumulative_input_tokens=$expectedInput AND cumulative_cached_input_tokens=$expectedCached AND cumulative_output_tokens=$expectedOutput;";
        command.Parameters.AddWithValue("$fingerprint", sourceFingerprint); command.Parameters.AddWithValue("$identity", checkpoint.Identity); command.Parameters.AddWithValue("$length", checkpoint.Length); command.Parameters.AddWithValue("$mtime", checkpoint.LastWriteUtcTicks); command.Parameters.AddWithValue("$offset", checkpoint.Offset); command.Parameters.AddWithValue("$parser", checkpoint.ParserVersion); command.Parameters.AddWithValue("$input", checkpoint.CumulativeInputTokens); command.Parameters.AddWithValue("$cached", checkpoint.CumulativeCachedInputTokens); command.Parameters.AddWithValue("$output", checkpoint.CumulativeOutputTokens);
        if (expectedCheckpoint is not null)
        {
            command.Parameters.AddWithValue("$expectedIdentity", expectedCheckpoint.Identity); command.Parameters.AddWithValue("$expectedLength", expectedCheckpoint.Length); command.Parameters.AddWithValue("$expectedMtime", expectedCheckpoint.LastWriteUtcTicks); command.Parameters.AddWithValue("$expectedOffset", expectedCheckpoint.Offset); command.Parameters.AddWithValue("$expectedParser", expectedCheckpoint.ParserVersion); command.Parameters.AddWithValue("$expectedInput", expectedCheckpoint.CumulativeInputTokens); command.Parameters.AddWithValue("$expectedCached", expectedCheckpoint.CumulativeCachedInputTokens); command.Parameters.AddWithValue("$expectedOutput", expectedCheckpoint.CumulativeOutputTokens);
        }
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The analytics checkpoint changed during the scan.");
        await SaveAsync(connection, transaction, usage, cancellationToken);
        await SaveContributionsAsync(connection, transaction, usage, sourceFingerprint, policyVersion, cancellationToken);
        _beforeCommit?.Invoke();
        await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<DailyUsage>> LoadContributionAsync(string sourceFingerprint, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT local_day, time_zone_id, observed_offset_minutes, model, input_tokens, cached_input_tokens, output_tokens FROM source_daily_model_usage WHERE source_fingerprint=$fingerprint ORDER BY local_day, time_zone_id, observed_offset_minutes, model;";
        command.Parameters.AddWithValue("$fingerprint", sourceFingerprint);
        return await ReadUsageAsync(command, cancellationToken);
    }

    public async ValueTask<string?> LoadContributionPolicyAsync(string sourceFingerprint, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT policy_version FROM source_contribution_state WHERE source_fingerprint=$fingerprint;";
        command.Parameters.AddWithValue("$fingerprint", sourceFingerprint);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async ValueTask<SourceContributionStatus> GetContributionStatusAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT rebuild_required OR (EXISTS(SELECT 1 FROM daily_model_usage) AND NOT EXISTS(SELECT 1 FROM source_contribution_state)) OR EXISTS(SELECT 1 FROM source_contribution_state WHERE policy_version IN ('', 'legacy')) FROM daily_model_usage_migration LIMIT 1;";
        return new(Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 0);
    }

    public async ValueTask ReplaceSourcesAsync(IReadOnlyList<SourceReplacement> sources, string policyVersion, bool fullRebuild, CancellationToken cancellationToken)
    {
        if (sources.Count == 0 || sources.Select(source => source.Fingerprint).Distinct(StringComparer.Ordinal).Count() != sources.Count) throw new ArgumentException("Sources must be unique and non-empty.", nameof(sources));
        await using var connection = await OpenAsync(cancellationToken); await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var status = connection.CreateCommand(); status.Transaction = transaction;
        status.CommandText = "SELECT rebuild_required OR (EXISTS(SELECT 1 FROM daily_model_usage) AND NOT EXISTS(SELECT 1 FROM source_contribution_state)) OR EXISTS(SELECT 1 FROM source_contribution_state WHERE policy_version IN ('', 'legacy')) FROM daily_model_usage_migration LIMIT 1;";
        var required = Convert.ToInt64(await status.ExecuteScalarAsync(cancellationToken)) != 0;
        if (required && !fullRebuild) throw new InvalidOperationException("A full source rescan is required before replacing legacy analytics.");
        status.CommandText = $"SELECT COUNT(*) = 0 OR (COUNT(*) = $count AND NOT EXISTS(SELECT 1 FROM file_checkpoint WHERE source_fingerprint NOT IN ({string.Join(',', sources.Select((_, index) => "$source" + index))}))) FROM file_checkpoint;";
        status.Parameters.AddWithValue("$count", sources.Count); foreach (var (source, index) in sources.Select((source, index) => (source, index))) status.Parameters.AddWithValue("$source" + index, source.Fingerprint);
        var completeCheckpointSet = Convert.ToInt64(await status.ExecuteScalarAsync(cancellationToken)) != 0;
        if ((fullRebuild && !completeCheckpointSet) || (!fullRebuild && await HasOtherPolicyAsync(connection, transaction, sources, policyVersion, cancellationToken))) throw new InvalidOperationException("All sources affected by the analytics policy must be rebuilt together.");
        foreach (var source in sources)
        {
            await SaveCheckpointAsync(connection, transaction, source.Fingerprint, source.ExpectedCheckpoint, source.Checkpoint, cancellationToken);
            await using var delete = connection.CreateCommand(); delete.Transaction = transaction; delete.CommandText = "DELETE FROM source_daily_model_usage WHERE source_fingerprint=$fingerprint; DELETE FROM source_contribution_state WHERE source_fingerprint=$fingerprint;"; delete.Parameters.AddWithValue("$fingerprint", source.Fingerprint); await delete.ExecuteNonQueryAsync(cancellationToken);
            await SaveContributionsAsync(connection, transaction, source.Usage, source.Fingerprint, policyVersion, cancellationToken);
        }
        await using var aggregate = connection.CreateCommand(); aggregate.Transaction = transaction;
        aggregate.CommandText = "DELETE FROM daily_model_usage; INSERT INTO daily_model_usage SELECT local_day, time_zone_id, observed_offset_minutes, model, SUM(input_tokens), SUM(cached_input_tokens), SUM(output_tokens) FROM source_daily_model_usage GROUP BY local_day, time_zone_id, observed_offset_minutes, model; UPDATE daily_model_usage_migration SET rebuild_required=0;";
        await aggregate.ExecuteNonQueryAsync(cancellationToken); _beforeCommit?.Invoke(); await transaction.CommitAsync(cancellationToken);
    }

    private static async ValueTask SaveAsync(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<DailyUsage> usage, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO daily_model_usage (local_day, time_zone_id, observed_offset_minutes, model, input_tokens, cached_input_tokens, output_tokens) VALUES ($day, $zone, $offset, $model, $input, $cached, $output) ON CONFLICT(local_day, time_zone_id, observed_offset_minutes, model) DO UPDATE SET input_tokens = input_tokens + excluded.input_tokens, cached_input_tokens = cached_input_tokens + excluded.cached_input_tokens, output_tokens = output_tokens + excluded.output_tokens WHERE input_tokens <= 9223372036854775807 - excluded.input_tokens AND cached_input_tokens <= 9223372036854775807 - excluded.cached_input_tokens AND output_tokens <= 9223372036854775807 - excluded.output_tokens;";
        foreach (var item in usage)
        {
            if (item.Tokens is { Input: < 0 } or { CachedInput: < 0 } or { Output: < 0 }) throw new ArgumentOutOfRangeException(nameof(usage));
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$day", item.LocalDay.ToString("O")); command.Parameters.AddWithValue("$zone", item.TimeZoneId); command.Parameters.AddWithValue("$offset", (long)item.ObservedOffset.TotalMinutes); command.Parameters.AddWithValue("$model", string.IsNullOrWhiteSpace(item.Model) ? AnalyticsPolicy.UnknownModel : item.Model); command.Parameters.AddWithValue("$input", item.Tokens.Input); command.Parameters.AddWithValue("$cached", item.Tokens.CachedInput); command.Parameters.AddWithValue("$output", item.Tokens.Output);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
                throw new OverflowException("Daily model usage token totals exceed Int64 capacity.");
        }
    }

    private static async ValueTask<bool> HasOtherPolicyAsync(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<SourceReplacement> sources, string policyVersion, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT EXISTS(SELECT 1 FROM source_contribution_state WHERE policy_version <> $policy AND source_fingerprint NOT IN ({string.Join(',', sources.Select((_, index) => "$source" + index))}));";
        command.Parameters.AddWithValue("$policy", policyVersion); foreach (var (source, index) in sources.Select((source, index) => (source, index))) command.Parameters.AddWithValue("$source" + index, source.Fingerprint);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    private static async ValueTask SaveCheckpointAsync(SqliteConnection connection, SqliteTransaction transaction, string fingerprint, SessionCheckpoint? expectedCheckpoint, SessionCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = expectedCheckpoint is null ? "INSERT INTO file_checkpoint (source_fingerprint, identity, length, last_write_utc_ticks, offset, parser_version, cumulative_input_tokens, cumulative_cached_input_tokens, cumulative_output_tokens) VALUES ($fingerprint, $identity, $length, $mtime, $offset, $parser, $input, $cached, $output) ON CONFLICT(source_fingerprint) DO NOTHING;" : "UPDATE file_checkpoint SET identity=$identity, length=$length, last_write_utc_ticks=$mtime, offset=$offset, parser_version=$parser, cumulative_input_tokens=$input, cumulative_cached_input_tokens=$cached, cumulative_output_tokens=$output WHERE source_fingerprint=$fingerprint AND identity=$expectedIdentity AND length=$expectedLength AND last_write_utc_ticks=$expectedMtime AND offset=$expectedOffset AND parser_version=$expectedParser AND cumulative_input_tokens=$expectedInput AND cumulative_cached_input_tokens=$expectedCached AND cumulative_output_tokens=$expectedOutput;";
        command.Parameters.AddWithValue("$fingerprint", fingerprint); command.Parameters.AddWithValue("$identity", checkpoint.Identity); command.Parameters.AddWithValue("$length", checkpoint.Length); command.Parameters.AddWithValue("$mtime", checkpoint.LastWriteUtcTicks); command.Parameters.AddWithValue("$offset", checkpoint.Offset); command.Parameters.AddWithValue("$parser", checkpoint.ParserVersion); command.Parameters.AddWithValue("$input", checkpoint.CumulativeInputTokens); command.Parameters.AddWithValue("$cached", checkpoint.CumulativeCachedInputTokens); command.Parameters.AddWithValue("$output", checkpoint.CumulativeOutputTokens);
        if (expectedCheckpoint is not null) { command.Parameters.AddWithValue("$expectedIdentity", expectedCheckpoint.Identity); command.Parameters.AddWithValue("$expectedLength", expectedCheckpoint.Length); command.Parameters.AddWithValue("$expectedMtime", expectedCheckpoint.LastWriteUtcTicks); command.Parameters.AddWithValue("$expectedOffset", expectedCheckpoint.Offset); command.Parameters.AddWithValue("$expectedParser", expectedCheckpoint.ParserVersion); command.Parameters.AddWithValue("$expectedInput", expectedCheckpoint.CumulativeInputTokens); command.Parameters.AddWithValue("$expectedCached", expectedCheckpoint.CumulativeCachedInputTokens); command.Parameters.AddWithValue("$expectedOutput", expectedCheckpoint.CumulativeOutputTokens); }
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("The analytics checkpoint changed during the scan.");
    }

    private static async ValueTask SaveContributionsAsync(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<DailyUsage> usage, string fingerprint, string policyVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(policyVersion)) throw new ArgumentException("A policy version is required.", nameof(policyVersion));
        await using var state = connection.CreateCommand(); state.Transaction = transaction;
        state.CommandText = "INSERT INTO source_contribution_state (source_fingerprint, policy_version) VALUES ($fingerprint, $policy) ON CONFLICT(source_fingerprint) DO UPDATE SET policy_version=excluded.policy_version WHERE policy_version=excluded.policy_version;";
        state.Parameters.AddWithValue("$fingerprint", fingerprint); state.Parameters.AddWithValue("$policy", policyVersion);
        if (await state.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("The analytics policy changed and requires a rebuild.");
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO source_daily_model_usage (source_fingerprint, local_day, time_zone_id, observed_offset_minutes, model, input_tokens, cached_input_tokens, output_tokens) VALUES ($fingerprint, $day, $zone, $offset, $model, $input, $cached, $output) ON CONFLICT(source_fingerprint, local_day, time_zone_id, observed_offset_minutes, model) DO UPDATE SET input_tokens=input_tokens + excluded.input_tokens, cached_input_tokens=cached_input_tokens + excluded.cached_input_tokens, output_tokens=output_tokens + excluded.output_tokens WHERE input_tokens <= 9223372036854775807 - excluded.input_tokens AND cached_input_tokens <= 9223372036854775807 - excluded.cached_input_tokens AND output_tokens <= 9223372036854775807 - excluded.output_tokens;";
        foreach (var item in usage)
        {
            command.Parameters.Clear(); command.Parameters.AddWithValue("$fingerprint", fingerprint); command.Parameters.AddWithValue("$day", item.LocalDay.ToString("O")); command.Parameters.AddWithValue("$zone", item.TimeZoneId); command.Parameters.AddWithValue("$offset", (long)item.ObservedOffset.TotalMinutes); command.Parameters.AddWithValue("$model", string.IsNullOrWhiteSpace(item.Model) ? AnalyticsPolicy.UnknownModel : item.Model); command.Parameters.AddWithValue("$input", item.Tokens.Input); command.Parameters.AddWithValue("$cached", item.Tokens.CachedInput); command.Parameters.AddWithValue("$output", item.Tokens.Output);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) throw new OverflowException("Source contribution token totals exceed Int64 capacity.");
        }
    }

    public async ValueTask<IReadOnlyList<DailyUsage>> LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT local_day, time_zone_id, observed_offset_minutes, model, input_tokens, cached_input_tokens, output_tokens FROM daily_model_usage ORDER BY local_day, time_zone_id, observed_offset_minutes, model;";
        return await ReadUsageAsync(command, cancellationToken);
    }

    private static async ValueTask<IReadOnlyList<DailyUsage>> ReadUsageAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var usage = new List<DailyUsage>();
        while (await reader.ReadAsync(cancellationToken))
            usage.Add(new(DateOnly.Parse(reader.GetString(0)), reader.GetString(1), TimeSpan.FromMinutes(reader.GetInt64(2)), reader.GetString(3), new(reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6))));
        return usage;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; CREATE TABLE IF NOT EXISTS daily_model_usage_schema (version INTEGER NOT NULL); INSERT INTO daily_model_usage_schema SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM daily_model_usage_schema);";
            await command.ExecuteNonQueryAsync(cancellationToken);
            command.CommandText = "SELECT MAX(version) FROM daily_model_usage_schema;";
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 2)
                throw new NotSupportedException("The daily model usage schema is newer than this application supports.");
            command.CommandText = "CREATE TABLE IF NOT EXISTS daily_model_usage (local_day TEXT NOT NULL, time_zone_id TEXT NOT NULL, observed_offset_minutes INTEGER NOT NULL, model TEXT NOT NULL, input_tokens INTEGER NOT NULL CHECK (input_tokens >= 0), cached_input_tokens INTEGER NOT NULL CHECK (cached_input_tokens >= 0), output_tokens INTEGER NOT NULL CHECK (output_tokens >= 0), PRIMARY KEY (local_day, time_zone_id, observed_offset_minutes, model)); CREATE TABLE IF NOT EXISTS file_checkpoint (source_fingerprint TEXT PRIMARY KEY, identity INTEGER NOT NULL, length INTEGER NOT NULL, last_write_utc_ticks INTEGER NOT NULL, offset INTEGER NOT NULL, parser_version TEXT NOT NULL, cumulative_input_tokens INTEGER NOT NULL, cumulative_cached_input_tokens INTEGER NOT NULL, cumulative_output_tokens INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS source_contribution_state (source_fingerprint TEXT PRIMARY KEY, policy_version TEXT NOT NULL); CREATE TABLE IF NOT EXISTS source_daily_model_usage (source_fingerprint TEXT NOT NULL, local_day TEXT NOT NULL, time_zone_id TEXT NOT NULL, observed_offset_minutes INTEGER NOT NULL, model TEXT NOT NULL, input_tokens INTEGER NOT NULL CHECK (input_tokens >= 0), cached_input_tokens INTEGER NOT NULL CHECK (cached_input_tokens >= 0), output_tokens INTEGER NOT NULL CHECK (output_tokens >= 0), PRIMARY KEY (source_fingerprint, local_day, time_zone_id, observed_offset_minutes, model)); CREATE TABLE IF NOT EXISTS daily_model_usage_migration (rebuild_required INTEGER NOT NULL); INSERT INTO daily_model_usage_migration SELECT EXISTS(SELECT 1 FROM daily_model_usage) WHERE NOT EXISTS(SELECT 1 FROM daily_model_usage_migration); INSERT INTO daily_model_usage_schema SELECT 2 WHERE (SELECT MAX(version) FROM daily_model_usage_schema) = 1;";
            await command.ExecuteNonQueryAsync(cancellationToken); return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
}
