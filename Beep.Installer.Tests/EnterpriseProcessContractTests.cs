using System.Text.Json;
using System.Security.Cryptography;
using Beep.Installer.Engine;
using Beep.Installer.Engine.Updates;
using Beep.Installer.Models;
using FluentAssertions;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class EnterpriseProcessContractTests : IDisposable
{
    private readonly string _root;
    private readonly string _sourceRoot;
    private readonly string _scriptPath;
    private string? _imageTestProduct;
    private string _imageAppId = "";

    public EnterpriseProcessContractTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"BeepProcessContract_{Guid.NewGuid():N}");
        _sourceRoot = Path.Combine(_root, "source");
        Directory.CreateDirectory(_sourceRoot);
        File.WriteAllText(Path.Combine(_sourceRoot, "app.exe"), "contract-app");

        var project = InstallerProjectFactory.CreateNew("Contract App", "1.2.3", "ACME", _sourceRoot);
        project.DefaultScope = InstallationScope.User;
        project.CreateUninstallEntry = false;
        // Package identity is authored, never derived from display names — without these the
        // MSIX format is "Blocked" and /FORMATREADINESS refuses to report the project releasable.
        project.MsixIdentity = "ACME.ContractApp";
        project.MsixPublisher = "CN=ACME";
        project.AppUpdateChannel = "stable";
        project.UpdateChannels.Add(new UpdateChannelDefinition
        {
            Id = "stable",
            Name = "Stable",
            Ring = "production",
            FeedUrl = "https://updates.example.test/stable/",
            RolloutPercentage = 25,
            MinimumVersion = "1.0.0",
            Critical = true,
            MaintenanceWindow = "Sun 02:00-04:00 UTC",
            RollbackVersion = "1.0.0"
        });
        project.Components.Clear();
        project.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true,
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = Path.Combine(_sourceRoot, "app.exe"),
                    DestinationPath = "app.exe"
                }
            }
        });

        _scriptPath = Path.Combine(_root, "contract.bsetup");
        InstallerScriptSerializer.Save(project, _scriptPath);
    }

    public void Dispose()
    {
        if (_imageTestProduct is not null)
        {
            using var key = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Registry64);
            key.DeleteSubKeyTree(UpgradeEngine.RegistrationKeyPath(_imageAppId), throwOnMissingSubKey: false);
            key.DeleteSubKeyTree(InstallationRegistration.UninstallKeyPath(_imageAppId), throwOnMissingSubKey: false);
        }
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("/S")]
    [InlineData("/REPAIR")]
    [InlineData("/UNINSTALL")]
    public void RuntimeOperation_RejectsBusyInstallationAcrossProcesses(string mode)
    {
        var installPath = Path.Combine(_root, "busy-install");
        Directory.CreateDirectory(installPath);
        var marker = Path.Combine(installPath, "preserve.txt");
        File.WriteAllText(marker, "existing installation");

        using (InstallationOperationLock.Acquire(installPath))
        {
            var result = InstallerCliRunner.Run(
                $"/SCRIPT=\"{_scriptPath}\" {mode} /JSON /D=\"{installPath}\"");
            result.ExitCode.Should().Be(2, result.StandardOutput + result.StandardError);
            result.StandardError.Should().Contain("Another operation is already active");
            using var json = JsonDocument.Parse(result.StandardOutput);
            json.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
            Directory.GetFiles(installPath, "*", SearchOption.AllDirectories).Should().Equal(marker);
            File.ReadAllText(marker).Should().Be("existing installation");
        }

        // A rejected child must not retain or release its owner's lease.
        using var reacquired = InstallationOperationLock.Acquire(installPath);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void InstalledImageDelta_UpdatesIndependentInstallAndRetainsLocalRecoveryHistory(bool customJournal, bool externalJournal, bool changeFiles)
    {
        _imageTestProduct = "BeepImageTest_" + Guid.NewGuid().ToString("N");
        var originalProject = InstallerScriptSerializer.Load(_scriptPath).Item1!;
        originalProject.AppName = _imageTestProduct;
        _imageAppId = originalProject.AppId;
        if (changeFiles)
        {
            File.WriteAllText(Path.Combine(_sourceRoot, "old.txt"), "retired file");
            originalProject.Components[0].Files.Add(new() { SourcePath = Path.Combine(_sourceRoot, "old.txt"), DestinationPath = "old.txt" });
        }
        InstallerScriptSerializer.Save(originalProject, _scriptPath);
        var authorBase = Path.Combine(_root, "author-base");
        var authorTarget = Path.Combine(_root, "author-target");
        var client = Path.Combine(_root, "client");
        string JournalArgument(string root) => customJournal ? $" /JOURNAL=\"{(externalJournal ? Path.Combine(_root, "external-journals", Path.GetFileName(root) + ".json") : Path.Combine(root, "recovery", "custom.json"))}\"" : "";
        void Run(string arguments)
        {
            var result = InstallerCliRunner.Run(arguments);
            result.ExitCode.Should().Be(0, result.StandardOutput + result.StandardError);
        }
        Run($"/SCRIPT=\"{_scriptPath}\" /S /D=\"{authorBase}\" /NORESTART" + JournalArgument(authorBase));
        Run($"/SCRIPT=\"{_scriptPath}\" /S /D=\"{client}\" /NORESTART" + JournalArgument(client));
        var clientJournalPath = Beep.Installer.Extensibility.ResourceExecutionJournalStore.ResolvePath(client, _imageAppId);
        var beforeJournal = File.ReadAllBytes(clientJournalPath);
        var before = new Beep.Installer.Extensibility.ResourceExecutionJournalStore(clientJournalPath).Load();
        var project = InstallerScriptSerializer.Load(_scriptPath).Item1!;
        project.AppVersion = "1.2.4";
        if (changeFiles)
        {
            project.Components[0].Files.Remove(project.Components[0].Files.Single(file => file.DestinationPath == "old.txt"));
            File.WriteAllText(Path.Combine(_sourceRoot, "new.txt"), "new file");
            project.Components[0].Files.Add(new() { SourcePath = Path.Combine(_sourceRoot, "new.txt"), DestinationPath = "new.txt" });
        }
        var targetScript = Path.Combine(_root, "target.bsetup");
        InstallerScriptSerializer.Save(project, targetScript);
        File.WriteAllText(Path.Combine(_sourceRoot, "app.exe"), "updated-contract-app");
        Run($"/SCRIPT=\"{targetScript}\" /S /D=\"{authorTarget}\" /NORESTART" + JournalArgument(authorTarget));
        using var registrationHive = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.CurrentUser, Microsoft.Win32.RegistryView.Registry64);
        new UpgradeEngine().RegisterInstall(new InstallConfig
        {
            AppId = _imageAppId, ProductName = _imageTestProduct, ProductVersion = "1.2.3", Publisher = "ACME"
        }, client, registrationHive);
        using (var arp = registrationHive.CreateSubKey(InstallationRegistration.UninstallKeyPath(_imageAppId)))
        {
            arp.SetValue("InstallLocation", client);
            arp.SetValue("Publisher", "ACME");
            arp.SetValue("DisplayVersion", "1.2.3");
            arp.SetValue("UninstallString", "preserve existing maintenance command");
        }
        void AssertRegisteredVersion(string expected)
        {
            using var registration = registrationHive.OpenSubKey(UpgradeEngine.RegistrationKeyPath(_imageAppId));
            using var arp = registrationHive.OpenSubKey(InstallationRegistration.UninstallKeyPath(_imageAppId));
            registration!.GetValue("Version").Should().Be(expected);
            registration.GetValue("InstallPath").Should().Be(client);
            arp!.GetValue("DisplayVersion").Should().Be(expected);
            arp.GetValue("UninstallString").Should().Be("preserve existing maintenance command");
        }
        using var rsa = RSA.Create(2048);
        var privateKey = Path.Combine(_root, "image-private.pem");
        var publicKey = Path.Combine(_root, "image-public.pem");
        File.WriteAllText(privateKey, rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(publicKey, rsa.ExportSubjectPublicKeyInfoPem());
        var delta = Path.Combine(_root, "image-delta");
        Run($"/DELTA=\"{delta}\" /SCRIPT=\"{targetScript}\" /DELTABASE=\"{authorBase}\" /DELTATARGET=\"{authorTarget}\" /DELTABASEVERSION=1.2.3 /DELTASIGNKEY=\"{privateKey}\"");
        var substitutedJournal = System.Text.Json.Nodes.JsonNode.Parse(beforeJournal)!;
        substitutedJournal["metadata"]!["appId"] = Guid.NewGuid().ToString("D");
        File.WriteAllText(clientJournalPath, substitutedJournal.ToJsonString());
        var substitutedBytes = File.ReadAllBytes(clientJournalPath);
        var identityRefused = new DeltaUpdatePackageService().ApplyAtomically(new()
        {
            DeltaDirectory = delta, CurrentInstallDirectory = client, StageDirectory = Path.Combine(_root, "wrong-id-stage"),
            CurrentVersion = "1.2.3", RequireSignature = true, TrustedPublicKeys = new() { rsa.ExportSubjectPublicKeyInfoPem() }
        });
        identityRefused.Success.Should().BeFalse();
        identityRefused.Error.Should().Contain("AppId");
        File.ReadAllBytes(clientJournalPath).Should().Equal(substitutedBytes);
        Directory.Exists(client + ".bak1").Should().BeFalse();
        File.WriteAllBytes(clientJournalPath, beforeJournal);
        using (var arp = registrationHive.OpenSubKey(InstallationRegistration.UninstallKeyPath(_imageAppId), writable: true))
            arp!.SetValue("Publisher", "Unrelated Publisher");
        var refused = new DeltaUpdatePackageService().ApplyAtomically(new()
        {
            DeltaDirectory = delta, CurrentInstallDirectory = client, StageDirectory = Path.Combine(_root, "refused-stage"),
            CurrentVersion = "1.2.3", RequireSignature = true, TrustedPublicKeys = new() { rsa.ExportSubjectPublicKeyInfoPem() }
        });
        refused.Success.Should().BeFalse();
        refused.Error.Should().Contain("publisher");
        File.ReadAllBytes(clientJournalPath).Should().Equal(beforeJournal);
        Directory.Exists(client + ".bak1").Should().BeFalse();
        AssertRegisteredVersion("1.2.3");
        using (var arp = registrationHive.OpenSubKey(InstallationRegistration.UninstallKeyPath(_imageAppId), writable: true))
            arp!.SetValue("Publisher", "ACME");
        void Apply(string stage) => Run($"/APPLYDELTA=\"{delta}\" /DELTACURRENT=\"{client}\" /DELTASTAGE=\"{stage}\" /DELTACURRENTVERSION=1.2.3 /DELTATRUSTKEY=\"{publicKey}\" /REQUIRESIGNED");
        if (externalJournal)
        {
            using (var locked = new FileStream(clientJournalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var interrupted = InstallerCliRunner.Run($"/APPLYDELTA=\"{delta}\" /DELTACURRENT=\"{client}\" /DELTASTAGE=\"{Path.Combine(_root, "stage-one")}\" /DELTACURRENTVERSION=1.2.3 /DELTATRUSTKEY=\"{publicKey}\" /REQUIRESIGNED");
                interrupted.ExitCode.Should().Be(1, interrupted.StandardOutput + interrupted.StandardError);
                interrupted.StandardError.Should().Contain("Files were promoted");
                File.ReadAllBytes(clientJournalPath).Should().Equal(beforeJournal);
                File.ReadAllText(Path.Combine(client, "app.exe")).Should().Be("updated-contract-app");
            }
            Run($"/RECOVERDELTA=\"{client}.delta-journal.json\" /DRYRUN");
            File.ReadAllBytes(clientJournalPath).Should().Equal(beforeJournal);
            Run($"/RECOVERDELTA=\"{client}.delta-journal.json\"");
        }
        else Apply(Path.Combine(_root, "stage-one"));
        Beep.Installer.Extensibility.ResourceExecutionJournalStore.ResolvePath(client, _imageAppId).Should().Be(clientJournalPath);
        AssertRegisteredVersion("1.2.4");
        File.ReadAllText(Path.Combine(client, "app.exe")).Should().Be("updated-contract-app");
        var updated = new Beep.Installer.Extensibility.ResourceExecutionJournalStore(clientJournalPath).Load();
        updated.Metadata.ProductVersion.Should().Be("1.2.4");
        updated.Metadata.AppId.Should().Be(before.Metadata.AppId);
        updated.Metadata.AttemptId.Should().Be(before.Metadata.AttemptId);
        JsonSerializer.Serialize(updated.Entries.Take(before.Entries.Count)).Should().Be(JsonSerializer.Serialize(before.Entries));
        if (changeFiles)
        {
            File.Exists(Path.Combine(client, "old.txt")).Should().BeFalse();
            File.ReadAllText(Path.Combine(client, "new.txt")).Should().Be("new file");
            var activeFiles = Beep.Installer.Extensibility.ResourceJournalRecoveryService.OperationsToReplay(updated)
                .Where(operation => operation.Type == "file.copy").Select(operation => operation.Inputs["destination"]);
            activeFiles.Should().BeEquivalentTo(new[] { "app.exe", "new.txt" });
        }
        else updated.Entries.Should().HaveCount(before.Entries.Count);
        if (externalJournal)
        {
            var expected = File.ReadAllBytes(clientJournalPath);
            File.AppendAllText(clientJournalPath, " ");
            var refusedRollback = InstallerCliRunner.Run($"/ROLLBACKDELTA=\"{client}.delta-journal.json\"");
            refusedRollback.ExitCode.Should().Be(1);
            refusedRollback.StandardError.Should().Contain("differs from both transaction snapshots");
            File.ReadAllText(Path.Combine(client, "app.exe")).Should().Be("updated-contract-app");
            File.WriteAllBytes(clientJournalPath, expected);
        }
        Run($"/ROLLBACKDELTA=\"{client}.delta-journal.json\"");
        AssertRegisteredVersion("1.2.3");
        File.ReadAllBytes(clientJournalPath).Should().Equal(beforeJournal);
        File.ReadAllText(Path.Combine(client, "app.exe")).Should().Be("contract-app");
        if (changeFiles)
        {
            File.ReadAllText(Path.Combine(client, "old.txt")).Should().Be("retired file");
            File.Exists(Path.Combine(client, "new.txt")).Should().BeFalse();
        }
        Apply(Path.Combine(_root, "stage-two"));
        AssertRegisteredVersion("1.2.4");
        if (externalJournal)
        {
            var checkpointPath = client + ".delta-journal.json";
            var checkpoint = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(checkpointPath))!;
            checkpoint["state"] = "prepared";
            File.WriteAllText(checkpointPath, checkpoint.ToJsonString());
            var retainedTarget = client + ".interrupted-target";
            Directory.Move(client, retainedTarget);
            var externalBeforeRecovery = File.ReadAllBytes(clientJournalPath);
            Run($"/RECOVERDELTA=\"{checkpointPath}\" /DRYRUN");
            Directory.Exists(client).Should().BeFalse();
            File.ReadAllBytes(clientJournalPath).Should().Equal(externalBeforeRecovery);
            Run($"/RECOVERDELTA=\"{checkpointPath}\"");
            File.ReadAllBytes(clientJournalPath).Should().Equal(beforeJournal);
            File.ReadAllText(Path.Combine(client, "app.exe")).Should().Be("contract-app");
            Directory.Exists(retainedTarget).Should().BeTrue();
            AssertRegisteredVersion("1.2.3");
            Apply(Path.Combine(_root, "stage-three"));
            AssertRegisteredVersion("1.2.4");
        }
        using (var registration = registrationHive.OpenSubKey(UpgradeEngine.RegistrationKeyPath(_imageAppId), writable: true))
            registration!.SetValue("Version", "1.2.3");
        Run($"/RECOVERDELTA=\"{client}.delta-journal.json\" /DRYRUN");
        using (var registration = registrationHive.OpenSubKey(UpgradeEngine.RegistrationKeyPath(_imageAppId)))
            registration!.GetValue("Version").Should().Be("1.2.3", "preview must not reconcile registration");
        Run($"/RECOVERDELTA=\"{client}.delta-journal.json\"");
        AssertRegisteredVersion("1.2.4");
        if (changeFiles && externalJournal)
        {
            project.AppVersion = "1.2.5";
            File.WriteAllText(Path.Combine(_sourceRoot, "third.txt"), "third release file");
            project.Components[0].Files.Add(new() { SourcePath = Path.Combine(_sourceRoot, "third.txt"), DestinationPath = "third.txt" });
            InstallerScriptSerializer.Save(project, targetScript);
            var thirdImage = Path.Combine(_root, "author-third");
            Run($"/SCRIPT=\"{targetScript}\" /S /D=\"{thirdImage}\" /NORESTART" + JournalArgument(thirdImage));
            new UpgradeEngine().RegisterInstall(new InstallConfig { AppId = _imageAppId, ProductName = _imageTestProduct, ProductVersion = "1.2.4", Publisher = "ACME" }, client, registrationHive);
            var nextDelta = Path.Combine(_root, "next-delta");
            Run($"/DELTA=\"{nextDelta}\" /SCRIPT=\"{targetScript}\" /DELTABASE=\"{authorTarget}\" /DELTATARGET=\"{thirdImage}\" /DELTABASEVERSION=1.2.4 /DELTASIGNKEY=\"{privateKey}\"");
            Run($"/APPLYDELTA=\"{nextDelta}\" /DELTACURRENT=\"{client}\" /DELTASTAGE=\"{Path.Combine(_root, "next-stage")}\" /DELTACURRENTVERSION=1.2.4 /DELTATRUSTKEY=\"{publicKey}\" /REQUIRESIGNED");
            File.ReadAllText(Path.Combine(client, "third.txt")).Should().Be("third release file");
            AssertRegisteredVersion("1.2.5");
        }
        if (changeFiles) File.Delete(Path.Combine(client, "new.txt"));
        Run($"/SCRIPT=\"{targetScript}\" /REPAIR /D=\"{client}\" /NORESTART");
        if (changeFiles) File.ReadAllText(Path.Combine(client, "new.txt")).Should().Be("new file");
        Run($"/SCRIPT=\"{targetScript}\" /UNINSTALL /D=\"{client}\" /NORESTART");
        File.Exists(Path.Combine(client, "app.exe")).Should().BeFalse();
        File.Exists(Path.Combine(client, "new.txt")).Should().BeFalse();
        File.Exists(Path.Combine(client, "third.txt")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomJournal_IsRediscoveredForRepairRecoveryAndUninstall(bool external)
    {
        _imageTestProduct = "BeepJournalTest_" + Guid.NewGuid().ToString("N");
        var project = InstallerScriptSerializer.Load(_scriptPath).Item1!;
        project.AppName = _imageTestProduct;
        InstallerScriptSerializer.Save(project, _scriptPath);
        // Journal discovery and registry cleanup both key off the AppId, not the display name.
        _imageAppId = project.AppId;
        var install = Path.Combine(_root, "custom-install");
        var custom = Path.Combine(external ? _root : install, "recovery", "selected.json");
        Directory.CreateDirectory(Path.GetDirectoryName(custom)!);
        var neighbor = Path.Combine(Path.GetDirectoryName(custom)!, "operator-notes.txt");
        File.WriteAllText(neighbor, "preserve");
        void Run(string arguments)
        {
            var result = InstallerCliRunner.Run(arguments);
            result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        }
        Run($"/SCRIPT=\"{_scriptPath}\" /S /D=\"{install}\" /JOURNAL=\"{custom}\" /NORESTART");
        var location = Beep.Installer.Extensibility.ResourceExecutionJournalStore.LocationPath(install, _imageAppId);
        File.Exists(location).Should().BeTrue();
        Beep.Installer.Extensibility.ResourceExecutionJournalStore.ResolvePath(install, _imageAppId).Should().Be(custom);
        File.Exists(Beep.Installer.Extensibility.ResourceExecutionJournalStore.DefaultPath(install, _imageAppId)).Should().BeFalse();
        File.WriteAllText(Path.Combine(install, "app.exe"), "damaged");
        Run($"/SCRIPT=\"{_scriptPath}\" /REPAIR /D=\"{install}\" /NORESTART");
        File.ReadAllText(Path.Combine(install, "app.exe")).Should().Be("contract-app");
        Run($"/RECOVERY=\"{_scriptPath}\" /D=\"{install}\"");
        var saved = File.ReadAllBytes(custom);
        File.Delete(custom);
        var missing = InstallerCliRunner.Run($"/SCRIPT=\"{_scriptPath}\" /REPAIR /D=\"{install}\" /JSON");
        missing.ExitCode.Should().Be(2);
        missing.StandardError.Should().Contain("journal is missing");
        File.Exists(Beep.Installer.Extensibility.ResourceExecutionJournalStore.DefaultPath(install, _imageAppId)).Should().BeFalse();
        File.WriteAllBytes(custom, saved);
        Run($"/SCRIPT=\"{_scriptPath}\" /UNINSTALL /D=\"{install}\" /NORESTART");
        File.Exists(custom).Should().BeFalse();
        File.Exists(location).Should().BeFalse();
        File.Exists(Path.Combine(install, "app.exe")).Should().BeFalse();
        File.ReadAllText(neighbor).Should().Be("preserve");
    }

    [Fact]
    public void ValidateCli_UsesHeadlessSdkValidationContract()
    {
        var result = InstallerCliRunner.Run($"/VALIDATE=\"{_scriptPath}\" /STRICT");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardError.Trim().Should().BeEmpty();
        result.StandardOutput.Should().Contain("Script  :");
        result.StandardOutput.Should().Contain("Product : Contract App 1.2.3");
        result.StandardOutput.Should().Contain("All checks passed.");
        result.StandardOutput.Should().Contain("Exit code: 0");
    }

    [Fact]
    public void ValidateJsonCli_WritesMachineReportToStdoutAndOutFile()
    {
        var sampleExtension = SampleExtensionDirectory();
        var reportPath = Path.Combine(_root, "validation", "validation-report.json");

        var result = InstallerCliRunner.Run($"/VALIDATE=\"{_scriptPath}\" /EXTENSIONS=\"{sampleExtension}\" /JSON /OUT=\"{reportPath}\"");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardError.Trim().Should().BeEmpty();
        result.StandardOutput.Should().NotContain("Script  :");
        result.StandardOutput.Should().NotContain("Exit code:");
        File.Exists(reportPath).Should().BeTrue();

        using var stdoutJson = JsonDocument.Parse(result.StandardOutput);
        var root = stdoutJson.RootElement;
        root.GetProperty("schemaVersion").GetString().Should().Be("1.0");
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("exitCode").GetInt32().Should().Be(0);
        root.GetProperty("productName").GetString().Should().Be("Contract App");
        root.GetProperty("productVersion").GetString().Should().Be("1.2.3");
        root.GetProperty("componentCount").GetInt32().Should().Be(1);
        root.GetProperty("warningCount").GetInt32().Should().Be(1);
        root.GetProperty("extensionDiagnostics")[0].GetProperty("severity").GetString().Should().Be("warning");
        root.GetProperty("extensionDiagnostics")[0].GetProperty("code").GetString().Should().Be("SAMPLE100");

        using var fileJson = JsonDocument.Parse(File.ReadAllText(reportPath));
        fileJson.RootElement.GetProperty("extensionDiagnostics")[0].GetProperty("code").GetString().Should().Be("SAMPLE100");
    }

    [Fact]
    public void ValidateJsonCli_ReturnsMachineDiagnosticsWhenExtensionValidatorBlocks()
    {
        var blockedSource = Path.Combine(_root, "blocked-validate-source");
        Directory.CreateDirectory(blockedSource);
        File.WriteAllText(Path.Combine(blockedSource, "blocked.exe"), "blocked");
        var blockedProject = InstallerProjectFactory.CreateNew("Blocked App", "1.0.0", "ACME", blockedSource);
        blockedProject.Components.Clear();
        blockedProject.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true,
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = Path.Combine(blockedSource, "blocked.exe"),
                    DestinationPath = "blocked.exe"
                }
            }
        });
        var blockedScript = Path.Combine(_root, "blocked-validate.bsetup");
        InstallerScriptSerializer.Save(blockedProject, blockedScript);

        var result = InstallerCliRunner.Run($"/VALIDATE=\"{blockedScript}\" /EXTENSIONS=\"{SampleExtensionDirectory()}\" /JSON");

        result.ExitCode.Should().Be(1, result.StandardError + result.StandardOutput);
        result.StandardError.Trim().Should().BeEmpty();
        result.StandardOutput.Should().NotContain("extension diagnostic(s):");
        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("exitCode").GetInt32().Should().Be(1);
        root.GetProperty("errorCount").GetInt32().Should().Be(1);
        root.GetProperty("extensionDiagnostics")[0].GetProperty("severity").GetString().Should().Be("error");
        root.GetProperty("extensionDiagnostics")[0].GetProperty("code").GetString().Should().Be("SAMPLE101");
        root.GetProperty("extensionDiagnostics")[0].GetProperty("message").GetString().Should().Be("Sample extension validator blocked 'Blocked App'.");
    }

    [Fact]
    public void PlanJsonCli_WritesOutFileThroughHeadlessSdkContract()
    {
        var planPath = Path.Combine(_root, "plans", "install-plan.json");

        var result = InstallerCliRunner.Run($"/PLAN=\"{_scriptPath}\" /JSON /OUT=\"{planPath}\"");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardError.Trim().Should().BeEmpty();
        result.StandardOutput.Should().Contain("Plan JSON written:");
        File.Exists(planPath).Should().BeTrue();

        using var json = JsonDocument.Parse(File.ReadAllText(planPath));
        var root = json.RootElement;
        root.GetProperty("ProductName").GetString().Should().Be("Contract App");
        root.GetProperty("ProductVersion").GetString().Should().Be("1.2.3");
        root.GetProperty("PlanHash").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("Operations").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void PlanCli_BlocksWhenDiscoveredExtensionValidatorReturnsError()
    {
        var blockedSource = Path.Combine(_root, "blocked-plan-source");
        Directory.CreateDirectory(blockedSource);
        File.WriteAllText(Path.Combine(blockedSource, "blocked.exe"), "blocked");
        var blockedProject = InstallerProjectFactory.CreateNew("Blocked App", "1.0.0", "ACME", blockedSource);
        blockedProject.Components.Clear();
        blockedProject.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true,
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = Path.Combine(blockedSource, "blocked.exe"),
                    DestinationPath = "blocked.exe"
                }
            }
        });
        var blockedScript = Path.Combine(_root, "blocked-plan.bsetup");
        var planPath = Path.Combine(_root, "blocked-plan.json");
        InstallerScriptSerializer.Save(blockedProject, blockedScript);

        var result = InstallerCliRunner.Run($"/PLAN=\"{blockedScript}\" /EXTENSIONS=\"{SampleExtensionDirectory()}\" /JSON /OUT=\"{planPath}\"");

        result.ExitCode.Should().Be(1, result.StandardError + result.StandardOutput);
        result.StandardError.Should().Contain("SAMPLE101");
        result.StandardError.Should().Contain("Sample extension validator blocked 'Blocked App'.");
        File.Exists(planPath).Should().BeFalse("extension validation must stop /PLAN before plan JSON is written");
    }

    [Fact]
    public void WinGetCli_AppliesFormatOverrideForMsixBundleSamples()
    {
        var output = Path.Combine(_root, "winget-msixbundle");

        var result = InstallerCliRunner.Run(
            $"/WINGET=\"{_scriptPath}\" /FORMAT=msixbundle /INSTALLERURL=https://downloads.example.test/ContractApp.msixbundle /SHA256={new string('c', 64)} /SIGNATURESHA256={new string('d', 64)} /OUT=\"{output}\" /PACKAGEID=Acme.ContractApp");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardError.Trim().Should().BeEmpty();

        var installerManifest = Directory.GetFiles(output, "*.installer.yaml", SearchOption.AllDirectories)
            .Should().ContainSingle().Subject;
        var yaml = File.ReadAllText(installerManifest);
        yaml.Should().Contain("InstallerType: msix");
        yaml.Should().Contain("InstallerUrl: https://downloads.example.test/ContractApp.msixbundle");
        yaml.Should().Contain("SignatureSha256: " + new string('D', 64));
    }

    [Fact]
    public void InstallJsonMode_WritesMachineEnvelopeToStdoutOnly()
    {
        var installPath = Path.Combine(_root, "install");

        var result = InstallerCliRunner.Run(
            $"/SCRIPT=\"{_scriptPath}\" /S /JSON /DRYRUN=true /NORESTART /D=\"{installPath}\" /PROPERTY:Tenant=acme");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardError.Trim().Should().BeEmpty();
        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;
        root.GetProperty("action").GetString().Should().Be("install");
        root.GetProperty("productName").GetString().Should().Be("Contract App");
        root.GetProperty("productVersion").GetString().Should().Be("1.2.3");
        root.GetProperty("installPath").GetString().Should().Be(installPath);
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("exitCode").GetInt32().Should().Be(0);
        root.GetProperty("rebootRequired").GetBoolean().Should().BeFalse();
        root.GetProperty("correlationId").GetString().Should().StartWith("bi-install-");
        root.GetProperty("journalPath").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RepairJsonMode_MissingInstallReturnsFailureEnvelopeAndHumanErrorOnStderr()
    {
        var installPath = Path.Combine(_root, "missing-install");

        var result = InstallerCliRunner.Run(
            $"/SCRIPT=\"{_scriptPath}\" /REPAIR /JSON /D=\"{installPath}\"");

        result.ExitCode.Should().Be(2);
        result.StandardError.Should().Contain("No installation of Contract App was found to repair.");
        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;
        root.GetProperty("action").GetString().Should().Be("repair");
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("exitCode").GetInt32().Should().Be(2);
        root.GetProperty("installPath").GetString().Should().Be(installPath);
        root.GetProperty("message").GetString().Should().Contain("No installation of Contract App");
        root.GetProperty("correlationId").GetString().Should().StartWith("bi-repair-");
    }

    [Theory]
    [InlineData("repair")]
    [InlineData("uninstall")]
    public void MaintenanceJsonMode_InvalidJournalReturnsFailureEnvelope(string action)
    {
        var installPath = Path.Combine(_root, "invalid-scope-install");
        Directory.CreateDirectory(installPath);
        var journalPath = Path.Combine(_root, "invalid-scope-journal.json");
        new Beep.Installer.Extensibility.ResourceExecutionJournalStore(journalPath).Save(new()
        {
            Metadata = new() { AppId = InstallerScriptSerializer.Load(_scriptPath).project!.AppId, ProductName = "Contract App", Publisher = "ACME", InstallScope = "invalid", InstallRoot = installPath }
        });
        var result = InstallerCliRunner.Run(
            $"/SCRIPT=\"{_scriptPath}\" /{action.ToUpperInvariant()} /JSON /D=\"{installPath}\" /JOURNAL=\"{journalPath}\"");
        result.ExitCode.Should().Be(2);
        result.StandardError.Should().Contain("invalid scope");
        using var json = JsonDocument.Parse(result.StandardOutput);
        json.RootElement.GetProperty("action").GetString().Should().Be(action);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("exitCode").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("message").GetString().Should().Contain("invalid scope");
        Directory.GetFiles(installPath).Should().BeEmpty();
    }

    [Fact]
    public void UninstallJsonMode_MissingJournalReturnsFailureEnvelope()
    {
        var installPath = Path.Combine(_root, "installed-without-journal");
        Directory.CreateDirectory(installPath);

        var result = InstallerCliRunner.Run(
            $"/SCRIPT=\"{_scriptPath}\" /UNINSTALL /JSON /D=\"{installPath}\"");

        result.ExitCode.Should().Be(1);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;
        root.GetProperty("action").GetString().Should().Be("uninstall");
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("exitCode").GetInt32().Should().Be(1);
        root.GetProperty("installPath").GetString().Should().Be(installPath);
        root.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("correlationId").GetString().Should().StartWith("bi-uninstall-");
    }

    [Fact]
    public void UpdateChannelFeedCli_ExportsAndVerifiesSignedFeed()
    {
        using var rsa = RSA.Create(2048);
        var privateKeyPath = Path.Combine(_root, "channels.private.pem");
        var publicKeyPath = Path.Combine(_root, "channels.public.pem");
        var feedOut = Path.Combine(_root, "feeds");
        File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());

        var export = InstallerCliRunner.Run(
            $"/UPDATECHANNELFEED=\"{_scriptPath}\" /UPDATECHANNELFEEDSIGNKEY=\"{privateKeyPath}\" /UPDATECHANNELFEEDISSUER=\"Release Engineering\" /OUT=\"{feedOut}\"");

        export.ExitCode.Should().Be(0, export.StandardError + export.StandardOutput);
        export.StandardOutput.Should().Contain("Update channel feed:");
        var feedPath = Path.Combine(feedOut, "beep-update-channels.json");
        var signaturePath = Path.Combine(feedOut, "beep-update-channels.json.sig");
        File.Exists(feedPath).Should().BeTrue();
        File.Exists(signaturePath).Should().BeTrue();

        var verify = InstallerCliRunner.Run(
            $"/VERIFYUPDATECHANNELFEED=\"{feedPath}\" /UPDATECHANNELFEEDTRUSTKEY=\"{publicKeyPath}\"");

        verify.ExitCode.Should().Be(0, verify.StandardError + verify.StandardOutput);
        verify.StandardOutput.Should().Contain("Update channel feed verified:");
        verify.StandardOutput.Should().Contain("Channels");
        var check = InstallerCliRunner.Run(
            $"--check-update-channel=\"{feedPath}\" /UPDATESTATE=\"{Path.Combine(_root, "update-state")}\" /UPDATEAPPID={InstallerScriptSerializer.Load(_scriptPath).project!.AppId} /UPDATEAPPNAME=\"Contract App\" /UPDATEPUBLISHER=ACME /UPDATECHANNELFEEDTRUSTKEY=\"{publicKeyPath}\" /UPDATECHANNELINSTALLEDVERSION=2.0.0 /UPDATECHANNELCURRENT=stable /UPDATECHANNELTARGET=stable /UPDATECHANNELCOHORT=private-device-seed /JSON");
        check.ExitCode.Should().Be(0, check.StandardError + check.StandardOutput);
        using var checkJson = JsonDocument.Parse(check.StandardOutput);
        checkJson.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        checkJson.RootElement.GetProperty("signatureTrusted").GetBoolean().Should().BeTrue();
        checkJson.RootElement.GetProperty("decision").GetProperty("targetChannelId").GetString().Should().Be("stable");
        check.StandardOutput.Should().NotContain("private-device-seed");
        var restrictedPolicy = Path.Combine(_root, "update-policy.json");
        File.WriteAllText(restrictedPolicy, """{"allowedUpdateChannels":["internal"]}""");
        var denied = InstallerCliRunner.Run(
            $"/CHECKUPDATECHANNEL=\"{feedPath}\" /UPDATESTATE=\"{Path.Combine(_root, "update-state")}\" /UPDATEAPPID={InstallerScriptSerializer.Load(_scriptPath).project!.AppId} /UPDATEAPPNAME=\"Contract App\" /UPDATEPUBLISHER=ACME /UPDATECHANNELFEEDTRUSTKEY=\"{publicKeyPath}\" /UPDATECHANNELINSTALLEDVERSION=2.0.0 /UPDATECHANNELTARGET=stable /MACHINEPOLICY=\"{restrictedPolicy}\" /JSON");
        denied.ExitCode.Should().Be(2, denied.StandardOutput + denied.StandardError);
        using var deniedJson = JsonDocument.Parse(denied.StandardOutput);
        deniedJson.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        deniedJson.RootElement.GetProperty("decision").ValueKind.Should().Be(JsonValueKind.Null);
        denied.StandardError.Should().Contain("BI8038");
        File.AppendAllText(feedPath, " ");
        var tampered = InstallerCliRunner.Run(
            $"/CHECKUPDATECHANNEL=\"{feedPath}\" /UPDATESTATE=\"{Path.Combine(_root, "update-state")}\" /UPDATEAPPID={InstallerScriptSerializer.Load(_scriptPath).project!.AppId} /UPDATEAPPNAME=\"Contract App\" /UPDATEPUBLISHER=ACME /UPDATECHANNELFEEDTRUSTKEY=\"{publicKeyPath}\" /UPDATECHANNELINSTALLEDVERSION=2.0.0 /JSON");
        tampered.ExitCode.Should().Be(1);
        using var tamperedJson = JsonDocument.Parse(tampered.StandardOutput);
        tamperedJson.RootElement.GetProperty("decision").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, true, true, true)]
    public void ApplyUpdateChannelCli_UsesTrustedHandoffAndReadOnlyPreview(bool dryRun, bool hold, bool remote = false, bool remoteFeed = false, bool reconnect = false)
    {
        using var rsa = RSA.Create(2048);
        var privateKey = Path.Combine(_root, "apply.private.pem");
        var publicKey = Path.Combine(_root, "apply.public.pem");
        File.WriteAllText(privateKey, rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(publicKey, rsa.ExportSubjectPublicKeyInfoPem());
        var current = Path.Combine(_root, "current");
        var target = Path.Combine(_root, "target");
        var stage = Path.Combine(_root, "stage");
        var delta = Path.Combine(_root, "delta");
        Directory.CreateDirectory(current);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(current, "app.txt"), "old");
        File.WriteAllText(Path.Combine(target, "app.txt"), "new");
        UpdateChannelFeedPackageServiceTests.WriteInstalledJournal(current, "ChannelCli", "Publisher", "1.0.0");
        UpdateChannelFeedPackageServiceTests.WriteInstalledJournal(target, "ChannelCli", "Publisher", "2.0.0");
        new DeltaUpdatePackageService().Build(new()
        {
            ExpectedAppId = "a34321a2-680b-43a8-af88-c56d6afab012", ExpectedProductName = "ChannelCli", ExpectedPublisher = "Publisher",
            BaseDirectory = current, UpdatedDirectory = target, OutputDirectory = delta,
            BaseVersion = "1.0.0", TargetVersion = "2.0.0", SigningPrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem()
        }).Success.Should().BeTrue();
        var project = InstallerProjectFactory.CreateNew("ChannelCli", "2.0.0", "Publisher", "");
        project.AppId = "a34321a2-680b-43a8-af88-c56d6afab012";
        using var server = remote ? new UpdateChannelFeedPackageServiceTests.DeltaHttpServer(delta, "remote") : null;
        project.AppUpdateChannel = "stable";
        project.UpdateChannels.Add(new() { Id = "stable", RolloutPercentage = hold ? 0 : 100 });
        var feed = UpdateChannelFeedPackageService.Export(new()
        {
            Project = project, OutputDirectory = Path.Combine(_root, "channel"), SigningPrivateKeyPath = privateKey,
            DeltaPackageDirectory = delta, DeltaPackageBaseUrl = server?.Url ?? ""
        });
        feed.Success.Should().BeTrue();
        using var feedServer = remoteFeed ? new UpdateChannelFeedPackageServiceTests.DeltaHttpServer(Path.GetDirectoryName(feed.FeedPath)!, "remote") : null;
        var feedSource = feedServer is null ? feed.FeedPath : feedServer.Url + UpdateChannelFeedPackageService.FeedFileName;
        var cache = Path.Combine(_root, "update-cache");
        var arguments = $"--apply-update-channel=\"{feedSource}\" /UPDATECACHE=\"{cache}\" /UPDATESTATE=\"{Path.Combine(_root, "update-state")}\" /UPDATEAPPNAME=ChannelCli /UPDATEPUBLISHER=Publisher {(remote ? "" : $"/DELTA=\"{delta}\"")} /DELTACURRENT=\"{current}\" /DELTASTAGE=\"{stage}\" /UPDATECHANNELINSTALLEDVERSION=1.0.0 /UPDATECHANNELFEEDTRUSTKEY=\"{publicKey}\" /DELTATRUSTKEY=\"{publicKey}\" /JSON {(dryRun ? "/DRYRUN" : "")}";
        arguments += " /UPDATEAPPID=" + project.AppId;
        if (reconnect)
        {
            server!.InterruptBlobs = true;
            var interrupted = InstallerCliRunner.Run(arguments);
            interrupted.ExitCode.Should().Be(1, interrupted.StandardOutput + interrupted.StandardError);
            File.ReadAllText(Path.Combine(current, "app.txt")).Should().Be("old");
            Directory.GetFiles(cache, "*.partial", SearchOption.AllDirectories).Should().NotBeEmpty();
            File.Exists(current + ".delta-journal.json").Should().BeFalse();
            server.InterruptBlobs = false;
        }
        var rangesBefore = server?.RangeRequestCount ?? 0;
        var result = InstallerCliRunner.Run(arguments);
        result.ExitCode.Should().Be(hold ? 2 : 0, result.StandardOutput + result.StandardError);
        if (feedServer is not null) feedServer.RequestCount.Should().Be(reconnect ? 4 : 2);
        if (reconnect) server!.RangeRequestCount.Should().BeGreaterThan(rangesBefore);
        using var json = JsonDocument.Parse(result.StandardOutput);
        json.RootElement.GetProperty("applied").GetBoolean().Should().Be(!dryRun && !hold);
        json.RootElement.GetProperty("previewOnly").GetBoolean().Should().Be(dryRun);
        File.ReadAllText(Path.Combine(current, "app.txt")).Should().Be(dryRun || hold ? "old" : "new");
        if (dryRun || hold)
        {
            if (server is not null) server.RequestCount.Should().Be(0);
            Directory.Exists(stage).Should().BeFalse();
            File.Exists(current + ".delta-journal.json").Should().BeFalse();
        }
        else
        {
            var rollback = InstallerCliRunner.Run($"/ROLLBACKDELTA=\"{current}.delta-journal.json\"");
            rollback.ExitCode.Should().Be(0, rollback.StandardError);
            File.ReadAllText(Path.Combine(current, "app.txt")).Should().Be("old");
        }
    }

    [Fact]
    public void UpdateChannelQualificationCli_WritesScenarioEvidenceReport()
    {
        using var rsa = RSA.Create(2048);
        var privateKeyPath = Path.Combine(_root, "qualification.private.pem");
        var publicKeyPath = Path.Combine(_root, "qualification.public.pem");
        var feedOut = Path.Combine(_root, "qualification-feeds");
        var evidenceOut = Path.Combine(_root, "qualification-evidence");
        File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());

        var export = InstallerCliRunner.Run(
            $"/UPDATECHANNELFEED=\"{_scriptPath}\" /UPDATECHANNELFEEDSIGNKEY=\"{privateKeyPath}\" /UPDATECHANNELFEEDISSUER=\"Release Engineering\" /OUT=\"{feedOut}\"");

        export.ExitCode.Should().Be(0, export.StandardError + export.StandardOutput);
        var feedPath = Path.Combine(feedOut, "beep-update-channels.json");

        var qualify = InstallerCliRunner.Run(
            $"/QUALIFYUPDATECHANNELFEED=\"{feedPath}\" /UPDATECHANNELFEEDTRUSTKEY=\"{publicKeyPath}\" /UPDATECHANNELCURRENT=stable /UPDATECHANNELTARGET=stable /UPDATECHANNELINSTALLEDVERSION=1.2.3 /UPDATECHANNELCOHORT=process-contract /UPDATECHANNELLIFECYCLE /UPDATECHANNELSCRIPT=\"{_scriptPath}\" /UPDATECHANNELUPDATEDSCRIPT=\"{_scriptPath}\" /UPDATECHANNELDOWNGRADESCRIPT=\"{_scriptPath}\" /UPDATECHANNELINSTALLDIR=\"{Path.Combine(_root, "qualified-install")}\" /DRYRUN /OUT=\"{evidenceOut}\"");

        qualify.ExitCode.Should().Be(0, qualify.StandardError + qualify.StandardOutput);
        qualify.StandardOutput.Should().Contain("Update channel qualification:");
        qualify.StandardOutput.Should().Contain("PASS verify-feed");
        qualify.StandardOutput.Should().Contain("PASS tampered-feed-failure");
        qualify.StandardOutput.Should().Contain("PASS offline-reconnect");
        qualify.StandardOutput.Should().Contain("PASS rollout-hold");
        qualify.StandardOutput.Should().Contain("PASS revoked-rollback");
        qualify.StandardOutput.Should().Contain("PASS install-current");
        qualify.StandardOutput.Should().Contain("PASS update-target");
        qualify.StandardOutput.Should().Contain("PASS downgrade-blocked");
        qualify.StandardOutput.Should().Contain("PASS uninstall-current");
        File.Exists(Path.Combine(evidenceOut, "update-channel-qualification.json")).Should().BeTrue();
        File.Exists(Path.Combine(evidenceOut, "install-current.command.json")).Should().BeTrue();
    }

    [Fact]
    public void DeltaCli_BuildsVerifiesAppliesAndRollsBackSignedDelta()
    {
        using var rsa = RSA.Create(2048);
        var privateKeyPath = Path.Combine(_root, "delta.private.pem");
        var publicKeyPath = Path.Combine(_root, "delta.public.pem");
        File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());

        var baseDir = Path.Combine(_root, "delta-base");
        var targetDir = Path.Combine(_root, "delta-target");
        var deltaOut = Path.Combine(_root, "delta-out");
        var stageDir = Path.Combine(_root, "delta-stage");
        var journalPath = Path.Combine(_root, "delta-journal.json");
        Write(baseDir, "App.exe", "old");
        Write(baseDir, "keep.txt", "same");
        Write(targetDir, "App.exe", "new");
        Write(targetDir, "keep.txt", "same");
        Write(targetDir, "added.txt", "added");
        // Attaching the delta to a channel feed (BI1576) requires its installed-image identity to
        // match the project it is published for, so both sides carry that project's journal.
        var contract = InstallerScriptSerializer.Load(_scriptPath).Item1!;
        UpdateChannelFeedPackageServiceTests.WriteInstalledJournal(
            baseDir, contract.AppName, contract.AppPublisher, "1.0.0", contract.AppId);
        UpdateChannelFeedPackageServiceTests.WriteInstalledJournal(
            targetDir, contract.AppName, contract.AppPublisher, "1.1.0", contract.AppId, "added.txt");

        var build = InstallerCliRunner.Run(
            $"/SCRIPT=\"{_scriptPath}\" /DELTA=\"{deltaOut}\" /DELTABASE=\"{baseDir}\" /DELTATARGET=\"{targetDir}\" /DELTABASEVERSION=1.0.0 /DELTATARGETVERSION=1.1.0 /DELTASIGNKEY=\"{privateKeyPath}\"");

        build.ExitCode.Should().Be(0, build.StandardError + build.StandardOutput);
        build.StandardOutput.Should().Contain("Delta package");
        var manifestPath = Path.Combine(deltaOut, "beep-delta-manifest.json");
        var signaturePath = Path.Combine(deltaOut, "beep-delta-manifest.json.sig");
        File.Exists(manifestPath).Should().BeTrue();
        File.Exists(signaturePath).Should().BeTrue();

        var verify = InstallerCliRunner.Run(
            $"/VERIFYDELTA=\"{deltaOut}\" /DELTATRUSTKEY=\"{publicKeyPath}\" /REQUIRESIGNED");

        verify.ExitCode.Should().Be(0, verify.StandardError + verify.StandardOutput);
        verify.StandardOutput.Should().Contain("Delta verified");
        verify.StandardOutput.Should().Contain("Signature     : trusted");

        var qualifyOut = Path.Combine(_root, "delta-qualification");
        var qualify = InstallerCliRunner.Run(
            $"/QUALIFYDELTA=\"{deltaOut}\" /DELTACURRENT=\"{baseDir}\" /DELTACURRENTVERSION=1.0.0 /DELTATRUSTKEY=\"{publicKeyPath}\" /REQUIRESIGNED /OUT=\"{qualifyOut}\"");

        qualify.ExitCode.Should().Be(0, qualify.StandardError + qualify.StandardOutput);
        qualify.StandardOutput.Should().Contain("Delta qualification");
        qualify.StandardOutput.Should().Contain("tampered-manifest-failure");
        qualify.StandardOutput.Should().Contain("missing-blob-failure");
        File.Exists(Path.Combine(qualifyOut, "delta-update-qualification.json")).Should().BeTrue();

        var apply = InstallerCliRunner.Run(
            $"/APPLYDELTA=\"{deltaOut}\" /DELTACURRENT=\"{baseDir}\" /DELTASTAGE=\"{stageDir}\" /DELTAJOURNAL=\"{journalPath}\" /DELTACURRENTVERSION=1.0.0 /DELTATRUSTKEY=\"{publicKeyPath}\" /REQUIRESIGNED");

        apply.ExitCode.Should().Be(0, apply.StandardError + apply.StandardOutput);
        apply.StandardOutput.Should().Contain("Delta applied");
        apply.StandardOutput.Should().Contain("Journal");
        File.ReadAllText(Path.Combine(baseDir, "App.exe")).Should().Be("new");
        File.ReadAllText(Path.Combine(baseDir, "keep.txt")).Should().Be("same");
        File.ReadAllText(Path.Combine(baseDir, "added.txt")).Should().Be("added");
        File.Exists(journalPath).Should().BeTrue();

        var rollback = InstallerCliRunner.Run($"/ROLLBACKDELTA=\"{journalPath}\"");
        rollback.ExitCode.Should().Be(0, rollback.StandardError + rollback.StandardOutput);
        rollback.StandardOutput.Should().Contain("Delta rolled back");
        File.ReadAllText(Path.Combine(baseDir, "App.exe")).Should().Be("old");
        File.ReadAllText(Path.Combine(baseDir, "keep.txt")).Should().Be("same");

        var feedOut = Path.Combine(_root, "delta-feed");
        var feed = InstallerCliRunner.Run(
            $"/UPDATECHANNELFEED=\"{_scriptPath}\" /UPDATECHANNELFEEDSIGNKEY=\"{privateKeyPath}\" /UPDATECHANNELFEEDTRUSTKEY=\"{publicKeyPath}\" /UPDATECHANNELTARGET=stable /DELTA=\"{deltaOut}\" /OUT=\"{feedOut}\"");

        feed.ExitCode.Should().Be(0, feed.StandardError + feed.StandardOutput);
        feed.StandardOutput.Should().Contain("Delta metadata     : attached");
        using var feedJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(feedOut, "beep-update-channels.json")));
        var deltaPackages = feedJson.RootElement.GetProperty("Channels")[0].GetProperty("DeltaPackages");
        deltaPackages.GetArrayLength().Should().Be(1);
        deltaPackages[0].GetProperty("BaseVersion").GetString().Should().Be("1.0.0");
        deltaPackages[0].GetProperty("TargetVersion").GetString().Should().Be("1.1.0");
    }

    [Fact]
    public void HelpCli_GroupsEnterpriseCommandsAndDocumentsReleasePresets()
    {
        var result = InstallerCliRunner.Run("/?");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardError.Trim().Should().BeEmpty();
        result.StandardOutput.Should().Contain("Builder, validation and planning:");
        result.StandardOutput.Should().Contain("SDK, extensions and unattended properties:");
        result.StandardOutput.Should().Contain("Catalogs, updates and offline layouts:");
        result.StandardOutput.Should().Contain("Qualification and release gates:");
        result.StandardOutput.Should().Contain("Enterprise package formats:");
        result.StandardOutput.Should().Contain("Security, signing, evidence and policy:");
        result.StandardOutput.Should().Contain("Runtime install, repair and uninstall:");
        result.StandardOutput.Should().Contain("/FORMATREADINESS=<script.bsetup>");
        result.StandardOutput.Should().Contain("/LISTTEMPLATES");
        result.StandardOutput.Should().Contain("/EXPORTTEMPLATEPACKAGE=<template-id>");
        result.StandardOutput.Should().Contain("/VERIFYTEMPLATEPACKAGE=<dir>");
        result.StandardOutput.Should().Contain("/EXTENSIONEXPORT=<script.bsetup>");
        result.StandardOutput.Should().Contain("/EXTENSIONKIND=provider|validator|exporter");
        result.StandardOutput.Should().Contain("/REQUIREDQUALIFICATIONS=id,id|core|enterprise|release");
        CountOccurrences(result.StandardOutput, "/EXTENSIONS=<dir[;dir]>").Should().Be(2);
    }

    [Fact]
    public void ExtensionTemplateCli_ExportsValidatorAndExporterTemplates()
    {
        var validatorOutput = Path.Combine(_root, "extension-validator-template");
        var exporterOutput = Path.Combine(_root, "extension-exporter-template");
        var exporterJsonPath = Path.Combine(_root, "extension-exporter-template.json");

        var validator = InstallerCliRunner.Run(
            $"/EXTENSIONTEMPLATE=\"{validatorOutput}\" /EXTENSIONKIND=validator /EXTENSIONID=beep.sample.validator /EXTENSIONVALIDATORTYPE=sample.validator /EXTENSIONPROJECT=Sample.Validator");
        var exporter = InstallerCliRunner.Run(
            $"/EXTENSIONTEMPLATE=\"{exporterOutput}\" /EXTENSIONKIND=exporter /EXTENSIONID=beep.sample.exporter /EXTENSIONEXPORTERFORMAT=sample-format /EXTENSIONPROJECT=Sample.Exporter /JSON /OUT=\"{exporterJsonPath}\"");

        validator.ExitCode.Should().Be(0, validator.StandardError + validator.StandardOutput);
        validator.StandardOutput.Should().Contain("Validator SDK template exported.");
        File.Exists(Path.Combine(validatorOutput, "src", "ProjectValidator.cs")).Should().BeTrue();
        using var validatorManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(validatorOutput, "beep-extension.template.json")));
        validatorManifest.RootElement.GetProperty("validatorTypes")[0].GetString().Should().Be("sample.validator");

        exporter.ExitCode.Should().Be(0, exporter.StandardError + exporter.StandardOutput);
        exporter.StandardError.Trim().Should().BeEmpty();
        using var exporterResult = JsonDocument.Parse(exporter.StandardOutput);
        exporterResult.RootElement.GetProperty("Kind").GetString().Should().Be("exporter");
        exporterResult.RootElement.GetProperty("ExtensionId").GetString().Should().Be("beep.sample.exporter");
        exporterResult.RootElement.GetProperty("ProjectName").GetString().Should().Be("Sample.Exporter");
        exporterResult.RootElement.GetProperty("ExporterFormats")[0].GetString().Should().Be("sample-format");
        exporterResult.RootElement.GetProperty("OutputDirectory").GetString().Should().Be(Path.GetFullPath(exporterOutput));
        exporterResult.RootElement.GetProperty("Files").EnumerateArray().Select(file => file.GetString()).Should().Contain("src/PackageExporter.cs");
        File.Exists(Path.Combine(exporterOutput, "src", "PackageExporter.cs")).Should().BeTrue();
        File.Exists(exporterJsonPath).Should().BeTrue();
        File.ReadAllText(exporterJsonPath).Should().Contain("src/PackageExporter.cs");
        using var exporterManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(exporterOutput, "beep-extension.template.json")));
        exporterManifest.RootElement.GetProperty("exporterFormats")[0].GetString().Should().Be("sample-format");
    }

    [Fact]
    public void ExtensionsCli_PrintsAllCapabilityFamilies()
    {
        var sampleExtension = SampleExtensionDirectory();

        var result = InstallerCliRunner.Run($"/EXTENSIONS=\"{sampleExtension}\"");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardOutput.Should().Contain("Extensions: 1");
        result.StandardOutput.Should().Contain("Resources : sample.resource");
        result.StandardOutput.Should().Contain("Validators: sample.validator");
        result.StandardOutput.Should().Contain("Exporters : sample-format");
        result.StandardOutput.Should().Contain("Providers :");
        result.StandardOutput.Should().Contain("Validator types: Beep.Installer.Samples.Extensions.SampleProvider.SampleProjectValidator");
        result.StandardOutput.Should().Contain("Exporter types : Beep.Installer.Samples.Extensions.SampleProvider.SamplePackageExporter");
    }

    [Fact]
    public void ExtensionsJsonCli_WritesConformanceReportContract()
    {
        var sampleExtension = SampleExtensionDirectory();
        var outputPath = Path.Combine(_root, "extension-discovery.json");

        var result = InstallerCliRunner.Run($"/EXTENSIONS=\"{sampleExtension}\" /JSON /OUT=\"{outputPath}\"");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardError.Trim().Should().BeEmpty();
        using var json = JsonDocument.Parse(result.StandardOutput);
        json.RootElement.GetProperty("schemaVersion").GetString().Should().Be("1.0");
        json.RootElement.GetProperty("extensionCount").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("providerCount").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("validatorCount").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("exporterCount").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("extensions")[0].GetProperty("declaredResourceTypes")[0].GetString().Should().Be("sample.resource");
        json.RootElement.GetProperty("extensions")[0].GetProperty("declaredValidatorTypes")[0].GetString().Should().Be("sample.validator");
        json.RootElement.GetProperty("extensions")[0].GetProperty("declaredExporterFormats")[0].GetString().Should().Be("sample-format");
        File.Exists(outputPath).Should().BeTrue();
        File.ReadAllText(outputPath).Should().Contain("\"providerCount\"");
    }

    [Fact]
    public void ValidateCli_RunsDiscoveredExtensionProjectValidators()
    {
        var sampleExtension = SampleExtensionDirectory();

        var result = InstallerCliRunner.Run($"/VALIDATE=\"{_scriptPath}\" /EXTENSIONS=\"{sampleExtension}\"");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardOutput.Should().Contain("1 extension diagnostic(s):");
        result.StandardOutput.Should().Contain("SAMPLE100");
        result.StandardOutput.Should().Contain("Sample extension validator inspected 'Contract App'.");
    }

    [Fact]
    public void BuildCli_BlocksWhenDiscoveredExtensionValidatorReturnsError()
    {
        var blockedSource = Path.Combine(_root, "blocked-source");
        Directory.CreateDirectory(blockedSource);
        File.WriteAllText(Path.Combine(blockedSource, "blocked.exe"), "blocked");
        var blockedProject = InstallerProjectFactory.CreateNew("Blocked App", "1.0.0", "ACME", blockedSource);
        blockedProject.Components.Clear();
        blockedProject.Components.Add(new InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true,
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = Path.Combine(blockedSource, "blocked.exe"),
                    DestinationPath = "blocked.exe"
                }
            }
        });
        var blockedScript = Path.Combine(_root, "blocked.bsetup");
        var blockedOutput = Path.Combine(_root, "blocked-output");
        InstallerScriptSerializer.Save(blockedProject, blockedScript);

        var result = InstallerCliRunner.Run($"/BUILD=\"{blockedScript}\" /EXTENSIONS=\"{SampleExtensionDirectory()}\" /OUT=\"{blockedOutput}\"");

        result.ExitCode.Should().Be(1, result.StandardError + result.StandardOutput);
        result.StandardOutput.Should().Contain("1 extension diagnostic(s):");
        result.StandardOutput.Should().Contain("SAMPLE101");
        result.StandardOutput.Should().Contain("Sample extension validator blocked 'Blocked App'.");
        Directory.Exists(blockedOutput).Should().BeFalse("extension validation must stop /BUILD before artifact generation");
    }

    [Fact]
    public void ExtensionExportJsonCli_InvokesDiscoveredPackageExporter()
    {
        var sampleExtension = SampleExtensionDirectory();
        var outputDirectory = Path.Combine(_root, "sample-extension-export");

        var result = InstallerCliRunner.Run($"/EXTENSIONEXPORT=\"{_scriptPath}\" /EXTENSIONS=\"{sampleExtension}\" /FORMAT=sample-format /OUT=\"{outputDirectory}\" /JSON");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardError.Trim().Should().BeEmpty();
        using var json = JsonDocument.Parse(result.StandardOutput);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("format").GetString().Should().Be("sample-format");
        json.RootElement.GetProperty("extensionId").GetString().Should().Be("beep.sample.provider");
        json.RootElement.GetProperty("exporterType").GetString().Should().Be("Beep.Installer.Samples.Extensions.SampleProvider.SamplePackageExporter");

        var artifact = Path.Combine(outputDirectory, "Contract App.sample-format.txt");
        File.Exists(artifact).Should().BeTrue();
        File.ReadAllText(artifact).Should().Contain("Product: Contract App");
        json.RootElement.GetProperty("artifacts")
            .EnumerateArray()
            .Select(item => item.GetString())
            .Should()
            .Contain(Path.GetFullPath(artifact));
    }

    [Fact]
    public void ExtensionsJsonCli_ReturnsMachineDiagnosticsWhenPolicyCannotLoad()
    {
        var sampleExtension = SampleExtensionDirectory();
        var outputPath = Path.Combine(_root, "extension-policy-error.json");
        var missingPolicy = Path.Combine(_root, "missing-policy.json");

        var result = InstallerCliRunner.Run($"/EXTENSIONS=\"{sampleExtension}\" /POLICY=\"{missingPolicy}\" /JSON /OUT=\"{outputPath}\"");

        result.ExitCode.Should().Be(1, result.StandardError + result.StandardOutput);
        result.StandardError.Trim().Should().BeEmpty();
        using var json = JsonDocument.Parse(result.StandardOutput);
        json.RootElement.GetProperty("hasErrors").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("extensionCount").GetInt32().Should().Be(0);
        json.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString().Should().Be("BI8016");
        File.Exists(outputPath).Should().BeTrue();
        File.ReadAllText(outputPath).Should().Contain("\"BI8016\"");
    }

    [Fact]
    public void ListTemplatesJsonCli_ListsBuiltInTemplateIds()
    {
        var outputPath = Path.Combine(_root, "template-catalog.json");

        var result = InstallerCliRunner.Run($"/LISTTEMPLATES /JSON /OUT=\"{outputPath}\"");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardError.Trim().Should().BeEmpty();
        using var json = JsonDocument.Parse(result.StandardOutput);
        var ids = json.RootElement.GetProperty("Templates")
            .EnumerateArray()
            .Select(template => template.GetProperty("Id").GetString())
            .ToArray();

        ids.Should().Contain(new[] { "empty", "console", "winforms", "wpf", "service" });
        File.Exists(outputPath).Should().BeTrue();
        File.ReadAllText(outputPath).Should().Contain("\"Templates\"");
    }

    [Fact]
    public void FormatReadinessJsonCli_WritesMachineReportToStdoutAndOutFile()
    {
        var outputPath = Path.Combine(_root, "format-readiness", "report.json");

        var result = InstallerCliRunner.Run($"/FORMATREADINESS=\"{_scriptPath}\" /JSON /OUT=\"{outputPath}\"");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardError.Trim().Should().BeEmpty();
        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;
        root.GetProperty("PlanHash").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("ReleaseReadinessStatus").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("Formats").GetArrayLength().Should().BeGreaterThan(3);
        File.Exists(outputPath).Should().BeTrue();
        File.ReadAllText(outputPath).Should().Contain("ReleaseReadinessStatus");
    }

    [Fact]
    public void TemplatePackageCli_ExportsAndVerifiesSignedPackage()
    {
        using var rsa = RSA.Create(2048);
        var privateKeyPath = Path.Combine(_root, "template.private.pem");
        var publicKeyPath = Path.Combine(_root, "template.public.pem");
        File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(publicKeyPath, rsa.ExportSubjectPublicKeyInfoPem());
        var output = Path.Combine(_root, "template-package");

        var export = InstallerCliRunner.Run(
            $"/EXPORTTEMPLATEPACKAGE=winforms /TEMPLATESIGNKEY=\"{privateKeyPath}\" /TEMPLATEPRODUCT=\"Reusable App\" /TEMPLATEVERSION=2.0.0 /TEMPLATEPUBLISHER=ACME /TEMPLATESOURCEDIR=\"{_sourceRoot}\" /TEMPLATEISSUER=\"ACME Installer Platform\" /JSON /OUT=\"{output}\"");

        export.ExitCode.Should().Be(0, export.StandardError + export.StandardOutput);
        export.StandardError.Trim().Should().BeEmpty();
        using var exportJson = JsonDocument.Parse(export.StandardOutput);
        exportJson.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
        exportJson.RootElement.GetProperty("TemplateId").GetString().Should().Be("winforms");
        File.Exists(Path.Combine(output, "beep-project-template.json")).Should().BeTrue();
        File.Exists(Path.Combine(output, "template.project.canonical.json")).Should().BeTrue();
        File.Exists(Path.Combine(output, "beep-project-template.json.sig")).Should().BeTrue();

        var verify = InstallerCliRunner.Run(
            $"/VERIFYTEMPLATEPACKAGE=\"{output}\" /TEMPLATETRUSTKEY=\"{publicKeyPath}\" /JSON");

        verify.ExitCode.Should().Be(0, verify.StandardError + verify.StandardOutput);
        verify.StandardError.Trim().Should().BeEmpty();
        using var verifyJson = JsonDocument.Parse(verify.StandardOutput);
        verifyJson.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
        verifyJson.RootElement.GetProperty("SignatureTrusted").GetBoolean().Should().BeTrue();
        verifyJson.RootElement.GetProperty("Manifest").GetProperty("Issuer").GetString().Should().Be("ACME Installer Platform");
    }

    [Fact]
    public void ReleasePortfolioJsonCli_WritesMachineReportToStdoutOnly()
    {
        var evidence = Path.Combine(_root, "release-evidence");
        WriteQualificationReport(Path.Combine(evidence, "plan"), "compiled-plan-qualification.json", success: true);
        WriteQualificationReport(Path.Combine(evidence, "cli"), "enterprise-cli-qualification.json", success: true);
        WriteQualificationReport(Path.Combine(evidence, "release"), "release-evidence-qualification.json", success: true);
        WriteQualificationReport(Path.Combine(evidence, "security"), "supply-chain-qualification.json", success: true);
        var output = Path.Combine(_root, "release-portfolio");

        var result = InstallerCliRunner.Run($"/QUALIFYRELEASE=\"{evidence}\" /REQUIREDQUALIFICATIONS=core /JSON /OUT=\"{output}\"");

        result.ExitCode.Should().Be(0, result.StandardError + result.StandardOutput);
        result.StandardError.Trim().Should().BeEmpty();
        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;
        root.GetProperty("Success").GetBoolean().Should().BeTrue();
        root.GetProperty("RequestedQualificationsInput")[0].GetString().Should().Be("core");
        root.GetProperty("Summary").GetProperty("PassedCount").GetInt32().Should().Be(4);
        root.GetProperty("GapPlanPath").GetString().Should().EndWith("release-evidence-gap-plan.md");
        File.Exists(Path.Combine(output, "release-qualification-portfolio.json")).Should().BeTrue();
        File.Exists(Path.Combine(output, "release-evidence-gap-manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(output, "release-evidence-gap-plan.md")).Should().BeTrue();
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void WriteQualificationReport(string directory, string fileName, bool success)
    {
        Directory.CreateDirectory(directory);
        var payload = new
        {
            StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedUtc = DateTimeOffset.UtcNow,
            Success = success,
            ExitCode = success ? 0 : 1,
            Message = success ? "passed" : "failed"
        };
        File.WriteAllText(Path.Combine(directory, fileName), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static string SampleExtensionDirectory()
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Beep.Installer",
            "samples",
            "Extensions",
            "SampleProvider"));

}
