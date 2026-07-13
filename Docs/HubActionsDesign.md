# Sportland — Hub Actions & the Daily Economy

**Status:** Living design document
**Last updated:** 2026-07-10
**Scope:** The day as the management layer's currency — the action budget, what actions exist, where they live in the hub, and the pressure that makes spending them interesting.

> Code-agnostic by design. Companion to `HubWorldDesign.md` (buildings, core loop), `CharacterCreatorDesign.md` (archetype action modifiers), and `ConflictChemistryDesign.md` (conflicts as action sinks).

---

## 1. Purpose

Between scheduled games, Sportland runs on days, and days run on **actions**. Each day gives the player a small budget of actions to spend on managing the team — training, scouting, conflict talks, events, recovery. The budget is always smaller than the list of things worth doing; choosing what *doesn't* get done is the management game.

Two principles:

- **Information is free; intervention costs.** Looking at any screen, reading reports, and talking to Skip never cost actions. Changing the world does. The player should never feel taxed for trying to understand their situation.
- **The day is the unit of drama.** Deadlines (next game, a Fed up athlete, a closing transfer window) are measured in days, so every spent action is a small commitment about what kind of coach you are.

## 2. The Day Loop

1. **Morning — hub opens.** The player is in the hub with their action budget. Skip surfaces anything urgent (free): *"Jackson's still sulking, and we play the Comets in two days."*
2. **Spend actions.** Walk to buildings, take actions, in any order. Free activities anytime.
3. **End the day.** Return Home (or choose "advance day" from anywhere once designed). Unspent actions are lost — **use it or lose it**, so ending early is itself a choice, not a savings plan.
4. **Overnight tick.** Fatigue recovery, discontent checkpoints, training results land, calendar advances. Next morning: new budget, new situation.

**Game days** are different: the scheduled game consumes the heart of the day. The player gets **one action** before the game (a pre-game talk, a last lineup fix, a quick treatment) — then it's off to the arena, and the evening belongs to the post-game.

### 2.1 The post-game address

After the final whistle, before the recap closes the day, the coach speaks — a **free choice** (no action cost; it's part of game day), picked from:

| Option | What it does | The fine print |
|---|---|---|
| **Motivate / praise** | Contentment and morale up; **the main feeder of the Recognition trait** (`ConflictChemistryDesign.md` §2) — big performers who go unpraised here are the ones who sour | Praise has to land: a rousing speech after a gutless blowout loss rings hollow (reduced or no effect); the Motivator archetype's words land harder |
| **Chastise** | The risk/reward read: can sharpen focus (a performance edge next game) when the team *knows* it deserved it and chemistry can absorb it | Backfires into discontent against volatile players or after games they actually played well; punishing effort is how locker rooms are lost |
| **Call practice** | Books tomorrow as an enhanced team practice (focus chosen now, small efficiency bonus for striking while the iron's hot) | Spends one of tomorrow's actions tonight; Workload egos grumble on the spot |
| **Team building** | Books a team event for tomorrow (guest-list flow as normal) | After a **loss**, this is the spiral-breaker: the event lands with a morale-repair bonus — the designed answer to bad-night momentum |
| **Rest day** | Tomorrow is off: everyone (including you) recovers extra fatigue, Workload egos are pleased | Tomorrow's entire action budget is forfeit — recovery is bought with a management day |

Two design notes: **the address is free but rarely neutral** — most options mortgage tomorrow, so the post-game choice is really a calendar commitment made in an emotional moment; and **reception is filtered through who's listening** — dispositions (`ConflictChemistryDesign.md` §2.3), expectation traits, volatility, and the actual result all shape how each athlete takes the same words. Skip and the player character, as ever, take everything well.

## 3. The Action Budget

- **Base budget: 3 actions per day** *(placeholder number, like all numbers here)*.
- **Archetype modifiers** (from `CharacterCreatorDesign.md`): the Player-Coach's *Double Shift* grants +1; the Superstar's management D manifests as a smaller budget (2). Other archetypes sit at base.
- **Your own fatigue costs actions.** The player character is a playable athlete; playing heavy minutes builds personal fatigue like anyone else, and a sufficiently exhausted coach loses an action the next day. This makes the play-vs-manage tension *mechanical*: a Superstar who takes over games personally is spending tomorrow's management on tonight's heroics.
- **Time Management** (management stat) is the tuning dial for occasional bonus actions or discounts — exact mechanism open.

## 4. Action Catalog (by building)

The catalog is data-driven and will grow; this is the starting set. **(A)** = costs an action, **(F)** = free.

### Office — the front desk of the franchise
- **Set lineup / rotation** (F) — never charge for the basic job of coaching.
- **Talk it down** (A) — resolve a live conflict event (`ConflictChemistryDesign.md` §6).
- **Assign scouting** (A) — investigate an athlete (own roster or target): reveals expectation traits, condition, potential. Results may take days; depth scales with scouting stat / *Growth Eye*. Scouting is done through activity channels — attending matches, 1v1 invitationals, interviews — see `ScoutingDesign.md`.
- **Sign / release** (F) — the paperwork is free. Once an athlete is willing to join, signing them costs nothing; releasing anyone costs nothing. Roster churn is limited by *willingness*, not clerical friction.
- **Interview / schmooze** (A) — the recruiting pitch: sit down with a signable athlete to entice them onto the team. Raises their **willingness to sign**, and — being a conversation — has a chance to reveal an expectation trait (`ConflictChemistryDesign.md` §2.2), so recruiting doubles as due diligence. Low-profile athletes may sign without any courtship; stars expect to be wooed, possibly across multiple sittings. In the offseason the same action points inward as a **retention pitch** to a wavering player (`ConflictChemistryDesign.md` §6.1) — recruiting and retention are one skill. Skip never needs schmoozing — he is always willing, per his contract.
- **League business** (F to view; A for commitments like joining a league or scheduling a friendly).

### Practice Facility — where the weeks are won
- **Team practice** (A) — team-wide training with a chosen focus (offense, defense, conditioning, set plays). Raises Workload — heavy schedules grind athletes with Workload expectations.
- **Individual session** (A) — focused development for one athlete; the Developer's *Growth Eye* multiplier lives here.
- **Personal training** (A) — train your own character's athletic/sport skills. The Superstar's main sink.

### Hospital — damage control
- **Treatment** (A) — accelerate an athlete's injury recovery or burn off heavy fatigue.
- **Checkup** (F) — condition report on any athlete; pairs with scouting for full information.

### Cafe / Home — the social engine
- **Team event** (A) — group chemistry builder (dinner, bowling night, outing) with a **guest list** the player curates — see below.
- **One-on-one** (A) — time with a single athlete: contentment gain, and it can reveal an expectation trait through conversation — the social alternative to scouting.
- **Rest** (A) — deliberately burn an action to clear your own character's fatigue. Turning time into readiness.
- **End the day** (F) — sleep; forfeit anything unspent.

#### The guest list — how team events work

One action buys the event; the player decides who's on the list and on what terms:

- **Invite anyone, from two players to the whole pool.** The guest list is the design space — an intimate dinner and a full-club bowling night are the same action with very different shapes.
- **Depth dilutes with size.** The event's benefits spread across attendees: a small gathering gives strong per-person contentment/chemistry gains and a real chance of learning something about an individual (an expectation-trait read, per the scouting discovery rules — `ScoutingDesign.md`); a big party gives everyone a little and reveals almost nothing about anyone. Broad-and-shallow or narrow-and-deep is the core choice.
- **Exclusion has a social cost.** Every player left off the list has a *chance to feel left out* — a contentment hit scaled by how big the party was (nobody resents missing a quiet dinner for two; being one of three names left off the whole-team night out stings), and by who they are: Recognition and Spotlight egos take exclusion hardest, high familiarity shrugs it off.
- **Optional or mandatory attendance.** Invited players don't always want to come:
  - **Optional** — reluctant invitees (tired, low contentment, unfamiliar newcomers, private personalities) simply decline, no harm done. You get a willing room and a pure vibe, but no guarantee the players who *need* to be there show up.
  - **Mandatory** — everyone invited attends. Now you can force the two feuding teammates into the same booth (a Mediator's favorite move) — but unwilling attendees drag the event's benefit down for everyone and pick up a little discontent themselves (Workload egos especially resent mandatory fun).
- **Skip and the player character** are always happy to attend and never feel left out — ego immunity extends to party invitations.

### Skip — everywhere, always free
Consulting Skip is **never an action**: his warnings, suggestions, and explanations are ambient (per `HubWorldDesign.md` §4.3). He tells you *where* the fires are; putting them out is what actions are for.

## 5. Pressure — What Makes the Budget Interesting

- **The calendar squeezes.** Days until the next game are the real budget. Three free days before a big game is training camp; a back-to-back means triage.
- **Conflicts are action taxes.** Every live conflict event demands an action to resolve (or festers, draining chemistry) — an ego-heavy roster literally costs more actions per week to operate. This is the Mediator's economy: fewer failed talk-downs means fewer repeat taxes.
- **Multi-sport overlap.** When seasons overlap, game days multiply and free days evaporate — the busy season should feel like holding two jobs.
- **Your own body is on the budget.** Playing yourself hard costs future actions (Section 3); resting yourself costs a present one.

## 6. System Dependencies

1. **A calendar/day system** — dates, scheduled games, day advancement with an overnight processing tick.
2. **An action-budget resource** — per-day count with modifier hooks (archetype, personal fatigue, stats).
3. **Action definitions as data** — id, building, cost, requirements, effects — so the catalog grows without code changes.
4. **Building interaction points in the hub scene** — each building exposes its actions when approached/entered.
5. **Consumer-system hooks** — training, scouting, conflict queue, chemistry, injury/fatigue systems all execute the effects actions trigger.
6. **A willingness-to-sign value on signable athletes** — raised by interviews/schmoozing (scaled by talent/ego), gating signings instead of an action cost; the mentor is permanently at maximum willingness.
7. **A day-summary surface** — morning briefing (Skip) and overnight results need somewhere to land.

## 7. Open Questions

- **Points vs. time slots.** Current model is abstract points spent in any order. Alternative: Persona-style day slots (morning/afternoon/evening) where actions occupy times — more flavor and more scheduling texture, more UI. Points are the simpler default; slots are worth a look once the hub is walkable.
- **Recruiting depth.** How many interviews does a star demand, and what raises willingness besides schmoozing (team success, chemistry reputation, a friend already on the roster)? Does a botched pitch or a release *lower* willingness — can burned bridges exist, and how do they mend?
- **Banking and overdraft.** Use-it-or-lose-it is the proposal; any exceptions (bank exactly 1? borrow 1 against tomorrow at a morale cost?) or is the hard edge better?
- **Delegation/staff.** Can hired staff (or Skip?) execute actions autonomously later in a career — auto-scouting, assistant-run practices? Big lever for late-game scale; out of scope for the first pass.
- **Batch actions.** Does a multi-day training camp exist (spend several days' actions at once for efficiency), or is everything day-granular?
- ~~Event attendance willingness~~ **Resolved:** disposition traits (Social, Competitive, Openness — `ConflictChemistryDesign.md` §2.3) drive activity willingness, layered on contentment, fatigue, and familiarity.
- ~~Event timing plays~~ **Resolved:** the post-game address (§2.1) books next-day events, and post-loss team building carries a morale-repair bonus.
- **Numbers.** Base 3 / Superstar 2 / Player-Coach 4, one pre-game action, fatigue thresholds, dilution curves and exclusion odds — all placeholders pending playtesting.
