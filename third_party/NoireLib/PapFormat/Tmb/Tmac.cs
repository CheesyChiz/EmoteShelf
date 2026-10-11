using NoireLib.Animations.PapFormat.Parsing;
using NoireLib.Animations.PapFormat.Tmb.Entries;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Animations.PapFormat.Tmb;

// Internal wire-format plumbing behind PapFile/TmbFile; not part of the public PapFormat surface.
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public class Tmac : TmbItemWithTime
{
    public override string Magic => "TMAC";
    public override int Size => 0x1C;
    public override int ExtraSize => 0;

    private readonly ParsedInt AbilityDelay = new("Ability Delay", 4, 0);
    private readonly ParsedInt Unk2 = new("Unknown 2", 4, 0);

    public readonly List<Tmtr> Tracks = [];
    private List<int> TempIds = [];

    public Tmac(TmbFile file) : base(file) { }

    public Tmac(TmbFile file, TmbReader reader) : base(file, reader)
    {
        AbilityDelay.Read(reader);
        Unk2.Read(reader);
        TempIds = reader.ReadOffsetTimeline();
    }

    public void PickTracks(TmbReader reader)
    {
        Tracks.AddRange(reader.Pick<Tmtr>(TempIds));
    }

    public override void Write(TmbWriter writer)
    {
        base.Write(writer);
        AbilityDelay.Write(writer);
        Unk2.Write(writer);
        writer.WriteOffsetTimeline(Tracks);
    }

    public List<C009> GetAllC009Entries()
    {
        return Tracks.SelectMany(track => track.GetC009Entries()).ToList();
    }
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
