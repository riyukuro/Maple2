using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The divorce: the two filed states, the npc script tests and steps, the cooling off, the fee, the result codes
// 0x2d to 0x37 and the propose refusals 0x19 to 0x1e.
public class DivorceRulesTests {
    private const long First = 11;
    private const long Second = 22;
    private const long Day = DivorceRules.DaySeconds;
    private const long Now = 100 * Day;
    private const int RequireDays = 30;
    private const int CoolDays = 7;
    private const int CooltimeDays = 2;
    private const long Fee = 1000000;

    private const MaritalStatus ByGroom = MaritalStatus.DivorceFiledByGroom;
    private const MaritalStatus ByBride = MaritalStatus.DivorceFiledByBride;

    [Test]
    public void States_HaveTheClientsNumbers() {
        Assert.That((short) MaritalStatus.Married, Is.EqualTo(2));
        Assert.That((short) ByGroom, Is.EqualTo(3));
        Assert.That((short) ByBride, Is.EqualTo(4));
        Assert.That((short) MaritalStatus.Divorced, Is.EqualTo(5));
    }

    [Test]
    public void FiledState_NamesTheFilerByPartnerOrder() {
        Assert.That(DivorceRules.FiledStatus(First, First), Is.EqualTo(ByGroom));
        Assert.That(DivorceRules.FiledStatus(Second, First), Is.EqualTo(ByBride));
        Assert.That(DivorceRules.FilerId(ByGroom, First, Second), Is.EqualTo(First));
        Assert.That(DivorceRules.FilerId(ByBride, First, Second), Is.EqualTo(Second));
        Assert.That(DivorceRules.FilerId(MaritalStatus.Married, First, Second), Is.EqualTo(0));
        Assert.That(DivorceRules.FilerId(MaritalStatus.Divorced, First, Second), Is.EqualTo(0));
    }

    [Test]
    public void SwappedView_KeepsTheSameFiler() {
        foreach (long filer in new[] { First, Second }) {
            MaritalStatus stored = DivorceRules.FiledStatus(filer, First);
            // The first stored partner sees the partner first and itself second.
            Assert.That(DivorceRules.FilerId(DivorceRules.Swapped(stored), Second, First), Is.EqualTo(filer));
        }
        Assert.That(DivorceRules.Swapped(MaritalStatus.Married), Is.EqualTo(MaritalStatus.Married));
        Assert.That(DivorceRules.Swapped(MaritalStatus.Divorced), Is.EqualTo(MaritalStatus.Divorced));
        Assert.That(DivorceRules.Swapped(MaritalStatus.Engaged), Is.EqualTo(MaritalStatus.Engaged));
    }

    // The client's WeddingProxy::IsPartner (weddingproxy.cpp:639) on the packet a viewer gets: groom = the first
    // partner sent (the other one), bride = the second (the viewer). True names the filer: CheckLeave gives 0x31
    // (s_wedding_result_invalid_divorce_agree_user) and CheckEnter 0x37 (s_wedding_result_divorcecancel) on it.
    private static bool ClientIsPartner(MaritalStatus sent, long groomId, long id) {
        return sent switch {
            ByGroom => groomId == id,
            ByBride => groomId != id,
            _ => false,
        };
    }

    [Test]
    public void ClientReading_OfTheSentStateNamesTheFilerForBothViewers() {
        foreach (long filer in new[] { First, Second }) {
            MaritalStatus stored = DivorceRules.FiledStatus(filer, First);
            // First stored partner: gets the swapped state with Second first. Second: the stored state with First first.
            var views = new[] { (Viewer: First, Sent: DivorceRules.Swapped(stored), Groom: Second), (Viewer: Second, Sent: stored, Groom: First) };
            foreach ((long viewer, MaritalStatus sent, long groom) in views) {
                bool isPartner = ClientIsPartner(sent, groom, viewer);
                Assert.That(isPartner, Is.EqualTo(viewer == filer));
                // UIWeddingDialog::OnLoadComplete (uiweddingdialog.cpp:159): the window's divorce line is set for
                // IsPartner(own id) == false, so it is the other partner's line, the one with the agree box.
                bool divorceLine = !isPartner;
                Assert.That(divorceLine, Is.EqualTo(viewer != filer));
            }
        }
        // The two bytes the live try saw: the filer A (stored second here) gets 04, B gets 03.
        Assert.That(DivorceRules.Swapped(DivorceRules.FiledStatus(First, First)), Is.EqualTo(ByBride));
        Assert.That(DivorceRules.FiledStatus(Second, First), Is.EqualTo(ByBride));
    }

    [Test]
    public void OlderThanDays_IsTheClientsTest() {
        Assert.That(DivorceRules.OlderThanDays(Now - 30 * Day, 30, Now), Is.False, "exactly 30 days is not older");
        Assert.That(DivorceRules.OlderThanDays(Now - 30 * Day - 1, 30, Now), Is.True);
        Assert.That(DivorceRules.OlderThanDays(0, 30000, 3000000000), Is.True, "no int overflow");
    }

    [Test]
    public void File_NeedsMarriedLongEnoughAndTheFee() {
        long longAgo = Now - 31 * Day;
        Assert.That(DivorceRules.CheckFile(MaritalStatus.Married, longAgo, RequireDays, Fee, Fee, Now), Is.EqualTo(WeddingError.none));
        foreach (MaritalStatus status in new[] { MaritalStatus.Single, MaritalStatus.Engaged, ByGroom, ByBride, MaritalStatus.Divorced }) {
            Assert.That(DivorceRules.CheckFile(status, longAgo, RequireDays, Fee, Fee, Now), Is.EqualTo(WeddingError.s_wedding_result_invaild_state_divorce), status.ToString());
        }
        Assert.That(DivorceRules.CheckFile(MaritalStatus.Married, Now - 30 * Day, RequireDays, Fee, Fee, Now), Is.EqualTo(WeddingError.s_wedding_result_lack_marriage_date));
        Assert.That(DivorceRules.CheckFile(MaritalStatus.Married, longAgo, RequireDays, Fee - 1, Fee, Now), Is.EqualTo(WeddingError.s_wedding_result_lack_meso_divorce));
        // The state comes before the days, the days before the fee.
        Assert.That(DivorceRules.CheckFile(MaritalStatus.Engaged, Now, RequireDays, 0, Fee, Now), Is.EqualTo(WeddingError.s_wedding_result_invaild_state_divorce));
        Assert.That(DivorceRules.CheckFile(MaritalStatus.Married, Now, RequireDays, 0, Fee, Now), Is.EqualTo(WeddingError.s_wedding_result_lack_marriage_date));
    }

    [Test]
    public void Agree_IsForTheOtherPartnerDuringTheCoolingOff() {
        long filed = Now - Day;
        Assert.That(DivorceRules.CheckAgree(ByGroom, false, filed, CoolDays, Now), Is.EqualTo(WeddingError.none));
        Assert.That(DivorceRules.CheckAgree(ByBride, false, filed, CoolDays, Now), Is.EqualTo(WeddingError.none));
        Assert.That(DivorceRules.CheckAgree(ByGroom, true, filed, CoolDays, Now), Is.EqualTo(WeddingError.s_wedding_result_invalid_divorce_agree_user));
        Assert.That(DivorceRules.CheckAgree(MaritalStatus.Married, false, 0, CoolDays, Now), Is.EqualTo(WeddingError.s_wedding_result_invaild_state_divorce));
        Assert.That(DivorceRules.CheckAgree(MaritalStatus.Divorced, false, filed, CoolDays, Now), Is.EqualTo(WeddingError.s_wedding_result_invaild_state_divorce));
        Assert.That(DivorceRules.CheckAgree(ByGroom, false, Now - 7 * Day, CoolDays, Now), Is.EqualTo(WeddingError.none), "the last second");
        Assert.That(DivorceRules.CheckAgree(ByGroom, false, Now - 7 * Day - 1, CoolDays, Now), Is.EqualTo(WeddingError.s_wedding_result_late_divorce_agree));
    }

    [Test]
    public void Cancel_IsForTheFilerDuringTheCoolingOff() {
        long filed = Now - Day;
        Assert.That(DivorceRules.CheckCancel(ByGroom, true, filed, CoolDays, Now), Is.EqualTo(WeddingError.none));
        Assert.That(DivorceRules.CheckCancel(ByBride, true, Now - 7 * Day, CoolDays, Now), Is.EqualTo(WeddingError.none));
        Assert.That(DivorceRules.CheckCancel(ByGroom, false, filed, CoolDays, Now), Is.EqualTo(WeddingError.s_wedding_result_divorcecancel));
        Assert.That(DivorceRules.CheckCancel(ByGroom, true, Now - 7 * Day - 1, CoolDays, Now), Is.EqualTo(WeddingError.s_wedding_result_divorcecancel));
        Assert.That(DivorceRules.CheckCancel(MaritalStatus.Married, false, 0, CoolDays, Now), Is.EqualTo(WeddingError.s_wedding_result_divorcecancel));
    }

    [Test]
    public void Mutual_NeedsMarried() {
        Assert.That(DivorceRules.CheckMutual(MaritalStatus.Married), Is.EqualTo(WeddingError.none));
        foreach (MaritalStatus status in new[] { MaritalStatus.Single, MaritalStatus.Engaged, ByGroom, ByBride, MaritalStatus.Divorced }) {
            Assert.That(DivorceRules.CheckMutual(status), Is.EqualTo(WeddingError.s_wedding_result_only_marriage_user), status.ToString());
        }
        Assert.That(DivorceRules.AnswerYes, Is.EqualTo(13));
        Assert.That(DivorceRules.AnswerNo, Is.EqualTo(14));
    }

    [Test]
    public void Visit_IsForTheOtherPartnerOfAFiledDivorce() {
        Assert.That(DivorceRules.CheckVisit(ByGroom, false), Is.EqualTo(WeddingError.none));
        Assert.That(DivorceRules.CheckVisit(ByBride, false), Is.EqualTo(WeddingError.none));
        Assert.That(DivorceRules.CheckVisit(ByBride, true), Is.EqualTo(WeddingError.s_wedding_result_invalid_divorce_agree_user));
        Assert.That(DivorceRules.CheckVisit(MaritalStatus.Married, false), Is.EqualTo(WeddingError.s_wedding_result_invaild_state_divorce));
        Assert.That(DivorceRules.CheckVisit(MaritalStatus.Divorced, false), Is.EqualTo(WeddingError.s_wedding_result_invaild_state_divorce));
    }

    [Test]
    public void ResultCodes_AreTheClients() {
        Assert.That((int) WeddingError.s_wedding_result_invaild_state_divorce, Is.EqualTo(0x2d));
        Assert.That((int) WeddingError.s_wedding_result_lack_marriage_date, Is.EqualTo(0x2e));
        Assert.That((int) WeddingError.s_wedding_result_lack_meso_divorce, Is.EqualTo(0x2f));
        Assert.That((int) WeddingError.s_wedding_result_late_divorce_agree, Is.EqualTo(0x30));
        Assert.That((int) WeddingError.s_wedding_result_invalid_divorce_agree_user, Is.EqualTo(0x31));
        Assert.That((int) WeddingError.s_wedding_result_only_marriage_user, Is.EqualTo(0x32));
        Assert.That((int) WeddingError.s_wedding_result_divorcecancel, Is.EqualTo(0x37));
    }

    [Test]
    public void CoolingOff_EndsSevenDaysAfterTheFiling() {
        Assert.That(DivorceRules.CoolingOffEnd(1000, CoolDays), Is.EqualTo(1000 + 7 * Day));
        Assert.That(DivorceRules.CoolingOffOver(ByGroom, Now - 7 * Day, CoolDays, Now), Is.False);
        Assert.That(DivorceRules.CoolingOffOver(ByBride, Now - 7 * Day - 1, CoolDays, Now), Is.True);
        Assert.That(DivorceRules.CoolingOffOver(MaritalStatus.Married, 0, CoolDays, Now), Is.False, "nothing filed");
        Assert.That(DivorceRules.CoolingOffOver(MaritalStatus.Divorced, 0, CoolDays, Now), Is.False);
    }

    [Test]
    public void ProposeCooltime_EndsTheDivorcedState() {
        Assert.That(DivorceRules.ProposeCooltimeOver(MaritalStatus.Divorced, Now - 2 * Day, CooltimeDays, Now), Is.False);
        Assert.That(DivorceRules.ProposeCooltimeOver(MaritalStatus.Divorced, Now - 2 * Day - 1, CooltimeDays, Now), Is.True);
        Assert.That(DivorceRules.ProposeCooltimeOver(ByGroom, 0, CooltimeDays, Now), Is.False);
        Assert.That(DivorceRules.ProposeCooltimeOver(MaritalStatus.Married, 0, CooltimeDays, Now), Is.False);
    }

    [Test]
    public void Propose_IsRefusedByEitherSidesState() {
        Assert.That(DivorceRules.ProposeFrom(MaritalStatus.Single), Is.EqualTo(WeddingError.none));
        Assert.That((int) DivorceRules.ProposeFrom(MaritalStatus.Engaged), Is.EqualTo(0x17));
        Assert.That((int) DivorceRules.ProposeFrom(MaritalStatus.Married), Is.EqualTo(0x18));
        Assert.That((int) DivorceRules.ProposeFrom(ByGroom), Is.EqualTo(0x19));
        Assert.That((int) DivorceRules.ProposeFrom(ByBride), Is.EqualTo(0x19));
        Assert.That((int) DivorceRules.ProposeFrom(MaritalStatus.Divorced), Is.EqualTo(0x1a));
        Assert.That(DivorceRules.ProposeTo(MaritalStatus.Single), Is.EqualTo(WeddingError.none));
        Assert.That((int) DivorceRules.ProposeTo(MaritalStatus.Engaged), Is.EqualTo(0x1b));
        Assert.That((int) DivorceRules.ProposeTo(MaritalStatus.Married), Is.EqualTo(0x1c));
        Assert.That((int) DivorceRules.ProposeTo(ByGroom), Is.EqualTo(0x1d));
        Assert.That((int) DivorceRules.ProposeTo(ByBride), Is.EqualTo(0x1d));
        Assert.That((int) DivorceRules.ProposeTo(MaritalStatus.Divorced), Is.EqualTo(0x1e));
    }

    [Test]
    public void ScriptCondition_WeddingState() {
        Assert.That(DivorceRules.ScriptCondition(null, 0, "", MaritalStatus.Married, false, 0, Now), Is.True, "no test");
        Assert.That(DivorceRules.ScriptCondition(MaritalStatus.Married, 0, "", MaritalStatus.Married, false, 0, Now), Is.True);
        Assert.That(DivorceRules.ScriptCondition(MaritalStatus.Single, 0, "", MaritalStatus.Married, false, 0, Now), Is.False);
        Assert.That(DivorceRules.ScriptCondition(MaritalStatus.Single, 0, null, MaritalStatus.Single, false, 0, Now), Is.True);
        Assert.That(DivorceRules.ScriptCondition(ByGroom, 0, "", ByBride, false, 0, Now), Is.False);
        Assert.That(DivorceRules.ScriptCondition(MaritalStatus.Divorced, 0, "", MaritalStatus.Divorced, false, 0, Now), Is.True);
    }

    [Test]
    public void ScriptCondition_MarriageDate() {
        Assert.That(DivorceRules.ScriptCondition(MaritalStatus.Married, 30, "", MaritalStatus.Married, false, Now - 31 * Day, Now), Is.True);
        Assert.That(DivorceRules.ScriptCondition(MaritalStatus.Married, 30, "", MaritalStatus.Married, false, Now - 30 * Day, Now), Is.False);
        Assert.That(DivorceRules.ScriptCondition(null, 30, "", MaritalStatus.Engaged, false, 0, Now), Is.False, "never married");
    }

    [Test]
    public void ScriptCondition_CoolingOff() {
        // request: the filer's script (it leads to the cancel step); response: the other partner's (the agree step).
        Assert.That(DivorceRules.ScriptCondition(ByGroom, 0, DivorceRules.CoolingOffRequest, ByGroom, true, 0, Now), Is.True);
        Assert.That(DivorceRules.ScriptCondition(ByGroom, 0, DivorceRules.CoolingOffRequest, ByGroom, false, 0, Now), Is.False);
        Assert.That(DivorceRules.ScriptCondition(ByBride, 0, DivorceRules.CoolingOffResponse, ByBride, false, 0, Now), Is.True);
        Assert.That(DivorceRules.ScriptCondition(ByBride, 0, DivorceRules.CoolingOffResponse, ByBride, true, 0, Now), Is.False);
        Assert.That(DivorceRules.ScriptCondition(null, 0, DivorceRules.CoolingOffResponse, MaritalStatus.Married, false, 0, Now), Is.False, "nothing filed");
        Assert.That(DivorceRules.ScriptCondition(null, 0, DivorceRules.CoolingOffRequest, MaritalStatus.Married, false, 0, Now), Is.False);
        Assert.That(DivorceRules.ScriptCondition(null, 0, "other", ByGroom, false, 0, Now), Is.True, "unknown value is no test");
    }

    [Test]
    public void FunctionNames_AreTheServerTables() {
        Assert.That(DivorceRules.FunctionMutual, Is.EqualTo("mutual"));
        Assert.That(DivorceRules.FunctionFile, Is.EqualTo("individual"));
        Assert.That(DivorceRules.FunctionAgree, Is.EqualTo("individualAgree"));
        Assert.That(DivorceRules.FunctionCancel, Is.EqualTo("individualcancel"));
    }

    [Test]
    public void StateTime_IsTheDivorceTimeInTheDivorceStates() {
        Assert.That(DivorceRules.StateTime(MaritalStatus.Engaged, 5, 9), Is.EqualTo(5));
        Assert.That(DivorceRules.StateTime(MaritalStatus.Married, 5, 9), Is.EqualTo(5));
        Assert.That(DivorceRules.StateTime(ByGroom, 5, 9), Is.EqualTo(9));
        Assert.That(DivorceRules.StateTime(ByBride, 5, 9), Is.EqualTo(9));
        Assert.That(DivorceRules.StateTime(MaritalStatus.Divorced, 5, 9), Is.EqualTo(9));
    }

    [Test]
    public void MarriagePacket_CarriesTheFilingTimeFirst() {
        var marriage = new Marriage {
            Id = 7,
            Status = ByBride,
            CreationTime = 5,
            MarriageTime = 6,
            DivorceTime = 9,
            Partner1 = new MarriagePartner(),
            Partner2 = new MarriagePartner(),
        };
        var writer = new ByteWriter();
        marriage.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.ReadLong(), Is.EqualTo(7));
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadLong(), Is.EqualTo(9), "state time");
        Assert.That(reader.ReadLong(), Is.EqualTo(6), "marriage time");
    }

    [Test]
    public void Visit_OnlyFromWhereAMapMoveIsAllowed() {
        Assert.That(DivorceRules.CanVisitFrom(inField: true, dead: false, dungeonField: false, weddingHall: false), Is.True);
        Assert.That(DivorceRules.CanVisitFrom(inField: false, dead: false, dungeonField: false, weddingHall: false), Is.False);
        Assert.That(DivorceRules.CanVisitFrom(inField: true, dead: true, dungeonField: false, weddingHall: false), Is.False);
        Assert.That(DivorceRules.CanVisitFrom(inField: true, dead: false, dungeonField: true, weddingHall: false), Is.False);
        Assert.That(DivorceRules.CanVisitFrom(inField: true, dead: false, dungeonField: false, weddingHall: true), Is.False);
    }

    [Test]
    public void StateChange_IsStoredOnlyByTheRequestThatChangedTheRow() {
        // Two filings, or two ends of one cooling off, at the same time: the update that tests the stored
        // state changes the row for the first and no row for the second.
        Assert.That(DivorceRules.Stored(1), Is.True);
        Assert.That(DivorceRules.Stored(0), Is.False);
    }

    [Test]
    public void Fee_IsBackWhenTheFilingIsNotStored() {
        long held = 3 * Fee;
        Assert.That(DivorceRules.MesoAfterFiling(held - Fee, Fee, stored: true), Is.EqualTo(held - Fee));
        Assert.That(DivorceRules.MesoAfterFiling(held - Fee, Fee, stored: false), Is.EqualTo(held));
    }

    [Test]
    public void MutualAsk_ExpiresAndNeedsTheAskerInTheField() {
        const long tick = 5000;
        long expire = tick + DivorceRules.AskTimeoutMs;
        Assert.That(DivorceRules.AskOpen(First, expire, tick), Is.True);
        Assert.That(DivorceRules.AskOpen(0, expire, tick), Is.False);
        Assert.That(DivorceRules.AskOpen(First, expire, expire), Is.False, "time over");

        Assert.That(DivorceRules.CanAnswerAsk(First, expire, tick + 1, First, askerInField: true), Is.True);
        Assert.That(DivorceRules.CanAnswerAsk(First, expire, expire, First, askerInField: true), Is.False, "time over");
        Assert.That(DivorceRules.CanAnswerAsk(First, expire, tick + 1, First, askerInField: false), Is.False, "asker left");
        Assert.That(DivorceRules.CanAnswerAsk(First, expire, tick + 1, Second, askerInField: true), Is.False, "not the partner");
        Assert.That(DivorceRules.CanAnswerAsk(0, expire, tick + 1, First, askerInField: true), Is.False, "nothing asked");
    }

    [Test]
    public void DivorceRequestPacket_IsTheModeAlone() {
        ByteWriter packet = WeddingPacket.DivorceRequest();
        var reader = new ByteReader(packet.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Wedding));
        Assert.That(reader.ReadByte(), Is.EqualTo(8));
        Assert.That(reader.Position, Is.EqualTo(packet.Length));
    }
}
