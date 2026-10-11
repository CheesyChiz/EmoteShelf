using Dalamud.Bindings.ImGui;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;
using System.Numerics;

namespace EmoteShelf;

public sealed partial class Plugin
{
    private readonly GetAvailableModSettings availableSettings = new(Pi);
    private readonly OpenMainWindow openPenumbra = new(Pi);
    private int libraryFilter;
    private string inspectedDirectory = "";
    private string inspection = "";

    private bool IntegrationLoaded(string name) => Pi.InstalledPlugins.Any(p =>
        p.InternalName.Equals(name, StringComparison.OrdinalIgnoreCase) && p.IsLoaded);

    private void DrawDependencies()
    {
        if (!ImGui.CollapsingHeader(T("Нужные плагины", "Required plugins"))) return;
        ImGui.TextWrapped(T("Скопируй репозиторий, открой настройки Dalamud → Experimental → Custom Plugin Repositories, вставь адрес, нажми + и сохрани. Затем нажми «Найти плагин» и установи его. Не добавляй повторно уже подключённый репозиторий.", "Copy the repository, open Dalamud settings → Experimental → Custom Plugin Repositories, paste the URL, press + and save. Then choose Find plugin and install it. Do not add a repository twice."));
        Dependency("Penumbra", T("Библиотека и запуск", "Library and playback"), IntegrationLoaded("Penumbra"), "https://raw.githubusercontent.com/xivdev/Penumbra/master/repo.json", "https://github.com/xivdev/Penumbra");
        Dependency("Lightless Sync", T("Запуск в паре", "Pair launch"), LightlessLoaded(), "https://repo.lightless-sync.org/", "https://git.lightless-sync.org/Lightless-Sync/LightlessClient");
        Dependency("SimpleHeels", T("Подстройка положения", "Position adjustment"), IntegrationLoaded("SimpleHeels"), "https://repo.caraxian.com", "https://github.com/Caraxi/SimpleHeels");
        Dependency("Brio", "GPose", IntegrationLoaded("Brio"), "https://raw.githubusercontent.com/Etheirys/WorldOfEtheirys/main/repo.json", "https://github.com/Etheirys/Brio");
        ImGui.TextWrapped(T("Каждый плагин нужен только для своей функции. Отсутствие Brio или SimpleHeels не мешает обычному запуску анимаций.",
            "Each plugin is required only for its feature. Missing Brio or SimpleHeels does not block ordinary animation playback."));
        void Dependency(string name, string purpose, bool ready, string repository, string project)
        {
            ImGui.PushID(name);
            ImGui.Separator();
            ImGui.TextColored(ready ? new Vector4(.5f, .85f, .65f, 1) : new Vector4(1, .7f, .4f, 1),
                name + " — " + (ready ? T("Загружен", "Loaded") : T("Нужно установить или включить", "Install or enable")));
            ImGui.SameLine(); ImGui.TextDisabled(purpose);
            if (ImGui.SmallButton(T("Скопировать репозиторий", "Copy repository"))) ImGui.SetClipboardText(repository);
            ImGui.SameLine();
            if (ImGui.SmallButton(T("Настройки Dalamud", "Dalamud settings"))) Pi.OpenDalamudSettingsTo(Dalamud.Interface.SettingsOpenKind.Experimental);
            if (ImGui.SmallButton(T("Найти плагин", "Find plugin"))) Pi.OpenPluginInstallerTo(Dalamud.Interface.PluginInstallerOpenKind.AllPlugins, name);
            ImGui.SameLine();
            if (ImGui.SmallButton(T("Сайт проекта", "Project website"))) Dalamud.Utility.Util.OpenLink(project);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(project);
            ImGui.TextWrapped(repository);
            ImGui.PopID();
        }
    }

    private void OpenIntegration(string name, string command)
    {
        try
        {
            if (!IntegrationLoaded(name) || !Commands.ProcessCommand(command))
                throw new InvalidOperationException(string.Format(T("Для этой функции установи и включи {0}.", "Install and enable {0} for this feature."), name));
        }
        catch (Exception ex)
        {
            status = ex.Message; settingsOpen = true;
            Log.Warning("Integration open failed: plugin={Plugin} exception={Type}", name, ex.GetType().Name);
        }
    }

    private void ValidateBookmark(Bookmark bookmark, EmoteMod mod)
    {
        var available = availableSettings.Invoke(mod.Directory)
            ?? throw new InvalidOperationException(T("Не удалось прочитать варианты мода. Проверь Penumbra.", "Could not read mod options. Check Penumbra."));
        var missing = BookmarkValidation.MissingOptions(bookmark.SavedOptions,
            available.ToDictionary(x => x.Key, x => x.Value.Item1));
        if (missing.Length > 0)
            throw new InvalidOperationException(string.Format(T("Набор устарел: больше нет {0}. Выбери варианты заново и пересохрани закладку.",
                "This preset needs review: {0} no longer exists. Choose the options again and save a new bookmark."), string.Join(", ", missing)));
        if (bookmark.PoseIndex is { } pose && mod.PoseSlots.Length > 0 && !mod.PoseSlots.Contains(pose))
            throw new InvalidOperationException(T("Сохранённого слота позы больше нет. Выбери позу заново и пересохрани закладку.",
                "The saved pose slot no longer exists. Choose the pose again and save a new bookmark."));
    }

    private void RestoreShelfSelection()
    {
        CancelCarrierPlayback();
        RestoreOffset();
        lastAcceptedPair = null; queuedTransferPair = null;
        // Stop queued work before removal, or a deferred preparation could reapply the override.
        CancelPair(); waitingForStand = null; pendingCommand = ""; pendingPoseIndex = -1;
        manualPoseBookmark = null; launchingBookmark = null;
        pendingModDirectory = ""; pendingOptions = null;
        pendingExpectedPaths = []; pendingExpectedFiles.Clear();
        try
        {
            if (activeCollection != Guid.Empty)
            {
                var result = removeTemporary.Invoke(activeCollection, TemporaryKey);
                if (result is not (PenumbraApiEc.Success or PenumbraApiEc.NothingChanged))
                    throw new InvalidOperationException("Penumbra: " + result);
            }
            activeCollection = Guid.Empty;
            if (Objects.LocalPlayer is { } player) redraw.Invoke(player.ObjectIndex, RedrawType.Redraw);
            status = T("Временные настройки сняты.", "Temporary settings cleared.");
            inspection = "";
        }
        catch (Exception ex) { status = ex.Message; Log.Warning("Restore failed: {Type}", ex.GetType().Name); }
    }

    private void DrawLibraryFilter()
    {
        var labels = new[] { T("Все", "All"), T("Idle и сидячие позы", "Idle and sitting poses"),
            T("На панели", "On the panel"), T("В парных наборах", "In pair presets"),
            T("Со звуковыми файлами", "Contains sound files"), T("С VFX", "Contains VFX") };
        ImGui.SetNextItemWidth(210);
        if (ImGui.BeginCombo(T("Фильтр", "Filter"), labels[libraryFilter]))
        {
            for (var i = 0; i < labels.Length; i++)
                if (ImGui.Selectable(labels[i], libraryFilter == i)) libraryFilter = i;
            ImGui.EndCombo();
        }
    }

    private bool MatchesLibraryFilter(EmoteMod mod) => !IsCarrierCopy(mod.Directory) && (libraryFilter switch
    {
        1 => IsPoseCommand(mod.Command),
        2 => config.Bookmarks.Any(b => b.ModDirectory == mod.Directory && b.Command == mod.Command),
        3 => config.PairPresets.Any(p => SameMod(p.First, mod) || SameMod(p.Second, mod)),
        4 => mod.HasSound,
        5 => mod.HasVfx,
        _ => true,
    });

    private static bool SameMod(Bookmark b, EmoteMod mod) => b.ModDirectory == mod.Directory && b.Command == mod.Command;

    private void DrawModInspection(EmoteMod mod)
    {
        if (inspectedDirectory != mod.Directory) { inspection = ""; inspectedDirectory = mod.Directory; }
        if (ImGui.Button(T("Проверить применение", "Check application"))) InspectMod(mod);
        ImGui.SameLine();
        if (ImGui.Button(T("Открыть в Penumbra", "Open in Penumbra")))
        {
            try { var result = openPenumbra.Invoke(TabType.Mods, mod.Directory); if (result != PenumbraApiEc.Success) status = "Penumbra: " + result; }
            catch (Exception ex) { status = ex.Message; }
        }
        if (inspection.Length > 0) ImGui.TextWrapped(inspection);
    }

    private void InspectMod(EmoteMod mod)
    {
        try
        {
            if (Objects.LocalPlayer is not { } player) throw new InvalidOperationException(T("Персонаж не в игре.", "Character is not in game."));
            var (valid, _, collection) = getCollection.Invoke(player.ObjectIndex);
            if (!valid || collection.Id == Guid.Empty) throw new InvalidOperationException(T("Коллекция не найдена.", "Collection not found."));
            var (ec, current) = getSettingsWithTemp.Invoke(collection.Id, mod.Directory, "", false, false, TemporaryKey);
            if (ec != PenumbraApiEc.Success || current is null) throw new InvalidOperationException("Penumbra: " + ec);
            var state = current.Value;
            var lines = new List<string> { string.Format(T("Коллекция: {0}", "Collection: {0}"), collection.Name),
                state.Item1 ? T("Мод включён для тебя.", "The mod is enabled for you.") : T("Мод выключен для тебя.", "The mod is disabled for you."),
                string.Format(T("Приоритет: {0}", "Priority: {0}"), state.Item2) };
            if (state.Item5) lines.Add(T("Действуют временные настройки.", "Temporary settings are active."));
            var root = getRoot.Invoke();
            var mods = getMods.Invoke();
            var paths = discovered.Where(m => m.Directory == mod.Directory).SelectMany(m => m.BasePaths.Concat(
                m.Variants.Where(v => state.Item3.TryGetValue(v.Group, out var values) && values.Contains(v.Option))
                    .SelectMany(v => v.Paths))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var owned = 0; var outside = 0; var missing = 0;
            var owners = new HashSet<string>();
            foreach (var path in paths)
            {
                var pathEc = resolvePath.Invoke(collection.Id, path, out var resolved);
                if (pathEc != PenumbraApiEc.Success) throw new InvalidOperationException("Penumbra resolve: " + pathEc);
                if (ResolvedAnimation.BelongsToMod(Path.Combine(root, mod.Directory), resolved)) owned++;
                else
                {
                    outside++;
                    var owner = mods.FirstOrDefault(m => ResolvedAnimation.BelongsToMod(Path.Combine(root, m.Key), resolved));
                    owners.Add(owner.Value ?? T("Игра или другой источник", "Game or another source"));
                }
                if (Path.IsPathRooted(resolved) && !File.Exists(resolved)) missing++;
            }
            lines.Add(string.Format(T("Файлы анимаций: своих {0}, из других источников {1}, отсутствует {2}.",
                "Animation files: selected mod {0}, other sources {1}, missing {2}."), owned, outside, missing));
            if (owners.Count > 0) lines.Add(string.Join(", ", owners));
            lines.Add(T("Проверка текущих файлов, не гарантия загрузки у партнёра. После изменений проверь ещё раз.",
                "This checks current files, not your partner's download status. Check again after changes."));
            inspection = string.Join("\n", lines);
        }
        catch (Exception ex) { inspection = ex.Message; Log.Warning("Mod inspection failed: {Type}", ex.GetType().Name); }
    }
}
