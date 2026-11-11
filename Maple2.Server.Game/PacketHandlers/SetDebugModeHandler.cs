using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

// The client's debug mode (a GM hotkey) and the windows it opens. GM only.
// Mode 2: a click on a minimap entity in debug mode, with the entity's position. The sender is moved there.
// Mode 4: the NPC control window. Picking an NPC is answered with its skill list and state (0x87 DebugMode,
// kinds 6 and 5); the health, additional effect, skill and stop skill buttons are applied; the others are read
// and logged.
// Mode 7: the debug guide window's "multi execute": the lines go back to the sender only (0x87 kind 7),
// whose client enters them in its chat.
public class SetDebugModeHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.SetDebugMode;

    private const byte MODE_SWITCH = 0;
    private const byte MODE_MINIMAP = 2;
    private const byte MODE_NPC_CONTROL = 4;
    private const byte MODE_GUIDE = 7;

    public override void Handle(GameSession session, IByteReader packet) {
        // The mode byte is read after the check, so a plain account changes nothing.
        if (!GmCheck.Allow(session, OpCode)) {
            return;
        }

        byte mode = packet.ReadByte();
        switch (mode) {
            case MODE_SWITCH:
                Logger.Debug("SetDebugMode: debug mode {Value} from account {AccountId}", packet.ReadByte(), session.AccountId);
                return;
            case MODE_MINIMAP:
                HandleMinimap(session, packet);
                return;
            case MODE_NPC_CONTROL:
                HandleNpcControl(session, packet);
                return;
            case MODE_GUIDE:
                HandleGuide(session, packet);
                return;
            default:
                Logger.Debug("SetDebugMode: mode {Mode} from account {AccountId} not handled", mode, session.AccountId);
                return;
        }
    }

    private void HandleMinimap(GameSession session, IByteReader packet) {
        Vector3 position = packet.Read<Vector3S>();
        if (session.Field == null || position == default) {
            return;
        }
        if (!session.Field.ValidPosition(position)) {
            Logger.Debug("SetDebugMode: position {Position} is not in field {MapId}", position, session.Field.MapId);
            return;
        }

        session.Send(PortalPacket.MoveByPortal(session.Player, position, session.Player.Rotation));
    }

    private void HandleNpcControl(GameSession session, IByteReader packet) {
        GmNpcControlRequest request = GmNpcControlRequest.Read(packet);
        if (request.Command == GmNpcControlRequest.Select) {
            // The client opened its NPC control window before it sent this (camera.cpp:1064), found or not.
            session.NpcControlPicked = true;
        }
        if (session.Field == null) {
            return;
        }
        if (!session.Field.Mobs.TryGetValue(request.ObjectId, out FieldNpc? npc) && !session.Field.Npcs.TryGetValue(request.ObjectId, out npc)) {
            return;
        }
        if (npc.IsDead) {
            return;
        }

        switch (request.Command) {
            case GmNpcControlRequest.Select:
                session.Send(DebugModePacket.NpcSkills(npc.Value.Metadata.Skill.Entries.Select(entry => (entry.Id, (int) entry.Level)).ToList()));
                SendNpcState(session, npc);
                return;
            case GmNpcControlRequest.HealthPercent:
            case GmNpcControlRequest.HealthValue:
                Stat health = npc.Stats.Values[BasicAttribute.Health];
                long target = GmNpcControlRequest.HealthTarget(request.Command, request.Value, health.Total);
                if (target < 0) {
                    return;
                }
                health.Add(target - health.Current);
                session.Field.Broadcast(StatsPacket.Update(npc, BasicAttribute.Health));
                SendNpcState(session, npc);
                return;
            case GmNpcControlRequest.AddEffect:
                if (request.EffectLevel is < 1 or > short.MaxValue
                    || !session.SkillMetadata.TryGetEffect(request.EffectId, (short) request.EffectLevel, out AdditionalEffectMetadata? _)) {
                    Logger.Warning("SetDebugMode: unknown effect {EffectId} level {Level} from account {AccountId}", request.EffectId, request.EffectLevel, session.AccountId);
                    return;
                }
                npc.Buffs.AddBuff(session.Player, npc, request.EffectId, (short) request.EffectLevel, session.Field.FieldTick);
                return;
            case GmNpcControlRequest.RemoveEffect:
                foreach (Buff buff in npc.Buffs.EnumerateBuffs(request.EffectId)) {
                    npc.Buffs.Remove(buff.Id, buff.Caster.ObjectId);
                }
                return;
            case GmNpcControlRequest.UseSkill:
                // The first skill of the list is cast once, facing the sender; the loop count and the rest
                // of the list are not played.
                if (request.SkillCount < 1 || request.SkillLevel is < 1 or > short.MaxValue
                    || !session.Field.SkillMetadata.TryGet(request.SkillId, (short) request.SkillLevel, out SkillMetadata? _)) {
                    Logger.Warning("SetDebugMode: unknown skill {SkillId} level {Level} from account {AccountId}", request.SkillId, request.SkillLevel, session.AccountId);
                    return;
                }
                npc.CastAiSkill(request.SkillId, (short) request.SkillLevel, faceTarget: 1, facePos: session.Player.Position);
                return;
            case GmNpcControlRequest.StopSkill:
                npc.MovementState.CastTask?.Cancel();
                return;
            case GmNpcControlRequest.AiOn:
            case GmNpcControlRequest.AiOff:
                npc.AiOff = request.Command == GmNpcControlRequest.AiOff;
                Logger.Information("SetDebugMode: AI of NPC {NpcId} (object {ObjectId}) in field {MapId} switched {State} by account {AccountId}",
                    npc.Value.Id, npc.ObjectId, session.Field.MapId, npc.AiOff ? "off" : "on", session.AccountId);
                SendNpcState(session, npc);
                return;
            case GmNpcControlRequest.Extra:
                // The window has no reader of its own for this button; its extra data is part of kind 5.
                SendNpcState(session, npc);
                return;
            case GmNpcControlRequest.AiFile:
                // The NPC takes the named AI file at its next update, on the field thread. A name the
                // server has no AI for changes nothing.
                string? aiFile = GmNpcControlRequest.AiFileName(request.Text);
                if (aiFile == null || !session.Field.AiMetadata.TryGet(aiFile, out AiMetadata? ai)) {
                    Logger.Warning("SetDebugMode: no AI file for the name sent by account {AccountId} (name {AiFile})", session.AccountId, aiFile ?? "-");
                    return;
                }
                npc.RequestAiFile(ai.Name);
                Logger.Information("SetDebugMode: NPC {NpcId} (object {ObjectId}) in field {MapId} gets the AI file {AiFile} from account {AccountId}",
                    npc.Value.Id, npc.ObjectId, session.Field.MapId, ai.Name, session.AccountId);
                SendNpcState(session, npc);
                return;
            default:
                Logger.Debug("SetDebugMode: NPC control command {Command} on {ObjectId} from account {AccountId} not handled", request.Command, request.ObjectId, session.AccountId);
                return;
        }
    }

    // The window's text: the movement state as the node, the AI file as the headline, the battle target and
    // the six stats it lists. The server keeps no AI trace, so "tracking" is off and there is no extra data.
    private static void SendNpcState(GameSession session, FieldNpc npc) {
        Stat health = npc.Stats.Values[BasicAttribute.Health];
        // An AI file that waits for the NPC's next update is already shown as its AI file.
        string? aiName = npc.PendingAiFile ?? npc.AiState.AiMetadata?.Name;
        session.Send(DebugModePacket.NpcState(npc.ObjectId, npc.MovementState.State.ToString(), GmNpcControlRequest.Headline(aiName, npc.AiOff),
            npc.BattleState.TargetId, false, GmNpcControlRequest.ExtraList(npc.AiExtraData),
            npc.Stats.Values[BasicAttribute.PhysicalAtk].Total, npc.Stats.Values[BasicAttribute.MagicalAtk].Total,
            npc.Stats.Values[BasicAttribute.Defense].Total, npc.Stats.Values[BasicAttribute.CriticalRate].Total,
            health.Current, health.Total));
    }

    private void HandleGuide(GameSession session, IByteReader packet) {
        List<(string Command, string Argument)>? lines = GmGuideRequest.Read(packet);
        if (lines == null) {
            return;
        }

        // Every open window of the client is handed every packet (networkservice.cpp:719, uiservice.cpp:1386),
        // and the NPC control window reads 0x0087 kind 7 as an AI file name and an id and opens an AI debug
        // window for it (uinpccontroldialog.cpp:233). The server cannot see that window close, so after an NPC
        // pick the guide lines are not sent back for the rest of the session.
        if (session.NpcControlPicked) {
            Logger.Information("SetDebugMode: {Count} guide command lines from account {AccountId} not sent back, its NPC control window may be open", lines.Count, session.AccountId);
            return;
        }

        // The client that gets the lines enters them in its own chat, so each one comes back as a chat
        // command of that account and passes that command's own permission check. Only the sender gets them:
        // one GM must not make another client run commands.
        Logger.Information("SetDebugMode: {Count} guide command lines from account {AccountId}", lines.Count, session.AccountId);
        session.Send(DebugModePacket.GuideCommands(session.PlayerName, lines));
    }
}
