using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EmoteShelf;

public sealed partial class Plugin
{
    private readonly ErrorReports errorReports = new();
    private const int DiagnosticsPolicyVersion = 1;
    private bool diagnosticsNoticeOpen = true;
    private bool DiagnosticsAcknowledged => config.DiagnosticsNoticeVersion == DiagnosticsPolicyVersion;
    private void DrawDiagnosticsNotice()
    {
        if (!diagnosticsNoticeOpen) return;
        ImGui.SetNextWindowSize(new Vector2(620, 0), ImGuiCond.FirstUseEver);
        if (ImGui.Begin(T("Emote Shelf — обязательная диагностика", "Emote Shelf — required diagnostics"), ref diagnosticsNoticeOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 570);
            ImGui.TextWrapped(T("При использовании Emote Shelf технические отчёты автоматически отправляются разработчикам Emote Shelf. Отключить отправку, продолжая пользоваться плагином, нельзя.",
                "While you use Emote Shelf, technical reports are automatically sent to the Emote Shelf developers. Reporting cannot be disabled while continuing to use the plugin."));
            DrawReportDisclosure();
            ImGui.TextWrapped(T("До принятия плагин не запускает эмоции, не подключает пару и ничего не отправляет. Закрой это окно, чтобы не использовать плагин; отключить или удалить его можно в установщике Dalamud. После принятия прекратить сбор можно отключением плагина.",
                "Until accepted, the plugin does not play emotes, connect pairs or upload data. Close this window to leave it inactive; disable or uninstall it in Dalamud. After acceptance, disable the plugin to stop collection."));
            if (ImGui.Button(T("Принять условия и использовать", "Accept terms and use plugin")))
            { config.DiagnosticsNoticeVersion = DiagnosticsPolicyVersion; Save(); }
            ImGui.SameLine();
            if (ImGui.Button(T("Не использовать", "Do not use"))) diagnosticsNoticeOpen = false;
            ImGui.PopTextWrapPos();
        }
        ImGui.End();
    }
    private void DrawReportDisclosure()
    {
        ImGui.TextWrapped(T("Передаются: версия плагина, случайные ID отчёта/попытки, технический код и этап сбоя, состояние связи и Lightless, флаги подготовки, расстояние и угол между участниками. Полные логи, имена, чат, токены, пути и абсолютные координаты не отправляются. Сервер видит IP соединения, но не записывает его в отчёт. Доступ к отчётам имеет администратор Emote Shelf по SSH. Хранение до 14 дней с очисткой раз в час; база ограничена 10 000 отчётов / 32 МиБ. При недоступности сети — ограниченные повторы в памяти; выход из игры удаляет очередь.",
            "Reports contain plugin version, random report/attempt IDs, technical failure code and checkpoint, relay/Lightless state, preparation flags, relative distance and angle. No full logs, names, chat, tokens, paths or absolute coordinates. The server sees your connection IP but does not store it in reports. The Emote Shelf administrator accesses reports via SSH. Retention is 14 days with hourly cleanup; storage is capped at 10,000 reports / 32 MiB. Network failures use bounded in-memory retries; exiting the game discards the queue."));
    }
    private void ReportPairError(string code)
    {
        if (!DiagnosticsAcknowledged) return;
        var local = Objects.LocalPlayer;
        var peer = LinkedPartner;
        var alignment = pairAlignment?.Error ?? "";
        var detail = code == "alignment_failed"
            ? alignment.Contains("stabilize") ? "unstable" : alignment.Contains("input") ? "input" : alignment.Contains("timed out") ? "timeout" : alignment.Contains("height") ? "height" : "state"
            : !LightlessLoaded() ? "lightless_missing"
            : link is null ? "not_connected"
            : link.Snapshot.State == "error" ? "connection_error"
            : !LinkFresh ? "stale"
            : link.Snapshot.State is "incoming" or "outgoing" ? "pending"
            : code == "invite_blocked" && link.Snapshot.State == "linked" ? "already_linked"
            : code == "invite_blocked" ? lightlessIpcError.Length > 0 ? "lightless_api" : "lightless_unhandled"
            : code == "launch_cancelled" ? pairCheckpoint : "unknown";
        PairDebug($"report code={code} detail={detail} upload=requested", true);
        errorReports.Send(code, detail, link?.Snapshot.State ?? "none", LinkFresh, LightlessLoaded(), pairOfferId,
            local is not null && peer is not null ? Vector3.Distance(local.Position, peer.Position) : null,
            local is not null && peer is not null ? PairRules.AngleDistance(local.Rotation, peer.Rotation) : null,
            new ReportContext(pairSession?.Snapshot.State ?? "none", pairSession?.Snapshot.Anchor ?? false,
                pairAlignStarted, pairStartedPreparation, pairPreparing, pairReadySent, Math.Clamp(pairStableFrames, 0, 1000)));
    }
    private void DrawReportSettings()
    {
        ImGui.TextWrapped(T("Обязательная техническая диагностика включена.", "Required technical diagnostics are enabled."));
        DrawReportDisclosure();
        if (errorReports.Status.Length > 0) ImGui.TextWrapped(Localization.Runtime(config.Language, errorReports.Status));
    }
}
