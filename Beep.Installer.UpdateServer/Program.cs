using System;
using System.Collections.Generic;
using System.IO;
using Beep.Installer.UpdateServer.Models;
using Beep.Installer.UpdateServer.Services;
using Microsoft.AspNetCore.Http.Features;
using TheTechIdea.Beep.Updates;

var builder = WebApplication.CreateBuilder(args);

var options = new FeedServerOptions();
builder.Configuration.GetSection("UpdateServer").Bind(options);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<FeedStore>();
builder.Services.AddSingleton<TelemetryStore>();
builder.Services.AddSingleton<TokenService>();

// Setup.exe payloads are large — lift the upload limits.
builder.Services.Configure<FormOptions>(o => { o.MultipartBodyLengthLimit = long.MaxValue; });
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = null);

var app = builder.Build();

app.UseDefaultFiles();   // serve wwwroot/index.html (the admin dashboard) at /
app.UseStaticFiles();

static bool IsAuthed(HttpRequest req, FeedServerOptions opt)
    => req.Headers.TryGetValue("X-Api-Key", out var key) && key == opt.ApiKey && opt.ApiKey != "change-me";

static string BaseUrl(HttpRequest req) => $"{req.Scheme}://{req.Host}";

// ── Public: the feed a client polls (tailored to its rollout cohort via ?cid=) ──
app.MapGet("/{channel}/feed.json", (string channel, HttpRequest req, FeedStore store) =>
{
    var feed = store.BuildFeed(channel, req.Query["cid"], BaseUrl(req));
    return Results.Text(UpdateFeedClient.Serialize(feed), "application/json");
});

// ── Public: artifact download (gated when the channel requires a token) ──
app.MapGet("/artifacts/{channel}/{version}/{**path}", (string channel, string version, string path, HttpRequest req, FeedStore store, TokenService tokens) =>
{
    var state = store.GetChannel(channel);
    if (state.RequireDownloadToken && !tokens.Verify(channel, version, req.Query["token"]))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    var file = store.ArtifactPath(channel, version, path);
    return file == null
        ? Results.NotFound()
        : Results.File(file, "application/octet-stream", Path.GetFileName(file));
});

// ── Public: telemetry ingest (clients report their update outcomes) ──
app.MapPost("/api/telemetry", (TelemetryEvent evt, TelemetryStore telemetry) =>
{
    telemetry.Record(evt);
    return Results.Ok();
});

// ── Admin: list channels + versions ──
app.MapGet("/api/channels", (HttpRequest req, FeedStore store, FeedServerOptions opt) =>
{
    if (!IsAuthed(req, opt)) return Results.Unauthorized();
    var channels = new List<ChannelState>();
    foreach (var c in store.ListChannels()) channels.Add(store.GetChannel(c));
    return Results.Ok(channels);
});

// ── Admin: telemetry stats ──
app.MapGet("/api/stats", (HttpRequest req, TelemetryStore telemetry, FeedServerOptions opt) =>
    IsAuthed(req, opt) ? Results.Ok(telemetry.GetStats()) : Results.Unauthorized());

// ── Admin: publish a new version (multipart: setup [+ payload] + metadata) ──
app.MapPost("/api/publish", async (HttpRequest req, FeedStore store, FeedServerOptions opt) =>
{
    if (!IsAuthed(req, opt)) return Results.Unauthorized();
    if (!req.HasFormContentType) return Results.BadRequest("Expected multipart/form-data.");

    var form = await req.ReadFormAsync();
    var setup = form.Files["setup"];
    if (setup == null) return Results.BadRequest("Missing 'setup' file (the full Setup.exe).");

    var channel = Value(form, "channel", "stable");
    var version = Value(form, "version", "");
    if (string.IsNullOrWhiteSpace(version)) return Results.BadRequest("Missing 'version'.");

    var temp = Path.Combine(Path.GetTempPath(), "beepupsrv_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        var exePath = Path.Combine(temp, setup.FileName);
        await using (var fs = File.Create(exePath)) await setup.CopyToAsync(fs);

        string? payloadPath = null;
        if (form.Files["payload"] is { } payload)
        {
            payloadPath = Path.Combine(temp, payload.FileName);
            await using var fs = File.Create(payloadPath);
            await payload.CopyToAsync(fs);
        }

        var (ok, error) = store.Publish(
            channel,
            Value(form, "product", ""),
            version,
            exePath,
            payloadPath,
            Value(form, "minSupported", null),
            Value(form, "releaseNotes", null),
            int.TryParse(Value(form, "rolloutPercent", "100"), out var pct) ? pct : 100,
            bool.TryParse(Value(form, "republish", "false"), out var rp) && rp);

        return ok ? Results.Ok(new { channel, version }) : Results.BadRequest(error);
    }
    finally { try { Directory.Delete(temp, true); } catch { /* temp cleanup best-effort */ } }
});

// ── Admin: set rollout percent / enable-disable a version ──
app.MapPost("/api/rollout", (RolloutRequest body, HttpRequest req, FeedStore store, FeedServerOptions opt) =>
{
    if (!IsAuthed(req, opt)) return Results.Unauthorized();
    var (ok, error) = store.SetRollout(body);
    return ok ? Results.Ok() : Results.BadRequest(error);
});

// ── Admin: set the channel's module channel ──
app.MapPost("/api/modules", (ModulesRequest body, HttpRequest req, FeedStore store, FeedServerOptions opt) =>
{
    if (!IsAuthed(req, opt)) return Results.Unauthorized();
    store.SetModules(body.Channel, body.Modules ?? new List<ModuleRef>());
    return Results.Ok();
});

// ── Admin: turn download-token gating on/off for a channel ──
app.MapPost("/api/gate", (GateRequest body, HttpRequest req, FeedStore store, FeedServerOptions opt) =>
{
    if (!IsAuthed(req, opt)) return Results.Unauthorized();
    store.SetGate(body.Channel, body.Require);
    return Results.Ok();
});

// ── Admin: issue a download token for a gated channel ──
app.MapPost("/api/token", (TokenRequest body, HttpRequest req, TokenService tokens, FeedServerOptions opt) =>
{
    if (!IsAuthed(req, opt)) return Results.Unauthorized();
    var token = tokens.Issue(body.Channel, body.Version, TimeSpan.FromMinutes(body.TtlMinutes <= 0 ? 60 : body.TtlMinutes));
    return Results.Ok(new { token });
});

app.Run();

static string? Value(IFormCollection form, string key, string? fallback)
    => form.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v.ToString() : fallback;

// Request bodies for the JSON admin endpoints.
public sealed record ModulesRequest(string Channel, List<ModuleRef> Modules);
public sealed record TokenRequest(string Channel, string Version, int TtlMinutes);
public sealed record GateRequest(string Channel, bool Require);
