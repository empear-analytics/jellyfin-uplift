# Test harness for the refactoring campaign

The refactoring of the scope in [scope.json](scope.json) (about 32 kLoC, 47 files) is protected by
acceptance tests pinned to stable boundaries: the public API of the plugin-facing packages
(Jellyfin.Model, Jellyfin.Controller, ...) and the HTTP API. They record what the current code
does and fail when that changes. **The refactoring may not modify these tests.**

The tests deliberately stay off internals, so they do not become write protection: a refactoring
may move, split or rename anything that is not public in a plugin-facing package or visible over
HTTP.

## Suites

All of them live in `tests/*/Acceptance/`.

| Project | Boundary | What is recorded |
|---|---|---|
| Jellyfin.MediaEncoding.Tests | `ProbeResultNormalizer` (public) | MediaInfo for 15 ffprobe samples × 12 JSON variants |
| Jellyfin.Controller.Tests | `EncodingHelper`, `EncodingJobInfo`, `TranscodingJob`, `JobLogger`, `DownMixAlgorithmsHelper` | 856 ffmpeg video command lines over hardware types and option profiles; audio command lines; stream-copy decisions; job bookkeeping, kill timer and progress parsing |
| Jellyfin.Model.Tests | `StreamBuilder`, DLNA profile classes | Playback decisions: 19 device profiles × 3 variants × 33 media sources; normalisation and matching tables |
| Jellyfin.Server.Implementations.Tests | `MediaStreamSelector` via `MediaSourceManager` | Default audio and subtitle choices over stream sets, subtitle modes and preferences |
| Jellyfin.Server.Integration.Tests | HTTP | HLS master, variant and live playlists; segment command lines; PlaybackInfo with real device profiles; audio streams; remote static streams; library services |

The in-memory server replaces two things that are outside the scope:

- **ffmpeg.** `RecordingTranscodeManager` records the command line and starts nothing. For live HLS
  it writes the playlist that ffmpeg would write, built from that command line.
- **Remote media servers.** `UpstreamStub` answers range requests in six ways.

## Running and re-recording

```
dotnet test tests/<project> --filter "FullyQualifiedName~Acceptance"
JELLYFIN_ACCEPTANCE_RECORD=1 dotnet test tests/<project> --filter "FullyQualifiedName~Acceptance"
```

- Recordings are kept per OS, under `Recorded/<suite>/windows/` and `Recorded/<suite>/linux/`,
  because paths and hardware-acceleration paths differ. The Linux recordings are made in WSL.
- A missing recording skips the test rather than failing it.
- Masked in the recordings: the access token, the server root, the media folder, random
  PlaySessionIds and transcode file names. Carriage returns are written as `\r`.

## Mutation testing

- The configs are in `tools/mutation/` (`<part>.json`) and run with Stryker.NET 5, via `run.sh <part>`.
- `profiles.py` reports every score twice:
  - the **Stryker** standard set;
  - a **Mull** profile: Mull's `cxx_default` plus removal of void calls.
- Windows and Linux runs are merged per mutant: a mutant counts as killed if either run kills it.
- The api and library parts use `perTestInIsolation`. Plain per-test coverage misses code that runs
  on the in-memory server's threads.
- Before a run, `dotnet build Jellyfin.sln --no-incremental` must show 0 errors. Some analyzers
  report only in full builds, and Stryker fails on them.

Scores in % of tested mutants:

| Part | Baseline (Stryker / Mull) | With the harness (Stryker / Mull) |
|---|---|---|
| probing | 40.6 / 44.4 | 63.5 / 67.7 |
| encoding | 6.3 / 7.5 | 52.4 / 50.8 |
| dlna | 28.3 / 28.2 | 49.4 / 57.4 |
| library | 37.0 / 36.4 | 54.8 / 54.8 |
| api | 4.8 / 8.0 | 68.8 / 63.3 |
| **scope** | **18.1 / 22.2** | **55.5 / 55.6** |

[mutation-baseline.json](mutation-baseline.json) and [mutation-after.json](mutation-after.json) hold
the per-file figures.

### The 30% floor

Every scope file is at or above 30% in both profiles except three. Testing them through a stable
boundary is not reasonable:

- **TranscodingThrottler.** Every 5 s it compares transcode and download positions. It then pauses
  or resumes ffmpeg by writing keys to the process's stdin. Testing it needs a running ffmpeg
  process and waits of several seconds.
- **TranscodingSegmentCleaner.** Every 20 s it deletes segment files, after a further delay. Testing
  it needs a real transcode in progress and waits of tens of seconds.
- **LiveStreamHelper.** It probes live-TV streams with ffprobe when a stream is opened. Testing it
  needs a live-TV media source provider and ffprobe on a live stream.

### Known surviving mutants

- `TranscodingJob.GetHighestServedSegmentIndexEndingAtOrBefore`: the mutant `index > result` →
  `index >= result` is equivalent, because segment indices are dictionary keys and never repeat.
- Mutants of log-message strings and of log calls survive by design: logs are not part of the
  boundary.

## What the tests freeze

- **The acceptance tests use no internals.** Compiling them without the source projects'
  `InternalsVisibleTo` gives no errors. They depend only on the public API of the plugin-facing
  packages and on HTTP, so everything below those boundaries is free to change.
- **Recorded output is byte-exact.** A refactoring must reproduce ffmpeg command lines, playlists
  and JSON exactly, including whitespace and argument order.
- **The existing upstream unit tests call nine internal members in scope.** The strict rule (existing
  tests are not modified) freezes their names, signatures and owning classes. A refactoring may move
  the body but must keep each member as an entry point:

  | Class | Internal members |
  |---|---|
  | ProbeResultNormalizer | `GetEstimatedAudioBitrate`, `GetFrameRate`, `IsNearSquarePixelSar` |
  | StreamBuilder | `ContainerReasons` |
  | DynamicHlsController | `GetDolbyVisionHevcCodecTag`, `GetSegmentLengthsInternal`, `WaitForActiveTranscodingRequests` |
  | MediaInfoHelper | `GetPublishedLiveStreamPath` |
  | RequestHelpers | `GetUserId` |

  Some upstream tests for LibraryManager and MediaSourceManager also check how many times
  collaborators are called.
- **Decision (2026-10-16): a block is reported, not worked around.** A Code Health issue that can only
  be fixed by changing the frozen public API or a frozen member stays, and the campaign reports why.
  The known case is *Constructor Over-Injection* on `EncodingHelper`: its six-argument public
  constructor belongs to the plugin-facing MediaBrowser.Controller package.

## Upstream-bug candidates

These were found while recording. The tests record the current behaviour, and none of them is fixed.

- `JobLogger` does not parse ffmpeg's padded `size=   256kB` progress field, so the bytes
  transcoded are not reported.
- `TranscodingJob.DecrementActiveRequestCount` can drive the active request count below zero.
- `MediaStreamSelector.SetSubtitleStreamScores`, Always mode: `...ToList() ?? BehaviorOnlyForced(...)`
  never falls back, because `ToList()` never returns null. Unlike `GetDefaultSubtitleStreamIndex`,
  that mode therefore never falls back to forced subtitles. (Reported by the R19 agent during the
  refactoring campaign.)
- In the Default subtitle mode, an external Swedish subtitle is selected in a case where the
  preferences point elsewhere. This needs checking against the intended rules.
- `DynamicHlsHelper.GetMasterPlaylistInternal`: `sdrPlaylistQuery` and `variantQuery` are the same
  dictionary as `playlistQuery`, so the SDR and H.264 compatibility edits leak into the
  adaptive-bitrate variant URLs. (Reported by the R07 agent.)
- `DynamicHlsHelper.GetOutputVideoCodecProfile`: the defaults `profileString ??= "high"` and
  `??= "main"` never apply. (Reported by the R07 agent.)
- `ProbeResultNormalizer.FetchWtvInfo` (now `SetWtvDates` in `ProbeResultNormalizer.Video.cs`):
  `PremiereDate` is parsed from `year`; `premiereDateString` is read and never used. (Reported by
  the R04 agent.)
- `StreamBuilder.ApplyTranscodingConditions` (now in `StreamBuilder.TranscodingConditions.cs`):
  every `condition.Condition == ProfileConditionType.GreaterThanEqual` branch inside the switch is
  unreachable, because the loop skips conditions of that type before the switch. (Reported by the
  R2-02 agent.)
