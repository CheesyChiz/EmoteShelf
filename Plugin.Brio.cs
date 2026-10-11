using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;

namespace EmoteShelf;

public sealed partial class Plugin
{
    private string? brioUndo;
    private ulong brioUndoActor;
    private uint brioUndoTerritory;

    private IGameObject BrioTarget()
    {
        if (!ClientState.IsGPosing || !IntegrationLoaded("Brio") ||
            Pi.GetIpcSubscriber<(int, int)>("Brio.ApiVersion").InvokeFunc().Item1 != 3 ||
            Targets.Target is not { } actor)
            throw new InvalidOperationException("brio_gpose_target_required");
        return actor;
    }
    private string ReadBrioPose(IGameObject actor)
    {
        var json = Pi.GetIpcSubscriber<IGameObject, string>("Brio.GetPoseAsJson.V3").InvokeFunc(actor);
        if (string.IsNullOrEmpty(json) || json.Length > 262144) throw new InvalidOperationException("brio_pose_invalid");
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return json;
    }
    private void DrawBrioBookmark(Bookmark bookmark)
    {
        ImGui.BeginDisabled(!ClientState.IsGPosing || !IntegrationLoaded("Brio"));
        if (ImGui.SmallButton(T("Сохранить позу цели Brio", "Save Brio target pose")))
        {
            try { bookmark.BrioPose = ReadBrioPose(BrioTarget()); Save(); }
            catch (Exception ex) { BrioFailure(ex); }
        }
        if (bookmark.BrioPose is { Length: > 0 } saved)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(T("Применить позу к цели GPose", "Apply pose to GPose target")))
            {
                try
                {
                    var actor = BrioTarget();
                    var previous = ReadBrioPose(actor);
                    if (saved.Length > 262144 || !Pi.GetIpcSubscriber<IGameObject, bool, string, bool>("Brio.LoadPoseFromJson.V3").InvokeFunc(actor, false, saved))
                        throw new InvalidOperationException("brio_apply_failed");
                    brioUndo = previous; brioUndoActor = actor.GameObjectId; brioUndoTerritory = ClientState.TerritoryType;
                }
                catch (Exception ex) { BrioFailure(ex); }
            }
        }
        if (brioUndo is not null && ImGui.SmallButton(T("Отменить применение позы", "Undo pose application")))
        {
            try
            {
                var actor = BrioTarget();
                if (actor.GameObjectId != brioUndoActor || ClientState.TerritoryType != brioUndoTerritory ||
                    !Pi.GetIpcSubscriber<IGameObject, bool, string, bool>("Brio.LoadPoseFromJson.V3").InvokeFunc(actor, false, brioUndo))
                    throw new InvalidOperationException("brio_undo_target_changed");
                brioUndo = null;
            }
            catch (Exception ex) { BrioFailure(ex); }
        }
        ImGui.EndDisabled();
    }
    private void BrioFailure(Exception ex)
    {
        status = T("Нужен Brio с API 3 и выбранный актёр в GPose. Проверь, что IPC включён в Brio.", "Requires Brio API 3 and a selected GPose actor. Check that IPC is enabled in Brio.");
        Log.Warning("Brio operation failed: {Type}", ex.GetType().Name);
    }
}
