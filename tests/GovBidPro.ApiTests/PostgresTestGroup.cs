using GovBidPro.Testing;

namespace GovBidPro.ApiTests;

[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres-api";
}
