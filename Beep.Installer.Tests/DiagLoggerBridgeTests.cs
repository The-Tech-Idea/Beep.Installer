using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Beep.Installer.Engine;
using FluentAssertions;
using TheTechIdea.Beep.Logger;
using Xunit;

namespace Beep.Installer.Tests;

/// <summary>
/// The Diag → IDMLogger bridge (5.B.2).
///
/// An application consuming <c>TheTechIdea.Beep.Installer.Sdk</c> gets installer diagnostics only
/// in <c>%TEMP%</c>; one call at startup puts them wherever that application already logs. The
/// bridge is deliberately not an adoption of IDMLogger at the call sites — IDMLogger takes flat
/// strings, so doing that would discard the scope, event ids, JSONL log and in-memory ring that
/// the diagnostics qualification runner reads.
/// </summary>
[Collection("Diagnostics")]
public class DiagLoggerBridgeTests
{
    private sealed class RecordingLogger : IDMLogger
    {
        public List<(string Level, string Message)> Entries { get; } = new();
        public Func<string, bool>? Throw { get; set; }

        private void Record(string level, string message)
        {
            if (Throw?.Invoke(level) == true) throw new InvalidOperationException("host logger is broken");
            Entries.Add((level, message));
        }

        public void LogWarning(string warning) => Record("WARN", warning);
        public void LogInfo(string info) => Record("INFO", info);
        public void LogDebug(string message) => Record("DEBUG", message);
        public void LogError(string error) => Record("ERROR", error);
        public void LogCritical(string error) => Record("CRITICAL", error);
        public void LogTrace(string message) => Record("TRACE", message);
        public void WriteLog(string info) => Record("LOG", info);

        public void LogWithContext(string message, object context) { }
        public void LogStructured(string message, object properties) { }
        public void StartLog() { }
        public void StopLog() { }
        public void PauseLog() { }
        public void Flush() { }
        public void ConfigureLogger(Action<object> configure) { }
        public void AddLogFilter(Func<string, bool> filter) { }

#pragma warning disable CS0067 // The bridge raises neither event.
        public event EventHandler<string>? Onevent;
        public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067
    }

    private static string NewDiagDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BeepDiagBridge_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void EachLevel_ReachesTheMatchingLoggerMethod()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var logger = new RecordingLogger();
        Diag.Reset();

        using (Diag.UseLogger(logger))
        {
            Diag.Warn("Ctx", "a warning");
            Diag.Info("Ctx", "some information");
            Diag.Debug("Ctx", "a debug note");
        }

        logger.Entries.Select(e => e.Level).Should().Equal("WARN", "INFO", "DEBUG");
    }

    [Fact]
    public void ForwardedLine_KeepsTheContextThatIDMLoggerCannotCarry()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var logger = new RecordingLogger();
        Diag.Reset();

        using (Diag.UseLogger(logger))
        using (Diag.BeginScope("install", "corr-42"))
            Diag.Warn("Payload", "hash mismatch", eventId: "BI2499");

        // IDMLogger takes a bare string, so the scope, event id and context have to survive inside it.
        var line = logger.Entries.Should().ContainSingle().Subject.Message;
        line.Should().Contain("BI2499").And.Contain("install/Payload").And.Contain("hash mismatch");
    }

    [Fact]
    public void ExceptionDetail_SurvivesTheFlattening()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var logger = new RecordingLogger();
        Diag.Reset();

        using (Diag.UseLogger(logger))
            Diag.Warn("Copy", "could not copy", new IOException("file is locked"));

        logger.Entries.Should().ContainSingle().Which.Message.Should().Contain("file is locked");
    }

    [Fact]
    public void NothingIsForwarded_AfterTheHandleIsDisposed()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var logger = new RecordingLogger();
        Diag.Reset();

        using (Diag.UseLogger(logger))
            Diag.Info("Ctx", "inside");
        Diag.Info("Ctx", "outside");

        logger.Entries.Should().ContainSingle().Which.Message.Should().Contain("inside");
    }

    [Fact]
    public void NestedBridges_RestoreThePreviousLogger()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var outer = new RecordingLogger();
        var inner = new RecordingLogger();
        Diag.Reset();

        using (Diag.UseLogger(outer))
        {
            using (Diag.UseLogger(inner))
                Diag.Info("Ctx", "to inner");
            Diag.Info("Ctx", "back to outer");
        }

        inner.Entries.Should().ContainSingle().Which.Message.Should().Contain("to inner");
        outer.Entries.Should().ContainSingle().Which.Message.Should().Contain("back to outer");
    }

    [Fact]
    public void ABrokenHostLogger_DoesNotBreakTheInstaller()
    {
        using var logs = Diag.UseLogDirectory(NewDiagDirectory());
        var logger = new RecordingLogger { Throw = _ => true };
        Diag.Reset();

        // Diag's contract is that logging never throws; bridging must not weaken it.
        using (Diag.UseLogger(logger))
        {
            var log = () => Diag.Warn("Ctx", "still recorded locally");
            log.Should().NotThrow();
        }

        Diag.Recent.Should().Contain(e => e.Message == "still recorded locally",
            "the local ring is the canonical sink and must survive a failing host logger");
    }

    [Fact]
    public void UseLogger_RejectsNull()
    {
        var use = () => Diag.UseLogger(null!);

        use.Should().Throw<ArgumentNullException>();
    }
}
