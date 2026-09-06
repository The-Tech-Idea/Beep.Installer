using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using Beep.Installer.Models;
using Beep.Installer.Steps;
using FluentAssertions;
using TheTechIdea.Beep.ConfigUtil;
using Xunit;

namespace Beep.Installer.Tests;

public class ScheduledTaskResourceTests : IDisposable
{
    private readonly string _tempDir;

    public ScheduledTaskResourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepTask_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Bsetup_RoundTripsScheduledTasks()
    {
        var project = InstallerProjectFactory.CreateNew("TaskApp", "1.0.0", "ACME", "");
        project.ScheduledTasks.Add(new ScheduledTaskDefinition
        {
            Name = "ACME\\TaskApp",
            Description = "Runs TaskApp maintenance",
            ExecutablePath = @"{app}\TaskApp.exe",
            Arguments = "--maintain",
            WorkingDirectory = @"{app}",
            Trigger = ScheduledTaskTrigger.Daily,
            StartTime = "02:15",
            Enabled = false,
            RunElevated = true,
            Username = "SYSTEM",
            StopOnUninstall = true
        });

        var path = Path.Combine(_tempDir, "task.bsetup");
        InstallerScriptSerializer.Save(project, path).ok.Should().BeTrue();

        var (loaded, err) = InstallerScriptSerializer.Load(path);

        err.Should().BeNull();
        loaded!.ScheduledTasks.Should().ContainSingle();
        var task = loaded.ScheduledTasks[0];
        task.Name.Should().Be("ACME\\TaskApp");
        task.ExecutablePath.Should().Be(@"%InstallPath%\TaskApp.exe");
        task.WorkingDirectory.Should().Be("%InstallPath%");
        task.Trigger.Should().Be(ScheduledTaskTrigger.Daily);
        task.StartTime.Should().Be("02:15");
        task.Enabled.Should().BeFalse();
        task.RunElevated.Should().BeTrue();
        task.Username.Should().Be("SYSTEM");
    }

    [Fact]
    public void SchemaValidation_FindsInvalidScheduledTask()
    {
        var project = InstallerProjectFactory.CreateNew("TaskApp", "1.0.0", "ACME", "");
        project.ScheduledTasks.Add(new ScheduledTaskDefinition
        {
            Name = "TaskApp",
            Trigger = ScheduledTaskTrigger.Daily,
            StartTime = "bad"
        });

        var result = ProjectSchemaService.Validate(project, new ProjectSchemaValidationOptions { Strict = true });

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Code == "BI1603");
        result.Diagnostics.Should().Contain(d => d.Code == "BI1604");
    }

    [Fact]
    public void CompiledPlan_EmitsScheduledTaskOperationWithRedactedPassword()
    {
        var project = InstallerProjectFactory.CreateNew("TaskApp", "1.0.0", "ACME", "");
        project.ScheduledTasks.Add(new ScheduledTaskDefinition
        {
            Name = "TaskApp",
            ExecutablePath = @"%InstallPath%\TaskApp.exe",
            Arguments = "--secret abc",
            Trigger = ScheduledTaskTrigger.OnLogon,
            Username = ".\\task-user",
            Password = "plain-secret"
        });

        var result = new InstallPlanCompiler().Compile(project);

        result.Success.Should().BeTrue();
        var operation = result.Plan!.Operations.Single(o => o.Type == "scheduled-task.create");
        operation.Id.Should().Be("scheduled-task:taskapp");
        operation.Inputs["password"].Should().Be("<redacted>");
        operation.SensitiveInputs.Should().Contain("password");
    }

    [Fact]
    public void ScheduledTaskProvider_ApplyCreatesTaskThroughSchtasksRunner()
    {
        var runner = new FakeScheduledTaskCommandRunner(queryExitCode: 1);
        var provider = new ScheduledTaskResourceProvider(runner);

        var result = provider.Apply(TaskOperation(), new ResourceProviderContext { InstallRoot = @"C:\Program Files\ACME" });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        var create = runner.Calls.Single(c => c[0] == "/Create");
        create.Should().ContainInOrder(
            "/Create",
            "/F",
            "/TN",
            "\\ACME\\TaskApp",
            "/TR",
            @"cmd.exe /c cd /d ""C:\Program Files\ACME"" && ""C:\Program Files\ACME\TaskApp.exe"" --maintain",
            "/SC",
            "DAILY",
            "/ST",
            "02:15");
        create.Should().Contain("HIGHEST");
        create.Should().Contain("SYSTEM");
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "/Change", "/TN", "\\ACME\\TaskApp", "/DISABLE" }));
    }

    [Fact]
    public void ScheduledTaskProvider_RollbackEndsAndDeletesExistingTask()
    {
        var runner = new FakeScheduledTaskCommandRunner(queryExitCode: 0);
        var provider = new ScheduledTaskResourceProvider(runner);

        var result = provider.Rollback(TaskOperation(), new ResourceProviderContext { InstallRoot = _tempDir });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "/Query", "/TN", "\\ACME\\TaskApp" }));
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "/End", "/TN", "\\ACME\\TaskApp" }));
        runner.Calls.Should().Contain(c => c.SequenceEqual(new[] { "/Delete", "/TN", "\\ACME\\TaskApp", "/F" }));
    }

    [Fact]
    public void ResourceProviderStep_ExecutesScheduledTaskProvider()
    {
        var project = InstallerProjectFactory.CreateNew("TaskApp", "1.0.0", "ACME", "");
        project.ScheduledTasks.Add(new ScheduledTaskDefinition
        {
            Name = "TaskApp",
            ExecutablePath = @"%InstallPath%\TaskApp.exe",
            Trigger = ScheduledTaskTrigger.OnLogon
        });
        var runner = new FakeScheduledTaskCommandRunner(queryExitCode: 1);
        var context = InstallContextBuilder.ForInstall(project, _tempDir, perUser: true);
        context.Properties[InstallContextKeys.ResourceExecutionJournalPath] = Path.Combine(_tempDir, "task-journal.json");
        var step = new ResourceProviderStep(registry: new BuiltInResourceProviderRegistry()
            .Register(new ScheduledTaskResourceProvider(runner)));

        var result = step.Execute(context);

        result.Flag.Should().Be(Errors.Ok);
        runner.Calls.Should().Contain(c => c[0] == "/Create");
        context.Properties[InstallContextKeys.ResourceExecutionJournal]
            .Should().BeOfType<ResourceExecutionJournal>().Subject.Entries
            .Should().Contain(e => e.OperationType == "scheduled-task.create"
                                   && e.Action == ResourceExecutionAction.Apply
                                   && e.ResultCode == ResourceProviderResultCode.Succeeded);
    }

    private static CompiledInstallOperation TaskOperation()
        => new()
        {
            Id = "scheduled-task:taskapp",
            Type = "scheduled-task.create",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "ACME\\TaskApp",
                ["executablePath"] = @"%InstallPath%\TaskApp.exe",
                ["arguments"] = "--maintain",
                ["workingDirectory"] = @"%InstallPath%",
                ["trigger"] = "Daily",
                ["startTime"] = "02:15",
                ["enabled"] = "false",
                ["runElevated"] = "true",
                ["username"] = "SYSTEM",
                ["stopOnUninstall"] = "true"
            }
        };

    private sealed class FakeScheduledTaskCommandRunner : IScheduledTaskCommandRunner
    {
        private bool _exists;

        public FakeScheduledTaskCommandRunner(int queryExitCode)
        {
            _exists = queryExitCode == 0;
        }

        public bool IsSupported => true;
        public List<IReadOnlyList<string>> Calls { get; } = new();

        public ScheduledTaskCommandResult Run(IReadOnlyList<string> arguments)
        {
            Calls.Add(arguments.ToArray());
            switch (arguments[0])
            {
                case "/Query":
                    return _exists
                        ? new ScheduledTaskCommandResult(0, "TaskName: \\ACME\\TaskApp", "")
                        : new ScheduledTaskCommandResult(1, "", "The system cannot find the file specified.");
                case "/Create":
                    _exists = true;
                    return new ScheduledTaskCommandResult(0, "", "");
                case "/Delete":
                    _exists = false;
                    return new ScheduledTaskCommandResult(0, "", "");
                default:
                    return new ScheduledTaskCommandResult(0, "", "");
            }
        }
    }
}
