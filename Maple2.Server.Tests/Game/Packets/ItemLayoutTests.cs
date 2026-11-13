using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// The shared Item writer (Item.WriteTo and its sub-blocks) read back in the order of the client's Item reader,
// CItem::ReadFromStream (item.cpp:2655-2704). Each test reads a block field by field as its client reader does and
// ends on that reader's last field: nothing may be left in the buffer. Stat ids are only read back as the enum the
// server wrote; which client id each attribute is belongs to the item option tests.
public class ItemLayoutTests {
    private static ByteReader Reader(ByteWriter writer) => new ByteReader(writer.ToArray(), 0);

    private static ByteReader Serialize<T>(T value) where T : class, IByteSerializable {
        var writer = new ByteWriter();
        writer.WriteClass<T>(value);
        return Reader(writer);
    }

    private static void End(ByteReader reader) => Assert.That(reader.Available, Is.EqualTo(0));

    // EquipColorInfo::ReadFromStream (itemextradata.cpp:94): three i32 colours, i32 index, i32 palette.
    private static void ReadColor(ByteReader reader, int index, int palette) {
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadInt();
        Assert.That(reader.ReadInt(), Is.EqualTo(index));
        Assert.That(reader.ReadInt(), Is.EqualTo(palette));
    }

    private static void ReadVector(ByteReader reader, Vector3 expected) {
        Assert.That(reader.ReadFloat(), Is.EqualTo(expected.X));
        Assert.That(reader.ReadFloat(), Is.EqualTo(expected.Y));
        Assert.That(reader.ReadFloat(), Is.EqualTo(expected.Z));
    }

    // CItemExtraData::vfunc_6 (itemextradata.cpp:200): the colour only.
    [Test]
    public void Appearance_Base_IsColorOnly() {
        ByteReader reader = Serialize<ItemAppearance>(new ItemAppearance(EquipColor.None));
        ReadColor(reader, -1, 0);
        End(reader);
    }

    // CHairExtraData::vfunc_6 (itemextradata.cpp:659): colour, then {float, v3, v3} twice (back, front).
    [Test]
    public void Appearance_Hair_IsColorAndTwoStrands() {
        var hair = new HairAppearance(EquipColor.None, 1.5f, new Vector3(1, 2, 3), new Vector3(4, 5, 6),
            2.5f, new Vector3(7, 8, 9), new Vector3(10, 11, 12));
        ByteReader reader = Serialize<ItemAppearance>(hair);
        ReadColor(reader, -1, 0);
        Assert.That(reader.ReadFloat(), Is.EqualTo(1.5f));
        ReadVector(reader, new Vector3(1, 2, 3));
        ReadVector(reader, new Vector3(4, 5, 6));
        Assert.That(reader.ReadFloat(), Is.EqualTo(2.5f));
        ReadVector(reader, new Vector3(7, 8, 9));
        ReadVector(reader, new Vector3(10, 11, 12));
        End(reader);
    }

    // CDecalExtraData (itemextradata.cpp:524): colour and four floats.
    [Test]
    public void Appearance_Decal_IsColorAndFourFloats() {
        ByteReader reader = Serialize<ItemAppearance>(new DecalAppearance(EquipColor.None, 1, 2, 3, 4));
        ReadColor(reader, -1, 0);
        for (int i = 1; i <= 4; i++) {
            Assert.That(reader.ReadFloat(), Is.EqualTo((float) i));
        }
        End(reader);
    }

    // CCapExtraData (itemextradata.cpp:799): colour, four v3 and a float.
    [Test]
    public void Appearance_Cap_IsColorFourVectorsAndAFloat() {
        var cap = new CapAppearance(EquipColor.None, new Vector3(1, 1, 1), new Vector3(2, 2, 2), new Vector3(3, 3, 3),
            new Vector3(4, 4, 4), 0.5f);
        ByteReader reader = Serialize<ItemAppearance>(cap);
        ReadColor(reader, -1, 0);
        for (int i = 1; i <= 4; i++) {
            ReadVector(reader, new Vector3(i, i, i));
        }
        Assert.That(reader.ReadFloat(), Is.EqualTo(0.5f));
        End(reader);
    }

    // CItemOption::Deserialize (itemoptiondata_1.cpp:242): five groups of {i16 n, {i16 id, i32}×n, i16 m,
    // {i16 id, i32}×m}, then i32 merge id.
    [Test]
    public void Stats_AreFiveGroupsThenMergeId() {
        var basic = new Dictionary<BasicAttribute, BasicOption>[ItemStats.TYPE_COUNT];
        var special = new Dictionary<SpecialAttribute, SpecialOption>[ItemStats.TYPE_COUNT];
        for (int i = 0; i < ItemStats.TYPE_COUNT; i++) {
            basic[i] = new Dictionary<BasicAttribute, BasicOption>();
            special[i] = new Dictionary<SpecialAttribute, SpecialOption>();
        }
        basic[(int) ItemStats.Type.Static][BasicAttribute.Dexterity] = new BasicOption(12);
        special[(int) ItemStats.Type.Random][SpecialAttribute.Experience] = new SpecialOption(34);
        special[(int) ItemStats.Type.Empowerment][SpecialAttribute.Experience] = new SpecialOption(56);

        ByteReader reader = Serialize(new ItemStats(basic, special, 40300001));
        for (int group = 0; group < ItemStats.TYPE_COUNT; group++) {
            short basicCount = reader.ReadShort();
            Assert.That(basicCount, Is.EqualTo(basic[group].Count));
            for (int i = 0; i < basicCount; i++) {
                Assert.That(reader.ReadShort(), Is.EqualTo((short) BasicAttribute.Dexterity));
                Assert.That(reader.ReadInt(), Is.EqualTo(12));
            }
            short specialCount = reader.ReadShort();
            Assert.That(specialCount, Is.EqualTo(special[group].Count));
            for (int i = 0; i < specialCount; i++) {
                Assert.That(reader.ReadShort(), Is.EqualTo((short) SpecialAttribute.Experience));
                Assert.That(reader.ReadInt(), Is.EqualTo(group == (int) ItemStats.Type.Random ? 34 : 56));
            }
        }
        Assert.That(reader.ReadInt(), Is.EqualTo(40300001));
        End(reader);
    }

    // CItemEnchantInfo::Read (itemenchantinfo.cpp:194): i32 level, i32 exp, u8 charges; the history
    // (:30: i64, i32, i32, bool, i32); then u8 count and {i32 id, i32 value}×count.
    [Test]
    public void Enchant_IsLevelExpChargesHistoryAndOptions() {
        var enchant = new ItemEnchant(7, 3500, 2, 9, new Dictionary<BasicAttribute, BasicOption> {
            [BasicAttribute.Strength] = new BasicOption(45),
        });
        ByteReader reader = Serialize(enchant);
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadInt(), Is.EqualTo(3500));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(9));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) BasicAttribute.Strength));
        Assert.That(reader.ReadInt(), Is.EqualTo(45));
        End(reader);
    }

    // UnlimitedEnchantInfo::ReadFromStream (unlimitedenchantinfo.cpp:101): i32 level, i32 n {i16, i32}×n,
    // i32 m {i16, i32}×m.
    [Test]
    public void LimitBreak_IsLevelAndTwoIntCountedLists() {
        var limitBreak = new ItemLimitBreak(3,
            new Dictionary<BasicAttribute, BasicOption> { [BasicAttribute.Luck] = new BasicOption(5) },
            new Dictionary<SpecialAttribute, SpecialOption> { [SpecialAttribute.Experience] = new SpecialOption(6) });
        ByteReader reader = Serialize(limitBreak);
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadShort(), Is.EqualTo((short) BasicAttribute.Luck));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SpecialAttribute.Experience));
        Assert.That(reader.ReadInt(), Is.EqualTo(6));
        End(reader);
    }

    // CUgcItemLook::Read (ugcdef.cpp:1553): i64, ustrA, ustr, u8, i32, i64, i64, ustr, i64, ustrA, u8.
    private static void ReadTemplate(ByteReader reader) {
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("77"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("look"));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(22));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("author"));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("/ugc/a"));
        Assert.That(reader.ReadByte(), Is.EqualTo(0)); // m_n60, the reader's last field
    }

    private static UgcItemLook Template() => new UgcItemLook {
        Id = 77, Name = "look", AccountId = 11, CharacterId = 22, Author = "author", CreationTime = 1700000000, Url = "/ugc/a",
    };

    // CBlueprintItemData::Read (ugcdef.cpp:1385): i64, i32, i32, i32, i64, i32, i64, i64, ustr.
    private static void ReadBlueprint(ByteReader reader) {
        Assert.That(reader.ReadLong(), Is.EqualTo(88));
        Assert.That(reader.ReadInt(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(6));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000001));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) BlueprintType.Copy));
        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(22));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("author"));
    }

    private static ItemBlueprint Blueprint() => new ItemBlueprint {
        BlueprintUid = 88, Length = 4, Width = 5, Height = 6, CreationTime = System.DateTimeOffset.FromUnixTimeSeconds(1700000001),
        Type = BlueprintType.Copy, AccountId = 11, CharacterId = 22, CharacterName = "author",
    };

    [Test]
    public void Template_EndsOnTheLookFlagByte() {
        ByteReader reader = Serialize(Template());
        ReadTemplate(reader);
        End(reader);
    }

    [Test]
    public void Blueprint_EndsOnTheOwnerName() {
        ByteReader reader = Serialize(Blueprint());
        ReadBlueprint(reader);
        End(reader);
    }

    // CItemUgc::ReadFromStream (itemsocketsystemutil.cpp:1650): the look, then the blueprint.
    [Test]
    public void Ugc_IsTemplateThenBlueprint() {
        var writer = new ByteWriter();
        writer.WriteClass<UgcItemLook>(Template());
        writer.WriteClass<ItemBlueprint>(Blueprint());
        ByteReader reader = Reader(writer);
        ReadTemplate(reader);
        ReadBlueprint(reader);
        End(reader);
    }

    // The pet block of CItem::ReadFromStream (item.cpp, IsPetItem): ustr name, i64 exp, i32, i32 level, u8.
    [Test]
    public void Pet_IsNameExpPointsLevelAndFlag() {
        var pet = new ItemPet { Name = "pet", Exp = 1234, EvolvePoints = 5, Level = 6, HasItems = true };
        ByteReader reader = Serialize(pet);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("pet"));
        Assert.That(reader.ReadLong(), Is.EqualTo(1234));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(6));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        End(reader);
    }

    // CItemCustomMusicScore::ReadFromStream (itemsocketsystemutil.cpp:1779): i32, i32, ustr, ustr, i32, i64, u8,
    // i64, i64.
    [Test]
    public void Music_EndsOnTheSecondLong() {
        var music = new ItemCustomMusicScore {
            Length = 9000, Instrument = Instrument.Harp, Title = "title", Author = "author", AuthorId = 33, IsLocked = true,
        };
        ByteReader reader = Serialize(music);
        Assert.That(reader.ReadInt(), Is.EqualTo(9000));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) Instrument.Harp));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("title"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("author"));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(33));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        End(reader);
    }

    // CItemGem::vfunc_4 (itemgem.cpp:1004): bool has, u8 type, i32 gem id, then the type's own reader:
    // Transparency (1) reads 10 u8, PetSkin (11) one i32, every other type nothing.
    [Test]
    public void Badge_Transparency_IsTenFlags() {
        bool[] flags = [true, false, true, false, false, false, false, false, false, true];
        ByteReader reader = Serialize(new ItemBadge(70100001, flags));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) BadgeType.Transparency));
        Assert.That(reader.ReadInt(), Is.EqualTo(70100001));
        foreach (bool flag in flags) {
            Assert.That(reader.ReadBool(), Is.EqualTo(flag));
        }
        End(reader);
    }

    [Test]
    public void Badge_PetSkin_IsOnePetId() {
        ByteReader reader = Serialize(new ItemBadge(70100000) { PetSkinId = 60000012 });
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) BadgeType.PetSkin));
        Assert.That(reader.ReadInt(), Is.EqualTo(70100000));
        Assert.That(reader.ReadInt(), Is.EqualTo(60000012));
        End(reader);
    }

    [Test]
    public void Badge_OtherType_EndsOnTheGemId() {
        ByteReader reader = Serialize(new ItemBadge(70200001));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) BadgeType.ChatBubble));
        Assert.That(reader.ReadInt(), Is.EqualTo(70200001));
        End(reader);
    }

    // CItemBinding (itemsocketsystemutil.cpp:2592): i64 character id, ustr name.
    [Test]
    public void Binding_IsIdAndName() {
        ByteReader reader = Serialize(new ItemBinding(44, "owner"));
        Assert.That(reader.ReadLong(), Is.EqualTo(44));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("owner"));
        End(reader);
    }

    // CItemTransfer::vfunc_2 (itemsocketsystemutil.cpp:2711): i32 rule, bool, i32, i32, u8, u8, bool bound and
    // the binding when set. Split is a server rule and goes out cleared.
    [Test]
    public void Transfer_Bound_EndsOnTheBindingName() {
        var transfer = new ItemTransfer(TransferFlag.Split | TransferFlag.Trade, 3, 2, new ItemBinding(44, "owner")) {
            DisableBreak = true,
        };
        ByteReader reader = Serialize(transfer);
        Assert.That(reader.ReadInt(), Is.EqualTo((int) TransferFlag.Trade));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadLong(), Is.EqualTo(44));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("owner"));
        End(reader);
    }

    [Test]
    public void Transfer_Unbound_EndsOnTheBoundFlag() {
        ByteReader reader = Serialize(new ItemTransfer(TransferFlag.Trade, 1));
        Assert.That(reader.ReadInt(), Is.EqualTo((int) TransferFlag.Trade));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadBool(), Is.False);
        End(reader);
    }

    // CItemSocket::ReadFromStream (itemsocket.cpp:314): u8 max, u8 unlocked, then per unlocked slot bool filled
    // and, when filled, TSocketGemStoneInfo (:228): i32 gem id, bool bound + binding, bool locked + (u8, i64).
    [Test]
    public void Sockets_EmptyBoundAndLockedGems() {
        var socket = new ItemSocket(3, [
            null,
            new ItemGemstone(40000001, new ItemBinding(44, "owner"), new ItemStats()),
            new ItemGemstone(40000002, stats: new ItemStats(), isLocked: true, unlockTime: 1700000002),
        ]);
        ByteReader reader = Serialize(socket);
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));

        Assert.That(reader.ReadBool(), Is.False);

        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(40000001));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadLong(), Is.EqualTo(44));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("owner"));
        Assert.That(reader.ReadBool(), Is.False);

        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(40000002));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000002));
        End(reader);
    }

    [Test]
    public void Sockets_None_IsTwoZeroBytes() {
        ByteReader reader = Serialize(ItemSocket.Default);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        End(reader);
    }

    // CItemCoupleInfo::ReadFromStream (item.cpp:2847): i64 partner, and only when it is not 0: ustr, bool.
    [Test]
    public void Couple_None_IsOneLong() {
        ByteReader reader = Serialize(ItemCoupleInfo.Default);
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        End(reader);
    }

    [Test]
    public void Couple_Set_IsPartnerNameAndCreator() {
        ByteReader reader = Serialize(new ItemCoupleInfo(55, "partner", true));
        Assert.That(reader.ReadLong(), Is.EqualTo(55));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("partner"));
        Assert.That(reader.ReadBool(), Is.True);
        End(reader);
    }

    // CItem::ReadFromStream as a whole: the header, appearance, options, enchant, limit break, the item's one
    // extra block (here a badge), transfer, sockets, couple and the binding (i64 + ustr), which ends the item.
    [Test]
    public void Item_HeaderBlocksInOrder_EndsOnTheBinding() {
        var item = new Item(TestGameSession.Metadata(70200001, EquipSlot.HR)) {
            Amount = 1, CreationTime = 1700000003, ExpiryTime = 1700000004, TimeChangedOption = 5, RemainUses = 6,
            IsLocked = true, UnlockTime = 1700000005, GachaDismantleId = 8,
            Badge = new ItemBadge(70200001),
            CoupleInfo = new ItemCoupleInfo(55, "partner", false),
            Binding = new ItemBinding(44, "owner"),
            Socket = new ItemSocket(1, 0),
        };
        Assert.That(item.Appearance, Is.InstanceOf<HairAppearance>());
        var writer = new ByteWriter();
        writer.WriteClass<Item>(item);
        ByteReader reader = Reader(writer);

        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(-1));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000003));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000004));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(6));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000005));
        Assert.That(reader.ReadShort(), Is.EqualTo(item.GlamorForges));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadInt(), Is.EqualTo(8));

        // Hair: colour (20 bytes) and 2 × (float + 2 v3) = 56 bytes.
        ReadColor(reader, -1, 0);
        reader.Skip(56);
        // Options: five empty groups and the merge id.
        for (int i = 0; i < ItemStats.TYPE_COUNT; i++) {
            Assert.That(reader.ReadShort(), Is.EqualTo(0));
            Assert.That(reader.ReadShort(), Is.EqualTo(0));
        }
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        // Enchant: level, exp, charges, history (8 + 4 + 4 + 1 + 4), count.
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        reader.Skip(21);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        // Limit break: level and two empty lists.
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        // Badge.
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) BadgeType.ChatBubble));
        Assert.That(reader.ReadInt(), Is.EqualTo(70200001));
        // Transfer: 4 + 1 + 4 + 4 + 1 + 1, unbound.
        reader.Skip(15);
        Assert.That(reader.ReadBool(), Is.False);
        // Sockets: max 1, none unlocked.
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        // Couple.
        Assert.That(reader.ReadLong(), Is.EqualTo(55));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("partner"));
        Assert.That(reader.ReadBool(), Is.False);
        // Binding.
        Assert.That(reader.ReadLong(), Is.EqualTo(44));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("owner"));
        End(reader);
    }
}
