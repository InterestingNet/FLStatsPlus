using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.Sheets;
using PvpStats.Types.ClientStruct.Action;
using PvpStats.Types.Match.Timeline;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using GameObjectId = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObjectId;

namespace PvpStats.Managers.Game;

/// <summary>
/// Records every hit the local player deals and receives during a Frontline match:
/// actions (with crit / direct hit / block / parry flags), DoT and HoT ticks, and deaths.
/// Only events where the local player (or their pet) is the source or the target are kept.
/// Hooks are the same functions PvpStats' Crystalline Conflict tracker uses.
///
/// Performance: these hooks fire for every hit/tick/status event of every player and NPC in range.
/// Each detour first decides relevance with plain integer compares on raw game memory (your entity id,
/// your pets' ids, the target ids in the packet) and returns immediately for everything else, so the
/// cost of an event that isn't yours is a few nanoseconds and no allocations. Object lookups, name
/// strings and Excel lookups only happen for events that involve you, and are cached per match.
/// </summary>
internal unsafe class FrontlineCombatLogger : IDisposable {

    private readonly Plugin _plugin;
    private readonly object _lock = new();
    private List<CombatLogEntry>? _sink;
    private readonly Dictionary<uint, (string Name, uint Icon)> _actionCache = [];
    private readonly Dictionary<uint, (string Name, uint Icon)> _statusCache = [];

    private readonly Hook<ActionEffectHandler.Delegates.Receive>? _actionEffectHook;

    private delegate void ProcessPacketActorControlDelegate(uint entityId, uint type, uint p1, uint p2, uint p3, uint p4, uint p5, uint p6, uint p7, uint p8, ulong targetEntityId, byte flag);
    [Signature("E8 ?? ?? ?? ?? 0F B7 0B 83 E9 64", DetourName = nameof(ActorControlDetour))]
    private readonly Hook<ProcessPacketActorControlDelegate> _actorControlHook = null!;

    private delegate void ProcessHoTDoTDelegate(IntPtr targetStatusManager, IntPtr targetObj, uint statusId, uint kind, int amount, uint sourceEntityId, int beneficial);
    [Signature("48 8B C4 48 89 58 ?? 48 89 68 ?? 48 89 70 ?? 57 41 54 41 56 48 83 EC ?? 4C 89 78", DetourName = nameof(HoTDoTDetour))]
    private readonly Hook<ProcessHoTDoTDelegate> _hotDotHook = null!;

    public bool IsActive => _sink != null;

    // cached per match (game thread only)
    private uint _localId;
    private readonly HashSet<uint> _petIds = [];
    private readonly Dictionary<uint, string> _nameCache = [];

    // overhead meter
    private long _hookTicks, _hookCalls, _recorded;
    private int _peakCount;
    private DateTime _startedAt;
    /// <summary>Human-readable overhead summary for the current/last match.</summary>
    public string OverheadSummary { get; private set; } = "No Frontline match recorded yet this session.";

    public FrontlineCombatLogger(Plugin plugin) {
        _plugin = plugin;
        _plugin.InteropProvider.InitializeFromAttributes(this);
        _actionEffectHook = _plugin.InteropProvider.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
            ActionEffectHandler.MemberFunctionPointers.Receive, ActionEffectDetour);
        _actionEffectHook.Enable();
        _actorControlHook.Enable();
        _hotDotHook.Enable();
    }

    public void Dispose() {
        _actionEffectHook?.Dispose();
        _actorControlHook?.Dispose();
        _hotDotHook?.Dispose();
    }

    /// <summary>Begin writing entries into <paramref name="sink"/>.</summary>
    public void Start(List<CombatLogEntry> sink) {
        lock(_lock) {
            _localId = 0; // resolved lazily on the game thread
            _petIds.Clear();
            _nameCache.Clear();
            _hookTicks = _hookCalls = _recorded = 0;
            _startedAt = DateTime.UtcNow;
            _sink = sink;
        }
    }

    /// <summary>Stop recording. Call on the game thread before the match is saved.</summary>
    public void Stop() {
        lock(_lock) {
            if(_sink != null) {
                _peakCount = _sink.Count;
                var ms = _hookTicks * 1000.0 / Stopwatch.Frequency;
                var minutes = Math.Max((DateTime.UtcNow - _startedAt).TotalMinutes, 1 / 60.0);
                OverheadSummary = $"Last match: {_recorded:N0} events recorded from {_hookCalls:N0} game events seen; " +
                    $"total time in combat-log hooks {ms:N1} ms over {minutes:N1} min (≈{ms / (minutes * 60 * 60):N3} ms per frame at 60 fps).";
                Plugin.Log2.Information($"[FLStatsPlus] combat log overhead: {OverheadSummary}");
            }
            _sink = null;
        }
    }

    /// <summary>Your entity id; resolved once per match on the game thread.</summary>
    private uint ResolveLocalId() {
        if(_localId == 0) {
            _localId = _plugin.ObjectTable.LocalPlayer?.EntityId ?? 0;
        }
        return _localId;
    }

    private void Add(CombatLogEntry entry) {
        SetPosition(entry);
        lock(_lock) {
            _sink?.Add(entry);
            _recorded++;
        }
    }

    private void SetPosition(CombatLogEntry entry) {
        var me = _plugin.ObjectTable.LocalPlayer;
        if(me != null) {
            entry.SelfX = me.Position.X;
            entry.SelfZ = me.Position.Z;
        }
    }

    private uint LocalId => _localId;

    /// <summary>Returns true if the object is the local player or one of their pets.</summary>
    private bool IsSelf(IGameObject? obj, out bool isPet) {
        isPet = false;
        var local = LocalId;
        if(obj == null || local == 0) return false;
        if(obj.EntityId == local) return true;
        if(obj.OwnerId == local) {
            isPet = true;
            return true;
        }
        return false;
    }

    private string Describe(IGameObject? obj) {
        if(obj == null) return "Unknown";
        if(_nameCache.TryGetValue(obj.EntityId, out var name)) return name;
        name = obj is IPlayerCharacter pc ? $"{pc.Name} {pc.HomeWorld.Value.Name}" : obj.Name.TextValue;
        _nameCache[obj.EntityId] = name;
        return name;
    }

    private (string Name, uint Icon) ActionInfo(uint id) {
        if(!_actionCache.TryGetValue(id, out var info)) {
            var row = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRowOrDefault(id);
            info = row is null ? ($"Action #{id}", 0u)
                : (row.Value.ActionCategory.RowId == 1 ? "Auto-attack" : row.Value.Name.ExtractText(), (uint)row.Value.Icon);
            _actionCache[id] = info;
        }
        return info;
    }

    private (string Name, uint Icon) StatusInfo(uint id) {
        if(!_statusCache.TryGetValue(id, out var info)) {
            var row = _plugin.DataManager.GetExcelSheet<Status>().GetRowOrDefault(id);
            info = row is null ? ($"Status #{id}", 0u) : (row.Value.Name.ExtractText(), row.Value.Icon);
            _statusCache[id] = info;
        }
        return info;
    }

    /// <summary>Attribute pets/summons to their owning player.</summary>
    private IGameObject? ResolveOwner(IGameObject? obj) {
        if(obj != null && obj is not IPlayerCharacter && obj.OwnerId != 0 && obj.OwnerId != 0xE0000000) {
            var owner = _plugin.ObjectTable.SearchByEntityId(obj.OwnerId);
            if(owner is IPlayerCharacter) return owner;
        }
        return obj;
    }

    private void FillOther(CombatLogEntry e, IGameObject? other) {
        e.Other = Describe(other);
        e.OtherIsPlayer = other is IPlayerCharacter;
        if(other is IBattleChara bc) {
            e.OtherClassJobId = bc.ClassJob.RowId;
            e.OtherHpBefore = bc.CurrentHp;
            e.OtherMaxHp = bc.MaxHp;
        }
    }

    private void ActionEffectDetour(uint casterEntityId, Character* casterPtr, Vector3* targetPos,
        ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetIds) {
        long t0 = 0;
        try {
            if(_sink == null) return;
            t0 = Stopwatch.GetTimestamp();
            _hookCalls++;
            // ── fast path: integer compares only ──
            var local = ResolveLocalId();
            if(local == 0) return;
            uint ownerId = casterPtr != null ? ((CSGameObject*)casterPtr)->OwnerId : 0;
            bool fromPet = casterEntityId != local && (ownerId == local || _petIds.Contains(casterEntityId));
            bool casterIsSelf = casterEntityId == local || fromPet;
            if(fromPet) _petIds.Add(casterEntityId);
            if(!casterIsSelf) {
                bool hitsMe = false;
                for(var i = 0; i < header->NumTargets; i++) {
                    if((uint)(targetIds[i] & uint.MaxValue) == local) { hitsMe = true; break; }
                }
                if(!hitsMe) return;
            }
            // ── slow path: only for events that involve you ──
            var caster = casterPtr != null ? _plugin.ObjectTable.CreateObjectReference((nint)casterPtr) : _plugin.ObjectTable.SearchByEntityId(casterEntityId);
            var now = DateTime.UtcNow;
            uint actionId = header->SpellId;
            var info = ActionInfo(actionId);

            for(var i = 0; i < header->NumTargets; i++) {
                var targetId = (uint)(targetIds[i] & uint.MaxValue);
                bool targetIsSelf = targetId == local;
                if(!casterIsSelf && !targetIsSelf) continue;
                var target = targetIsSelf ? _plugin.ObjectTable.LocalPlayer : _plugin.ObjectTable.SearchByEntityId(targetId);

                var entry = new CombatLogEntry {
                    Timestamp = now,
                    Kind = CombatEntryKind.Action,
                    ActionId = actionId,
                    ActionName = info.Name,
                    Icon = info.Icon,
                    FromPet = fromPet,
                    GlobalSequence = header->GlobalSequence,
                    Direction = casterIsSelf && targetIsSelf ? CombatDirection.Self
                        : casterIsSelf ? CombatDirection.Dealt : CombatDirection.Taken,
                };
                FillOther(entry, casterIsSelf ? target : ResolveOwner(caster));

                bool any = false;
                for(var j = 0; j < 8; j++) {
                    ref var fx = ref effects[i].Effects[j];
                    if(fx.Type == 0) continue;
                    uint amount = fx.Value;
                    if((fx.Param4 & 0x40) == 0x40) {
                        amount += (uint)fx.Param3 << 16;
                    }
                    // reflected effects (lifesteal, Chiten...) land on the caster, not the target
                    bool reflected = (fx.Param4 & 0x80) == 0x80;
                    var e = entry;
                    if(reflected) {
                        if(!casterIsSelf && !targetIsSelf) continue;
                        // reflected onto the caster: re-express relative to the local player
                        if(casterIsSelf) {
                            e = Clone(entry, CombatDirection.Self);
                        } else {
                            continue; // the other player's lifesteal off of you is not your stat
                        }
                    }
                    switch((ActionEffectType)fx.Type) {
                        case ActionEffectType.Miss:
                            e.Missed = true; any = true; break;
                        case ActionEffectType.Damage:
                        case ActionEffectType.BlockedDamage:
                        case ActionEffectType.ParriedDamage:
                            e.Damage += amount;
                            e.Crit |= (fx.Param0 & 0x20) == 0x20;
                            e.DirectHit |= (fx.Param0 & 0x40) == 0x40;
                            e.DamageType = (byte)(fx.Param1 & 0xF);
                            e.Blocked |= fx.Type == (byte)ActionEffectType.BlockedDamage;
                            e.Parried |= fx.Type == (byte)ActionEffectType.ParriedDamage;
                            any = true; break;
                        case ActionEffectType.Heal:
                            e.Heal += amount;
                            e.Crit |= (fx.Param1 & 0x20) == 0x20;
                            any = true; break;
                        case ActionEffectType.MpLoss:
                            e.MpDrain += amount; any = true; break;
                        case ActionEffectType.MpGain:
                            e.MpGain += amount; any = true; break;
                        case ActionEffectType.Invulnerable:
                        case ActionEffectType.PartialInvulnerable:
                            e.Invulnerable = true; any = true; break;
                        case ActionEffectType.ApplyStatusEffectTarget:
                            e.StatusesApplied++;
                            (e.AppliedStatusIds ??= []).Add(fx.Value);
                            any = true; break;
                        case ActionEffectType.StatusNoEffect:
                        case ActionEffectType.FullResistStatus:
                            e.StatusesResisted++; any = true; break;
                    }
                    if(reflected && e != entry && (e.Damage | e.Heal | e.MpGain | e.MpDrain) != 0) {
                        Add(e);
                    }
                }
                if(any) {
                    Add(entry);
                }
            }
        } catch(Exception ex) {
            Plugin.Log2.Error(ex, "FL combat log: action effect");
        } finally {
            if(t0 != 0) _hookTicks += Stopwatch.GetTimestamp() - t0;
            _actionEffectHook!.Original(casterEntityId, casterPtr, targetPos, header, effects, targetIds);
        }
    }

    private static CombatLogEntry Clone(CombatLogEntry src, CombatDirection dir) => new() {
        Timestamp = src.Timestamp, Kind = src.Kind, ActionId = src.ActionId, ActionName = src.ActionName, Icon = src.Icon,
        FromPet = src.FromPet, GlobalSequence = src.GlobalSequence, Direction = dir, Other = "(self)",
    };

    private void HoTDoTDetour(IntPtr targetStatusManager, IntPtr targetObj, uint statusId, uint kind, int amount, uint sourceEntityId, int beneficial) {
        long t0 = 0;
        try {
            // kind: 3 = damage, 4 = heal, 11 = mp
            if(_sink == null) return;
            t0 = Stopwatch.GetTimestamp();
            _hookCalls++;
            if(statusId == 0 || (kind != 3 && kind != 4)) return;
            // ── fast path ──
            var local = ResolveLocalId();
            if(local == 0) return;
            bool fromPet = sourceEntityId != local && _petIds.Contains(sourceEntityId);
            bool sourceIsSelf = sourceEntityId == local || fromPet;
            bool targetIsSelf = targetObj != IntPtr.Zero && ((CSGameObject*)targetObj)->EntityId == local;
            if(!sourceIsSelf && !targetIsSelf) return;
            // ── slow path ──
            var target = _plugin.ObjectTable.CreateObjectReference(targetObj);
            var source = sourceIsSelf && !fromPet ? _plugin.ObjectTable.LocalPlayer : _plugin.ObjectTable.SearchByEntityId(sourceEntityId);

            var info = StatusInfo(statusId);
            var entry = new CombatLogEntry {
                Timestamp = DateTime.UtcNow,
                Kind = kind == 3 ? CombatEntryKind.DoT : CombatEntryKind.HoT,
                ActionId = statusId,
                ActionName = info.Name,
                Icon = info.Icon,
                FromPet = fromPet,
                Direction = sourceIsSelf && targetIsSelf ? CombatDirection.Self
                    : sourceIsSelf ? CombatDirection.Dealt : CombatDirection.Taken,
            };
            FillOther(entry, sourceIsSelf ? target : ResolveOwner(source));
            if(kind == 3) entry.Damage = amount; else entry.Heal = amount;
            Add(entry);
        } catch(Exception ex) {
            Plugin.Log2.Error(ex, "FL combat log: hot/dot");
        } finally {
            if(t0 != 0) _hookTicks += Stopwatch.GetTimestamp() - t0;
            _hotDotHook.Original(targetStatusManager, targetObj, statusId, kind, amount, sourceEntityId, beneficial);
        }
    }

    private void ActorControlDetour(uint entityId, uint type, uint p1, uint p2, uint p3, uint p4, uint p5, uint p6, uint p7, uint p8, ulong targetEntityId, byte flag) {
        long t0 = 0;
        try {
            if(_sink == null) return;
            t0 = Stopwatch.GetTimestamp();
            _hookCalls++;
            if(type != (uint)ActorControlCategory.Death) return;
            // entityId = victim, p2 = killer
            var local = ResolveLocalId();
            if(local == 0) return;
            if(entityId != local && p2 != local && !_petIds.Contains(p2)) return;
            var victim = _plugin.ObjectTable.SearchByEntityId(entityId);
            var killer = _plugin.ObjectTable.SearchByEntityId(p2);
            bool victimIsSelf = entityId == local;
            bool killerIsSelf = IsSelf(killer, out var fromPet);
            if(!victimIsSelf && !killerIsSelf) return;
            if(victimIsSelf && killerIsSelf) return;

            var entry = new CombatLogEntry {
                Timestamp = DateTime.UtcNow,
                Kind = CombatEntryKind.Death,
                FromPet = fromPet,
                Direction = killerIsSelf ? CombatDirection.Dealt : CombatDirection.Taken,
                ActionName = killerIsSelf ? "Kill" : "Death",
            };
            FillOther(entry, killerIsSelf ? victim : ResolveOwner(killer));

            lock(_lock) {
                if(_sink == null) return;
                // flag the last matching hit within 5s as the killing blow
                for(int i = _sink.Count - 1; i >= 0; i--) {
                    var e = _sink[i];
                    if((entry.Timestamp - e.Timestamp).TotalSeconds > 5) break;
                    if(e.Direction == entry.Direction && e.Kind != CombatEntryKind.Death && e.Damage > 0 && e.Other == entry.Other) {
                        e.KillingBlow = true;
                        entry.ActionId = e.ActionId;
                        entry.ActionName = e.ActionName;
                        entry.Icon = e.Icon;
                        break;
                    }
                }
                SetPosition(entry);
                _sink.Add(entry);
                _recorded++;
            }
        } catch(Exception ex) {
            Plugin.Log2.Error(ex, "FL combat log: actor control");
        } finally {
            if(t0 != 0) _hookTicks += Stopwatch.GetTimestamp() - t0;
            _actorControlHook.Original(entityId, type, p1, p2, p3, p4, p5, p6, p7, p8, targetEntityId, flag);
        }
    }
}
