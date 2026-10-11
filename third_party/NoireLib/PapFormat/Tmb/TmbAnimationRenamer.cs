using NoireLib.Animations.PapFormat.Tmb.Entries;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NoireLib.Animations.PapFormat.Tmb;

/// <summary>
/// Repoints the animation references inside a standalone action-timeline TMB: every C010 or C009 whose
/// <c>Path</c> is a key in the rename map takes that key's value. A character binds a havok animation by the name
/// its C010 carries, so pairing this with <see cref="PapRetargeter.RenameInternalAnimations"/> on the served pap
/// is what makes it resolve a resource no earlier content is already resident under. Only the parsed model is
/// touched, and a rewrite that does not read back as a structurally valid TMB is refused.
/// </summary>
public static class TmbAnimationRenamer
{
    /// <summary>
    /// Copies a TMB with every C010 and C009 animation reference that appears as a key in the rename map repointed
    /// to its mapped value, leaving unmatched references alone and never modifying the input array.
    /// </summary>
    /// <param name="tmb">The action-timeline bytes to copy.</param>
    /// <param name="renames">The old-name to new-name map; a partial or empty map is not an error.</param>
    /// <returns>The rewritten bytes.</returns>
    /// <exception cref="InvalidDataException">
    /// The produced bytes do not read back as a valid TMB, or a rewritten reference did not survive the round-trip.
    /// </exception>
    public static byte[] Rename(byte[] tmb, IReadOnlyDictionary<string, string> renames)
    {
        var file = new TmbFile(new BinaryReader(new MemoryStream(tmb)));

        var applied = new List<string>();

        foreach (var entry in file.AllEntries)
        {
            var path = entry switch
            {
                C010 c010 => c010.Path,
                C009 c009 => c009.Path,
                _ => null,
            };

            if (path is null || !renames.TryGetValue(path.Value, out var newName) || newName == path.Value)
                continue;

            path.Value = newName;
            applied.Add(newName);
        }

        var result = file.ToBytes();
        Verify(result, applied);
        return result;
    }

    /// <summary>
    /// The distinct animation names a TMB references through its C010 and C009 entries, in first-seen order, which
    /// is what a character binds by name and what a rename map has to be scoped to.
    /// </summary>
    /// <param name="tmb">The action-timeline bytes to read.</param>
    /// <returns>The referenced names, or an empty list when the TMB is unreadable.</returns>
    public static IReadOnlyList<string> ReadAnimationReferences(byte[] tmb)
    {
        try
        {
            var file = new TmbFile(new BinaryReader(new MemoryStream(tmb)));
            var references = new List<string>();

            foreach (var entry in file.AllEntries)
            {
                var value = entry switch
                {
                    C010 c010 => c010.Path.Value,
                    C009 c009 => c009.Path.Value,
                    _ => null,
                };

                if (!string.IsNullOrEmpty(value) && !references.Contains(value))
                    references.Add(value);
            }

            return references;
        }
        catch (System.Exception)
        {
            return [];
        }
    }

    // Confirms the rewritten bytes re-parse as a valid TMB and that every applied name is present among the re-read
    // references, since a broken action tmb crashes the game just as a broken pap does.
    private static void Verify(byte[] result, IReadOnlyList<string> applied)
    {
        TmbFile reparsed;

        try
        {
            reparsed = new TmbFile(new BinaryReader(new MemoryStream(result)));
        }
        catch (System.Exception ex)
        {
            throw new InvalidDataException(
                "The rewritten action tmb failed to read back as a structurally valid TMB; refusing to return it.", ex);
        }

        var references = new HashSet<string>(
            reparsed.AllEntries.Select(entry => entry switch
            {
                C010 c010 => c010.Path.Value,
                C009 c009 => c009.Path.Value,
                _ => null,
            }).Where(value => value is not null)!);

        foreach (var name in applied)
        {
            if (!references.Contains(name))
                throw new InvalidDataException(
                    $"The rewritten action tmb does not reference '{name}' after being read back; refusing to return it.");
        }
    }
}
