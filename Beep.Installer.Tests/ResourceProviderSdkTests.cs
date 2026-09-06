using System.Net;
using System.Net.Sockets;
using System.Text;
using Beep.Installer.Engine;
using Beep.Installer.Deployment;
using Beep.Installer.Extensibility;
using Beep.Installer.Extensibility.Providers;
using Beep.Installer.Models;
using FluentAssertions;
using Microsoft.Win32;
using Xunit;

namespace Beep.Installer.Tests;

public class ResourceProviderSdkTests
{
    [Fact]
    public void Registry_RejectsDuplicateResourceTypes()
    {
        var registry = new BuiltInResourceProviderRegistry()
            .Register(new FileCopyResourceProvider());

        var act = () => registry.Register(new FileCopyResourceProvider());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*file.copy*");
    }

    [Fact]
    public void FileCopyProvider_DetectsExistingDestination()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "app.exe"), "payload");
        var operation = new CompiledInstallOperation
        {
            Id = "file:core:app.exe",
            Type = "file.copy",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "bin/app.exe",
                ["destination"] = "app.exe"
            }
        };

        var provider = new FileCopyResourceProvider();
        var result = provider.Detect(operation, new ResourceProviderContext { InstallRoot = temp.Path });

        result.Exists.Should().BeTrue();
        result.Facts["destination"].Should().Be(Path.Combine(temp.Path, "app.exe"));
    }

    [Fact]
    public void FileCopyProvider_AppliesNewFileAndRollbackDeletesIt()
    {
        using var source = new TempDirectory();
        using var install = new TempDirectory();
        var sourcePath = Path.Combine(source.Path, "app.exe");
        File.WriteAllText(sourcePath, "new payload");
        var operation = FileOperation(sourcePath, "bin/app.exe");
        var provider = new FileCopyResourceProvider();

        var apply = provider.Apply(operation, new ResourceProviderContext { InstallRoot = install.Path });

        apply.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        File.ReadAllText(Path.Combine(install.Path, "bin", "app.exe")).Should().Be("new payload");

        var rollback = provider.Rollback(operation, new ResourceProviderContext { InstallRoot = install.Path });

        rollback.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        File.Exists(Path.Combine(install.Path, "bin", "app.exe")).Should().BeFalse();
    }

    [Fact]
    public void FileCopyProvider_OverwritesExistingFileAndRollbackRestoresBackup()
    {
        using var source = new TempDirectory();
        using var install = new TempDirectory();
        var sourcePath = Path.Combine(source.Path, "app.exe");
        var destinationPath = Path.Combine(install.Path, "app.exe");
        File.WriteAllText(sourcePath, "new payload");
        File.WriteAllText(destinationPath, "old payload");
        var operation = FileOperation(sourcePath, "app.exe");
        var provider = new FileCopyResourceProvider();

        provider.Apply(operation, new ResourceProviderContext { InstallRoot = install.Path })
            .Code.Should().Be(ResourceProviderResultCode.Succeeded);
        File.ReadAllText(destinationPath).Should().Be("new payload");

        provider.Rollback(operation, new ResourceProviderContext { InstallRoot = install.Path })
            .Code.Should().Be(ResourceProviderResultCode.Succeeded);
        File.ReadAllText(destinationPath).Should().Be("old payload");
    }

    [Fact]
    public void FileCopyProvider_SkipsMissingOptionalSource()
    {
        using var install = new TempDirectory();
        var operation = FileOperation(Path.Combine(install.Path, "missing.txt"), "missing.txt", required: false);
        var provider = new FileCopyResourceProvider();

        var result = provider.Apply(operation, new ResourceProviderContext { InstallRoot = install.Path });

        result.Code.Should().Be(ResourceProviderResultCode.Skipped);
    }

    [Fact]
    public void FileCopyProvider_RejectsDestinationOutsideInstallRoot()
    {
        using var source = new TempDirectory();
        using var install = new TempDirectory();
        var sourcePath = Path.Combine(source.Path, "app.exe");
        File.WriteAllText(sourcePath, "payload");
        var outside = Path.Combine(Path.GetTempPath(), $"beep-outside-{Guid.NewGuid():N}.exe");
        var operation = FileOperation(sourcePath, outside);

        var result = new FileCopyResourceProvider()
            .Validate(operation, new ResourceProviderContext { InstallRoot = install.Path });

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI3004");
    }

    [Fact]
    public void RegistryWriteProvider_WritesAndVerifiesValue()
    {
        var store = new FakeRegistryStore();
        var operation = RegistryOperation(@"Software\ACME\App", "InstallPath", "%InstallPath%", "ExpandString");
        var provider = new RegistryWriteResourceProvider(store);

        var apply = provider.Apply(operation, new ResourceProviderContext { InstallRoot = @"C:\Program Files\ACME", PerUser = true });
        var verify = provider.Verify(operation, new ResourceProviderContext { InstallRoot = @"C:\Program Files\ACME", PerUser = true });

        apply.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        verify.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.ReadValue(InstallerRegistryHive.CurrentUser, @"Software\ACME\App", "InstallPath")
            .Should().Be(new RegistryValueSnapshot(true, @"C:\Program Files\ACME", RegistryValueKind.ExpandString));
    }

    [Fact]
    public void RegistryWriteProvider_OverwritesAndRollbackRestoresPriorValue()
    {
        var store = new FakeRegistryStore();
        store.WriteValue(InstallerRegistryHive.LocalMachine, @"Software\ACME\App", "Version", "1.0.0", RegistryValueKind.String);
        var operation = RegistryOperation(@"Software\ACME\App", "Version", "2.0.0", "String");
        var provider = new RegistryWriteResourceProvider(store);

        provider.Apply(operation, new ResourceProviderContext { InstallRoot = @"C:\App", PerUser = false })
            .Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.ReadValue(InstallerRegistryHive.LocalMachine, @"Software\ACME\App", "Version").Value
            .Should().Be("2.0.0");

        provider.Rollback(operation, new ResourceProviderContext { InstallRoot = @"C:\App", PerUser = false })
            .Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.ReadValue(InstallerRegistryHive.LocalMachine, @"Software\ACME\App", "Version").Value
            .Should().Be("1.0.0");
    }

    [Fact]
    public void RegistryWriteProvider_RollbackDeletesCreatedValue()
    {
        var store = new FakeRegistryStore();
        var operation = RegistryOperation(@"Software\ACME\App", "Created", "1", "DWord");
        var provider = new RegistryWriteResourceProvider(store);

        provider.Apply(operation, new ResourceProviderContext { InstallRoot = @"C:\App", PerUser = true })
            .Code.Should().Be(ResourceProviderResultCode.Succeeded);
        provider.Rollback(operation, new ResourceProviderContext { InstallRoot = @"C:\App", PerUser = true })
            .Code.Should().Be(ResourceProviderResultCode.Succeeded);

        store.ReadValue(InstallerRegistryHive.CurrentUser, @"Software\ACME\App", "Created").Exists
            .Should().BeFalse();
    }

    [Fact]
    public void RegistryWriteProvider_RejectsHiveQualifiedKeyPath()
    {
        var operation = RegistryOperation(@"HKLM\Software\ACME\App", "Version", "1", "String");

        var result = new RegistryWriteResourceProvider(new FakeRegistryStore())
            .Validate(operation, new ResourceProviderContext { InstallRoot = @"C:\App" });

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI3102");
    }

    [Fact]
    public void EnvironmentSetProvider_WritesVerifiesAndRollsBackCreatedVariable()
    {
        var store = new FakeEnvironmentStore();
        var operation = EnvironmentOperation("BEEP_TEST_HOME", "{InstallPath}\\bin", "User");
        var provider = new EnvironmentSetResourceProvider(store);
        var context = new ResourceProviderContext { InstallRoot = @"C:\Apps\Beep", PerUser = true };

        provider.Apply(operation, context).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        provider.Verify(operation, context).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.Read("BEEP_TEST_HOME", EnvironmentVariableTarget.User).Should().Be(@"C:\Apps\Beep\bin");

        provider.Rollback(operation, context).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.Read("BEEP_TEST_HOME", EnvironmentVariableTarget.User).Should().BeNull();
    }

    [Fact]
    public void EnvironmentSetProvider_DowngradesMachineScopeForPerUserInstall()
    {
        var store = new FakeEnvironmentStore();
        var operation = EnvironmentOperation("BEEP_TEST_SCOPE", "value", "Machine");
        var provider = new EnvironmentSetResourceProvider(store);

        provider.Apply(operation, new ResourceProviderContext { InstallRoot = @"C:\App", PerUser = true })
            .Code.Should().Be(ResourceProviderResultCode.Succeeded);

        store.Read("BEEP_TEST_SCOPE", EnvironmentVariableTarget.User).Should().Be("value");
        store.Read("BEEP_TEST_SCOPE", EnvironmentVariableTarget.Machine).Should().BeNull();
    }

    [Fact]
    public void EnvironmentSetProvider_RollbackRestoresPriorValue()
    {
        var store = new FakeEnvironmentStore();
        store.Write("BEEP_TEST_PRIOR", "old", EnvironmentVariableTarget.User);
        var operation = EnvironmentOperation("BEEP_TEST_PRIOR", "new", "User");
        var provider = new EnvironmentSetResourceProvider(store);
        var context = new ResourceProviderContext { InstallRoot = @"C:\App", PerUser = true };

        provider.Apply(operation, context).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.Read("BEEP_TEST_PRIOR", EnvironmentVariableTarget.User).Should().Be("new");

        provider.Rollback(operation, context).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.Read("BEEP_TEST_PRIOR", EnvironmentVariableTarget.User).Should().Be("old");
    }

    [Fact]
    public void ShortcutCreateProvider_WritesVerifiesAndRollsBackCreatedShortcut()
    {
        using var temp = new TempDirectory();
        var store = new FakeShortcutStore(temp.Path);
        var targetPath = Path.Combine(temp.Path, "install", "app.exe");
        store.AddExistingFile(targetPath);
        var operation = ShortcutOperation("Beep App", "app.exe", "StartMenu", startMenuSubfolder: "Tools");
        var provider = new ShortcutCreateResourceProvider(store);
        var context = new ResourceProviderContext
        {
            InstallRoot = Path.Combine(temp.Path, "install"),
            ProductName = "Beep App",
            PerUser = true
        };

        provider.Apply(operation, context).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        provider.Verify(operation, context).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        var linkPath = Path.Combine(store.Folder(Environment.SpecialFolder.Programs), "Tools", "Beep App.lnk");
        store.Read(linkPath).Should().Match<ShortcutSnapshot>(s => s.Exists && s.TargetPath == targetPath);

        provider.Rollback(operation, context).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.Read(linkPath).Exists.Should().BeFalse();
    }

    [Fact]
    public void ShortcutCreateProvider_UsesProductNameAsDefaultStartMenuFolder()
    {
        using var temp = new TempDirectory();
        var store = new FakeShortcutStore(temp.Path);
        var installRoot = Path.Combine(temp.Path, "install");
        store.AddExistingFile(Path.Combine(installRoot, "app.exe"));
        var operation = ShortcutOperation("Beep App", "app.exe", "StartMenu");
        var provider = new ShortcutCreateResourceProvider(store);

        provider.Apply(operation, new ResourceProviderContext
            {
                InstallRoot = installRoot,
                ProductName = "Beep Product",
                PerUser = true
            })
            .Code.Should().Be(ResourceProviderResultCode.Succeeded);

        var linkPath = Path.Combine(store.Folder(Environment.SpecialFolder.Programs), "Beep Product", "Beep App.lnk");
        store.Read(linkPath).Exists.Should().BeTrue();
    }

    [Fact]
    public void ShortcutCreateProvider_RollbackRestoresPriorShortcut()
    {
        using var temp = new TempDirectory();
        var store = new FakeShortcutStore(temp.Path);
        var installRoot = Path.Combine(temp.Path, "install");
        var oldTarget = Path.Combine(temp.Path, "old", "app.exe");
        var newTarget = Path.Combine(installRoot, "app.exe");
        store.AddExistingFile(newTarget);
        var linkPath = Path.Combine(store.Folder(Environment.SpecialFolder.DesktopDirectory), "Beep App.lnk");
        store.Write(linkPath, oldTarget, "--old", Path.GetDirectoryName(oldTarget)!, oldTarget);
        var operation = ShortcutOperation("Beep App", "app.exe", "Desktop");
        var provider = new ShortcutCreateResourceProvider(store);
        var context = new ResourceProviderContext { InstallRoot = installRoot, ProductName = "Beep", PerUser = true };

        provider.Apply(operation, context).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.Read(linkPath).TargetPath.Should().Be(newTarget);

        provider.Rollback(operation, context).Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.Read(linkPath).Should().Match<ShortcutSnapshot>(s => s.Exists
                                                                  && s.TargetPath == oldTarget
                                                                  && s.Arguments == "--old");
    }

    [Fact]
    public void ShortcutCreateProvider_SkipsWhenTargetDoesNotExist()
    {
        using var temp = new TempDirectory();
        var store = new FakeShortcutStore(temp.Path);
        var operation = ShortcutOperation("Missing", "missing.exe", "Desktop");

        var result = new ShortcutCreateResourceProvider(store)
            .Apply(operation, new ResourceProviderContext { InstallRoot = temp.Path, ProductName = "Beep", PerUser = true });

        result.Code.Should().Be(ResourceProviderResultCode.Skipped);
    }

    [Fact]
    public void PackageInstallProvider_SkipsAlreadyDetectedPackage()
    {
        var runner = new FakePackageRunner();
        runner.Results.Enqueue(new PackageCommandResult(0, "runtime installed", ""));
        var provider = new PackageInstallResourceProvider(runner, new FakePackageAcquisitionStore());
        var operation = PackageOperation();

        var result = provider.Apply(operation, new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Skipped);
        runner.Invocations.Should().ContainSingle(i => i.FileName == "detect-runtime");
    }

    [Fact]
    public void PackageInstallProvider_AcquiresInstallsAndVerifiesMissingPackage()
    {
        var runner = new FakePackageRunner();
        runner.Results.Enqueue(new PackageCommandResult(1, "", ""));
        runner.Results.Enqueue(new PackageCommandResult(0, "installed", ""));
        runner.Results.Enqueue(new PackageCommandResult(0, "runtime installed", ""));
        var store = new FakePackageAcquisitionStore();
        var provider = new PackageInstallResourceProvider(runner, store);
        var operation = PackageOperation();

        var apply = provider.Apply(operation, new ResourceProviderContext());
        var verify = provider.Verify(operation, new ResourceProviderContext());

        apply.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        verify.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        store.AcquiredSources.Should().ContainSingle("https://example.test/runtime.exe");
        runner.Invocations.Should().Contain(i => i.FileName.EndsWith("runtime.exe", StringComparison.OrdinalIgnoreCase)
                                                 && i.Arguments == "/install /quiet /norestart");
    }

    [Fact]
    public void PackageInstallProvider_ReusesVerifiedRemoteCacheWithoutAcquiring()
    {
        using var temp = new TempDirectory();
        var packagePath = System.IO.Path.Combine(temp.Path, "runtime.exe");
        File.WriteAllText(packagePath, "cached payload");
        var operation = PackageOperation();
        operation.Inputs["sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(packagePath))).ToLowerInvariant();
        operation.Inputs.Remove("detectionCommand");
        operation.Inputs.Remove("detectionPattern");
        var runner = new FakePackageRunner();
        runner.Results.Enqueue(new PackageCommandResult(0, "installed", ""));
        var store = new FakePackageAcquisitionStore(packagePath);
        var provider = new PackageInstallResourceProvider(runner, store);

        var result = provider.Apply(operation, new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        result.Evidence["acquisition.status"].Should().Be("cache-hit");
        result.Evidence["acquisition.cacheHit"].Should().Be("true");
        result.Evidence["acquisition.attempts"].Should().Be("0");
        result.Evidence["hash.verified"].Should().Be("true");
        store.AcquiredSources.Should().BeEmpty();
        runner.Invocations.Should().ContainSingle(i => i.FileName == packagePath);
    }

    [Fact]
    public void PackageInstallProvider_RedownloadsRemoteCacheWhenHashDoesNotMatch()
    {
        using var temp = new TempDirectory();
        var stalePath = System.IO.Path.Combine(temp.Path, "runtime.exe");
        var freshPath = System.IO.Path.Combine(temp.Path, "fresh-runtime.exe");
        File.WriteAllText(stalePath, "stale payload");
        File.WriteAllText(freshPath, "fresh payload");
        var operation = PackageOperation();
        operation.Inputs["sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(freshPath))).ToLowerInvariant();
        operation.Inputs.Remove("detectionCommand");
        operation.Inputs.Remove("detectionPattern");
        var runner = new FakePackageRunner();
        runner.Results.Enqueue(new PackageCommandResult(0, "installed", ""));
        var store = new FakePackageAcquisitionStore(stalePath, freshPath);
        var provider = new PackageInstallResourceProvider(runner, store);

        var result = provider.Apply(operation, new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        result.Evidence["acquisition.cacheHit"].Should().Be("false");
        result.Evidence["acquisition.cacheRejected"].Should().Be("hash-mismatch");
        result.Evidence["acquisition.status"].Should().Be("succeeded");
        result.Evidence["acquisition.path"].Should().Be(freshPath);
        result.Evidence["hash.verified"].Should().Be("true");
        store.AcquiredSources.Should().ContainSingle("https://example.test/runtime.exe");
        runner.Invocations.Should().ContainSingle(i => i.FileName == freshPath);
    }

    [Fact]
    public void PackageInstallProvider_PreservesStaleCacheEvidenceWhenReacquireFails()
    {
        using var temp = new TempDirectory();
        var stalePath = System.IO.Path.Combine(temp.Path, "runtime.exe");
        var partialPath = stalePath + ".partial";
        File.WriteAllText(stalePath, "stale payload");
        File.WriteAllText(partialPath, "partial");
        var operation = PackageOperation();
        operation.Inputs["sha256"] = new string('a', 64);
        operation.Inputs.Remove("detectionCommand");
        operation.Inputs.Remove("detectionPattern");
        var runner = new FakePackageRunner();
        var provider = new PackageInstallResourceProvider(
            runner,
            new FailingPackageAcquisitionStore(stalePath, partialPath));

        var result = provider.Apply(operation, new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI4305");
        result.Evidence["acquisition.cacheHit"].Should().Be("false");
        result.Evidence["acquisition.cacheRejected"].Should().Be("hash-mismatch");
        result.Evidence["acquisition.status"].Should().Be("failed");
        result.Evidence["acquisition.attempts"].Should().Be("2");
        result.Evidence["acquisition.resumed"].Should().Be("true");
        result.Evidence["acquisition.resumeOffsetBytes"].Should().Be("7");
        result.Evidence["acquisition.partialPath"].Should().Be(partialPath);
        result.Evidence["hash.verified"].Should().Be("false");
        runner.Invocations.Should().BeEmpty();
    }

    [Fact]
    public void PackageInstallProvider_MapsRebootExitCode()
    {
        var runner = new FakePackageRunner();
        runner.Results.Enqueue(new PackageCommandResult(1, "", ""));
        runner.Results.Enqueue(new PackageCommandResult(3010, "reboot", ""));
        var provider = new PackageInstallResourceProvider(runner, new FakePackageAcquisitionStore());

        var result = provider.Apply(PackageOperation(), new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.RebootRequired);
    }

    [Fact]
    public void PackageInstallProvider_VerifiesAuthoredSha256BeforeExecuting()
    {
        using var temp = new TempDirectory();
        var packagePath = System.IO.Path.Combine(temp.Path, "runtime.exe");
        File.WriteAllText(packagePath, "payload");
        var operation = PackageOperation();
        operation.Inputs["sourcePath"] = packagePath;
        operation.Inputs["sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(packagePath))).ToLowerInvariant();
        operation.Inputs.Remove("downloadUrl");
        operation.Inputs.Remove("detectionCommand");
        operation.Inputs.Remove("detectionPattern");
        var runner = new FakePackageRunner();
        runner.Results.Enqueue(new PackageCommandResult(0, "installed", ""));
        var provider = new PackageInstallResourceProvider(runner, new FakePackageAcquisitionStore(packagePath));

        var result = provider.Apply(operation, new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        result.Evidence["acquisition.status"].Should().Be("succeeded");
        result.Evidence["hash.verified"].Should().Be("true");
        result.Evidence["process.exitCode"].Should().Be("0");
        runner.Invocations.Should().ContainSingle(i => i.FileName == packagePath);
    }

    [Fact]
    public void PackageInstallProvider_JournalsAcquisitionAndHashEvidence()
    {
        using var temp = new TempDirectory();
        var packagePath = System.IO.Path.Combine(temp.Path, "runtime.exe");
        File.WriteAllText(packagePath, "payload");
        var operation = PackageOperation();
        operation.Inputs["sourcePath"] = packagePath;
        operation.Inputs["sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(packagePath))).ToLowerInvariant();
        operation.Inputs.Remove("downloadUrl");
        operation.Inputs.Remove("detectionCommand");
        operation.Inputs.Remove("detectionPattern");
        var runner = new FakePackageRunner();
        runner.Results.Enqueue(new PackageCommandResult(0, "installed", ""));
        var registry = new BuiltInResourceProviderRegistry()
            .Register(new PackageInstallResourceProvider(runner, new FakePackageAcquisitionStore(packagePath)));
        var executor = new ResourcePlanExecutor(registry);
        var plan = new CompiledInstallPlan
        {
            ProductName = "Runtime Host",
            ProductVersion = "1.0.0",
            InstallScope = "perMachine",
            Operations = new List<CompiledInstallOperation> { operation }
        };

        var result = executor.ExecuteInstall(plan, new ResourceProviderContext { ProductName = "Runtime Host", ProductVersion = "1.0.0" });

        result.Succeeded.Should().BeTrue();
        var apply = result.Journal.Entries.Should().ContainSingle(e => e.Action == ResourceExecutionAction.Apply).Subject;
        apply.Evidence["acquisition.status"].Should().Be("succeeded");
        apply.Evidence["acquisition.path"].Should().Be(packagePath);
        apply.Evidence["hash.actualSha256"].Should().Be(operation.Inputs["sha256"]);
        apply.Evidence["hash.verified"].Should().Be("true");
        apply.Evidence["process.exitCode"].Should().Be("0");
    }

    [Fact]
    public void PackageInstallProvider_RejectsSha256Mismatch()
    {
        using var temp = new TempDirectory();
        var packagePath = System.IO.Path.Combine(temp.Path, "runtime.exe");
        File.WriteAllText(packagePath, "payload");
        var operation = PackageOperation();
        operation.Inputs["sourcePath"] = packagePath;
        operation.Inputs["sha256"] = new string('0', 64);
        operation.Inputs.Remove("downloadUrl");
        operation.Inputs.Remove("detectionCommand");
        operation.Inputs.Remove("detectionPattern");
        var runner = new FakePackageRunner();
        var provider = new PackageInstallResourceProvider(runner, new FakePackageAcquisitionStore(packagePath));

        var result = provider.Apply(operation, new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI4307");
        runner.Invocations.Should().BeEmpty();
    }

    [Fact]
    public void PackageInstallProvider_JournalsAcquisitionFailureResumeEvidence()
    {
        using var temp = new TempDirectory();
        var packagePath = System.IO.Path.Combine(temp.Path, "runtime.exe");
        var partialPath = packagePath + ".partial";
        File.WriteAllText(partialPath, "partial");
        var operation = PackageOperation();
        operation.Inputs.Remove("detectionCommand");
        operation.Inputs.Remove("detectionPattern");
        var runner = new FakePackageRunner();
        var provider = new PackageInstallResourceProvider(
            runner,
            new FailingPackageAcquisitionStore(packagePath, partialPath));

        var result = provider.Apply(operation, new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "BI4305");
        result.Evidence["acquisition.status"].Should().Be("failed");
        result.Evidence["acquisition.attempts"].Should().Be("2");
        result.Evidence["acquisition.resumed"].Should().Be("true");
        result.Evidence["acquisition.resumeOffsetBytes"].Should().Be("7");
        result.Evidence["acquisition.partialPath"].Should().Be(partialPath);
        runner.Invocations.Should().BeEmpty();
    }

    [Fact]
    public void PackageInstallProvider_VerifiesAuthoredSha512BeforeExecuting()
    {
        using var temp = new TempDirectory();
        var packagePath = System.IO.Path.Combine(temp.Path, "runtime.exe");
        File.WriteAllText(packagePath, "payload");
        var operation = PackageOperation();
        operation.Inputs["sourcePath"] = packagePath;
        operation.Inputs["sha512"] = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(File.ReadAllBytes(packagePath))).ToLowerInvariant();
        operation.Inputs.Remove("downloadUrl");
        operation.Inputs.Remove("detectionCommand");
        operation.Inputs.Remove("detectionPattern");
        var runner = new FakePackageRunner();
        runner.Results.Enqueue(new PackageCommandResult(0, "installed", ""));
        var provider = new PackageInstallResourceProvider(runner, new FakePackageAcquisitionStore(packagePath));

        var result = provider.Apply(operation, new ResourceProviderContext());

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        result.Evidence["hash.algorithm"].Should().Be("SHA-512");
        result.Evidence["hash.actualSha512"].Should().Be(operation.Inputs["sha512"]);
        result.Evidence["hash.verified"].Should().Be("true");
        runner.Invocations.Should().ContainSingle(i => i.FileName == packagePath);
    }

    [Fact]
    public void PackageInstallProvider_UsesX86DetectionMetadataWhenArchitectureIsX86()
    {
        var runner = new FakePackageRunner();
        runner.Results.Enqueue(new PackageCommandResult(0, "x86 runtime installed", ""));
        var provider = new PackageInstallResourceProvider(runner, new FakePackageAcquisitionStore());
        var operation = PackageOperation();
        operation.Inputs["detectionCommand"] = "detect-x64-runtime";
        operation.Inputs["detectionCommandX86"] = "detect-x86-runtime";
        operation.Inputs["detectionPattern"] = "x64 runtime installed";
        operation.Inputs["detectionPatternX86"] = "x86 runtime installed";

        var result = provider.Detect(operation, new ResourceProviderContext
        {
            Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Architecture"] = "x86"
            }
        });

        result.Exists.Should().BeTrue();
        result.Facts["detectionCommand"].Should().Be("detect-x86-runtime");
        result.Facts["detectionPattern"].Should().Be("x86 runtime installed");
        runner.Invocations.Should().ContainSingle(i => i.FileName == "detect-x86-runtime");
    }

    [Fact]
    public void PackageInstallProvider_UsesOfflineLayoutBlobWhenAvailable()
    {
        using var temp = new TempDirectory();
        var packagePath = System.IO.Path.Combine(temp.Path, "runtime.exe");
        File.WriteAllText(packagePath, "offline payload");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA512.HashData(File.ReadAllBytes(packagePath))).ToLowerInvariant();
        var project = InstallerProjectFactory.CreateNew("OfflineRuntime", "1.0.0", "ACME", temp.Path);
        project.Packages.Add(new PackageNodeDefinition
        {
            Id = "runtime",
            SourcePath = "runtime.exe",
            Sha512 = hash,
            InstallArgs = "/quiet"
        });
        var layoutResult = new OfflineLayoutBuilder().Build(project, System.IO.Path.Combine(temp.Path, "layout"), new OfflineLayoutOptions
        {
            BaseDirectory = temp.Path
        });
        var operation = PackageOperation();
        operation.Inputs["id"] = "runtime";
        operation.Inputs["downloadUrl"] = "https://downloads.example.test/runtime.exe";
        operation.Inputs["sha512"] = hash;
        operation.Inputs.Remove("detectionCommand");
        operation.Inputs.Remove("detectionPattern");
        var runner = new FakePackageRunner();
        runner.Results.Enqueue(new PackageCommandResult(0, "installed", ""));
        var provider = new PackageInstallResourceProvider(runner, new PackageAcquisitionStore());

        var result = provider.Apply(operation, new ResourceProviderContext
        {
            ProductName = "OfflineRuntime",
            Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["OfflineLayoutDirectory"] = layoutResult.LayoutDirectory
            }
        });

        result.Code.Should().Be(ResourceProviderResultCode.Succeeded);
        result.Evidence["acquisition.sourceKind"].Should().Be("offline-layout");
        result.Evidence["acquisition.source"].Should().Contain("blobs");
        result.Evidence["acquisition.offlineLayout"].Should().Be("true");
        result.Evidence["acquisition.contentAddress"].Should().Be($"sha512:{hash}");
        result.Evidence["acquisition.offlineActualHash"].Should().Be(hash);
        result.Evidence["acquisition.cacheHit"].Should().Be("true");
        result.Evidence["acquisition.offlineInventoryPath"].Should().EndWith(OfflineLayoutBuilder.InventoryFileName);
        result.Evidence["hash.actualSha512"].Should().Be(hash);
        runner.Invocations.Should().ContainSingle(i => i.FileName.Contains("blobs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PackageAcquisitionStore_ResumesPartialHttpDownloadWithRangeRequest()
    {
        using var temp = new TempDirectory();
        var packagePath = System.IO.Path.Combine(temp.Path, "runtime.exe");
        var payload = Encoding.UTF8.GetBytes("partial-download-resumed");
        File.WriteAllBytes(packagePath + ".partial", payload[..8]);
        using var server = new SingleResponseHttpServer(payload[8..], "bytes=8-");

        var acquired = new PackageAcquisitionStore()
            .Acquire(server.Url, packagePath, retryCount: 0);

        acquired.Path.Should().Be(packagePath);
        acquired.Status.Should().Be("succeeded");
        acquired.Resumed.Should().BeTrue();
        acquired.ResumeOffsetBytes.Should().Be(8);
        acquired.Attempts.Should().Be(1);
        File.ReadAllBytes(packagePath).Should().Equal(payload);
        File.Exists(packagePath + ".partial").Should().BeFalse();
        server.RangeHeader.Should().Be("Range: bytes=8-");
        server.Dispose();
    }

    [Fact]
    public void PackageAcquisitionStore_EnforcesSizeLimitBeforePublishing()
    {
        using var temp = new TempDirectory();
        var packagePath = Path.Combine(temp.Path, "limited.bin");
        using var server = new SingleResponseHttpServer(Encoding.UTF8.GetBytes("too large"), "bytes=0-");
        Action acquire = () => new PackageAcquisitionStore().Acquire(server.Url, packagePath, 0,
            CancellationToken.None, 3, allowRedirects: false);
        acquire.Should().Throw<PackageAcquisitionException>().WithInnerException<IOException>();
        File.Exists(packagePath).Should().BeFalse();
        File.Exists(packagePath + ".partial").Should().BeFalse();
    }

    [Fact]
    public void PackageAcquisitionStore_CancellationPreventsNetworkAndFileCreation()
    {
        using var temp = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action acquire = () => new PackageAcquisitionStore().Acquire("http://127.0.0.1:1/package", Path.Combine(temp.Path, "package"),
            2, cancellation.Token, 100, allowRedirects: false);
        acquire.Should().Throw<OperationCanceledException>();
        Directory.GetFiles(temp.Path).Should().BeEmpty();
    }

    private static CompiledInstallOperation FileOperation(string source, string destination, bool required = true)
        => new()
        {
            Id = $"file:{Guid.NewGuid():N}",
            Type = "file.copy",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = source,
                ["destination"] = destination,
                ["required"] = required ? "true" : "false",
                ["overwrite"] = "true"
            }
        };

    private static CompiledInstallOperation RegistryOperation(
        string keyPath,
        string valueName,
        string value,
        string valueKind)
        => new()
        {
            Id = $"registry:{Guid.NewGuid():N}",
            Type = "registry.write",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["keyPath"] = keyPath,
                ["valueName"] = valueName,
                ["value"] = value,
                ["valueKind"] = valueKind,
                ["createIfNotExists"] = "true"
            }
        };

    private static CompiledInstallOperation EnvironmentOperation(
        string name,
        string value,
        string scope)
        => new()
        {
            Id = $"env:{Guid.NewGuid():N}",
            Type = "environment.set",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["value"] = value,
                ["scope"] = scope
            }
        };

    private static CompiledInstallOperation ShortcutOperation(
        string name,
        string targetPath,
        string location,
        string startMenuSubfolder = "")
        => new()
        {
            Id = $"shortcut:{Guid.NewGuid():N}",
            Type = "shortcut.create",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["targetPath"] = targetPath,
                ["location"] = location,
                ["startMenuSubfolder"] = startMenuSubfolder
            }
        };

    private static CompiledInstallOperation PackageOperation()
        => new()
        {
            Id = $"package:{Guid.NewGuid():N}",
            Type = "package.install",
            Inputs = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["id"] = "runtime",
                ["downloadUrl"] = "https://example.test/runtime.exe",
                ["detectionCommand"] = "detect-runtime",
                ["detectionPattern"] = "runtime installed",
                ["installArgs"] = "/install /quiet /norestart",
                ["mandatory"] = "true",
                ["successExitCodes"] = "0,3010,1641",
                ["rebootExitCodes"] = "3010,1641"
            }
        };

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

    private sealed class FakePackageRunner : IPackageCommandRunner
    {
        public Queue<PackageCommandResult> Results { get; } = new();
        public List<(string FileName, string Arguments, string WorkingDirectory, int TimeoutSeconds)> Invocations { get; } = new();
        public bool IsSupported => true;

        public PackageCommandResult Run(string fileName, string arguments, string workingDirectory, int timeoutSeconds)
        {
            Invocations.Add((fileName, arguments, workingDirectory, timeoutSeconds));
            return Results.Count == 0 ? new PackageCommandResult(0, "", "") : Results.Dequeue();
        }
    }

    private sealed class FakePackageAcquisitionStore : IPackageAcquisitionStore
    {
        private readonly string? _packagePath;
        private readonly string? _acquiredPath;

        public FakePackageAcquisitionStore(string? packagePath = null, string? acquiredPath = null)
        {
            _packagePath = packagePath;
            _acquiredPath = acquiredPath;
        }

        public List<string> AcquiredSources { get; } = new();

        public bool FileExists(string path)
            => File.Exists(path) || (!string.IsNullOrWhiteSpace(_packagePath) && Path.GetFullPath(path) == Path.GetFullPath(_packagePath));

        public string ResolvePackagePath(CompiledInstallOperation operation, ResourceProviderContext context)
            => _packagePath ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "runtime.exe");

        public PackageAcquisitionResult Acquire(string source, string packagePath, int retryCount)
        {
            AcquiredSources.Add(source);
            return new PackageAcquisitionResult(_acquiredPath ?? _packagePath ?? packagePath, 1, false, 0, "succeeded");
        }
    }

    private sealed class FailingPackageAcquisitionStore : IPackageAcquisitionStore
    {
        private readonly string _packagePath;
        private readonly string _partialPath;

        public FailingPackageAcquisitionStore(string packagePath, string partialPath)
        {
            _packagePath = packagePath;
            _partialPath = partialPath;
        }

        public bool FileExists(string path) => true;

        public string ResolvePackagePath(CompiledInstallOperation operation, ResourceProviderContext context)
            => _packagePath;

        public PackageAcquisitionResult Acquire(string source, string packagePath, int retryCount)
            => throw new PackageAcquisitionException(
                "Remote package could not be acquired after 2 attempt(s).",
                attempts: 2,
                resumed: true,
                resumeOffsetBytes: 7,
                partialPath: _partialPath,
                innerException: new IOException("network interrupted"));
    }

    private sealed class SingleResponseHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _serverTask;
        private readonly byte[] _body;
        private readonly string _expectedRange;

        public SingleResponseHttpServer(byte[] body, string expectedRange)
        {
            _body = body;
            _expectedRange = expectedRange;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            Url = $"http://127.0.0.1:{endpoint.Port}/runtime.exe";
            _serverTask = Task.Run(Serve);
        }

        public string Url { get; }
        public string RangeHeader { get; private set; } = "";

        public void Dispose()
        {
            try { _serverTask.GetAwaiter().GetResult(); } catch { }
            try { _listener.Stop(); } catch { }
        }

        private async Task Serve()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                    RangeHeader = line;
            }

            var status = RangeHeader.Equals("Range: " + _expectedRange, StringComparison.OrdinalIgnoreCase)
                ? "206 Partial Content"
                : "200 OK";
            var start = int.Parse(_expectedRange["bytes=".Length..].TrimEnd('-'));
            var range = status.StartsWith("206")
                ? $"Content-Range: bytes {start}-{start + _body.Length - 1}/{start + _body.Length}\r\n" : "";
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Length: {_body.Length}\r\n{range}Connection: close\r\n\r\n");
            await stream.WriteAsync(headers);
            await stream.WriteAsync(_body);
            await stream.FlushAsync();
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"beep-provider-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
