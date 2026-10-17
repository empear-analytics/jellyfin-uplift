# R2-01 - refactor `MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs`

| Field | Value |
| --- | --- |
| Baseline Code Health | **1.73** / 10 (Red) |
| Exit target | **10.00** |
| File size | 8572 lines (4405 lines of code) |
| Git churn | 674 commits |
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

The score must read 1.73. If it does not, this file changed after the task was
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

If Recipe E or P steps score `DOWN` or `WORSE` only because they bring *Number of Functions in a Single
Module*, that is the signal for Recipe S (see the playbook).

## 4. What must not change here

- This file is in a **plugin-facing package**. Public and protected members are plugin API:
  do not change their names, signatures or accessibility. New members you add are `private`.
- Nothing in `tests/` may be modified.

## 5. Target methods, highest leverage first

| # | Method | Line | Cyclomatic | Nesting | Bumps | Complex conditionals |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `GetHardwareVideoDecoder` | 6875 | 25 | 4 | 2 | - |
| 2 | `TryStreamCopy` | 7720 | 15 | - | 2 | 2 |
| 3 | `GetHwaccelType` | 7064 | - | 4 | 5 | - |
| 4 | `GetIntelQsvDx11VidFiltersPrefered` | 4790 | 81 | - | 6 | - |
| 5 | `GetVideoQualityParam` | 2227 | 76 | - | 8 | - |
| 6 | `GetIntelQsvVaapiVidFiltersPrefered` | 5092 | 75 | - | 7 | - |
| 7 | `GetEncoderParam` | 1804 | 72 | - | 8 | - |
| 8 | `GetIntelVaapiFullVidFiltersPrefered` | 5438 | 60 | - | 6 | - |
| 9 | `GetRkmppVidFiltersPrefered` | 6390 | 60 | - | 6 | - |
| 10 | `GetVaapiLimitedVidFiltersPrefered` | 5958 | 51 | - | 7 | - |
| 11 | `CanStreamCopyVideo` | 2551 | 45 | - | 10 | - |
| 12 | `GetAmdDx11VidFiltersPrefered` | 4531 | 41 | - | 5 | - |
| 13 | `GetAmdVaapiFullVidFiltersPrefered` | 5674 | 41 | - | 7 | - |
| 14 | `GetNvidiaVidFiltersPrefered` | 4313 | 38 | - | 5 | - |
| 15 | `GetInputVideoHwaccelArgs` | 1049 | 37 | - | 6 | - |
| 16 | `GetAppleVidFiltersPreferred` | 6199 | 32 | - | 4 | - |
| 17 | `GetQsvHwVidDecoder` | 7237 | 30 | - | 3 | - |
| 18 | `GetRkmppVidDecoder` | 7602 | 29 | - | 2 | - |
| 19 | `GetVaapiVidDecoder` | 7459 | 27 | - | 3 | - |
| 20 | `GetVideoProcessingFilterParam` | 6658 | 24 | - | 2 | - |
| 21 | `GetVideotoolboxVidDecoder` | 7532 | 24 | - | 3 | - |
| 22 | `GetAudioVbrModeParam` | 3122 | 23 | - | 3 | - |
| 23 | `GetNvdecVidDecoder` | 7325 | 22 | - | 3 | - |
| 24 | `GetAudioBitrateParam` | 3067 | 20 | - | 2 | - |
| 25 | `GetBitStreamArgs` | 1563 | 20 | - | 2 | - |
| 26 | `GetMapArgs` | 3354 | 19 | - | 2 | - |
| 27 | `GetProgressiveAudioFullCommandLine` | 8380 | 19 | - | 2 | - |
| 28 | `GetHwTonemapFilter` | 3991 | 18 | - | 2 | - |
| 29 | `GetAmfVidDecoder` | 7403 | 17 | - | 2 | - |
| 30 | `GetProgressiveVideoArguments` | 8229 | 16 | - | 2 | - |
| 31 | `GetInputArgument` | 1318 | 15 | - | 3 | - |
| 32 | `GetNumAudioChannelsParam` | 3228 | 14 | - | 2 | - |
| 33 | `GetSwScaleFilter` | 3766 | 14 | - | 2 | - |
| 34 | `GetVideoBitrateParam` | 1712 | 14 | - | 2 | - |
| 35 | `AppendVaapiHwDeviceArgs` | 1192 | 12 | - | 3 | - |
| 36 | `NormalizeTranscodingLevel` | 1990 | 12 | - | 3 | - |
| 37 | `GetVideoBitrateParamValue` | 2937 | 11 | - | 2 | - |
| 38 | `GetBitDepthCompensatedBitrate` | 2915 | 10 | - | - | 3 |
| 39 | `GetFixedSwScaleFilter` | 3877 | 9 | - | 2 | - |
| 40 | `GetInputModifier` | 7801 | 25 | - | - | - |
| 41 | `GetInputFormat` | 578 | 20 | - | - | - |
| 42 | `GetSwVidFilterChain` | 4160 | 20 | - | - | - |
| 43 | `GetHwDeinterlaceFilter` | 3935 | 19 | - | - | - |
| 44 | `GetLibplaceboFilter` | 4070 | 17 | - | - | - |
| 45 | `AttachMediaSourceInfo` | 7944 | 16 | - | - | - |
| 46 | `ShouldRemoveDynamicHdrMetadata` | 1491 | 15 | - | - | - |
| 47 | `GetVaapiVidFilterChain` | 5367 | 14 | - | - | - |
| 48 | `GetFixedOutputSize` | 3522 | 13 | - | - | - |
| 49 | `GetHwDecoderName` | 6991 | 13 | - | - | - |
| 50 | `GetProgressiveVideoAudioArguments` | 8326 | 13 | - | - | - |
| 51 | `GetHwScaleFilter` | 3616 | 12 | - | - | - |
| 52 | `GetAudioEncoder` | 798 | 11 | - | - | - |
| 53 | `GetIntelVidFilterChain` | 4739 | 11 | - | - | - |
| 54 | `IsGopOnlyKeyFrameEncoder` | 2189 | 11 | - | - | - |
| 55 | `GetTextSubtitlesFilter` | 2039 | 10 | - | - | - |
| 56 | `GetVideoSyncOption` | 8544 | 10 | - | - | - |
| 57 | `IsHwTonemapAvailable` | 364 | 10 | - | - | - |
| 58 | `IsVideoStreamHevcRext` | 487 | 10 | - | - | - |
| 59 | `GetAudioFilterParam` | 3176 | 9 | - | - | - |
| 60 | `IsIncompatibleRangeType` | 2775 | 9 | - | - | - |
| 61 | `IsVaapiFullSupported` | 283 | 9 | - | - | - |
| 62 | `GetH26xOrAv1Encoder` | 233 | - | - | - | 3 |
| 63 | `GetMjpegEncoder` | 259 | - | - | - | 3 |
| 64 | `AppendQsvHwDeviceArgs` | 1260 | - | - | - | - |
| 65 | `EncodingHelper` | 162 | - | - | - | - |
| 66 | `GetAlphaSrcFilter` | 3732 | - | - | - | - |
| 67 | `GetGraphicalSubPreProcessFilters` | 3671 | - | - | - | - |
| 68 | `GetHlsVideoKeyFrameArguments` | 2127 | - | - | - | - |
| 69 | `GetVaapiDeviceArgs` | 946 | - | - | - | - |
| 70 | `HasMissingScaleInput` | 3598 | - | - | - | - |
| 71 | `IsScaleRatioSupported` | 3564 | - | - | - | - |

Thresholds: cyclomatic complexity under 9, nesting depth under 4.

## 6. Steps

Work **one method at a time, in the order above**. After each method run the verification in
section 7. Do not batch several methods into one change.

### Step 1: `GetHardwareVideoDecoder` (line 6875)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 25): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetHardwareVideoDecoder`

### Step 2: `TryStreamCopy` (line 7720)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Recipe P (named predicate)** - 2 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 15): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify TryStreamCopy`

### Step 3: `GetHwaccelType` (line 7064)

- **Recipe G (guard clauses)** - nesting is 4, target is under 4.
- **Recipe E (extract function)** - 5 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity None): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetHwaccelType`

### Step 4: `GetIntelQsvDx11VidFiltersPrefered` (line 4790)

- **Recipe E (extract function)** - 6 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 81): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetIntelQsvDx11VidFiltersPrefered`

### Step 5: `GetVideoQualityParam` (line 2227)

- **Recipe E (extract function)** - 8 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 76): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetVideoQualityParam`

### Step 6: `GetIntelQsvVaapiVidFiltersPrefered` (line 5092)

- **Recipe E (extract function)** - 7 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 75): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetIntelQsvVaapiVidFiltersPrefered`

### Step 7: `GetEncoderParam` (line 1804)

- **Recipe E (extract function)** - 8 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 72): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetEncoderParam`

### Step 8: `GetIntelVaapiFullVidFiltersPrefered` (line 5438)

- **Recipe E (extract function)** - 6 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 60): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetIntelVaapiFullVidFiltersPrefered`

### Step 9: `GetRkmppVidFiltersPrefered` (line 6390)

- **Recipe E (extract function)** - 6 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 60): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetRkmppVidFiltersPrefered`

### Step 10: `GetVaapiLimitedVidFiltersPrefered` (line 5958)

- **Recipe E (extract function)** - 7 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 51): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetVaapiLimitedVidFiltersPrefered`

### Step 11: `CanStreamCopyVideo` (line 2551)

- **Recipe E (extract function)** - 10 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 45): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify CanStreamCopyVideo`

### Step 12: `GetAmdDx11VidFiltersPrefered` (line 4531)

- **Recipe E (extract function)** - 5 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 41): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetAmdDx11VidFiltersPrefered`

### Step 13: `GetAmdVaapiFullVidFiltersPrefered` (line 5674)

- **Recipe E (extract function)** - 7 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 41): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetAmdVaapiFullVidFiltersPrefered`

### Step 14: `GetNvidiaVidFiltersPrefered` (line 4313)

- **Recipe E (extract function)** - 5 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 38): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetNvidiaVidFiltersPrefered`

### Step 15: `GetInputVideoHwaccelArgs` (line 1049)

- **Recipe E (extract function)** - 6 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 37): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetInputVideoHwaccelArgs`

### Step 16: `GetAppleVidFiltersPreferred` (line 6199)

- **Recipe E (extract function)** - 4 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 32): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetAppleVidFiltersPreferred`

### Step 17: `GetQsvHwVidDecoder` (line 7237)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 30): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetQsvHwVidDecoder`

### Step 18: `GetRkmppVidDecoder` (line 7602)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 29): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetRkmppVidDecoder`

### Step 19: `GetVaapiVidDecoder` (line 7459)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 27): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetVaapiVidDecoder`

### Step 20: `GetVideoProcessingFilterParam` (line 6658)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 24): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetVideoProcessingFilterParam`

### Step 21: `GetVideotoolboxVidDecoder` (line 7532)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 24): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetVideotoolboxVidDecoder`

### Step 22: `GetAudioVbrModeParam` (line 3122)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 23): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetAudioVbrModeParam`

### Step 23: `GetNvdecVidDecoder` (line 7325)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 22): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetNvdecVidDecoder`

### Step 24: `GetAudioBitrateParam` (line 3067)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 20): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetAudioBitrateParam`

### Step 25: `GetBitStreamArgs` (line 1563)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 20): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetBitStreamArgs`

### Step 26: `GetMapArgs` (line 3354)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 19): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetMapArgs`

### Step 27: `GetProgressiveAudioFullCommandLine` (line 8380)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 19): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetProgressiveAudioFullCommandLine`

### Step 28: `GetHwTonemapFilter` (line 3991)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 18): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetHwTonemapFilter`

### Step 29: `GetAmfVidDecoder` (line 7403)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 17): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetAmfVidDecoder`

### Step 30: `GetProgressiveVideoArguments` (line 8229)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 16): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetProgressiveVideoArguments`

### Step 31: `GetInputArgument` (line 1318)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 15): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetInputArgument`

### Step 32: `GetNumAudioChannelsParam` (line 3228)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 14): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetNumAudioChannelsParam`

### Step 33: `GetSwScaleFilter` (line 3766)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 14): Recipes G, E, P as they apply, one recipe per commit.
- *Excess Number of Function Arguments* (10 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetSwScaleFilter`

### Step 34: `GetVideoBitrateParam` (line 1712)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 14): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetVideoBitrateParam`

### Step 35: `AppendVaapiHwDeviceArgs` (line 1192)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 12): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify AppendVaapiHwDeviceArgs`

### Step 36: `NormalizeTranscodingLevel` (line 1990)

- **Recipe E (extract function)** - 3 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 12): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify NormalizeTranscodingLevel`

### Step 37: `GetVideoBitrateParamValue` (line 2937)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 11): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetVideoBitrateParamValue`

### Step 38: `GetBitDepthCompensatedBitrate` (line 2915)

- **Recipe P (named predicate)** - 3 complex conditional expression(s).
- **Then, while Complex Method remains** (cyclomatic complexity 10): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetBitDepthCompensatedBitrate`

### Step 39: `GetFixedSwScaleFilter` (line 3877)

- **Recipe E (extract function)** - 2 bumps; each bump is a missing method.
- **Then, while Complex Method remains** (cyclomatic complexity 9): Recipes G, E, P as they apply, one recipe per commit.

Commit message: `refactor(EncodingHelper): simplify GetFixedSwScaleFilter`

### Step 40: `GetInputModifier` (line 7801)

- **Recipes G, E, P** - cyclomatic complexity 25; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetInputModifier`

### Step 41: `GetInputFormat` (line 578)

- **Recipes G, E, P** - cyclomatic complexity 20; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetInputFormat`

### Step 42: `GetSwVidFilterChain` (line 4160)

- **Recipes G, E, P** - cyclomatic complexity 20; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetSwVidFilterChain`

### Step 43: `GetHwDeinterlaceFilter` (line 3935)

- **Recipes G, E, P** - cyclomatic complexity 19; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetHwDeinterlaceFilter`

### Step 44: `GetLibplaceboFilter` (line 4070)

- **Recipes G, E, P** - cyclomatic complexity 17; reduce it with the recipes above as they apply.
- *Excess Number of Function Arguments* (10 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetLibplaceboFilter`

### Step 45: `AttachMediaSourceInfo` (line 7944)

- **Recipes G, E, P** - cyclomatic complexity 16; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify AttachMediaSourceInfo`

### Step 46: `ShouldRemoveDynamicHdrMetadata` (line 1491)

- **Recipes G, E, P** - cyclomatic complexity 15; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify ShouldRemoveDynamicHdrMetadata`

### Step 47: `GetVaapiVidFilterChain` (line 5367)

- **Recipes G, E, P** - cyclomatic complexity 14; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetVaapiVidFilterChain`

### Step 48: `GetFixedOutputSize` (line 3522)

- **Recipes G, E, P** - cyclomatic complexity 13; reduce it with the recipes above as they apply.
- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetFixedOutputSize`

### Step 49: `GetHwDecoderName` (line 6991)

- **Recipes G, E, P** - cyclomatic complexity 13; reduce it with the recipes above as they apply.
- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetHwDecoderName`

### Step 50: `GetProgressiveVideoAudioArguments` (line 8326)

- **Recipes G, E, P** - cyclomatic complexity 13; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetProgressiveVideoAudioArguments`

### Step 51: `GetHwScaleFilter` (line 3616)

- **Recipes G, E, P** - cyclomatic complexity 12; reduce it with the recipes above as they apply.
- *Excess Number of Function Arguments* (10 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetHwScaleFilter`

### Step 52: `GetAudioEncoder` (line 798)

- **Recipes G, E, P** - cyclomatic complexity 11; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetAudioEncoder`

### Step 53: `GetIntelVidFilterChain` (line 4739)

- **Recipes G, E, P** - cyclomatic complexity 11; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetIntelVidFilterChain`

### Step 54: `IsGopOnlyKeyFrameEncoder` (line 2189)

- **Recipes G, E, P** - cyclomatic complexity 11; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify IsGopOnlyKeyFrameEncoder`

### Step 55: `GetTextSubtitlesFilter` (line 2039)

- **Recipes G, E, P** - cyclomatic complexity 10; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetTextSubtitlesFilter`

### Step 56: `GetVideoSyncOption` (line 8544)

- **Recipes G, E, P** - cyclomatic complexity 10; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetVideoSyncOption`

### Step 57: `IsHwTonemapAvailable` (line 364)

- **Recipes G, E, P** - cyclomatic complexity 10; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify IsHwTonemapAvailable`

### Step 58: `IsVideoStreamHevcRext` (line 487)

- **Recipes G, E, P** - cyclomatic complexity 10; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify IsVideoStreamHevcRext`

### Step 59: `GetAudioFilterParam` (line 3176)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify GetAudioFilterParam`

### Step 60: `IsIncompatibleRangeType` (line 2775)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.
- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify IsIncompatibleRangeType`

### Step 61: `IsVaapiFullSupported` (line 283)

- **Recipes G, E, P** - cyclomatic complexity 9; reduce it with the recipes above as they apply.

Commit message: `refactor(EncodingHelper): simplify IsVaapiFullSupported`

### Step 62: `GetH26xOrAv1Encoder` (line 233)

- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetH26xOrAv1Encoder`

### Step 63: `GetMjpegEncoder` (line 259)

- **Recipe P (named predicate)** - 3 complex conditional expression(s).

Commit message: `refactor(EncodingHelper): simplify GetMjpegEncoder`

### Step 64: `AppendQsvHwDeviceArgs` (line 1260)

- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify AppendQsvHwDeviceArgs`

### Step 65: `EncodingHelper` (line 162)

- *Constructor Over-Injection* - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify EncodingHelper`

### Step 66: `GetAlphaSrcFilter` (line 3732)

- *Excess Number of Function Arguments* (8 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetAlphaSrcFilter`

### Step 67: `GetGraphicalSubPreProcessFilters` (line 3671)

- *Excess Number of Function Arguments* (8 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetGraphicalSubPreProcessFilters`

### Step 68: `GetHlsVideoKeyFrameArguments` (line 2127)

- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetHlsVideoKeyFrameArguments`

### Step 69: `GetVaapiDeviceArgs` (line 946)

- *Excess Number of Function Arguments* (6 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify GetVaapiDeviceArgs`

### Step 70: `HasMissingScaleInput` (line 3598)

- *Excess Number of Function Arguments* (5 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify HasMissingScaleInput`

### Step 71: `IsScaleRatioSupported` (line 3564)

- *Excess Number of Function Arguments* (7 arguments) - **no recipe in the playbook.** Leave it and report it.

Commit message: `refactor(EncodingHelper): simplify IsScaleRatioSupported`

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
- `WORSE`, `FLAT` or `DOWN`: revert that step with `git checkout -- MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs` and move to the next method.
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
Task: R2-01 (MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs)
Baseline score: 1.73
Final score:    <x.xx>
Steps completed: <n> of 71
Steps reverted:  <n>   (list which, and why)
Smells left without a recipe: <list, or NONE>
Build: PASS / FAIL
Tests: PASS / FAIL
Commits: <sha list>
Unsure about: <free text, or NONE>
```
