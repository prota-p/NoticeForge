using NoticeForge.Domain;

namespace NoticeForge.Tests.Domain;

public class NoticePolicyTests
{
    static NoticePolicy With(params string[] keys) => new()
    {
        Packages = keys.ToDictionary(k => k, k => new PackageOverride(null, null, false, k), StringComparer.OrdinalIgnoreCase),
    };

    [Theory]
    // An exact name match first; otherwise the longest matching key ending with *
    [InlineData("SkiaSharp", "SkiaSharp")]
    [InlineData("SkiaSharp.NativeAssets.Win32", "SkiaSharp.NativeAssets.*")]
    [InlineData("SkiaSharp.Views", "SkiaSharp*")]
    [InlineData("skiasharp.nativeassets.linux", "SkiaSharp.NativeAssets.*")]   // case-insensitive
    [InlineData("HarfBuzzSharp", null)]
    public void Exact_name_wins_then_the_longest_prefix(string id, string? expected)
    {
        var policy = With("SkiaSharp", "SkiaSharp*", "SkiaSharp.NativeAssets.*");
        Assert.Equal(expected, policy.FindOverrideKey(id));
        Assert.Equal(expected, policy.FindOverride(id)?.Reason);
    }

    [Fact]
    public void Prefix_matches_the_name_as_written_before_the_star()
    {
        // "Microsoft.IdentityModel.*" does not apply to System.IdentityModel.Tokens.Jwt
        var policy = With("Microsoft.IdentityModel.*");
        Assert.Equal("Microsoft.IdentityModel.*", policy.FindOverrideKey("Microsoft.IdentityModel.Tokens"));
        Assert.Null(policy.FindOverrideKey("System.IdentityModel.Tokens.Jwt"));
        Assert.Null(policy.FindOverrideKey("Microsoft.IdentityModelX"));
    }
}
