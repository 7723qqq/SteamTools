using Microsoft.AspNetCore.HostFiltering;
using NLog.Web;

// ReSharper disable once CheckNamespace
namespace BD.WTTS.Services.Implementation;

sealed partial class YarpReverseProxyServiceImpl : ReverseProxyServiceImpl, IReverseProxyService, IReverseProxySettings, IAsyncDisposable
{
    static readonly string RootPath = Path.Combine(IOPath.AppDataDirectory, "Yarp");

    WebApplication? app;
    int proxyHealthy;
    readonly SemaphoreSlim lifecycleLock = new(1, 1);
    readonly IPCSubProcessService ipc;
    readonly IPCToastService toast;
    readonly IPCPlatformService platformService;

    public YarpReverseProxyServiceImpl(
        IPCSubProcessService ipc,
        DnsAnalysisServiceImpl dnsAnalysisServiceImpl,
        DnsDohAnalysisService dnsDohAnalysisService,
        ICertificateManager certificateManager) : base(dnsAnalysisServiceImpl, dnsDohAnalysisService)
    {
        this.ipc = ipc;
        platformService = GetIPCService<IPCPlatformService>(nameof(platformService));
        toast = GetIPCService<IPCToastService>(nameof(toast));
        CertificateManager = (CertificateManagerImpl)certificateManager;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    TIPCService GetIPCService<TIPCService>(string paramName) where TIPCService : class
    {
        var ipcService = ipc.GetService<TIPCService>() ?? throw new ArgumentNullException(paramName);
        ipcAddSingletonServices += s =>
        {
            s.AddSingleton(ipcService);
        };
        return ipcService;
    }

    public override CertificateManagerImpl CertificateManager { get; }

    public override bool ProxyRunning => app != null && Volatile.Read(ref proxyHealthy) != 0;

    public void UpdateServerSideProxyToken(string? token)
        => ServerSideProxyToken = token;

    protected override StartProxyResultCode CheckRootCertificate()
    {
        if (OperatingSystem.IsWindows())
        {
            var certificateResult = ICertificateManager.Constants.CheckRootCertificate(
                platformService,
                CertificateManager);
            if (certificateResult != StartProxyResultCode.Ok)
                return certificateResult;
        }

        StartCertificateTimer();
        return StartProxyResultCode.Ok;
    }

    private System.Timers.Timer? _certificateTimer;
    private int certificateRenewalRunning;

    private void StartCertificateTimer()
    {
        StopCertificateTimer();
        _certificateTimer = new System.Timers.Timer(TimeSpan.FromMinutes(5))
        {
            AutoReset = true,
            Interval = TimeSpan.FromDays(1).TotalMilliseconds,
        };
        _certificateTimer.Elapsed += (_, _) => _ = CheckCertificateRenewalAsync();
        _certificateTimer.Start();
    }

    async Task CheckCertificateRenewalAsync()
    {
        if (Interlocked.CompareExchange(ref certificateRenewalRunning, 1, 0) != 0)
            return;
        try
        {
            X509Certificate2? certificate = CertificateManager.RootCertificatePackable;
            if (certificate == null ||
                !CertificateRenewalPolicy.ShouldRenew(DateTimeOffset.UtcNow, certificate.NotAfter))
            {
                return;
            }

            if (!CertificateManager.RenewRootCertificate())
                return;

            app?.Services.GetRequiredService<CertService>()?.ReloadRootCertificate();
        }
        catch (Exception e)
        {
            e.LogAndShowT(TAG, msg: "CheckRootCertificate in Timer.Elapsed Error");
        }
        finally
        {
            Interlocked.Exchange(ref certificateRenewalRunning, 0);
        }
    }

    private void StopCertificateTimer()
    {
        _certificateTimer?.Stop();
        _certificateTimer?.Dispose();
        _certificateTimer = null;
    }

    protected override Task<StartProxyResult> StartProxyImpl() => StartProxyCoreAsync();

    readonly string[] allowedHosts = ["*",];

#if WINDOWS
    /// <summary>
    /// https://learn.microsoft.com/zh-cn/dotnet/core/extensions/globalization-icu#determine-if-your-app-is-using-icu
    /// </summary>
    /// <returns></returns>
    static bool ICUMode()
    {
        SortVersion sortVersion = CultureInfo.InvariantCulture.CompareInfo.Version;
        byte[] bytes = sortVersion.SortId.ToByteArray();
        int version = bytes[3] << 24 | bytes[2] << 16 | bytes[1] << 8 | bytes[0];
        return version != 0 && version == sortVersion.FullVersion;
    }

    static bool IcuTest(IEnumerable<string> incoming)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362) && ICUMode())
        {
            // https://learn.microsoft.com/zh-cn/dotnet/core/extensions/globalization-icu#icu-on-windows
            // 与 .NET 6 和 .NET 5 相比，.NET 7 及更高版本能够在早期 Windows 版本上加载 ICU
            // 通常问题出在 1703 ~ 1903 之间的 Windows 版本
            try
            {
                // https://github.com/dotnet/aspnetcore/blob/v9.0.2/src/Middleware/HostFiltering/src/MiddlewareConfigurationManager.cs#L67
                foreach (var entry in incoming)
                {
                    // Punycode. Http.Sys requires you to register Unicode hosts, but the headers contain punycode.
                    var host = new HostString(entry).ToUriComponent();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
        else
        {
            return true;
        }
    }
#endif

    async Task<StartProxyResult> StartProxyCoreAsync()
    {
        await lifecycleLock.WaitAsync();
        try
        {
            return await StartProxyCoreAsyncCore();
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    async Task<StartProxyResult> StartProxyCoreAsyncCore()
    {
#if WINDOWS
        if (!IcuTest(allowedHosts))
        {
            return StartProxyResultCode.IcuTestFail;
        }
#endif
        // https://github.com/dotnetcore/FastGithub/blob/2.1.4/FastGithub/Program.cs#L29
        try
        {
            IOPath.DirCreateByNotExists(RootPath);
            IReverseProxyConfig.RefreshPorts();

            var applicationOptions = new WebApplicationOptions
            {
                ContentRootPath = RootPath,
                WebRootPath = RootPath,
            };
#if WTTS_NATIVE_AOT
            var builder = WebApplication.CreateSlimBuilder(applicationOptions);
            // The slim host omits these transports; acceleration still needs TLS and HTTP/3.
            builder.WebHost.UseKestrelHttpsConfiguration().UseQuic();
#else
            var builder = WebApplication.CreateBuilder(applicationOptions);
#endif

            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new LogConsoleService.Utf8StringLoggerProvider(AssemblyInfo.Accelerator));

            builder.Services.AddHostFiltering(o =>
            {
                o.AllowEmptyHosts = true;
                o.AllowedHosts = allowedHosts;
            });

            builder.Host.UseNLog();
            StartupConfigureServices(builder.Services);
            builder.WebHost.UseShutdownTimeout(TimeSpan.FromSeconds(1d));
            builder.WebHost.UseKestrel(options =>
            {
                options.AddServerHeader = false;
                options.RequestHeaderEncodingSelector = _ => Encoding.UTF8;
                options.ResponseHeaderEncodingSelector = _ => Encoding.UTF8;
                options.NoLimit();
#if WINDOWS
#if !NET7_0_OR_GREATER
                if (OperatingSystem2.IsWindows7())
                {
                    //https://github.com/dotnet/aspnetcore/issues/22563
                    options.ConfigureHttpsDefaults(httpsOptions =>
                    {
                        httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                    });
                }
#endif
                //options.ListenSshReverseProxy();
                //options.ListenGitReverseProxy();
#endif

                // 证书吊销列表分发服务在所有模式下都需要启动
                options.ListenCrl();

                if (ProxyMode is ProxyMode.System or ProxyMode.PAC or ProxyMode.VPN)
                {
                    options.ListenHttpProxy();
                }
                else
                {
                    options.ListenHttpsReverseProxy();
                    if (EnableHttpProxyToHttps)
                        options.ListenHttpReverseProxy();
                }
            });

            app = builder.Build();
            app.Lifetime.ApplicationStopped.Register(() => Volatile.Write(ref proxyHealthy, 0));
            app.UseHostFiltering();
            StartupConfigure(app);

            await app.StartAsync();
            Volatile.Write(ref proxyHealthy, 1);
            return StartProxyResultCode.Ok;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref proxyHealthy, 0);
            OnException(ex);
            StopCertificateTimer();
            var failedApp = app;
            app = null;
            if (failedApp != null)
            {
                try
                {
                    await failedApp.DisposeAsync();
                }
                catch (Exception disposeException)
                {
                    OnException(disposeException);
                }
            }
            return IsListenerBindFailure(ex)
                ? new StartProxyResult(StartProxyResultCode.ListenerBindFailed, ex.ToString())
                : ex;
        }
    }

    static bool IsListenerBindFailure(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is System.Net.Sockets.SocketException socketException &&
                socketException.SocketErrorCode is System.Net.Sockets.SocketError.AccessDenied or System.Net.Sockets.SocketError.AddressAlreadyInUse)
                return true;
        }
        return false;
    }

    public void Exit()
    {
        ipc.Dispose();
    }

    public async Task StopProxyAsync()
    {
        await lifecycleLock.WaitAsync();
        try
        {
            await StopProxyAsyncCore();
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    async Task StopProxyAsyncCore()
    {
        StopCertificateTimer();
        Scripts = null;
        if (app == null) return;
        var currentApp = app;
        app = null;
        Volatile.Write(ref proxyHealthy, 0);
        try
        {
            await currentApp.StopAsync();
        }
        finally
        {
            await currentApp.DisposeAsync();
        }
    }

    public byte[]? GetFlowStatistics_Bytes()
    {
        var flowAnalyzer = app?.Services.GetService<IFlowAnalyzer>();
        var flowStatistics = flowAnalyzer?.GetFlowStatistics();
        var bytes = Serializable.SMP2(flowStatistics);
        return bytes;
    }

    static readonly Guid logStreamId = Guid.NewGuid();

    public Task<ProxyLogUpdate?> GetLogUpdateAsync(Guid streamId, long offset)
    {
        var (end, reset, utf8) = LogConsoleService.Builder.ReadSince(streamId == logStreamId ? offset : -1);
        return Task.FromResult<ProxyLogUpdate?>(reset || utf8.Length != 0
            ? new(logStreamId, end, reset, utf8)
            : null);
    }

    // IDisposable

    protected override void DisposeCore()
    {
        (app as IDisposable)?.Dispose();
    }

    // https://docs.microsoft.com/zh-cn/dotnet/standard/garbage-collection/implementing-disposeasync#implement-both-dispose-and-async-dispose-patterns

    async ValueTask DisposeAsyncCore()
    {
        if (app is not null)
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);

        Dispose(disposing: false);
        GC.SuppressFinalize(this);
    }
}
