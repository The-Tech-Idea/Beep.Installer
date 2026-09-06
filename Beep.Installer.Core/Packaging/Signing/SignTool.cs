using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Beep.Installer.Engine;

public sealed class SignToolAuthenticodeSigner : IAuthenticodeSigner
{
    public CodeSigningResult Sign(AuthenticodeSigningRequest request)
    {
        var signtool = SignTool.Find();
        if (signtool == null)
            return new CodeSigningResult { Error = "signtool.exe not found. Install the Windows SDK." };
        var toolVersion = SignTool.Version(signtool);

        var timestampPolicy = string.IsNullOrWhiteSpace(request.TimestampOutagePolicy)
            ? "fail"
            : request.TimestampOutagePolicy.Trim().ToLowerInvariant();
        var sign = Run(signtool, BuildSignArguments(request, includeTimestamp: true), TimeSpan.FromSeconds(60));
        var timestampWarning = "";
        var timestampRetryCount = 0;
        if (!sign.Success && !string.IsNullOrWhiteSpace(request.TimestampUrl) && IsTimestampFailure(sign.Error))
        {
            if (timestampPolicy.Equals("retry", StringComparison.OrdinalIgnoreCase))
            {
                for (var attempt = 0; attempt < Math.Max(0, request.TimestampRetryCount) && !sign.Success; attempt++)
                {
                    timestampRetryCount++;
                    sign = Run(signtool, BuildSignArguments(request, includeTimestamp: true), TimeSpan.FromSeconds(60));
                }
            }

            if (!sign.Success && timestampPolicy.Equals("warn", StringComparison.OrdinalIgnoreCase))
            {
                timestampWarning = $"Timestamp server failed; signed without timestamp because TimestampOutagePolicy=warn. Original error: {sign.Error}";
                sign = Run(signtool, BuildSignArguments(request, includeTimestamp: false), TimeSpan.FromSeconds(60));
            }
        }

        if (!sign.Success)
            return new CodeSigningResult
            {
                Error = sign.Error,
                ToolPath = signtool,
                ToolVersion = toolVersion,
                TimestampOutagePolicy = timestampPolicy,
                TimestampRetryCount = timestampRetryCount
            };

        if (!request.VerifyAfterSign)
            return new CodeSigningResult
            {
                Success = true,
                ToolPath = signtool,
                ToolVersion = toolVersion,
                TimestampOutagePolicy = timestampPolicy,
                TimestampRetryCount = timestampRetryCount,
                TimestampPolicyWarning = timestampWarning
            };

        var verify = Run(signtool, new[] { "verify", "/pa", "/all", "/v", request.FilePath }, TimeSpan.FromSeconds(60));
        if (!verify.Success)
            return new CodeSigningResult
            {
                Error = $"Post-sign verification failed: {verify.Error}",
                ToolPath = signtool,
                ToolVersion = toolVersion,
                TimestampOutagePolicy = timestampPolicy,
                TimestampRetryCount = timestampRetryCount,
                TimestampPolicyWarning = timestampWarning
            };

        var evidence = SignToolVerificationParser.Parse(verify.Output);
        if (!string.IsNullOrWhiteSpace(request.ExpectedSubject)
            && !CertificateSubjectMatches(evidence.CertificateSubject, request.ExpectedSubject))
        {
            return new CodeSigningResult
            {
                Error = string.IsNullOrWhiteSpace(evidence.CertificateSubject)
                    ? $"Post-sign verification did not expose a signer subject for expected subject '{request.ExpectedSubject}'."
                    : $"Post-sign verification signer subject '{evidence.CertificateSubject}' did not match expected subject '{request.ExpectedSubject}'.",
                ToolPath = signtool,
                ToolVersion = toolVersion,
                VerificationSummary = TrimForLog(verify.Output),
                CertificateSubject = evidence.CertificateSubject,
                CertificateIssuer = evidence.CertificateIssuer,
                CertificateThumbprint = evidence.CertificateThumbprint,
                SignatureDigestAlgorithm = evidence.SignatureDigestAlgorithm,
                FileDigestSha256 = evidence.FileDigestSha256,
                Timestamped = evidence.Timestamped,
                TimestampDescription = evidence.TimestampDescription,
                TimestampCertificateSubject = evidence.TimestampCertificateSubject,
                TimestampCertificateIssuer = evidence.TimestampCertificateIssuer,
                TimestampCertificateThumbprint = evidence.TimestampCertificateThumbprint,
                CertificateStoreName = request.StoreName,
                CertificateStoreLocation = request.StoreLocation,
                CertificateStoreThumbprint = request.StoreThumbprint,
                CertificateStoreSubject = request.StoreSubject,
                TimestampOutagePolicy = timestampPolicy,
                TimestampRetryCount = timestampRetryCount,
                TimestampPolicyWarning = timestampWarning
            };
        }

        return new CodeSigningResult
        {
            Success = true,
            VerificationSummary = TrimForLog(verify.Output),
            ToolPath = signtool,
            ToolVersion = toolVersion,
            CertificateSubject = evidence.CertificateSubject,
            CertificateIssuer = evidence.CertificateIssuer,
            CertificateThumbprint = evidence.CertificateThumbprint,
            SignatureDigestAlgorithm = evidence.SignatureDigestAlgorithm,
            FileDigestSha256 = evidence.FileDigestSha256,
            Timestamped = evidence.Timestamped,
            TimestampDescription = evidence.TimestampDescription,
            TimestampCertificateSubject = evidence.TimestampCertificateSubject,
            TimestampCertificateIssuer = evidence.TimestampCertificateIssuer,
            TimestampCertificateThumbprint = evidence.TimestampCertificateThumbprint,
            CertificateStoreName = request.StoreName,
            CertificateStoreLocation = request.StoreLocation,
            CertificateStoreThumbprint = request.StoreThumbprint,
            CertificateStoreSubject = request.StoreSubject,
            TimestampOutagePolicy = timestampPolicy,
            TimestampRetryCount = timestampRetryCount,
            TimestampPolicyWarning = timestampWarning
        };
    }

    private static bool CertificateSubjectMatches(string? actualSubject, string expectedSubject)
        => !string.IsNullOrWhiteSpace(actualSubject)
           && actualSubject.Contains(expectedSubject, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> BuildSignArguments(AuthenticodeSigningRequest request, bool includeTimestamp)
    {
        var args = new List<string> { "sign", "/fd", "SHA256" };
        if (includeTimestamp && !string.IsNullOrWhiteSpace(request.TimestampUrl))
        {
            args.Add("/tr");
            args.Add(request.TimestampUrl);
            args.Add("/td");
            args.Add("SHA256");
        }

        if (!string.IsNullOrWhiteSpace(request.CertificatePath))
        {
            args.Add("/f");
            args.Add(request.CertificatePath);
            if (!string.IsNullOrEmpty(request.CertificatePassword))
            {
                args.Add("/p");
                args.Add(request.CertificatePassword);
            }
        }
        else
        {
            if (request.StoreLocation?.Equals("LocalMachine", StringComparison.OrdinalIgnoreCase) == true
                || request.StoreLocation?.Equals("Machine", StringComparison.OrdinalIgnoreCase) == true)
            {
                args.Add("/sm");
            }

            if (!string.IsNullOrWhiteSpace(request.StoreName))
            {
                args.Add("/s");
                args.Add(request.StoreName);
            }

            if (!string.IsNullOrWhiteSpace(request.StoreThumbprint))
            {
                args.Add("/sha1");
                args.Add(request.StoreThumbprint);
            }
            else if (!string.IsNullOrWhiteSpace(request.StoreSubject))
            {
                args.Add("/n");
                args.Add(request.StoreSubject);
            }
        }

        args.Add(request.FilePath);
        return args;
    }

    private static bool IsTimestampFailure(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && (value.Contains("timestamp", StringComparison.OrdinalIgnoreCase)
               || value.Contains("/tr", StringComparison.OrdinalIgnoreCase)
               || value.Contains("/t ", StringComparison.OrdinalIgnoreCase));

    private static ProcessRunResult Run(string fileName, IReadOnlyList<string> args, TimeSpan timeout)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process is null)
                return new ProcessRunResult(false, "", "Could not start signtool.exe.");

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) { Diag.Debug("SignTool", "signtool timeout kill failed", ex); }
                return new ProcessRunResult(false, stdout, "signtool.exe timed out.");
            }

            var output = $"{stdout} {stderr}".Trim();
            return process.ExitCode == 0
                ? new ProcessRunResult(true, output, null)
                : new ProcessRunResult(false, output, $"signtool exited {process.ExitCode}: {output}");
        }
        catch (Exception ex)
        {
            return new ProcessRunResult(false, "", ex.Message);
        }
    }

    private static string TrimForLog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var singleLine = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return singleLine.Length <= 400 ? singleLine : singleLine[..400] + "…";
    }

    private sealed record ProcessRunResult(bool Success, string Output, string? Error);
}

public sealed class SignToolVerificationEvidence
{
    public string CertificateSubject { get; init; } = "";
    public string CertificateIssuer { get; init; } = "";
    public string CertificateThumbprint { get; init; } = "";
    public string SignatureDigestAlgorithm { get; init; } = "";
    public string FileDigestSha256 { get; init; } = "";
    public bool? Timestamped { get; init; }
    public string TimestampDescription { get; init; } = "";
    public string TimestampCertificateSubject { get; init; } = "";
    public string TimestampCertificateIssuer { get; init; } = "";
    public string TimestampCertificateThumbprint { get; init; } = "";
}

public static class SignToolVerificationParser
{
    private static readonly Regex FileDigestRegex = new(
        @"Hash of file\s*\((?<algorithm>[^)]+)\)\s*:\s*(?<hash>[A-Fa-f0-9]{32,128})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static SignToolVerificationEvidence Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return new SignToolVerificationEvidence();

        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var signer = new CertificateBlock();
        var timestamp = new CertificateBlock();
        var inTimestampBlock = false;
        string signatureDigestAlgorithm = "";
        string fileDigestSha256 = "";
        bool? timestamped = null;
        string timestampDescription = "";

        foreach (var line in lines)
        {
            var fileDigest = FileDigestRegex.Match(line);
            if (fileDigest.Success)
            {
                signatureDigestAlgorithm = fileDigest.Groups["algorithm"].Value.Trim().ToUpperInvariant();
                fileDigestSha256 = fileDigest.Groups["hash"].Value.Trim().ToUpperInvariant();
                continue;
            }

            if (line.Contains("The signature is timestamped", StringComparison.OrdinalIgnoreCase))
            {
                timestamped = true;
                timestampDescription = ValueAfterColon(line);
                continue;
            }

            if (line.Contains("No timestamp", StringComparison.OrdinalIgnoreCase)
                || line.Contains("not timestamped", StringComparison.OrdinalIgnoreCase))
            {
                timestamped = false;
                continue;
            }

            if (line.Contains("Timestamp Verified by", StringComparison.OrdinalIgnoreCase))
            {
                inTimestampBlock = true;
                continue;
            }

            if (line.Contains("Signing Certificate Chain", StringComparison.OrdinalIgnoreCase))
            {
                inTimestampBlock = false;
                continue;
            }

            var target = inTimestampBlock ? timestamp : signer;
            if (line.StartsWith("Issued to:", StringComparison.OrdinalIgnoreCase))
                target.Subject = ValueAfterColon(line);
            else if (line.StartsWith("Issued by:", StringComparison.OrdinalIgnoreCase))
                target.Issuer = ValueAfterColon(line);
            else if (line.StartsWith("SHA1 hash:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("SHA256 hash:", StringComparison.OrdinalIgnoreCase))
                target.Thumbprint = ValueAfterColon(line).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        }

        return new SignToolVerificationEvidence
        {
            CertificateSubject = signer.Subject,
            CertificateIssuer = signer.Issuer,
            CertificateThumbprint = signer.Thumbprint,
            SignatureDigestAlgorithm = signatureDigestAlgorithm,
            FileDigestSha256 = fileDigestSha256,
            Timestamped = timestamped,
            TimestampDescription = timestampDescription,
            TimestampCertificateSubject = timestamp.Subject,
            TimestampCertificateIssuer = timestamp.Issuer,
            TimestampCertificateThumbprint = timestamp.Thumbprint
        };
    }

    private static string ValueAfterColon(string line)
    {
        var idx = line.IndexOf(':');
        return idx < 0 ? "" : line[(idx + 1)..].Trim();
    }

    private sealed class CertificateBlock
    {
        public string Subject { get; set; } = "";
        public string Issuer { get; set; } = "";
        public string Thumbprint { get; set; } = "";
    }
}

public static class SignTool
{
    public static string? Find()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { var candidate = Path.Combine(dir, "signtool.exe"); if (File.Exists(candidate)) return candidate; }
            catch (Exception ex) { Diag.Debug("SignTool", "PATH entry skipped", ex); }
        }

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            var sdkBase = Path.Combine(root, "Windows Kits", "10", "bin");
            if (!Directory.Exists(sdkBase)) continue;
            foreach (var verDir in Directory.EnumerateDirectories(sdkBase))
            {
                var candidate = Path.Combine(verDir, "x64", "signtool.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public static string Version(string signtoolPath)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(signtoolPath);
            return string.IsNullOrWhiteSpace(version.FileVersion) ? "" : version.FileVersion!;
        }
        catch (Exception ex)
        {
            Diag.Debug("SignTool", "signtool version inspection failed", ex);
            return "";
        }
    }

}
