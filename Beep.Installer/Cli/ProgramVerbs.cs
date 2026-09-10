using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using Beep.Installer.Cli;
using Beep.Installer.Engine;
using Beep.Installer.Engine.Updates;
using Beep.Installer.Forms;

namespace Beep.Installer;

internal static partial class Program
{
    /// <summary>
    /// Every CLI verb, in dispatch order. Order is behaviour: <c>/PUBLISHFEED=</c> is ahead of
    /// <c>/BUILD=</c> so that <c>/BUILD=&lt;project&gt; /PUBLISHFEED=&lt;dir&gt;</c> publishes
    /// rather than only building, and <c>/RECOVERDELTA=</c> is ahead of <c>/ROLLBACKDELTA=</c>.
    /// Keep new verbs in the position their precedence requires, not at the end.
    ///
    /// This table is also what decides whether a console gets attached, so a verb cannot be
    /// dispatchable but console-less (or the reverse) the way two hand-kept lists allowed.
    /// </summary>
    internal static IReadOnlyList<CliVerb> Verbs { get; } = new[]
    {
        // Flags that override everything.
        CliVerb.Flag(_ => { PrintUsage(); return 0; }, tokens: new[] { "/?", "/H", "/HELP" }),
        CliVerb.Flag(_ => { Console.WriteLine($"Beep Installer v{AppInfo.Version}"); return 0; }, tokens: new[] { "/VER" }),
        CliVerb.Flag(o => RunListTemplates(o.Args), tokens: new[] { "/LISTTEMPLATES" }),

        // Runtime maintenance — used by the SHIPPED installer and by Add/Remove Programs.
        // /VERYSILENT and /SUPPRESSMSGBOXES are Inno Setup's grammar; accepting them means
        // deployment scripts written for Inno (Intune/SCCM/winget) work unchanged.
        CliVerb.Flag(RunSilentInstallVerb, tokens: new[] { "/S", "/SILENT", "/VERYSILENT" }),
        CliVerb.Flag(o => LoadRuntimeProject(o.Args) is { } p ? RunUninstall(p, o.Args) : 2, tokens: new[] { "/UNINSTALL" }),
        CliVerb.Flag(o => LoadRuntimeProject(o.Args) is { } p ? RunRepair(p, o.Args) : 2, tokens: new[] { "/REPAIR" }),
        CliVerb.Flag(_ => RunSelfTest(), tokens: new[] { "/SELFTEST" }),

        // App self-update — thin wrappers over the BeepDM IAppUpdateService.
        CliVerb.Flag(o => RunCheckUpdate(o.Args), tokens: new[] { "/CHECKUPDATE" }),
        CliVerb.Flag(o => RunUpdate(o.Args), tokens: new[] { "/UPDATE" }),

        // Authoring, build and packaging.
        CliVerb.Value("/PUBLISHFEED=", (o, v) => RunPublishFeed(v, o.Args)),
        CliVerb.Value("/BUILD=", (o, v) => RunHeadlessBuild(v, o.Args)),
        CliVerb.Value("/VALIDATE=", (o, v) => RunValidate(v, o.Args)),
        CliVerb.Value("/CANONICALIZE=", (o, v) => RunCanonicalize(v, o.Args)),
        CliVerb.Value("/PLAN=", (o, v) => RunPlan(v, o.Args)),
        CliVerb.Value("/FORMATREADINESS=", (o, v) => RunFormatReadiness(v, o.Args)),
        CliVerb.Value("/EXPORTTEMPLATEPACKAGE=", (o, v) => RunExportTemplatePackage(v, o.Args)),
        CliVerb.Value("/VERIFYTEMPLATEPACKAGE=", (o, v) => RunVerifyTemplatePackage(v, o.Args)),
        CliVerb.Value("/QUALIFYSDK=", (o, v) => RunQualifyHeadlessSdk(v, o.Args)),
        CliVerb.Value("/PUBLISHSDK=", (o, v) => RunPublishSdkPackage(v, o.Args)),
        CliVerb.Value("/QUALIFYPLAN=", (o, v) => RunQualifyCompiledPlan(v, o.Args)),
        CliVerb.Value("/PROPERTIES=", (o, v) => RunEnterpriseProperties(v, o.Args)),
        CliVerb.Value("/QUALIFYCLI=", (o, v) => RunQualifyEnterpriseCli(v, o.Args)),
        CliVerb.Value("/QUALIFYCONFIG=", (o, v) => RunQualifyConfigTransforms(v, o.Args)),
        CliVerb.Value("/EXPORTCATALOG=", (o, v) => RunPrerequisiteCatalogExport(v, o.Args)),
        CliVerb.Value("/QUALIFYCATALOG=", (o, v) => RunPrerequisiteCatalogQualification(v, o.Args)),

        // Update channels. Apply and check are ahead of verify/qualify, as they always were.
        CliVerb.Value("/UPDATECHANNELFEED=", (o, v) => RunUpdateChannelFeedExport(v, o.Args)),
        CliVerb.LastValue("/APPLYUPDATECHANNEL=", (o, v) => RunCheckUpdateChannel(v, o.Args, applyDelta: true)),
        CliVerb.LastValue("/CHECKUPDATECHANNEL=", (o, v) => RunCheckUpdateChannel(v, o.Args)),
        CliVerb.Value("/VERIFYUPDATECHANNELFEED=", (o, v) => RunVerifyUpdateChannelFeed(v, o.Args)),
        CliVerb.Value("/QUALIFYUPDATECHANNELFEED=", (o, v) => RunQualifyUpdateChannelFeed(v, o.Args)),

        // Delta packages.
        CliVerb.Value("/DELTA=", (o, v) => RunDeltaBuild(v, o.Args)),
        CliVerb.Value("/VERIFYDELTA=", (o, v) => RunDeltaVerify(v, o.Args)),
        CliVerb.Value("/QUALIFYDELTA=", (o, v) => RunQualifyDelta(v, o.Args)),
        CliVerb.Value("/APPLYDELTA=", (o, v) => RunDeltaApply(v, o.Args)),
        CliVerb.LastValue("/RECOVERDELTA=", (o, v) => RunDeltaRecover(v, o.Args)),
        CliVerb.Value("/ROLLBACKDELTA=", (o, v) => RunDeltaRollback(v, o.Args)),

        // Offline layouts and qualification runners.
        CliVerb.Value("/LAYOUT=", (o, v) => RunOfflineLayout(v, o.Args)),
        CliVerb.Value("/VERIFYLAYOUT=", (o, v) => RunVerifyOfflineLayout(v, o.Args)),
        CliVerb.Value("/QUALIFYLAYOUT=", (o, v) => RunQualifyOfflineLayout(v, o.Args)),
        CliVerb.Value("/QUALIFYA11Y=", (o, v) => RunQualifyAccessibilityLocalization(v, o.Args)),
        CliVerb.Value("/QUALIFYVM=", (o, v) => RunQualifyVmReadiness(v, o.Args)),
        CliVerb.Value("/QUALIFYRELEASE=", (o, v) => RunQualifyReleasePortfolio(v, o.Args)),
        CliVerb.Flag(o => RunQualifySigning(o.Args), tokens: new[] { "/QUALIFYSIGNING" }),
        CliVerb.Value("/QUALIFYUPGRADE=", (o, v) => RunQualifyUpgrade(v, o.Args)),
        CliVerb.Value("/QUALIFYRECOVERY=", (o, v) => RunQualifyRecovery(v, o.Args)),
        CliVerb.Value("/QUALIFYSERVICES=", (o, v) => RunQualifyServices(v, o.Args)),
        CliVerb.Value("/QUALIFYIIS=", (o, v) => RunQualifyIis(v, o.Args)),
        CliVerb.Value("/QUALIFYSYSTEM=", (o, v) => RunQualifySystemResources(v, o.Args)),
        CliVerb.Value("/QUALIFYDEPLOYMENTKIT=", (o, v) => RunDeploymentKitQualification(v, o.Args)),
        CliVerb.Value("/DEPLOYMENTKIT=", (o, v) => RunDeploymentKit(v, o.Args)),

        // Other package formats.
        CliVerb.Value("/MSI=", (o, v) => RunMsiExport(v, o.Args)),
        CliVerb.Value("/WINGET=", (o, v) => RunWinGetExport(v, o.Args)),

        // Release evidence and security.
        CliVerb.Value("/QUALIFYEVIDENCE=", (o, v) => RunQualifyReleaseEvidence(v, o.Args)),
        CliVerb.Value("/EVIDENCE=", (o, v) => RunReleaseEvidence(v, o.Args)),
        CliVerb.Value("/VERIFYEVIDENCE=", (o, v) => RunVerifyReleaseEvidence(v, o.Args)),
        CliVerb.Value("/QUALIFYSECURITY=", (o, v) => RunQualifySupplyChainSecurity(v, o.Args)),
        CliVerb.Value("/SECURITYSCAN=", (o, v) => RunSupplyChainScan(v, o.Args)),
        CliVerb.Value("/QUALIFYDIAGNOSTICS=", (o, v) => RunQualifyDiagnostics(v, o.Args)),
        CliVerb.Value("/RECOVERY=", (o, v) => RunRecovery(v, o.Args)),

        // Extension SDK.
        CliVerb.Value("/EXTENSIONEXPORT=", (o, v) => RunExtensionExport(v, o.Args)),
        CliVerb.Value("/EXTENSIONS=", (o, v) => RunExtensionDiscovery(v, o.Args)),
        CliVerb.Value("/EXTENSIONTEMPLATE=", (o, v) => RunExtensionTemplate(v, o.Args)),
        CliVerb.Value("/EXTENSIONCONFORMANCE=", (o, v) => RunExtensionConformance(v, o.Args)),
        CliVerb.Value("/QUALIFYEXTENSIONSDK=", (o, v) => RunQualifyExtensionSdkCompatibility(v, o.Args)),

        CliVerb.Value("/PREVIEW=", (_, v) => RunPreview(v)),
        CliVerb.Value("/PUBLISH=", (o, v) => RunPublish(v, o.Args)),

        // Windowed tools: no console, so they are not headless.
        CliVerb.Flag(RunUpdateCenterVerb, headless: false, tokens: new[] { "/UPDATEUI" }),
        CliVerb.Flag(_ => { Application.Run(new LanguageManagerForm()); return 0; }, headless: false, tokens: new[] { "/LANGMGR" }),
    };

    private static int RunSilentInstallVerb(CliOptions options)
    {
        var project = LoadRuntimeProject(options.Args);
        if (project == null) { Console.Error.WriteLine("No installer script was found in the executable."); return 2; }
        return RunSilentInstall(project, options.Args);
    }

    private static int RunUpdateCenterVerb(CliOptions options)
    {
        var project = LoadRuntimeProject(options.Args);
        if (project is null) { ShowFatalMessage("Update center requires a project script (/SCRIPT=<path>)."); return 2; }
        Application.Run(CreateUpdateCenter(project, options.Args));
        return 0;
    }

    private static int RunDeltaRecover(string journalPath, string[] args)
    {
        var recovery = new DeltaUpdatePackageService().RecoverAtomicApply(new() { JournalPath = journalPath }, IsDryRun(args));
        if (!recovery.Success) Console.Error.WriteLine(recovery.Error);
        if (Has(args, "/JSON"))
            Console.WriteLine(JsonSerializer.Serialize(new { action = "recover-delta", dryRun = IsDryRun(args), result = recovery },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        else if (recovery.Success)
            Console.WriteLine($"Delta recovery {(IsDryRun(args) ? "preview" : "complete")}: {recovery.RecoveryState}. Journal: {recovery.JournalPath}");
        return recovery.Success ? 0 : 1;
    }

    /// <summary>
    /// No verb matched: act as the shipped installer if a script travels with this executable,
    /// otherwise open the Package Builder.
    /// </summary>
    private static int RunDefaultMode(string[] args)
    {
        if (IsRuntimeMode())
        {
            var project = LoadRuntimeProject(args);
            if (project == null) { ShowFatalMessage("Installer script could not be parsed — the installer is corrupted."); return 2; }
            Application.Run(new BeepModernInstallerForm(project, previewMode: false, runtimeArgs: args));
            return 0;
        }

        // Resolved from the composition root when there is one; constructed directly only when the
        // shell is driven in-process by a test, which never goes through Main.
        if (Services?.GetService(typeof(PackageBuilderForm)) is PackageBuilderForm form)
            Application.Run(form);
        else
            RunColdStart(args);
        return 0;
    }

    /// <summary>
    /// Cold start (P12 §3.4, §5 12.D.2). A returning user with a valid recent project reopens it
    /// straight into Advanced mode -- unchanged from before this project ever had a wizard shell, no
    /// re-walk of steps already filled in. A genuinely fresh start opens <see cref="BuilderWizardForm"/>
    /// as the primary window instead of the flat 40-section builder: it absorbs the fields
    /// <see cref="ProjectNewDialog"/>/<see cref="QuickStartWizard"/> used to collect across two separate
    /// popup windows, and "Open full editor" swaps to the same <see cref="PackageBuilderForm"/> Advanced
    /// mode the returning-user path already uses, sharing the same <see cref="InstallerController"/>.
    /// </summary>
    private static void RunColdStart(string[] args)
    {
        var mostRecent = RecentProjects.Load().FirstOrDefault();
        if (mostRecent is { Path.Length: > 0 } && System.IO.File.Exists(mostRecent.Path))
        {
            var controller = new InstallerController();
            var (ok, _) = controller.Open(mostRecent.Path);
            if (ok) { Application.Run(new PackageBuilderForm(controller, args)); return; }
        }

        var wizard = new BuilderWizardForm(new InstallerController());
        wizard.AdvancedRequested += (_, _) =>
        {
            wizard.Hide();
            wizard.AdvancedForm.Show();
            wizard.AdvancedForm.FormClosed += (_, _) => wizard.Close();
        };
        Application.Run(wizard);
    }
}
