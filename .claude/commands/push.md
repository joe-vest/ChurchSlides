# Push to GitHub

Verify and push the current branch of this .NET repository to GitHub.

## Context
```bash
git status
git branch --show-current
git log --oneline -3
git remote -v
```

## Instructions

Run the steps in sequence. Stop and report if a step fails.

### 1. Pre-flight
- Warn if there are uncommitted changes (they won't be pushed).
- This is a small solo repository, so working directly on `main` is normal. Don't warn about it.
- Show the commits that will be pushed: `git log --oneline @{u}..HEAD` (or the last 3 if no upstream yet).

### 2. Verification
Run `./scripts/check.ps1` (warnings-as-errors build, then all tests). If it fails, do NOT push. Report what needs fixing.

### 3. Push
- `git push -u origin HEAD`
- Report success with the remote URL.

### 4. Summary
Report the branch pushed, the number of commits, the remote URL, and any warnings.
