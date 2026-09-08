# Sessions — Coding Agent Instructions

Sessions is an open-source, local-first desktop application for creating, launching, managing, and ending reusable computer contexts such as work, gaming, streaming, and development.

## Sources of truth

- [docs/PRODUCT.md](docs/PRODUCT.md) defines product intent, user-visible behaviour, UX direction, and milestone scope.
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) defines the intended architecture, technical decisions, and implementation boundaries.
- [docs/DEVELOPMENT_NOTES.md](docs/DEVELOPMENT_NOTES.md) records user preferences, decision rationale, dated findings, and verification limits for continuity between work sessions.
- [docs/BACKLOG.md](docs/BACKLOG.md) tracks outstanding work, priorities, evidence, and completion criteria. It does not expand milestone scope or authorize all listed work.

Before making a change that affects product behaviour or architecture, read the relevant document under `docs/`. If the implementation changes an established decision, update the documentation in the same change. Distinguish implemented behaviour from planned work.

At the start of a task, read the current handoff in DEVELOPMENT_NOTES.md and relevant BACKLOG.md entries. Record significant new findings and user feedback as work proceeds. Keep backlog IDs stable, distinguish reported issues from code-inspection findings and proposals, and mark work done only with validation evidence. Update PRODUCT.md or ARCHITECTURE.md when a decision changes; notes and backlog must not become competing specifications.

For a fresh conversation, start with the **Resume next session** section in DEVELOPMENT_NOTES.md. It records the last stopping point, confirmed outcomes, validation commands, and remaining choices; do not assume the conversation history is available or automatically start the next backlog item.

## Repository structure

```text
Sessions.slnx
src/
  Sessions.App/         Avalonia UI, ViewModels, and application composition
  Sessions.Core/        Platform-neutral domain and execution logic
tests/
  Sessions.Core.Tests/  Meaningful Core behaviour tests using xUnit
  Sessions.App.Tests/   Headless Avalonia interaction and rendering tests
  Sessions.LaunchProbe/ Windowless helper for native launch-lifetime regression tests
docs/
  PRODUCT.md
  ARCHITECTURE.md
  DEVELOPMENT_NOTES.md
  BACKLOG.md
```

## Coding rules

- Preserve the Session-centric model. Gaming is one use case; do not assume every Session has a main executable.
- Keep domain and execution logic out of Avalonia Views and ViewModels. Core must not depend on Avalonia; isolate platform-specific behaviour.
- Preserve process ownership during cleanup: never blindly kill all configured processes. Follow the semantics in ARCHITECTURE.md.
- Inspect existing code and relevant domain types before implementation or architectural changes.
- Prefer small, coherent changes and readable code. Avoid unrelated broad refactors and over-engineering; introduce abstractions only for real boundaries.
- Use async APIs where work may block. Use dependency injection when it improves separation and testing, without excessive services or interfaces.
- Use descriptive names, keep nullable reference types enabled, and investigate compiler warnings rather than suppressing them without understanding them.
- Test execution, ownership, ordering, state transitions, cleanup, and failure policies. Avoid tests of trivial properties.
- Respect the milestone scope in PRODUCT.md. Do not implement speculative actions or subsystems merely because they are listed as future possibilities.
- When ambiguity permits a reasonable reversible decision, make it and explain significant architectural choices rather than blocking progress.

## Validation

After changes, build the full solution and run relevant tests from the repository root:

```powershell
dotnet build Sessions.slnx
dotnet test tests/Sessions.Core.Tests/Sessions.Core.Tests.csproj
```

For UI changes, also run `dotnet test tests/Sessions.App.Tests/Sessions.App.Tests.csproj`.

Leave the repository in a building state. Report validation results and any checks that could not be completed. For documentation changes, also check links, consistency between documents, and preservation of requirements.
