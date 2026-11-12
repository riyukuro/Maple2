using System;
using System.Linq;
using System.Text.Json;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

// Microgame 0x0114 (couple dance) and the CoupleDance game event. Synthetic XML, made-up ids and item ids.
public class CoupleDanceRulesTests {
    private const string Rewards = """
        <rewards>
            <daily order="2"><items><v itemID="20000002" grade="2" count="3" /></items></daily>
            <daily order="1"><items><v itemID="20000001" /><v itemID="0" /></items></daily>
            <daily order="2"><items><v itemID="20000009" /></items></daily>
            <goodEndBonus count="3"><items><v itemID="20000003" grade="3" count="1" /></items></goodEndBonus>
        </rewards>
        """;

    private static string GameData(string dances = "<a anim=\"d1\" /><b anim=\"d2\" />", string maxRound = "5", string startStep = "<action anim=\"s\" />") => $"""
        <gameData>
            <dances>{dances}</dances>
            <start fieldID="9000001">{startStep}</start>
            <play maxRound="{maxRound}">
                <ready><message value="m1" /></ready>
                <select><action anim="a2" /></select>
                <waitResult><action anim="a3" /><message value="m3" /></waitResult>
                <result><success><action anim="a4" /></success><fail><action anim="a5" /></fail></result>
            </play>
            <end><normal><message value="m6" /></normal><good><message value="m7" /></good></end>
        </gameData>
        """;

    [Test]
    public void Read_RewardsAndDance() {
        CoupleDance? dance = ServerTableMapper.ReadCoupleDance(Rewards, GameData());
        Assert.That(dance, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(dance!.DailyRewards, Has.Length.EqualTo(2));
            Assert.That(dance.DailyRewards[0], Is.EqualTo(new[] { new RewardItem(20000001, 1, 1) }));     // by order, defaults
            Assert.That(dance.DailyRewards[1], Is.EqualTo(new[] { new RewardItem(20000002, 2, 3) }));     // the first of order 2
            Assert.That(dance.GoodEndCount, Is.EqualTo(3));
            Assert.That(dance.GoodEndRewards, Is.EqualTo(new[] { new RewardItem(20000003, 3, 1) }));
            Assert.That(dance.Dances, Is.EqualTo(new[] { "d1", "d2" }));
            Assert.That(dance.MaxRound, Is.EqualTo(5));
            Assert.That(dance.FieldId, Is.EqualTo(9000001));
            Assert.That(dance.Steps.Select(step => step.Action), Is.EqualTo(new[] { "s", "", "a2", "a3", "a4", "a5", "", "" }));
            Assert.That(dance.Steps.Select(step => step.Message), Is.EqualTo(new[] { "", "m1", "", "m3", "", "", "m6", "m7" }));
        });
    }

    [Test]
    public void Read_FailsAsTheClient() {
        Assert.Multiple(() => {
            Assert.That(ServerTableMapper.ReadCoupleDance("", GameData()), Is.Null);
            Assert.That(ServerTableMapper.ReadCoupleDance(Rewards, "<x/>"), Is.Null);
            Assert.That(ServerTableMapper.ReadCoupleDance(Rewards.Replace("<goodEndBonus count=\"3\"><items><v itemID=\"20000003\" grade=\"3\" count=\"1\" /></items></goodEndBonus>", ""), GameData()), Is.Null);
            Assert.That(ServerTableMapper.ReadCoupleDance(Rewards.Replace("<daily order=\"1\"><items><v itemID=\"20000001\" /><v itemID=\"0\" /></items></daily>", "<daily order=\"1\" />"), GameData()), Is.Null);
            Assert.That(ServerTableMapper.ReadCoupleDance(Rewards, GameData(dances: "")), Is.Null);
            Assert.That(ServerTableMapper.ReadCoupleDance(Rewards, GameData(maxRound: "0")), Is.Null);
            Assert.That(ServerTableMapper.ReadCoupleDance(Rewards, GameData(startStep: "")), Is.Null); // a step without action and message
        });
    }

    [Test]
    public void Metadata_JsonRoundTrip() {
        GameEventData data = ServerTableMapper.ReadCoupleDance(Rewards, GameData())!;
        var back = (CoupleDance) JsonSerializer.Deserialize<GameEventData>(JsonSerializer.Serialize(data))!;
        Assert.Multiple(() => {
            Assert.That(back.DailyRewards[1], Is.EqualTo(new[] { new RewardItem(20000002, 2, 3) }));
            Assert.That(back.Dances, Is.EqualTo(new[] { "d1", "d2" }));
            Assert.That(back.Steps[3], Is.EqualTo(new CoupleDance.Step("a3", "m3")));
        });
    }

    // GameEvent::vfunc_4 then CoupleDanceContext::Read (gameeventcontext.cpp).
    [Test]
    public void GameEvent_WritesTheClientLayout() {
        CoupleDance dance = ServerTableMapper.ReadCoupleDance(Rewards, GameData())!;
        var metadata = new GameEventMetadata(900002, GameEventType.CoupleDance, DateTime.UnixEpoch.AddSeconds(100), DateTime.UnixEpoch.AddSeconds(1000),
            TimeSpan.Zero, TimeSpan.Zero, [], dance, "", "", "", "");
        var writer = new ByteWriter();
        new GameEvent(metadata).WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("CoupleDance"));
        Assert.That(reader.ReadInt(), Is.EqualTo(900002));
        Assert.That(reader.ReadInt(), Is.EqualTo(900002));
        Assert.That(reader.ReadLong(), Is.EqualTo(100));
        Assert.That(reader.ReadLong(), Is.EqualTo(1000));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));                // daily rewards
        Assert.That(reader.ReadInt(), Is.EqualTo(1));                // items of the first
        Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
        Assert.That(reader.ReadShort(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        reader.Skip(4);
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000002));
        reader.Skip(10);
        Assert.That(reader.ReadShort(), Is.EqualTo(3));              // goodEndCount
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000003));
        reader.Skip(10);
        Assert.That(reader.ReadByte(), Is.EqualTo(5));               // maxRound
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("d1"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("d2"));
        Assert.That(reader.ReadInt(), Is.EqualTo(8));
        for (int i = 0; i < 8; i++) {
            reader.ReadUnicodeString();
            reader.ReadUnicodeString();
        }
        Assert.That(reader.ReadInt(), Is.EqualTo(9000001));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    [Test]
    public void GoodRoundAndGoodEnd() {
        Assert.Multiple(() => {
            Assert.That(CoupleDanceRules.IsGood(2, 2), Is.True);
            Assert.That(CoupleDanceRules.IsGood(2, 1), Is.False);
            Assert.That(CoupleDanceRules.IsGoodEnd(3, 3), Is.True);
            Assert.That(CoupleDanceRules.IsGoodEnd(2, 3), Is.False);
        });
    }

    [Test]
    public void PickStages_OnePerRoundInRange() {
        short[] stages = CoupleDanceRules.PickStages(3, 40, new Random(7));
        Assert.That(stages, Has.Length.EqualTo(40));
        Assert.That(stages, Is.All.InRange(0, 2));
        Assert.That(CoupleDanceRules.PickStages(3, 0, new Random(7)), Is.Empty);
    }

    [Test]
    public void States_CommaListPadded() {
        Assert.That(CoupleDanceRules.ParseStates(null, 3), Is.EqualTo(new[] { 0, 0, 0 }));
        Assert.That(CoupleDanceRules.ParseStates("1, x,4,2", 3), Is.EqualTo(new[] { 1, 0, 4 }));
        Assert.That(CoupleDanceRules.FormatStates([1, 0, 4]), Is.EqualTo("1,0,4"));
    }

    [Test]
    public void Complete_TakesTheFirstOpenSlot() {
        Assert.That(CoupleDanceRules.Complete("", 3, false), Is.EqualTo("1,0,0"));
        Assert.That(CoupleDanceRules.Complete("3,0,0", 3, true), Is.EqualTo("3,2,0"));
        Assert.That(CoupleDanceRules.Complete("1,2,4", 3, true), Is.Null);
        Assert.That(CoupleDanceRules.Complete("", 0, true), Is.Null);
    }

    [Test]
    public void Claim_CompletedStepsOnce() {
        Assert.That(CoupleDanceRules.Claim("1,2,0", 3, 0), Is.EqualTo(("3,2,0", false)));
        Assert.That(CoupleDanceRules.Claim("1,2,0", 3, 1), Is.EqualTo(("1,4,0", true)));
        Assert.That(CoupleDanceRules.Claim("3,4,0", 3, 0), Is.Null);
        Assert.That(CoupleDanceRules.Claim("3,4,0", 3, 2), Is.Null);
        Assert.That(CoupleDanceRules.Claim("1,1,1", 3, 3), Is.Null);
        Assert.That(CoupleDanceRules.Claim("1,1,1", 3, -1), Is.Null);
    }

    [Test]
    public void Table_InviteStartAnswer() {
        var table = new CoupleDanceMatchTable();
        Assert.That(table.Invite(1, 1), Is.False);
        Assert.That(table.Invite(1, 2), Is.True);
        Assert.That(table.Invite(3, 2), Is.False);
        Assert.That(table.IsPending(1, 2), Is.True);
        Assert.That(table.Answer(1, 0, 0, 3, out _), Is.False); // not started
        Assert.That(table.Start(1, 2, []), Is.False);
        Assert.That(table.Start(2, 1, [0, 1]), Is.False);
        Assert.That(table.Start(1, 2, [0, 1]), Is.True);
        Assert.That(table.Start(1, 2, [0, 1]), Is.False);
        Assert.That(table.GamePartner(2), Is.EqualTo(1));
        Assert.That(table.RemovePending(1, 2), Is.False);

        Assert.That(table.Answer(1, 0, 3, 3, out _), Is.False); // no such dance
        Assert.That(table.Answer(1, 1, 0, 3, out _), Is.False); // another round
        Assert.That(table.Answer(1, 0, 2, 3, out CoupleDanceMatchTable.RoundResult? none), Is.True);
        Assert.That(none, Is.Null);
        Assert.That(table.Answer(1, 0, 1, 3, out _), Is.False); // one answer each
        Assert.That(table.Answer(2, 0, 2, 3, out CoupleDanceMatchTable.RoundResult? first), Is.True);
        Assert.That(first, Is.EqualTo(new CoupleDanceMatchTable.RoundResult(1, 2, 2, 2, 0, true, 1, false)));

        Assert.That(table.Answer(2, 1, 0, 3, out _), Is.True);
        Assert.That(table.Answer(1, 1, 1, 3, out CoupleDanceMatchTable.RoundResult? last), Is.True);
        Assert.That(last, Is.EqualTo(new CoupleDanceMatchTable.RoundResult(1, 1, 2, 0, 1, false, 1, true)));
        Assert.That(table.IsBusy(1) || table.IsBusy(2), Is.False);
    }

    [Test]
    public void Table_DeclineAndLeave() {
        var table = new CoupleDanceMatchTable();
        table.Invite(1, 2);
        Assert.That(table.RemovePending(2, 1), Is.False);
        Assert.That(table.RemovePending(1, 2), Is.True);
        Assert.That(table.IsBusy(2), Is.False);

        table.Invite(1, 2);
        table.Start(1, 2, [0]);
        Assert.That(table.Remove(3), Is.EqualTo(0));
        Assert.That(table.Remove(1), Is.EqualTo(2));
        Assert.That(table.IsBusy(2), Is.False);
    }

    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    // MicroGameManagerCli: a short mode, the event id, then the mode's fields.
    [Test]
    public void Packets_Layout() {
        Assert.That(Body(MicrogamePacket.CoupleDanceBanner(0x01020304)), Is.EqualTo(new byte[] { 0xD, 0, 4, 3, 2, 1 }));

        byte[] invite = Body(MicrogamePacket.CoupleDanceInvite(5, 0x0102030405060708));
        Assert.That(BitConverter.ToInt16(invite, 0), Is.EqualTo(0xE));
        Assert.That(BitConverter.ToInt32(invite, 2), Is.EqualTo(5));
        Assert.That(BitConverter.ToInt64(invite, 6), Is.EqualTo(0x0102030405060708));
        Assert.That(invite, Has.Length.EqualTo(14));
        Assert.That(BitConverter.ToInt16(Body(MicrogamePacket.CoupleDanceAccepted(5, 7)), 0), Is.EqualTo(0xF));
        Assert.That(BitConverter.ToInt16(Body(MicrogamePacket.CoupleDanceInviteSent(5, 7)), 0), Is.EqualTo(0x10));
        Assert.That(BitConverter.ToInt16(Body(MicrogamePacket.CoupleDanceDeclined(5, 7)), 0), Is.EqualTo(0x11));
        Assert.That(BitConverter.ToInt16(Body(MicrogamePacket.CoupleDanceCancelled(5, 7)), 0), Is.EqualTo(0x13));

        Assert.That(Body(MicrogamePacket.CoupleDanceStart(5, [2, 0, 1])), Is.EqualTo(new byte[] { 0x12, 0, 5, 0, 0, 0, 2, 0, 0, 0, 1, 0 }));

        byte[] failed = Body(MicrogamePacket.CoupleDanceFailed(5, 9, CoupleDanceRules.ReasonFailed));
        Assert.That(BitConverter.ToInt16(failed, 0), Is.EqualTo(0x14));
        Assert.That(BitConverter.ToInt64(failed, 6), Is.EqualTo(9));
        Assert.That(failed[14], Is.EqualTo(4));
        Assert.That(failed, Has.Length.EqualTo(15));

        Assert.That(Body(MicrogamePacket.CoupleDanceResult(5, 3, true, 2, 1)), Is.EqualTo(new byte[] { 0x16, 0, 5, 0, 0, 0, 3, 1, 2, 0, 1, 0 }));
    }
}
