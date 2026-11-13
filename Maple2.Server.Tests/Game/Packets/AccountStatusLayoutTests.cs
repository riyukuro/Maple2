using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Insignia (CSceneStateGame::OnNametag, scenestategame.cpp:7314): int object id, u16 insignia, u8 shown.
// Prestige (OnPrestige, scenestategame.cpp:8815; payload reader FUN_1407a8b20, missions FUN_1407a90e0, exe only):
// byte mode; 0: long current exp, int level, long exp, int count x int claimed level; 1: long current exp, long gained;
// 2: int object id, int level; 4: byte 1, int count x int level; 6: bool, int count x (long id, long progress, byte
// complete); 7: int count x the same.
// PremiumClub (OnPremiumClub, scenestategame.cpp:8426): byte mode; 0: int object id, long expiry; 1: int count x int
// benefit id; 2: int benefit id (UIVIPDialog::FUN_140fb87d0, uivipdialog.cpp:234).
public class AccountStatusLayoutTests {
    private static ByteReader Read(ByteWriter writer, SendOp op) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) op));
        return reader;
    }

    [Test]
    public void Insignia() {
        ByteReader reader = Read(InsigniaPacket.Update(1234, 27, true), SendOp.Insignia);
        Assert.That(reader.ReadInt(), Is.EqualTo(1234));
        Assert.That(reader.ReadShort(), Is.EqualTo(27));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void PrestigeLoadAndExp() {
        var account = new Account {
            Username = "a",
            PrestigeCurrentExp = 500,
            PrestigeLevel = 12,
            PrestigeLevelsGained = 2,
            PrestigeExp = 9000,
            PrestigeRewardsClaimed = [5, 10],
        };
        ByteReader reader = Read(PrestigePacket.Load(account), SendOp.Prestige);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(500));
        Assert.That(reader.ReadInt(), Is.EqualTo(10));
        Assert.That(reader.ReadLong(), Is.EqualTo(9000));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(10));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(PrestigePacket.AddExp(600, 100), SendOp.Prestige);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(600));
        Assert.That(reader.ReadLong(), Is.EqualTo(100));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(PrestigePacket.LevelUp(1234, 13), SendOp.Prestige);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(1234));
        Assert.That(reader.ReadInt(), Is.EqualTo(13));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(PrestigePacket.ClaimReward(15), SendOp.Prestige);
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(15));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void PrestigeMissions() {
        var account = new Account { Username = "a" };
        account.PrestigeMissions.Add(new PrestigeMission(3) { GainedLevels = 4, Awarded = true });
        ByteReader reader = Read(PrestigePacket.UpdateMissions(account), SendOp.Prestige);
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(4));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(PrestigePacket.LoadMissions(account), SendOp.Prestige);
        Assert.That(reader.ReadByte(), Is.EqualTo(7));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(4));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void PremiumClub() {
        ByteReader reader = Read(PremiumCubPacket.Activate(1234, 1700000000), SendOp.PremiumClub);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(1234));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(PremiumCubPacket.LoadItems([7, 8]), SendOp.PremiumClub);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadInt(), Is.EqualTo(8));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(PremiumCubPacket.ClaimItem(9), SendOp.PremiumClub);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(9));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
