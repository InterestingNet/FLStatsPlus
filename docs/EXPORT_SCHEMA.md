# FLStats Plus export schema (v1)

This one format is used by the HTML report (embedded in `<script id="report-data">`), the JSON export, and the `GetMatchJson` IPC. Field names are camelCase. Fields with no value are left out.

```jsonc
{
  "schema": 1,
  "plugin": "FLStats Plus 0.1.0.0",
  "generated": "2026-11-01T10:00:00Z",
  "mode": "match" | "history",
  "matches": [ Match, ... ]
}
```

## Match

| field | type | notes |
|---|---|---|
| id | string | LiteDB ObjectId |
| dutyStart, start | ISO time (UTC) | `start` = match start; every `t` below is seconds after it |
| durationSec | int | |
| map / mapName / mapType | string | e.g. `SealRock` / `Seal Rock` / `Seize` |
| dataCenter, gameVersion, pluginVersion, localPlayer | string | `localPlayer` = "Name World" |
| favorite, tags | bool, string | |
| wolfMarks, seriesXP, jobXP | int | from the results screen (new in FLStats Plus) |
| battleHighVTimeSec, battleHighSpike, battleHighSpikeTimeSec, momentaryPointAdvantage, momentaryPointDeficit | number | values PvpStats pre-computes |
| teams | `{ "Maelstrom": Team, "Adders": Team, "Flames": Team }` | |
| players | Player[] | all players on the scoreboard (normally 72) |
| teamPoints | `{ team: [[t, points], ...] }` | team score over time |
| battleHigh | `[[t, battleHighPoints], ...]` | your Battle High points over time (Battle High V at 100) |
| combatSummary | CombatSummary | per-match rollup of your combat log |
| actions | `{ actionId: { name, recastSec, maxCharges, icon } }` | cooldown info for the ability timeline |
| combat | CombatEntry[] | raw combat log (single-match reports, JSON exports, small history reports) |

**Team**: `placement` (0 = 1st), `totalPoints`, `killPoints`, `deathPointLosses`, `occupationPoints`, `targetablePoints` (ice / tomeliths), `dronePoints`.

**Player**: `name`, `world`, `job` (e.g. `MCH`), `role` (`TANK`, `HEALER`, `MELEE`, `RANGED`, `CASTER`), `team`, `alliance` (0–2 = A–C), `isYou`, `kills`, `deaths`, `assists`, `damageDealt`, `damageToPCs`, `damageToOther`, `damageTaken`, `hpRestored`, `special`, `occupations`, `claimTimeSec` (Worqor Chirteh), `maxBattleHigh` (0–5).

## CombatEntry

| field | notes |
|---|---|
| t | seconds after match start |
| dir | `out` = you hit someone, `in` = someone hit you, `self` = you on yourself |
| kind | `action`, `dot`, `hot`, `death` (`death` + `out` = you killed `other`; `death` + `in` = `other` killed you) |
| id, name, icon | Action id/name/icon; for DoT and HoT ticks these are the Status's |
| pet | true if your pet or summon did it |
| other, otherPlayer, otherJob, otherTeam | the other party ("Name World" for players) |
| otherHp, otherMaxHp | the other party's HP when the packet arrived (before the hit) |
| dmg, heal, mpDrain, mpGain | amounts |
| crit, dh, blocked, parried, miss, invuln | hit flags |
| statusApplied, statusResisted | number of status effects the action applied or that were resisted |
| kb | this hit was the killing blow |
| seq | server effect sequence number. Every target one cast hit shares it, so it is how AoE hits are grouped into a single cast |
| x, z | your position when the event arrived |

## CombatSummary

`dealt`, `taken`, `selfSkills`: SkillRow[] (`name`, `kind`, `icon`, `hits`, `damage`, `heal`, `crits`, `directHits`, `maxHit`, `killingBlows`).
`targets`, `sources`: OtherRow[] (`name`, `job`, `team`, `hits`, `damage`, `heal`, `killingBlows`).
`kills`, `deaths`: ints.
`topCasts`: your 15 biggest casts: `t`, `id`, `name`, `targets`, `damage`, `heal`, `crits`, `kills`, `x`, `z`, `hits[]` (`other`, `job`, `team`, `dmg`, `heal`, `crit`, `dh`, `kb`).
Rows in `dealt` also carry `casts`, `maxTargets` and `bestCast`.
