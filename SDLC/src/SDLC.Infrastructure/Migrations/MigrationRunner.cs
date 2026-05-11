using System.Data.Common;
using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging;
using SDLC.Infrastructure.Migrations;

namespace SDLC.Infrastructure;

public class MigrationRunner
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<MigrationRunner>? _logger;

    public MigrationRunner(IDbConnectionFactory factory, ILogger<MigrationRunner>? logger = null)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();

        // Ensure _migrations table exists (created by the migration system itself)
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS _migrations (
                version INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL
            )");

        var appliedVersions = (await conn.QueryAsync<int>(
            "SELECT version FROM _migrations ORDER BY version"))
            .ToHashSet();

        var migrations = typeof(MigrationRunner)
            .Assembly
            .GetTypes()
            .Where(t => typeof(IMigration).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => (IMigration)Activator.CreateInstance(t)!)
            .OrderBy(m => m.Version)
            .ToList();

        var pending = migrations.Where(m => !appliedVersions.Contains(m.Version)).ToList();

        if (pending.Count == 0)
        {
            _logger?.LogInformation("No pending migrations.");
            return;
        }

        _logger?.LogInformation("Applying {Count} pending migration(s)...", pending.Count);

        foreach (var migration in pending)
        {
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await migration.ApplyAsync(conn);
                await conn.ExecuteAsync(
                    "INSERT INTO _migrations (version, applied_at) VALUES (@Version, @AppliedAt)",
                    new { migration.Version, AppliedAt = DateTimeOffset.UtcNow.ToString("o") },
                    (DbTransaction)tx);
                await tx.CommitAsync();
                _logger?.LogInformation("Applied migration {Version}.", migration.Version);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
    }
}
