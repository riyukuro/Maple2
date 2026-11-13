using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.World;

// Black market search keys as the client builds them, "%01d%01d%03d" of group, percent and stat (BlackMarketStatKey),
// for stats of table/blackmarkettable.xml: bap (basic, flat), pen (basic, percent="true"), killHPRestore and
// KnockBackReduce (special, percent="false"), fireDamage (special, percent="true").
public class BlackMarketStatKeyTests {
    [TestCase(29, false, false, 29)]
    [TestCase(1031, false, true, 31)]
    [TestCase(10013, true, false, 13)]
    [TestCase(10039, true, false, 39)]
    [TestCase(11019, true, true, 19)]
    public void Decode_IsTheClientsFormat(int key, bool special, bool percent, int stat) {
        Assert.That(BlackMarketStatKey.Decode(key), Is.EqualTo(new BlackMarketStatKey(special, percent, stat)));
        Assert.That(new BlackMarketStatKey(special, percent, stat).Key, Is.EqualTo(key));
    }

    [TestCase(-1)]
    [TestCase(2000)]
    [TestCase(12000)]
    [TestCase(20000)]
    [TestCase(21005)]
    public void Decode_RefusesWhatTheClientDoesNotBuild(int key) {
        Assert.That(BlackMarketStatKey.Decode(key), Is.Null);
        Assert.That(BlackMarketHandler.ServerStatKey(key), Is.Null);
    }

    // The game server maps a basic stat's client id to the server's and keeps a special stat's key.
    [Test]
    public void ServerStatKey_MapsOnlyBasicStats() {
        Assert.Multiple(() => {
            Assert.That(BlackMarketHandler.ServerStatKey(29), Is.EqualTo((int) BasicAttribute.BonusAtk));
            Assert.That(BlackMarketHandler.ServerStatKey(1031), Is.EqualTo(1000 + (int) BasicAttribute.Piercing));
            Assert.That(BlackMarketHandler.ServerStatKey(10013), Is.EqualTo(10013));
            Assert.That(BlackMarketHandler.ServerStatKey(10039), Is.EqualTo(10039));
            Assert.That(BlackMarketHandler.ServerStatKey(11019), Is.EqualTo(11019));
        });
    }

    // The world server's filter of each key: a flat special stat is a special value, not a basic rate.
    [Test]
    public void ReadStat_DecodesAllFourGroups() {
        var basic = new Dictionary<BasicAttribute, BasicOption>();
        var special = new Dictionary<SpecialAttribute, SpecialOption>();
        foreach ((int key, int value) in new[] { (29, 12), (1031, 145), (10013, 30), (10039, 2), (11019, 95) }) {
            WorldService.ReadStat(basic, special, BlackMarketHandler.ServerStatKey(key)!.Value, value);
        }
        Assert.Multiple(() => {
            Assert.That(basic, Has.Count.EqualTo(2));
            Assert.That(basic[BasicAttribute.BonusAtk], Is.EqualTo(new BasicOption(12)));
            Assert.That(basic[BasicAttribute.Piercing], Is.EqualTo(new BasicOption(0.015f)));
            Assert.That(special, Has.Count.EqualTo(3));
            Assert.That(special[SpecialAttribute.HpOnKill], Is.EqualTo(new SpecialOption(30)));
            Assert.That(special[SpecialAttribute.ReduceKnockBack], Is.EqualTo(new SpecialOption(2)));
            Assert.That(special[SpecialAttribute.FireDamage], Is.EqualTo(new SpecialOption(0.01f)));
        });
    }

    // A stat id the server's enums have no member for is left out, not cast into another stat.
    [Test]
    public void ReadStat_LeavesOutUnknownStats() {
        var basic = new Dictionary<BasicAttribute, BasicOption>();
        var special = new Dictionary<SpecialAttribute, SpecialOption>();
        WorldService.ReadStat(basic, special, 10300, 5);
        WorldService.ReadStat(basic, special, 10999, 5);
        WorldService.ReadStat(basic, special, 285, 5);
        Assert.That(basic, Is.Empty);
        Assert.That(special, Is.Empty);
    }
}
