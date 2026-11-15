// ReSharper disable once CheckNamespace
namespace BD.WTTS.Services.Implementation;

/// <summary>
/// 证书吊销列表生成器
/// </summary>
/// <remarks>
/// 本地根证书签发的中间人证书没有吊销信息，强制校验吊销状态的客户端(Git、curl、Microsoft Store 等)
/// 会以 <c>CRYPT_E_NO_REVOCATION_CHECK</c> 拒绝握手。此处生成由根证书签名的证书吊销列表，
/// 并由叶子证书的 CRL Distribution Point 扩展告知客户端吊销列表的获取地址。
/// </remarks>
static class CrlGenerator
{
    /// <summary>
    /// sha256WithRSAEncryption 算法标识
    /// </summary>
    static readonly byte[] SignatureAlgorithm = DerEncoder.Sequence(
        DerEncoder.ObjectIdentifier(new byte[] { 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x0B }),
        new byte[] { 0x05, 0x00 });

    /// <summary>
    /// crlNumber 扩展标识 (2.5.29.20)
    /// </summary>
    static readonly byte[] CrlNumberOid = DerEncoder.ObjectIdentifier(new byte[] { 0x55, 0x1D, 0x14 });

    /// <summary>
    /// 生成由根证书签名的 DER 编码证书吊销列表
    /// </summary>
    /// <param name="caCertificate">签发者(根证书)，需要包含私钥</param>
    /// <param name="thisUpdate">本次更新起始时间</param>
    /// <param name="nextUpdate">下次更新时间</param>
    /// <param name="crlNumber">吊销列表编号</param>
    /// <returns>DER 编码的证书吊销列表</returns>
    public static byte[] Generate(X509Certificate2 caCertificate, DateTimeOffset thisUpdate, DateTimeOffset nextUpdate, long crlNumber)
    {
        using var rsa = caCertificate.GetRSAPrivateKey()
            ?? throw new CryptographicException("The root certificate does not contain a usable RSA private key.");

        // crlExtensions [0] EXPLICIT Extensions ::= SEQUENCE SIZE (1..MAX) OF Extension
        var crlNumberExtension = DerEncoder.Sequence(
            CrlNumberOid,
            DerEncoder.OctetString(DerEncoder.Integer(crlNumber)));
        var crlExtensions = DerEncoder.Encode(DerEncoder.ContextSpecificConstructedTag(0), DerEncoder.Sequence(crlNumberExtension));

        var tbsCertList = DerEncoder.Sequence(
            DerEncoder.Integer(1), // version v2
            SignatureAlgorithm,
            caCertificate.SubjectName.RawData, // issuer
            DerEncoder.UtcTime(thisUpdate),
            DerEncoder.UtcTime(nextUpdate),
            crlExtensions);

        var signature = rsa.SignData(tbsCertList, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return DerEncoder.Sequence(tbsCertList, SignatureAlgorithm, DerEncoder.BitString(signature));
    }
}
