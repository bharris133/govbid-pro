using GovBidPro.Application.Abstractions;
using GovBidPro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GovBidPro.Testing;

/// <summary>
/// Provides throwaway PostgreSQL databases. Uses Testcontainers (CI default). Set
/// GOVBID_TEST_POSTGRES to an admin connection string to use an existing server instead.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string ExternalServerVariable = "GOVBID_TEST_POSTGRES";
    private PostgreSqlContainer? _container;
    private string _adminConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(ExternalServerVariable);
        if (!string.IsNullOrWhiteSpace(external))
        {
            _adminConnectionString = external;
            return;
        }

        _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _container.StartAsync();
        _adminConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = "govbid_test_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(_adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = name }.ConnectionString;
    }

    /// <summary>Creates a database and applies all migrations.</summary>
    public async Task<string> CreateMigratedDatabaseAsync()
    {
        var connectionString = await CreateEmptyDatabaseAsync();
        await using var context = CreateContext(connectionString, SystemContext());
        await context.Database.MigrateAsync();
        return connectionString;
    }

    public static GovBidProDbContext CreateContext(string connectionString, ITenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<GovBidProDbContext>().UseNpgsql(connectionString).Options;
        return new GovBidProDbContext(options, tenant);
    }

    public static TenantContextHolder SystemContext()
    {
        var holder = new TenantContextHolder();
        holder.SetSystem();
        return holder;
    }
}
