using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autofac;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Login.Session;

namespace Maple2.Server.Tests.Core;

// The servers' start registers their packet handlers with PacketHandlerScan and resolves the PacketRouter from them:
// a handler the container cannot build stops the server at its start, so it fails here first.
public class PacketHandlerScanTests {
    private static Assembly GameAssembly => typeof(GameSession).Assembly;
    private static Assembly LoginAssembly => typeof(LoginSession).Assembly;

    private static IContainer Container<T>(Assembly assembly) where T : Session {
        var autofac = new ContainerBuilder();
        autofac.RegisterType<PacketRouter<T>>().As<PacketRouter<T>>().SingleInstance();
        PacketHandlerScan.Register<T>(autofac, assembly);
        return autofac.Build();
    }

    private static void ResolvesEveryHandler<T>(Assembly assembly) where T : Session {
        using IContainer container = Container<T>(assembly);
        List<PacketHandler<T>> handlers = [];
        Assert.DoesNotThrow(() => handlers = container.Resolve<IEnumerable<PacketHandler<T>>>().ToList());
        Assert.DoesNotThrow(() => container.Resolve<PacketRouter<T>>());
        Assert.That(handlers, Is.Not.Empty);
    }

    [Test]
    public void Game_ResolvesTheRouterAndEveryHandler() => ResolvesEveryHandler<GameSession>(GameAssembly);

    [Test]
    public void Login_ResolvesTheRouterAndEveryHandler() => ResolvesEveryHandler<LoginSession>(LoginAssembly);

    private static IEnumerable<Type> Concrete<T>(Assembly assembly) where T : Session =>
        assembly.GetTypes().Where(type => typeof(PacketHandler<T>).IsAssignableFrom(type) && type is { IsClass: true, IsAbstract: false });

    // Only the wrapper the field handlers queue is left out: every other concrete handler is public and served.
    [Test]
    public void OnlyTheQueuedWrapperIsLeftOut() {
        Assert.Multiple(() => {
            Assert.That(Concrete<GameSession>(GameAssembly).Where(type => !PacketHandlerScan.Serves<GameSession>(type)).Select(type => type.Name),
                Is.EqualTo(new[] { "AwaitedPacket" }));
            Assert.That(Concrete<LoginSession>(LoginAssembly).Where(type => !PacketHandlerScan.Serves<LoginSession>(type)), Is.Empty);
        });
    }
}
