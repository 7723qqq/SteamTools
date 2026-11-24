namespace BD.WTTS.UnitTest;

public sealed class CertificateRevocationTest
{
    [Test]
    public void ExpiredIssuerProducesExpiredLeafCertificate()
    {
        var now = DateTimeOffset.UtcNow;
        using var ca = CertGenerator.CreateCACertificate(
            new X500DistinguishedName("CN=Expired UnitTest CA"),
            now.AddDays(-2), now.AddDays(-1));
        using var leaf = CertGenerator.CreateEndCertificate(
            ca, new X500DistinguishedName("CN=github.com"), new[] { "github.com" });

        Assert.That(leaf.NotAfter, Is.LessThanOrEqualTo(DateTime.Now));
    }
}
