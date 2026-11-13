using GovBidPro.Testing;
using Microsoft.EntityFrameworkCore;

namespace GovBidPro.IntegrationTests;

[Collection(PostgresTestGroup.Name)]
public sealed class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_apply_cleanly_and_match_the_model()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connectionString, PostgresFixture.SystemContext());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
