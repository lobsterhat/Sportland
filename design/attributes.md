# Sportland — Attributes

The character sheet. Every player carries a set of rated attributes; sport code reads
the **`Effective*`** values (base × ability/stamina multipliers) to drive the sim. This
doc is the reference for the attribute set, what each maps to in today's code, and
what's built vs. still to come.

> Loop reminder (see [README](README.md)): chat to think → this doc to persist → Claude
> memory to lock. Decided code-level facts live in memory `project_dodgeball_status.md`.

## Rating scale (0–20 → F–S)

Rated attributes use a **hidden 0–20 internal scale**, surfaced to players as a coarse
**F–S letter grade**:

| 0–2 | 3–5 | 6–8 | 9–11 | 12–14 | 15–17 | 18–20 |
|-----|-----|-----|------|-------|-------|-------|
| F | E | D | C | B | A | S |

Seven tiers, three ratings wide each, **no ± modifiers**. The in-grade ambiguity is
*intentional* — a low A and a high A both read "A", so players judge relative quality by
feel, not by a number.

- `Rating.cs`: `To01(v) = v/20` feeds the 0..1 gameplay math; `Grade(v)` returns the letter.
- Each rating is translated to its in-sim effect by a **per-stat mapping**. For a bounded
  physical output (e.g. Throw Power → release speed) that mapping is a **linear
  floor→ceiling** (rating 0 → 12 u/s, rating 20 → 36) — the conventional model. Elite
  differentiation comes from the **Special Abilities** layer, *not* a curved base stat.
- Abilities + stamina multiply the rating's 0..1 *before* translation (the `Effective*`
  pipeline), so a buff is felt everywhere with no per-system code.

## The three layers

1. **General attributes** — cross-sport; carried between sports.
2. **Sport-specific attributes** — e.g. dodgeball throw/catch skills.
3. **Special Abilities** — conditional multipliers on top. See [special_abilities.md](special_abilities.md).

## Cross-sport attributes (General)

| Attribute | What it is | Code today | Status |
|-----------|-----------|-----------|--------|
| **Stamina** | Size of the effort/gas tank — how long before fatigue bites | `maxEnergy` (shared pool) | exists; rename + convert to 0–20 |
| **Recovery** | How fast the tank refills, and how quickly you re-set after an action | `endurance` (regen) + the post-catch recovery timer | partial — split across two things; consolidate |
| **Speed** | Top movement speed | — (walk/run are fixed constants in `PlayerMovement`) | **new stat** |
| **Agility** | Quickness to change direction / *execute* a dodge | `changeOfDirection` | exists; rename + convert |
| **Damage Capacity** | How much punishment before you're benched | `toughness` (per-hit reduction) + `maxEnergy` (pool) | reframe |
| **Defensive Anticipation** | *Reading* an incoming throw — evasiveness | — (none) | **new mechanics to build** |

> **Stamina vs Damage Capacity share a pool today.** The current `energy` is one tank
> doing both jobs (fatigue *and* health in Energy/Hybrid mode). The model splits them:
> **Stamina** = effort for running, **Damage Capacity** = health for taking hits.

## Dodgeball attributes (sport-specific)

| Attribute | What it is | Code today | Status |
|-----------|-----------|-----------|--------|
| **Throw Power** | Release speed → how hard the ball arrives (and thus damage) | `throwSpeedRating` (0–20) | ✅ built; player-name rename pending |
| **Throw Technique** | Aim accuracy — tightness of the throw-scatter envelope | `throwAccuracyRating` (0–20) | ✅ built; player-name rename pending |
| **Catch Technique** | Sizes the catch timing window (see [defense.md](defense.md)) | `catching` (0–100) | converting now |
| **Offensive Anticipation** | *Leading* a moving target with your throw | `anticipation` (0–100) | exists; convert (+ maybe move to General) |

## Anticipation: offensive vs defensive

A single read/"game-sense" skill, split by side of the ball:

- **Offensive Anticipation** *(leading the target)* — already the entire job of today's
  `anticipation` stat: it powers `LeadAim` (predicting where a moving target will be)
  and shooter-quality routing (`ScorePotential01`).
  **100% offensive today** — there is no defensive use of it.
- **Defensive Anticipation** *(evasiveness)* — **new.** Right now evasion has *no*
  attribute behind it: a human dodge is pure input timing (smart-evade reads the throw's
  height and auto-picks duck/jump), and the AI reacts off fixed windows + its `catching`.
  This rating would scale how early/well you read an incoming throw — widening the evade
  and catch-arm windows, improving reaction.

Pairs with **Agility** for a clean **read-vs-execute** split on defense: Anticipation =
how early you read it, Agility = how fast you get out of the way. (Great reader / stiff
feet, or twitchy / slow to recognize — distinct archetypes.)

## Conversion status (0–20 rating scale)

- ✅ **Throw Power** (`throwSpeedRating`), **Throw Technique** (`throwAccuracyRating`) — on the scale, with F–S grades.
- ⏳ **Catch Technique** (`catching`), **Offensive Anticipation** (`anticipation`) — still 0–100; identical conversion when we get to them.
- 🆕 **Speed**, **Recovery**, **Defensive Anticipation** — new stats / mechanics, not just renames.
- 🔁 **Stamina**, **Agility**, **Damage Capacity** — exist under old names (`maxEnergy`, `changeOfDirection`, `toughness`); rename + convert.

## Open questions / parking lot

- **Player-facing names vs code fields.** Adopt Throw Power / Throw Technique / Catch
  Technique as the field names (`throwPowerRating`, …)? Cheap to align now, more churn later.
- **Which layer for the two Anticipations?** Lean **cross-sport** (reading an opponent
  generalizes to any sport), which would move Offensive Anticipation out of
  `DodgeballAttributes`. Undecided.
- **`luck`** exists in code (nudges catch success via `luckBonus`) but isn't on this list —
  keep as a hidden modifier, fold into something, or drop?
- **Recovery's scope** — one stat for both stamina regen *and* the post-catch recovery
  timer, or are those two different things?
- **Damage Capacity** — is it the size of the health pool, the per-hit reduction, or both?
