# 01: Engine scaffolding, build guardrails, and CI

**Issue:** #3 — https://github.com/mykola-blonskyi/gestoria/issues/3

**What to build:** A place for calculation code to live, with the rules that stop the most expensive class of bug in this project from ever compiling. Writing a float where money belongs becomes a build error rather than a review comment, and CI proves it on every push.

This is prefactoring and it is the root of the whole graph. It delivers no tax answer. It exists so ADR-0004's "money is never a float" stops being a sentence in a document. The `Money` and `Rate` types themselves are #13.

**Blocked by:** None (can start immediately)

**Status:** done (merged in #17, `BannedSymbols.txt` fixed separately)

- [x] `src/GestorIA.Engine` and `tests/GestorIA.Engine.Tests` exist and are registered in `GestorIA.slnx`
- [x] `Directory.Build.props` sets nullable enabled and warnings as errors for every project
- [x] `Directory.Build.props` sets **both** `TreatWarningsAsErrors` and `MSBuildTreatWarningsAsErrors`. The first covers compiler warnings (`CS****`), the second covers MSBuild warnings (`MSB****`). Without the second, a `ProjectReference` pointing at a path that does not exist builds green
- [x] A banned-API rule makes `double` and `float` **member access** a build error inside the Engine, per ADR-0004. `BannedSymbols.txt` must be non-empty; an empty file disables the rule silently
- [x] A deliberate `double.Parse` in the Engine fails `dotnet build` with `RS0030`, demonstrated once rather than assumed
- [x] A deliberate `double` **declaration** in the Engine fails `dotnet test` via a guard test naming the file and line. `BannedApiAnalyzers` does not catch declarations, only member access, so this second mechanism is required rather than optional
- [x] `AnalysisLevel` is left at its default. Raising it to `latest-recommended` currently fails on three CA rules in `Result.cs` (one `CA1716`, two `CA1000`) and is a separate decision
- [x] The existing parser test still passes under warnings-as-errors, or the warnings it surfaces are fixed rather than suppressed
- [x] GitHub Actions runs `dotnet build` and `dotnet test` on push and on pull request, and is green

**Note.** Criterion 4 originally read "a deliberate float declaration fails `dotnet build`". That is not achievable with `BannedApiAnalyzers`, which fires on member access through the banned type and not on the type appearing in a signature, a local or a field. The criteria above split it into the two mechanisms that between them cover both cases.

The issue was closed by #17 with `BannedSymbols.txt` empty, so the analyzer half was wired up but inert. Fixed separately.
