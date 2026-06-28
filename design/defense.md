# Sportland — Defense (Catch / Evade / Brace)

How a defender deals with an incoming throw. Three options with a real risk/reward
between them, and **no RNG for the human** — outcomes come from positioning and
timing; stats change the *windows*, not the dice.

> Status: **designed (this doc), not yet built.** Replaces the current additive
> catch-chance model in `Ball.BuildCatchFactors` (base chance − speed/facing/stance/
> timing/luck). See [attributes.md](attributes.md) for the stats this leans on.

## The decision

Facing an incoming throw, the defender picks:

- **Catch** — go for the ball. High reward (possession), but you must land the timing
  window, and a hot or awkward ball can bobble or hit you.
- **Evade** — get out of the line. Safe (no hit), but you concede the ball.
- …plus a posture, **Brace**, that biases you toward catching (below).

You lean Evade when a catch is a bad bet: ball too fast, you're facing wrong, low on
health, or the HUD says this character has weak hands.

## Catch — a zoned timing window

A window of time **anchored to the ball's arrival** (not the button press), so it's
forgiving on both ends:

```
   miss │ bobble │   CLEAN CATCH   │ bobble │ miss
  too early ◄──── ball arrives ────► too late
```

- **Clean core** → secure the ball.
- **Bobble edges** (pressed a little early/late) → you tip it; pops loose / caroms off
  you, stays live.
- **Outside** → miss → it hits or caroms.

Mechanics:
- **Two-sided forgiveness.** A press *before* arrival is remembered (armed); the ball
  stays catchable for a brief grace *after* it reaches the body, so a slightly late
  press still snags it. Keep the post-grace small (~0.1 s) or the ball visibly "waits."
- **Catch Ability (`catchTechniqueRating`)** sizes the window *and* the clean core —
  soft hands = wide + forgiving; weak hands = a sliver, mostly bobbles.
- **Ball speed shrinks the window** — fast balls are harder because you get less margin.
  This *replaces* the old flat speed penalty (the difficulty is now emergent).
- **Facing / stance pinch the clean core** — back-facing ⇒ no clean core (always a
  bobble/carom); flat-footed ⇒ narrower.
- **Deterministic for the human** (position + timing) — no RNG; player skill aims at the
  window, which `catchTechnique` sizes. The cap + speed punch-through apply here.
- **The AI does NOT emulate a press** — it has no button to time. It runs a **skill
  check**: the character's catch skill vs the throw's difficulty (speed + facing) gives
  `catchChance` (hand on it = clean or bobble) and `cleanShare` (secure vs fumble); roll
  once for the zone. `P(clean)` is clamped to `[2% … 95%]` — a giant-killer floor and a
  sure-thing cap. The grade×grade grid is **authored** (e.g. F-vs-S `2/5/93`, S-vs-F
  `95/5/0`, F-vs-F `17/68/15`), not reverse-engineered from a timing distribution. This
  replaced the old emulated-press model, which made the AI's outcomes a tuning cliff and
  couldn't give a weak AI a reachable floor. Both paths emit the same `CatchZone`.

## Brace — posture, ramped

Grows out of the existing defensive-stance trigger (face-the-ball, 80% speed),
changed from a **toggle to a hold**:

| Posture | Input | Catch window | Evade? |
|---------|-------|-------------|--------|
| **Braced** | hold + planted (ramps over ~0.3–0.5 s) | **max** | **no** — committed |
| **Mobile stance** | hold + moving (80% speed) | modest | yes |
| **Loose** | nothing | small | full |

- The brace **ramps** — the window climbs the longer you hold still, so you must plant
  *in anticipation*. A last-instant plant gives only a partial brace; caught mid-move,
  none. The ramp time is the commitment cost.
- **Planted = can't dodge.** To bail you release + move → a *slower* escape dash
  (existing `dashOutOfStanceScale = 0.5`) because you over-committed.
- **Defensive Anticipation** shortens the ramp / boosts the braced window — the read
  stat helping you *catch*, not just evade.

## Evade — one dash, off the line

A single evade: dash out of the ball's path (direction from the stick).

- **Agility** = how far/fast you move (execution).
- **Defensive Anticipation** = how early you read it / how forgiving the evade timing is
  (the read). No auto-dodge for the human — your input + timing, with Anticipation
  buying reaction *time*. **AI** rolls its dodge against the stat.

> We deliberately dropped **Duck/Jump-by-height**: it needs high *and* low attacks we
> don't have (our throws are chest ≈1.29 and a waist spike ≈0.9 — no head-hunter, no
> leg-skimmer) plus a height tell to read them. **Jumping stays a purely offensive
> ability.**

## Caroms

Every failure stays live: a mistimed catch bobbles off you, an un-evaded ball
hits/deflects — head/torso/limb per the existing zone bounce (`ResolveMiss` / `Carom`).
No clean pass-throughs. (This is the chaos we want to keep.)

## Damage & impact

When a ball is thrown at an opposing player there are **five fates**, crossing the
*contact* (did the ball touch them, and how) with the *rebound* (caught or grounded):

| # | Contact | Rebound | Points game | Damage game |
|---|---------|---------|-------------|-------------|
| 1 | **Connects** (body / beat the hands) | hits ground | attacker scores | **more** dmg |
| 2 | **Connects** | caught (self/teammate) | no score — *save* | **more** dmg |
| 3 | **Mishandled** (hands, fumbled) | hits ground | attacker scores | **less** dmg |
| 4 | **Mishandled** | caught (self/teammate) | no score — *save* | **less** dmg |
| 5 | **Misses** completely | hits ground | — | none |

Plus the defensive win not in the table: a **clean interception** (caught in the hands,
no prior contact) → no damage, gains possession, outs the thrower in the points game.

**The key rule: catching the rebound does NOT undo the damage.** The impact already
happened (1≡2, 3≡4 deal the same damage). A catch only governs **points & possession** —
in the points game it *saves the point* and outs the thrower; in the damage game it just
recovers the ball. So a teammate snagging your rebound saves the point but you still
*took the hit*.

**Maps onto the catch zones:**

- `Clean` → clean interception → **no damage** (the defensive win).
- `Bobble` → **mishandled** → **less** damage (chip), still recatchable.
- `Miss` → **connects** (body hit) → **more** damage. Carom *and* deflect both deal it —
  the backward glance is just rebound *direction*, never a damage exemption.
- ball wide of the body / no attempt → **misses completely** → none.

**Damage formula (damage game / Mode 3 Energy):**

```
dmg = ballSpeed × damagePerSpeed × contactMul × throwerSting × (1 − victimToughness × reduction)
```

- **contactMul** — connect (`Miss`) = 1.0; mishandle (`Bobble`) ≈ ⅓ (`bobbleDamageMul`).
- **throwerSting** — a future thrower attribute, "knack for tender places" (a damage
  multiplier, and/or a bias toward the high-damage **Head** zone). Stubbed at 1.0 for
  now, same pattern as the ability `Effective` hooks.
- **ballSpeed / victimToughness** — already in (`damagePerSpeed`, Damage Capacity).

**Impact is immediate, never deferred/wiped.** Damage + the stamina sap below land at
the moment of contact (`OnImpact`), decoupled from the deferred `OnHit` that the points
game uses for scoring + the catch-save. (This also kills the lab artifact where a frozen
dummy recovered its own carom and nullified the damage.)

**Endurance sap — every impact, BOTH games.** Any contact (connect or mishandle, caught
or not) drains the victim's stamina pool (`PlayerStamina`), scaled by `speed × contactMul`.
Getting hit tires you even when your team catches the rebound and saves the point.

## Stat map

| Stat | Drives |
|------|--------|
| **Catch Ability** (`catchTechniqueRating`) | catch window size + clean-core |
| **Agility** | evade dash distance / speed |
| **Defensive Anticipation** | read / lead-time on catch & evade; brace ramp |
| **Throw Power** (attacker) | ball speed → shrinks the defender's windows |

## Build order

1. **`catching → catchTechniqueRating`** (0–20 + grade) — mechanical, isolated. ← *first*
2. **Zoned catch window** — replace the additive `BuildCatchFactors` chance with the
   arrival-anchored clean-core/bobble/miss model. The core change; playtest in isolation.
3. **Ramped brace** — stance toggle → hold + plant ramp; window scales with brace.
4. **Evade dash** on Agility + the Def-Anticipation lead-time; collapse the old
   duck/jump/dodge verbs into one evade.
5. **Defensive Anticipation** as a real 0–20 stat (+ AI dodge roll).

Explicitly *not* building: throw-height attack dimension, high/low tell, Duck/Jump defense.
