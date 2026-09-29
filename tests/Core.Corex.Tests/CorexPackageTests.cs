using System.IO;
using System.IO.Compression;
using Core.Corex;
using Core.Corex.IO;
using Core.Corex.Validation;
using Xunit;

namespace Core.Corex.Tests;

public class CorexPackageTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Core.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Repo root not found.");
    }

    private static string SchemaDir() => Path.Combine(RepoRoot(), "schemas", "corex");
    private static string Fixture(string relative) => Path.Combine(RepoRoot(), "fixtures", "corex", relative);

    [Theory]
    [InlineData("mini-site/package")]
    [InlineData("mini-site-rev2/package")]
    public void RoundTrip_Directory_PreservesObjects(string relative)
    {
        var original = CorexReader.ReadDirectory(Fixture(relative));
        var temp = Path.Combine(Path.GetTempPath(), "corex-rt-" + Path.GetRandomFileName());
        Directory.CreateDirectory(temp);
        try
        {
            CorexWriter.WriteDirectory(original, temp);
            var again = CorexReader.ReadDirectory(temp);
            Assert.True(PackageEquality.DeepEquals(original, again));
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    [Theory]
    [InlineData("mini-site/package")]
    [InlineData("mini-site-rev2/package")]
    public void Zip_And_Directory_ProduceIdenticalPackages(string relative)
    {
        var dirPkg = CorexReader.ReadDirectory(Fixture(relative));
        var zipPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".corex");
        try
        {
            CorexWriter.WriteZip(dirPkg, zipPath);
            var zipPkg = CorexReader.ReadZip(zipPath);
            Assert.True(PackageEquality.DeepEquals(dirPkg, zipPkg));
        }
        finally
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
    }

    [Theory]
    [InlineData("mini-site/package")]
    [InlineData("mini-site-rev2/package")]
    public void ValidFixtures_PassSchemaValidation(string relative)
    {
        var validator = new CorexSchemaValidator(SchemaDir());
        var errors = validator.ValidatePackage(Fixture(relative));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("invalid/missing-typed-payload")]
    [InlineData("invalid/unknown-field")]
    [InlineData("invalid/bad-unit")]
    [InlineData("invalid/paper-without-owner")]
    [InlineData("invalid/non-hex-handle")]
    public void InvalidPackages_AreRejected(string relative)
    {
        var validator = new CorexSchemaValidator(SchemaDir());
        var errors = validator.ValidatePackage(Fixture(relative));
        Assert.NotEmpty(errors);
        Assert.All(errors, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.File));
            Assert.False(string.IsNullOrWhiteSpace(e.JsonPath));
        });
    }

    [Fact]
    public void Scaffold_SmokeTest()
    {
        Assert.True(Directory.Exists(SchemaDir()));
    }
}
