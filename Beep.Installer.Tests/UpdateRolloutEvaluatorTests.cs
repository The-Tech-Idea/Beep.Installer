using Beep.Installer.Engine.Updates;
using Beep.Installer.Engine;
using Beep.Installer.Models;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public class UpdateRolloutEvaluatorTests
{
    [Fact]
    public void Evaluate_Returns_Deterministic_Cohort_For_Same_Project_Channel_And_Seed()
    {
        var project = InstallerProjectFactory.CreateNew("CohortApp", "1.0.0", "Acme", "src");
        var channel = new UpdateChannelDefinition
        {
            Id = "beta",
            Ring = "early",
            RolloutPercentage = 35
        };

        var first = UpdateRolloutEvaluator.Evaluate(project, channel, "device-001");
        var second = UpdateRolloutEvaluator.Evaluate(project, channel, "device-001");
        var otherSeed = UpdateRolloutEvaluator.Evaluate(project, channel, "device-002");

        second.Should().BeEquivalentTo(first);
        first.Bucket.Should().BeInRange(0, 99);
        first.CohortHash.Should().HaveLength(64);
        first.CohortHash.Should().NotContain("device-001");
        otherSeed.CohortHash.Should().NotBe(first.CohortHash);
    }

    [Fact]
    public void Evaluate_Blocks_Revoked_Channel_Regardless_Of_Percentage()
    {
        var project = InstallerProjectFactory.CreateNew("RevokedApp", "1.0.0", "Acme", "src");
        var channel = new UpdateChannelDefinition
        {
            Id = "stable",
            Ring = "production",
            RolloutPercentage = 100,
            Revoked = true
        };

        var decision = UpdateRolloutEvaluator.Evaluate(project, channel, "device-001");

        decision.Included.Should().BeFalse();
        decision.RolloutPercentage.Should().Be(100);
        decision.Reason.Should().Be("Channel is revoked.");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(100, true)]
    public void Evaluate_Handles_Rollout_Boundaries(int rolloutPercentage, bool expectedIncluded)
    {
        var project = InstallerProjectFactory.CreateNew("BoundaryApp", "1.0.0", "Acme", "src");
        var channel = new UpdateChannelDefinition
        {
            Id = "stable",
            Ring = "production",
            RolloutPercentage = rolloutPercentage
        };

        var decision = UpdateRolloutEvaluator.Evaluate(project, channel, "device-001");

        decision.Included.Should().Be(expectedIncluded);
        decision.RolloutPercentage.Should().Be(rolloutPercentage);
    }
}
