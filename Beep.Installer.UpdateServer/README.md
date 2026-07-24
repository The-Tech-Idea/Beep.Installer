# Beep.Installer.UpdateServer

An **optional** hosting server for Beep app self-update (decision **D10 = C**). You don't need it
to ship updates — a static host (GitHub Releases / S3 / IIS / a folder share) works out of the box.
Run this only when you want what a static host can't do:

- **Staged / percentage rollout** — release to 5% → 50% → 100%, per-version, with a stable
  per-client cohort so users don't flip-flop.
- **Telemetry & stats** — clients report update outcomes; the dashboard shows successes/failures
  and how many clients are on each version.
- **Token-gated downloads** — require a signed `?token=` per channel for license-gated releases.
- **Publish API + admin dashboard** — upload a build and manage rollout/gating from a web page.

The client contract is **unchanged**: it still fetches `feed.json` + `_blobs/<sha256>` over HTTP.
The server just tailors the feed it returns to the caller's rollout cohort.

## Run it

```
dotnet run --project Beep.Installer.UpdateServer
```

Configure in `appsettings.json` (or env vars `UpdateServer__ApiKey`, etc.):

```json
"UpdateServer": {
  "StorageRoot": "App_Data/feed",   // where channels/versions/artifacts live
  "ApiKey": "change-me",            // required (X-Api-Key) for publish/admin — admin is locked until you change this
  "TokenSecret": "change-me-too"    // HMAC secret for download tokens
}
```

The admin dashboard is at `/` (enter the API key, Connect).

## Point an app at it

```csharp
new UpdateSettings {
    FeedUrl = "https://updates.example.com/stable/feed.json?cid=" + machineId,  // cid = stable per-client id for cohorts
    CurrentVersion = "1.2.0",
    InstallRoot = AppContext.BaseDirectory
}
```

`cid` is any stable per-install identifier; it decides the rollout cohort. Omit it and the client
is simply held on the fully-rolled-out version until a release reaches 100%.

## Endpoints

| Method | Route | Auth | Purpose |
|---|---|---|---|
| GET | `/{channel}/feed.json?cid=…` | public | The rollout-tailored feed for a client |
| GET | `/artifacts/{channel}/{version}/{**path}` | public (or `?token=` if gated) | Setup.exe, `_blobs/*`, manifest |
| POST | `/api/telemetry` | public | Client reports an update event |
| POST | `/api/publish` | `X-Api-Key` | Publish a version (multipart: `setup` + optional `payload` + fields) |
| POST | `/api/rollout` | `X-Api-Key` | `{channel,version,percent?,enabled?}` |
| POST | `/api/gate` | `X-Api-Key` | `{channel,require}` — toggle token-gated downloads |
| POST | `/api/token` | `X-Api-Key` | `{channel,version,ttlMinutes}` — issue a download token |
| POST | `/api/modules` | `X-Api-Key` | `{channel,modules[]}` — set the feed's module channel |
| GET | `/api/channels` / `/api/stats` | `X-Api-Key` | Admin reads |

## Publish from CI

Either upload the installer's `/PUBLISHFEED` output via the publish API, or from the dashboard:

```bash
curl -X POST https://updates.example.com/api/publish \
  -H "X-Api-Key: $KEY" \
  -F channel=stable -F product=MyApp -F version=1.2.3 -F rolloutPercent=10 \
  -F setup=@out/Setup-MyApp-1.2.3.exe \
  -F payload=@out/payload.zip            # optional; enables deltas
```

The server runs the same `FeedPublisher` the installer uses (immutable versions, solid→loose blob
expansion), then stages the version at 10% rollout. Bump it in the dashboard when you're confident.

## Deploy

Publish a self-contained build and run it behind a TLS-terminating reverse proxy:

```bash
dotnet publish Beep.Installer.UpdateServer -c Release -o /opt/beep-updates
# configure (never ship the defaults):
export UpdateServer__ApiKey="$(openssl rand -hex 24)"
export UpdateServer__TokenSecret="$(openssl rand -hex 32)"
export UpdateServer__StorageRoot="/var/lib/beep-updates"
export ASPNETCORE_URLS="http://127.0.0.1:5080"
/opt/beep-updates/Beep.Installer.UpdateServer
```

- **HTTPS**: terminate TLS at nginx/Caddy/IIS and proxy to the Kestrel port above — artifacts and
  the feed must be served over HTTPS (D11: the client trusts TLS + per-artifact SHA-256).
- **Large uploads**: the publish endpoint accepts whole Setup.exe files, so raise the proxy's body
  limit (nginx `client_max_body_size 0;`). Kestrel's own limit is already lifted in-process.
- **Run as a service**: a systemd unit (`ExecStart=/opt/beep-updates/Beep.Installer.UpdateServer`,
  `Restart=always`) or Windows IIS with the ASP.NET Core Module. Set the config via environment
  variables (double-underscore = section nesting, as above) rather than editing `appsettings.json`.
- **Back up `StorageRoot`** — it holds every published version, `channel.json` (rollout state) and
  `telemetry.jsonl`. It is the whole server; the process is stateless.
- **Scale**: it's a static file host plus small JSON reads/writes; one small VM serves large fleets.
  For very large fleets, put a CDN in front of `/artifacts/*` (immutable, content-addressed) and
  keep only the feed/publish/telemetry endpoints on the origin.

## Notes

- File-backed (no database): channels/versions/rollout in `channel.json` per channel, telemetry in
  `telemetry.jsonl`.
- Admin is **locked until you change the default `ApiKey`** (the server rejects the literal
  `change-me`). Put it behind HTTPS + real auth for internet exposure.
- Telemetry populates automatically: the `TheTechIdea.Beep.Updates` client POSTs `check` /
  `apply-success` / `apply-failure` / `modules-*` events to `/api/telemetry` when the app sets
  `UpdateSettings.TelemetryUrl` (and `ClientId` for the rollout cohort).
