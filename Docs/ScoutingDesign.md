# Sportland — Scouting Design

**Status:** Living design document
**Last updated:** 2026-07-11
**Scope:** How the player learns what's hidden — the scouting channels (attending matches, 1v1 invitationals, direct contact, and more), what each reveals, and how scouting doubles as the delivery vehicle for individual sports and mini-game sports.

> Code-agnostic by design. Implements the discovery rules of `ConflictChemistryDesign.md` §2.2 and `AthleteDevelopmentDesign.md` §3 (hidden expectations and ceilings); spends the actions of `HubActionsDesign.md`.

---

## 1. Purpose & Principle

A lot of Sportland's most valuable information is hidden: expectation traits, per-sport ceilings, volatility, rival tendencies. Scouting is how the player buys that information — and the governing principle is:

**Scouting is something you *do*, not a menu you click.** Every channel is an activity — going somewhere, playing something, talking to someone. The channel you choose determines the *kind* of information you get, so scouting has texture: different questions send you to different places.

## 2. What Scouting Reveals

Knowledge about an athlete is a per-target report that deepens with attention:

- **Current ability** — stat readings sharpen from fuzzy ranges to exact values.
- **Ceilings** — per-sport potential (`AthleteDevelopmentDesign.md` §3).
- **Expectation traits & volatility** — the ego card (`ConflictChemistryDesign.md` §2).
- **Condition** — fatigue, injury susceptibility (overlaps the Hospital checkup).
- **Tendencies** — for rival teams and managers: play-calling habits, in-game patterns (`RivalManagersDesign.md`), feeding Skip's pre-game intel.
- **Willingness signals** — how receptive a target would be to a pitch.

Scouting yield scales with the scouting management stat, the Developer's *Growth Eye*, and Skip's presence. Repeated scouting through different channels assembles the full picture — no single channel reveals everything.

## 3. The Channels

### 3.1 Attend a match (A)

Go watch a league game **not involving your team** — the fixture calendar of every division is real, so any game is somewhere to be. Best for **on-field truth**:

- Sharp readings of current sport skills and athletic stats for athletes you focus on.
- **Ego tells in the wild**: watch a target wave off his own coach's substitution or freeze out a teammate — expectation traits revealed by observed behavior, the away-game version of the discovery rules.
- Team/manager tendencies — the same trip scouts your next opponent's schemes. Player scouting and match prep are one activity with two reads.

Costs the action and the travel — you watched someone else's game instead of running practice. A focused target per attendance keeps it a choice, not a vacuum cleaner.

### 3.2 The 1v1 invitational (A) — and the mini-sport Trojan horse

Invite a target to something casual and competitive: **golf, bowling, table tennis** — played, by you, as an actual mini-game. Best for **character truth**:

- **Personality under mild pressure.** Nobody's ego hides across eighteen holes: the guy who rage-taps the ball return after an open frame just told you his Volatility; gracious losing tells too. Character reads come from *how the game goes*, which the player watches firsthand.
- **Conversation between frames.** Expectation traits and willingness hints surface naturally — the social layer of the interview, relocated somewhere disarming.
- **Relationship on the side.** A good afternoon raises a free agent's willingness or an own-player's familiarity — scouting, recruiting, and bonding in one action.
- **Athletic fundamentals leak.** Elite reaction time shows up at the table-tennis table; raw power shows in a drive. Fuzzy but real cross-sport stat signals.

**This channel is how individual sports enter the package.** Each 1v1 game is a lightweight sport module — small scope, quick sessions — that earns its place through scouting and then gets reused everywhere (Section 4).

### 3.3 Direct contact (A)

The interview/schmooze from `HubActionsDesign.md` — sit down and talk. Best for **intentions**: willingness, role hopes, a chance at an expectation trait. Cheapest and most direct, least good at anything the athlete would rather you not know; people manage their own image in interviews in a way they can't mid-bowling-meltdown. Disposition-gated (`ConflictChemistryDesign.md` §2.3): high-Openness athletes spill readily; private ones are nearly interview-proof — take *them* bowling, and note that a high-Competitive target rarely turns down a 1v1 challenge.

### 3.4 Additional channels (proposed)

- **Skip's film room (F, passive).** Skip is a scout — assign him a watchlist and he grinds background film: slow, shallow, free, and steady. Ambient intel that nudges reports along without spending actions; his hint hooks already exist.
- **Trial day (seasonal event).** A preseason combine where free agents show themselves — a burst of shallow reads across many athletes at once, the wide-net complement to the deep single-target channels.
- **Word of mouth (F, ambient).** Sign a player, learn his former teammates: your roster's history quietly seeds partial reports on athletes they've played with. Locker rooms talk.

## 4. Mini-Sports — Small Modules, Big Reuse

The 1v1 roster (golf, bowling, table tennis) and mini-game sports (**rowing, rock climbing**) are deliberately cheap to build relative to team sports — and every one pays multiple ways:

1. **Scouting venue** (Section 3.2) — their entry point into the package.
2. **Team events.** The Cafe's chemistry-building event (`HubActionsDesign.md` §4) gets real: bowling night *is* the bowling module with the guest list and pizza. The guest-list rules apply — small gatherings can double as individual scouting reads; big parties are chemistry-broad and information-shallow.
3. **Training minigames.** Rowing is a conditioning session you can actually play; rock climbing reads as strength/agility work — individual training sessions with hands on the controller.
4. **Their own competitions, later.** Nothing stops a bowling ladder or a climbing meet becoming a real fixture on the calendar wheel eventually — the module's already built, and athletes carry ratings in these sports like any other (a fun tell: your point guard is a secret table-tennis monster).

This is the scope-engineering play: team sports are expensive; mini-sports let the package grow variety (and the calendar grow texture) at a fraction of the cost, with scouting as the reason each one exists from day one.

## 5. System Dependencies

1. **A per-athlete knowledge model** — what the player knows about each athlete/manager, with per-fact reveal state and sharpening precision (extends the reveal-state dependency of `ConflictChemistryDesign.md` §8).
2. **Channel actions in the action economy** — attend-match, 1v1 invitational, and interview as costed actions; film room and word of mouth as passive feeds.
3. **Attendable league fixtures** — other divisions' games exist as calendar events the player can spend a day at (the AI league already simulates them; this makes them destinations).
4. **A lightweight mini-sport module framework** — the sport-module pattern scaled down: quick setup, 1–2 participants (or a small group for events), minutes-long sessions, reusable across scouting/events/training.
5. **Observation → reveal hooks** — behavior during watched matches and 1v1s emitting trait reveals (the observed-behavior counterpart of in-game refusal reveals).
6. **Relationship side-effects** — 1v1 and interview outcomes feeding willingness/familiarity.

## 6. Open Questions

- **Report fallibility.** Are low-confidence readings ever *wrong* (a scout's misjudgment to be corrected by deeper looks), or just vague? Wrong is more realistic and crueler; vague is friendlier.
- **Mini-sport launch roster.** Which two or three ship first? (Proposal: bowling and table tennis — cheap, readable, high personality-expression; golf and the mini-game sports follow.)
- **Does your performance matter in a 1v1?** Does blowing out the target (or getting blown out) change what you learn or how willingness moves — should the player *manage* the match socially (keep it close, let him win?) as its own little game?
- **Can you 1v1 your own players?** (Proposal: yes — it's a familiarity builder and re-scout, overlapping the Cafe one-on-one; may just be the same action with a venue choice.)
- **Skip's watchlist depth.** How many athletes Skip can grind at once, and whether his film room ever reveals ego traits or only ability.
- **Simulated match watching.** Is attending a match a playable/watchable scene (even a stylized recap) or a report with flavor text? Big presentation cost either way — worth deciding late.
