using System;
using System.Collections.Generic;

namespace PvpStats.Types.Match.Timeline;

/// <summary>Who the entry is relative to the local player.</summary>
public enum CombatDirection {
    /// <summary>You (or your pet) did this to someone else.</summary>
    Dealt = 0,
    /// <summary>Someone else did this to you.</summary>
    Taken = 1,
    /// <summary>You did this to yourself (self-heals, reflected effects).</summary>
    Self = 2,
}

public enum CombatEntryKind {
    Action = 0,
    DoT = 1,
    HoT = 2,
    Death = 3,
}

/// <summary>
/// One hit (or tick, or death) in the local player's Frontline combat log.
/// Only events involving the local player are recorded.
/// </summary>
public class CombatLogEntry {
    public DateTime Timestamp { get; set; }
    public CombatDirection Direction { get; set; }
    public CombatEntryKind Kind { get; set; }

    /// <summary>Action row id for actions, Status row id for DoT/HoT ticks.</summary>
    public uint ActionId { get; set; }
    public string ActionName { get; set; } = "";
    /// <summary>Action icon id (for the HTML report / UI), 0 if unknown.</summary>
    public uint Icon { get; set; }
    /// <summary>True if the local player's pet/summon was the actual source.</summary>
    public bool FromPet { get; set; }

    /// <summary>The other party: "Name World" for players, NPC name otherwise.</summary>
    public string Other { get; set; } = "";
    public bool OtherIsPlayer { get; set; }
    public uint? OtherClassJobId { get; set; }
    public uint? OtherHpBefore { get; set; }
    public uint? OtherMaxHp { get; set; }

    public long Damage { get; set; }
    public long Heal { get; set; }
    public long MpDrain { get; set; }
    public long MpGain { get; set; }

    public bool Crit { get; set; }
    public bool DirectHit { get; set; }
    public bool Blocked { get; set; }
    public bool Parried { get; set; }
    public bool Missed { get; set; }
    public bool Invulnerable { get; set; }
    /// <summary>Damage type nibble from the effect (1 slashing, 2 piercing, 3 blunt, 5 magic, 6 darkness...).</summary>
    public byte DamageType { get; set; }
    public int StatusesApplied { get; set; }
    /// <summary>Status ids this hit applied (the effect value of ApplyStatusEffectTarget). Used to link DoT/HoT ticks to the cast.</summary>
    public List<uint>? AppliedStatusIds { get; set; }
    public int StatusesResisted { get; set; }

    /// <summary>Set on the last hit before a death credited to the other party.</summary>
    public bool KillingBlow { get; set; }

    /// <summary>Your position (map X / Z) when the event arrived.</summary>
    public float? SelfX { get; set; }
    public float? SelfZ { get; set; }

    /// <summary>Server-side effect counter. Every target hit by one cast shares it, so it groups AoE hits into a cast.</summary>
    public uint GlobalSequence { get; set; }
}
