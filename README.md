# Maple2 for the KMS2 client, build 2525

This is a fork of [Maple2](https://github.com/MS2Community/Maple2), the MapleStory2 server emulator in C#, for the
Korean MapleStory2 client, build 2525. Its branch is `kms2525`. Upstream's own README text is kept below, under
[Upstream](#upstream).

The server assumes its players run the restored client: [MS2Recomp](https://github.com/riyukuro/MS2Recomp), branch
[`restore`](https://github.com/riyukuro/MS2Recomp/tree/restore), a recompiled KMS2 client with features that the KMS2
build lacks or cannot reach, such as Mushking Royale (Maple Survival) and the Crimson Tear Mine arena row. A stock KR
client of build 2525 works too. Set `MS2_SURVIVAL_CLIENT=0` for it: the server then stops sending the survival lobby,
season and queue state that only the restored client reads. Everything outside Mushking Royale is the same for both
clients on the server side; the stock client only lacks the restored windows (see
[Maple Survival](FORK.md#maple-survival-mushking-royale)).

[FORK.md](FORK.md) is the full record: every change, by system, with the client function it follows.

## Goals

- **Every feature is on by default.** An operator turns a feature off through their own client's
  `table/feature.xml` before the ingest. The servers have no runtime switch for it.
- **Money, items and rewards are checked on the server.** A request that would give mesos, merets, items, exp or a
  reward is checked against the data and the player's state, whatever the client claims.
- **Movement, reach, timing and hits are never stricter than the client.** Where the server cannot know the client's
  state exactly (a death or a stance that has not reached the client yet, lag), it lets the request through.
  A refusal is quiet: no error box that the client would not show itself.
- **The client source is the reference.** Each fix names the client function and file it follows. Upstream and other
  servers are not used as the reference.

## Quick start

You need the .NET 8 SDK, MySQL 8 and the `dotnet-ef` tool (`dotnet tool install --global dotnet-ef`). Then:

1. **Data.** Take the data pack [MapleStory2-XML v1.3.2](https://github.com/MS2Community/MapleStory2-XML/releases/tag/v1.3.2),
   not `releases/latest`. Copy a client's `Data` folder and put the pack's four files (`Server.m2d`, `Server.m2h`,
   `Xml.m2d`, `Xml.m2h`) into the copy. Check them against the sha256 list in
   [Data and ingest](FORK.md#data-and-ingest).
2. **Settings.** Copy `.env.example` to `.env` and set `MS2_DATA_FOLDER` to that copy, the database settings
   (`DB_IP`, `DB_PORT`, `DB_USER`, `DB_PASSWORD`, `DATA_DB_NAME`, `GAME_DB_NAME`) and `LOCALE` to the region of the
   client you play (KR when unset). The ingest and the servers must use the same `LOCALE`.
3. **Ingest.** Run `Maple2.File.Ingest`. It fills the metadata database and runs `dotnet ef database update` in
   `Maple2.Server.World` for the game database.
4. **Start** the World server first, then Login, Web and Game (`start.bat` on Windows starts all four).

After you pull an update:

- **Ingest commits.** After a commit that touches `Maple2.File.Ingest` (FORK marks such changes "needs a re-ingest"),
  run the ingest again before the servers start. The servers stop at start when the metadata database is older than they are.
- **Migrations.** A commit that adds a migration needs `dotnet ef database update` in `Maple2.Server.World`; see
  [Database migrations](FORK.md#database-migrations).

The servers listen on loopback (127.0.0.1) only, unlike upstream. To serve other machines, set `MS2_BIND_IP`
(`0.0.0.0` for all interfaces) together with upstream's `GAME_IP` and `LOGIN_IP`. The server has no hardening for the
open internet beyond upstream's. With `docker compose`, the servers bind all interfaces inside their containers (so they
reach each other), and `MS2_BIND_IP` in `.env` is the host address their ports are published on, loopback when unset.

## Operator settings

These are the environment variables this fork adds. Upstream's (database, addresses, `LANGUAGE`, `WEB_PORT`,
`INSTANCED_CONTENT`) are in `.env.example`. Every setting below is optional, and leaving it unset gives the default.

Ingest (`Maple2.File.Ingest`):

| Variable | Default | What it does |
|---|---|---|
| `LOCALE` | `KR` | The client's region (KR, NA, CN, JP, TH, TW). The ingest keeps the rows whose feature is on for it, and the servers read it too. Any other value stops the ingest and the servers. See [Data and ingest](FORK.md#data-and-ingest). |
| `MS2_PLAYED_CLIENT_XML` | unset | The `Xml.m2d` of the client you play, when it is another version than the data pack. The client-side rows are taken from it, and maps, npcs, items and missions that client lacks are left out. See [Data and ingest](FORK.md#data-and-ingest). |
| `MS2_PLAYED_CLIENT_OVERLAY` | unset | One or more directories (separated by `;`) of loose files laid out as in the played client's `Xml.m2d` (`table/na/...`, `itemdata/...`). They are read over that pack the way the client reads loose `Data/Xml` files over it. A file the pack has is replaced, a new one is added, and a later directory wins. Use it for files you add to your client, such as the survival tables below. A missing directory stops the ingest. |
| `MS2_ORIGINAL_DROP_TABLE` | `0` | `1` reads the server pack's original (KMS) monster and box drop table instead of today's. Any value other than 0/1/true/false stops the ingest. |
| `MS2_SERVER_OVERLAY` | unset | A directory of server pack files laid out as in `Server.m2d`. Its `WorldChampionBase.xml` and `table/Server/NA/GameEvent.xml` (rows by `gameEventID`) are read over the pack's, so you can run ended seasons and events again with new dates. See "World champion seasons and trophies" in [Fixes so far](FORK.md#fixes-so-far-by-system). |
| `MS2_FEATURE_ENV` | `Live` | The `feature_setting.xml` server type the features are read at: Dev, Qa, DevStage, Stage or Live. An unknown value stops the ingest. |
| `MS2_REPLAY_ATTEND_GIFT` | `0` | `1` replays one ended event of each type that the data pack has only ended rows of, moved to start on the ingest day with its own length: attendance gifts, the stamp card, sales, hot times, event arcades, the minigames, the item box, the snowman and others. See "Ended attendance event replay" in [Known not to work](FORK.md#known-not-to-work). |
| `MS2_REPLAY_EVENTS` | unset | Ended game event ids, comma separated, to run again from the ingest day with their own length, e.g. `320002` for the epic quest restart or the Mapleopoly boards `270001` to `270005`. An event the played client lacks an item or quest for is not replayed, with a line. A value that is not a list of ids stops the ingest. See "Ended event replay by id" in [Known not to work](FORK.md#known-not-to-work). |

Servers:

| Variable | Read by | Default | What it does |
|---|---|---|---|
| `MS2_BIND_IP` | all | loopback | The address every listener binds to. See [Two local changes](FORK.md#two-local-changes-a-server-operator-must-know). |
| `MS2_SURVIVAL_CLIENT` | Game | on | `0` for stock KR clients: the survival lobby, season, queue state and survival game events are not sent. See [Maple Survival](FORK.md#maple-survival-mushking-royale). |
| `MS2_SURVIVAL_PLAYERS` | Game | `20` | The players a Mushking Royale match takes, 1 to 20. A lower value lets a match start with fewer players. |
| `MS2_CAST_CHECKS` | Game | log only | `enforce` makes the timing checks on casts and hits (motion, cooldown, hits per attack, held-skill timing) refuse. Otherwise they only log what they would refuse. The other cast checks always refuse. |
| `MS2_CONTENT_SHUTDOWN` | Game | none | Content ids (1 to 116, comma separated, decimal or `0x` hex) that the client turns off. It changes only the client's windows: the server does not refuse a shut content's requests. |
| `PCBANG_IP_RANGES` | Game | none | CIDR networks (comma separated) whose players get the PC bang bonus. |
| `CHANNEL_CAPACITY` | all | `200` | Players per channel. It sets the server list's traffic bar and when a login is refused as a full server (GM accounts pass). |
| `MAINTENANCE_MESSAGE` | World | unset | Turns maintenance on at start with this text. Logins are refused with it (GM accounts pass). The GM command `maintenance` changes it while the server runs. |
| `NOTICE_BANNER`, `NOTICE_LOGIN` | World | off | `1` turns on the notice banner at field entry and the login notice at start. The GM command `noticedialog` changes them while the server runs. See "Notice config" in [Fixes so far](FORK.md#fixes-so-far-by-system). |
| `MAIL_ORPHANED_DOLL_ITEMS` | World | off | `1` mails the items of fitting dolls whose cube is gone back to their owners at world start. Unset, they are only logged. |
| `GRPC_LOGIN_PORT`, `GAME_PORT_BASE`, `GRPC_CHANNEL_PORT_BASE` | all | 21000, 20002, 21002 | Ports that upstream fixes in the code. |
| `TST_CMD_FILE`, `TST_PKT_DIR` | Game; all | unset | **Test hooks. Never set them on a server others can reach.** `TST_CMD_FILE` runs chat commands from a file as the logged-in player. `TST_PKT_DIR` logs every packet the clients send, chat included. See [Two local changes](FORK.md#two-local-changes-a-server-operator-must-know). |

## Client data

Nothing from either game is in this repository or in MS2Recomp: bring your own KMS2 client data, and for Mushking
Royale a global (GMS) client's data too.

The restored client's data is built with `m2d patch` from MS2Recomp's
[`restore`](https://github.com/riyukuro/MS2Recomp/tree/restore) branch (`tools/m2d`, needs the .NET 8 SDK). It
applies the patch files in `tools/restore/patches/` to your own copies of the client packs:

- `survival-items`: the Mushking Royale skins and the pass and level reward items the KMS2 pack lacks, copied by id
  from the global client, with the skin table and the survival pass buffs.
- `string-names`: achievement names and NPC titles the KMS2 Korean table lacks.
- `daily-mission` (optional): the Daily Missions menu entries and window texts, for a server that turns the
  `DailyMission` feature on in its own `feature.xml`.

A patch holds no game data: it copies rows from your packs and stops without writing anything when your pack is not
the one it was made for. The restore branch's README ("Client data you supply") has the command, what each patch
changes, and the survival tables that you still put in as loose files yourself.

`m2d patch` writes loose files under its `--out` directory (`Xml/...`), which can be the client's own `Data`
directory, or with `--repack` patched copies of `Xml.m2d` and `Xml.m2h`. The ingest must read the same data the
client reads:

- Loose files: set `MS2_PLAYED_CLIENT_OVERLAY` to the `Xml` directory under `--out` (the client's `Data/Xml`), so
  the ingest reads them over the played pack as the client does.
- A repacked pack: set `MS2_PLAYED_CLIENT_XML` to the patched `Xml.m2d`.

The ingest reads the survival season, level and pass tables from the data pack's `table/na` (v1.3.2 has them), and
the skin table, the items and the names from the played pack with these files over it.

## Status

- **Works:** most of the game the KMS2 client reaches, in the 860 or so entries of
  [Fixes so far, by system](FORK.md#fixes-so-far-by-system): packet layouts checked against the client, combat and
  skills, items and enchanting, quests and events, guilds, housing, dungeons, PvP and the arenas, rankings, Mushking
  Royale with the restored client.
- **Partly:** what is listed with its reason under [Known not to work](FORK.md#known-not-to-work), and the "Not built"
  lines of [Maple Survival](FORK.md#maple-survival-mushking-royale) (squad-only matches, re-invite, the season window
  and rankings, revival of squad members). Some answers are chosen here because neither the client nor the data
  settles them; FORK says "Chosen here" for each.
- **Not built:** 22 sub-types the client sends have no handler yet; [Known not to work](FORK.md#known-not-to-work)
  lists them per system.

Most changes have been checked by tests only, not in a live game.

## How changes are made

The changes in this fork are written by AI agents. Before a change is published it passes automatic checks (commit
author, message and leak scans), an AI review of its diff, a Release build of the servers and the full test suite
with the KR and NA locales. No person reviews the code, and most changes have not been tried in a live game. Where a
change was tried live, its commit says so.

## Links

- [FORK.md](FORK.md): every change, by system
- [Database migrations](FORK.md#database-migrations)
- [GM level](FORK.md#gm-level) and [GM-only packets](FORK.md#gm-only-packets)
- [Errata in commit subjects](FORK.md#errata-in-commit-subjects)
- Licence: AGPL-3.0, as upstream ([LICENSE](LICENSE))

## Upstream

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/MS2Community/Maple2)

# MapleStory2 Server Emulator

This is an open source MapleStory2 server emulation project created in C#. It is being developed completely for educational purposes only. This software is being provided "as-is", completely for free. As a result, we, nor anybody who chooses to contribute, are not responsible for any damages or other liability as a result of this software.

Setup Guide: [https://github.com/MS2Community/Maple2/wiki/Prerequisites](https://github.com/MS2Community/Maple2/wiki/Prerequisites)

Join the [community discord](https://discord.gg/r78CXkUmuj)! - Updated as of 9/28/2024
