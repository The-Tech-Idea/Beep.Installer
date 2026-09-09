using System.Text;
using System.Text.Json;

namespace Beep.Installer.Engine;

public sealed class EnterpriseCommandLineResult
{
    public string[] Args { get; init; } = Array.Empty<string>();
    public List<ProjectSchemaDiagnostic> Diagnostics { get; init; } = new();
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ProjectSchemaDiagnosticSeverity.Error);
}

public static class EnterpriseCommandLine
{
    private static readonly HashSet<string> KnownFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "/?",
        "/H",
        "/HELP",
        "/VER",
        "/S",
        "/SILENT",
        "/VERYSILENT",
        "/SUPPRESSMSGBOXES",
        "/UNINSTALL",
        "/REPAIR",
        "/SELFTEST",
        "/LANGMGR",
        "/UPDATEUI",
        "/STRICT",
        "/WRITE",
        "/JSON",
        "/FORCE",
        "/NORESTART",
        "/ROLLBACK",
        "/ABANDON",
        "/ALLOWUNSIGNEDLAYOUT",
        "/LAYOUTNORESUME",
        "/REQUIREMIXEDPACKAGES",
        "/REQUIREA11YEVIDENCE",
        "/REQUIREMANAGEDEVIDENCE",
        "/REQUIRENEGATIVESECURITYEVIDENCE",
        "/QUALIFYSIGNING",
        "/MSIBUILD",
        "/MSIVALIDATE",
        "/MSILIFECYCLE",
        "/MSIMATRIX",
        "/MSPLIFECYCLE",
        "/MSTLIFECYCLE",
        "/MSTPRESERVE",
        "/MSPALLOWREMOVAL",
        "/MSPNOREMOVAL",
        "/MSPNOSUPERSEDE",
        "/MSPALLOWEMPTYDELTA",
        "/UPDATECHANNELLIFECYCLE",
        "/REQUIRESIGNED",
        "/NOSIGN",
        "/NOPROMPT",
        "/NO-PROMPT",
        "/DRYRUN",
        "/SBOM",
        "/PROVENANCE",
        "/EVIDENCE",
        "/DOWNLOAD",
        "/SUPPORTBUNDLE",
        "/SUPPORTBUNDLEPREVIEW",
        "/LISTTEMPLATES",
        "--no-prompt",
        "--json",
        "--dry-run",
        "--qualify-signing",
        "--sbom",
        "--provenance",
        "--evidence",
        "--download",
        "--support-bundle",
        "--support-bundle-preview",
        "--list-templates",
        "--msi-build",
        "--msi-validate",
        "--msi-lifecycle",
        "--msi-matrix",
        "--msp-lifecycle",
        "--mst-lifecycle",
        "--mst-preserve",
        "--msp-allow-removal",
        "--msp-no-removal",
        "--msp-no-supersede",
        "--msp-allow-empty-delta",
        "--update-channel-lifecycle",
        "--allow-unsigned-layout",
        "--layout-no-resume",
        "--require-mixed-packages",
        "--require-a11y-evidence",
        "--require-managed-evidence",
        "--require-negative-security-evidence",
        "--plan",
        "--layout",
        "--validate",
        "--build",
        "--canonicalize",
        "--silent",
        "--uninstall",
        "--repair"
    };

    private static readonly string[] KnownPrefixes =
    {
        "--environment",
        "--os",
        "--arch",
        "--channel",
        "--plan-hash",
        "--log-dir",
        "--msi",
        "--mst",
        "--msp",
        "--msp-product",
        "--scenario-pack",
        "--scenario",
        "--property",
        "--msiexec",
        "/BUILD=",
        "/VALIDATE=",
        "/PLAN=",
        "/FORMATREADINESS=",
        "/EXPORTTEMPLATEPACKAGE=",
        "/VERIFYTEMPLATEPACKAGE=",
        "/TEMPLATEPRODUCT=",
        "/TEMPLATEVERSION=",
        "/TEMPLATEPUBLISHER=",
        "/TEMPLATESOURCEDIR=",
        "/TEMPLATEISSUER=",
        "/TEMPLATESIGNKEY=",
        "/TEMPLATETRUSTKEY=",
        "/QUALIFYSDK=",
        "/SDKPROJECT=",
        "/SDKPACKAGEVERSION=",
        "/PUBLISHSDK=",
        "/SDKFEED=",
        "/SDKAPIKEY=",
        "/QUALIFYPLAN=",
        "/PROPERTIES=",
        "/QUALIFYCLI=",
        "/QUALIFYCONFIG=",
        "/EXPORTCATALOG=",
        "/QUALIFYCATALOG=",
        "/QUALIFYCATALOGLAYOUT=",
        "/CATALOGSIGNKEY=",
        "/CATALOGKEYID=",
        "/CATALOGAPPROVEDBY=",
        "/CATALOGAPPROVALREASON=",
        // The self-update verbs. Dispatchable in ProgramVerbs and documented in CLAUDE.md, but
        // never registered here, so both were rejected with BI7005 before they could run -- the
        // whole Phase 11 self-update CLI surface was unreachable.
        "/CHECKUPDATE",
        "/UPDATE",
        "/FEED=",

        // The /PUBLISHFEED verb and its options. These dispatch correctly but were never added
        // here, so the validator rejected them with BI7005 before the verb could run -- which made
        // the whole publish-feed capability unreachable, including the exact
        // `/BUILD=<project> /PUBLISHFEED=<dir>` usage the verb table documents.
        "/PUBLISHFEED=",
        "/CHANNEL=",
        "/FEEDURL=",
        "/MINVERSION=",
        "/REPUBLISH",
        "/UPDATECHANNELFEED=",
        "/VERIFYUPDATECHANNELFEED=",
        "/CHECKUPDATECHANNEL=",
        "/APPLYUPDATECHANNEL=",
        "/QUALIFYUPDATECHANNELFEED=",
        "/DELTA=",
        "/VERIFYDELTA=",
        "/QUALIFYDELTA=",
        "/APPLYDELTA=",
        "/ROLLBACKDELTA=",
        "/RECOVERDELTA=",
        "/DELTABASE=",
        "/DELTATARGET=",
        "/DELTABASEVERSION=",
        "/DELTATARGETVERSION=",
        "/DELTASIGNKEY=",
        "/DELTATRUSTKEY=",
        "/DELTACURRENT=",
        "/DELTASTAGE=",
        "/DELTAJOURNAL=",
        "/DELTACURRENTVERSION=",
        "/UPDATECHANNELFEEDSIGNKEY=",
        "/UPDATECHANNELFEEDTRUSTKEY=",
        "/UPDATECHANNELFEEDISSUER=",
        "/UPDATECHANNELDELTABASEURL=",
        "/UPDATEAUTHORIGIN=",
        "/UPDATEAPPNAME=",
        "/UPDATEAPPID=",
        "/UPDATECACHE=",
        "/UPDATECACHEMAXBYTES=",
        "/UPDATECACHERETENTIONDAYS=",
        "/UPDATESTATE=",
        "/UPDATEPUBLISHER=",
        "/UPDATEBEARERREF=",
        "/UPDATEPROXY=",
        "/UPDATEPROXYUSER=",
        "/UPDATEPROXYPASSWORDREF=",
        "/UPDATECHANNELCURRENT=",
        "/UPDATECHANNELTARGET=",
        "/UPDATECHANNELINSTALLEDVERSION=",
        "/UPDATECHANNELCOHORT=",
        "/UPDATECHANNELSCRIPT=",
        "/UPDATECHANNELUPDATEDSCRIPT=",
        "/UPDATECHANNELDOWNGRADESCRIPT=",
        "/UPDATECHANNELINSTALLDIR=",
        "/UPDATECHANNELINSTALLER=",
        "/LAYOUT=",
        "/VERIFYLAYOUT=",
        "/QUALIFYLAYOUT=",
        "/QUALIFYA11Y=",
        "/A11YEVIDENCE=",
        "/QUALIFYVM=",
        "/QUALIFYRELEASE=",
        "/REQUIREDQUALIFICATIONS=",
        "/QUALIFYUPGRADE=",
        "/QUALIFYRECOVERY=",
        "/QUALIFYSERVICES=",
        "/QUALIFYIIS=",
        "/QUALIFYSYSTEM=",
        "/QUALIFYEVIDENCE=",
        "/QUALIFYDEPLOYMENTKIT=",
        "/QUALIFYVMTARGETS=",
        "/QUALIFYVMSCENARIOS=",
        "/MAXEVIDENCEAGEDAYS=",
        "/MAXFLAKYFAILURES=",
        "/MAXDURATIONSECONDS=",
        "/REQUIREDPACKAGETYPES=",
        "/OFFLINELAYOUT=",
        "/LAYOUTSIGNKEY=",
        "/LAYOUTTRUSTKEY=",
        "/LAYOUTCACHE=",
        "/LAYOUTCACHERETENTIONDAYS=",
        "/LAYOUTPROXY=",
        "/LAYOUTPROXYUSER=",
        "/LAYOUTPROXYPASSWORD=",
        "/LAYOUTBEARERTOKEN=",
        "/LAYOUTHEADERNAME=",
        "/LAYOUTHEADERVALUE=",
        "/DEPLOYMENTKIT=",
        "/MSI=",
        "/WIX=",
        "/MST=",
        "/MSITARGET=",
        "/MSIUPDATED=",
        "/MSTTYPE=",
        "/MSTVALIDATION=",
        "/MSTSUPPRESSERRORS=",
        "/MSTPROFILE=",
        "/MSTPROPERTY=",
        "/MSTLIFECYCLEPACKAGE=",
        "/MSTLIFECYCLETRANSFORM=",
        "/MSTLIFECYCLELOGDIR=",
        "/MSTLIFECYCLEPROPERTIES=",
        "/MSP=",
        "/MSPTARGET=",
        "/MSPUPDATED=",
        "/MSPBASELINE=",
        "/MSPFAMILY=",
        "/MSPVERSION=",
        "/MSPCLASSIFICATION=",
        "/MSPPRODUCT=",
        "/MSPLOGDIR=",
        "/MSPPROPERTIES=",
        "/MSIVALIDATEPACKAGE=",
        "/MSIVALIDATEPDB=",
        "/MSIVALIDATECUB=",
        "/MSIVALIDATEICE=",
        "/MSIVALIDATESUPPRESSICE=",
        "/MSILIFECYCLEPACKAGE=",
        "/MSILIFECYCLELOGDIR=",
        "/MSILIFECYCLEPROPERTIES=",
        "/MSIMATRIXTARGETS=",
        "/MSIMATRIXRUNNER=",
        "/MSIMATRIXLOGDIR=",
        "/MSIMATRIXPROPERTIES=",
        "/MSIMATRIXSCENARIOS=",
        "/MSICUSTOMACTIONS=",
        "/MSIEXEC=",
        "/WINGET=",
        "/EVIDENCE=",
        "/VERIFYEVIDENCE=",
        "/EVIDENCEDIR=",
        "/EVIDENCEREPORT=",
        "/QUALIFYSECURITY=",
        "/QUALIFYDIAGNOSTICS=",
        "/SECURITYSCAN=",
        "/SECURITYREPORT=",
        "/SUPPORTBUNDLE=",
        "/SUPPORTBUNDLERETENTIONDAYS=",
        "/TELEMETRYOUT=",
        "/POLICY=",
        "/PROJECTPOLICY=",
        "/PROFILEPOLICY=",
        "/MACHINEPOLICY=",
        "/RECOVERY=",
        "/CANONICALIZE=",
        "/EXTENSIONEXPORT=",
        "/EXTENSIONTEMPLATE=",
        "/EXTENSIONID=",
        "/EXTENSIONPUBLISHER=",
        "/EXTENSIONVERSION=",
        "/EXTENSIONENGINEVERSION=",
        "/EXTENSIONKIND=",
        "/EXTENSIONRESOURCETYPE=",
        "/EXTENSIONVALIDATORTYPE=",
        "/EXTENSIONEXPORTERFORMAT=",
        "/EXTENSIONPROJECT=",
        "/EXTENSIONCONFORMANCE=",
        "/QUALIFYEXTENSIONSDK=",
        "/SDKENGINEVERSIONS=",
        "/EXTENSIONS=",
        "/PREVIEW=",
        "/PUBLISH=",
        "/SCRIPT=",
        "/D=",
        "/DIR=",
        "/TARGETDIR=",
        "/INSTALLDIR=",
        "/COMPONENTS=",
        "/LOG=",
        "/OUT=",
        "/FORMAT=",
        "/UPDATEURL=",
        "/RESTARTEXITCODE=",
        "/JOURNAL=",
        "/INSTALLER=",
        "/INSTALLERURL=",
        "/SHA256=",
        "/SIGNATURESHA256=",
        "/INSTALLERX86=",
        "/INSTALLERURLX86=",
        "/SHA256X86=",
        "/SIGNATURESHA256X86=",
        "/INSTALLERX64=",
        "/INSTALLERURLX64=",
        "/SHA256X64=",
        "/SIGNATURESHA256X64=",
        "/INSTALLERARM64=",
        "/INSTALLERURLARM64=",
        "/SHA256ARM64=",
        "/SIGNATURESHA256ARM64=",
        "/INSTALLERNEUTRAL=",
        "/INSTALLERURLNEUTRAL=",
        "/SHA256NEUTRAL=",
        "/SIGNATURESHA256NEUTRAL=",
        "/SBOMPATH=",
        "/PROVENANCEPATH=",
        "/SIGNINGEVIDENCE=",
        "/PACKAGEID=",
        "/PACKAGELOCALE=",
        "/LICENSE=",
        "/DESCRIPTION=",
        "/MONIKER=",
        "/SOURCEROOT=",
        "/SOURCEREVISION=",
        "/BUILDTYPE=",
        "/ATTESTKEY=",
        "/ATTESTKEYID=",
        "/ATTESTTRUSTKEY=",
        "/REQUIREATTESTATIONS",
        "/SIGNCERT=",
        "/SIGNPASSWORD=",
        "/SIGNSTORE=",
        "/SIGNSTORELOCATION=",
        "/SIGNTHUMBPRINT=",
        "/SIGNSUBJECT=",
        "/SIGNREMOTEPROVIDER=",
        "/SIGNREMOTEENDPOINT=",
        "/SIGNREMOTEKEY=",
        "/SIGNREMOTECREDENTIAL=",
        "/TIMESTAMP=",
        "/TIMESTAMPOUTAGE=",
        "/TIMESTAMPRETRIES=",
        "/SIGNINGSUBJECT=",
        "--policy=",
        "--project-policy=",
        "--profile-policy=",
        "--machine-policy=",
        "/PROPERTY:",
        "/RESPONSE=",
        "/CONFIG=",
        "/DRYRUN=",
        "--response=",
        "--config=",
        "--script=",
        "--install-dir=",
        "--components=",
        "--log=",
        "--out=",
        "--format=",
        "--build=",
        "--validate=",
        "--plan=",
        "--format-readiness=",
        "--export-template-package=",
        "--verify-template-package=",
        "--template-product=",
        "--template-version=",
        "--template-publisher=",
        "--template-source-dir=",
        "--template-issuer=",
        "--template-sign-key=",
        "--template-trust-key=",
        "--qualify-sdk=",
        "--sdk-project=",
        "--sdk-package-version=",
        "--qualify-plan=",
        "--export-catalog=",
        "--qualify-config=",
        "--qualify-catalog=",
        "--qualify-catalog-layout=",
        "--catalog-sign-key=",
        "--catalog-key-id=",
        "--catalog-approved-by=",
        "--catalog-approval-reason=",
        "--extension-export=",
        "--extension-template=",
        "--extension-id=",
        "--extension-publisher=",
        "--extension-version=",
        "--extension-engine-version=",
        "--extension-kind=",
        "--extension-resource-type=",
        "--extension-validator-type=",
        "--extension-exporter-format=",
        "--extension-project=",
        "--extension-conformance=",
        "--extensions=",
        "--properties=",
        "--qualify-cli=",
        "--qualify-layout=",
        "--qualify-a11y=",
        "--a11y-evidence=",
        "--qualify-vm=",
        "--qualify-vm-targets=",
        "--qualify-vm-scenarios=",
        "--max-evidence-age-days=",
        "--max-flaky-failures=",
        "--max-duration-seconds=",
        "--deployment-kit=",
        "--msi=",
        "--winget=",
        "--evidence=",
        "--qualify-evidence=",
        "--security-scan=",
        "--qualify-security=",
        "--qualify-diagnostics=",
        "--security-report=",
        "--support-bundle=",
        "--support-bundle-retention-days=",
        "--update-url=",
        "--restart-exit-code=",
        "--journal=",
        "--installer=",
        "--installer-url=",
        "--sha256=",
        "--package-id=",
        "--package-locale=",
        "--license=",
        "--description=",
        "--moniker=",
        "--source-root=",
        "--source-revision=",
        "--build-type=",
        "--sign-cert=",
        "--sign-password=",
        "--timestamp=",
        "--signing-subject=",
        "--msi-custom-actions=",
        "--layout-cache=",
        "--layout-cache-retention-days=",
        "--layout-proxy=",
        "--layout-proxy-user=",
        "--layout-proxy-password=",
        "--layout-bearer-token=",
        "--layout-header-name=",
        "--layout-header-value="
    };

    public static EnterpriseCommandLineResult ExpandAndValidate(string[] args)
    {
        var diagnostics = new List<ProjectSchemaDiagnostic>();
        var expanded = Expand(args, diagnostics).ToList();
        NormalizeAliases(expanded);
        diagnostics.AddRange(Validate(expanded));
        return new EnterpriseCommandLineResult
        {
            Args = expanded.ToArray(),
            Diagnostics = diagnostics
        };
    }

    private static IEnumerable<string> Expand(IEnumerable<string> args, List<ProjectSchemaDiagnostic> diagnostics)
    {
        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            if (arg.StartsWith('@') && arg.Length > 1)
            {
                foreach (var nested in LoadResponseFile(arg[1..], diagnostics))
                    yield return nested;
                continue;
            }

            var responsePath = Value(arg, "/RESPONSE=") ?? Value(arg, "--response=") ?? Value(arg, "/CONFIG=") ?? Value(arg, "--config=");
            if (!string.IsNullOrWhiteSpace(responsePath))
            {
                foreach (var nested in LoadResponseFile(responsePath, diagnostics))
                    yield return nested;
                continue;
            }

            yield return arg;
        }
    }

    private static IEnumerable<string> LoadResponseFile(string path, List<ProjectSchemaDiagnostic> diagnostics)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI7001", path, $"Response file not found: {path}"));
                return Array.Empty<string>();
            }

            var text = File.ReadAllText(fullPath, new UTF8Encoding(false, throwOnInvalidBytes: true));
            var trimmed = text.TrimStart();
            return trimmed.StartsWith('{') ? FromJsonResponse(text, fullPath, diagnostics) : FromArgumentResponse(text);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI7002", path, $"Could not read response file '{path}': {ex.Message}"));
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> FromJsonResponse(string text, string path, List<ProjectSchemaDiagnostic> diagnostics)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI7003", path, "Response JSON root must be an object."));
                return Array.Empty<string>();
            }

            var args = new List<string>();
            AddBool(root, "silent", args, "/S");
            AddBool(root, "verySilent", args, "/VERYSILENT");
            AddBool(root, "uninstall", args, "/UNINSTALL");
            AddBool(root, "repair", args, "/REPAIR");
            AddBool(root, "force", args, "/FORCE");
            AddBool(root, "noRestart", args, "/NORESTART");
            AddBool(root, "json", args, "/JSON");
            AddBool(root, "strict", args, "/STRICT");
            AddBool(root, "dryRun", args, "--dry-run");
            AddBool(root, "sbom", args, "/SBOM");
            AddBool(root, "provenance", args, "/PROVENANCE");
            AddBool(root, "evidence", args, "/EVIDENCE");
            AddBool(root, "download", args, "/DOWNLOAD");
            AddBool(root, "layoutNoResume", args, "/LAYOUTNORESUME");
            AddBool(root, "requireA11yEvidence", args, "/REQUIREA11YEVIDENCE");
            AddBool(root, "requireManagedEvidence", args, "/REQUIREMANAGEDEVIDENCE");
            AddBool(root, "requireNegativeSecurityEvidence", args, "/REQUIRENEGATIVESECURITYEVIDENCE");
            AddBool(root, "qualifySigning", args, "/QUALIFYSIGNING");
            AddBool(root, "supportBundle", args, "/SUPPORTBUNDLE");
            AddBool(root, "supportBundlePreview", args, "/SUPPORTBUNDLEPREVIEW");
            AddBool(root, "listTemplates", args, "/LISTTEMPLATES");
            AddBool(root, "msiBuild", args, "/MSIBUILD");
            AddBool(root, "msiValidate", args, "/MSIVALIDATE");
            AddBool(root, "msiLifecycle", args, "/MSILIFECYCLE");
            AddBool(root, "msiMatrix", args, "/MSIMATRIX");
            AddBool(root, "mspLifecycle", args, "/MSPLIFECYCLE");
            AddBool(root, "mstLifecycle", args, "/MSTLIFECYCLE");
            AddBool(root, "mstPreserve", args, "/MSTPRESERVE");
            AddBool(root, "mspAllowRemoval", args, "/MSPALLOWREMOVAL");
            AddBool(root, "mspNoRemoval", args, "/MSPNOREMOVAL");
            AddBool(root, "mspNoSupersede", args, "/MSPNOSUPERSEDE");
            AddBool(root, "mspAllowEmptyDelta", args, "/MSPALLOWEMPTYDELTA");
            AddBool(root, "updateChannelLifecycle", args, "/UPDATECHANNELLIFECYCLE");
            AddValue(root, "script", args, "/SCRIPT=");
            AddValue(root, "installPath", args, "/D=");
            AddValue(root, "targetDir", args, "/D=");
            AddValue(root, "log", args, "/LOG=");
            AddValue(root, "journal", args, "/JOURNAL=");
            AddValue(root, "restartExitCode", args, "/RESTARTEXITCODE=");
            AddValue(root, "build", args, "/BUILD=");
            AddValue(root, "validate", args, "/VALIDATE=");
            AddValue(root, "plan", args, "/PLAN=");
            AddValue(root, "formatReadiness", args, "/FORMATREADINESS=");
            AddValue(root, "exportTemplatePackage", args, "/EXPORTTEMPLATEPACKAGE=");
            AddValue(root, "verifyTemplatePackage", args, "/VERIFYTEMPLATEPACKAGE=");
            AddValue(root, "templateProduct", args, "/TEMPLATEPRODUCT=");
            AddValue(root, "templateVersion", args, "/TEMPLATEVERSION=");
            AddValue(root, "templatePublisher", args, "/TEMPLATEPUBLISHER=");
            AddValue(root, "templateSourceDir", args, "/TEMPLATESOURCEDIR=");
            AddValue(root, "templateIssuer", args, "/TEMPLATEISSUER=");
            AddValue(root, "templateSignKey", args, "/TEMPLATESIGNKEY=");
            AddValue(root, "templateTrustKey", args, "/TEMPLATETRUSTKEY=");
            AddValue(root, "qualifySdk", args, "/QUALIFYSDK=");
            AddValue(root, "sdkProject", args, "/SDKPROJECT=");
            AddValue(root, "sdkPackageVersion", args, "/SDKPACKAGEVERSION=");
            AddValue(root, "publishSdk", args, "/PUBLISHSDK=");
            AddValue(root, "sdkFeed", args, "/SDKFEED=");
            AddValue(root, "sdkApiKey", args, "/SDKAPIKEY=");
            AddValue(root, "qualifyPlan", args, "/QUALIFYPLAN=");
            AddValue(root, "canonicalize", args, "/CANONICALIZE=");
            AddValue(root, "qualifyConfig", args, "/QUALIFYCONFIG=");
            AddValue(root, "extensionExport", args, "/EXTENSIONEXPORT=");
            AddValue(root, "extensionTemplate", args, "/EXTENSIONTEMPLATE=");
            AddValue(root, "extensionId", args, "/EXTENSIONID=");
            AddValue(root, "extensionPublisher", args, "/EXTENSIONPUBLISHER=");
            AddValue(root, "extensionVersion", args, "/EXTENSIONVERSION=");
            AddValue(root, "extensionEngineVersion", args, "/EXTENSIONENGINEVERSION=");
            AddValue(root, "extensionKind", args, "/EXTENSIONKIND=");
            AddValue(root, "extensionResourceType", args, "/EXTENSIONRESOURCETYPE=");
            AddValue(root, "extensionValidatorType", args, "/EXTENSIONVALIDATORTYPE=");
            AddValue(root, "extensionExporterFormat", args, "/EXTENSIONEXPORTERFORMAT=");
            AddValue(root, "extensionProject", args, "/EXTENSIONPROJECT=");
            AddValue(root, "exportCatalog", args, "/EXPORTCATALOG=");
            AddValue(root, "qualifyCatalog", args, "/QUALIFYCATALOG=");
            AddValue(root, "qualifyCatalogLayout", args, "/QUALIFYCATALOGLAYOUT=");
            AddValue(root, "catalogSignKey", args, "/CATALOGSIGNKEY=");
            AddValue(root, "catalogKeyId", args, "/CATALOGKEYID=");
            AddValue(root, "catalogApprovedBy", args, "/CATALOGAPPROVEDBY=");
            AddValue(root, "catalogApprovalReason", args, "/CATALOGAPPROVALREASON=");
            AddValue(root, "updateChannelFeed", args, "/UPDATECHANNELFEED=");
            AddValue(root, "verifyUpdateChannelFeed", args, "/VERIFYUPDATECHANNELFEED=");
            AddValue(root, "checkUpdateChannel", args, "/CHECKUPDATECHANNEL=");
            AddValue(root, "applyUpdateChannel", args, "/APPLYUPDATECHANNEL=");
            AddValue(root, "qualifyUpdateChannelFeed", args, "/QUALIFYUPDATECHANNELFEED=");
            AddValue(root, "updateChannelFeedSignKey", args, "/UPDATECHANNELFEEDSIGNKEY=");
            AddValue(root, "updateChannelFeedTrustKey", args, "/UPDATECHANNELFEEDTRUSTKEY=");
            AddValue(root, "updateChannelFeedIssuer", args, "/UPDATECHANNELFEEDISSUER=");
            AddValue(root, "updateChannelDeltaBaseUrl", args, "/UPDATECHANNELDELTABASEURL=");
            AddValue(root, "updateAuthOrigin", args, "/UPDATEAUTHORIGIN=");
            AddValue(root, "updateAppName", args, "/UPDATEAPPNAME=");
            AddValue(root, "updateAppId", args, "/UPDATEAPPID=");
            AddValue(root, "updateCache", args, "/UPDATECACHE=");
            AddValue(root, "updateCacheMaxBytes", args, "/UPDATECACHEMAXBYTES=");
            AddValue(root, "updateCacheRetentionDays", args, "/UPDATECACHERETENTIONDAYS=");
            AddValue(root, "updateState", args, "/UPDATESTATE=");
            AddValue(root, "updatePublisher", args, "/UPDATEPUBLISHER=");
            AddValue(root, "updateBearerRef", args, "/UPDATEBEARERREF=");
            AddValue(root, "updateProxy", args, "/UPDATEPROXY=");
            AddValue(root, "updateProxyUser", args, "/UPDATEPROXYUSER=");
            AddValue(root, "updateProxyPasswordRef", args, "/UPDATEPROXYPASSWORDREF=");
            AddValue(root, "updateChannelCurrent", args, "/UPDATECHANNELCURRENT=");
            AddValue(root, "updateChannelTarget", args, "/UPDATECHANNELTARGET=");
            AddValue(root, "updateChannelInstalledVersion", args, "/UPDATECHANNELINSTALLEDVERSION=");
            AddValue(root, "updateChannelCohort", args, "/UPDATECHANNELCOHORT=");
            AddValue(root, "delta", args, "/DELTA=");
            AddValue(root, "verifyDelta", args, "/VERIFYDELTA=");
            AddValue(root, "qualifyDelta", args, "/QUALIFYDELTA=");
            AddValue(root, "applyDelta", args, "/APPLYDELTA=");
            AddValue(root, "rollbackDelta", args, "/ROLLBACKDELTA=");
            AddValue(root, "recoverDelta", args, "/RECOVERDELTA=");
            AddValue(root, "deltaBase", args, "/DELTABASE=");
            AddValue(root, "deltaTarget", args, "/DELTATARGET=");
            AddValue(root, "deltaBaseVersion", args, "/DELTABASEVERSION=");
            AddValue(root, "deltaTargetVersion", args, "/DELTATARGETVERSION=");
            AddValue(root, "deltaSignKey", args, "/DELTASIGNKEY=");
            AddValue(root, "deltaTrustKey", args, "/DELTATRUSTKEY=");
            AddValue(root, "deltaCurrent", args, "/DELTACURRENT=");
            AddValue(root, "deltaStage", args, "/DELTASTAGE=");
            AddValue(root, "deltaJournal", args, "/DELTAJOURNAL=");
            AddValue(root, "deltaCurrentVersion", args, "/DELTACURRENTVERSION=");
            AddValue(root, "updateChannelScript", args, "/UPDATECHANNELSCRIPT=");
            AddValue(root, "updateChannelUpdatedScript", args, "/UPDATECHANNELUPDATEDSCRIPT=");
            AddValue(root, "updateChannelDowngradeScript", args, "/UPDATECHANNELDOWNGRADESCRIPT=");
            AddValue(root, "updateChannelInstallDir", args, "/UPDATECHANNELINSTALLDIR=");
            AddValue(root, "updateChannelInstaller", args, "/UPDATECHANNELINSTALLER=");
            AddValue(root, "extensionConformance", args, "/EXTENSIONCONFORMANCE=");
            AddValue(root, "extensions", args, "/EXTENSIONS=");
            AddValue(root, "qualifyExtensionSdk", args, "/QUALIFYEXTENSIONSDK=");
            AddValue(root, "sdkEngineVersions", args, "/SDKENGINEVERSIONS=");
            AddValue(root, "layout", args, "/LAYOUT=");
            AddValue(root, "verifyLayout", args, "/VERIFYLAYOUT=");
            AddValue(root, "qualifyLayout", args, "/QUALIFYLAYOUT=");
            AddValue(root, "qualifyA11y", args, "/QUALIFYA11Y=");
            AddValue(root, "a11yEvidence", args, "/A11YEVIDENCE=");
            AddValue(root, "qualifyVm", args, "/QUALIFYVM=");
            AddValue(root, "qualifyRelease", args, "/QUALIFYRELEASE=");
            AddValue(root, "requiredQualifications", args, "/REQUIREDQUALIFICATIONS=");
            AddValue(root, "qualifyUpgrade", args, "/QUALIFYUPGRADE=");
            AddValue(root, "qualifyRecovery", args, "/QUALIFYRECOVERY=");
            AddValue(root, "qualifyServices", args, "/QUALIFYSERVICES=");
            AddValue(root, "qualifyIis", args, "/QUALIFYIIS=");
            AddValue(root, "qualifySystem", args, "/QUALIFYSYSTEM=");
            AddValue(root, "qualifyEvidence", args, "/QUALIFYEVIDENCE=");
            AddValue(root, "qualifyDeploymentKit", args, "/QUALIFYDEPLOYMENTKIT=");
            AddValue(root, "qualifyVmTargets", args, "/QUALIFYVMTARGETS=");
            AddValue(root, "qualifyVmScenarios", args, "/QUALIFYVMSCENARIOS=");
            AddValue(root, "maxEvidenceAgeDays", args, "/MAXEVIDENCEAGEDAYS=");
            AddValue(root, "maxFlakyFailures", args, "/MAXFLAKYFAILURES=");
            AddValue(root, "maxDurationSeconds", args, "/MAXDURATIONSECONDS=");
            AddValue(root, "offlineLayout", args, "/OFFLINELAYOUT=");
            AddValue(root, "layoutSigningKey", args, "/LAYOUTSIGNKEY=");
            AddValue(root, "layoutTrustKey", args, "/LAYOUTTRUSTKEY=");
            AddValue(root, "layoutCache", args, "/LAYOUTCACHE=");
            AddValue(root, "layoutCacheRetentionDays", args, "/LAYOUTCACHERETENTIONDAYS=");
            AddValue(root, "layoutProxy", args, "/LAYOUTPROXY=");
            AddValue(root, "layoutProxyUser", args, "/LAYOUTPROXYUSER=");
            AddValue(root, "layoutProxyPassword", args, "/LAYOUTPROXYPASSWORD=");
            AddValue(root, "layoutBearerToken", args, "/LAYOUTBEARERTOKEN=");
            AddValue(root, "layoutHeaderName", args, "/LAYOUTHEADERNAME=");
            AddValue(root, "layoutHeaderValue", args, "/LAYOUTHEADERVALUE=");
            AddValue(root, "propertyCatalog", args, "/PROPERTIES=");
            AddValue(root, "propertiesCatalog", args, "/PROPERTIES=");
            AddValue(root, "qualifyCli", args, "/QUALIFYCLI=");
            AddValue(root, "deploymentKit", args, "/DEPLOYMENTKIT=");
            AddValue(root, "msi", args, "/MSI=");
            AddValue(root, "wix", args, "/WIX=");
            AddValue(root, "mst", args, "/MST=");
            AddValue(root, "mstTarget", args, "/MSITARGET=");
            AddValue(root, "mstUpdated", args, "/MSIUPDATED=");
            AddValue(root, "mstType", args, "/MSTTYPE=");
            AddValue(root, "mstValidation", args, "/MSTVALIDATION=");
            AddValue(root, "mstSuppressErrors", args, "/MSTSUPPRESSERRORS=");
            AddValue(root, "mstProfile", args, "/MSTPROFILE=");
            AddValue(root, "mstProperty", args, "/MSTPROPERTY=");
            AddValue(root, "mstLifecyclePackage", args, "/MSTLIFECYCLEPACKAGE=");
            AddValue(root, "mstLifecycleTransform", args, "/MSTLIFECYCLETRANSFORM=");
            AddValue(root, "mstLifecycleLogDir", args, "/MSTLIFECYCLELOGDIR=");
            AddValue(root, "mstLifecycleProperties", args, "/MSTLIFECYCLEPROPERTIES=");
            AddValue(root, "msp", args, "/MSP=");
            AddValue(root, "mspTarget", args, "/MSPTARGET=");
            AddValue(root, "mspUpdated", args, "/MSPUPDATED=");
            AddValue(root, "mspBaseline", args, "/MSPBASELINE=");
            AddValue(root, "mspFamily", args, "/MSPFAMILY=");
            AddValue(root, "mspVersion", args, "/MSPVERSION=");
            AddValue(root, "mspClassification", args, "/MSPCLASSIFICATION=");
            AddValue(root, "mspProduct", args, "/MSPPRODUCT=");
            AddValue(root, "mspLogDir", args, "/MSPLOGDIR=");
            AddValue(root, "mspProperties", args, "/MSPPROPERTIES=");
            AddValue(root, "msiValidatePackage", args, "/MSIVALIDATEPACKAGE=");
            AddValue(root, "msiValidatePdb", args, "/MSIVALIDATEPDB=");
            AddValue(root, "msiValidateCub", args, "/MSIVALIDATECUB=");
            AddValue(root, "msiValidateIce", args, "/MSIVALIDATEICE=");
            AddValue(root, "msiValidateSuppressIce", args, "/MSIVALIDATESUPPRESSICE=");
            AddValue(root, "msiLifecyclePackage", args, "/MSILIFECYCLEPACKAGE=");
            AddValue(root, "msiLifecycleLogDir", args, "/MSILIFECYCLELOGDIR=");
            AddValue(root, "msiLifecycleProperties", args, "/MSILIFECYCLEPROPERTIES=");
            AddValue(root, "msiMatrixTargets", args, "/MSIMATRIXTARGETS=");
            AddValue(root, "msiMatrixRunner", args, "/MSIMATRIXRUNNER=");
            AddValue(root, "msiMatrixLogDir", args, "/MSIMATRIXLOGDIR=");
            AddValue(root, "msiMatrixProperties", args, "/MSIMATRIXPROPERTIES=");
            AddValue(root, "msiMatrixScenarios", args, "/MSIMATRIXSCENARIOS=");
            AddValue(root, "msiCustomActions", args, "/MSICUSTOMACTIONS=");
            AddValue(root, "msiexec", args, "/MSIEXEC=");
            AddValue(root, "winget", args, "/WINGET=");
            AddValue(root, "releaseEvidence", args, "/EVIDENCE=");
            AddValue(root, "verifyEvidence", args, "/VERIFYEVIDENCE=");
            AddValue(root, "evidenceDir", args, "/EVIDENCEDIR=");
            AddValue(root, "evidenceReport", args, "/EVIDENCEREPORT=");
            AddValue(root, "qualifySecurity", args, "/QUALIFYSECURITY=");
            AddValue(root, "qualifyDiagnostics", args, "/QUALIFYDIAGNOSTICS=");
            AddValue(root, "securityScan", args, "/SECURITYSCAN=");
            AddValue(root, "securityReport", args, "/SECURITYREPORT=");
            AddValue(root, "supportBundlePath", args, "/SUPPORTBUNDLE=");
            AddValue(root, "supportBundleRetentionDays", args, "/SUPPORTBUNDLERETENTIONDAYS=");
            AddValue(root, "telemetryOut", args, "/TELEMETRYOUT=");
            AddValue(root, "policy", args, "/POLICY=");
            AddValue(root, "projectPolicy", args, "/PROJECTPOLICY=");
            AddValue(root, "profilePolicy", args, "/PROFILEPOLICY=");
            AddValue(root, "machinePolicy", args, "/MACHINEPOLICY=");
            AddValue(root, "out", args, "/OUT=");
            AddValue(root, "format", args, "/FORMAT=");
            AddValue(root, "installer", args, "/INSTALLER=");
            AddValue(root, "installerUrl", args, "/INSTALLERURL=");
            AddValue(root, "sha256", args, "/SHA256=");
            AddValue(root, "signatureSha256", args, "/SIGNATURESHA256=");
            AddValue(root, "installerX86", args, "/INSTALLERX86=");
            AddValue(root, "installerUrlX86", args, "/INSTALLERURLX86=");
            AddValue(root, "sha256X86", args, "/SHA256X86=");
            AddValue(root, "signatureSha256X86", args, "/SIGNATURESHA256X86=");
            AddValue(root, "installerX64", args, "/INSTALLERX64=");
            AddValue(root, "installerUrlX64", args, "/INSTALLERURLX64=");
            AddValue(root, "sha256X64", args, "/SHA256X64=");
            AddValue(root, "signatureSha256X64", args, "/SIGNATURESHA256X64=");
            AddValue(root, "installerArm64", args, "/INSTALLERARM64=");
            AddValue(root, "installerUrlArm64", args, "/INSTALLERURLARM64=");
            AddValue(root, "sha256Arm64", args, "/SHA256ARM64=");
            AddValue(root, "signatureSha256Arm64", args, "/SIGNATURESHA256ARM64=");
            AddValue(root, "installerNeutral", args, "/INSTALLERNEUTRAL=");
            AddValue(root, "installerUrlNeutral", args, "/INSTALLERURLNEUTRAL=");
            AddValue(root, "sha256Neutral", args, "/SHA256NEUTRAL=");
            AddValue(root, "signatureSha256Neutral", args, "/SIGNATURESHA256NEUTRAL=");
            AddValue(root, "sbomPath", args, "/SBOMPATH=");
            AddValue(root, "provenancePath", args, "/PROVENANCEPATH=");
            AddValue(root, "signingEvidence", args, "/SIGNINGEVIDENCE=");
            AddValue(root, "packageId", args, "/PACKAGEID=");
            AddValue(root, "packageLocale", args, "/PACKAGELOCALE=");
            AddValue(root, "license", args, "/LICENSE=");
            AddValue(root, "description", args, "/DESCRIPTION=");
            AddValue(root, "moniker", args, "/MONIKER=");
            AddValue(root, "sourceRoot", args, "/SOURCEROOT=");
            AddValue(root, "sourceRevision", args, "/SOURCEREVISION=");
            AddValue(root, "buildType", args, "/BUILDTYPE=");
            AddValue(root, "attestKey", args, "/ATTESTKEY=");
            AddValue(root, "attestKeyId", args, "/ATTESTKEYID=");
            AddValue(root, "attestTrustKey", args, "/ATTESTTRUSTKEY=");
            AddBool(root, "requireAttestations", args, "/REQUIREATTESTATIONS");
            AddValue(root, "signCert", args, "/SIGNCERT=");
            AddValue(root, "signPassword", args, "/SIGNPASSWORD=");
            AddValue(root, "signStore", args, "/SIGNSTORE=");
            AddValue(root, "signStoreLocation", args, "/SIGNSTORELOCATION=");
            AddValue(root, "signThumbprint", args, "/SIGNTHUMBPRINT=");
            AddValue(root, "signSubject", args, "/SIGNSUBJECT=");
            AddValue(root, "signRemoteProvider", args, "/SIGNREMOTEPROVIDER=");
            AddValue(root, "signRemoteEndpoint", args, "/SIGNREMOTEENDPOINT=");
            AddValue(root, "signRemoteKey", args, "/SIGNREMOTEKEY=");
            AddValue(root, "signRemoteCredential", args, "/SIGNREMOTECREDENTIAL=");
            AddValue(root, "timestamp", args, "/TIMESTAMP=");
            AddValue(root, "timestampOutage", args, "/TIMESTAMPOUTAGE=");
            AddValue(root, "timestampRetries", args, "/TIMESTAMPRETRIES=");
            AddValue(root, "signingSubject", args, "/SIGNINGSUBJECT=");
            AddComponents(root, args);
            AddProperties(root, args);
            return args;
        }
        catch (JsonException ex)
        {
            diagnostics.Add(new ProjectSchemaDiagnostic(ProjectSchemaDiagnosticSeverity.Error, "BI7004", path, $"Response JSON is invalid: {ex.Message}"));
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> FromArgumentResponse(string text)
    {
        var args = new List<string>();
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
                continue;
            args.AddRange(SplitCommandLine(line));
        }

        return args;
    }

    private static IEnumerable<ProjectSchemaDiagnostic> Validate(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg))
                continue;
            if (!arg.StartsWith('/') && !arg.StartsWith("--"))
                continue;
            if (KnownFlags.Contains(arg))
                continue;
            if (KnownPrefixes.Any(p => arg.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;

            yield return new ProjectSchemaDiagnostic(
                ProjectSchemaDiagnosticSeverity.Error,
                "BI7005",
                arg,
                $"Unknown command-line argument '{arg}'.");
        }
    }

    private static void NormalizeAliases(List<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var value = args[i];
            args[i] = value switch
            {
                "--json" => "/JSON",
                "--dry-run" => "/DRYRUN=true",
                "--sbom" => "/SBOM",
                "--provenance" => "/PROVENANCE",
                "--evidence" => "/EVIDENCE",
                "--download" => "/DOWNLOAD",
                "--support-bundle" => "/SUPPORTBUNDLE",
                "--support-bundle-preview" => "/SUPPORTBUNDLEPREVIEW",
                "--list-templates" => "/LISTTEMPLATES",
                "--msi-build" => "/MSIBUILD",
                "--msi-validate" => "/MSIVALIDATE",
                "--msi-lifecycle" => "/MSILIFECYCLE",
                "--msi-matrix" => "/MSIMATRIX",
                "--msp-lifecycle" => "/MSPLIFECYCLE",
                "--mst-lifecycle" => "/MSTLIFECYCLE",
                "--mst-preserve" => "/MSTPRESERVE",
                "--msp-allow-removal" => "/MSPALLOWREMOVAL",
                "--msp-no-removal" => "/MSPNOREMOVAL",
                "--msp-no-supersede" => "/MSPNOSUPERSEDE",
                "--msp-allow-empty-delta" => "/MSPALLOWEMPTYDELTA",
                "--update-channel-lifecycle" => "/UPDATECHANNELLIFECYCLE",
                "--allow-unsigned-layout" => "/ALLOWUNSIGNEDLAYOUT",
                "--layout-no-resume" => "/LAYOUTNORESUME",
                "--require-mixed-packages" => "/REQUIREMIXEDPACKAGES",
                "--require-a11y-evidence" => "/REQUIREA11YEVIDENCE",
                "--require-managed-evidence" => "/REQUIREMANAGEDEVIDENCE",
                "--require-negative-security-evidence" => "/REQUIRENEGATIVESECURITYEVIDENCE",
                "--no-prompt" => "/NOPROMPT",
                "--silent" => "/S",
                "--uninstall" => "/UNINSTALL",
                "--repair" => "/REPAIR",
                _ when value.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase) => "/D=" + value["/DIR=".Length..],
                _ when value.StartsWith("/TARGETDIR=", StringComparison.OrdinalIgnoreCase) => "/D=" + value["/TARGETDIR=".Length..],
                _ when value.StartsWith("/INSTALLDIR=", StringComparison.OrdinalIgnoreCase) => "/D=" + value["/INSTALLDIR=".Length..],
                _ when value.StartsWith("--script=", StringComparison.OrdinalIgnoreCase) => "/SCRIPT=" + value["--script=".Length..],
                _ when value.StartsWith("--install-dir=", StringComparison.OrdinalIgnoreCase) => "/D=" + value["--install-dir=".Length..],
                _ when value.StartsWith("--components=", StringComparison.OrdinalIgnoreCase) => "/COMPONENTS=" + value["--components=".Length..],
                _ when value.StartsWith("--log=", StringComparison.OrdinalIgnoreCase) => "/LOG=" + value["--log=".Length..],
                _ when value.StartsWith("--journal=", StringComparison.OrdinalIgnoreCase) => "/JOURNAL=" + value["--journal=".Length..],
                _ when value.StartsWith("--out=", StringComparison.OrdinalIgnoreCase) => "/OUT=" + value["--out=".Length..],
                _ when value.StartsWith("--format=", StringComparison.OrdinalIgnoreCase) => "/FORMAT=" + value["--format=".Length..],
                _ when value.StartsWith("--build=", StringComparison.OrdinalIgnoreCase) => "/BUILD=" + value["--build=".Length..],
                _ when value.StartsWith("--validate=", StringComparison.OrdinalIgnoreCase) => "/VALIDATE=" + value["--validate=".Length..],
                _ when value.StartsWith("--plan=", StringComparison.OrdinalIgnoreCase) => "/PLAN=" + value["--plan=".Length..],
                _ when value.StartsWith("--format-readiness=", StringComparison.OrdinalIgnoreCase) => "/FORMATREADINESS=" + value["--format-readiness=".Length..],
                _ when value.StartsWith("--export-template-package=", StringComparison.OrdinalIgnoreCase) => "/EXPORTTEMPLATEPACKAGE=" + value["--export-template-package=".Length..],
                _ when value.StartsWith("--verify-template-package=", StringComparison.OrdinalIgnoreCase) => "/VERIFYTEMPLATEPACKAGE=" + value["--verify-template-package=".Length..],
                _ when value.StartsWith("--template-product=", StringComparison.OrdinalIgnoreCase) => "/TEMPLATEPRODUCT=" + value["--template-product=".Length..],
                _ when value.StartsWith("--template-version=", StringComparison.OrdinalIgnoreCase) => "/TEMPLATEVERSION=" + value["--template-version=".Length..],
                _ when value.StartsWith("--template-publisher=", StringComparison.OrdinalIgnoreCase) => "/TEMPLATEPUBLISHER=" + value["--template-publisher=".Length..],
                _ when value.StartsWith("--template-source-dir=", StringComparison.OrdinalIgnoreCase) => "/TEMPLATESOURCEDIR=" + value["--template-source-dir=".Length..],
                _ when value.StartsWith("--template-issuer=", StringComparison.OrdinalIgnoreCase) => "/TEMPLATEISSUER=" + value["--template-issuer=".Length..],
                _ when value.StartsWith("--template-sign-key=", StringComparison.OrdinalIgnoreCase) => "/TEMPLATESIGNKEY=" + value["--template-sign-key=".Length..],
                _ when value.StartsWith("--template-trust-key=", StringComparison.OrdinalIgnoreCase) => "/TEMPLATETRUSTKEY=" + value["--template-trust-key=".Length..],
                _ when value.StartsWith("--qualify-sdk=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYSDK=" + value["--qualify-sdk=".Length..],
                _ when value.StartsWith("--sdk-project=", StringComparison.OrdinalIgnoreCase) => "/SDKPROJECT=" + value["--sdk-project=".Length..],
                _ when value.StartsWith("--sdk-package-version=", StringComparison.OrdinalIgnoreCase) => "/SDKPACKAGEVERSION=" + value["--sdk-package-version=".Length..],
                _ when value.StartsWith("--publish-sdk=", StringComparison.OrdinalIgnoreCase) => "/PUBLISHSDK=" + value["--publish-sdk=".Length..],
                _ when value.StartsWith("--sdk-feed=", StringComparison.OrdinalIgnoreCase) => "/SDKFEED=" + value["--sdk-feed=".Length..],
                _ when value.StartsWith("--sdk-api-key=", StringComparison.OrdinalIgnoreCase) => "/SDKAPIKEY=" + value["--sdk-api-key=".Length..],
                _ when value.StartsWith("--qualify-plan=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYPLAN=" + value["--qualify-plan=".Length..],
                _ when value.StartsWith("--canonicalize=", StringComparison.OrdinalIgnoreCase) => "/CANONICALIZE=" + value["--canonicalize=".Length..],
                _ when value.StartsWith("--qualify-config=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYCONFIG=" + value["--qualify-config=".Length..],
                _ when value.StartsWith("--extension-export=", StringComparison.OrdinalIgnoreCase) => "/EXTENSIONEXPORT=" + value["--extension-export=".Length..],
                _ when value.StartsWith("--extension-template=", StringComparison.OrdinalIgnoreCase) => "/EXTENSIONTEMPLATE=" + value["--extension-template=".Length..],
                _ when value.StartsWith("--extension-id=", StringComparison.OrdinalIgnoreCase) => "/EXTENSIONID=" + value["--extension-id=".Length..],
                _ when value.StartsWith("--extension-publisher=", StringComparison.OrdinalIgnoreCase) => "/EXTENSIONPUBLISHER=" + value["--extension-publisher=".Length..],
                _ when value.StartsWith("--extension-version=", StringComparison.OrdinalIgnoreCase) => "/EXTENSIONVERSION=" + value["--extension-version=".Length..],
                _ when value.StartsWith("--extension-engine-version=", StringComparison.OrdinalIgnoreCase) => "/EXTENSIONENGINEVERSION=" + value["--extension-engine-version=".Length..],
                _ when value.StartsWith("--extension-kind=", StringComparison.OrdinalIgnoreCase) => "/EXTENSIONKIND=" + value["--extension-kind=".Length..],
                _ when value.StartsWith("--extension-resource-type=", StringComparison.OrdinalIgnoreCase) => "/EXTENSIONRESOURCETYPE=" + value["--extension-resource-type=".Length..],
                _ when value.StartsWith("--extension-validator-type=", StringComparison.OrdinalIgnoreCase) => "/EXTENSIONVALIDATORTYPE=" + value["--extension-validator-type=".Length..],
                _ when value.StartsWith("--extension-exporter-format=", StringComparison.OrdinalIgnoreCase) => "/EXTENSIONEXPORTERFORMAT=" + value["--extension-exporter-format=".Length..],
                _ when value.StartsWith("--extension-project=", StringComparison.OrdinalIgnoreCase) => "/EXTENSIONPROJECT=" + value["--extension-project=".Length..],
                _ when value.StartsWith("--export-catalog=", StringComparison.OrdinalIgnoreCase) => "/EXPORTCATALOG=" + value["--export-catalog=".Length..],
                _ when value.StartsWith("--qualify-catalog=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYCATALOG=" + value["--qualify-catalog=".Length..],
                _ when value.StartsWith("--qualify-catalog-layout=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYCATALOGLAYOUT=" + value["--qualify-catalog-layout=".Length..],
                _ when value.StartsWith("--catalog-sign-key=", StringComparison.OrdinalIgnoreCase) => "/CATALOGSIGNKEY=" + value["--catalog-sign-key=".Length..],
                _ when value.StartsWith("--catalog-key-id=", StringComparison.OrdinalIgnoreCase) => "/CATALOGKEYID=" + value["--catalog-key-id=".Length..],
                _ when value.StartsWith("--catalog-approved-by=", StringComparison.OrdinalIgnoreCase) => "/CATALOGAPPROVEDBY=" + value["--catalog-approved-by=".Length..],
                _ when value.StartsWith("--catalog-approval-reason=", StringComparison.OrdinalIgnoreCase) => "/CATALOGAPPROVALREASON=" + value["--catalog-approval-reason=".Length..],
                _ when value.StartsWith("--update-channel-feed=", StringComparison.OrdinalIgnoreCase) => "/UPDATECHANNELFEED=" + value["--update-channel-feed=".Length..],
                _ when value.StartsWith("--verify-update-channel-feed=", StringComparison.OrdinalIgnoreCase) => "/VERIFYUPDATECHANNELFEED=" + value["--verify-update-channel-feed=".Length..],
                _ when value.StartsWith("--check-update-channel=", StringComparison.OrdinalIgnoreCase) => "/CHECKUPDATECHANNEL=" + value["--check-update-channel=".Length..],
                _ when value.StartsWith("--apply-update-channel=", StringComparison.OrdinalIgnoreCase) => "/APPLYUPDATECHANNEL=" + value["--apply-update-channel=".Length..],
                _ when value.StartsWith("--qualify-update-channel-feed=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYUPDATECHANNELFEED=" + value["--qualify-update-channel-feed=".Length..],
                _ when value.StartsWith("--update-channel-feed-sign-key=", StringComparison.OrdinalIgnoreCase) => "/UPDATECHANNELFEEDSIGNKEY=" + value["--update-channel-feed-sign-key=".Length..],
                _ when value.StartsWith("--update-channel-feed-trust-key=", StringComparison.OrdinalIgnoreCase) => "/UPDATECHANNELFEEDTRUSTKEY=" + value["--update-channel-feed-trust-key=".Length..],
                _ when value.StartsWith("--update-channel-feed-issuer=", StringComparison.OrdinalIgnoreCase) => "/UPDATECHANNELFEEDISSUER=" + value["--update-channel-feed-issuer=".Length..],
                _ when value.StartsWith("--update-channel-delta-base-url=", StringComparison.OrdinalIgnoreCase) => "/UPDATECHANNELDELTABASEURL=" + value["--update-channel-delta-base-url=".Length..],
                _ when value.StartsWith("--update-auth-origin=", StringComparison.OrdinalIgnoreCase) => "/UPDATEAUTHORIGIN=" + value["--update-auth-origin=".Length..],
                _ when value.StartsWith("--update-app-name=", StringComparison.OrdinalIgnoreCase) => "/UPDATEAPPNAME=" + value["--update-app-name=".Length..],
                _ when value.StartsWith("--update-app-id=", StringComparison.OrdinalIgnoreCase) => "/UPDATEAPPID=" + value["--update-app-id=".Length..],
                _ when value.StartsWith("--update-cache=", StringComparison.OrdinalIgnoreCase) => "/UPDATECACHE=" + value["--update-cache=".Length..],
                _ when value.StartsWith("--update-cache-max-bytes=", StringComparison.OrdinalIgnoreCase) => "/UPDATECACHEMAXBYTES=" + value["--update-cache-max-bytes=".Length..],
                _ when value.StartsWith("--update-cache-retention-days=", StringComparison.OrdinalIgnoreCase) => "/UPDATECACHERETENTIONDAYS=" + value["--update-cache-retention-days=".Length..],
                _ when value.StartsWith("--update-state=", StringComparison.OrdinalIgnoreCase) => "/UPDATESTATE=" + value["--update-state=".Length..],
                _ when value.StartsWith("--update-publisher=", StringComparison.OrdinalIgnoreCase) => "/UPDATEPUBLISHER=" + value["--update-publisher=".Length..],
                _ when value.StartsWith("--update-bearer-ref=", StringComparison.OrdinalIgnoreCase) => "/UPDATEBEARERREF=" + value["--update-bearer-ref=".Length..],
                _ when value.StartsWith("--update-proxy=", StringComparison.OrdinalIgnoreCase) => "/UPDATEPROXY=" + value["--update-proxy=".Length..],
                _ when value.StartsWith("--update-proxy-user=", StringComparison.OrdinalIgnoreCase) => "/UPDATEPROXYUSER=" + value["--update-proxy-user=".Length..],
                _ when value.StartsWith("--update-proxy-password-ref=", StringComparison.OrdinalIgnoreCase) => "/UPDATEPROXYPASSWORDREF=" + value["--update-proxy-password-ref=".Length..],
                _ when value.StartsWith("--update-channel-current=", StringComparison.OrdinalIgnoreCase) => "/UPDATECHANNELCURRENT=" + value["--update-channel-current=".Length..],
                _ when value.StartsWith("--update-channel-target=", StringComparison.OrdinalIgnoreCase) => "/UPDATECHANNELTARGET=" + value["--update-channel-target=".Length..],
                _ when value.StartsWith("--update-channel-installed-version=", StringComparison.OrdinalIgnoreCase) => "/UPDATECHANNELINSTALLEDVERSION=" + value["--update-channel-installed-version=".Length..],
                _ when value.StartsWith("--update-channel-cohort=", StringComparison.OrdinalIgnoreCase) => "/UPDATECHANNELCOHORT=" + value["--update-channel-cohort=".Length..],
                _ when value.StartsWith("--delta=", StringComparison.OrdinalIgnoreCase) => "/DELTA=" + value["--delta=".Length..],
                _ when value.StartsWith("--verify-delta=", StringComparison.OrdinalIgnoreCase) => "/VERIFYDELTA=" + value["--verify-delta=".Length..],
                _ when value.StartsWith("--qualify-delta=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYDELTA=" + value["--qualify-delta=".Length..],
                _ when value.StartsWith("--apply-delta=", StringComparison.OrdinalIgnoreCase) => "/APPLYDELTA=" + value["--apply-delta=".Length..],
                _ when value.StartsWith("--rollback-delta=", StringComparison.OrdinalIgnoreCase) => "/ROLLBACKDELTA=" + value["--rollback-delta=".Length..],
                _ when value.StartsWith("--recover-delta=", StringComparison.OrdinalIgnoreCase) => "/RECOVERDELTA=" + value["--recover-delta=".Length..],
                _ when value.StartsWith("--delta-base=", StringComparison.OrdinalIgnoreCase) => "/DELTABASE=" + value["--delta-base=".Length..],
                _ when value.StartsWith("--delta-target=", StringComparison.OrdinalIgnoreCase) => "/DELTATARGET=" + value["--delta-target=".Length..],
                _ when value.StartsWith("--delta-base-version=", StringComparison.OrdinalIgnoreCase) => "/DELTABASEVERSION=" + value["--delta-base-version=".Length..],
                _ when value.StartsWith("--delta-target-version=", StringComparison.OrdinalIgnoreCase) => "/DELTATARGETVERSION=" + value["--delta-target-version=".Length..],
                _ when value.StartsWith("--delta-sign-key=", StringComparison.OrdinalIgnoreCase) => "/DELTASIGNKEY=" + value["--delta-sign-key=".Length..],
                _ when value.StartsWith("--delta-trust-key=", StringComparison.OrdinalIgnoreCase) => "/DELTATRUSTKEY=" + value["--delta-trust-key=".Length..],
                _ when value.StartsWith("--delta-current=", StringComparison.OrdinalIgnoreCase) => "/DELTACURRENT=" + value["--delta-current=".Length..],
                _ when value.StartsWith("--delta-stage=", StringComparison.OrdinalIgnoreCase) => "/DELTASTAGE=" + value["--delta-stage=".Length..],
                _ when value.StartsWith("--delta-journal=", StringComparison.OrdinalIgnoreCase) => "/DELTAJOURNAL=" + value["--delta-journal=".Length..],
                _ when value.StartsWith("--delta-current-version=", StringComparison.OrdinalIgnoreCase) => "/DELTACURRENTVERSION=" + value["--delta-current-version=".Length..],
                _ when value.StartsWith("--update-channel-script=", StringComparison.OrdinalIgnoreCase) => "/UPDATECHANNELSCRIPT=" + value["--update-channel-script=".Length..],
                _ when value.StartsWith("--update-channel-updated-script=", StringComparison.OrdinalIgnoreCase) => "/UPDATECHANNELUPDATEDSCRIPT=" + value["--update-channel-updated-script=".Length..],
                _ when value.StartsWith("--update-channel-downgrade-script=", StringComparison.OrdinalIgnoreCase) => "/UPDATECHANNELDOWNGRADESCRIPT=" + value["--update-channel-downgrade-script=".Length..],
                _ when value.StartsWith("--update-channel-install-dir=", StringComparison.OrdinalIgnoreCase) => "/UPDATECHANNELINSTALLDIR=" + value["--update-channel-install-dir=".Length..],
                _ when value.StartsWith("--update-channel-installer=", StringComparison.OrdinalIgnoreCase) => "/UPDATECHANNELINSTALLER=" + value["--update-channel-installer=".Length..],
                _ when value.StartsWith("--extension-conformance=", StringComparison.OrdinalIgnoreCase) => "/EXTENSIONCONFORMANCE=" + value["--extension-conformance=".Length..],
                _ when value.StartsWith("--extensions=", StringComparison.OrdinalIgnoreCase) => "/EXTENSIONS=" + value["--extensions=".Length..],
                _ when value.StartsWith("--qualify-extension-sdk=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYEXTENSIONSDK=" + value["--qualify-extension-sdk=".Length..],
                _ when value.StartsWith("--sdk-engine-versions=", StringComparison.OrdinalIgnoreCase) => "/SDKENGINEVERSIONS=" + value["--sdk-engine-versions=".Length..],
                _ when value.StartsWith("--layout=", StringComparison.OrdinalIgnoreCase) => "/LAYOUT=" + value["--layout=".Length..],
                _ when value.StartsWith("--verify-layout=", StringComparison.OrdinalIgnoreCase) => "/VERIFYLAYOUT=" + value["--verify-layout=".Length..],
                _ when value.StartsWith("--qualify-layout=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYLAYOUT=" + value["--qualify-layout=".Length..],
                _ when value.StartsWith("--qualify-a11y=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYA11Y=" + value["--qualify-a11y=".Length..],
                _ when value.StartsWith("--a11y-evidence=", StringComparison.OrdinalIgnoreCase) => "/A11YEVIDENCE=" + value["--a11y-evidence=".Length..],
                _ when value.StartsWith("--qualify-vm=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYVM=" + value["--qualify-vm=".Length..],
                _ when value.StartsWith("--qualify-release=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYRELEASE=" + value["--qualify-release=".Length..],
                _ when value.StartsWith("--required-qualifications=", StringComparison.OrdinalIgnoreCase) => "/REQUIREDQUALIFICATIONS=" + value["--required-qualifications=".Length..],
                _ when value.Equals("--qualify-signing", StringComparison.OrdinalIgnoreCase) => "/QUALIFYSIGNING",
                _ when value.StartsWith("--qualify-upgrade=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYUPGRADE=" + value["--qualify-upgrade=".Length..],
                _ when value.StartsWith("--qualify-recovery=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYRECOVERY=" + value["--qualify-recovery=".Length..],
                _ when value.StartsWith("--qualify-services=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYSERVICES=" + value["--qualify-services=".Length..],
                _ when value.StartsWith("--qualify-iis=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYIIS=" + value["--qualify-iis=".Length..],
                _ when value.StartsWith("--qualify-system=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYSYSTEM=" + value["--qualify-system=".Length..],
                _ when value.StartsWith("--qualify-deployment-kit=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYDEPLOYMENTKIT=" + value["--qualify-deployment-kit=".Length..],
                _ when value.StartsWith("--qualify-vm-targets=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYVMTARGETS=" + value["--qualify-vm-targets=".Length..],
                _ when value.StartsWith("--qualify-vm-scenarios=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYVMSCENARIOS=" + value["--qualify-vm-scenarios=".Length..],
                _ when value.StartsWith("--max-evidence-age-days=", StringComparison.OrdinalIgnoreCase) => "/MAXEVIDENCEAGEDAYS=" + value["--max-evidence-age-days=".Length..],
                _ when value.StartsWith("--max-flaky-failures=", StringComparison.OrdinalIgnoreCase) => "/MAXFLAKYFAILURES=" + value["--max-flaky-failures=".Length..],
                _ when value.StartsWith("--max-duration-seconds=", StringComparison.OrdinalIgnoreCase) => "/MAXDURATIONSECONDS=" + value["--max-duration-seconds=".Length..],
                _ when value.StartsWith("--required-package-types=", StringComparison.OrdinalIgnoreCase) => "/REQUIREDPACKAGETYPES=" + value["--required-package-types=".Length..],
                _ when value.StartsWith("--offline-layout=", StringComparison.OrdinalIgnoreCase) => "/OFFLINELAYOUT=" + value["--offline-layout=".Length..],
                _ when value.StartsWith("--layout-signing-key=", StringComparison.OrdinalIgnoreCase) => "/LAYOUTSIGNKEY=" + value["--layout-signing-key=".Length..],
                _ when value.StartsWith("--layout-trust-key=", StringComparison.OrdinalIgnoreCase) => "/LAYOUTTRUSTKEY=" + value["--layout-trust-key=".Length..],
                _ when value.StartsWith("--layout-cache=", StringComparison.OrdinalIgnoreCase) => "/LAYOUTCACHE=" + value["--layout-cache=".Length..],
                _ when value.StartsWith("--layout-cache-retention-days=", StringComparison.OrdinalIgnoreCase) => "/LAYOUTCACHERETENTIONDAYS=" + value["--layout-cache-retention-days=".Length..],
                _ when value.StartsWith("--layout-proxy=", StringComparison.OrdinalIgnoreCase) => "/LAYOUTPROXY=" + value["--layout-proxy=".Length..],
                _ when value.StartsWith("--layout-proxy-user=", StringComparison.OrdinalIgnoreCase) => "/LAYOUTPROXYUSER=" + value["--layout-proxy-user=".Length..],
                _ when value.StartsWith("--layout-proxy-password=", StringComparison.OrdinalIgnoreCase) => "/LAYOUTPROXYPASSWORD=" + value["--layout-proxy-password=".Length..],
                _ when value.StartsWith("--layout-bearer-token=", StringComparison.OrdinalIgnoreCase) => "/LAYOUTBEARERTOKEN=" + value["--layout-bearer-token=".Length..],
                _ when value.StartsWith("--layout-header-name=", StringComparison.OrdinalIgnoreCase) => "/LAYOUTHEADERNAME=" + value["--layout-header-name=".Length..],
                _ when value.StartsWith("--layout-header-value=", StringComparison.OrdinalIgnoreCase) => "/LAYOUTHEADERVALUE=" + value["--layout-header-value=".Length..],
                _ when value.StartsWith("--properties=", StringComparison.OrdinalIgnoreCase) => "/PROPERTIES=" + value["--properties=".Length..],
                _ when value.StartsWith("--qualify-cli=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYCLI=" + value["--qualify-cli=".Length..],
                _ when value.StartsWith("--deployment-kit=", StringComparison.OrdinalIgnoreCase) => "/DEPLOYMENTKIT=" + value["--deployment-kit=".Length..],
                _ when value.StartsWith("--msi=", StringComparison.OrdinalIgnoreCase) => "/MSI=" + value["--msi=".Length..],
                _ when value.StartsWith("--wix=", StringComparison.OrdinalIgnoreCase) => "/WIX=" + value["--wix=".Length..],
                _ when value.StartsWith("--mst=", StringComparison.OrdinalIgnoreCase) => "/MST=" + value["--mst=".Length..],
                _ when value.StartsWith("--mst-target=", StringComparison.OrdinalIgnoreCase) => "/MSITARGET=" + value["--mst-target=".Length..],
                _ when value.StartsWith("--mst-updated=", StringComparison.OrdinalIgnoreCase) => "/MSIUPDATED=" + value["--mst-updated=".Length..],
                _ when value.StartsWith("--mst-type=", StringComparison.OrdinalIgnoreCase) => "/MSTTYPE=" + value["--mst-type=".Length..],
                _ when value.StartsWith("--mst-validation=", StringComparison.OrdinalIgnoreCase) => "/MSTVALIDATION=" + value["--mst-validation=".Length..],
                _ when value.StartsWith("--mst-suppress-errors=", StringComparison.OrdinalIgnoreCase) => "/MSTSUPPRESSERRORS=" + value["--mst-suppress-errors=".Length..],
                _ when value.StartsWith("--mst-profile=", StringComparison.OrdinalIgnoreCase) => "/MSTPROFILE=" + value["--mst-profile=".Length..],
                _ when value.StartsWith("--mst-property=", StringComparison.OrdinalIgnoreCase) => "/MSTPROPERTY=" + value["--mst-property=".Length..],
                _ when value.StartsWith("--mst-lifecycle-package=", StringComparison.OrdinalIgnoreCase) => "/MSTLIFECYCLEPACKAGE=" + value["--mst-lifecycle-package=".Length..],
                _ when value.StartsWith("--mst-lifecycle-transform=", StringComparison.OrdinalIgnoreCase) => "/MSTLIFECYCLETRANSFORM=" + value["--mst-lifecycle-transform=".Length..],
                _ when value.StartsWith("--mst-lifecycle-log-dir=", StringComparison.OrdinalIgnoreCase) => "/MSTLIFECYCLELOGDIR=" + value["--mst-lifecycle-log-dir=".Length..],
                _ when value.StartsWith("--mst-lifecycle-properties=", StringComparison.OrdinalIgnoreCase) => "/MSTLIFECYCLEPROPERTIES=" + value["--mst-lifecycle-properties=".Length..],
                _ when value.StartsWith("--msp=", StringComparison.OrdinalIgnoreCase) => "/MSP=" + value["--msp=".Length..],
                _ when value.StartsWith("--msp-target=", StringComparison.OrdinalIgnoreCase) => "/MSPTARGET=" + value["--msp-target=".Length..],
                _ when value.StartsWith("--msp-updated=", StringComparison.OrdinalIgnoreCase) => "/MSPUPDATED=" + value["--msp-updated=".Length..],
                _ when value.StartsWith("--msp-baseline=", StringComparison.OrdinalIgnoreCase) => "/MSPBASELINE=" + value["--msp-baseline=".Length..],
                _ when value.StartsWith("--msp-family=", StringComparison.OrdinalIgnoreCase) => "/MSPFAMILY=" + value["--msp-family=".Length..],
                _ when value.StartsWith("--msp-version=", StringComparison.OrdinalIgnoreCase) => "/MSPVERSION=" + value["--msp-version=".Length..],
                _ when value.StartsWith("--msp-classification=", StringComparison.OrdinalIgnoreCase) => "/MSPCLASSIFICATION=" + value["--msp-classification=".Length..],
                _ when value.StartsWith("--msp-product=", StringComparison.OrdinalIgnoreCase) => "/MSPPRODUCT=" + value["--msp-product=".Length..],
                _ when value.StartsWith("--msp-log-dir=", StringComparison.OrdinalIgnoreCase) => "/MSPLOGDIR=" + value["--msp-log-dir=".Length..],
                _ when value.StartsWith("--msp-properties=", StringComparison.OrdinalIgnoreCase) => "/MSPPROPERTIES=" + value["--msp-properties=".Length..],
                _ when value.StartsWith("--msi-validate-package=", StringComparison.OrdinalIgnoreCase) => "/MSIVALIDATEPACKAGE=" + value["--msi-validate-package=".Length..],
                _ when value.StartsWith("--msi-validate-pdb=", StringComparison.OrdinalIgnoreCase) => "/MSIVALIDATEPDB=" + value["--msi-validate-pdb=".Length..],
                _ when value.StartsWith("--msi-validate-cub=", StringComparison.OrdinalIgnoreCase) => "/MSIVALIDATECUB=" + value["--msi-validate-cub=".Length..],
                _ when value.StartsWith("--msi-validate-ice=", StringComparison.OrdinalIgnoreCase) => "/MSIVALIDATEICE=" + value["--msi-validate-ice=".Length..],
                _ when value.StartsWith("--msi-validate-suppress-ice=", StringComparison.OrdinalIgnoreCase) => "/MSIVALIDATESUPPRESSICE=" + value["--msi-validate-suppress-ice=".Length..],
                _ when value.StartsWith("--msi-lifecycle-package=", StringComparison.OrdinalIgnoreCase) => "/MSILIFECYCLEPACKAGE=" + value["--msi-lifecycle-package=".Length..],
                _ when value.StartsWith("--msi-lifecycle-log-dir=", StringComparison.OrdinalIgnoreCase) => "/MSILIFECYCLELOGDIR=" + value["--msi-lifecycle-log-dir=".Length..],
                _ when value.StartsWith("--msi-lifecycle-properties=", StringComparison.OrdinalIgnoreCase) => "/MSILIFECYCLEPROPERTIES=" + value["--msi-lifecycle-properties=".Length..],
                _ when value.StartsWith("--msi-matrix-targets=", StringComparison.OrdinalIgnoreCase) => "/MSIMATRIXTARGETS=" + value["--msi-matrix-targets=".Length..],
                _ when value.StartsWith("--msi-matrix-runner=", StringComparison.OrdinalIgnoreCase) => "/MSIMATRIXRUNNER=" + value["--msi-matrix-runner=".Length..],
                _ when value.StartsWith("--msi-matrix-log-dir=", StringComparison.OrdinalIgnoreCase) => "/MSIMATRIXLOGDIR=" + value["--msi-matrix-log-dir=".Length..],
                _ when value.StartsWith("--msi-matrix-properties=", StringComparison.OrdinalIgnoreCase) => "/MSIMATRIXPROPERTIES=" + value["--msi-matrix-properties=".Length..],
                _ when value.StartsWith("--msi-matrix-scenarios=", StringComparison.OrdinalIgnoreCase) => "/MSIMATRIXSCENARIOS=" + value["--msi-matrix-scenarios=".Length..],
                _ when value.StartsWith("--msi-custom-actions=", StringComparison.OrdinalIgnoreCase) => "/MSICUSTOMACTIONS=" + value["--msi-custom-actions=".Length..],
                _ when value.StartsWith("--msiexec=", StringComparison.OrdinalIgnoreCase) => "/MSIEXEC=" + value["--msiexec=".Length..],
                _ when value.StartsWith("--winget=", StringComparison.OrdinalIgnoreCase) => "/WINGET=" + value["--winget=".Length..],
                _ when value.StartsWith("--evidence=", StringComparison.OrdinalIgnoreCase) => "/EVIDENCE=" + value["--evidence=".Length..],
                _ when value.StartsWith("--qualify-evidence=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYEVIDENCE=" + value["--qualify-evidence=".Length..],
                _ when value.StartsWith("--verify-evidence=", StringComparison.OrdinalIgnoreCase) => "/VERIFYEVIDENCE=" + value["--verify-evidence=".Length..],
                _ when value.StartsWith("--evidence-dir=", StringComparison.OrdinalIgnoreCase) => "/EVIDENCEDIR=" + value["--evidence-dir=".Length..],
                _ when value.StartsWith("--evidence-report=", StringComparison.OrdinalIgnoreCase) => "/EVIDENCEREPORT=" + value["--evidence-report=".Length..],
                _ when value.StartsWith("--qualify-security=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYSECURITY=" + value["--qualify-security=".Length..],
                _ when value.StartsWith("--qualify-diagnostics=", StringComparison.OrdinalIgnoreCase) => "/QUALIFYDIAGNOSTICS=" + value["--qualify-diagnostics=".Length..],
                _ when value.StartsWith("--security-scan=", StringComparison.OrdinalIgnoreCase) => "/SECURITYSCAN=" + value["--security-scan=".Length..],
                _ when value.StartsWith("--security-report=", StringComparison.OrdinalIgnoreCase) => "/SECURITYREPORT=" + value["--security-report=".Length..],
                _ when value.StartsWith("--support-bundle=", StringComparison.OrdinalIgnoreCase) => "/SUPPORTBUNDLE=" + value["--support-bundle=".Length..],
                _ when value.StartsWith("--support-bundle-retention-days=", StringComparison.OrdinalIgnoreCase) => "/SUPPORTBUNDLERETENTIONDAYS=" + value["--support-bundle-retention-days=".Length..],
                _ when value.StartsWith("--telemetry-out=", StringComparison.OrdinalIgnoreCase) => "/TELEMETRYOUT=" + value["--telemetry-out=".Length..],
                _ when value.StartsWith("--policy=", StringComparison.OrdinalIgnoreCase) => "/POLICY=" + value["--policy=".Length..],
                _ when value.StartsWith("--project-policy=", StringComparison.OrdinalIgnoreCase) => "/PROJECTPOLICY=" + value["--project-policy=".Length..],
                _ when value.StartsWith("--profile-policy=", StringComparison.OrdinalIgnoreCase) => "/PROFILEPOLICY=" + value["--profile-policy=".Length..],
                _ when value.StartsWith("--machine-policy=", StringComparison.OrdinalIgnoreCase) => "/MACHINEPOLICY=" + value["--machine-policy=".Length..],
                _ when value.StartsWith("--update-url=", StringComparison.OrdinalIgnoreCase) => "/UPDATEURL=" + value["--update-url=".Length..],
                _ when value.StartsWith("--restart-exit-code=", StringComparison.OrdinalIgnoreCase) => "/RESTARTEXITCODE=" + value["--restart-exit-code=".Length..],
                _ when value.StartsWith("--installer=", StringComparison.OrdinalIgnoreCase) => "/INSTALLER=" + value["--installer=".Length..],
                _ when value.StartsWith("--installer-url=", StringComparison.OrdinalIgnoreCase) => "/INSTALLERURL=" + value["--installer-url=".Length..],
                _ when value.StartsWith("--sha256=", StringComparison.OrdinalIgnoreCase) => "/SHA256=" + value["--sha256=".Length..],
                _ when value.StartsWith("--signature-sha256=", StringComparison.OrdinalIgnoreCase) => "/SIGNATURESHA256=" + value["--signature-sha256=".Length..],
                _ when value.StartsWith("--installer-x86=", StringComparison.OrdinalIgnoreCase) => "/INSTALLERX86=" + value["--installer-x86=".Length..],
                _ when value.StartsWith("--installer-url-x86=", StringComparison.OrdinalIgnoreCase) => "/INSTALLERURLX86=" + value["--installer-url-x86=".Length..],
                _ when value.StartsWith("--sha256-x86=", StringComparison.OrdinalIgnoreCase) => "/SHA256X86=" + value["--sha256-x86=".Length..],
                _ when value.StartsWith("--signature-sha256-x86=", StringComparison.OrdinalIgnoreCase) => "/SIGNATURESHA256X86=" + value["--signature-sha256-x86=".Length..],
                _ when value.StartsWith("--installer-x64=", StringComparison.OrdinalIgnoreCase) => "/INSTALLERX64=" + value["--installer-x64=".Length..],
                _ when value.StartsWith("--installer-url-x64=", StringComparison.OrdinalIgnoreCase) => "/INSTALLERURLX64=" + value["--installer-url-x64=".Length..],
                _ when value.StartsWith("--sha256-x64=", StringComparison.OrdinalIgnoreCase) => "/SHA256X64=" + value["--sha256-x64=".Length..],
                _ when value.StartsWith("--signature-sha256-x64=", StringComparison.OrdinalIgnoreCase) => "/SIGNATURESHA256X64=" + value["--signature-sha256-x64=".Length..],
                _ when value.StartsWith("--installer-arm64=", StringComparison.OrdinalIgnoreCase) => "/INSTALLERARM64=" + value["--installer-arm64=".Length..],
                _ when value.StartsWith("--installer-url-arm64=", StringComparison.OrdinalIgnoreCase) => "/INSTALLERURLARM64=" + value["--installer-url-arm64=".Length..],
                _ when value.StartsWith("--sha256-arm64=", StringComparison.OrdinalIgnoreCase) => "/SHA256ARM64=" + value["--sha256-arm64=".Length..],
                _ when value.StartsWith("--signature-sha256-arm64=", StringComparison.OrdinalIgnoreCase) => "/SIGNATURESHA256ARM64=" + value["--signature-sha256-arm64=".Length..],
                _ when value.StartsWith("--installer-neutral=", StringComparison.OrdinalIgnoreCase) => "/INSTALLERNEUTRAL=" + value["--installer-neutral=".Length..],
                _ when value.StartsWith("--installer-url-neutral=", StringComparison.OrdinalIgnoreCase) => "/INSTALLERURLNEUTRAL=" + value["--installer-url-neutral=".Length..],
                _ when value.StartsWith("--sha256-neutral=", StringComparison.OrdinalIgnoreCase) => "/SHA256NEUTRAL=" + value["--sha256-neutral=".Length..],
                _ when value.StartsWith("--signature-sha256-neutral=", StringComparison.OrdinalIgnoreCase) => "/SIGNATURESHA256NEUTRAL=" + value["--signature-sha256-neutral=".Length..],
                _ when value.StartsWith("--sbom-path=", StringComparison.OrdinalIgnoreCase) => "/SBOMPATH=" + value["--sbom-path=".Length..],
                _ when value.StartsWith("--provenance-path=", StringComparison.OrdinalIgnoreCase) => "/PROVENANCEPATH=" + value["--provenance-path=".Length..],
                _ when value.StartsWith("--signing-evidence=", StringComparison.OrdinalIgnoreCase) => "/SIGNINGEVIDENCE=" + value["--signing-evidence=".Length..],
                _ when value.StartsWith("--package-id=", StringComparison.OrdinalIgnoreCase) => "/PACKAGEID=" + value["--package-id=".Length..],
                _ when value.StartsWith("--package-locale=", StringComparison.OrdinalIgnoreCase) => "/PACKAGELOCALE=" + value["--package-locale=".Length..],
                _ when value.StartsWith("--license=", StringComparison.OrdinalIgnoreCase) => "/LICENSE=" + value["--license=".Length..],
                _ when value.StartsWith("--description=", StringComparison.OrdinalIgnoreCase) => "/DESCRIPTION=" + value["--description=".Length..],
                _ when value.StartsWith("--moniker=", StringComparison.OrdinalIgnoreCase) => "/MONIKER=" + value["--moniker=".Length..],
                _ when value.StartsWith("--source-root=", StringComparison.OrdinalIgnoreCase) => "/SOURCEROOT=" + value["--source-root=".Length..],
                _ when value.StartsWith("--source-revision=", StringComparison.OrdinalIgnoreCase) => "/SOURCEREVISION=" + value["--source-revision=".Length..],
                _ when value.StartsWith("--build-type=", StringComparison.OrdinalIgnoreCase) => "/BUILDTYPE=" + value["--build-type=".Length..],
                _ when value.StartsWith("--attest-key=", StringComparison.OrdinalIgnoreCase) => "/ATTESTKEY=" + value["--attest-key=".Length..],
                _ when value.StartsWith("--attest-key-id=", StringComparison.OrdinalIgnoreCase) => "/ATTESTKEYID=" + value["--attest-key-id=".Length..],
                _ when value.StartsWith("--attest-trust-key=", StringComparison.OrdinalIgnoreCase) => "/ATTESTTRUSTKEY=" + value["--attest-trust-key=".Length..],
                _ when value.Equals("--require-attestations", StringComparison.OrdinalIgnoreCase) => "/REQUIREATTESTATIONS",
                _ when value.StartsWith("--sign-cert=", StringComparison.OrdinalIgnoreCase) => "/SIGNCERT=" + value["--sign-cert=".Length..],
                _ when value.StartsWith("--sign-password=", StringComparison.OrdinalIgnoreCase) => "/SIGNPASSWORD=" + value["--sign-password=".Length..],
                _ when value.StartsWith("--sign-store=", StringComparison.OrdinalIgnoreCase) => "/SIGNSTORE=" + value["--sign-store=".Length..],
                _ when value.StartsWith("--sign-store-location=", StringComparison.OrdinalIgnoreCase) => "/SIGNSTORELOCATION=" + value["--sign-store-location=".Length..],
                _ when value.StartsWith("--sign-thumbprint=", StringComparison.OrdinalIgnoreCase) => "/SIGNTHUMBPRINT=" + value["--sign-thumbprint=".Length..],
                _ when value.StartsWith("--sign-subject=", StringComparison.OrdinalIgnoreCase) => "/SIGNSUBJECT=" + value["--sign-subject=".Length..],
                _ when value.StartsWith("--sign-remote-provider=", StringComparison.OrdinalIgnoreCase) => "/SIGNREMOTEPROVIDER=" + value["--sign-remote-provider=".Length..],
                _ when value.StartsWith("--sign-remote-endpoint=", StringComparison.OrdinalIgnoreCase) => "/SIGNREMOTEENDPOINT=" + value["--sign-remote-endpoint=".Length..],
                _ when value.StartsWith("--sign-remote-key=", StringComparison.OrdinalIgnoreCase) => "/SIGNREMOTEKEY=" + value["--sign-remote-key=".Length..],
                _ when value.StartsWith("--sign-remote-credential=", StringComparison.OrdinalIgnoreCase) => "/SIGNREMOTECREDENTIAL=" + value["--sign-remote-credential=".Length..],
                _ when value.StartsWith("--timestamp=", StringComparison.OrdinalIgnoreCase) => "/TIMESTAMP=" + value["--timestamp=".Length..],
                _ when value.StartsWith("--timestamp-outage=", StringComparison.OrdinalIgnoreCase) => "/TIMESTAMPOUTAGE=" + value["--timestamp-outage=".Length..],
                _ when value.StartsWith("--timestamp-retries=", StringComparison.OrdinalIgnoreCase) => "/TIMESTAMPRETRIES=" + value["--timestamp-retries=".Length..],
                _ when value.StartsWith("--signing-subject=", StringComparison.OrdinalIgnoreCase) => "/SIGNINGSUBJECT=" + value["--signing-subject=".Length..],
                _ => value
            };
        }
    }

    private static void AddBool(JsonElement root, string name, List<string> args, string flag)
    {
        if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True)
            args.Add(flag);
    }

    private static void AddValue(JsonElement root, string name, List<string> args, string prefix)
    {
        if (!root.TryGetProperty(name, out var value))
            return;

        var text = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind == JsonValueKind.Number
                ? value.GetRawText()
                : null;
        if (!string.IsNullOrWhiteSpace(text))
            args.Add(prefix + text);
    }

    private static void AddComponents(JsonElement root, List<string> args)
    {
        if (!root.TryGetProperty("components", out var value))
            return;

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                args.Add("/COMPONENTS=" + text);
            return;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            var components = value.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s));
            args.Add("/COMPONENTS=" + string.Join(',', components));
        }
    }

    private static void AddProperties(JsonElement root, List<string> args)
    {
        if (!root.TryGetProperty("properties", out var value) || value.ValueKind != JsonValueKind.Object)
            return;

        foreach (var property in value.EnumerateObject())
        {
            var text = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                    ? property.Value.GetRawText()
                    : null;
            if (!string.IsNullOrWhiteSpace(text))
                args.Add($"/PROPERTY:{property.Name}={text}");
        }
    }

    private static string? Value(string arg, string prefix)
        => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? arg[prefix.Length..] : null;

    private static string StripComment(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
                quoted = !quoted;
            if (!quoted && line[i] is '#' or ';')
                return line[..i];
        }

        return line;
    }

    private static IEnumerable<string> SplitCommandLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !quoted)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }
}
