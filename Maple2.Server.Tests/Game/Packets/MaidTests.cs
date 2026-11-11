using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Home maids. Client: CFieldMaidManager::OnPacket mode 0 reads a mode byte and CMaid::ReadFromPacket, mode 1 the
// maid uid. CMaid::ReadFromPacket: five longs, item id, maid id, a byte, mood, level, exp, the expiry, a counted
// list of longs and the 13 profile strings.
public class MaidTests {
    private const int CubeItemId = 50400001;
    private const int MaidId = 3;

    private static ItemMetadata Metadata(int maidId) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var housing = new ItemMetadataHousing(0, 0, 0, default, true, maidId);
        return new ItemMetadata(CubeItemId, null, [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [],
            null, null, housing, null);
    }

    [Test]
    public void AddIsModeByteThenCMaid() {
        var maid = new Maid {
            Uid = 77,
            OwnerId = 5,
            ItemId = CubeItemId,
            MaidId = MaidId,
            Mood = 4,
            Level = 2,
            Exp = 30,
            CreationTime = 1000,
            ExpiryTime = 2000,
            Profile = new MaidProfile { Name = "n", BelongTo = "b" },
        };
        ByteWriter writer = FieldMaidPacket.Add(maid);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.FieldMaid));
            Assert.That(reader.ReadByte(), Is.EqualTo(0)); // add
            Assert.That(reader.ReadByte(), Is.EqualTo(0)); // the maid's mode
            Assert.That(reader.ReadLong(), Is.EqualTo(0));
            Assert.That(reader.ReadLong(), Is.EqualTo(77));
            Assert.That(reader.ReadLong(), Is.EqualTo(1000));
            Assert.That(reader.ReadLong(), Is.EqualTo(1000));
            Assert.That(reader.ReadLong(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.EqualTo(CubeItemId));
            Assert.That(reader.ReadInt(), Is.EqualTo(MaidId));
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(4));
            Assert.That(reader.ReadInt(), Is.EqualTo(2));
            Assert.That(reader.ReadInt(), Is.EqualTo(30));
            Assert.That(reader.ReadLong(), Is.EqualTo(2000));
            Assert.That(reader.ReadInt(), Is.EqualTo(0));
            Assert.That(reader.ReadUnicodeString(), Is.Empty); // title
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("n"));
            for (int i = 0; i < 10; i++) {
                Assert.That(reader.ReadUnicodeString(), Is.Empty);
            }
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("b"));
            Assert.That(reader.Available, Is.EqualTo(0));
        });
    }

    [Test]
    public void RemoveIsModeByteThenUid() {
        ByteWriter writer = FieldMaidPacket.Remove(77);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.FieldMaid));
            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.ReadLong(), Is.EqualTo(77));
            Assert.That(writer.Length, Is.EqualTo(2 + 1 + 8));
        });
    }

    [Test]
    public void TableRow_NeedsPositiveMaidAndNpc() {
        Assert.Multiple(() => {
            Assert.That(MaidTable.Row("3", "12", "11001234"), Is.EqualTo(new MaidTable.Entry(3, 12, 11001234)));
            Assert.That(MaidTable.Row("3", "", "11001234"), Is.EqualTo(new MaidTable.Entry(3, 0, 11001234)));
            Assert.That(MaidTable.Row("0", "12", "11001234"), Is.Null);
            Assert.That(MaidTable.Row("3", "12", ""), Is.Null);
            Assert.That(MaidTable.Row("x", "12", "11001234"), Is.Null);
        });
    }

    [Test]
    public void MaidId_IsZeroOnAPlannerPlotAndForOtherCubes() {
        Assert.Multiple(() => {
            Assert.That(MaidRules.MaidId(Metadata(MaidId), planner: false), Is.EqualTo(MaidId));
            Assert.That(MaidRules.MaidId(Metadata(MaidId), planner: true), Is.EqualTo(0));
            Assert.That(MaidRules.MaidId(Metadata(0), planner: false), Is.EqualTo(0));
        });
    }

    [Test]
    public void Hire_StartsAtLevelOneForThePeriod() {
        var cube = new PlotCube(Metadata(MaidId), id: 77) { Type = PlotCube.CubeType.Construction };
        Maid maid = MaidRules.Hire(cube, MaidId, ownerId: 5, now: 1000, periodDays: 30);
        Assert.Multiple(() => {
            Assert.That(maid.Uid, Is.EqualTo(77));
            Assert.That(maid.OwnerId, Is.EqualTo(5));
            Assert.That(maid.ItemId, Is.EqualTo(CubeItemId));
            Assert.That(maid.MaidId, Is.EqualTo(MaidId));
            Assert.That(maid.Level, Is.EqualTo(1));
            Assert.That(maid.Mood, Is.EqualTo(0));
            Assert.That(maid.CreationTime, Is.EqualTo(1000));
            Assert.That(maid.ExpiryTime, Is.EqualTo(1000 + 30L * 86400));
        });
    }

    [Test]
    public void Orphans_AreTheMaidsWhoseCubeIsGone() {
        Assert.Multiple(() => {
            Assert.That(MaidRules.Orphans([77, 78, 79], [78, 90]), Is.EqualTo(new long[] { 77, 79 }));
            Assert.That(MaidRules.Orphans([77], [77]), Is.Empty);
            Assert.That(MaidRules.Orphans([77], []), Is.EqualTo(new long[] { 77 }));
        });
    }

    private static Maid NewMaid(int level = 1, int exp = 0, int mood = 0) {
        return new Maid { Uid = 77, OwnerId = 5, ItemId = CubeItemId, MaidId = MaidId, Level = level, Exp = exp, Mood = mood, CreationTime = 1000, ExpiryTime = 2000 };
    }

    // Levels 1-4 need 10, 20, 30 and 40 exp.
    private static readonly Dictionary<int, int> Exps = new() { [1] = 10, [2] = 20, [3] = 30, [4] = 40 };

    [Test]
    public void UserMaid_ListClearsThenCountedMaids() {
        Maid maid = NewMaid();
        byte[] body = FieldMaidPacket.Add(maid).ToArray()[3..];
        byte[] list = UserMaidPacket.List([maid, maid]).ToArray();
        Assert.That(list.Length, Is.EqualTo(2 + 1 + 4 + 2 * body.Length));
        var reader = new ByteReader(list, 0);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.UserMaid));
            Assert.That(reader.ReadByte(), Is.EqualTo(0));
            Assert.That(reader.ReadInt(), Is.EqualTo(2));
            Assert.That(reader.ReadByte(), Is.EqualTo(0)); // the first maid's mode
            Assert.That(reader.ReadLong(), Is.EqualTo(0));
            Assert.That(reader.ReadLong(), Is.EqualTo(77));
        });
    }

    [Test]
    public void UserMaid_AddUpdateRemoveProperty() {
        Maid maid = NewMaid();
        byte[] body = FieldMaidPacket.Add(maid).ToArray()[3..]; // after the opcode and FieldMaid's mode
        byte[] add = UserMaidPacket.Add(maid).ToArray();
        byte[] update = UserMaidPacket.Update(maid).ToArray();
        var remove = new ByteReader(UserMaidPacket.Remove(77).ToArray(), 0);
        var property = new ByteReader(UserMaidPacket.Property(1234).ToArray(), 0);
        Assert.Multiple(() => {
            Assert.That(add[2], Is.EqualTo(1));
            Assert.That(add[3..], Is.EqualTo(body));
            Assert.That(update[2], Is.EqualTo(3));
            Assert.That(update[3..], Is.EqualTo(body));
            Assert.That(remove.Read<SendOp>(), Is.EqualTo(SendOp.UserMaid));
            Assert.That(remove.ReadByte(), Is.EqualTo(2));
            Assert.That(remove.ReadLong(), Is.EqualTo(77));
            Assert.That(remove.Available, Is.EqualTo(0));
            Assert.That(property.Read<SendOp>(), Is.EqualTo(SendOp.UserMaid));
            Assert.That(property.ReadByte(), Is.EqualTo(4));
            Assert.That(property.ReadInt(), Is.EqualTo(1234));
            Assert.That(property.Available, Is.EqualTo(0));
        });
    }

    [Test]
    public void Mood_StaysIn0To100_AndGrades() {
        Maid maid = NewMaid(mood: 95);
        Assert.Multiple(() => {
            Assert.That(MaidRules.AddMood(maid, 10), Is.True);
            Assert.That(maid.Mood, Is.EqualTo(100));
            Assert.That(MaidRules.AddMood(maid, 1), Is.False);
            Assert.That(MaidRules.AddMood(maid, -150), Is.True);
            Assert.That(maid.Mood, Is.EqualTo(0));
            Assert.That(MaidRules.MoodGrade(30), Is.EqualTo(0));
            Assert.That(MaidRules.MoodGrade(31), Is.EqualTo(1));
            Assert.That(MaidRules.MoodGrade(70), Is.EqualTo(1));
            Assert.That(MaidRules.MoodGrade(71), Is.EqualTo(2));
        });
    }

    [Test]
    public void AddExp_CarriesOverLevelsAndStopsAtTheLimit() {
        Maid maid = NewMaid(level: 1, exp: 5);
        Assert.Multiple(() => {
            Assert.That(MaidRules.AddExp(maid, 30, Exps, affinityMax: 10), Is.True); // 35: level 1 takes 10, level 2 takes 20
            Assert.That(maid.Level, Is.EqualTo(3));
            Assert.That(maid.Exp, Is.EqualTo(5));
            Assert.That(MaidRules.AddExp(maid, 4, Exps, affinityMax: 10), Is.False);
            Assert.That(maid.Exp, Is.EqualTo(9));
        });

        Maid capped = NewMaid(level: 1);
        MaidRules.AddExp(capped, 1000, Exps, affinityMax: 3);
        Assert.Multiple(() => {
            Assert.That(capped.Level, Is.EqualTo(3)); // MaidAffinityMax below the row count
            Assert.That(capped.Exp, Is.EqualTo(0));
            Assert.That(MaidRules.IsAffinityLevelAtMax(capped, 3, Exps.Count), Is.True);
        });
    }

    [Test]
    public void AddLevel_StaysUnderTheLimit_AndResetGoesBackToOne() {
        Maid maid = NewMaid(level: 3, exp: 7);
        MaidRules.AddLevel(maid, 1, affinityMax: 4, maxLevel: Exps.Count);
        Assert.Multiple(() => {
            Assert.That(maid.Level, Is.EqualTo(4));
            Assert.That(maid.Exp, Is.EqualTo(0));
        });
        MaidRules.AddLevel(maid, 1, affinityMax: 4, maxLevel: Exps.Count);
        Assert.That(maid.Level, Is.EqualTo(4));
        MaidRules.AddLevel(maid, 1, affinityMax: 2, maxLevel: Exps.Count, ignoreLimit: true);
        Assert.That(maid.Level, Is.EqualTo(4));
        MaidRules.ResetLevel(maid);
        Assert.Multiple(() => {
            Assert.That(maid.Level, Is.EqualTo(1));
            Assert.That(maid.Exp, Is.EqualTo(0));
        });
    }

    [Test]
    public void Profile_IsTheOwnersWhileUnnamed() {
        Maid maid = NewMaid();
        Assert.Multiple(() => {
            Assert.That(MaidRules.CanEditProfile(maid, 5, debug: false), Is.True);
            Assert.That(MaidRules.CanEditProfile(maid, 6, debug: true), Is.False);
            maid.Profile.Name = "n";
            Assert.That(MaidRules.CanEditProfile(maid, 5, debug: false), Is.False);
            Assert.That(MaidRules.CanEditProfile(maid, 5, debug: true), Is.True);
            Assert.That(MaidRules.ProfileTexts(maid.Profile).Count(), Is.EqualTo(13));
            Assert.That(MaidRules.IsOwner(maid, 5), Is.True);
            Assert.That(MaidRules.IsOwner(maid, 6), Is.False);
            Assert.That(MaidRules.IsOwner(null, 5), Is.False);
        });
    }

    [Test]
    public void PaidUntil_ExtendsFromTheEndOrFromNow() {
        Assert.Multiple(() => {
            Assert.That(MaidRules.PaidUntil(expiryTime: 5000, now: 1000, periodDays: 2), Is.EqualTo(5000 + 2 * 86400));
            Assert.That(MaidRules.PaidUntil(expiryTime: 500, now: 1000, periodDays: 2), Is.EqualTo(1000 + 2 * 86400));
            Assert.That(MaidRules.PaidUntil(expiryTime: 500, now: 1000, periodDays: -1), Is.EqualTo(1000));
        });
    }

    [Test]
    public void TableEntry_SalaryDefaultsToNone() {
        MaidTable.Entry entry = MaidTable.Row("3", "12", "11001234")!;
        var table = new MaidTable(new Dictionary<int, MaidTable.Entry> { [3] = entry });
        Assert.Multiple(() => {
            Assert.That(entry.SalaryType, Is.EqualTo(0));
            Assert.That(entry.Salary, Is.EqualTo(0));
            Assert.That(table.Exps, Is.Null);
        });
    }

    private static Maid PlacedMaid(long uid = 77) {
        return new Maid { Uid = uid, OwnerId = 5, ItemId = CubeItemId, MaidId = MaidId, CreationTime = 1000, ExpiryTime = 2000 };
    }

    [Test]
    public void StillPlaced_OnlyTheSameMaidObject() {
        Maid maid = PlacedMaid();
        Assert.Multiple(() => {
            Assert.That(MaidRules.StillPlaced(maid, maid), Is.True);
            Assert.That(MaidRules.StillPlaced(null, maid), Is.False); // removed meanwhile
            Assert.That(MaidRules.StillPlaced(PlacedMaid(), maid), Is.False); // placed again: a new record of the uid
        });
    }

    [Test]
    public void Pay_ExtendsOnlyWhenTheSalaryWasTaken() {
        Maid maid = PlacedMaid();
        Assert.Multiple(() => {
            Assert.That(MaidRules.Pay(maid, () => false, 1500, 7), Is.False);
            Assert.That(maid.ExpiryTime, Is.EqualTo(2000));
            Assert.That(MaidRules.Pay(maid, () => true, 1500, 7), Is.True);
            Assert.That(maid.ExpiryTime, Is.EqualTo(2000 + 7L * 86400));
        });
    }

    [Test]
    public void ScheduledPay_TakesNoSalaryForAGoneMaid() {
        // The scheduled step of maidPay: StillPlaced first, then Pay; the charge is never asked for a gone maid.
        Maid maid = PlacedMaid();
        int charged = 0;
        bool Step(Maid? current) => MaidRules.StillPlaced(current, maid) && MaidRules.Pay(maid, () => { charged++; return true; }, 1500, 7);
        Assert.Multiple(() => {
            Assert.That(Step(null), Is.False);
            Assert.That(charged, Is.EqualTo(0));
            Assert.That(maid.ExpiryTime, Is.EqualTo(2000));
            Assert.That(Step(maid), Is.True);
            Assert.That(charged, Is.EqualTo(1));
        });
    }

    [Test]
    public void ApplyDebug_EachButton() {
        var exps = new Dictionary<int, int> { [1] = 100, [2] = 200, [3] = 300 };
        Maid maid = PlacedMaid();
        MaidRules.ApplyDebug(maid, MaidRules.DebugCommand.AffinityGrade, 0, 0, exps, 10, 10);
        Assert.That(maid.Level, Is.EqualTo(2));
        MaidRules.ApplyDebug(maid, MaidRules.DebugCommand.AffinityUp, 0, 0, exps, 10, 10);
        Assert.That(maid.Level, Is.EqualTo(3));
        MaidRules.ApplyDebug(maid, MaidRules.DebugCommand.AffinityReset, 0, 0, exps, 10, 10);
        Assert.That(maid.Level, Is.EqualTo(1));
        MaidRules.ApplyDebug(maid, MaidRules.DebugCommand.MoodUp, 0, 0, exps, 10, 10);
        MaidRules.ApplyDebug(maid, MaidRules.DebugCommand.MoodUp, 0, 0, exps, 10, 10);
        MaidRules.ApplyDebug(maid, MaidRules.DebugCommand.MoodDown, 0, 0, exps, 10, 10);
        Assert.That(maid.Mood, Is.EqualTo(10));
        MaidRules.ApplyDebug(maid, MaidRules.DebugCommand.Expiry, 0, 500, exps, 10, 10);
        Assert.That(maid.ExpiryTime, Is.EqualTo(1500));
        MaidRules.ApplyDebug(maid, MaidRules.DebugCommand.Expiry, 1, 900, exps, 10, 10);
        Assert.That(maid.ExpiryTime, Is.EqualTo(2400));
        MaidRules.ApplyDebug(maid, MaidRules.DebugCommand.Expiry, 1, -900, exps, 10, 10);
        Assert.That(maid.ExpiryTime, Is.EqualTo(2400)); // a negative span is ignored
    }
}
