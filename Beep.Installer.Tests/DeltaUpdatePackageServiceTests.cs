using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beep.Installer.Engine.Updates;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class DeltaUpdatePackageServiceTests : IDisposable
{
    [Theory]
    [InlineData("valid")]
    [InlineData("missing-base")]
    [InlineData("missing-target")]
    [InlineData("wrong-publisher")]
    [InlineData("wrong-version")]
    [InlineData("incomplete-pin")]
    [InlineData("resource-change")]
    [InlineData("layout-change")]
    [InlineData("wrong-app-id")]
    [InlineData("missing-app-id")]
    [InlineData("target-app-id")]
    public void Build_ValidatesInstallationImagesBeforeWriting(string scenario)
    {
        var current = Path.Combine(_tempDir, "current");
        var target = Path.Combine(_tempDir, "target");
        var delta = Path.Combine(_tempDir, "delta");
        Write(current, "app.txt", "old");
        Write(target, "app.txt", "new");
        Write(delta, DeltaUpdatePackageService.ManifestFileName, "existing publication");
        if (scenario != "missing-base") UpdateChannelFeedPackageServiceTests.WriteInstalledJournal(current, "App", "Publisher", "1.0");
        if (scenario != "missing-target") UpdateChannelFeedPackageServiceTests.WriteInstalledJournal(target, "App",
            scenario == "wrong-publisher" ? "Other" : "Publisher", scenario == "wrong-version" ? "3.0" : "2.0");
        if (scenario == "layout-change") Write(target, "extra.txt", "requires new ownership");
        if (scenario == "target-app-id")
        {
            var journalPath = Beep.Installer.Extensibility.ResourceExecutionJournalStore.DefaultPath(target, "a34321a2-680b-43a8-af88-c56d6afab012");
            var journal = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(journalPath))!;
            journal["metadata"]!["appId"] = Guid.NewGuid().ToString("D");
            File.WriteAllText(journalPath, journal.ToJsonString());
        }
        if (scenario == "resource-change")
        {
            var store = new Beep.Installer.Extensibility.ResourceExecutionJournalStore(
                Beep.Installer.Extensibility.ResourceExecutionJournalStore.DefaultPath(target, "a34321a2-680b-43a8-af88-c56d6afab012"));
            var journal = store.Load();
            journal.Entries.Add(new() { Action = Beep.Installer.Extensibility.ResourceExecutionAction.Apply,
                OperationId = "changed", Operation = new() { Id = "changed", Type = "service", RollbackSupported = false } });
            store.Save(journal);
        }
        var result = new DeltaUpdatePackageService().Build(new()
        {
            BaseDirectory = current, UpdatedDirectory = target, OutputDirectory = delta,
            BaseVersion = "1.0", TargetVersion = "2.0", ExpectedProductName = "App",
            ExpectedAppId = scenario == "wrong-app-id" ? Guid.NewGuid().ToString("D")
                : scenario == "missing-app-id" ? "" : "a34321a2-680b-43a8-af88-c56d6afab012",
            ExpectedPublisher = scenario == "incomplete-pin" ? "" : "Publisher"
        });
        result.Success.Should().Be(scenario == "valid", result.Error);
        if (scenario != "valid")
        {
            File.ReadAllText(Path.Combine(delta, DeltaUpdatePackageService.ManifestFileName)).Should().Be("existing publication");
            Directory.EnumerateFileSystemEntries(delta).Should().ContainSingle();
        }
    }

    [Theory]
    // Target-side variations (wrong publisher, wrong version, missing or foreign journal) are
    // refused at build time and are covered by Build_ValidatesInstallationImagesBeforeWriting.
    // What only Apply can catch is the installation drifting after the delta was signed, so these
    // scenarios move the *current* install off the identity the delta was published against.
    [InlineData("current-publisher")]
    [InlineData("current-version")]
    [InlineData("valid")]
    public void ApplyAtomically_BindsBothInstalledAndStagedIdentity(string scenario)
    {
        var current = Path.Combine(_tempDir, "current");
        var target = Path.Combine(_tempDir, "target");
        var delta = Path.Combine(_tempDir, "delta");
        Write(current, "app.txt", "old");
        Write(target, "app.txt", "new");
        UpdateChannelFeedPackageServiceTests.WriteInstalledJournal(current, "App", "Publisher", "1.0");
        UpdateChannelFeedPackageServiceTests.WriteInstalledJournal(target, "App", "Publisher", "2.0");
        var service = new DeltaUpdatePackageService();
        // Expectations here are what make the package carry an installed-image contract at all;
        // without one Apply has nothing to bind the live installation against.
        service.Build(new() { BaseDirectory = current, UpdatedDirectory = target, OutputDirectory = delta,
            BaseVersion = "1.0", TargetVersion = "2.0",
            ExpectedAppId = UpdateChannelFeedPackageServiceTests.FixtureAppId,
            ExpectedProductName = "App", ExpectedPublisher = "Publisher" }).Success.Should().BeTrue();

        if (scenario != "valid")
            UpdateChannelFeedPackageServiceTests.WriteInstalledJournal(current, "App",
                scenario == "current-publisher" ? "Other" : "Publisher",
                scenario == "current-version" ? "9.0" : "1.0");

        var applied = service.ApplyAtomically(new() { DeltaDirectory = delta, CurrentInstallDirectory = current,
            StageDirectory = Path.Combine(_tempDir, "stage"), CurrentVersion = "1.0",
            ExpectedAppId = UpdateChannelFeedPackageServiceTests.FixtureAppId,
            ExpectedProductName = "App", ExpectedPublisher = "Publisher" });
        applied.Success.Should().Be(scenario == "valid", applied.Error);
        File.ReadAllText(Path.Combine(current, "app.txt")).Should().Be(scenario == "valid" ? "new" : "old");
        if (scenario != "valid")
        {
            Directory.Exists(current + ".bak1").Should().BeFalse();
            File.Exists(current + ".delta-journal.json").Should().BeFalse();
        }
    }

    private readonly string _tempDir;

    public DeltaUpdatePackageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BeepDelta_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("same")]
    [InlineData("parent")]
    [InlineData("child")]
    [InlineData("package")]
    [InlineData("occupied")]
    public void ApplyToStage_RejectsUnsafeTargetsWithoutDeletingFiles(string scenario)
    {
        var current = Path.Combine(_tempDir, "current");
        var target = Path.Combine(_tempDir, "target");
        var delta = Path.Combine(_tempDir, "delta");
        Write(current, "app.txt", "old");
        Write(target, "app.txt", "new");
        var service = new DeltaUpdatePackageService();
        service.Build(new() { BaseDirectory = current, UpdatedDirectory = target, OutputDirectory = delta }).Success.Should().BeTrue();
        var stage = scenario switch
        {
            "same" => current, "parent" => _tempDir, "child" => Path.Combine(current, "stage"),
            "package" => delta, _ => Path.Combine(_tempDir, "occupied")
        };
        if (scenario == "occupied") Write(stage, "user.txt", "preserve");
        var result = service.ApplyToStage(new() { CurrentInstallDirectory = current, StageDirectory = stage, DeltaDirectory = delta });
        result.Success.Should().BeFalse();
        File.ReadAllText(Path.Combine(current, "app.txt")).Should().Be("old");
        File.ReadAllText(Path.Combine(target, "app.txt")).Should().Be("new");
        File.Exists(Path.Combine(delta, DeltaUpdatePackageService.ManifestFileName)).Should().BeTrue();
        if (scenario == "occupied") File.ReadAllText(Path.Combine(stage, "user.txt")).Should().Be("preserve");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyAtomically_RejectsJournalAndBackupOverlapBeforeStaging(bool backup)
    {
        var current = Path.Combine(_tempDir, "current");
        Write(current, "app.txt", "old");
        var stage = backup ? current + ".bak1" : Path.Combine(_tempDir, "stage");
        var result = new DeltaUpdatePackageService().ApplyAtomically(new()
        {
            CurrentInstallDirectory = current, StageDirectory = stage, DeltaDirectory = Path.Combine(_tempDir, "delta"),
            JournalPath = backup ? "" : Path.Combine(current, "app.txt")
        });
        result.Success.Should().BeFalse();
        Directory.Exists(stage).Should().BeFalse();
        File.ReadAllText(Path.Combine(current, "app.txt")).Should().Be("old");
    }

    [Theory]
    [InlineData("before")]
    [InlineData("staging")]
    [InlineData("ready-to-commit")]
    public void ApplyAtomically_CancellationBeforeCommitPreservesInstallation(string phase)
    {
        var current = Path.Combine(_tempDir, "current");
        var target = Path.Combine(_tempDir, "target");
        var delta = Path.Combine(_tempDir, "delta");
        var stage = Path.Combine(_tempDir, "stage");
        Write(current, "app.txt", "old");
        Write(target, "app.txt", "new");
        var service = new DeltaUpdatePackageService();
        service.Build(new() { BaseDirectory = current, UpdatedDirectory = target, OutputDirectory = delta }).Success.Should().BeTrue();
        using var cancellation = new CancellationTokenSource();
        if (phase == "before") cancellation.Cancel();
        Action apply = () => service.ApplyAtomically(new()
        {
            CurrentInstallDirectory = current, DeltaDirectory = delta, StageDirectory = stage,
            CancellationToken = cancellation.Token, Progress = new CallbackProgress(value => { if (value == phase) cancellation.Cancel(); })
        });
        apply.Should().Throw<OperationCanceledException>();
        File.ReadAllText(Path.Combine(current, "app.txt")).Should().Be("old");
        File.Exists(current + ".delta-journal.json").Should().BeFalse();
        Directory.Exists(current + ".bak1").Should().BeFalse();
        Directory.Exists(stage).Should().Be(phase == "ready-to-commit");
        if (phase == "ready-to-commit") File.ReadAllText(Path.Combine(stage, "app.txt")).Should().Be("new");
    }

    private sealed class CallbackProgress(Action<string> callback) : IProgress<string>
    {
        public void Report(string value) => callback(value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyAtomically_RejectsOverlappingOperationAndReleasesLease(bool otherThread)
    {
        var current = Path.Combine(_tempDir, "current");
        var target = Path.Combine(_tempDir, "target");
        var delta = Path.Combine(_tempDir, "delta");
        var otherStage = Path.Combine(_tempDir, "other-stage");
        Write(current, "app.txt", "old");
        Write(target, "app.txt", "new");
        var service = new DeltaUpdatePackageService();
        service.Build(new() { BaseDirectory = current, UpdatedDirectory = target, OutputDirectory = delta }).Success.Should().BeTrue();
        var attempted = false;
        var result = service.ApplyAtomically(new()
        {
            CurrentInstallDirectory = current, DeltaDirectory = delta, StageDirectory = Path.Combine(_tempDir, "stage"),
            Progress = new CallbackProgress(phase =>
            {
                if (phase != "ready-to-commit") return;
                attempted = true;
                Exception? Competing() => Record.Exception(() => service.ApplyAtomically(new()
                {
                    CurrentInstallDirectory = current, DeltaDirectory = delta, StageDirectory = otherStage
                }));
                var failure = otherThread ? Task.Run(Competing).GetAwaiter().GetResult() : Competing();
                failure.Should().BeOfType<IOException>().Which.Message.Should().Contain("already active");
                Directory.Exists(otherStage).Should().BeFalse();
            })
        });
        attempted.Should().BeTrue();
        result.Success.Should().BeTrue(result.Error);
        service.RollbackAtomicApply(new() { JournalPath = result.JournalPath }).Success.Should().BeTrue();
    }

    [Fact]
    public void ApplyAtomically_PreservesUnfinishedJournalBeforeStaging()
    {
        var current = Path.Combine(_tempDir, "current");
        Write(current, "app.txt", "old");
        var journal = current + ".delta-journal.json";
        File.WriteAllText(journal, """{"schemaVersion":"1.0","state":"prepared"}""");
        var before = File.ReadAllText(journal);
        var stage = Path.Combine(_tempDir, "stage");
        var result = new DeltaUpdatePackageService().ApplyAtomically(new()
        {
            CurrentInstallDirectory = current, StageDirectory = stage, DeltaDirectory = Path.Combine(_tempDir, "delta")
        });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("explicit recovery");
        File.ReadAllText(journal).Should().Be(before);
        Directory.Exists(stage).Should().BeFalse();
    }

    [Theory]
    [InlineData("backup")]
    [InlineData("current")]
    [InlineData("path")]
    [InlineData("json")]
    [InlineData("state")]
    public void RollbackAtomicApply_RejectsAlteredRecoveryInputsBeforeMoving(string altered)
    {
        var current = Path.Combine(_tempDir, "current");
        var target = Path.Combine(_tempDir, "target");
        var delta = Path.Combine(_tempDir, "delta");
        Write(current, "app.txt", "old");
        Write(target, "app.txt", "new");
        var service = new DeltaUpdatePackageService();
        service.Build(new() { BaseDirectory = current, UpdatedDirectory = target, OutputDirectory = delta }).Success.Should().BeTrue();
        var applied = service.ApplyAtomically(new()
        {
            CurrentInstallDirectory = current, StageDirectory = Path.Combine(_tempDir, "stage"), DeltaDirectory = delta
        });
        applied.Success.Should().BeTrue(applied.Error);
        if (altered == "backup") Write(current + ".bak1", "app.txt", "changed backup");
        if (altered == "current") Write(current, "user.txt", "user content");
        if (altered == "json") File.WriteAllText(applied.JournalPath, "invalid");
        if (altered is "path" or "state")
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(applied.JournalPath))!;
            if (altered == "path") node["backupDirectory"] = Path.Combine(_tempDir, "unrelated");
            else node["state"] = null;
            File.WriteAllText(applied.JournalPath, node.ToJsonString());
        }
        var journalBefore = File.ReadAllText(applied.JournalPath);
        var rollback = service.RollbackAtomicApply(new() { JournalPath = applied.JournalPath });
        rollback.Success.Should().BeFalse();
        File.ReadAllText(Path.Combine(current, "app.txt")).Should().Be("new");
        File.ReadAllText(Path.Combine(current + ".bak1", "app.txt")).Should().Be(altered == "backup" ? "changed backup" : "old");
        if (altered == "current") File.ReadAllText(Path.Combine(current, "user.txt")).Should().Be("user content");
        File.ReadAllText(applied.JournalPath).Should().Be(journalBefore);
        Directory.Exists(current + ".swap").Should().BeFalse();
    }

    [Theory]
    [InlineData("promoted")]
    [InlineData("missing")]
    [InlineData("changed")]
    public void RecoverAtomicApply_UsesVerifiedTreesAndSupportsReadOnlyPreview(string interrupted)
    {
        var current = Path.Combine(_tempDir, "current");
        var target = Path.Combine(_tempDir, "target");
        var delta = Path.Combine(_tempDir, "delta");
        var stage = Path.Combine(_tempDir, "stage");
        Write(current, "app.txt", "old");
        Write(target, "app.txt", "new");
        var service = new DeltaUpdatePackageService();
        service.Build(new() { BaseDirectory = current, UpdatedDirectory = target, OutputDirectory = delta }).Success.Should().BeTrue();
        var applied = service.ApplyAtomically(new() { CurrentInstallDirectory = current, StageDirectory = stage, DeltaDirectory = delta });
        applied.Success.Should().BeTrue();
        var journal = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(applied.JournalPath))!;
        journal["state"] = "prepared";
        File.WriteAllText(applied.JournalPath, journal.ToJsonString());
        if (interrupted == "missing") Directory.Move(current, stage);
        if (interrupted == "changed") Write(current, "user.txt", "preserve");
        var before = File.ReadAllText(applied.JournalPath);
        var preview = service.RecoverAtomicApply(new() { JournalPath = applied.JournalPath }, dryRun: true);
        preview.Success.Should().Be(interrupted != "changed");
        File.ReadAllText(applied.JournalPath).Should().Be(before);
        Directory.Exists(current).Should().Be(interrupted != "missing");
        var command = InstallerCliRunner.Run($"--recover-delta=\"{applied.JournalPath}\" /JSON");
        command.ExitCode.Should().Be(interrupted == "changed" ? 1 : 0, command.StandardError);
        using var output = JsonDocument.Parse(command.StandardOutput);
        var result = output.RootElement.GetProperty("result").Deserialize<DeltaUpdatePackageResult>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        result.Success.Should().Be(interrupted != "changed", result.Error);
        if (interrupted == "changed")
        {
            File.ReadAllText(Path.Combine(current, "user.txt")).Should().Be("preserve");
            File.ReadAllText(applied.JournalPath).Should().Be(before);
        }
        else
        {
            File.ReadAllText(Path.Combine(current, "app.txt")).Should().Be(interrupted == "missing" ? "old" : "new");
            result.RecoveryState.Should().Be(interrupted == "missing" ? "recovered" : "committed");
            service.RecoverAtomicApply(new() { JournalPath = applied.JournalPath }).Success.Should().BeTrue();
        }
    }

    [Fact]
    public void Build_WritesContentAddressedDeltaForChangedFilesOnly()
    {
        var baseDir = Path.Combine(_tempDir, "base");
        var updatedDir = Path.Combine(_tempDir, "updated");
        var outDir = Path.Combine(_tempDir, "delta");
        Write(baseDir, "App.exe", "same");
        Write(baseDir, "lib.dll", "old");
        Write(baseDir, "remove.txt", "remove");
        Write(updatedDir, "App.exe", "same");
        Write(updatedDir, "lib.dll", "new");
        Write(updatedDir, "add.txt", "add");

        var result = new DeltaUpdatePackageService().Build(new DeltaUpdateBuildOptions
        {
            BaseDirectory = baseDir,
            UpdatedDirectory = updatedDir,
            OutputDirectory = outDir,
            BaseVersion = "1.0.0",
            TargetVersion = "1.1.0"
        });

        result.Success.Should().BeTrue(result.Error);
        result.AddedFiles.Should().Be(1);
        result.UpdatedFiles.Should().Be(1);
        result.RemovedFiles.Should().Be(1);
        result.UnchangedFiles.Should().Be(1);
        result.DeltaBytes.Should().BeLessThan(result.TargetBytes);

        using var manifest = JsonDocument.Parse(File.ReadAllText(result.ManifestPath));
        manifest.RootElement.GetProperty("baseVersion").GetString().Should().Be("1.0.0");
        manifest.RootElement.GetProperty("targetVersion").GetString().Should().Be("1.1.0");
        var files = manifest.RootElement.GetProperty("files").EnumerateArray().ToList();
        files.Should().Contain(file => file.GetProperty("path").GetString() == "lib.dll"
                                      && file.GetProperty("action").GetString() == "update"
                                      && file.GetProperty("blobPath").GetString()!.StartsWith("blobs/sha256/", StringComparison.Ordinal));
        files.Should().Contain(file => file.GetProperty("path").GetString() == "remove.txt"
                                      && file.GetProperty("action").GetString() == "remove");
        Directory.EnumerateFiles(Path.Combine(outDir, "blobs"), "*", SearchOption.AllDirectories)
            .Should().HaveCount(2);
    }

    [Fact]
    public void ApplyToStage_RequiresExactBaseTreeBeforeApplying()
    {
        var baseDir = Path.Combine(_tempDir, "base");
        var updatedDir = Path.Combine(_tempDir, "updated");
        var outDir = Path.Combine(_tempDir, "delta");
        var installDir = Path.Combine(_tempDir, "install");
        var stageDir = Path.Combine(_tempDir, "stage");
        Write(baseDir, "App.exe", "old");
        Write(updatedDir, "App.exe", "new");
        Write(installDir, "App.exe", "tampered");

        new DeltaUpdatePackageService().Build(new DeltaUpdateBuildOptions
        {
            BaseDirectory = baseDir,
            UpdatedDirectory = updatedDir,
            OutputDirectory = outDir,
            BaseVersion = "1.0.0",
            TargetVersion = "1.1.0"
        });

        var result = new DeltaUpdatePackageService().ApplyToStage(new DeltaUpdateApplyOptions
        {
            DeltaDirectory = outDir,
            CurrentInstallDirectory = installDir,
            StageDirectory = stageDir,
            CurrentVersion = "1.0.0"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("does not match delta base tree hash");
        Directory.Exists(stageDir).Should().BeFalse();
    }

    [Fact]
    public void SignedDelta_VerifiesAndAppliesToStage()
    {
        using var rsa = RSA.Create(2048);
        var privateKey = rsa.ExportRSAPrivateKeyPem();
        var publicKey = rsa.ExportRSAPublicKeyPem();
        var baseDir = Path.Combine(_tempDir, "base");
        var updatedDir = Path.Combine(_tempDir, "updated");
        var outDir = Path.Combine(_tempDir, "delta");
        var stageDir = Path.Combine(_tempDir, "stage");
        Write(baseDir, "App.exe", "old");
        Write(baseDir, "keep.txt", "keep");
        Write(updatedDir, "App.exe", "new");
        Write(updatedDir, "keep.txt", "keep");

        var build = new DeltaUpdatePackageService().Build(new DeltaUpdateBuildOptions
        {
            BaseDirectory = baseDir,
            UpdatedDirectory = updatedDir,
            OutputDirectory = outDir,
            BaseVersion = "1.0.0",
            TargetVersion = "1.1.0",
            SigningPrivateKeyPem = privateKey
        });

        build.Success.Should().BeTrue(build.Error);
        File.Exists(build.SignaturePath).Should().BeTrue();

        var apply = new DeltaUpdatePackageService().ApplyToStage(new DeltaUpdateApplyOptions
        {
            DeltaDirectory = outDir,
            CurrentInstallDirectory = baseDir,
            StageDirectory = stageDir,
            CurrentVersion = "1.0.0",
            RequireSignature = true,
            TrustedPublicKeys = { publicKey }
        });

        apply.Success.Should().BeTrue(apply.Error);
        File.ReadAllText(Path.Combine(stageDir, "App.exe"), Encoding.UTF8).Should().Be("new");
        File.ReadAllText(Path.Combine(stageDir, "keep.txt"), Encoding.UTF8).Should().Be("keep");
    }

    [Fact]
    public void ApplyAtomically_CommitsDeltaAndRollsBackFromJournal()
    {
        var baseDir = Path.Combine(_tempDir, "base");
        var updatedDir = Path.Combine(_tempDir, "updated");
        var installDir = Path.Combine(_tempDir, "install");
        var outDir = Path.Combine(_tempDir, "delta");
        var stageDir = Path.Combine(_tempDir, "stage");
        var journalPath = Path.Combine(_tempDir, "delta-journal.json");
        Write(baseDir, "App.exe", "old");
        Write(baseDir, "remove.txt", "remove");
        Write(updatedDir, "App.exe", "new");
        Write(updatedDir, "added.txt", "added");
        Write(installDir, "App.exe", "old");
        Write(installDir, "remove.txt", "remove");

        new DeltaUpdatePackageService().Build(new DeltaUpdateBuildOptions
        {
            BaseDirectory = baseDir,
            UpdatedDirectory = updatedDir,
            OutputDirectory = outDir,
            BaseVersion = "1.0.0",
            TargetVersion = "1.1.0"
        });

        var apply = new DeltaUpdatePackageService().ApplyAtomically(new DeltaUpdateAtomicApplyOptions
        {
            DeltaDirectory = outDir,
            CurrentInstallDirectory = installDir,
            StageDirectory = stageDir,
            JournalPath = journalPath,
            CurrentVersion = "1.0.0"
        });

        apply.Success.Should().BeTrue(apply.Error);
        apply.InstalledVersion.Should().Be("1.1.0");
        var preview = new DeltaUpdatePackageService().RecoverAtomicApply(new() { JournalPath = journalPath }, dryRun: true);
        preview.InstalledVersion.Should().Be("1.1.0");
        File.ReadAllText(Path.Combine(installDir, "App.exe"), Encoding.UTF8).Should().Be("new");
        File.ReadAllText(Path.Combine(installDir, "added.txt"), Encoding.UTF8).Should().Be("added");
        File.Exists(Path.Combine(installDir, "remove.txt")).Should().BeFalse();
        Directory.Exists(installDir + ".bak1").Should().BeTrue();
        File.Exists(journalPath).Should().BeTrue();
        JsonDocument.Parse(File.ReadAllText(journalPath, Encoding.UTF8)).RootElement.GetProperty("state").GetString().Should().Be("committed");

        var rollback = new DeltaUpdatePackageService().RollbackAtomicApply(new DeltaUpdateRollbackOptions
        {
            JournalPath = journalPath
        });

        rollback.Success.Should().BeTrue(rollback.Error);
        rollback.InstalledVersion.Should().Be("1.0.0");
        new DeltaUpdatePackageService().RecoverAtomicApply(new() { JournalPath = journalPath }, dryRun: true)
            .InstalledVersion.Should().Be("1.0.0");
        File.ReadAllText(Path.Combine(installDir, "App.exe"), Encoding.UTF8).Should().Be("old");
        File.ReadAllText(Path.Combine(installDir, "remove.txt"), Encoding.UTF8).Should().Be("remove");
        File.Exists(Path.Combine(installDir, "added.txt")).Should().BeFalse();
        JsonDocument.Parse(File.ReadAllText(journalPath, Encoding.UTF8)).RootElement.GetProperty("state").GetString().Should().Be("rolled-back");
    }

    [Fact]
    public void QualificationRunner_WritesValidAndNegativeDeltaLifecycleEvidence()
    {
        using var rsa = RSA.Create(2048);
        var privateKey = rsa.ExportRSAPrivateKeyPem();
        var publicKey = rsa.ExportRSAPublicKeyPem();
        var baseDir = Path.Combine(_tempDir, "base");
        var updatedDir = Path.Combine(_tempDir, "updated");
        var outDir = Path.Combine(_tempDir, "delta");
        var evidenceDir = Path.Combine(_tempDir, "qualification");
        Write(baseDir, "App.exe", "old");
        Write(baseDir, "remove.txt", "remove");
        Write(updatedDir, "App.exe", "new");
        Write(updatedDir, "added.txt", "added");

        new DeltaUpdatePackageService().Build(new DeltaUpdateBuildOptions
        {
            BaseDirectory = baseDir,
            UpdatedDirectory = updatedDir,
            OutputDirectory = outDir,
            BaseVersion = "1.0.0",
            TargetVersion = "1.1.0",
            SigningPrivateKeyPem = privateKey
        });

        var report = new DeltaUpdateQualificationRunner().Run(new DeltaUpdateQualificationOptions
        {
            DeltaDirectory = outDir,
            CurrentInstallDirectory = baseDir,
            OutputDirectory = evidenceDir,
            CurrentVersion = "1.0.0",
            RequireSignature = true,
            TrustedPublicKeys = { publicKey }
        });

        report.Success.Should().BeTrue();
        File.Exists(report.ReportPath).Should().BeTrue();
        report.Scenarios.Select(s => s.Id).Should().Equal(
            "verify-delta",
            "apply-rollback",
            "tampered-manifest-failure",
            "missing-blob-failure",
            "wrong-base-tree-failure");
        report.Scenarios.Should().OnlyContain(s => s.Success);
        report.Scenarios.Where(s => s.ExpectedToFail).Should().HaveCount(3);
        report.Scenarios.Single(s => s.Id == "missing-blob-failure").Message.Should().Contain("Missing blob was rejected");
        report.Scenarios.Single(s => s.Id == "wrong-base-tree-failure").Message.Should().Contain("Wrong base tree was rejected");

        using var document = JsonDocument.Parse(File.ReadAllText(report.ReportPath, Encoding.UTF8));
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("scenarios").GetArrayLength().Should().Be(5);
        File.Exists(Path.Combine(evidenceDir, "apply-rollback.json")).Should().BeTrue();
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
    }
}
