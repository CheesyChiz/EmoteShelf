using System;

namespace NoireLib.Animations.PapFormat;

/// <summary>
/// Reads and strips the C125 timeline entries that hold a character in place. Bed pose animations carry them, and a
/// retargeted animation usually needs them gone or the character will not move.
/// </summary>
public static class PapAnimationLock
{
    private const string LockMagic = "C125";

    private static readonly string[] BedPoseFileNames =
    [
        "/bed_liedown_start.pap",
        "/bed_liedown_loop.pap",
        "/l_pose01_start.pap",
        "/l_pose01_loop.pap",
        "/l_pose02_start.pap",
        "/l_pose02_loop.pap",
    ];

    /// <summary>Whether a path names one of the bed or floor pose files that carry the lock.</summary>
    /// <param name="path">The game path to test.</param>
    /// <returns>True when the path names a bed pose file.</returns>
    public static bool IsBedPosePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        return Array.Exists(BedPoseFileNames, normalized.EndsWith);
    }

    /// <summary>Whether an animation's TMB carries a C125 lock entry.</summary>
    /// <param name="animation">The animation to read.</param>
    /// <returns>True when a lock entry is present.</returns>
    public static bool HasLock(PapAnimation animation)
        => animation.Tmb?.AllEntries.Exists(entry => entry.Magic == LockMagic) ?? false;

    /// <summary>Strips every C125 lock entry from an animation's TMB.</summary>
    /// <param name="animation">The animation to strip.</param>
    /// <returns>How many lock entries were removed.</returns>
    public static int Remove(PapAnimation animation)
    {
        if (animation.Tmb == null)
            return 0;

        // Invalidating the layout rebuilds the timeline from the parsed model instead of keeping the source file's
        // own bytes, so it must not happen when there is nothing to remove.
        if (!HasLock(animation))
            return 0;

        animation.Tmb.InvalidateSourceLayout();

        var removed = 0;
        foreach (var actor in animation.Tmb.Actors)
        {
            foreach (var track in actor.Tracks)
                removed += track.Entries.RemoveAll(entry => entry.Magic == LockMagic);
        }

        animation.Tmb.AllEntries.RemoveAll(entry => entry.Magic == LockMagic);
        return removed;
    }
}
