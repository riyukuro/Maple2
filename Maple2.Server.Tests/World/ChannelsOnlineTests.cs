using System.Collections.Generic;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.World;

// The World's Channels reply carries the characters online for the server list's traffic, which is taken over the
// listed channels' capacity: a character on a channel that is not listed (instanced content, not active) or offline
// (-1) is not counted.
public class ChannelsOnlineTests {
    [Test]
    public void OnlyTheListedChannelsCount() {
        var listed = new HashSet<int> { 1, 2 };
        short[] players = [1, 1, 2, 3, -1];

        Assert.That(WorldService.OnlineOn(players, listed), Is.EqualTo(3));
    }
}
