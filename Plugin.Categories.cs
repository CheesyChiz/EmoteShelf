using Dalamud.Bindings.ImGui;

namespace EmoteShelf;

public sealed partial class Plugin
{
    private bool DefaultDance(string directory, string command)
        => DefaultDanceInvitation(directory, command) || EmoteCategory.Dance(config.EmoteDanceModes, directory, command);
    private bool DefaultDanceInvitation(string directory, string command)
        => config.EmoteDanceInvitations.Contains(EmoteCategory.Key(directory, command));

    private string[] CategoryLabels() => [
        T("Парная анимация — в одной точке", "Paired animation — same position"),
        T("Совместный танец — одна эмоция, 2 ялма между вами", "Shared dance — same emote, 2 yalms apart"),
        T("Совместный танец — без перемещения", "Shared dance — no movement")];

    private void DrawEmoteCategory(string directory, string command)
    {
        ImGui.PushID(directory + "|" + command);
        ImGui.TextUnformatted(T("Категория запуска по умолчанию", "Default launch category"));
        ImGui.SetNextItemWidth(-1);
        var labels = CategoryLabels();
        var selected = DefaultDanceInvitation(directory, command) ? 2 : DefaultDance(directory, command) ? 1 : 0;
        if (ImGui.BeginCombo("##category", labels[selected]))
        {
            for (var i = 0; i < labels.Length; i++)
                if (ImGui.Selectable(labels[i], selected == i))
                {
                    var key = EmoteCategory.Key(directory, command);
                    config.EmoteDanceModes[key] = i != 0;
                    if (i == 2) config.EmoteDanceInvitations.Add(key); else config.EmoteDanceInvitations.Remove(key);
                    Save();
                }
            ImGui.EndCombo();
        }
        ImGui.PopID();
    }

    private void DrawAlternateLaunch(EmoteMod mod)
    {
        var selected = DefaultDanceInvitation(mod.Directory, mod.Command) ? 2 : DefaultDance(mod.Directory, mod.Command) ? 1 : 0;
        ImGui.BeginDisabled(mod.Variants.Length > 0 && !draftOptions.ContainsKey(mod.Directory));
        for (var i = 0; i < 3; i++)
        {
            if (i == selected || !ImGui.BeginMenu(CategoryLabels()[i])) continue;
            if (ImGui.MenuItem(T("Запустить один раз в другом режиме", "Launch once in the other mode"))) Run(false, i);
            if (ImGui.MenuItem(T("Передать с запуском в другом режиме", "Send with playback in the other mode"))) Run(true, i);
            ImGui.EndMenu();
        }
        ImGui.EndDisabled();
        void Run(bool transfer, int mode)
        {
            var dance = mode != 0;
            if (mod.Variants.Length > 0) PlayDraft(mod, false, pair: !transfer, transfer: transfer, dance: dance, inviteOnly: mode == 2);
            else try
            {
                var bookmark = CreateBookmark(mod, null);
                if (transfer) PrepareTransferLaunch(bookmark, dance, mode == 2); else BeginPair(bookmark, dance, mode == 2);
            }
            catch (Exception ex) { status = ex.Message; }
        }
    }
}
