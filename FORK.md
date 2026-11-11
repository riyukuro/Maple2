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
- **Room dungeon:** the dungeon result is sub-type 10; the stage packets write the int, byte and bool the client reads (create) and nothing after the sub-type (clear). A room is refused for a party larger than the dungeon's user maximum: a rule of this server, the client's dialog does not test it.
- **Interact object:** the start of an interaction keeps only objects in reach, and the end of an object that is not gathered is taken only in reach. Rules of this server: a gathered object's reach is the client's MasteryObjectInteractionDistance constant; any other object's reach is 1500 (the client tests the object row's own interactionDistance, which the server does not load); every reach test has 150 (one block) of slack for position lag; a reach constant of 0 tests nothing. Farming and breeding cubes are tested the same way with the constant.
- **Beauty:** a new item (sub-type 3) reads the long between the item id and the appearance; the fee of sub-types 3, 5, 21 and 22 is taken only after the change succeeded. Sub-types 17 and 19 extend the saved style slots by BeautyStyleExpandSlotCount1time for BeautyStyleExpandSlotPrice merets, up to BeautyStyleMaxSlotCount extra slots; 17 then saves the hair it names.
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
  - The enchant history is in the client's layout (itemenchantinfo.cpp:28). Its bool is always sent true: it is not "tradeable" but a flag that, while false, makes the client refuse to destroy, trade, list or repack the item whatever its transfer flags say (itemsocketsystemutil.cpp:2316, 2440, 2468; itemrepackingscrolldata.cpp:154), and the server has no state that locks an item this way.
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

The GM grade byte in the party, guild and buddy views comes from the player info the World server keeps. A `perm` change is sent to World as a player update and goes on to the channels with the other profile fields, so those views follow it the next time they are sent. The field view and the Admin packet follow at once.

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

The client's GM create windows are handled, each behind that check (a plain account changes nothing):

| Packet | Window | What the server does |
|---|---|---|
| 0x15 RequestItemCreate | item create | Needs SpawnItem as well. Creates the item (id, amount, rank) into the inventory by the normal add path. A rank outside 1-6 or an amount below 1 is refused. A stackable item gives one stack at most; other items give one per free slot; a currency id (meso, meret, experience orb and the like) gives 100,000,000 at most per request. A full bag answers with the inventory-full notice. An unknown id is refused. The period, level, enchant and option fields are read and not applied. |
| 0x24 RequestNpcCreate | NPC create | Needs SpawnNpc as well. Spawns the NPC at the position and rotation the window sends (1 to 50 per request, within 3000 units of the sender). One field instance holds at most 200 living GM-spawned NPCs, counted together with the `npc` command, which follows the same two limits. The NPC belongs to that field instance and goes with it. The pet button (pet flag set) is read and not handled. |
| 0x5C RequestAdditionalEffect | effect control | Mode 0 puts the effect (id, level) on the sender; the packet names no target. Mode 1 (reset) takes off the buffs this window created in this session that are still on. A buff the sender already had, or got again later from a skill or an item, stays. |
| 0x50 SetDebugMode | debug mode: minimap click (mode 2), NPC control (mode 4), debug guide commands (mode 7) | Mode 2 (a click on a minimap entity while the debug mode is on) moves the sender to the entity's position when the field accepts it. Mode 4 names an NPC by object id: picking the NPC is answered with DebugMode 0x0087 kind 6 (its skill list) and kind 5 (the window's text: movement state, AI file, battle target, physical and magic attack, defense, critical rate, health and its total; which texts retail put there is not known, these are the server's own choice; no AI trace is kept, so tracking is off; the extra list carries the NPC's AI values by key, at most 32; the AI file text is marked while the AI is switched off), and kind 5 is sent again after a health button, an AI on or off button, the AI file button and the extra button. The health buttons set its health (1 to 100 percent, or a value; never below 1 or above the total, so the window does not kill), the additional effect buttons put an effect (id, level) on it or take every buff of that id off. The skill button makes the NPC cast the first skill of the list once, facing the sender (the loop count and the rest of the list are not played); stop skill cancels the cast. That cast is a normal NPC cast: it hits players in range exactly like the NPC's own skill, so it is a GM tool to use with care. AI off (command 5) stops the NPC from choosing what to do next until AI on (command 4): its AI and idle steps are skipped, what it already does runs to its end, and its battle target is still kept up to date. The switch is logged with the account and lasts until the NPC leaves the field. The extra button (command 0xB) sends kind 5 again; the client has no reader of its own for that button, so what retail answered is not known. The AI file button (command 0xD) sends the path of an xml file picked under the client's Data/Xml/AI folder (uinpccontroldialog.cpp:576-591). The server takes the part of the path below that folder (or the bare file name) as the AI's name; a name it has no AI for, a text that is no xml name or one with a `..` part changes nothing and is logged. The NPC gets the named AI at its next update on the field thread: the AI it ran is dropped where it stood, its AI values and battle target stay. The change is logged with the account, lasts until the NPC leaves the field, and kind 5 is sent again with the new name. That the file replaces the NPC's AI is read from the button's name and its one argument; what else retail did with it is not known. The other buttons (battle setting, traces, assist, current additional) are read and logged, as are modes 0 and 9. Mode 7 (the debug guide window's multi execute button) carries 1 to 32 lines of command and argument, each text at most 256 characters; anything else is dropped. The lines are sent back as DebugMode 0x0087 kind 7 with the sender's name to the sender only; a client with the debug guide window open enters each line in its own chat, so every line comes back as that account's chat command and passes that command's permission check. No other client gets them, also not another GM in the same field: one GM cannot make another client run commands. The client hands every packet to every open window (networkservice.cpp:719, uiservice.cpp:1386), and its NPC control window reads kind 7 as an AI file name and an id and opens an AI debug window for it (uinpccontroldialog.cpp:233); the client sends nothing when that window closes, so once the sender has picked an NPC with the debug camera (mode 4, select) the lines are no longer sent back for the rest of that session (logged), until it logs in again or changes channel. Who retail sent kind 7 to is not known from the client; sender only is this server's own choice. |

The item and NPC windows need the same permission as the chat command that does the same thing (`item`: SpawnItem, `npc`: SpawnNpc), on top of the GM check. GameMaster alone holds neither flag, so a GameMaster account gets them with `perm` or is an Admin.

The client's debug mode (the GM buttons of the enchant, remake, exchange scroll and other dialogs) is toggled by key action 600001, the ` key in the client's default key file; the client turns it on only at GM level 50 or more. The key table the server sends holds that bind for a GM account only (added on ` or Scroll Lock when the saved table lacks it, taken out for a plain account). Every packet those buttons send is behind the GM check or has no handler.

A field with `enterAdminLevel` above 0 in its map file (8 test fields in the data: 80000000 and seven 99999xxx ids) is closed to plain accounts: every move into it is refused, and so is taxi travel to it (walk, car, rotors, meso call and the air taxi item), with the client's own message box, `s_field_limit_admin`. A plain character saved in such a field logs into its return field. A GM account passes. The value is read at ingest, so it needs a re-ingest.

The client reads no reply of its own for these three; it shows the normal item, NPC and buff packets.

## Known not to work

The client sends 42 sub-types that the server does not handle yet. They are listed here per system, with the number of unhandled sub-types:

| system | count | system | count | system | count |
|---|---|---|---|---|---|
| EventReward | 12 | Guild | 1 | Wedding | 2 |
| RequestItemEnchant | 3 | RequestCube | 6 | HomeAction | 2 |
| Quest | 7 | RoomDungeon | 4 | Party | 2 |

These systems have one unhandled sub-type each: FurnishingStorage, InteractObject, Ugc.

Guild handling is switched on (`HandlingOn` in `GuildHandler.cs`). The Guild count above uses this measure: the 39 sub-types that the client writes (one writer each in `CGuildManager`), minus those with a `case` in the switch of `GuildHandler.cs`. That leaves 1: 65 (emblem). A second measure, not in the table: 15 more sub-types have a case that only reads the body or writes a debug line and changes nothing: 74 and 75 (PvP history and season), 90, 91 and 92 (buffs), 99 and 100 (arcade), 101 and 102 (house rank and theme), 109 and 112 (gift and gift log), 114 (NPC upgrade), 115, 116 and 120 (guild events). The count was 6 before and the total 47; neither measure gives 6 today.

Guild rank rights, as the client tests them before it sends: invite 2, expel 4, notice 8, change a member's rank 32, emblem 64. The notice and emblem buttons of the guild window are enabled from rights 8 and 64 (and for the leader), so a rank with rights 3 has both grey and sends nothing. The server sends a changed rank (29, with the rights) and a changed member rank (21) to every member in the form the client reads, and the client stores them and hands them to the window; an open window showed the new rank name, the new member rank and the invite button only after it was reopened in the live try. That part is inside the window's Flash file and is not answered by a packet.

Guild 68, 87 and 88 are handled (they had a case that only read). 68 stores the "listed" flag and the focus bits of the guild; the sender needs the invite right, as in the client, and every member gets the new values. 87 (search by focus) and 88 (search by name, 2 characters or more) answer only a player without a guild, as the client only sends them then. The server's own rules, not read from the client: only listed guilds are found, a focus search finds a guild that shares at least one focus bit (the client's "any" value finds all), a name search finds names that contain the text, and at most 90 guilds are sent (10 pages of the window). The three trophy numbers of a search row are sent as 0. The third number after the member count is the guild's experience, which the client turns into the level; the fork sent the focus there before. Existing guilds become listed with the migration `AddGuildListed`.

Guild 82, 83 and 84 (join requests) are handled, with the lists 85 and 86 and the notices 45 to 48. 82: a player without a guild asks to join a listed guild; one open request per guild and at most 10 open requests (the client's own limit, error 37). The answer is 82 with the guild id and name, and the members of that guild who hold the invite right get the new row (45). 83 withdraws an own request; the id is matched against the guild ids of the player's requests first and then against the request ids, because the client code names it a guild id and the request window holds both. Those members get 46. 84 is sent by a member with the invite right, with the applicant's character id and accept or refuse. On accept the applicant joins as with an invite, all other requests of the applicant are removed (46 to those guilds), and the members with the invite right get 47; the applicant gets 48 and, when online, the guild. 85 lists the open requests of the own guild (invite right), 86 the own requests of a player without a guild. In the request row the server now writes the account id before the character id: the client sends the fourth id back in 84 and uses it for the profile picture. The requests are rows of the existing `guild-application` table; no migration. The client has no separate flag for automatic acceptance: the value the request page shows is the "listed" byte of Guild 68 (`CGuildInfo` +0x154, set with the focus bits), so a request always waits for an answer. A guild that is not listed refuses a request with "guild not found". A player who joins a guild by an invite, or creates one, loses the open requests too (46 to those guilds): the member row and the removal are one save, so a request made before never outlives the join. The member limit is checked and the member added under one lock for both ways in.

Guild 71 (mail): a member with the mail right sends a title (1 to 100 characters) and a text (1 to 2000); every other member gets it as a player mail from the sender, and the sender gets 71. The client has no waiting time between two mails, and the server adds none. Guild 113 (donate): the body is the count of donations. One donation costs the `guildDonateMeso` constant and adds the same to the guild fund. Refused, as in the client: a count under 1, a member who joined today, more donations in a day (UTC) than the guild level allows (`donationMax`), a fund over the level's maximum, too little meso. The member's count and time are stored and sent back in 113; the guild gets the new fund (51). Not done: the client adds a share of the cost while a guild fund event runs, and the guild table names coins and experience for a donation; the client shows neither amount in the reply, so the donor gets none.

Narrowed, with the reason:
- FurnishingStorage 4 (take a stored furnishing item into the bag): the server keeps every furnishing item in the furnishing storage and has no bag tab for them, so the request is dropped and the item stays stored. Placing the item from the storage works.
- EventReward 0 and 1 (stamp event), 2 (event box), 5 (buddy event page opened), 12 and 13 (snowman event), 15 (level-up event), 16 (event character), 19 (item collection event) and 25 to 27 (long-term attendance): the server's event table has no data for these event kinds, so there is nothing to pay out. They are read and logged. Sub-type 5 is sent by any player; it is not a GM mode.
- Ugc 12 (the path of an uploaded wedding profile picture): the upload itself (kind 10) is not accepted and a marriage stores one profile text where the client keeps one per partner, so the request never arrives in normal play and is only logged.
- Revival 3 (revive a dead summon): only logged. It is not counted in the table above.
- Quest 15 (sky fortress mission), 17 (epic quest restart), 19 (re-enter a quest's hidden field), 28 (find a player for a quest) and 33 to 35 (alliance quest ticket, accept all, complete all): the server does not load the data or run the system behind them (no sky fortress missions, no restart event, no re-entry flag in the quest data, no alliance quest accept).
- RequestItemEnchant 9 (refund), 13 and 14 (enchant simulation): read and logged only. The GM debug buttons 15 to 18 are handled for a GM account. 15 (force enchant): the enchant of the item in the window succeeds at once and nothing is consumed. 16 (fill ingredients): the materials of that enchant are put in the bag; the first button tops the bag up to what the enchant needs, the second gives 10 of each (the server's reading of the two button names). 17 and 18: the item's stored fail charges are filled to a full charge rate or emptied. 19 has a sender in the client that nothing calls, so it is not counted; it is logged for a GM account.
- Party 39 and 43 (join a dungeon party from a chat link): the link is built from a recruiting notice that the server never sends. Party 32 to 34 (dungeon match queue) have no matchmaker behind them.
- Wedding 9 (the answer to the divorce message box) and 37 (the item of a wedding skill): the server has no divorce flow and does not load the wedding skill table. Both are read and logged. Wedding 34 (the couple message) is handled: the message is saved and shown to both partners.
- RoomDungeon 8 (buy an extra reward count with a coupon item): the dungeon data the server loads has no coupon item or cost. 15 and 16 (ask for helpers or members): they need the recruiting notice, which the server never sends. 21 (reset the united reward, after a confirm box): what the reset changes is not settled from the client. All are read and logged. 17 (expert mode) and 24 (favourite mark), counted here before, are handled. The four lobby buttons of the dungeon search window (4, 12, 13, 14) belong to the match queue and are not counted.
- RequestCube 5 (the owner uses the sign of their own plot) and 30 (the two strings of the home summon dialog): what they should do is not settled from the client. 19 (a GM debug index) needs a GM account and is only logged. 56 to 58 (the build budget and the build helpers of a home): the server stores neither. Sub-type 13 has a case but the client never sends it, so it is not counted. 68 (the market's blueprint preview) builds only in the model home; if the model home already holds cubes they are removed and the client is asked to send the preview again (sub-type 0x45 of the cube packet, which the client answers with a new 68 once its own cube list is empty; that the retail server used 0x45 this way is read from the client's reader, not seen). In a planner the server refuses a cube that the player neither owns nor finds in the shop, so such a blueprint previews in part. The client gives no rule for this case: for 68 it sends only the blueprint's id, does no owned or shop test of its own, and draws the cubes the server sends back, so the server's refusal is kept. 70 (the bill of a used blueprint item) is handled.
- Achieve 5 (GM debug buttons of the trophy window) is handled for a GM account. The client names the four buttons up, down, up30 and up99; the server reads them as: finish the grade in progress, go one grade back (the reward grade stays, so finishing the grade again gives no second reward), and set the counter to 30 % or 99 % of the grade's goal. That reading comes from the names only.
- Mapleopoly 5 (the GM debug dice) is handled for a GM account: one roll with the two dice the GM picked, at the cost of a normal roll.
- Buddy: with "refuse friend requests" on, the request is declined by the server and no longer shown to the refusing client, which used to send a second decline for the entry that was already gone.
- SkillMacro 1 with no tab (every skill macro tab removed): the save is refused, because the client also sends an empty save when the window closes before the load answer came. A player who removes all tabs gets the stored ones back at the next login.

Revival 1 (the meret revive in place, which was only logged and not counted above) and ChatSticker 1 (expired sticker groups) are handled now. So are Quest 18 (the quest portal opens again when the quest talk ends) and Buddy 20 (the "refuse friend requests" option: a request is declined on receipt while the option is on; the server asks the client for the option at login and also learns it when the options are saved), Club 12 (the leader hands the club to a member) and HomeAction 1 to 3 (the home owner kills, sends out or revives a visitor from the player menu; only sub-type 3 was counted above). RequestRide 6 (the "ride alone" game option: the field is told and nobody can board the mount while it is on) and FunctionCube 5 and 7 (use a cube; closing a cube's dialog hands the cube back to other players) are handled too, EventReward 8 as well, and the client sends no Prestige sub-type other than the two that were already handled. The two HomeAction entries left are the PvP control dialog (14) and the OX quiz (10). The revive block of a buff (`disableRevivalHere`) is a new metadata property: it is empty until the metadata is ingested again. RequestItemInventory 8 and 9 (sort, expand) and Trade 14 (busy answer) were already handled and are no longer counted. The meret revive charges the price the button shows, or one free revive coupon when the box is ticked and a coupon is held; the revive debuff is added only if the server's constants name it.

Packets the client reads that the server never sends, although a player can ask for them. The request is dropped (no handler) unless a line says otherwise:
- MoveEventField 0x0085 (the world map's event field button): no handler for RequestMoveEventField and no event field schedule.
- InteractNpc 0x0059 (object id, state byte; the answer to the client's 0x32, which it sends after the cast on an NPC whose data has an interact function): no handler; the server loads none of the NPC interact fields, so the state flag is never changed.
- FittingDoll 0x007B (the fitting doll in a home): the server has no fitting doll storage.
- FieldMaid 0x009D, UserMaid 0x009E and MaidCraftItem 0x00A9 (the home maid and its crafting): the server has no maid system.
- RecallUser 0x008C (call another player to your place): the client has no request that starts it, so the GM command `recalluser <name>` sends the invite to a player on any channel (for another channel the World server passes the invite to that channel and the result back to the caller's; the GM check is the command's, on the caller's channel). The result values follow the client's string keys (ids 2299 to 2305): the caller gets kind 5 value 1 (s_recall_user_notify_confirm_recall) when the invite goes out, kind 6 (s_recall_user_notify_recalled) after the player was moved, and kind 7 for every failure: 6 not found, 4 already being recalled, 2 the move failed, any other value the player declined. Kind 5 values 0 and 2 (s_recall_user_notify_recall, a recall without a question) are not sent. A player who is offline is answered as not found.
- Gamble 0x009C, CardReverseGame 0x00DA, PlayArcade 0x00D8, TreeWatering 0x0118 and BirthdayCard 0x0111 (event windows): the server's event table has no data for these events.
- SmartRecommendBilling 0x00A0 and NoticeDialog 0x00CF (shop recommendation and notice windows): the server has no content for them.
- PcBangBonus 0x00A5 and SetPcBang 0x00B5 (PC cafe benefits): accounts carry no PC cafe state.
- Pvp 0x00B6 and TeamPvp 0x009A (arena matches): the server has no arena matchmaking; a field's PK mode works without these packets.
- SpectateMushtopia 0x011A, Survival 0x0119 and SurvivalEvent 0x0116 (Mushking Royale): no match server. The server writes Survival and SurvivalEvent for its medal data, and this client has no reader for either.
- WaitingTicketUpdate 0x00B4 (the login queue): the server has no login queue, so the client never waits.
- DungeonMatch 0x00EF and DungeonHelp 0x0109 (dungeon match queue and helper call): no matchmaker and no recruiting notice, as for Party 32 to 34 and RoomDungeon 15 and 16 above.
- MeretUse 0x0117 (the event meret balance after a purchase): the server keeps no event meret balance; purchases update the normal meret packet.
- StateFishingData 0x00FA (another player's fishing state): sent to the others in the field when a player starts and stops fishing. A player who enters the field later is told which players are already fishing.
- RelocateWorld 0x0072 (the world transfer request): no handler; the server runs one world, so there is no world list and no transfer address to send.
- Cash 0x00AD (the cash inventory and cash shop requests): no handler and no cash shop data; the server's shop is the Meret Market, which has its own packets.
- CharacterAbility 0x00DF (the character ability window): no handler; the server stores no learned abilities, ability points or reset time.
- Microgame 0x0114 (a rock-paper-scissors game between two players): no handler and no game state on the server.
- DynamicBanword 0x0124 (the client sends its ban word list version when it enters the game): the handler only logs the request. The server's ban words are the client's own static list, so there is no newer list to send.
- ChangeAttributes 0x00C8 kind 8 (a GM-level client asks for the cost again): it has a writer and nothing sends it. ChangeAttributes answers with kinds 1, 2, 4 (the cost of the staged item), 5 (error) and 6 (the pending change when the window opens). ItemExchange 0x0102 kind 0 is sent when a target item is put into the exchange scroll window and lists the scroll's ingredients (the same list for every target); kind 2 is the result and kind 3 answers the GM fill buttons.
- DebugMode 0x0087: the client reads it in three debug windows: the NPC control window (kind 5 state text, 6 skill list, 7 open the AI debug window, 8 AI line), the debug guide window (kind 7 command lines) and the dialog center window (kind 10 trigger debug line). The server sends kinds 5 and 6 of the NPC control window and kind 7 of the debug guide window, to GM accounts only, as answers to SetDebugMode 0x50 (see GM-only packets). The AI debug kinds 7 and 8 and the trigger kind 10 are not sent. Kind 10 (string trigger file path, int line; asked for with SetDebugMode mode 9: path, bool on or off, uidialogcenterdialog.cpp:285, 411) needs the 1-based line of the client's own trigger XML file for each step; the server keeps only a merged and rewritten copy of that XML without the original lines, so it has no correct line to send.

The checker follows the client's item reader (CItem::ReadFromStream, commonfiles/item.cpp:2655). It picks the item's optional blocks from the item tables, which it reads at run time. Where a packet does not carry the item id, the checker reads a plain item.

## Two local changes a server operator must know

1. **All servers bind to loopback (127.0.0.1) by default.** To serve other machines, set `MS2_BIND_IP` to the address to listen on (`0.0.0.0` for all interfaces, as upstream); the server has no hardening for the open internet beyond upstream's.
2. **Test hooks.** Two environment variables turn on test hooks. Neither does anything unless it is set.
   - `TST_CMD_FILE`: once a second, the game server reads a file named after this prefix and the character id, and runs each line as a chat command **as the logged-in player**. Never set it on a server that others can reach.
   - `TST_PKT_DIR`: the server appends every packet that clients send, including chat, to a log file per character in this directory.

## Errata in commit subjects

Published commits are not rewritten, so wrong numbers in their subjects are listed here.

- `6fea6fcf` and `fce39da6` say "Guild 0x0054". The opcode is `RecvOp.Guild = 0x004B`.
- `e319062d` says "RequestPet 0x00C2". The opcode is `RecvOp.RequestPet = 0x0078`.

## Licence

AGPL-3.0, as upstream. See LICENSE.
