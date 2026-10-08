using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
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
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private const int TemporaryKey = -170025;
    private readonly Configuration config;
    private readonly GetModDirectory getRoot = new(Pi);
    private readonly GetModList getMods = new(Pi);
    private readonly GetCollectionForObject getCollection = new(Pi);
    private readonly GetCurrentModSettings getSettings = new(Pi);
    private readonly SetTemporaryModSettings setTemporary = new(Pi);
    private readonly RemoveAllTemporaryModSettings removeTemporary = new(Pi);
    private List<EmoteMod> discovered = [];
    private bool settingsOpen;
    private string search = "";
    private string status = "Нажми «Обновить список» для поиска модов с анимациями эмоций.";
    private Guid activeCollection;
    private string pendingCommand = "";
    private long sendAt;
    private bool initialScanPending = true;

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
            discovered = ModScanner.Scan(root, mods);
            status = $"Найдено {discovered.Count} сочетаний мод–эмоция. Команды, не указанные автором, нужно заполнить вручную.";
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
        var selected = discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory);
        if (selected is null) { Scan(); selected = discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory); }
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
            var priority = states.Count == 0 ? 1 : Math.Min(100000, states.Max(x => x.Priority) + 1);
            var chosenState = states.First(x => x.Mod.Directory == selected.Directory);
            IReadOnlyDictionary<string, IReadOnlyList<string>> selectedOptions = bookmark.SavedOptions is null
                ? chosenState.Options
                : bookmark.SavedOptions.ToDictionary(x => x.Key, x => (IReadOnlyList<string>)x.Value);
            var ecSet = setTemporary.Invoke(collection.Id, selected.Directory, false, true,
                priority, selectedOptions, "Emote Shelf", TemporaryKey);
            if (ecSet != PenumbraApiEc.Success && ecSet != PenumbraApiEc.NothingChanged)
                throw new InvalidOperationException($"Не удалось переключить {selected.Name}: {ecSet}");
            pendingCommand = bookmark.Command.Trim();
            sendAt = Environment.TickCount64 + 400;
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
        ImGui.SetNextWindowSize(new Vector2(230, 100), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Emote Shelf##overlay", ref config.OverlayVisible, ImGuiWindowFlags.AlwaysAutoResize))
        { ImGui.End(); return; }
        if (ImGui.SmallButton("Настроить")) settingsOpen = true;
        if (config.Bookmarks.Count == 0) ImGui.TextDisabled("Закладки пусты — открой настройки.");
        foreach (var (bookmark, index) in config.Bookmarks.ToArray().Select((b, i) => (b, i)))
            if (ImGui.Button($"{bookmark.Name}##{index}", new Vector2(210, 0))) Play(bookmark);
        ImGui.End();
    }

    private void DrawSettings()
    {
        ImGui.SetNextWindowSize(new Vector2(700, 530), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Emote Shelf — настройки", ref settingsOpen)) { ImGui.End(); return; }
        if (ImGui.Button("Обновить список модов")) Scan();
        ImGui.SameLine();
        if (ImGui.Checkbox("Показывать панель", ref config.OverlayVisible)) Save();
        ImGui.TextWrapped(status);
        ImGui.Separator();
        ImGui.TextUnformatted("Обнаруженные моды (кнопка + добавляет на панель):");
        ImGui.InputTextWithHint("##search", "Поиск по названию", ref search, 120);
        if (ImGui.BeginChild("discovered", new Vector2(0, 270), true))
        {
            foreach (var mod in discovered.Where(m => m.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
            {
                var overrideKey = mod.Directory + "|" + mod.Command;
                var command = config.CommandOverrides.GetValueOrDefault(overrideKey, mod.Command);
                var id = $"##{overrideKey}";
                if (ImGui.SmallButton("+" + id))
                {
                    if (ModScanner.ValidCommand(command))
                    { config.Bookmarks.Add(new Bookmark { ModDirectory = mod.Directory, Name = $"{mod.Name} — {command}", Command = command }); Save(); }
                    else status = "Укажи команду /эмоции.";
                }
                ImGui.SameLine();
                ImGui.TextUnformatted(mod.Command.Length > 0 ? $"{mod.Name} — {mod.Command}" : mod.Name);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(125);
                if (ImGui.InputText("Команда" + id, ref command, 40))
                {
                    config.CommandOverrides[overrideKey] = command;
                    Save();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Замена: {string.Join(", ", mod.Paths.Select(Path.GetFileName).Distinct().Take(5))}");
            }
        }
        ImGui.EndChild();
        ImGui.Separator();
        ImGui.TextUnformatted("Закладки на панели:");
        for (var i = 0; i < config.Bookmarks.Count; i++)
        {
            var b = config.Bookmarks[i];
            if (ImGui.SmallButton($"↑##{i}") && i > 0)
            { (config.Bookmarks[i - 1], config.Bookmarks[i]) = (config.Bookmarks[i], config.Bookmarks[i - 1]); Save(); }
            ImGui.SameLine();
            if (ImGui.SmallButton($"×##{i}")) { config.Bookmarks.RemoveAt(i); Save(); i--; continue; }
            ImGui.SameLine();
            var name = b.Name;
            ImGui.SetNextItemWidth(280);
            if (ImGui.InputText($"##name{i}", ref name, 100)) { b.Name = name; Save(); }
            ImGui.SameLine();
            ImGui.TextDisabled(b.Command);
            ImGui.SameLine();
            if (ImGui.SmallButton($"Запомнить опции##{i}")) CaptureOptions(b);
            if (b.SavedOptions is not null) { ImGui.SameLine(); ImGui.TextDisabled("✓"); }
        }
        if (ImGui.Button("Сбросить временное переключение"))
        {
            try { if (activeCollection != Guid.Empty) removeTemporary.Invoke(activeCollection, TemporaryKey); status = "Временные настройки сняты."; }
            catch (Exception ex) { status = ex.Message; }
            activeCollection = Guid.Empty;
        }
        ImGui.End();
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
