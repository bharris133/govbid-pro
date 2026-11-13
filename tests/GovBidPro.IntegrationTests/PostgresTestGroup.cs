using GovBidPro.Testing;

namespace GovBidPro.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
