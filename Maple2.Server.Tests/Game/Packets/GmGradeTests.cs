using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The character block: account, character, name, u8 gender, u8 GM grade. The client shows a GM for 50 or more.
public class GmGradeTests {
    private const string Name = "name";
    private const int GradeInBlock = 8 + 8 + (2 + 4 * 2) + 1;

    [TestCase(AdminPermissions.None, 1)]
    [TestCase(AdminPermissions.GameMaster, 50)]
    [TestCase(AdminPermissions.Admin, 255)]
    public void CharacterList_WritesTheAccountsGrade(AdminPermissions permissions, int grade) {
        var account = new Account { Username = "user", AdminPermissions = permissions };
        var character = new Character { Name = Name, Mastery = new Mastery() };
        character.ReturnMaps.Push(2000062);
        var entries = new List<(Character, IDictionary<ItemGroup, List<Item>>)> {
            (character, new Dictionary<ItemGroup, List<Item>>()),
        };

        ByteWriter writer = CharacterListPacket.AddEntries(account, entries);

        // opcode, command, count, then the block
        Assert.That(writer.Buffer[2 + 1 + 1 + GradeInBlock], Is.EqualTo((byte) grade));
    }

    [TestCase(AdminPermissions.None, 1)]
    [TestCase(AdminPermissions.Warp, 1)]
    [TestCase(AdminPermissions.GameMaster, 50)]
    [TestCase(AdminPermissions.Admin, 255)]
    public void PlayerInfo_WritesTheAccountsGrade(AdminPermissions permissions, int grade) {
        var info = new PlayerInfo(new CharacterInfo(1, 2, Name, "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []) {
            AccountAdminPermissions = permissions,
        };
        var writer = new ByteWriter();

        info.WriteTo(writer);

        Assert.That(writer.Buffer[GradeInBlock], Is.EqualTo((byte) grade));
    }
}
