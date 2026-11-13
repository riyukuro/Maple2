using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Packets;

public static class StatsPacket {
    private enum Command : byte {
        Update = 0,
    }

    public static ByteWriter Init(Actor<Player> entity) {
        Stats stats = entity.Stats.Values;

        var pWriter = Packet.Of(SendOp.Stat);
        pWriter.WriteInt(entity.ObjectId);
        pWriter.WriteByte((byte) Command.Update);
        pWriter.WriteAllStats(stats);

        return pWriter;
    }

    public static ByteWriter Update<T>(Actor<T> entity) {
        var pWriter = Packet.Of(SendOp.Stat);
        pWriter.WriteInt(entity.ObjectId);
        pWriter.WriteByte((byte) Command.Update);
        switch (entity.Value) {
            case Player:
                pWriter.WritePlayerStats(entity.Stats.Values);
                break;
            default:
                pWriter.WriteNpcStats(entity.Stats.Values);
                break;
        }

        return pWriter;
    }

    // The stat lists of a heal or a dot on any actor. A player's client reads a list (PCBase/PC/MyPC::vfunc_90: u8 count,
    // count x (u8 type, 3 values)), but an npc's reads one stat: CNpc::vfunc_90 (npcactorandvibrate.cpp:2253) takes the
    // byte as a mode, 1 = (u8 type, 3 values), anything else = 3 x (long health, int attack speed). Two stats in one
    // packet would be read as the second form, so a non-player gets one packet per stat.
    public static IEnumerable<ByteWriter> Updates(IActor entity, IReadOnlyList<BasicAttribute> attributes) {
        if (attributes.Count == 0) {
            yield break;
        }
        if (entity is FieldPlayer) {
            yield return Update(entity, attributes.ToArray());
            yield break;
        }
        foreach (BasicAttribute attribute in attributes) {
            yield return Update(entity, attribute);
        }
    }

    // Players only for more than one stat (see Updates).
    public static ByteWriter Update(IActor entity, params BasicAttribute[] attributes) {
        var pWriter = Packet.Of(SendOp.Stat);
        pWriter.WriteInt(entity.ObjectId);
        pWriter.WriteByte((byte) Command.Update);
        pWriter.WriteByte((byte) attributes.Length);
        foreach (BasicAttribute attribute in attributes) {
            pWriter.WriteByte(attribute.ClientStat());
            pWriter.WriteAttribute(attribute, entity.Stats.Values[attribute]);
        }

        return pWriter;
    }

    // Note: this is a copy of above to reduce array allocations.
    public static ByteWriter Update(IActor entity, BasicAttribute attribute) {
        var pWriter = Packet.Of(SendOp.Stat);
        pWriter.WriteInt(entity.ObjectId);
        pWriter.WriteByte((byte) Command.Update);
        pWriter.WriteByte(1);
        pWriter.WriteByte(attribute.ClientStat());
        pWriter.WriteAttribute(attribute, entity.Stats.Values[attribute]);

        return pWriter;
    }

    #region Helpers
    // Client: MyPC::vfunc_90 (mypc.cpp:1078) reads count 0x23 as every stat in order. Only the player's own client
    // reads it this way: PC::vfunc_90 reads the same count as the five stats of WritePlayerStats, so this form goes
    // to the owner alone.
    public static void WriteAllStats(this IByteWriter pWriter, Stats stats) {
        pWriter.WriteByte(Stats.BASIC_TOTAL);
        for (int i = 0; i < Stats.BASIC_TOTAL; i++) {
            BasicAttribute attribute = BasicAttributeIds.ServerStat(i);
            pWriter.WriteAttribute(attribute, stats[attribute]);
        }
    }

    // Client: PC::vfunc_90 (pc.cpp:1644), count 0x23: per value kind long stat 4, int stats 0xd, 0xe, 0x20, 0x16
    // (health, attack speed, move speed, mount speed, jump height).
    public static void WritePlayerStats(this IByteWriter pWriter, Stats stats) {
        pWriter.WriteByte(Stats.BASIC_TOTAL);
        for (int i = 0; i < Stat.TOTAL; i++) {
            pWriter.WriteLong(stats[BasicAttribute.Health][i]);
            pWriter.WriteInt((int) stats[BasicAttribute.AttackSpeed][i]);
            pWriter.WriteInt((int) stats[BasicAttribute.MovementSpeed][i]);
            pWriter.WriteInt((int) stats[BasicAttribute.MountSpeed][i]);
            pWriter.WriteInt((int) stats[BasicAttribute.JumpHeight][i]);
        }
    }

    public static void WriteNpcStats(this IByteWriter pWriter, Stats stats) {
        pWriter.WriteByte(Stats.BASIC_TOTAL);
        for (int i = 0; i < Stat.TOTAL; i++) {
            pWriter.WriteLong(stats[BasicAttribute.Health][i]);
            pWriter.WriteInt((int) stats[BasicAttribute.AttackSpeed][i]);
        }
    }

    private static void WriteAttribute(this IByteWriter pWriter, BasicAttribute attribute, Stat stat) {
        for (int i = 0; i < Stat.TOTAL; i++) {
            if (attribute == BasicAttribute.Health) {
                pWriter.WriteLong(stat[i]);
            } else {
                pWriter.WriteInt((int) stat[i]);
            }
        }
    }
    #endregion
}
