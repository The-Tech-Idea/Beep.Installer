using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Beep.Installer.Engine;
using FluentAssertions;
using TheTechIdea.Beep.Services.Logging;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// The Diag → IBeepLog bridge (5.B.2).
///
/// <c>IBeepLog</c> is the target rather than the legacy <c>IDMLogger</c>: its <c>Log</c> takes a
/// level, a category, a property bag and the exception itself, so a diagnostic entry crosses into
/// Beep's telemetry pipeline whole instead of being flattened into one string. BeepDM is moving the
/// same way — <c>BeepLoggingOptions.ReplaceDMLogger</c> defaults to true.
/// </summary>
[Collection("Diagnostics")]
public class DiagLoggerBridgeTests
{
    private sealed record Recorded(
        BeepLogLevel Level,
        string Category,
        string Message,
        IReadOnlyDictionary<string, object>? Properties,
        Exception? Exception);

    private sealed class RecordingLog : IBeepLog
    {
        public List<Recorded> Entries { get; } = new();
        public bool IsEnabled { get; set; } = true;
        public BeepLogLevel MinLevel { get; set; } = BeepLogLevel.Trace;
        public bool ThrowOnLog { get; set; }

        public void Log(BeepLogLevel level, string category, string message,
            IReadOnlyDictionary<string, object>? properties = null, Exception? exception = null)
        {
            if (ThrowOnLog) throw new InvalidOperationException("host pipeline is broken");
            Entries.Add(new Recorded(level, category, message, properties, exception));
        }

        public void Trace(string message, object? properties = null) { }
        public void Debug(string message, object? properties = null) { }
        public void Info(string message, object? properties = null) { }
        public void Warn(string message, object? properties = null) { }
        public void Error(string message, Exception? ex = null, object? properties = null) { }
        public void Critical(string message, Exception? ex = null, object? properties = null) { }
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static string NewDiagDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BeepDiagBridge_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void EachLevel_MapsOntoTheBeepLogLevel()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var log = new RecordingLog();
        Diag.Reset();

        using (Diag.UseLog(log))
        {
            Diag.Warn("Ctx", "a warning");
            Diag.Info("Ctx", "some information");
            Diag.Debug("Ctx", "a debug note");
        }

        log.Entries.Select(e => e.Level).Should()
            .Equal(BeepLogLevel.Warning, BeepLogLevel.Information, BeepLogLevel.Debug);
    }

    [Fact]
    public void ScopeAndEventId_TravelAsPropertiesNotAsAFlattenedString()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var log = new RecordingLog();
        Diag.Reset();

        using (Diag.UseLog(log))
        using (Diag.BeginScope("install", "corr-42"))
            Diag.Warn("Payload", "hash mismatch", eventId: "BI2499");

        var entry = log.Entries.Should().ContainSingle().Subject;
        entry.Category.Should().Be("Payload", "the diagnostic context is the log category");
        entry.Message.Should().Be("hash mismatch", "the message stays a message, not a rendered line");
        entry.Properties.Should().NotBeNull();
        entry.Properties!["eventId"].Should().Be("BI2499");
        entry.Properties["operation"].Should().Be("install");
        entry.Properties["correlationId"].Should().Be("corr-42");
    }

    [Fact]
    public void TheExceptionItself_IsHandedOver_NotItsToString()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var log = new RecordingLog();
        var failure = new IOException("file is locked");
        Diag.Reset();

        using (Diag.UseLog(log))
            Diag.Warn("Copy", "could not copy", failure);

        log.Entries.Should().ContainSingle().Which.Exception.Should().BeSameAs(failure);
    }

    [Fact]
    public void ContextlessEntries_GetAStableCategory()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var log = new RecordingLog();
        Diag.Reset();

        using (Diag.UseLog(log))
            Diag.Info("", "no context supplied");

        log.Entries.Should().ContainSingle().Which.Category.Should().Be("Installer");
    }

    [Fact]
    public void NothingIsForwarded_WhenTheHostPipelineIsDisabled()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var log = new RecordingLog { IsEnabled = false };
        Diag.Reset();

        using (Diag.UseLog(log))
            Diag.Warn("Ctx", "dropped before the property bag is built");

        log.Entries.Should().BeEmpty();
        Diag.Recent.Should().ContainSingle(e => e.Context == "Ctx", "the local ring is unaffected");
    }

    [Fact]
    public void EntriesBelowMinLevel_AreNotForwarded()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var log = new RecordingLog { MinLevel = BeepLogLevel.Warning };
        Diag.Reset();

        using (Diag.UseLog(log))
        {
            Diag.Debug("Ctx", "below the floor");
            Diag.Warn("Ctx", "at the floor");
        }

        log.Entries.Should().ContainSingle().Which.Level.Should().Be(BeepLogLevel.Warning);
    }

    [Fact]
    public void NothingIsForwarded_AfterTheHandleIsDisposed()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var log = new RecordingLog();
        Diag.Reset();

        using (Diag.UseLog(log))
            Diag.Info("Ctx", "inside");
        Diag.Info("Ctx", "outside");

        log.Entries.Should().ContainSingle().Which.Message.Should().Be("inside");
    }

    [Fact]
    public void NestedBridges_RestoreThePreviousLog()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var outer = new RecordingLog();
        var inner = new RecordingLog();
        Diag.Reset();

        using (Diag.UseLog(outer))
        {
            using (Diag.UseLog(inner))
                Diag.Info("Ctx", "to inner");
            Diag.Info("Ctx", "back to outer");
        }

        inner.Entries.Should().ContainSingle().Which.Message.Should().Be("to inner");
        outer.Entries.Should().ContainSingle().Which.Message.Should().Be("back to outer");
    }

    [Fact]
    public void ABrokenHostPipeline_DoesNotBreakTheInstaller()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var log = new RecordingLog { ThrowOnLog = true };
        Diag.Reset();

        // Diag's contract is that logging never throws; bridging must not weaken it.
        using (Diag.UseLog(log))
        {
            var write = () => Diag.Warn("Ctx", "still recorded locally");
            write.Should().NotThrow();
        }

        Diag.Recent.Should().Contain(e => e.Message == "still recorded locally",
            "the local ring is canonical and must survive a failing host pipeline");
    }

    [Fact]
    public void UseLog_RejectsNull()
    {
        var use = () => Diag.UseLog(null!);

        use.Should().Throw<ArgumentNullException>();
    }
}
