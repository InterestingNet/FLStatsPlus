# FLStats Plus

A Dalamud plugin for FFXIV PvP stats. It's a fork of [PvP Tracker (PvpStats)](https://github.com/wrath16/PvpStats) by SaMo, so it keeps **everything PvpStats does** for Crystalline Conflict, Frontline and Rival Wings, and adds:

- **Frontline personal combat log** – every skill you hit with, who you hit, for how much (crit / direct hit / block / parry), every hit you took and from whom, DoT/HoT ticks, your kills and deaths with the killing blow.
- **HTML match reports** – one self-contained `.html` file per match or for your whole history. Opens in any browser, works offline, and can be shared or downloaded.
- **JSON and CSV export** of full 72-player scoreboards and your combat log.
- **Saves data PvpStats throws away** – Wolf Marks, Series XP and Job XP per match.
- **Better IPC** for other plugins (see below).

## Installing (for players)

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste the repo URL below, tick **Enabled**, then click **+** and **Save**:
   `https://raw.githubusercontent.com/OWNER/REPO/BRANCH/repo.json`
3. Type `/xlplugins`, search for **FLStats Plus**, and click **Install**. Dalamud updates it automatically when a new version is released.

The custom-repository link only works while the GitHub repository is **public**. With a private repo, players need GitHub access to download `latest.zip` from the Releases page, and must load it as a dev plugin (see *Building*).

## Releasing a new version (maintainer)

Every push is built by GitHub Actions; the plugin zip appears under **Actions → Build → Artifacts**. To publish a version players get automatically, open **Actions → Build → Run workflow**, enter a version such as `0.1.0`, and click **Run workflow**. Pushing a `v0.1.0` tag does the same.

The workflow then builds that version, creates the `v0.1.0` tag and a GitHub Release with `latest.zip`, and updates `repo.json` on the default branch. Dalamud sees the new version number and offers the update.

## Commands

Renamed from PvpStats so both plugins can be installed side by side.

| Command | What it does |
|---|---|
| `/flp` | Launcher |
| `/flpfl` · `/flpcc` · `/flprw` | Frontline / Crystalline Conflict / Rival Wings tracker |
| `/flplast` | Details of your last match |
| `/flpconfig` | Settings |
| `/flpexport` | Export your last Frontline match as HTML and open it |
| `/flpexport all` | Export your whole Frontline history as one HTML report |
| `/flpexport json` · `/flpexport csv` | Whole Frontline history as JSON / CSV |
| `/flpexport folder` | Open the export folder |

There are also export buttons in the **Frontline tracker** (exports whatever your filters currently show) and in each **Frontline match details** window. Under *Settings → Enhanced Tracking Features* you can turn the combat log on or off and auto-export a report after every match.

Exports go to `%AppData%\XIVLauncher\pluginConfigs\FLStatsPlus\exports\`.

## The HTML report

Tabs, all with the same filters (map, your job, result, dates, tags, favorites, another player as teammate/opponent):

- **Matches** – your match list. Click one to open it.
- **Summary** – placements, average place, KDA, kill participation, average performance (per match / per minute / median team contribution), jobs, maps, placement trend over time, teammates' and opponents' jobs, top teammates and opponents, Wolf Marks and Series XP earned.
- **Records** – streaks, longest/shortest match, combat-point records, momentary lead/deficit, fastest Battle High V, biggest Battle High chain, most of every stat (total and per minute), plus combat-log records (biggest hit, most killing blows, best crit rate).
- **Jobs** / **Players** – PvpStats' job and player tables, for you, teammates, opponents or everyone, with heat-map colouring.
- **Meta** – how evenly teams share kills, damage and healing (top-1 / top-4 share and Gini coefficient).
- **Combat** – your skills across all logged matches, skills used on you, damage and kills by enemy job, players you hit most and players who hit you most.
- **Match Timeline** (top of Match Detail) – one integrated timeline with a single time axis and zoom window. Its tracks are **team points** for all three teams, **your Battle High**, **your damage dealt/taken** (hits + ticks), and **one lane per ability**. Your deaths (☠) and kills (⚔) cut through every track; click one for a recap of the last 10 seconds. Hovering shows a readout of every track at that moment.
- **Ability lanes** (part of the Match Timeline) – one lane per skill, one bubble per cast. Bubble size is the **total damage of that cast across every player it hit, including the DoT ticks it applied**, and the number inside is how many players it hit, so a 12-player Bioblaster at Onsal mid stands out. Each cast's ticks are drawn as a gold trail of tick marks after it. Each tick is credited to the cast that applied it: the latest earlier cast (within 60 s) that hit the same player and applied that status, or shares the status's name. Ticks that can't be matched get their own "(ticks)" lane, so nothing is dropped. Grey bars show when a skill was on cooldown, green bars show when it was ready but you hadn't used it, and dashed lines mark your deaths and kills. You can zoom to a 30 s / 1 / 2 / 5 minute window and hide small casts. Click a cast to see every player it hit, how much each took, crits, kills, their HP before the hit, your position, and how long the skill had been ready. **Biggest Casts** lists your top casts in the match, and the **Combat** tab lists your best casts across all matches.
- **Match Detail** – team points breakdown, team points and Battle High graphs, full 72-player scoreboard (grouped by team or alliance, totals, % of team), and your combat log: damage timeline, death and kill recaps, skill → target drill-down, and a filterable full log you can download as CSV.

**Merge export…** loads another FLStats Plus export (for example from another character) into the same view.

A history report embeds the raw combat log when it has 25 matches or fewer. Above that it embeds per-match combat summaries only, to keep the file small. Single-match reports always include the full log.

## Reflected damage (Chiten etc.)

When someone hits you while a reflect status such as Chiten is up, the damage it sends back is counted as **your damage dealt** to that attacker. Reflected damage that kills the attacker also counts as the killing blow. On the timeline, each Chiten activation is one purple bubble placed when you used it. The bubble's size is the total reflected, the number is how many attackers took damage, and each reflect is marked after it. Clicking the bubble lists every attacker and how much they took. Reflects are also in Skills (marked *reflected*), Targets and the combat log. To recognise other jobs' reflect statuses, add their names to `ReflectStatusNames` in `FrontlineCombatLogger.cs`. Until then they're labelled "Reflected damage".

## Shields

The game reports a hit's damage **after** shields, so a hit fully soaked by a shield shows as 0. The plugin reads the victim's shield before the hit and again from the game's effect-result message after it, then adds the absorbed part back. Totals therefore include shield damage, as the scoreboard does. The shield part is shown separately: in the cast breakdown, a *To Shields* column in Skills, the combat log ("(x shield)"), and the Damage dealt/taken tiles. Shields are reported in whole percent of max HP, so each value is accurate to about 1% of the victim's max HP.

## What the combat log can and can't see

The log records events **involving you**. The game only sends your client events for players near you, so you cannot see fights elsewhere on the map. The end-of-match scoreboard still covers all 72 players. Logged damage can be higher than the scoreboard's "damage to PCs", because the log also counts hits on NPCs and objects.

## Performance

The plugin is built to stay out of the game's way:

- **The hooks bail out almost instantly on events that aren't yours.** The combat-log hooks see every hit, tick and status event from all ~72 players and every NPC. Each one first checks whether you are involved, using only a few integer comparisons on game memory: your entity ID, your pets' IDs, and the target IDs in the packet. Anything else returns right away with no allocations or lookups. Names, world lookups and skill data are only fetched for your own events, and they are cached for the match.
- **No disk writes during a match.** The log is kept in memory, then compressed once on a background thread when the match ends. That's about 50 KB per match instead of roughly 1.3 MB as a raw database document.
- **Less per-frame work than PvpStats.** PvpStats scanned every player's status list every frame to track Battle High. Here that scan runs twice a second in a single pass, and players already recorded at Battle High 0 are skipped.
- **Exports never run on the game thread.** Building HTML, JSON or CSV happens in the background, and auto-export is off by default. The HTML report opens in your browser, not in the game.
- **Built-in overhead meter.** *Settings → Enhanced Tracking Features* shows, after each match, how many game events the hooks saw, how many were recorded, and the total and per-frame time spent in the hooks. The same line goes to `/xllog`.
- **Can be turned off.** Unticking *Frontline Personal Combat Log* stops all recording. The hooks stay installed but return immediately.

## IPC (for other plugins)

| Gate | Type | Notes |
|---|---|---|
| `FLStatsPlus.OnMatchSaved` | message `(string id)` | Fires when a match **starts** (inherited from PvpStats) |
| `FLStatsPlus.OnMatchCompleted` | message `(string id)` | Fires when a completed match is saved (final scoreboard). It also fires again if the match is edited, for example by tags or favorites. |
| `FLStatsPlus.OpenMatchDetailsWindow` | func `(string id) → bool` | |
| `FLStatsPlus.GetMatchJson` | func `(string id) → string?` | Frontline matches use the export schema, including the combat log |
| `FLStatsPlus.ExportMatchHtml` | func `(string id) → string?` | Writes the HTML report and returns its path |

The data format is described in [`docs/EXPORT_SCHEMA.md`](docs/EXPORT_SCHEMA.md).

## Building

You need the .NET 10 SDK and Dalamud's dev files (installed by XIVLauncher with Dalamud, or set `DALAMUD_HOME`).

```
dotnet build FLStatsPlus.sln -c Release
```

The plugin zip is at `PvpStats/bin/Release/FLStatsPlus/latest.zip`. To test in game, add the folder containing `FLStatsPlus.dll` under *Dalamud Settings → Experimental → Dev Plugin Locations*.

GitHub Actions (`.github/workflows/build.yml`) builds every push and uploads the zip as an artifact.

### Previewing the report template without the game

```
python3 tools/make_sample_report.py PvpStats/Export/report_template.html sample.html 60
```

## Status / to verify in game

This first version was written without access to a compiler or the game, so check these before you rely on it:

1. It compiles (push to GitHub to let the workflow build it, or build locally).
2. The combat log fills in during a Frontline match. Check by exporting a match and looking at the Combat Log section.
3. Crit and direct-hit flags look right. They use Death Recap's bit layout.
4. DoT ticks link to the right casts. Tick linking uses the status ID each hit applied (read from the effect's value field) and falls back to matching names and timing.
5. The killing blow is attributed to the right hit.
6. The overhead meter in settings shows small numbers after a match. The expected range is well under 0.05 ms per frame. It is matched to the last hit on the same player within 5 seconds of the death.

## Credits

Based on PvP Tracker (PvpStats) by SaMo. See `NOTICE.md`. The original README is in `docs/PvpStats_README.md`.
