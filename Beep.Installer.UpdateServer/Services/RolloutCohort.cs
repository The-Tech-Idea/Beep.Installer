using System;
using System.Security.Cryptography;
using System.Text;

namespace Beep.Installer.UpdateServer.Services
{
    /// <summary>
    /// Deterministic staged-rollout bucketing. A client's bucket for a given version is a stable
    /// hash of (clientId, version) mod 100 — so a client either is or isn't in the rollout cohort
    /// and stays that way across checks (no flip-flopping as the percentage climbs). Because the
    /// version is part of the hash, the cohort is reshuffled per release, so the same 10% of users
    /// aren't always the guinea pigs.
    /// </summary>
    public static class RolloutCohort
    {
        public static int Bucket(string clientId, string version)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{clientId}:{version}"));
            return (int)(BitConverter.ToUInt32(hash, 0) % 100u); // 0..99
        }

        /// <summary>True when a client should be offered a version rolling out at <paramref name="percent"/>.</summary>
        public static bool IsEligible(string clientId, string version, int percent)
        {
            if (percent >= 100) return true;
            if (percent <= 0) return false;
            // Without a stable client id we can't place the client in a cohort; hold them on the
            // fully-rolled-out version until this one reaches 100%.
            if (string.IsNullOrWhiteSpace(clientId)) return false;
            return Bucket(clientId, version) < percent;
        }
    }

    /// <summary>Best-effort version ordering: numeric when both parse, else ordinal.</summary>
    public static class VersionOrder
    {
        public static int Compare(string a, string b)
        {
            if (Version.TryParse(Trim(a), out var va) && Version.TryParse(Trim(b), out var vb))
                return va.CompareTo(vb);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // Version.TryParse rejects a pre-release suffix; compare the numeric core when present.
        private static string Trim(string v)
        {
            var dash = v.IndexOf('-');
            return dash > 0 ? v[..dash] : v;
        }
    }
}
