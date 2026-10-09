using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.IoC;
using Dalamud.Plugin.Services;

namespace EmoteShelf;

public sealed partial class Plugin
{
    [PluginService] private static IContextMenu ContextMenus { get; set; } = null!;
    private LinkClient? link;
    private bool linkPreview, linkPositionApplied;
    private string linkNotice = "", lastLinkState = "", lastLinkPartner = "", handledInvitation = "";
    private long linkNoticeUntil;
    private long pairNoticeUntil;
    private static string LinkIdentity(IPlayerCharacter player) => PairRules.Identity(player.Name.TextValue, player.HomeWorld.RowId, 0, 0);
    private IPlayerCharacter? LinkActor(string identity) => Objects.OfType<IPlayerCharacter>().FirstOrDefault(p => LinkIdentity(p) == identity);
    private bool LinkFresh => link is not null && Environment.TickCount64 - link.Snapshot.ReceivedAt < 4000;
    private IPlayerCharacter? LinkedPartner => LinkFresh && link!.Snapshot.State == "linked" ? LinkActor(link.Snapshot.Partner) : null;

    private void StopLink()
    {
        link?.Dispose(); link = null;
        lastLinkState = lastLinkPartner = handledInvitation = "";
        CancelPair();
    }
    private void UpdateLink()
    {
        if (!config.PairEnabled || !ClientState.IsLoggedIn || Objects.LocalPlayer is not { } local || !LightlessLoaded())
        { if (link is not null) StopLink(); return; }
        if (link is not null && link.Identity != LinkIdentity(local)) StopLink();
        link ??= new LinkClient(LinkIdentity(local));
        var s = link.Snapshot;
        if (s.Error.Length > 0 && linkNotice != s.Error) { linkNotice = s.Error; linkNoticeUntil = Environment.TickCount64 + 5000; }
        if (s.State != lastLinkState || s.Partner != lastLinkPartner)
        {
            if (s.State == "linked") linkNotice = T("Партнёр подключён. Shift+иконка отправляет предложение анимации с выбором роли.", "Partner connected. Shift+icon sends an animation proposal with role selection.");
            else if (lastLinkState == "linked") { CancelPair(); linkNotice = T("Связь с партнёром завершена.", "Partner connection ended."); }
            else if (s.State == "idle" && lastLinkState is "incoming" or "outgoing") linkNotice = T("Приглашение отклонено, отменено или истекло.", "Invitation declined, cancelled or expired.");
            if (linkNotice.Length > 0) linkNoticeUntil = Environment.TickCount64 + 6000;
            lastLinkState = s.State; lastLinkPartner = s.Partner;
        }
        if (pairSession is not null && (!LinkFresh || s.State != "linked")) CancelPair(T("Связь с партнёром потеряна.", "Partner connection lost."));
    }
    private void OnPairContextMenu(IMenuOpenedArgs args)
    {
        if (!config.PairEnabled || args.Target is not MenuTargetDefault { TargetObject: IPlayerCharacter actor } ||
            Objects.LocalPlayer is not { } local || actor.GameObjectId == local.GameObjectId) return;
        var identity = LinkIdentity(actor);
        var disconnect = LinkFresh && link!.Snapshot.State == "linked" && link.Snapshot.Partner == identity;
        args.AddMenuItem(new MenuItem
        {
            Name = T(disconnect ? "Emote Shelf: отключить партнёра" : "Emote Shelf: предложить парную сессию",
                disconnect ? "Emote Shelf: disconnect partner" : "Emote Shelf: invite partner"),
            PrefixChar = 'E', Priority = 100,
            IsEnabled = disconnect || (LinkFresh && link!.Snapshot.State == "idle" && LightlessHandles(actor.Address)),
            OnClicked = _ =>
            {
                if (disconnect) { CancelPair(); link?.Act("disconnect"); }
                else if (LinkFresh && link!.Snapshot.State == "idle" && LinkActor(identity) is { } current && LightlessHandles(current.Address))
                    link.Act("invite", identity);
            },
        });
    }
    private void DrawLinkSettings()
    {
        var partner = LinkedPartner;
        ImGui.TextWrapped(link?.Snapshot.State == "linked"
            ? T("Партнёр: ", "Partner: ") + (partner?.Name.TextValue ?? T("не рядом", "not nearby"))
            : T("Пригласи партнёра через правый клик по персонажу → Emote Shelf. Принимать приглашения можно, пока включён парный режим.",
                "Right-click a player → Emote Shelf to invite. Invitations are received while pair mode is enabled."));
        if (link?.Snapshot.State is "linked" or "incoming" or "outgoing")
            if (ImGui.Button(T("Разорвать связь / отменить приглашение", "Disconnect / cancel invitation"))) { CancelPair(); link.Act("disconnect"); }
        if (link?.Snapshot.State == "error" && ImGui.Button(T("Переподключиться", "Reconnect"))) StopLink();
        if (link?.Snapshot.Error.Length > 0) ImGui.TextWrapped(link.Snapshot.Error);
        if (ImGui.Checkbox(T("Настроить положение уведомления", "Preview / position notification"), ref linkPreview)) linkPositionApplied = false;
        ImGui.SameLine();
        if (ImGui.Button(T("Сбросить положение", "Reset position"))) { config.PairNoticeX = config.PairNoticeY = -1; linkPositionApplied = false; linkPreview = true; Save(); }
    }
    private void DrawLinkNotice()
    {
        var s = link?.Snapshot;
        var invite = LinkFresh && s?.State == "incoming";
        var outgoing = LinkFresh && s?.State == "outgoing";
        var launch = LinkFresh && s?.Launch is { Status: "waiting" };
        if (!PairRules.ShowPairHud(config.PairEnabled, LinkFresh, s?.State, linkPreview)) { linkPositionApplied = false; return; }
        var interactive = invite || outgoing || launch || pairSession is not null;
        var viewport = ImGui.GetMainViewport();
        if (!linkPositionApplied)
        {
            var position = config.PairNoticeX < 0 ? viewport.WorkPos + new Vector2(Math.Max(0, (viewport.WorkSize.X - 420) / 2), 80)
                : viewport.WorkPos + new Vector2(Math.Clamp(config.PairNoticeX, 0, Math.Max(0, viewport.WorkSize.X - 420)), Math.Clamp(config.PairNoticeY, 0, Math.Max(0, viewport.WorkSize.Y - 220)));
            ImGui.SetNextWindowPos(position, ImGuiCond.Always); linkPositionApplied = true;
        }
        ImGui.SetNextWindowSize(new Vector2(420, 0), ImGuiCond.Always);
        var flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing;
        if (!linkPreview) flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove;
        if (!interactive && !linkPreview) flags |= ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs;
        ImGui.SetNextWindowBgAlpha(.8f);
        if (ImGui.Begin(T("Emote Shelf — положение статуса", "Emote Shelf — status position") + "###EmoteShelfLinkNotice", flags))
        {
            var pos = ImGui.GetWindowPos() - viewport.WorkPos;
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && (Math.Abs(config.PairNoticeX-pos.X) > 1 || Math.Abs(config.PairNoticeY-pos.Y) > 1))
            { config.PairNoticeX = pos.X; config.PairNoticeY = pos.Y; Save(); }
            if (launch) DrawLaunchInvitation(s!.Launch!);
            else if (invite || outgoing)
            {
                var actor = LinkActor(s!.Partner);
                ImGui.TextWrapped((actor?.Name.TextValue ?? T("Игрок не рядом", "Player not nearby")) + $" · {s.Remaining}s");
                ImGui.TextWrapped(invite ? T("Предлагает парную сессию. Принятие не запускает эмоцию и не двигает персонажа.", "Invites you to a pair session. Accepting does not start an emote or move your character.") : T("Приглашение отправлено. Ожидаю ответ…", "Invitation sent. Waiting for an answer…"));
                ImGui.TextWrapped(T("Принимай только ожидаемое приглашение от знакомого: сервер не проверяет личность персонажа.", "Only accept an expected invitation from a trusted partner: the relay cannot verify character identity."));
                if (invite)
                {
                    ImGui.BeginDisabled(actor is null || !LightlessHandles(actor.Address) || handledInvitation == s.Invitation);
                    if (ImGui.Button(T("Принять", "Accept"))) { handledInvitation = s.Invitation; link!.Act("accept", invitation: s.Invitation); }
                    ImGui.EndDisabled(); ImGui.SameLine();
                    if (ImGui.Button(T("Отклонить", "Decline"))) { handledInvitation = s.Invitation; link!.Act("decline", invitation: s.Invitation); }
                }
                else if (ImGui.Button(T("Отменить", "Cancel"))) link!.Act("disconnect");
            }
            else
            {
                var name = LinkedPartner?.Name.TextValue ?? T("партнёр не рядом", "partner not nearby");
                ImGui.TextColored(new Vector4(.65f, 1f, .8f, 1), T("● В паре: ", "● Linked: ") + name);
                if (linkPreview) ImGui.TextWrapped(T("Перетащи за заголовок. Положение сохранится после отпускания мыши.", "Drag by the title. Position is saved when you release the mouse."));
                else if (pairSession is not null || Environment.TickCount64 < pairNoticeUntil)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, .85f, .55f, 1));
                    ImGui.TextWrapped(pairMessage);
                    ImGui.PopStyleColor();
                }
                else if (Environment.TickCount64 < linkNoticeUntil && linkNotice.Length > 0) ImGui.TextWrapped(linkNotice);
                if (pairSession is not null && ImGui.Button(T("Отменить запуск", "Cancel launch"))) CancelPair(T("Запуск отменён.", "Launch cancelled."));
                if (pairSession is not null && pairCollectionWarning.Length > 0) ImGui.TextWrapped(pairCollectionWarning);
            }
            if (linkPreview && ImGui.Button(T("Закрыть предпросмотр", "Close preview"))) linkPreview = false;
        }
        ImGui.End();
    }
}
