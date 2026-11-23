# Simplify Code

Review and simplify the C# code that was just written, before it is committed.

## Context
```bash
git status --short
git diff --name-only HEAD
```

## Instructions

Review the files changed since the last commit: modified, staged and new untracked files under `src/` and `tests/`. If the working tree is clean, review the files in the last commit (`git diff --name-only HEAD~1`).

Look for opportunities to simplify:

1. **Dead code**: unused `using` directives, unused private members, unused parameters, unused locals, unreachable branches.
2. **Redundant qualification**: fully qualified names where a `using` already exists.
3. **Nesting**: flatten with early returns or guard clauses.
4. **Duplication**: extract repeated patterns only when it clearly helps (don't over-abstract).
5. **Over-engineering**: speculative parameters, abstractions with a single use, features nothing calls yet and no plan names. Check `docs/plans/` before removing something a coming chunk needs.
6. **Naming**: glossary names only (FRD section 3 and CLAUDE.md rule 2).

Do NOT:
- Add features or capabilities.
- Change behavior or the saved file format. Golden-file tests must stay unchanged.
- Add comments or documentation. Keep existing requirement-ID comments.
- Touch code that wasn't recently changed.

After editing, run `./scripts/check.ps1`. It must still show 0 warnings, 0 errors and all tests passing.

Report what was simplified, or confirm the code is already clean.
