using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Textures;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.UI;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;
using System.Numerics;

namespace EmoteShelf;

public sealed partial class Plugin : IDalamudPlugin
{
    [PluginService] private static IDalamudPluginInterface Pi { get; set; } = null!;
    [PluginService] private static ICommandManager Commands { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IObjectTable Objects { get; set; } = null!;
    [PluginService] private static IChatGui Chat { get; set; } = null!;
    [PluginService] private static IDataManager Data { get; set; } = null!;
    [PluginService] private static ITextureProvider Textures { get; set; } = null!;
    [PluginService] private static IUnlockState Unlocks { get; set; } = null!;
    [PluginService] private static ITargetManager Targets { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private const int TemporaryKey = -170025;
    private readonly Configuration config;
    private readonly GetModDirectory getRoot = new(Pi);
    private readonly GetModList getMods = new(Pi);
    private readonly GetChangedItems getChangedItems = new(Pi);
    private readonly GetCollectionForObject getCollection = new(Pi);
    private readonly GetCurrentModSettings getSettings = new(Pi);
    private readonly GetCurrentModSettingsWithTemp getSettingsWithTemp = new(Pi);
    private readonly GetAllModSettings getAllSettings = new(Pi);
    private readonly SetTemporaryModSettings setTemporary = new(Pi);
    private readonly RemoveAllTemporaryModSettings removeTemporary = new(Pi);
    private readonly RedrawObject redraw = new(Pi);
    private readonly IDisposable redrawCompletedSubscription;
    private readonly ResolvePath resolvePath = new(Pi);
    private List<EmoteMod> discovered = [];
    private EmoteCatalog? catalog;
    private bool settingsOpen;
    private string search = "";
    private string status = "";
    private Guid activeCollection;
    private string pendingCommand = "";
    private long sendAt;
    private long redrawDeadline;
    private bool redrawCompleted;
    private int pendingPoseIndex = -1;
    private EmoteController.PoseType pendingPoseType = EmoteController.PoseType.GroundSit;
    private long poseAt;
    private int poseAttempts;
    private bool initialScanPending = true;
    private string pendingModDirectory = "";
    private Guid pendingCollection;
    private int pendingPriority;
    private Dictionary<string, string[]>? pendingOptions;
    private string[] pendingExpectedPaths = [];
    private Dictionary<string, string> pendingExpectedFiles = new(StringComparer.OrdinalIgnoreCase);
    private string pendingExpectedModRoot = "";
    private Bookmark? waitingForStand;
    private long standDeadline;
    private long standSettledAt;
    private readonly HashSet<string> expandedVariants = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> expandedMods = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> collapsedMods = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> expandedFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> collapsedFolders = new(StringComparer.OrdinalIgnoreCase);
    private int draggedBookmark = -1;
    private string newFolderName = "";
    private string selectedFolderName = "";
    private string selectedModDirectory = "";
    private int folderEditMode;
    private Bookmark? manualPoseBookmark;
    private int manualPoseTarget = -1;
    private long manualPoseReturnAt;
    private Bookmark? launchingBookmark;
    private bool selectEmotesTab;
    private string rightClickMod = "";
    private bool scrollToSelectedMod;
    private string draggedFolder = "";
    private bool createAtRoot;
    private bool showAllVariants;
    private PapCompatibility? papCompatibility;
    private bool papTableLoaded;
    private readonly Dictionary<(ushort, string), bool?> compatibilityCache = [];
    private readonly Dictionary<string, Dictionary<string, List<string>>> draftOptions = new(StringComparer.OrdinalIgnoreCase);
    private string draggedModDirectory = "";

    public Plugin()
    {
        redrawCompletedSubscription = GameObjectRedrawn.Subscriber(Pi, OnGameObjectRedrawn);
        config = Pi.GetPluginConfig() as Configuration ?? new Configuration();
        config.Bookmarks ??= [];
        config.CommandOverrides ??= new(StringComparer.OrdinalIgnoreCase);
        config.HiddenMods ??= new(StringComparer.OrdinalIgnoreCase);
        config.PoseOverrides ??= new(StringComparer.OrdinalIgnoreCase);
        config.Folders ??= [];
        config.ModFolders ??= new(StringComparer.OrdinalIgnoreCase);
        config.ClosedFolders ??= new(StringComparer.OrdinalIgnoreCase);
        collapsedFolders.UnionWith(config.ClosedFolders);
        expandedFolders.UnionWith(config.Folders.Except(collapsedFolders));
        if (!config.ManualPoseMigration)
        {
            config.AutomaticPose = false;
            config.ManualPoseMigration = true;
            Save();
        }
        if (string.IsNullOrEmpty(config.Language)) config.Language = config.English ? "en" : "ru";
        status = T("Поиск замен эмоций в Penumbra…", "Searching Penumbra emote replacements…");
        Commands.AddHandler("/eshelf", new CommandInfo(OnCommand) { HelpMessage = "Emote Shelf: /eshelf, /eshelf scan, /eshelf show, /eshelf hide" });
        Commands.AddHandler("/es", new CommandInfo(OnCommand) { HelpMessage = "Emote Shelf: /es, /es scan, /es show, /es hide" });
        Pi.UiBuilder.Draw += Draw;
        Pi.UiBuilder.OpenConfigUi += OpenSettings;
        Pi.UiBuilder.OpenMainUi += OpenSettings;
        Framework.Update += Update;
        ContextMenus.OnMenuOpened += OnPairContextMenu;
    }

    private void Save() => Pi.SavePluginConfig(config);
    private void OnGameObjectRedrawn(nint address, int index)
    {
        if (Objects.LocalPlayer is { } player && (index == player.ObjectIndex || address == player.Address))
            redrawCompleted = true;
    }
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
            catalog = new EmoteCatalog(Data);
            discovered = ModScanner.Scan(root, mods, catalog,
                directory => getChangedItems.Invoke(directory, "").Keys);
            var changedBookmarks = false;
            foreach (var bookmark in config.Bookmarks)
            {
                var match = discovered.FirstOrDefault(m => m.Directory == bookmark.ModDirectory &&
                    m.Command.Equals(bookmark.Command, StringComparison.OrdinalIgnoreCase));
                if (match is null) continue;
                if (bookmark.IconId == 0 && match.Icon != 0) { bookmark.IconId = match.Icon; changedBookmarks = true; }
                if (match.PoseIndex.HasValue && bookmark.PoseIndex != match.PoseIndex)
                { bookmark.PoseIndex = match.PoseIndex; changedBookmarks = true; }
                else if (match.PoseSlots.Length > 1 &&
                    (!bookmark.PoseIndex.HasValue || !match.PoseSlots.Contains(bookmark.PoseIndex.Value)))
                { bookmark.PoseIndex = match.PoseSlots[0]; changedBookmarks = true; }
                if (match.PoseSlots.Length > 0 && bookmark.SavedOptions is not null)
                {
                    var prefix = match.Command.Equals("/groundsit", StringComparison.OrdinalIgnoreCase) ? "j_" : "";
                    var selectedVariant = match.Variants.FirstOrDefault(v =>
                        bookmark.SavedOptions.TryGetValue(v.Group, out var options) &&
                        options.Contains(v.Option, StringComparer.OrdinalIgnoreCase) &&
                        (bookmark.Name.EndsWith($"— {v.Group} → {v.Option}", StringComparison.Ordinal) ||
                         bookmark.Name.EndsWith($"— {v.Option}", StringComparison.Ordinal)));
                    var variantPose = selectedVariant is null ? null : PoseSlot.FromPaths(selectedVariant.Paths, prefix);
                    if (variantPose.HasValue && bookmark.PoseIndex != variantPose)
                    { bookmark.PoseIndex = variantPose; changedBookmarks = true; }
                    if (selectedVariant is not null && bookmark.Name.Equals(
                            $"{match.Name} — {match.EmoteName} — {selectedVariant.Option}", StringComparison.Ordinal))
                    {
                        bookmark.Name = $"{match.Name} — {match.EmoteName} — {selectedVariant.Group} → {selectedVariant.Option}";
                        changedBookmarks = true;
                    }
                }
                var oldVariant = match.Variants.FirstOrDefault(v =>
                    bookmark.Name.Equals($"{match.EmoteName} — {v.Option}", StringComparison.Ordinal));
                if (oldVariant is not null)
                {
                    bookmark.Name = $"{match.Name} — {match.EmoteName} — {oldVariant.Group} → {oldVariant.Option}";
                    changedBookmarks = true;
                }
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

    private void Play(Bookmark bookmark, bool pairPreparation = false)
    {
        if (!pairPreparation) CancelPair();
        manualPoseBookmark = null;
        launchingBookmark = bookmark;
        waitingForStand = null;
        standSettledAt = 0;
        pendingCommand = "";
        pendingModDirectory = "";
        pendingPoseIndex = -1;
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
            if (PosePlayback.ShouldStandUp(bookmark.Command, IsGroundSitting()))
            {
                StandUpFromGroundSit();
                waitingForStand = bookmark;
                standDeadline = Environment.TickCount64 + 4000;
                status = T("Выхожу из /groundsit перед переключением мода…", "Standing up before switching mods…");
                return;
            }
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
            redrawCompleted = false;
            redrawDeadline = Environment.TickCount64 + 1600;
            try { if (!IsPoseCommand(bookmark.Command)) redraw.Invoke(player.ObjectIndex, RedrawType.Redraw); }
            catch (Exception ex) { redrawCompleted = true; Log.Warning(ex, "Redraw failed; animation may remain cached"); }
            pendingCommand = bookmark.Command.Trim();
            pendingModDirectory = selected.Directory;
            pendingCollection = collection.Id;
            pendingPriority = priority;
            pendingOptions = selectedOptions.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            var basePaths = selected.Variants.Length == 0 ? selected.Paths : selected.BasePaths;
            var activeVariantPaths = selected.Variants.Where(v => selectedOptions.TryGetValue(v.Group, out var enabled) &&
                    enabled.Contains(v.Option, StringComparer.OrdinalIgnoreCase))
                .SelectMany(v => v.Paths);
            pendingExpectedPaths = basePaths.Concat(activeVariantPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (pendingExpectedPaths.Length == 0)
                throw new InvalidOperationException(T("Для выбранного варианта нет активных файлов анимации.",
                    "The selected variant has no active animation files."));
            pendingExpectedModRoot = Path.GetFullPath(Path.Combine(getRoot.Invoke(), selected.Directory));
            pendingExpectedFiles.Clear();
            foreach (var variant in selected.Variants.Where(v => selectedOptions.TryGetValue(v.Group, out var enabled) &&
                         enabled.Contains(v.Option, StringComparer.OrdinalIgnoreCase)))
            {
                foreach (var (gamePath, relativeFile) in variant.Files)
                {
                    if (string.IsNullOrWhiteSpace(relativeFile)) continue;
                    var absoluteFile = Path.GetFullPath(Path.Combine(pendingExpectedModRoot, relativeFile));
                    if (!absoluteFile.StartsWith(pendingExpectedModRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(T("Файл варианта выходит за пределы папки мода.",
                            "A variant file points outside its mod directory."));
                    // Penumbra resolves overlapping groups by their own priorities.
                    // The catalog does not model Multi/Combining groups, so it cannot
                    // predict the final file (e.g. a lip-sync override).
                    pendingExpectedFiles[gamePath] = absoluteFile;
                }
            }
            sendAt = Environment.TickCount64 + (IsPoseCommand(bookmark.Command) ? 300 : 150);
            pendingPoseIndex = bookmark.PoseIndex ?? GetPoseIndex(selected);
            pendingPoseType = bookmark.Command.Equals("/groundsit", StringComparison.OrdinalIgnoreCase)
                ? EmoteController.PoseType.GroundSit : EmoteController.PoseType.Idle;
            poseAttempts = 0;
            status = string.Format(T("Выбрано: {0} → {1}", "Selected: {0} → {1}"), selected.Name, pendingCommand);
        }
        catch (Exception ex)
        {
            if (pairPreparation) CancelPair();
            pendingCommand = "";
            pendingModDirectory = "";
            pendingOptions = null;
            pendingExpectedPaths = [];
            pendingExpectedFiles.Clear();
            pendingExpectedModRoot = "";
            try { if (activeCollection != Guid.Empty) removeTemporary.Invoke(activeCollection, TemporaryKey); } catch { /* Penumbra unavailable */ }
            status = ex.Message;
            Log.Warning(ex, "Emote Shelf switching failed");
        }
    }

    private void Update(IFramework _)
    {
        if (pairSession is null && pendingPoseIndex < 0) pairUntargetedPlayback = false;
        UpdateLink();
        pairAlignment?.Update();
        UpdatePair();
        if (manualPoseBookmark is { } manual && Environment.TickCount64 >= manualPoseReturnAt && manualPoseTarget >= 0)
        {
            var type = manual.Command.Equals("/groundsit", StringComparison.OrdinalIgnoreCase) ? EmoteController.PoseType.GroundSit : EmoteController.PoseType.Idle;
            if (CurrentPoseIndex(type) == manualPoseTarget) manualPoseBookmark = null;
        }
        if (initialScanPending && Objects.LocalPlayer is not null)
        {
            initialScanPending = false;
            Scan();
        }
        if (waitingForStand is { } bookmark)
        {
            var now = Environment.TickCount64;
            if (!IsGroundSitting())
            {
                if (standSettledAt == 0) standSettledAt = now + 250;
                if (now >= standSettledAt)
                {
                    waitingForStand = null;
                    Play(bookmark, pairSession is not null);
                }
            }
            else
            {
                standSettledAt = 0;
                if (now >= standDeadline)
                {
                    waitingForStand = null;
                    status = T("Не удалось встать из /groundsit.", "Could not stand up from /groundsit.") + " " + PoseDiagnostics();
                }
            }
            return;
        }
        if (pendingCommand.Length > 0 && Environment.TickCount64 >= sendAt)
        {
            if (!IsPoseCommand(pendingCommand) && !redrawCompleted && Environment.TickCount64 < redrawDeadline) return;
            if (!IsPoseCommand(pendingCommand) && !redrawCompleted) Log.Warning("Penumbra redraw confirmation timed out; continuing with verified mod paths");
            var command = pendingCommand;
            pendingCommand = "";
            try
            {
                var (ec, current) = getSettingsWithTemp.Invoke(pendingCollection, pendingModDirectory, "", false, false, 0);
                if (ec != PenumbraApiEc.Success || current is null || !current.Value.Item1 || current.Value.Item2 != pendingPriority)
                    throw new InvalidOperationException(T("Penumbra не подтвердила выбор мода; эмоция не запущена.",
                        "Penumbra did not confirm the selected mod; emote was not played."));
                if (pendingOptions is not null && pendingOptions.Any(expected =>
                    !current.Value.Item3.TryGetValue(expected.Key, out var actual) ||
                    !actual.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(
                        expected.Value.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(T("Penumbra не подтвердила вариант настроек мода; эмоция не запущена.",
                        "Penumbra did not confirm the saved mod options; emote was not played."));
                foreach (var gamePath in pendingExpectedPaths)
                {
                    var ecPath = resolvePath.Invoke(pendingCollection, gamePath, out var effectivePath);
                    if (ecPath != PenumbraApiEc.Success || !ResolvedAnimation.BelongsToMod(pendingExpectedModRoot, effectivePath))
                        throw new InvalidOperationException(string.Format(T(
                            "Penumbra отдаёт для {0} файл не из выбранного мода: {1}",
                            "Penumbra resolves {0} outside the selected mod: {1}"), gamePath, effectivePath));
                    if (pendingExpectedFiles.TryGetValue(gamePath, out var expectedFile) &&
                        !Path.GetFullPath(effectivePath).Equals(expectedFile, StringComparison.OrdinalIgnoreCase))
                        Log.Debug("Same-mod option override for {GamePath}: {EffectivePath}", gamePath, effectivePath);
                }
                if (pairSession is not null && pairPreparing)
                {
                    pairHeldCommand = command;
                    pairPreparing = false;
                    pairConfirmUntil = Environment.TickCount64 + 5000;
                    // UpdatePair confirms position/facing after redraw before readiness.
                }
                else DispatchPreparedCommand(command);
            }
            catch (Exception ex)
            {
                CancelPair();
                pendingPoseIndex = -1;
                status = string.Format(T("Мод переключен, но эмоция не запустилась: {0}", "Mod switched, but the emote did not start: {0}"), ex.Message);
                Log.Warning(ex, "Emote failed");
            }
            finally { if (pairHeldCommand.Length == 0) ClearPendingValidation(); }
        }
        if (pairSession is not null) return;
        if (pendingPoseIndex < 0 || pendingCommand.Length > 0 || Environment.TickCount64 < poseAt) return;
        if (!config.AutomaticPose) { pendingPoseIndex = -1; return; }
        try
        {
            var currentPose = CurrentPoseIndex(pendingPoseType);
            if (currentPose < 0)
            {
                if (++poseAttempts >= 8)
                {
                    pendingPoseIndex = -1;
                    status = pendingPoseType == EmoteController.PoseType.GroundSit
                        ? T("Не удалось войти в /groundsit до смены позы.", "Could not enter /groundsit before changing pose.")
                        : T("Стоячая idle-поза недоступна: останови предыдущую зацикленную эмоцию или движение и попробуй снова.",
                            "Standing idle is unavailable: stop the previous looping emote or movement, then retry.");
                    status += " " + PoseDiagnostics();
                }
                else poseAt = Environment.TickCount64 + 100;
                return;
            }
            var highestPose = EmoteController.GetAvailablePoses(pendingPoseType);
            if (pendingPoseIndex > highestPose)
            {
                status = string.Format(T("Поза {0} недоступна персонажу (максимум: {1}).",
                    "Pose {0} is unavailable for this character (maximum: {1})."), pendingPoseIndex, highestPose);
                pendingPoseIndex = -1;
                return;
            }
            if (currentPose == pendingPoseIndex)
            {
                status = string.Format(T("Игра сообщает слот {0:00}.", "Game reports slot {0:00}."), currentPose);
                Log.Information("Pose confirmed: {State}", PoseDiagnostics());
                pendingPoseIndex = -1;
                return;
            }
            if (poseAttempts++ >= 8)
            {
                pendingPoseIndex = -1;
                status = T("Не удалось переключить нужную позу. Попробуй /cpose вручную.", "Could not reach the target pose. Try /cpose manually.");
                return;
            }
            Log.Information("Automatic /cpose: target={Target}, current={Current}, attempt={Attempt}, tick={Tick}", pendingPoseIndex, currentPose, poseAttempts, Environment.TickCount64);
            ExecuteEmote("/cpose");
            status = string.Format(T("Переключаю позу {0} → {1} (шаг {2}).", "Changing pose {0} → {1} (step {2})."),
                currentPose, pendingPoseIndex, poseAttempts);
            poseAt = Environment.TickCount64 + 100;
        }
        catch (Exception ex) { pendingPoseIndex = -1; status = ex.Message; Log.Warning(ex, "Pose switch failed"); }
    }

    private void ClearPendingValidation()
    { pendingModDirectory = ""; pendingOptions = null; pendingExpectedPaths = []; pendingExpectedFiles.Clear(); pendingExpectedModRoot = ""; }

    private void DispatchPreparedCommand(string command)
    {
        if (!IsPoseCommand(command)) { ExecuteEmote(command); return; }
        var plan = PosePlayback.Prepare(command.Equals("/groundsit", StringComparison.OrdinalIgnoreCase),
            CurrentPoseIndex(pendingPoseType) >= 0, config.AutomaticPose, pendingPoseIndex >= 0);
        if (plan.Action == PosePlayback.Entry.RefreshActivePose)
        {
            if (Objects.LocalPlayer is { } local) redraw.Invoke(local.ObjectIndex, RedrawType.Redraw);
        }
        else if (plan.Action == PosePlayback.Entry.EnterGroundSit)
            ExecutePreparedEmote(command, plan.SetSavedGroundSlot);
        manualPoseBookmark = launchingBookmark;
        manualPoseTarget = pendingPoseIndex;
        manualPoseReturnAt = Environment.TickCount64 + plan.SettleMilliseconds;
        poseAt = manualPoseReturnAt;
        if (!plan.Cycle) pendingPoseIndex = -1;
    }

    private unsafe int CurrentPoseIndex(EmoteController.PoseType poseType = EmoteController.PoseType.GroundSit)
    {
        var player = Objects.LocalPlayer;
        if (player is null) return -1;
        var character = (Character*)player.Address;
        var controller = &character->EmoteController;
        if (poseType == EmoteController.PoseType.GroundSit)
        {
            return character->Mode == CharacterModes.InPositionLoop && character->ModeParam == 1
                ? controller->CPoseState : -1;
        }
        // A previous looping emote can leave an idle pose number cached in the
        // controller even though the character is not actually standing idle.
        return character->Mode == CharacterModes.Normal
            ? controller->CPoseState : -1;
    }


    private unsafe bool IsGroundSitting()
    {
        var player = Objects.LocalPlayer;
        if (player is null) return false;
        var character = (Character*)player.Address;
        return (character->Mode is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop) &&
               character->ModeParam == 1;
    }

    private unsafe void ExecutePreparedEmote(string command, bool setSavedSlot)
    {
        // Initial ground-sit selection happens just before entering the
        // stance, never while already seated. Active poses use ordinary /cpose.
        var state = PlayerState.Instance();
        if (!setSavedSlot || !config.AutomaticPose || !command.Equals("/groundsit", StringComparison.OrdinalIgnoreCase) ||
            pendingPoseIndex < 0 || pendingPoseIndex > EmoteController.GetAvailablePoses(EmoteController.PoseType.GroundSit) ||
            IsGroundSitting() || state == null)
        { ExecuteEmote(command); return; }
        var old = state->SelectedPoses[(int)EmoteController.PoseType.GroundSit];
        state->SelectedPoses[(int)EmoteController.PoseType.GroundSit] = (byte)pendingPoseIndex;
        try { ExecuteEmote(command); }
        catch { state->SelectedPoses[(int)EmoteController.PoseType.GroundSit] = old; throw; }
    }

    private unsafe string PoseDiagnostics()
    {
        var player = Objects.LocalPlayer;
        if (player is null) return "(no character)";
        var character = (Character*)player.Address;
        return $"(mode={(byte)character->Mode}/{character->ModeParam}, poseType={(byte)character->EmoteController.CurrentPoseType}, pose={character->EmoteController.CPoseState})";
    }

    private unsafe void StandUpFromGroundSit()
    {
        // /groundsit is a game toggle: while seated it exits the stance.
        ExecuteEmote("/groundsit");
    }

    private int GetPoseIndex(EmoteMod mod)
    {
        if (mod.PoseIndex.HasValue) return mod.PoseIndex.Value;
        if (config.PoseOverrides.TryGetValue($"{mod.Directory}|{mod.Command}", out var poseOverride) &&
            poseOverride >= 0 && (mod.PoseSlots.Length == 0 || mod.PoseSlots.Contains(poseOverride)))
            return poseOverride;
        return mod.PoseSlots.Length > 0 ? mod.PoseSlots[0] : -1;
    }

    private static bool IsPoseCommand(string command)
        => command.Equals("/groundsit", StringComparison.OrdinalIgnoreCase) ||
           IsStandingPoseCommand(command);

    private static bool IsStandingPoseCommand(string command)
        => command.Equals("/cpose", StringComparison.OrdinalIgnoreCase) ||
           command.Equals("/changepose", StringComparison.OrdinalIgnoreCase);

    private unsafe void ExecuteEmote(string command)
    {
        if (pairUntargetedPlayback) { ExecutePairEmote(command); return; }
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
        DrawPairWindow();
        DrawLinkNotice();
    }

    private void DrawOverlay()
    {
        var flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar;
        if (config.OverlayLocked) flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBackground;
        ImGui.SetNextWindowBgAlpha(Math.Clamp(config.PanelOpacity, 0f, 1f));
        ImGui.SetNextWindowSize(new Vector2(240, 90), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Emote Shelf##overlay", flags))
        { ImGui.End(); return; }
        if (config.Bookmarks.Count == 0) ImGui.TextDisabled(T("Добавь эмоции: /eshelf → Эмоции", "Add emotes: /eshelf → Emotes"));
        var columns = Math.Clamp(config.Columns, 1, 12);
        var size = new Vector2(Math.Clamp(config.IconSize, 16, 80));
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, Math.Clamp(config.PanelOpacity, 0.1f, 1f));
        for (var i = 0; i < config.Bookmarks.Count; i++)
        {
            var bookmark = config.Bookmarks[i];
            if (i % columns != 0) ImGui.SameLine();
            var isPose = ReferenceEquals(manualPoseBookmark, bookmark);
            var poseIcon = catalog?.IconFor("/changepose") ?? 0;
            if (poseIcon == 0) poseIcon = catalog?.IconFor("/cpose") ?? 0;
            var iconId = isPose ? poseIcon : bookmark.IconId == 0 ? 19u : bookmark.IconId;
            var icon = iconId == 0 ? null : Textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault();
            ImGui.PushID(i);
            var clicked = icon is not null
                ? ImGui.ImageButton(icon.Handle, size)
                : ImGui.Button(isPose ? "↻##cpose" : bookmark.Command, size);
            if (clicked)
            {
                if (ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift)
                {
                    config.Bookmarks.RemoveAt(i);
                    if (isPose) manualPoseBookmark = null;
                    Save();
                    i--;
                }
                else if (isPose) { pendingPoseIndex = -1; manualPoseReturnAt = Environment.TickCount64 + 100; ExecuteEmote("/cpose"); }
                else if (ImGui.GetIO().KeyShift && config.PairEnabled) BeginPair(bookmark);
                else Play(bookmark);
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                selectedModDirectory = bookmark.ModDirectory;
                rightClickMod = bookmark.ModDirectory;
                scrollToSelectedMod = true;
                var folder = FolderFor(bookmark.ModDirectory);
                foreach (var parent in FolderTree.Ancestors(folder).Append(folder))
                { expandedFolders.Add(parent); collapsedFolders.Remove(parent); }
                search = "";
                settingsOpen = true;
                selectEmotesTab = true;
                expandedVariants.Add($"{bookmark.ModDirectory}|{bookmark.Command}");
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{bookmark.Name}\n{(isPose ? ManualPoseHint(bookmark) : bookmark.Command)}\n{T("ПКМ — открыть мод; Ctrl+Shift+клик — убрать", "Right-click — open mod; Ctrl+Shift+click — remove")}" +
                (config.PairEnabled ? "\n" + T("Shift+клик — запуск в паре", "Shift+click — pair launch") : ""));
            ReorderBookmarkDragDrop(i);
            ImGui.PopID();
        }
        ImGui.End();
        ImGui.PopStyleVar();
    }

    private string ManualPoseHint(Bookmark bookmark)
    {
        var type = bookmark.Command.Equals("/groundsit", StringComparison.OrdinalIgnoreCase)
            ? EmoteController.PoseType.GroundSit : EmoteController.PoseType.Idle;
        var current = CurrentPoseIndex(type);
        return $"/cpose · {T("слот игры", "reported slot")}: {(current < 0 ? "?" : current.ToString("00"))} → {(manualPoseTarget < 0 ? "?" : manualPoseTarget.ToString("00"))}\n{T("Нажимай до нужной позы. Иконка вернётся автоматически.", "Click to reach the target pose. The icon restores automatically.")}";
    }

    private void DrawSettings()
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(.055f, .06f, .085f, .97f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.075f, .08f, .11f, .95f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(.27f, .24f, .36f, .7f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.25f, .19f, .36f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(.39f, .29f, .53f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(.48f, .34f, .65f, 1));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(.25f, .20f, .35f, 1));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(.32f, .25f, .44f, 1));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(.115f, .12f, .165f, 1));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 7f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(9, 7));
        try
        {
        ImGui.SetNextWindowSize(new Vector2(850, 650), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(540, 360), new Vector2(1600, 1200));
        if (!ImGui.Begin("Emote Shelf##settings", ref settingsOpen)) { ImGui.End(); return; }
        if (ImGui.BeginTabBar("##tabs"))
        {
            if (ImGui.BeginTabItem(T("Эмоции", "Emotes") + "###emotes", selectEmotesTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            {
                selectEmotesTab = false;
                DrawEmoteTabTwoPane();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(T("Панель", "Panel") + "###panel")) { DrawPanelTab(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(T("Как пользоваться", "How to use") + "###help")) { DrawHelpTab(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(T("Настройки", "Settings") + "###settings")) { DrawAdvancedTab(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        ImGui.End();
        }
        finally { ImGui.PopStyleVar(4); ImGui.PopStyleColor(9); }
    }

    private string T(string ru, string en) => Localization.Get(config.Language, ru, en);

    private void DrawEmoteTabTwoPane()
    {
        ImGui.TextWrapped(T("Выбери мод слева, затем предпросмотри и добавь его эмоцию справа. Закладки сохраняются автоматически.",
            "Select a mod on the left, then preview and add its emote on the right. Bookmarks save automatically."));
        if (ImGui.SmallButton(T("Обновить список", "Refresh list"))) Scan();
        ImGui.SameLine();
        ImGui.TextWrapped(status);
        if (config.HiddenMods.Count > 0)
            ImGui.TextDisabled(T("Скрытые моды можно вернуть во вкладке «Настройки».", "Restore hidden mods on the Settings tab."));
        ImGui.InputTextWithHint("##search", T("Поиск по эмоции или моду", "Search emote or mod"), ref search, 120);

        var groups = discovered.Where(m => m.Command.Length > 0 && (!config.HiddenMods.Contains(m.Directory) || m.Directory == rightClickMod))
            .GroupBy(m => m.Directory, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Any(m => m.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                m.EmoteName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                m.Command.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(g => g.First().Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (groups.Length > 0 && !groups.Any(g => g.Key == selectedModDirectory)) selectedModDirectory = groups[0].Key;

        var available = ImGui.GetContentRegionAvail();
        var leftWidth = Math.Clamp(available.X * .34f, 230, 340);
        ImGui.BeginChild("##leftPane", new Vector2(leftWidth, 0), false);
        if (ImGui.BeginChild("##modTree", new Vector2(0, -36), true))
        {
            foreach (var folder in config.Folders.Order(StringComparer.OrdinalIgnoreCase).Concat([""]).ToArray())
            {
                if (search.Length == 0 && FolderTree.Ancestors(folder).Any(collapsedFolders.Contains)) continue;
                var indent = folder.Count(c => c == '/') * 14f;
                if (indent > 0) ImGui.Indent(indent);
                if (folder.Length > 0)
                {
                    ImGui.PushID(folder);
                    var open = !collapsedFolders.Contains(folder) && (expandedFolders.Contains(folder) || search.Length > 0);
                    if (ImGui.Selectable($"{(open ? "▼" : "▶")} {FolderTree.Name(folder)}##folder", selectedFolderName == folder))
                    {
                        selectedFolderName = folder;
                        if (open) { expandedFolders.Remove(folder); collapsedFolders.Add(folder); }
                        else { collapsedFolders.Remove(folder); expandedFolders.Add(folder); }
                        open = !open;
                        config.ClosedFolders = new(collapsedFolders, StringComparer.OrdinalIgnoreCase); Save();
                    }
                    if (ImGui.BeginDragDropSource())
                    { draggedFolder = folder; ImGui.SetDragDropPayload("ESHELF_FOLDER", new byte[] { 1 }); ImGui.TextUnformatted(folder); ImGui.EndDragDropSource(); }
                    AcceptModDrop(folder);
                    ImGui.PopID();
                    if (!open) { if (indent > 0) ImGui.Unindent(indent); continue; }
                }
                else if (config.Folders.Count > 0)
                {
                    ImGui.TextDisabled(T("Без папки", "Unfiled"));
                    AcceptModDrop("");
                }
                foreach (var group in groups.Where(g => FolderFor(g.Key).Equals(folder, StringComparison.OrdinalIgnoreCase)))
                {
                    ImGui.PushID(group.Key);
                    if (ImGui.Selectable(group.First().Name, selectedModDirectory == group.Key))
                    { selectedModDirectory = group.Key; selectedFolderName = folder; }
                    if (scrollToSelectedMod && selectedModDirectory == group.Key) { ImGui.SetScrollHereY(.5f); scrollToSelectedMod = false; }
                    if (ImGui.BeginDragDropSource())
                    {
                        draggedModDirectory = group.Key;
                        ImGui.SetDragDropPayload("ESHELF_MOD", new byte[] { 1 });
                        ImGui.TextUnformatted(group.First().Name);
                        ImGui.EndDragDropSource();
                    }
                    ImGui.PopID();
                }
                if (indent > 0) ImGui.Unindent(indent);
            }
        }
        ImGui.EndChild();
        ImGui.PushID("folderBar");
        if (ImGui.SmallButton("+")) { folderEditMode = 1; createAtRoot = false; newFolderName = ""; ImGui.OpenPopup("##folderEdit"); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(T("Создать папку", "Create folder"));
        ImGui.SameLine();
        ImGui.BeginDisabled(selectedFolderName.Length == 0);
        if (ImGui.SmallButton("✎")) { folderEditMode = 2; newFolderName = FolderTree.Name(selectedFolderName); ImGui.OpenPopup("##folderEdit"); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(T("Переименовать папку", "Rename folder"));
        ImGui.SameLine();
        if (ImGui.SmallButton("×")) ImGui.OpenPopup("##deleteFolder");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(T("Удалить папку", "Delete folder"));
        ImGui.EndDisabled();
        if (ImGui.BeginPopup("##folderEdit"))
        {
            if (folderEditMode == 1) ImGui.Checkbox(T("Создать в корне", "Create at root"), ref createAtRoot);
            ImGui.TextUnformatted(folderEditMode == 1 && !createAtRoot ? selectedFolderName : FolderTree.Parent(selectedFolderName));
            ImGui.InputText(T("Название папки", "Folder name"), ref newFolderName, 60);
            if (ImGui.Button(T("Сохранить", "Save")))
            {
                SaveFolderEdit();
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        if (ImGui.BeginPopup("##deleteFolder"))
        {
            ImGui.TextWrapped(T("Моды останутся в каталоге без папки.", "Mods will remain in the catalog, unfiled."));
            if (ImGui.Button(T("Удалить папку", "Delete folder")))
            {
                config.Folders.RemoveAll(f => FolderTree.Contains(selectedFolderName, f));
                foreach (var directory in config.ModFolders.Where(x => FolderTree.Contains(selectedFolderName, x.Value))
                    .Select(x => x.Key).ToArray()) config.ModFolders.Remove(directory);
                expandedFolders.RemoveWhere(f => FolderTree.Contains(selectedFolderName, f));
                collapsedFolders.RemoveWhere(f => FolderTree.Contains(selectedFolderName, f));
                config.ClosedFolders = new(collapsedFolders, StringComparer.OrdinalIgnoreCase);
                selectedFolderName = "";
                Save();
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##modDetails", new Vector2(0, 0), true))
        {
            var selected = discovered.Where(m => m.Directory == selectedModDirectory && (!config.HiddenMods.Contains(m.Directory) || m.Directory == rightClickMod)).ToArray();
            if (selected.Length == 0) ImGui.TextDisabled(T("Выбери мод слева.", "Select a mod on the left."));
            else
            {
                ImGui.TextUnformatted(selected[0].Name);
                if (ImGui.SmallButton(T("Скрыть мод", "Hide mod"))) { rightClickMod = ""; config.HiddenMods.Add(selectedModDirectory); Save(); }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(180);
                var currentFolder = FolderFor(selectedModDirectory);
                if (ImGui.BeginCombo("##moveFolder", string.Format(T("Папка: {0}", "Folder: {0}"),
                    currentFolder.Length == 0 ? T("Без папки", "Unfiled") : currentFolder)))
                {
                    if (ImGui.Selectable(T("Без папки", "Unfiled"))) { config.ModFolders.Remove(selectedModDirectory); Save(); }
                    foreach (var folder in config.Folders)
                        if (ImGui.Selectable(folder, folder == currentFolder))
                        { config.ModFolders[selectedModDirectory] = folder; expandedFolders.Add(folder); Save(); }
                    ImGui.EndCombo();
                }
                ImGui.Separator();
                foreach (var mod in selected) DrawEmoteCard(mod);
            }
        }
        ImGui.EndChild();
    }

    private void AcceptModDrop(string folder)
    {
        if (!ImGui.BeginDragDropTarget()) return;
        var payload = ImGui.AcceptDragDropPayload("ESHELF_MOD");
        if (!payload.IsNull && draggedModDirectory.Length > 0)
        {
            if (folder.Length == 0) config.ModFolders.Remove(draggedModDirectory);
            else config.ModFolders[draggedModDirectory] = folder;
            draggedModDirectory = "";
            expandedFolders.Add(folder);
            Save();
        }
        var folderPayload = ImGui.AcceptDragDropPayload("ESHELF_FOLDER");
        if (!folderPayload.IsNull && draggedFolder.Length > 0 && !FolderTree.Contains(draggedFolder, folder))
        {
            MoveFolder(draggedFolder, FolderTree.Join(folder, FolderTree.Name(draggedFolder)));
            draggedFolder = "";
        }
        ImGui.EndDragDropTarget();
    }

    private void SaveFolderEdit()
    {
        var name = newFolderName.Trim();
        if (name.Length == 0 || name.Contains('/') || name.Contains('\\')) return;
        name = FolderTree.Join(folderEditMode == 2 ? FolderTree.Parent(selectedFolderName) : createAtRoot ? "" : selectedFolderName, name);
        if (config.Folders.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        if (folderEditMode == 2 && selectedFolderName.Length > 0)
        {
            MoveFolder(selectedFolderName, name);
        }
        else config.Folders.Add(name);
        selectedFolderName = name;
        expandedFolders.Add(name);
        foreach (var parent in FolderTree.Ancestors(name)) { expandedFolders.Add(parent); collapsedFolders.Remove(parent); }
        newFolderName = "";
        config.ClosedFolders = new(collapsedFolders, StringComparer.OrdinalIgnoreCase);
        Save();
    }

    private void MoveFolder(string oldRoot, string newRoot)
    {
        if (FolderTree.Contains(oldRoot, newRoot) || config.Folders.Contains(newRoot, StringComparer.OrdinalIgnoreCase)) return;
        config.Folders = config.Folders.Select(f => FolderTree.Rebase(f, oldRoot, newRoot)).ToList();
        foreach (var key in config.ModFolders.Keys.ToArray())
            config.ModFolders[key] = FolderTree.Rebase(config.ModFolders[key], oldRoot, newRoot);
        var closed = collapsedFolders.Select(f => FolderTree.Rebase(f, oldRoot, newRoot)).ToArray();
        collapsedFolders.Clear(); collapsedFolders.UnionWith(closed);
        expandedFolders.Clear(); expandedFolders.UnionWith(config.Folders.Except(collapsedFolders));
        foreach (var parent in FolderTree.Ancestors(newRoot)) { collapsedFolders.Remove(parent); expandedFolders.Add(parent); }
        config.ClosedFolders = new(collapsedFolders, StringComparer.OrdinalIgnoreCase);
        selectedFolderName = newRoot;
        Save();
    }

    private void DrawEmoteCard(EmoteMod mod)
    {
        var visibleVariants = mod.Variants.Where(v => showAllVariants || VariantCompatible(v, mod)).ToArray();
        ImGui.PushID(mod.Command);
        ImGui.BeginGroup();
        var icon = Textures.GetFromGameIcon(new GameIconLookup(mod.Icon == 0 ? 19u : mod.Icon)).GetWrapOrDefault();
        if (icon is not null) ImGui.Image(icon.Handle, new Vector2(42));
        else ImGui.Dummy(new Vector2(42));
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.TextUnformatted(mod.EmoteName);
        ImGui.SameLine();
        ImGui.TextDisabled(mod.Command);
        var locked = catalog?.IsUnlocked(mod.Command, Unlocks) == false;
        if (locked) ImGui.TextColored(new Vector4(1f, .65f, .25f, 1f), T("Не разблокирована", "Not unlocked"));
        if (mod.Variants.Length == 0)
        {
            ImGui.BeginDisabled(locked);
            if (ImGui.SmallButton(T("Предпросмотр", "Preview"))) Preview(mod, null);
            ImGui.SameLine();
            if (ImGui.SmallButton(T("На панель", "Add to panel"))) AddBookmark(mod, null);
            if (config.PairEnabled) { ImGui.SameLine(); if (ImGui.SmallButton(T("В паре", "Pair"))) BeginPair(mod); }
            ImGui.EndDisabled();
        }
        ImGui.EndGroup();
        if (IsPoseCommand(mod.Command)) DrawPoseSelector(mod);
        if (mod.Variants.Length > 0)
        {
            ImGui.Checkbox(T("Показать все варианты", "Show all variants"), ref showAllVariants);
            if (!draftOptions.TryGetValue(mod.Directory, out var draft))
            {
                try { draft = CreateBookmark(mod, null).SavedOptions ?? []; draftOptions[mod.Directory] = draft; }
                catch (Exception ex) { ImGui.TextWrapped(ex.Message); }
            }
            if (draft is not null)
            {
                foreach (var group in visibleVariants.GroupBy(v => v.Group))
                {
                    ImGui.PushID(group.Key);
                    var choices = draft.GetValueOrDefault(group.Key, []);
                    ImGui.TextUnformatted(group.Key);
                    ImGui.SetNextItemWidth(Math.Max(160, ImGui.GetContentRegionAvail().X - 16));
                    if (group.First().Multi)
                    {
                        foreach (var option in group)
                        {
                            var enabled = choices.Contains(option.Option);
                            if (ImGui.Checkbox(option.Option, ref enabled))
                            {
                                var updated = choices.ToList();
                                if (enabled) updated.Add(option.Option); else updated.Remove(option.Option);
                                draft[group.Key] = updated;
                            }
                        }
                    }
                    else if (ImGui.BeginCombo("##choice", choices.FirstOrDefault() ?? "—"))
                    {
                        foreach (var option in group)
                            if (ImGui.Selectable(option.Option, choices.Contains(option.Option)))
                            {
                                draft[group.Key] = [option.Option];
                                foreach (var other in mod.Variants.Where(v => v.Group != group.Key && v.OffOption is not null && v.Paths.Intersect(option.Paths, StringComparer.OrdinalIgnoreCase).Any()))
                                    draft[other.Group] = [other.OffOption!];
                            }
                        ImGui.EndCombo();
                    }
                    ImGui.PopID();
                }
                ImGui.Spacing();
                ImGui.BeginDisabled(locked);
                if (ImGui.Button(T("Предпросмотр", "Preview"))) PlayDraft(mod, false);
                ImGui.SameLine();
                if (ImGui.Button(T("На панель", "Add to panel"))) PlayDraft(mod, true);
                if (config.PairEnabled) { ImGui.SameLine(); if (ImGui.Button(T("В паре", "Pair"))) PlayDraft(mod, false, true); }
                ImGui.EndDisabled();
            }
        }
        ImGui.EndGroup();
        ImGui.Separator();
        ImGui.PopID();
    }

    private void PlayDraft(EmoteMod mod, bool add, bool pair = false)
    {
        try
        {
            var bookmark = CreateBookmark(mod, null);
            bookmark.SavedOptions = draftOptions[mod.Directory].ToDictionary(x => x.Key, x => x.Value.ToList());
            var active = mod.Variants.Where(v => bookmark.SavedOptions.TryGetValue(v.Group, out var values) && values.Contains(v.Option)).ToArray();
            if (!showAllVariants && active.Any(v => !VariantCompatible(v, mod)))
                throw new InvalidOperationException(T("Выбран вариант для другой модели. Выбери совместимый вариант или включи «Показать все».", "An option for another model is selected. Choose a compatible option or enable Show all."));
            bookmark.Name = $"{mod.Name} — {mod.EmoteName} — " + string.Join("; ", active.Select(v => $"{v.Group}: {v.Option}"));
            var pose = PoseSlot.FromPaths(active.SelectMany(v => v.Paths), mod.Command.Equals("/groundsit", StringComparison.OrdinalIgnoreCase) ? "j_" : "");
            if (IsPoseCommand(mod.Command) && pose.HasValue) bookmark.PoseIndex = pose;
            if (add) { config.Bookmarks.Add(bookmark); config.OverlayVisible = true; Save(); status = T("Добавлено на панель.", "Added to panel."); }
            else if (pair) BeginPair(bookmark);
            else Play(bookmark);
        }
        catch (Exception ex) { status = ex.Message; Log.Warning(ex, "Selected options failed"); }
    }

    private unsafe bool VariantCompatible(EmoteVariant variant, EmoteMod mod)
    {
        if (!papTableLoaded)
        {
            papTableLoaded = true;
            try
            {
                var file = Data.GetFile<Lumina.Data.FileResource>("chara/xls/animation/papLoadTable.plt");
                if (file is not null) papCompatibility = new(file.Data);
            }
            catch (Exception ex) { Log.Warning(ex, "PAP compatibility unavailable; retaining all variants"); }
        }
        if (papCompatibility is null || Objects.LocalPlayer is not { } player) return true;
        var draw = (CharacterBase*)((Character*)player.Address)->GameObject.DrawObject;
        if (draw == null || draw->GetObjectType() != ObjectType.CharacterBase || draw->GetModelType() != CharacterBase.ModelType.Human) return true;
        var model = ((Human*)draw)->RaceSexId;
        var paths = variant.Paths.Where(p => mod.Paths.Contains(p, StringComparer.OrdinalIgnoreCase)).ToArray();
        return paths.Length == 0 || paths.Any(p =>
        {
            if (!compatibilityCache.TryGetValue((model, p), out var match))
                compatibilityCache[(model, p)] = match = papCompatibility.Matches(p, model);
            return match != false;
        });
    }

    private void DrawEmoteTab()
    {
        ImGui.TextWrapped(T("Найди эмоцию, проверь её через «Предпросмотр» и добавь на панель. Закладка сохранится автоматически.",
            "Find an emote, try Preview, then add it to the panel. The bookmark is saved automatically."));
        if (ImGui.Button(T("Обновить список", "Refresh list"))) Scan();
        ImGui.TextWrapped(status);
        if (config.HiddenMods.Count > 0)
            ImGui.TextDisabled(T("Скрытые моды можно вернуть во вкладке «Настройки».",
                "Restore hidden mods on the Settings tab."));
        ImGui.SetNextItemWidth(180);
        ImGui.InputTextWithHint("##newFolder", T("Новая папка", "New folder"), ref newFolderName, 60);
        ImGui.SameLine();
        if (ImGui.SmallButton(T("Создать папку", "Create folder")))
        {
            var folder = newFolderName.Trim();
            if (folder.Length > 0 && !config.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                config.Folders.Add(folder);
                expandedFolders.Add(folder);
                newFolderName = "";
                Save();
            }
        }
        ImGui.InputTextWithHint("##search", T("Поиск по эмоции или моду", "Search emote or mod"), ref search, 120);
        if (ImGui.BeginChild("##emoteList", new Vector2(0, 0), true))
        {
            var groups = discovered.Where(m => m.Command.Length > 0 && !config.HiddenMods.Contains(m.Directory))
                .GroupBy(m => m.Directory, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.First().Name, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var folder in config.Folders.Concat([""]))
            {
                if (folder.Length > 0)
                {
                    var open = !collapsedFolders.Contains(folder) && (expandedFolders.Contains(folder) || search.Length > 0);
                    if (ImGui.SmallButton($"{(open ? "▼" : "▶")} {folder}##folder{folder}"))
                    {
                        if (open) { expandedFolders.Remove(folder); collapsedFolders.Add(folder); }
                        else { collapsedFolders.Remove(folder); expandedFolders.Add(folder); }
                        open = !open;
                    }
                    if (!open) continue;
                }
                else if (config.Folders.Count > 0) ImGui.TextDisabled(T("Без папки", "Unfiled"));
            foreach (var group in groups.Where(g => FolderFor(g.Key).Equals(folder, StringComparison.OrdinalIgnoreCase)))
            {
                var matches = group.Where(m => m.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    m.EmoteName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    m.Command.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length == 0) continue;
                ImGui.PushID(group.Key);
                var modExpanded = !collapsedMods.Contains(group.Key) && (expandedMods.Contains(group.Key) || search.Length > 0);
                if (ImGui.SmallButton($"{(modExpanded ? "▼" : "▶")} {group.First().Name} ({matches.Length})##mod"))
                {
                    if (modExpanded) { expandedMods.Remove(group.Key); collapsedMods.Add(group.Key); }
                    else { collapsedMods.Remove(group.Key); expandedMods.Add(group.Key); }
                }
                ImGui.SameLine();
                if (ImGui.SmallButton(T("Скрыть мод", "Hide mod"))) { config.HiddenMods.Add(group.Key); Save(); }
                ImGui.SameLine();
                ImGui.SetNextItemWidth(150);
                if (ImGui.BeginCombo("##folderSelect", folder.Length == 0 ? T("Без папки", "Unfiled") : folder))
                {
                    if (ImGui.Selectable(T("Без папки", "Unfiled"), folder.Length == 0))
                    { config.ModFolders.Remove(group.Key); Save(); }
                    foreach (var option in config.Folders)
                        if (ImGui.Selectable(option, option.Equals(folder, StringComparison.OrdinalIgnoreCase)))
                        { config.ModFolders[group.Key] = option; expandedFolders.Add(option); Save(); }
                    ImGui.EndCombo();
                }
                if (modExpanded)
                {
                    ImGui.Indent();
                    foreach (var mod in matches)
                    {
                        ImGui.PushID(mod.Command);
                        var variantKey = $"{mod.Directory}|{mod.Command}";
                        var expanded = expandedVariants.Contains(variantKey);
                        var poseHintHeight = IsPoseCommand(mod.Command)
                            ? mod.PoseIndex.HasValue ? 94 : 137 : 0;
                        ImGui.BeginChild("##card", new Vector2(0, (mod.Variants.Length == 0 ? 88 : expanded ? 90 + mod.Variants.Length * 23 : 80) + poseHintHeight), true);
                        var icon = Textures.GetFromGameIcon(new GameIconLookup(mod.Icon == 0 ? 19u : mod.Icon)).GetWrapOrDefault();
                        if (icon is not null) ImGui.Image(icon.Handle, new Vector2(42));
                        else ImGui.Dummy(new Vector2(42));
                        ImGui.SameLine();
                        ImGui.BeginGroup();
                        ImGui.TextUnformatted(mod.EmoteName);
                        ImGui.SameLine();
                        ImGui.TextDisabled(mod.Command);
                        var lockedEmote = catalog?.IsUnlocked(mod.Command, Unlocks) == false;
                        if (lockedEmote)
                        {
                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(1f, .65f, .25f, 1f), T("Не разблокирована", "Not unlocked"));
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip(T("Эта игровая эмоция ещё не открыта у персонажа. Мод не может запустить её одной заменой файлов.",
                                "This game emote is not unlocked on this character. A file replacement alone cannot play it."));
                        }
                        if (mod.Variants.Length == 0)
                        {
                            ImGui.BeginDisabled(lockedEmote);
                            if (ImGui.SmallButton(T("Предпросмотр", "Preview"))) Preview(mod, null);
                            ImGui.SameLine();
                            if (ImGui.SmallButton(T("На панель", "Add to panel"))) AddBookmark(mod, null);
                            ImGui.EndDisabled();
                        }
                        ImGui.EndGroup();
                        if (IsPoseCommand(mod.Command)) DrawPoseSelector(mod);
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
                                    ImGui.BeginDisabled(lockedEmote);
                                    if (ImGui.SmallButton(T("Предпросмотр", "Preview"))) Preview(mod, variant);
                                    ImGui.SameLine();
                                    if (ImGui.SmallButton(T("На панель", "Add to panel"))) AddBookmark(mod, variant);
                                    ImGui.EndDisabled();
                                    ImGui.SameLine();
                                    ImGui.TextUnformatted($"{variant.Group} → {variant.Option}");
                                    ImGui.PopID();
                                }
                        }
                        ImGui.EndChild();
                        ImGui.PopID();
                    }
                    ImGui.Unindent();
                }
                ImGui.Separator();
                ImGui.PopID();
            }
            }
        }
        ImGui.EndChild();
    }

    private string FolderFor(string modDirectory)
    {
        var folder = config.ModFolders.GetValueOrDefault(modDirectory, "");
        return config.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase) ? folder : "";
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
        if (variant is not null)
        {
            foreach (var other in mod.Variants.Where(v => v.Group != variant.Group && v.OffOption is not null &&
                v.Paths.Intersect(variant.Paths, StringComparer.OrdinalIgnoreCase).Any()))
                saved[other.Group] = [other.OffOption!];
            saved[variant.Group] = [variant.Option];
        }
        var posePrefix = mod.Command.Equals("/groundsit", StringComparison.OrdinalIgnoreCase) ? "j_" : "";
        var variantPose = variant is not null && mod.PoseSlots.Length > 0
            ? PoseSlot.FromPaths(variant.Paths, posePrefix) : null;
        var targetPose = variantPose ?? GetPoseIndex(mod);
        return new Bookmark { ModDirectory = mod.Directory,
            Name = variant is null ? $"{mod.EmoteName} — {mod.Name}" :
                $"{mod.Name} — {mod.EmoteName} — {variant.Group} → {variant.Option}",
            Command = mod.Command, IconId = mod.Icon, SavedOptions = saved,
            PoseIndex = targetPose >= 0 ? targetPose : null };
    }

    private void DrawPoseSelector(EmoteMod mod)
    {
        var type = mod.Command.Equals("/groundsit", StringComparison.OrdinalIgnoreCase)
            ? EmoteController.PoseType.GroundSit : EmoteController.PoseType.Idle;
        var current = CurrentPoseIndex(type);
        var target = GetPoseIndex(mod);
        ImGui.TextDisabled($"{T("Текущий слот", "Current slot")}: {(current < 0 ? "—" : current.ToString("00"))} / {T("нужен", "target")}: {(target < 0 ? "—" : target.ToString("00"))}");
        ImGui.BeginDisabled(current < 0 || pendingCommand.Length > 0 || waitingForStand is not null);
        if (ImGui.SmallButton("Change pose##manual"))
        {
            pendingPoseIndex = -1;
            ExecuteEmote("/cpose");
        }
        ImGui.EndDisabled();
        ImGui.TextWrapped(T("Выбирай позу кнопкой Change pose. Автоматика — экспериментальная настройка.", "Select the pose with Change pose. Automation is an experimental setting."));
        if (mod.PoseIndex.HasValue)
        {
            var file = mod.Command.Equals("/groundsit", StringComparison.OrdinalIgnoreCase) ? "j_pose" : "pose";
            ImGui.TextDisabled(string.Format(T("Мод заменяет слот {0}{1:00}.",
                "This mod replaces slot {0}{1:00}."), file, mod.PoseIndex.Value));
            return;
        }
        if (mod.PoseSlots.Length > 1)
        {
            var slotKey = $"{mod.Directory}|{mod.Command}";
            var selectedSlot = GetPoseIndex(mod);
            var file = mod.Command.Equals("/groundsit", StringComparison.OrdinalIgnoreCase) ? "j_pose" : "pose";
            ImGui.SetNextItemWidth(150);
            if (ImGui.BeginCombo(T("Слот мода", "Mod slot") + "##pose", $"{file}{selectedSlot:00}"))
            {
                foreach (var slot in mod.PoseSlots)
                    if (ImGui.Selectable($"{file}{slot:00}", selectedSlot == slot))
                    { config.PoseOverrides[slotKey] = slot; Save(); }
                ImGui.EndCombo();
            }
            ImGui.TextDisabled(T("В этом моде несколько слотов поз; выбери нужный файл.",
                "This mod replaces several pose slots; choose the intended file."));
            return;
        }
        var key = $"{mod.Directory}|{mod.Command}";
        var selected = config.PoseOverrides.GetValueOrDefault(key, -1);
        var label = selected < 0 ? T("Авто", "Auto") : string.Format(T("Поза {0}", "Pose {0}"), selected + 1);
        if (!ImGui.TreeNode(T("Выбрать позу вручную (если авто не работает)", "Select pose manually (if auto fails)") + "##poseAdvanced")) return;
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo(T("Нужная поза", "Target pose") + "##pose", label))
        {
            if (ImGui.Selectable(T("Авто", "Auto"), selected < 0)) { config.PoseOverrides.Remove(key); Save(); }
            for (var i = 0; i < 4; i++)
                if (ImGui.Selectable(string.Format(T("Поза {0}", "Pose {0}"), i + 1), selected == i))
                { config.PoseOverrides[key] = i; Save(); }
            ImGui.EndCombo();
        }
        ImGui.TextDisabled(T("После /groundsit смена позы может занять пару секунд.",
            "After /groundsit, changing pose may take a couple of seconds."));
        ImGui.TextDisabled(T("Авто не меняет позу. Поза 1–4 — слоты /groundsit, не варианты мода.",
            "Auto does not switch pose. Poses 1–4 are /groundsit slots, not mod variants."));
        ImGui.TreePop();
    }

    private void DrawPanelTab()
    {
        if (ImGui.Checkbox(T("Показывать игровую панель", "Show in-game panel"), ref config.OverlayVisible)) Save();
        ImGui.SameLine();
        if (ImGui.Checkbox(T("Закрепить положение", "Lock position"), ref config.OverlayLocked)) Save();
        if (ImGui.SliderInt(T("Значков в строке", "Icons per row"), ref config.Columns, 1, 12)) Save();
        if (ImGui.SliderFloat(T("Размер значка", "Icon size"), ref config.IconSize, 16, 80)) Save();
        if (ImGui.SliderFloat(T("Прозрачность панели", "Panel opacity"), ref config.PanelOpacity, 0.1f, 1f)) Save();
        ImGui.TextWrapped(T("Панель — отдельное окно с иконками. Открепи её, чтобы перетащить за свободное место. Скрыть можно здесь или командой /eshelf hide.",
            "The icon panel is a separate window. Unlock it and drag its empty area to move it. Hide it here or with /eshelf hide."));
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
            ImGui.SmallButton($"≡##drag{i}");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(T("Перетащи, чтобы изменить порядок", "Drag to reorder"));
            ReorderBookmarkDragDrop(i);
            ImGui.SameLine();
            var name = b.Name;
            ImGui.SetNextItemWidth(320);
            if (ImGui.InputText($"##name{i}", ref name, 100)) { b.Name = name; Save(); }
            ImGui.SameLine();
            ImGui.TextDisabled(b.Command);
        }
        ImGui.TextDisabled(T("Каждый значок хранит свой вариант настроек мода. Он не меняется при переключении других эмоций.",
            "Each icon keeps its own mod options. Switching other emotes does not replace them."));
    }

    private void ReorderBookmarkDragDrop(int target)
    {
        if (ImGui.BeginDragDropSource())
        {
            draggedBookmark = target;
            ImGui.SetDragDropPayload("ESHELF_BOOKMARK", new byte[] { 1 });
            ImGui.TextUnformatted(config.Bookmarks[target].Name);
            ImGui.EndDragDropSource();
        }
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ESHELF_BOOKMARK");
            if (!payload.IsNull && draggedBookmark >= 0 && draggedBookmark < config.Bookmarks.Count && draggedBookmark != target)
            {
                var bookmark = config.Bookmarks[draggedBookmark];
                config.Bookmarks.RemoveAt(draggedBookmark);
                config.Bookmarks.Insert(target, bookmark);
                draggedBookmark = -1;
                Save();
            }
            ImGui.EndDragDropTarget();
        }
    }

    private void DrawHelpTab()
    {
        ImGui.TextWrapped(T(
            "1. Во вкладке «Эмоции» выбери мод слева. Справа проверь нужный вариант через «Предпросмотр» и добавь его на панель. Кнопки под списком создают, переименовывают и удаляют локальные папки; мод можно перетащить в папку.\n\n2. Клик по значку запускает эмоцию, Ctrl+Shift+клик удаляет значок. Значки можно переставлять перетаскиванием. Панель настраивается во вкладке «Панель».\n\n3. Плагин временно выбирает мод в Penumbra; сброс — во вкладке «Настройки». Для /groundsit и стоячих idle номер позы определяется из файлов мода. При нескольких слотах выбери нужный файл. Неопознанные моды находятся в отдельном разделе настроек.",
            "1. On Emotes, select a mod on the left. On the right, test a variant with Preview and add it to the panel. Buttons below the list create, rename, and delete local folders; drag mods onto folders.\n\n2. Click an icon to play; Ctrl+Shift+click to remove it. Drag icons to reorder them. Customize the overlay on Panel.\n\n3. The plugin temporarily selects a Penumbra mod; clear this on Settings. It detects /groundsit and standing-idle slots from mod files. Select a file when several slots are present. Unrecognized animation mods are tucked away in Settings."));
        ImGui.Separator();
        ImGui.TextWrapped(T("Неинтересные моды можно скрыть прямо в каталоге. Вернуть их можно во вкладке «Настройки» → «Скрытые моды». Уже добавленные значки при этом остаются на панели.",
            "Hide unwanted mods directly in the browser. Restore them under Settings → Hidden mods. Existing panel bookmarks remain intact."));
    }

    private void DrawAdvancedTab()
    {
        if (ImGui.Checkbox(T("Автовыбор позы (экспериментально, недостаточно протестировано)", "Automatic pose selection (experimental, not fully tested)"), ref config.AutomaticPose))
        { pendingPoseIndex = -1; Save(); }
        DrawPairSettings();
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
            CancelPair();
            try { if (activeCollection != Guid.Empty) removeTemporary.Invoke(activeCollection, TemporaryKey); status = T("Временные настройки сняты.", "Temporary settings cleared."); }
            catch (Exception ex) { status = ex.Message; }
            activeCollection = Guid.Empty;
        }
        ImGui.TextWrapped(T("Неопознанные анимации: только если нужной эмоции нет во вкладке «Эмоции», укажи её игровую команду.",
            "Unidentified animations: only if your emote is missing from Emotes, enter its game command here."));
        if (config.HiddenMods.Count > 0 && ImGui.TreeNode($"{T("Скрытые моды", "Hidden mods")} ({config.HiddenMods.Count})###hidden"))
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
        var unknown = discovered.Where(m => m.Command.Length == 0 && !config.HiddenMods.Contains(m.Directory)).ToArray();
        if (unknown.Length > 0 && ImGui.TreeNode($"{T("Неопознанные моды", "Unrecognized mods")} ({unknown.Length})###unknown"))
        {
        ImGui.TextWrapped(T("Это моды с файлами анимаций, но без надёжно определённой игровой эмоции. Многие меняют боевые или фоновые анимации и не требуют действий здесь. /cpose3 — не команда игры.",
            "These mods contain animation files, but no reliable game emote was identified. Many affect combat or idle animations and need no action here. /cpose3 is not a game command."));
        foreach (var mod in unknown)
        {
            var command = config.CommandOverrides.GetValueOrDefault(mod.Directory, "");
            ImGui.PushID(mod.Directory);
            ImGui.TextUnformatted(mod.Name);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputText("##command", ref command, 40)) { config.CommandOverrides[mod.Directory] = command; Save(); }
            ImGui.SameLine();
            ImGui.BeginDisabled(!ModScanner.ValidCommand(command) || catalog?.IsKnownCommand(command) != true);
            if (ImGui.Button(T("На панель", "Add to panel")))
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
            ImGui.EndDisabled();
            ImGui.PopID();
        }
        ImGui.TreePop();
        }
    }

    public void Dispose()
    {
        ContextMenus.OnMenuOpened -= OnPairContextMenu;
        StopLink();
        CancelPair();
        pairAlignment?.Dispose();
        pendingCommand = "";
        redrawCompletedSubscription.Dispose();
        try { if (activeCollection != Guid.Empty) removeTemporary.Invoke(activeCollection, TemporaryKey); } catch { /* Penumbra unloaded */ }
        Framework.Update -= Update;
        Pi.UiBuilder.Draw -= Draw;
        Pi.UiBuilder.OpenConfigUi -= OpenSettings;
        Pi.UiBuilder.OpenMainUi -= OpenSettings;
        Commands.RemoveHandler("/eshelf");
        Commands.RemoveHandler("/es");
        Save();
    }
}
