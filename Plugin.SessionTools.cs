using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace EmoteShelf;

public sealed partial class Plugin
{
    private long lastVisualSync;

    private void DrawSessionTools()
    {
        ImGui.PushID("sessionTools");
        ImGui.BeginDisabled(lastAcceptedPair is null || pairSession is not null || !LinkFresh);
        if (ImGui.Button(T("Перезапустить принятую анимацию у обоих", "Restart accepted animation on both clients"))) RestartAcceptedPair();
        ImGui.EndDisabled();
        ImGui.BeginDisabled(!LinkFresh || LinkedPartner is null || pairSession is not null);
        if (ImGui.Button(T("Синхронизировать видимую пару", "Sync visible pair"))) SyncVisiblePair();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(T("Сбрасывает время циклических анимаций только на твоём экране. Не меняет чужой клиент и не перематывает музыку.",
                "Resets looping animations on your screen only. It does not change your partner's client or rewind music."));
        if (ImGui.Button(T("Подстроить положение (Simple Heels)", "Adjust position (Simple Heels)"))) OpenHeels(true);
        ImGui.SameLine();
        if (ImGui.Button("Livepose##heels")) OpenHeels(false);
        if (ImGui.Button(T("Открыть Brio (GPose)", "Open Brio (GPose)"))) OpenIntegration("Brio", "/brio");
        if (ImGui.Button(T("Вернуть настройки Penumbra", "Restore Penumbra settings"))) RestoreShelfSelection();
        ImGui.PopID();
    }

    private void OpenHeels(bool offset)
    {
        // Only fixed commands owned by the installed plugin, never arbitrary user text.
        if (!Pi.InstalledPlugins.Any(p => p.InternalName == "SimpleHeels" && p.IsLoaded) ||
            !Commands.ProcessCommand(offset ? "/heels temp" : "/heels livepose"))
        {
            status = T("Simple Heels недоступен. Установи и включи его для подстройки положения.",
                "Simple Heels is unavailable. Install and enable it to adjust position.");
            settingsOpen = true;
        }
    }

    private unsafe void SyncVisiblePair()
    {
        var local = Objects.LocalPlayer;
        var partner = LinkedPartner;
        if (Environment.TickCount64 - lastVisualSync < 1000) return;
        if (!config.PairEnabled || !LinkFresh || local is null || partner is null || pairSession is not null ||
            System.Numerics.Vector3.Distance(local.Position, partner.Position) > 3 ||
            !CanRewindLoop(local.Address, false) || !CanRewindLoop(partner.Address, false))
        {
            status = T("Для синхронизации оба должны быть рядом и уже выполнять циклическую эмоцию. Дождись завершения подготовки пары.",
                "Both players must be nearby and already playing looping emotes. Wait for pair preparation to finish.");
            settingsOpen = true; return;
        }
        // Re-resolve both actors each click; never retain game pointers across frames.
        CanRewindLoop(local.Address, true);
        CanRewindLoop(partner.Address, true);
        lastVisualSync = Environment.TickCount64;
        status = T("Видимые анимации пары перезапущены на твоём экране. Музыка не перематывалась.",
            "The pair's visible animations restarted on your screen. Music was not rewound.");
        PairDebug("visual_sync actors=2 scope=local music=unchanged", true);
    }

    private static unsafe bool CanRewindLoop(nint address, bool apply)
    {
        if (address == 0) return false;
        var actor = (Character*)address;
        if (actor->Mode is not (CharacterModes.EmoteLoop or CharacterModes.InPositionLoop)) return false;
        var draw = actor->DrawObject;
        if (draw == null || draw->GetObjectType() != ObjectType.CharacterBase) return false;
        if (((CharacterBase*)draw)->GetModelType() != CharacterBase.ModelType.Human) return false;
        var skeleton = ((Human*)draw)->Skeleton;
        if (skeleton == null || skeleton->PartialSkeletonCount == 0) return false;
        var animation = skeleton->PartialSkeletons[0].GetHavokAnimatedSkeleton(0);
        if (animation == null || animation->AnimationControls.Length == 0) return false;
        var control = animation->AnimationControls[0].Value;
        if (control == null) return false;
        if (apply) control->hkaAnimationControl.LocalTime = 0;
        return true;
    }
}
