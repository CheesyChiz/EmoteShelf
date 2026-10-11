using Dalamud.Bindings.ImGui;

namespace EmoteShelf;

public sealed partial class Plugin
{
    private int presetFirst, presetSecond;
    private string presetName = "";

    private static Bookmark SnapshotBookmark(Bookmark b) => new()
    {
        Name = b.Name, ModDirectory = b.ModDirectory, Command = b.Command, IconId = b.IconId,
        PoseIndex = b.PoseIndex, RoleLabel = b.RoleLabel, Offset = b.Offset, BrioPose = b.BrioPose,
        SavedOptions = b.SavedOptions?.ToDictionary(x => x.Key, x => x.Value.ToList()),
    };

    private string RoleName(Bookmark b) => string.IsNullOrWhiteSpace(b.RoleLabel) ? b.Name : b.RoleLabel;

    private bool HandleLibraryCommand(string args)
    {
        var (verb, argument) = LibraryCommands.Parse(args);
        if (verb is not ("play" or "pair" or "sync" or "offset" or "livepose")) return false;
        try
        {
            if (verb == "sync") { SyncVisiblePair(); return true; }
            if (verb is "offset" or "livepose") { OpenHeels(verb == "offset"); return true; }
            var matches = config.Bookmarks.Where(b => LibraryCommands.ValidAlias(b.Alias ?? "") &&
                string.Equals(b.Alias, argument, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(T("Укажи уникальную команду закладки из вкладки «Панель».", "Use a unique bookmark alias from the Panel tab."));
            if (verb == "pair") BeginPair(SnapshotBookmark(matches[0]));
            else Play(SnapshotBookmark(matches[0]));
        }
        catch (Exception ex) { status = ex.Message; settingsOpen = true; Log.Warning("Library command failed: {Type}", ex.GetType().Name); }
        return true;
    }

    private void DrawBookmarkTools(Bookmark b, int index)
    {
        ImGui.PushID("bookmarkTools" + index);
        if (ImGui.SmallButton("...##edit")) ImGui.OpenPopup("bookmarkEditor");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(T("Роль и команда", "Role and command"));
        if (ImGui.BeginPopup("bookmarkEditor"))
        {
            b.RoleLabel ??= ""; b.Alias ??= "";
            DrawOffsetBookmark(b);
            DrawBrioBookmark(b);
            if (ImGui.InputText(T("Название роли", "Role label"), ref b.RoleLabel, 80)) Save();
            if (ImGui.InputText(T("Команда закладки", "Bookmark alias"), ref b.Alias, 32)) Save();
            var valid = LibraryCommands.ValidAlias(b.Alias) && config.Bookmarks.Count(x =>
                string.Equals(x.Alias, b.Alias, StringComparison.OrdinalIgnoreCase)) == 1;
            if (valid) ImGui.TextDisabled("/es play " + b.Alias + "   /es pair " + b.Alias);
            else ImGui.TextWrapped(T("Команда: латинские буквы, цифры, - и _. Название должно быть уникальным.", "Alias: letters A-Z, digits, - and _. Each alias must be unique."));
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private void DrawPairLibrary()
    {
        if (!ImGui.CollapsingHeader(T("Парные наборы", "Pair presets"))) return;
        ImGui.TextWrapped(T("Сохрани две закладки одного мода как роли. Настройки копируются в набор; партнёр по-прежнему подтверждает запуск и выбирает свою локальную роль.",
            "Save two bookmarks of the same mod as roles. Their options are copied into the preset; your partner still accepts and chooses a local role."));
        DrawSessionTools();
        if (config.Bookmarks.Count >= 2)
        {
            presetFirst = Math.Clamp(presetFirst, 0, config.Bookmarks.Count - 1);
            presetSecond = Math.Clamp(presetSecond, 0, config.Bookmarks.Count - 1);
            Choose(T("Первая роль", "First role"), ref presetFirst);
            Choose(T("Вторая роль", "Second role"), ref presetSecond);
            ImGui.InputText(T("Название набора", "Preset name"), ref presetName, 80);
            var a = config.Bookmarks[presetFirst]; var b = config.Bookmarks[presetSecond];
            var valid = presetName.Trim().Length > 0 && presetFirst != presetSecond &&
                a.ModDirectory == b.ModDirectory && OfferRole(a) != OfferRole(b);
            ImGui.BeginDisabled(!valid);
            if (ImGui.Button(T("Сохранить набор", "Save preset")))
            {
                config.PairPresets.Add(new() { Name = presetName.Trim(), First = SnapshotBookmark(a), Second = SnapshotBookmark(b) });
                presetName = ""; Save();
            }
            ImGui.EndDisabled();
        }
        for (var i = 0; i < config.PairPresets.Count; i++)
        {
            var p = config.PairPresets[i]; ImGui.PushID("pairPreset" + i);
            ImGui.TextUnformatted(p.Name);
            ImGui.PushID("firstOffset"); DrawOffsetBookmark(p.First); ImGui.PopID();
            ImGui.PushID("secondOffset"); DrawOffsetBookmark(p.Second); ImGui.PopID();
            if (ImGui.Button(RoleName(p.First) + "##first")) BeginPair(SnapshotBookmark(p.First));
            if (ImGui.Button(RoleName(p.Second) + "##second")) BeginPair(SnapshotBookmark(p.Second));
            if (ImGui.SmallButton(T("Удалить набор", "Delete preset"))) { config.PairPresets.RemoveAt(i--); Save(); }
            ImGui.PopID();
        }
        void Choose(string label, ref int selected)
        {
            if (!ImGui.BeginCombo(label, RoleName(config.Bookmarks[selected]))) return;
            for (var n = 0; n < config.Bookmarks.Count; n++)
                if (ImGui.Selectable(RoleName(config.Bookmarks[n]) + "##" + n, selected == n)) selected = n;
            ImGui.EndCombo();
        }
    }

    private void DrawSavedOfferRoles(LaunchInvitation request)
    {
        if (!config.PairPresets.Any(p =>
            (OfferRole(p.Second) == request.Offer.Role && offerMods.Any(m => m.Directory == p.First.ModDirectory && m.Command == p.First.Command)) ||
            (OfferRole(p.First) == request.Offer.Role && offerMods.Any(m => m.Directory == p.Second.ModDirectory && m.Command == p.Second.Command)))) return;
        if (!ImGui.BeginCombo(T("Сохранённая роль", "Saved role"), T("Выбрать…", "Choose…"))) return;
        for (var presetIndex = 0; presetIndex < config.PairPresets.Count; presetIndex++)
        {
            var p = config.PairPresets[presetIndex];
            for (var side = 0; side < 2; side++)
            {
                var chosen = side == 0 ? p.First : p.Second;
                var opposite = side == 0 ? p.Second : p.First;
                if (OfferRole(opposite) != request.Offer.Role) continue;
                var index = Array.FindIndex(offerMods, m => m.Directory == chosen.ModDirectory && m.Command == chosen.Command);
                if (index < 0) continue;
                if (ImGui.Selectable(p.Name + " — " + RoleName(chosen) + "##" + presetIndex + "-" + side))
                { offerModIndex = index; offerDraft = SnapshotBookmark(chosen); offerEditorError = ""; }
            }
        }
        ImGui.EndCombo();
    }
}
