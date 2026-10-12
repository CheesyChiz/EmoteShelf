namespace EmoteShelf;

public sealed partial class Plugin
{
    private string requestedMissingMod = "", sentMissingMod = "";
    private Bookmark? missingModResume;
    private bool missingModStationary;
    private TransferClient? missingModClient;
    private string missingModPartner = "";
    private bool missingModDance, missingModOffered;
    private uint missingModTerritory;
    private long missingModUntil;

    private void UpdateMissingModTransfer(LaunchInvitation? proposal)
    {
        if (missingModResume is { } resume)
        {
            if (transfers != missingModClient || link?.Snapshot.Partner != missingModPartner ||
                ClientState.TerritoryType != missingModTerritory || Environment.TickCount64 > missingModUntil ||
                transfers?.Error.Length > 0)
                missingModResume = null;
            else if (transfers?.Offer is { Incoming: false }) missingModOffered = true;
            else if (missingModOffered && transfers?.Outcome is { Length: > 0 } outcome)
            {
                missingModResume = null;
                if (outcome == "completed" && proposal is null && pairSession is null)
                {
                    BeginPair(resume, missingModDance, missingModStationary);
                }
            }
        }
        if (proposal is not { Status: "waiting" } request) return;
        if (request.Incoming)
        {
            if (initialScanPending || libraryScan is not null || requestedMissingMod == request.Offer.Id ||
                discovered.Any(m => !IsCarrierCopy(m.Directory) && PairRules.Family(m.Name) == request.Offer.Family)) return;
            requestedMissingMod = request.Offer.Id;
            link?.Act("request_mod", invitation: request.Offer.Id);
            return;
        }
        if (!request.MissingMod || sentMissingMod == request.Offer.Id || pairBookmark is not { } sender ||
            pairOfferId != request.Offer.Id || transfers is null || transfers.Offer is not null ||
            transfers.ProposalPending || transferSending is not null) return;
        // The request is bound to the current invitation: never accept a directory from the peer.
        sentMissingMod = request.Offer.Id;
        var bookmark = SnapshotBookmark(sender);
        var dance = request.Offer.Dance;
        CancelPair(null, false);
        PrepareTransferLaunch(bookmark);
        transferLaunchDraft = ""; transferSenderRole = transferReceiverRole = null;
        settingsOpen = true;
        if (config.TransferNoticeVersion != 1) return; // First-use redistribution acknowledgement remains mandatory.
        try
        {
            var client = transfers;
            var root = getRoot.Invoke();
            var directory = bookmark.ModDirectory;
            var name = ModPackage.DisplayFileName(getMods.Invoke()[directory]);
            missingModResume = bookmark; missingModClient = client; missingModPartner = transferPartner;
            missingModTerritory = ClientState.TerritoryType; missingModDance = dance;
            missingModStationary = request.Offer.InviteOnly;
            missingModOffered = false; missingModUntil = Environment.TickCount64 + 660000;
            transferSending = Task.Run(async () => await client.ProposePackage(ModPackage.PackMod(root, directory), name, ""));
        }
        catch (Exception ex) { missingModResume = null; TransferFailure(ex.Message); }
    }
}
