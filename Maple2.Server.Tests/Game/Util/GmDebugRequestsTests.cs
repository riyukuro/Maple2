using System;
using System.Collections.Generic;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using System.Runtime.CompilerServices;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// SetDebugMode mode 4 bodies as the client's NPC control window writes them (after the mode byte).
public class GmDebugRequestsTests {
    private static ByteReader Reader(string hex) {
        return new ByteReader(Convert.FromHexString(hex.Replace(" ", "")), 0);
    }

    [Test]
    public void NpcControl_SelectAndStopSkillHaveNoFields() {
        foreach (string command in new[] { "00", "09" }) {
            ByteReader reader = Reader("2A 00 00 00 " + command);

            GmNpcControlRequest request = GmNpcControlRequest.Read(reader);

            Assert.That(request.ObjectId, Is.EqualTo(42));
            Assert.That(reader.Available, Is.EqualTo(0));
        }
    }

    [Test]
    public void NpcControl_HealthPercentIsOneLong() {
        ByteReader reader = Reader("2A 00 00 00 01 32 00 00 00 00 00 00 00");

        GmNpcControlRequest request = GmNpcControlRequest.Read(reader);

        Assert.That(request.Command, Is.EqualTo(GmNpcControlRequest.HealthPercent));
        Assert.That(request.Value, Is.EqualTo(50));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void NpcControl_UseSkillList() {
        ByteReader reader = Reader(
            "2A 00 00 00 08 " +
            "01 00 02 00 " + // loop, 2 skills
            "01 00 00 00 01 00 00 00 " +
            "02 00 00 00 03 00 00 00");

        GmNpcControlRequest request = GmNpcControlRequest.Read(reader);

        Assert.That(request.SkillCount, Is.EqualTo(2));
        Assert.That((request.SkillId, request.SkillLevel), Is.EqualTo((1, 1)));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void NpcControl_AddAndRemoveEffect() {
        ByteReader add = Reader("2A 00 00 00 0E 65 66 A0 00 03 00 00 00 00 00 00 00");
        ByteReader remove = Reader("2A 00 00 00 0F 65 66 A0 00 00 00 00 00");

        GmNpcControlRequest added = GmNpcControlRequest.Read(add);
        GmNpcControlRequest removed = GmNpcControlRequest.Read(remove);

        Assert.That((added.EffectId, added.EffectLevel), Is.EqualTo((10511973, 3)));
        Assert.That(removed.EffectId, Is.EqualTo(10511973));
        Assert.That(add.Available, Is.EqualTo(0));
        Assert.That(remove.Available, Is.EqualTo(0));
    }

    // A sender that is not a GM (here: a session without a player) is dropped before the mode byte is read.
    [Test]
    public void SetDebugMode_FromANonGmReadsNothing() {
        var session = (GameSession) RuntimeHelpers.GetUninitializedObject(typeof(GameSession));
        GC.SuppressFinalize(session); // never constructed, so its finalizer has nothing to release
        ByteReader reader = Reader("04 2A 00 00 00 01 32 00 00 00 00 00 00 00");

        new SetDebugModeHandler().Handle(session, reader);

        Assert.That(reader.Available, Is.EqualTo(14));
    }

    [Test]
    public void RecallUser_DeadOrInADungeonIsNotMoved() {
        Assert.That(UserRecallUtil.CanBeMoved(false, false), Is.True);
        Assert.That(UserRecallUtil.CanBeMoved(true, false), Is.False);
        Assert.That(UserRecallUtil.CanBeMoved(false, true), Is.False);
    }

    [Test]
    public void HealthTarget_StaysBetweenOneAndTheTotal() {
        Assert.That(GmNpcControlRequest.HealthTarget(GmNpcControlRequest.HealthPercent, 50, 1000), Is.EqualTo(500));
        Assert.That(GmNpcControlRequest.HealthTarget(GmNpcControlRequest.HealthPercent, 1, 10), Is.EqualTo(1));
        Assert.That(GmNpcControlRequest.HealthTarget(GmNpcControlRequest.HealthPercent, 0, 1000), Is.EqualTo(-1));
        Assert.That(GmNpcControlRequest.HealthTarget(GmNpcControlRequest.HealthPercent, 101, 1000), Is.EqualTo(-1));
        Assert.That(GmNpcControlRequest.HealthTarget(GmNpcControlRequest.HealthValue, 5000, 1000), Is.EqualTo(1000));
        Assert.That(GmNpcControlRequest.HealthTarget(GmNpcControlRequest.HealthValue, 0, 1000), Is.EqualTo(-1));
        Assert.That(GmNpcControlRequest.HealthTarget(GmNpcControlRequest.StopSkill, 5, 1000), Is.EqualTo(-1));
    }

    // A session with a real player whose account holds these permissions. Nothing else of it is set up.
    private static GameSession SessionOf(AdminPermissions permissions) {
        var session = (GameSession) RuntimeHelpers.GetUninitializedObject(typeof(GameSession));
        GC.SuppressFinalize(session); // never constructed, so its finalizer has nothing to release
        var player = (FieldPlayer) RuntimeHelpers.GetUninitializedObject(typeof(FieldPlayer));
        GC.SuppressFinalize(player);
        var value = new Player(new Account { Username = "a", AdminPermissions = permissions }, new Character { Name = "A", Mastery = new Mastery() }, 1) {
            Currency = new Currency(),
            Unlock = new Unlock(),
            Home = (Home) RuntimeHelpers.GetUninitializedObject(typeof(Home)),
        };
        typeof(Actor<Player>).GetField("<Value>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, value);
        typeof(GameSession).GetProperty(nameof(GameSession.Player))!.SetValue(session, player);
        return session;
    }

    // The AdminPermissions check itself: a player without the GameMaster flag, also one that holds finer flags.
    [Test]
    public void SetDebugMode_FromAPlayerWithoutGmReadsNothing() {
        foreach (AdminPermissions permissions in new[] { AdminPermissions.None, AdminPermissions.Warp | AdminPermissions.Debug | AdminPermissions.SpawnNpc }) {
            GameSession session = SessionOf(permissions);
            Assert.That(session.Player.AdminPermissions, Is.EqualTo(permissions));
            ByteReader reader = Reader("00 01");

            new SetDebugModeHandler().Handle(session, reader);

            Assert.That(reader.Available, Is.EqualTo(2), permissions.ToString());
        }
    }

    // The same packet from a GameMaster is read (mode 0 is only logged).
    [Test]
    public void SetDebugMode_FromAGmIsRead() {
        ByteReader reader = Reader("00 01");

        new SetDebugModeHandler().Handle(SessionOf(AdminPermissions.GameMaster), reader);

        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // recalluser is offered by the same rule the command router registers with.
    [Test]
    public void RecallUser_IsNotOfferedToAPlayerWithoutGm() {
        var command = new RecallUserCommand(null!);

        Assert.That(CommandRouter.Allowed(SessionOf(AdminPermissions.None).Player.AdminPermissions, command), Is.False);
        Assert.That(CommandRouter.Allowed(SessionOf(AdminPermissions.Warp | AdminPermissions.Debug).Player.AdminPermissions, command), Is.False);
        Assert.That(CommandRouter.Allowed(null, command), Is.False);
        Assert.That(CommandRouter.Allowed(SessionOf(AdminPermissions.GameMaster).Player.AdminPermissions, command), Is.True);
        Assert.That(CommandRouter.Allowed(SessionOf(AdminPermissions.Admin).Player.AdminPermissions, command), Is.True);
    }

    [Test]
    public void RecallUser_OpenInviteIsReportedWhenTheSessionEnds() {
        var pending = new PendingUserRecall(3, 9, "A", 2000001, 100);

        Assert.That(PendingUserRecall.IsOpen(pending, 100), Is.True);
        Assert.That(PendingUserRecall.IsOpen(pending, 101), Is.False);
        Assert.That(PendingUserRecall.IsOpen(null, 0), Is.False);
    }

    // UIDebugGuideDialog::SendCommand (uidebugguidedialog.cpp:446): int count, count x (command, argument).
    [Test]
    public void Guide_ReadsTheLines() {
        var writer = new ByteWriter();
        writer.WriteInt(2);
        writer.WriteUnicodeString("/a");
        writer.WriteUnicodeString("");
        writer.WriteUnicodeString("/b");
        writer.WriteUnicodeString("12");

        List<(string Command, string Argument)>? lines = GmGuideRequest.Read(new ByteReader(writer.ToArray(), 0));

        Assert.That(lines, Is.EqualTo(new[] { ("/a", ""), ("/b", "12") }));
    }

    [Test]
    public void Guide_RefusesACountOrTextOutsideTheWindow() {
        foreach (int count in new[] { 0, -1, GmGuideRequest.MaxLines + 1 }) {
            var writer = new ByteWriter();
            writer.WriteInt(count);
            Assert.That(GmGuideRequest.Read(new ByteReader(writer.ToArray(), 0)), Is.Null);
        }

        var longLine = new ByteWriter();
        longLine.WriteInt(1);
        longLine.WriteUnicodeString(new string('a', GmGuideRequest.MaxLength + 1));
        longLine.WriteUnicodeString("");
        Assert.That(GmGuideRequest.Read(new ByteReader(longLine.ToArray(), 0)), Is.Null);
    }

    // The three kinds of 0x87 the server sends, as the client reads them (uinpccontroldialog.cpp:165,
    // uidebugguidedialog.cpp:296).
    [Test]
    public void DebugModePacket_Layouts() {
        Assert.That(DebugModePacket.NpcSkills([(10, 2)]).ToArray(), Is.EqualTo(new byte[] { 0x87, 0, 6, 1, 0, 0, 0, 10, 0, 0, 0, 2, 0, 0, 0 }));

        var reader = new ByteReader(DebugModePacket.NpcState(7, "Walk", "Ai", 5, false, [("k", 3)], 1, 2, 3, 4, 5, 6).ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.DebugMode));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Walk"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Ai"));
        Assert.That(reader.ReadLong(), Is.EqualTo(5));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("k"));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        for (long stat = 1; stat <= 6; stat++) {
            Assert.That(reader.ReadLong(), Is.EqualTo(stat));
        }
        Assert.That(reader.Available, Is.EqualTo(0));

        Assert.That(DebugModePacket.GuideCommands("A", [("/a", "")]).ToArray(),
            Is.EqualTo(new byte[] { 0x87, 0, 7, 1, 0, (byte) 'A', 0, 1, 0, 0, 0, 2, 0, (byte) '/', 0, (byte) 'a', 0, 0, 0 }));
    }
}
