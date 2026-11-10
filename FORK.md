# Maple2 for the KMS 2525 client

This fork is upstream [Maple2](https://github.com/MS2Community/Maple2) with fixes for the Korean MapleStory 2 client, build 2525. The branch is `kms2525`. Upstream's README.md is unchanged, which keeps merges from upstream simple. Setup and running are the same as upstream.

## How packet layouts are checked

Packet layouts are checked against the client source, not against another server. For each packet type and sub-type, a checker compares two things:

- Client to server: does the server read exactly the bytes the client's writer sends?
- Server to client: does the server write exactly the bytes the client's reader expects?

Each row gets one verdict:

- **pass:** the bytes match exactly.
- **warning:** the server writes more bytes than the client reads (trailing bytes).
- **fail:** the server reads past the end of what the client sends, the client would misread what the server sends, or the server does not handle a sub-type the client sends.
- **not checkable:** the client's reader or writer could not be followed automatically, or the server's code needs game state before it reads.

Every fix in this fork names the client function it follows (a file and line under the client source). The commit message quotes the checker's line before and after.

Results today:

| direction | rows | pass | warning | fail | not checkable |
|---|---|---|---|---|---|
| client to server | 678 | 215 | 5 | 91 | 367 |
| server to client | 1,012 | 419 | 29 | 0 | 564 |

Of the 91 client-to-server fails, 90 are sub-types the server does not handle (listed below). The other one is a known false alarm in the checker.

## Fixes so far, by system

- **Opcodes:** client-to-server opcodes 0xAB to 0xBC are renumbered to the client's list.
- **Emote:** the list entry is 26 bytes; the favorite request (sub-type 4) is read as the client writes it.
- **Trigger:** the sub-type numbers are the client's (skip cutscene, UI, load script, save script, discard script).
- **Item merge:**
  - Request and response sub-types are renumbered to the client's.
  - The option preview (Select) is written in the client's layout.
- **Ride:**
  - RideSync reads one trailing int per record.
  - The ride start request carries a byte after the object id, and the response echoes it.
- **Mentor:** the server no longer sends an initial packet that the client has no reader for.
- **Party:** cancel party search reads one byte.
- **Party search:** Remove sends sub-type 1, and the error's request type is one byte.
- **Ugc:** the confirmation request reads the client's two strings.
- **Change attributes:** the ForceFill sub-type is 8 and the debug remake sub-type is 7.
- **Skill damage:** sub-types 7 and 8 write the client's fields.
- **Guild:** server-to-client sub-types 52 to 89 are shifted up by 2 to the client's numbers.
- **Server list:** the error writes the string the client reads first.
- **Field portal:** sub-type 3 writes the portal id before the two vectors.
- **Cube:** the architect score, build budget and build permission packets (sub-types 28 and 56 to 59) write the client's fields.
- **Fishing:** the simulation result writes four fixed ints per fish.
- **Item socket:** unequipping a gem writes the gem's uid at the end.
- **Club:** a member rename writes the club id before the names.
- **Shop:** a shop item writes the extra flag the client reads.
- **Strings:** server message codes are mapped to the client's string ids by name.
- **Data:** 62 constants missing from the KMS constants file get the old built-in values as defaults; the file parser is updated for current XML.
- **Network:** a session is closed gracefully after the last send, so the last packet is not lost to a reset.

## Known not to work

The client sends 90 sub-types that the server does not handle yet. They are listed here per system, with the number of unhandled sub-types:

| system | count | system | count | system | count |
|---|---|---|---|---|---|
| RequestCube | 9 | ChangeAttributes | 3 | ItemExchangeScroll | 2 |
| MeretMarket | 9 | Wedding | 3 | FunctionCube | 2 |
| EventReward | 9 | ItemMerge | 3 | BuddyBadge | 2 |
| Quest | 8 | HomeAction | 3 | Party | 2 |
| Guild | 6 | Revival | 2 | ItemSocketSystem | 2 |
| RoomDungeon | 5 | RequestItemInventory | 2 | | |
| RequestItemEnchant | 5 | | | | |

These systems have one unhandled sub-type each: Achieve, Buddy, ChatSticker, Club, EnchantScroll, FurnishingStorage, InteractObject, Mapleopoly, Prestige, RequestRide, SystemShop, Trade, Ugc.

The checker also cannot yet follow the client's item reader. The item block inside inventory, shop and trade packets is therefore not checked yet.

## Two local changes a server operator must know

1. **All servers bind to loopback (127.0.0.1).** The login, game, world and web servers do not accept connections from other machines. To serve other machines, change the addresses in `Maple2.Server.Core/Constants/Target.cs` and in the listeners.
2. **Test hooks.** Two environment variables turn on test hooks. Neither does anything unless it is set.
   - `TST_CMD_FILE`: once a second, the game server reads a file named after this prefix and the character id, and runs each line as a chat command **as the logged-in player**. Never set it on a server that others can reach.
   - `TST_PKT_DIR`: the server appends every packet that clients send, including chat, to a log file per character in this directory.

## Licence

AGPL-3.0, as upstream. See LICENSE.
