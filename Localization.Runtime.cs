namespace EmoteShelf;

internal static partial class Localization
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> EnglishMessages = new(StringComparer.Ordinal);

    // UI translations never become the canonical diagnostic vocabulary.
    internal static string English(string value)
    {
        if (EnglishMessages.TryGetValue(value, out var english)) return english;
        foreach (var (key, translations) in Text)
            if (value == translations.Ja || value == translations.De || value == translations.Fr) return key;
        foreach (var row in RuntimeRows.Split('\n'))
        {
            var cells = row.TrimEnd('\r').Split('|');
            if (cells.Skip(1).Contains(value)) return cells[0];
        }
        return value;
    }
    private const string RuntimeRows = """
idle|готов к подключению|接続待機中|Bereit|Prêt
linked|партнёр подключён|パートナーに接続済み|Verbunden|Partenaire connecté
disconnected|нет подключения|未接続|Nicht verbunden|Déconnecté
connecting|подключение…|接続中…|Verbindung wird hergestellt…|Connexion…
waiting|ожидание партнёра|パートナーを待機中|Warte auf Partner|En attente du partenaire
error|ошибка подключения|接続エラー|Verbindungsfehler|Erreur de connexion
Lightless Sync: OK|Lightless Sync подключён|Lightless Sync 接続済み|Lightless Sync verbunden|Lightless Sync connecté
Change pose|Сменить позу|ポーズを変える|Pose wechseln|Changer de pose
FAQ|Вопросы и ответы|よくある質問|Häufige Fragen|Questions fréquentes
Invalid relay response.|Сервер прислал некорректный ответ.|サーバーの応答が不正です。|Ungültige Serverantwort.|Réponse du serveur invalide.
Pair readiness expired.|Время ожидания готовности истекло.|準備の待機時間が切れました。|Wartezeit für die Bereitschaft abgelaufen.|Délai de préparation dépassé.
The pair was cancelled or expired.|Совместный запуск отменён или время ожидания истекло.|ペア再生がキャンセルされたか、待機時間が切れました。|Gemeinsamer Start abgebrochen oder abgelaufen.|Lancement à deux annulé ou délai dépassé.
Unexpected start before local preparation.|Сигнал запуска пришёл до завершения подготовки.|準備完了前に開始信号を受信しました。|Startsignal vor Abschluss der Vorbereitung.|Signal de départ reçu avant la fin de la préparation.
Start signal arrived too late; please retry.|Сигнал запуска пришёл слишком поздно. Попробуйте ещё раз.|開始信号が遅れました。もう一度お試しください。|Startsignal kam zu spät. Bitte erneut versuchen.|Signal de départ reçu trop tard. Réessayez.
Move within 2 yalms on the same level first.|Подойдите на расстояние до 2 ялмов и встаньте на одном уровне.|同じ高さで2ヤルム以内に近づいてください。|Auf gleicher Höhe bis auf 2 Yalme nähern.|Approchez à moins de 2 yalms, à la même hauteur.
Final correction exceeds 0.10 yalms or has invalid coordinates.|Смещение больше 0,1 ялма или координаты недоступны.|ずれが0.1ヤルムを超えるか、座標が不正です。|Abweichung über 0,1 Yalm oder ungültige Koordinaten.|Écart supérieur à 0,1 yalm ou coordonnées invalides.
Alignment confirmation timed out.|Не удалось подтвердить выравнивание вовремя.|位置合わせの確認がタイムアウトしました。|Zeitlimit für die Ausrichtungsprüfung erreicht.|Délai de vérification de l’alignement dépassé.
Fine alignment cancelled: character moved or changed state.|Доводка отменена: персонаж сдвинулся или изменил состояние.|キャラクターの移動や状態変化により微調整を中止しました。|Feinausrichtung abgebrochen: Bewegung oder Zustandswechsel.|Ajustement annulé : déplacement ou changement d’état du personnage.
Position/facing did not stabilize; pair launch cancelled.|Положение или поворот не закрепились. Запуск отменён.|位置や向きが安定しないため、再生を中止しました。|Position oder Blickrichtung nicht stabil. Start abgebrochen.|Position ou orientation instable. Lancement annulé.
Alignment cancelled by movement input.|Выравнивание отменено ручным движением.|移動操作により位置合わせを中止しました。|Ausrichtung durch Bewegungseingabe abgebrochen.|Alignement annulé par une commande de déplacement.
Alignment timed out; move closer and retry.|Не успели выровняться. Подойдите ближе и повторите.|位置合わせがタイムアウトしました。近づいて再試行してください。|Ausrichtung dauerte zu lange. Näher herangehen und erneut versuchen.|Alignement trop long. Rapprochez-vous et réessayez.
Character unavailable.|Персонаж сейчас недоступен.|キャラクターが見つかりません。|Charakter derzeit nicht verfügbar.|Personnage indisponible.
Stand normally before aligning.|Перед выравниванием встаньте и завершите другие действия.|立ち上がり、他の動作を終えてから位置合わせしてください。|Vor dem Ausrichten aufstehen und andere Aktionen beenden.|Levez-vous et terminez les autres actions avant l’alignement.
Alignment stopped: different ground height.|Выравнивание остановлено: вы стоите на разной высоте.|地面の高さが異なるため位置合わせを中止しました。|Ausrichtung gestoppt: unterschiedliche Bodenhöhe.|Alignement arrêté : hauteur du sol différente.
Too much height difference for fine alignment.|Перепад высоты слишком большой для доводки.|微調整できる範囲を超える高低差があります。|Höhenunterschied für Feinausrichtung zu groß.|Différence de hauteur trop importante pour l’ajustement.
Alignment blocked; no position correction was applied.|Не удаётся подойти к нужной точке. Проверьте, нет ли препятствия.|目的地に進めません。障害物を確認してください。|Zielposition nicht erreichbar. Auf Hindernisse prüfen.|Impossible d’atteindre la position. Vérifiez les obstacles.
Camera unavailable.|Не удалось получить данные камеры.|カメラ情報を取得できません。|Kameradaten nicht verfügbar.|Données de caméra indisponibles.
Partner moved during final correction.|Партнёр сдвинулся во время доводки.|微調整中にパートナーが移動しました。|Partner bewegte sich während der Feinausrichtung.|Le partenaire a bougé pendant l’ajustement.
No locally prepared action.|Анимация ещё не подготовлена к запуску.|アニメーションの準備ができていません。|Animation noch nicht vorbereitet.|L’animation n’est pas encore prête.
Prepared Penumbra options changed; pair cancelled.|Настройки Penumbra изменились после подготовки. Запуск отменён.|準備後にPenumbraの設定が変わったため中止しました。|Penumbra-Einstellungen nach Vorbereitung geändert. Start abgebrochen.|Options Penumbra modifiées après la préparation. Lancement annulé.
Prepared animation was replaced; pair cancelled.|Подготовленная анимация была заменена. Запуск отменён.|準備したアニメーションが置き換わったため中止しました。|Vorbereitete Animation ersetzt. Start abgebrochen.|Animation préparée remplacée. Lancement annulé.
Pair facing unavailable.|Не удалось определить направление персонажей.|キャラクターの向きを確認できません。|Blickrichtung nicht verfügbar.|Orientation des personnages indisponible.
No local role selected|Сначала выберите свою роль.|自分の役割を選んでください。|Zuerst eigene Rolle wählen.|Choisissez d’abord votre rôle.
Proposal expired|Время действия предложения истекло.|提案の有効期限が切れました。|Vorschlag abgelaufen.|La proposition a expiré.
Unreliable relay timing; please retry.|Задержка связи мешает синхронному запуску. Попробуйте ещё раз.|通信遅延により同期できません。再試行してください。|Verzögerung verhindert synchronen Start. Bitte erneut versuchen.|Le délai réseau empêche la synchronisation. Réessayez.
Partner unavailable, busy, or invitation expired. Please retry later.|Партнёр недоступен, занят или приглашение истекло. Попробуйте ещё раз.|パートナーが不在、対応中、または招待が期限切れです。再試行してください。|Partner nicht verfügbar, beschäftigt oder Einladung abgelaufen. Erneut versuchen.|Partenaire indisponible, occupé ou invitation expirée. Réessayez.
Report queued: |Отчёт в очереди: |送信待ちのレポート: |Bericht in Warteschlange: |Rapport en attente : 
Report received: |Сервер получил отчёт: |サーバー受信済み: |Bericht empfangen: |Rapport reçu : 
Report not delivered; ID |Отчёт не доставлен; ID |レポート未送信; ID |Bericht nicht zugestellt; ID |Rapport non transmis ; ID 
Local diagnostics retained.|Локальная диагностика сохранена.|ローカル診断は保存されています。|Lokale Diagnose bleibt erhalten.|Diagnostic local conservé.
""";
    internal static string Runtime(string language, string value)
    {
        if (language == "en" || string.IsNullOrEmpty(value)) return value;
        var index = language switch { "ru" => 1, "ja" => 2, "de" => 3, "fr" => 4, _ => 0 };
        foreach (var row in RuntimeRows.Split('\n'))
        {
            var cells = row.TrimEnd('\r').Split('|');
            if (value == cells[0]) return cells[index];
            if (cells[0].Length > 20 && value.Contains(cells[0], StringComparison.Ordinal))
                value = value.Replace(cells[0], cells[index], StringComparison.Ordinal);
        }
        return value;
    }
}
