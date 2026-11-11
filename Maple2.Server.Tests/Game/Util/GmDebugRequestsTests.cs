using System;
using System.Collections.Concurrent;
using Maple2.Server.Game.Manager.Field;
using System.Collections.Generic;
using System.Linq;
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

    // Gives a bare session what Send needs (the two collections, and the logger the Debug build's LogSend
    // writes to) and returns the queue the packets land in.
    private static BlockingCollection<(byte[] packet, int length)> SendQueueOf(GameSession session) {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = typeof(Maple2.Server.Core.Network.Session);
        var queue = new BlockingCollection<(byte[] packet, int length)>();
        type.GetField("sendQueue", flags)!.SetValue(session, queue);
        type.GetField("lastSentPackets", flags)!.SetValue(session, new ConcurrentDictionary<SendOp, byte[]>());
        type.GetField("Logger", flags)!.SetValue(session, Serilog.Log.Logger);
        return queue;
    }

    // The debug guide lines go back to the sender only: another GM in the same field receives nothing.
    [Test]
    public void Guide_IsSentToTheSenderOnly() {
        GameSession sender = SessionOf(AdminPermissions.GameMaster);
        GameSession other = SessionOf(AdminPermissions.GameMaster);
        BlockingCollection<(byte[] packet, int length)> sent = SendQueueOf(sender);
        BlockingCollection<(byte[] packet, int length)> sentToOther = SendQueueOf(other);
        var field = (FieldManager) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager));
        var players = new ConcurrentDictionary<int, FieldPlayer>();
        players[1] = sender.Player;
        players[2] = other.Player;
        typeof(FieldManager).GetField("<Players>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(field, players);
        typeof(FieldPlayer).GetField(nameof(FieldPlayer.Session))!.SetValue(other.Player, other);
        typeof(FieldPlayer).GetField(nameof(FieldPlayer.Session))!.SetValue(sender.Player, sender);
        sender.Field = field;
        other.Field = field;

        var writer = new ByteWriter();
        writer.WriteByte(7);
        writer.WriteInt(1);
        writer.WriteUnicodeString("/a");
        writer.WriteUnicodeString("");
        new SetDebugModeHandler().Handle(sender, new ByteReader(writer.ToArray(), 0));

        Assert.That(sent.Count, Is.EqualTo(1));
        (byte[] packet, int length) = sent.Take();
        Assert.That(packet.AsSpan(0, length).ToArray(), Is.EqualTo(DebugModePacket.GuideCommands("A", [("/a", "")]).ToArray()));
        Assert.That(sentToOther.Count, Is.EqualTo(0));
    }

    // A bare target session on a bare channel server that also holds the caller's session (character id 9).
    private static (GameSession Target, BlockingCollection<(byte[] packet, int length)> SentToCaller) RecallPair() {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        GameSession target = SessionOf(AdminPermissions.None);
        GameSession caller = SessionOf(AdminPermissions.GameMaster);
        BlockingCollection<(byte[] packet, int length)> sentToCaller = SendQueueOf(caller);
        SendQueueOf(target);
        var server = (Maple2.Server.Game.GameServer) RuntimeHelpers.GetUninitializedObject(typeof(Maple2.Server.Game.GameServer));
        var sessions = new ConcurrentDictionary<long, GameSession>();
        sessions[9] = caller;
        typeof(Maple2.Server.Game.GameServer).GetField("sessions", flags)!.SetValue(server, sessions);
        typeof(GameSession).GetField("server", flags)!.SetValue(target, server);
        return (target, sentToCaller);
    }

    private static readonly byte[] NotFoundForA = RecallUserPacket.Fail("A", RecallUserPacket.FAIL_NOT_FOUND).ToArray();

    [Test]
    public void RecallUser_DropPendingTellsTheCallerOnce() {
        (GameSession target, BlockingCollection<(byte[] packet, int length)> sentToCaller) = RecallPair();
        target.UserRecall = new PendingUserRecall(3, 9, "Gm", 2000001, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30);

        UserRecallUtil.DropPending(target);
        UserRecallUtil.DropPending(target);

        Assert.That(target.UserRecall, Is.Null);
        Assert.That(sentToCaller.Count, Is.EqualTo(1));
        (byte[] packet, int length) = sentToCaller.Take();
        Assert.That(packet.AsSpan(0, length).ToArray(), Is.EqualTo(NotFoundForA));
    }

    [Test]
    public void RecallUser_DropPendingIsSilentWithoutAnOpenInvite() {
        (GameSession target, BlockingCollection<(byte[] packet, int length)> sentToCaller) = RecallPair();

        UserRecallUtil.DropPending(target); // no invite
        target.UserRecall = new PendingUserRecall(3, 9, "Gm", 2000001, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1);
        UserRecallUtil.DropPending(target); // run out

        Assert.That(target.UserRecall, Is.Null);
        Assert.That(sentToCaller.Count, Is.EqualTo(0));
    }

    // The hook itself: disposing the session of a player with an open invite tells the caller. The session is
    // bare, so the rest of the cleanup fails inside its own try blocks; only the collections the cleanup walks
    // outside of them are given.
    [Test]
    public void RecallUser_SessionEndTellsTheCaller() {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        (GameSession target, BlockingCollection<(byte[] packet, int length)> sentToCaller) = RecallPair();
        typeof(Maple2.Server.Core.Network.Session).GetField("Logger", flags)!.SetValue(target, Serilog.Log.Logger);
        target.GroupChats = new ConcurrentDictionary<int, Maple2.Server.Game.Manager.GroupChatManager>();
        target.Clubs = new ConcurrentDictionary<long, Maple2.Server.Game.Manager.ClubManager>();
        target.UserRecall = new PendingUserRecall(3, 9, "Gm", 2000001, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30);

        target.Dispose();

        Assert.That(target.UserRecall, Is.Null);
        Assert.That(sentToCaller.Count, Is.EqualTo(1));
        (byte[] packet, int length) = sentToCaller.Take();
        Assert.That(packet.AsSpan(0, length).ToArray(), Is.EqualTo(NotFoundForA));
    }

    // The answer and the end of the session race for the invite: whoever takes it first tells the caller.
    [Test]
    public void RecallUser_AnswerAfterTheSessionEndedTellsNothingMore() {
        (GameSession target, BlockingCollection<(byte[] packet, int length)> sentToCaller) = RecallPair();
        target.UserRecall = new PendingUserRecall(3, 9, "Gm", 2000001, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30);

        UserRecallUtil.DropPending(target);
        var answer = new ByteWriter();
        answer.WriteByte(4);
        answer.WriteInt(3);
        answer.WriteByte(0);
        new RecallUserHandler().Handle(target, new ByteReader(answer.ToArray(), 0));

        Assert.That(sentToCaller.Count, Is.EqualTo(1));
    }

    [Test]
    public void RecallUser_SessionEndAfterTheAnswerTellsNothingMore() {
        (GameSession target, BlockingCollection<(byte[] packet, int length)> sentToCaller) = RecallPair();
        target.UserRecall = new PendingUserRecall(3, 9, "Gm", 2000001, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30);

        var answer = new ByteWriter();
        answer.WriteByte(4);
        answer.WriteInt(3);
        answer.WriteByte(0);
        new RecallUserHandler().Handle(target, new ByteReader(answer.ToArray(), 0));
        UserRecallUtil.DropPending(target);

        Assert.That(target.UserRecall, Is.Null);
        Assert.That(sentToCaller.Count, Is.EqualTo(1));
        (byte[] packet, int length) = sentToCaller.Take();
        Assert.That(packet.AsSpan(0, length).ToArray(), Is.EqualTo(RecallUserPacket.Fail("A", RecallUserPacket.FAIL_REJECT).ToArray()));
    }

    [Test]
    public void RecallUser_TakeAnsweredLeavesAnInviteWithAnotherId() {
        GameSession target = SessionOf(AdminPermissions.None);
        var pending = new PendingUserRecall(3, 9, "Gm", 2000001, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30);
        target.UserRecall = pending;

        Assert.That(UserRecallUtil.TakeAnswered(target, 4), Is.Null);
        Assert.That(target.UserRecall, Is.SameAs(pending));
        Assert.That(UserRecallUtil.TakeAnswered(target, 3), Is.SameAs(pending));
        Assert.That(target.UserRecall, Is.Null);
    }

    // Two threads, one invite: over many rounds exactly one of the two takes it.
    [Test]
    public void RecallUser_AnswerAndSessionEndTakeTheInviteOnce() {
        GameSession target = SessionOf(AdminPermissions.None);
        for (int round = 0; round < 500; round++) {
            target.UserRecall = new PendingUserRecall(3, 9, "Gm", 2000001, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30);
            int taken = 0;
            System.Threading.Tasks.Parallel.Invoke(
                () => { if (UserRecallUtil.TakeAnswered(target, 3) != null) System.Threading.Interlocked.Increment(ref taken); },
                () => { if (UserRecallUtil.TakeOpen(target) != null) System.Threading.Interlocked.Increment(ref taken); });
            Assert.That(taken, Is.EqualTo(1));
        }
    }

    // The NPC control window reads 0x0087 kind 7 as an AI file and opens a window for it, and the server
    // cannot see it close: after an NPC pick the guide lines are not sent back.
    [Test]
    public void Guide_IsNotSentBackAfterAnNpcPick() {
        GameSession sender = SessionOf(AdminPermissions.GameMaster);
        BlockingCollection<(byte[] packet, int length)> sent = SendQueueOf(sender);

        var pick = new ByteWriter();
        pick.WriteByte(4);
        pick.WriteInt(77);
        pick.WriteByte(GmNpcControlRequest.Select);
        new SetDebugModeHandler().Handle(sender, new ByteReader(pick.ToArray(), 0));
        Assert.That(sender.NpcControlPicked, Is.True);

        var writer = new ByteWriter();
        writer.WriteByte(7);
        writer.WriteInt(1);
        writer.WriteUnicodeString("/a");
        writer.WriteUnicodeString("");
        new SetDebugModeHandler().Handle(sender, new ByteReader(writer.ToArray(), 0));

        Assert.That(sent.Count, Is.EqualTo(0));
    }

    [Test]
    public void Guide_OtherNpcControlButtonsDoNotStopTheGuide() {
        GameSession sender = SessionOf(AdminPermissions.GameMaster);
        var stop = new ByteWriter();
        stop.WriteByte(4);
        stop.WriteInt(77);
        stop.WriteByte(GmNpcControlRequest.StopSkill);
        new SetDebugModeHandler().Handle(sender, new ByteReader(stop.ToArray(), 0));
        Assert.That(sender.NpcControlPicked, Is.False);
    }

    [Test]
    public void Guide_LimitsAreInclusive() {
        var full = new ByteWriter();
        full.WriteInt(GmGuideRequest.MaxLines);
        for (int i = 0; i < GmGuideRequest.MaxLines; i++) {
            full.WriteUnicodeString(new string('c', GmGuideRequest.MaxLength));
            full.WriteUnicodeString(new string('a', GmGuideRequest.MaxLength));
        }
        Assert.That(GmGuideRequest.Read(new ByteReader(full.ToArray(), 0))?.Count, Is.EqualTo(GmGuideRequest.MaxLines));

        var longArgument = new ByteWriter();
        longArgument.WriteInt(1);
        longArgument.WriteUnicodeString("/a");
        longArgument.WriteUnicodeString(new string('a', GmGuideRequest.MaxLength + 1));
        Assert.That(GmGuideRequest.Read(new ByteReader(longArgument.ToArray(), 0)), Is.Null);
    }

    private sealed class StubWorld : Maple2.Server.World.Service.World.WorldClient {
        public int Error;
        public int Calls;

        public override global::Maple2.PlayerWarpResponse PlayerWarp(global::Maple2.PlayerWarpRequest request, Grpc.Core.CallOptions options) {
            Calls++;
            return new global::Maple2.PlayerWarpResponse { Error = Error };
        }
    }

    // GoToPlayer says false when nothing was started, so the recall answer can tell the caller "cannot move".
    [Test]
    public void GoToPlayer_IsFalseWithoutAFieldOrWhenTheWorldRefuses() {
        GameSession session = SessionOf(AdminPermissions.None);
        var world = new StubWorld { Error = 1 };
        typeof(GameSession).GetField("<World>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, world);
        var info = (PlayerInfo) RuntimeHelpers.GetUninitializedObject(typeof(PlayerInfo));

        Assert.That(PlayerWarpUtil.GoToPlayer(session, info), Is.False);
        Assert.That(world.Calls, Is.EqualTo(0));

        session.Field = (FieldManager) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager));
        Assert.That(PlayerWarpUtil.GoToPlayer(session, info), Is.False);
        Assert.That(world.Calls, Is.EqualTo(1));
    }

    // A channel change is refused, not thrown, when the World server answers with no usable address.
    [Test]
    public void GoToPlayer_EndpointRefusesABadAddressOrPort() {
        Assert.That(PlayerWarpUtil.TryEndpoint("127.0.0.1", 20001, out System.Net.IPEndPoint? endpoint), Is.True);
        Assert.That(endpoint!.ToString(), Is.EqualTo("127.0.0.1:20001"));
        foreach (string? address in new[] { null, "", "localhost", "1.2.3.4.5", "not an address" }) {
            Assert.That(PlayerWarpUtil.TryEndpoint(address, 20001, out endpoint), Is.False);
            Assert.That(endpoint, Is.Null);
        }
        foreach (int port in new[] { 0, -1, 65536 }) {
            Assert.That(PlayerWarpUtil.TryEndpoint("127.0.0.1", port, out endpoint), Is.False);
        }
    }

    // The AI on, AI off and extra buttons (uinpccontroldialog.cpp:517-534): one long, 0.
    [Test]
    public void NpcControl_AiButtonsAreOneLong() {
        foreach (byte command in new[] { GmNpcControlRequest.AiOn, GmNpcControlRequest.AiOff, GmNpcControlRequest.Extra }) {
            var writer = new ByteWriter();
            writer.WriteInt(77);
            writer.WriteByte(command);
            writer.WriteLong(0);
            var reader = new ByteReader(writer.ToArray(), 0);
            GmNpcControlRequest request = GmNpcControlRequest.Read(reader);
            Assert.That(request.ObjectId, Is.EqualTo(77));
            Assert.That(request.Command, Is.EqualTo(command));
            Assert.That(reader.Available, Is.EqualTo(0));
        }
    }

    [Test]
    public void NpcControl_ExtraListIsSortedAndBounded() {
        var values = new Dictionary<string, int>();
        for (int i = 0; i < GmNpcControlRequest.MaxExtra + 5; i++) {
            values["k" + (99 - i)] = i;
        }
        List<(string Key, int Value)> list = GmNpcControlRequest.ExtraList(values);
        Assert.That(list.Count, Is.EqualTo(GmNpcControlRequest.MaxExtra));
        Assert.That(list[0], Is.EqualTo(("k63", 36)));
        Assert.That(list[1].Key, Is.EqualTo("k64"));
        Assert.That(GmNpcControlRequest.ExtraList(new Dictionary<string, int>()), Is.Empty);
    }

    [Test]
    public void NpcControl_HeadlineMarksASwitchedOffAi() {
        Assert.That(GmNpcControlRequest.Headline("AI_Default.xml", false), Is.EqualTo("AI_Default.xml"));
        Assert.That(GmNpcControlRequest.Headline("AI_Default.xml", true), Is.EqualTo("AI_Default.xml (AI off)"));
        Assert.That(GmNpcControlRequest.Headline(null, true), Is.EqualTo("- (AI off)"));
    }

    // AI off is the handler's own switch on the NPC, and a plain account cannot reach it.
    [Test]
    public void NpcControl_AiOffFromAPlainAccountChangesNothing() {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        GameSession session = SessionOf(AdminPermissions.None);
        var field = (FieldManager) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager));
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        var mobs = new ConcurrentDictionary<int, FieldNpc>();
        mobs[77] = npc;
        typeof(FieldManager).GetField("<Mobs>k__BackingField", flags)!.SetValue(field, mobs);
        session.Field = field;

        var writer = new ByteWriter();
        writer.WriteByte(4);
        writer.WriteInt(77);
        writer.WriteByte(GmNpcControlRequest.AiOff);
        writer.WriteLong(0);
        var reader = new ByteReader(writer.ToArray(), 0);
        new SetDebugModeHandler().Handle(session, reader);

        Assert.That(npc.AiOff, Is.False);
        Assert.That(reader.Position, Is.EqualTo(0));
    }

    // Login and Game share the address check of a migration answer.
    [Test]
    public void Migration_EndpointHelperRefusesABadAddressOrPort() {
        Assert.That(Maple2.Server.Core.Helpers.EndpointHelper.TryEndpoint("10.0.0.2", 65535, out System.Net.IPEndPoint? endpoint), Is.True);
        Assert.That(endpoint!.Port, Is.EqualTo(65535));
        Assert.That(Maple2.Server.Core.Helpers.EndpointHelper.TryEndpoint("", 20001, out endpoint), Is.False);
        Assert.That(Maple2.Server.Core.Helpers.EndpointHelper.TryEndpoint(null, 20001, out endpoint), Is.False);
        Assert.That(Maple2.Server.Core.Helpers.EndpointHelper.TryEndpoint("10.0.0.2", 0, out endpoint), Is.False);
        Assert.That(endpoint, Is.Null);
    }

    // The NPC's AI values are written by the field thread while a session thread lists them for the window.
    [Test]
    public void NpcControl_ExtraListReadsWhileTheValuesChange() {
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(FieldNpc).GetField(nameof(FieldNpc.AiExtraData))!.SetValue(npc, new ConcurrentDictionary<string, int>());
        Assert.That(npc.AiExtraData, Is.InstanceOf<ConcurrentDictionary<string, int>>());

        using var stop = new System.Threading.CancellationTokenSource();
        System.Threading.Tasks.Task writer = System.Threading.Tasks.Task.Run(() => {
            for (int i = 0; !stop.IsCancellationRequested; i++) {
                npc.AiExtraData["k" + (i % 200)] = i;
                npc.AiExtraData.TryRemove("k" + ((i + 100) % 200), out _);
            }
        });
        try {
            for (int i = 0; i < 2000; i++) {
                List<(string Key, int Value)> list = GmNpcControlRequest.ExtraList(npc.AiExtraData);
                Assert.That(list.Count, Is.LessThanOrEqualTo(GmNpcControlRequest.MaxExtra));
                Assert.That(list.Select(entry => entry.Key).Distinct().Count(), Is.EqualTo(list.Count));
            }
        } finally {
            stop.Cancel();
            writer.Wait();
        }
    }

    // The AI file button (uinpccontroldialog.cpp:576-591) sends the path of the picked file.
    [Test]
    public void NpcControl_AiFileNameIsThePathBelowTheAiFolder() {
        Assert.That(GmNpcControlRequest.AiFileName(@"C:\Game\Data\Xml\AI\AI_Default.xml"), Is.EqualTo("AI_Default.xml"));
        Assert.That(GmNpcControlRequest.AiFileName(@"C:\Game\Data\Xml\AI\Boss\AI_Some.xml"), Is.EqualTo("Boss/AI_Some.xml"));
        Assert.That(GmNpcControlRequest.AiFileName("D:/x/data/xml/ai/Sub/a.XML"), Is.EqualTo("Sub/a.XML"));
        Assert.That(GmNpcControlRequest.AiFileName(@"C:\Elsewhere\AI_Default.xml"), Is.EqualTo("AI_Default.xml"));
        Assert.That(GmNpcControlRequest.AiFileName("AI_Default.xml"), Is.EqualTo("AI_Default.xml"));
        foreach (string? text in new[] { null, "", ".xml", "AI_Default.txt", @"C:\Game\Data\Xml\AI\..\secret.xml", "x/Xml/AI//a.xml",
                     "a\nb.xml", new string('a', GmNpcControlRequest.MaxAiFile) + ".xml" }) {
            Assert.That(GmNpcControlRequest.AiFileName(text), Is.Null, text);
        }
    }

    // The AI file button parses to one string, and a plain account cannot hand an NPC another AI file.
    [Test]
    public void NpcControl_AiFileFromAPlainAccountChangesNothing() {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var body = new ByteWriter();
        body.WriteInt(77);
        body.WriteByte(GmNpcControlRequest.AiFile);
        body.WriteUnicodeString("C:/Game/Data/Xml/AI/AI_Default.xml");
        var bodyReader = new ByteReader(body.ToArray(), 0);
        GmNpcControlRequest request = GmNpcControlRequest.Read(bodyReader);
        Assert.That(request.Command, Is.EqualTo(GmNpcControlRequest.AiFile));
        Assert.That(GmNpcControlRequest.AiFileName(request.Text), Is.EqualTo("AI_Default.xml"));
        Assert.That(bodyReader.Available, Is.EqualTo(0));

        GameSession session = SessionOf(AdminPermissions.None);
        var field = (FieldManager) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager));
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        var mobs = new ConcurrentDictionary<int, FieldNpc>();
        mobs[77] = npc;
        typeof(FieldManager).GetField("<Mobs>k__BackingField", flags)!.SetValue(field, mobs);
        session.Field = field;

        var writer = new ByteWriter();
        writer.WriteByte(4);
        writer.WriteBytes(body.ToArray());
        var reader = new ByteReader(writer.ToArray(), 0);
        new SetDebugModeHandler().Handle(session, reader);

        Assert.That(npc.PendingAiFile, Is.Null);
        Assert.That(reader.Position, Is.EqualTo(0));

        npc.RequestAiFile("AI_Default.xml");
        Assert.That(npc.PendingAiFile, Is.EqualTo("AI_Default.xml"));
    }
}
