using Dalamud.Bindings.ImGui;

namespace EmoteShelf;

public sealed partial class Plugin
{
    private bool DefaultDance(string directory, string command)
        => EmoteCategory.Dance(config.EmoteDanceModes, directory, command);

    private string[] CategoryLabels() => [
        T("Парная анимация — в одной точке", "Paired animation — same position"),
        T("Совместный танец — одна эмоция, 2 ялма между вами", "Shared dance — same emote, 2 yalms apart")];

    private void DrawEmoteCategory(string directory, string command)
    {
        ImGui.PushID(directory + "|" + command);
        ImGui.TextUnformatted(T("Категория запуска по умолчанию", "Default launch category"));
        ImGui.SetNextItemWidth(-1);
        var labels = CategoryLabels();
        var dance = DefaultDance(directory, command);
        if (ImGui.BeginCombo("##category", labels[dance ? 1 : 0]))
        {
            for (var i = 0; i < labels.Length; i++)
                if (ImGui.Selectable(labels[i], dance == (i == 1)))
                { config.EmoteDanceModes[EmoteCategory.Key(directory, command)] = i == 1; Save(); }
            ImGui.EndCombo();
        }
        ImGui.PopID();
    }

    private void DrawAlternateLaunch(EmoteMod mod)
    {
        var dance = !DefaultDance(mod.Directory, mod.Command);
        ImGui.TextWrapped(CategoryLabels()[dance ? 1 : 0]);
        ImGui.BeginDisabled(mod.Variants.Length > 0 && !draftOptions.ContainsKey(mod.Directory));
        if (ImGui.MenuItem(T("Запустить один раз в другом режиме", "Launch once in the other mode"))) Run(false);
        if (ImGui.MenuItem(T("Передать с запуском в другом режиме", "Send with playback in the other mode"))) Run(true);
        ImGui.EndDisabled();
        void Run(bool transfer)
        {
            if (mod.Variants.Length > 0) PlayDraft(mod, false, pair: !transfer, transfer: transfer, dance: dance);
            else try
            {
                var bookmark = CreateBookmark(mod, null);
                if (transfer) PrepareTransferLaunch(bookmark, dance); else BeginPair(bookmark, dance);
            }
            catch (Exception ex) { status = ex.Message; }
        }
    }
}
