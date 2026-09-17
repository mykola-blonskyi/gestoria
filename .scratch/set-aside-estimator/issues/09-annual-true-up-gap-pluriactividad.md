# 09: The taxpayer is warned that 20 % is not his real tax rate

**Issue:** #9 — https://github.com/mykola-blonskyi/gestoria/issues/9

**What to build:** The gap between what Modelo 130 collects during the year and what the annual return will actually charge, for a person with both employment income and activity income in the same year.

This is the ticket that makes the whole feature worth building. Modelo 130 takes a flat 20 %, but activity income stacks on top of employment income, so the marginal rate on it is higher. Holding back 20 % feels prudent and produces a large unexpected bill. Generic advice gets this wrong; this taxpayer needs the specific answer.

**Blocked by:** 06 (#6), 08 (#8)

**Status:** ready-for-agent

- [ ] Given projected employment income and projected activity income for the same year, compute the marginal rate applying to the activity income once it is stacked on the employment income
- [ ] State and regional scales are both applied, per business rule 9
- [ ] The output states the gap between Modelo 130 advances at the configured rate and the projected annual liability, in euros, and when it falls due
- [ ] A warning fires when activity income exceeds the other-income cap, because that destroys the `reducción por trabajo` entirely, per business rule 6 and golden G8
- [ ] The amount of relief lost is stated, not just the fact of the loss, per SPEC-002 §7
- [ ] The trace shows the stacking, so the figure can be argued with rather than only believed
- [ ] Worked example: 40,000 employment plus 30,000 activity shows a true-up gap well above zero and names the month it is payable
