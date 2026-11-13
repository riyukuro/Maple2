using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Commands;

// SkillResetCooldown 0x43 has no body; the client (CSceneStateGame::OnSkillResetCooldown) ends all its skill cooldowns
// and recharge uses. The debug command resetcooldown ends the server's too and sends it. The skill ids are made up.
public class ResetCooldownCommandTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        typeof(ConfigManager).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Config, session);
        typeof(ConfigManager).GetField("skillCooldowns", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Config, new Dictionary<int, SkillCooldown>());
        typeof(ConfigManager).GetField("lastCasts", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session.Config, new Dictionary<int, SkillCastRules.LastCast>());
        test.TakeSent();
    }

    private static byte[] Bytes(ByteWriter writer) => writer.Buffer.AsSpan(0, writer.Length).ToArray();

    [Test]
    public void WritesNoBody() {
        Assert.That(Bytes(SkillPacket.ResetCooldown()), Is.EqualTo(new byte[] { 0x43, 0x00 }));
        Assert.That((int) SendOp.SkillResetCooldown, Is.EqualTo(0x43));
    }

    // Two running cooldowns end on the server and the client gets 0x43.
    [Test]
    public void EndsEveryCooldownAndTellsTheClient() {
        int end = (int) (Environment.TickCount64 + 600_000);
        session.Config.SetSkillCooldown(90000001, 1, end);
        session.Config.SetSkillCooldown(90000002, 1, end);
        test.TakeSent();

        int code = new ResetCooldownCommand(session).Invoke(Array.Empty<string>(), new TestConsole());

        Assert.Multiple(() => {
            Assert.That(code, Is.EqualTo(0));
            Assert.That(session.Config.GetSkillCooldown(90000001), Is.Null);
            Assert.That(session.Config.GetSkillCooldown(90000002), Is.Null);
            Assert.That(session.Config.GetCurrentSkillCooldowns(), Is.Empty);
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.SkillResetCooldown }));
        });
    }

    [Test]
    public void NeedsTheDebugPermission() {
        var command = new ResetCooldownCommand(session);
        Assert.Multiple(() => {
            Assert.That(CommandRouter.Allowed(AdminPermissions.Debug, command), Is.True);
            Assert.That(CommandRouter.Allowed(AdminPermissions.GameMaster, command), Is.False);
            Assert.That(CommandRouter.Allowed(AdminPermissions.None, command), Is.False);
        });
    }
}
