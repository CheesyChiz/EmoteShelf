using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace EmoteShelf;

public sealed partial class Plugin
{
    private readonly ErrorReports errorReports = new();
    private void ReportPairError(string code)
    {
        if (!config.UploadErrorReports) return;
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
            : "unknown";
        errorReports.Send(code, detail, link?.Snapshot.State ?? "none", LinkFresh, LightlessLoaded(), pairOfferId,
            local is not null && peer is not null ? Vector3.Distance(local.Position, peer.Position) : null,
            local is not null && peer is not null ? PairRules.AngleDistance(local.Rotation, peer.Rotation) : null);
    }
    private void DrawReportSettings()
    {
        ImGui.TextWrapped(T("Отчёты ошибок: только версия, случайный ID попытки, код сбоя, состояние связи/Lightless и остаточные расстояние/угол. Без имён, чата, токенов, путей и координат. Сервер видит IP при подключении, но не сохраняет его в отчёт. Хранение до 14 дней, доступ администратора по SSH. Не более одного отчёта в минуту; одинаковая категория — раз в 10 минут.",
            "Error reports contain only version, random attempt ID, failure code, relay/Lightless state and remaining distance/angle. No names, chat, tokens, paths or coordinates. The server sees your connection IP but does not store it in reports. Retained up to 14 days, administrator access via SSH. At most one report/minute, one per category/10 minutes."));
        if (ImGui.Checkbox(T("Разрешить автоматическую отправку отчётов ошибок", "Allow automatic error reports"), ref config.UploadErrorReports))
        {
            if (!config.UploadErrorReports) errorReports.Revoke();
            Save();
        }
        if (errorReports.Status.Length > 0) ImGui.TextWrapped(errorReports.Status);
    }
}
