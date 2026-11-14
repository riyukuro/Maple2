using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// ItemPet.HasItems is not stored: GameStorage reads it from the pet's bag rows (owned by the pet's uid) wherever a
// pet item loads, so a pet with a full bag is refused as destroy, dismantle, sell, evolve or fusion fodder however
// it came back (inventory, account storage, a mail).
public class PetBagStorageTests {
    private const int ItemId = 20000101;
    private const int PetItemId = 61000001;
    private const long Character = 1001;
    private const long Account = 2001;
    private const long Mail = 3001;
    private GameStorage storage = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        storage = new GameStorage(options, Storage(Metadata(ItemId), Metadata(PetItemId)), null!, null!, null!, null!, null!,
            NullLogger<GameStorage>.Instance, null!);
    }

    // As MoveItemStorageTests: the item data goes in the metadata storage's cache, where TryGet looks first.
    private static ItemMetadataStorage Storage(params ItemMetadata[] items) {
        var itemStorage = (ItemMetadataStorage) RuntimeHelpers.GetUninitializedObject(typeof(ItemMetadataStorage));
        var cache = new LRUCache<int, ItemMetadata>(16, 1);
        foreach (ItemMetadata item in items) {
            cache.AddReplace(item.Id, item);
        }
        typeof(MetadataStorage<int, ItemMetadata>).GetField("Cache", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(itemStorage, cache);
        return itemStorage;
    }

    private static ItemMetadata Metadata(int id) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
    }

    private Item CreatePet(long ownerId, bool bagItem) {
        Item pet;
        using (GameStorage.Request db = storage.Context()) {
            pet = db.CreateItem(ownerId, new Item(Metadata(PetItemId)) { Pet = new ItemPet() })!;
        }
        if (bagItem) {
            CreateBagItem(pet.Uid);
        }
        return pet;
    }

    private void CreateBagItem(long petUid) {
        using GameStorage.Request db = storage.Context();
        Assert.That(db.CreateItem(petUid, new Item(Metadata(ItemId))), Is.Not.Null);
    }

    [Test]
    public void OwnersWithItems_GivesTheOwnersThatHoldABagRow() {
        Item full = CreatePet(Character, bagItem: true);
        Item empty = CreatePet(Character, bagItem: false);
        Item hair;
        using (GameStorage.Request db = storage.Context()) {
            hair = db.CreateItem(Character, new Item(Metadata(ItemId)))!;
        }
        // A saved-hair row is not a bag row.
        hair.Group = ItemGroup.SavedHair;
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveItems(empty.Uid, hair), Is.True);
        }

        using GameStorage.Request check = storage.Context();
        Assert.That(check.OwnersWithItems([full.Uid, empty.Uid, 999999]), Is.EquivalentTo(new[] { full.Uid }));
        Assert.That(check.OwnersWithItems(new List<long>()), Is.Empty);
    }

    [Test]
    public void AccountStorage_PetWithBagItems_LoadsWithHasItems() {
        Item full = CreatePet(Account, bagItem: true);
        Item empty = CreatePet(Account, bagItem: false);

        using GameStorage.Request db = storage.Context();
        Dictionary<long, Item> loaded = db.GetStorage(Account).ToDictionary(item => item.Uid);
        Assert.That(loaded[full.Uid].Pet!.HasItems, Is.True);
        Assert.That(loaded[empty.Uid].Pet!.HasItems, Is.False);
    }

    [Test]
    public void InventoryMailAndSingleLoad_PetWithBagItems_LoadsWithHasItems() {
        Item inInventory = CreatePet(Character, bagItem: true);
        Item inMail = CreatePet(Mail, bagItem: true);

        using GameStorage.Request db = storage.Context();
        Assert.That(db.GetInventory(Character).Values.SelectMany(items => items).Single(item => item.Uid == inInventory.Uid).Pet!.HasItems, Is.True);
        Assert.That(db.GetAllItems(Mail).Single(item => item.Uid == inMail.Uid).Pet!.HasItems, Is.True);
        Assert.That(db.GetItem(inMail.Uid)!.Pet!.HasItems, Is.True);
    }

    [Test]
    public void ItemThatIsNotAPet_IsLeftAlone() {
        Item item;
        using (GameStorage.Request db = storage.Context()) {
            item = db.CreateItem(Account, new Item(Metadata(ItemId)))!;
        }
        CreateBagItem(item.Uid);

        using GameStorage.Request check = storage.Context();
        Assert.That(check.GetStorage(Account).Single().Pet, Is.Null);
    }
}
