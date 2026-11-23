# Run Tests

Run the .NET test suite and report results. Optional argument: a test filter such as `FullyQualifiedName~Serialization` or a project name.

## Context
```bash
git status --short
```

## Instructions

1. Run the tests: `dotnet test --nologo $ARGUMENTS`
   - With no argument this builds and runs the whole solution.
   - With a filter: `dotnet test --nologo --filter "$ARGUMENTS"`. With a project path, pass it as the first argument.
2. If tests fail:
   - Summarize which tests failed and why (test name, assertion message, requirement ID if the name has one).
   - Suggest fixes if the cause is obvious.
   - A failing **golden file** test means the saved file format changed. Say so, and don't regenerate the file without the owner's agreement (see `/dotnet-verify`).
3. If tests pass:
   - Report the passed count per test project.
   - Note any warnings or skipped tests.
