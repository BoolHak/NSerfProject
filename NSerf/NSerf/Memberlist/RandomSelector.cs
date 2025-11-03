// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Thread-safe random selection utilities.
/// </summary>
public static class RandomSelector
{
    [ThreadStatic]
    private static Random? _random;

    private static Random Random => _random ??= new Random();

    /// <summary>
    /// Selects a random item from a list.
    /// </summary>
    public static T SelectRandom<T>(List<T> items)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("Cannot select from empty list");
        }

        return items[Random.Next(items.Count)];
    }

    /// <summary>
    /// Selects k random items from a list without replacement.
    /// </summary>
    public static List<T> SelectRandomK<T>(List<T> items, int k)
    {
        if (k >= items.Count)
        {
            return [.. items];
        }

        var selected = new List<T>(k);
        var indices = new HashSet<int>();

        while (selected.Count < k)
        {
            var idx = Random.Next(items.Count);
            if (indices.Add(idx))
            {
                selected.Add(items[idx]);
            }
        }

        return selected;
    }

    /// <summary>
    /// Shuffles a list in place.
    /// </summary>
    public static void Shuffle<T>(List<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
