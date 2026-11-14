using System.Reflection;
using Autofac;
using Maple2.Server.Core.PacketHandlers;

namespace Maple2.Server.Core.Network;

// The packet handlers a server's PacketRouter is built from, registered by each server's start (Program.cs) and by
// the tests that resolve them.
public static class PacketHandlerScan {
    // A handler of its own: a public, concrete class. A handler nested private in another (a wrapper that handler
    // queues, FieldPacketHandler.AwaitedPacket) is built by that handler and is no handler the router serves; its
    // constructor takes what the container cannot give, so registered it stopped the server's start.
    public static bool Serves<T>(Type type) where T : Session {
        return typeof(PacketHandler<T>).IsAssignableFrom(type) && type is { IsClass: true, IsAbstract: false }
            && (type.IsPublic || type.IsNestedPublic);
    }

    public static void Register<T>(ContainerBuilder autofac, Assembly assembly) where T : Session {
        autofac.RegisterAssemblyTypes(assembly)
            .Where(Serves<T>)
            .As<PacketHandler<T>>()
            .PropertiesAutowired()
            .SingleInstance();
    }
}
