using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

// The client's GM effect control window. GM only. The packet names no target: the effect goes on the
// sender. The reset button takes off what this window put on in this session, nothing else.
// The client has no reply of its own for this packet; it shows the normal buff packets.
public class RequestAdditionalEffectHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestAdditionalEffect;

    public override void Handle(GameSession session, IByteReader packet) {
        // The mode byte is read after the check, so a plain account changes nothing.
        if (!GmCheck.Allow(session, OpCode)) {
            return;
        }

        GmEffectRequest request = GmEffectRequest.Read(packet);
        if (session.Field == null) {
            return;
        }

        switch (request.Mode) {
            case GmEffectRequest.Add:
                if (!session.SkillMetadata.TryGetEffect(request.EffectId, request.Level, out AdditionalEffectMetadata? _)) {
                    Logger.Warning("RequestAdditionalEffect: unknown effect {EffectId} level {Level} from account {AccountId}", request.EffectId, request.Level, session.AccountId);
                    return;
                }

                session.Player.Buffs.AddBuff(session.Player, session.Player, request.EffectId, request.Level, session.Field.FieldTick);
                lock (session.GmEffects) {
                    session.GmEffects.Add(request.EffectId);
                }
                return;
            case GmEffectRequest.Reset:
                int[] effectIds;
                lock (session.GmEffects) {
                    effectIds = session.GmEffects.ToArray();
                    session.GmEffects.Clear();
                }
                foreach (int effectId in effectIds) {
                    session.Player.Buffs.Remove(effectId, session.Player.ObjectId);
                }
                return;
        }
    }
}
