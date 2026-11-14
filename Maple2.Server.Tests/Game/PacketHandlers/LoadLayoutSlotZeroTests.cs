using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube 48 loads the saved layout of the given id (UIUGCCubeBillDialog::SendRequestCube kind 1,
// uiugccubebilldialog.cpp:216); a blueprint is confirmed only with 70 and the uid of its item. Slot 0 applied the
// staged blueprint instead, with no check of the item, and never loaded a layout saved in slot 0.
public class LoadLayoutSlotZeroTests {
    private const byte LayoutArea = 12, BlueprintArea = 14;
    private TestGameSession test = null!;

    // The owner in their empty home, with a blueprint staged whose layout is stored; `slotZero` adds a saved layout
    // of id 0.
    private GameSession Start(bool slotZero) {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        test.EnterHome();
        if (slotZero) {
            session.Player.Value.Home.Layouts.Add(new HomeLayout(0, "slot 0", LayoutArea, 4, DateTimeOffset.UnixEpoch, []));
        }
        HomeLayout stored;
        using (GameStorage.Request db = test.Storage.Context()) {
            stored = db.SaveHomeLayout(new HomeLayout(1, "blueprint", BlueprintArea, 5, DateTimeOffset.UnixEpoch, [])) ?? throw new InvalidOperationException("layout not saved");
        }
        session.Housing.StagedItemBlueprint = new ItemBlueprint { BlueprintUid = stored.Uid };
        session.Housing.StagedBlueprintItemUid = 777;
        return session;
    }

    private void Load(GameSession session, int slot) {
        test.TakeSent();
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(48);
            writer.WriteInt(slot);
        }));
    }

    [Test]
    public void SlotZero_LoadsTheSavedLayout() {
        GameSession session = Start(slotZero: true);

        Load(session, 0);

        Assert.That(session.Player.Value.Home.Area, Is.EqualTo(LayoutArea));
    }

    [Test]
    public void SlotZero_NoSavedLayout_DoesNotApplyTheBlueprint() {
        GameSession session = Start(slotZero: false);
        byte area = session.Player.Value.Home.Area;

        Load(session, 0);

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Home.Area, Is.EqualTo(area));
            Assert.That(session.Housing.StagedItemBlueprint, Is.Not.Null);
        });
    }
}
