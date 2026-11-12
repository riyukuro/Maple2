using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Server.Core.Constants;
using Maple2.Server.Login;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Service;
using Serilog;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Login;

// A LoginSession for handler tests: no socket, no world server and no database server.
//
//   var test = new TestLoginSession();              // an account with one character, in an in-memory storage
//   long other = test.AddCharacter("Name");         // more rows, of this account or another
//   LoginSession session = test.Start();            // the session after its account is known (LoginSession.Init)
//   handler.Handle(session, TestGameSession.Packet(writer => ...));
//   test.TakeSent()                                 // the packets sent since the last call, in order
//
// The rows and the storage are TestGameSession's. The session is made without its constructor: it has its send
// queue, its logger, the storage, a world that grants every lock and accepts every move and player update, and a
// server without banners or events. A close (Disconnect, Cut) ends the session; nothing sent after it is kept.
public sealed class TestLoginSession {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public readonly TestGameSession Rows = new();
    public readonly LoginWorld World = new();
    public readonly List<string> Errors = [];
    public long AccountId => Rows.AccountId;
    public long CharacterId => Rows.CharacterId;
    public GameStorage Storage => Rows.Storage;

    private readonly BlockingCollection<(byte[] packet, int length)> sendQueue = new(new ConcurrentQueue<(byte[], int)>());

    // A character row; of the test's account unless another is given. The name is stored as given (a name longer
    // than a create allows, too).
    public long AddCharacter(string name, long accountId = 0) {
        using GameStorage.Request db = Storage.Context();
        Character character = db.CreateCharacter(new Character {
            AccountId = accountId == 0 ? AccountId : accountId, Name = name, Gender = Gender.Male, Job = Job.Newbie,
            Mastery = new Mastery(), MapId = 2000023, ReturnMaps = new Maple2.Tools.LimitedStack<int>(3, 2000023),
        }) ?? throw new InvalidOperationException("character row not created");
        return character.Id;
    }

    // The stored name of a character.
    public string Name(long characterId) {
        using var context = new Ms2Context(Rows.Options);
        return context.Character.Find(characterId)!.Name;
    }

    public void SetName(long characterId, string name) {
        using var context = new Ms2Context(Rows.Options);
        context.Character.Find(characterId)!.Name = name;
        context.SaveChanges();
    }

    // init: the account is known, as after ResponseKey (false for a test of that handler itself).
    public LoginSession Start(bool init = true) {
        var server = (LoginServer) RuntimeHelpers.GetUninitializedObject(typeof(LoginServer));
        GC.SuppressFinalize(server);
        SetField(server, "mutex", new object());
        SetField(server, "connectingSessions", new HashSet<LoginSession>());
        SetField(server, "sessions", new Dictionary<long, LoginSession>());
        SetField(server, "bannerCache", new List<SystemBanner>());
        SetField(server, "eventCache", new Dictionary<int, GameEvent>());

        var session = (LoginSession) RuntimeHelpers.GetUninitializedObject(typeof(LoginSession));
        GC.SuppressFinalize(session);
        SetField(session, "sendQueue", sendQueue);
        SetField(session, "lastSentPackets", new ConcurrentDictionary<SendOp, byte[]>());
        SetField(session, "Logger", new LoggerConfiguration().WriteTo.Sink(new ErrorSink(Errors)).CreateLogger());
        SetField(session, nameof(LoginSession.Server), server);
        SetField(session, "<World>k__BackingField", World);
        SetField(session, "<GameStorage>k__BackingField", Storage);
        if (init) {
            session.Init(AccountId, Guid.NewGuid());
        }
        return session;
    }

    // The packets sent since the last call, in the order they were sent; each starts with its opcode.
    public List<byte[]> TakeSent() {
        var sent = new List<byte[]>();
        while (sendQueue.TryTake(out (byte[] packet, int length) entry)) {
            sent.Add(entry.packet.Take(entry.length).ToArray());
        }
        return sent;
    }

    public List<SendOp> TakeSentOps() => TakeSent().Select(TestGameSession.Op).ToList();

    private static void SetField(object target, string name, object value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    public sealed class LoginWorld : WorldClient {
        public readonly List<MigrateOutRequest> Moves = [];
        public readonly List<MigrateInRequest> Arrivals = [];
        public readonly List<PlayerUpdateRequest> PlayerUpdates = [];

        public override ChannelsResponse Channels(ChannelsRequest request, CallOptions options) {
            return new ChannelsResponse { Channels = { 1 } };
        }

        public override MigrateInResponse MigrateIn(MigrateInRequest request, CallOptions options) {
            Arrivals.Add(request);
            return new MigrateInResponse();
        }

        public override MigrateOutResponse MigrateOut(MigrateOutRequest request, CallOptions options) {
            Moves.Add(request);
            return new MigrateOutResponse { IpAddress = "127.0.0.1", Port = 20002, Token = 1 };
        }

        public override LockResponse AcquireLock(LockRequest request, CallOptions options) => new();

        public override LockResponse ReleaseLock(LockRequest request, CallOptions options) => new();

        public override PlayerUpdateResponse UpdatePlayer(PlayerUpdateRequest request, CallOptions options) {
            PlayerUpdates.Add(request);
            return new PlayerUpdateResponse();
        }

        // The club requests; ClubError is what the world answers (0: done).
        public readonly List<ClubRequest> Clubs = [];
        public int ClubError;

        public override Maple2.Server.World.Service.ClubResponse Club(ClubRequest request, CallOptions options) {
            Clubs.Add(request);
            return new Maple2.Server.World.Service.ClubResponse { Error = ClubError };
        }
    }

    private sealed class ErrorSink(List<string> errors) : Serilog.Core.ILogEventSink {
        public void Emit(Serilog.Events.LogEvent logEvent) {
            if (logEvent.Level >= Serilog.Events.LogEventLevel.Error) {
                lock (errors) {
                    errors.Add($"{logEvent.RenderMessage()} {logEvent.Exception}");
                }
            }
        }
    }
}
