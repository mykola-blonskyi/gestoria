# 01: Engine scaffolding, build guardrails, and CI

**Issue:** #3 — https://github.com/mykola-blonskyi/gestoria/issues/3

**What to build:** A place for calculation code to live, with the rules that stop the most expensive class of bug in this project from ever compiling. Writing a float where money belongs becomes a build error rather than a review comment, and CI proves it on every push.

This is prefactoring and it is the root of the whole graph. It delivers no tax answer. It exists so ADR-0004's "money is never a float" stops being a sentence in a document. The `Money` and `Rate` types themselves are #13.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] `src/GestorIA.Engine` and `tests/GestorIA.Engine.Tests` exist and are registered in `GestorIA.slnx`
- [ ] `Directory.Build.props` sets nullable enabled and warnings as errors for every project
- [ ] A banned-API rule makes `double` or `float` a build error inside the Engine, per ADR-0004 and `docs/CONVENTIONS.md`
- [ ] A deliberate float declaration in the Engine fails `dotnet build`, demonstrated once rather than assumed
- [ ] The existing parser test still passes under warnings-as-errors, or the warnings it surfaces are fixed rather than suppressed
- [ ] GitHub Actions runs `dotnet build` and `dotnet test` on push and on pull request, and is green
