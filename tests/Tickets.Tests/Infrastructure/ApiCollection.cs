namespace Tickets.Tests.Infrastructure;

/// <summary>All API integration tests share one SQL Server container and run sequentially.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}
