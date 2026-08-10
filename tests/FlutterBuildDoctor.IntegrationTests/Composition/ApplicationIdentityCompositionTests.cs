using System.Reflection;
using System.Reflection.Emit;
using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Composition;

public sealed class ApplicationIdentityCompositionTests
{
    [Fact]
    public void PresentationCompositionRegistersApplicationIdentityService()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorPresentation();

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IApplicationIdentityService>();

        Assert.NotNull(service.Current);
        Assert.False(string.IsNullOrWhiteSpace(service.Current.ProductVersion));
        Assert.False(string.IsNullOrWhiteSpace(service.Current.BuildNumber));
        Assert.StartsWith("v", service.Current.DisplayText);
    }

    [Fact]
    public void IdentityUsesSafeVersionCommitAndNumericCiBuildNumber()
    {
        var assembly = CreateAssembly(
            informationalVersion: "2.3.4+0123456789abcdef0123456789abcdef01234567",
            fileVersion: "2.3.4.5");
        var service = new ApplicationIdentityService(
            assembly,
            key => key == "GITHUB_RUN_NUMBER" ? "77" : null);

        Assert.Equal("2.3.4", service.Current.ProductVersion);
        Assert.Equal("77", service.Current.BuildNumber);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", service.Current.CommitSha);
        Assert.Contains("0123456789ab", service.Current.DisplayText);
    }

    [Fact]
    public void IdentityRejectsUnsafeEnvironmentValues()
    {
        var assembly = CreateAssembly(
            informationalVersion: "5.0.0+not-a-commit",
            fileVersion: "5.0.0.9");
        var service = new ApplicationIdentityService(
            assembly,
            key => key switch
            {
                "GITHUB_SHA" => "token=super-secret-value",
                "BUILD_SOURCEVERSION" => "../../unsafe",
                "GITHUB_RUN_NUMBER" => "run-123-secret",
                "BUILD_BUILDID" => "build-secret",
                _ => null
            });

        Assert.Equal("5.0.0", service.Current.ProductVersion);
        Assert.Equal("5.0.0.9", service.Current.BuildNumber);
        Assert.Null(service.Current.CommitSha);
        Assert.False(service.Current.DisplayText.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.False(service.Current.DisplayText.Contains("unsafe", StringComparison.OrdinalIgnoreCase));
    }

    private static Assembly CreateAssembly(string informationalVersion, string fileVersion)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"ApplicationIdentityFixture_{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.Run);

        assembly.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!,
            [informationalVersion]));
        assembly.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(AssemblyFileVersionAttribute).GetConstructor([typeof(string)])!,
            [fileVersion]));

        return assembly;
    }
}
