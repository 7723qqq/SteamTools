namespace BD.WTTS.Services.Implementation;

static class CertificateRenewalPolicy
{
    public static bool ShouldRenew(DateTimeOffset now, DateTimeOffset notAfter)
        => notAfter <= now.AddDays(30);
}
