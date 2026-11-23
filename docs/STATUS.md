# Status

Product: ChurchSlides. Current milestone: **M0 Foundations**.
Last updated: 2026-11-23

## Chunks

| Chunk | Title | Status |
|---|---|---|
| C0.1 | Repository and documentation setup | done |
| C0.2 | Spike A: output and crossfade | todo |
| C0.3 | Spike B: rich text | todo |
| C0.4 | Spike C: audio and timer | todo |
| G0 | Owner runs spikes on church machine | blocked on C0.2-C0.4 |
| C1.1 | Domain model and serialization | done (64 tests; see deviations in docs/plans/C1.1.md) |
| C1.2 | Resolution logic (review gate) | todo |
| C1.3 | Storage foundation | todo |
| C1.4 | Library watching and search | todo |
| C1.5 | Navigation engine (review gate) | todo |
| C2.1 | Slide renderer and image pipeline | todo |
| C2.2 | Output window and operator shell (G1) | todo |
| C2.3 | Rich-text slide editor | todo |
| C2.4 | Shows, Catalog, categories, search UI | todo |
| C2.5 | Service editor (G2) | todo |
| C3.1 - C9.5 | See DEVELOPMENT-PLAN.md | todo |

## Decisions log (since FRD v0.1)

- Product name: ChurchSlides (Q-010).
- Architecture decisions D-1 to D-9 accepted as recommended in ARCHITECTURE.md section 14.
- Q-A1 to Q-A7 resolved (ARCHITECTURE.md section 15). FRD amended to v0.2 (FRD section 26).
- Audio stops on operator-initiated change of Service Item, not on Actions or Cues (DEC-020).
- Code names for four colliding glossary terms: `TriggeredAction`, `CountdownTimer`, `ActionTrigger`, `SlideTextBox` (UI/FRD wording unchanged). Decided in the C1.1 plan.
- Schema policy: any saved-shape change bumps the schema version. Older app opens newer files read-only. Decided in the C1.1 plan.

## Open FRD questions that block chunks

| Question | Needed by |
|---|---|
| Q-004 Black/Clear/Logo vs navigation (current default: keep until toggled) | C1.5 |
| Q-005 CCLI reporting spreadsheet template | C6.3 |
| Q-006 Scripture reference line scope (current default: per-slide range) | C7.2 |
| Q-007, Q-008, Q-009 | Defaults in FRD are fine until their chunks |

## Follow-ups (found, out of scope of the chunk that found them)

- C1.3: reject empty IDs (`Id<T>.IsEmpty`) and empty required names on save, because the model no longer uses `required`.
- C1.3: migrations operate on the JSON before deserializing, and must add any newly required fields to old files.
- C1.3: write files as UTF-8 without BOM using `VersionedFile.Serialize` output as-is.
- Id<T> uses `Activator`/`MakeGenericType` in its converter factory. Fine now, revisit if trimming or AOT is ever wanted.
- Optional: add an `AssemblyInfo`/version stamp when packaging (C9.3).
- Markdown lint warnings in docs (cosmetic).

## Known issues

None.
