using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game;

// The ride-off action the client sends in RequestRide 1 and reads back in ResponseRide Stop.
// Layouts follow the client's RideOffAction classes (ReadRideOffAction). After the dismount the
// client continues the action from these fields (skill, interact, taxi, chair...), so the server
// echoes what the client sent.
public class RideOffAction : IByteSerializable, IByteDeserializable {
    public readonly RideOffType Type;
    public bool Forced { get; private set; }

    public RideOffAction(bool forced) : this(RideOffType.Default) {
        Forced = forced;
    }

    protected RideOffAction(RideOffType type) {
        Type = type;
    }

    public static RideOffAction Read(IByteReader reader) {
        RideOffAction action = Create((RideOffType) reader.ReadByte());
        action.ReadFrom(reader);
        return action;
    }

    public static RideOffAction Create(RideOffType type) {
        return type switch {
            RideOffType.UseSkill => new RideOffActionUseSkill(),
            RideOffType.Interact => new RideOffActionInteract(),
            RideOffType.Taxi => new RideOffActionTaxi(),
            RideOffType.CashCall => new RideOffActionCashCall(),
            RideOffType.TakeLr => new RideOffActionTakeLr(),
            RideOffType.Recall => new RideOffActionRecall(),
            RideOffType.SummonPetOn => new RideOffActionSummonPetOn(),
            RideOffType.SummonPetTransfer => new RideOffActionSummonPetTransfer(),
            RideOffType.HomeConvenient => new RideOffActionHomeConvenient(),
            RideOffType.Homemade => new RideOffActionHomemade(),
            RideOffType.AutoInteraction => new RideOffActionAutoInteraction(),
            RideOffType.CoupleEmotion => new RideOffActionCoupleEmotion(),
            RideOffType.UseFunctionItem => new RideOffActionUseFunctionItem(),
            RideOffType.Nurturing => new RideOffActionNurturing(),
            RideOffType.UseGlideItem => new RideOffActionUseGlideItem(),
            // Types with no extra fields. React (20) has no case in the client's reader.
            RideOffType.BeautyShop or RideOffType.Hold or RideOffType.DisableField or RideOffType.Dead
                or RideOffType.AdditionalEffect or RideOffType.RidingUi or RideOffType.AutoClimb
                or RideOffType.Groggy or RideOffType.UnRideSkill or RideOffType.HideAndSeek => new RideOffAction(type),
            _ => new RideOffAction(false),
        };
    }

    public virtual void WriteTo(IByteWriter writer) {
        writer.WriteByte((byte) Type);
        writer.WriteBool(Forced);
    }

    // The type byte is read by Read().
    public virtual void ReadFrom(IByteReader reader) {
        Forced = reader.ReadBool();
    }
}

public class RideOffActionUseSkill() : RideOffAction(RideOffType.UseSkill) {
    // The client casts this skill after the dismount.
    private int skillId;
    private bool remap;
    private int unknown;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteInt(skillId);
        writer.WriteBool(remap);
        writer.WriteInt(unknown);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        skillId = reader.ReadInt();
        remap = reader.ReadBool();
        unknown = reader.ReadInt();
    }
}

public class RideOffActionInteract() : RideOffAction(RideOffType.Interact) {
    // The client starts the interact with this object after the dismount.
    private int objectId;
    private string entityId = string.Empty;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteInt(objectId);
        writer.WriteUnicodeString(entityId);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        objectId = reader.ReadInt();
        entityId = reader.ReadUnicodeString();
    }
}

public class RideOffActionTaxi() : RideOffAction(RideOffType.Taxi) {
    // The client calls this taxi after the dismount.
    private int taxiId;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteInt(taxiId);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        taxiId = reader.ReadInt();
    }
}

public class RideOffActionCashCall() : RideOffAction(RideOffType.CashCall) {
    // The client's cash call state arguments.
    private int value1;
    private byte value2;
    private long value3;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteInt(value1);
        writer.WriteByte(value2);
        writer.WriteLong(value3);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        value1 = reader.ReadInt();
        value2 = reader.ReadByte();
        value3 = reader.ReadLong();
    }
}

public class RideOffActionTakeLr() : RideOffAction(RideOffType.TakeLr) {
    // The client sits on this chair or bed after the dismount.
    private byte side = 1;
    private int objectId;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteByte(side);
        writer.WriteInt(objectId);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        side = reader.ReadByte();
        objectId = reader.ReadInt();
    }
}

public class RideOffActionRecall() : RideOffAction(RideOffType.Recall) {
    private long value;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteLong(value);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        value = reader.ReadLong();
    }
}

public class RideOffActionSummonPetOn() : RideOffAction(RideOffType.SummonPetOn) {
    private long petUid;
    private byte value;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteLong(petUid);
        writer.WriteByte(value);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        petUid = reader.ReadLong();
        value = reader.ReadByte();
    }
}

public class RideOffActionSummonPetTransfer() : RideOffAction(RideOffType.SummonPetTransfer) {
    private long petUid;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteLong(petUid);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        petUid = reader.ReadLong();
    }
}

public class RideOffActionHomeConvenient() : RideOffAction(RideOffType.HomeConvenient) {
    private byte value;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteByte(value);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        value = reader.ReadByte();
    }
}

public class RideOffActionHomemade() : RideOffAction(RideOffType.Homemade) {
    private string name = string.Empty;
    private int value1;
    private short value2;
    private int value3;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteUnicodeString(name);
        writer.WriteInt(value1);
        writer.WriteShort(value2);
        writer.WriteInt(value3);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        name = reader.ReadUnicodeString();
        value1 = reader.ReadInt();
        value2 = reader.ReadShort();
        value3 = reader.ReadInt();
    }
}

public class RideOffActionAutoInteraction() : RideOffAction(RideOffType.AutoInteraction) {
    private int value1;
    private short value2;
    private int value3;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteInt(value1);
        writer.WriteShort(value2);
        writer.WriteInt(value3);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        value1 = reader.ReadInt();
        value2 = reader.ReadShort();
        value3 = reader.ReadInt();
    }
}

public class RideOffActionCoupleEmotion() : RideOffAction(RideOffType.CoupleEmotion) {
    private int emotionId;
    private long value;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteInt(emotionId);
        writer.WriteLong(value);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        emotionId = reader.ReadInt();
        value = reader.ReadLong();
    }
}

public class RideOffActionUseFunctionItem() : RideOffAction(RideOffType.UseFunctionItem) {
    private long itemUid;
    private string value = string.Empty;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteLong(itemUid);
        writer.WriteUnicodeString(value);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        itemUid = reader.ReadLong();
        value = reader.ReadUnicodeString();
    }
}

public class RideOffActionNurturing() : RideOffAction(RideOffType.Nurturing) {
    private string name = string.Empty;
    private int value;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteUnicodeString(name);
        writer.WriteInt(value);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        name = reader.ReadUnicodeString();
        value = reader.ReadInt();
    }
}

public class RideOffActionUseGlideItem() : RideOffAction(RideOffType.UseGlideItem) {
    // The client uses this glide item after the dismount.
    private long itemUid;
    private bool value1;
    private int value2;

    public override void WriteTo(IByteWriter writer) {
        base.WriteTo(writer);
        writer.WriteLong(itemUid);
        writer.WriteBool(value1);
        writer.WriteInt(value2);
    }

    public override void ReadFrom(IByteReader reader) {
        base.ReadFrom(reader);
        itemUid = reader.ReadLong();
        value1 = reader.ReadBool();
        value2 = reader.ReadInt();
    }
}
