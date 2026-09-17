# 02: Money and Rate value objects

**Issue:** #13 — https://github.com/mykola-blonskyi/gestoria/issues/13

**What to build:** Money stops being a loose `decimal` and becomes a type that cannot be built from a `double` or rounded behind your back. `Rate` cannot hold a value outside [0,1].

Every calculation ticket depends on these, which is why they land before any of them. Split out of #3 so that the build infrastructure and the domain types are separate concerns.

**Blocked by:** 01 (#3)

**Status:** ready-for-agent

- [ ] `Money` is a readonly record struct over `decimal` with currency fixed to EUR
- [ ] There is no implicit conversion from `double` or `float`; constructing a `Money` from one does not compile
- [ ] `Round2()` is explicit and no operation rounds implicitly, per ADR-0004 and SPEC-002 §5
- [ ] Arithmetic covers addition, subtraction, negation, multiplication by a `Rate`, comparison and ordering
- [ ] `Rate` rejects values outside [0,1] at construction
- [ ] Unit tests cover the arithmetic, the rejection cases, and that rounding never happens unless asked for
