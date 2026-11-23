# Status

Product: ChurchSlides. Current milestone: **M0 Foundations**.
Last updated: 2026-11-23 (C1.4a done)

## Chunks

| Chunk | Title | Status |
|---|---|---|
| C0.1 | Repository and documentation setup | done |
| C0.2 | Spike A: output and crossfade (one combined spike program) | todo, next |
| C0.3 | Spike B: rich text | todo |
| C0.4 | Spike C: audio and timer | todo |
| G0 | Owner runs the spike program on the church machine | blocked on C0.2-C0.4 |
| C1.1 | Domain model and serialization | done (64 tests; see deviations in docs/plans/C1.1.md) |
| C1.2 | Resolution logic (review gate), split C1.2a slide resolution / C1.2b copy rules | done: C1.2a slide resolution, C1.2b copy and skip rules (263 domain tests, see docs/plans/C1.2.md) |
| C1.3 | Storage foundation | done: C1.3a Library files, C1.3b local data and drafts (61 storage tests, see docs/plans/C1.3.md) |
| C1.3c | Model and storage changes from the document review | done: C1.3c-1 domain, C1.3c-2 storage (313 domain, 86 storage tests, see docs/plans/C1.3c.md) |
| C1.4 | Library watching and search | C1.4a watching done (114 storage tests); C1.4b search next (docs/plans/C1.4.md) |
| C1.5 | Navigation engine (review gate) | todo |
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

## Open FRD questions

| Question | Needed by |
|---|---|
| Q-012 What changing the canvas aspect ratio should do (default: warning only) | C2.6 |
| Q-005 CCLI reporting spreadsheet template | C6.3 |
| Q-006 Scripture reference line scope (default: per-slide range) | C7.2 |
| Q-011 Slide text on the phone remote (default: resolved values, Timers as names) | C8.1 |
| Q-001, Q-002, Q-007 | Defaults in FRD are fine until their chunks |

## Follow-ups (found, out of scope of the chunk that found them)

- C1.5: navigation must use `SkipRules.InForce(item, show)` for the skip list, never `ShowItem.SkippedSlideIds` directly.
- C2.5: deleting a Service must also call `ServiceStates.Delete` (a state file with no Service is harmless but untidy).
- C5.2: the Action editor must only offer triggers allowed at the level (`ActionRules.IsAllowed`); storage already refuses the rest.
- C4.2 (cue editor): adding the first Cue to a Show with Skipped marks. FRD 0.3 says the marks and any Service skip lists are then ignored (SVC-025). The cue editor should say so.
- C1.5: navigation must honor `ShowItem.SkippedSlideIds` for both reference and copy items (except for cued Shows), and allow a direct jump to a skipped slide (LIVE-017).
- C2.5: the editor must call `EditPolicy.ForSlideEdit`, `ShowCopying.Fork`/`Revert`, `ShowItems.AddReference`/`FillPlaceholder` and `SkipRules` rather than reimplement them.
- C3.3: `ServiceCopying` must implement Save as Service Template conversions (SVC-005): Show Copies back to References, remembered Placeholders back to Placeholders, Scripture Items to stubs.
- Id<T> uses `Activator`/`MakeGenericType` in its converter factory. Fine now, revisit if trimming or AOT is ever wanted.
- First schema bump: add its migration step and an end-to-end test that opens an older file through `FolderLibrary` (C1.3 only tests the chain with fake steps, because every kind is still version 1).
- Reading a file held open exclusively by a sync tool returns Unreadable (listed as a problem until the next event). Measure at G2 before adding read retries.
- C1.4a: the app (C2.2) must call `Monitor.Scan()` at startup, then start `FolderWatcher` and post `Updated` to the UI thread. `SystemClock` is the one allowed `DateTimeOffset.Now` in Domain.
- Optional: add an `AssemblyInfo`/version stamp when packaging (C9.3).
- Markdown lint warnings in docs (cosmetic).

## Known issues

None.
