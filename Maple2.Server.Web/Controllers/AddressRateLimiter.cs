using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Maple2.Server.Web.Controllers;

// A fixed-window count of accepted requests per client address. A refused request is not counted, so a client that
// keeps asking gets in again when its window ends. An IPv6 address counts by its /64, which one host can fill with
// addresses of its own. Behind a reverse proxy every client has the proxy's address and shares one count.
public sealed class AddressRateLimiter {
    private const int PruneAt = 4096;

    private readonly int limit;
    private readonly long windowMs;
    private readonly Dictionary<string, (long Start, int Count)> windows = new();
    private int nextPrune = PruneAt;

    public AddressRateLimiter(int limit, TimeSpan window) {
        this.limit = limit;
        windowMs = (long) window.TotalMilliseconds;
    }

    public int Tracked {
        get {
            lock (windows) {
                return windows.Count;
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
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes) + "/64";
        }
        return address.ToString();
    }

    public bool TryAcquire(string key, long nowMs) {
        lock (windows) {
            if (windows.Count >= nextPrune) {
                Prune(nowMs);
                nextPrune = Math.Max(PruneAt, windows.Count * 2);
            }

            if (!windows.TryGetValue(key, out (long Start, int Count) window) || nowMs - window.Start >= windowMs) {
                windows[key] = (nowMs, 1);
                return true;
            }
            if (window.Count >= limit) {
                return false;
            }
            windows[key] = (window.Start, window.Count + 1);
            return true;
        }
    }

    private void Prune(long nowMs) {
        var expired = new List<string>();
        foreach ((string key, (long start, int _)) in windows) {
            if (nowMs - start >= windowMs) {
                expired.Add(key);
            }
        }
        foreach (string key in expired) {
            windows.Remove(key);
        }
    }
}
