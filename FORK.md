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
| client to server | 680 | 239 | 5 | 67 | 369 |
| server to client | 1,193 | 592 | 4 | 0 | 597 |

The 67 client-to-server fails are all sub-types the server does not handle (listed below).

Server-to-client rows whose layout depends on client state are checked once per branch the client can take; a row passes when one branch reads the packet exactly. Of the server-to-client passes, 10 are weak (only a branch that ignores the packet, or a guessed condition, fits).

The four server-to-client warnings are kept on purpose; in each the client reads a prefix and ignores the rest:
- Trade sub-type 13 carries a trailing byte that the official server sends too.
- The skill state packet (sub-type 6) carries an extra int that shifts fields the client reads and then discards.
- The achievement favorite packet has no reader in the client (it handles sub-types 0 to 2 only).
- The skill macro load (sub-type 0) is read by a client handler that acts on sub-type 2 only.

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
- **Mentor:** the server no longer sends an initial packet that the client has no reader for, and its writer is removed.
- **Party:** cancel party search reads one byte; the dungeon match confirm (sub-type 31) has no body; the survival squad register is sub-type 55.
- **Room dungeon:** the dungeon result is sub-type 10; the stage packets write the int, byte and bool the client reads (create) and nothing after the sub-type (clear).
- **Party search:** Remove sends sub-type 1, and the error's request type is one byte.
- **Ugc:** the confirmation request reads the client's two strings.
- **Change attributes:** the ForceFill sub-type is 8 and the debug remake sub-type is 7.
- **Skill damage:** sub-types 7 and 8 write the client's fields; dot damage (sub-type 3) writes the amount as a long.
- **Guide objects:** the skill guide has no int before the entry count; a ball is created on field entry through the home action packet, not the guide object create, which refuses a ball.
- **Guild:**
  - Server-to-client sub-types 52 to 89 are shifted up by 2 to the client's numbers.
  - Experience, funds and member contribution use the client's sub-types (53 and 97).
- **Server list:** the error writes the string the client reads first.
- **Field portal:** sub-type 3 writes the portal id before the two vectors.
- **Cube:**
  - The architect score, build budget and build permission packets (sub-types 28 and 56 to 59) write the client's fields.
  - The profile update writes four ints before the name and a trailing byte; plot extension writes only the error byte.
  - The vote confirm (sub-type 25) writes the error byte and, on success, the character id and value; sub-type 53 writes the four longs the client reads.
- **Fishing:** the simulation result writes four fixed ints per fish.
- **Item socket:** unequipping a gem writes the gem's uid at the end.
- **Club:** a member rename writes the club id before the names; a member update writes only the member profile; leaving writes only the club id.
- **Shop:** a shop item writes the extra flag the client reads.
- **Character list:** the name change result writes only what the client reads.
- **User environment:** the gathering counts have no trailing int.
- **Mail, global portal, premium club:** results the client reads no fields of (mail sent, portal closed, premium club sub-types 3 and 4) write only the sub-type.
- **Chat stickers:** adding a sticker group writes a group count of 1, not the item stack size.
- **Strings:** server message codes are mapped to the client's string ids by name.
- **Data:** 62 constants missing from the KMS constants file get the old built-in values as defaults; the file parser is updated for current XML.
- **Items:**
  - The option block ends with the int the client reads (itemoptiondata_1.cpp:260).
  - The enchant history is in the client's layout (itemenchantinfo.cpp:28).
  - A gem writes its id as an int (itemgem.cpp:1010).
  - Enchant sub-type 15 writes the uid and the whole item.
  - The limit break preview has no meso field.
  - Adding a pet item writes no string before the item.
- **Function cube:** the command numbers are the client's, and a gathering success writes the count 1 the client adds to its tally.
- **Requests read without a reply yet** (each logs what it read; the reply needs state the server does not keep):
  - Item merge choice, revert and material select; manufacture through the enchant scroll; socket transfer; exchange scroll target and fill.
  - Change attributes: keep current (drops the pending result), stage item, open dialog.
  - Revival: coupon revive and summon revive.
  - Meret Market: UGC sale details, the by-id lists (at most 20 ids) and the random sale requests.
  - System shop: every shop kind; closing one clears the open shop.
- **Network:** a session is closed gracefully after the last send, so the last packet is not lost to a reset.

## Database migrations
The servers do not migrate the game database when they start. After an update that adds a migration (`Maple2.Server.World/Migrations`), run `dotnet ef database update` in `Maple2.Server.World` (the ingest runs the same command) before the servers start. Without the `home-block` table of `AddHomeBlockTable` every login fails, because the home block list is read at login.

## GM level
The client has one GM level byte. The server sends it from the account's permissions (`AdminLevel.Of`), in the Admin packet at field entry and again when `perm` changes the permissions:

| Account permissions | Level |
|---|---|
| Admin | 255 |
| GameMaster (all of its flags; finer flags on top do not matter) | 50 |
| anything else, also single flags such as Warp or SpawnItem | 1 (the client's own value for a player) |

The level only switches client features on (debug keys, filter skip, debug buttons). The client can raise it by itself, so every GM action is still checked on the server.

The GM grade byte in the party, guild and buddy views comes from the account as it was loaded at login; a `perm` change shows there after a relog. The field view and the Admin packet follow at once.

GM chat commands the client's own GM windows and hotkeys send (each needs the permission in brackets):

| Command | What the server does |
|---|---|
| `/MoveField <id> [flag]` (Warp) | move to the field |
| `/AddField <id>` (Warp) | a plain move to the field, the same as `/MoveField` |
| `/ClearInven` (GameMaster) | empties every inventory tab; worn items and the item of a summoned pet stay |
| `/ToggleInvisibleFromNpc` (GameMaster) | monsters do not pick the player as a target |
| `/ToggleDeathPower` (GameMaster) | each hit kills the monster it lands on; friendly NPCs and pets take normal damage |

The two toggles last for the session (they stay on across field changes and go off at a channel change, a relog, or when the account stops being a GM).

## GM-only packets
Some packets and packet modes are sent by the client only from a GM command, a debug hotkey or a developer dialog (the client tests its GM level or its debug mode before it writes them). A handler for one of those starts with `GmCheck.Allow(session, OpCode, mode)` (`Maple2.Server.Game/Util/GmCheck.cs`): the account needs the GameMaster or Admin permission, as set with the `perm` command. Any other sender is dropped with one warning (account id, opcode, mode) and gets no reply. A new handler or mode of that kind must start the same way.

## Known not to work

The client sends 67 sub-types that the server does not handle yet. They are listed here per system, with the number of unhandled sub-types:

| system | count | system | count | system | count |
|---|---|---|---|---|---|
| RequestCube | 9 | RequestItemEnchant | 5 | FunctionCube | 2 |
| EventReward | 9 | Wedding | 3 | BuddyBadge | 2 |
| Quest | 8 | HomeAction | 3 | RequestItemInventory | 2 |
| Guild | 6 | Party | 2 | | |
| RoomDungeon | 5 | | | | |

These systems have one unhandled sub-type each: Achieve, Buddy, ChatSticker, Club, FurnishingStorage, InteractObject, Mapleopoly, Prestige, RequestRide, Trade, Ugc.

The checker follows the client's item reader (CItem::ReadFromStream, commonfiles/item.cpp:2655). It picks the item's optional blocks from the item tables, which it reads at run time. Where a packet does not carry the item id, the checker reads a plain item.

## Two local changes a server operator must know

1. **All servers bind to loopback (127.0.0.1) by default.** To serve other machines, set `MS2_BIND_IP` to the address to listen on (`0.0.0.0` for all interfaces, as upstream); the server has no hardening for the open internet beyond upstream's.
2. **Test hooks.** Two environment variables turn on test hooks. Neither does anything unless it is set.
   - `TST_CMD_FILE`: once a second, the game server reads a file named after this prefix and the character id, and runs each line as a chat command **as the logged-in player**. Never set it on a server that others can reach.
   - `TST_PKT_DIR`: the server appends every packet that clients send, including chat, to a log file per character in this directory.

## Licence

AGPL-3.0, as upstream. See LICENSE.
