# osu-performance-server

A server for calculating osu! performance points (pp) and star ratings.

## Prerequisites

- .NET SDK 8.0+ installed
- Internet access for on-demand beatmap fetching (unless providing `beatmap_file` or cached locally)

## Quick Start

```bash
# Restore dependencies
 dotnet restore

# Run the server (HTTP on http://localhost:5000 by default, or use ASPNETCORE_URLS)
 dotnet run --project PerformanceServer

# (Optional) Specify a custom port
 ASPNETCORE_URLS=http://0.0.0.0:5080 dotnet run --project PerformanceServer
```

## Configuration (Environment Variables)

| Variable                | Default                      | Description                                                                                 |
|-------------------------|------------------------------|---------------------------------------------------------------------------------------------|
| `SAVE_BEATMAP_FILES`    | (unset / false)              | When set to `true`, fetched beatmaps are cached locally.                                    |
| `BEATMAPS_PATH`         | `./beatmaps`                 | Directory for cached `.osu` files. Created if missing.                                      |
| `RULESETS_PATH`         | `./rulesets`                 | Directory for ruleset DLLS loaded at runtime.                                               |
| `OSU_FILE_WEB_URL`      | `https://osu.ppy.sh/osu/{0}` | Format string used to fetch beatmaps by ID. `{0}` replaced with beatmap ID.                 |
| `MAX_BEATMAP_FILE_SIZE` | `5242880` (5 MB)             | Max size (bytes) allowed when deciding to persist fetched file. Larger files are not saved. |

## Using delta custom osu! ruleset calculations

This repository can run with a custom `osu.Game.Rulesets.Osu.dll` bundled at:

```text
PerformanceServer/libs/osu.Game.Rulesets.Osu.dll
```

`PerformanceServer.csproj` references this DLL directly so server-side `/difficulty` and `/performance` use the same osu!standard SR/PP logic as your client fork.

When updating client-side calculation logic, rebuild `osu.Game.Rulesets.Osu.dll` and replace this file before building/publishing the server.

## Health Endpoints

- `GET /` returns `{ "status": "ok", "time": "<UTC timestamp>" }` for readiness probes.

## POST /difficulty

Calculate difficulty attributes for a beatmap (star rating, etc.).

Request JSON fields:

- `beatmap_id` (int) Required if `beatmap_file` not provided.
- `checksum` (string, optional) MD5 hash used to validate local cached file; if mismatch triggers re-download.
- `mods` (array, optional) List of osu! API mod objects (typically at least `{ "acronym": "HD" }`). Unsupported or
  invalid acronyms will be ignored by the underlying conversion if not recognized.
- `ruleset_id` (int, optional) Override ruleset; if omitted and beatmap file is provided/decoded it falls back to the
  beatmap's own ruleset.
- `ruleset` (string, optional) Same as above but by name (e.g., `osu`, `taiko`, `fruits`, `mania`). If both ID and
  name are provided, Name takes precedence.
- `beatmap_file` (string, optional) Raw `.osu` file content (entire file). If present, `beatmap_id` may still be
  supplied for caching, but content is authoritative.

Example request using inline file content:

```json
{
  "beatmap_id": 2226842,
  "mods": [
    {
      "acronym": "HD"
    },
    {
      "acronym": "HR"
    }
  ],
  "ruleset_id": 0,
  "beatmap_file": "osu file format..."
}
```

Successful Response: `200 OK` with a JSON-serialized [
`DifficultyAttributes`](https://osu.ppy.sh/docs/index.html#beatmapdifficultyattributes) object.

Error Cases:

- `400 Bad Request` – Internal calculation failed (rare / invalid input).
- `503 Service Unavailable` – Remote fetch failed (e.g., beatmap not accessible online).

## POST /performance

Calculate performance attributes (pp) using both difficulty and supplied score context.

Request JSON fields:

- `beatmap_id` / `beatmap_file` / `checksum` – Same semantics as `/difficulty`.
- `mods` (array) Same structure as above.
- `is_legacy` (bool) Whether to treat score as stable scores.
- `accuracy` (float) Accuracy value (0.0–1.0). Provide either accurate `statistics` or a suitable accuracy.
- `ruleset_id` (int, optional) Explicit ruleset selection (Either `ruleset_id` or `ruleset` must be provided).
- `ruleset` (string, optional) Same as above but by name (e.g., `osu`, `taiko`, `fruits`, `mania`). If both ID and
  name are provided, Name takes precedence.
- `combo` (int) Achieved max combo for the score.
- `statistics` (object) Mapping of hit result enum names to counts. Keys must match `HitResult` enumeration names from
  osu! (e.g., `great`, `ok`, `meh`, `miss`, `perfect`, `good`, etc. — varies by ruleset). Only relevant ones need to be
  present.

Minimal example:

```json
{
  "beatmap_id": 2226842,
  "ruleset_id": 0,
  "mods": [
    {
      "acronym": "HD"
    }
  ],
  "is_legacy": false,
  "accuracy": 0.9853,
  "combo": 1234,
  "statistics": {
    "great": 1000,
    "good": 15,
    "meh": 2,
    "miss": 1
  }
}
```

Successful Response: `200 OK` with a JSON `PerformanceAttributes` object with `ruleset` (name) including (fields differ
by ruleset):

- `pp` (float) Total pp value.
- Component breakdown fields (aim, speed, flashlight, accuracy, strain, etc.)

Error Cases:

- `400 Bad Request` – Calculation failed (e.g., malformed beatmap or inconsistent inputs).
- `503 Service Unavailable` – Beatmap fetch failed.

## GET /available_rulesets

```bash
curl -X GET "http://localhost:5225/available_rulesets"
```

Response: `200 OK` with a JSON object like this:

```json
{
  "has_performance_calculator": [
    "osu",
    "taiko",
    "fruits",
    "mania"
  ],
  "has_difficulty_calculator": [
    "osu",
    "taiko",
    "fruits",
    "mania"
  ],
  "loaded_rulesets": [
    "osu",
    "taiko",
    "fruits",
    "mania"
  ]
}
```

## Curl Examples

```bash
# Difficulty (remote fetch by ID)
curl -X POST "http://localhost:5225/difficulty" \
  -H 'Content-Type: application/json' \
  -d '{"beatmap_id":2226842,"mods":[{"acronym":"HD"}],"ruleset_id":0}'

# Performance (with statistics)
curl -X POST "http://localhost:5225/performance" \
  -H 'Content-Type: application/json' \
  -d '{"beatmap_id":2226842,"ruleset_id":0,"accuracy":0.99,"combo":1200,"mods":[{"acronym":"HD"}],"is_legacy":false,"statistics":{"great":1000,"good":10,"meh":3,"miss":0}}'
```

## Mods Format Notes

`mods` leverages the osu! API `APIMod` model. The most common / minimal viable property is the mod acronym:

```json
{
  "acronym": "HR"
}
```

Advanced mods with settings (e.g., DT with speed change) are not yet surfaced here; if needed you can extend the server
to accept config objects that map to mod settings.

## Local Beatmap Caching

- When `SAVE_BEATMAP_FILES=true`, successful remote fetches (and inline uploads with `beatmap_id`) are written under
  `BEATMAPS_PATH` using `<beatmap_id>.osu`.
- Providing a `checksum` with a cached map triggers integrity verification (MD5). A mismatch forces redownload.

## Troubleshooting

| Issue              | Cause                              | Resolution                                                             |
|--------------------|------------------------------------|------------------------------------------------------------------------|
| 503 on fetch       | Beatmap not reachable              | Verify ID exists, network, or site availability                        |
| 400 on performance | Invalid statistics / internal null | Ensure ruleset matches beatmap & stats not contradictory               |
| 400 on difficulty  | Invalid beatmap / ruleset          | Ensure beatmap is valid & beatmap can be convert into specific ruleset |

