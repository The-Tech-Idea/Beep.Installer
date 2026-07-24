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

## Notes

- File-backed (no database): channels/versions/rollout in `channel.json` per channel, telemetry in
  `telemetry.jsonl`. Back up `StorageRoot`.
- Admin is **locked until you change the default `ApiKey`** (the server rejects the literal
  `change-me`). Put it behind HTTPS + real auth for internet exposure.
- Telemetry only populates once clients POST to `/api/telemetry` — wire that from the app's update
  flow (a small follow-on to the `TheTechIdea.Beep.Updates` client).
