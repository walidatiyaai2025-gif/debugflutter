using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.UnitTests.B550;

public sealed class ProcessTerminationPolicyTests
{
    [Fact]
    public void Evaluate_PrefersGracefulThenForceForOwnedProcess()
    {
        var request = new ProcessTerminationRequest(42, " flutter ", true, false, true, false, TimeSpan.Zero, " user requested ");
        var first = ProcessTerminationPolicy.Evaluate(request);
        var second = ProcessTerminationPolicy.Evaluate(request);

        Assert.True(first.Allowed);
        Assert.True(first.ForceAllowed);
        Assert.Equal(new[] { "graceful-stop", "force-kill-if-running" }, first.Steps);
        Assert.Equal(ProcessTerminationPolicy.MinGracefulTimeout, first.GracefulTimeout);
        Assert.Equal("user requested", first.CancellationReason);
        Assert.Equal("graceful-then-force", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RequiresConfirmationForExternalForceAndProtectsProcesses()
    {
        var external = ProcessTerminationPolicy.Evaluate(new ProcessTerminationRequest(10, "adb", false, false, true, false, TimeSpan.FromSeconds(10)));
        Assert.False(external.Allowed);
        Assert.False(external.ForceAllowed);
        Assert.Equal("external-force-confirmation-required", external.ReasonCode);

        var confirmed = ProcessTerminationPolicy.Evaluate(new ProcessTerminationRequest(10, "adb", false, false, true, true, TimeSpan.FromSeconds(10)));
        Assert.True(confirmed.Allowed);
        Assert.True(confirmed.ForceAllowed);

        var protectedProcess = ProcessTerminationPolicy.Evaluate(new ProcessTerminationRequest(11, "system", true, true, true, true, TimeSpan.FromSeconds(10)));
        Assert.False(protectedProcess.Allowed);
        Assert.Equal("protected-force-denied", protectedProcess.ReasonCode);
    }

    [Fact]
    public void Evaluate_ExternalGracefulStopDoesNotRequireForceConfirmation()
    {
        var decision = ProcessTerminationPolicy.Evaluate(new ProcessTerminationRequest(99, "gradle", false, false, false, false, TimeSpan.FromMinutes(10)));
        Assert.True(decision.Allowed);
        Assert.False(decision.ForceAllowed);
        Assert.Equal("external-graceful-stop", decision.ReasonCode);
        Assert.Equal(ProcessTerminationPolicy.MaxGracefulTimeout, decision.GracefulTimeout);
    }

    [Fact]
    public void Evaluate_RejectsInvalidProcessIdentity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProcessTerminationPolicy.Evaluate(new ProcessTerminationRequest(0, "flutter", true, false, false, false, TimeSpan.FromSeconds(1))));
        Assert.Throws<ArgumentException>(() => ProcessTerminationPolicy.Evaluate(new ProcessTerminationRequest(1, "flutter\n", true, false, false, false, TimeSpan.FromSeconds(1))));
    }
}
