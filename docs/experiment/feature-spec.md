# Feature: accessibility-aware tracks

**Status:** agreed 2026-10-16. Fixed for both arms of the experiment.

Media files can mark their tracks for accessibility: an audio track can be an *audio description*
for blind and partially sighted viewers, or a *commentary*, and a subtitle track can be *SDH*
(subtitles for the deaf and hard of hearing). Jellyfin reads only some of these marks and acts on
none of them. As a result it can auto-play a director's commentary or an audio-description track,
it drops the marks when transcoding, and it doesn't tell HLS players which subtitles are SDH.

This feature makes Jellyfin recognise the marks, choose default audio tracks with them in mind,
keep them through transcoding, and declare SDH subtitles in HLS master playlists.

## What is already in place

The scaffolding commit adds everything the feature needs outside the code it changes:

- `MediaStream.IsAudioDescription` and `MediaStream.IsCommentary` (both `bool`, default `false`).
  They are stored in the database with the rest of the stream, and are returned to clients
  wherever media streams are, for example in PlaybackInfo.
- The user setting `PreferAudioDescription` (`bool`, default `false`), in `UserConfiguration` and on
  the `User` entity. It is saved and loaded like `PlayDefaultAudioTrack`.

`MediaStream.IsHearingImpaired` already exists and is already read from ffprobe.

The recorded acceptance tests in `tests/*/Acceptance/` leave `IsAudioDescription` and `IsCommentary`
out of their recordings, so they neither require nor block this feature.

## Requirements

The feature has five parts. Each is graded separately.

### R1: recognise audio description and commentary when probing

When ffprobe reports an **audio** stream with:

- `"disposition": { "visual_impaired": 1 }`, set `IsAudioDescription = true`;
- `"disposition": { "comment": 1 }`, set `IsCommentary = true`.

Both may be set on the same stream. Leave both `false` for other stream types and when the
disposition is missing or 0. Nothing else about the probed result changes.

### R2: choose the default audio track with the marks in mind

When Jellyfin picks a default audio track automatically, it first narrows the candidates:

1. **Commentary is never auto-picked.** Commentary tracks are removed from the candidates,
   unless every audio track is a commentary track.
2. **Audio description is auto-picked only on request.**
   - If the user's `PreferAudioDescription` is `false`, audio-description tracks are removed from
     the remaining candidates, unless every remaining track is one.
   - If it is `true`, and at least one remaining audio-description track matches the user's
     preferred audio language, the candidates become the matching audio-description tracks. When
     the user has no preferred language, every audio-description track matches.
   - If it is `true` but no audio-description track matches, the audio-description tracks are
     removed as if the setting were `false`.
3. **The existing rules then pick from the candidates, unchanged:** preferred or original
   language, and the default flag when `PlayDefaultAudioTrack` is on.

This applies wherever the default audio track is chosen automatically, including the
"original language" preference. A track the user chose themselves and Jellyfin remembered is
kept as it is.

### R3: the playback decision doesn't switch to commentary or audio description on its own

The playback decision (`StreamBuilder`) chooses an audio track in two situations, and both change.

1. **Fallback.** When no audio track is named, it uses the first audio track flagged as default,
   otherwise the first audio track. No track is named when neither the request nor the media
   source's `DefaultAudioStreamIndex` names one, and always for audio-only items. The fallback must
   skip commentary and audio-description tracks, unless every audio track is one of those.
2. **Re-selection.** To play a file directly, the decision may switch from the chosen audio track to
   another one the client can play, for example another track in the same language. It may only
   switch to a track with the same `IsCommentary` and `IsAudioDescription` values as the track it
   started from.

When the request names an audio track, that track is used as before.

### R4: keep the marks when transcoding

When the output has an audio stream taken from a source track that is marked, the ffmpeg command
line sets the output audio stream's disposition. This applies whether the audio is re-encoded or
copied, and to progressive and HLS outputs alike:

| Source track | Argument |
|---|---|
| `IsAudioDescription` | `-disposition:a:0 visual_impaired` |
| `IsCommentary` | `-disposition:a:0 comment` |
| both | `-disposition:a:0 visual_impaired+comment` |

Command lines for unmarked tracks don't change.

### R5: declare SDH subtitles in HLS master playlists

In the master playlist, each `#EXT-X-MEDIA:TYPE=SUBTITLES` line for a subtitle stream with
`IsHearingImpaired` ends with:

```
,CHARACTERISTICS="public.accessibility.transcribes-spoken-dialog,public.accessibility.describes-music-and-sound"
```

Lines for other subtitle streams don't change.

## Constraints

- **Existing behaviour stays the same for media without the marks.** The existing tests, including
  the recorded acceptance tests in `tests/*/Acceptance/`, must still pass unchanged.
- **The public API of the plugin packages may only be extended, never changed.**
  These are Jellyfin.Model, Jellyfin.Controller, Jellyfin.Common, Jellyfin.Naming,
  Jellyfin.Data, Jellyfin.Extensions, Jellyfin.Database.Implementations and
  Jellyfin.MediaEncoding.Keyframes.
- **The scaffolding is complete.** No database migration, setting or client-facing property should
  be needed beyond it.

## How it is graded

Acceptance tests that are not part of this repository check each requirement through public APIs
and HTTP. The grade is the share of those tests that pass, together with any regressions in this
repository's own tests.
