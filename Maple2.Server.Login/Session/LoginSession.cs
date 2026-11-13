using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Helpers;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.Packets;
using Maple2.Server.World.Service;
using static Maple2.Model.Error.CharacterCreateError;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Login.Session;

public class LoginSession : Core.Network.Session {
    protected override PatchType Type => PatchType.Delete;

    private bool disposed;
    public readonly LoginServer Server;

    public new long AccountId { get; private set; }
    public new long CharacterId { get; private set; } // Used only as a temporary variable
    public Guid MachineId { get; private set; }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required WorldClient World { private get; init; }
    public required GameStorage GameStorage { private get; init; }
    // ReSharper restore All
    #endregion

    // Set by ListCharacters; null until the character list was sent.
    private Account account = null!;
    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
    public bool HasAccount => account != null;
    public int MaxCharacters => account.MaxCharacters;

    public int ServerTick;
    public int ClientTick;

    public LoginSession(TcpClient tcpClient, LoginServer server) : base(tcpClient) {
        Server = server;
        State = SessionState.ChangeMap;
    }

    public void Init(long accountId, Guid machineId) {
        AccountId = accountId;
        MachineId = machineId;

        State = SessionState.Connected;
        Server.OnConnected(this);
    }

    private void AcquireLock(long accountId, int maxRetries = 3) {
        int retryCount = 0;
        const int backoffMs = 500;

        while (retryCount < maxRetries) {
            LockResponse? response;
            try {
                response = World.AcquireLock(new LockRequest {
                    AccountId = accountId,
                });
            } catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable) {
                // A world that is down (stopped before this server) holds no locks and grants none. The save goes on
                // without the lock, as after the retries below; a throw here would skip the save.
                Logger.Warning("World unavailable; lock for account {AccountId} not taken", accountId);
                return;
            }

            if (string.IsNullOrEmpty(response.Error)) {
                return;
            }

            retryCount++;
            Thread.Sleep(backoffMs);
        }

        Logger.Error("Failed to acquire lock for account {AccountId} after {MaxRetries} retries", accountId, maxRetries);
    }

    private void ReleaseLock(long accountId) {
        try {
            LockResponse response = World.ReleaseLock(new LockRequest {
                AccountId = accountId,
            });
            if (!string.IsNullOrEmpty(response.Error)) {
                Logger.Warning("Failed to release lock for account {AccountId}: {ErrorMessage}", accountId, response.Error);
            }
        } catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable) {
            // The world keeps its locks in memory: a world that is down (stopped before this server) holds none.
            Logger.Warning("World unavailable; lock for account {AccountId} not released", accountId);
        } catch (RpcException ex) {
            Logger.Error(ex, "Failed to release lock for account {AccountId}", accountId);
        }
    }

    // The login's game events: the table's active ones, and a LoginNotice while the GM notice config has the login
    // notice on and the table has none (NoticeRules).
    public void SendEvents() {
        List<GameEvent> events = Server.GetEvents().ToList();
        if (!events.Any(gameEvent => gameEvent.Metadata.Data is LoginNotice) && LoginNoticeOn()) {
            events.Add(NoticeRules.LoginNoticeEvent(DateTime.Now));
        }
        Send(GameEventPacket.Load(events.ToArray()));
    }

    private bool LoginNoticeOn() {
        try {
            return World.Admin(new AdminRequest {
                Notice = new AdminRequest.Types.Notice {
                    Kind = NoticeRules.Login,
                    Action = NoticeRules.Status,
                },
            }).NoticeLogin;
        } catch (RpcException ex) {
            Logger.Warning(ex, "World unavailable; no login notice");
            return false;
        }
    }

    public void ListServers(long accountId) {
        ChannelsResponse response = World.Channels(new ChannelsRequest());
        int characters;
        using (GameStorage.Request db = GameStorage.Context()) {
            characters = db.CountCharacters(accountId);
        }
        Send(BannerListPacket.Load(Server.GetSystemBanners()));
        Send(ServerListPacket.Load(Target.SERVER_NAME, [new IPEndPoint(Target.LoginIp, Target.LoginPort)], response.Channels, characters, response.Online));
    }

    public void ListCharacters() {
        using GameStorage.Request db = GameStorage.Context();
        AcquireLock(AccountId);
        Account? readAccount;
        IList<Character>? characters;
        try {
            (readAccount, characters) = db.ListCharacters(AccountId);
        } finally {
            ReleaseLock(AccountId);
        }
        if (readAccount == null || characters == null) {
            throw new InvalidOperationException($"Failed to load characters for account: {AccountId}");
        }

        account = readAccount;
        var entries = new List<(Character, IDictionary<ItemGroup, List<Item>>)>();
        foreach (Character character in characters) {
            IDictionary<ItemGroup, List<Item>> equips =
                db.GetItemGroups(character.Id, ItemGroup.Gear, ItemGroup.Outfit, ItemGroup.Outfit2, ItemGroup.Badge);
            entries.Add((character, equips));
        }

        Send(CharacterListPacket.SetMax(account.MaxCharacters, Constant.ServerMaxCharacters));
        Send(CharacterListPacket.StartList());
        // Send each character data
        Send(CharacterListPacket.AddEntries(account, entries));
        Send(CharacterListPacket.EndList(characters.Any(character => IsRenameDue(character.Name))));
    }

    // No server path stores a name longer than the limit; a row that has one (written by hand, or kept from another
    // region's limit) must be renamed before it enters. The client's test counts the length first, so a long name
    // with a character the region does not allow is due as well.
    public static bool IsRenameDue(string name) {
        return NameChangeRules.IsRenameDue(NameValidator.CharacterNameScanWidth(name, Target.LOCALE), Constant.CharacterNameLengthMax);
    }

    public void CreateCharacter(Character createCharacter, List<Item> createOutfits) {
        using GameStorage.Request db = GameStorage.Context();
        db.BeginTransaction();
        // A step that fails sends the create error and returns; the request's dispose rolls back what was written.
        Character? character = db.CreateCharacter(createCharacter);
        if (character == null) {
            Logger.Error("Failed to create character {Name} of account {AccountId}", createCharacter.Name, AccountId);
            Send(CharacterListPacket.CreateError(s_char_err_system));
            return;
        }
        CharacterId = character.Id;

        var unlock = new Unlock();

        foreach (int emoteId in Constant.DefaultEmotes) {
            unlock.Emotes.Add(emoteId);
        }
        character.AchievementInfo = db.GetAchievementInfo(AccountId, character.Id);
        character.PremiumTime = account.PremiumTime;

        // The unlocks, the first skill tab and the config: a character without them is half made.
        if (!db.InitNewCharacter(character.Id, unlock)) {
            Logger.Error("Failed to set up new character {CharacterId}", character.Id);
            Send(CharacterListPacket.CreateError(s_char_err_system));
            return;
        }

        foreach (Item item in createOutfits) {
            item.Transfer?.Bind(character);
        }
        List<Item>? outfits = db.CreateItems(character.Id, createOutfits.ToArray());
        // The running CharacterFirstConnect rows mail the new character (KR 110000: the beginner guide).
        bool mailed = true;
        foreach (Mail mail in EventMailRules.FirstConnect(Server.GetEvents().Select(gameEvent => gameEvent.Metadata), character.Id)) {
            mailed &= db.CreateMail(mail) != null;
        }

        if (outfits == null || !mailed || !db.Commit()) {
            Send(CharacterListPacket.CreateError(s_char_err_system));
            return;
        }

        Send(CharacterListPacket.SetMax(account.MaxCharacters, Constant.ServerMaxCharacters));
        Send(CharacterListPacket.AppendEntry(account, character,
            new Dictionary<ItemGroup, List<Item>> {
                { ItemGroup.Outfit, outfits },
            }));
    }

    #region Dispose
    ~LoginSession() => Dispose(false);

    protected override void Dispose(bool disposing) {
        if (disposed) return;
        disposed = true;

        try {
            Server.OnDisconnected(this);
        } catch (Exception ex) {
            Logger.Debug(ex, "Error during LoginSession.OnDisconnected");
        }

        State = SessionState.Disconnected;

        try {
            base.Dispose(disposing);
        } catch (Exception ex) {
            Logger.Debug(ex, "Error during LoginSession base disposal");
        }
    }
    #endregion
}
