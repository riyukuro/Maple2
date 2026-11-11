using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Packets;

// A taxi request to a field with enterAdminLevel above 0. The client asks only with debug mode on
// (fielddatacli.cpp FUN_140a55d20) and shows string 0x99d, s_field_limit_admin, otherwise.
public class TaxiAdminFieldTests {
    [Test]
    public void OnlyAGmEntersAnAdminField() {
        Assert.That(FieldEntryRules.AdminFieldAllowed(0, gm: false), Is.True);
        Assert.That(FieldEntryRules.AdminFieldAllowed(0, gm: true), Is.True);
        Assert.That(FieldEntryRules.AdminFieldAllowed(1, gm: false), Is.False);
        Assert.That(FieldEntryRules.AdminFieldAllowed(50, gm: false), Is.False);
        Assert.That(FieldEntryRules.AdminFieldAllowed(1, gm: true), Is.True);
        Assert.That(FieldEntryRules.AdminFieldAllowed(-1, gm: false), Is.True);
    }

    [Test]
    public void PermissionsDecide_NotASingleFlag() {
        Assert.That(GmCheck.IsGm(AdminPermissions.None), Is.False);
        Assert.That(GmCheck.IsGm(AdminPermissions.Warp), Is.False);
        Assert.That(GmCheck.IsGm(AdminPermissions.GameMaster), Is.True);
        Assert.That(GmCheck.IsGm(AdminPermissions.GameMaster | AdminPermissions.SpawnItem), Is.True);
        Assert.That(GmCheck.IsGm(AdminPermissions.Admin), Is.True);
    }

    // The refusal is the client's own text: a message box with the client string id 2461 (0x99d), no arguments.
    [Test]
    public void Refusal_IsTheClientsString() {
        ByteWriter writer = NoticePacket.MessageBox(StringCode.s_field_limit_admin);
        byte[] tail = writer.Buffer.AsSpan(writer.Length - 13, 13).ToArray();

        Assert.That(tail, Is.EqualTo(new byte[] {
            0x01, // a string id follows
            0x01, 0x00, 0x00, 0x00,
            0x9D, 0x09, 0x00, 0x00, // 2461
            0x00, 0x00, 0x00, 0x00, // no arguments
        }));
    }
}
