namespace EmoteShelf;

internal static class CarrierSettings
{
    internal static void DisablePersistent(IEnumerable<Guid> collections, Func<Guid, bool> disable,
        Func<Guid, bool, (bool Success, bool? Enabled)> read)
    {
        var ids = collections.Distinct().ToArray();
        foreach (var id in ids)
            if (!disable(id)) throw new InvalidOperationException("carrier_disable_failed");
        foreach (var id in ids)
        {
            // Temporary playback settings may remain enabled until Play safely
            // stops the previous animation. Never clear them just to verify persistence.
            var state = read(id, true);
            if (!state.Success || state.Enabled != false)
                throw new InvalidOperationException("carrier_disable_unconfirmed");
        }
    }
}
