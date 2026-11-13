using System.Text;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Game;

public sealed class ItemStats : IByteSerializable, IByteDeserializable {
    public static readonly ItemStats Default = new ItemStats();

    public const int TYPE_COUNT = 5;

    public enum Type {
        Constant = 0,
        Static = 1,
        Random = 2,
        Title = 3,
        Empowerment = 4,
    }

    private readonly Option[] options;

    // The int the client reads after the five groups (CItemOption::Deserialize, itemoptiondata_1.cpp:260;
    // GetMergeId): the item id of the crystal that gave the Empowerment group. The client finds the
    // crystal's option table with it to mark a value in the top range (itemmergeoptiontable.cpp:622).
    public int MergeId { get; private set; }

    // CItemOption::HasGroup4Options: the item carries a merge option.
    public bool HasMergeOption => options[(int) Type.Empowerment].Count != 0;

    public ItemStats() {
        options = new Option[TYPE_COUNT];
        for (int i = 0; i < TYPE_COUNT; i++) {
            options[i] = new Option();
        }
    }

    public ItemStats(Dictionary<BasicAttribute, BasicOption>[] basicOption, Dictionary<SpecialAttribute, SpecialOption>[] specialOption, int mergeId = 0) {
        MergeId = mergeId;
        // Ensure all entries are set.
        options = new Option[TYPE_COUNT];
        for (int i = 0; i < TYPE_COUNT; i++) {
            options[i] = new Option(
                basicOption.ElementAtOrDefault(i, () => new Dictionary<BasicAttribute, BasicOption>()),
                specialOption.ElementAtOrDefault(i, () => new Dictionary<SpecialAttribute, SpecialOption>()));
        }
    }

    public ItemStats Clone() {
        var stats = new ItemStats();
        for (int i = 0; i < TYPE_COUNT; i++) {
            stats.options[i] = new Option(
                new Dictionary<BasicAttribute, BasicOption>(options[i].Basic),
                new Dictionary<SpecialAttribute, SpecialOption>(options[i].Special));
        }
        stats.MergeId = MergeId;
        return stats;
    }

    // A merge leaves one option in the Empowerment group: the new one replaces the old one.
    public void SetMergeOption(MergeRoll roll, int crystalId) {
        var option = new Option();
        if (roll.Basic != null) {
            option.Basic[roll.Basic.Value] = new BasicOption(roll.Value);
        } else {
            option.Special[roll.Special] = new SpecialOption(roll.Value);
        }
        options[(int) Type.Empowerment] = option;
        MergeId = crystalId;
    }

    // The revert of a merge: no option in the Empowerment group and no crystal id.
    public void ClearMergeOption() {
        options[(int) Type.Empowerment] = new Option();
        MergeId = 0;
    }

    // CItemStatus::CopyGroup4From (itemsocketsystemutil.cpp:2060): each Empowerment option of the other item is set on
    // this one, and this item takes the other's crystal id when it has none.
    public void CopyEmpowermentFrom(ItemStats other) {
        Option source = other.options[(int) Type.Empowerment];
        Option target = options[(int) Type.Empowerment];
        foreach ((BasicAttribute attribute, BasicOption option) in source.Basic) {
            target.Basic[attribute] = option;
        }
        foreach ((SpecialAttribute attribute, SpecialOption option) in source.Special) {
            target.Special[attribute] = option;
        }
        if (source.Count != 0 && MergeId == 0) {
            MergeId = other.MergeId;
        }
    }

    public Option this[Type type] {
        get => options[(int) type];
        set => options[(int) type] = value;
    }

    public void WriteTo(IByteWriter writer) {
        for (int i = 0; i < TYPE_COUNT; i++) {
            Option option = options[i];
            writer.WriteShort((short) option.Basic.Count);
            foreach ((BasicAttribute type, BasicOption basicOption) in option.Basic) {
                writer.WriteShort(type.ClientStat());
                writer.Write<BasicOption>(basicOption);
            }
            writer.WriteShort((short) option.Special.Count);
            foreach ((SpecialAttribute type, SpecialOption specialOption) in option.Special) {
                writer.WriteShort((short) type);
                writer.WriteInt(specialOption.Value);
            }
        }

        writer.WriteInt(MergeId);
    }

    public void ReadFrom(IByteReader reader) {
        for (int i = 0; i < TYPE_COUNT; i++) {
            Option option = options[i];
            short basicCount = reader.ReadShort();
            for (int j = 0; j < basicCount; j++) {
                BasicAttribute type = BasicAttributeIds.ServerStat(reader.ReadShort());
                option.Basic[type] = reader.Read<BasicOption>();
            }
            short specialCount = reader.ReadShort();
            for (int j = 0; j < specialCount; j++) {
                var type = (SpecialAttribute) reader.ReadShort();
                option.Special[type] = new SpecialOption(reader.ReadInt());
            }
        }

        MergeId = reader.ReadInt();
    }

    public class Option {
        public readonly Dictionary<BasicAttribute, BasicOption> Basic;
        public readonly Dictionary<SpecialAttribute, SpecialOption> Special;

        public readonly float MultiplyFactor;

        public int Count => Basic.Count + Special.Count;

        public Option(Dictionary<BasicAttribute, BasicOption>? basicOption = null, Dictionary<SpecialAttribute, SpecialOption>? specialOption = null, float multiplyFactor = 1) {
            Basic = basicOption ?? new Dictionary<BasicAttribute, BasicOption>();
            Special = specialOption ?? new Dictionary<SpecialAttribute, SpecialOption>();
            MultiplyFactor = multiplyFactor;
        }

        public override string ToString() {
            var builder = new StringBuilder();
            builder.AppendLine("BasicOption:");
            foreach ((BasicAttribute attribute, BasicOption option) in Basic) {
                builder.AppendLine($"- {attribute}={option}");
            }
            builder.AppendLine("SpecialOption:");
            foreach ((SpecialAttribute attribute, SpecialOption option) in Special) {
                builder.AppendLine($"- {attribute}={option}");
            }
            return builder.ToString();
        }
    }
}
