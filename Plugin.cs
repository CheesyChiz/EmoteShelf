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
    private string status = "";
    private Guid activeCollection;
    private string pendingCommand = "";
    private long sendAt;
    private bool initialScanPending = true;
    private string lastCommand = "";
    private readonly HashSet<string> expandedVariants = new(StringComparer.OrdinalIgnoreCase);

    public Plugin()
    {
        config = Pi.GetPluginConfig() as Configuration ?? new Configuration();
        config.Bookmarks ??= [];
        config.CommandOverrides ??= new(StringComparer.OrdinalIgnoreCase);
        config.HiddenMods ??= new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(config.Language)) config.Language = config.English ? "en" : "ru";
        status = T("Поиск замен эмоций в Penumbra…", "Searching Penumbra emote replacements…");
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
            if (string.IsNullOrWhiteSpace(root) || mods is null) throw new InvalidOperationException(T("Penumbra недоступна.", "Penumbra is unavailable."));
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
            status = string.Format(T("В списке {0} сочетаний мод–эмоция.", "Showing {0} mod–emote pairs."),
                discovered.Count(m => !config.HiddenMods.Contains(m.Directory)));
            initialScanPending = false;
        }
        catch (Exception ex)
        {
            status = string.Format(T("Поиск не удался: {0}", "Scan failed: {0}"), ex.Message);
            Log.Warning(ex, "Emote Shelf scan failed");
        }
    }

    private void Play(Bookmark bookmark)
    {
        if (!ModScanner.ValidCommand(bookmark.Command)) { status = T("У закладки нет корректной команды эмоции.", "The bookmark has no valid emote command."); return; }
        if (Objects.LocalPlayer is not { } player) { status = T("Персонаж не в игре.", "Character is not in game."); return; }
        var selected = discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory &&
            m.Command.Equals(bookmark.Command, StringComparison.OrdinalIgnoreCase));
        if (selected is null) { Scan(); selected = discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory &&
            m.Command.Equals(bookmark.Command, StringComparison.OrdinalIgnoreCase)); }
        selected ??= discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory);
        if (selected is null) { status = T("Мод не найден в Penumbra; обнови список.", "Mod not found in Penumbra; refresh the list."); return; }
        try
        {
            var (valid, _, collection) = getCollection.Invoke(player.ObjectIndex);
            if (!valid || collection.Id == Guid.Empty) throw new InvalidOperationException(T("Не удалось определить коллекцию персонажа.", "Could not determine the character's collection."));
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
                    throw new InvalidOperationException(string.Format(T("Не удалось прочитать настройки {0}: {1}", "Could not read settings for {0}: {1}"), mod.Name, ec));
                var value = current.Value;
                var options = value.Item3.ToDictionary(k => k.Key, v => (IReadOnlyList<string>)v.Value, StringComparer.OrdinalIgnoreCase);
                states.Add((mod, value.Item2, options));
            }
            var (allEc, allSettings) = getAllSettings.Invoke(collection.Id, false, false, TemporaryKey);
            if (allEc != PenumbraApiEc.Success || allSettings is null)
                throw new InvalidOperationException(string.Format(T("Не удалось прочитать приоритеты коллекции: {0}", "Could not read collection priorities: {0}"), allEc));
            var highest = allSettings.Values.Where(x => x.Item1).Select(x => x.Item2).DefaultIfEmpty(0).Max();
            if (highest == int.MaxValue) throw new InvalidOperationException(T("Максимальный приоритет Penumbra уже занят.", "Penumbra's maximum priority is already in use."));
            var priority = highest + 1;
            var chosenState = states.First(x => x.Mod.Directory == selected.Directory);
            IReadOnlyDictionary<string, IReadOnlyList<string>> selectedOptions = bookmark.SavedOptions is null
                ? chosenState.Options
                : bookmark.SavedOptions.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value);
            var ecSet = setTemporary.Invoke(collection.Id, selected.Directory, false, true,
                priority, selectedOptions, "Emote Shelf", TemporaryKey);
            if (ecSet != PenumbraApiEc.Success && ecSet != PenumbraApiEc.NothingChanged)
                throw new InvalidOperationException(string.Format(T("Не удалось переключить {0}: {1}", "Could not switch {0}: {1}"), selected.Name, ecSet));
            if (lastCommand.Equals(bookmark.Command, StringComparison.OrdinalIgnoreCase))
            {
                try { redraw.Invoke(0, RedrawType.Redraw); }
                catch (Exception ex) { Log.Warning(ex, "Redraw failed; animation may remain cached"); }
            }
            pendingCommand = bookmark.Command.Trim();
            sendAt = Environment.TickCount64 + 900;
            lastCommand = pendingCommand;
            status = string.Format(T("Выбрано: {0} → {1}", "Selected: {0} → {1}"), selected.Name, pendingCommand);
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
        catch (Exception ex) { status = string.Format(T("Мод переключен, но эмоция не запустилась: {0}", "Mod switched, but the emote did not start: {0}"), ex.Message); Log.Warning(ex, "Emote failed"); }
    }

    private unsafe void ExecuteEmote(string command)
    {
        // A strict single-token slash command is the only text we ever pass to the game.
        if (!ModScanner.ValidCommand(command)) throw new ArgumentException(T("Недопустимая команда эмоции.", "Invalid emote command."));
        var ui = UIModule.Instance();
        if (ui == null) throw new InvalidOperationException(T("Чат игры не готов.", "Game chat is not ready."));
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
        if (config.OverlayLocked) flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground;
        ImGui.SetNextWindowBgAlpha(Math.Clamp(config.PanelOpacity, 0f, 1f));
        ImGui.SetNextWindowSize(new Vector2(240, 90), ImGuiCond.FirstUseEver);
        if (config.OverlayLocked) ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (!ImGui.Begin("Emote Shelf##overlay", flags))
        { ImGui.End(); if (config.OverlayLocked) ImGui.PopStyleVar(); return; }
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
            if (clicked)
            {
                if (ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift)
                {
                    config.Bookmarks.RemoveAt(i);
                    Save();
                    i--;
                }
                else Play(bookmark);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{bookmark.Name}\n{bookmark.Command}\n{T("Ctrl+Shift+клик — убрать с панели", "Ctrl+Shift+click — remove from panel")}");
            ImGui.PopID();
        }
        ImGui.End();
        if (config.OverlayLocked) ImGui.PopStyleVar();
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

    private string T(string ru, string en) => Localization.Get(config.Language, ru, en);

    private void DrawEmoteTab()
    {
        ImGui.TextWrapped(T("Найди эмоцию, проверь её через «Предпросмотр» и добавь на панель. Закладка сохранится автоматически.",
            "Find an emote, try Preview, then add it to the panel. The bookmark is saved automatically."));
        if (ImGui.Button(T("Обновить список", "Refresh list"))) Scan();
        ImGui.TextWrapped(status);
        ImGui.InputTextWithHint("##search", T("Поиск по эмоции или моду", "Search emote or mod"), ref search, 120);
        if (ImGui.BeginChild("##emoteList", new Vector2(0, 0), true))
        {
            foreach (var (mod, index) in discovered.Select((m, i) => (m, i))
                         .Where(x => x.m.Command.Length > 0 && !config.HiddenMods.Contains(x.m.Directory) &&
                                     (x.m.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                      x.m.EmoteName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                      x.m.Command.Contains(search, StringComparison.OrdinalIgnoreCase))))
            {
                ImGui.PushID(index);
                var variantKey = $"{mod.Directory}|{mod.Command}";
                var expanded = expandedVariants.Contains(variantKey);
                ImGui.BeginChild("##card", new Vector2(0, mod.Variants.Length == 0 ? 88 : expanded ? 90 + mod.Variants.Length * 23 : 80), true);
                var icon = Textures.GetFromGameIcon(new GameIconLookup(mod.Icon == 0 ? 19u : mod.Icon)).GetWrapOrDefault();
                if (icon is not null) ImGui.Image(icon.Handle, new Vector2(42));
                else ImGui.Dummy(new Vector2(42));
                ImGui.SameLine();
                ImGui.BeginGroup();
                ImGui.TextUnformatted(mod.EmoteName);
                ImGui.SameLine();
                ImGui.TextDisabled(mod.Command);
                ImGui.TextDisabled(mod.Name);
                ImGui.SameLine();
                if (ImGui.SmallButton(T("Скрыть мод", "Hide mod")))
                {
                    config.HiddenMods.Add(mod.Directory);
                    Save();
                }
                if (mod.Variants.Length == 0)
                {
                    if (ImGui.SmallButton(T("Предпросмотр", "Preview"))) Preview(mod, null);
                    ImGui.SameLine();
                    if (ImGui.SmallButton(T("На панель", "Add to panel"))) AddBookmark(mod, null);
                }
                ImGui.EndGroup();
                if (mod.Variants.Length > 0)
                {
                    if (ImGui.SmallButton($"{(expanded ? "▼" : "▶")} {T("Варианты мода", "Mod variants")} ({mod.Variants.Length})##variants"))
                    {
                        if (expanded) expandedVariants.Remove(variantKey);
                        else expandedVariants.Add(variantKey);
                    }
                    if (expanded)
                        foreach (var (variant, variantIndex) in mod.Variants.Select((v, i) => (v, i)))
                        {
                            ImGui.PushID(variantIndex);
                            if (ImGui.SmallButton(T("На панель", "Add to panel"))) AddBookmark(mod, variant);
                            ImGui.SameLine();
                            if (ImGui.SmallButton(T("Предпросмотр", "Preview"))) Preview(mod, variant);
                            ImGui.SameLine();
                            ImGui.TextUnformatted($"{variant.Group} → {variant.Option}");
                            ImGui.PopID();
                        }
                }
                ImGui.EndChild();
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
    }

    private void AddBookmark(EmoteMod mod, EmoteVariant? variant)
    {
        try
        {
            config.Bookmarks.Add(CreateBookmark(mod, variant));
            config.OverlayVisible = true;
            Save();
            status = T("Добавлено на панель. Настрой расположение во вкладке «Панель».",
                "Added to panel. Arrange it on the Panel tab.");
        }
        catch (Exception ex) { status = ex.Message; Log.Warning(ex, "Adding bookmark failed"); }
    }

    private void Preview(EmoteMod mod, EmoteVariant? variant)
    {
        try { Play(CreateBookmark(mod, variant)); }
        catch (Exception ex) { status = ex.Message; Log.Warning(ex, "Preview failed"); }
    }

    private Bookmark CreateBookmark(EmoteMod mod, EmoteVariant? variant)
    {
        if (Objects.LocalPlayer is not { } player) throw new InvalidOperationException(T("Персонаж не в игре.", "Character is not in game."));
        var (valid, _, collection) = getCollection.Invoke(player.ObjectIndex);
        if (!valid || collection.Id == Guid.Empty) throw new InvalidOperationException(T("Коллекция не найдена.", "Collection not found."));
        var (ec, current) = getSettings.Invoke(collection.Id, mod.Directory, "", false);
        if (ec != PenumbraApiEc.Success || current is null) throw new InvalidOperationException($"Penumbra: {ec}");
        var saved = current.Value.Item3.ToDictionary(x => x.Key, x => x.Value.ToList());
        if (variant is not null) saved[variant.Group] = [variant.Option];
        return new Bookmark { ModDirectory = mod.Directory,
            Name = variant is null ? $"{mod.EmoteName} — {mod.Name}" : $"{mod.EmoteName} — {variant.Option}",
            Command = mod.Command, IconId = mod.Icon, SavedOptions = saved };
    }

    private void DrawPanelTab()
    {
        if (ImGui.Checkbox(T("Показывать игровую панель", "Show in-game panel"), ref config.OverlayVisible)) Save();
        ImGui.SameLine();
        if (ImGui.Checkbox(T("Закрепить положение", "Lock position"), ref config.OverlayLocked)) Save();
        if (ImGui.SliderInt(T("Значков в строке", "Icons per row"), ref config.Columns, 1, 12)) Save();
        if (ImGui.SliderFloat(T("Размер значка", "Icon size"), ref config.IconSize, 28, 80)) Save();
        if (ImGui.SliderFloat(T("Прозрачность фона", "Background opacity"), ref config.PanelOpacity, 0f, 1f)) Save();
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
            if (ImGui.SmallButton($"{T("Обновить опции", "Update options")}##{i}")) CaptureOptions(b);
            if (b.SavedOptions is not null) { ImGui.SameLine(); ImGui.TextDisabled("✓"); }
        }
    }

    private void DrawHelpTab()
    {
        ImGui.TextWrapped(T(
            "1. Во вкладке «Эмоции» найди нужный танец или эмоцию. «Предпросмотр» запускает её без добавления, «На панель» создаёт постоянную закладку с текущими опциями мода. Если есть распознанные варианты, раскрой «Варианты мода».\n\n2. На экране появится панель значков. Клик запускает эмоцию, Ctrl+Shift+клик удаляет значок. Предпросмотр и клик временно выбирают мод в Penumbra; сбросить переключение можно во вкладке «Настройки».\n\n3. Во вкладке «Панель» настрой сетку, размер и положение. Опции сохраняются автоматически при добавлении. Если позже изменишь настройки мода и захочешь заменить снимок, нажми «Обновить опции». Сочетания нескольких групп вариантов автоматически не перечисляются: настрой нужное сочетание в Penumbra и добавь закладку.\n\nЕсли эмоция не распознана по игровым данным или описанию мода, открой «Настройки» и укажи её команду вручную. Это запасной вариант, а не обязательный шаг.",
            "1. On Emotes, find a dance or emote. Preview plays it without adding it; Add to panel creates a persistent bookmark with the mod's current options. Expand Mod variants if detected.\n\n2. An icon panel appears in-game. Click to play, Ctrl+Shift+click to remove an icon. Preview and click temporarily select the mod in Penumbra; reset the temporary selection on Settings.\n\n3. On Panel, set the grid, icon size and position. Options are captured automatically on add. If you later change the mod and want to replace that snapshot, click Update options. Combinations of multiple option groups are not enumerated automatically: configure the combination in Penumbra and add a bookmark.\n\nIf an emote cannot be identified from game data or the mod description, use Settings to enter its command manually. This is a fallback, not a required step."));
        ImGui.Separator();
        ImGui.TextWrapped(T("Неинтересные моды можно скрыть прямо в каталоге. Вернуть их можно во вкладке «Настройки» → «Скрытые моды». Уже добавленные значки при этом остаются на панели.",
            "Hide unwanted mods directly in the browser. Restore them under Settings → Hidden mods. Existing panel bookmarks remain intact."));
    }

    private void DrawAdvancedTab()
    {
        var languages = new[] { ("ru", "Русский"), ("en", "English"), ("ja", "日本語"), ("de", "Deutsch"), ("fr", "Français") };
        var selectedLanguage = languages.FirstOrDefault(x => x.Item1 == config.Language).Item2 ?? "Русский";
        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo(T("Язык интерфейса", "Interface language"), selectedLanguage))
        {
            foreach (var (code, label) in languages)
                if (ImGui.Selectable(label, config.Language == code))
                {
                    config.Language = code;
                    status = T("Язык изменён.", "Language changed.");
                    Save();
                }
            ImGui.EndCombo();
        }
        if (ImGui.Button(T("Сбросить временное переключение", "Clear temporary selection")))
        {
            try { if (activeCollection != Guid.Empty) removeTemporary.Invoke(activeCollection, TemporaryKey); status = T("Временные настройки сняты.", "Temporary settings cleared."); }
            catch (Exception ex) { status = ex.Message; }
            activeCollection = Guid.Empty;
        }
        ImGui.TextWrapped(T("Неопознанные анимации: только если нужной эмоции нет во вкладке «Эмоции», укажи её игровую команду.",
            "Unidentified animations: only if your emote is missing from Emotes, enter its game command here."));
        if (config.HiddenMods.Count > 0 && ImGui.TreeNode($"{T("Скрытые моды", "Hidden mods")} ({config.HiddenMods.Count})##hidden"))
        {
            ImGui.TextWrapped(T("Скрытие убирает мод из каталога, но не удаляет уже добавленные значки с панели.",
                "Hiding removes a mod from the browser but keeps existing panel bookmarks."));
            foreach (var directory in config.HiddenMods.ToArray())
            {
                ImGui.PushID(directory);
                if (ImGui.SmallButton(T("Вернуть", "Restore"))) { config.HiddenMods.Remove(directory); Save(); }
                ImGui.SameLine();
                ImGui.TextUnformatted(discovered.FirstOrDefault(m => m.Directory == directory)?.Name ?? directory);
                ImGui.PopID();
            }
            ImGui.TreePop();
        }
        foreach (var mod in discovered.Where(m => m.Command.Length == 0 && !config.HiddenMods.Contains(m.Directory)))
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
                try
                {
                    var bookmark = CreateBookmark(mod, null);
                    bookmark.Command = command;
                    config.Bookmarks.Add(bookmark);
                    config.OverlayVisible = true;
                    Save();
                }
                catch (Exception ex) { status = ex.Message; Log.Warning(ex, "Adding manual bookmark failed"); }
            }
            ImGui.PopID();
        }
    }

    private void CaptureOptions(Bookmark bookmark)
    {
        try
        {
            if (Objects.LocalPlayer is not { } player) throw new InvalidOperationException(T("Персонаж не в игре.", "Character is not in game."));
            var (valid, _, collection) = getCollection.Invoke(player.ObjectIndex);
            if (!valid || collection.Id == Guid.Empty) throw new InvalidOperationException(T("Коллекция не найдена.", "Collection not found."));
            var (ec, current) = getSettings.Invoke(collection.Id, bookmark.ModDirectory, "", false);
            if (ec != PenumbraApiEc.Success || current is null) throw new InvalidOperationException(string.Format(T("Настройки мода недоступны: {0}", "Mod settings are unavailable: {0}"), ec));
            bookmark.SavedOptions = current.Value.Item3.ToDictionary(x => x.Key, x => x.Value.ToList());
            Save();
            status = string.Format(T("Опции Penumbra сохранены для «{0}».", "Penumbra options saved for “{0}”."), bookmark.Name);
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
