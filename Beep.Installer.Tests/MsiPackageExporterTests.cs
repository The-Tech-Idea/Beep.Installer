using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Engine.Msi;
using Beep.Installer.Models;
using FluentAssertions;
using Microsoft.Win32;
using TheTechIdea.Beep.Installer;
using Xunit;

namespace Beep.Installer.Tests;

public sealed class MsiPackageExporterTests : IDisposable
{
    private readonly string _tempDir;

    public MsiPackageExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BeepMsi_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Generate_WritesWixSourceWithStableComponentIdentities()
    {
        var project = CreateFileOnlyProject();
        var first = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "first")
        });
        var second = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "second")
        });

        first.HasErrors.Should().BeFalse();
        first.Components.Should().ContainSingle();
        second.Components.Should().ContainSingle();
        second.Components[0].Guid.Should().Be(first.Components[0].Guid);
        File.Exists(first.WixSourcePath).Should().BeTrue();
        File.Exists(first.CapabilityReportPath).Should().BeTrue();

        var document = XDocument.Load(first.WixSourcePath);
        var xml = document.ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("http://wixtoolset.org/schemas/v4/wxs");
        xml.Should().Contain("Name=\"Msi App\"");
        xml.Should().Contain("Manufacturer=\"ACME\"");
        xml.Should().Contain("Version=\"1.2.3\"");
        xml.Should().Contain($"ProductCode=\"{first.ProductCode}\"");
        second.ProductCode.Should().Be(first.ProductCode);
        xml.Should().Contain("MajorUpgrade");
        xml.Should().Contain(first.Components[0].Guid);
        xml.Should().Contain("Source=\"bin\\MsiApp.exe\"");

        using var report = JsonDocument.Parse(File.ReadAllText(first.CapabilityReportPath));
        report.RootElement.GetProperty("planHash").GetString().Should().Be(first.PlanHash);
        report.RootElement.GetProperty("upgradeCode").GetString().Should().Be(first.UpgradeCode);
        report.RootElement.GetProperty("findings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Generate_ArchitectureSeparatesProductAndComponentIdentity()
    {
        var project = CreateFileOnlyProject();
        project.Prefer64Bit = false;
        var x86 = MsiPackageExporter.Generate(project,
            new MsiExportOptions { OutputDirectory = Path.Combine(_tempDir, "x86") });
        project.Prefer64Bit = true;
        var x64 = MsiPackageExporter.Generate(project,
            new MsiExportOptions { OutputDirectory = Path.Combine(_tempDir, "x64") });

        x86.HasErrors.Should().BeFalse();
        x64.HasErrors.Should().BeFalse();
        x86.Architecture.Should().Be("x86");
        x64.Architecture.Should().Be("x64");
        x64.ProductCode.Should().NotBe(x86.ProductCode);
        x64.Components[0].Guid.Should().NotBe(x86.Components[0].Guid);
        x64.UpgradeCode.Should().Be(x86.UpgradeCode);
        foreach (var result in new[] { x86, x64 })
        {
            var source = File.ReadAllText(result.WixSourcePath);
            source.Should().Contain($"$(sys.BUILDARCH) != {result.Architecture}");
            source.Should().Contain("ProgramFiles6432Folder");
        }

        project.DefaultScope = InstallationScope.User;
        var user = MsiPackageExporter.Generate(project,
            new MsiExportOptions { OutputDirectory = Path.Combine(_tempDir, "user") });
        File.ReadAllText(user.WixSourcePath).Should().Contain("LocalAppDataFolder")
            .And.NotContain("ProgramFiles6432Folder");
    }

    [Fact]
    public void Generate_ProductIdentitySurvivesRenameAndSeparatesProductsAndScopes()
    {
        var project = CreateFileOnlyProject();
        MsiExportResult Export(string folder) => MsiPackageExporter.Generate(project,
            new MsiExportOptions { OutputDirectory = Path.Combine(_tempDir, folder) });
        var original = Export("identity-base");
        project.AppVersion = "1.2.4";
        var upgraded = Export("identity-upgrade");
        upgraded.ProductCode.Should().NotBe(original.ProductCode);
        upgraded.UpgradeCode.Should().Be(original.UpgradeCode);
        upgraded.Components[0].Guid.Should().Be(original.Components[0].Guid);

        project.AppVersion = "1.2.3";
        project.AppPublisher = "Other publisher";
        project.AppName = "Renamed product";
        var renamed = Export("identity-renamed");
        renamed.ProductCode.Should().Be(original.ProductCode);
        renamed.UpgradeCode.Should().Be(original.UpgradeCode);
        renamed.Components[0].Guid.Should().Be(original.Components[0].Guid);

        var originalId = project.AppId;
        project.AppId = Guid.NewGuid().ToString("D");
        var otherProduct = Export("identity-other-product");
        otherProduct.ProductCode.Should().NotBe(original.ProductCode);
        otherProduct.UpgradeCode.Should().NotBe(original.UpgradeCode);
        otherProduct.Components[0].Guid.Should().NotBe(original.Components[0].Guid);
        project.AppId = originalId;

        project.AppPublisher = "ACME";
        project.DefaultScope = project.DefaultScope == InstallationScope.User
            ? InstallationScope.Machine : InstallationScope.User;
        var otherScope = Export("identity-scope");
        otherScope.ProductCode.Should().NotBe(original.ProductCode);
        otherScope.Components[0].Guid.Should().NotBe(original.Components[0].Guid);
    }

    [Fact]
    public void Generate_MissingAppIdRejectsBeforeWritingOutput()
    {
        var project = CreateFileOnlyProject();
        project.AppId = "";
        var output = Path.Combine(_tempDir, "missing-identity");
        Action export = () => MsiPackageExporter.Generate(project, new MsiExportOptions { OutputDirectory = output });
        export.Should().Throw<InvalidOperationException>().WithMessage("*AppId*");
        Directory.Exists(output).Should().BeFalse();
    }

    [Fact]
    public void Generate_MapsInstallComponentsToMsiFeatureTree()
    {
        var project = CreateFileOnlyProject();
        project.Components[0].Description = "Required runtime files";
        project.Components.Add(new InstallComponent
        {
            Id = "analytics-tools",
            Name = "Analytics Tools",
            Description = "Optional analytics command-line utilities",
            Required = false,
            Selected = false,
            Files =
            {
                new FileCopyOperation
                {
                    SourcePath = "bin\\analytics.exe",
                    DestinationPath = "{InstallPath}\\analytics.exe",
                    Description = "Analytics CLI"
                }
            }
        });

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "features")
        });

        result.HasErrors.Should().BeFalse();
        result.Components.Should().Contain(c => c.OperationId.StartsWith("file:core:", StringComparison.Ordinal) && c.FeatureId == "feat_core");
        result.Components.Should().Contain(c => c.OperationId.StartsWith("file:analytics-tools:", StringComparison.Ordinal) && c.FeatureId == "feat_analytics_tools");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("Feature Id=\"MainFeature\"");
        xml.Should().Contain("AllowAbsent=\"no\"");
        xml.Should().Contain("Feature Id=\"feat_core\"");
        xml.Should().Contain("Title=\"Core\"");
        xml.Should().Contain("Description=\"Required runtime files\"");
        xml.Should().Contain("Level=\"1\"");
        xml.Should().Contain("Feature Id=\"feat_analytics_tools\"");
        xml.Should().Contain("Title=\"Analytics Tools\"");
        xml.Should().Contain("Description=\"Optional analytics command-line utilities\"");
        xml.Should().Contain("Level=\"1000\"");
        xml.Should().Contain("AllowAbsent=\"yes\"");
        xml.Should().Contain("ComponentRef Id=\"cmp_file_core__installpath__msiapp_exe\"");
        xml.Should().Contain("ComponentRef Id=\"cmp_file_analytics_tools__installpath__analytics_exe\"");
    }

    [Fact]
    public void Generate_MapsSupportedComponentConditionsToMsiFeatureLevels()
    {
        var project = CreateFileOnlyProject();
        project.Components[0].Conditions.Add(new InstallCondition
        {
            Type = ConditionType.Architecture,
            Value = "x64"
        });
        project.Components[0].Conditions.Add(new InstallCondition
        {
            Type = ConditionType.IsAdmin
        });
        project.Components[0].Conditions.Add(new InstallCondition
        {
            Type = ConditionType.FileExists,
            Value = @"C:\marker.txt"
        });
        project.Components[0].Conditions.Add(new InstallCondition
        {
            Type = ConditionType.DirectoryExists,
            Value = @"C:\ProgramData\ACME"
        });
        project.Components[0].Conditions.Add(new InstallCondition
        {
            Type = ConditionType.RegistryExists,
            Value = @"HKLM\Software\ACME"
        });
        project.Components[0].Conditions.Add(new InstallCondition
        {
            Type = ConditionType.RegistryValue,
            Value = @"HKCU\Software\ACME|Mode",
            Operator = "==",
            Value2 = "Enterprise"
        });

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "feature-conditions")
        });

        result.HasErrors.Should().BeFalse();
        result.Findings.Should().NotContain(f => f.Code == "BI1615");
        result.AppSearches.Should().HaveCount(4);
        result.AppSearches.Should().Contain(s => s.ConditionType == nameof(ConditionType.FileExists) && s.Path == "C:\\" && s.FileName == "marker.txt");
        result.AppSearches.Should().Contain(s => s.ConditionType == nameof(ConditionType.DirectoryExists) && s.Path == @"C:\ProgramData\ACME");
        result.AppSearches.Should().Contain(s => s.ConditionType == nameof(ConditionType.RegistryExists) && s.RegistryRoot == "HKLM" && s.RegistryKey == @"Software\ACME");
        result.AppSearches.Should().Contain(s => s.ConditionType == nameof(ConditionType.RegistryValue) && s.RegistryRoot == "HKCU" && s.RegistryKey == @"Software\ACME" && s.RegistryName == "Mode");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("Feature Id=\"feat_core\"");
        xml.Should().Contain("Level Value=\"1\" Condition=\"(VersionNT64) AND (Privileged) AND (BEEPSEARCH_");
        xml.Should().Contain("Property Id=\"BEEPSEARCH_");
        xml.Should().Contain("DirectorySearch");
        xml.Should().Contain("Path=\"C:\\\"");
        xml.Should().Contain("FileSearch");
        xml.Should().Contain("Name=\"marker.txt\"");
        xml.Should().Contain("Path=\"C:\\ProgramData\\ACME\"");
        xml.Should().Contain("RegistrySearch");
        xml.Should().Contain("Root=\"HKLM\"");
        xml.Should().Contain("Key=\"Software\\ACME\"");
        xml.Should().Contain("Root=\"HKCU\"");
        xml.Should().Contain("Name=\"Mode\"");
        xml.Should().Contain("&quot;Enterprise&quot;");
        xml.Should().Contain("InstallUISequence");
        xml.Should().Contain("InstallExecuteSequence");

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("appSearches").GetArrayLength().Should().Be(4);
    }

    [Fact]
    public void Generate_ReportsUnsupportedOperationsAsErrorsUnlessPolicyConfiguresWarnings()
    {
        var project = CreateFileOnlyProject();
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            Name = "Nested INI",
            Format = ConfigTransformFormat.Ini,
            Operation = ConfigTransformOperation.Set,
            TargetPath = @"{InstallPath}\config\app.ini",
            Section = "app",
            KeyPath = "Mode",
            Value = "Enterprise"
        });

        var strict = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "strict")
        });
        strict.HasErrors.Should().BeTrue();
        strict.Findings.Should().Contain(f =>
            f.Code == "BI1601"
            && f.Severity == "error"
            && f.OperationType == "config.transform");

        var permissive = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "permissive"),
            FailOnUnsupportedOperations = false
        });
        permissive.HasErrors.Should().BeFalse();
        permissive.Findings.Should().Contain(f =>
            f.Code == "BI1601"
            && f.Severity == "warning"
            && f.OperationType == "config.transform");
    }

    [Fact]
    public void Generate_MapsPnpInfDriversToPnputilCustomActionsAndStagesPackagePayload()
    {
        var project = CreateFileOnlyProject();
        var appPath = Path.Combine(_tempDir, "MsiApp.exe");
        var driverDirectory = Path.Combine(_tempDir, "pnp-driver");
        Directory.CreateDirectory(driverDirectory);
        var infPath = Path.Combine(driverDirectory, "acmevirt.inf");
        var catalogPath = Path.Combine(driverDirectory, "acmevirt.cat");
        File.WriteAllText(appPath, "app");
        File.WriteAllText(infPath, "inf");
        File.WriteAllText(catalogPath, "catalog");
        project.Components[0].Files[0].SourcePath = appPath;
        project.DriverPackages.Add(new DriverPackageDefinition
        {
            Name = "ACME Virtual Device",
            Kind = DriverPackageKind.Pnp,
            InfPath = infPath,
            PublishedName = "oem42.inf",
            HardwareId = @"ROOT\ACMEVIRT",
            InstallDevices = true,
            RemoveOnUninstall = true,
            RebootBehavior = DriverPackageRebootBehavior.Required
        });
        MsiToolInvocation? captured = null;

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "pnp-driver-msi"),
            BuildPackage = true,
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                captured = invocation;
                return new MsiToolResult { ExitCode = 0, StandardOutput = "built" };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.Components.Should().Contain(c => c.OperationType == "driver.package" && c.DirectoryId == "INSTALLFOLDER");
        result.RequiredWixExtensions.Should().NotContain("FireGiant.HeatWave.BuildTools.wixext");
        captured.Should().NotBeNull();
        captured!.Arguments.Should().NotContain("FireGiant.HeatWave.BuildTools.wixext");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("pnputil.exe /add-driver");
        xml.Should().Contain("[INSTALLFOLDER]acmevirt.inf");
        xml.Should().Contain("/install /reboot");
        xml.Should().Contain("pnputil.exe /delete-driver");
        xml.Should().Contain("oem42.inf");
        xml.Should().Contain("/uninstall /force /reboot");
        xml.Should().Contain("CustomAction");
        xml.Should().Contain("InstallExecuteSequence");
        xml.Should().Contain("Condition=\"NOT Installed\"");
        xml.Should().Contain("Condition=\"REMOVE=&quot;ALL&quot;\"");
        xml.Should().Contain("acmevirt.inf");
        xml.Should().Contain("acmevirt.cat");

        using var payloadManifest = JsonDocument.Parse(File.ReadAllText(result.PayloadManifestPath));
        payloadManifest.RootElement.EnumerateArray().Should().Contain(entry =>
            entry.GetProperty("operationType").GetString() == "driver.package"
            && entry.GetProperty("sourcePath").GetString() == infPath);
        payloadManifest.RootElement.EnumerateArray().Should().Contain(entry =>
            entry.GetProperty("operationType").GetString() == "driver.package"
            && entry.GetProperty("sourcePath").GetString() == catalogPath);
    }

    [Fact]
    public void Generate_MapsScheduledTasksToMsiCustomActions()
    {
        var project = CreateFileOnlyProject();
        project.ScheduledTasks.Add(new ScheduledTaskDefinition
        {
            Name = @"ACME\TaskApp",
            Description = "Runs TaskApp maintenance",
            ExecutablePath = @"{InstallPath}\MsiApp.exe",
            Arguments = "--maintain",
            WorkingDirectory = "{InstallPath}",
            Trigger = ScheduledTaskTrigger.Daily,
            StartTime = "02:15",
            Enabled = false,
            RunElevated = true,
            Username = "SYSTEM",
            StopOnUninstall = true
        });

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = _tempDir
        });

        result.HasErrors.Should().BeFalse();
        result.Components.Should().Contain(c => c.OperationType == "scheduled-task.create");
        result.Findings.Should().NotContain(f => f.OperationType == "scheduled-task.create");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("CustomAction");
        xml.Should().Contain("Id=\"task_Create_scheduled_task_acme_taskapp\"");
        xml.Should().Contain("[SystemFolder]schtasks.exe /Create /F");
        xml.Should().Contain("/TN &quot;\\ACME\\TaskApp&quot;");
        xml.Should().Contain("/TR &quot;cmd.exe /c cd /d \\&quot;[INSTALLFOLDER]\\&quot; &amp;&amp; \\&quot;[INSTALLFOLDER]MsiApp.exe\\&quot; --maintain&quot;");
        xml.Should().Contain("/SC DAILY");
        xml.Should().Contain("/ST 02:15");
        xml.Should().Contain("/RL HIGHEST");
        xml.Should().Contain("/RU &quot;SYSTEM&quot;");
        xml.Should().Contain("Id=\"task_Disable_scheduled_task_acme_taskapp\"");
        xml.Should().Contain("/Change /TN &quot;\\ACME\\TaskApp&quot; /DISABLE");
        xml.Should().Contain("Id=\"task_End_scheduled_task_acme_taskapp\"");
        xml.Should().Contain("/End /TN &quot;\\ACME\\TaskApp&quot;");
        xml.Should().Contain("Id=\"task_Delete_scheduled_task_acme_taskapp\"");
        xml.Should().Contain("/Delete /TN &quot;\\ACME\\TaskApp&quot; /F");
        xml.Should().Contain("InstallExecuteSequence");
        xml.Should().Contain("Condition=\"NOT Installed\"");
        xml.Should().Contain("Condition=\"REMOVE=&quot;ALL&quot;\"");
    }

    [Fact]
    public void Generate_MapsRegistryAndShortcutOperationsToNativeWix()
    {
        var project = CreateFileOnlyProject();
        project.RegistryEntries.Add(new RegistryOperation
        {
            KeyPath = @"HKLM\Software\ACME\MsiApp",
            ValueName = "InstallPath",
            Value = "[INSTALLFOLDER]",
            ValueKind = RegistryValueKind.String
        });
        project.Shortcuts.Add(new ShortcutDefinition
        {
            Name = "Msi App",
            TargetPath = @"{InstallPath}\MsiApp.exe",
            Location = ShortcutLocation.StartMenu,
            StartMenuSubfolder = "ACME"
        });

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = _tempDir
        });

        result.HasErrors.Should().BeFalse();
        result.Components.Should().Contain(c => c.OperationType == "registry.write");
        result.Components.Should().Contain(c => c.OperationType == "shortcut.create");
        result.Findings.Should().NotContain(f => f.OperationType == "registry.write");
        result.Findings.Should().NotContain(f => f.OperationType == "shortcut.create");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("RegistryValue");
        xml.Should().Contain("Root=\"HKLM\"");
        xml.Should().Contain("Key=\"Software\\ACME\\MsiApp\"");
        xml.Should().Contain("Name=\"InstallPath\"");
        xml.Should().Contain("Shortcut");
        xml.Should().Contain("Target=\"[INSTALLFOLDER]MsiApp.exe\"");
        xml.Should().Contain("ApplicationProgramsFolder");
    }

    [Fact]
    public void Generate_MapsEnvironmentAndWindowsServiceOperationsToNativeWix()
    {
        var project = CreateFileOnlyProject();
        project.EnvironmentVariables.Add(new EnvironmentVariableOp
        {
            Name = "MSI_APP_HOME",
            Value = "{InstallPath}",
            Scope = EnvironmentVariableTarget.Machine
        });
        project.WindowsServices.Add(new WindowsServiceDefinition
        {
            Name = "MsiSvc",
            DisplayName = "MSI Service",
            Description = "Runs MSI service workloads",
            ExecutablePath = @"{InstallPath}\MsiService.exe",
            Arguments = "--run-service",
            StartMode = WindowsServiceStartMode.Auto,
            StartAfterInstall = true,
            StopOnUninstall = true,
            Account = WindowsServiceAccount.LocalSystem
        });

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = _tempDir
        });

        result.HasErrors.Should().BeFalse();
        result.Components.Should().Contain(c => c.OperationType == "environment.set");
        result.Components.Should().Contain(c => c.OperationType == "service.install");
        result.Findings.Should().NotContain(f => f.OperationType == "environment.set");
        result.Findings.Should().NotContain(f => f.OperationType == "service.install");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("Environment");
        xml.Should().Contain("Name=\"MSI_APP_HOME\"");
        xml.Should().Contain("Value=\"[INSTALLFOLDER]\"");
        xml.Should().Contain("System=\"yes\"");
        xml.Should().Contain("ServiceInstall");
        xml.Should().Contain("Name=\"MsiSvc\"");
        xml.Should().Contain("DisplayName=\"MSI Service\"");
        xml.Should().Contain("Start=\"auto\"");
        xml.Should().Contain("Arguments=\"--run-service\"");
        xml.Should().Contain("Source=\"MsiService.exe\"");
        xml.Should().Contain("ServiceControl");
        xml.Should().Contain("Remove=\"uninstall\"");
        xml.Should().Contain("Stop=\"both\"");
    }

    [Fact]
    public void Generate_MapsFileAssociationsToNativeWixRegistryAuthoring()
    {
        var project = CreateFileOnlyProject();
        project.FileAssociations.Add(new FileAssociationDefinition
        {
            Extension = ".acme",
            ProgId = "ACME.MsiApp.Document",
            Description = "ACME MSI Document",
            ExecutablePath = @"{InstallPath}\MsiApp.exe",
            Arguments = "\"%1\" --from-msi",
            IconPath = @"{InstallPath}\MsiApp.exe,0",
            ContentType = "application/x-acme",
            PerceivedType = "document",
            Verb = "open",
            VerbDisplayName = "Open with MSI App"
        });

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = _tempDir
        });

        result.HasErrors.Should().BeFalse();
        result.Components.Should().Contain(c => c.OperationType == "file-association.register");
        result.Findings.Should().NotContain(f => f.OperationType == "file-association.register");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("Key=\"Software\\Classes\\.acme\"");
        xml.Should().Contain("Value=\"ACME.MsiApp.Document\"");
        xml.Should().Contain("Name=\"Content Type\"");
        xml.Should().Contain("Value=\"application/x-acme\"");
        xml.Should().Contain("Name=\"PerceivedType\"");
        xml.Should().Contain("Value=\"document\"");
        xml.Should().Contain("Key=\"Software\\Classes\\ACME.MsiApp.Document\"");
        xml.Should().Contain("Value=\"ACME MSI Document\"");
        xml.Should().Contain("Key=\"Software\\Classes\\ACME.MsiApp.Document\\DefaultIcon\"");
        xml.Should().Contain("Value=\"[INSTALLFOLDER]MsiApp.exe,0\"");
        xml.Should().Contain("Key=\"Software\\Classes\\ACME.MsiApp.Document\\shell\\open\"");
        xml.Should().Contain("Value=\"Open with MSI App\"");
        xml.Should().Contain("Key=\"Software\\Classes\\ACME.MsiApp.Document\\shell\\open\\command\"");
        xml.Should().Contain("Value=\"&quot;[INSTALLFOLDER]MsiApp.exe&quot; &quot;%1&quot; --from-msi\"");
    }

    [Fact]
    public void Generate_MapsComRegistrationsToNativeWixRegistryAuthoring()
    {
        var project = CreateFileOnlyProject();
        project.ComRegistrations.Add(new ComRegistrationDefinition
        {
            Clsid = "{11111111-2222-3333-4444-555555555555}",
            ProgId = "ACME.MsiApp.1",
            VersionIndependentProgId = "ACME.MsiApp",
            Description = "ACME MSI COM Server",
            ServerPath = @"{InstallPath}\MsiCom.dll",
            ServerType = ComServerType.InProc,
            ThreadingModel = "Both",
            TypeLibId = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
            Version = "1.0"
        });

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = _tempDir
        });

        result.HasErrors.Should().BeFalse();
        result.Components.Should().Contain(c => c.OperationType == "com.register");
        result.Findings.Should().NotContain(f => f.OperationType == "com.register");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("Key=\"Software\\Classes\\CLSID\\{11111111-2222-3333-4444-555555555555}\"");
        xml.Should().Contain("Value=\"ACME MSI COM Server\"");
        xml.Should().Contain("Key=\"Software\\Classes\\CLSID\\{11111111-2222-3333-4444-555555555555}\\InprocServer32\"");
        xml.Should().Contain("Value=\"&quot;[INSTALLFOLDER]MsiCom.dll&quot;\"");
        xml.Should().Contain("Name=\"ThreadingModel\"");
        xml.Should().Contain("Value=\"Both\"");
        xml.Should().Contain("Key=\"Software\\Classes\\CLSID\\{11111111-2222-3333-4444-555555555555}\\ProgID\"");
        xml.Should().Contain("Value=\"ACME.MsiApp.1\"");
        xml.Should().Contain("Key=\"Software\\Classes\\CLSID\\{11111111-2222-3333-4444-555555555555}\\VersionIndependentProgID\"");
        xml.Should().Contain("Value=\"ACME.MsiApp\"");
        xml.Should().Contain("Key=\"Software\\Classes\\ACME.MsiApp.1\\CLSID\"");
        xml.Should().Contain("Key=\"Software\\Classes\\ACME.MsiApp\\CurVer\"");
    }

    [Fact]
    public void Generate_MapsCertificatesToWixIisExtensionAuthoringAndStagesPayload()
    {
        var project = CreateFileOnlyProject();
        var appSourceDir = Path.Combine(_tempDir, "source", "bin");
        Directory.CreateDirectory(appSourceDir);
        var appSource = Path.Combine(appSourceDir, "MsiApp.exe");
        File.WriteAllText(appSource, "payload");
        project.Components[0].Files[0].SourcePath = appSource;
        var sourceDir = Path.Combine(_tempDir, "source", "certs");
        Directory.CreateDirectory(sourceDir);
        var certificatePath = Path.Combine(sourceDir, "root-ca.cer");
        File.WriteAllText(certificatePath, "certificate");
        project.Certificates.Add(new CertificateDefinition
        {
            SourcePath = certificatePath,
            StoreName = "Root",
            StoreLocation = InstallationScope.Machine,
            FriendlyName = "ACME Root CA"
        });

        MsiToolInvocation? captured = null;
        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = _tempDir,
            BuildPackage = true,
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                captured = invocation;
                return new MsiToolResult { ExitCode = 0 };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.Components.Should().Contain(c => c.OperationType == "certificate.install");
        result.Findings.Should().NotContain(f => f.OperationType == "certificate.install");
        result.RequiredWixExtensions.Should().Contain("WixToolset.Iis.wixext");
        captured.Should().NotBeNull();
        captured!.Arguments.Should().ContainInOrder("-ext", "WixToolset.Iis.wixext");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("xmlns:iis=\"http://wixtoolset.org/schemas/v4/wxs/iis\"");
        xml.Should().Contain("Certificate");
        xml.Should().Contain("Name=\"ACME Root CA\"");
        xml.Should().Contain("CertificatePath=\"payload\\certificate_machine_root_");
        xml.Should().Contain("root-ca.cer\"");
        xml.Should().Contain("Request=\"no\"");
        xml.Should().Contain("StoreLocation=\"localMachine\"");
        xml.Should().Contain("StoreName=\"root\"");
        xml.Should().Contain("Vital=\"yes\"");

        using var payloadReport = JsonDocument.Parse(File.ReadAllText(result.PayloadManifestPath));
        payloadReport.RootElement.EnumerateArray()
            .Should().Contain(e => e.GetProperty("operationType").GetString() == "certificate.install"
                                   && e.GetProperty("sourcePath").GetString() == certificatePath);

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("requiredWixExtensions").EnumerateArray()
            .Select(e => e.GetString())
            .Should().Contain("WixToolset.Iis.wixext");
    }

    [Fact]
    public void Generate_MapsIisAppPoolsAndSitesToWixIisExtensionAuthoring()
    {
        var project = CreateFileOnlyProject();
        var sourceDir = Path.Combine(_tempDir, "source", "bin");
        Directory.CreateDirectory(sourceDir);
        var sourceExe = Path.Combine(sourceDir, "MsiApp.exe");
        File.WriteAllText(sourceExe, "payload");
        project.Components[0].Files[0].SourcePath = sourceExe;
        project.IisAppPools.Add(new IisAppPoolDefinition
        {
            Name = "ACME Pool",
            RuntimeVersion = "v4.0",
            PipelineMode = IisManagedPipelineMode.Classic,
            Identity = "NetworkService"
        });
        project.IisSites.Add(new IisSiteDefinition
        {
            Name = "ACME Site",
            PhysicalPath = "{InstallPath}\\wwwroot",
            ApplicationPool = "ACME Pool",
            StartAfterInstall = true,
            Bindings =
            {
                new IisBindingDefinition { Protocol = IisBindingProtocol.Http, IpAddress = "*", Port = 8080, Host = "acme.local" },
                new IisBindingDefinition { Protocol = IisBindingProtocol.Https, IpAddress = "*", Port = 9443, Host = "secure.acme.local" }
            }
        });

        MsiToolInvocation? captured = null;
        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = _tempDir,
            BuildPackage = true,
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                captured = invocation;
                return new MsiToolResult { ExitCode = 0 };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.Components.Should().Contain(c => c.OperationType == "iis.appPool");
        result.Components.Should().Contain(c => c.OperationType == "iis.site");
        result.Findings.Should().NotContain(f => f.OperationType == "iis.appPool" || f.OperationType == "iis.site");
        result.RequiredWixExtensions.Should().ContainSingle("WixToolset.Iis.wixext");
        captured.Should().NotBeNull();
        captured!.Arguments.Should().ContainInOrder("-ext", "WixToolset.Iis.wixext");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("xmlns:iis=\"http://wixtoolset.org/schemas/v4/wxs/iis\"");
        xml.Should().Contain("WebAppPool");
        xml.Should().Contain("Name=\"ACME Pool\"");
        xml.Should().Contain("ManagedRuntimeVersion=\"v4.0\"");
        xml.Should().Contain("ManagedPipelineMode=\"Classic\"");
        xml.Should().Contain("Identity=\"networkService\"");
        xml.Should().Contain("WebSite");
        xml.Should().Contain("Description=\"ACME Site\"");
        xml.Should().Contain("Directory=\"INSTALLFOLDER\"");
        xml.Should().Contain("StartOnInstall=\"yes\"");
        xml.Should().Contain("AutoStart=\"yes\"");
        xml.Should().Contain("WebApplication");
        xml.Should().Contain("WebAppPool=\"apppool_iis_appPool_ACME_Pool\"");
        xml.Should().Contain("WebAddress");
        xml.Should().Contain("Port=\"8080\"");
        xml.Should().Contain("Secure=\"no\"");
        xml.Should().Contain("Header=\"acme.local\"");
        xml.Should().Contain("Port=\"9443\"");
        xml.Should().Contain("Secure=\"yes\"");
        xml.Should().Contain("Header=\"secure.acme.local\"");

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("requiredWixExtensions").EnumerateArray()
            .Select(e => e.GetString())
            .Should().Contain("WixToolset.Iis.wixext");
    }

    [Fact]
    public void Generate_MapsXmlConfigTransformsToWixUtilXmlFileAuthoring()
    {
        var project = CreateFileOnlyProject();
        var sourceDir = Path.Combine(_tempDir, "source", "bin");
        Directory.CreateDirectory(sourceDir);
        var sourceExe = Path.Combine(sourceDir, "MsiApp.exe");
        File.WriteAllText(sourceExe, "payload");
        project.Components[0].Files[0].SourcePath = sourceExe;
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            Name = "Set service endpoint",
            TargetPath = "{InstallPath}\\web.config",
            Format = ConfigTransformFormat.Xml,
            Operation = ConfigTransformOperation.Set,
            Section = "/configuration/appSettings/add[@key='ServiceUrl']",
            KeyPath = "value",
            Value = "https://api.acme.local"
        });

        MsiToolInvocation? captured = null;
        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = _tempDir,
            BuildPackage = true,
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                captured = invocation;
                return new MsiToolResult { ExitCode = 0 };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.Components.Should().Contain(c => c.OperationType == "config.transform");
        result.Findings.Should().NotContain(f => f.OperationType == "config.transform");
        result.RequiredWixExtensions.Should().ContainSingle("WixToolset.Util.wixext");
        captured.Should().NotBeNull();
        captured!.Arguments.Should().ContainInOrder("-ext", "WixToolset.Util.wixext");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("xmlns:util=\"http://wixtoolset.org/schemas/v4/wxs/util\"");
        xml.Should().Contain("XmlFile");
        xml.Should().Contain("File=\"[INSTALLFOLDER]web.config\"");
        xml.Should().Contain("ElementPath=\"/configuration/appSettings/add[@key='ServiceUrl']\"");
        xml.Should().Contain("Action=\"setValue\"");
        xml.Should().Contain("SelectionLanguage=\"XPath\"");
        xml.Should().Contain("Name=\"value\"");
        xml.Should().Contain("Value=\"https://api.acme.local\"");
    }

    [Fact]
    public void Generate_MapsJsonConfigTransformsToDeferredPowerShellCustomAction()
    {
        var project = CreateFileOnlyProject();
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            Name = "Set API endpoint",
            TargetPath = "{InstallPath}\\appsettings.json",
            Format = ConfigTransformFormat.Json,
            Operation = ConfigTransformOperation.Set,
            KeyPath = "Api:BaseUrl",
            Value = "https://api.acme.local",
            RestoreOnRollback = true
        });

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "json-transform")
        });

        result.HasErrors.Should().BeFalse();
        result.Components.Should().Contain(c => c.OperationType == "config.transform"
                                                && c.Inputs["format"] == "json");
        result.Findings.Should().NotContain(f => f.OperationType == "config.transform");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("CustomAction");
        xml.Should().Contain("WindowsPowerShell\\v1.0\\powershell.exe");
        xml.Should().Contain("ConvertFrom-Json");
        xml.Should().Contain("ConvertTo-Json");
        xml.Should().Contain("[INSTALLFOLDER]appsettings.json");
        xml.Should().Contain("Api");
        xml.Should().Contain("BaseUrl");
        xml.Should().Contain("https://api.acme.local");
        xml.Should().Contain("Execute=\"deferred\"");
        xml.Should().Contain("Execute=\"rollback\"");
        xml.Should().Contain("Condition=\"NOT REMOVE=&quot;ALL&quot;\"");
        xml.Should().NotContain("ComponentRef Id=\"cmp_config__installpath__appsettings_json__api_baseurl\"");
    }

    [Fact]
    public void Generate_ReportsAndBlocksMsiCustomActionsWhenPolicyForbidsThem()
    {
        var project = CreateFileOnlyProject();
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            Name = "Set API endpoint",
            TargetPath = "{InstallPath}\\appsettings.json",
            Format = ConfigTransformFormat.Json,
            Operation = ConfigTransformOperation.Set,
            KeyPath = "Api:BaseUrl",
            Value = "https://api.acme.local",
            RestoreOnRollback = true
        });

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "json-transform-forbidden"),
            ForbidCustomActions = true
        });

        result.HasErrors.Should().BeTrue();
        result.CustomActions.Should().Contain(a => a.Family == "json-config-transform" && a.Execute == "deferred");
        result.CustomActions.Should().Contain(a => a.Family == "json-config-transform" && a.Execute == "rollback");
        result.CustomActionPolicy.Outcome.Should().Be("blocked");
        result.CustomActionPolicy.ForbidCustomActions.Should().BeTrue();
        result.CustomActionPolicy.EmittedFamilies.Should().ContainSingle("json-config-transform");
        result.CustomActionPolicy.BlockedFamilies.Should().ContainSingle("json-config-transform");
        result.Findings.Should().Contain(f => f.Code == "BI1629"
                                              && f.OperationType == "msi.customAction"
                                              && f.Message.Contains("json-config-transform", StringComparison.OrdinalIgnoreCase));

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("customActions").GetArrayLength().Should().Be(2);
        var policy = report.RootElement.GetProperty("customActionPolicy");
        policy.GetProperty("outcome").GetString().Should().Be("blocked");
        policy.GetProperty("forbidCustomActions").GetBoolean().Should().BeTrue();
        policy.GetProperty("blockedFamilies").EnumerateArray().Select(x => x.GetString()).Should().ContainSingle("json-config-transform");
        report.RootElement.GetProperty("findings").EnumerateArray()
            .Should().Contain(f => f.GetProperty("Code").GetString() == "BI1629");
    }

    [Fact]
    public void Generate_BlocksCustomActionFamiliesNotAllowedByPolicy()
    {
        var project = CreateFileOnlyProject();
        project.ScheduledTasks.Add(new ScheduledTaskDefinition
        {
            Name = "ACME\\Maintenance",
            ExecutablePath = @"{InstallPath}\MsiApp.exe",
            Arguments = "--maintain",
            Trigger = ScheduledTaskTrigger.Daily,
            StartTime = "02:00",
            Enabled = true,
            RunElevated = true,
            StopOnUninstall = true
        });

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "scheduled-task-disallowed"),
            AllowedCustomActionFamilies = new[] { "json-config-transform" }
        });

        result.HasErrors.Should().BeTrue();
        result.CustomActions.Should().Contain(a => a.Family == "scheduled-task");
        result.CustomActionPolicy.Outcome.Should().Be("blocked");
        result.CustomActionPolicy.AllowedFamilies.Should().ContainSingle("json-config-transform");
        result.CustomActionPolicy.BlockedFamilies.Should().ContainSingle("scheduled-task");
        result.Findings.Should().Contain(f => f.Code == "BI1630"
                                              && f.OperationType == "scheduled-task.create"
                                              && f.Message.Contains("scheduled-task", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generate_AllowsExplicitlyPermittedCustomActionFamilies()
    {
        var project = CreateFileOnlyProject();
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            Name = "Set API endpoint",
            TargetPath = "{InstallPath}\\appsettings.json",
            Format = ConfigTransformFormat.Json,
            Operation = ConfigTransformOperation.Set,
            KeyPath = "Api:BaseUrl",
            Value = "https://api.acme.local"
        });

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "json-transform-allowed"),
            AllowedCustomActionFamilies = new[] { "json-config-transform" }
        });

        result.HasErrors.Should().BeFalse();
        result.CustomActions.Should().OnlyContain(a => a.Family == "json-config-transform");
        result.CustomActionPolicy.Outcome.Should().Be("allowed");
        result.CustomActionPolicy.AllowedFamilies.Should().ContainSingle("json-config-transform");
        result.CustomActionPolicy.BlockedFamilies.Should().BeEmpty();
        result.Findings.Select(f => f.Code).Should().NotContain(new[] { "BI1629", "BI1630" });
    }

    [Fact]
    public void Generate_MapsIniConfigTransformsToNativeWixIniFileAuthoring()
    {
        var project = CreateFileOnlyProject();
        project.ConfigTransforms.Add(new ConfigTransformDefinition
        {
            Name = "Set INI endpoint",
            TargetPath = "{InstallPath}\\settings.ini",
            Format = ConfigTransformFormat.Ini,
            Operation = ConfigTransformOperation.Set,
            Section = "Api",
            KeyPath = "BaseUrl",
            Value = "https://api.acme.local"
        });

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "ini")
        });

        result.HasErrors.Should().BeFalse();
        result.Components.Should().Contain(c => c.OperationType == "config.transform");
        result.RequiredWixExtensions.Should().NotContain("WixToolset.Util.wixext");
        result.Findings.Should().NotContain(f => f.OperationType == "config.transform");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("IniFile");
        xml.Should().Contain("Action=\"addLine\"");
        xml.Should().Contain("Directory=\"INSTALLFOLDER\"");
        xml.Should().Contain("Name=\"settings.ini\"");
        xml.Should().Contain("Section=\"Api\"");
        xml.Should().Contain("Key=\"BaseUrl\"");
        xml.Should().Contain("Value=\"https://api.acme.local\"");
    }

    [Fact]
    public void Generate_MapsFirewallRulesToWixFirewallExtensionAuthoring()
    {
        var project = CreateFileOnlyProject();
        var sourceDir = Path.Combine(_tempDir, "source", "bin");
        Directory.CreateDirectory(sourceDir);
        var sourceExe = Path.Combine(sourceDir, "MsiApp.exe");
        File.WriteAllText(sourceExe, "payload");
        project.Components[0].Files[0].SourcePath = sourceExe;
        project.FirewallRules.Add(new FirewallRuleDefinition
        {
            Name = "ACME MSI API",
            Description = "Allows the ACME API listener",
            Direction = FirewallRuleDirection.In,
            Action = FirewallRuleAction.Allow,
            Protocol = FirewallRuleProtocol.Tcp,
            LocalPort = "9443",
            Program = @"{InstallPath}\MsiApp.exe",
            Profile = "domain,private"
        });

        MsiToolInvocation? captured = null;
        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = _tempDir,
            BuildPackage = true,
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                captured = invocation;
                return new MsiToolResult { ExitCode = 0 };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.Components.Should().Contain(c => c.OperationType == "firewall.rule");
        result.Findings.Should().NotContain(f => f.OperationType == "firewall.rule");
        result.RequiredWixExtensions.Should().ContainSingle("WixToolset.Firewall.wixext");
        captured.Should().NotBeNull();
        captured!.Arguments.Should().ContainInOrder("-ext", "WixToolset.Firewall.wixext");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("xmlns:fire=\"http://wixtoolset.org/schemas/v4/wxs/firewall\"");
        xml.Should().Contain("FirewallException");
        xml.Should().Contain("Name=\"ACME MSI API\"");
        xml.Should().Contain("Description=\"Allows the ACME API listener\"");
        xml.Should().Contain("Action=\"allow\"");
        xml.Should().Contain("Outbound=\"no\"");
        xml.Should().Contain("Program=\"[INSTALLFOLDER]MsiApp.exe\"");
        xml.Should().Contain("Port=\"9443\"");
        xml.Should().Contain("Protocol=\"tcp\"");
        xml.Should().Contain("Profile=\"domain,private\"");

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("requiredWixExtensions")[0].GetString().Should().Be("WixToolset.Firewall.wixext");
    }

    [Theory]
    [InlineData(true, "x64")]
    [InlineData(false, "x86")]
    public void Generate_BuildPackage_InvokesWixToolchainAndRecordsResult(bool prefer64Bit, string architecture)
    {
        var project = CreateFileOnlyProject();
        project.Prefer64Bit = prefer64Bit;
        var sourceDir = Path.Combine(_tempDir, "source", "bin");
        Directory.CreateDirectory(sourceDir);
        var sourceExe = Path.Combine(sourceDir, "MsiApp.exe");
        File.WriteAllText(sourceExe, "payload");
        project.Components[0].Files[0].SourcePath = sourceExe;
        MsiToolInvocation? captured = null;

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = _tempDir,
            BuildPackage = true,
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                captured = invocation;
                return new MsiToolResult
                {
                    ExitCode = 0,
                    StandardOutput = "built",
                    ToolVersion = "wix 5.0.0"
                };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.PackagePath.Should().Be(Path.Combine(_tempDir, "Msi-App.msi"));
        result.PayloadDirectory.Should().Be(Path.Combine(_tempDir, "payload"));
        result.PayloadManifestPath.Should().Be(Path.Combine(_tempDir, "msi-payloads.json"));
        result.WixExitCode.Should().Be(0);
        result.WixToolVersion.Should().Be("wix 5.0.0");
        result.WixStandardOutput.Should().Be("built");
        result.WixCommandLine.Should().Contain("wix.exe");
        captured.Should().NotBeNull();
        captured!.ToolPath.Should().Be(@"C:\Tools\wix.exe");
        captured.Arguments.Should().Equal("build", result.WixSourcePath, "-arch", architecture, "-o", result.PackagePath);

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("Source=\"payload\\file_core__installpath__msiapp_exe\\MsiApp.exe\"");

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("payloadManifestPath").GetString().Should().Be(result.PayloadManifestPath);
        report.RootElement.GetProperty("packagePath").GetString().Should().Be(result.PackagePath);
        report.RootElement.GetProperty("wixToolVersion").GetString().Should().Be("wix 5.0.0");
        report.RootElement.GetProperty("wixExitCode").GetInt32().Should().Be(0);
        report.RootElement.GetProperty("architecture").GetString().Should().Be(architecture);

        using var payloadReport = JsonDocument.Parse(File.ReadAllText(result.PayloadManifestPath));
        payloadReport.RootElement.GetArrayLength().Should().Be(1);
        payloadReport.RootElement[0].GetProperty("sourcePath").GetString().Should().Be(sourceExe);
        payloadReport.RootElement[0].GetProperty("wixSource").GetString().Should().Be(@"payload\file_core__installpath__msiapp_exe\MsiApp.exe");
        payloadReport.RootElement[0].GetProperty("sha256").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Generate_BuildPackage_SignsMsiAndRecordsEvidenceWithoutResolvedPassword()
    {
        var project = CreateFileOnlyProject();
        var sourceDir = Path.Combine(_tempDir, "signed-source", "bin");
        Directory.CreateDirectory(sourceDir);
        var sourceExe = Path.Combine(sourceDir, "MsiApp.exe");
        File.WriteAllText(sourceExe, "payload");
        project.Components[0].Files[0].SourcePath = sourceExe;
        project.CodeSignCertificatePath = Path.Combine(_tempDir, "release.pfx");
        project.CodeSignCertificatePassword = "secret://env/SIGNING_PFX_PASSWORD";
        project.CodeSignTimestampUrl = "https://timestamp.acme.test";

        var signer = new CapturingSigner();

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "signed-msi"),
            BuildPackage = true,
            SignOutput = true,
            ExpectedSigningSubject = "CN=ACME",
            SigningService = new CodeSigningService(signer, new FixedSecretProvider("env", "resolved-password")),
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                var arguments = invocation.Arguments.ToList();
                var outputIndex = arguments.IndexOf("-o");
                File.WriteAllText(arguments[outputIndex + 1], "msi");
                return new MsiToolResult { ExitCode = 0, StandardOutput = "built", ToolVersion = "wix 5.0.0" };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.SigningEvidence.Should().ContainSingle(e =>
            e.ArtifactKind == "msi"
            && e.ArtifactPath == result.PackagePath
            && e.Success
            && e.PasswordWasSecretReference
            && e.VerificationSummary == "verified");
        signer.Requests.Should().ContainSingle();
        signer.Requests[0].FilePath.Should().Be(result.PackagePath);
        signer.Requests[0].CertificatePassword.Should().Be("resolved-password");
        signer.Requests[0].TimestampUrl.Should().Be("https://timestamp.acme.test");
        signer.Requests[0].ExpectedSubject.Should().Be("CN=ACME");
        result.SigningEvidence[0].ToolPath.Should().Be(@"C:\Tools\signtool.exe");
        result.SigningEvidence[0].ToolVersion.Should().Be("10.0.26100.1");
        result.SigningEvidence[0].CertificateSubject.Should().Be("CN=ACME Release");
        result.SigningEvidence[0].CertificateIssuer.Should().Be("CN=ACME Root");
        result.SigningEvidence[0].CertificateThumbprint.Should().Be("AABBCCDDEEFF00112233445566778899AABBCCDD");
        result.SigningEvidence[0].CertificateNotBeforeUtc.Should().Be(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        result.SigningEvidence[0].CertificateNotAfterUtc.Should().Be(DateTimeOffset.Parse("2027-01-01T00:00:00Z"));

        var reportText = File.ReadAllText(result.CapabilityReportPath);
        reportText.Should().NotContain("resolved-password");
        using var report = JsonDocument.Parse(reportText);
        var signing = report.RootElement.GetProperty("signingEvidence");
        signing.GetArrayLength().Should().Be(1);
        signing[0].GetProperty("artifactKind").GetString().Should().Be("msi");
        signing[0].GetProperty("toolVersion").GetString().Should().Be("10.0.26100.1");
        signing[0].GetProperty("certificateSubject").GetString().Should().Be("CN=ACME Release");
        signing[0].GetProperty("certificateIssuer").GetString().Should().Be("CN=ACME Root");
        signing[0].GetProperty("certificateThumbprint").GetString().Should().Be("AABBCCDDEEFF00112233445566778899AABBCCDD");
        signing[0].GetProperty("passwordWasSecretReference").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Generate_ValidatePackage_InvokesWixMsiValidateAndRecordsEvidence()
    {
        var project = CreateFileOnlyProject();
        var sourceDir = Path.Combine(_tempDir, "source", "bin");
        Directory.CreateDirectory(sourceDir);
        var sourceExe = Path.Combine(sourceDir, "MsiApp.exe");
        File.WriteAllText(sourceExe, "payload");
        project.Components[0].Files[0].SourcePath = sourceExe;
        var invocations = new List<MsiToolInvocation>();

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = _tempDir,
            BuildPackage = true,
            ValidatePackage = true,
            ValidationPdbPath = @"C:\Packages\Msi-App.wixpdb",
            ValidationCubePath = @"C:\Validation\enterprise.cub",
            ValidationIceIds = "ICE03;ICE64",
            ValidationSuppressIceIds = "ICE57",
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                invocations.Add(invocation);
                return new MsiToolResult
                {
                    ExitCode = 0,
                    StandardOutput = invocation.Arguments.Contains("validate") ? "validated" : "built"
                };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.ValidationPackagePath.Should().Be(result.PackagePath);
        result.WixValidationExitCode.Should().Be(0);
        result.WixValidationStandardOutput.Should().Be("validated");
        invocations.Should().HaveCount(2);
        invocations[0].Arguments.Should().StartWith(new[] { "build", result.WixSourcePath });
        invocations[1].Arguments.Should().ContainInOrder("msi", "validate", "-pdb", @"C:\Packages\Msi-App.wixpdb", "-cub", @"C:\Validation\enterprise.cub", "-ice", "ICE03", "-ice", "ICE64", "-sice", "ICE57", result.PackagePath);

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("validationPackagePath").GetString().Should().Be(result.PackagePath);
        report.RootElement.GetProperty("wixValidationCommandLine").GetString().Should().Contain("msi validate");
        report.RootElement.GetProperty("wixValidationExitCode").GetInt32().Should().Be(0);
    }

    [Fact]
    public void Generate_TransformPath_InvokesWixMsiTransformAndRecordsResult()
    {
        var project = CreateFileOnlyProject();
        var target = Path.Combine(_tempDir, "target.msi");
        var updated = Path.Combine(_tempDir, "updated.msi");
        var transform = Path.Combine(_tempDir, "enterprise.mst");
        File.WriteAllText(target, "target");
        File.WriteAllText(updated, "updated");
        MsiToolInvocation? captured = null;

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = _tempDir,
            TransformPath = transform,
            TransformTargetPackagePath = target,
            TransformUpdatedPackagePath = updated,
            TransformType = "language",
            TransformValidationFlags = "gl",
            TransformSuppressErrorFlags = "ef",
            PreserveUnchangedTransformRows = true,
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                captured = invocation;
                return new MsiToolResult
                {
                    ExitCode = 0,
                    StandardOutput = "transform"
                };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.TransformPath.Should().Be(transform);
        result.WixTransformExitCode.Should().Be(0);
        result.WixTransformStandardOutput.Should().Be("transform");
        captured.Should().NotBeNull();
        captured!.Arguments.Should().Equal(
            "msi",
            "transform",
            target,
            updated,
            "-out",
            transform,
            "-t",
            "language",
            "-val",
            "gl",
            "-serr",
            "ef",
            "-p");

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("transformPath").GetString().Should().Be(transform);
        report.RootElement.GetProperty("wixTransformExitCode").GetInt32().Should().Be(0);
    }

    [Theory]
    [InlineData(true, "x64")]
    [InlineData(false, "x86")]
    public void Generate_TransformPath_MaterializesProfilePropertiesBuildsUpdatedMsiAndRecordsEvidence(bool prefer64Bit, string architecture)
    {
        var project = CreateFileOnlyProject();
        project.Prefer64Bit = prefer64Bit;
        var target = Path.Combine(_tempDir, "target-profile.msi");
        var transform = Path.Combine(_tempDir, "enterprise-profile.mst");
        File.WriteAllText(target, "target");
        var invocations = new List<MsiToolInvocation>();

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "profile-transform"),
            TransformPath = transform,
            TransformTargetPackagePath = target,
            TransformProfile = "Enterprise",
            TransformPropertyValues = "INSTALLLEVEL=100;API_BASE_URL=https://api.example.test",
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                invocations.Add(invocation);
                if (invocation.Arguments.Count > 0
                    && invocation.Arguments[0] == "build"
                    && invocation.Arguments.Contains("-o"))
                {
                    var outputIndex = invocation.Arguments.ToList().IndexOf("-o");
                    File.WriteAllText(invocation.Arguments[outputIndex + 1], "updated msi");
                    return new MsiToolResult { ExitCode = 0, StandardOutput = "updated", ToolVersion = "wix 5.0.0" };
                }

                return new MsiToolResult { ExitCode = 0, StandardOutput = "transform", ToolVersion = "wix 5.0.0" };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.TransformProfile.Should().Be("Enterprise");
        result.TransformUpdatedSourcePath.Should().Be(Path.Combine(result.OutputDirectory, "Msi-App.TransformProfile.wxs"));
        result.TransformUpdatedPackagePath.Should().Be(Path.Combine(result.OutputDirectory, "Msi-App.TransformProfile.msi"));
        result.WixTransformUpdatedBuildExitCode.Should().Be(0);
        result.TransformProperties.Should().BeEquivalentTo(new[]
        {
            new { Id = "BEEP_PROFILE", Value = "Enterprise", Source = "profile" },
            new { Id = "INSTALLLEVEL", Value = "100", Source = "cli" },
            new { Id = "API_BASE_URL", Value = "https://api.example.test", Source = "cli" }
        });

        invocations.Should().HaveCount(2);
        invocations[0].Arguments.Should().Equal("build", result.TransformUpdatedSourcePath, "-arch", architecture, "-o", result.TransformUpdatedPackagePath);
        invocations[1].Arguments.Should().ContainInOrder("msi", "transform", target, result.TransformUpdatedPackagePath, "-out", transform);

        var profileXml = XDocument.Load(result.TransformUpdatedSourcePath).ToString(SaveOptions.DisableFormatting);
        profileXml.Should().Contain($"$(sys.BUILDARCH) != {architecture}");
        profileXml.Should().Contain("Property Id=\"BEEP_PROFILE\" Value=\"Enterprise\"");
        profileXml.Should().Contain("Property Id=\"INSTALLLEVEL\" Value=\"100\"");
        profileXml.Should().Contain("Property Id=\"API_BASE_URL\" Value=\"https://api.example.test\"");

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("transformProfile").GetString().Should().Be("Enterprise");
        report.RootElement.GetProperty("transformUpdatedSourcePath").GetString().Should().Be(result.TransformUpdatedSourcePath);
        report.RootElement.GetProperty("transformUpdatedPackagePath").GetString().Should().Be(result.TransformUpdatedPackagePath);
        report.RootElement.GetProperty("wixTransformUpdatedBuildToolVersion").GetString().Should().Be("wix 5.0.0");
        report.RootElement.GetProperty("transformProperties").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void Generate_TransformPath_RejectsInvalidProfilePropertyNames()
    {
        var project = CreateFileOnlyProject();
        var target = Path.Combine(_tempDir, "target-invalid-property.msi");
        File.WriteAllText(target, "target");

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "invalid-profile-transform"),
            TransformPath = Path.Combine(_tempDir, "invalid.mst"),
            TransformTargetPackagePath = target,
            TransformPropertyValues = "ApiBaseUrl=https://api.example.test"
        });

        result.HasErrors.Should().BeTrue();
        result.Findings.Should().ContainSingle(f =>
            f.Code == "BI1618"
            && f.OperationType == "msi.transform"
            && f.Message.Contains("ApiBaseUrl"));
        result.WixTransformCommandLine.Should().BeEmpty();
        result.TransformUpdatedPackagePath.Should().BeEmpty();
    }

    [Fact]
    public void Generate_TransformLifecycle_RunsInstallWithTransformAndUninstallAndRecordsEvidence()
    {
        var project = CreateFileOnlyProject();
        var target = Path.Combine(_tempDir, "target-transform-lifecycle.msi");
        var updated = Path.Combine(_tempDir, "updated-transform-lifecycle.msi");
        var transform = Path.Combine(_tempDir, "enterprise-lifecycle.mst");
        var logDir = Path.Combine(_tempDir, "mst-logs");
        File.WriteAllText(target, "target");
        File.WriteAllText(updated, "updated");
        var invocations = new List<MsiToolInvocation>();

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "mst-lifecycle"),
            TransformPath = transform,
            TransformTargetPackagePath = target,
            TransformUpdatedPackagePath = updated,
            VerifyTransformLifecycle = true,
            TransformLifecycleLogDirectory = logDir,
            TransformLifecycleProperties = "INSTALLLEVEL=100;BEEP_ENV=ci",
            MsiexecToolPath = @"C:\Windows\System32\msiexec.exe",
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                invocations.Add(invocation);
                if (invocation.ToolPath.EndsWith("wix.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var outputIndex = invocation.Arguments.ToList().IndexOf("-out");
                    File.WriteAllText(invocation.Arguments[outputIndex + 1], "mst");
                    return new MsiToolResult { ExitCode = 0, StandardOutput = "transform", ToolVersion = "wix 5.0.0" };
                }

                return new MsiToolResult { ExitCode = 0, StandardOutput = "ok", ToolVersion = "msiexec 10.0.26100.1" };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.TransformLifecyclePackagePath.Should().Be(target);
        result.TransformLifecycleTransformPath.Should().Be(transform);
        result.TransformLifecycleLogDirectory.Should().Be(logDir);
        result.TransformLifecycleEvidence.Select(e => e.Action).Should().Equal("transform-install", "transform-uninstall");
        result.TransformLifecycleEvidence.Should().OnlyContain(e => e.Success && e.ToolVersion == "msiexec 10.0.26100.1");

        var mstInvocations = invocations
            .Where(i => i.ToolPath.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase))
            .ToList();
        mstInvocations.Should().HaveCount(2);
        mstInvocations[0].Arguments.Should().ContainInOrder("/i", target, "TRANSFORMS=" + transform, "INSTALLLEVEL=100", "BEEP_ENV=ci", "/qn", "/norestart", "/L*v", Path.Combine(logDir, "apply-transform.log"));
        mstInvocations[1].Arguments.Should().ContainInOrder("/x", target, "INSTALLLEVEL=100", "BEEP_ENV=ci", "/qn", "/norestart", "/L*v", Path.Combine(logDir, "remove-transform.log"));

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("transformLifecyclePackagePath").GetString().Should().Be(target);
        report.RootElement.GetProperty("transformLifecycleTransformPath").GetString().Should().Be(transform);
        report.RootElement.GetProperty("transformLifecycleLogDirectory").GetString().Should().Be(logDir);
        report.RootElement.GetProperty("transformLifecycleEvidence").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void Generate_PatchPath_WritesPatchAuthoringInvokesWixBuildAndRecordsResult()
    {
        var project = CreateFileOnlyProject();
        var target = Path.Combine(_tempDir, "target.msi");
        var updated = Path.Combine(_tempDir, "updated.msi");
        var patch = Path.Combine(_tempDir, "enterprise.msp");
        File.WriteAllText(target, "target");
        File.WriteAllText(updated, "updated");
        MsiToolInvocation? captured = null;

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = _tempDir,
            PatchPath = patch,
            PatchTargetPackagePath = target,
            PatchUpdatedPackagePath = updated,
            PatchBaselineId = "RTM",
            PatchFamilyId = "EnterprisePatchFamily",
            PatchVersion = "1.2.4",
            PatchClassification = "Security Update",
            PatchAllowRemoval = false,
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                captured = invocation;
                return new MsiToolResult
                {
                    ExitCode = 0,
                    StandardOutput = "patch",
                    ToolVersion = "wix 5.0.0"
                };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.PatchPath.Should().Be(patch);
        result.PatchSourcePath.Should().Be(Path.Combine(_tempDir, "Msi-App.Patch.wxs"));
        result.WixPatchExitCode.Should().Be(0);
        result.WixPatchToolVersion.Should().Be("wix 5.0.0");
        result.WixPatchStandardOutput.Should().Be("patch");
        captured.Should().NotBeNull();
        captured!.Arguments.Should().Equal("build", result.PatchSourcePath, "-out", patch);

        var patchXml = XDocument.Load(result.PatchSourcePath).ToString(SaveOptions.DisableFormatting);
        patchXml.Should().Contain("Patch");
        patchXml.Should().Contain("AllowRemoval=\"no\"");
        patchXml.Should().Contain("DisplayName=\"Msi App Patch 1.2.4\"");
        patchXml.Should().Contain("Classification=\"Security Update\"");
        patchXml.Should().Contain("PatchBaseline");
        patchXml.Should().Contain($"BaselineFile=\"{target}\"");
        patchXml.Should().Contain($"UpdateFile=\"{updated}\"");
        patchXml.Should().Contain("PatchFamily");
        patchXml.Should().Contain("Id=\"EnterprisePatchFamily\"");
        patchXml.Should().Contain("Version=\"1.2.4\"");
        patchXml.Should().Contain("Supersede=\"yes\"");

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("patchSourcePath").GetString().Should().Be(result.PatchSourcePath);
        report.RootElement.GetProperty("patchPath").GetString().Should().Be(patch);
        report.RootElement.GetProperty("wixPatchToolVersion").GetString().Should().Be("wix 5.0.0");
        report.RootElement.GetProperty("wixPatchExitCode").GetInt32().Should().Be(0);
        report.RootElement.GetProperty("patchBaselineId").GetString().Should().Be("RTM");
        report.RootElement.GetProperty("patchFamilyId").GetString().Should().Be("EnterprisePatchFamily");
        report.RootElement.GetProperty("patchVersion").GetString().Should().Be("1.2.4");
        report.RootElement.GetProperty("patchClassification").GetString().Should().Be("Security Update");
        report.RootElement.GetProperty("patchAllowRemoval").GetBoolean().Should().BeFalse();
        report.RootElement.GetProperty("patchSupersede").GetBoolean().Should().BeTrue();
        report.RootElement.GetProperty("patchTargetSha256").GetString().Should().Be(Sha256(target));
        report.RootElement.GetProperty("patchUpdatedSha256").GetString().Should().Be(Sha256(updated));
        report.RootElement.GetProperty("patchDeltaChanged").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Generate_PatchPath_FailsEmptyDeltaUnlessExplicitlyAllowed()
    {
        var project = CreateFileOnlyProject();
        var target = Path.Combine(_tempDir, "target-empty-delta.msi");
        var updated = Path.Combine(_tempDir, "updated-empty-delta.msi");
        File.WriteAllText(target, "same");
        File.WriteAllText(updated, "same");

        var strict = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "empty-delta-strict"),
            PatchPath = Path.Combine(_tempDir, "strict-empty.msp"),
            PatchTargetPackagePath = target,
            PatchUpdatedPackagePath = updated
        });

        strict.HasErrors.Should().BeTrue();
        strict.Findings.Should().ContainSingle(f =>
            f.Code == "BI1625"
            && f.OperationType == "msi.patch"
            && f.Message.Contains("target and updated packages to differ"));
        strict.WixPatchCommandLine.Should().BeEmpty();
        strict.PatchDeltaChanged.Should().BeFalse();

        var permissive = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "empty-delta-permissive"),
            PatchPath = Path.Combine(_tempDir, "permissive-empty.msp"),
            PatchTargetPackagePath = target,
            PatchUpdatedPackagePath = updated,
            PatchRequireChangedPackages = false,
            PatchSupersede = false,
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation => new MsiToolResult { ExitCode = 0, StandardOutput = "patch" }
        });

        permissive.HasErrors.Should().BeFalse();
        permissive.PatchDeltaChanged.Should().BeFalse();
        XDocument.Load(permissive.PatchSourcePath)
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Contain("Supersede=\"no\"");
    }

    [Fact]
    public void Generate_PatchPath_RejectsUnsafePatchFamilyIdentifiers()
    {
        var project = CreateFileOnlyProject();
        var target = Path.Combine(_tempDir, "target-unsafe-family.msi");
        var updated = Path.Combine(_tempDir, "updated-unsafe-family.msi");
        File.WriteAllText(target, "target");
        File.WriteAllText(updated, "updated");

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "unsafe-family"),
            PatchPath = Path.Combine(_tempDir, "unsafe-family.msp"),
            PatchTargetPackagePath = target,
            PatchUpdatedPackagePath = updated,
            PatchFamilyId = "Enterprise Patch Family"
        });

        result.HasErrors.Should().BeTrue();
        result.Findings.Should().ContainSingle(f =>
            f.Code == "BI1626"
            && f.OperationType == "msi.patch"
            && f.Message.Contains("not a valid MSI identifier"));
        result.WixPatchCommandLine.Should().BeEmpty();
    }

    [Fact]
    public void Generate_PatchPath_SignsMspAndRecordsEvidence()
    {
        var project = CreateFileOnlyProject();
        var target = Path.Combine(_tempDir, "target-signed.msi");
        var updated = Path.Combine(_tempDir, "updated-signed.msi");
        var patch = Path.Combine(_tempDir, "enterprise-signed.msp");
        File.WriteAllText(target, "target");
        File.WriteAllText(updated, "updated");
        project.CodeSignCertificatePath = Path.Combine(_tempDir, "release.pfx");
        project.CodeSignCertificatePassword = "secret://env/SIGNING_PFX_PASSWORD";

        var signer = new CapturingSigner();

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "signed-msp"),
            PatchPath = patch,
            PatchTargetPackagePath = target,
            PatchUpdatedPackagePath = updated,
            SignOutput = true,
            SigningService = new CodeSigningService(signer, new FixedSecretProvider("env", "resolved-password")),
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                var arguments = invocation.Arguments.ToList();
                var outputIndex = arguments.IndexOf("-out");
                File.WriteAllText(arguments[outputIndex + 1], "msp");
                return new MsiToolResult { ExitCode = 0, StandardOutput = "patch", ToolVersion = "wix 5.0.0" };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.SigningEvidence.Should().ContainSingle(e =>
            e.ArtifactKind == "msp"
            && e.ArtifactPath == patch
            && e.Success
            && e.PasswordWasSecretReference);
        signer.Requests.Should().ContainSingle();
        signer.Requests[0].FilePath.Should().Be(patch);

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        var signing = report.RootElement.GetProperty("signingEvidence");
        signing.GetArrayLength().Should().Be(1);
        signing[0].GetProperty("artifactKind").GetString().Should().Be("msp");
        signing[0].GetProperty("artifactPath").GetString().Should().Be(patch);
        signing[0].GetProperty("toolVersion").GetString().Should().Be("10.0.26100.1");
    }

    [Fact]
    public void Generate_PatchLifecycle_RunsApplyAndRemoveAndRecordsEvidence()
    {
        var project = CreateFileOnlyProject();
        var target = Path.Combine(_tempDir, "target-lifecycle.msi");
        var updated = Path.Combine(_tempDir, "updated-lifecycle.msi");
        var patch = Path.Combine(_tempDir, "enterprise-lifecycle.msp");
        var logDir = Path.Combine(_tempDir, "msp-logs");
        File.WriteAllText(target, "target");
        File.WriteAllText(updated, "updated");
        var invocations = new List<MsiToolInvocation>();

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "msp-lifecycle"),
            PatchPath = patch,
            PatchTargetPackagePath = target,
            PatchUpdatedPackagePath = updated,
            VerifyPatchLifecycle = true,
            PatchLifecycleProductPackagePath = target,
            PatchLifecycleLogDirectory = logDir,
            PatchLifecycleProperties = "PATCH_ENV=ci",
            MsiexecToolPath = @"C:\Windows\System32\msiexec.exe",
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                invocations.Add(invocation);
                if (invocation.ToolPath.EndsWith("wix.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var outputIndex = invocation.Arguments.ToList().IndexOf("-out");
                    File.WriteAllText(invocation.Arguments[outputIndex + 1], "msp");
                    return new MsiToolResult { ExitCode = 0, StandardOutput = "patch", ToolVersion = "wix 5.0.0" };
                }

                return new MsiToolResult { ExitCode = 0, StandardOutput = "ok", ToolVersion = "msiexec 10.0.26100.1" };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.PatchLifecycleProductPackagePath.Should().Be(target);
        result.PatchLifecycleLogDirectory.Should().Be(logDir);
        result.PatchLifecycleEvidence.Select(e => e.Action).Should().Equal("patch-apply", "patch-remove");
        result.PatchLifecycleEvidence.Should().OnlyContain(e => e.Success && e.ToolVersion == "msiexec 10.0.26100.1");

        var mspInvocations = invocations
            .Where(i => i.ToolPath.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase))
            .ToList();
        mspInvocations.Should().HaveCount(2);
        mspInvocations[0].Arguments.Should().ContainInOrder("/p", patch, "REINSTALL=ALL", "REINSTALLMODE=ecmus", "PATCH_ENV=ci", "/qn", "/norestart", "/L*v", Path.Combine(logDir, "apply-patch.log"));
        mspInvocations[1].Arguments.Should().ContainInOrder("/package", target, "/uninstall", patch, "PATCH_ENV=ci", "/qn", "/norestart", "/L*v", Path.Combine(logDir, "remove-patch.log"));

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("patchLifecycleProductPackagePath").GetString().Should().Be(target);
        report.RootElement.GetProperty("patchLifecycleLogDirectory").GetString().Should().Be(logDir);
        report.RootElement.GetProperty("patchLifecycleEvidence").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void Generate_LifecycleMatrix_RunsExternalRunnerForEachTargetAndRecordsEvidence()
    {
        var project = CreateFileOnlyProject();
        var target = Path.Combine(_tempDir, "target-matrix.msi");
        var updated = Path.Combine(_tempDir, "updated-matrix.msi");
        var transform = Path.Combine(_tempDir, "enterprise-matrix.mst");
        var patch = Path.Combine(_tempDir, "enterprise-matrix.msp");
        var logDir = Path.Combine(_tempDir, "matrix-logs");
        File.WriteAllText(target, "target");
        File.WriteAllText(updated, "updated");
        File.WriteAllText(transform, "mst");
        var invocations = new List<MsiToolInvocation>();

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "matrix"),
            LifecyclePackagePath = updated,
            TransformLifecycleTransformPath = transform,
            PatchPath = patch,
            PatchTargetPackagePath = target,
            PatchUpdatedPackagePath = updated,
            PatchLifecycleProductPackagePath = target,
            VerifyLifecycleMatrix = true,
            LifecycleMatrixTargets = "win10-22h2|Windows 10 22H2|x64|hyperv;win11-23h2|Windows 11 23H2|arm64|azure",
            LifecycleMatrixRunnerPath = @"C:\Tools\beep-matrix-runner.exe",
            LifecycleMatrixLogDirectory = logDir,
            LifecycleMatrixProperties = "INSTALLLEVEL=100;TENANT=acme",
            LifecycleMatrixScenarioPackPath = "enterprise-default",
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                invocations.Add(invocation);
                if (invocation.ToolPath.EndsWith("wix.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var outputIndex = invocation.Arguments.ToList().IndexOf("-out");
                    File.WriteAllText(invocation.Arguments[outputIndex + 1], "msp");
                    return new MsiToolResult { ExitCode = 0, StandardOutput = "patch", ToolVersion = "wix 5.0.0" };
                }

                return new MsiToolResult
                {
                    ExitCode = 0,
                    StandardOutput = "matrix ok",
                    ToolVersion = "matrix 1.0.0"
                };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.LifecycleMatrixLogDirectory.Should().Be(logDir);
        result.LifecycleMatrixEvidence.Select(e => e.EnvironmentId).Should().Equal("win10-22h2", "win11-23h2");
        result.LifecycleMatrixEvidence.Should().OnlyContain(e =>
            e.Success
            && e.ToolPath == @"C:\Tools\beep-matrix-runner.exe"
            && e.ToolVersion == "matrix 1.0.0"
            && e.StandardOutput == "matrix ok");

        var matrixInvocations = invocations
            .Where(i => i.ToolPath.EndsWith("beep-matrix-runner.exe", StringComparison.OrdinalIgnoreCase))
            .ToList();
        matrixInvocations.Should().HaveCount(2);
        matrixInvocations[0].Arguments.Should().ContainInOrder(
            "qualify",
            "--environment", "win10-22h2",
            "--os", "Windows 10 22H2",
            "--arch", "x64",
            "--channel", "hyperv",
            "--msi", target,
            "--updated-msi", updated,
            "--mst", transform,
            "--msp", patch,
            "--msp-product", target,
            "--scenario-pack", "enterprise-default",
            "--property", "INSTALLLEVEL=100",
            "--property", "TENANT=acme");
        matrixInvocations[0].Arguments.Should().Contain(Path.Combine(logDir, "win10-22h2"));
        matrixInvocations[1].Arguments.Should().ContainInOrder("--environment", "win11-23h2", "--os", "Windows 11 23H2", "--arch", "arm64", "--channel", "azure");

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("lifecycleMatrixLogDirectory").GetString().Should().Be(logDir);
        report.RootElement.GetProperty("lifecycleMatrixEvidence").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void Generate_LifecycleVerification_RunsInstallRepairUninstallAndRecordsEvidence()
    {
        var project = CreateFileOnlyProject();
        var sourceDir = Path.Combine(_tempDir, "lifecycle-source", "bin");
        Directory.CreateDirectory(sourceDir);
        var sourceExe = Path.Combine(sourceDir, "MsiApp.exe");
        File.WriteAllText(sourceExe, "payload");
        project.Components[0].Files[0].SourcePath = sourceExe;
        var invocations = new List<MsiToolInvocation>();

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "lifecycle-msi"),
            BuildPackage = true,
            VerifyLifecycle = true,
            LifecycleLogDirectory = Path.Combine(_tempDir, "logs"),
            LifecycleProperties = "INSTALLLEVEL=100;BEEP_ENV=ci",
            WixToolPath = @"C:\Tools\wix.exe",
            MsiexecToolPath = @"C:\Windows\System32\msiexec.exe",
            ToolRunner = invocation =>
            {
                invocations.Add(invocation);
                if (invocation.ToolPath.EndsWith("wix.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var arguments = invocation.Arguments.ToList();
                    var outputIndex = arguments.IndexOf("-o");
                    File.WriteAllText(arguments[outputIndex + 1], "msi");
                    return new MsiToolResult { ExitCode = 0, StandardOutput = "built", ToolVersion = "wix 5.0.0" };
                }

                return new MsiToolResult
                {
                    ExitCode = 0,
                    StandardOutput = "ok",
                    ToolVersion = "msiexec 10.0.26100.1"
                };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.LifecyclePackagePath.Should().Be(result.PackagePath);
        result.LifecycleLogDirectory.Should().Be(Path.Combine(_tempDir, "logs"));
        result.MsiexecToolVersion.Should().Be("msiexec 10.0.26100.1");
        result.LifecycleEvidence.Select(e => e.Action).Should().Equal("install", "repair", "uninstall");
        result.LifecycleEvidence.Should().OnlyContain(e =>
            e.Success
            && e.ToolPath == @"C:\Windows\System32\msiexec.exe"
            && e.PackagePath == result.PackagePath
            && e.CommandLine.Contains("INSTALLLEVEL=100", StringComparison.Ordinal)
            && e.CommandLine.Contains("BEEP_ENV=ci", StringComparison.Ordinal)
            && e.CommandLine.Contains("/qn", StringComparison.Ordinal)
            && e.CommandLine.Contains("/norestart", StringComparison.Ordinal)
            && e.CommandLine.Contains("/L*v", StringComparison.Ordinal));

        var lifecycleInvocations = invocations
            .Where(i => i.ToolPath.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase))
            .ToList();
        lifecycleInvocations.Should().HaveCount(3);
        lifecycleInvocations[0].Arguments.Should().ContainInOrder("/i", result.PackagePath, "INSTALLLEVEL=100", "BEEP_ENV=ci", "/qn", "/norestart", "/L*v", Path.Combine(_tempDir, "logs", "install.log"));
        lifecycleInvocations[1].Arguments.Should().ContainInOrder("/famus", result.PackagePath, "INSTALLLEVEL=100", "BEEP_ENV=ci", "/qn", "/norestart", "/L*v", Path.Combine(_tempDir, "logs", "repair.log"));
        lifecycleInvocations[2].Arguments.Should().ContainInOrder("/x", result.PackagePath, "INSTALLLEVEL=100", "BEEP_ENV=ci", "/qn", "/norestart", "/L*v", Path.Combine(_tempDir, "logs", "uninstall.log"));

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("lifecyclePackagePath").GetString().Should().Be(result.PackagePath);
        report.RootElement.GetProperty("lifecycleLogDirectory").GetString().Should().Be(Path.Combine(_tempDir, "logs"));
        report.RootElement.GetProperty("msiexecToolVersion").GetString().Should().Be("msiexec 10.0.26100.1");
        report.RootElement.GetProperty("lifecycleEvidence").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void Generate_LifecycleVerification_FailsLoudlyButStillCollectsUninstallEvidenceAfterRepairFailure()
    {
        var project = CreateFileOnlyProject();
        var package = Path.Combine(_tempDir, "existing.msi");
        File.WriteAllText(package, "msi");
        var actions = new List<string>();

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "lifecycle-failure"),
            VerifyLifecycle = true,
            LifecyclePackagePath = package,
            MsiexecToolPath = "msiexec",
            ToolRunner = invocation =>
            {
                var action = invocation.Arguments[0] switch
                {
                    "/i" => "install",
                    "/famus" => "repair",
                    "/x" => "uninstall",
                    _ => "unknown"
                };
                actions.Add(action);
                return new MsiToolResult
                {
                    ExitCode = action == "repair" ? 1603 : 0,
                    StandardError = action == "repair" ? "repair failed" : "",
                    ToolVersion = "msiexec 10.0.26100.1"
                };
            }
        });

        result.HasErrors.Should().BeTrue();
        result.Findings.Should().Contain(f => f.Code == "BI1617" && f.OperationType == "msi.lifecycle" && f.Message.Contains("repair failed with exit code 1603"));
        actions.Should().Equal("install", "repair", "uninstall");
        result.LifecycleEvidence.Select(e => e.Success).Should().Equal(true, false, true);

        using var report = JsonDocument.Parse(File.ReadAllText(result.CapabilityReportPath));
        report.RootElement.GetProperty("findings").EnumerateArray().Should().Contain(f => f.GetProperty("Code").GetString() == "BI1617");
        report.RootElement.GetProperty("lifecycleEvidence").EnumerateArray().Should().Contain(e =>
            e.GetProperty("action").GetString() == "uninstall"
            && e.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void Generate_MapsKernelDriversToFireGiantDriverAuthoringAndStagesPayload()
    {
        var project = CreateFileOnlyProject();
        var appPath = Path.Combine(_tempDir, "MsiApp.exe");
        var driverPath = Path.Combine(_tempDir, "acmevirt.sys");
        File.WriteAllText(appPath, "app");
        File.WriteAllText(driverPath, "driver");
        project.Components[0].Files[0].SourcePath = appPath;
        project.DriverPackages.Add(new DriverPackageDefinition
        {
            Name = "ACME Virtual Device",
            Kind = DriverPackageKind.Kernel,
            DriverBinaryPath = driverPath,
            ServiceName = "AcmeVirt",
            DisplayName = "ACME Virtual Device Driver",
            StartMode = DriverPackageStartMode.Demand,
            ErrorControl = DriverPackageErrorControl.Normal,
            LoadOrderGroup = "Base",
            DependsOn = new List<string> { "Tcpip", "group:Network" }
        });
        MsiToolInvocation? captured = null;

        var result = MsiPackageExporter.Generate(project, new MsiExportOptions
        {
            OutputDirectory = Path.Combine(_tempDir, "driver-msi"),
            BuildPackage = true,
            WixToolPath = @"C:\Tools\wix.exe",
            ToolRunner = invocation =>
            {
                captured = invocation;
                return new MsiToolResult { ExitCode = 0, StandardOutput = "built" };
            }
        });

        result.HasErrors.Should().BeFalse();
        result.Components.Should().Contain(c => c.OperationType == "driver.package" && c.DirectoryId == "SystemFolder");
        result.RequiredWixExtensions.Should().Contain("FireGiant.HeatWave.BuildTools.wixext");
        captured.Should().NotBeNull();
        captured!.Arguments.Should().ContainInOrder("-ext", "FireGiant.HeatWave.BuildTools.wixext");

        var xml = XDocument.Load(result.WixSourcePath).ToString(SaveOptions.DisableFormatting);
        xml.Should().Contain("http://www.firegiant.com/schemas/v4/wxs/heatwave/buildtools");
        xml.Should().Contain("StandardDirectory Id=\"SystemFolder\"");
        xml.Should().Contain("File");
        xml.Should().Contain("acmevirt.sys");
        xml.Should().Contain("Driver Name=\"AcmeVirt\"");
        xml.Should().Contain("DisplayName=\"ACME Virtual Device Driver\"");
        xml.Should().Contain("Type=\"kernel\"");
        xml.Should().Contain("ErrorControl=\"normal\"");
        xml.Should().Contain("Start=\"demand\"");
        xml.Should().Contain("BinaryPath=\"System32\\acmevirt.sys\"");
        xml.Should().Contain("LoadOrderGroup=\"Base\"");
        xml.Should().Contain("DriverDependency Name=\"Tcpip\"");
        xml.Should().Contain("DriverDependency Name=\"Network\" Group=\"yes\"");

        using var payloadManifest = JsonDocument.Parse(File.ReadAllText(result.PayloadManifestPath));
        payloadManifest.RootElement.EnumerateArray().Should().Contain(entry =>
            entry.GetProperty("operationType").GetString() == "driver.package"
            && entry.GetProperty("sourcePath").GetString() == driverPath);
    }

    private static Beep.Installer.Models.InstallProject CreateFileOnlyProject()
    {
        var project = InstallerProjectFactory.CreateNew("Msi App", "1.2.3-preview.4", "ACME", "");
        project.DefaultScope = Beep.Installer.Models.InstallationScope.Machine;
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
                    SourcePath = "bin\\MsiApp.exe",
                    DestinationPath = "{InstallPath}\\MsiApp.exe",
                    Description = "Main executable"
                }
            }
        });
        return project;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed class CapturingSigner : IAuthenticodeSigner
    {
        public List<AuthenticodeSigningRequest> Requests { get; } = new();

        public CodeSigningResult Sign(AuthenticodeSigningRequest request)
        {
            Requests.Add(request);
            return new CodeSigningResult
            {
                Success = true,
                VerificationSummary = "verified",
                ToolPath = @"C:\Tools\signtool.exe",
                ToolVersion = "10.0.26100.1",
                CertificateSubject = "CN=ACME Release",
                CertificateIssuer = "CN=ACME Root",
                CertificateThumbprint = "AABBCCDDEEFF00112233445566778899AABBCCDD",
                CertificateNotBeforeUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                CertificateNotAfterUtc = DateTimeOffset.Parse("2027-01-01T00:00:00Z")
            };
        }
    }

    private sealed class FixedSecretProvider : ISecretProvider
    {
        private readonly string _scheme;
        private readonly string _value;

        public FixedSecretProvider(string scheme, string value)
        {
            _scheme = scheme;
            _value = value;
        }

        public bool Supports(string scheme)
            => scheme.Equals(_scheme, StringComparison.OrdinalIgnoreCase);

        public SecretResolutionResult Resolve(SecretReference reference)
            => SecretResolutionResult.Found(_value);
    }
}
