using System.Net;
using WebReaper.Proxy;
using WebReaper.Proxy.Abstract;
using WebReaper.Proxy.Concrete;

namespace WebReaper.UnitTests
{
    public class ProxyValidationTests
    {
        private static WebProxy Proxy(int i) => new(new Uri($"http://10.0.0.{i}:8080"));

        private sealed class FakeValidator : IProxyValidator
        {
            private readonly Func<WebProxy, bool> _predicate;
            private readonly TimeSpan _delay;

            public FakeValidator(Func<WebProxy, bool> predicate, TimeSpan delay = default)
            {
                _predicate = predicate;
                _delay = delay;
            }

            public async Task<bool> IsValidAsync(WebProxy proxy, CancellationToken cancellationToken = default)
            {
                if (_delay > TimeSpan.Zero)
                {
                    await Task.Delay(_delay, cancellationToken);
                }

                return _predicate(proxy);
            }
        }

        private sealed class CountingSource : IProxySource
        {
            private readonly IReadOnlyList<WebProxy> _proxies;
            public int CallCount { get; private set; }

            public CountingSource(params WebProxy[] proxies) => _proxies = proxies;

            public Task<IReadOnlyList<WebProxy>> GetCandidatesAsync(CancellationToken cancellationToken = default)
            {
                CallCount++;
                return Task.FromResult(_proxies);
            }
        }

        [Fact]
        public async Task KeepsOnlyProxiesPassingValidator()
        {
            var source = new StaticProxySource(new[] { Proxy(1), Proxy(2), Proxy(3) });
            var onlyProxy2 = new FakeValidator(p => p.Address!.ToString().Contains("10.0.0.2"));

            var provider = new ValidatedProxyProvider(source, new[] { onlyProxy2 });

            for (var i = 0; i < 20; i++)
            {
                var picked = await provider.GetProxyAsync();
                Assert.Contains("10.0.0.2", picked.Address!.ToString());
            }
        }

        [Fact]
        public async Task AppliesAllValidatorsWithAndSemantics()
        {
            var source = new StaticProxySource(new[] { Proxy(1), Proxy(2) });
            var allowAll = new FakeValidator(_ => true);
            var rejectAll = new FakeValidator(_ => false);

            var provider = new ValidatedProxyProvider(source, new[] { allowAll, rejectAll });

            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetProxyAsync());
        }

        [Fact]
        public async Task ThrowsWhenNoProxyPassesValidation()
        {
            var source = new StaticProxySource(new[] { Proxy(1), Proxy(2) });
            var provider = new ValidatedProxyProvider(source, new[] { new FakeValidator(_ => false) });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetProxyAsync());
            Assert.Contains("no proxy passed validation", ex.Message);
        }

        [Fact]
        public async Task NoValidatorsMeansAcceptAll()
        {
            var source = new StaticProxySource(new[] { Proxy(1) });
            var provider = new ValidatedProxyProvider(source, Array.Empty<IProxyValidator>());

            var picked = await provider.GetProxyAsync();
            Assert.Contains("10.0.0.1", picked.Address!.ToString());
        }

        [Fact]
        public async Task SlowValidatorBeyondTimeoutIsTreatedAsInvalid()
        {
            var source = new StaticProxySource(new[] { Proxy(1) });
            var slow = new FakeValidator(_ => true, delay: TimeSpan.FromSeconds(5));
            var options = new ValidatedProxyProviderOptions { ValidationTimeout = TimeSpan.FromMilliseconds(100) };

            var provider = new ValidatedProxyProvider(source, new[] { slow }, options);

            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetProxyAsync());
        }

        [Fact]
        public async Task CachesValidatedSetWithinRefreshInterval()
        {
            var source = new CountingSource(Proxy(1), Proxy(2));
            var options = new ValidatedProxyProviderOptions { RefreshInterval = TimeSpan.FromMinutes(10) };
            var provider = new ValidatedProxyProvider(source, new[] { new FakeValidator(_ => true) }, options);

            for (var i = 0; i < 50; i++)
            {
                await provider.GetProxyAsync();
            }

            Assert.Equal(1, source.CallCount);
        }

        [Fact]
        public async Task StaticProxySourceParsesAddresses()
        {
            var source = StaticProxySource.FromAddresses(new[] { "1.2.3.4:8080", "http://5.6.7.8:3128" });

            var proxies = await source.GetCandidatesAsync();

            Assert.Equal(2, proxies.Count);
            Assert.Equal("http://1.2.3.4:8080/", proxies[0].Address!.ToString());
            Assert.Equal("http://5.6.7.8:3128/", proxies[1].Address!.ToString());
        }
    }
}
