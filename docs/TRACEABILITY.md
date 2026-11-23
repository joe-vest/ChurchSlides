# Traceability: FRD requirement to chunk

Generated from docs/FRD.md in C0.1. Update the Status column (todo / done / partial) and add code/test pointers as chunks complete. Chunk IDs refer to docs/DEVELOPMENT-PLAN.md.

| ID | Priority | Chunk | Status |
|---|---|---|---|
| PLT-001 | MUST | C0.2-C0.4 | todo |
| PLT-002 | MUST | C0.2-C0.4 | todo |
| PLT-003 | MUST | C0.2-C0.4 | todo |
| PLT-004 | MUST NOT | C0.2-C0.4 | todo |
| PLT-005 | SHOULD | C9.2 | todo |
| PLT-006 | SHOULD | C9.2 | todo |
| PLT-007 | MUST | C9.2 | todo |
| PLT-008 | MUST | C0.2-C0.4 | todo |
| PLT-009 | MUST | C0.2-C0.4 | todo |
| LIB-001 | MUST | C1.3 | done (C1.3b: LocalSettings.LibraryPath) |
| LIB-002 | MUST | C1.3 | done |
| LIB-003 | MUST | C1.3 | partial (types, file format, no absolute paths in saved files; media copy comes with the first media import, C2.1/C2.4) |
| LIB-004 | MUST | C1.3 | partial (read-only newer files, migration chain with fake steps; end-to-end migration test comes with the first schema bump) |
| LIB-005 | MUST | C1.3 | done |
| LIB-006 | MUST | C1.3, C1.4 | partial (C1.3: save-time hash check; C1.4a: watcher done; Reload / Save as copy flow in editors) |
| LIB-007 | MUST | C1.4 | done (C1.4a, storage level; editors decide what to do with a change) |
| LIB-008 | MUST | C1.3 | done |
| LIB-009 | SHOULD | C1.4 | partial (C1.4a: problems detected; panel in C9.1) |
| LIB-010 | MUST | C1.4 | partial (C1.4a: folder check and Unavailable; prompt in C2.6/C9.1) |
| LIB-011 | SHOULD | C2.1, C6.2 | todo (moved from C1.4) |
| LIB-012 | COULD | backlog | todo |
| CAT-001 | MUST | C2.4 | todo |
| CAT-002 | MUST | C2.4 | todo |
| CAT-003 | MUST | C1.4 | done (C1.4b, storage level; UI in C2.4) |
| CAT-004 | SHOULD | C1.4 | done (C1.4b, storage level; UI in C2.4) |
| CAT-005 | MUST | C1.4 | done (C1.4b, storage level; UI in C2.4) |
| CAT-006 | MUST | C2.4 | todo |
| CAT-007 | MUST | C2.4 | todo |
| CAT-008 | COULD | backlog | todo |
| CAT-009 | COULD | backlog | todo |
| SHOW-001 | MUST | C2.4 | todo |
| SHOW-002 | MUST | C2.4 | todo |
| SHOW-003 | MUST | C2.4 | todo |
| SHOW-004 | MUST | C2.4 | todo |
| SHOW-005 | MUST | C2.4 | todo |
| SHOW-006 | MUST | C2.4 | todo |
| SHOW-007 | MUST | C2.4 | todo |
| SLD-001 | MUST | C1.1 | partial (C1.1: types and file format; behavior in later chunks) |
| SLD-002 | MUST | C2.1 | todo |
| SLD-003 | MUST | C2.1 | todo |
| SLD-004 | SHOULD | C2.6 (after Q-012) | todo |
| SLD-005 | MUST | C1.1 | partial (C1.1: types and file format; behavior in later chunks) |
| SLD-006 | MUST | C2.1 | logic done (C1.2a); UI in a later chunk |
| SLD-007 | MUST | C2.1 | todo |
| SLD-008 | SHOULD | C2.1 | todo |
| SLD-009 | MUST | C1.1 | partial (C1.1: types and file format; behavior in later chunks) |
| SLD-020 | MUST | C2.3 | todo |
| SLD-021 | MUST | C2.3 | todo |
| SLD-022 | MUST | C2.3 | todo |
| SLD-023 | SHOULD | C2.3 | todo |
| SLD-024 | COULD | C2.3 | todo |
| SLD-025 | COULD | backlog | todo |
| SLD-026 | MUST | C2.3 | todo |
| SLD-027 | MUST | C2.3 | todo |
| SLD-028 | SHOULD | C2.3 | todo |
| SLD-029 | COULD | backlog | todo |
| TPL-001 | MUST | C1.2 | todo |
| TPL-002 | MUST | C3.1 | partial (C1.2a: fall-through and reporting; usage warning in C3.1) |
| TPL-003 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| TPL-010 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| TPL-011 | COULD | backlog | todo |
| TPL-012 | MUST | C3.1 | todo |
| TPL-013 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| TPL-020 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| TPL-021 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| TPL-022 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| TPL-023 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| TPL-024 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| TPL-025 | SHOULD | C1.2 | logic done (C1.2a); UI in a later chunk |
| TPL-026 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| TPL-027 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| VAR-001 | MUST | C1.2, C1.3c, C3.2 | logic done (name rule C1.2a; uniqueness checked on save C1.3c); UI in C3.2 |
| VAR-002 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| VAR-003 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| VAR-004 | MUST | C1.2 | logic done (C1.2b); UI in a later chunk |
| VAR-005 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| VAR-006 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| VAR-007 | SHOULD | C3.2 | todo |
| VAR-008 | MUST NOT | C1.2 | todo |
| SVC-001 | MUST | C2.5 | todo |
| SVC-002 | MUST | C2.5 | todo |
| SVC-003 | MUST | C2.5 | partial (C1.2b: duplicate logic; UI in C2.5) |
| SVC-004 | MUST | C2.5 | todo |
| SVC-005 | SHOULD | C3.3 | todo |
| SVC-020 | MUST | C1.2, C2.5 | logic done (C1.2b); UI in a later chunk |
| SVC-021 | MUST | C1.2, C2.5 | logic done (C1.2b); UI in a later chunk |
| SVC-022 | MUST | C1.2, C2.5 | logic done (C1.2b); UI in a later chunk |
| SVC-023 | MUST | C1.2, C2.5 | partial (C1.2b: data kept; marker UI in C2.5) |
| SVC-024 | SHOULD | C2.5 | logic done (C1.2b); UI in a later chunk |
| SVC-025 | MUST | C1.2, C1.3c, C2.5 | logic done (C1.2b; shared `SkipRules.InForce` C1.3c); UI in a later chunk |
| SVC-026 | MUST | C1.2, C1.5, C2.5 | partial (C1.5a: listed as Missing show, passed over); UI and check later |
| SVC-030 | MUST | C3.3 | partial (C1.2b: instantiate logic; UI in C3.3) |
| SVC-031 | MUST | C3.3 | todo |
| SVC-032 | MUST | C3.3 | todo |
| SVC-033 | SHOULD | C3.3 | todo |
| SVC-034 | MUST | C1.5, C3.3 | partial (C1.5a: passed over in the sequence); check in C3.3 |
| SVC-035 | MUST | C3.3 | logic done (C1.2b); UI in a later chunk |
| AUD-001 | MUST | C4.1 | todo |
| AUD-002 | MUST | C4.1 | todo |
| AUD-003 | MUST | C4.1 | todo |
| AUD-004 | MUST | C4.1 | todo |
| AUD-005 | MUST | C4.1 | todo |
| AUD-006 | MUST | C4.1 | todo |
| AUD-007 | MUST | C4.1 | todo |
| AUD-008 | MUST | C4.2 | todo |
| AUD-009 | MUST | C4.2 | todo |
| AUD-010 | MUST | C4.2 | todo |
| AUD-011 | MUST | C4.2 | todo |
| AUD-012 | COULD | C4.2 | todo |
| AUD-013 | MUST | C4.1 | todo |
| AUD-014 | MUST | C4.1 | todo |
| AUD-015 | MUST | C4.1 | todo |
| AUD-016 | MUST | C4.1 | todo |
| AUD-017 | MUST | C4.1 | todo |
| AUD-018 | MUST | C4.1 | todo |
| AUD-019 | SHOULD | C4.1 | todo |
| AUD-020 | MUST | C4.1 | todo |
| TMR-001 | MUST | C5.1 | todo |
| TMR-002 | MUST | C5.1 | todo |
| TMR-003 | MUST | C5.1 | todo |
| TMR-004 | MUST | C5.1 | todo |
| TMR-005 | MUST | C5.1 | todo |
| TMR-006 | MUST | C5.1 | todo |
| TMR-007 | MUST | C5.1 | todo |
| TMR-008 | MUST | C5.1 | todo |
| TMR-009 | MUST | C1.3c (problem report), C2.3 (editor), C5.1 | partial (C1.3c: TimerTokenNotAlone problem) |
| TMR-010 | MUST | C5.1 | todo |
| TMR-011 | MUST | C5.1 | todo |
| TMR-012 | MUST | C5.1 | todo |
| TMR-013 | MUST | C5.1 | todo |
| TMR-014 | MUST | C5.1 | todo |
| TMR-015 | COULD | C5.1 | todo |
| TMR-016 | MUST | C5.1 | todo |
| ACT-001 | MUST | C5.2 | todo |
| ACT-002 | MUST | C5.2 | partial (C1.1 model, C1.2b id remap; processor in C5.2) |
| ACT-003 | MUST | C5.2 | todo |
| ACT-004 | MUST | C5.2 | todo |
| ACT-010 | MUST | C1.3c, C5.2 | partial (C1.3c: allowed levels checked on save) |
| ACT-011 | MUST | C1.3c, C5.2 | partial (C1.3c: allowed levels checked on save) |
| ACT-012 | MUST | C1.3c, C5.2 | partial (C1.3c: allowed levels checked on save) |
| ACT-013 | MUST | C1.3c, C5.2 | partial (C1.3c: allowed levels checked on save; C1.5b: re-going to the current slide fires nothing) |
| ACT-014 | MUST | C1.3c, C5.2 | partial (C1.3c: allowed levels checked on save) |
| ACT-015 | MUST | C1.3c, C5.2 | partial (C1.3c: allowed levels checked on save) |
| ACT-016 | SHOULD | C1.3c, C5.2 | partial (C1.3c: allowed levels checked on save) |
| ACT-020 | MUST | C5.2 | partial (C1.5b session command; run from Actions in C5.2) |
| ACT-021 | MUST | C5.2 | partial (C1.5b session command; run from Actions in C5.2) |
| ACT-022 | MUST | C5.2 | partial (C1.5b session command; run from Actions in C5.2) |
| ACT-023 | MUST | C5.2 | partial (C1.5b session command; run from Actions in C5.2) |
| ACT-024 | MUST | C5.2 | todo |
| ACT-025 | MUST | C5.2 | todo |
| ACT-026 | MUST | C5.2 | partial (C1.5b session command; run from Actions in C5.2) |
| ACT-027 | MUST | C5.2 | partial (C1.5b session command; run from Actions in C5.2) |
| ACT-028 | MUST | C5.2 | partial (C1.5b session command; run from Actions in C5.2) |
| ACT-029 | MUST | C5.2 | todo |
| ACT-030 | MUST | C5.2 | todo |
| ACT-031 | MUST | C5.2 | partial (C1.5b session command; run from Actions in C5.2) |
| ACT-032 | MUST | C5.2 | partial (C1.5b session command; run from Actions in C5.2) |
| ACT-040 | MUST | C5.2 | todo |
| ACT-041 | MUST | C5.2 | partial (C1.5b session command; run from Actions in C5.2) |
| ACT-042 | MUST | C5.2 | todo |
| ACT-043 | SHOULD | C5.2 | todo |
| ACT-044 | MUST NOT | C5.2 | todo |
| MODE-001 | MUST | C1.3c, C6.1 | partial (C1.3c: Mode lives in the state file, not the Service) |
| MODE-002 | MUST | C6.1 | todo |
| MODE-003 | MUST | C6.1 | todo |
| MODE-004 | MUST | C6.1 | todo |
| CCLI-001 | MUST | C6.1 | todo |
| CCLI-002 | MUST | C6.1 | todo |
| CCLI-003 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| CCLI-004 | MUST | C1.2, C1.3c | logic done (C1.2a; cued Shows ignore stale skips C1.3c); UI in a later chunk |
| CCLI-005 | MUST | C1.2 | logic done (C1.2a); UI in a later chunk |
| CCLI-010 | MUST | C6.1 | todo |
| CCLI-011 | MUST | C6.1 | todo |
| CCLI-012 | MUST | C6.1 | todo |
| CCLI-013 | MUST | C6.1 | todo |
| CCLI-014 | MUST | C6.1 | todo |
| CCLI-015 | MUST | C6.1 | todo |
| CCLI-016 | MUST | C6.1 | todo |
| CCLI-017 | SHOULD | C6.1 | todo |
| CCLI-020 | MUST | C6.1 | todo |
| CCLI-021 | SHOULD | C6.3 | todo |
| CCLI-022 | SHOULD | C6.3 | todo |
| CCLI-023 | SHOULD | C6.3 | todo |
| CCLI-024 | MUST NOT | C6.1 | todo |
| LIVE-001 | MUST | C2.2 | todo |
| LIVE-002 | MUST | C2.2 | todo |
| LIVE-003 | MUST | C2.2 | todo |
| LIVE-004 | MUST | C2.2 | todo |
| LIVE-005 | MUST | C1.5 | done (C1.5a sequence, C1.5b session) |
| LIVE-006 | MUST | C1.5 | done (C1.5a sequence, C1.5b session; list UI C2.2) |
| LIVE-007 | MUST | C1.5 | done (C1.5a sequence, C1.5b session) |
| LIVE-008 | MUST | C1.5 | done (C1.5b) |
| LIVE-009 | MUST | C1.5 | done (C1.5b) |
| LIVE-016 | MUST | C1.5 | done (C1.5a sequence, C1.5b session) |
| LIVE-010 | MUST | C2.2 | todo |
| LIVE-011 | MUST | C2.2 | todo |
| LIVE-012 | MUST | C2.2 | todo |
| LIVE-013 | MUST | C2.2 | partial (C1.5b toggle logic; buttons and rendering C2.2) |
| LIVE-014 | MUST | C1.5 | done (C1.5b) |
| LIVE-015 | MUST NOT | C2.2 | todo |
| LIVE-020 | MUST | C2.2 | todo |
| LIVE-021 | MUST | C2.2 | todo |
| LIVE-022 | MUST | C2.2 | todo |
| LIVE-023 | COULD | C2.2 | todo |
| LIVE-030 | MUST | C2.2 | todo |
| LIVE-031 | MUST | C2.2 | todo |
| LIVE-032 | MUST | C2.2 | todo |
| LIVE-033 | MUST NOT | C2.2 | todo |
| LIVE-040 | SHOULD | C1.5 | partial (C1.5b logic done; calling Refresh on changes C2.2) |
| REM-001 | SHOULD | C8.1 | todo |
| REM-002 | SHOULD | C8.1 | todo |
| REM-003 | SHOULD | C8.1 | todo |
| REM-004 | SHOULD | C8.1 | todo |
| REM-005 | SHOULD | C8.1 | todo |
| REM-006 | SHOULD | C8.1 | todo |
| REM-007 | SHOULD | C8.1 | todo |
| REM-008 | SHOULD | C8.1 | todo |
| REM-009 | MUST NOT | C8.1 | todo |
| REM-010 | SHOULD | C8.1 | todo |
| SCR-001 | MUST | C7.1 | todo |
| SCR-002 | MUST | C7.1 | todo |
| SCR-003 | MUST | C7.1 | todo |
| SCR-004 | MUST | C7.1 | todo |
| SCR-005 | MUST | C7.1 | todo |
| SCR-006 | MUST | C7.1 | todo |
| SCR-007 | MUST | C7.2 | todo |
| SCR-008 | MUST | C7.2 | todo |
| SCR-009 | MUST | C7.2 | todo |
| SCR-010 | MUST | C7.2 | todo |
| SCR-011 | MUST | C7.2 | todo |
| SCR-012 | MUST | C7.2 | todo |
| SCR-013 | MUST | C1.3c, C7.2 | partial (C1.3c: built-in Scripture Category; generation in C7.2) |
| SCR-014 | COULD | C7.2 | todo |
| SCR-015 | COULD | C7.2 | todo |
| SCR-016 | MUST NOT | C7.2 | todo |
| CHK-001 | MUST | C6.2 | todo |
| CHK-002 | MUST | C6.2 | todo |
| CHK-003 | SHOULD | C6.2 | todo |
| CHK-004 | SHOULD | C6.2 | todo |
| SAFE-001 | MUST | C1.3, C2.3 | partial (C1.3: draft store; editing UI in C2.3) |
| SAFE-002 | MUST | C1.3, C2.3 | partial (C1.3: draft store, outside the Library; 30 s autosave timer in C2.3) |
| SAFE-003 | MUST | C2.3 | todo |
| SAFE-004 | SHOULD | C9.1 | todo |
| SAFE-005 | SHOULD | C9.1 | todo |
| SAFE-006 | MUST | C9.1 | partial (C1.5b: a throwing session listener doesn't stop a move) |
| SAFE-007 | MUST NOT | C9.1 | todo |
| IMP-001 | COULD | C9.5 (deferred) | todo |
| IMP-002 | COULD | C9.5 (deferred) | todo |
| IMP-003 | COULD | C9.5 (deferred) | todo |
| IMP-004 | COULD | C9.5 (deferred) | todo |
| FUT-001 | MUST | C1.5, C2.1 | partial (C1.1: types and file format; behavior in later chunks) |
| FUT-002 | MUST | C1.5, C2.1 | todo |
| FUT-003 | MUST | C1.5, C2.1 | todo |
| FUT-004 | SHOULD | C1.5, C2.1 | todo |
| SHOW-008 | MUST | C1.2a (flag), C1.2b (rules), C2.4 (UI) | logic done (C1.2b); UI in a later chunk |
| SVC-027 | MUST | C1.2b, C2.5 | logic done (C1.2b); UI in a later chunk |
| SVC-028 | SHOULD | C1.2b, C2.5 | logic done (C1.2b); UI in a later chunk |
| LIVE-017 | MUST | C1.5, C2.2 | partial (C1.5 logic done; greyed list UI C2.2) |
| TPL-014 | MUST | C1.2a | logic done (C1.2a) |
| LIB-013 | MUST | C1.3c, C1.4, C6.1, C6.2 | partial (C1.3c: state file and repository; C1.4a: watched; Mode switch and CCLI flows later) |
| CAT-010 | MUST | C1.3c, C2.4, C7.2 | partial (C1.3c: added on load, save refused without it; UI later) |
| LIVE-018 | MUST | C1.5, C2.2 | partial (C1.5b logic done; black Output C2.2) |
| LIVE-041 | MUST | C1.5 | done (C1.5b) |
| MODE-005 | MUST | C1.5, C2.2 | partial (C1.5b logic done; buttons and saving the Mode C2.2) |
| ACT-005 | MUST | C1.5, C5.2 | partial (C1.5b event order done; Actions run in C5.2) |
| ACT-006 | MUST | C5.2 | todo |
| ACT-033 | MUST | C1.3c, C5.2 | partial (C1.3c: Normal command in the model; C1.5b SetScreen(Normal); run from Actions in C5.2) |
| AUD-021 | MUST | C4.1 | todo |
| TMR-017 | MUST | C5.1 | todo |
| CCLI-018 | MUST | C6.1 | todo |
