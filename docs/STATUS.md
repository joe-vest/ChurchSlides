# Status

Product: ChurchSlides. Current milestone: **M0 Foundations**.
Last updated: 2026-11-23 (C0.4a done)

## Chunks

| Chunk | Title | Status |
|---|---|---|
| C0.1 | Repository and documentation setup | done |
| C0.2 | Spike A: output and crossfade (one combined spike program) | done: C0.2a shell (System S8, Output S1, Startup S7), C0.2b slides (Slides S2, Memory S6). Ready for G0 once C0.4 has added its tabs |
| C0.3 | Spike B: rich text | done: C0.3a converter, renderer and automatic run, C0.3b chips, paste and undo (Rich text S3 tab). Development-PC figures and quirks below |
| C0.4 | Spike C: audio and timer | in progress: C0.4a audio measurements (Audio S4 tab, `--auto-s4`) done; C0.4b Cues, ear checks and timer next |
| G0 | Owner runs the spike program on the church machine | blocked on C0.4 |
| C1.1 | Domain model and serialization | done (64 tests; see deviations in docs/plans/C1.1.md) |
| C1.2 | Resolution logic (review gate), split C1.2a slide resolution / C1.2b copy rules | done: C1.2a slide resolution, C1.2b copy and skip rules (263 domain tests, see docs/plans/C1.2.md) |
| C1.3 | Storage foundation | done: C1.3a Library files, C1.3b local data and drafts (61 storage tests, see docs/plans/C1.3.md) |
| C1.3c | Model and storage changes from the document review | done: C1.3c-1 domain, C1.3c-2 storage (313 domain, 86 storage tests, see docs/plans/C1.3c.md) |
| C1.4 | Library watching and search | done: C1.4a watching, C1.4b search (131 storage tests, see docs/plans/C1.4.md) |
| C1.5 | Navigation engine (review gate) | done: C1.5a sequence (324 domain tests), C1.5b session (35 engine tests, see docs/plans/C1.5.md) |
| C2.1 | Slide renderer and image pipeline | todo |
| C2.2 | Output window and operator shell (G1) | todo |
| C2.3 | Rich-text slide editor | todo |
| C2.4 | Shows, Catalog, categories, search UI | todo |
| C2.5 | Service editor | todo |
| C2.6 | Settings screens (G2 follows) | todo |
| C2.7 | Fix-ups from G2 | todo |
| C3.1 - C9.5 | See DEVELOPMENT-PLAN.md | todo |

## Decisions log (since FRD v0.1)

- Product name: ChurchSlides (Q-010).
- Architecture decisions D-1 to D-9 accepted as recommended in ARCHITECTURE.md section 14.
- Q-A1 to Q-A7 resolved (ARCHITECTURE.md section 15). FRD amended to v0.2 (FRD section 26).
- Audio stops on operator-initiated change of Service Item, not on Actions or Cues (DEC-020).
- Code names for four colliding glossary terms: `TriggeredAction`, `CountdownTimer`, `ActionTrigger`, `SlideTextBox` (UI/FRD wording unchanged). Decided in the C1.1 plan.
- Schema policy: any saved-shape change bumps the schema version. Older app opens newer files read-only. Decided in the C1.1 plan.
- Document review, 2026-11-23 (FRD v0.3, ARCH v0.2, plan v0.2; details in FRD section 26):
  - Presenting starts only with Go Live or Rehearse, which set the Mode (DEC-021, MODE-005). Clicking a slide when not presenting only selects it. Output is black when not presenting.
  - Q-004: screen state is kept through navigation. Clear hides all text. Logo with no logo set shows black.
  - Fixed event order on item changes (ACT-005/006). Action screen commands set rather than toggle, plus `Normal` (ACT-033). Re-clicking the current slide fires nothing.
  - `StartAudio` only at a Show's first displayable slide (AUD-021). Restart from another item goes back to the song.
  - Loop guard pauses Actions for the current item, resuming on the next item (ACT-040).
  - Timers: names follow the Variable rules, starting an Expired Timer restarts it, Timer state isn't saved.
  - Service state file for Mode, check dismissals and the CCLI-016 answer (LIB-013, D-10).
  - Built-in Scripture Category replaces the default Scripture Template setting (CAT-010, D-11).
  - CCLI: Cancel doesn't stop a close or exit. Crash safety net on startup (CCLI-018).
  - The review's file-shape changes amend schema version 1 in place, with no migration, because no real Library exists yet. An exception to the schema policy above, for C1.3c only.
- Owner, 2026-11-23: trim (AUD-003) is a nice-to-have. If spike S4 shows Trim Start can't seek accurately on the church's MP3s (all VBR), drop Trim Start at G0, and drop Trim End too if it turns out hard. The FRD stays unchanged until G0 decides. Seek accuracy still matters for the cue editor's click-to-seek (C4.2), where it is less critical because it isn't live.

## Open FRD questions

| Question | Needed by |
|---|---|
| Q-012 What changing the canvas aspect ratio should do (default: warning only) | C2.6 |
| Q-005 CCLI reporting spreadsheet template | C6.3 |
| Q-006 Scripture reference line scope (default: per-slide range) | C7.2 |
| Q-011 Slide text on the phone remote (default: resolved values, Timers as names) | C8.1 |
| Q-001, Q-002, Q-007 | Defaults in FRD are fine until their chunks |

## Follow-ups (found, out of scope of the chunk that found them)

- C2.5: deleting a Service must also call `ServiceStates.Delete` (a state file with no Service is harmless but untidy).
- C5.2: the Action editor must only offer triggers allowed at the level (`ActionRules.IsAllowed`); storage already refuses the rest.
- C4.2 (cue editor): adding the first Cue to a Show with Skipped marks. FRD 0.3 says the marks and any Service skip lists are then ignored (SVC-025). The cue editor should say so.
- The doc comments on `Clear`, `Black`, `Logo` in `Actions.cs` cite ACT-030..032; they should cite ACT-026..028.
- C2.2: the App backs the session's `findShow` with the in-memory Library, calls `Refresh` on Library changes and editor saves, writes the Mode to the Service state file when presenting starts, and logs `onListenerError`.
- C2.5: the editor must call `EditPolicy.ForSlideEdit`, `ShowCopying.Fork`/`Revert`, `ShowItems.AddReference`/`FillPlaceholder` and `SkipRules` rather than reimplement them.
- C3.3: `ServiceCopying` must implement Save as Service Template conversions (SVC-005): Show Copies back to References, remembered Placeholders back to Placeholders, Scripture Items to stubs.
- Id<T> uses `Activator`/`MakeGenericType` in its converter factory. Fine now, revisit if trimming or AOT is ever wanted.
- First schema bump: add its migration step and an end-to-end test that opens an older file through `FolderLibrary` (C1.3 only tests the chain with fake steps, because every kind is still version 1).
- Reading a file held open exclusively by a sync tool returns Unreadable (listed as a problem until the next event). Measure at G2 before adding read retries.
- C1.4a: the app (C2.2) must call `Monitor.Scan()` at startup, then start `FolderWatcher` and post `Updated` to the UI thread. `SystemClock` is the one allowed `DateTimeOffset.Now` in Domain.
- Optional: add an `AssemblyInfo`/version stamp when packaging (C9.3).
- Markdown lint warnings in docs (cosmetic).
- For the G0 analysis and C4.1/C4.2 (C0.4a, development PC, NAudio 3.3.0; test MP3s made with ffmpeg, one LAME VBR with a Xing header and one CBR, 3 min, not church files). Owner, 2026-11-23: NAudio 3.3 rather than 2.x, and `WasapiPlayer` in place of the obsolete `WasapiOut` (see docs/plans/C0.4.md).
  - Seeks: `Mp3FileReader` lands exactly (0.0 ms) on both files. `MediaFoundationReader` is exact on CBR but 4-427 ms off on the VBR file (mean 228 ms), in both directions. Seek-and-play with it then starts at the wrong place and its position runs 240-340 ms ahead of the sound. Unless the church files disagree, the product uses `Mp3FileReader` (S4-a).
  - `Mp3FileReader` 3.3 quirk: after some seeks (1-2 in 12) a read made straight away returns zeros without moving, for about 50 ms of wall time, then the right audio. In playback this showed once as the output position running 23 ms ahead of the sound after a seek-and-play. C4.1 must guard against it (for example, prime the reader after a seek before handing it to the output) and the test should cover it.
  - The two readers place the same sound 12-14 ms apart (opposite signs on the two files): Cues saved with one reader move by that much under the other (AUD-005). Pick one reader for good.
  - Position: the output's `GetPosition` minus a fixed +1.1 ms (`Mp3FileReader`) or +6.4 ms (`MediaFoundationReader`) is within 0.1-0.3 ms at p95 and 1.8 ms at most, for WaveOut and both WasapiPlayer buffers (S4-b passes widely). The reader's `CurrentTime` runs 65-120 ms ahead (the buffers) and jitters up to 34 ms with WaveOut, so Cues must use the output position.
  - Trim Start and Trim End within 0.2 ms (`Mp3FileReader`) and 5 ms (`MediaFoundationReader`) with every output. Seek-and-play 17-29 ms on average, 52 ms at most. Playing adds under 7 MB working set and under 0.5% CPU.
  - WaveOut doesn't reset its played-sample count when the stream ends by itself (only `Stop` does). C4.1 must stop before replaying or count from the position at Play.
  - NAudio 3.3 changes for C4.1: `WaveOutEvent` is now `WaveOut`, `WasapiOut` and `WasapiLoopbackCapture` are obsolete (`WasapiPlayer`, `WasapiRecorder`), `IMMNotificationClient` is internal (`MMDeviceEnumerator.CreateNotificationClient`), and `Mp3FileReader` opens without scanning the file (open about 0-14 ms).
  - Device change (S4-d, S4-4) not yet tried: it needs a second output device. The Audio tab logs it at G0.
- Spike (before G0): the self-contained publish ships the runtime pack the installed SDK knows (.NET 10.0.3 with SDK 10.0.103), older than the dev PC's runtime (10.0.15). Update the SDK before publishing for G0. The spike's "GPU driver" line reads the first display-adapter registry key only; fine on the church PC's single GPU.
- For the G0 analysis (C0.2b, development PC at 3840×2160): placing a decoded background into an output-size bitmap costs far more than decoding it (about 120 ms against 7-28 ms), and UI-thread pre-render dropped about 2 frames per 300 ms crossfade against 0.6 on the render thread. If the church figures agree, C2.1 could draw the decoded background directly instead of rasterizing it, and pre-render on the render thread. The spike ignores EXIF orientation.
- For the G0 analysis and C2.1/C2.3 (C0.3, development PC; `--auto-s3`). S3-a: all samples and 500 random documents round-trip unchanged with both chip designs; all scripted edits give the expected model. S3-e: unset properties stay unset. S3-b: every sample breaks lines the same except the long one, where the editor wraps one word early. The editor lays out about 5 px narrower than its box (probably the caret). Hymn lines with random word formatting: 12 of 100 break differently at the box width, 2 of 100 with the editor 5 px wider. Ink-pixel differences of 0.5-6.5% are edge smoothing only (the overlays match). S3-d: renderer 2.3 ms per slide on average, keystroke to next frame 27 ms on average (max 50-80 ms). A geometry outline (SLD-025) costs about 19 ms on average and up to 60 ms per slide.
- C2.3 quirk list from C0.3 (WPF `RichTextBox`):
  - Line width: decide at G0 between widening the editor by the measured allowance (check whether it is constant or grows with font size) and drawing the editor's `FlowDocument` (D-4). Leftover differences are on lines that mix font sizes, runs of spaces or lone punctuation.
  - `FormattedText` trims with an ellipsis by default (`Trimming = WordEllipsis`). The renderer must set `None`.
  - The control can't align text vertically. Top padding on the document does it, and `ExtentHeight` only gives the text height with `VerticalScrollBarVisibility` Hidden, not Disabled.
  - The control queues typed text and inserts it at Background priority. A raised `TextInput` event is ignored. Tests type with `TextCompositionManager.StartComposition` and then let the dispatcher run.
  - The control merges neighbouring runs whose formatting values are equal (brushes compare by value). Two tagged-run tokens side by side become one run, and `ClearAllProperties` (which also clears the chip shading) merges a token into the text beside it. Fix in the spike: the converter reads `{{...}}` markup in any run as tokens, a repair step runs after each change, and Clear formatting clears only the five properties.
  - Range formatting (`ApplyPropertyValue`) skips `InlineUIContainer`, so container chips must be formatted directly.
  - The control's own copy (XAML) loses the token tag. Tagged-run tokens survive only as markup, container chips vanish, and a pasted copy gets explicit formatting. The spike copies its own model fragment to the clipboard and pastes by editing the model (slice and splice by offset) and reloading the editor. A paste handler can't replace the document straight away (the control is in a change block), so it runs on the dispatcher.
  - Container chips make lines taller (148 against 119 px on the token sample), so the editor stops matching the Output. A long token can't wrap, and text typed after one doesn't carry on its formatting. Tagged-run chips match the renderer but need the guards (caret, typing beside, repair). Pick at G0 (S3-2, S3-3).
  - A Word-like paste with "keep five" puts Calibri at 11 pt (0.042 of canvas height) on every run, so Template defaults stop applying (TPL-024). The owner picks the paste mode at G0 (S3-1).
  - Undo by snapshot on typing pauses (D-2) restores both the model and the caret offset.
  - Domain needs a `RichText` normalise step (merge neighbouring runs with equal formatting, a lone empty paragraph is Empty, token markup in a text run becomes a token). The spike has it as `FlowConverter.Normalize` and `Merge`.

## Known issues

None.
