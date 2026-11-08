using LiteDB;
using PvpStats.Types.Event.Frontline;
using System;
using System.Collections.Generic;

namespace PvpStats.Types.Match.Timeline;
internal class FrontlineMatchTimeline : PvpMatchTimeline {

    public Dictionary<FrontlineTeamName, List<TeamPointsEvent>>? TeamPoints { get; set; }
    public List<BattleHighLevelEvent>? SelfBattleHigh { get; set; }
    /// <summary>FLStatsPlus: every hit you dealt / took, DoT/HoT ticks and kills/deaths. In memory only; stored packed.</summary>
    [BsonIgnore]
    public List<CombatLogEntry>? SelfCombatLog { get; set; }

    /// <summary>FLStatsPlus: your shield every time it changed (gains, absorbed damage, expiry).</summary>
    public List<ShieldSample>? SelfShield { get; set; }

    /// <summary>FLStatsPlus: <see cref="SelfCombatLog"/> as gzip-compressed JSON (see <see cref="CombatLogCodec"/>).</summary>
    public byte[]? SelfCombatLogPacked { get; set; }

    /// <summary>Call before saving the finished match.</summary>
    public void PackCombatLog() {
        if(SelfCombatLog != null) {
            SelfCombatLogPacked = CombatLogCodec.Pack(SelfCombatLog);
        }
    }

    /// <summary>The combat log, unpacking it on first use after loading from the database.</summary>
    public List<CombatLogEntry>? GetCombatLog() {
        if(SelfCombatLog == null && SelfCombatLogPacked != null) {
            SelfCombatLog = CombatLogCodec.Unpack(SelfCombatLogPacked);
        }
        return SelfCombatLog;
    }
    public FrontlineMatchTimeline() : base() {

    }

    public Dictionary<FrontlineTeamName, int>? GetTeamPoints(DateTime time) {
        if(TeamPoints == null) return null;

        var maelPoints = TeamPoints[FrontlineTeamName.Maelstrom].FindLast(x => x.Timestamp <= time)?.Points ?? 0;
        var adderPoints = TeamPoints[FrontlineTeamName.Adders].FindLast(x => x.Timestamp <= time)?.Points ?? 0;
        var flamePoints = TeamPoints[FrontlineTeamName.Flames].FindLast(x => x.Timestamp <= time)?.Points ?? 0;
        return new() {
            {FrontlineTeamName.Maelstrom, maelPoints },
            {FrontlineTeamName.Adders, adderPoints },
            {FrontlineTeamName.Flames, flamePoints }
        };
    }

    public List<TeamPointsEvent>? GetFlattenedTeamPoints() {
        if(TeamPoints == null) return null;
        //var list = TeamPoints?.SelectMany(x => {
        //    x.Value.ForEach(y => y.Team = x.Key);
        //    return x.Value;
        //}).ToList();
        List<TeamPointsEvent> list = new();

        FrontlineTeamName[] teams = [FrontlineTeamName.Maelstrom, FrontlineTeamName.Adders, FrontlineTeamName.Flames];
        foreach(var team in teams) {
            foreach(var pEvent in TeamPoints[team]) {
                list.Add(new TeamPointsEvent(pEvent.Timestamp, pEvent.Points) {
                    Team = team
                });
            }
        }

        list?.Sort();
        return list;
    }
}
