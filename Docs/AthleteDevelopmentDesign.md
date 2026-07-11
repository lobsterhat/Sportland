# Sportland — Athlete Pool, Development & the Club Roster

**Status:** Living design document
**Last updated:** 2026-07-11
**Scope:** The athlete lifecycle — where players come from, ceilings and decline, how development works and why it pays, familiarity, multi-sport aptitude vs. specialization, and how one club's pool of players maps onto its many sport rosters.

> Code-agnostic by design. Companion to `CalendarLeagueDesign.md` (the league ecosystem this pool populates), `ConflictChemistryDesign.md` (egos and discovery), and `HubActionsDesign.md` (training and recruiting actions).

---

## 1. Purpose

With **at least 8 teams per division**, 3–4 divisions per sport, and multiple sports, Sportland needs a big, living player pool — hundreds of athletes with somewhere to be. This doc defines that pool's ecology: lower divisions churn with low-ceiling and declining players, while **player development is a core pillar** — patiently building athletes up, and the familiarity that comes with keeping them, is meant to be the deepest strategy in the game.

## 2. The Athlete Pool

Every athlete is generated with an **age**, current per-sport stats, hidden **ceilings** (Section 3), and a career arc:

```
growth ──────► peak ──────► decline ──────► retirement
(low teens–20s)  (sport-dependent)  (30s)         (out of the pool)
```

New athletes enter the pool every season (youth intake); retirements remove the old. AI clubs sign, develop, and release from the same pool the player does.

### Churn is the lower divisions' weather

High roster turnover at the lower levels isn't a scripted rule — it **emerges** from who lives there:

- **Low-ceiling players** — honest journeymen already at their modest peak. Easily replaced, frequently released, always available.
- **Declining veterans** — sliding down the ladder as their stats fade, playing out the string a division below their old life. Some carry big egos their current stats no longer justify (a Starter-A expectation on Division 3 legs — a discovery-system trap for careless recruiters).
- **Passing-through prospects** — the occasional high-ceiling youngster who won't be here long, one way or the other.

Practical consequence: a bottom-division club can always fill a roster cheaply, but building anything *lasting* down there requires development — which is the point.

## 3. Ceilings, Multi-Sport Aptitude & Specialization

- **Per-sport ceilings.** Every athlete has a hidden ceiling in each sport — how good they could become with full development. Current stats show where they are; the ceiling is where they could go.
- **Hidden until scouted.** Ceilings follow the discovery rules (`ConflictChemistryDesign.md` §2.2): scouting, *Growth Eye*, and time reveal them. A lower-division roster spot is a bet on information.
- **Aptitude profiles differ.** Some athletes are genuine multi-sport talents (respectable ceilings in several sports); others are born specialists (one tall ceiling, the rest low). Neither is strictly better — the profile shapes the right plan for that athlete.
- **Specialization is a development choice, not just a trait.** Development effort spread across two sports splits its effect; focusing one sport pushes toward that ceiling fastest. A dual-sport talent can be worth developing in both (flexible, fills two rosters) — or better served specializing, trading versatility for a higher realized peak. The player makes this call per athlete, and re-makes it as ceilings reveal themselves.

## 4. Development — The Core Pillar

Building players up must be a winning strategy, not flavor:

- **Training raises stats toward the ceiling.** Team practice lifts the group; individual sessions (`HubActionsDesign.md` §4) push one athlete hard. The **Developer** archetype's *Growth Eye* multiplies gains and reveals hidden potential — this doc is that archetype's home turf.
- **Minutes are food.** Game time develops players — prospects need real games to grow, especially in their growth years. Which creates the game's best staffing tension: **the minutes your prospect needs are the minutes your veteran's ego claims.** Playing the kid means managing the vet (concede, talk down, or trade away) — development and the conflict system feed each other by design.
- **Age gates the return.** Growth-phase athletes gain fast; peak athletes plateau near their ceiling; declining athletes can slow the slide with conditioning but not reverse it. Developing a 19-year-old and a 31-year-old are different investments and should feel like it.
- **The alternative is buying.** Signing ready-made stars always works — but stars carry big egos, big willingness costs (courtship, `HubActionsDesign.md`), no familiarity, and the decline clock. Homegrown-vs-bought is the strategic axis, and homegrown's edge is Section 5.

## 5. Familiarity — Why Keeping Players Pays

**Familiarity** is a per-athlete value with the club that grows with seasons on the roster, games played, and personal attention (one-on-ones, individual training):

- **Effects.** Familiar players execute team play better (a personal amplifier on team chemistry's cooperative bonuses), are slower to escalate discontent (they trust the coach — a Volatility dampener), and respond better to your training.
- **It cannot be bought.** Familiarity only accumulates through time and attention — a signed star arrives at zero. A patiently built core of developed, familiar players outperforms the sum of its stat lines; a churned roster of strangers underperforms theirs.
- **It leaves when they leave.** Releasing a developed, familiar player destroys accumulated value, making retention decisions real. (And it's lost, not banked — re-signing him later starts the trust rebuild, though perhaps not from zero.)
- **Skip and the player character** max the scale by definition — the coach knows himself, and Skip's whole deal is trust. Consistent with ego immunity, this never needs managing.

Together with development, familiarity makes the intended fantasy work: the club that *raised* its roster beats the club that *bought* one, all else equal.

## 6. The Club Pool & Sport Rosters

The club signs athletes into **one club pool**; each sport team the club operates fields a **roster drawn from that pool**:

- **Nobody has to be everywhere.** A pool athlete can be rostered on one sport's team, several (the dual-sport high-wire act, `CalendarLeagueDesign.md` §4), or **none**.
- **Unrostered athletes are the development squad.** They train, build familiarity, and wait — the natural home for growth-phase prospects not ready for league minutes.
- **Egos apply where relevant.** Playing Time/Starter/Spotlight expectations are evaluated per sport roster an athlete is on. Being left *entirely* unrostered frustrates a strong Playing Time ego eventually — low-ego youngsters sit happily in the development squad; a proud veteran will not. Roster assignment is itself an ego-budget decision.
- **And it's not a one-way street.** An athlete stuck out of the games or the role he wants can demand a trade/release mid-season or jump ship at season's end (`ConflictChemistryDesign.md` §6.1) — the development squad only holds players who accept being there.
- **Assignment is free.** Like lineups, moving pool athletes onto and off sport rosters costs no actions (`HubActionsDesign.md`: the paperwork is free) — though league rules may impose windows/deadlines for competition integrity (open question).

## 7. System Dependencies

1. **Athlete generation at scale** — procedural athletes (age, per-sport stats, ceilings, aptitude profile, expectation traits) sufficient to populate 8+ teams × 3–4 divisions × all sports, plus free agents, refreshed seasonally.
2. **Career-arc simulation** — growth/peak/decline curves applied on season (or overnight) ticks, plus retirement and youth intake.
3. **Hidden ceiling data with reveal states** — per-sport potential under the same discovery machinery as expectation traits.
4. **A development model** — training and game minutes converting into stat gains, scaled by age phase, ceiling headroom, focus/specialization choices, and archetype/stat multipliers.
5. **Familiarity value per athlete** — accumulation sources, its team-play/volatility/training effects, and loss on departure.
6. **Club pool ↔ sport roster separation** — one signed pool, per-sport roster assignment (including none), with per-roster ego evaluation.
7. **AI club roster behavior** — AI teams signing, developing, and releasing so the league's churn (especially lower divisions) actually happens in the world, not just in flavor text.

## 8. Open Questions

- **Pool size & generation rates.** How many athletes exist per sport, youth-intake volume, and free-agent pool depth — the numbers behind "a pretty big player pool."
- **Minutes-vs-training balance.** How much of development comes from games vs. practice? (Proposal: games matter enough that pure bench development is meaningfully slower — the vet-vs-prospect tension needs teeth.)
- **Specialization mechanics.** Is focus a declared setting per athlete, or implicit in how you spend sessions/minutes? Does specializing permanently sacrifice the other sports' ceilings or just leave them fallow?
- **Familiarity decay.** Does familiarity erode during long unrostered/benched stretches, or only reset on departure? And does a returning player rebuild faster than a stranger?
- **Roster limits & windows.** Pool size caps, per-sport roster sizes, and whether transfers/assignments lock during a season's stretch run.
- **Aging visibility.** Do players see decline coming (scouting-readable curves) or is a veteran's cliff a surprise?
- **AI development fidelity.** Do AI clubs really develop players (a rival's homegrown star you regret not signing at 18), or approximate it statistically? The former is expensive and wonderful.
