using Beep.Installer.Extensibility.Providers;

namespace Beep.Installer.Extensibility;

public static class BuiltInInstallerResourceProviders
{
    public static BuiltInResourceProviderRegistry CreateDefaultRegistry()
        => new BuiltInResourceProviderRegistry()
            .Register(new ComponentSelectResourceProvider())
            .Register(new PackageInstallResourceProvider())
            .Register(new FileCopyResourceProvider())
            .Register(new RegistryWriteResourceProvider())
            .Register(new EnvironmentSetResourceProvider())
            .Register(new ShortcutCreateResourceProvider())
            .Register(new WindowsServiceResourceProvider())
            .Register(new ScheduledTaskResourceProvider())
            .Register(new FirewallRuleResourceProvider())
            .Register(new FileAssociationResourceProvider())
            .Register(new CertificateInstallResourceProvider())
            .Register(new ComRegistrationResourceProvider())
            .Register(new DriverPackageResourceProvider())
            .Register(new ConfigTransformResourceProvider())
            .Register(new IisAppPoolResourceProvider())
            .Register(new IisSiteResourceProvider())
            .Register(new WebDeployPackageResourceProvider());
}
