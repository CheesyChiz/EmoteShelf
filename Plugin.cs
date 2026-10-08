using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Textures;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;
using System.Numerics;

namespace EmoteShelf;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] private static IDalamudPluginInterface Pi { get; set; } = null!;
    [PluginService] private static ICommandManager Commands { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IObjectTable Objects { get; set; } = null!;
    [PluginService] private static IChatGui Chat { get; set; } = null!;
    [PluginService] private static IDataManager Data { get; set; } = null!;
    [PluginService] private static ITextureProvider Textures { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private const int TemporaryKey = -170025;
    private readonly Configuration config;
    private readonly GetModDirectory getRoot = new(Pi);
    private readonly GetModList getMods = new(Pi);
    private readonly GetChangedItems getChangedItems = new(Pi);
    private readonly GetCollectionForObject getCollection = new(Pi);
    private readonly GetCurrentModSettings getSettings = new(Pi);
    private readonly GetAllModSettings getAllSettings = new(Pi);
    private readonly SetTemporaryModSettings setTemporary = new(Pi);
    private readonly RemoveAllTemporaryModSettings removeTemporary = new(Pi);
    private readonly RedrawObject redraw = new(Pi);
    private List<EmoteMod> discovered = [];
    private bool settingsOpen;
    private string search = "";
    private string status = "Поиск замен эмоций в Penumbra…";
    private Guid activeCollection;
    private string pendingCommand = "";
    private long sendAt;
    private bool initialScanPending = true;
    private string lastCommand = "";

    public Plugin()
    {
        config = Pi.GetPluginConfig() as Configuration ?? new Configuration();
        config.Bookmarks ??= [];
        config.CommandOverrides ??= new(StringComparer.OrdinalIgnoreCase);
        Commands.AddHandler("/eshelf", new CommandInfo(OnCommand) { HelpMessage = "Emote Shelf: /eshelf, /eshelf scan, /eshelf show, /eshelf hide" });
        Pi.UiBuilder.Draw += Draw;
        Pi.UiBuilder.OpenConfigUi += OpenSettings;
        Pi.UiBuilder.OpenMainUi += OpenSettings;
        Framework.Update += Update;
    }

    private void Save() => Pi.SavePluginConfig(config);
    private void OpenSettings() => settingsOpen = true;
    private void OnCommand(string _, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "scan": Scan(); settingsOpen = true; break;
            case "show": config.OverlayVisible = true; Save(); break;
            case "hide": config.OverlayVisible = false; Save(); break;
            default: settingsOpen = !settingsOpen; break;
        }
    }

    private void Scan()
    {
        try
        {
            var root = getRoot.Invoke();
            var mods = getMods.Invoke();
            if (string.IsNullOrWhiteSpace(root) || mods is null) throw new InvalidOperationException("Penumbra недоступна.");
            var catalog = new EmoteCatalog(Data);
            discovered = ModScanner.Scan(root, mods, catalog,
                directory => getChangedItems.Invoke(directory, "").Keys);
            var changedBookmarks = false;
            foreach (var bookmark in config.Bookmarks.Where(b => b.IconId == 0))
            {
                var match = discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory &&
                    m.Command.Equals(bookmark.Command, StringComparison.OrdinalIgnoreCase));
                if (match is null || match.Icon == 0) continue;
                bookmark.IconId = match.Icon;
                changedBookmarks = true;
            }
            if (changedBookmarks) Save();
            status = $"Найдено {discovered.Count} сочетаний мод–эмоция.";
            initialScanPending = false;
        }
        catch (Exception ex)
        {
            status = $"Поиск не удался: {ex.Message}";
            Log.Warning(ex, "Emote Shelf scan failed");
        }
    }

    private void Play(Bookmark bookmark)
    {
        if (!ModScanner.ValidCommand(bookmark.Command)) { status = "У закладки нет корректной команды эмоции."; return; }
        if (Objects.LocalPlayer is not { } player) { status = "Персонаж не в игре."; return; }
        var selected = discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory &&
            m.Command.Equals(bookmark.Command, StringComparison.OrdinalIgnoreCase));
        if (selected is null) { Scan(); selected = discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory &&
            m.Command.Equals(bookmark.Command, StringComparison.OrdinalIgnoreCase)); }
        selected ??= discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory);
        if (selected is null) { status = "Мод не найден в Penumbra; обнови список."; return; }
        try
        {
            var (valid, _, collection) = getCollection.Invoke(player.ObjectIndex);
            if (!valid || collection.Id == Guid.Empty) throw new InvalidOperationException("Не удалось определить коллекцию персонажа.");
            if (activeCollection != Guid.Empty && activeCollection != collection.Id) removeTemporary.Invoke(activeCollection, TemporaryKey);
            removeTemporary.Invoke(collection.Id, TemporaryKey);
            activeCollection = collection.Id;

            // Only touch mods with the same game animation path. Other Penumbra mods remain intact.
            var conflicts = discovered.Where(m => m.Paths.Intersect(selected.Paths, StringComparer.OrdinalIgnoreCase).Any())
                .DistinctBy(m => m.Directory).ToList();
            var states = new List<(EmoteMod Mod, int Priority, IReadOnlyDictionary<string, IReadOnlyList<string>> Options)>();
            foreach (var mod in conflicts)
            {
                var (ec, current) = getSettings.Invoke(collection.Id, mod.Directory, "", false);
                if (ec != PenumbraApiEc.Success || current is null)
                    throw new InvalidOperationException($"Не удалось прочитать настройки {mod.Name}: {ec}");
                var value = current.Value;
                var options = value.Item3.ToDictionary(k => k.Key, v => (IReadOnlyList<string>)v.Value, StringComparer.OrdinalIgnoreCase);
                states.Add((mod, value.Item2, options));
            }
            var (allEc, allSettings) = getAllSettings.Invoke(collection.Id, false, false, TemporaryKey);
            if (allEc != PenumbraApiEc.Success || allSettings is null)
                throw new InvalidOperationException($"Не удалось прочитать приоритеты коллекции: {allEc}");
            var highest = allSettings.Values.Where(x => x.Item1).Select(x => x.Item2).DefaultIfEmpty(0).Max();
            if (highest == int.MaxValue) throw new InvalidOperationException("Максимальный приоритет Penumbra уже занят.");
            var priority = highest + 1;
            var chosenState = states.First(x => x.Mod.Directory == selected.Directory);
            IReadOnlyDictionary<string, IReadOnlyList<string>> selectedOptions = bookmark.SavedOptions is null
                ? chosenState.Options
                : bookmark.SavedOptions.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value);
            var ecSet = setTemporary.Invoke(collection.Id, selected.Directory, false, true,
                priority, selectedOptions, "Emote Shelf", TemporaryKey);
            if (ecSet != PenumbraApiEc.Success && ecSet != PenumbraApiEc.NothingChanged)
                throw new InvalidOperationException($"Не удалось переключить {selected.Name}: {ecSet}");
            if (lastCommand.Equals(bookmark.Command, StringComparison.OrdinalIgnoreCase))
            {
                try { redraw.Invoke(0, RedrawType.Redraw); }
                catch (Exception ex) { Log.Warning(ex, "Redraw failed; animation may remain cached"); }
            }
            pendingCommand = bookmark.Command.Trim();
            sendAt = Environment.TickCount64 + 900;
            lastCommand = pendingCommand;
            status = $"Выбрано: {selected.Name} → {pendingCommand}";
        }
        catch (Exception ex)
        {
            pendingCommand = "";
            try { if (activeCollection != Guid.Empty) removeTemporary.Invoke(activeCollection, TemporaryKey); } catch { /* Penumbra unavailable */ }
            status = ex.Message;
            Log.Warning(ex, "Emote Shelf switching failed");
        }
    }

    private void Update(IFramework _)
    {
        if (initialScanPending && Objects.LocalPlayer is not null)
        {
            initialScanPending = false;
            Scan();
        }
        if (pendingCommand.Length == 0 || Environment.TickCount64 < sendAt) return;
        var command = pendingCommand;
        pendingCommand = "";
        try { ExecuteEmote(command); }
        catch (Exception ex) { status = $"Мод переключен, но эмоция не запустилась: {ex.Message}"; Log.Warning(ex, "Emote failed"); }
    }

    private static unsafe void ExecuteEmote(string command)
    {
        // A strict single-token slash command is the only text we ever pass to the game.
        if (!ModScanner.ValidCommand(command)) throw new ArgumentException("Недопустимая команда эмоции.");
        var ui = UIModule.Instance();
        if (ui == null) throw new InvalidOperationException("Чат игры не готов.");
        var text = Utf8String.FromString(command);
        try { ui->ProcessChatBoxEntry(text); }
        finally { text->Dtor(true); }
    }

    private void Draw()
    {
        if (settingsOpen) DrawSettings();
        if (config.OverlayVisible) DrawOverlay();
    }

    private void DrawOverlay()
    {
        var flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;
        if (config.OverlayLocked) flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
        ImGui.SetNextWindowSize(new Vector2(240, 90), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Emote Shelf##overlay", flags))
        { ImGui.End(); return; }
        if (config.Bookmarks.Count == 0) ImGui.TextDisabled(T("Добавь эмоции: /eshelf → Эмоции", "Add emotes: /eshelf → Emotes"));
        var columns = Math.Clamp(config.Columns, 1, 12);
        var size = new Vector2(Math.Clamp(config.IconSize, 28, 80));
        for (var i = 0; i < config.Bookmarks.Count; i++)
        {
            var bookmark = config.Bookmarks[i];
            if (i % columns != 0) ImGui.SameLine();
            var icon = Textures.GetFromGameIcon(new GameIconLookup(bookmark.IconId == 0 ? 19u : bookmark.IconId)).GetWrapOrDefault();
            ImGui.PushID(i);
            var clicked = icon is not null
                ? ImGui.ImageButton(icon.Handle, size)
                : ImGui.Button(bookmark.Command, size);
            if (clicked) Play(bookmark);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{bookmark.Name}\n{bookmark.Command}");
            ImGui.PopID();
        }
        ImGui.End();
    }

    private void DrawSettings()
    {
        ImGui.SetNextWindowSize(new Vector2(850, 650), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(540, 360), new Vector2(1600, 1200));
        if (!ImGui.Begin("Emote Shelf##settings", ref settingsOpen)) { ImGui.End(); return; }
        if (ImGui.BeginTabBar("##tabs"))
        {
            if (ImGui.BeginTabItem(T("Эмоции", "Emotes")))
            {
                DrawEmoteTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(T("Панель", "Panel"))) { DrawPanelTab(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(T("Как пользоваться", "How to use"))) { DrawHelpTab(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(T("Настройки", "Settings"))) { DrawAdvancedTab(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        ImGui.End();
    }

    private string T(string ru, string en) => config.English ? en : ru;

    private void DrawEmoteTab()
    {
        ImGui.TextWrapped(T("Выбери эмоцию и нажми «На панель». Затем нажми её значок на игровой панели.",
            "Choose an emote and click Add to panel. Then click its icon on the in-game panel."));
        if (ImGui.Button(T("Обновить список", "Refresh list"))) Scan();
        ImGui.SameLine();
        ImGui.TextDisabled(status);
        ImGui.InputTextWithHint("##search", T("Поиск по эмоции или моду", "Search emote or mod"), ref search, 120);
        if (ImGui.BeginChild("##emoteList", new Vector2(0, 0), true))
        {
            foreach (var (mod, index) in discovered.Select((m, i) => (m, i))
                         .Where(x => x.m.Command.Length > 0 &&
                                     (x.m.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                      x.m.EmoteName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                      x.m.Command.Contains(search, StringComparison.OrdinalIgnoreCase))))
            {
                ImGui.PushID(index);
                ImGui.TextUnformatted($"{mod.EmoteName}  {mod.Command}");
                ImGui.SameLine();
                if (ImGui.Button(T("На панель", "Add to panel")))
                {
                    config.Bookmarks.Add(new Bookmark { ModDirectory = mod.Directory,
                        Name = $"{mod.EmoteName} — {mod.Name}", Command = mod.Command, IconId = mod.Icon });
                    config.OverlayVisible = true;
                    Save();
                    status = T("Добавлено. Открой вкладку «Панель», чтобы настроить значки.",
                        "Added. Open the Panel tab to arrange icons.");
                }
                ImGui.TextDisabled(mod.Name);
                ImGui.Separator();
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
    }

    private void DrawPanelTab()
    {
        if (ImGui.Checkbox(T("Показывать игровую панель", "Show in-game panel"), ref config.OverlayVisible)) Save();
        ImGui.SameLine();
        if (ImGui.Checkbox(T("Закрепить положение", "Lock position"), ref config.OverlayLocked)) Save();
        if (ImGui.SliderInt(T("Значков в строке", "Icons per row"), ref config.Columns, 1, 12)) Save();
        if (ImGui.SliderFloat(T("Размер значка", "Icon size"), ref config.IconSize, 28, 80)) Save();
        ImGui.TextWrapped(T("Панель — отдельное маленькое окно с иконками эмоций. Когда положение не закреплено, перетащи её за заголовок. Закрывается только здесь или командой /eshelf hide.",
            "The panel is a separate small window of emote icons. Unlock it to drag by its title. Hide it here or with /eshelf hide."));
        ImGui.Separator();
        if (config.Bookmarks.Count == 0) ImGui.TextDisabled(T("Пока пусто: добавь эмоцию во вкладке «Эмоции».", "Empty: add an emote on the Emotes tab."));
        for (var i = 0; i < config.Bookmarks.Count; i++)
        {
            var b = config.Bookmarks[i];
            if (ImGui.SmallButton($"↑##{i}") && i > 0)
            { (config.Bookmarks[i - 1], config.Bookmarks[i]) = (config.Bookmarks[i], config.Bookmarks[i - 1]); Save(); }
            ImGui.SameLine();
            if (ImGui.SmallButton($"×##{i}")) { config.Bookmarks.RemoveAt(i); Save(); i--; continue; }
            ImGui.SameLine();
            var name = b.Name;
            ImGui.SetNextItemWidth(320);
            if (ImGui.InputText($"##name{i}", ref name, 100)) { b.Name = name; Save(); }
            ImGui.SameLine();
            ImGui.TextDisabled(b.Command);
            ImGui.SameLine();
            if (ImGui.SmallButton($"{T("Запомнить опции", "Save options")}##{i}")) CaptureOptions(b);
            if (b.SavedOptions is not null) { ImGui.SameLine(); ImGui.TextDisabled("✓"); }
        }
    }

    private void DrawHelpTab()
    {
        ImGui.TextWrapped(T(
            "1. Во вкладке «Эмоции» найди нужный танец или эмоцию и нажми «На панель». В строке указаны игровая эмоция и мод, который её заменяет.\n\n2. На экране появится маленькая панель значков. Нажми значок: плагин временно выберет этот мод в Penumbra и запустит соответствующую игровую эмоцию.\n\n3. Во вкладке «Панель» настрой сетку, размер и положение. Для разных вариантов одной эмоции можно добавить её дважды и у каждой закладки сохранить текущие опции Penumbra.\n\nЕсли эмоция не распознана по игровым данным или описанию мода, открой «Настройки» и укажи её команду вручную. Это запасной вариант, а не обязательный шаг.",
            "1. On Emotes, find a dance or emote and click Add to panel. Each row shows the game emote and its replacing mod.\n\n2. A small icon panel appears in-game. Click an icon to temporarily select that Penumbra mod and run the matching game emote.\n\n3. On Panel, set the grid, icon size and position. To save two variants of one emote, add it twice and save the current Penumbra options for each bookmark.\n\nIf an emote cannot be identified from game data or the mod description, use Settings to enter its command manually. This is a fallback, not a required step."));
    }

    private void DrawAdvancedTab()
    {
        if (ImGui.Checkbox("English UI", ref config.English)) Save();
        if (ImGui.Button(T("Сбросить временное переключение", "Clear temporary selection")))
        {
            try { if (activeCollection != Guid.Empty) removeTemporary.Invoke(activeCollection, TemporaryKey); status = T("Временные настройки сняты.", "Temporary settings cleared."); }
            catch (Exception ex) { status = ex.Message; }
            activeCollection = Guid.Empty;
        }
        ImGui.TextWrapped(T("Неопознанные анимации: только если нужной эмоции нет во вкладке «Эмоции», укажи её игровую команду.",
            "Unidentified animations: only if your emote is missing from Emotes, enter its game command here."));
        foreach (var mod in discovered.Where(m => m.Command.Length == 0))
        {
            var command = config.CommandOverrides.GetValueOrDefault(mod.Directory, "");
            ImGui.PushID(mod.Directory);
            ImGui.TextUnformatted(mod.Name);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputText("##command", ref command, 40)) { config.CommandOverrides[mod.Directory] = command; Save(); }
            ImGui.SameLine();
            if (ImGui.Button(T("На панель", "Add to panel")) && ModScanner.ValidCommand(command))
            {
                config.Bookmarks.Add(new Bookmark { ModDirectory = mod.Directory, Name = mod.Name, Command = command });
                config.OverlayVisible = true;
                Save();
            }
            ImGui.PopID();
        }
    }

    private void CaptureOptions(Bookmark bookmark)
    {
        try
        {
            if (Objects.LocalPlayer is not { } player) throw new InvalidOperationException("Персонаж не в игре.");
            var (valid, _, collection) = getCollection.Invoke(player.ObjectIndex);
            if (!valid || collection.Id == Guid.Empty) throw new InvalidOperationException("Коллекция не найдена.");
            var (ec, current) = getSettings.Invoke(collection.Id, bookmark.ModDirectory, "", false);
            if (ec != PenumbraApiEc.Success || current is null) throw new InvalidOperationException($"Настройки мода недоступны: {ec}");
            bookmark.SavedOptions = current.Value.Item3.ToDictionary(x => x.Key, x => x.Value.ToList());
            Save();
            status = $"Опции Penumbra сохранены для «{bookmark.Name}». Можно создать ещё одну закладку этого мода с другими опциями.";
        }
        catch (Exception ex) { status = ex.Message; Log.Warning(ex, "Option capture failed"); }
    }

    public void Dispose()
    {
        pendingCommand = "";
        try { if (activeCollection != Guid.Empty) removeTemporary.Invoke(activeCollection, TemporaryKey); } catch { /* Penumbra unloaded */ }
        Framework.Update -= Update;
        Pi.UiBuilder.Draw -= Draw;
        Pi.UiBuilder.OpenConfigUi -= OpenSettings;
        Pi.UiBuilder.OpenMainUi -= OpenSettings;
        Commands.RemoveHandler("/eshelf");
        Save();
    }
}
