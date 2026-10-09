using System.Text.Json;
using Dalamud.Bindings.ImGui;

namespace EmoteShelf;

public sealed partial class Plugin
{
    private string pairOfferId = "", offerEditorId = "", offerEditorError = "";
    private bool pairOfferSeen;
    private long pairOfferQueuedAt;
    private EmoteMod[] offerMods = [];
    private int offerModIndex;
    private Bookmark? offerDraft;

    private static string OfferRole(Bookmark bookmark) => PairRules.Hash(JsonSerializer.Serialize(new
    {
        command = bookmark.Command.ToLowerInvariant(), pose = bookmark.PoseIndex,
        options = bookmark.SavedOptions?.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new { group = p.Key, choices = p.Value.Order(StringComparer.Ordinal).ToArray() }).ToArray(),
    }));

    private void SelectOfferMod(int index)
    {
        offerModIndex = index;
        offerEditorError = "";
        offerDraft = null;
        try
        {
            var mod = offerMods[index];
            offerDraft = CreateBookmark(mod, null);
            // Adapt known incompatible defaults only when a compatible choice is unique.
            foreach (var group in mod.Variants.GroupBy(v => v.Group).Where(g => !g.First().Multi))
            {
                var compatible = group.Where(v => VariantCompatible(v, mod) && v.Option != v.OffOption).ToArray();
                var chosen = offerDraft.SavedOptions!.GetValueOrDefault(group.Key, []);
                if (compatible.Length == 1 && group.Any(v => chosen.Contains(v.Option) && !VariantCompatible(v, mod)))
                    offerDraft.SavedOptions![group.Key] = [compatible[0].Option];
            }
        }
        catch (Exception ex) { offerEditorError = ex.Message; }
    }

    private Bookmark FinalOfferDraft(EmoteMod mod)
    {
        var draft = offerDraft ?? throw new InvalidOperationException("No local role selected");
        var active = mod.Variants.Where(v => draft.SavedOptions!.TryGetValue(v.Group, out var choices) && choices.Contains(v.Option)).ToArray();
        if (active.Any(v => !VariantCompatible(v, mod)))
            throw new InvalidOperationException(T("Выбери вариант для своей модели тела.", "Choose options compatible with your character model."));
        var pose = IsPoseCommand(mod.Command)
            ? PoseSlot.FromPaths(active.SelectMany(v => v.Paths), mod.Command.Equals("/groundsit", StringComparison.OrdinalIgnoreCase) ? "j_" : "") ?? draft.PoseIndex
            : draft.PoseIndex;
        return new Bookmark { ModDirectory = draft.ModDirectory, Command = draft.Command, IconId = draft.IconId, PoseIndex = pose,
            Name = $"{mod.Name} — {mod.EmoteName} — " + string.Join("; ", active.Select(v => $"{v.Group}: {v.Option}")),
            SavedOptions = draft.SavedOptions!.ToDictionary(p => p.Key, p => p.Value.ToList()) };
    }

    private void DrawLaunchInvitation(LaunchInvitation request)
    {
        if (offerEditorId != request.Offer.Id)
        {
            offerEditorId = request.Offer.Id;
            offerEditorError = "";
            offerDraft = null;
            offerMods = discovered.Where(m => PairRules.Family(m.Name) == request.Offer.Family).ToArray();
            // A different emote is a suggestion, not proof of a semantic role.
            var alternatives = offerMods.Select((m, i) => (m, i)).Where(x => PairRules.Hash(x.m.Command.ToLowerInvariant()) != request.Offer.Command).ToArray();
            if (offerMods.Length > 0) SelectOfferMod(alternatives.Length == 1 ? alternatives[0].i : 0);
        }
        ImGui.TextWrapped((LinkedPartner?.Name.TextValue ?? T("Партнёр", "Partner")) + $" · {request.Remaining}s");
        ImGui.TextWrapped(offerMods.FirstOrDefault()?.Name ?? T("Этот мод не найден в твоём списке.", "This mod is not in your local list."));
        if (!request.Incoming)
        {
            ImGui.TextWrapped(T("Ожидаю, пока партнёр выберет роль и примет предложение.", "Waiting for your partner to choose a role and accept."));
            if (ImGui.Button(T("Отменить предложение", "Cancel proposal")))
            {
                if (pairOfferId == request.Offer.Id) CancelPair();
                else link?.Act("cancel_launch", invitation: request.Offer.Id);
            }
            return;
        }
        var occupied = offerMods.FirstOrDefault(m => PairRules.Hash(m.Command.ToLowerInvariant()) == request.Offer.Command);
        ImGui.TextWrapped(T("Партнёр выбрал: ", "Partner selected: ") + (occupied?.EmoteName ?? T("другую локальную версию", "another local version")));
        ImGui.TextWrapped(T("Выбери свою роль. Другая эмоция предложена, если она одна; варианты одного слота проверь вручную.",
            "Choose your role. A unique alternative emote is suggested; verify same-slot role options yourself."));
        if (offerMods.Length > 0)
        {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo(T("##role", "##role"), offerMods[offerModIndex].EmoteName))
            {
                for (var i = 0; i < offerMods.Length; i++)
                    if (ImGui.Selectable(offerMods[i].EmoteName + "##" + i, offerModIndex == i)) SelectOfferMod(i);
                ImGui.EndCombo();
            }
            var mod = offerMods[offerModIndex];
            if (offerDraft is not null)
            {
                if (ImGui.BeginChild("offerOptions", new System.Numerics.Vector2(0, 180)))
                foreach (var group in mod.Variants.GroupBy(v => v.Group))
                {
                    ImGui.PushID(group.Key);
                    ImGui.TextUnformatted(group.Key);
                    var choices = offerDraft.SavedOptions!.GetValueOrDefault(group.Key, []);
                    var options = group.Where(v => VariantCompatible(v, mod)).ToArray();
                    if (group.First().Multi)
                    {
                        foreach (var option in options)
                        {
                            var selected = choices.Contains(option.Option);
                            if (ImGui.Checkbox(option.Option, ref selected))
                            {
                                var updated = choices.ToList();
                                if (selected) updated.Add(option.Option); else updated.Remove(option.Option);
                                offerDraft.SavedOptions![group.Key] = updated;
                            }
                        }
                    }
                    else
                    {
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.BeginCombo("##option", choices.FirstOrDefault() ?? "—"))
                        {
                            foreach (var option in options)
                                if (ImGui.Selectable(option.Option, choices.Contains(option.Option)))
                                {
                                    offerDraft.SavedOptions![group.Key] = [option.Option];
                                    foreach (var other in mod.Variants.Where(v => v.Group != group.Key && v.OffOption is not null && v.Paths.Intersect(option.Paths, StringComparer.OrdinalIgnoreCase).Any()))
                                        offerDraft.SavedOptions[other.Group] = [other.OffOption!];
                                }
                            ImGui.EndCombo();
                        }
                    }
                    ImGui.PopID();
                }
                ImGui.EndChild();
                if (IsPoseCommand(mod.Command) && mod.PoseSlots.Length > 1)
                {
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.BeginCombo("##offerPose", T("Слот: ", "Slot: ") + (offerDraft.PoseIndex?.ToString("00") ?? "?")))
                    {
                        foreach (var slot in mod.PoseSlots)
                            if (ImGui.Selectable(slot.ToString("00"), offerDraft.PoseIndex == slot)) offerDraft.PoseIndex = slot;
                        ImGui.EndCombo();
                    }
                }
            }
            ImGui.TextWrapped(T("Принятие разрешает обязательное выравнивание: подход, точную доводку позиции и угла, затем запуск.", "Accepting authorizes required alignment: approach, fine position/facing correction, then playback."));
            ImGui.BeginDisabled(offerDraft is null || pairSession is not null || LinkedPartner is null);
            if (ImGui.Button(T("Принять и запустить", "Accept and start")))
            {
                try
                {
                    if (!LinkFresh || link?.Snapshot.Launch?.Offer.Id != request.Offer.Id) throw new InvalidOperationException("Proposal expired");
                    var bookmark = FinalOfferDraft(mod);
                    if (OfferRole(bookmark) == request.Offer.Role)
                        throw new InvalidOperationException(T("Это та же комбинация, что у партнёра. Выбери другую роль/вариант.", "This is the partner's exact combination. Select another role/option."));
                    BeginPairCore(bookmark, request.Offer.Id);
                    if (pairSession is not null) link!.Act("accept_launch", invitation: request.Offer.Id);
                    else offerEditorError = pairMessage;
                }
                catch (Exception ex) { offerEditorError = ex.Message; }
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
        }
        else if (ImGui.Button(T("Обновить список модов", "Refresh mods"))) { Scan(); offerEditorId = ""; }
        if (ImGui.Button(T("Отказаться", "Decline"))) link?.Act("reject_launch", invitation: request.Offer.Id);
        if (offerEditorError.Length > 0) ImGui.TextWrapped(offerEditorError);
    }
}
