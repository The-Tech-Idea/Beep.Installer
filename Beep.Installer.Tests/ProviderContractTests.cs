using System;
using System.Collections.Generic;
using System.Linq;
using Beep.Installer.Engine;
using Beep.Installer.Extensibility;
using FluentAssertions;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// A provider that throws is attributed to that provider (SOLID review, L).
///
/// <c>IResourceProvider.Apply</c> is contracted to report failure by returning a
/// <c>Failed</c> result, because <c>ResourcePlanExecutor</c> decides rollback from the return value.
/// One implementation did not: <c>FileCopyResourceProvider</c> let an <c>IOException</c> from its
/// pre-copy backup escape, and it surfaced two layers up as "Failed to checkpoint typed resource
/// journal" — a message naming the wrong component, which is what made the underlying defect hard to
/// find. The type system cannot express "does not throw", so the executor enforces it instead.
/// </summary>
public class ProviderContractTests
{
    private sealed class ThrowingProvider : IResourceProvider
    {
        public string ResourceType => "test.throws";
        public InstallerExtensionPermission RequiredPermissions => InstallerExtensionPermission.None;

        public ResourceDetectionResult Detect(CompiledInstallOperation operation, ResourceProviderContext context)
            => new() { Exists = false };

        public ResourceProviderResult Validate(CompiledInstallOperation operation, ResourceProviderContext context)
            => new();

        public ResourcePlanResult Plan(CompiledInstallOperation operation, ResourceDetectionResult detection, ResourceProviderContext context)
            => new()
            {
                ChangeKind = ResourceChangeKind.Create,
                Operations = new List<CompiledInstallOperation> { operation }
            };

        public ResourceProviderResult Apply(CompiledInstallOperation operation, ResourceProviderContext context)
            => throw new InvalidOperationException("this provider breaks its contract");

        public ResourceProviderResult Rollback(CompiledInstallOperation operation, ResourceProviderContext context)
            => new();

        public ResourceProviderResult Verify(CompiledInstallOperation operation, ResourceProviderContext context)
            => new();
    }

    private static (CompiledInstallPlan Plan, ResourceProviderContext Context) OneOperation()
    {
        var operation = new CompiledInstallOperation
        {
            Id = "test.throws:1",
            Type = "test.throws",
            RollbackSupported = false
        };

        // The executor validates journal metadata before it reaches any provider, so the plan needs
        // a real identity or the run bails with BI6002 and never exercises the contract.
        var plan = new CompiledInstallPlan
        {
            AppId = "a34321a2-680b-43a8-af88-c56d6afab012",
            ProductName = "ContractApp",
            ProductVersion = "1.0.0",
            Operations = { operation }
        };
        var context = new ResourceProviderContext { InstallRoot = @"C:\App", ExecutionMode = "install" };
        return (plan, context);
    }

    [Fact]
    public void AProviderThatThrows_IsReportedAsAFailedOperation_NotAnUnwind()
    {
        var registry = new BuiltInResourceProviderRegistry();
        registry.Register(new ThrowingProvider());
        var (plan, context) = OneOperation();

        var act = () => new ResourcePlanExecutor(registry).ExecuteInstall(plan, context, new ResourceExecutionOptions());

        act.Should().NotThrow("the executor must convert a contract breach into a result");
    }

    [Fact]
    public void TheFailureNamesTheProviderThatBrokeTheContract()
    {
        // The original defect was not the exception -- it was that the message blamed the journal,
        // sending anyone investigating to the wrong component.
        var registry = new BuiltInResourceProviderRegistry();
        registry.Register(new ThrowingProvider());
        var (plan, context) = OneOperation();

        var result = new ResourcePlanExecutor(registry).ExecuteInstall(plan, context, new ResourceExecutionOptions());

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "BI3020",
            "a contract breach gets its own diagnostic code so it is not mistaken for an ordinary failure");
        result.Diagnostics.Should().Contain(d => d.Message.Contains("test.throws", StringComparison.Ordinal),
            "the message must name the offending provider");
    }

    [Fact]
    public void EveryShippedProviderDeclaresItsResourceType()
    {
        // Cheap guard on the registry contract: a provider with no type cannot be dispatched to, and
        // the failure would be a silent "no provider registered" rather than anything diagnosable.
        var registry = BuiltInInstallerResourceProviders.CreateDefaultRegistry();

        var types = registry.Providers.Select(p => p.ResourceType).ToList();

        types.Should().NotBeEmpty("the default registry ships the built-in providers");

        types.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t));
        types.Should().OnlyHaveUniqueItems("two providers claiming one type makes dispatch ambiguous");
    }
}
