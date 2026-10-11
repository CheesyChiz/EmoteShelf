using Dalamud.Bindings.ImGui;

namespace EmoteShelf;

public sealed partial class Plugin
{
    private PositionOffset? offsetPrevious, offsetApplied, offsetPending;
    private ulong offsetActor;
    private long offsetDue, offsetAppliedAt;
    private uint offsetTerritory;
    private void PrepareOffset(Bookmark bookmark)
    {
        if (bookmark.Offset is not null)
        {
            if (!bookmark.Offset.Valid || !IntegrationLoaded("SimpleHeels")) throw new InvalidOperationException(T("Не удалось прочитать подстройку SimpleHeels.", "Could not read the SimpleHeels adjustment."));
            offsetPrevious = ReadHeelsOffset();
        }
    }
    private PositionOffset? ReadHeelsOffset() => PositionOffset.Read(Pi.GetIpcSubscriber<string>("SimpleHeels.GetLocalPlayer").InvokeFunc());

    private void DrawOffsetBookmark(Bookmark bookmark)
    {
        ImGui.BeginDisabled(!IntegrationLoaded("SimpleHeels"));
        if (ImGui.SmallButton(T("Запомнить текущую подстройку SimpleHeels", "Save current SimpleHeels adjustment")))
        {
            try { bookmark.Offset = ReadHeelsOffset(); Save(); }
            catch (Exception ex) { Log.Warning("Offset capture failed: {Type}", ex.GetType().Name); status = T("Не удалось прочитать подстройку SimpleHeels.", "Could not read the SimpleHeels adjustment."); }
        }
        ImGui.EndDisabled();
        if (bookmark.Offset is not null)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(T("Убрать подстройку", "Remove adjustment"))) { bookmark.Offset = null; Save(); }
        }
    }

    private void ScheduleOffset(Bookmark? bookmark)
    {
        if (bookmark?.Offset is not { Valid: true } value || Objects.LocalPlayer is not { } player) return;
        offsetPending = value; offsetActor = player.GameObjectId; offsetTerritory = ClientState.TerritoryType;
        offsetDue = Environment.TickCount64 + 500;
    }

    private void UpdateOffset()
    {
        if (!ClientState.IsGPosing) brioUndo = null;
        try
        {
            if ((offsetPending is not null || offsetApplied is not null) &&
                (Objects.LocalPlayer?.GameObjectId != offsetActor || ClientState.TerritoryType != offsetTerritory))
            { offsetPending = offsetApplied = offsetPrevious = null; return; }
            if (offsetPending is { } pending && Environment.TickCount64 >= offsetDue)
            {
                offsetPending = null;
                if (!IntegrationLoaded("SimpleHeels")) throw new InvalidOperationException("heels_unavailable");
                if (!Commands.ProcessCommand(pending.Command())) throw new InvalidOperationException("heels_command_failed");
                offsetApplied = pending; offsetAppliedAt = Environment.TickCount64;
                if (!pending.Near(ReadHeelsOffset())) { RestoreOffset(); throw new InvalidOperationException("heels_apply_unconfirmed"); }
            }
            if (offsetApplied is not null && Environment.TickCount64 - offsetAppliedAt > 1500 &&
                !IsPlayingLoop() && !IsGroundSitting()) RestoreOffset();
        }
        catch (Exception ex) { offsetPending = null; Log.Warning("Offset update failed: {Type}", ex.GetType().Name); status = T("Не удалось применить подстройку SimpleHeels.", "Could not apply the SimpleHeels adjustment."); }
    }

    private void RestoreOffset()
    {
        offsetPending = null;
        if (offsetApplied is null) return;
        try
        {
            if (Objects.LocalPlayer?.GameObjectId == offsetActor && ClientState.TerritoryType == offsetTerritory && IntegrationLoaded("SimpleHeels"))
            {
                var current = ReadHeelsOffset();
                // Never overwrite an adjustment the user changed while the animation was running.
                if (current is null || offsetApplied.Near(current))
                    Commands.ProcessCommand(offsetPrevious?.Command() ?? "/heels temp reset");
            }
        }
        catch (Exception ex) { Log.Warning("Offset restore failed: {Type}", ex.GetType().Name); }
        finally { offsetPrevious = offsetApplied = null; }
    }
}
