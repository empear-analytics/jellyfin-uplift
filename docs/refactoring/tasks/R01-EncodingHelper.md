# R01 - refactor `MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **1.00** / 10 (Red) |
| Exit target | **10.00** |
| File size | 8093 lines (4226 lines of code) |
| Git churn | 616 commits |
| Part | encoding (`MediaBrowser.Controller/MediaEncoding`) |
| Recipes needed | E, G, P |
| Smells without a recipe | Constructor Over-Injection, Excess Number of Function Arguments, Low Cohesion, Primitive Obsession |
| Suitable for | strongest available agent |
| Mutation score (Stryker / Mull) | 53% / 54% |

## 1. Read these first

- [`../PLAYBOOK.md`](../PLAYBOOK.md) - the only transformations you are allowed to apply.
- [`../README.md`](../README.md) - campaign rules and the definition of done.

## 2. Record the baseline before touching anything

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs
```

The score must read 1.00. If it does not, this file changed after the task was
written - stop and report that instead of proceeding.

## 3. What CodeScene flags here

| Smell | Severity (1-3) | Recipe |
| --- | --- | --- |
| Low Cohesion | 3 | none - report it |
| Brain Method | 3 | G, E, P |
| Bumpy Road Ahead | 3 | E |
| Deep, Nested Complexity | 3 | G |
| Overall Code Complexity | 2 | follows the functions |
| Complex Method | 2 | G, E, P |
| Complex Conditional | 2 | P |
| Excess Number of Function Arguments | 2 | none - report it |
| Constructor Over-Injection | 2 | none - report it |
| Primitive Obsession | 2 | none - report it |

## 4. What must not change here

- This file is in a **plugin-facing package**. Public and protected members are plugin API:
  do not change their names, signatures or accessibility. New members you add are `private`.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetInputVideoHwaccelArgs` | 1017 | 64 | 4 | 10 | 6 |
| 2 | `GetAmdVaapiFullVidFiltersPrefered` | 5373 | 58 | 5 | 8 | 10 |
| 3 | `GetHardwareVideoDecoder` | 6501 | 36 | 4 | 2 | 9 |
| 4 | `GetHwaccelType` | 6660 | - | 4 | 5 | 9 |
| 5 | `GetVideoQualityParam` | 2116 | 95 | - | 8 | 19 |
| 6 | `GetIntelQsvDx11VidFiltersPrefered` | 4514 | 85 | - | 6 | 4 |
| 7 | `GetEncoderParam` | 1726 | 80 | - | 8 | 8 |
| 8 | `GetIntelQsvVaapiVidFiltersPrefered` | 4806 | 79 | - | 7 | 4 |
| 9 | `CanStreamCopyVideo` | 2426 | 74 | - | 10 | 27 |
| 10 | `GetRkmppVidFiltersPrefered` | 6051 | 71 | - | 6 | 11 |
| 11 | `GetIntelVaapiFullVidFiltersPrefered` | 5137 | 62 | - | 6 | 2 |
| 12 | `GetVaapiLimitedVidFiltersPrefered` | 5622 | 53 | - | 7 | 3 |
| 13 | `GetAmdDx11VidFiltersPrefered` | 4270 | 45 | - | 5 | 4 |
| 14 | `GetNvidiaVidFiltersPrefered` | 4060 | 43 | - | 5 | 5 |
| 15 | `GetQsvHwVidDecoder` | 6813 | 32 | - | 3 | 2 |
| 16 | `GetAudioBitrateParam` | 2857 | 26 | - | 2 | 6 |
| 17 | `GetVideoProcessingFilterParam` | 6304 | 26 | - | 2 | 2 |
| 18 | `GetVideotoolboxVidDecoder` | 7098 | 26 | - | 3 | 2 |
| 19 | `GetNvdecVidDecoder` | 6896 | 24 | - | 3 | 2 |
| 20 | `GetProgressiveAudioFullCommandLine` | 7911 | 21 | - | 2 | 2 |
| 21 | `GetHwTonemapFilter` | 3741 | 20 | - | 2 | 2 |
| 22 | `GetProgressiveVideoArguments` | 7770 | 20 | - | 2 | 4 |
| 23 | `GetVideoBitrateParam` | 1635 | 20 | - | 2 | 6 |
| 24 | `GetNumAudioChannelsParam` | 3008 | 17 | - | 2 | 3 |
| 25 | `TryStreamCopy` | 7281 | 15 | - | 2 | 2 |
| 26 | `GetVideoBitrateParamValue` | 2737 | 13 | - | 2 | 2 |
| 27 | `GetAppleVidFiltersPreferred` | 5858 | 32 | - | 4 | - |
| 28 | `GetInputModifier` | 7362 | 31 | - | - | 6 |
| 29 | `GetRkmppVidDecoder` | 7163 | 29 | - | 2 | - |
| 30 | `GetVaapiVidDecoder` | 7025 | 27 | - | 3 | - |
| 31 | `AttachMediaSourceInfo` | 7495 | 23 | - | - | 7 |
| 32 | `GetAudioVbrModeParam` | 2907 | 23 | - | 3 | - |
| 33 | `GetHlsVideoKeyFrameArguments` | 2031 | 21 | - | - | 14 |
| 34 | `GetBitStreamArgs` | 1496 | 20 | - | 2 | - |
| 35 | `GetVaapiVidFilterChain` | 5076 | 20 | - | - | 6 |
| 36 | `GetHwDecoderName` | 6602 | 19 | - | - | 6 |
| 37 | `GetMapArgs` | 3129 | 19 | - | 2 | - |
| 38 | `GetAmfVidDecoder` | 6969 | 17 | - | 2 | - |
| 39 | `GetAudioStreamCopyFailureReasons` | 2665 | 17 | - | - | 10 |
| 40 | `GetInputArgument` | 1251 | 15 | - | 3 | - |
| 41 | `GetIntelVidFilterChain` | 4468 | 15 | - | - | 4 |
| 42 | `GetProgressiveVideoAudioArguments` | 7857 | 15 | - | - | 2 |
| 43 | `GetHwScaleFilter` | 3381 | 14 | - | - | 2 |
| 44 | `GetSwScaleFilter` | 3516 | 14 | - | 2 | - |
| 45 | `IsHwTonemapAvailable` | 359 | 14 | - | - | 4 |
| 46 | `GetVideoColorBitDepth` | 6457 | 13 | - | - | 7 |
| 47 | `GetAudioFilterParam` | 2961 | 12 | - | - | 3 |
| 48 | `NormalizeTranscodingLevel` | 1894 | 12 | - | 3 | - |
| 49 | `IsScaleRatioSupported` | 3339 | 11 | - | - | 8 |
| 50 | `GetBitDepthCompensatedBitrate` | 2715 | 10 | - | - | 3 |
| 51 | `GetGraphicalSubPreProcessFilters` | 3431 | 10 | - | - | 6 |
| 52 | `GetRkmppVidFilterChain` | 6019 | 10 | - | - | 3 |
| 53 | `IsIntelVppTonemapAvailable` | 407 | 10 | - | - | 4 |
| 54 | `GetAmdVidFilterChain` | 4242 | 9 | - | - | 3 |
| 55 | `GetCopiedAudioTrimBsf` | 1600 | 9 | - | - | 5 |
| 56 | `GetFixedSwScaleFilter` | 3627 | 9 | - | 2 | - |
| 57 | `GetGraphicalSubCanvasSize` | 984 | 9 | - | - | 6 |
| 58 | `IsVideoToolboxTonemapAvailable` | 431 | 9 | - | - | 2 |
| 59 | `GetInputFormat` | 548 | 20 | - | - | - |
| 60 | `GetSwVidFilterChain` | 3912 | 20 | - | - | - |
| 61 | `GetHwDeinterlaceFilter` | 3685 | 19 | - | - | - |
| 62 | `GetLibplaceboFilter` | 3822 | 17 | - | - | - |
| 63 | `ShouldRemoveDynamicHdrMetadata` | 1424 | 15 | - | - | - |
| 64 | `GetFixedOutputSize` | 3297 | 13 | - | - | - |
| 65 | `GetAudioEncoder` | 768 | 11 | - | - | - |
| 66 | `GetTextSubtitlesFilter` | 1943 | 10 | - | - | - |
| 67 | `GetVideoSyncOption` | 8065 | 10 | - | - | - |
| 68 | `IsVideoStreamHevcRext` | 457 | 10 | - | - | - |
| 69 | `IsVaapiFullSupported` | 283 | 9 | - | - | - |
| 70 | `GetAppleVidFilterChain` | 5847 | - | - | - | 3 |
| 71 | `GetAudioBitStreamArguments` | 1579 | - | - | - | 4 |
| 72 | `GetH26xOrAv1Encoder` | 233 | - | - | - | 3 |
| 73 | `GetMjpegEncoder` | 259 | - | - | - | 3 |
| 74 | `GetNvidiaVidFilterChain` | 4049 | - | - | - | 3 |
| 75 | `GetSubtitleStreamIndexForFfmpeg` | 8031 | - | - | - | 2 |
| 76 | `GetVideoBitrateScaleFactor` | 2802 | - | - | - | 2 |
| 77 | `IsSwTonemapAvailable` | 349 | - | - | - | 2 |
| 78 | `EncodingHelper` | 162 | - | - | - | - |
| 79 | `GetAlphaSrcFilter` | 3482 | - | - | - | - |
| 80 | `GetVaapiDeviceArgs` | 916 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetInputVideoHwaccelArgs` (line 1017)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 10 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 6 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetInputVideoHwaccelArgs`

### Step 2: `GetAmdVaapiFullVidFiltersPrefered` (line 5373)

- **Recipe G (guard clauses)** - nesting is 5, target is under 4.
- **Recipe E (extract function)** - 8 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 10 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetAmdVaapiFullVidFiltersPrefered`

### Step 3: `GetHardwareVideoDecoder` (line 6501)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 9 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetHardwareVideoDecoder`

### Step 4: `GetHwaccelType` (line 6660)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 5 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 9 complex conditional expression(s).
- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetHwaccelType`

### Step 5: `GetVideoQualityParam` (line 2116)

- **Recipe E (extract function)** - 8 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 19 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetVideoQualityParam`

### Step 6: `GetIntelQsvDx11VidFiltersPrefered` (line 4514)

- **Recipe E (extract function)** - 6 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 4 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetIntelQsvDx11VidFiltersPrefered`

### Step 7: `GetEncoderParam` (line 1726)

- **Recipe E (extract function)** - 8 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 8 complex conditional expression(s).
- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetEncoderParam`

### Step 8: `GetIntelQsvVaapiVidFiltersPrefered` (line 4806)

- **Recipe E (extract function)** - 7 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 4 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetIntelQsvVaapiVidFiltersPrefered`

### Step 9: `CanStreamCopyVideo` (line 2426)

- **Recipe E (extract function)** - 10 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 27 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify CanStreamCopyVideo`

### Step 10: `GetRkmppVidFiltersPrefered` (line 6051)

- **Recipe E (extract function)** - 6 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 11 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetRkmppVidFiltersPrefered`

### Step 11: `GetIntelVaapiFullVidFiltersPrefered` (line 5137)

- **Recipe E (extract function)** - 6 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetIntelVaapiFullVidFiltersPrefered`

### Step 12: `GetVaapiLimitedVidFiltersPrefered` (line 5622)

- **Recipe E (extract function)** - 7 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetVaapiLimitedVidFiltersPrefered`

### Step 13: `GetAmdDx11VidFiltersPrefered` (line 4270)

- **Recipe E (extract function)** - 5 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 4 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetAmdDx11VidFiltersPrefered`

### Step 14: `GetNvidiaVidFiltersPrefered` (line 4060)

- **Recipe E (extract function)** - 5 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 5 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetNvidiaVidFiltersPrefered`

### Step 15: `GetQsvHwVidDecoder` (line 6813)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetQsvHwVidDecoder`

### Step 16: `GetAudioBitrateParam` (line 2857)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 6 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetAudioBitrateParam`

### Step 17: `GetVideoProcessingFilterParam` (line 6304)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetVideoProcessingFilterParam`

### Step 18: `GetVideotoolboxVidDecoder` (line 7098)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetVideotoolboxVidDecoder`

### Step 19: `GetNvdecVidDecoder` (line 6896)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetNvdecVidDecoder`

### Step 20: `GetProgressiveAudioFullCommandLine` (line 7911)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetProgressiveAudioFullCommandLine`

### Step 21: `GetHwTonemapFilter` (line 3741)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetHwTonemapFilter`

### Step 22: `GetProgressiveVideoArguments` (line 7770)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 4 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetProgressiveVideoArguments`

### Step 23: `GetVideoBitrateParam` (line 1635)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 6 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetVideoBitrateParam`

### Step 24: `GetNumAudioChannelsParam` (line 3008)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetNumAudioChannelsParam`

### Step 25: `TryStreamCopy` (line 7281)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify TryStreamCopy`

### Step 26: `GetVideoBitrateParamValue` (line 2737)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetVideoBitrateParamValue`

### Step 27: `GetAppleVidFiltersPreferred` (line 5858)

- **Recipe E (extract function)** - 4 bumps; each bump is a missing method.

Commit message: `refactor(EncodingHelper): simplify GetAppleVidFiltersPreferred`

### Step 28: `GetInputModifier` (line 7362)

- **Recipe P (named predicate)** - 6 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetInputModifier`

### Step 29: `GetRkmppVidDecoder` (line 7163)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(EncodingHelper): simplify GetRkmppVidDecoder`

### Step 30: `GetVaapiVidDecoder` (line 7025)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.

Commit message: `refactor(EncodingHelper): simplify GetVaapiVidDecoder`

### Step 31: `AttachMediaSourceInfo` (line 7495)

- **Recipe P (named predicate)** - 7 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify AttachMediaSourceInfo`

### Step 32: `GetAudioVbrModeParam` (line 2907)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.

Commit message: `refactor(EncodingHelper): simplify GetAudioVbrModeParam`

### Step 33: `GetHlsVideoKeyFrameArguments` (line 2031)

- **Recipe P (named predicate)** - 14 complex conditional expression(s).
- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetHlsVideoKeyFrameArguments`

### Step 34: `GetBitStreamArgs` (line 1496)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(EncodingHelper): simplify GetBitStreamArgs`

### Step 35: `GetVaapiVidFilterChain` (line 5076)

- **Recipe P (named predicate)** - 6 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetVaapiVidFilterChain`

### Step 36: `GetHwDecoderName` (line 6602)

- **Recipe P (named predicate)** - 6 complex conditional expression(s).
- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetHwDecoderName`

### Step 37: `GetMapArgs` (line 3129)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(EncodingHelper): simplify GetMapArgs`

### Step 38: `GetAmfVidDecoder` (line 6969)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(EncodingHelper): simplify GetAmfVidDecoder`

### Step 39: `GetAudioStreamCopyFailureReasons` (line 2665)

- **Recipe P (named predicate)** - 10 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetAudioStreamCopyFailureReasons`

### Step 40: `GetInputArgument` (line 1251)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.

Commit message: `refactor(EncodingHelper): simplify GetInputArgument`

### Step 41: `GetIntelVidFilterChain` (line 4468)

- **Recipe P (named predicate)** - 4 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetIntelVidFilterChain`

### Step 42: `GetProgressiveVideoAudioArguments` (line 7857)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetProgressiveVideoAudioArguments`

### Step 43: `GetHwScaleFilter` (line 3381)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- *Excess Number of Function Arguments* (10 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetHwScaleFilter`

### Step 44: `GetSwScaleFilter` (line 3516)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- *Excess Number of Function Arguments* (10 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetSwScaleFilter`

### Step 45: `IsHwTonemapAvailable` (line 359)

- **Recipe P (named predicate)** - 4 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify IsHwTonemapAvailable`

### Step 46: `GetVideoColorBitDepth` (line 6457)

- **Recipe P (named predicate)** - 7 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetVideoColorBitDepth`

### Step 47: `GetAudioFilterParam` (line 2961)

- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetAudioFilterParam`

### Step 48: `NormalizeTranscodingLevel` (line 1894)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.

Commit message: `refactor(EncodingHelper): simplify NormalizeTranscodingLevel`

### Step 49: `IsScaleRatioSupported` (line 3339)

- **Recipe P (named predicate)** - 8 complex conditional expression(s).
- *Excess Number of Function Arguments* (7 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify IsScaleRatioSupported`

### Step 50: `GetBitDepthCompensatedBitrate` (line 2715)

- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetBitDepthCompensatedBitrate`

### Step 51: `GetGraphicalSubPreProcessFilters` (line 3431)

- **Recipe P (named predicate)** - 6 complex conditional expression(s).
- *Excess Number of Function Arguments* (8 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetGraphicalSubPreProcessFilters`

### Step 52: `GetRkmppVidFilterChain` (line 6019)

- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetRkmppVidFilterChain`

### Step 53: `IsIntelVppTonemapAvailable` (line 407)

- **Recipe P (named predicate)** - 4 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify IsIntelVppTonemapAvailable`

### Step 54: `GetAmdVidFilterChain` (line 4242)

- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetAmdVidFilterChain`

### Step 55: `GetCopiedAudioTrimBsf` (line 1600)

- **Recipe P (named predicate)** - 5 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetCopiedAudioTrimBsf`

### Step 56: `GetFixedSwScaleFilter` (line 3627)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.

Commit message: `refactor(EncodingHelper): simplify GetFixedSwScaleFilter`

### Step 57: `GetGraphicalSubCanvasSize` (line 984)

- **Recipe P (named predicate)** - 6 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetGraphicalSubCanvasSize`

### Step 58: `IsVideoToolboxTonemapAvailable` (line 431)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify IsVideoToolboxTonemapAvailable`

### Step 59: `GetInputFormat` (line 548)

- **Recipes G, E, P** - cyclomatic complexity 20; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetInputFormat`

### Step 60: `GetSwVidFilterChain` (line 3912)

- **Recipes G, E, P** - cyclomatic complexity 20; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetSwVidFilterChain`

### Step 61: `GetHwDeinterlaceFilter` (line 3685)

- **Recipes G, E, P** - cyclomatic complexity 19; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetHwDeinterlaceFilter`

### Step 62: `GetLibplaceboFilter` (line 3822)

- **Recipes G, E, P** - cyclomatic complexity 17; reduce it with the recipes above as they apply.
- *Excess Number of Function Arguments* (10 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetLibplaceboFilter`

### Step 63: `ShouldRemoveDynamicHdrMetadata` (line 1424)

- **Recipes G, E, P** - cyclomatic complexity 15; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify ShouldRemoveDynamicHdrMetadata`

### Step 64: `GetFixedOutputSize` (line 3297)

- **Recipes G, E, P** - cyclomatic complexity 13; reduce it with the recipes above as they apply.
- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetFixedOutputSize`

### Step 65: `GetAudioEncoder` (line 768)

- **Recipes G, E, P** - cyclomatic complexity 11; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetAudioEncoder`

### Step 66: `GetTextSubtitlesFilter` (line 1943)

- **Recipes G, E, P** - cyclomatic complexity 10; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetTextSubtitlesFilter`

### Step 67: `GetVideoSyncOption` (line 8065)

- **Recipes G, E, P** - cyclomatic complexity 10; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetVideoSyncOption`

### Step 68: `IsVideoStreamHevcRext` (line 457)

- **Recipes G, E, P** - cyclomatic complexity 10; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify IsVideoStreamHevcRext`

### Step 69: `IsVaapiFullSupported` (line 283)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify IsVaapiFullSupported`

### Step 70: `GetAppleVidFilterChain` (line 5847)

- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetAppleVidFilterChain`

### Step 71: `GetAudioBitStreamArguments` (line 1579)

- **Recipe P (named predicate)** - 4 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetAudioBitStreamArguments`

### Step 72: `GetH26xOrAv1Encoder` (line 233)

- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetH26xOrAv1Encoder`

### Step 73: `GetMjpegEncoder` (line 259)

- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetMjpegEncoder`

### Step 74: `GetNvidiaVidFilterChain` (line 4049)

- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetNvidiaVidFilterChain`

### Step 75: `GetSubtitleStreamIndexForFfmpeg` (line 8031)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetSubtitleStreamIndexForFfmpeg`

### Step 76: `GetVideoBitrateScaleFactor` (line 2802)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetVideoBitrateScaleFactor`

### Step 77: `IsSwTonemapAvailable` (line 349)

- **Recipe P (named predicate)** - 2 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify IsSwTonemapAvailable`

### Step 78: `EncodingHelper` (line 162)

- *Constructor Over-Injection* - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify EncodingHelper`

### Step 79: `GetAlphaSrcFilter` (line 3482)

- *Excess Number of Function Arguments* (8 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetAlphaSrcFilter`

### Step 80: `GetVaapiDeviceArgs` (line 916)

- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetVaapiDeviceArgs`

### File-level smells

- *Low Cohesion*: no recipe in the playbook. Report it.
- *Overall Code Complexity*: follows the functions
- *Primitive Obsession*: no recipe in the playbook. Report it.

### Final step: re-measure, then stop

Once the score reaches 10.00, **stop**.

## 7. Verification - run all four after EVERY step, before committing it

The tools compare your working tree with `HEAD`, so run them before `git commit`. To
re-check a commit you already made, add `--base HEAD~1`.

**1 - it still builds, warnings included.**

```bash
dotnet build Jellyfin.sln
```

Zero errors, and no new warning in the file you changed.

**2 - the tests that cover this part still pass.**

```bash
dotnet test tests/Jellyfin.Controller.Tests --no-build
dotnet test tests/Jellyfin.Server.Integration.Tests --no-build
```

**3 - no constant was removed or altered.**

```bash
python tools/refactor_guard.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs
```

A `FAIL` here means a literal disappeared or changed value - that is not a legal refactor.
Revert immediately. A `WARN` about *added* literals is normally fine. For Recipe D add
`--dedup`; for Recipe S add `--together` and list the original file and the new ones.

**4 - the metric improved.**

```bash
python tools/codehealth_check.py MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs
```

- `UP`, build passed, tests passed, guard passed: commit and continue.
- `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs` and move to the next method.
- Tests or guard **failed**: revert that step, no exceptions.

## 8. Definition of done

- [ ] `python tools/codehealth_check.py` reports 10.00, or every remaining smell is one the playbook has no recipe for and is listed in the report
- [ ] `dotnet build Jellyfin.sln --no-incremental` succeeds with no new warnings (211 on the base)
- [ ] `dotnet test Jellyfin.sln --no-build` passes in full
- [ ] `python tools/refactor_guard.py --base <start commit> --together MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs <any files split off it>` reports no removed literals
- [ ] Each commit covers one method and uses the message format above
- [ ] Nothing in `tests/` changed: `git diff --stat <start commit> -- tests` is empty

## 9. Report back

Reply with exactly this, filled in:

```
Task: R01 (MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs)
Baseline score: 1.00
Final score:    <x.xx>
Steps completed: <n> of 80
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
