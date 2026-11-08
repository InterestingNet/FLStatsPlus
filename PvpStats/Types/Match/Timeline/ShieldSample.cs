using System;

namespace PvpStats.Types.Match.Timeline;

/// <summary>Your shield whenever it changed: in % of max HP (as the game stores it) and your max HP at the time.</summary>
public class ShieldSample {
    public DateTime Timestamp { get; set; }
    public byte Percent { get; set; }
    public uint MaxHp { get; set; }
}
