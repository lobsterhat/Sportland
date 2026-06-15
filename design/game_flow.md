# Game Flow — Design

Match structure, modes, win conditions, and the RPG / progression layer that wraps
matches. Canvas for cross-sport flow. The dodgeball match flow is built (branch
`dodgeball_ai`); the multi-sport and career layers are open.

---

## Built: dodgeball match flow

- **Modes** (GameMode presets):
  1. **Running Hits** — timed; most hits over the clock wins; no elimination.
  2. **Elimination** — N hits and you're out; last team with infielders standing wins.
  3. **Energy** — hits drain energy (softened by toughness); 0 energy = out.
  4. **Hybrid** — timed; a hit sidelines you, a team catch revives the bench.
- **Clocks:**
  - Per-team **shot clock** — stalling on offense → referee transfer.
  - **Delay-of-game** — a loose ball sitting too long (suppressed in turnover-only modes).
  - **Referee reset** — on any clock expiry the ref pauses play, takes the ball, and
    hands it to a player on the other team; game time stops during the handoff.
- **Possession / scoring:** hit / catch / opposing-infield rules; per-mode
  turnover-vs-point (`GameMode.clockExpiryEffect`); bonus when a throw hits/catches an
  outfielder who's in the opposing infield.

---

## Open: cross-sport flow

Questions the multi-sport layer needs to answer. Spitball below.

### Match structure
- Periods / halves / quarters — per sport, or a unified shell?
- Timed vs target-score vs elimination — which sports default to which?
- Overtime / tiebreak rules?
- Is there a shared "core loop" (face-off / serve / tip-off → possession → score) that
  every sport specializes?

### The RPG / career layer
- What wraps a match — season, tournament, story mode, exhibition?
- Roster management: draft, trade, train between matches?
- How do players grow — XP, attribute training, ability unlocks, aging/decline?
- Persistence: how is the player pool stored across matches and sports? (The
  `PlayerProfile` asset thread — see special_abilities.md / Claude memory — is the
  likely home for a character's stats + abilities across sports.)

### How abilities surface in flow
- Earned/unlocked through play, or chosen at character creation?
- Per-match loadout choices, or fixed to the character?
- Visible to the opponent? Any counter-play or bans?

---

## Idea backlog

Dump raw ideas here; promote them into the sections above as they firm up.

### Modes (any sport)
-

### Progression / meta
-

### Parking lot (raw, unsorted)
-
