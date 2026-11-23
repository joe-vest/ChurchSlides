# Verify Changes

Run full verification for this .NET solution before committing: format, build (warnings are errors), tests, and project rules.

## Context
```bash
git status --short
git diff --stat HEAD
```

## Instructions

Run the steps in sequence. Stop and report if a step fails. Run from the repository root (PowerShell).

1. **Format** (applies fixes): `dotnet format ChurchSlides.slnx --no-restore`
   Then `git status --short` to see which files the formatter changed.
2. **Build + analyzers**: `dotnet build -warnaserror --nologo`
   Warnings are errors here, so this is the lint and type check.
3. **Tests**: `dotnet test --no-build --nologo`
   This includes the architecture tests that enforce the assembly dependency rule (CLAUDE.md rule 4).
   - If a **golden file** test fails, the saved file format changed. Don't regenerate it to make the test pass. Report it and ask: the change needs a schema version decision (see `SchemaVersions` in `ChurchSlides.Domain`). Only after the owner agrees, regenerate with `CHURCHSLIDES_UPDATE_GOLDEN=1`, review the diff by hand, and run the tests again without the variable.
4. **Project rules** (quick searches over `src/`):
   - No `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now` or `DateTimeOffset.UtcNow` in `src/ChurchSlides.Domain` or `src/ChurchSlides.Engine` (all time goes through `IClock`, CLAUDE.md rule 10).
   - No glossary synonyms introduced for core types (Project, Section, Group, Song item) and none of the four colliding names used raw (`Action`, `Timer`, `Trigger`, `TextBox` as our own types).
5. **Docs**: if the work belongs to a chunk, check that `docs/STATUS.md` and `docs/TRACEABILITY.md` were updated.

Report a summary:
- Format: files changed or already clean
- Build: warnings/errors (expect 0 and 0)
- Tests: X passed / Y failed (name any failures)
- Rules: clean, or the offending lines
- Docs: updated or missing

If everything passes, the changes are ready to commit.
