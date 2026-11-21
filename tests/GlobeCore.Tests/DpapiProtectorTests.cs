using GlobeCore.Wpf.Services;

namespace GlobeCore.Tests;

public class DpapiProtectorTests
{
    [Fact]
    public void RoundTripsForTheCurrentUserAndHidesThePlainText()
    {
        var p = new DpapiProtector("GlobeCore.Tests");
        string blob = p.Protect("consumer-secret-123");
        Assert.DoesNotContain("consumer-secret-123", blob);
        Assert.Equal("consumer-secret-123", p.Unprotect(blob));
    }

    [Theory, InlineData("not base64 !!"), InlineData("AAAA"), InlineData("")]
    public void UndecryptableGivesNull(string blob) => Assert.Null(new DpapiProtector("GlobeCore.Tests").Unprotect(blob));
}
