using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Sync;

namespace Maple2.Server.Tests.Game.Util;

// A permission change reaches the account's other cached characters too.
public class PlayerInfoSiblingsTests {
    private static PlayerInfo Info(long accountId, long characterId, AdminPermissions permissions) {
        return new PlayerInfo(new CharacterInfo(accountId, characterId, "n", "", "", Gender.Male, Job.Newbie, 1), "", default, new List<long>()) {
            AccountAdminPermissions = permissions,
        };
    }

    [Test]
    public void OtherCharactersOfTheAccount_WithAnotherValue() {
        PlayerInfo[] cached = [
            Info(1, 10, AdminPermissions.GameMaster),
            Info(1, 11, AdminPermissions.None),
            Info(1, 12, AdminPermissions.GameMaster),
            Info(2, 20, AdminPermissions.None),
        ];

        Assert.That(PlayerInfoSiblings.Of(cached, 10, AdminPermissions.GameMaster), Is.EqualTo(new long[] { 11 }));
        Assert.That(PlayerInfoSiblings.Of(cached, 10, AdminPermissions.None), Is.EqualTo(new long[] { 12 }));
        Assert.That(PlayerInfoSiblings.Of(cached, 20, AdminPermissions.GameMaster), Is.Empty);
        Assert.That(PlayerInfoSiblings.Of(cached, 99, AdminPermissions.GameMaster), Is.Empty);
    }
}
