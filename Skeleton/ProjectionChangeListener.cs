using Npgsql;

namespace Skeleton;

public class ProjectionChangeListener(ILogger<ProjectionChangeListener> logger, IConfiguration configuration) : BackgroundService
{
    private NpgsqlConnection? _conn;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connString = configuration.GetConnectionString("Marten")
                         ?? throw new InvalidOperationException("Missing connection string 'Marten'.");

        _conn = new NpgsqlConnection(connString);
        _conn.Notification += OnNotificationReceived;

        await _conn.OpenAsync(stoppingToken);
        await CleanUpObsoleteTriggersAsync(stoppingToken);

        foreach (var projection in Enum.GetValues<ProjectionEnum>())
        {
            var projectionMetadata = ProjectionMetadata.FromEnum(projection);

            await CreateTriggerIfNotExistsAsync(projectionMetadata, stoppingToken);

            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"LISTEN {projectionMetadata.Channel};";
            await cmd.ExecuteNonQueryAsync(stoppingToken);
            logger.LogInformation("Listening to PostgreSQL channel: {Channel}", projectionMetadata.Channel);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _conn.WaitAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Connection to PostgreSQL LISTEN dropped. Retrying in 5s.");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task CreateTriggerIfNotExistsAsync(ProjectionMetadata meta, CancellationToken ct)
    {
        var tableExistsSql = $"""
                                  SELECT EXISTS (
                                      SELECT 1
                                      FROM information_schema.tables
                                      WHERE table_name = '{meta.TableName}'
                                  );
                              """;

        await using var tableCheckCmd = _conn!.CreateCommand();
        tableCheckCmd.CommandText = tableExistsSql;

        var tableExists = (bool)(await tableCheckCmd.ExecuteScalarAsync(ct))!;
        if (!tableExists)
        {
            logger.LogWarning("Skipping trigger creation for '{Table}' because the table does not exist yet.", meta.TableName);
            return;
        }

        var createFunctionSql = $"""
                                     CREATE OR REPLACE FUNCTION {meta.FunctionName}() RETURNS trigger AS $$
                                     BEGIN
                                         PERFORM pg_notify('{meta.Channel}', NEW.id::text);
                                         RETURN NEW;
                                     END;
                                     $$ LANGUAGE plpgsql;
                                 """;

        var triggerExistsSql = $"""
                                    SELECT 1 FROM pg_trigger 
                                    WHERE tgname = '{meta.TriggerName}' 
                                    AND tgrelid = '{meta.TableName}'::regclass;
                                """;

        var createTriggerSql = $"""
                                    CREATE TRIGGER {meta.TriggerName}
                                    AFTER INSERT OR UPDATE ON {meta.TableName}
                                    FOR EACH ROW
                                    EXECUTE FUNCTION {meta.FunctionName}();
                                """;

        await using var checkCmd = _conn!.CreateCommand();
        checkCmd.CommandText = triggerExistsSql;
        var exists = await checkCmd.ExecuteScalarAsync(ct) is not null;

        if (!exists)
        {
            await using var fnCmd = _conn.CreateCommand();
            fnCmd.CommandText = createFunctionSql;
            await fnCmd.ExecuteNonQueryAsync(ct);

            await using var trgCmd = _conn.CreateCommand();
            trgCmd.CommandText = createTriggerSql;
            await trgCmd.ExecuteNonQueryAsync(ct);

            logger.LogInformation("Created trigger and function for projection on table '{Table}'", meta.TableName);
        }
    }

    private async Task CleanUpObsoleteTriggersAsync(CancellationToken ct)
    {
        var expectedTriggers = Enum
            .GetValues<ProjectionEnum>()
            .Select(p => ProjectionMetadata.FromEnum(p).TriggerName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingTriggers = new List<ProjectionMetadata>();

        const string sql = """
                               SELECT tg.tgname, c.relname
                               FROM pg_trigger tg
                               JOIN pg_class c ON tg.tgrelid = c.oid
                               WHERE NOT tg.tgisinternal
                                 AND tg.tgname LIKE '%_trigger'
                           """;

        await using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var triggerName = reader.GetString(0);

                if (expectedTriggers.Contains(triggerName)) continue;
                var meta = ProjectionMetadata.FromTriggerName(triggerName);
                existingTriggers.Add(meta);
            }
        }

        foreach (var meta in existingTriggers)
        {
            var dropTrigger = $"DROP TRIGGER IF EXISTS {meta.TriggerName} ON {meta.TableName};";
            logger.LogInformation("Dropping obsolete trigger '{Trigger}' on table '{Table}'", meta.TriggerName, meta.TableName);

            await using var dropCmd = _conn.CreateCommand();
            dropCmd.CommandText = dropTrigger;
            await dropCmd.ExecuteNonQueryAsync(ct);

            var dropFn = $"DROP FUNCTION IF EXISTS {meta.FunctionName}();";
            await using var dropFnCmd = _conn.CreateCommand();
            dropFnCmd.CommandText = dropFn;
            await dropFnCmd.ExecuteNonQueryAsync(ct);

            logger.LogInformation("Also dropped function '{Function}'", meta.FunctionName);
        }
    }

    private void OnNotificationReceived(object? sender, NpgsqlNotificationEventArgs e)
    {
        var projectionMetadata = ProjectionMetadata.FromChannel(e.Channel);
        switch (projectionMetadata.ProjectionEnum)
        {
            case ProjectionEnum.ItemDetails:
            case ProjectionEnum.ItemSummary:
            case ProjectionEnum.ItemChangeLog:
                logger.LogInformation("Projection '{projection}' updated for ID: {id}", projectionMetadata.ProjectionEnum.Value.GetProjectionViewModelName(), e.Payload);
                // TODO: Send to SignalR/SSE/etc.
                break;
            case ProjectionEnum.OrderOverview:
                break;
        }
    }

    public override void Dispose()
    {
        _conn?.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}

public record ProjectionMetadata(
    string TableName,
    string Channel,
    string FunctionName,
    string TriggerName,
    ProjectionEnum? ProjectionEnum)
{
    private const string TablePrefix = "mt_doc_";
    private const string ChannelPrefix = "projection_updated_";
    private const string FunctionPrefix = "notify_";
    private const string FunctionSuffix = "_updated";
    private const string TriggerSuffix = "_trigger";

    public static ProjectionMetadata FromEnum(ProjectionEnum projectionEnum)
    {
        var enumName = projectionEnum.ToString().ToLowerInvariant();
        var tableName = $"{TablePrefix}{enumName}";

        return new ProjectionMetadata(
            tableName,
            $"{ChannelPrefix}{tableName}",
            $"{FunctionPrefix}{tableName}{FunctionSuffix}",
            $"{tableName}{TriggerSuffix}",
            projectionEnum
        );
    }

    public static ProjectionMetadata FromTriggerName(string triggerName)
    {
        if (!triggerName.EndsWith(TriggerSuffix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid trigger name: '{triggerName}'");

        var tableName = triggerName[..^TriggerSuffix.Length];
        var resolvedEnum = ResolveEnum(tableName);

        return new ProjectionMetadata(
            tableName,
            $"{ChannelPrefix}{tableName}",
            $"{FunctionPrefix}{tableName}{FunctionSuffix}",
            triggerName,
            resolvedEnum
        );
    }

    public static ProjectionMetadata FromChannel(string channel)
    {
        if (!channel.StartsWith(ChannelPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid channel name: '{channel}'");

        var tableName = channel[ChannelPrefix.Length..];
        var resolvedEnum = ResolveEnum(tableName);

        return new ProjectionMetadata(
            tableName,
            channel,
            $"{FunctionPrefix}{tableName}{FunctionSuffix}",
            $"{tableName}{TriggerSuffix}",
            resolvedEnum
        );
    }

    private static ProjectionEnum? ResolveEnum(string tableName)
    {
        foreach (var e in Enum.GetValues<ProjectionEnum>())
        {
            var expected = $"{TablePrefix}{e.ToString().ToLowerInvariant()}";
            if (string.Equals(expected, tableName, StringComparison.OrdinalIgnoreCase))
                return e;
        }

        return null;
    }
}