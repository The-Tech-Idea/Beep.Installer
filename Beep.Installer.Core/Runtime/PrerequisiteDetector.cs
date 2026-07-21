using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Beep.Installer.Models;
using TheTechIdea.Beep.Installer;

namespace Beep.Installer.Engine
{
    public static class PrerequisiteDetector
    {
        public class PrerequisiteResult
        {
            public Prerequisite Config { get; set; } = new();
            public bool Detected { get; set; }
            public string? InstalledVersion { get; set; }
            public string StatusText { get; set; } = "";
            public bool IsChecking { get; set; }
            public bool IsMandatory => Config.IsMandatory;
        }

        public static List<PrerequisiteResult> CheckAll(InstallProject project, bool isSelfContained = false)
        {
            var results = new List<PrerequisiteResult>();

            var dotnetResult = new PrerequisiteResult
            {
                Config = new Prerequisite
                {
                    Id = "dotnet-runtime",
                    Name = ".NET Runtime",
                    IsMandatory = !isSelfContained,
                    DownloadUrl = "https://dotnet.microsoft.com/download",
                    HelpUrl = "https://dotnet.microsoft.com/download"
                },
                IsChecking = false
            };

            if (isSelfContained)
            {
                dotnetResult.Detected = true;
                dotnetResult.StatusText = "Bundled with installer";
            }
            else
            {
                var version = GetDotNetRuntimeVersion();
                dotnetResult.Detected = version != null;
                dotnetResult.InstalledVersion = version;
                dotnetResult.StatusText = version != null ? $"Detected (v{version})" : "Not found";
            }
            results.Add(dotnetResult);

            if (project.Prerequisites?.Count > 0)
            {
                foreach (var prereq in project.Prerequisites)
                {
                    var result = new PrerequisiteResult
                    {
                        Config = prereq,
                        IsChecking = false
                    };

                    if (!string.IsNullOrWhiteSpace(prereq.DetectionCommand))
                    {
                        result.Detected = RunDetectionCommand(prereq.DetectionCommand, prereq.DetectionPattern);
                        result.StatusText = result.Detected ? "Detected" : (prereq.IsMandatory ? "Not found" : "Optional");
                    }
                    else
                    {
                        result.Detected = true;
                        result.StatusText = "No check defined";
                    }

                    results.Add(result);
                }
            }

            return results;
        }

        public static string? GetDotNetRuntimeVersion()
        {
            var version = TryDotNetCli();
            if (version != null) return version;

            version = ScanSharedRuntimeFolders();
            if (version != null) return version;

            version = GetCurrentProcessRuntimeVersion();
            if (version != null) return version;

            version = GetRegistryDotNetVersion();
            if (version != null) return version;

            return null;
        }

        private static string? TryDotNetCli()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = "--list-runtimes",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                var matches = Regex.Matches(output, @"Microsoft\.NETCore\.App\s+([\d.]+)");
                Version? best = null;
                foreach (Match m in matches)
                {
                    if (Version.TryParse(m.Groups[1].Value, out var v) && (best == null || v > best))
                        best = v;
                }
                return best?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string? ScanSharedRuntimeFolders()
        {
            try
            {
                var sharedDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "dotnet", "shared", "Microsoft.NETCore.App");
                if (!Directory.Exists(sharedDir)) return null;

                Version? best = null;
                foreach (var dir in Directory.EnumerateDirectories(sharedDir))
                {
                    var name = Path.GetFileName(dir);
                    if (Version.TryParse(name, out var v) && (best == null || v > best))
                        best = v;
                }
                return best?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string? GetCurrentProcessRuntimeVersion()
        {
            try
            {
                var desc = RuntimeInformation.FrameworkDescription;
                var m = Regex.Match(desc, @"(\d+\.\d+(?:\.\d+)?)");
                if (m.Success && Version.TryParse(m.Groups[1].Value, out var v))
                    return v.ToString();
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string? GetRegistryDotNetVersion()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost");
                var value = key?.GetValue("Version") as string;
                if (value != null && Version.TryParse(value, out _))
                    return value;
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool RunDetectionCommand(string command, string? pattern)
        {
            try
            {
                var parts = command.Split(' ', 2);
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = parts[0],
                        Arguments = parts.Length > 1 ? parts[1] : "",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10000);

                if (string.IsNullOrWhiteSpace(pattern)) return process.ExitCode == 0;
                return Regex.IsMatch(output, pattern, RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
