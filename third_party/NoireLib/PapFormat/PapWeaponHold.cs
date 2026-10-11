using NoireLib.Animations.PapFormat.Tmb;
using NoireLib.Animations.PapFormat.Tmb.Entries;
using System;
using System.Collections.Generic;
using System.IO;

namespace NoireLib.Animations.PapFormat;

/// <summary>
/// Adds the timeline commands that bring a character's weapons into their hands for the length of an animation
/// and send them back to their stowed point at its end.
/// </summary>
/// <remarks>
/// These commands drive the equipped object's attach point and its own animation, never the character's stance.
/// </remarks>
public static class PapWeaponHold
{
    private const string LogPrefix = "[PapWeaponHold] ";

    private const string ScaleMagic = "C015";

    // Weapon Position: which attach point of the .atch file the object hangs from.
    private const string PositionMagic = "C014";

    // Summon Animation: the weapon timeline the object itself plays as it travels.
    private const string SummonMagic = "C031";

    private const int Enabled = 1;
    private const int ScaleDuration = 10;

    // Object Position and ATCH Object Scale both read 1 in hand and 0 stowed.
    private const int InHand = 1;
    private const int Stowed = 0;

    // WeaponTimeline rows: weapon/active as the draw animation uses it, and weapon/deactive.
    private const int DrawTimelineRow = 119;
    private const int SheatheTimelineRow = 7;

    private const int MainHand = 0;
    private const int OffHand = 1;

    private const short HoldFrame = 0;

    // How often the hold is stated again, in frames. Anything that ends a timeline hands the weapons back, and an
    // overlapping play lands that while this one is still running, so stating it once is not enough.
    private const int ReassertInterval = 10;

    // The entry magics a rewritten timeline is known to carry safely: those with a model of their own, those whose
    // pointing fields StringFieldOffsets and ExtraFloatFieldOffsets describe, and those shown to point nowhere. A
    // magic outside this set may name data by an offset nothing here knows to move.
    private static readonly HashSet<string> RewritableMagics = new(StringComparer.Ordinal)
    {
        "C009", "C010", "C012", "C042", ScaleMagic, PositionMagic, SummonMagic,
    };

    /// <summary>
    /// Returns a copy of a .pap whose every animation brings the character's weapons into their hands.
    /// </summary>
    /// <param name="papBytes">The .pap's bytes, never modified.</param>
    /// <param name="offHand">Whether the character carries a second weapon. A two-handed weapon has none.</param>
    /// <param name="stowAtEnd">
    /// Whether each animation sends the weapons back on its last frame. The game hands them back on its own when
    /// a timeline ends, so leaving this off costs nothing.
    /// </param>
    /// <param name="withTravel">
    /// Whether the weapon plays its own travel animation rather than appearing where it belongs. The command
    /// driving it summons the object it animates, and an effect already in the file can bind itself to a summon,
    /// which shows as a copy of that effect per summon.
    /// </param>
    /// <returns>The rewritten .pap's bytes, verified to read back.</returns>
    /// <exception cref="InvalidDataException">The produced bytes do not read back as a valid .pap.</exception>
    public static byte[] Apply(byte[] papBytes, bool offHand, bool stowAtEnd = true, bool withTravel = false)
    {
        using var reader = new BinaryReader(new MemoryStream(papBytes));
        var pap = new PapFile(reader);

        var objects = new List<int> { MainHand };
        if (offHand)
            objects.Add(OffHand);

        var touched = false;

        foreach (var animation in pap.Animations)
        {
            if (animation.Tmb is not { } timeline || timeline.Actors.Count == 0)
                continue;

            // One animation this cannot rewrite leaves the others held rather than the whole file untouched.
            if (UnrewritableMagic(timeline) is { } blocker)
            {
                NoireLogger.LogDebug($"'{animation.GetName()}' carries a {blocker} entry, which a rewritten "
                    + "timeline cannot be trusted to carry; its weapons are left where they are.", LogPrefix);

                continue;
            }

            HoldThrough(timeline, timeline.Actors[0], objects, stowAtEnd, withTravel);

            timeline.InvalidateSourceLayout();
            timeline.RefreshIds();
            touched = true;
        }

        if (!touched)
            return papBytes;

        var result = pap.ToBytes();
        Verify(result);

        return result;
    }

    // The first entry magic in a timeline that a rewrite cannot be trusted to carry, or null.
    private static string? UnrewritableMagic(TmbFile timeline)
    {
        foreach (var entry in timeline.AllEntries)
        {
            if (!RewritableMagics.Contains(entry.Magic))
                return entry.Magic;
        }

        return null;
    }

    // States the hold on every frame of the beat, for each of the character's weapons. Each command gets a track of
    // its own, as the game's own draw animation gives it.
    private static void HoldThrough(TmbFile timeline, Tmac actor, IReadOnlyList<int> objects, bool stowAtEnd,
        bool withTravel)
    {
        var length = timeline.HeaderTmdh.GetLength();
        var lastFrame = (short)(length - 1);
        var stows = stowAtEnd && lastFrame > HoldFrame;

        foreach (var objectControl in objects)
        {
            var scale = AddTrack(timeline, actor);
            var position = AddTrack(timeline, actor);

            foreach (var frame in HoldFrames(length))
            {
                AddCommand(timeline, scale, ScaleMagic, frame, [ScaleDuration, 0, InHand, objectControl]);
                AddCommand(timeline, position, PositionMagic, frame, [Enabled, 0, InHand, objectControl]);
            }

            if (stows)
            {
                AddCommand(timeline, scale, ScaleMagic, lastFrame, [ScaleDuration, 0, Stowed, objectControl]);
                AddCommand(timeline, position, PositionMagic, lastFrame, [Enabled, 0, Stowed, objectControl]);
            }

            // The travel plays once on the way out and once on the way back: repeating it on the beat would
            // show as the weapon drawing itself over and over.
            if (!withTravel)
                continue;

            var summon = AddTrack(timeline, actor);

            AddCommand(timeline, summon, SummonMagic, HoldFrame,
                [Enabled, 0, (objectControl << 16) | DrawTimelineRow]);

            if (stows)
            {
                AddCommand(timeline, summon, SummonMagic, lastFrame,
                    [Enabled, 0, (objectControl << 16) | SheatheTimelineRow]);
            }
        }
    }

    // The frames the hold is stated on: the first, then the beat up to the animation's end.
    private static IEnumerable<short> HoldFrames(short length)
    {
        yield return HoldFrame;

        for (var frame = HoldFrame + ReassertInterval; frame < length - 1; frame += ReassertInterval)
            yield return (short)frame;
    }

    private static Tmtr AddTrack(TmbFile timeline, Tmac actor)
    {
        var track = new Tmtr(timeline);

        actor.Tracks.Add(track);
        timeline.AllTracks.Add(track);

        return track;
    }

    private static void AddCommand(TmbFile timeline, Tmtr track, string magic, short frame, int[] payload)
    {
        var entry = new TmbEntryRaw(timeline, magic, frame, payload);

        track.Entries.Add(entry);
        timeline.AllEntries.Add(entry);
    }

    private static void Verify(byte[] papBytes)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(papBytes));
            var reread = new PapFile(reader);

            if (reread.Animations.Count == 0)
                throw new InvalidDataException("the rewritten .pap declares no animation.");
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException($"the rewritten .pap does not read back ({ex.Message}).", ex);
        }
    }
}
