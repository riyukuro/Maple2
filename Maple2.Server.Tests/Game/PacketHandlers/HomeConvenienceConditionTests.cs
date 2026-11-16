using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The quest conditions of the home's bank and doctor, from real KR rows, through the handlers.
public class HomeConvenienceConditionTests {
    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
    }

    private Quest StartQuest(params ConditionMetadata[] conditions) {
        return test.StartQuest(TestGameSession.QuestData(91000001) with { Conditions = conditions });
    }

    // 74000003: <condition type="home_bank" value="1" />: the bank called from the own home (kind 1) counts; the
    // inventory's bank button (kind 2, here with premium) opens the bank but does not count.
    [Test]
    public void HomeBank_74000003_TheBankOfTheOwnHome() {
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        Quest quest = StartQuest(new ConditionMetadata(ConditionType.home_bank, 1, null, null));
        var handler = new HomeBankHandler { ServerTableMetadata = test.ServerTableMetadata };

        session.Player.Value.Account.PremiumTime = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400;
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(2)));
        Assert.That(test.TakeSentOps(), Does.Contain(SendOp.HomeBank));
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        session.Player.Value.Character.StorageCooldown = 0;
        test.EnterHome();
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(1)));

        Assert.That(test.Errors, Is.Empty);
        Assert.That(test.TakeSentOps(), Does.Contain(SendOp.HomeBank));
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }

    // 23500017: <condition type="home_doctor" value="1" /> and 23100017: <condition type="resolve_panelty" value="1" />:
    // the doctor of the own home cures a running death penalty and counts both; outside the home it does nothing.
    [Test]
    public void HomeDoctor_23500017_23100017_TheCureOfAPenalty() {
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        session.Currency.Meso = 1_000_000;
        Quest quest = StartQuest(
            new ConditionMetadata(ConditionType.home_doctor, 1, null, null),
            new ConditionMetadata(ConditionType.resolve_panelty, 1, null, null));
        var handler = new HomeDoctorHandler { ServerTableMetadata = test.ServerTableMetadata };
        typeof(Maple2.Server.Game.Manager.ConfigManager).GetField("session",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(session.Config, session);
        session.Player.Value.Character.DeathCount = 2;
        session.Player.Value.Character.DeathTick = session.Field!.FieldTick + 600000;

        handler.Handle(session, TestGameSession.Packet(_ => { }));
        Assert.That(quest.Conditions.Values.Select(condition => condition.Counter), Is.EqualTo(new long[] { 0, 0 }));
        test.EnterHome();
        session.Player.Value.Character.DeathTick = session.Field!.FieldTick + 600000;
        handler.Handle(session, TestGameSession.Packet(_ => { }));

        Assert.That(test.Errors, Is.Empty);
        Assert.That(session.Config.DeathPenaltyEndTick, Is.EqualTo(0));
        Assert.That(quest.Conditions.Values.Select(condition => condition.Counter), Is.EqualTo(new long[] { 1, 1 }));
    }
}
