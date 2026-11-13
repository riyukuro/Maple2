using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class FishingManager {
    private readonly GameSession session;
    private IDictionary<int, FishEntry> FishAlbum => session.Player.Value.Unlock.FishAlbum;
    private readonly TableMetadataStorage tableMetadata;
    private readonly ServerTableMetadataStorage serverTableMetadata;
    private FieldGuideObject? GuideObject {
        get => session.GuideObject;
        set => session.GuideObject = value;
    }
    private Item? rod;
    private bool fishFightGame;
    private FishTable.Fish? selectedFish;
    private FishingTile? selectedTile;
    // The cast in progress: whether its fish bites, and the field tick of the bite.
    private bool bite;
    private long biteTick;
    // The lure buffs held at the cast: the catch's size and lure boxes are theirs, though a buff ends during the wait.
    private IReadOnlyList<FishTable.Lure> castLures = [];
    private IDictionary<Vector3, FishingTile> tiles = new Dictionary<Vector3, FishingTile>();

    private ConstantsTable Constants => serverTableMetadata.ConstantsTable;

    private readonly ILogger logger = Log.ForContext<FishingManager>();

    public FishingManager(GameSession session, TableMetadataStorage tableMetadata, ServerTableMetadataStorage serverTableMetadata) {
        this.session = session;
        this.tableMetadata = tableMetadata;
        this.serverTableMetadata = serverTableMetadata;
    }

    // The buffs just removed held an AutoFish buff: see FishingRules.AutoFishEndNotice.
    public void AutoFishRemoved(bool stillAutoFish) {
        if (FishingRules.AutoFishEndNotice(true, stillAutoFish, GuideObject != null)) {
            session.Send(FishingPacket.Auto(false));
        }
    }

    public void Reset() {
        if (GuideObject == null) {
            return;
        }
        session.Send(FishingPacket.Stop());
        session.Field?.Broadcast(GuideObjectPacket.Remove(GuideObject));
        // The player's own client leaves the state by itself; the others are told.
        session.Field?.Broadcast(StateFishingPacket.State(session.Player.ObjectId, StateFishingPacket.STATE_IDLE), session);
        GuideObject = null;
        LeaveField();
    }

    // A field change ends the fishing (GameSession.LeaveField): the client leaves its fishing state on the move and
    // drops the guide with the player, so the rod, the tiles and a cast in progress must not carry into the next
    // field, where a catch without a new start would land the old cast's fish.
    public void LeaveField() {
        rod = null;
        tiles.Clear();
        EndCast();
    }

    // Whether a cast is open: started (mode 9) and not yet ended by a catch, a lost reel game or a leave.
    internal bool Casting => selectedTile != null || selectedFish != null;

    internal void Cast(FishingTile tile, FishTable.Fish fish, bool fishBites, long fishBiteTick, bool fightGame, IReadOnlyList<FishTable.Lure>? lures = null) {
        castLures = lures ?? [];
        selectedTile = tile;
        selectedFish = fish;
        bite = fishBites;
        biteTick = fishBiteTick;
        fishFightGame = fightGame;
    }

    private void EndCast() {
        selectedTile = null;
        selectedFish = null;
        bite = false;
        biteTick = 0;
        fishFightGame = false;
        castLures = [];
    }

    public FishingError Prepare(long fishingRodUid) {
        // The client does not ask from inside a house: it shows this refusal by itself (CFishingManager, the
        // check before Fishing 0).
        if (session.Field is HomeFieldManager) {
            return FishingError.s_fishing_error_ugcmap;
        }
        if (session.GuideObject != null || session.Field?.AccelerationStructure == null) {
            return FishingError.s_fishing_error_system_error;
        }

        Item? rodItem = session.Item.Inventory.Get(fishingRodUid);
        if (rodItem == null || rodItem.Metadata.Function?.Type != ItemFunction.FishingRod) {
            return FishingError.s_fishing_error_invalid_item;
        }
        if (!int.TryParse(rodItem.Metadata.Function?.Parameters, out int rodCode) || !tableMetadata.FishingRodTable.Entries.TryGetValue(rodCode, out FishingRodTable.Entry? rodMetadata)) {
            return FishingError.s_fishing_error_invalid_item;
        }
        if (session.Mastery[MasteryType.Fishing] < rodMetadata.MinMastery) {
            return FishingError.s_fishing_error_fishingrod_mastery;
        }
        if (!serverTableMetadata.FishTable.Spots.TryGetValue(session.Field.MapId, out FishTable.Spot? spotMetadata)) {
            return FishingError.s_fishing_error_notexist_fish;
        }
        if (session.Mastery[MasteryType.Fishing] < spotMetadata.MinMastery) {
            return FishingError.s_fishing_error_lack_mastery;
        }

        tiles = GetFishingBlocks(rodMetadata);
        if (tiles.Count == 0) {
            return FishingError.s_fishing_error_notexist_fish;
        }
        rod = rodItem;
        // get the fishing block closest to player
        Vector3 guidePosition = tiles.Keys.MinBy(block =>
            Vector2.Distance(new Vector2(session.Player.Position.X, session.Player.Position.Y), new Vector2(block.X, block.Y)));
        // Move guide 1 block up so it sits on top of the fishing block
        guidePosition.Z += Constant.BlockSize;
        var guide = new FishingGuideObject(rodMetadata, spotMetadata);
        session.GuideObject = session.Field.SpawnGuideObject(session.Player, guide, guidePosition);

        session.Send(FishingPacket.LoadTiles(tiles.Values));
        session.Field.Broadcast(GuideObjectPacket.Create(session.GuideObject));
        session.Send(FishingPacket.Prepare(fishingRodUid));
        // The player's own client enters the fishing state on the packet above; the others are told here.
        session.Field.Broadcast(StateFishingPacket.State(session.Player.ObjectId, StateFishingPacket.STATE_FISHING), session);
        return FishingError.none;
    }

    private Dictionary<Vector3, FishingTile> GetFishingBlocks(FishingRodTable.Entry rod) {
        Dictionary<Vector3, FishingTile> tiles = new();
        if (session.Field?.AccelerationStructure == null) {
            return tiles;
        }
        Vector3 playerPosition = session.Player.Position;
        float direction = session.Player.Rotation.AlignRotation().Z;

        Vector3 position1 = Vector3.Zero;
        Vector3 position2 = Vector3.Zero;

        switch (direction) {
            case Constant.NorthEast:
                position1 = new Vector3(
                    playerPosition.X + (3 * Constant.BlockSize),
                    playerPosition.Y + (2 * Constant.BlockSize),
                    playerPosition.Z - (int) (Constant.BlockSize / 2)
                );
                position2 = new Vector3(
                    playerPosition.X + (1 * Constant.BlockSize),
                    playerPosition.Y - (2 * Constant.BlockSize),
                    playerPosition.Z - (3 * Constant.BlockSize)
                );
                break;
            case Constant.NorthWest:
                position1 = new Vector3(
                    playerPosition.X - (2 * Constant.BlockSize),
                    playerPosition.Y + (3 * Constant.BlockSize),
                    playerPosition.Z - (int) (Constant.BlockSize / 2)
                );
                position2 = new Vector3(
                    playerPosition.X + (2 * Constant.BlockSize),
                    playerPosition.Y + (1 * Constant.BlockSize),
                    playerPosition.Z - (3 * Constant.BlockSize)
                );
                break;
            case Constant.SouthWest:
                position1 = new Vector3(
                    playerPosition.X - (3 * Constant.BlockSize),
                    playerPosition.Y - (2 * Constant.BlockSize),
                    playerPosition.Z - (int) (Constant.BlockSize / 2)
                );
                position2 = new Vector3(
                    playerPosition.X - (1 * Constant.BlockSize),
                    playerPosition.Y + (2 * Constant.BlockSize),
                    playerPosition.Z - (3 * Constant.BlockSize)
                );
                break;
            case Constant.SouthEast:
                position1 = new Vector3(
                    playerPosition.X + (2 * Constant.BlockSize),
                    playerPosition.Y - (3 * Constant.BlockSize),
                    playerPosition.Z - (int) (Constant.BlockSize / 2)
                );
                position2 = new Vector3(
                    playerPosition.X - (2 * Constant.BlockSize),
                    playerPosition.Y - (1 * Constant.BlockSize),
                    playerPosition.Z - (3 * Constant.BlockSize)
                );
                break;
        }

        session.Field.AccelerationStructure.QueryFluids(
            new Vector3(
                Math.Min(position1.X, position2.X),
                Math.Min(position1.Y, position2.Y),
                Math.Min(position1.Z, position2.Z)),
            new Vector3(
                Math.Max(position1.X, position2.X),
                Math.Max(position1.Y, position2.Y),
                Math.Max(position1.Z, position2.Z)),
            (tile) => tiles.Add(tile.Position, new FishingTile(tile, rod.ReduceTime)));
        return tiles;
    }

    // The lures among the enabled buffs: a fishLure row for the buff's id and level.
    internal static List<FishTable.Lure> HeldLures(FishTable table, IEnumerable<(int Id, short Level)> held) {
        var lures = new List<FishTable.Lure>();
        foreach ((int id, short level) in held) {
            if (table.Lures.TryGetValue(id, out IReadOnlyDictionary<short, FishTable.Lure>? levels) && levels.TryGetValue(level, out FishTable.Lure? lure)) {
                lures.Add(lure);
            }
        }
        return lures;
    }

    private List<FishTable.Lure> HeldLures() {
        return HeldLures(serverTableMetadata.FishTable, session.Player.Buffs.EnumerateBuffs()
            .Where(buff => buff.Enabled)
            .Select(buff => (buff.Id, buff.Level)));
    }

    // Whether the fish lives in the liquid: habitat all (the bait fish) lives in any.
    internal static bool LivesIn(FishTable.Fish fish, LiquidType liquidType) {
        return fish.FluidHabitat == LiquidType.all || fish.FluidHabitat == liquidType;
    }

    internal static bool AtSpot(FishTable.Fish fish, FishTable.Spot spot) {
        return fish.IgnoreSpotMastery || (spot.MinMastery <= fish.Mastery && fish.Mastery <= spot.MaxMastery);
    }

    // The fish a lure draws out (spawnFish, spawnFishRate): one roll out of 1000 over the held lures' rates in turn
    // (each KR list adds up to 500 or less; the scale is inferred). A miss, or a fish not in the table, not living in
    // the liquid or not at the spot, leaves the cast to the spot's boxes.
    internal static FishTable.Fish? LureFish(FishTable table, IEnumerable<FishTable.Lure> lures, FishTable.Spot spot, LiquidType liquidType, int roll) {
        foreach (FishTable.Lure lure in lures) {
            foreach (FishTable.Lure.Spawn spawn in lure.SpawnFishes) {
                roll -= spawn.Rate;
                if (roll >= 0) {
                    continue;
                }
                if (table.Fishes.TryGetValue(spawn.FishId, out FishTable.Fish? fish) && LivesIn(fish, liquidType) && AtSpot(fish, spot)) {
                    return fish;
                }
                return null;
            }
        }
        return null;
    }

    // A box weight under the held lures' spawnRank/spawnProp: each rate for the fish's rank scales it by rate/10000
    // (90000266: ranks 3 and 4 at 100000, ten times; the scale is inferred).
    internal static int LureWeight(int weight, FishTable.Fish fish, IEnumerable<FishTable.Lure> lures) {
        long scaled = weight;
        foreach (FishTable.Lure.RankRate spawn in lures.SelectMany(lure => lure.Spawns)) {
            if (spawn.Rank == 0 || spawn.Rank == fish.Rarity) {
                scaled = scaled * spawn.Rate / 10000;
            }
        }
        return (int) Math.Clamp(scaled, 0, int.MaxValue);
    }

    // The bite chance out of 10000: the fish's baitProp plus the held lures' catchProp for its rank. The fish's own
    // catchProp is 10000 on every KR row, so the lure's raise can only be on the bite.
    internal static int BiteChance(FishTable.Fish fish, IEnumerable<FishTable.Lure> lures) {
        int chance = fish.BaitProbability;
        foreach (FishTable.Lure.RankRate rate in lures.SelectMany(lure => lure.Catches)) {
            if (rate.Rank == 0 || rate.Rank == fish.Rarity) {
                chance += rate.Rate;
            }
        }
        return Math.Min(chance, 10000);
    }

    // A size lure (fishSize, fishSizeProp out of 10000) puts the catch in that size class. The classes are the
    // client's: 0 below the small size's max, 1 below the big size's min, 2 (gold medal) below the big size's max
    // and 3 (prize) from it; the prize range is the base roll's. One roll counts for every held size lure in turn.
    internal static int? LureSize(FishTable.Fish fish, IEnumerable<FishTable.Lure> lures, int roll, Random random) {
        foreach (FishTable.Lure lure in lures) {
            if (lure.FishSizeProbability <= 0 || roll >= lure.FishSizeProbability) {
                continue;
            }
            switch (lure.FishSizeClass) {
                case 0:
                    return random.Next(fish.SmallSize.Min, fish.SmallSize.Max);
                case 1:
                    return random.Next(fish.SmallSize.Max, fish.BigSize.Min);
                case 2:
                    return random.Next(fish.BigSize.Min, fish.BigSize.Max);
                case 3:
                    return random.Next(fish.BigSize.Max, fish.BigSize.Max * 2);
            }
        }
        return null;
    }

    private WeightedSet<FishTable.Fish> GetFishes(FishTable.Spot spot, LiquidType liquidType, IReadOnlyCollection<FishTable.Lure> lures) {
        if (liquidType == LiquidType.water && spot.LiquidTypes.Contains(LiquidType.seawater)) { // If spot is water and has seawater, it will be seawater
            liquidType = LiquidType.seawater;
        }
        var fishes = new WeightedSet<FishTable.Fish>();

        FishTable.Fish? lureFish = LureFish(serverTableMetadata.FishTable, lures, spot, liquidType, Random.Shared.Next(0, 1000));
        if (lureFish != null) {
            fishes.Add(lureFish, 1);
            return fishes;
        }

        if (serverTableMetadata.FishTable.GlobalFishBoxes.TryGetValue(spot.GlobalFishBoxId, out FishTable.FishBox? globalFishBox)) {
            fishes = CombineBox(fishes, globalFishBox);
        }

        if (serverTableMetadata.FishTable.IndividualFishBoxes.TryGetValue(spot.IndividualFishBoxId, out FishTable.FishBox? individualFishBox)) {
            fishes = CombineBox(fishes, individualFishBox);
        }
        return fishes;

        WeightedSet<FishTable.Fish> CombineBox(WeightedSet<FishTable.Fish> fishes, FishTable.FishBox fishBox) {
            if (fishBox.Probability < Random.Shared.Next(0, 10000)) {
                return fishes;
            }
            foreach ((int fishid, int weight) in fishBox.Fishes) {
                if (!serverTableMetadata.FishTable.Fishes.TryGetValue(fishid, out FishTable.Fish? fish)) {
                    continue;
                }

                if (!LivesIn(fish, liquidType) || !AtSpot(fish, spot)) {
                    continue;
                }

                // A weight of 0 (the bait fish in the KR boxes) is never caught from the box: only its lure draws it.
                int lureWeight = LureWeight(weight, fish, lures);
                if (lureWeight <= 0) {
                    continue;
                }
                fishes.Add(fish, lureWeight);
            }
            return fishes;
        }
    }

    public FishingError Start(Vector3 position) {
        if (session.Field?.AccelerationStructure == null) {
            return FishingError.s_fishing_error_system_error;
        }

        if (GuideObject == null) {
            return FishingError.s_fishing_error_system_error;
        }

        if (rod?.Metadata.Function?.Type != ItemFunction.FishingRod) {
            return FishingError.s_fishing_error_invalid_item;
        }

        if (!int.TryParse(rod.Metadata.Function.Parameters, out int rodCode) || !tableMetadata.FishingRodTable.Entries.TryGetValue(rodCode, out FishingRodTable.Entry? rodMetadata)) {
            return FishingError.s_fishing_error_invalid_item;
        }

        FishingTile? block = tiles.FirstOrDefault(tile => tile.Key == position).Value;
        if (block == null) {
            return FishingError.s_fishing_error_system_error;
        }

        if (!serverTableMetadata.FishTable.Spots.TryGetValue(session.Field.MapId, out FishTable.Spot? spotMetadata)) {
            return FishingError.s_fishing_error_notexist_fish;
        }

        List<FishTable.Lure> lures = HeldLures();
        WeightedSet<FishTable.Fish> fishes = GetFishes(spotMetadata, block.LiquidType, lures);
        if (fishes.Count == 0) {
            return FishingError.s_fishing_error_notexist_fish;
        }

        FishTable.Fish fish = fishes.Get();

        int fishingTick = Constants.fisherBoreDuration;
        bool hasAutoFish = session.Player.Buffs.HasBuff(BuffEventType.AutoFish);

        // Fishing Success
        bool fishBites = Random.Shared.Next(0, 10000) < BiteChance(fish, lures);
        // The reel game belongs to this cast alone: a game won on an earlier cast must not carry over.
        bool fightGame = FishingRules.IsFightGame(fishBites, hasAutoFish, Random.Shared.Next(0, 10000), Constants.fishFightingProp);
        if (fishBites) {
            fishingTick -= rodMetadata.ReduceTime;
            fishingTick = Random.Shared.Next(fishingTick - fishingTick / 3, fishingTick); // Chance for early catch
        } else {
            fishingTick = Random.Shared.Next(fishingTick + 1, fishingTick * 2); // If tick is over bore duration, it will fail
        }

        long fishBiteTick = session.Field.FieldTick + fishingTick;
        Cast(block, fish, fishBites, fishBiteTick, fightGame, lures);
        session.Send(FishingPacket.Start(fishBiteTick.Truncate32(), fightGame));

        return FishingError.none;
    }

    public FishingError Catch(bool success) {
        if (selectedTile == null || selectedFish == null || session.Field is null) {
            return FishingError.s_fishing_error_system_error;
        }

        if (!serverTableMetadata.FishTable.Spots.TryGetValue(session.Field.MapId, out FishTable.Spot? spotMetadata)) { // Should not happen.
            return FishingError.s_fishing_error_notexist_fish;
        }

        // determine fish size: a held size lure first, then the base roll.
        int fishSize = LureSize(selectedFish, castLures, Random.Shared.Next(0, 10000), Random.Shared) ?? Random.Shared.NextDouble() switch {
            >= 0.0 and < 0.025 => Random.Shared.Next(selectedFish.BigSize.Max, selectedFish.BigSize.Max * 2), // prize fish
            >= 0.025 and < 0.03 => Random.Shared.Next(selectedFish.SmallSize.Min, selectedFish.SmallSize.Max),
            >= 0.03 and < 0.15 => Random.Shared.Next(selectedFish.SmallSize.Max, selectedFish.BigSize.Min),
            >= 0.15 => Random.Shared.Next(selectedFish.SmallSize.Min, selectedFish.SmallSize.Max),
            _ => Random.Shared.Next(selectedFish.SmallSize.Min, selectedFish.SmallSize.Max),
        };

        bool hasAutoFish = session.Player.Buffs.HasBuff(BuffEventType.AutoFish);

        // The result byte is the client's: it counts only for a cast that had a bite, once the bite tick has come.
        // A cast without the reel game ends with result 1 alone (CFishingManager::Update); a result 0 before it
        // (the casting bar running out) has no answer in KMS2 and leaves the cast open for the result 1 after it.
        if (FishingRules.IsIgnoredResult(success, fishFightGame)) {
            return FishingError.none;
        }

        // KMS2 order: mastery, the catch, the items, the tile again, then exp and conditions.
        success = FishingRules.IsCatch(success, bite, session.Field.FieldTick, biteTick);
        if (success) {
            Action report = AddFish(fishSize, hasAutoFish);
            CatchItem(spotMetadata, selectedFish);
            session.Send(FishingPacket.LoadTiles([selectedTile], append: true));
            report();
        } else {
            session.Send(FishingPacket.CatchFish(selectedFish.Id, fishSize, hasAutoFish));
            session.Send(FishingPacket.LoadTiles([selectedTile], append: true));
            session.ConditionUpdate(ConditionType.fish_fail, codeLong: selectedFish.Id, targetLong: session.Field.MapId);
        }
        // One catch per cast: the next one needs a new start.
        EndCast();

        return FishingError.none;
    }

    // A catch made while auto fishing is on (the catch packet's auto flag) counts for auto_fishing (23100181, no code).
    internal static void ReportAutoCatch(GameSession session, bool hasAutoFish) {
        if (hasAutoFish) {
            session.ConditionUpdate(ConditionType.auto_fishing);
        }
    }

    // Sends the mastery gain and the catch; the returned action reports the conditions and the exp, which KMS2
    // sends after the caught items and the tile.
    private Action AddFish(int fishSize, bool hasAutoFish) {
        if (selectedFish == null || session.Field is null) {
            return () => { };
        }

        FishTable.Fish fish = selectedFish;
        int mapId = session.Field.MapId;
        bool prizeFish = fishSize >= fish.BigSize.Max;
        int masteryExp = 0;
        var caughtFishType = CaughtFishType.Default;
        bool firstCatch = false;

        if (FishAlbum.TryGetValue(selectedFish.Id, out FishEntry? fishEntry)) {
            fishEntry.TotalCaught++;
            fishEntry.LargestSize = Math.Max(fishEntry.LargestSize, fishSize);

            if (fishEntry.TotalCaught % selectedFish.PointCount == 0) {
                masteryExp = selectedFish.MasteryExp;
            }

            if (prizeFish) {
                fishEntry.TotalPrizeFish++;
            }
        } else {
            fishEntry = new FishEntry(selectedFish.Id) {
                TotalCaught = 1,
                TotalPrizeFish = prizeFish ? 1 : 0,
                LargestSize = fishSize,
            };
            FishAlbum.Add(selectedFish.Id, fishEntry);
            firstCatch = true;
            masteryExp = selectedFish.MasteryExp * 2; // double mastery exp if first catch
            caughtFishType = CaughtFishType.FirstKind;
        }

        if (prizeFish) {
            caughtFishType = CaughtFishType.Prize;
            masteryExp = selectedFish.MasteryExp * 2; // double mastery exp if prize fish
        }

        if (masteryExp != 0) {
            session.Mastery[MasteryType.Fishing] += masteryExp;
            short masteryLevel = session.Mastery.GetLevel(MasteryType.Fishing);
            session.Send(FishingPacket.IncreaseMastery(selectedFish.Id, masteryLevel, masteryExp, caughtFishType));
        }

        session.Send(FishingPacket.CatchFish(selectedFish.Id, fishSize, hasAutoFish, fishEntry));
        if (prizeFish) {
            session.Field.Broadcast(FishingPacket.PrizeFish(session.PlayerName, selectedFish.Id));
        }

        return () => {
            if (firstCatch) {
                session.ConditionUpdate(ConditionType.fish_collect, codeLong: fish.Id, targetLong: mapId);
            }
            if (fishSize >= fish.BigSize.Min) {
                session.ConditionUpdate(ConditionType.fish_goldmedal, codeLong: fish.Id, targetLong: mapId);
            }
            if (prizeFish) {
                session.ConditionUpdate(ConditionType.fish_big, codeLong: fish.Id, targetLong: mapId);
            }
            session.ConditionUpdate(ConditionType.fish, codeLong: fish.Id, targetLong: mapId);
            ReportAutoCatch(session, hasAutoFish);
            // Every catch gives the common exp table's fishing award for the character's level.
            session.Exp.AddExp(ExpType.fishing);
        };
    }

    // The drop boxes a catch opens, in order: the spot's, the fish's own (individualDropBoxID: the bait fish's items),
    // then each held lure's. A lure box with a spot mastery range (globalDropFishingSpotMastery,
    // individualDropFishingSpotMastery) counts at a spot whose mastery band starts in it: 0-1189 is every KR spot,
    // 1000-1000 the top band (1000-1189). The drop ranks are 1 or empty on every KR row and are not read.
    internal static (List<int> Global, List<int> Individual) DropBoxes(FishTable.Spot spot, FishTable.Fish fish, IEnumerable<FishTable.Lure> lures) {
        var global = new List<int>();
        var individual = new List<int>();
        if (spot.GlobalDropBoxId > 0) {
            global.Add(spot.GlobalDropBoxId);
        }
        if (spot.IndividualDropBoxId > 0) {
            individual.Add(spot.IndividualDropBoxId);
        }
        if (fish.IndividualDropBoxId > 0) {
            individual.Add(fish.IndividualDropBoxId);
        }
        foreach (FishTable.Lure lure in lures) {
            if (AtSpot(lure.GlobalDrop, spot)) {
                global.Add(lure.GlobalDrop!.BoxId);
            }
            if (AtSpot(lure.IndividualDrop, spot)) {
                individual.Add(lure.IndividualDrop!.BoxId);
            }
        }
        return (global, individual);

        static bool AtSpot(FishTable.Lure.Drop? drop, FishTable.Spot spot) {
            if (drop == null || drop.BoxId <= 0) {
                return false;
            }
            return drop.SpotMastery is not { } range || (range.Min <= spot.MinMastery && spot.MinMastery <= range.Max);
        }
    }

    private void CatchItem(FishTable.Spot spot, FishTable.Fish fish) {
        if (session.Field is null) return;
        var items = new List<Item>();
        (List<int> globalBoxes, List<int> individualBoxes) = DropBoxes(spot, fish, castLures);
        foreach (int boxId in globalBoxes) {
            items.AddRange(session.Field.ItemDrop.GetGlobalDropItems(boxId, spot.SpotLevel, session));
        }
        foreach (int boxId in individualBoxes) {
            items.AddRange(session.Field.ItemDrop.GetIndividualDropItems(session, session.Player.Value.Character.Level, boxId));
        }

        foreach (Item item in items) {
            if (!session.Item.Inventory.Add(item, true)) {
                session.Item.MailItem(item);
            }
        }

        if (items.Count > 0) {
            session.Send(FishingPacket.CatchItem(items));
        }
    }

    // A lost reel game ends the cast: KMS2 answers with the tile alone, no catch packet.
    public void FailMinigame() {
        if (fishFightGame && selectedTile != null) {
            session.Send(FishingPacket.LoadTiles([selectedTile], append: true));
        }
        EndCast();
    }

}
