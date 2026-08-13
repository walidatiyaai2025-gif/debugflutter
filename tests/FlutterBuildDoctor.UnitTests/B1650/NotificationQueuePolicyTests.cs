using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B1650;

public sealed class NotificationQueuePolicyTests
{
    [Fact]
    public void Evaluate_SelectsMandatoryItemFirst()
    {
        var now = DateTimeOffset.Parse("2026-08-13T12:00:00Z");
        var result = NotificationQueueSelectionPolicy.Evaluate(new[]
        {
            new NotificationQueueItem("normal", 90, now, null, false),
            new NotificationQueueItem("mandatory", 10, now, null, true)
        }, now, 2);

        Assert.Equal(new[] { "mandatory", "normal" }, result.Selected.Select(item => item.Identity));
        Assert.Empty(result.ExpiredIds);
        Assert.Equal(64, result.Fingerprint.Length);
    }
}
