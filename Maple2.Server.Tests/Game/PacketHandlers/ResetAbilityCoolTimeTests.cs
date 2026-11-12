using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The Rizeta Stone (item function ResetAbilityCoolTime): the client's confirm box sends the plain item use; one
// stone clears the character ability reset cool time and the load packet carries reset time 0. The quest and item
// ids are made up.
public class ResetAbilityCoolTimeTests {
    private const int OpenQuest = 90000300;
    private const int CoolMinutes = 60;
    private const int Stone = 40190028;
    private const int Strength = 90000311;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private ItemUseHandler handler = null!;
    private Item stone = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            CharacterAbilityOpenQuestID = OpenQuest,
            CharacterAbilityDefaultPoint = 1,
            CharacterAbilityResetCoolTimeMinute = CoolMinutes,
        });
        test.SetTable(new CharacterAbilityTable(new Dictionary<int, CharacterAbilityTable.Entry> {
            [Strength] = new(Strength, 1, 50, 0, 0),
        }));
        stone = test.Store(TestGameSession.Metadata(Stone,
            function: new ItemMetadataFunction(ItemFunction.ResetAbilityCoolTime, "ResetAbilityCoolTime", "", false)), amount: 2);
        handler = new ItemUseHandler { GameStorage = test.Storage, TableMetadata = test.TableMetadata, BanWordStorage = null! };
        test.Start();
        typeof(ConfigManager).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Config, session);
        Character.Level = 55;
        Character.Abilities.Add(Strength);
        test.SetQuest(OpenQuest, QuestState.Completed);
        test.TakeSent();
    }

    private Character Character => session.Player.Value.Character;

    private int Stones => session.Item.Inventory.Get(stone.Uid)?.Amount ?? 0;

    private void Use() => handler.Handle(session, TestGameSession.Packet(writer => {
        writer.WriteLong(stone.Uid);
        writer.WriteUnicodeString("");
    }));

    // A reset 10 minutes ago: the stone is used, the reset time goes to 0 and the load packet keeps the list.
    [Test]
    public void Use_ClearsTheCoolTime() {
        Character.AbilityResetTime = DateTime.Now.ToEpochSeconds() - 600;

        Use();

        Assert.That(Character.AbilityResetTime, Is.EqualTo(0));
        Assert.That(Character.Abilities, Is.EqualTo(new[] { Strength }));
        Assert.That(Stones, Is.EqualTo(1));
        List<byte[]> sent = test.TakeSent();
        byte[] load = sent.Single(packet => TestGameSession.Op(packet) == SendOp.CharacterAbility);
        var reader = new ByteReader(load, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.CharacterAbility));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(Strength));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(test.Errors, Is.Empty);
    }

    // Never reset, or the cool time over: nothing to clear, the stone is kept.
    [TestCase(0L)]
    [TestCase(CoolMinutes * 60L + 1)]
    public void Use_KeptWithoutACoolTime(long ago) {
        Character.AbilityResetTime = ago == 0 ? 0 : DateTime.Now.ToEpochSeconds() - ago;
        long before = Character.AbilityResetTime;

        Use();

        Assert.That(Character.AbilityResetTime, Is.EqualTo(before));
        Assert.That(Stones, Is.EqualTo(2));
        Assert.That(test.TakeSentOps(), Does.Not.Contain(SendOp.CharacterAbility));
    }

    // The window is not open (below level 50 or the open quest not done), as the client's message 0xc63.
    [TestCase(49, QuestState.Completed)]
    [TestCase(55, QuestState.Started)]
    public void Use_RefusedWhileTheWindowIsClosed(int level, QuestState quest) {
        Character.Level = (short) level;
        test.SetQuest(OpenQuest, quest);
        long last = DateTime.Now.ToEpochSeconds() - 600;
        Character.AbilityResetTime = last;
        test.TakeSent();

        Use();

        Assert.That(Character.AbilityResetTime, Is.EqualTo(last));
        Assert.That(Stones, Is.EqualTo(2));
        Assert.That(test.TakeSentOps(), Does.Not.Contain(SendOp.CharacterAbility));
    }

    [TestCase(true, 1L, true)]
    [TestCase(true, 0L, false)]
    [TestCase(false, 1L, false)]
    public void Rules(bool open, long wait, bool expected) {
        Assert.That(CharacterAbilityRules.CanResetCoolTime(open, wait), Is.EqualTo(expected));
    }
}
