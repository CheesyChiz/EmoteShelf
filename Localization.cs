namespace EmoteShelf;

internal static class Localization
{
    private static readonly Dictionary<string, (string Ja, string De, string Fr)> Text = new(StringComparer.Ordinal)
    {
        ["Emotes"] = ("エモート", "Emotes", "Emotes"),
        ["Panel"] = ("パネル", "Leiste", "Panneau"),
        ["How to use"] = ("使い方", "Anleitung", "Mode d'emploi"),
        ["Settings"] = ("設定", "Einstellungen", "Paramètres"),
        ["Find an emote, try Preview, then add it to the panel. The bookmark is saved automatically."] =
            ("エモートを探し、プレビューで確認してからパネルに追加してください。登録内容は自動保存されます。",
             "Emote suchen, mit Vorschau testen und zur Leiste hinzufügen. Das Lesezeichen wird automatisch gespeichert.",
             "Trouvez une emote, testez-la avec Aperçu, puis ajoutez-la au panneau. Le raccourci est enregistré automatiquement."),
        ["Refresh list"] = ("一覧を更新", "Liste aktualisieren", "Actualiser la liste"),
        ["Search emote or mod"] = ("エモート・MODを検索", "Emote oder Mod suchen", "Rechercher une emote ou un mod"),
        ["Preview"] = ("プレビュー", "Vorschau", "Aperçu"),
        ["Add to panel"] = ("パネルに追加", "Zur Leiste hinzufügen", "Ajouter au panneau"),
        ["Mod variants"] = ("MODのバリエーション", "Mod-Varianten", "Variantes du mod"),
        ["Added to panel. Arrange it on the Panel tab."] =
            ("パネルに追加しました。配置は「パネル」タブで変更できます。",
             "Zur Leiste hinzugefügt. Anordnung im Reiter „Leiste“ ändern.",
             "Ajouté au panneau. Modifiez sa disposition dans l'onglet Panneau."),
        ["Show in-game panel"] = ("ゲーム内パネルを表示", "Leiste im Spiel anzeigen", "Afficher le panneau en jeu"),
        ["Lock position"] = ("位置を固定", "Position sperren", "Verrouiller la position"),
        ["Icons per row"] = ("1行のアイコン数", "Symbole pro Zeile", "Icônes par ligne"),
        ["Icon size"] = ("アイコンのサイズ", "Symbolgröße", "Taille des icônes"),
        ["Background opacity"] = ("背景の不透明度", "Deckkraft des Hintergrunds", "Opacité du fond"),
        ["The panel is a separate small window of emote icons. Unlock it to drag by its title. Hide it here or with /eshelf hide."] =
            ("パネルはエモートのアイコンを並べる小さなウィンドウです。移動するには固定を解除してタイトルをドラッグしてください。ここか /eshelf hide で非表示にできます。",
             "Die Leiste ist ein kleines Fenster mit Emote-Symbolen. Zum Verschieben entsperren und an der Titelleiste ziehen. Hier oder mit /eshelf hide ausblenden.",
             "Le panneau est une petite fenêtre d'icônes d'emotes. Déverrouillez-le et faites glisser sa barre de titre pour le déplacer. Masquez-le ici ou avec /eshelf hide."),
        ["Empty: add an emote on the Emotes tab."] =
            ("まだ空です。「エモート」タブから追加してください。", "Noch leer: Im Reiter „Emotes“ eine Emote hinzufügen.", "Vide : ajoutez une emote dans l'onglet Emotes."),
        ["Update options"] = ("オプションを更新", "Optionen aktualisieren", "Actualiser les options"),
        ["Interface language"] = ("表示言語", "Sprache", "Langue de l'interface"),
        ["Clear temporary selection"] = ("一時的な選択を解除", "Temporäre Auswahl zurücksetzen", "Effacer la sélection temporaire"),
        ["Temporary settings cleared."] = ("一時設定を解除しました。", "Temporäre Einstellungen zurückgesetzt.", "Paramètres temporaires effacés."),
        ["Unidentified animations: only if your emote is missing from Emotes, enter its game command here."] =
            ("未識別のアニメーション：「エモート」タブにない場合だけ、ゲーム内コマンドをここに入力してください。",
             "Nicht erkannte Animationen: Nur wenn die Emote im Reiter „Emotes“ fehlt, hier ihren Spielbefehl eingeben.",
             "Animations non reconnues : saisissez ici la commande du jeu seulement si l'emote manque dans l'onglet Emotes."),
        ["Add emotes: /eshelf → Emotes"] = ("エモートを追加: /eshelf → エモート", "Emotes hinzufügen: /eshelf → Emotes", "Ajouter des emotes : /eshelf → Emotes"),
        ["Ctrl+Shift+click — remove from panel"] = ("Ctrl+Shift+クリック — パネルから削除", "Strg+Umschalt+Klick — aus der Leiste entfernen", "Ctrl+Maj+clic — retirer du panneau"),
        ["Character is not in game."] = ("キャラクターがゲーム内にいません。", "Charakter ist nicht im Spiel.", "Le personnage n'est pas en jeu."),
        ["Collection not found."] = ("コレクションが見つかりません。", "Kollektion nicht gefunden.", "Collection introuvable."),
        ["Searching Penumbra emote replacements…"] = ("Penumbraのエモート置換を検索中…", "Penumbra-Emote-Ersetzungen werden gesucht…", "Recherche des emotes remplacées dans Penumbra…"),
        ["Penumbra is unavailable."] = ("Penumbraを利用できません。", "Penumbra ist nicht verfügbar.", "Penumbra est indisponible."),
        ["Found {0} mod–emote pairs."] = ("MODとエモートの組み合わせを{0}件検出しました。", "{0} Mod-Emote-Paare gefunden.", "{0} associations mod–emote trouvées."),
        ["Scan failed: {0}"] = ("検索に失敗しました: {0}", "Suche fehlgeschlagen: {0}", "Échec de la recherche : {0}"),
        ["The bookmark has no valid emote command."] = ("登録されたエモートコマンドが無効です。", "Das Lesezeichen enthält keinen gültigen Emote-Befehl.", "Le raccourci ne contient pas de commande d'emote valide."),
        ["Mod not found in Penumbra; refresh the list."] = ("PenumbraでMODが見つかりません。一覧を更新してください。", "Mod in Penumbra nicht gefunden; Liste aktualisieren.", "Mod introuvable dans Penumbra ; actualisez la liste."),
        ["Could not determine the character's collection."] = ("キャラクターのコレクションを特定できませんでした。", "Die Kollektion des Charakters konnte nicht ermittelt werden.", "Impossible de déterminer la collection du personnage."),
        ["Could not read settings for {0}: {1}"] = ("{0}の設定を読み取れませんでした: {1}", "Einstellungen für {0} konnten nicht gelesen werden: {1}", "Impossible de lire les paramètres de {0} : {1}"),
        ["Could not read collection priorities: {0}"] = ("コレクションの優先度を読み取れませんでした: {0}", "Prioritäten der Kollektion konnten nicht gelesen werden: {0}", "Impossible de lire les priorités de la collection : {0}"),
        ["Penumbra's maximum priority is already in use."] = ("Penumbraの最大優先度は既に使用されています。", "Die maximale Penumbra-Priorität ist bereits belegt.", "La priorité maximale de Penumbra est déjà utilisée."),
        ["Could not switch {0}: {1}"] = ("{0}に切り替えられませんでした: {1}", "Wechsel zu {0} fehlgeschlagen: {1}", "Impossible d'activer {0} : {1}"),
        ["Selected: {0} → {1}"] = ("選択中: {0} → {1}", "Ausgewählt: {0} → {1}", "Sélectionné : {0} → {1}"),
        ["Mod switched, but the emote did not start: {0}"] = ("MODは切り替わりましたが、エモートを開始できませんでした: {0}", "Mod gewechselt, aber die Emote wurde nicht gestartet: {0}", "Mod activé, mais l'emote n'a pas démarré : {0}"),
        ["Mod settings are unavailable: {0}"] = ("MODの設定を利用できません: {0}", "Mod-Einstellungen sind nicht verfügbar: {0}", "Paramètres du mod indisponibles : {0}"),
        ["Penumbra options saved for “{0}”."] = ("「{0}」のPenumbraオプションを保存しました。", "Penumbra-Optionen für „{0}“ gespeichert.", "Options Penumbra enregistrées pour « {0} »."),
        ["Invalid emote command."] = ("エモートコマンドが無効です。", "Ungültiger Emote-Befehl.", "Commande d'emote non valide."),
        ["Game chat is not ready."] = ("ゲーム内チャットの準備ができていません。", "Der Spielchat ist noch nicht bereit.", "La fenêtre de discussion du jeu n'est pas prête."),
        ["Language changed."] = ("言語を変更しました。", "Sprache geändert.", "Langue modifiée."),
        ["Showing {0} mod–emote pairs."] = ("一覧にMODとエモートの組み合わせを{0}件表示しています。", "{0} Mod-Emote-Paare werden angezeigt.", "{0} associations mod–emote affichées."),
        ["Hide mod"] = ("MODを非表示", "Mod ausblenden", "Masquer le mod"),
        ["Hidden mods"] = ("非表示のMOD", "Ausgeblendete Mods", "Mods masqués"),
        ["Restore"] = ("戻す", "Wiederherstellen", "Restaurer"),
        ["Hiding removes a mod from the browser but keeps existing panel bookmarks."] =
            ("非表示にしても、パネルに登録済みのアイコンは残ります。", "Ausblenden entfernt den Mod aus der Liste, vorhandene Lesezeichen bleiben aber erhalten.", "Masquer un mod le retire de la liste, mais conserve les raccourcis déjà présents sur le panneau."),
        ["Hide unwanted mods directly in the browser. Restore them under Settings → Hidden mods. Existing panel bookmarks remain intact."] =
            ("不要なMODは一覧から非表示にできます。「設定」→「非表示のMOD」から戻せます。登録済みのアイコンは残ります。",
             "Unerwünschte Mods direkt in der Liste ausblenden. Unter Einstellungen → Ausgeblendete Mods wiederherstellen. Vorhandene Lesezeichen bleiben erhalten.",
             "Masquez les mods indésirables dans la liste. Restaurez-les via Paramètres → Mods masqués. Les raccourcis déjà ajoutés restent sur le panneau."),
    };

    internal static string Get(string language, string ru, string en)
    {
        if (language == "ru") return ru;
        if (language == "en") return en;
        if (en.StartsWith("1. On Emotes", StringComparison.Ordinal)) return Help(language, en);
        if (!Text.TryGetValue(en, out var translated)) return en;
        return language switch { "ja" => translated.Ja, "de" => translated.De, "fr" => translated.Fr, _ => en };
    }

    private static string Help(string language, string fallback) => language switch
    {
        "ja" => "1. 「エモート」タブで使いたいエモートを探します。プレビューは登録せずに再生します。「パネルに追加」を押すと、現在のMOD設定とともに登録・自動保存されます。バリエーションが検出された場合は「MODのバリエーション」を開いて選べます。\n\n2. ゲーム画面のアイコンをクリックすると再生、Ctrl+Shift+クリックで削除します。プレビューや再生ではPenumbraのMODを一時的に切り替えます。「設定」タブから一時設定を解除できます。\n\n3. 「パネル」タブで配置、アイコンの大きさ、背景の不透明度を調整できます。MODの設定を後から変えた場合は「オプションを更新」で保存済み設定を置き換えてください。複数のオプショングループの組み合わせは自動列挙されないため、Penumbraで組み合わせを設定してから個別に追加してください。\n\nエモートが見つからない場合だけ、「設定」タブでゲーム内コマンドを手入力してください。",
        "de" => "1. Im Reiter „Emotes“ die gewünschte Emote suchen. „Vorschau“ spielt sie ab, ohne sie hinzuzufügen. „Zur Leiste hinzufügen“ speichert ein dauerhaftes Lesezeichen mit den aktuellen Mod-Optionen. Erkannte Varianten stehen unter „Mod-Varianten“.\n\n2. Ein Klick auf ein Symbol spielt die Emote ab; Strg+Umschalt+Klick entfernt es. Vorschau und Wiedergabe wählen den Mod in Penumbra vorübergehend aus. Diese Auswahl kann unter „Einstellungen“ zurückgesetzt werden.\n\n3. Im Reiter „Leiste“ Anordnung, Symbolgröße und Hintergrund-Deckkraft einstellen. Optionen werden beim Hinzufügen automatisch gespeichert. Nach einer späteren Änderung des Mods ersetzt „Optionen aktualisieren“ den gespeicherten Stand. Kombinationen mehrerer Optionsgruppen werden nicht automatisch aufgelistet: In Penumbra einstellen und jeweils ein eigenes Lesezeichen hinzufügen.\n\nNur wenn eine Emote nicht erkannt wird, ihren Spielbefehl unter „Einstellungen“ von Hand eingeben.",
        "fr" => "1. Dans l'onglet Emotes, trouvez l'emote voulue. « Aperçu » la lance sans l'ajouter. « Ajouter au panneau » crée un raccourci permanent avec les options actuelles du mod. Les variantes détectées se trouvent sous « Variantes du mod ».\n\n2. Cliquez sur une icône pour jouer l'emote ; Ctrl+Maj+clic la retire. L'aperçu et la lecture sélectionnent temporairement le mod dans Penumbra. Vous pouvez annuler cette sélection dans Paramètres.\n\n3. Dans Panneau, réglez la disposition, la taille des icônes et l'opacité du fond. Les options sont enregistrées automatiquement à l'ajout. Si vous modifiez ensuite le mod, « Actualiser les options » remplace les options mémorisées. Les combinaisons de plusieurs groupes ne sont pas listées automatiquement : configurez-les dans Penumbra et ajoutez un raccourci pour chacune.\n\nSaisissez une commande du jeu dans Paramètres uniquement si l'emote n'a pas été reconnue.",
        _ => fallback,
    };
}
