using Beep.Installer.Models;

namespace Beep.Installer.Engine.Updates;

public sealed class UpdateChannelTransitionRequest
{
    public UpdateChannelFeedManifest? Feed { get; init; }
    public string CurrentChannelId { get; init; } = "";
    public string TargetChannelId { get; init; } = "";
    public string InstalledVersion { get; init; } = "";
    public string CohortSeed { get; init; } = "";
    public DateTimeOffset? NowUtc { get; init; }
}

public sealed class UpdateChannelTransitionDecision
{
    public bool Allowed { get; init; }
    public string Action { get; init; } = "";
    public string CurrentChannelId { get; init; } = "";
    public string TargetChannelId { get; init; } = "";
    public string ChannelRing { get; init; } = "";
    public string FeedUrl { get; init; } = "";
    public string InstalledVersion { get; init; } = "";
    public string MinimumVersion { get; init; } = "";
    public string RollbackVersion { get; init; } = "";
    public DateTimeOffset? DeadlineUtc { get; init; }
    public bool DeadlineReached { get; init; }
    public bool Critical { get; init; }
    public string MaintenanceWindow { get; init; } = "";
    public bool MaintenanceWindowMatched { get; init; }
    public UpdateRolloutDecision? Rollout { get; init; }
    public List<string> Reasons { get; init; } = new();
}

public static class UpdateChannelTransitionEvaluator
{
    public const string ActionUpdate = "update";
    public const string ActionTransition = "transition";
    public const string ActionRollback = "rollback";
    public const string ActionHold = "hold";
    public const string ActionBlocked = "blocked";

    public static UpdateChannelTransitionDecision Evaluate(UpdateChannelTransitionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Feed is null)
        {
            return Blocked(request, "", "", "", "", critical: false, null, "Update channel feed is required.");
        }

        var targetChannelId = string.IsNullOrWhiteSpace(request.TargetChannelId)
            ? request.Feed.SelectedChannelId
            : request.TargetChannelId;
        var channel = request.Feed.Channels.FirstOrDefault(c =>
            string.Equals(c.Id, targetChannelId, StringComparison.OrdinalIgnoreCase));
        if (channel is null)
            return Blocked(request, targetChannelId, "", "", "", critical: false, null, $"Target update channel '{targetChannelId}' is not declared in the feed.");

        var rollout = UpdateRolloutEvaluator.Evaluate(ProjectFromFeed(request.Feed), ChannelFromFeed(channel), request.CohortSeed);
        var reasons = new List<string>();

        if (!IsValidMaintenanceWindow(channel.MaintenanceWindow))
            return Decision(false, ActionBlocked, request, channel, rollout,
                new List<string> { "Invalid maintenance window. Use 'Sun 02:00-04:00 UTC' with distinct 24-hour start and end times." });

        if ((!string.IsNullOrWhiteSpace(channel.MinimumVersion) && !TryParseChannelVersion(channel.MinimumVersion, out _))
            || (!string.IsNullOrWhiteSpace(channel.RollbackVersion) && !TryParseChannelVersion(channel.RollbackVersion, out _)))
            return Decision(false, ActionBlocked, request, channel, rollout,
                new List<string> { "Channel minimum or rollback version is invalid." });

        if (channel.Revoked)
        {
            reasons.Add($"Target update channel '{channel.Id}' is revoked.");
            if (!string.IsNullOrWhiteSpace(channel.RollbackVersion))
                reasons.Add($"Rollback target is {channel.RollbackVersion}.");
            return Decision(
                allowed: false,
                action: string.IsNullOrWhiteSpace(channel.RollbackVersion) ? ActionBlocked : ActionRollback,
                request,
                channel,
                rollout,
                reasons);
        }

        if (!IsMinimumVersionSatisfied(request.InstalledVersion, channel.MinimumVersion))
        {
            reasons.Add(!TryParseChannelVersion(request.InstalledVersion, out _)
                ? "Installed version is required and must contain one to four non-negative numeric components when a channel minimum is configured."
                : $"Installed version {request.InstalledVersion} is below channel minimum version {channel.MinimumVersion}.");
            return Decision(false, ActionBlocked, request, channel, rollout, reasons);
        }

        if (!rollout.Included)
        {
            reasons.Add(rollout.Reason);
            return Decision(false, ActionHold, request, channel, rollout, reasons);
        }

        reasons.Add(rollout.Reason);
        var nowUtc = (request.NowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var deadlineReached = channel.DeadlineUtc.HasValue && nowUtc >= channel.DeadlineUtc.Value.ToUniversalTime();
        var maintenanceWindowMatched = IsWithinMaintenanceWindow(channel.MaintenanceWindow, nowUtc);
        if (!string.IsNullOrWhiteSpace(channel.MaintenanceWindow))
        {
            if (maintenanceWindowMatched)
            {
                reasons.Add($"Current time is inside maintenance window '{channel.MaintenanceWindow}'.");
            }
            else if (channel.Critical)
            {
                reasons.Add($"Critical update bypasses maintenance window '{channel.MaintenanceWindow}'.");
            }
            else if (deadlineReached && channel.DeadlineUtc is { } deadline)
            {
                reasons.Add($"Update deadline {deadline:O} has been reached; maintenance window '{channel.MaintenanceWindow}' no longer holds the transition.");
            }
            else
            {
                reasons.Add($"Current time is outside maintenance window '{channel.MaintenanceWindow}'.");
                return Decision(false, ActionHold, request, channel, rollout, reasons, deadlineReached, maintenanceWindowMatched);
            }
        }

        reasons.Add(string.Equals(request.CurrentChannelId, channel.Id, StringComparison.OrdinalIgnoreCase)
            ? $"Device remains on update channel '{channel.Id}'."
            : $"Device can transition from '{request.CurrentChannelId}' to '{channel.Id}'.");

        return Decision(
            allowed: true,
            action: string.Equals(request.CurrentChannelId, channel.Id, StringComparison.OrdinalIgnoreCase)
                ? ActionUpdate
                : ActionTransition,
            request,
            channel,
            rollout,
            reasons,
            deadlineReached,
            maintenanceWindowMatched);
    }

    private static UpdateChannelTransitionDecision Blocked(
        UpdateChannelTransitionRequest request,
        string targetChannelId,
        string ring,
        string feedUrl,
        string minimumVersion,
        bool critical,
        UpdateRolloutDecision? rollout,
        string reason)
        => new()
        {
            Allowed = false,
            Action = ActionBlocked,
            CurrentChannelId = request.CurrentChannelId,
            TargetChannelId = targetChannelId,
            ChannelRing = ring,
            FeedUrl = feedUrl,
            InstalledVersion = request.InstalledVersion,
            MinimumVersion = minimumVersion,
            Critical = critical,
            Rollout = rollout,
            Reasons = { reason }
        };

    private static UpdateChannelTransitionDecision Decision(
        bool allowed,
        string action,
        UpdateChannelTransitionRequest request,
        UpdateChannelFeedEntry channel,
        UpdateRolloutDecision rollout,
        List<string> reasons)
        => new()
        {
            Allowed = allowed,
            Action = action,
            CurrentChannelId = request.CurrentChannelId,
            TargetChannelId = channel.Id,
            ChannelRing = channel.Ring,
            FeedUrl = channel.FeedUrl,
            InstalledVersion = request.InstalledVersion,
            MinimumVersion = channel.MinimumVersion,
            RollbackVersion = channel.RollbackVersion,
            DeadlineUtc = channel.DeadlineUtc,
            DeadlineReached = false,
            Critical = channel.Critical,
            MaintenanceWindow = channel.MaintenanceWindow,
            MaintenanceWindowMatched = false,
            Rollout = rollout,
            Reasons = reasons
        };

    private static UpdateChannelTransitionDecision Decision(
        bool allowed,
        string action,
        UpdateChannelTransitionRequest request,
        UpdateChannelFeedEntry channel,
        UpdateRolloutDecision rollout,
        List<string> reasons,
        bool deadlineReached,
        bool maintenanceWindowMatched)
        => new()
        {
            Allowed = allowed,
            Action = action,
            CurrentChannelId = request.CurrentChannelId,
            TargetChannelId = channel.Id,
            ChannelRing = channel.Ring,
            FeedUrl = channel.FeedUrl,
            InstalledVersion = request.InstalledVersion,
            MinimumVersion = channel.MinimumVersion,
            RollbackVersion = channel.RollbackVersion,
            DeadlineUtc = channel.DeadlineUtc,
            DeadlineReached = deadlineReached,
            Critical = channel.Critical,
            MaintenanceWindow = channel.MaintenanceWindow,
            MaintenanceWindowMatched = maintenanceWindowMatched,
            Rollout = rollout,
            Reasons = reasons
        };

    private static InstallProject ProjectFromFeed(UpdateChannelFeedManifest feed)
        => new()
        {
            AppName = feed.AppName,
            AppPublisher = feed.AppPublisher,
            AppVersion = feed.AppVersion,
            AppUpdateChannel = feed.SelectedChannelId
        };

    private static UpdateChannelDefinition ChannelFromFeed(UpdateChannelFeedEntry channel)
        => new()
        {
            Id = channel.Id,
            Name = channel.Name,
            Ring = channel.Ring,
            FeedUrl = channel.FeedUrl,
            RolloutPercentage = channel.RolloutPercentage,
            MinimumVersion = channel.MinimumVersion,
            DeadlineUtc = channel.DeadlineUtc,
            Critical = channel.Critical,
            MaintenanceWindow = channel.MaintenanceWindow,
            RollbackVersion = channel.RollbackVersion,
            Revoked = channel.Revoked
        };

    private static bool IsMinimumVersionSatisfied(string installedVersion, string minimumVersion)
    {
        if (string.IsNullOrWhiteSpace(minimumVersion))
            return true;
        if (!TryParseChannelVersion(installedVersion, out var installed))
            return false;
        if (!TryParseChannelVersion(minimumVersion, out var minimum))
            return false;
        return installed.CompareTo(minimum) >= 0;
    }

    public static bool TryParseChannelVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('.');
        if (parts.Length is < 1 or > 4) return false;
        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || parts[i].Any(c => c is < '0' or > '9')
                || !int.TryParse(parts[i], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }
        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    public static bool IsValidMaintenanceWindow(string? maintenanceWindow)
        => string.IsNullOrWhiteSpace(maintenanceWindow)
           || TryParseMaintenanceWindow(maintenanceWindow, out _, out _, out _);

    private static bool IsWithinMaintenanceWindow(string maintenanceWindow, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(maintenanceWindow))
            return true;

        if (!TryParseMaintenanceWindow(maintenanceWindow, out var day, out var start, out var end))
            return false;
        var currentDay = nowUtc.DayOfWeek;
        var currentTime = TimeOnly.FromDateTime(nowUtc.UtcDateTime);
        if (start < end)
            return currentDay == day && currentTime >= start && currentTime < end;
        return (currentDay == day && currentTime >= start)
               || (currentDay == NextDay(day) && currentTime < end);
    }

    private static bool TryParseMaintenanceWindow(string maintenanceWindow, out DayOfWeek day,
        out TimeOnly start, out TimeOnly end)
    {
        day = default;
        start = default;
        end = default;
        var normalized = maintenanceWindow.Trim();
        const string utcSuffix = " UTC";
        if (!normalized.EndsWith(utcSuffix, StringComparison.OrdinalIgnoreCase))
            return false;

        normalized = normalized[..^utcSuffix.Length].Trim();
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        if (!TryParseDay(parts[0], out day))
            return false;

        var range = parts[1].Split('-', StringSplitOptions.TrimEntries);
        if (range.Length != 2
            || !TimeOnly.TryParseExact(range[0], "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out start)
            || !TimeOnly.TryParseExact(range[1], "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out end))
            return false;
        return start != end;
    }

    private static bool TryParseDay(string value, out DayOfWeek day)
    {
        foreach (var candidate in Enum.GetValues<DayOfWeek>())
        {
            if (string.Equals(value, candidate.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, candidate.ToString()[..3], StringComparison.OrdinalIgnoreCase))
            {
                day = candidate;
                return true;
            }
        }

        day = default;
        return false;
    }

    private static DayOfWeek NextDay(DayOfWeek day)
        => day == DayOfWeek.Saturday ? DayOfWeek.Sunday : day + 1;
}
