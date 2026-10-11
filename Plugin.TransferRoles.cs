using Dalamud.Bindings.ImGui;
namespace EmoteShelf;
public sealed partial class Plugin
{
    private void DrawTransferRoleOptions()
    {
        if (transferReceiverRole is not { } role) return;
        var mod = discovered.FirstOrDefault(m => m.Directory == role.ModDirectory && m.Command == role.Command);
        if (mod is null) return;
        ImGui.TextWrapped(T("Настрой роль для персонажа партнёра. Закладка не нужна; одинаковый набор у обоих отправить нельзя.", "Choose options for your partner's character. No bookmark is needed; both roles cannot use the identical selection."));
        role.SavedOptions ??= [];
        ImGui.PushID("transferRoleOptions");
        foreach (var group in mod.Variants.GroupBy(v => v.Group))
        {
            ImGui.PushID(group.Key);
            var chosen = role.SavedOptions.GetValueOrDefault(group.Key, []);
            if (group.First().Multi)
            {
                if (ImGui.TreeNode(group.Key))
                {
                    foreach (var option in group)
                    {
                        var enabled = chosen.Contains(option.Option);
                        if (ImGui.Checkbox(option.Option, ref enabled))
                        { var updated = chosen.ToList(); if (enabled) updated.Add(option.Option); else updated.Remove(option.Option); role.SavedOptions[group.Key] = updated; }
                    }
                    ImGui.TreePop();
                }
            }
            else if (ImGui.BeginCombo(group.Key, chosen.FirstOrDefault() ?? "—"))
            {
                foreach (var option in group)
                    if (ImGui.Selectable(option.Option, chosen.Contains(option.Option))) role.SavedOptions[group.Key] = [option.Option];
                ImGui.EndCombo();
            }
            ImGui.PopID();
        }
        if (mod.PoseSlots.Length > 0 && ImGui.BeginCombo(T("Слот: ", "Slot: "), role.PoseIndex?.ToString("00") ?? "—"))
        {
            foreach (var pose in mod.PoseSlots) if (ImGui.Selectable(pose.ToString("00"))) role.PoseIndex = pose;
            ImGui.EndCombo();
        }
        role.Name = mod.EmoteName;
        ImGui.PopID();
    }
}
