using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B850;

public sealed class SecretReferencePolicyTests
{
    [Fact]
    public void Resolve_NormalizesDeduplicatesAndRedactsReferences()
    {
        var input = new[]
        {
            new SecretReference(" Vault ", " Signing-Key ", "secret://vault/signing-key"),
            new SecretReference("vault", "signing-key", "SECRET://VAULT/SIGNING-KEY"),
            new SecretReference("env", "android-token", "secret://env/android-token")
        };

        var first = SecretReferencePolicy.Resolve(input);
        var second = SecretReferencePolicy.Resolve(input.OrderByDescending(item => item.Provider));

        Assert.Equal(2, first.References.Count);
        Assert.Contains("${vault:signing-key}", first.SafeDisplays);
        Assert.DoesNotContain(first.SafeDisplays, value => value.Contains("secret://", StringComparison.Ordinal));
        Assert.Equal("secret-references-safe", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Theory]
    [InlineData("password=abc")]
    [InlineData("plain-secret")]
    [InlineData("secret://vault/bad value")]
    public void NormalizeReference_RejectsInlineOrUnsafeValues(string value)
        => Assert.Throws<ArgumentException>(() => SecretReferencePolicy.NormalizeReference(value));

    [Fact]
    public void NormalizeReference_RejectsControlCharacters()
        => Assert.Throws<ArgumentException>(() => SecretReferencePolicy.NormalizeReference("secret://vault/key\n"));
}
