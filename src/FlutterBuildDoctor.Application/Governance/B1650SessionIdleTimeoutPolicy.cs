using System;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record SessionIdleTimeoutDecision(
    string SessionIdentity,
    DateTimeOffset LastActivityUtc,
    TimeSpan IdleDuration,
    TimeSpan IdleTimeout,
    bool Active,
    bool ExpirationRequired,
    string ReasonCode,
    string Fingerprint);

public static class SessionIdleTimeoutPolicy
{
    public static readonly TimeSpan MinTimeout = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromHours(24);
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public static SessionIdleTimeoutDecision Evaluate(string sessionIdentity, DateTimeOffset lastActivity, DateTimeOffset now, TimeSpan idleTimeout)
    {
        var identity = B1550PolicyHelpers.Identity(sessionIdentity, nameof(sessionIdentity));
        var activity = B1550PolicyHelpers.Utc(lastActivity);
        now = B1550PolicyHelpers.Utc(now);
        if (activity > now + FutureTolerance) throw new ArgumentException("Last activity is beyond the supported future tolerance.", nameof(lastActivity));
        var timeout = idleTimeout < MinTimeout ? MinTimeout : idleTimeout > MaxTimeout ? MaxTimeout : idleTimeout;
        var idle = activity > now ? TimeSpan.Zero : now - activity;
        var expired = idle >= timeout;
        var active = !expired;
        var reason = active ? "session-idle-active" : "session-idle-expired";
        var payload = $"{identity}|{activity:O}|{now:O}|{idle.Ticks}|{timeout.Ticks}|{active}";
        return new SessionIdleTimeoutDecision(identity, activity, idle, timeout, active, expired, reason, B1550PolicyHelpers.Fingerprint(payload));
    }
}
