namespace EmoteShelf;

public sealed partial class Plugin
{
    private unsafe bool CanRestartActor(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter actor)
        => !actor.IsDead && !actor.IsCasting && !Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat] &&
           !Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounted] &&
           !Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas] &&
           !Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas51] &&
           !Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.Jumping] &&
           ((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)actor.Address)->Mode is
               FFXIVClientStructs.FFXIV.Client.Game.Character.CharacterModes.EmoteLoop or
               FFXIVClientStructs.FFXIV.Client.Game.Character.CharacterModes.InPositionLoop;
    private sealed record AcceptedPair(Bookmark Role, string Id, string Family, string Partner, uint Territory, long Until);
    private AcceptedPair? queuedTransferPair, lastAcceptedPair;
    private bool pairRestart;
    private string pairFamily = "";
    private string automaticPairHandled = "";
    private uint acceptedEmoteId;
    private long acceptedEmoteCheckAfter;

    private unsafe bool StillPlayingAcceptedEmote()
    {
        if (Objects.LocalPlayer is not { } player) return false;
        var actor = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)player.Address;
        return CanRestartActor(player) && actor->EmoteController.EmoteId == acceptedEmoteId;
    }

    private void UpdateAutomaticPair()
    {
        if (!LinkFresh || link?.Snapshot.State != "linked")
        { queuedTransferPair = null; lastAcceptedPair = null; return; }
        var proposal = link.Snapshot.Launch;
        UpdateMissingModTransfer(proposal);
        if (pairSession is null && proposal is null && lastAcceptedPair is not null &&
            Environment.TickCount64 >= acceptedEmoteCheckAfter && !StillPlayingAcceptedEmote())
            lastAcceptedPair = null;
        if (pairSession is not null || proposal is null || proposal.Status != "accepted" || automaticPairHandled == proposal.Offer.Id) return;
        AcceptedPair? agreed = null;
        if (queuedTransferPair is { } transfer && transfer.Id == proposal.Offer.Id) agreed = transfer;
        else if (proposal.Previous.Length > 0 && lastAcceptedPair is { } prior && prior.Id == proposal.Previous) agreed = prior;
        if (agreed is null)
        {
            if (proposal.Previous.Length > 0)
            {
                automaticPairHandled = proposal.Offer.Id;
                Log.Warning("Shared restart rejected: accepted local playback is no longer available.");
                link.Act("cancel_launch", invitation: proposal.Offer.Id);
            }
            return;
        }
        if (proposal.Previous.Length > 0 && !StillPlayingAcceptedEmote())
        {
            automaticPairHandled = proposal.Offer.Id;
            link.Act("cancel_launch", invitation: proposal.Offer.Id);
            lastAcceptedPair = null;
            return;
        }
        if (agreed.Partner != link.Snapshot.Partner || agreed.Territory != ClientState.TerritoryType ||
            Environment.TickCount64 > agreed.Until || agreed.Family != proposal.Offer.Family)
        {
            link.Act("cancel_launch", invitation: proposal.Offer.Id);
            queuedTransferPair = null; lastAcceptedPair = null;
            return;
        }
        queuedTransferPair = null;
        automaticPairHandled = proposal.Offer.Id;
        BeginPairCore(SnapshotBookmark(agreed.Role), proposal.Offer.Id, agreed.Family, proposal.Previous.Length > 0, proposal.Offer.Dance, proposal.Offer.InviteOnly);
    }

    private void RememberPairPlayback()
    {
        acceptedEmoteId = catalog?.IdFor(pairHeldCommand) ?? 0;
        acceptedEmoteCheckAfter = Environment.TickCount64 + 2500;
        if (pairBookmark is not null && link is not null)
            lastAcceptedPair = new(SnapshotBookmark(pairBookmark), pairOfferId, pairFamily,
                link.Snapshot.Partner, ClientState.TerritoryType, Environment.TickCount64 + 1800000);
    }

    private void RestartAcceptedPair()
    {
        if (lastAcceptedPair is not { } accepted || !LinkFresh || link is null ||
            accepted.Partner != link.Snapshot.Partner || accepted.Territory != ClientState.TerritoryType ||
            Environment.TickCount64 > accepted.Until || pairSession is not null) return;
        link.Act("restart");
    }
}
