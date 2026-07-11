# Sportland — Rival Managers

**Status:** Living design document
**Last updated:** 2026-07-11
**Scope:** The manager-players who run every AI club — built from the same parts as the player's character, with their own abilities, skills, and personalities that drive both their club's behavior and their presence on the field.

> Code-agnostic by design. Companion to `CharacterCreatorDesign.md` (the shared archetype system), `CalendarLeagueDesign.md` (the leagues rivals populate), and `AthleteDevelopmentDesign.md` (AI club roster behavior).

---

## 1. Purpose

Every AI club in every division is run by a **manager-player**: a character with the same dual nature as the player's — they manage their club *and* suit up as a playable athlete on their own roster. The league isn't a spreadsheet of opponent teams; it's a cast of rival coaches, each with a face, a style, and a club that reflects them.

This is deliberate symmetry: whatever the player's character can be, a rival can be. It makes the player's own choices legible ("he's a Superstar-type, like me — but he built his club completely differently") and gives the climb up the divisions a cast of recurring characters instead of anonymous fixtures.

## 2. Built From the Same Parts

A rival manager is assembled from the same data the character creator uses (`CharacterCreatorDesign.md`):

- **An archetype** — from the same roster (Superstar, Player-Coach, Tactician, Motivator, Mediator, Developer), with the same perks. The archetype system being data-driven pays off here: one definition set serves both the creator and the rival generator.
- **Athletic skills** — real per-sport stats; the rival is a genuine athlete on their team's roster, fielded (or benched) like anyone else.
- **Management stats** — governing how well their club actually runs: training quality, scouting accuracy, conflict handling.
- **A personality** — the layer that makes them *them* (Section 3).

**Notable rivals are authored; the rest are generated.** Each sport's upper divisions hold a handful of hand-crafted signature rivals — named characters with distinct looks, voices, and club philosophies, the "bosses" of the climb. Lower divisions and vacant slots fill with procedurally generated managers from the same parts, so every club has a face even if not every face is famous.

## 3. Personality — How a Manager Runs a Club

Personality is what turns one archetype into many different rivals. Proposed dimensions (each a rating, like athlete traits):

| Dimension | What it drives |
|---|---|
| **Recruiting style** | Star collector vs. youth developer vs. bargain hunter — what their roster looks like over time |
| **Aggression** | Poaching appetite: how actively they court *your* discontented players (the visible-poaching answer from `ConflictChemistryDesign.md` §9 — a high-aggression rival is who comes knocking) |
| **Loyalty** | Roster churn rate: some rivals keep a core for years (familiarity powerhouses), others flip rosters every season |
| **Temperament** | Rivalry flavor: gracious, fiery, trash-talking; how they react to wins, losses, and being knocked down a division |
| **Risk appetite** | In-game: gambles and trick plays vs. percentage calls; in the hub: dual-rostering multi-sport athletes, playing volatile egos |

A rival's club should *smell* like its manager: meet a Loyalty-A Developer's club and it's a family of homegrown veterans; a star-collecting Superstar's club is a glittering, fractious mess held together by his personal brilliance.

## 4. Rivals on the Field

The manager-player is on the roster, so the archetype threat is physical:

- A **Superstar** rival is a game-plan problem: their team runs through them, and stopping *the manager himself* is the assignment.
- A **Motivator/Mediator** rival's team plays above its stat lines — the roster looks beatable on paper and isn't.
- A **Tactician** rival calls the game hard: expect counters, tempo changes, and traps (their AI play-calling gets the expanded toolkit).
- A **Developer** rival's roster is scarier every time you meet it — the gap between September and March is the threat.
- A **Player-Coach** rival does a bit of everything and their bench runs deep.

Skip's pre-game scouting naturally extends to rival managers: *"Coach Vega calls the press late in close games — watch for it."* Knowing the manager becomes part of knowing the opponent.

## 5. Rivalry Over Time

Persistent managers + a promotion ladder = rivalry arcs for free:

- **Recurring meetings.** Division rivals are faced many times a season, and promotion means climbing into a new cast while old rivals chase you up (or wave from below).
- **History accrues.** Playoff eliminations, promotion deciders, poached players — the fixture list writes grudges without scripting. (How much the game *tracks and surfaces* this history is an open question; even a simple head-to-head record with flavor lines goes far.)
- **Pre/post-game presence.** Rival managers are the natural voice of the league: a line before the match, a reaction after, temperament-flavored.

## 6. System Dependencies

1. **A shared manager-player data model** — the player character and rivals are the same shape (archetype + athletic skills + management stats + personality), differing only in who controls them.
2. **Personality-driven club AI** — recruiting, retention, development, poaching, and roster churn decisions parameterized by the manager's personality and management stats (this implements `AthleteDevelopmentDesign.md` §7's "AI club roster behavior").
3. **Rivals as fieldable athletes** — sport modules treat a rival manager as a normal athlete on the opposing roster, with their archetype's in-game expression (especially Tactician play-calling and Superstar usage).
4. **Authored-content pipeline** — signature rivals defined as data (identity, look, dialogue flavor, fixed archetype/personality) layered over the procedural generator.
5. **Persistence** — rival managers survive across seasons with their clubs, promotions, relegations, and roster history.
6. **Rivalry memory (lightweight)** — at minimum, head-to-head records per rival manager for flavor and Skip intel.

## 7. Open Questions

- **Do rival managers have egos?** The player character and Skip are immune by rule; rivals presumably feel like personalities, but do their expectation traits mechanically matter (their locker rooms suffering their temperament), or is rival club mood abstracted?
- **Can a rival manager ever join you?** If their club folds or they're deposed — can a former rival be signed as an athlete (their management days done), and what happens to their perks? Memorable if yes; complicated if yes.
- **Manager progression.** Do rival managers develop over a career like their athletes do — a lower-division Tactician slowly climbing with you, or are they static anchors of their tier?
- **How much history is surfaced?** Head-to-head records only, or a real rivalry meter with escalating flavor (special pre-game scenes for old enemies in a promotion decider)?
- **Authored rival count.** How many signature rivals per sport, and are any *cross-sport* characters (a manager you battle in two leagues)?
- **The player's own vacancy.** Rivals are player-shaped; is the reverse true — when the player's club plays a sport the player doesn't personally suit up for, does the symmetry hold or is the player-character special?
