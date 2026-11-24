// https://github.com/dotnetcore/FastGithub/blob/2.1.4/FastGithub.Http/HttpClientFactory.cs

// ReSharper disable once CheckNamespace
using System.Net;

namespace BD.WTTS.Services.Implementation;

sealed class ReverseProxyHttpClientFactory : IReverseProxyHttpClientFactory
{
    readonly IDomainResolver domainResolver;
    readonly IWebProxy webProxy;

    /// <summary>
    /// 首次生命周期
    /// </summary>
    readonly TimeSpan firstLiftTime = TimeSpan.FromSeconds(10d);

    /// <summary>
    /// 非首次生命周期
    /// </summary>
    readonly TimeSpan nextLifeTime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// <see cref="LifetimeHttpHandler"/> 清理器
    /// </summary>
    readonly LifetimeHttpHandlerCleaner httpHandlerCleaner = new();

    /// <summary>
    /// Lazy(LifetimeHttpHandler) 缓存
    /// </summary>
    readonly ConcurrentDictionary<LifeTimeKey, Lazy<LifetimeEntry>> httpHandlerLazyCache = new();

    public ReverseProxyHttpClientFactory(IDomainResolver domainResolver, IReverseProxyConfig reverseProxyConfig)
    {
        this.domainResolver = domainResolver;
        if (reverseProxyConfig.Service.TwoLevelAgentEnable)
        {
            this.webProxy = new WebProxy($"{reverseProxyConfig.Service.TwoLevelAgentProxyType}://{reverseProxyConfig.Service.TwoLevelAgentIp}:{reverseProxyConfig.Service.TwoLevelAgentPortId}", true, null, null);
            if (!string.IsNullOrEmpty(reverseProxyConfig.Service.TwoLevelAgentUserName) || !string.IsNullOrEmpty(reverseProxyConfig.Service.TwoLevelAgentPassword))
            {
                webProxy.Credentials = new NetworkCredential(reverseProxyConfig.Service.TwoLevelAgentUserName, reverseProxyConfig.Service.TwoLevelAgentPassword);
            }
        }
        else
            this.webProxy = HttpNoProxy.Instance;
    }

    public ReverseProxyHttpClient CreateHttpClient(string domain, IDomainConfig domainConfig)
    {
        var lifeTimeKey = new LifeTimeKey(domain, domainConfig);
        var entry = httpHandlerLazyCache.GetOrAdd(lifeTimeKey,
            static (key, factory) => new Lazy<LifetimeEntry>(
                () => factory.CreateLifetimeEntry(key, factory.firstLiftTime), true), this).Value;
        return entry.Client;
    }

    LifetimeEntry CreateLifetimeEntry(LifeTimeKey lifeTimeKey, TimeSpan lifeTime)
    {
        var handler = CreateLifetimeHttpHandler(lifeTimeKey, lifeTime);
        return new LifetimeEntry(handler, new ReverseProxyHttpClient(handler, disposeHandler: false));
    }

    sealed class LifetimeEntry
    {
        public LifetimeEntry(LifetimeHttpHandler handler, ReverseProxyHttpClient client)
        {
            Handler = handler;
            Client = client;
        }

        public LifetimeHttpHandler Handler { get; }
        public ReverseProxyHttpClient Client { get; }
    }

    LifetimeHttpHandler CreateLifetimeHttpHandler(LifeTimeKey lifeTimeKey, TimeSpan lifeTime)
        => new(domainResolver, webProxy, lifeTimeKey, lifeTime, OnLifetimeHttpHandlerDeactivate);

    void OnLifetimeHttpHandlerDeactivate(LifetimeHttpHandler handler)
    {
        var lifeTimeKey = handler.LifeTimeKey;
        httpHandlerLazyCache[lifeTimeKey] = CreateNextLifetimeEntryLazy(lifeTimeKey);
        httpHandlerCleaner.Add(handler);

        Lazy<LifetimeEntry> CreateNextLifetimeEntryLazy(LifeTimeKey lifeTimeKey)
            => new(() => CreateLifetimeEntry(lifeTimeKey, nextLifeTime), true);
    }
}