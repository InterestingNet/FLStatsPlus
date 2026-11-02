using LiteDB;
using Lumina.Excel.Sheets;
using PvpStats.Helpers;
using PvpStats.Types.Match;
using PvpStats.Types.Match.Timeline;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JsonSerializer = System.Text.Json.JsonSerializer;
using System.Threading.Tasks;

namespace PvpStats.Services;

/// <summary>
/// FLStatsPlus: exports Frontline matches as a self-contained HTML report, JSON or CSV.
/// The HTML report is <c>Export/report_template.html</c> with the match data embedded as JSON.
/// </summary>
internal class ExportService {

    internal const int SchemaVersion = 1;
    private const string TemplateResource = "FLStatsPlus.report_template.html";
    private const string DataPlaceholder = "/*__FLSTATS_DATA__*/null";

    private readonly Plugin _plugin;
    private readonly Dictionary<uint, string> _jobAbbrCache = [];

    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new ObjectIdConverter(), new JsonStringEnumConverter() },
    };

    internal ExportService(Plugin plugin) {
        _plugin = plugin;
    }

    internal string ExportDirectory {
        get {
            var dir = Path.Combine(_plugin.PluginInterface.GetPluginConfigDirectory(), "exports");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    internal List<FrontlineMatch> AllFrontlineMatches() =>
        _plugin.FLCache.Matches.Where(IsExportable).OrderBy(x => x.DutyStartTime).ToList();

    private static bool IsExportable(FrontlineMatch m) => m.IsCompleted && !m.IsDeleted && !m.IsQuarantined;

    // ───────────────────────────── public export entry points ─────────────────────────────

    internal async Task<string?> ExportLastFrontlineMatchHtml() {
        var last = _plugin.FLCache.Matches.Where(IsExportable).OrderByDescending(x => x.DutyStartTime).FirstOrDefault();
        return last == null ? null : await ExportFrontlineMatchHtml(last);
    }

    /// <summary>One match, including the full combat log. Opens straight to the match view.</summary>
    internal Task<string?> ExportFrontlineMatchHtml(FrontlineMatch match, FrontlineMatchTimeline? timeline = null) {
        return Task.Run<string?>(() => {
            timeline ??= _plugin.FLCache.GetTimeline(match);
            var report = new ReportDto {
                Mode = "match",
                Matches = [BuildMatch(match, timeline, includeRawCombat: true)],
            };
            var name = $"FL_{Stamp(match.DutyStartTime)}_{Safe(MatchHelper.GetFrontlineArenaName(match.Arena))}.html";
            return WriteHtml(report, name);
        });
    }

    /// <summary>Many matches with every flstats tab (Matches, Summary, Records, Jobs, Players, Meta) plus per-match detail.</summary>
    internal Task<string?> ExportFrontlineHistoryHtml(IEnumerable<FrontlineMatch> matches) {
        return Task.Run<string?>(() => {
            var list = matches.Where(IsExportable).OrderBy(x => x.DutyStartTime).ToList();
            if(list.Count == 0) return null;
            // raw logs make large reports; history keeps per-match combat rollups instead,
            // unless the selection is small enough to embed everything
            bool raw = list.Count <= 25;
            var report = new ReportDto {
                Mode = "history",
                Matches = list.Select(m => BuildMatch(m, _plugin.FLCache.GetTimeline(m), raw)).ToList(),
            };
            return WriteHtml(report, $"FL_history_{Stamp(DateTime.UtcNow)}_{list.Count}matches.html");
        });
    }

    internal Task<string?> ExportFrontlineJson(IEnumerable<FrontlineMatch> matches) {
        return Task.Run<string?>(() => {
            var list = matches.Where(IsExportable).OrderBy(x => x.DutyStartTime).ToList();
            if(list.Count == 0) return null;
            var report = new ReportDto {
                Mode = list.Count == 1 ? "match" : "history",
                Matches = list.Select(m => BuildMatch(m, _plugin.FLCache.GetTimeline(m), true)).ToList(),
            };
            var path = Path.Combine(ExportDirectory, list.Count == 1
                ? $"FL_{Stamp(list[0].DutyStartTime)}_{Safe(MatchHelper.GetFrontlineArenaName(list[0].Arena))}.json"
                : $"FL_history_{Stamp(DateTime.UtcNow)}_{list.Count}matches.json");
            File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }), Encoding.UTF8);
            return path;
        });
    }

    /// <summary>One row per player per match (the full 72-player scoreboard), plus a second file with your combat log.</summary>
    internal Task<string?> ExportFrontlineCsv(IEnumerable<FrontlineMatch> matches) {
        return Task.Run<string?>(() => {
            var list = matches.Where(IsExportable).OrderBy(x => x.DutyStartTime).ToList();
            if(list.Count == 0) return null;
            var stamp = Stamp(DateTime.UtcNow);
            var sb = new StringBuilder();
            sb.AppendLine(Csv("match_id", "start_utc", "map", "mode", "duration_s", "team", "team_placement", "team_points", "player", "world", "job",
                "alliance", "is_you", "kills", "deaths", "assists", "damage_dealt", "damage_to_pcs", "damage_to_other", "damage_taken", "hp_restored",
                "special", "occupations", "claim_time_s", "max_battle_high"));
            var combat = new StringBuilder();
            combat.AppendLine(Csv("match_id", "start_utc", "map", "t_s", "direction", "kind", "skill", "other", "other_job", "other_team", "damage", "heal",
                "mp_drain", "crit", "direct_hit", "blocked", "parried", "missed", "invulnerable", "killing_blow", "pet"));
            foreach(var m in list) {
                var dto = BuildMatch(m, _plugin.FLCache.GetTimeline(m), true);
                foreach(var p in dto.Players) {
                    var team = dto.Teams.GetValueOrDefault(p.Team);
                    sb.AppendLine(Csv(dto.Id, dto.Start, dto.MapName, dto.MapType, dto.DurationSec, p.Team, (team?.Placement ?? -1) + 1, team?.TotalPoints,
                        p.Name, p.World, p.Job, p.Alliance, p.IsYou, p.Kills, p.Deaths, p.Assists, p.DamageDealt, p.DamageToPCs, p.DamageToOther,
                        p.DamageTaken, p.HpRestored, p.Special, p.Occupations, p.ClaimTimeSec, p.MaxBattleHigh));
                }
                foreach(var c in dto.Combat ?? []) {
                    combat.AppendLine(Csv(dto.Id, dto.Start, dto.MapName, c.T, c.Dir, c.Kind, c.Name, c.Other, c.OtherJob, c.OtherTeam, c.Dmg, c.Heal,
                        c.MpDrain, c.Crit, c.Dh, c.Blocked, c.Parried, c.Miss, c.Invuln, c.Kb, c.Pet));
                }
            }
            var path = Path.Combine(ExportDirectory, $"FL_scoreboards_{stamp}_{list.Count}matches.csv");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(ExportDirectory, $"FL_combatlog_{stamp}_{list.Count}matches.csv"), combat.ToString(), new UTF8Encoding(true));
            return path;
        });
    }

    /// <summary>Used by IPC: any match type as JSON. Frontline matches use the export schema.</summary>
    internal string SerializeMatch(PvpMatch match) {
        if(match is FrontlineMatch fl) {
            return JsonSerializer.Serialize(new ReportDto {
                Mode = "match",
                Matches = [BuildMatch(fl, _plugin.FLCache.GetTimeline(fl), true)],
            }, JsonOptions);
        }
        return JsonSerializer.Serialize(match, match.GetType(), JsonOptions);
    }

    internal string? OpenExportFolder() {
        OpenFile(ExportDirectory);
        return ExportDirectory;
    }

    internal void OpenFile(string path) {
        try {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        } catch(Exception e) {
            _plugin.Log.Warning(e, $"could not open {path}");
        }
    }

    // ───────────────────────────── building the export model ─────────────────────────────

    private MatchDto BuildMatch(FrontlineMatch m, FrontlineMatchTimeline? tl, bool includeRawCombat) {
        var t0 = m.MatchStartTime ?? m.DutyStartTime;
        double Rel(DateTime t) => Math.Round((t - t0).TotalSeconds, 1);

        var dto = new MatchDto {
            Id = m.Id.ToString(),
            DutyStart = m.DutyStartTime,
            Start = t0,
            DurationSec = (int)(m.MatchDuration?.TotalSeconds ?? 0),
            Map = m.Arena?.ToString(),
            MapName = MatchHelper.GetFrontlineArenaName(m.Arena),
            MapType = MatchHelper.GetFrontlineArenaType(m.Arena),
            DataCenter = m.DataCenter,
            GameVersion = m.GameVersion,
            PluginVersion = m.PluginVersion,
            LocalPlayer = m.LocalPlayer?.FullName,
            Favorite = m.IsBookmarked,
            Tags = m.Tags,
            WolfMarks = m.WolfMarks,
            SeriesXP = m.SeriesXP,
            JobXP = m.JobXP,
            BattleHighVTimeSec = m.BattleHighVTime?.TotalSeconds,
            BattleHighSpike = m.BattleHighSpike,
            BattleHighSpikeTimeSec = m.BattleHighSpikeTime?.TotalSeconds,
            MomentaryPointAdvantage = m.MomentaryPointAdvantage,
            MomentaryPointDeficit = m.MomentaryPointDeficit,
            Teams = m.Teams.ToDictionary(x => x.Key.ToString(), x => new TeamDto {
                Placement = x.Value.Placement,
                TotalPoints = x.Value.TotalPoints,
                KillPoints = x.Value.KillPoints,
                DeathPointLosses = x.Value.DeathPointLosses,
                OccupationPoints = x.Value.OccupationPoints,
                TargetablePoints = x.Value.TargetablePoints,
                DronePoints = x.Value.DronePoints,
            }),
        };

        var lookup = new Dictionary<string, PlayerDto>();
        foreach(var p in m.Players) {
            m.PlayerScoreboards.TryGetValue(p.Name, out var sb);
            int? maxBh = null;
            if(m.MaxBattleHigh != null && m.MaxBattleHigh.TryGetValue(p.Name, out var bh)) maxBh = bh;
            var pd = new PlayerDto {
                Name = p.Name.Name,
                World = p.Name.HomeWorld,
                Job = p.Job?.ToString() ?? JobAbbr(p.ClassJobId),
                Role = p.Job != null ? PlayerJobHelper.GetSubRoleFromJob(p.Job)?.ToString() : null,
                Team = p.Team.ToString(),
                Alliance = p.Alliance,
                IsYou = m.LocalPlayer != null && m.LocalPlayer.Equals(p.Name),
                Kills = sb?.Kills ?? 0,
                Deaths = sb?.Deaths ?? 0,
                Assists = sb?.Assists ?? 0,
                DamageDealt = sb?.DamageDealt ?? 0,
                DamageToPCs = sb?.DamageToPCs ?? 0,
                DamageToOther = sb?.DamageToOther ?? 0,
                DamageTaken = sb?.DamageTaken ?? 0,
                HpRestored = sb?.HPRestored ?? 0,
                Special = sb?.Special1 ?? 0,
                Occupations = sb?.Occupations ?? 0,
                ClaimTimeSec = (int)(sb?.ClaimTime.TotalSeconds ?? 0),
                MaxBattleHigh = maxBh,
            };
            dto.Players.Add(pd);
            lookup[p.Name.FullName] = pd;
        }

        if(tl != null) {
            if(tl.TeamPoints != null) {
                dto.TeamPoints = tl.TeamPoints.ToDictionary(x => x.Key.ToString(),
                    x => x.Value.Select(e => new[] { Rel(e.Timestamp), e.Points }).ToList());
            }
            if(tl.SelfBattleHigh != null) {
                dto.BattleHigh = tl.SelfBattleHigh.Select(e => new[] { Rel(e.Timestamp), e.Count }).ToList();
            }
            var combatLog = tl.GetCombatLog();
            if(combatLog != null) {
                List<CombatLogEntry> log;
                lock(combatLog) { log = combatLog.ToList(); }
                var rows = log.Select(e => {
                    lookup.TryGetValue(e.Other, out var other);
                    return new CombatDto {
                        T = Rel(e.Timestamp),
                        Dir = e.Direction switch { CombatDirection.Dealt => "out", CombatDirection.Taken => "in", _ => "self" },
                        Kind = e.Kind.ToString().ToLowerInvariant(),
                        Id = e.ActionId,
                        Name = e.ActionName,
                        Icon = e.Icon == 0 ? null : e.Icon,
                        Pet = e.FromPet ? true : null,
                        Other = e.Other,
                        OtherPlayer = e.OtherIsPlayer,
                        OtherJob = other?.Job ?? JobAbbr(e.OtherClassJobId),
                        OtherTeam = other?.Team,
                        OtherHp = e.OtherHpBefore,
                        OtherMaxHp = e.OtherMaxHp,
                        Dmg = e.Damage == 0 ? null : e.Damage,
                        Heal = e.Heal == 0 ? null : e.Heal,
                        MpDrain = e.MpDrain == 0 ? null : e.MpDrain,
                        MpGain = e.MpGain == 0 ? null : e.MpGain,
                        Crit = e.Crit ? true : null,
                        Dh = e.DirectHit ? true : null,
                        Blocked = e.Blocked ? true : null,
                        Parried = e.Parried ? true : null,
                        Miss = e.Missed ? true : null,
                        Invuln = e.Invulnerable ? true : null,
                        StatusApplied = e.StatusesApplied == 0 ? null : e.StatusesApplied,
                        StatusResisted = e.StatusesResisted == 0 ? null : e.StatusesResisted,
                        Kb = e.KillingBlow ? true : null,
                        Seq = e.GlobalSequence == 0 ? null : e.GlobalSequence,
                        Statuses = e.AppliedStatusIds is { Count: > 0 } ids ? new List<uint>(ids) : null,
                        X = e.SelfX is float x ? MathF.Round(x, 1) : null,
                        Z = e.SelfZ is float z ? MathF.Round(z, 1) : null,
                    };
                }).ToList();
                dto.Actions = rows.Where(r => r.Kind == "action" || r.Kind == "death").Select(r => r.Id).Distinct()
                    .Select(id => (id, info: ActionMeta(id))).Where(x => x.info != null)
                    .ToDictionary(x => x.id.ToString(), x => x.info!);
                dto.CombatSummary = Summarize(rows);
                if(includeRawCombat) dto.Combat = rows;
            }
        }
        return dto;
    }

    private static CombatSummaryDto Summarize(List<CombatDto> rows) {
        static List<SkillRowDto> Skills(IEnumerable<CombatDto> src) => src.Where(r => r.Kind != "death")
            .GroupBy(r => (r.Name, r.Kind)).Select(g => new SkillRowDto {
                Name = g.Key.Name, Kind = g.Key.Kind, Icon = g.First().Icon,
                Hits = g.Count(), Damage = g.Sum(r => r.Dmg ?? 0), Heal = g.Sum(r => r.Heal ?? 0),
                Crits = g.Count(r => r.Crit == true), DirectHits = g.Count(r => r.Dh == true),
                MaxHit = g.Max(r => r.Dmg ?? 0), KillingBlows = g.Count(r => r.Kb == true),
            }).OrderByDescending(r => r.Damage + r.Heal).ToList();
        static List<OtherRowDto> Others(IEnumerable<CombatDto> src) => src.Where(r => r.Kind != "death")
            .GroupBy(r => r.Other).Select(g => new OtherRowDto {
                Name = g.Key, Job = g.First().OtherJob, Team = g.First().OtherTeam,
                Hits = g.Count(), Damage = g.Sum(r => r.Dmg ?? 0), Heal = g.Sum(r => r.Heal ?? 0),
                KillingBlows = g.Count(r => r.Kb == true),
            }).OrderByDescending(r => r.Damage).ToList();
        var outRows = rows.Where(r => r.Dir == "out").ToList();
        var inRows = rows.Where(r => r.Dir == "in").ToList();
        var casts = GroupCasts(outRows);
        var dealt = Skills(outRows);
        foreach(var s in dealt.Where(s => s.Kind == "action")) {
            var mine = casts.Where(c => c.Name == s.Name).ToList();
            s.Casts = mine.Count;
            s.MaxTargets = mine.Count > 0 ? mine.Max(c => c.Targets) : 0;
            s.BestCast = mine.Count > 0 ? mine.Max(c => c.Total) : 0;
            s.TickDamage = mine.Sum(c => c.TickDamage);
        }
        return new CombatSummaryDto {
            TopCasts = casts.OrderByDescending(c => c.Total).Take(15).ToList(),
            Dealt = dealt,
            Taken = Skills(inRows),
            SelfSkills = Skills(rows.Where(r => r.Dir == "self")),
            Targets = Others(outRows),
            Sources = Others(inRows),
            Kills = outRows.Count(r => r.Kind == "death"),
            Deaths = inRows.Count(r => r.Kind == "death"),
        };
    }

    /// <summary>
    /// Groups your outgoing action hits into casts. Hits from one cast share the server's effect
    /// sequence number; hits without one fall back to same action within 0.5 s.
    /// </summary>
    internal static List<CastDto> GroupCasts(IEnumerable<CombatDto> outRows) {
        var casts = new List<CastDto>();
        CastDto? last = null;
        var rows = outRows.ToList();
        foreach(var r in rows.Where(r => r.Kind == "action").OrderBy(r => r.T)) {
            bool same = last != null && last.Id == r.Id &&
                (r.Seq != null ? r.Seq == last.SeqKey : Math.Abs(r.T - last.T) <= 0.5);
            if(!same) {
                last = new CastDto { T = r.T, Id = r.Id, Name = r.Name, X = r.X, Z = r.Z, SeqKey = r.Seq };
                casts.Add(last);
            }
            var hit = last!.Hits.FirstOrDefault(h => h.Other == r.Other);
            if(hit == null) {
                hit = new CastHitDto { Other = r.Other, Job = r.OtherJob, Team = r.OtherTeam };
                last.Hits.Add(hit);
            }
            hit.Dmg += r.Dmg ?? 0;
            hit.Heal += r.Heal ?? 0;
            if(r.Crit == true) hit.Crit = true;
            if(r.Dh == true) hit.Dh = true;
            if(r.Kb == true) hit.Kb = true;
            foreach(var s in r.Statuses ?? []) last.AppliedStatuses.Add(s);
            last.Damage += r.Dmg ?? 0;
            last.Heal += r.Heal ?? 0;
            if(r.Crit == true) last.Crits++;
            if(r.Kb == true) last.Kills++;
            last.Targets = last.Hits.Count;
        }
        AttachTicks(casts, rows.Where(r => r.Kind is "dot" or "hot"));
        return casts;
    }

    /// <summary>
    /// Credits each of your DoT/HoT ticks to the cast that applied it: the latest earlier cast (within 60 s) that
    /// hit the same player and either applied that status id, shares the status's name, or applied some status.
    /// Ticks with no matching cast are left unattributed (shown as their own lane in the report).
    /// </summary>
    internal static void AttachTicks(List<CastDto> casts, IEnumerable<CombatDto> ticks) {
        foreach(var tick in ticks.OrderBy(t => t.T)) {
            CastDto? best = null;
            int bestScore = 0;
            for(int i = casts.Count - 1; i >= 0; i--) {
                var c = casts[i];
                if(c.T > tick.T) continue;
                if(tick.T - c.T > 60) break;
                if(!c.Hits.Any(h => h.Other == tick.Other)) continue;
                int score = c.AppliedStatuses.Contains(tick.Id) ? 3
                    : string.Equals(c.Name, tick.Name, StringComparison.OrdinalIgnoreCase) ? 2
                    : c.AppliedStatuses.Count > 0 ? 1 : 0;
                if(score > bestScore) {
                    best = c;
                    bestScore = score;
                    if(score == 3) break;
                }
            }
            if(best == null) continue;
            tick.CastT = best.T;
            best.Ticks++;
            best.TickDamage += tick.Dmg ?? 0;
            best.TickHeal += tick.Heal ?? 0;
            best.LastTickT = tick.T;
            var hit = best.Hits.First(h => h.Other == tick.Other);
            hit.Ticks++;
            hit.TickDmg += tick.Dmg ?? 0;
            hit.TickHeal += tick.Heal ?? 0;
            if(tick.Kb == true) {
                hit.Kb = true;
                best.Kills++;
            }
        }
    }

    private readonly Dictionary<uint, ActionMetaDto?> _actionMetaCache = [];

    private ActionMetaDto? ActionMeta(uint id) {
        if(!_actionMetaCache.TryGetValue(id, out var meta)) {
            var row = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRowOrDefault(id);
            meta = row is null ? null : new ActionMetaDto {
                Name = row.Value.Name.ExtractText(),
                RecastSec = row.Value.Recast100ms / 10.0,
                MaxCharges = row.Value.MaxCharges,
                Icon = row.Value.Icon,
            };
            _actionMetaCache[id] = meta;
        }
        return meta;
    }

    private string? JobAbbr(uint? classJobId) {
        if(classJobId is null or 0) return null;
        if(!_jobAbbrCache.TryGetValue(classJobId.Value, out var abbr)) {
            abbr = _plugin.DataManager.GetExcelSheet<ClassJob>().GetRowOrDefault(classJobId.Value)?.Abbreviation.ExtractText() ?? "";
            _jobAbbrCache[classJobId.Value] = abbr;
        }
        return abbr.Length == 0 ? null : abbr;
    }

    private string WriteHtml(ReportDto report, string fileName) {
        string template;
        using(var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(TemplateResource)
            ?? throw new InvalidOperationException($"missing embedded resource {TemplateResource}"))
        using(var reader = new StreamReader(stream)) {
            template = reader.ReadToEnd();
        }
        // default encoder escapes <, >, & so the JSON is safe inside a <script> block
        var json = JsonSerializer.Serialize(report, JsonOptions);
        var path = Path.Combine(ExportDirectory, fileName);
        File.WriteAllText(path, template.Replace(DataPlaceholder, json), Encoding.UTF8);
        return path;
    }

    private static string Stamp(DateTime t) => t.ToLocalTime().ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture);
    private static string Safe(string s) => string.Concat(s.Where(c => char.IsLetterOrDigit(c) || c == '-')).Trim();

    private static string Csv(params object?[] values) => string.Join(",", values.Select(v => {
        var s = v switch {
            null => "",
            DateTime d => d.ToString("o", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "",
        };
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }));

    // ───────────────────────────── export schema (see docs/EXPORT_SCHEMA.md) ─────────────────────────────

    internal class ReportDto {
        public int Schema { get; set; } = SchemaVersion;
        public string Plugin { get; set; } = $"FLStats Plus {Assembly.GetExecutingAssembly().GetName().Version}";
        public DateTime Generated { get; set; } = DateTime.UtcNow;
        public string Mode { get; set; } = "history";
        public List<MatchDto> Matches { get; set; } = [];
    }

    internal class MatchDto {
        public string Id { get; set; } = "";
        public DateTime DutyStart { get; set; }
        public DateTime Start { get; set; }
        public int DurationSec { get; set; }
        public string? Map { get; set; }
        public string MapName { get; set; } = "";
        public string MapType { get; set; } = "";
        public string? DataCenter { get; set; }
        public string? GameVersion { get; set; }
        public string? PluginVersion { get; set; }
        public string? LocalPlayer { get; set; }
        public bool Favorite { get; set; }
        public string? Tags { get; set; }
        public int? WolfMarks { get; set; }
        public int? SeriesXP { get; set; }
        public long? JobXP { get; set; }
        public double? BattleHighVTimeSec { get; set; }
        public int? BattleHighSpike { get; set; }
        public double? BattleHighSpikeTimeSec { get; set; }
        public int? MomentaryPointAdvantage { get; set; }
        public int? MomentaryPointDeficit { get; set; }
        public Dictionary<string, TeamDto> Teams { get; set; } = [];
        public List<PlayerDto> Players { get; set; } = [];
        /// <summary>team → [[seconds, points], ...]</summary>
        public Dictionary<string, List<double[]>>? TeamPoints { get; set; }
        /// <summary>[[seconds, battle high level], ...]</summary>
        public List<double[]>? BattleHigh { get; set; }
        public CombatSummaryDto? CombatSummary { get; set; }
        public List<CombatDto>? Combat { get; set; }
        /// <summary>action id → recast / charges, for the cooldown lanes in the ability timeline</summary>
        public Dictionary<string, ActionMetaDto>? Actions { get; set; }
    }

    internal class ActionMetaDto {
        public string Name { get; set; } = "";
        public double RecastSec { get; set; }
        public int MaxCharges { get; set; }
        public uint? Icon { get; set; }
    }

    /// <summary>One use of an ability: every target its effect packet hit, added together.</summary>
    internal class CastDto {
        public double T { get; set; }
        public uint Id { get; set; }
        public string Name { get; set; } = "";
        public int Targets { get; set; }
        public long Damage { get; set; }
        public long Heal { get; set; }
        public int Crits { get; set; }
        public int Kills { get; set; }
        public float? X { get; set; }
        public float? Z { get; set; }
        /// <summary>DoT/HoT ticks from statuses this cast applied.</summary>
        public int Ticks { get; set; }
        public long TickDamage { get; set; }
        public long TickHeal { get; set; }
        public double? LastTickT { get; set; }
        /// <summary>Direct damage + tick damage.</summary>
        public long Total => Damage + TickDamage;
        public List<CastHitDto> Hits { get; set; } = [];
        internal uint? SeqKey;
        internal HashSet<uint> AppliedStatuses = [];
    }

    internal class CastHitDto {
        public string Other { get; set; } = "";
        public string? Job { get; set; }
        public string? Team { get; set; }
        public long Dmg { get; set; }
        public long Heal { get; set; }
        public bool? Crit { get; set; }
        public bool? Dh { get; set; }
        public bool? Kb { get; set; }
        public int Ticks { get; set; }
        public long TickDmg { get; set; }
        public long TickHeal { get; set; }
    }

    internal class TeamDto {
        public int? Placement { get; set; }
        public int TotalPoints { get; set; }
        public int KillPoints { get; set; }
        public int DeathPointLosses { get; set; }
        public int OccupationPoints { get; set; }
        public int TargetablePoints { get; set; }
        public int DronePoints { get; set; }
    }

    internal class PlayerDto {
        public string Name { get; set; } = "";
        public string World { get; set; } = "";
        public string? Job { get; set; }
        public string? Role { get; set; }
        public string Team { get; set; } = "";
        public int Alliance { get; set; }
        public bool IsYou { get; set; }
        public long Kills { get; set; }
        public long Deaths { get; set; }
        public long Assists { get; set; }
        public long DamageDealt { get; set; }
        public long DamageToPCs { get; set; }
        public long DamageToOther { get; set; }
        public long DamageTaken { get; set; }
        public long HpRestored { get; set; }
        public long Special { get; set; }
        public long Occupations { get; set; }
        public int ClaimTimeSec { get; set; }
        public int? MaxBattleHigh { get; set; }
    }

    internal class CombatDto {
        public double T { get; set; }
        public string Dir { get; set; } = "";
        public string Kind { get; set; } = "";
        public uint Id { get; set; }
        public string Name { get; set; } = "";
        public uint? Icon { get; set; }
        public bool? Pet { get; set; }
        public string Other { get; set; } = "";
        public bool OtherPlayer { get; set; }
        public string? OtherJob { get; set; }
        public string? OtherTeam { get; set; }
        public uint? OtherHp { get; set; }
        public uint? OtherMaxHp { get; set; }
        public long? Dmg { get; set; }
        public long? Heal { get; set; }
        public long? MpDrain { get; set; }
        public long? MpGain { get; set; }
        public bool? Crit { get; set; }
        public bool? Dh { get; set; }
        public bool? Blocked { get; set; }
        public bool? Parried { get; set; }
        public bool? Miss { get; set; }
        public bool? Invuln { get; set; }
        public int? StatusApplied { get; set; }
        public int? StatusResisted { get; set; }
        public bool? Kb { get; set; }
        public uint? Seq { get; set; }
        public List<uint>? Statuses { get; set; }
        /// <summary>For DoT/HoT ticks: time of the cast this tick was attributed to.</summary>
        public double? CastT { get; set; }
        public float? X { get; set; }
        public float? Z { get; set; }
    }

    internal class CombatSummaryDto {
        public List<SkillRowDto> Dealt { get; set; } = [];
        public List<SkillRowDto> Taken { get; set; } = [];
        public List<SkillRowDto> SelfSkills { get; set; } = [];
        public List<OtherRowDto> Targets { get; set; } = [];
        public List<OtherRowDto> Sources { get; set; } = [];
        public int Kills { get; set; }
        public int Deaths { get; set; }
        /// <summary>Your 15 biggest casts by total damage.</summary>
        public List<CastDto> TopCasts { get; set; } = [];
    }

    internal class SkillRowDto {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public uint? Icon { get; set; }
        public int Hits { get; set; }
        public long Damage { get; set; }
        public long Heal { get; set; }
        public int Crits { get; set; }
        public int DirectHits { get; set; }
        public long MaxHit { get; set; }
        public int KillingBlows { get; set; }
        public int Casts { get; set; }
        public int MaxTargets { get; set; }
        public long BestCast { get; set; }
        /// <summary>Damage from DoT ticks credited to this skill's casts.</summary>
        public long TickDamage { get; set; }
    }

    internal class OtherRowDto {
        public string Name { get; set; } = "";
        public string? Job { get; set; }
        public string? Team { get; set; }
        public int Hits { get; set; }
        public long Damage { get; set; }
        public long Heal { get; set; }
        public int KillingBlows { get; set; }
    }

    private class ObjectIdConverter : JsonConverter<ObjectId> {
        public override ObjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new(reader.GetString());
        public override void Write(Utf8JsonWriter writer, ObjectId value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
