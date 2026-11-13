using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// MeretMarket 101 for the home page (section 0): a product with bg 0 (BannerTag None) draws the system banner named
// by its category, and with no such banner a blank grey cell (asitemslot.cpp:2209-2230,
// uimeratmarketdialog.cpp:2920), so it is left off the page. A product with a bg (PinkGift) is sent. The ids are made up.
public class MeretMarketHomeTests {
    private const int Design = 11300001;
    private const int Town = 90000100;
    private const byte HomeTab = 10;
    private const int Blank = 0x5A5A1001;
    private const int Gift = 0x5A5A1002;
    private const string BannerName = "homeproduct_1";

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        test = new TestGameSession(options);
        test.Register(TestGameSession.Metadata(Design, EquipSlot.CP, slotMax: 1));
        test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        test.TakeSent();

        SetBanners();
        SetPremiumMarket(Premium(Blank, BannerName, MeretMarketBannerTag.None), Premium(Gift, string.Empty, MeretMarketBannerTag.PinkGift));
    }

    private static MeretMarketItemMetadata Premium(int id, string banner, MeretMarketBannerTag bannerTag) {
        return new MeretMarketItemMetadata(id, 0, HomeTab, banner, bannerTag, Design, 1, 1, 0, 0,
            MeretMarketItemSaleTag.None, MeretMarketCurrencyType.Meret, 100, 0, 0, 0, JobFilterFlag.None,
            false, 0, 0, 0, 0, false, false, false, string.Empty, 0, 0);
    }

    private object Server => typeof(GameSession).GetField("server", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;

    private void SetPremiumMarket(params MeretMarketItemMetadata[] entries) {
        var table = new MeretMarketTable(entries.ToDictionary(entry => entry.Id));
        typeof(Maple2.Server.Game.GameServer).GetField("premiumMarketCache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(Server, Maple2.Server.Game.GameServer.PremiumMarket(table, id => session.ItemMetadata.TryGet(id, out ItemMetadata? item) ? item : null));
    }

    private void SetBanners(params SystemBanner[] banners) {
        typeof(Maple2.Server.Game.GameServer).GetField("bannerCache", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(Server, banners.ToImmutableList());
    }

    // The FeaturedPremium answer: op (2), command, section, tab, three group counts, a byte, the slot count, then per
    // slot an exists flag and a product (is-ugc flag, id, ...). Returns the slot count and which test products it holds
    // (found by their exists flag, is-ugc flag and id bytes; the ids are chosen not to occur elsewhere).
    private (byte slots, bool blank, bool gift) Featured() {
        new MeretMarketHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(101);
                writer.WriteByte(0);
                writer.WriteByte(HomeTab);
            }));
        byte[] packet = test.TakeSent().Single(p => TestGameSession.Op(p) == SendOp.MeretMarket);
        string hex = Convert.ToHexString(packet);
        bool Holds(int id) => hex.Contains("0100" + Convert.ToHexString(BitConverter.GetBytes(id)));
        return (packet[9], Holds(Blank), Holds(Gift));
    }

    [Test]
    public void Home_LeavesOffTheProductWithNoBackground() {
        Assert.That(Featured(), Is.EqualTo(((byte) 2, false, true)));
    }

    [Test]
    public void Home_KeepsTheProductWithItsBanner() {
        SetBanners(new SystemBanner(1) { Name = BannerName, Url = "http://127.0.0.1/banner.png" });

        Assert.That(Featured(), Is.EqualTo(((byte) 2, true, true)));
    }

    [Test]
    public void Home_ABannerWithAnotherNameDoesNotCount() {
        SetBanners(new SystemBanner(1) { Name = "homeproduct_2", Url = "http://127.0.0.1/banner.png" });

        Assert.That(Featured(), Is.EqualTo(((byte) 2, false, true)));
    }
}
