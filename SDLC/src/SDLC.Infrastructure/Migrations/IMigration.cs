using System.Data.Common;

namespace SDLC.Infrastructure.Migrations;

public interface IMigration
{
    int Version { get; }
    Task ApplyAsync(DbConnection conn);
}
