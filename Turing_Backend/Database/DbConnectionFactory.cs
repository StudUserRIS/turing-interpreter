using System.Data;
using Npgsql;

namespace Turing_Backend.Database;

public class DbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Не задана строка подключения 'Postgres' в appsettings.json");
    }

    public IDbConnection Create()
    {
        var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
