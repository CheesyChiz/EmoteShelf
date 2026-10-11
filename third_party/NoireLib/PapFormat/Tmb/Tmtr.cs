using NoireLib.Animations.PapFormat.Tmb.Entries;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Animations.PapFormat.Tmb;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public class Tmtr : TmbItemWithTime
{
    public override string Magic => "TMTR";
    public override int Size => 0x18;
    public override int ExtraSize => Condition.Length;

    public readonly List<TmbEntry> Entries = [];
    private List<int> TempIds = [];

    /// <summary> Where this track's condition block sits in the source file, or 0 when it has none. </summary>
    public int ConditionOffset { get; private set; }

    /// <summary> Where the block starts in the whole timeline's bytes. </summary>
    public long ConditionPosition { get; private set; }

    /// <summary> The track's condition block, kept whole because nothing here reads its contents. </summary>
    public byte[] Condition { get; private set; } = [];

    /// <summary> Hands the track the bytes its condition offset pointed at. </summary>
    /// <param name="condition">The block's bytes.</param>
    public void SetCondition(byte[] condition) => Condition = condition;

    // The header of a condition block: a word of its own, then the number of steps that follow.
    private const int ConditionHeaderLength = 8;

    // Every step of a condition block is the same length.
    private const int ConditionStepLength = 12;

    /// <summary>
    /// How long the condition block starting at <paramref name="start"/> says it is.
    /// </summary>
    /// <param name="timelineBytes">The whole timeline's bytes.</param>
    /// <param name="start">Where the block starts in them.</param>
    /// <returns>The block's length in bytes, or 0 when the bytes there cannot say.</returns>
    public static int DeclaredConditionLength(byte[] timelineBytes, int start)
    {
        if (start < 0 || start + ConditionHeaderLength > timelineBytes.Length)
            return 0;

        var steps = BinaryPrimitives.ReadInt32LittleEndian(timelineBytes.AsSpan(start + 4, sizeof(int)));

        return steps < 0 ? 0 : ConditionHeaderLength + steps * ConditionStepLength;
    }

    public Tmtr(TmbFile file) : base(file) { }

    public Tmtr(TmbFile file, TmbReader reader) : base(file, reader)
    {
        TempIds = reader.ReadOffsetTimeline();

        // The condition block gates whether the track runs at all, so one .pap can carry a variant per race and
        // play only the matching one. Nothing points at its end, so the file slices it later.
        ConditionOffset = reader.ReadInt32();
        ConditionPosition = ConditionOffset == 0 ? 0 : reader.StartPosition + 8 + ConditionOffset;
    }

    public void PickEntries(TmbReader reader)
    {
        Entries.AddRange(reader.Pick<TmbEntry>(TempIds));
    }

    public override void Write(TmbWriter writer)
    {
        base.Write(writer);
        writer.WriteOffsetTimeline(Entries);

        if (Condition.Length == 0)
        {
            writer.Write(0);
            return;
        }

        writer.WriteExtra(extra => extra.Write(Condition));
    }

    public List<C009> GetC009Entries()
    {
        return Entries.OfType<C009>().ToList();
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
