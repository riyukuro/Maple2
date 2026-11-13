using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class ResolveDeathPenaltyHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ResolvePenalty;

    public override void Handle(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        int npcObjectId = NpcTalkRules.ReadNpc(packet);
        if (!session.Field.Npcs.TryGetValue(npcObjectId, out FieldNpc? npc) || npc.Value.Metadata.Basic.Kind != NpcTalkRules.DoctorKind) {
            return;
        }
        // The client sends this from the doctor's talk button, before it closes the talk (cinematiccomponent.cpp:1082-1088).
        if (!NpcTalkRules.PenaltyResolveOpen(session.NpcScript?.Npc?.ObjectId ?? 0, npcObjectId, npc.Value.Metadata.Basic.Kind)) {
            return;
        }

        if (session.Config.DeathPenaltyEndTick < session.Field.FieldTick) {
            return;
        }

        int mode = session.Field.Metadata.Property.Continent == Continent.ShadowWorld ? 1 : 0;
        int cost = Lua.CalcResolvePenaltyPrice((ushort) session.Player.Value.Character.Level, session.Config.DeathCount, mode);
        if (session.Currency.Meso < cost) {
            return;
        }

        session.Currency.ChangeMeso(-cost);
        session.Config.UpdateDeathPenalty(0);
        session.ConditionUpdate(ConditionType.resolve_panelty);
    }
}
