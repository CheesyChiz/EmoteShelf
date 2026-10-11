using System.Collections.Generic;

namespace NoireLib.Animations.PapFormat.Tmb;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public class Tmal : TmbItem
{
    public override string Magic => "TMAL";
    public override int Size => 0x10;
    public override int ExtraSize => 0;

    public readonly List<Tmac> Actors = [];
    private readonly List<int> TempIds;

    public Tmal(TmbFile file, TmbReader reader) : base(file, reader)
    {
        TempIds = reader.ReadOffsetTimeline();
    }

    public override void Write(TmbWriter writer)
    {
        base.Write(writer);
        writer.WriteOffsetTimeline(Actors);
    }

    public void PickActors(TmbReader reader)
    {
        Actors.AddRange(reader.Pick<Tmac>(TempIds));
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
