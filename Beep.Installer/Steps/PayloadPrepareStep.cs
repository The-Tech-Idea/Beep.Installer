using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using TheTechIdea.Beep.Addin;
using TheTechIdea.Beep.ConfigUtil;
using TheTechIdea.Beep.SetUp;

namespace Beep.Installer.Steps;

/// <summary>
/// Prepares the local payload so <see cref="FileCopyStep"/> can resolve rebased
/// (relative) source paths. Handles two local layouts produced by the builder:
///   - <c>{configDir}/payload/</c>  (uncompressed)
///   - <c>{configDir}/payload.zip</c> (compressed — extracted on demand)
/// After running, <c>context["PayloadRoot"]</c> points at the directory that contains
/// the staged files. If <see cref="PayloadDownloadStep"/> already set PayloadRoot
/// (Url mode), this step is a no-op.
/// </summary>
public class PayloadPrepareStep : ISetupStep
{
    public string StepId => "installer.payload.prepare";
    public string StepName => "Prepare payload";
    public string Description => "Locates (and extracts if needed) the local install payload.";
    public IReadOnlyList<string> DependsOn { get; }

    public PayloadPrepareStep(string? dependsOn = null)
    {
        DependsOn = dependsOn != null ? new List<string> { dependsOn } : Array.Empty<string>();
    }

    public bool CanSkip(SetupContext context)
    {
        // Already resolved by PayloadDownloadStep (Url mode) — nothing to do.
        return context.TryGetProperty<string>("PayloadRoot") != null;
    }

    public IErrorsInfo Validate(SetupContext context) => StepErrorHelpers.Ok("Payload location will be resolved at execution time.");

    public IErrorsInfo Execute(SetupContext context, IProgress<PassedArgs>? progress = null)
    {
        if (context.TryGetProperty<string>("PayloadRoot") != null)
            return StepErrorHelpers.Ok("PayloadRoot already set.");

        // A2.3: self-extracting single-file payload embedded at the end of this EXE.
        var myExe = Environment.ProcessPath;
        var embedded = !string.IsNullOrEmpty(myExe) ? Engine.PePayloadWriter.FindEmbedded(myExe) : null;
        if (embedded != null)
        {
            var tempZip = Path.Combine(Path.GetTempPath(), $"BeepEmbedded_{Guid.NewGuid():N}.zip");
            Engine.PePayloadWriter.Extract(myExe!, tempZip);
            var extractRoot = Path.Combine(Path.GetTempPath(), $"BeepPayload_{Guid.NewGuid():N}");
            var embeddedFolder = ResolvePayloadFolderName();
            var root = Engine.PayloadPackager.ExtractZip(tempZip, extractRoot, embeddedFolder);
            context.Properties["PayloadRoot"] = root;
            try { File.Delete(tempZip); } catch { }
            progress?.Report(new PassedArgs { Messege = "Payload extracted from embedded resource.", ParameterInt1 = 100 });
            return StepErrorHelpers.Ok($"Payload ready (embedded): {root}");
        }

        var folder = ResolvePayloadFolderName();
        var searchBases = ResolveSearchBases();

        foreach (var basePath in searchBases)
        {
            var payloadDir = Path.Combine(basePath, folder);
            if (Directory.Exists(payloadDir) && HasAnyFile(payloadDir))
            {
                context.Properties["PayloadRoot"] = payloadDir;
                progress?.Report(new PassedArgs { Messege = $"Using payload folder: {payloadDir}", ParameterInt1 = 100 });
                return StepErrorHelpers.Ok($"Payload ready: {payloadDir}");
            }
        }

        var method = ResolveCompressionMethod();

        // LZMA2 (.7z) archive — best-effort via 7z.
        if (string.Equals(method, "lzma2", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var basePath in searchBases)
            {
                var sevenZ = Path.Combine(basePath, folder + ".7z");
                if (File.Exists(sevenZ))
                {
                    var extractRoot = Path.Combine(Path.GetTempPath(), $"BeepPayload_{Guid.NewGuid():N}");
                    if (!Engine.PayloadPackager.TryExtractLzma(sevenZ, extractRoot, out var lzmaErr))
                        return StepErrorHelpers.Fail($"Failed to extract LZMA payload '{sevenZ}': {lzmaErr}");
                    var root = Directory.Exists(Path.Combine(extractRoot, folder)) ? Path.Combine(extractRoot, folder) : extractRoot;
                    context.Properties["PayloadRoot"] = root;
                    return StepErrorHelpers.Ok($"Payload extracted (LZMA): {root}");
                }
            }
        }

        // No uncompressed folder — look for payload.zip (solid or plain) and extract it.
        foreach (var basePath in searchBases)
        {
            var zipPath = Path.Combine(basePath, folder + ".zip");
            if (File.Exists(zipPath))
            {
                try
                {
                    var extractRoot = Path.Combine(Path.GetTempPath(), $"BeepPayload_{Guid.NewGuid():N}");
                    var root = Engine.PayloadPackager.ExtractZip(zipPath, extractRoot, folder);
                    context.Properties["PayloadRoot"] = root;
                    progress?.Report(new PassedArgs { Messege = $"Extracted payload zip: {zipPath}", ParameterInt1 = 100 });
                    return StepErrorHelpers.Ok($"Payload extracted: {root}");
                }
                catch (Exception ex)
                {
                    return StepErrorHelpers.Fail($"Failed to extract payload zip '{zipPath}': {ex.Message}");
                }
            }
        }

        // Nothing staged locally (e.g. empty payload or Url mode with no download).
        // Fall back to the config/exe directory so absolute paths still resolve.
        var fallback = searchBases[0];
        context.Properties["PayloadRoot"] = fallback;
        return StepErrorHelpers.Ok($"No local payload found — resolving relative paths against: {fallback}");
    }

    public Task<IErrorsInfo> ExecuteAsync(SetupContext context, IProgress<PassedArgs>? progress = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(Execute(context, progress));
    }

    // ── helpers ──

    private static string ResolvePayloadFolderName()
        => Engine.RuntimeProjectContext.Current?.PayloadFolderName ?? "payload";

    private static List<string> ResolveSearchBases()
    {
        var bases = new List<string>(2);
        var dir = Engine.RuntimeProjectContext.Current?.SourceDirectory;
        if (!string.IsNullOrWhiteSpace(dir))
            bases.Add(dir!);
        bases.Add(AppContext.BaseDirectory);
        return bases;
    }

    private static bool HasAnyFile(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).GetEnumerator().MoveNext(); }
        catch (Exception ex) { Engine.Diag.Debug("PayloadPrepareStep", "HasAnyFile check failed", ex); return false; }
    }

    private static string ResolveCompressionMethod()
    {
        return Engine.RuntimeProjectContext.Current?.Compression switch
        {
            Beep.Installer.Models.CompressionFormat.Lzma2 => "lzma2",
            _ => "zip"
        };
    }
}
