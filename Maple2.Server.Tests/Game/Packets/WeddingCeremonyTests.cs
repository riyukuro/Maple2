using System.Collections.Generic;
using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Packets;

// The ceremony in a wedding hall: the way in (Wedding 0x1a), the questions of the hall trigger (Wedding 0x1e,
// 0x1f), the vow and the marriage time.
public class WeddingCeremonyTests {
    private const long Groom = 11;
    private const long Bride = 22;
    private const long Guest = 33;

    private static WeddingCeremony Make() => new(7, Groom, Bride);

    [Test]
    public void EnterWindow_IsTheClientsTicketWindow() {
        Assert.That(WeddingCeremony.InEnterWindow(10000, 10000), Is.True);
        Assert.That(WeddingCeremony.InEnterWindow(10000, 10000 - 1800), Is.True, "half an hour before");
        Assert.That(WeddingCeremony.InEnterWindow(10000, 10000 + 1800), Is.True, "half an hour after");
        Assert.That(WeddingCeremony.InEnterWindow(10000, 10000 - 1801), Is.False);
        Assert.That(WeddingCeremony.InEnterWindow(10000, 10000 + 1801), Is.False);
    }

    // weddingpackage hall 1 (Tria): one day row and one night row (nightReward 1).
    [Test]
    public void CompleteGifts_DayOrNightRow() {
        WeddingPackage.HallData.Item day = new(20302963, 1, 1, false);
        WeddingPackage.HallData.Item night = new(20302966, 1, 1, true);
        WeddingPackage.HallData.Item[] items = [day, night];

        Assert.That(WeddingCeremony.CompleteGifts(items, night: false), Is.EqualTo(new[] { day }));
        Assert.That(WeddingCeremony.CompleteGifts(items, night: true), Is.EqualTo(new[] { night }));
        Assert.That(WeddingCeremony.CompleteGifts([], night: true), Is.Empty);
    }

    [Test]
    public void Enter_CoupleGuestsOrPublic() {
        Assert.That(WeddingCeremony.CanEnter(couple: true, publicHall: false, guest: false), Is.True);
        Assert.That(WeddingCeremony.CanEnter(couple: false, publicHall: true, guest: false), Is.True);
        Assert.That(WeddingCeremony.CanEnter(couple: false, publicHall: false, guest: true), Is.True);
        Assert.That(WeddingCeremony.CanEnter(couple: false, publicHall: false, guest: false), Is.False);
    }

    [Test]
    public void HallMap_DayHoursAre6To17() {
        Assert.That(WeddingCeremony.IsNight(5), Is.True);
        Assert.That(WeddingCeremony.IsNight(6), Is.False);
        Assert.That(WeddingCeremony.IsNight(17), Is.False);
        Assert.That(WeddingCeremony.IsNight(18), Is.True);
        Assert.That(WeddingCeremony.IsNight(0), Is.True);
        Assert.That(WeddingCeremony.HallMap(84000011, 84000021, 12), Is.EqualTo(84000011));
        Assert.That(WeddingCeremony.HallMap(84000011, 84000021, 20), Is.EqualTo(84000021));
        Assert.That(WeddingCeremony.HallMap(84000011, 0, 20), Is.EqualTo(84000011), "a hall without a night map");
    }

    [Test]
    public void HallMap_HasOneRoomPerMarriage() {
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.WeddingHall, 7, 0), Is.True);
        Assert.That(FieldEntryRules.HasOwnerRoom(InstanceType.WeddingHall, 0, 0), Is.False);
    }

    [Test]
    public void EntryInField_BothOfTheCouple() {
        WeddingCeremony ceremony = Make();
        var present = new HashSet<long> { Groom, Guest };
        Assert.That(ceremony.CoupleInField(true, present.Contains), Is.False);
        Assert.That(ceremony.CoupleInField(false, present.Contains), Is.True, "one is missing");
        present.Add(Bride);
        Assert.That(ceremony.CoupleInField(true, present.Contains), Is.True);
        Assert.That(ceremony.CoupleInField(false, present.Contains), Is.False);
        Assert.That(ceremony.IsCouple(Guest), Is.False);
        Assert.That(ceremony.IsCouple(0), Is.False);
    }

    [Test]
    public void MutualAgree_SuccessNeedsBothAnswers() {
        WeddingCeremony ceremony = Make();
        Assert.That(ceremony.Answer(Groom, WeddingAgreeType.StartActing, true), Is.False, "nothing asked");
        Assert.That(ceremony.AgreeResult(WeddingAgreeType.StartActing, true), Is.False);

        Assert.That(ceremony.Ask(WeddingAgreeType.StartActing), Is.True);
        Assert.That(ceremony.Answer(Guest, WeddingAgreeType.StartActing, true), Is.False, "not of the couple");
        Assert.That(ceremony.Answer(Groom, WeddingAgreeType.EndActing, true), Is.False, "another question");
        Assert.That(ceremony.Answer(Groom, WeddingAgreeType.StartActing, true), Is.True);
        Assert.That(ceremony.AgreeResult(WeddingAgreeType.StartActing, true), Is.False, "one answer");
        Assert.That(ceremony.AgreeResult(WeddingAgreeType.StartActing, false), Is.False, "nobody refused");
        Assert.That(ceremony.Answer(Bride, WeddingAgreeType.StartActing, true), Is.True);
        Assert.That(ceremony.AgreeResult(WeddingAgreeType.StartActing, true), Is.True);
        Assert.That(ceremony.AgreeResult(WeddingAgreeType.StartActing, false), Is.False);
    }

    [Test]
    public void MutualAgree_OneRefusalFails() {
        WeddingCeremony ceremony = Make();
        ceremony.Ask(WeddingAgreeType.EndActing);
        ceremony.Answer(Groom, WeddingAgreeType.EndActing, true);
        ceremony.Answer(Bride, WeddingAgreeType.EndActing, false);
        Assert.That(ceremony.AgreeResult(WeddingAgreeType.EndActing, true), Is.False);
        Assert.That(ceremony.AgreeResult(WeddingAgreeType.EndActing, false), Is.True);
    }

    [Test]
    public void MutualAgree_CancelAndAskAgainDropAnswers() {
        WeddingCeremony ceremony = Make();
        ceremony.Ask(WeddingAgreeType.PartnerName);
        ceremony.Answer(Groom, WeddingAgreeType.PartnerName, true);
        ceremony.Answer(Bride, WeddingAgreeType.PartnerName, true);
        Assert.That(ceremony.Cancel(WeddingAgreeType.PartnerName), Is.True);
        Assert.That(ceremony.AgreeResult(WeddingAgreeType.PartnerName, true), Is.False);
        Assert.That(ceremony.Cancel(WeddingAgreeType.PartnerName), Is.False, "nothing open");
        Assert.That(ceremony.Answer(Groom, WeddingAgreeType.PartnerName, true), Is.False);

        ceremony.Ask(WeddingAgreeType.PartnerName);
        ceremony.Answer(Groom, WeddingAgreeType.PartnerName, false);
        ceremony.Ask(WeddingAgreeType.PartnerName);
        Assert.That(ceremony.AgreeResult(WeddingAgreeType.PartnerName, false), Is.False, "the earlier refusal is gone");
        Assert.That(ceremony.Ask(WeddingAgreeType.None), Is.False);
    }

    [Test]
    public void AgreeType_FromTheTriggerAttribute() {
        Assert.That(WeddingCeremony.ParseAgreeType("startActing"), Is.EqualTo(WeddingAgreeType.StartActing));
        Assert.That(WeddingCeremony.ParseAgreeType("endActing"), Is.EqualTo(WeddingAgreeType.EndActing));
        Assert.That(WeddingCeremony.ParseAgreeType("partnerName"), Is.EqualTo(WeddingAgreeType.PartnerName));
        Assert.That(WeddingCeremony.ParseAgreeType(""), Is.EqualTo(WeddingAgreeType.None));
        Assert.That((byte) WeddingAgreeType.StartActing, Is.EqualTo(1));
        Assert.That((byte) WeddingAgreeType.EndActing, Is.EqualTo(2));
        Assert.That((byte) WeddingAgreeType.PartnerName, Is.EqualTo(3));
    }

    [Test]
    public void VowComplete_Once() {
        WeddingCeremony ceremony = Make();
        Assert.That(ceremony.Complete, Is.False);
        Assert.That(ceremony.TryComplete(), Is.True);
        Assert.That(ceremony.Complete, Is.True);
        Assert.That(ceremony.TryComplete(), Is.False);
        ceremony.Uncomplete();
        Assert.That(ceremony.Complete, Is.False);
        Assert.That(ceremony.TryComplete(), Is.True);
    }

    [Test]
    public void OnlyAnEngagedCoupleIsMarried() {
        Assert.That(Marriage.CanMarry(MaritalStatus.Engaged), Is.True);
        Assert.That(Marriage.CanMarry(MaritalStatus.Single), Is.False);
        Assert.That(Marriage.CanMarry(MaritalStatus.Married), Is.False);
    }

    [Test]
    public void ScriptCondition_HallStateAndEntryType() {
        WeddingCeremony ceremony = Make();
        Assert.That(WeddingCeremony.ScriptCondition(ceremony, Guest, "", true, null, true), Is.True, "no test");
        Assert.That(WeddingCeremony.ScriptCondition(null, Guest, "", true, "", true), Is.True);
        Assert.That(WeddingCeremony.ScriptCondition(ceremony, Groom, "", true, "GroomBride", true), Is.True);
        Assert.That(WeddingCeremony.ScriptCondition(ceremony, Guest, "", true, "GroomBride", true), Is.False);
        Assert.That(WeddingCeremony.ScriptCondition(ceremony, Guest, "", true, "GroomBride", false), Is.True);
        Assert.That(WeddingCeremony.ScriptCondition(ceremony, Groom, "", true, "GroomBride", false), Is.False);
        Assert.That(WeddingCeremony.ScriptCondition(null, Groom, "", true, "GroomBride", true), Is.False, "no ceremony");
        Assert.That(WeddingCeremony.ScriptCondition(ceremony, Groom, "weddingComplete", true, null, true), Is.False);
        Assert.That(WeddingCeremony.ScriptCondition(ceremony, Groom, "weddingComplete", false, null, true), Is.True);
        ceremony.TryComplete();
        Assert.That(WeddingCeremony.ScriptCondition(ceremony, Groom, "weddingComplete", true, null, true), Is.True);
        Assert.That(WeddingCeremony.ScriptCondition(ceremony, Groom, "weddingComplete", false, null, true), Is.False);
    }

    private static Marriage MakeMarriage() {
        return new Marriage {
            Id = 9,
            Status = MaritalStatus.Married,
            CreationTime = 1000,
            MarriageTime = 2000,
            Partner1 = new MarriagePartner { Info = new PlayerInfo(new CharacterInfo(1, 2, "a", "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []) },
            Partner2 = new MarriagePartner { Info = new PlayerInfo(new CharacterInfo(4, 3, "b", "", "", Gender.Female, Job.Newbie, 1), "", new AchievementInfo(), []) },
        };
    }

    [Test]
    public void Marriage_SecondTimeIsTheMarriageTime() {
        var writer = new ByteWriter();
        MakeMarriage().WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.ReadLong(), Is.EqualTo(9));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) MaritalStatus.Married));
        Assert.That(reader.ReadLong(), Is.EqualTo(1000));
        Assert.That(reader.ReadLong(), Is.EqualTo(2000));

        Marriage engaged = MakeMarriage();
        engaged.Status = MaritalStatus.Engaged;
        engaged.MarriageTime = 0;
        writer = new ByteWriter();
        engaged.WriteTo(writer);
        reader = new ByteReader(writer.Buffer, 0);
        reader.ReadLong();
        reader.ReadByte();
        reader.ReadLong();
        Assert.That(reader.ReadLong(), Is.EqualTo(0), "not married yet");
    }

    private static WeddingHall MakeHall() {
        return new WeddingHall {
            Id = 5,
            MarriageId = 9,
            ReserverAccountId = 2,
            ReserverCharacterId = 1,
            ReserverName = "a",
            PartnerName = "b",
            CeremonyTime = 3000,
            PackageId = 1,
            PackageHallId = 2,
        };
    }

    [Test]
    public void InvitationClear_IsMode23WithoutABody() {
        ByteWriter packet = WeddingPacket.InvitationClear();

        var reader = new ByteReader(packet.Buffer, 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.Wedding));
        Assert.That(reader.ReadByte(), Is.EqualTo(23));
        Assert.That(packet.Length, Is.EqualTo(3));
    }

    [Test]
    public void Ticket_IdThenTheHall() {
        WeddingHall hall = MakeHall();
        ByteWriter packet = WeddingPacket.Ticket(9, hall);
        var expected = new ByteWriter();
        hall.WriteTo(expected);

        var reader = new ByteReader(packet.Buffer, 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.Wedding));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x18));
        Assert.That(reader.ReadLong(), Is.EqualTo(9));
        Assert.That(packet.Length - reader.Position, Is.EqualTo(expected.Length));
        Assert.That(reader.ReadLong(), Is.EqualTo(5));
    }

    [Test]
    public void MutualAgree_NameOnlyForTheTypedStep() {
        ByteWriter packet = WeddingPacket.MutualAgree(WeddingAgreeType.StartActing, "b");
        var reader = new ByteReader(packet.Buffer, 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.Wedding));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x1e));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Position, Is.EqualTo(packet.Length));

        packet = WeddingPacket.MutualAgree(WeddingAgreeType.PartnerName, "b");
        reader = new ByteReader(packet.Buffer, 0);
        reader.ReadShort();
        Assert.That(reader.ReadByte(), Is.EqualTo(0x1e));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("b"));
        Assert.That(reader.Position, Is.EqualTo(packet.Length));
    }

    [Test]
    public void MutualCancel_OneByte() {
        ByteWriter packet = WeddingPacket.MutualCancel(WeddingAgreeType.PartnerName);
        var reader = new ByteReader(packet.Buffer, 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.Wedding));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x1f));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.Position, Is.EqualTo(packet.Length));
    }

    // The hall is over one hour after the ceremony time, and not before; no hall is never over.
    [Test]
    public void HallExpired_OneHourAfterTheCeremonyTime() {
        const long time = 1_000_000;
        Assert.That(WeddingCeremony.HallExpired(5, time, time + 3599), Is.False);
        Assert.That(WeddingCeremony.HallExpired(5, time, time + 3600), Is.True);
        Assert.That(WeddingCeremony.HallExpired(5, time, time - 100), Is.False);
        Assert.That(WeddingCeremony.HallExpired(0, time, time + 9999), Is.False);
    }

    // The end of the hall takes the hall only: a couple married in it stays married in the session of the
    // first partner to change field after it, and an engaged couple stays engaged.
    [Test]
    public void AfterHallExpiry_KeepsTheMarriageAndDropsTheHall() {
        foreach (MaritalStatus status in new[] { MaritalStatus.Married, MaritalStatus.Engaged }) {
            var marriage = new Marriage {
                Id = 77,
                Status = status,
                MarriageTime = 1_000_600,
                Partner1 = new MarriagePartner(),
                Partner2 = new MarriagePartner(),
            };
            (Marriage kept, WeddingHall hall) = WeddingCeremony.AfterHallExpiry(marriage);
            Assert.That(kept, Is.SameAs(marriage));
            Assert.That(kept.Id, Is.EqualTo(77));
            Assert.That(kept.Status, Is.EqualTo(status));
            Assert.That(kept.MarriageTime, Is.EqualTo(1_000_600));
            Assert.That(hall.Id, Is.EqualTo(0));
        }
    }

    // No way into a hall from a dungeon room; from any other field it is open.
    [Test]
    public void CanEnterFrom_NotFromADungeonRoom() {
        Assert.That(WeddingCeremony.CanEnterFrom(dungeonField: true), Is.False);
        Assert.That(WeddingCeremony.CanEnterFrom(dungeonField: false), Is.True);
    }

    // The stored trigger attributes are snake case; success is absent on the plain test.
    [Test]
    public void TriggerConditions_ReadTheStoredAttributes() {
        var doc = new XmlDocument();
        doc.LoadXml("<c hall_state=\"weddingComplete\" success=\"0\" agree_type=\"endActing\"/>");
        XmlAttributeCollection attrs = doc.DocumentElement!.Attributes;
        Assert.That(TriggerFunctionMapping.ConditionMap["wedding_hall_state"](attrs), Is.InstanceOf<Trigger.WeddingHallState>());
        Assert.That(TriggerFunctionMapping.ConditionMap["wedding_mutual_agree_result"](attrs), Is.InstanceOf<Trigger.WeddingMutualAgreeResult>());
        Assert.That(TriggerFunctionMapping.ParseBoolOr(null, true), Is.True);
        Assert.That(TriggerFunctionMapping.ParseBoolOr("", true), Is.True);
        Assert.That(TriggerFunctionMapping.ParseBoolOr("0", true), Is.False);
        Assert.That(TriggerFunctionMapping.ParseBoolOr("1", false), Is.True);
    }
}
