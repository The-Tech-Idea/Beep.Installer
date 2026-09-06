using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using Beep.Installer.Models;
using Beep.Installer.Steps;
using FluentAssertions;
using Microsoft.Win32;
using TheTechIdea.Beep.ConfigUtil;
using TheTechIdea.Beep.SetUp;
using Xunit;

namespace Beep.Installer.Tests;

public class ResourceProviderRuntimeTests
{
    [Fact]
    public void JournalLocator_UsesAppIdAndAllowsDisplayRenameForCustomJournal()
    {
        using var temp = new TempDirectory();
        var id = Guid.NewGuid().ToString("D");
        ResourceExecutionJournalStore.DefaultPath(temp.Path, id.ToUpperInvariant()).Should()
            .Be(ResourceExecutionJournalStore.DefaultPath(temp.Path, id));
        var custom = Path.Combine(temp.Path, "custom.json");
        new ResourceExecutionJournalStore(custom).Save(new()
        {
            Metadata = new() { AppId = id, ProductName = "Original display name", InstallRoot = temp.Path }
        });
        ResourceExecutionJournalStore.RecordLocation(temp.Path, id, custom);
        ResourceExecutionJournalStore.ResolvePath(temp.Path, id).Should().Be(custom);
        var changed = new ResourceExecutionJournalStore(custom).Load();
        new ResourceExecutionJournalStore(custom).Save(new()
        {
            Metadata = new() { AppId = id, ProductName = "Renamed display name", InstallRoot = temp.Path },
            Entries = changed.Entries
        });
        ResourceExecutionJournalStore.ResolvePath(temp.Path, id).Should().Be(custom);
        Action wrongId = () => ResourceExecutionJournalStore.ResolvePath(temp.Path, Guid.NewGuid().ToString("D"), custom);
        wrongId.Should().Throw<IOException>();
        Action missing = () => ResourceExecutionJournalStore.DefaultPath(temp.Path, "");
        missing.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("A34321A2-680B-43A8-AF88-C56D6AFAB012", true)]
    [InlineData("b34321a2-680b-43a8-af88-c56d6afab012", false)]
    [InlineData("", false)]
    [InlineData("not-an-id", false)]
    public void JournalAppId_PersistsAndRejectsDifferentOrMissingIdentity(string installedId, bool matches)
    {
        using var temp = new TempDirectory();
        var store = new ResourceExecutionJournalStore(Path.Combine(temp.Path, "identity.json"));
        store.Save(new() { Metadata = new() { AppId = installedId, ProductName = "Same name", Publisher = "Same publisher" } });
        var loaded = store.TryLoad().Journal!;
        loaded.Metadata.AppId.Should().Be(installedId);
        var error = ResourceJournalRecoveryService.ValidateAppId(loaded.Metadata, "a34321a2-680b-43a8-af88-c56d6afab012");
        string.IsNullOrEmpty(error).Should().Be(matches);
    }

    [Theory]
    [InlineData("Publisher", true)]
    [InlineData("Another publisher", false)]
    [InlineData("", false)]
    public void JournalIdentity_RequiresTheInstalledPublisher(string publisher, bool matches)
    {
        using var temp = new TempDirectory();
        var metadata = new ResourceExecutionJournalMetadata { ProductName = "App", Publisher = publisher };
        var path = ResourceExecutionJournalStore.DefaultPath(temp.Path, "a34321a2-680b-43a8-af88-c56d6afab012");
        var store = new ResourceExecutionJournalStore(path);
        store.Save(new() { Metadata = metadata });
        var loaded = store.TryLoad();
        loaded.Success.Should().BeTrue();
        loaded.Journal!.Metadata.Publisher.Should().Be(publisher);
        ResourceJournalRecoveryService.ValidateIdentity(loaded.Journal.Metadata, "App", "Publisher")
            .Length.Should().Be(matches ? 0 : "Typed resource journal publisher does not match the current project.".Length);
    }

    [Fact]
    public void ResourceExecutionJournalStore_SavesAndLoadsJournal()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "state", "resource-journal.json");
        var store = new ResourceExecutionJournalStore(path);
        var journal = new ResourceExecutionJournal
        {
            Entries =
            {
                new ResourceExecutionJournalEntry
                {
                    OperationId = "file:core:app.exe",
                    OperationType = "file.copy",
                    Action = ResourceExecutionAction.Apply,
                    ResultCode = ResourceProviderResultCode.Succeeded,
                    Message = "Copied."
                }
            }
        };

        store.Save(journal);

        File.Exists(path).Should().BeTrue();
        var loaded = store.Load();
        loaded.Entries.Should().ContainSingle(e => e.OperationId == "file:core:app.exe"
                                                   && e.OperationType == "file.copy"
                                                   && e.Action == ResourceExecutionAction.Apply
                                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
    }

    [Fact]
    public void ResourceExecutionJournalStore_TryLoadReportsMissingJournal()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "missing", "resource-journal.json");

        var result = new ResourceExecutionJournalStore(path).TryLoad();

        result.Status.Should().Be(ResourceExecutionJournalLoadStatus.Missing);
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public void ResourceExecutionJournalStore_TryLoadReportsCorruptJournal()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "resource-journal.json");
        File.WriteAllText(path, "{ not json");

        var result = new ResourceExecutionJournalStore(path).TryLoad();

        result.Status.Should().Be(ResourceExecutionJournalLoadStatus.Corrupt);
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("corrupt JSON");
    }

    [Fact]
    public void ResourceExecutionJournalStore_TryLoadReportsIncompatibleSchema()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "resource-journal.json");
        new ResourceExecutionJournalStore(path).Save(new ResourceExecutionJournal
        {
            Metadata = new ResourceExecutionJournalMetadata
            {
                AppId = "a34321a2-680b-43a8-af88-c56d6afab012",
                SchemaVersion = "99.0",
                AttemptId = "attempt",
                ProductName = "Future",
                ProductVersion = "1.0.0",
                PlanHash = "hash",
                InstallScope = "user"
            }
        });

        var result = new ResourceExecutionJournalStore(path).TryLoad();

        result.Status.Should().Be(ResourceExecutionJournalLoadStatus.Incompatible);
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not supported");
    }

    [Fact]
    public void ResourceJournalRecoveryService_InspectReportsPendingRollbackOperations()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "resource-journal.json");
        var store = new ResourceExecutionJournalStore(path);
        store.Save(new ResourceExecutionJournal
        {
            Metadata = new ResourceExecutionJournalMetadata
            {
                AppId = "a34321a2-680b-43a8-af88-c56d6afab012",
                ProductName = "RecoverableApp",
                ProductVersion = "1.0.0",
                PlanHash = "plan-hash",
                InstallScope = "per-user",
                AttemptId = "attempt"
            },
            Entries =
            {
                new ResourceExecutionJournalEntry
                {
                    OperationId = "file:one",
                    OperationType = "file.copy",
                    Action = ResourceExecutionAction.Apply,
                    ResultCode = ResourceProviderResultCode.Succeeded,
                    Operation = Operation("file:one")
                },
                new ResourceExecutionJournalEntry
                {
                    OperationId = "file:two",
                    OperationType = "file.copy",
                    Action = ResourceExecutionAction.Apply,
                    ResultCode = ResourceProviderResultCode.Succeeded,
                    Operation = Operation("file:two")
                },
                new ResourceExecutionJournalEntry
                {
                    OperationId = "file:two",
                    OperationType = "file.copy",
                    Action = ResourceExecutionAction.Rollback,
                    ResultCode = ResourceProviderResultCode.Succeeded
                }
            }
        });
        var plan = new CompiledInstallPlan
        {
            AppId = "a34321a2-680b-43a8-af88-c56d6afab012",
            ProductName = "RecoverableApp",
            ProductVersion = "1.0.0",
            PlanHash = "plan-hash",
            InstallScope = "per-user"
        };

        var summary = ResourceJournalRecoveryService.Inspect(path, plan);

        summary.Status.Should().Be(ResourceExecutionJournalLoadStatus.Loaded);
        summary.EntryCount.Should().Be(3);
        summary.AppliedCount.Should().Be(2);
        summary.RolledBackCount.Should().Be(1);
        summary.PendingRollbackOperations.Should().ContainSingle(o => o.Id == "file:one");
        summary.CanRollback.Should().BeTrue();
        summary.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void ResourceJournalRecoveryService_InspectWarnsWhenPlanDoesNotMatchJournal()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "resource-journal.json");
        new ResourceExecutionJournalStore(path).Save(new ResourceExecutionJournal
        {
            Metadata = new ResourceExecutionJournalMetadata
            {
                AppId = "a34321a2-680b-43a8-af88-c56d6afab012",
                ProductName = "RecoverableApp",
                ProductVersion = "1.0.0",
                PlanHash = "original-plan",
                InstallScope = "per-user",
                AttemptId = "attempt"
            }
        });
        var currentPlan = new CompiledInstallPlan
        {
            AppId = "a34321a2-680b-43a8-af88-c56d6afab012",
            ProductName = "RecoverableApp",
            ProductVersion = "1.0.0",
            PlanHash = "changed-plan",
            InstallScope = "per-user"
        };

        var summary = ResourceJournalRecoveryService.Inspect(path, currentPlan);

        summary.Status.Should().Be(ResourceExecutionJournalLoadStatus.Loaded);
        summary.Warnings.Should().ContainSingle(w => w.Contains("plan hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResourceJournalRecoveryService_AbandonMovesActiveJournalOutOfRuntimePath()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "resource-journal.json");
        File.WriteAllText(path, "{}");

        var result = ResourceJournalRecoveryService.Abandon(
            path,
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));

        result.Succeeded.Should().BeTrue();
        File.Exists(path).Should().BeFalse();
        result.ArchivedPath.Should().NotBeNull();
        File.Exists(result.ArchivedPath!).Should().BeTrue();
        result.ArchivedPath.Should().EndWith("resource-journal.json.abandoned.20260830120000");
    }

    [Fact]
    public void ResourceProviderStep_PersistsJournalMetadataBoundToPlan()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.Path, "src");
        var installRoot = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(installRoot);
        var sourceFile = Path.Combine(sourceRoot, "app.exe");
        File.WriteAllText(sourceFile, "payload");

        var project = InstallerProjectFactory.CreateNew("MetadataApp", "1.2.3", "ACME", sourceRoot);
        project.DefaultScope = InstallationScope.User;
        project.Components.Clear();
        project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Files =
            {
                new TheTechIdea.Beep.Installer.FileCopyOperation
                {
                    SourcePath = sourceFile,
                    DestinationPath = "app.exe"
                }
            }
        });
        var expectedPlan = new InstallPlanCompiler().Compile(project).Plan!;
        var journalPath = Path.Combine(temp.Path, "metadata-journal.json");
        var attemptId = "attempt-fixed";
        var context = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;
        context.Properties[InstallContextKeys.ResourceExecutionAttemptId] = attemptId;

        new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
                .Register(new ComponentSelectResourceProvider())
                .Register(new FileCopyResourceProvider()))
            .Execute(context)
            .Flag.Should().Be(Errors.Ok);

        var metadata = new ResourceExecutionJournalStore(journalPath).Load().Metadata;
        metadata.ProductName.Should().Be("MetadataApp");
        metadata.ProductVersion.Should().Be("1.2.3");
        metadata.PlanHash.Should().Be(expectedPlan.PlanHash);
        metadata.InstallScope.Should().Be(expectedPlan.InstallScope);
        metadata.AttemptId.Should().Be(attemptId);
        metadata.SchemaVersion.Should().Be("1.0");
    }

    [Fact]
    public void ResourcePlanExecutor_StoresRedactedOperationSnapshotForReplay()
    {
        var provider = new RecordingProvider(failApplyId: "");
        var registry = new BuiltInResourceProviderRegistry().Register(provider);
        var plan = new CompiledInstallPlan
        {
            AppId = "a34321a2-680b-43a8-af88-c56d6afab012",
            Operations =
            {
                new CompiledInstallOperation
                {
                    Id = "op:secret",
                    Type = "test.resource",
                    Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["name"] = "secret",
                        ["password"] = "open-sesame"
                    },
                    SensitiveInputs = { "password" }
                }
            }
        };

        var result = new ResourcePlanExecutor(registry)
            .ExecuteInstall(plan, new ResourceProviderContext { InstallRoot = @"C:\App" });

        result.Succeeded.Should().BeTrue();
        var apply = result.Journal.Entries.Should().ContainSingle(e => e.Action == ResourceExecutionAction.Apply).Subject;
        apply.Operation.Should().NotBeNull();
        apply.Operation!.Inputs["name"].Should().Be("secret");
        apply.Operation.Inputs["password"].Should().Be("<redacted>");
    }

    [Fact]
    public void ResourcePlanExecutor_ResumesVerifiedOperationsFromExistingJournal()
    {
        var provider = new RecordingProvider(failApplyId: "");
        var registry = new BuiltInResourceProviderRegistry().Register(provider);
        var plan = new CompiledInstallPlan
        {
            AppId = "a34321a2-680b-43a8-af88-c56d6afab012",
            ProductName = "ResumeApp",
            ProductVersion = "1.0.0",
            PlanHash = "plan-hash",
            InstallScope = "per-user",
            Operations =
            {
                Operation("op:b", dependsOn: "op:a"),
                Operation("op:a")
            }
        };
        var existing = new ResourceExecutionJournal
        {
            Metadata = new ResourceExecutionJournalMetadata
            {
                AppId = "a34321a2-680b-43a8-af88-c56d6afab012",
                ProductName = "ResumeApp",
                ProductVersion = "1.0.0",
                PlanHash = "plan-hash",
                InstallScope = "per-user",
                AttemptId = "attempt"
            },
            Entries =
            {
                new ResourceExecutionJournalEntry
                {
                    OperationId = "op:a",
                    OperationType = "test.resource",
                    Action = ResourceExecutionAction.Apply,
                    ResultCode = ResourceProviderResultCode.Succeeded,
                    Operation = Operation("op:a")
                },
                new ResourceExecutionJournalEntry
                {
                    OperationId = "op:a",
                    OperationType = "test.resource",
                    Action = ResourceExecutionAction.Verify,
                    ResultCode = ResourceProviderResultCode.Succeeded
                }
            }
        };
        var checkpoints = new List<int>();

        var result = new ResourcePlanExecutor(registry).ExecuteInstall(
            plan,
            new ResourceProviderContext { InstallRoot = @"C:\App", AttemptId = "attempt" },
            new ResourceExecutionOptions
            {
                ExistingJournal = existing,
                Checkpoint = journal => checkpoints.Add(journal.Entries.Count)
            });

        result.Succeeded.Should().BeTrue();
        provider.Actions.Should().Equal("apply:op:b");
        result.Journal.Entries.Should().Contain(e => e.OperationId == "op:a"
                                                     && e.Action == ResourceExecutionAction.Resume);
        result.Journal.Entries.Should().Contain(e => e.OperationId == "op:b"
                                                     && e.Action == ResourceExecutionAction.Apply
                                                     && e.ResultCode == ResourceProviderResultCode.Succeeded);
        checkpoints.Should().NotBeEmpty();
    }

    [Fact]
    public void ResourcePlanExecutor_ReplaysVerifiedOperationsWhenRepairDisablesResume()
    {
        var provider = new RecordingProvider(failApplyId: "");
        var registry = new BuiltInResourceProviderRegistry().Register(provider);
        var plan = new CompiledInstallPlan
        {
            AppId = "a34321a2-680b-43a8-af88-c56d6afab012",
            ProductName = "RepairReplayApp",
            ProductVersion = "1.0.0",
            PlanHash = "plan-hash",
            InstallScope = "per-user",
            Operations = { Operation("op:a") }
        };
        var existing = new ResourceExecutionJournal
        {
            Metadata = new ResourceExecutionJournalMetadata
            {
                AppId = "a34321a2-680b-43a8-af88-c56d6afab012",
                ProductName = "RepairReplayApp",
                ProductVersion = "1.0.0",
                PlanHash = "plan-hash",
                InstallScope = "per-user",
                AttemptId = "install-attempt",
                ExecutionMode = "install"
            },
            Entries =
            {
                new ResourceExecutionJournalEntry
                {
                    OperationId = "op:a",
                    OperationType = "test.resource",
                    ExecutionMode = "install",
                    Action = ResourceExecutionAction.Apply,
                    ResultCode = ResourceProviderResultCode.Succeeded,
                    Operation = Operation("op:a")
                },
                new ResourceExecutionJournalEntry
                {
                    OperationId = "op:a",
                    OperationType = "test.resource",
                    ExecutionMode = "install",
                    Action = ResourceExecutionAction.Verify,
                    ResultCode = ResourceProviderResultCode.Succeeded
                }
            }
        };

        var result = new ResourcePlanExecutor(registry).ExecuteInstall(
            plan,
            new ResourceProviderContext { InstallRoot = @"C:\App", AttemptId = "repair-attempt", ExecutionMode = "repair" },
            new ResourceExecutionOptions
            {
                ExistingJournal = existing,
                ResumeCompletedOperations = false
            });

        result.Succeeded.Should().BeTrue();
        provider.Actions.Should().Equal("apply:op:a");
        result.Journal.Entries.Should().NotContain(e => e.OperationId == "op:a"
                                                        && e.Action == ResourceExecutionAction.Resume
                                                        && e.ExecutionMode == "repair");
        result.Journal.Entries.Should().Contain(e => e.OperationId == "op:a"
                                                     && e.Action == ResourceExecutionAction.Apply
                                                     && e.ResultCode == ResourceProviderResultCode.Succeeded
                                                     && e.ExecutionMode == "repair");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void ResourcePlanExecutor_ConvergesAfterCheckpointFaultAtEveryProviderTransition(int faultAfterEntryCount)
    {
        using var temp = new TempDirectory();
        var journalPath = Path.Combine(temp.Path, "fault-journal.json");
        var store = new ResourceExecutionJournalStore(journalPath);
        var plan = new CompiledInstallPlan
        {
            AppId = "a34321a2-680b-43a8-af88-c56d6afab012",
            ProductName = "FaultReplayApp",
            ProductVersion = "1.0.0",
            PlanHash = "plan-hash",
            InstallScope = "per-user",
            Operations =
            {
                Operation("op:a"),
                Operation("op:b", dependsOn: "op:a")
            }
        };
        var firstProvider = new RecordingProvider(failApplyId: "");
        var firstRegistry = new BuiltInResourceProviderRegistry().Register(firstProvider);

        var firstRun = () => new ResourcePlanExecutor(firstRegistry).ExecuteInstall(
            plan,
            new ResourceProviderContext { InstallRoot = @"C:\App", AttemptId = "fault-attempt", ExecutionMode = "install" },
            new ResourceExecutionOptions
            {
                Checkpoint = journal =>
                {
                    store.Save(journal);
                    if (journal.Entries.Count == faultAfterEntryCount)
                        throw new InvalidOperationException($"Simulated crash after journal entry {faultAfterEntryCount}.");
                }
            });

        firstRun.Should().Throw<InvalidOperationException>()
            .WithMessage($"Simulated crash after journal entry {faultAfterEntryCount}.");
        File.Exists(journalPath).Should().BeTrue();

        var retryProvider = new RecordingProvider(failApplyId: "");
        var retryRegistry = new BuiltInResourceProviderRegistry().Register(retryProvider);
        var retry = new ResourcePlanExecutor(retryRegistry).ExecuteInstall(
            plan,
            new ResourceProviderContext { InstallRoot = @"C:\App", AttemptId = "fault-attempt", ExecutionMode = "install" },
            new ResourceExecutionOptions
            {
                ExistingJournal = store.Load(),
                Checkpoint = store.Save
            });

        retry.Succeeded.Should().BeTrue();
        retry.Journal.Entries.Should().Contain(e => e.OperationId == "op:a"
                                                   && e.Action == ResourceExecutionAction.Verify
                                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
        retry.Journal.Entries.Should().Contain(e => e.OperationId == "op:b"
                                                   && e.Action == ResourceExecutionAction.Verify
                                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
        retry.Journal.Entries.Should().NotContain(e => e.Action == ResourceExecutionAction.Rollback
                                                       && e.ResultCode == ResourceProviderResultCode.Failed);
    }

    [Fact]
    public void ResourcePlanExecutor_RollsBackAppliedOperationsWhenLaterOperationFails()
    {
        var provider = new RecordingProvider(failApplyId: "op:b");
        var registry = new BuiltInResourceProviderRegistry().Register(provider);
        var plan = new CompiledInstallPlan
        {
            AppId = "a34321a2-680b-43a8-af88-c56d6afab012",
            Operations =
            {
                Operation("op:b", dependsOn: "op:a"),
                Operation("op:a")
            }
        };

        var result = new ResourcePlanExecutor(registry)
            .ExecuteInstall(plan, new ResourceProviderContext { InstallRoot = @"C:\App" });

        result.Succeeded.Should().BeFalse();
        provider.Actions.Should().Equal("apply:op:a", "apply:op:b", "rollback:op:a");
        result.Journal.Entries.Should().Contain(e => e.Action == ResourceExecutionAction.Rollback);
    }

    [Fact]
    public void ResourceProviderStep_CompilesPlanAndExecutesWindowsServiceProvider()
    {
        var project = InstallerProjectFactory.CreateNew("SvcApp", "1.0.0", "ACME", "");
        project.WindowsServices.Add(new WindowsServiceDefinition
        {
            Name = "SvcApp",
            DisplayName = "SvcApp Worker",
            ExecutablePath = @"%InstallPath%\SvcApp.exe",
            StartAfterInstall = true,
            StopOnUninstall = true
        });

        var runner = new StatefulServiceRunner();
        var registry = new BuiltInResourceProviderRegistry()
            .Register(new WindowsServiceResourceProvider(runner));
        using var temp = new TempDirectory();
        var context = InstallContextBuilder.ForInstall(project, Path.Combine(temp.Path, "install"), perUser: false);
        var journalPath = Path.Combine(temp.Path, "svc-journal.json");
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;
        var step = new ResourceProviderStep(registry: registry);

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        context.Properties.Should().ContainKey(InstallContextKeys.CompiledInstallPlan);
        context.Properties.Should().ContainKey(InstallContextKeys.ResourceExecutionJournal);
        File.Exists(journalPath).Should().BeTrue();
        runner.Calls.Should().Contain(c => c[0] == "create");
        runner.Calls.Should().Contain(c => c[0] == "start");
    }

    [Fact]
    public void ResourceProviderStep_ExecutesFileCopyProvider()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.Path, "src");
        var installRoot = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(installRoot);
        var sourceFile = Path.Combine(sourceRoot, "app.exe");
        File.WriteAllText(sourceFile, "payload");

        var project = InstallerProjectFactory.CreateNew("FileApp", "1.0.0", "ACME", sourceRoot);
        var component = new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true
        };
        component.Files.Add(new TheTechIdea.Beep.Installer.FileCopyOperation
        {
            SourcePath = sourceFile,
            DestinationPath = "bin/app.exe",
            IsRequired = true,
            Overwrite = true
        });
        project.Components.Add(component);

        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new FileCopyResourceProvider()));
        var context = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        var journalPath = Path.Combine(temp.Path, "file-journal.json");
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        File.ReadAllText(Path.Combine(installRoot, "bin", "app.exe")).Should().Be("payload");
        context.TryGetProperty<List<string>>(InstallContextKeys.InstalledFiles)
            .Should().ContainSingle(Path.Combine(installRoot, "bin", "app.exe"));
        new ResourceExecutionJournalStore(journalPath).Load().Entries
            .Should().Contain(e => e.OperationType == "file.copy"
                                   && e.Action == ResourceExecutionAction.Apply
                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
        var journal = context.Properties[InstallContextKeys.ResourceExecutionJournal]
            .Should().BeOfType<ResourceExecutionJournal>().Subject;
        journal.Entries.Should().Contain(e => e.OperationType == "file.copy"
                                             && e.Action == ResourceExecutionAction.Apply
                                             && e.ResultCode == ResourceProviderResultCode.Succeeded);
    }

    [Fact]
    public void ResourceProviderStep_LoadsExistingJournalAndSkipsVerifiedOperations()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.Path, "src");
        var installRoot = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(installRoot);
        var sourceFile = Path.Combine(sourceRoot, "app.exe");
        File.WriteAllText(sourceFile, "payload");

        var project = InstallerProjectFactory.CreateNew("ResumeFileApp", "1.0.0", "ACME", sourceRoot);
        project.DefaultScope = InstallationScope.User;
        project.Components.Clear();
        project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true,
            Files =
            {
                new TheTechIdea.Beep.Installer.FileCopyOperation
                {
                    SourcePath = sourceFile,
                    DestinationPath = "bin/app.exe",
                    IsRequired = true,
                    Overwrite = true
                }
            }
        });

        var plan = new InstallPlanCompiler().Compile(project).Plan!;
        var fileOperation = plan.Operations.Single(o => o.Type == "file.copy");
        var journalPath = Path.Combine(temp.Path, "resume-file-journal.json");
        new ResourceExecutionJournalStore(journalPath).Save(new ResourceExecutionJournal
        {
            Metadata = new ResourceExecutionJournalMetadata
            {
                ProductName = plan.ProductName,
                Publisher = plan.Publisher,
                AppId = plan.AppId,
                ProductVersion = plan.ProductVersion,
                PlanHash = plan.PlanHash,
                InstallScope = plan.InstallScope,
                AttemptId = "resume-attempt", InstallRoot = installRoot
            },
            Entries =
            {
                new ResourceExecutionJournalEntry
                {
                    OperationId = fileOperation.Id,
                    OperationType = fileOperation.Type,
                    Action = ResourceExecutionAction.Apply,
                    ResultCode = ResourceProviderResultCode.Succeeded,
                    Operation = fileOperation
                },
                new ResourceExecutionJournalEntry
                {
                    OperationId = fileOperation.Id,
                    OperationType = fileOperation.Type,
                    Action = ResourceExecutionAction.Verify,
                    ResultCode = ResourceProviderResultCode.Succeeded
                }
            }
        });

        var context = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;
        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new FailingFileCopyProvider()));

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        context.Properties[InstallContextKeys.ResourceExecutionAttemptId].Should().Be("resume-attempt");
        var loaded = new ResourceExecutionJournalStore(journalPath).Load();
        loaded.Entries.Should().Contain(e => e.OperationId == fileOperation.Id
                                             && e.Action == ResourceExecutionAction.Resume);
        loaded.Entries.Should().NotContain(e => e.OperationId == fileOperation.Id
                                                && e.Action == ResourceExecutionAction.Apply
                                                && e.ResultCode == ResourceProviderResultCode.Failed);
    }

    [Fact]
    public void ResourceProviderStep_ReplaysVerifiedJournalOperationsDuringRepair()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.Path, "src");
        var installRoot = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(installRoot);
        var sourceFile = Path.Combine(sourceRoot, "app.exe");
        File.WriteAllText(sourceFile, "payload");

        var project = InstallerProjectFactory.CreateNew("RepairReplayFileApp", "1.0.0", "ACME", sourceRoot);
        project.DefaultScope = InstallationScope.User;
        project.Components.Clear();
        project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true,
            Files =
            {
                new TheTechIdea.Beep.Installer.FileCopyOperation
                {
                    SourcePath = sourceFile,
                    DestinationPath = "bin/app.exe",
                    IsRequired = true,
                    Overwrite = true
                }
            }
        });

        var plan = new InstallPlanCompiler().Compile(project).Plan!;
        var fileOperation = plan.Operations.Single(o => o.Type == "file.copy");
        var journalPath = Path.Combine(temp.Path, "repair-file-journal.json");
        new ResourceExecutionJournalStore(journalPath).Save(new ResourceExecutionJournal
        {
            Metadata = new ResourceExecutionJournalMetadata
            {
                ProductName = plan.ProductName,
                Publisher = plan.Publisher,
                AppId = plan.AppId,
                ProductVersion = plan.ProductVersion,
                PlanHash = plan.PlanHash,
                InstallScope = plan.InstallScope,
                AttemptId = "install-attempt", InstallRoot = installRoot,
                ExecutionMode = "install"
            },
            Entries =
            {
                new ResourceExecutionJournalEntry
                {
                    OperationId = fileOperation.Id,
                    OperationType = fileOperation.Type,
                    ExecutionMode = "install",
                    Action = ResourceExecutionAction.Apply,
                    ResultCode = ResourceProviderResultCode.Succeeded,
                    Operation = fileOperation
                },
                new ResourceExecutionJournalEntry
                {
                    OperationId = fileOperation.Id,
                    OperationType = fileOperation.Type,
                    ExecutionMode = "install",
                    Action = ResourceExecutionAction.Verify,
                    ResultCode = ResourceProviderResultCode.Succeeded
                }
            }
        });

        var context = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        context.Properties[InstallContextKeys.ResourceExecutionMode] = "repair";
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;
        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new FailingFileCopyProvider()));

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Failed);
        var loaded = new ResourceExecutionJournalStore(journalPath).Load();
        loaded.Entries.Should().NotContain(e => e.OperationId == fileOperation.Id
                                                && e.Action == ResourceExecutionAction.Resume
                                                && e.ExecutionMode == "repair");
        loaded.Entries.Should().Contain(e => e.OperationId == fileOperation.Id
                                             && e.Action == ResourceExecutionAction.Apply
                                             && e.ResultCode == ResourceProviderResultCode.Failed
                                             && e.ExecutionMode == "repair");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResourceProviderUninstallStep_ReplaysJournalAndRemovesAppliedFile(bool rename)
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.Path, "src");
        var installRoot = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(installRoot);
        var sourceFile = Path.Combine(sourceRoot, "app.exe");
        File.WriteAllText(sourceFile, "payload");

        var project = InstallerProjectFactory.CreateNew("ReplayFileApp", "1.0.0", "ACME", sourceRoot);
        project.Components.Clear();
        project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true,
            Files =
            {
                new TheTechIdea.Beep.Installer.FileCopyOperation
                {
                    SourcePath = sourceFile,
                    DestinationPath = "%ProductName%/app.exe",
                    IsRequired = true
                }
            }
        });

        var registry = new BuiltInResourceProviderRegistry()
            .Register(new ComponentSelectResourceProvider())
            .Register(new FileCopyResourceProvider());
        var journalPath = Path.Combine(temp.Path, "resource-journal.json");
        var installContext = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        installContext.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;

        new ResourceProviderStep(registry: registry).Execute(installContext).Flag.Should().Be(Errors.Ok);
        File.Exists(Path.Combine(installRoot, "ReplayFileApp", "app.exe")).Should().BeTrue();

        if (rename) project.AppName = "Renamed display name";
        File.Delete(Path.Combine(installRoot, "ReplayFileApp", "app.exe"));
        var repairContext = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        repairContext.Properties[InstallContextKeys.ResourceExecutionMode] = "repair";
        repairContext.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;
        new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new ComponentSelectResourceProvider())
            .Register(new FileCopyResourceProvider())).Execute(repairContext).Flag.Should().Be(Errors.Ok);
        File.ReadAllText(Path.Combine(installRoot, "ReplayFileApp", "app.exe")).Should().Be("payload");
        var uninstallContext = InstallContextBuilder.ForUninstall(project, installRoot, perUser: true);
        uninstallContext.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;
        var uninstall = new ResourceProviderUninstallStep(registry: registry).Execute(uninstallContext);

        uninstall.Flag.Should().Be(Errors.Ok);
        File.Exists(Path.Combine(installRoot, "ReplayFileApp", "app.exe")).Should().BeFalse();
        File.Exists(journalPath).Should().BeFalse("successful uninstall removes product-owned runtime artifacts");
        uninstallContext.Properties[InstallContextKeys.ResourceExecutionJournal]
            .Should().BeOfType<ResourceExecutionJournal>().Subject.Entries
            .Should().Contain(e => e.OperationType == "file.copy"
                                   && e.Action == ResourceExecutionAction.Rollback
                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
    }

    [Fact]
    public void ResourceProviderUninstallStep_RefusesReplayWhenCurrentPlanDoesNotMatchJournal()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.Path, "src");
        var installRoot = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(installRoot);
        var sourceFile = Path.Combine(sourceRoot, "app.exe");
        File.WriteAllText(sourceFile, "payload");

        var project = InstallerProjectFactory.CreateNew("MismatchApp", "1.0.0", "ACME", sourceRoot);
        project.Components.Clear();
        project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Files =
            {
                new TheTechIdea.Beep.Installer.FileCopyOperation
                {
                    SourcePath = sourceFile,
                    DestinationPath = "app.exe"
                }
            }
        });

        var registry = new BuiltInResourceProviderRegistry()
            .Register(new ComponentSelectResourceProvider())
            .Register(new FileCopyResourceProvider());
        var journalPath = Path.Combine(temp.Path, "mismatch-journal.json");
        var installContext = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        installContext.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;
        new ResourceProviderStep(registry: registry).Execute(installContext).Flag.Should().Be(Errors.Ok);
        var installedFile = Path.Combine(installRoot, "app.exe");
        File.Exists(installedFile).Should().BeTrue();

        project.AppVersion = "2.0.0";
        var uninstallContext = InstallContextBuilder.ForUninstall(project, installRoot, perUser: true);
        uninstallContext.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;

        var result = new ResourceProviderUninstallStep(registry: registry).Execute(uninstallContext);

        result.Flag.Should().Be(Errors.Failed);
        result.Message.Should().Contain("version mismatch");
        File.Exists(installedFile).Should().BeTrue("mismatched journals must not replay against a different plan");
        new ResourceExecutionJournalStore(journalPath).Load().Entries
            .Should().NotContain(e => e.Action == ResourceExecutionAction.Rollback);
    }

    [Fact]
    public void ResourceProviderUninstallStep_ReportsCorruptJournalWithoutTouchingResources()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.Path, "src");
        var installRoot = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(installRoot);
        var installedFile = Path.Combine(installRoot, "app.exe");
        File.WriteAllText(installedFile, "installed");

        var project = InstallerProjectFactory.CreateNew("CorruptJournalApp", "1.0.0", "ACME", sourceRoot);
        var journalPath = Path.Combine(temp.Path, "corrupt-journal.json");
        File.WriteAllText(journalPath, "{ bad json");
        var context = InstallContextBuilder.ForUninstall(project, installRoot, perUser: true);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;

        var result = new ResourceProviderUninstallStep(registry: new BuiltInResourceProviderRegistry())
            .Execute(context);

        result.Flag.Should().Be(Errors.Failed);
        result.Message.Should().Contain("corrupt JSON");
        File.Exists(installedFile).Should().BeTrue();
    }

    [Fact]
    public void ResourceProviderUninstallStep_ReplaysRegistryEnvironmentAndShortcutResources()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.Path, "src");
        var installRoot = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(installRoot);
        var target = Path.Combine(installRoot, "App.exe");
        File.WriteAllText(target, "exe");

        var project = InstallerProjectFactory.CreateNew("ReplayResourceApp", "1.0.0", "ACME", sourceRoot);
        project.RegistryEntries.Add(new TheTechIdea.Beep.Installer.RegistryOperation
        {
            KeyPath = @"Software\ReplayResourceApp",
            ValueName = "InstallPath",
            Value = "%InstallPath%"
        });
        project.EnvironmentVariables.Add(new TheTechIdea.Beep.Installer.EnvironmentVariableOp
        {
            Name = "REPLAY_RESOURCE_APP",
            Value = "%InstallPath%",
            Scope = EnvironmentVariableTarget.User
        });
        project.Shortcuts.Add(new TheTechIdea.Beep.Installer.ShortcutDefinition
        {
            Name = "ReplayResourceApp",
            TargetPath = "App.exe",
            Location = TheTechIdea.Beep.Installer.ShortcutLocation.StartMenu
        });

        var registryStore = new FakeRegistryStore();
        var environmentStore = new FakeEnvironmentStore();
        var shortcutStore = new FakeShortcutStore(temp.Path);
        shortcutStore.AddExistingFile(target);
        var providerRegistry = new BuiltInResourceProviderRegistry()
            .Register(new RegistryWriteResourceProvider(registryStore))
            .Register(new EnvironmentSetResourceProvider(environmentStore))
            .Register(new ShortcutCreateResourceProvider(shortcutStore));
        var journalPath = Path.Combine(temp.Path, "multi-resource-journal.json");

        var installContext = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        installContext.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;
        new ResourceProviderStep(registry: providerRegistry).Execute(installContext).Flag.Should().Be(Errors.Ok);

        registryStore.ReadValue(InstallerRegistryHive.CurrentUser, @"Software\ReplayResourceApp", "InstallPath")
            .Exists.Should().BeTrue();
        environmentStore.Read("REPLAY_RESOURCE_APP", EnvironmentVariableTarget.User).Should().Be(installRoot);
        shortcutStore.Read(Path.Combine(
                shortcutStore.Folder(Environment.SpecialFolder.Programs),
                "ReplayResourceApp",
                "ReplayResourceApp.lnk"))
            .Exists.Should().BeTrue();

        var uninstallContext = InstallContextBuilder.ForUninstall(project, installRoot, perUser: true);
        uninstallContext.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;
        new ResourceProviderUninstallStep(registry: providerRegistry).Execute(uninstallContext).Flag.Should().Be(Errors.Ok);

        registryStore.ReadValue(InstallerRegistryHive.CurrentUser, @"Software\ReplayResourceApp", "InstallPath")
            .Exists.Should().BeFalse();
        environmentStore.Read("REPLAY_RESOURCE_APP", EnvironmentVariableTarget.User).Should().BeNull();
        shortcutStore.Read(Path.Combine(
                shortcutStore.Folder(Environment.SpecialFolder.Programs),
                "ReplayResourceApp",
                "ReplayResourceApp.lnk"))
            .Exists.Should().BeFalse();
    }

    [Fact]
    public void ResourceProviderStep_SkipsResourcesForUnselectedOptionalComponent()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.Path, "src");
        var installRoot = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(installRoot);
        var sourceFile = Path.Combine(sourceRoot, "optional.dll");
        File.WriteAllText(sourceFile, "optional payload");

        var project = InstallerProjectFactory.CreateNew("OptionalApp", "1.0.0", "ACME", sourceRoot);
        project.Components.Clear();
        project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "optional",
            Name = "Optional",
            Required = false,
            Selected = false,
            Files =
            {
                new TheTechIdea.Beep.Installer.FileCopyOperation
                {
                    SourcePath = sourceFile,
                    DestinationPath = "optional.dll",
                    IsRequired = true,
                    Overwrite = true
                }
            }
        });

        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new ComponentSelectResourceProvider())
            .Register(new FileCopyResourceProvider()));
        var context = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = Path.Combine(temp.Path, "optional-journal.json");

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        File.Exists(Path.Combine(installRoot, "optional.dll")).Should().BeFalse();
        context.TryGetProperty<List<string>>(InstallContextKeys.InstalledFiles).Should().BeNull();
        var journal = context.Properties[InstallContextKeys.ResourceExecutionJournal]
            .Should().BeOfType<ResourceExecutionJournal>().Subject;
        journal.Entries.Should().Contain(e => e.OperationId == "component:optional"
                                             && e.Action == ResourceExecutionAction.Plan
                                             && e.Message == "None.");
        journal.Entries.Should().Contain(e => e.OperationType == "file.copy"
                                             && e.Action == ResourceExecutionAction.Skip
                                             && e.Message.Contains("Dependency 'component:optional'"));
    }

    [Fact]
    public void ResourceProviderStep_AppliesResourcesForRequiredComponentEvenWhenNotSelected()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.Path, "src");
        var installRoot = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(installRoot);
        var sourceFile = Path.Combine(sourceRoot, "required.dll");
        File.WriteAllText(sourceFile, "required payload");

        var project = InstallerProjectFactory.CreateNew("RequiredApp", "1.0.0", "ACME", sourceRoot);
        project.Components.Clear();
        project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = false,
            Files =
            {
                new TheTechIdea.Beep.Installer.FileCopyOperation
                {
                    SourcePath = sourceFile,
                    DestinationPath = "required.dll",
                    IsRequired = true,
                    Overwrite = true
                }
            }
        });

        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new ComponentSelectResourceProvider())
            .Register(new FileCopyResourceProvider()));
        var context = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = Path.Combine(temp.Path, "required-journal.json");

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        File.ReadAllText(Path.Combine(installRoot, "required.dll")).Should().Be("required payload");
        context.TryGetProperty<List<string>>(InstallContextKeys.InstalledFiles)
            .Should().ContainSingle(Path.Combine(installRoot, "required.dll"));
    }

    [Fact]
    public void ResourceProviderStep_SkipsComponentWhenAlwaysFalseConditionFails()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.Path, "src");
        var installRoot = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(installRoot);
        var sourceFile = Path.Combine(sourceRoot, "blocked.dll");
        File.WriteAllText(sourceFile, "blocked payload");

        var project = ProjectWithConditionalFile(sourceRoot, sourceFile, "blocked.dll",
            new TheTechIdea.Beep.Installer.InstallCondition { Type = TheTechIdea.Beep.Installer.ConditionType.AlwaysFalse });

        var context = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = Path.Combine(temp.Path, "condition-journal.json");

        var result = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
                .Register(new ComponentSelectResourceProvider())
                .Register(new FileCopyResourceProvider()))
            .Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        File.Exists(Path.Combine(installRoot, "blocked.dll")).Should().BeFalse();
        context.TryGetProperty<List<string>>(InstallContextKeys.InstalledFiles).Should().BeNull();
        context.Properties[InstallContextKeys.ResourceExecutionJournal].Should().BeOfType<ResourceExecutionJournal>()
            .Subject.Entries.Should().Contain(e => e.OperationType == "file.copy"
                                                   && e.Action == ResourceExecutionAction.Skip);
    }

    [Fact]
    public void ResourceProviderStep_AppliesComponentWhenFileConditionPasses()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.Path, "src");
        var installRoot = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(installRoot);
        var sourceFile = Path.Combine(sourceRoot, "feature.dll");
        File.WriteAllText(sourceFile, "feature payload");

        var project = ProjectWithConditionalFile(sourceRoot, sourceFile, "feature.dll",
            new TheTechIdea.Beep.Installer.InstallCondition
            {
                Type = TheTechIdea.Beep.Installer.ConditionType.FileExists,
                Value = "{InstallPath}\\feature.flag"
            });
        var facts = new FakeConditionFacts();
        facts.Files.Add(Path.Combine(installRoot, "feature.flag"));

        var context = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = Path.Combine(temp.Path, "file-condition-journal.json");
        context.Properties[InstallContextKeys.ResourceProviderRegistry] = new BuiltInResourceProviderRegistry()
            .Register(new ComponentSelectResourceProvider())
            .Register(new FileCopyResourceProvider());
        context.Properties[InstallContextKeys.ResourceConditionFacts] = facts;

        var result = new ResourceProviderStep().Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        File.ReadAllText(Path.Combine(installRoot, "feature.dll")).Should().Be("feature payload");
    }

    [Fact]
    public void ComponentSelectProvider_UsesArchitectureAndAdminFacts()
    {
        var operation = ComponentOperation(selected: true, required: false,
            ("0", TheTechIdea.Beep.Installer.ConditionType.Architecture, "arm64", "==", ""),
            ("1", TheTechIdea.Beep.Installer.ConditionType.IsAdmin, "", "==", ""));
        var facts = new FakeConditionFacts { Architecture = "arm64", IsAdmin = true };
        var provider = new ComponentSelectResourceProvider();

        provider.Plan(operation, provider.Detect(operation, new ResourceProviderContext { ConditionFacts = facts }),
                new ResourceProviderContext { ConditionFacts = facts })
            .ChangeKind.Should().Be(ResourceChangeKind.Create);

        facts.IsAdmin = false;
        provider.Plan(operation, provider.Detect(operation, new ResourceProviderContext { ConditionFacts = facts }),
                new ResourceProviderContext { ConditionFacts = facts })
            .ChangeKind.Should().Be(ResourceChangeKind.None);
    }

    [Fact]
    public void ComponentSelectProvider_UsesAnyConditionExpression()
    {
        var operation = ComponentOperation(true, false, "any",
            ("0", TheTechIdea.Beep.Installer.ConditionType.Architecture, "arm64", "==", ""),
            ("1", TheTechIdea.Beep.Installer.ConditionType.IsAdmin, "", "==", ""));
        var facts = new FakeConditionFacts { Architecture = "x64", IsAdmin = true };
        var provider = new ComponentSelectResourceProvider();

        provider.Plan(operation, provider.Detect(operation, new ResourceProviderContext { ConditionFacts = facts }),
                new ResourceProviderContext { ConditionFacts = facts })
            .ChangeKind.Should().Be(ResourceChangeKind.Create);
    }

    [Fact]
    public void ComponentSelectProvider_UsesNotConditionExpression()
    {
        var operation = ComponentOperation(true, false, "not",
            ("0", TheTechIdea.Beep.Installer.ConditionType.Architecture, "arm64", "==", ""),
            ("1", TheTechIdea.Beep.Installer.ConditionType.IsAdmin, "", "==", ""));
        var facts = new FakeConditionFacts { Architecture = "arm64", IsAdmin = true };
        var provider = new ComponentSelectResourceProvider();

        provider.Plan(operation, provider.Detect(operation, new ResourceProviderContext { ConditionFacts = facts }),
                new ResourceProviderContext { ConditionFacts = facts })
            .ChangeKind.Should().Be(ResourceChangeKind.None);

        facts.IsAdmin = false;
        provider.Plan(operation, provider.Detect(operation, new ResourceProviderContext { ConditionFacts = facts }),
                new ResourceProviderContext { ConditionFacts = facts })
            .ChangeKind.Should().Be(ResourceChangeKind.Create);
    }

    [Fact]
    public void ResourceProviderStep_ExecutesRegistryProvider()
    {
        var store = new FakeRegistryStore();
        var project = InstallerProjectFactory.CreateNew("RegApp", "1.0.0", "ACME", "");
        project.RegistryEntries.Add(new TheTechIdea.Beep.Installer.RegistryOperation
        {
            KeyPath = @"Software\ACME\RegApp",
            ValueName = "InstallPath",
            Value = "%InstallPath%",
            ValueKind = RegistryValueKind.String
        });

        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new RegistryWriteResourceProvider(store)));
        using var temp = new TempDirectory();
        var installRoot = Path.Combine(temp.Path, "install");
        var context = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        var journalPath = Path.Combine(temp.Path, "reg-journal.json");
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        store.ReadValue(InstallerRegistryHive.CurrentUser, @"Software\ACME\RegApp", "InstallPath").Value
            .Should().Be(installRoot);
        context.TryGetProperty<List<TheTechIdea.Beep.Installer.RegistryOperation>>(InstallContextKeys.RegistryEntriesWritten)
            .Should().ContainSingle(r => r.KeyPath == @"Software\ACME\RegApp"
                                         && r.ValueName == "InstallPath"
                                         && r.Value == installRoot);
        var journal = context.Properties[InstallContextKeys.ResourceExecutionJournal]
            .Should().BeOfType<ResourceExecutionJournal>().Subject;
        journal.Entries.Should().Contain(e => e.OperationType == "registry.write"
                                             && e.Action == ResourceExecutionAction.Apply
                                             && e.ResultCode == ResourceProviderResultCode.Succeeded);
        new ResourceExecutionJournalStore(journalPath).Load().Entries
            .Should().Contain(e => e.OperationType == "registry.write"
                                   && e.Action == ResourceExecutionAction.Apply
                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
    }

    [Fact]
    public void ResourceProviderStep_ExecutesEnvironmentProvider()
    {
        using var temp = new TempDirectory();
        var store = new FakeEnvironmentStore();
        var project = InstallerProjectFactory.CreateNew("EnvApp", "1.0.0", "ACME", "");
        project.EnvironmentVariables.Add(new TheTechIdea.Beep.Installer.EnvironmentVariableOp
        {
            Name = "BEEP_TEST_PROVIDER_HOME",
            Value = "{InstallPath}\\bin",
            Scope = EnvironmentVariableTarget.Machine
        });

        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new EnvironmentSetResourceProvider(store)));
        var context = InstallContextBuilder.ForInstall(project, temp.Path, perUser: true);
        var journalPath = Path.Combine(temp.Path, "env-journal.json");
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        store.Read("BEEP_TEST_PROVIDER_HOME", EnvironmentVariableTarget.User)
            .Should().Be(Path.Combine(temp.Path, "bin"));
        context.TryGetProperty<List<TheTechIdea.Beep.Installer.EnvironmentVariableOp>>(InstallContextKeys.EnvVarsSet)
            .Should().ContainSingle(v => v.Name == "BEEP_TEST_PROVIDER_HOME"
                                         && v.Scope == EnvironmentVariableTarget.User
                                         && v.Value == Path.Combine(temp.Path, "bin"));
        new ResourceExecutionJournalStore(journalPath).Load().Entries
            .Should().Contain(e => e.OperationType == "environment.set"
                                   && e.Action == ResourceExecutionAction.Apply
                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
    }

    [Fact]
    public void ResourceProviderStep_ExpandsRuntimeVariablesFromEnterpriseProperties()
    {
        using var temp = new TempDirectory();
        var store = new FakeEnvironmentStore();
        var project = InstallerProjectFactory.CreateNew("EnvApp", "1.0.0", "ACME", "");
        project.EnvironmentVariables.Add(new TheTechIdea.Beep.Installer.EnvironmentVariableOp
        {
            Name = "BEEP_TEST_PROVIDER_TENANT",
            Value = "{Tenant}|%ApiBaseUrl%|%InstallPath%",
            Scope = EnvironmentVariableTarget.User
        });

        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new EnvironmentSetResourceProvider(store)));
        var context = InstallContextBuilder.ForInstall(
            project,
            temp.Path,
            perUser: true,
            customValues: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tenant"] = "acme",
                ["ApiBaseUrl"] = "https://api.example.test",
                ["InstallPath"] = @"C:\spoofed"
            });
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] =
            Path.Combine(temp.Path, "env-properties-journal.json");

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        store.Read("BEEP_TEST_PROVIDER_TENANT", EnvironmentVariableTarget.User)
            .Should().Be($"acme|https://api.example.test|{temp.Path}");
    }

    [Fact]
    public void ResourceProviderStep_ExecutesShortcutProvider()
    {
        using var temp = new TempDirectory();
        var installRoot = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(installRoot);
        var target = Path.Combine(installRoot, "app.exe");
        File.WriteAllText(target, "payload");

        var store = new FakeShortcutStore(temp.Path);
        store.AddExistingFile(target);
        var project = InstallerProjectFactory.CreateNew("ShortcutApp", "1.0.0", "ACME", "");
        project.Shortcuts.Add(new TheTechIdea.Beep.Installer.ShortcutDefinition
        {
            Name = "ShortcutApp",
            TargetPath = "app.exe",
            Location = TheTechIdea.Beep.Installer.ShortcutLocation.StartMenu,
            StartMenuSubfolder = "ACME"
        });

        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new ShortcutCreateResourceProvider(store)));
        var context = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        var journalPath = Path.Combine(temp.Path, "shortcut-journal.json");
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        var linkPath = Path.Combine(store.Folder(Environment.SpecialFolder.Programs), "ACME", "ShortcutApp.lnk");
        store.Read(linkPath).Should().Match<ShortcutSnapshot>(s => s.Exists && s.TargetPath == target);
        context.TryGetProperty<List<TheTechIdea.Beep.Installer.ShortcutDefinition>>(InstallContextKeys.ShortcutsCreated)
            .Should().ContainSingle(s => s.Name == "ShortcutApp"
                                         && s.TargetPath == "app.exe"
                                         && s.StartMenuSubfolder == "ACME");
        new ResourceExecutionJournalStore(journalPath).Load().Entries
            .Should().Contain(e => e.OperationType == "shortcut.create"
                                   && e.Action == ResourceExecutionAction.Apply
                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
    }

    [Fact]
    public void ResourceProviderStep_PersistsJournalWhenProviderApplyFails()
    {
        using var temp = new TempDirectory();
        var sourceRoot = Path.Combine(temp.Path, "src");
        var installRoot = Path.Combine(temp.Path, "install");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(installRoot);
        var sourceFile = Path.Combine(sourceRoot, "app.exe");
        File.WriteAllText(sourceFile, "payload");

        var project = InstallerProjectFactory.CreateNew("BrokenApp", "1.0.0", "ACME", sourceRoot);
        var component = new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "core",
            Name = "Core",
            Required = true,
            Selected = true
        };
        component.Files.Add(new TheTechIdea.Beep.Installer.FileCopyOperation
        {
            SourcePath = sourceFile,
            DestinationPath = "bin/app.exe",
            IsRequired = true,
            Overwrite = true
        });
        project.Components.Add(component);

        var journalPath = Path.Combine(temp.Path, "failed-journal.json");
        var context = InstallContextBuilder.ForInstall(project, installRoot, perUser: true);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = journalPath;
        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new FailingFileCopyProvider()));

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Failed);
        File.Exists(journalPath).Should().BeTrue();
        var loaded = new ResourceExecutionJournalStore(journalPath).Load();
        loaded.Entries.Should().Contain(e => e.OperationType == "file.copy"
                                             && e.Action == ResourceExecutionAction.Apply
                                             && e.ResultCode == ResourceProviderResultCode.Failed);
    }

    private static CompiledInstallOperation Operation(string id, string? dependsOn = null)
        => new()
        {
            Id = id,
            Type = "test.resource",
            DependsOn = dependsOn == null ? new List<string>() : new List<string> { dependsOn },
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = id
            }
        };

    private static InstallProject ProjectWithConditionalFile(
        string sourceRoot,
        string sourceFile,
        string destination,
        TheTechIdea.Beep.Installer.InstallCondition condition)
    {
        var project = InstallerProjectFactory.CreateNew("ConditionalApp", "1.0.0", "ACME", sourceRoot);
        project.Components.Clear();
        project.Components.Add(new TheTechIdea.Beep.Installer.InstallComponent
        {
            Id = "conditional",
            Name = "Conditional",
            Required = false,
            Selected = true,
            Conditions = { condition },
            Files =
            {
                new TheTechIdea.Beep.Installer.FileCopyOperation
                {
                    SourcePath = sourceFile,
                    DestinationPath = destination,
                    IsRequired = true,
                    Overwrite = true
                }
            }
        });
        return project;
    }

    private static CompiledInstallOperation ComponentOperation(
        bool selected,
        bool required,
        params (string Index, TheTechIdea.Beep.Installer.ConditionType Type, string Value, string Operator, string Value2)[] conditions)
        => ComponentOperation(selected, required, "all", conditions);

    private static CompiledInstallOperation ComponentOperation(
        bool selected,
        bool required,
        string conditionExpression,
        params (string Index, TheTechIdea.Beep.Installer.ConditionType Type, string Value, string Operator, string Value2)[] conditions)
    {
        var inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = "feature",
            ["selected"] = selected ? "true" : "false",
            ["required"] = required ? "true" : "false",
            ["conditionExpression"] = conditionExpression,
            ["conditionCount"] = conditions.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        foreach (var condition in conditions)
        {
            inputs[$"condition.{condition.Index}.type"] = condition.Type.ToString();
            inputs[$"condition.{condition.Index}.value"] = condition.Value;
            inputs[$"condition.{condition.Index}.operator"] = condition.Operator;
            inputs[$"condition.{condition.Index}.value2"] = condition.Value2;
        }

        return new CompiledInstallOperation
        {
            Id = "component:feature",
            Type = "component.select",
            Inputs = inputs
        };
    }

    private sealed class RecordingProvider : IResourceProvider
    {
        private readonly string _failApplyId;

        public RecordingProvider(string failApplyId)
        {
            _failApplyId = failApplyId;
        }

        public string ResourceType => "test.resource";
        public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.None;
        public List<string> Actions { get; } = new();

        public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
            => new() { Exists = false };

        public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
            => new();

        public ResourcePlanResult Plan(
            CompiledInstallOperation operation,
            ResourceDetectionResult detection,
            ResourceProviderContext context)
            => new() { ChangeKind = ResourceChangeKind.Create, Operations = { operation } };

        public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
        {
            Actions.Add($"apply:{operation.Id}");
            return operation.Id == _failApplyId
                ? new ResourceProviderResult { Code = ResourceProviderResultCode.Failed, Message = "boom" }
                : new ResourceProviderResult { Message = "ok" };
        }

        public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
        {
            Actions.Add($"rollback:{operation.Id}");
            return new ResourceProviderResult { Message = "rolled back" };
        }

        public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
            => new() { Message = "verified" };
    }

    private sealed class FailingFileCopyProvider : IResourceProvider
    {
        public string ResourceType => "file.copy";
        public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.None;

        public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
            => new() { Exists = false };

        public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
            => new();

        public ResourcePlanResult Plan(
            CompiledInstallOperation operation,
            ResourceDetectionResult detection,
            ResourceProviderContext context)
            => new() { ChangeKind = ResourceChangeKind.Create, Operations = { operation } };

        public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
            => new() { Code = ResourceProviderResultCode.Failed, Message = "simulated apply failure" };

        public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
            => new() { Message = "nothing to roll back" };

        public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
            => new() { Message = "not reached" };
    }

    private sealed class StatefulServiceRunner : IWindowsServiceCommandRunner
    {
        private bool _exists;

        public bool IsSupported => true;
        public List<IReadOnlyList<string>> Calls { get; } = new();

        public WindowsServiceCommandResult Run(IReadOnlyList<string> arguments)
        {
            Calls.Add(arguments.ToArray());
            switch (arguments[0])
            {
                case "query":
                    return _exists
                        ? new WindowsServiceCommandResult(0, "SERVICE_NAME: SvcApp", "")
                        : new WindowsServiceCommandResult(1060, "", "");
                case "create":
                    _exists = true;
                    return new WindowsServiceCommandResult(0, "", "");
                default:
                    return new WindowsServiceCommandResult(0, "", "");
            }
        }
    }

    private sealed class FakeRegistryStore : IInstallerRegistryStore
    {
        private readonly Dictionary<string, RegistryValueSnapshot> _values = new(StringComparer.OrdinalIgnoreCase);

        public bool IsSupported => true;

        public RegistryValueSnapshot ReadValue(InstallerRegistryHive hive, string keyPath, string valueName)
            => _values.TryGetValue(Key(hive, keyPath, valueName), out var value)
                ? value
                : new RegistryValueSnapshot(false, null, RegistryValueKind.Unknown);

        public void WriteValue(InstallerRegistryHive hive, string keyPath, string valueName, object value, RegistryValueKind valueKind)
            => _values[Key(hive, keyPath, valueName)] = new RegistryValueSnapshot(true, value, valueKind);

        public void DeleteValue(InstallerRegistryHive hive, string keyPath, string valueName)
            => _values.Remove(Key(hive, keyPath, valueName));

        public void DeleteKeyTree(InstallerRegistryHive hive, string keyPath)
        {
            var prefix = $"{hive}\\{keyPath.TrimEnd('\\')}\\";
            foreach (var key in _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                _values.Remove(key);
        }

        private static string Key(InstallerRegistryHive hive, string keyPath, string valueName)
            => $"{hive}\\{keyPath}\\{valueName}";
    }

    private sealed class FakeEnvironmentStore : IInstallerEnvironmentStore
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

        public bool IsSupported => true;

        public string? Read(string name, EnvironmentVariableTarget target)
            => _values.TryGetValue(Key(name, target), out var value) ? value : null;

        public void Write(string name, string? value, EnvironmentVariableTarget target)
        {
            if (value == null)
                _values.Remove(Key(name, target));
            else
                _values[Key(name, target)] = value;
        }

        private static string Key(string name, EnvironmentVariableTarget target)
            => $"{target}:{name}";
    }

    private sealed class FakeShortcutStore : IInstallerShortcutStore
    {
        private readonly string _root;
        private readonly Dictionary<string, ShortcutSnapshot> _shortcuts = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

        public FakeShortcutStore(string root)
        {
            _root = root;
        }

        public bool IsSupported => true;

        public string Folder(Environment.SpecialFolder folder)
            => GetKnownFolder(folder);

        public void AddExistingFile(string path)
            => _files.Add(Path.GetFullPath(path));

        public string GetKnownFolder(Environment.SpecialFolder folder)
            => Path.Combine(_root, "known-folders", folder.ToString());

        public bool FileExists(string path)
            => _files.Contains(Path.GetFullPath(path)) || File.Exists(path);

        public ShortcutSnapshot Read(string linkPath)
            => _shortcuts.TryGetValue(Path.GetFullPath(linkPath), out var snapshot)
                ? snapshot
                : new ShortcutSnapshot(false);

        public void Write(string linkPath, string targetPath, string arguments, string workingDirectory, string iconPath)
            => _shortcuts[Path.GetFullPath(linkPath)] = new ShortcutSnapshot(
                true,
                Path.GetFullPath(targetPath),
                arguments,
                workingDirectory,
                iconPath);

        public void Delete(string linkPath)
            => _shortcuts.Remove(Path.GetFullPath(linkPath));
    }

    private sealed class FakeConditionFacts : IInstallerConditionFacts
    {
        public Version OsVersion { get; set; } = new(10, 0);
        public string Architecture { get; set; } = "x64";
        public bool IsAdmin { get; set; }
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> RegistryValues { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, CommandConditionResult> Commands { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool FileExists(string path) => Files.Contains(Path.GetFullPath(path));
        public bool DirectoryExists(string path) => Directories.Contains(Path.GetFullPath(path));
        public bool RegistryKeyExists(string keyPath) => RegistryValues.Keys.Any(k => k.StartsWith(keyPath + "|", StringComparison.OrdinalIgnoreCase));
        public string? RegistryValue(string keyPath, string valueName)
            => RegistryValues.TryGetValue($"{keyPath}|{valueName}", out var value) ? value : null;
        public CommandConditionResult RunCommand(string command)
            => Commands.TryGetValue(command, out var result) ? result : new CommandConditionResult(-1, "");
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"beep-runtime-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
