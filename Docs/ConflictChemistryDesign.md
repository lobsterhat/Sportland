# Sportland — Conflict & Chemistry Design

**Status:** Living design document
**Last updated:** 2026-07-10
**Scope:** The ego layer — personality expectations that generate friction, the conflicts that friction produces, how conflicts get resolved, and team chemistry as the resource the whole loop feeds.

> Code-agnostic by design. Referenced by `CharacterCreatorDesign.md` (Motivator/Mediator archetypes) and `HubWorldDesign.md` (Skip, hub workflow).

---

## 1. Purpose

Athletes in Sportland aren't just stat blocks — they have egos. Each athlete carries a set of **personality expectation traits** (rated, not binary): things they expect from their situation, like playing time or a specific position. Meeting expectations keeps the locker room content; ignoring them breeds discontent, discontent becomes conflict, and conflict drains **team chemistry** — unless it's handled well, in which case it can *build* it.

This system is what makes roster construction a personality puzzle on top of a talent puzzle, and it's the stage the Motivator and Mediator archetypes perform on.

## 2. Expectation Traits

Every athlete has a rating (0–100, displayed as the standard S–F letter grades) for each trait in a shared set. The rating expresses **how much they expect and how strongly they react** when they don't get it. A rating of 0/F means no ego on that axis at all.

Proposed starting set:

| Trait | The expectation | What upsets them |
|---|---|---|
| **Playing Time** | A share of minutes in games they dress for | Riding the bench, short shifts |
| **Position** | To play a specific position — *even if they aren't the best fit for it* | Being slotted anywhere else, however sensible |
| **Starter** | To be in the starting lineup (symbolic — distinct from total minutes) | Coming off the bench, even with heavy minutes |
| **Spotlight** | To be the focal point — plays called for them, touches, shots | Being a role player in someone else's show |
| **Recognition** | Acknowledgment after strong performances | Big games that go unpraised |
| **Workload** | Reasonable practice intensity and rest days | Being ground down by heavy training schedules |

Additionally, one athlete-level modifier:

- **Volatility** — a single rating that scales how *fast* discontent grows across all traits. Two athletes can expect the same minutes; the volatile one boils over in a week, the even-keeled one simmers for a month.

The trait set is data, not code — it should be extensible as sports and systems grow (see dependencies, Section 7).

### 2.1 Ego immunity — the player character and Skip

**The player's character and Skip have no ego and are immune to this entire system.**

- They carry no expectation ratings (all axes at zero *by rule*, not merely by value — nothing that raises egos over a career can touch them).
- They never generate discontent or conflicts, never demand minutes, positions, praise, or rest.
- Skip's penalty-free release/rejoin contract (see `HubWorldDesign.md` §4.4) is really a special case of this immunity.

Design intent: the player character is the coach's avatar and Skip is the tutorial's warmth — neither should ever be a management problem. They're also the pressure valve: benching yourself or Skip is always free, which gives the player two guaranteed-safe roster levers when the ego budget gets tight.

## 3. The Ego Budget

Expectations are claims on **finite resources**: there are only so many minutes, one starting lineup, one spot per position, so many plays to call, so much praise-worthy spotlight. This makes team building a budget problem:

- A roster of five Playing Time-A athletes cannot all be satisfied — someone's expectation *will* go unmet every game.
- Two athletes who both expect the same position are a built-in collision, regardless of talent.
- High-talent athletes will often carry high expectations (via generation/flags), so stacking stars has a hidden cost.

The interesting decisions fall out naturally: sign the weaker athlete with no ego, or the star who demands the position your captain already claims? A Mediator player can deliberately run a volatile roster that no one else could hold together.

## 4. From Discontent to Conflict

Each athlete's unmet expectations accumulate into **discontent**, evaluated at natural checkpoints (post-game for game-related traits, end-of-day for workload/recognition). Discontent moves through visible stages:

1. **Content** — expectations met. No effects.
2. **Annoyed** — grumbling; small personal morale dip. Visible early-warning state (Skip flags it: *"Jackson's been eyeing the minutes column."*).
3. **Upset** — visible attitude; personal performance dip; begins draining team chemistry passively; may trigger a **conflict event**.
4. **Fed up** — demands a meeting (forced conflict event); may refuse assignments; sustained chemistry drain.

**Conflict events** are discrete, dated things that happen and must be dealt with:

- **Athlete ↔ coach** — the athlete confronts the player character about the unmet expectation.
- **Athlete ↔ athlete** — collisions (both want the same position/spot/spotlight) or spillover (an Upset athlete snaps at a teammate; the flag system's personalities — tempers, ball hogs — make this more likely).
- **In-game flare-ups** — fights/blow-ups during games surface back in the hub as conflicts to handle (game modules already emit significant events; this consumes them).

Meeting expectations, conversely, slowly rebuilds contentment — and consistently satisfied athletes contribute small chemistry gains.

## 5. Resolution

When a conflict event is live, the player chooses how to handle it (a hub action, typically at the Office):

| Approach | What happens | Cost / risk |
|---|---|---|
| **Talk it down** | Chance-based resolution. Success: conflict cleared, **chemistry rises above where it started**. Failure: discontent worsens. | Costs a daily action; odds depend on management stats — the Mediator's *Clear the Air* is a large bonus here |
| **Concede** | Give them what they want (minutes, the position, the start). Guaranteed peace on that axis. | Lineup optimality — you're now playing their ego, not the best five |
| **Hold firm** | Refuse. The athlete stays upset. | Ongoing chemistry drain, performance dip, possible escalation or a lasting flag (e.g., *Disgruntled*) |
| **Move on** | Trade/release the athlete. | Roster cost; possible chemistry shock to close teammates |

The "resolution builds past neutral" rule (from the Mediator spec) applies to **successful talk-downs by anyone** — the Mediator just succeeds far more often. A team that fights and reconciles ends up tighter than one that never fought.

## 6. Team Chemistry

A single team-level value (0–100, letter-graded like everything else).

**Rises from:** winning, expectations being met over time, successfully resolved conflicts (past neutral), team-building events (Cafe/hub events), a Motivator's *Locker Room Aura*.

**Falls from:** Upset/Fed up athletes (passive drain), unresolved or failed conflicts, losing streaks, roster churn.

**Effects:**

- **On-field team play** — chemistry modifies the cooperative parts of every sport: passing quality, help defense, off-ball effort. Individual brilliance is untouched; *togetherness* is what swings.
- **Friction resistance** — high chemistry slows discontent growth (a happy locker room forgives a short night); low chemistry accelerates it (everything is an insult).

That second effect makes chemistry self-reinforcing in both directions — protecting it early matters, and death spirals are a real (intended) danger for ego-heavy rosters with no people skills.

### Division of labor recap

- **Motivator** — *prevents*: aura slows discontent growth and buoys morale.
- **Mediator** — *repairs*: big bonus to talk-downs; resolutions build chemistry.
- **Skip** — *informs*: flags rising discontent early and suggests handling, but changes no odds.
- **Player character & Skip** — *immune*: never part of the problem.

## 7. System Dependencies

1. **Expectation trait ratings on athlete data** — a universal, extensible set of rated traits (plus Volatility), distinct from the acquired/situational flag system, though flags feed conflict likelihood and can be produced by outcomes.
2. **An ego-immunity rule** — a hard exemption for the player character and the mentor that no career system can override.
3. **Lineup/rotation data per game** — minutes, positions, and starter status must be recorded so expectations can be evaluated against what actually happened.
4. **A conflict-event queue in the hub loop** — conflicts created at checkpoints, surfaced in the hub, resolved via daily actions.
5. **Resolution mechanics with modifiable odds** — the hook Mediator (and management stats generally) plug into.
6. **A team-chemistry value with game-side hooks** — sports need a way to read chemistry and apply it to cooperative play.
7. **Skip hint hooks** — discontent states feed Skip's pre-game/post-game tips.

## 8. Open Questions

- **Trait list final cut.** Are these six + Volatility right? Candidates considered and parked: Loyalty (expects not to be trade bait), Rivalry (specific-athlete grudges as links rather than ratings).
- **Visibility.** Are expectations fully visible for your own roster? Hidden until scouted for free agents/signings (a natural hook for scouting and *Growth Eye*)? Or partially hidden until they first flare?
- **Expectation growth.** Do egos change over a career — a breakout youngster's Playing Time expectation rising with his stats, a veteran mellowing? (Strong candidate: yes, driven by performance and age.)
- **Split "how much" from "how hard."** One rating currently covers both the size of the expectation and the reaction strength (with Volatility as a global modifier). Is a per-trait split worth the complexity?
- **Pairwise chemistry.** Team-level only for now; individual friendships/rivalries as links or flags could layer on later.
- **Position mapping per sport.** "Position" expectations need each sport to declare its position set; multi-sport athletes may hold different positional egos per sport.
- **Tuning.** Stage thresholds, drain/gain rates, talk-down base odds, and the size of the past-neutral bonus are all playtest-driven placeholders.
