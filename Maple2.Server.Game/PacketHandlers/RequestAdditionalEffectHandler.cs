using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
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

                // Only a buff this request created is recorded: not one that was already on (refreshed
                // or stacked), and nothing when the add was refused.
                List<Buff> before = session.Player.Buffs.EnumerateBuffs(request.EffectId);
                session.Player.Buffs.AddBuff(session.Player, session.Player, request.EffectId, request.Level, session.Field.FieldTick);
                List<Buff> created = GmEffectRules.Created(before, session.Player.Buffs.EnumerateBuffs(request.EffectId));
                lock (session.GmEffects) {
                    session.GmEffects.RemoveAll(buff => !IsOn(session, buff));
                    session.GmEffects.AddRange(created);
                }
                return;
            case GmEffectRequest.Reset:
                Buff[] buffs;
                lock (session.GmEffects) {
                    buffs = session.GmEffects.ToArray();
                    session.GmEffects.Clear();
                }
                foreach (Buff buff in buffs) {
                    // Still the same buff object: one that ended and came back from a skill or an item stays.
                    if (IsOn(session, buff)) {
                        session.Player.Buffs.Remove(buff.Id, buff.Caster.ObjectId);
                    }
                }
                return;
        }
    }

    private static bool IsOn(GameSession session, Buff buff) {
        return GmEffectRules.Contains(session.Player.Buffs.EnumerateBuffs(buff.Id), buff);
    }
}

// Which objects of `after` are not in `before`, by reference.
public static class GmEffectRules {
    public static List<T> Created<T>(IReadOnlyList<T> before, IReadOnlyList<T> after) where T : class {
        return after.Where(item => !Contains(before, item)).ToList();
    }

    public static bool Contains<T>(IReadOnlyList<T> list, T item) where T : class {
        foreach (T entry in list) {
            if (ReferenceEquals(entry, item)) {
                return true;
            }
        }
        return false;
    }
}
