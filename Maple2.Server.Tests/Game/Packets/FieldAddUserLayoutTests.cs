using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ICSharpCode.SharpZipLib.Zip.Compression;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// FieldAddUser after the character and skill info, walked in the order of the client's reader,
// CSceneStateGame::OnAddUser (scenestategame.cpp:11097), for a player with nothing equipped, no ride and no pet: the
// walk has to end on the last byte, so every field is read at its width.
public class FieldAddUserLayoutTests {
    // The zlib block (bool deflated, int size; deflated: int big-endian inflated length + the stream; else the bytes).
    private static ByteReader Block(ByteReader reader) {
        bool deflated = reader.ReadBool();
        int size = reader.ReadInt();
        if (!deflated) {
            return new ByteReader(reader.ReadBytes(size), 0);
        }
        byte[] lengthBytes = reader.ReadBytes(4);
        int length = lengthBytes[0] << 24 | lengthBytes[1] << 16 | lengthBytes[2] << 8 | lengthBytes[3];
        var inflater = new Inflater();
        inflater.SetInput(reader.ReadBytes(size - 4));
        byte[] data = new byte[length];
        Assert.That(inflater.Inflate(data), Is.EqualTo(length));
        return new ByteReader(data, 0);
    }

    [Test]
    public void TailAfterSkillInfo() {
        var test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        GameSession session = test.Session;
        // The test session has no skill manager: an empty skill info (job 0, no skill) for the head.
        var skillInfo = (SkillInfo) RuntimeHelpers.GetUninitializedObject(typeof(SkillInfo));
        var skills = new IDictionary<int, SkillInfo.Skill>[SkillInfo.SKILL_TYPES, SkillInfo.SKILL_RANKS];
        var subSkills = new IDictionary<int, SkillInfo.Skill>[SkillInfo.SKILL_TYPES, SkillInfo.SKILL_RANKS];
        for (int i = 0; i < SkillInfo.SKILL_TYPES; i++) {
            for (int j = 0; j < SkillInfo.SKILL_RANKS; j++) {
                skills[i, j] = new Dictionary<int, SkillInfo.Skill>();
                subSkills[i, j] = new Dictionary<int, SkillInfo.Skill>();
            }
        }
        typeof(SkillInfo).GetField(nameof(SkillInfo.Skills))!.SetValue(skillInfo, skills);
        typeof(SkillInfo).GetField(nameof(SkillInfo.SubSkills))!.SetValue(skillInfo, subSkills);
        var skill = (SkillManager) RuntimeHelpers.GetUninitializedObject(typeof(SkillManager));
        typeof(SkillManager).GetField(nameof(SkillManager.SkillInfo))!.SetValue(skill, skillInfo);
        typeof(ConfigManager).GetField(nameof(ConfigManager.Skill))!.SetValue(session.Config, skill);
        session.Ride = new RideManager(session); // not mounted
        test.SetTable(new InsigniaTable(new Dictionary<int, InsigniaTable.Entry>())); // the insignia is not shown
        FieldPlayer player = session.Player;
        Player value = player;
        value.Character.Title = 0x01020304;
        value.Character.Insignia = 0x0506;
        value.Home.CurrentArchitectScore = 7;
        value.Home.ArchitectScore = 8;
        value.Account.PremiumTime = 0x0102030405060708;

        ByteWriter packet = FieldPacket.AddPlayer(session);
        var head = new ByteWriter();
        head.WriteInt(player.ObjectId);
        typeof(FieldPacket).GetMethod("WriteCharacter", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [head, session]);
        head.WriteClass<SkillInfo>(session.Config.Skill.SkillInfo);
        var reader = new ByteReader(packet.Buffer[2..packet.Length], head.Length);

        Assert.Multiple(() => {
            Assert.That(reader.ReadFloat(), Is.EqualTo(player.Position.X)); // 4: pos, dir
            reader.Skip(8);
            reader.Skip(12);
            Assert.That(reader.ReadByte(), Is.Zero); // 5
            Assert.That(reader.ReadByte(), Is.EqualTo(0x23)); // 6: PC::vfunc_90, n == 0x23
            reader.Skip(3 * (8 + 4 * 4));
            Assert.That(reader.ReadBool(), Is.EqualTo(player.InBattle)); // 7: the battle state
            reader.ReadByte(); // 8: u8, CubeItemInfo (int, long, long, bool hasLook), int
            reader.ReadInt();
            reader.ReadLong();
            reader.ReadLong();
            Assert.That(reader.ReadBool(), Is.False); // the default held cube has no look
            reader.ReadInt();
            reader.Skip(8); // 10: two colours
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo(value.Character.Picture)); // 11
            Assert.That(reader.ReadBool(), Is.False); // 12: no ride
            Assert.That(reader.ReadInt(), Is.Zero); // 13: nothing held
            reader.ReadLong(); // 14
            Assert.That(reader.ReadInt(), Is.EqualTo(7));
            Assert.That(reader.ReadInt(), Is.EqualTo(8));

            ByteReader equips = Block(reader); // 15: u8 n, bool, i64, i64, u8 n
            Assert.That(equips.ReadByte(), Is.Zero);
            equips.ReadBool();
            Assert.That(equips.ReadLong(), Is.EqualTo(value.Character.SkinTabStart));
            Assert.That(equips.ReadLong(), Is.EqualTo(value.Character.SkinTabEnd));
            Assert.That(equips.ReadByte(), Is.Zero);
            Assert.That(equips.Available, Is.Zero);
            ByteReader cooldowns = Block(reader); // 16: u8 n
            Assert.That(cooldowns.ReadByte(), Is.Zero);
            Assert.That(cooldowns.Available, Is.Zero);
            ByteReader gems = Block(reader); // 17: u8 n
            Assert.That(gems.ReadByte(), Is.Zero);
            Assert.That(gems.Available, Is.Zero);

            int buffs = reader.ReadShort(); // 18: u16 n, n x (3 x i32, i32, i32, i32, i16, i32, u8, i64)
            reader.Skip(buffs * (3 * 4 + 4 + 4 + 4 + 2 + 4 + 1 + 8));
            reader.Skip(8); // 19: i32, i32
            reader.ReadByte(); // 20
            reader.Skip(4 + 1 + 1); // 21: PointTeamMode
            Assert.That(reader.ReadInt(), Is.EqualTo(0x01020304)); // 22: title
            Assert.That(reader.ReadShort(), Is.EqualTo(0x0506)); // 23: insignia, shown
            Assert.That(reader.ReadBool(), Is.False);
            reader.ReadInt(); // 24
            Assert.That(reader.ReadBool(), Is.False); // 25: no pet
            Assert.That(reader.ReadLong(), Is.EqualTo(0x0102030405060708)); // 26
            reader.ReadInt(); // 27: i32, u8 tag, i16
            Assert.That(reader.ReadByte(), Is.Zero);
            reader.ReadShort();
            Assert.That(reader.Available, Is.Zero);
        });
    }
}
