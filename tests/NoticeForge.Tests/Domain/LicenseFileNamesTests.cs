using NoticeForge.Domain;

namespace NoticeForge.Tests.Domain;

public class LicenseFileNamesTests
{
    [Theory]
    [InlineData("LICENSE")]
    [InlineData("LICENSE.txt")]
    [InlineData("License.md")]
    [InlineData("licence.md")]       // Avalonia
    [InlineData("LICENSE-MIT")]
    [InlineData("COPYING")]          // Tmds.DBus
    public void License_names(string name) => Assert.True(LicenseFileNames.IsLicense(name));

    [Theory]
    [InlineData("README.md")]
    [InlineData("licenses.json")]
    [InlineData("NOTICE")]
    [InlineData("MyLicenseHelper.cs")]
    public void Not_license_names(string name) => Assert.False(LicenseFileNames.IsLicense(name));

    [Theory]
    [InlineData("NOTICE")]
    [InlineData("NOTICE.md")]
    [InlineData("THIRD-PARTY-NOTICES.TXT")]
    [InlineData("ThirdPartyNotices.txt")]
    public void Notice_names(string name) => Assert.True(LicenseFileNames.IsNotice(name));

    [Theory]
    [InlineData("LICENSE")]
    [InlineData("NOTICES-OLD")]
    public void Not_notice_names(string name) => Assert.False(LicenseFileNames.IsNotice(name));
}
