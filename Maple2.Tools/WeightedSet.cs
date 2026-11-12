using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Maple2.Tools;

public class WeightedSet<T> : IEnumerable<(T, int)> {
    private readonly Random rng;
    // A list, in the order added: a row repeated with the same weight counts twice, as its weight does in the total.
    private readonly List<(T, int)> set;
    private int totalWeight;

    public int Count => set.Count;

    public WeightedSet(Random? random = null) {
        rng = random ?? Random.Shared;
        set = new List<(T, int)>();
    }

    public void Add(T value, int weight) {
        // A negative weight counts as none: it would take from the other rows' share (and throw once the total is negative).
        if (weight < 0) {
            weight = 0;
        }
        Interlocked.Add(ref totalWeight, weight);
        set.Add((value, weight));
    }

    public T Get() {
        int random = rng.Next(totalWeight);
        foreach ((T value, int weight) in set) {
            random -= weight;
            if (random < 0) {
                return value;
            }
        }

        return set.First().Item1;
    }

    public IEnumerator<(T, int)> GetEnumerator() {
        return set.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }
}
