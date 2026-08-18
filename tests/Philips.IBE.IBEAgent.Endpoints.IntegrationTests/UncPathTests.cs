using Philips.IBE.IBEAgent.Endpoints.File;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

public sealed class UncPathTests
{
    [Theory]
    [InlineData(@"\\server\share\in", true)]
    [InlineData("//server/share/in", true)]   // forward-slash UNC (legacy-shaped config)
    [InlineData(@"C:\ibe\in", false)]
    [InlineData("/var/ibe/in", false)]        // Unix-absolute, not UNC
    [InlineData("relative/in", false)]
    public void IsUnc_recognizes_both_separators(string path, bool expected)
        => Assert.Equal(expected, UncPath.IsUnc(path));

    [Fact]
    public void ToRemoteName_normalizes_forward_slashes_to_backslashes()
        => Assert.Equal(@"\\server\share\in", UncPath.ToRemoteName("//server/share/in"));

    [Fact]
    public void ToRemoteName_leaves_backslash_unc_unchanged()
        => Assert.Equal(@"\\server\share\in", UncPath.ToRemoteName(@"\\server\share\in"));
}
