using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Game.Shop;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Session;

public partial class GameSession {
    private const int ITEM_LOCK_MAX_STAGED_ITEMS = 18;
    public readonly long[] ItemLockStaging = new long[ITEM_LOCK_MAX_STAGED_ITEMS];

    private const int DISMANTLE_MAX_STAGED_ITEMS = 100;
    public bool DismantleOpened = false;
    public byte DismantleType;
    public readonly (long Uid, int Amount)[] DismantleStaging = new (long, int)[DISMANTLE_MAX_STAGED_ITEMS];

    public Item? StagedUgcItem = null;
    public GuildPoster? StagedGuildPoster = null;
    public long StagedGuildEmblemUgc = 0;
    public readonly UgcFeeBook UgcFees = new();
    // The banner slots the last banner upload (Ugc mode 1) put its design on, on the field of that upload.
    public Maple2.Model.Game.Ugc.UgcBanner? StagedBanner = null;

    public Item? StagedInstrumentItem = null;
    public Item? StagedScoreItem = null;
    public bool EnsembleReady = false;

    public Item? ChangeAttributesItem = null;

    public TradeManager? Trade;
    public StorageManager? Storage;

    public PetManager? Pet;
    // GM toggles (see FieldPlayer.InvisibleFromNpc / DeathPower); they last as long as the session.
    public bool GmInvisibleFromNpc;
    public bool GmDeathPower;
    // Buffs the GM effect control window (0x5C) created; its reset button takes off those still on.
    public readonly List<Buff> GmEffects = [];
    public FieldInstrument? Instrument;

    public FieldGuideObject? GuideObject;
    public HeldCube? HeldCube;

    public LiftupWeapon? HeldLiftup;

    public NpcScriptManager? NpcScript { get; set; }

    public BeautyShop? BeautyShop;

    public int BonusGameId;

    // The card game (CardReverseGameDialog) of the Ui object last used: its deck, cost and picks left.
    public CardReverseGame? CardReverseGame;

    public MiniGameUserRecord? MiniGameRecord;

    // The selected super chat item (charObj+0x11f0): the item id, which the chat line and the field packet carry.
    public int SuperChatId;
    public int SuperChatItemId;

    // The players whose whispers reached this session, by name: the client's whisper block option answers a received
    // whisper with chat kind 6 naming the sender, and only such a sender is sent the reject notice.
    public readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> WhisperedBy = new();

    // The chat spam restriction (FastChat and SameChat constants) of this player's lines.
    public readonly ChatRestriction ChatRestriction = new();

    // The account's live restrictions (AdminBlock 0x62), loaded at server entry and replaced whole when a GM changes
    // one.
    public volatile AccountBlock[] AccountBlocks = [];

    public long ReportCooldown = 0;

    public bool CanHold() {
        return GuideObject == null
               && Ride.Ride == null
               && HeldCube == null;
    }
}
