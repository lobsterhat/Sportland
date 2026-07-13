# Special Abilities — Design

Cross-sport conditional modifiers that stack on top of a player's base stats. The
architecture is locked and the engine is built in dodgeball (code map + commits in
Claude memory `project_special_abilities.md`). This doc is the **design canvas**: the
frame, a template, worked examples, and an idea backlog to spitball into.

---

## Locked architecture (the rules of the system)

- **Per player: on/off.** A player either has an ability or doesn't — no
  positive/negative *setting*.
- **Per ability: one modifier set**, which can mix boosts (×>1) and penalties (×<1).
  "Positive / negative / both" just describes what's in the set.
- **Composition: multiplicative.** `effective = base × ∏(active multipliers)`, clamped
  to [0,1]. Order-independent. Boosts **saturate at the rating ceiling** (the clamp) —
  an ability can't push a stat past its max unless we revisit the clamp (open question).
- **Trigger = a list of (source → action) rules.** Sources are events
  (e.g. `TookDamage`) or predicates (e.g. `LastAliveOnTeam`). Actions:
  - **Activate** — turn on (optional duration; 0 = on while the source holds).
  - **Deactivate** — secondary off-switch.
  - **Increase** — secondary escalation (adds a stack).
- **Two trigger archetypes, both built:** event-latched-with-duration (Hot Head) and
  continuous-predicate on/off (Sole Survivor).
- **Stacking: `baseMult ^ stacks`**, capped by `maxStacks`. Explicit per-level tables
  are deferred — the power formula is the implicit "each level multiplies once more,"
  swappable later behind one method with no call-site churn.

---

## Ability template

Copy this block for each new idea:

### \<Name\>
- **Sport(s):** dodgeball | hockey | any | …
- **Fantasy:** one line — the player-feel / story.
- **Trigger(s):** source → action; e.g. "TookDamage → Activate 5s; TookDamage again → Increase".
- **Effect (modifiers):** stat ×mult, … (note which are boosts vs penalties).
- **Stacks:** maxStacks, and what a single stack represents.
- **Notes / open Qs:** balance, edge cases, what game state it needs to read.

---

## Worked examples (built)

### Hot Head
- **Sport(s):** dodgeball (concept is cross-sport — any "took a hard hit / body check").
- **Fantasy:** get drilled and you see red — reckless power, sloppy finesse.
- **Trigger(s):** `TookDamage → Activate(activeSeconds, default 5)`; another hit in the
  window → `Increase` + refresh the timer; window elapses → `Deactivate`.
- **Effect:** throwSpeed ×1.25, throwAccuracy ×0.85, catching ×0.80 (a "both" set).
- **Stacks:** maxStacks 3; each hit raises every modifier to `^stacks` (frenzy compounds).
- **Notes:** exercises event-latch + duration + stacking + mixed modifiers in one ability.

### Sole Survivor
- **Sport(s):** any elimination-style mode.
- **Fantasy:** last one standing rises to the moment.
- **Trigger(s):** predicate `ActiveTeammates ≤ 1 → Activate(0)` (no duration); else
  `Deactivate`. Re-evaluated each frame, so a Mode-4 catch-revive flips it straight off.
- **Effect:** throwSpeed ×1.40, throwAccuracy ×1.30, anticipation ×1.30, catching ×1.30
  (pure boost).
- **Stacks:** none (on/off, maxStacks 1).
- **Notes:** the continuous-predicate path. `infieldersOnly` toggle matches the wipeout
  rule (game ends when a team's infielders are gone).

---

## Idea backlog

Dump raw ideas here; shape them into the template above as they firm up. The three
below are unbuilt seeds to show the shape and prime the pump — edit / delete freely.

### Cross-sport
- **Clutch** — late-game, close-score boost. Trigger: predicate (final minute AND
  score within N) → Activate. Pure boost. Open: needs match clock + score access.
- **Gasser** — finesse decays as endurance drains. Trigger: predicate (endurance < X) →
  Activate, scaling penalty (a use for a stacked/level effect). Penalty-only "flaw."
- **Showboat** — boost while comfortably ahead, penalty when behind. Trigger: two rules
  off the score margin (one Activate when ahead, a different set when behind) — a
  "both" ability that flips with game state.

### Dodgeball-specific
-

### Hockey-specific (future)
-

### Parking lot (raw, unsorted)
-

---

## Open design questions

- Should boosts be able to break the rating ceiling (the >1 clamp)? Currently no — an
  ability can't lift a near-max stat further.
- Secondary **Deactivate / Increase** triggers exist as primitives but no shipped
  ability chains a secondary off/boost trigger yet — worth a worked example.
- Negative-only "flaw" abilities as a balancing / drafting mechanic (take a flaw for a
  stronger upside)?
- How are abilities acquired in the RPG layer — earned through play, drafted at
  character creation, unlocked by leveling? (Ties into [game_flow.md](game_flow.md).)
- Do opponents see your active abilities? Is there counter-play?
