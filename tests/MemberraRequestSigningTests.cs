using Xunit;

namespace Memberra.Jellyfin.Tests;

public sealed class MemberraRequestSigningTests
{
    [Fact]
    public void SignatureBindsMethodPathTimeNonceAndBody()
    {
        var signature = MemberraRequestSigning.CreateSignature(
            "test-install-token",
            "POST",
            "/api/public/jellyfin-plugin/events",
            "1760000000",
            "7F3z_a0B9cD1eF2gH3iJ4kLm",
            "{\"EventId\":\"11111111-2222-3333-4444-555555555555\"}");

        Assert.Equal("dvXaGrRGBhzYKa4RgLKiSFLsI64BZJ1Ip8PSm61Er0k", signature);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", signature);
        Assert.NotEqual(signature, MemberraRequestSigning.CreateSignature(
            "test-install-token",
            "POST",
            "/api/public/jellyfin-plugin/events",
            "1760000000",
            "7F3z_a0B9cD1eF2gH3iJ4kLm",
            "{}"));
    }
}
