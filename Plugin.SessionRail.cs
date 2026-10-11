using Dalamud.Bindings.ImGui;
namespace EmoteShelf;
public sealed partial class Plugin
{
    private bool selectPartnerTab;
    private void DrawSessionRail()
    {
        ImGui.TextUnformatted(T("Пара", "Partner"));
        ImGui.Separator();
        ImGui.TextWrapped(LinkFresh && LinkedPartner is { } partner ? partner.Name.TextValue : T("Партнёр не подключён", "No partner linked"));
        if (ImGui.Button(T("Управление парой", "Partner controls"))) selectPartnerTab = true;
        ImGui.BeginDisabled(lastAcceptedPair is null || pairSession is not null || !LinkFresh);
        if (ImGui.Button(T("Перезапустить у обоих", "Restart both"))) RestartAcceptedPair();
        ImGui.EndDisabled();
        if (pairMessage.Length > 0) ImGui.TextWrapped(pairMessage);
        if (pairSync is not null)
        {
            ImGui.Separator();
            ImGui.TextWrapped(resourceState == "verified" ? T("Файлы партнёра совпали по контрольным суммам", "Partner files match their checksums") : T("Ожидаем совпадения файлов партнёра", "Waiting for matching partner files"));
            if (!double.IsNaN(observedPhaseDrift)) ImGui.TextWrapped(string.Format(T("Расхождение фазы: примерно {0:F0} мс", "Estimated phase drift: {0:F0} ms"), observedPhaseDrift*1000));
        }
    }
}
