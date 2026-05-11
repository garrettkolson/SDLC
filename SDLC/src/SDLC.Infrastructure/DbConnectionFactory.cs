using Microsoft.Data.Sqlite;
using System.Data;
using System.Data.Common;

namespace SDLC.Infrastructure;

public interface IDbConnectionFactory
{
    DbConnection Create();
}

public class SqlDbConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlDbConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DbConnection Create() => new SqliteConnection(_connectionString);
}
