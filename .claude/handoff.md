# Handoff — 2026-03-16

## Completed This Session

### Tap WebSocket Acks
- Removed `TAP_DISABLE_ACKS=true` from `AppHost/Program.cs` — Tap now runs in default acks mode
- Updated `TapWorker.cs` to implement the ack protocol:
  - `ProcessMessageAsync` now returns `uint?` (top-level event `id` from Tap message)
  - After each complete message, sends `{"type":"ack","id":<n>}` back over the WebSocket
  - Processing errors return `null` — event not acked, Tap retries after 60s
  - Ack is sent *after* DB write, so delivery is only confirmed once persisted

### labeler-db connection string
- Set via `dotnet user-secrets` in both `AltTextBot.Worker` and `AltTextBot.Web`
- Host=localhost;Port=5435;Database=AltLabels

## Discussed (No Code Changes)

### Jetstream vs Tap — architecture clarification
- Confirmed both workers are needed; they do different things
- JetstreamWorker: watches global `app.bsky.feed.like` firehose to discover new/lost subscribers
- TapWorker: watches registered subscriber repos for `app.bsky.feed.post` events
- Tap's "full network" mode (`TAP_FULL_NETWORK=true`) is impractical for like detection (days/weeks backfill)
- Jetstream could theoretically replace Tap (not the reverse), but Tap's server-side DID filtering is more efficient at scale

### Tap filtering modes (from README)
- Default: dynamically configured by DID list (`/repos/add`, `/repos/remove`)
- `TAP_SIGNAL_COLLECTION`: track all repos with records in a given collection
- `TAP_FULL_NETWORK=true`: enumerate entire network (resource-intensive)
- `TAP_COLLECTION_FILTERS`: comma-separated NSID filters (wildcards at period breaks)

## Pending / Next Steps

- Investigate `Labeler:BaseUrl` and `Labeler:ApiKey` — user thinks these may need to be handled via the idunno.Bluesky SDK rather than raw config. Review how ILabelerClient uses them and whether the SDK has a labeler auth pattern.
- Still needed before running end-to-end: `Admin:Password`/`Admin:ApiKey` (web UI), and the labeler config above
