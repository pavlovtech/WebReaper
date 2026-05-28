using WebReaper.TestServer;
using Xunit;

namespace WebReaper.IntegrationTests.Fixtures;

/// <summary>
/// Owns one <see cref="LocalTestSite"/> for the whole "LocalSite" collection —
/// the deterministic in-process Kestrel fixtures are read-only, so a single
/// site is shared across every test class in the collection (one boot, not one
/// per class). Per-test isolation comes from distinct URLs / fail-keys, not
/// distinct sites.
/// </summary>
public sealed class LocalSiteFixture : IAsyncLifetime
{
    public LocalTestSite Site { get; private set; } = null!;

    public async Task InitializeAsync() => Site = await LocalTestSite.StartAsync();

    public async Task DisposeAsync() => await Site.DisposeAsync();
}

/// <summary>The xUnit collection that shares one <see cref="LocalSiteFixture"/>.
/// Test classes opt in with <c>[Collection("LocalSite")]</c>.</summary>
[CollectionDefinition("LocalSite")]
public sealed class LocalSiteCollection : ICollectionFixture<LocalSiteFixture>;
