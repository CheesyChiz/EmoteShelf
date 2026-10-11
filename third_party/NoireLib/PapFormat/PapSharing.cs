using System;
using System.Collections.Generic;

namespace NoireLib.Animations.PapFormat;

/// <summary>
/// Decides which emotes are written into one .pap and which part of a file answers to each name the file must
/// declare. An emote is often several parts, such as a dance's start and loop, and those parts may be wanted in
/// one file or in separate files.
/// </summary>
public static class PapSharing
{
    /// <summary>Groups emotes sharing a key into one file each.</summary>
    /// <param name="keys">One key per emote, in order, naming what it is a part of, with null meaning a file of
    /// its own.</param>
    /// <returns>The emote positions making up each file to write, in first-appearance order.</returns>
    public static List<List<int>> Group(IReadOnlyList<string?> keys)
    {
        var groups = new List<List<int>>();
        var byKey  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < keys.Count; ++index)
        {
            var key = keys[index];

            if (key == null)
            {
                groups.Add([index]);
                continue;
            }

            if (byKey.TryGetValue(key, out var position))
            {
                groups[position].Add(index);
                continue;
            }

            byKey[key] = groups.Count;
            groups.Add([index]);
        }

        return groups;
    }

    /// <summary>
    /// Assigns each required name one of a file's animations, preferring a part whose name ends the same way and
    /// otherwise pairing them in order.
    /// </summary>
    /// <param name="sourceNames">The animation names the file holds, in file order.</param>
    /// <param name="requiredNames">The names being written onto the file, in order.</param>
    /// <returns>A source animation index per required name, or -1 where the file holds none.</returns>
    public static int[] Match(IReadOnlyList<string> sourceNames, IReadOnlyList<string> requiredNames)
    {
        var matches = new int[requiredNames.Count];
        Array.Fill(matches, -1);

        if (sourceNames.Count == 0)
            return matches;

        var taken = new bool[sourceNames.Count];

        for (var index = 0; index < requiredNames.Count; ++index)
        {
            var suffix = Suffix(requiredNames[index]);
            if (suffix.Length == 0)
                continue;

            for (var source = 0; source < sourceNames.Count; ++source)
            {
                if (taken[source] || !string.Equals(Suffix(sourceNames[source]), suffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                matches[index] = source;
                taken[source]  = true;
                break;
            }
        }

        var next = 0;

        for (var index = 0; index < requiredNames.Count; ++index)
        {
            if (matches[index] >= 0)
                continue;

            while (next < sourceNames.Count && taken[next])
                ++next;

            // More names than the file has parts, so this one cannot be answered from it.
            if (next >= sourceNames.Count)
                continue;

            matches[index] = next;
            taken[next]    = true;
        }

        return matches;
    }

    // The part of a name after its last underscore, such as the start or loop of one emote.
    private static string Suffix(string name)
    {
        var index = name.LastIndexOf('_');
        return index < 0 || index == name.Length - 1 ? string.Empty : name[(index + 1)..];
    }
}
