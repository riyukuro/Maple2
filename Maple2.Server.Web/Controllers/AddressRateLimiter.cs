using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Maple2.Server.Web.Controllers;

// A fixed-window count of accepted requests per client address. A refused request is not counted, so a client that
// keeps asking gets in again when its window ends. Behind a reverse proxy, a carrier-grade NAT or one shared NAT (an
// internet cafe), every client has the same address and they share one count.
//
// An IPv6 address counts by its /56: that is the usual prefix a provider gives one home, and every /64 inside it is the
// same customer, so a /64 key would give one home 256 counts. A /48 holder still gets 256 counts.
//
// At most maxTracked addresses are tracked. When a new address comes in with the table full, the address whose window
// started first is dropped and starts over; refusing new addresses instead would let whoever fills the table lock every
// new player out. Windows are kept in start order, so expired ones are dropped from the front on each call, and the
// table is trimmed once a burst has gone.
public sealed class AddressRateLimiter {
    public const int DefaultMaxTracked = 16384;
    private const int TrimFloor = 1024;

    private sealed class Window {
        public required string Key;
        public long Start;
        public int Count;
    }

    private readonly int limit;
    private readonly long windowMs;
    private readonly int maxTracked;
    private readonly Dictionary<string, LinkedListNode<Window>> windows = new();
    private readonly LinkedList<Window> byStart = new();
    private int highWater;

    public AddressRateLimiter(int limit, TimeSpan window, int maxTracked = DefaultMaxTracked) {
        this.limit = limit;
        windowMs = (long) window.TotalMilliseconds;
        this.maxTracked = Math.Max(1, maxTracked);
    }

    public int Tracked {
        get {
            lock (windows) {
                return windows.Count;
            }
        }
    }

    // The table's slot count, which a burst grows and the trim brings back down.
    public int Capacity {
        get {
            lock (windows) {
                return windows.EnsureCapacity(0);
            }
        }
    }

    public static string Key(IPAddress? address) {
        if (address == null) {
            return "";
        }
        if (address.IsIPv4MappedToIPv6) {
            return address.MapToIPv4().ToString();
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6) {
            byte[] bytes = address.GetAddressBytes();
            Array.Clear(bytes, 7, 9);
            return new IPAddress(bytes) + "/56";
        }
        return address.ToString();
    }

    public bool TryAcquire(string key, long nowMs) {
        lock (windows) {
            DropExpired(nowMs);

            if (windows.TryGetValue(key, out LinkedListNode<Window>? node)) {
                if (node.Value.Count >= limit) {
                    return false;
                }
                node.Value.Count++;
                return true;
            }

            if (windows.Count >= maxTracked) {
                Remove(byStart.First!);
            }
            windows[key] = byStart.AddLast(new Window { Key = key, Start = nowMs, Count = 1 });
            highWater = Math.Max(highWater, windows.Count);
            return true;
        }
    }

    private void DropExpired(long nowMs) {
        bool dropped = false;
        while (byStart.First is { } first && nowMs - first.Value.Start >= windowMs) {
            Remove(first);
            dropped = true;
        }
        if (dropped && highWater >= TrimFloor && windows.Count < highWater / 4) {
            windows.TrimExcess();
            highWater = windows.Count;
        }
    }

    private void Remove(LinkedListNode<Window> node) {
        windows.Remove(node.Value.Key);
        byStart.Remove(node);
    }
}
