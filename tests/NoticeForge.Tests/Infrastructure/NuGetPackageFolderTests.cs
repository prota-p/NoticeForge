using NoticeForge.Infrastructure;

namespace NoticeForge.Tests.Infrastructure;

public class NuGetPackageFolderTests
{
    static string Nuspec(string license, string extra = "") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>Sample</id>
            <version>1.0.0</version>
            {license}
            <copyright>Copyright (c) Sample Authors</copyright>
            <projectUrl>https://example.com/</projectUrl>
            <repository type="git" url="git://github.com/owner/sample.git" commit="0123456789abcdef0123456789abcdef01234567" />
            {extra}
          </metadata>
        </package>
        """;

    [Fact]
    public void Parses_license_expression_and_repository()
    {
        var meta = NuGetPackageFolder.ParseNuspec(Nuspec("""<license type="expression">MIT</license>"""));
        Assert.Equal("MIT", meta.LicenseExpression);
        Assert.Null(meta.LicenseFile);
        Assert.Equal("Copyright (c) Sample Authors", meta.Copyright);
        Assert.Equal("git://github.com/owner/sample.git", meta.RepositoryUrl);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", meta.RepositoryCommit);
        Assert.Equal("https://github.com/owner/sample", meta.SourceUrl);
    }

    [Fact]
    public void Parses_license_file()
    {
        var meta = NuGetPackageFolder.ParseNuspec(Nuspec("""<license type="file">docs\LICENSE.txt</license>"""));
        Assert.Null(meta.LicenseExpression);
        Assert.Equal(@"docs\LICENSE.txt", meta.LicenseFile);
    }

    [Fact]
    public void Finds_license_and_notice_files_in_the_extracted_package()
    {
        using var root = new TempDir();
        // The global packages folder stores the id and version in lowercase
        root.Write("sample/1.0.0-beta/sample.nuspec", Nuspec("""<license type="file">docs\LICENSE.txt</license>"""));
        root.Write("sample/1.0.0-beta/docs/LICENSE.txt", "file license");
        root.Write("sample/1.0.0-beta/LICENSE/LICENSE", "license in a folder");   // the layout used by DynamicData and ReactiveUI
        root.Write("sample/1.0.0-beta/THIRD-PARTY-NOTICES.TXT", "notices");
        root.Write("sample/1.0.0-beta/README.md", "readme");

        var pkg = new NuGetPackageFolder(root.Path).Find("Sample", "1.0.0-Beta");

        Assert.NotNull(pkg);
        Assert.Equal(["docs/LICENSE.txt", "LICENSE/LICENSE"], pkg.Licenses.Select(t => t.Name));
        Assert.All(pkg.Licenses, t => Assert.Equal("nupkg", t.Origin));
        Assert.Equal(["THIRD-PARTY-NOTICES.TXT"], pkg.Notices.Select(t => t.Name));
    }

    [Theory]
    [InlineData(@"..\..\secret.txt")]
    [InlineData("../../secret.txt")]
    public void Ignores_a_license_file_path_outside_the_package(string path)
    {
        // The nuspec is not trusted: a path escaping the package folder must not pull other local files into the notices
        using var root = new TempDir();
        root.Write("secret.txt", "not a license");
        root.Write("sample/1.0.0/sample.nuspec", Nuspec($"""<license type="file">{path}</license>"""));

        var pkg = new NuGetPackageFolder(root.Path).Find("Sample", "1.0.0");

        Assert.NotNull(pkg);
        Assert.Empty(pkg.Licenses);
    }

    [Fact]
    public void Returns_null_when_the_package_is_not_restored()
    {
        using var root = new TempDir();
        Assert.Null(new NuGetPackageFolder(root.Path).Find("Missing", "1.0.0"));
    }
}
