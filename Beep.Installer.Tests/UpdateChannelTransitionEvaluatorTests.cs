using Beep.Installer.Engine.Updates;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public class UpdateChannelTransitionEvaluatorTests
{
    [Fact]
    public void Evaluate_Allows_Channel_Transition_When_Minimum_Version_And_Rollout_Pass()
    {
        var decision = UpdateChannelTransitionEvaluator.Evaluate(new UpdateChannelTransitionRequest
        {
            Feed = Feed(),
            CurrentChannelId = "stable",
            TargetChannelId = "beta",
            InstalledVersion = "2.0.0",
            CohortSeed = "device-001"
        });

        decision.Allowed.Should().BeTrue();
        decision.Action.Should().Be(UpdateChannelTransitionEvaluator.ActionTransition);
        decision.TargetChannelId.Should().Be("beta");
        decision.ChannelRing.Should().Be("early");
        decision.Rollout!.Included.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_Holds_Device_When_Staged_Rollout_Excludes_Cohort()
    {
        var decision = UpdateChannelTransitionEvaluator.Evaluate(new UpdateChannelTransitionRequest
        {
            Feed = Feed(betaRolloutPercentage: 0),
            CurrentChannelId = "stable",
            TargetChannelId = "beta",
            InstalledVersion = "2.0.0",
            CohortSeed = "device-001"
        });

        decision.Allowed.Should().BeFalse();
        decision.Action.Should().Be(UpdateChannelTransitionEvaluator.ActionHold);
        decision.Reasons.Should().Contain("Rollout percentage is 0.");
    }

    [Fact]
    public void Evaluate_Returns_Rollback_When_Target_Channel_Is_Revoked_With_Rollback_Target()
    {
        var decision = UpdateChannelTransitionEvaluator.Evaluate(new UpdateChannelTransitionRequest
        {
            Feed = Feed(betaRevoked: true, betaRollbackVersion: "1.9.0"),
            CurrentChannelId = "stable",
            TargetChannelId = "beta",
            InstalledVersion = "2.0.0",
            CohortSeed = "device-001"
        });

        decision.Allowed.Should().BeFalse();
        decision.Action.Should().Be(UpdateChannelTransitionEvaluator.ActionRollback);
        decision.RollbackVersion.Should().Be("1.9.0");
        decision.Reasons.Should().Contain("Rollback target is 1.9.0.");
    }

    [Fact]
    public void Evaluate_Blocks_When_Installed_Version_Is_Below_Channel_Minimum()
    {
        var decision = UpdateChannelTransitionEvaluator.Evaluate(new UpdateChannelTransitionRequest
        {
            Feed = Feed(),
            CurrentChannelId = "stable",
            TargetChannelId = "beta",
            InstalledVersion = "1.5.0",
            CohortSeed = "device-001"
        });

        decision.Allowed.Should().BeFalse();
        decision.Action.Should().Be(UpdateChannelTransitionEvaluator.ActionBlocked);
        decision.Reasons.Should().Contain("Installed version 1.5.0 is below channel minimum version 2.0.0.");
    }

    [Fact]
    public void Evaluate_Holds_NonCritical_Update_Outside_Maintenance_Window()
    {
        var decision = UpdateChannelTransitionEvaluator.Evaluate(new UpdateChannelTransitionRequest
        {
            Feed = Feed(betaCritical: false, betaMaintenanceWindow: "Sun 02:00-04:00 UTC"),
            CurrentChannelId = "stable",
            TargetChannelId = "beta",
            InstalledVersion = "2.0.0",
            CohortSeed = "device-001",
            NowUtc = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero)
        });

        decision.Allowed.Should().BeFalse();
        decision.Action.Should().Be(UpdateChannelTransitionEvaluator.ActionHold);
        decision.MaintenanceWindow.Should().Be("Sun 02:00-04:00 UTC");
        decision.MaintenanceWindowMatched.Should().BeFalse();
        decision.DeadlineReached.Should().BeFalse();
        decision.Reasons.Should().Contain("Current time is outside maintenance window 'Sun 02:00-04:00 UTC'.");
    }

    [Fact]
    public void Evaluate_Allows_NonCritical_Update_Inside_Maintenance_Window()
    {
        var decision = UpdateChannelTransitionEvaluator.Evaluate(new UpdateChannelTransitionRequest
        {
            Feed = Feed(betaCritical: false, betaMaintenanceWindow: "Sun 02:00-04:00 UTC"),
            CurrentChannelId = "stable",
            TargetChannelId = "beta",
            InstalledVersion = "2.0.0",
            CohortSeed = "device-001",
            NowUtc = new DateTimeOffset(2026, 9, 6, 2, 30, 0, TimeSpan.Zero)
        });

        decision.Allowed.Should().BeTrue();
        decision.Action.Should().Be(UpdateChannelTransitionEvaluator.ActionTransition);
        decision.MaintenanceWindowMatched.Should().BeTrue();
        decision.Reasons.Should().Contain("Current time is inside maintenance window 'Sun 02:00-04:00 UTC'.");
    }

    [Fact]
    public void Evaluate_Allows_NonCritical_Update_After_Deadline_Outside_Window()
    {
        var deadline = new DateTimeOffset(2026, 9, 2, 11, 0, 0, TimeSpan.Zero);
        var decision = UpdateChannelTransitionEvaluator.Evaluate(new UpdateChannelTransitionRequest
        {
            Feed = Feed(
                betaCritical: false,
                betaMaintenanceWindow: "Sun 02:00-04:00 UTC",
                betaDeadlineUtc: deadline),
            CurrentChannelId = "stable",
            TargetChannelId = "beta",
            InstalledVersion = "2.0.0",
            CohortSeed = "device-001",
            NowUtc = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero)
        });

        decision.Allowed.Should().BeTrue();
        decision.DeadlineUtc.Should().Be(deadline);
        decision.DeadlineReached.Should().BeTrue();
        decision.MaintenanceWindowMatched.Should().BeFalse();
        decision.Reasons.Should().Contain($"Update deadline {deadline:O} has been reached; maintenance window 'Sun 02:00-04:00 UTC' no longer holds the transition.");
    }

    [Fact]
    public void Evaluate_Allows_Critical_Update_Outside_Maintenance_Window_With_Evidence()
    {
        var decision = UpdateChannelTransitionEvaluator.Evaluate(new UpdateChannelTransitionRequest
        {
            Feed = Feed(betaCritical: true, betaMaintenanceWindow: "Sun 02:00-04:00 UTC"),
            CurrentChannelId = "stable",
            TargetChannelId = "beta",
            InstalledVersion = "2.0.0",
            CohortSeed = "device-001",
            NowUtc = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero)
        });

        decision.Allowed.Should().BeTrue();
        decision.Critical.Should().BeTrue();
        decision.MaintenanceWindowMatched.Should().BeFalse();
        decision.Reasons.Should().Contain("Critical update bypasses maintenance window 'Sun 02:00-04:00 UTC'.");
    }

    [Theory]
    [InlineData("Sun 25:00-26:00 UTC")]
    [InlineData("Sun -02:00-04:00 UTC")]
    [InlineData("Sun 02:00-02:00 UTC")]
    [InlineData("Sun 2:00-4:00 UTC")]
    [InlineData("Sun 02:00-04:00 local")]
    [InlineData("invalid")]
    public void InvalidMaintenanceWindow_BlocksCriticalAndOverdueUpdatesAndAuthoring(string window)
    {
        var decision = UpdateChannelTransitionEvaluator.Evaluate(new()
        {
            Feed = Feed(betaMaintenanceWindow: window, betaCritical: true,
                betaDeadlineUtc: DateTimeOffset.MinValue),
            TargetChannelId = "beta", InstalledVersion = "2.0.0"
        });
        decision.Allowed.Should().BeFalse();
        decision.Action.Should().Be(UpdateChannelTransitionEvaluator.ActionBlocked);
        decision.Reasons.Should().Contain(reason => reason.Contains("Invalid maintenance window"));
        var project = new Beep.Installer.Models.InstallProject();
        project.UpdateChannels.Add(new() { Id = "beta", MaintenanceWindow = window });
        Beep.Installer.Engine.ProjectSchemaService.Validate(project).Diagnostics
            .Should().Contain(diagnostic => diagnostic.Code == "BI1159");
    }

    [Theory]
    [InlineData(5, 23, 0, true)]
    [InlineData(6, 0, 30, true)]
    [InlineData(6, 1, 0, false)]
    [InlineData(6, 23, 0, false)]
    public void OvernightMaintenanceWindow_WrapsWeekAndExcludesEnd(int day, int hour, int minute, bool included)
    {
        var decision = UpdateChannelTransitionEvaluator.Evaluate(new()
        {
            Feed = Feed(betaCritical: false, betaMaintenanceWindow: "Sat 23:00-01:00 UTC"),
            TargetChannelId = "beta", InstalledVersion = "2.0.0",
            NowUtc = new DateTimeOffset(2026, 9, day, hour, minute, 0, TimeSpan.Zero)
        });
        decision.MaintenanceWindowMatched.Should().Be(included);
        decision.Allowed.Should().Be(included);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("2..0")]
    [InlineData("2.0.0.0.1")]
    [InlineData("2.0.0-preview")]
    [InlineData("2.0.-1")]
    [InlineData("+2.0")]
    [InlineData("2147483648.0")]
    public void MinimumVersion_RejectsMissingOrMalformedInstalledVersion(string version)
    {
        var decision = UpdateChannelTransitionEvaluator.Evaluate(new()
        {
            Feed = Feed(), TargetChannelId = "beta", InstalledVersion = version
        });
        decision.Allowed.Should().BeFalse();
        decision.Action.Should().Be(UpdateChannelTransitionEvaluator.ActionBlocked);
        decision.Reasons.Should().Contain(reason => reason.Contains("Installed version is required"));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("2.0")]
    [InlineData("2.0.0")]
    [InlineData("2.0.0.0")]
    public void MinimumVersion_ZeroPadsEquivalentNumericVersions(string version)
    {
        var decision = UpdateChannelTransitionEvaluator.Evaluate(new()
        {
            Feed = Feed(), TargetChannelId = "beta", InstalledVersion = version
        });
        decision.Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData("2..0")]
    [InlineData("2.0.0.0.1")]
    [InlineData("2.0.0-preview")]
    public void ChannelAuthoring_UsesRuntimeVersionGrammar(string version)
    {
        var project = new Beep.Installer.Models.InstallProject();
        project.UpdateChannels.Add(new() { Id = "beta", MinimumVersion = version, RollbackVersion = version });
        var diagnostics = Beep.Installer.Engine.ProjectSchemaService.Validate(project).Diagnostics;
        diagnostics.Should().Contain(d => d.Code == "BI1155");
        diagnostics.Should().Contain(d => d.Code == "BI1156");
    }

    [Fact]
    public void RevokedChannel_InvalidRollbackVersionBlocksInsteadOfRecommendingRollback()
    {
        var decision = UpdateChannelTransitionEvaluator.Evaluate(new()
        {
            Feed = Feed(betaRevoked: true, betaRollbackVersion: "1..9"),
            TargetChannelId = "beta", InstalledVersion = "2.0"
        });
        decision.Allowed.Should().BeFalse();
        decision.Action.Should().Be(UpdateChannelTransitionEvaluator.ActionBlocked);
        decision.Reasons.Should().Contain("Channel minimum or rollback version is invalid.");
    }

    private static UpdateChannelFeedManifest Feed(
        int betaRolloutPercentage = 100,
        bool betaRevoked = false,
        string betaRollbackVersion = "",
        bool betaCritical = true,
        string betaMaintenanceWindow = "",
        DateTimeOffset? betaDeadlineUtc = null)
        => new()
        {
            AppName = "ChannelApp",
            AppPublisher = "Acme",
            AppVersion = "2.0.0",
            SelectedChannelId = "stable",
            Channels =
            {
                new UpdateChannelFeedEntry
                {
                    Id = "stable",
                    Name = "Stable",
                    Ring = "production",
                    FeedUrl = "https://updates.example.test/stable/",
                    RolloutPercentage = 100,
                    MinimumVersion = "1.0.0"
                },
                new UpdateChannelFeedEntry
                {
                    Id = "beta",
                    Name = "Beta",
                    Ring = "early",
                    FeedUrl = "https://updates.example.test/beta/",
                    RolloutPercentage = betaRolloutPercentage,
                    MinimumVersion = "2.0.0",
                    DeadlineUtc = betaDeadlineUtc,
                    Critical = betaCritical,
                    MaintenanceWindow = betaMaintenanceWindow,
                    RollbackVersion = betaRollbackVersion,
                    Revoked = betaRevoked
                }
            }
        };
}
