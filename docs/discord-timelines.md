# Discord game timelines

Discord uses one Components V2 message with game sections in the filter's game order. Each game image appears above its section in a full-width media gallery, followed by the game name and events. Within each game, events are ordered by that game's bracket start time. Multi-game events appear in each matching section without extra cross-reference labels. Multiple brackets for the same game retain separate times and registration links. `current entrants: #` shows the bracket's count, not a sum across games.

## Data and time handling

Intake requests start.gg `Event.startAt` and persists it in the existing `tournament_links.start_time` column. Preview responses include `events[].brackets` with selected games, bracket IDs, links, UTC start times and entrant counts. `maxEvents` still counts parent editions. Existing data needs a normal intake refresh to populate missing bracket times. Missing times and times outside the known event's start/end range are left blank, without warnings or a substitute parent time. Discord timestamps display in each reader's timezone. Old queued snapshots remain readable with a generic section and their original known time.

## One message and overflow

Messages stay within 4,000 text characters and 40 components. A tournament is kept whole; if the next tournament will not fit, it remains queued for a later eligible notification. OnInsert retains its wake-up and reserves history only for included parents. Daily/Interval continue an ID-only cursor from the previous successful delivery before starting a fresh digest; completion still removes the destination and message body. The hourly send limit, retry rules and ambiguous-send protection are unchanged.

A dense single tournament uses a shared heading and registration link. Very large selections may use text headings for some games to stay within the component limit; unknown games always have a text heading. An individual tournament that cannot fit even the compact layout fails validation rather than silently losing brackets. MaxEvents continues to bound each query; matches outside that configured cap are not part of a scheduled digest's overflow cursor.

## Artwork and rollout

Set `EventFeeds__AssetsBaseUrl` on Dispatcher to the public HTTPS origin of Provider before deploying. Provider serves bundled images under `/assets/game-headers/`; QA deployment verifies its existing Provider origin and all 11 images without changing app settings. Configuration changes can restart App Service and interrupt package extraction, so provision them separately and let the service recover before deploying code. The four supplied headers and seven additional game images are mapped by start.gg game ID. Source provenance is included alongside the assets. Missing configuration produces text-only headings. Discord controls image height and responsive sizing; images occupy their own media blocks above the text, with their original aspect ratios.

Deploy Provider's public artwork before the new Dispatcher. Existing rows acquire bracket times on the next normal intake refresh. No bot permissions, session authentication changes, CSRF changes, database migration or Data Protection changes are required.

## Local verification and preview

`./tests/Run-LocalTests.ps1` runs the full isolated workflow. It tests two selected games, an excluded third game, persisted bracket times, public artwork, and one delivery/history entry per parent. The database regression checks verify overflow across OnInsert, Daily and Interval, sensitive-snapshot cleanup, and no dropped or duplicated tournaments.

For an offline payload preview from public start.gg query data:

```powershell
dotnet run --project tests/EventFeeds.Tests -- --render-timeline <source.json> <payload.json>
```

The source is a JSON object whose values are tournament objects (`name`, `slug`, `city`, `addrState`, `startAt`, and `events` with `id`, `slug`, `startAt`, `numEntrants`, `videogame.id/name`). This sample renderer selects Strive and Granblue; it never contacts Discord.

References: [Discord component reference](https://docs.discord.com/developers/components/reference), [webhook execution](https://docs.discord.com/developers/resources/webhook#execute-webhook), [start.gg Event schema](https://smashgg-schema.netlify.app/reference/event.doc.html).
