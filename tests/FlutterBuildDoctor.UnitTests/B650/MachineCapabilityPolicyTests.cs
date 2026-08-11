using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B650;

public sealed class MachineCapabilityPolicyTests
{
    [Fact]
    public void Evaluate_ClassifiesCapableMachineAndNormalizesIdentity()
    {
        var first = MachineCapabilityPolicy.Evaluate(new MachineCapabilityRequest(8, 16, 100, "AMD64", " Windows   11 "));
        var second = MachineCapabilityPolicy.Evaluate(new MachineCapabilityRequest(8, 16, 100, "x64", "windows 11"));

        Assert.False(first.Constrained);
        Assert.Equal("x64", first.Architecture);
        Assert.Equal("windows 11", first.OperatingSystem);
        Assert.Equal(8, first.RecommendedParallelism);
        Assert.Equal(100, first.CapabilityScore);
        Assert.Equal("machine-capable", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_ConstrainsLowResourceMachines()
    {
        var result = MachineCapabilityPolicy.Evaluate(new MachineCapabilityRequest(2, 4, 5, "aarch64", "Windows 10"));
        Assert.True(result.Constrained);
        Assert.Equal("arm64", result.Architecture);
        Assert.InRange(result.RecommendedParallelism, 1, 2);
        Assert.InRange(result.CapabilityScore, 0, 99);
        Assert.Equal("machine-constrained", result.ReasonCode);
    }

    [Theory]
    [InlineData(0, 8, 20)]
    [InlineData(4, 0, 20)]
    [InlineData(4, 8, -1)]
    public void Evaluate_RejectsInvalidCapacities(int cores, double memory, double disk)
        => Assert.Throws<ArgumentOutOfRangeException>(() => MachineCapabilityPolicy.Evaluate(
            new MachineCapabilityRequest(cores, memory, disk, "x64", "windows")));

    [Theory]
    [InlineData("mips")]
    [InlineData("unknown")]
    public void NormalizeArchitecture_RejectsUnsupportedValues(string value)
        => Assert.Throws<ArgumentException>(() => MachineCapabilityPolicy.NormalizeArchitecture(value));
}
