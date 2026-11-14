using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class HomePermissionRulesTests {
    [Test]
    public void OnlyTheOwnerChanges() {
        Assert.That(HomePermissionRules.MayChange(homeOwnerId: 7, accountId: 7), Is.True);
        // A visitor (account 8) in the home of account 7.
        Assert.That(HomePermissionRules.MayChange(homeOwnerId: 7, accountId: 8), Is.False);
        Assert.That(HomePermissionRules.MayChange(homeOwnerId: 0, accountId: 0), Is.False);
    }

    [Test]
    public void EnableStartsAtZero() {
        var permissions = new Dictionary<HomePermission, HomePermissionSetting>();
        Assert.That(HomePermissionRules.Enable(permissions, HomePermission.Jump, true), Is.True);
        Assert.That(permissions[HomePermission.Jump], Is.EqualTo(HomePermissionSetting.None));
        Assert.That(HomePermissionRules.Enable(permissions, HomePermission.Jump, false), Is.True);
        Assert.That(permissions, Is.Empty);
        // The client has rows 0 to 8.
        Assert.That(HomePermissionRules.Enable(permissions, (HomePermission) 9, true), Is.False);
        Assert.That(permissions, Is.Empty);
    }

    [Test]
    public void SetOnEnabledRowsOnly() {
        var permissions = new Dictionary<HomePermission, HomePermissionSetting>();
        // The dialog sends 42 and then 43 for a row: 43 on a row turned off stays at 0.
        Assert.That(HomePermissionRules.Set(permissions, HomePermission.Skill, HomePermissionSetting.Party), Is.EqualTo(HomePermissionSetting.None));
        Assert.That(permissions, Is.Empty);

        HomePermissionRules.Enable(permissions, HomePermission.Skill, true);
        Assert.That(HomePermissionRules.Set(permissions, HomePermission.Skill, HomePermissionSetting.Party), Is.EqualTo(HomePermissionSetting.Party));
        Assert.That(permissions[HomePermission.Skill], Is.EqualTo(HomePermissionSetting.Party));

        Assert.That(HomePermissionRules.Set(permissions, HomePermission.Skill, (HomePermissionSetting) 3), Is.Null);
        Assert.That(HomePermissionRules.Set(permissions, (HomePermission) 9, HomePermissionSetting.Self), Is.Null);
        Assert.That(permissions[HomePermission.Skill], Is.EqualTo(HomePermissionSetting.Party));
    }

    // ResponseCube 0x2a and 0x2b: u8 row, u8 value (CSceneStateGame's ResponseCube handler).
    [Test]
    public void Packets() {
        byte[] enable = CubePacket.EnablePermission(HomePermission.Potion, true).ToArray();
        Assert.That(enable[2..], Is.EqualTo(new byte[] { 0x2a, 4, 1 }));
        byte[] set = CubePacket.SetPermission(HomePermission.AirMount, HomePermissionSetting.Self).ToArray();
        Assert.That(set[2..], Is.EqualTo(new byte[] { 0x2b, 6, 1 }));
    }
    // CubeMapInformationService::FUN_1410c3cd0: no row stops no one; 0 stops everyone; 1 everyone but the owner;
    // 2 everyone but the owner and a party member of the owner.
    [Test]
    public void Blocks() {
        var permissions = new Dictionary<HomePermission, HomePermissionSetting>();
        Assert.That(HomePermissionRules.Blocks(permissions, HomePermission.Music, 1, 2, () => false), Is.False);
        permissions[HomePermission.Music] = HomePermissionSetting.None;
        Assert.That(HomePermissionRules.Blocks(permissions, HomePermission.Music, 1, 1, () => true), Is.True);
        permissions[HomePermission.Music] = HomePermissionSetting.Self;
        Assert.That(HomePermissionRules.Blocks(permissions, HomePermission.Music, 1, 1, () => false), Is.False);
        Assert.That(HomePermissionRules.Blocks(permissions, HomePermission.Music, 1, 2, () => true), Is.True);
        permissions[HomePermission.Music] = HomePermissionSetting.Party;
        Assert.That(HomePermissionRules.Blocks(permissions, HomePermission.Music, 1, 1, () => false), Is.False);
        Assert.That(HomePermissionRules.Blocks(permissions, HomePermission.Music, 1, 2, () => true), Is.False);
        Assert.That(HomePermissionRules.Blocks(permissions, HomePermission.Music, 1, 2, () => false), Is.True);
        Assert.That(HomePermissionRules.Blocks(permissions, HomePermission.Skill, 1, 2, () => false), Is.False);
    }
}
