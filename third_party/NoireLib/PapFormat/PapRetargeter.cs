using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NoireLib.Animations.PapFormat;

/// <summary>
/// Renames a .pap's animations so they bind to a different emote, without touching the file system. With
/// <c>removeAnimationLock</c> false, nothing changes beyond the animation names and the C009 timeline entries
/// that repeat them. A .pap the game cannot parse crashes the client, so produced bytes are always read back
/// and structurally re-parsed before being returned.
/// </summary>
public static class PapRetargeter
{
    /// <summary>
    /// Renames one animation per name in <paramref name="requiredNames"/> that <see cref="PapSharing.Match"/> can
    /// answer from <paramref name="sourcePap"/>, rewriting the animation's own name and the C009 timeline entries
    /// that repeat it together. A required name no source animation can answer is skipped rather than failing.
    /// </summary>
    /// <param name="sourcePap"> The .pap's raw bytes, never modified. </param>
    /// <param name="requiredNames"> The animation names the retargeted file must declare, one per emote part. </param>
    /// <param name="removeAnimationLock">
    /// Whether to strip each renamed animation's C125 animation lock. Stripping invalidates that animation's TMB
    /// source layout (see <see cref="PapAnimationLock.Remove"/>), so its timeline is rebuilt from the parsed
    /// model rather than patched in place and its bytes no longer match the animator's original layout.
    /// </param>
    /// <param name="locksRemoved"> How many animation lock entries were removed across every renamed animation. </param>
    /// <returns> The retargeted .pap's bytes, verified to declare every name that was applied. </returns>
    /// <exception cref="InvalidDataException">
    /// The produced bytes do not read back as a valid .pap declaring every name that was applied.
    /// </exception>
    public static byte[] Retarget(byte[] sourcePap, IReadOnlyList<string> requiredNames, bool removeAnimationLock,
        out int locksRemoved)
    {
        using var reader = new BinaryReader(new MemoryStream(sourcePap));
        var pap = new PapFile(reader); // hkxTempPath omitted, since renaming never needs the havok blob on disk.

        var sourceNames = pap.Animations.ConvertAll(animation => animation.GetName());
        var matches     = PapSharing.Match(sourceNames, requiredNames);
        var applied     = new List<string>();

        locksRemoved = 0;

        for (var index = 0; index < requiredNames.Count; ++index)
        {
            var source = matches[index];
            if (source < 0)
                continue; // Nothing in this file can answer this name, so leave it unapplied rather than fail.

            var name      = requiredNames[index];
            var animation = pap.Animations[source];

            RenameAnimation(animation, name, removeAnimationLock, ref locksRemoved);
            applied.Add(name);
        }

        var result = pap.ToBytes();
        Verify(result, applied);
        return result;
    }

    /// <summary>
    /// Retargets like <see cref="Retarget"/>, but produces exactly one output animation per name in
    /// <paramref name="requiredNames"/>, duplicating a source animation whenever one has to answer more than one
    /// name. Each required name takes the source animation whose suffix matches, falling back to the file's first
    /// animation; the first name to claim a source renames it in place and any further claimant gets a deep
    /// <see cref="PapAnimation.Clone"/>, which shares the file's havok binding but owns its own TMB. Unmatched
    /// source animations are dropped, and a name no source can answer is skipped.
    /// </summary>
    /// <param name="sourcePap"> The .pap's raw bytes, never modified. </param>
    /// <param name="requiredNames"> The animation names the output must declare, one per output animation. </param>
    /// <param name="removeAnimationLock"> Whether to strip each renamed animation's C125 animation lock. </param>
    /// <param name="locksRemoved"> How many animation lock entries were removed across every renamed animation. </param>
    /// <param name="oneFrameWhenLentNames">
    /// Names whose output is clamped to a single frame when the source animation serving them also serves a name
    /// outside this set, which marks it as a lent duplicate of another channel. A listed name whose choice finds
    /// a source animation of its own keeps its full-length timing. Null clamps nothing.
    /// </param>
    /// <param name="clampedNames">
    /// Receives every name whose output took the one-frame clamp, and is left untouched when nothing clamps.
    /// </param>
    /// <returns> The retargeted .pap's bytes, verified to declare every name that was applied. </returns>
    /// <exception cref="InvalidDataException">
    /// The produced bytes do not read back as a valid .pap declaring every name that was applied.
    /// </exception>
    public static byte[] RetargetToNames(byte[] sourcePap, IReadOnlyList<string> requiredNames, bool removeAnimationLock,
        out int locksRemoved, IReadOnlyCollection<string>? oneFrameWhenLentNames = null,
        ICollection<string>? clampedNames = null)
    {
        using var reader = new BinaryReader(new MemoryStream(sourcePap));
        var pap = new PapFile(reader);

        var sourceNames = pap.Animations.ConvertAll(animation => animation.GetName());

        locksRemoved = 0;

        // Decided up front because lending looks across names: a marked name is a lent duplicate exactly when its
        // chosen source animation is also chosen by some unmarked name, wherever the two sit in the list.
        var choices = new int[requiredNames.Count];
        var fullLengthSources = new HashSet<int>();

        for (var index = 0; index < requiredNames.Count; ++index)
        {
            choices[index] = ChooseSourceForName(sourceNames, requiredNames[index]);

            if (choices[index] >= 0 && oneFrameWhenLentNames?.Contains(requiredNames[index]) != true)
                fullLengthSources.Add(choices[index]);
        }

        var outputs = new List<PapAnimation>(requiredNames.Count);
        var applied = new List<string>(requiredNames.Count);

        for (var index = 0; index < requiredNames.Count; ++index)
        {
            var source = choices[index];
            if (source < 0)
                continue; // Empty source pap, so nothing can answer this name.

            var name = requiredNames[index];

            // Cloning an untouched source lets one source animation back several names without one output's
            // rename mutating another's.
            var animation = pap.Animations[source].Clone();

            RenameAnimation(animation, name, removeAnimationLock, ref locksRemoved);

            if (oneFrameWhenLentNames?.Contains(name) == true && fullLengthSources.Contains(source))
            {
                ClampToOneFrame(animation);
                clampedNames?.Add(name);
            }

            outputs.Add(animation);
            applied.Add(name);
        }

        pap.Animations.Clear();
        pap.Animations.AddRange(outputs);

        var result = pap.ToBytes();
        Verify(result, applied);
        return result;
    }

    /// <summary>
    /// Renames animations by their current header name rather than by matching a target's required names, giving
    /// each animation whose <see cref="PapAnimation.GetName"/> is a key in <paramref name="renames"/> that key's
    /// value and leaving every other animation untouched. The game's havok resource is keyed by the internal
    /// name, so identical names across files let a fresh load resolve to the resident older animation; a
    /// content-unique internal name removes that shared key. A name no animation carries is skipped.
    /// </summary>
    /// <param name="sourcePap"> The .pap's raw bytes, never modified. </param>
    /// <param name="renames"> Current header name to new internal name; only present names are renamed. </param>
    /// <param name="keepOriginalAsAlias">
    /// Whether each renamed animation is joined by a deep <see cref="PapAnimation.Clone"/> keeping the original
    /// name, appended after every original entry with the same havok binding and its own TMB. The binder asks
    /// for whichever internal name the served action TMB carries, which is the vanilla name whenever the
    /// redirected TMB is not the one served, so an output declaring only the unique name answers nothing there.
    /// </param>
    /// <returns> The renamed .pap's bytes, verified to declare every applied new name. </returns>
    /// <exception cref="InvalidDataException"> The produced bytes do not read back as declaring a name that was applied. </exception>
    public static byte[] RenameInternalAnimations(byte[] sourcePap, IReadOnlyDictionary<string, string> renames,
        bool keepOriginalAsAlias = false)
    {
        using var reader = new BinaryReader(new MemoryStream(sourcePap));
        var pap = new PapFile(reader); // hkxTempPath omitted, since renaming never needs the havok blob on disk.

        var applied = new List<string>();
        var aliases = new List<PapAnimation>();
        var locksRemoved = 0;

        foreach (var animation in pap.Animations)
        {
            if (!renames.TryGetValue(animation.GetName(), out var newName) || newName == animation.GetName())
                continue;

            // Taken before the rename so the clone keeps the original name, C009 entries included.
            if (keepOriginalAsAlias)
            {
                aliases.Add(animation.Clone());
                applied.Add(animation.GetName());
            }

            RenameAnimation(animation, newName, removeAnimationLock: false, ref locksRemoved);
            applied.Add(newName);
        }

        pap.Animations.AddRange(aliases);

        var result = pap.ToBytes();
        Verify(result, applied);
        return result;
    }

    // The footstep entry magic, whose payload is four plain values with no string offset.
    private const string FootstepMagic = "C042";

    // Clamps one animation's timeline to a single frame. A channel lives as long as its latest and longest content,
    // so every dimension that can stretch it is pulled in: the TMDH length, every C009 and C010 clip duration, every
    // entry's start time including unknown magics, and each C010's playback segment.
    private static void ClampToOneFrame(PapAnimation animation)
    {
        if (animation.Tmb is not { } tmb)
            return;

        tmb.HeaderTmdh.SetLength(1);

        // Footsteps are removed rather than moved to frame 0, where they would all fire at once. They are safe
        // to remove because the string-table rebuild a removal forces cannot disturb an entry carrying no string.
        tmb.RemoveEntries(FootstepMagic);

        foreach (var entry in tmb.AllEntries)
        {
            entry.SetTime(0);

            switch (entry)
            {
                case Tmb.Entries.C009 body:
                    body.SetDuration(1);
                    break;

                case Tmb.Entries.C010 face:
                    face.SetDuration(1);
                    face.SetAnimationStart(0f);
                    face.SetAnimationEnd(1f);
                    break;
            }
        }
    }

    // Renames one animation and the C009 timeline entries that repeat its name, stripping the lock when asked.
    private static void RenameAnimation(PapAnimation animation, string name, bool removeAnimationLock, ref int locksRemoved)
    {
        animation.SetName(name);

        foreach (var entry in animation.Tmb?.GetAllC009Entries() ?? [])
            entry.Path.Value = name;

        if (removeAnimationLock)
            locksRemoved += PapAnimationLock.Remove(animation);
    }

    // Chooses the source animation that best answers , allowing one source to serve several names, unlike Match which
    // assigns each source at most once.
    private static int ChooseSourceForName(IReadOnlyList<string> sourceNames, string name)
    {
        if (sourceNames.Count == 0)
            return -1;

        var suffix = Suffix(name);
        if (suffix.Length > 0)
        {
            for (var source = 0; source < sourceNames.Count; ++source)
            {
                if (string.Equals(Suffix(sourceNames[source]), suffix, StringComparison.OrdinalIgnoreCase))
                    return source;
            }
        }

        return 0;
    }

    // Reads the part marker a name ends in past its last underscore.
    private static string Suffix(string name)
    {
        var index = name.LastIndexOf('_');
        return index < 0 || index == name.Length - 1 ? string.Empty : name[(index + 1)..];
    }

    // Confirms produced bytes are safe to return, first re-parsing them structurally with a fresh PapFile (headers,
    // havok blob and every embedded TMB timeline, which a header-only name scan cannot check) and then confirming
    // through Read that every applied name is declared.
    private static void Verify(byte[] result, IReadOnlyList<string> applied)
    {
        try
        {
            _ = new PapFile(new BinaryReader(new MemoryStream(result)));
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "The retargeted .pap failed to read back as a structurally valid .pap; refusing to return it.", ex);
        }

        var declared = PapAnimationNames.Read(result);

        foreach (var name in applied)
        {
            if (!declared.Contains(name))
                throw new InvalidDataException(
                    $"The retargeted .pap does not declare '{name}' after being read back; refusing to return it.");
        }
    }
}
