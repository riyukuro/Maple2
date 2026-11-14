using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A beauty shop's type comes from its npc's BeautyShopDialog script function ("kind,mode", as the client derives the
// dialog type: GetDialogType, uitalkcinematic.cpp:1998), not from the byte the client sends with the open (Beauty 0).
// The KR npcs and shops: 11000255 "hair,style" shop 504, 11001513 "hair,random" shop 508, 11000253 "hair,styleSave"
// shop 510, 11000072 "skin" shop 501, 11000257 "itemcolor" shop 506.
public class BeautyShopTypeTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;

    private static readonly BeautyShopCostMetadata Free = new(ShopCurrencyType.Meso, 0, string.Empty, 0);

    private static BeautyShopMetadata Shop(int id, BeautyShopCategory category, bool random = false) =>
        new(id, category, 0, Free, Free, random, false, 0, 0, ItemTag.None, [], []);

    private static ScriptFunctionMetadata Function(int npcId, string uiArg) => new(npcId, 1, ScriptType.Npc, 1, true, 99,
        "BeautyShopDialog", uiArg, "", 0, 0, "", "", [], [], 0, "", "", "", 0, 0, 0, 0, false);

    private static NpcMetadata Npc(int id, int shopId) {
        var basic = new NpcMetadataBasic(1, 0, 0, 0, shopId, 0, 0, 1, 1, false, 0, 0, 0, 0, [], [], 0);
        return new NpcMetadata(id, "npc", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
    }

    // The npc table without a database: the npcs are in its cache.
    private static NpcMetadataStorage NpcTable(params NpcMetadata[] npcs) {
        var storage = (NpcMetadataStorage) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadataStorage));
        var cache = new LRUCache<int, NpcMetadata>(100, 5);
        foreach (NpcMetadata npc in npcs) {
            cache.AddReplace(npc.Id, npc);
        }
        typeof(MetadataStorage<int, NpcMetadata>).GetField("Cache", Any)!.SetValue(storage, cache);
        return storage;
    }

    // Beauty 0 from npc 1000 of shop 600: the npc id, then the type byte.
    private BeautyShopType? Open(BeautyShopMetadata shop, string? uiArg, BeautyShopType sent) {
        const int npcId = 1000;
        test = new TestGameSession();
        test.SetServerTable(new BeautyShopTable(new Dictionary<int, BeautyShopMetadata> { [shop.Id] = shop }));
        var functions = new Dictionary<int, Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>>();
        if (uiArg != null) {
            functions[npcId] = new() { [1] = new() { [1] = Function(npcId, uiArg) } };
        }
        test.SetServerTable(new ScriptFunctionTable(functions));
        GameSession session = test.Start();
        session.Beauty = new BeautyManager(session);
        test.TakeSent();

        new BeautyHandler {
            ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata, NpcMetadata = NpcTable(Npc(npcId, shop.Id)),
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteInt(npcId);
            writer.WriteByte((byte) sent);
        }));
        return session.BeautyShop?.Type;
    }

    // Shop 504 opened as a style save shop (whose recolors were free) is still a style shop; shop 508 opened as a
    // style shop is still a random shop.
    [TestCase(504, BeautyShopCategory.Hair, false, "hair,style", BeautyShopType.Save, BeautyShopType.Default)]
    [TestCase(508, BeautyShopCategory.Hair, true, "hair,random", BeautyShopType.Default, BeautyShopType.Random)]
    [TestCase(510, BeautyShopCategory.Hair, false, "hair,styleSave", BeautyShopType.Default, BeautyShopType.Save)]
    [TestCase(501, BeautyShopCategory.Skin, false, "skin", BeautyShopType.Default, BeautyShopType.Modify)]
    [TestCase(506, BeautyShopCategory.Dye, false, "itemcolor", BeautyShopType.Save, BeautyShopType.Modify)]
    [TestCase(505, BeautyShopCategory.Makeup, false, "makeup", BeautyShopType.Modify, BeautyShopType.Default)]
    public void TheTypeIsTheNpcsNotTheSentByte(int id, BeautyShopCategory category, bool random, string uiArg,
                                               BeautyShopType sent, BeautyShopType type) {
        Assert.That(Open(Shop(id, category, random), uiArg, sent), Is.EqualTo(type));
    }

    // Chosen here: an npc with no beauty dialog function takes its shop row's type.
    [TestCase(BeautyShopCategory.Hair, true, BeautyShopType.Save, BeautyShopType.Random)]
    [TestCase(BeautyShopCategory.Mirror, false, BeautyShopType.Save, BeautyShopType.Modify)]
    [TestCase(BeautyShopCategory.Face, false, BeautyShopType.Save, BeautyShopType.Default)]
    public void WithoutAFunctionTheRowsType(BeautyShopCategory category, bool random, BeautyShopType sent, BeautyShopType type) {
        Assert.That(Open(Shop(600, category, random), null, sent), Is.EqualTo(type));
    }
}
