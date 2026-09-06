using System.Security.Cryptography;
using System.Text;
using Beep.Installer.Models;

namespace Beep.Installer.Engine.Updates;

public sealed record UpdateRolloutDecision
{
    public string ChannelId { get; init; } = "";
    public string Ring { get; init; } = "";
    public int RolloutPercentage { get; init; }
    public int Bucket { get; init; }
    public bool Included { get; init; }
    public string CohortHash { get; init; } = "";
    public string Reason { get; init; } = "";
}

public static class UpdateRolloutEvaluator
{
    public static UpdateRolloutDecision Evaluate(
        InstallProject project,
        UpdateChannelDefinition? channel,
        string cohortSeed)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (channel is null)
        {
            return new UpdateRolloutDecision
            {
                Included = true,
                RolloutPercentage = 100,
                Bucket = 0,
                Reason = "No selected update channel; rollout gate is not configured."
            };
        }

        var normalizedPercentage = Math.Clamp(channel.RolloutPercentage, 0, 100);
        if (channel.Revoked)
        {
            return Decision(project, channel, cohortSeed, normalizedPercentage, included: false, "Channel is revoked.");
        }

        if (normalizedPercentage <= 0)
        {
            return Decision(project, channel, cohortSeed, normalizedPercentage, included: false, "Rollout percentage is 0.");
        }

        if (normalizedPercentage >= 100)
        {
            return Decision(project, channel, cohortSeed, normalizedPercentage, included: true, "Rollout percentage is 100.");
        }

        var decision = Decision(project, channel, cohortSeed, normalizedPercentage, included: false, "");
        return decision with
        {
            Included = decision.Bucket < normalizedPercentage,
            Reason = decision.Bucket < normalizedPercentage
                ? $"Cohort bucket {decision.Bucket} is inside rollout percentage {normalizedPercentage}."
                : $"Cohort bucket {decision.Bucket} is outside rollout percentage {normalizedPercentage}."
        };
    }

    public static string DefaultCohortSeed()
        => Environment.MachineName;

    private static UpdateRolloutDecision Decision(
        InstallProject project,
        UpdateChannelDefinition channel,
        string cohortSeed,
        int rolloutPercentage,
        bool included,
        string reason)
    {
        var material = string.Join(
            "|",
            project.AppName ?? "",
            project.AppPublisher ?? "",
            channel.Id ?? "",
            channel.Ring ?? "",
            cohortSeed ?? "");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        var bucket = (int)(Convert.ToUInt32(hash[..8], 16) % 100);

        return new UpdateRolloutDecision
        {
            ChannelId = channel.Id ?? "",
            Ring = channel.Ring ?? "",
            RolloutPercentage = rolloutPercentage,
            Bucket = bucket,
            Included = included,
            CohortHash = hash,
            Reason = reason
        };
    }
}
