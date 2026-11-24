// https://github.com/dotnetcore/FastGithub/blob/2.1.4/FastGithub.HttpServer/CertService.cs

// ReSharper disable once CheckNamespace
using BD.Common.Columns;

namespace BD.WTTS.Services.Implementation;

/// <summary>
/// 证书服务
/// </summary>
sealed class CertService
{
    /// <summary>
    /// 域名证书缓存，存放 <see cref="Lazy{T}"/> 以便同一域名的并发握手只签发一次证书
    /// </summary>
    readonly ConcurrentDictionary<string, Lazy<X509Certificate2>> serverCerts = new();

    readonly ILogger<CertService> logger;
    readonly IReverseProxyConfig reverseProxyConfig;
    private X509Certificate2? caCert;

    /// <summary>
    /// 证书吊销列表有效期
    /// </summary>
    static readonly TimeSpan CrlValidity = TimeSpan.FromDays(7);

    readonly object lockCrl = new();
    byte[]? cachedCrl;
    DateTimeOffset cachedCrlNextUpdate;

    ReverseProxyServiceImpl ReverseProxyService => reverseProxyConfig.Service;

    /// <summary>
    /// 获取 CER 证书文件路径
    /// </summary>
    public string CaCerFilePath => ((ICertificateManager)ReverseProxyService.CertificateManager).CerFilePath;

    /// <summary>
    /// 获取 PFX 证书文件路径
    /// </summary>
    public string CaPfxFilePath => ((ICertificateManager)ReverseProxyService.CertificateManager).PfxFilePath;

    /// <summary>
    /// 根证书指纹
    /// </summary>
    public string CaThumbprint => GetCaCertificate().Thumbprint;

    /// <summary>
    /// 证书吊销列表地址，写入叶子证书的 CRL Distribution Point 扩展
    /// </summary>
    public string CrlUrl => $"http://127.0.0.1:{IReverseProxyConfig.CrlPort}/crl/{CaThumbprint}.crl";

    /// <summary>
    /// 签发者证书地址，写入叶子证书的 Authority Information Access 扩展
    /// </summary>
    public string CaIssuerUrl => $"http://127.0.0.1:{IReverseProxyConfig.CrlPort}/crl/{CaThumbprint}.cer";

    public CertService(
        ILogger<CertService> logger,
        IReverseProxyConfig reverseProxyConfig)
    {
        this.logger = logger;
        this.reverseProxyConfig = reverseProxyConfig;
    }

    /// <summary>
    /// 设置 SSL 验证
    /// </summary>
    /// <param name="value">是否验证</param>
    /// <returns></returns>
    public static bool GitConfigSslverify(bool value)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"config --global http.sslverify {value.ToLowerString()}",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 获取颁发给指定域名的证书
    /// </summary>
    /// <param name="domain"></param>
    /// <returns></returns>
    public X509Certificate2? GetOrCreateServerCert(string? domain)
    {
        // 证书内嵌了吊销列表地址，地址变化时需要重新签发
        var key = $"{nameof(CertService)}:{domain}:{CrlUrl}";

        while (true)
        {
            var lazy = serverCerts.GetOrAdd(key, _ => CreateLazy(domain));
            var cert = GetValue(key, lazy);
            if (cert.NotAfter > DateTime.Now)
                return cert;

            // 证书已过期(进程连续运行超过一年)时重新签发，替换完成前其余请求继续使用旧证书
            var renewed = CreateLazy(domain);
            if (serverCerts.TryUpdate(key, renewed, lazy))
                return GetValue(key, renewed);
        }
    }

    Lazy<X509Certificate2> CreateLazy(string? domain) => new(
        () => CreateServerCert(domain),
        LazyThreadSafetyMode.ExecutionAndPublication);

    X509Certificate2 GetValue(string key, Lazy<X509Certificate2> lazy)
    {
        try
        {
            return lazy.Value;
        }
        catch
        {
            // Lazy 会缓存异常，签发失败时移除条目，让下一次握手重新尝试
            serverCerts.TryRemove(new KeyValuePair<string, Lazy<X509Certificate2>>(key, lazy));
            throw;
        }
    }

    /// <summary>
    /// 生成域名的 1 年证书
    /// </summary>
    X509Certificate2 CreateServerCert(string? domain)
    {
        var subjectName = new X500DistinguishedName($"CN={domain}");
        using var serverCert = CertGenerator.CreateEndCertificate(GetCaCertificate(), subjectName, GetDomains(),
            crlDistributionPointUrl: CrlUrl, caIssuerUrl: CaIssuerUrl);
        var serverCertPfx = serverCert.Export(X509ContentType.Pfx);
        // 将生成的证书导出后重新创建一个
        return new X509Certificate2(serverCertPfx);
    }

    /// <summary>
    /// 获取根证书
    /// </summary>
    /// <returns></returns>
    X509Certificate2 GetCaCertificate()
    {
        caCert ??= new X509Certificate2(fileName: CaPfxFilePath, password: default(string));
        return caCert;
    }

    internal void ReloadRootCertificate()
    {
        lock (lockCrl)
        {
            Interlocked.Exchange(ref caCert, null);
            serverCerts.Clear();
            cachedCrl = null;
            cachedCrlNextUpdate = default;
        }
    }

    /// <summary>
    /// 获取根证书的 DER 编码公钥证书
    /// </summary>
    /// <returns></returns>
    public byte[]? GetCaCertificateBytes()
    {
        try
        {
            return GetCaCertificate().Export(X509ContentType.Cert);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to export the root certificate.");
            return null;
        }
    }

    /// <summary>
    /// 获取证书吊销列表
    /// </summary>
    /// <returns></returns>
    public byte[]? GetCrl()
    {
        lock (lockCrl)
        {
            var now = DateTimeOffset.UtcNow;
            // 剩余有效期不足一半时重新生成，避免客户端因吊销列表过期而拒绝握手
            if (cachedCrl is not null && now < cachedCrlNextUpdate - (CrlValidity / 2))
            {
                return cachedCrl;
            }
            try
            {
                cachedCrl = CrlGenerator.Generate(GetCaCertificate(),
                    now - TimeSpan.FromMinutes(5), now + CrlValidity, now.ToUnixTimeSeconds());
                cachedCrlNextUpdate = now + CrlValidity;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to generate the certificate revocation list.");
                // 生成失败时继续使用上一次的吊销列表
            }
            return cachedCrl;
        }
    }

    /// <summary>
    /// 获取域名
    /// </summary>
    /// <param name="domain"></param>
    /// <returns></returns>
    static IEnumerable<string> GetDomains()
    {
        yield return Environment.MachineName;
        yield return IPAddress.Loopback.ToString();
        yield return IPAddress.IPv6Loopback.ToString();
    }
}