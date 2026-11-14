using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The cooldown of a cash call item's skill (KR's bank passes: 30 s, cooldown group 100000017). The client's CanUseItem
// (@0x140d1b610) refuses the item while its skill or the skill's group is cooling, and the cash call casts nothing to
// the server: a use inside the cooldown gets no window, and a use carried out starts it and sends it. The pass ids
// are KR's; the skill rows are built here.
public class CashCallCooldownTests {
    private const int Group = 100000017;

    private TestGameSession test = null!;
    private ItemUseHandler handler = null!;

    private static SkillMetadata BankSkill(int id) {
        SkillMetadata skill = QuestConditionWiringTests.CastSkill(id);
        return skill with {
            State = skill.State with { CooldownGroupId = Group },
            Data = skill.Data with { Condition = skill.Data.Condition with { CooldownTime = 30 } },
        };
    }

    private Item Pass(int itemId, int skillId) {
        ItemMetadata metadata = TestGameSession.Metadata(itemId,
            function: new ItemMetadataFunction(ItemFunction.OpenBank, "OpenBank", string.Empty, false), type: 10, slotMax: 1);
        return test.Store(metadata with { Skill = new ItemMetadataSkill(skillId, 1, 0, 0) });
    }

    private (GameSession, Item, Item) Start() {
        test = new TestGameSession();
        Item first = Pass(20300087, 100000017);
        Item second = Pass(20300088, 100000018);
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000023) with { CashCall = new MapMetadataCashCall(true, true, true, true, true) });
        QuestConditionWiringTests.SetSkills(test, BankSkill(100000017), BankSkill(100000018));
        test.TakeSent();
        return (session, first, second);
    }

    private List<byte[]> Use(GameSession session, Item pass) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(pass.Uid);
            writer.WriteUnicodeString("0");
        }));
        return test.TakeSent();
    }

    private static bool Opened(List<byte[]> sent) => sent.Any(p => TestGameSession.Op(p) == SendOp.StorageInventory);

    [Test]
    public void ASecondUseInsideTheCooldown_OpensNothing() {
        (GameSession session, Item first, _) = Start();

        List<byte[]> sent = Use(session, first);
        Assert.Multiple(() => {
            Assert.That(Opened(sent), Is.True);
            Assert.That(sent.Count(p => TestGameSession.Op(p) == SendOp.SkillCooldown), Is.EqualTo(1));
        });
        Assert.That(Use(session, first), Is.Empty);
    }

    // Another pass whose skill is in the same cooldown group is refused too.
    [Test]
    public void ThePassesShareTheirGroup() {
        (GameSession session, Item first, Item second) = Start();

        Assert.That(Opened(Use(session, first)), Is.True);
        Assert.That(Use(session, second), Is.Empty);
    }

    [Test]
    public void AfterTheCooldown_ItOpensAgain() {
        (GameSession session, Item first, _) = Start();

        Assert.That(Opened(Use(session, first)), Is.True);
        session.Config.SetSkillCooldown(100000017, 1, 0);
        test.TakeSent();
        Assert.That(Opened(Use(session, first)), Is.True);
    }
}
