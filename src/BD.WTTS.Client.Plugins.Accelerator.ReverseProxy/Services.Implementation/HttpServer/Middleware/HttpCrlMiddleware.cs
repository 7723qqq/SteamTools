// ReSharper disable once CheckNamespace
namespace BD.WTTS.Services.Implementation;

/// <summary>
/// 证书吊销列表分发中间件
/// </summary>
/// <para>本地根证书签发的中间人证书没有吊销信息，强制校验吊销状态的客户端(Git、curl、Microsoft Store 等)
/// 会以 <c>CRYPT_E_NO_REVOCATION_CHECK</c> 拒绝握手。叶子证书的 CRL Distribution Point 扩展指向此处，
/// 客户端在此获取吊销列表后即可完成吊销校验。</para>
sealed class HttpCrlMiddleware
{
    const string CrlPathPrefix = "/crl/";

    readonly CertService certService;
    readonly ILogger<HttpCrlMiddleware> logger;

    public HttpCrlMiddleware(CertService certService, ILogger<HttpCrlMiddleware> logger)
    {
        this.certService = certService;
        this.logger = logger;
    }

    /// <summary>
    /// 处理请求
    /// </summary>
    /// <param name="context"></param>
    /// <param name="next"></param>
    /// <returns></returns>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path.Value;
        if (path is null || !path.StartsWith(CrlPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        try
        {
            var name = path[CrlPathPrefix.Length..];
            var thumbprint = certService.CaThumbprint;

            if (name.Equals($"{thumbprint}.crl", StringComparison.OrdinalIgnoreCase))
            {
                var crl = certService.GetCrl();
                if (crl is null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                await WriteAsync(context, crl, "application/pkix-crl");
                return;
            }

            if (name.Equals($"{thumbprint}.cer", StringComparison.OrdinalIgnoreCase))
            {
                var caCertificate = certService.GetCaCertificateBytes();
                if (caCertificate is null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }
                await WriteAsync(context, caCertificate, "application/pkix-cert");
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to serve the certificate revocation list.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    static async Task WriteAsync(HttpContext context, byte[] content, string contentType)
    {
        context.Response.ContentType = contentType;
        context.Response.ContentLength = content.Length;
        await context.Response.Body.WriteAsync(content, context.RequestAborted);
    }
}
