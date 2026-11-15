namespace BD.WTTS.UnitTest;

/// <summary>
/// 证书吊销信息回归测试
/// </summary>
/// <remarks>
/// 本地根证书签发的中间人证书若没有吊销信息，强制校验吊销状态的客户端(Git、curl 等)
/// 会以 <c>CRYPT_E_NO_REVOCATION_CHECK</c> 拒绝握手，表现为无法访问 GitHub。
/// 这里锁定叶子证书的 CRL Distribution Point 扩展与吊销列表本身的正确性。
/// </remarks>
public sealed class CertificateRevocationTest
{
    const string CrlDistributionPointOid = "2.5.29.31";
    const string AuthorityInformationAccessOid = "1.3.6.1.5.5.7.1.1";

    static X509Certificate2 CreateCaCertificate()
        => CertGenerator.CreateCACertificate(
            new X500DistinguishedName("C=CN, O=BeyondDimension, OU=UnitTest, CN=WattToolkit UnitTest CA"),
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

    [Test]
    public void CreateEndCertificateWithoutCrlUrlHasNoCrlDistributionPoint()
    {
        using var ca = CreateCaCertificate();
        using var leaf = CertGenerator.CreateEndCertificate(
            ca, new X500DistinguishedName("CN=github.com"), new[] { "github.com" });

        Assert.That(leaf.Extensions.Any(x => x.Oid?.Value == CrlDistributionPointOid), Is.False);
        Assert.That(leaf.Extensions.Any(x => x.Oid?.Value == AuthorityInformationAccessOid), Is.False);
    }

    [Test]
    public void CreateEndCertificateEmbedsCrlDistributionPointUrl()
    {
        using var ca = CreateCaCertificate();
        const string crlUrl = "http://127.0.0.1:26502/crl/0123456789ABCDEF.crl";
        using var leaf = CertGenerator.CreateEndCertificate(
            ca, new X500DistinguishedName("CN=github.com"), new[] { "github.com" },
            crlDistributionPointUrl: crlUrl);

        var cdp = leaf.Extensions.FirstOrDefault(x => x.Oid?.Value == CrlDistributionPointOid);
        Assert.That(cdp, Is.Not.Null);
        var raw = cdp!.RawData;
        // 扩展内容为 SEQUENCE { SEQUENCE { [0] { [0] { [6] IA5String } } } }，
        // 其中 [6] 之后即为吊销列表地址
        var urlBytes = Encoding.ASCII.GetBytes(crlUrl);
        Assert.That(Contains(raw, urlBytes), Is.True, "CRL Distribution Point 中未找到吊销列表地址");
    }

    [Test]
    public void CreateEndCertificateEmbedsAuthorityInformationAccessUrl()
    {
        using var ca = CreateCaCertificate();
        const string caIssuerUrl = "http://127.0.0.1:26502/crl/0123456789ABCDEF.cer";
        using var leaf = CertGenerator.CreateEndCertificate(
            ca, new X500DistinguishedName("CN=github.com"), new[] { "github.com" },
            caIssuerUrl: caIssuerUrl);

        var aia = leaf.Extensions.FirstOrDefault(x => x.Oid?.Value == AuthorityInformationAccessOid);
        Assert.That(aia, Is.Not.Null);
        Assert.That(Contains(aia!.RawData, Encoding.ASCII.GetBytes(caIssuerUrl)), Is.True);
    }

    [Test]
    public void CrlGeneratorProducesCrlSignedByRootCertificate()
    {
        using var ca = CreateCaCertificate();
        var thisUpdate = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var nextUpdate = new DateTimeOffset(2026, 1, 9, 3, 4, 5, TimeSpan.Zero);

        var crl = CrlGenerator.Generate(ca, thisUpdate, nextUpdate, 4096);

        Assert.That(crl, Is.Not.Empty);

        // 解析 CertificateList ::= SEQUENCE { tbsCertList, signatureAlgorithm, signatureValue }
        var (tbsCertList, signature) = SplitCertificateList(crl);

        // 签发者必须是根证书的主题名
        Assert.That(Contains(tbsCertList, ca.SubjectName.RawData), Is.True, "吊销列表的签发者与根证书主题名不一致");

        // 签名必须能被根证书公钥验证
        using var rsa = ca.GetRSAPublicKey();
        Assert.That(rsa, Is.Not.Null);
        Assert.That(rsa!.VerifyData(tbsCertList, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            Is.True, "吊销列表签名校验失败");

        // 有效期必须落在吊销列表内
        Assert.That(Contains(tbsCertList, Encoding.ASCII.GetBytes(thisUpdate.UtcDateTime.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture) + "Z")), Is.True);
        Assert.That(Contains(tbsCertList, Encoding.ASCII.GetBytes(nextUpdate.UtcDateTime.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture) + "Z")), Is.True);
    }

    [Test]
    public void CrlGeneratorRejectsCertificateWithoutPrivateKey()
    {
        using var ca = CreateCaCertificate();
        using var publicOnly = new X509Certificate2(ca.Export(X509ContentType.Cert));

        Assert.Throws<CryptographicException>(() =>
            CrlGenerator.Generate(publicOnly, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), 1));
    }

    static bool Contains(byte[] source, byte[] value)
    {
        if (value.Length == 0) return true;
        for (var i = 0; i + value.Length <= source.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < value.Length; j++)
            {
                if (source[i + j] != value[j])
                {
                    matched = false;
                    break;
                }
            }
            if (matched) return true;
        }
        return false;
    }

    /// <summary>
    /// 拆解 CertificateList，取出被签名的 tbsCertList 与签名值
    /// </summary>
    static (byte[] TbsCertList, byte[] Signature) SplitCertificateList(byte[] der)
    {
        var outer = ReadTlv(der, 0);
        Assert.That(outer.Tag, Is.EqualTo(0x30), "CertificateList 顶层应为 SEQUENCE");

        var tbs = ReadTlv(der, outer.ContentOffset);
        var algorithm = ReadTlv(der, outer.ContentOffset + tbs.TotalLength);
        var signature = ReadTlv(der, outer.ContentOffset + tbs.TotalLength + algorithm.TotalLength);

        var tbsBytes = new byte[tbs.TotalLength];
        Array.Copy(der, outer.ContentOffset, tbsBytes, 0, tbs.TotalLength);

        // signatureValue 为 BIT STRING，首字节是未使用位数，其后才是签名值
        Assert.That(signature.Tag, Is.EqualTo(0x03), "signatureValue 应为 BIT STRING");
        var signatureBytes = new byte[signature.ContentLength - 1];
        Array.Copy(der, signature.ContentOffset + 1, signatureBytes, 0, signatureBytes.Length);
        return (tbsBytes, signatureBytes);
    }

    readonly record struct Tlv(byte Tag, int ContentOffset, int ContentLength, int TotalLength);

    static Tlv ReadTlv(byte[] data, int offset)
    {
        var tag = data[offset];
        var first = data[offset + 1];
        int length;
        int headerSize;
        if ((first & 0x80) == 0)
        {
            length = first;
            headerSize = 2;
        }
        else
        {
            var count = first & 0x7F;
            length = 0;
            for (var i = 0; i < count; i++)
            {
                length = (length << 8) | data[offset + 2 + i];
            }
            headerSize = 2 + count;
        }
        return new Tlv(tag, offset + headerSize, length, headerSize + length);
    }
}
