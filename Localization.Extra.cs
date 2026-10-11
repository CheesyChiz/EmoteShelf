namespace EmoteShelf;

internal static partial class Localization
{
    static Localization()
    {
        foreach (var row in Additional.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cells = row.TrimEnd('\r').Split('|');
            if (cells.Length != 4) throw new InvalidOperationException("Invalid translation row");
            Text[cells[0].Replace("\\n", "\n")] = (cells[1].Replace("\\n", "\n"), cells[2].Replace("\\n", "\n"), cells[3].Replace("\\n", "\n"));
        }
    }
    private const string Additional = """
 — stale response| — 応答が古くなっています| — Antwort veraltet| — réponse périmée
##role|##role|##role|##role
● Linked: |● 接続中: |● Verbunden: |● Connecté : 
Accept|承諾|Annehmen|Accepter
Accept and start|承諾して再生|Annehmen und starten|Accepter et lancer
Accept terms and use plugin|条件に同意して使用|Bedingungen annehmen und starten|Accepter les conditions et continuer
Added to panel.|パネルに追加しました。|Zur Leiste hinzugefügt.|Ajouté au panneau.
already linked to a partner|すでにパートナーと接続しています|Bereits mit jemandem verbunden|Déjà connecté à un partenaire
another local version|別のローカル版|Andere lokale Version|Autre version locale
answer the incoming invitation first|先に届いた招待に返答してください|Zuerst die eingehende Einladung beantworten|Répondez d’abord à l’invitation reçue
Automatic pose selection (experimental, not fully tested)|ポーズを自動選択（試験機能）|Pose automatisch wählen (experimentell)|Choix automatique de la pose (expérimental)
Automatic poses disabled; pair launch cancelled.|ポーズの自動選択がオフのため、ペア再生を中止しました。|Automatische Posen sind aus. Paarstart abgebrochen.|Le choix automatique des poses est désactivé. Lancement à deux annulé.
Cancel|キャンセル|Abbrechen|Annuler
Cancel launch|再生を中止|Start abbrechen|Annuler le lancement
Cancel proposal|提案を取り消す|Vorschlag zurückziehen|Annuler la proposition
Changing pose {0} → {1} (step {2}).|ポーズを変更中: {0} → {1}（{2}回目）|Pose wechseln: {0} → {1} (Schritt {2}).|Changement de pose : {0} → {1} (étape {2}).
Close preview|プレビューを閉じる|Vorschau schließen|Fermer l’aperçu
connecting to relay…|サーバーに接続中…|Verbindung zum Server wird hergestellt…|Connexion au serveur…
connection error — reconnect in Settings|接続エラー — 設定から再接続してください|Verbindungsfehler – unter Einstellungen neu verbinden|Erreur de connexion — reconnectez-vous dans les paramètres
Copy diagnostics|診断情報をコピー|Diagnose kopieren|Copier le diagnostic
Could not stand up from /groundsit.|地面に座った状態から立ち上がれませんでした。|Aufstehen aus /groundsit fehlgeschlagen.|Impossible de se relever après /groundsit.
Create at root|最上位に作成|Im Hauptverzeichnis erstellen|Créer à la racine
Current slot|現在のスロット|Aktueller Platz|Emplacement actuel
Decline|辞退|Ablehnen|Refuser
Detailed pair diagnostics|詳しいペア診断|Ausführliche Paardiagnose|Diagnostic détaillé du duo
Diagnostics|診断|Diagnose|Diagnostic
Disconnect / cancel invitation|切断 / 招待を取り消す|Trennen / Einladung zurückziehen|Déconnecter / annuler l’invitation
Dismiss|閉じる|Ausblenden|Masquer
Do not use|使用しない|Nicht verwenden|Ne pas utiliser
Emote is unavailable.|このエモートは使用できません。|Diese Emote ist nicht verfügbar.|Cette emote n’est pas disponible.
Emote Shelf — required diagnostics|Emote Shelf — 必須の診断データ送信|Emote Shelf — erforderliche Diagnose|Emote Shelf — diagnostic obligatoire
Emote Shelf — status position|Emote Shelf — 通知の位置|Emote Shelf — Position der Statusanzeige|Emote Shelf — position des notifications
Enable experimental pair launch in Settings.|設定でペア再生（試験機能）を有効にしてください。|Aktiviere den experimentellen Paarstart in den Einstellungen.|Activez le lancement à deux dans les paramètres.
Enter a known game emote command.|ゲーム内エモートのコマンドを入力してください。|Gib einen gültigen Emote-Befehl ein.|Saisissez une commande d’emote du jeu.
Folder: {0}|フォルダー: {0}|Ordner: {0}|Dossier : {0}
Game reports slot {0:00}.|ゲームが返したスロット: {0:00}|Vom Spiel gemeldeter Platz: {0:00}.|Emplacement indiqué par le jeu : {0:00}.
Grow new columns leftward|新しい列を左に追加|Neue Spalten nach links|Ajouter les colonnes à gauche
Grow new rows upward|新しい行を上に追加|Neue Zeilen nach oben|Ajouter les lignes vers le haut
How do I add and play an emote?|エモートの追加・再生方法は？|Wie füge ich Emotes hinzu und spiele sie ab?|Comment ajouter et jouer une emote ?
How do I play with a partner?|パートナーと再生するには？|Wie spiele ich mit einem Partner?|Comment jouer avec un partenaire ?
How do I position the panel and notifications?|パネルと通知の位置を変えるには？|Wie verschiebe ich Leiste und Benachrichtigungen?|Comment déplacer le panneau et les notifications ?
How do poses work?|ポーズの選び方は？|Wie funktionieren Posen?|Comment fonctionnent les poses ?
invitation already pending|招待の返答待ちです|Eine Einladung ist bereits offen|Une invitation est déjà en attente
Invitation declined, cancelled or expired.|招待が辞退・取り消し・期限切れになりました。|Die Einladung wurde abgelehnt, zurückgezogen oder ist abgelaufen.|L’invitation a été refusée, annulée ou a expiré.
Invitation sent. Waiting for an answer…|招待を送りました。返答を待っています…|Einladung gesendet. Warte auf Antwort…|Invitation envoyée. En attente d’une réponse…
Last error (retained for diagnosis): |最後のエラー（診断用）: |Letzter Fehler (zur Diagnose gespeichert): |Dernière erreur conservée : 
Last invitation menu check: |招待の最終確認: |Letzte Prüfung des Einladungsmenüs: |Dernière vérification des invitations : 
Last launch did not complete|前回の再生は開始できませんでした|Der letzte Start ist fehlgeschlagen|Le dernier lancement n’a pas abouti
Launch cancelled by user.|ユーザーが再生を中止しました。|Start vom Benutzer abgebrochen.|Lancement annulé par l’utilisateur.
LIBRARY|ライブラリ|BIBLIOTHEK|BIBLIOTHÈQUE
Lightless API error: |Lightless API エラー: |Lightless-API-Fehler: |Erreur de l’API Lightless : 
Lightless does not handle this character|Lightless はこのキャラクターを同期していません|Lightless synchronisiert diesen Charakter nicht|Lightless ne synchronise pas ce personnage
Lightless is not loaded.|Lightless が読み込まれていません。|Lightless ist nicht geladen.|Lightless n’est pas chargé.
Lightless not loaded|Lightless が未読み込みです|Lightless nicht geladen|Lightless non chargé
Lightless Sync required|Lightless Sync が必要です|Lightless Sync erforderlich|Lightless Sync requis
Mod actions|MODの操作|Mod-Aktionen|Actions du mod
Mod preparation failed.|MODの準備に失敗しました。|Der Mod konnte nicht vorbereitet werden.|La préparation du mod a échoué.
Mod slot|MODのスロット|Mod-Platz|Emplacement du mod
mods|MOD|Mods|mods
Move closer: within 2 yalms and on the same level.|同じ高さで、2ヤルム以内に近づいてください。|Kommt auf gleicher Höhe bis auf 2 Yalme zusammen.|Rapprochez-vous à moins de 2 yalms, au même niveau.
not nearby|近くにいません|Nicht in der Nähe|Pas à proximité
Pair|ペアで再生|Zu zweit|À deux
Pair launch — experimental|ペア再生（試験機能）|Paarstart (experimentell)|Lancement à deux (expérimental)
Pair launch cancelled or timed out.|ペア再生が中止、またはタイムアウトしました。|Paarstart abgebrochen oder Zeitlimit erreicht.|Lancement à deux annulé ou délai dépassé.
Pair launch cancelled.|ペア再生を中止しました。|Paarstart abgebrochen.|Lancement à deux annulé.
Panel opacity|パネルの不透明度|Deckkraft der Leiste|Opacité du panneau
Partner|パートナー|Partner|Partenaire
Partner connection ended.|パートナーとの接続が終了しました。|Die Verbindung zum Partner wurde beendet.|La connexion au partenaire a été fermée.
Partner connection lost.|パートナーとの接続が切れました。|Verbindung zum Partner verloren.|Connexion au partenaire perdue.
partner not nearby|パートナーが近くにいません|Partner nicht in der Nähe|Partenaire absent
Partner selected: |選択したパートナー: |Gewählter Partner: |Partenaire choisi : 
Partner: |パートナー: |Partner: |Partenaire : 
Player not nearby|プレイヤーが近くにいません|Spieler nicht in der Nähe|Joueur absent
Pose {0} is unavailable for this character (maximum: {1}).|このキャラクターはポーズ{0}を使えません（最大: {1}）。|Pose {0} ist für diesen Charakter nicht verfügbar (Maximum: {1}).|La pose {0} n’est pas disponible pour ce personnage (maximum : {1}).
Prepared mod validation failed.|準備したMODの確認に失敗しました。|Die Prüfung des vorbereiteten Mods ist fehlgeschlagen.|La vérification du mod préparé a échoué.
Preparing your selected role…|選択した役割を準備中…|Deine gewählte Rolle wird vorbereitet…|Préparation du rôle choisi…
Preview / position notification|通知を表示して位置を調整|Benachrichtigung anzeigen / verschieben|Afficher / déplacer la notification
Reconnect|再接続|Neu verbinden|Reconnecter
Refining position and facing after preparation…|準備後の位置と向きを微調整中…|Position und Blickrichtung werden nachjustiert…|Ajustement final de la position et de l’orientation…
Refresh|更新|Aktualisieren|Actualiser
Refresh mods|MODを更新|Mods aktualisieren|Actualiser les mods
Refresh the mod list.|MOD一覧を更新してください。|Aktualisiere die Mod-Liste.|Actualisez la liste des mods.
relay not connected|サーバーに未接続です|Keine Serververbindung|Serveur déconnecté
relay response is stale|サーバーの応答が途絶えています|Serverantwort veraltet|La réponse du serveur est trop ancienne
Relay: |サーバー: |Server: |Serveur : 
reported slot|ゲーム内スロット|Gemeldeter Platz|Emplacement indiqué
Required diagnostics|必須の診断データ送信|Erforderliche Diagnose|Diagnostic obligatoire
Required technical diagnostics are enabled.|技術的な診断データの送信は必須です。|Die erforderliche technische Diagnose ist aktiv.|L’envoi du diagnostic technique est obligatoire.
Reset position|位置をリセット|Position zurücksetzen|Réinitialiser la position
Right-click — open mod; Ctrl+Shift+click — remove|右クリック: MODを開く / Ctrl+Shift+クリック: 削除|Rechtsklick: Mod öffnen; Strg+Umschalt+Klick: entfernen|Clic droit : ouvrir le mod ; Ctrl+Maj+clic : retirer
Select pose manually (if auto fails)|手動でポーズを選択（自動選択できない場合）|Pose manuell wählen (falls Automatik scheitert)|Choisir la pose manuellement (si l’auto échoue)
session not ready|セッションの準備ができていません|Sitzung noch nicht bereit|La session n’est pas prête
Shared start in {0:0.0}s|{0:0.0}秒後に同時再生|Gemeinsamer Start in {0:0.0} s|Départ commun dans {0:0.0} s
Shift+click — pair launch|Shift+クリック: ペアで再生|Umschalt+Klick: Paarstart|Maj+clic : lancement à deux
Show all variants|すべてのバリエーションを表示|Alle Varianten anzeigen|Afficher toutes les variantes
Slot: |スロット: |Platz: |Emplacement : 
Standing up before switching mods…|MODを切り替える前に立ち上がっています…|Vor dem Mod-Wechsel aufstehen…|Le personnage se relève avant de changer de mod…
target|対象|Ziel|cible
The game rejected the pair emote.|ゲームがペアエモートの再生を拒否しました。|Das Spiel hat die Paar-Emote abgelehnt.|Le jeu a refusé l’emote du duo.
The selected variant has no active animation files.|選択したバリエーションに有効なアニメーションがありません。|Die gewählte Variante hat keine aktiven Animationsdateien.|La variante choisie n’a aucun fichier d’animation actif.
This mod is not in your local list.|このMODはローカル一覧にありません。|Dieser Mod fehlt in deiner lokalen Liste.|Ce mod ne figure pas dans votre liste locale.
This mod replaces several pose slots; choose the intended file.|このMODは複数のポーズを置き換えます。使いたいファイルを選んでください。|Dieser Mod ersetzt mehrere Posen. Wähle die gewünschte Datei.|Ce mod remplace plusieurs poses. Choisissez le fichier souhaité.
This mod replaces slot {0}{1:00}.|このMODの置き換え先: {0}{1:00}|Dieser Mod ersetzt Platz {0}{1:00}.|Ce mod remplace l’emplacement {0}{1:00}.
Wait for Lightless synchronization with your partner.|Lightless の同期が完了するまでお待ちください。|Warte, bis Lightless mit deinem Partner synchronisiert ist.|Attendez la fin de la synchronisation Lightless avec votre partenaire.
Waiting for your partner to choose a role and accept.|パートナーの役割選択と承諾を待っています。|Warte auf Rollenwahl und Zustimmung deines Partners.|En attente du choix du rôle et de l’accord du partenaire.
What should I do if something fails?|うまく動かないときは？|Was tun, wenn etwas nicht klappt?|Que faire en cas de problème ?
Your partner moved; retry alignment.|パートナーが動きました。もう一度位置を合わせてください。|Dein Partner hat sich bewegt. Erneut ausrichten.|Votre partenaire a bougé. Relancez l’alignement.
\nDistance: {0:F4} yalms (limit ≤0.1); angle: {1:F2}° (limit ≤0.57°).|\n距離: {0:F4}ヤルム（上限0.1）、角度差: {1:F2}°（上限0.57°）。|\nAbstand: {0:F4} Yalme (höchstens 0,1); Winkel: {1:F2}° (höchstens 0,57°).|\nDistance : {0:F4} yalms (limite : 0,1) ; angle : {1:F2}° (limite : 0,57°).
A player moved or turned. Stop moving and retry.|どちらかが移動・回転しました。止まってからやり直してください。|Jemand hat sich bewegt oder gedreht. Bleibt stehen und versucht es erneut.|Un joueur a bougé ou tourné. Arrêtez-vous et réessayez.
A variant file points outside its mod directory.|バリエーションがMODフォルダー外のファイルを参照しています。|Eine Variantendatei verweist auf eine Datei außerhalb des Mod-Ordners.|Une variante pointe vers un fichier hors du dossier du mod.
Accepting authorizes required alignment: approach, fine position/facing correction, then playback.|承諾すると、相手への接近と位置・向きの調整後に再生します。|Mit deiner Zustimmung wird dein Charakter angenähert und ausgerichtet, dann startet die Animation.|En acceptant, vous autorisez le rapprochement et l’ajustement de la position et de l’orientation avant la lecture.
Alignment confirmed. Waiting for your partner…|位置と向きを確認しました。パートナーの準備を待っています…|Ausrichtung bestätigt. Warte auf deinen Partner…|Alignement confirmé. En attente du partenaire…
Alignment is required: short approach, fine position/facing correction, and confirmation before launch.|再生前に接近し、位置と向きを合わせて確認します。|Vor dem Start werden Abstand und Blickrichtung angeglichen und geprüft.|Avant le lancement, les personnages se rapprochent, s’alignent, puis leur position est vérifiée.
An animation proposal is already pending. Accept or decline it in the notification.|再生の提案が届いています。通知で承諾または辞退してください。|Ein Animationsvorschlag ist bereits offen. Nimm ihn in der Benachrichtigung an oder lehne ihn ab.|Une proposition est déjà en attente. Acceptez-la ou refusez-la dans la notification.
An option for another model is selected. Choose a compatible option or enable Show all.|別のモデル用の設定です。対応する設定を選ぶか、すべてのバリエーションを表示してください。|Diese Option ist für ein anderes Modell. Wähle eine passende Option oder zeige alle Varianten an.|Cette option est destinée à un autre modèle. Choisissez une option compatible ou affichez toutes les variantes.
Animation proposal declined, cancelled or expired.|再生の提案が辞退・取り消し・期限切れになりました。|Der Animationsvorschlag wurde abgelehnt, zurückgezogen oder ist abgelaufen.|La proposition a été refusée, annulée ou a expiré.
Animation proposed. Your partner chooses a role in the notification and accepts the launch.|提案を送りました。パートナーが通知で役割を選び、承諾すると準備を開始します。|Vorschlag gesendet. Dein Partner wählt in der Benachrichtigung eine Rolle und stimmt zu.|Proposition envoyée. Votre partenaire choisit son rôle dans la notification et accepte le lancement.
Approaching partner. Movement input cancels the launch.|パートナーに近づいています。移動操作をすると中止します。|Dein Charakter nähert sich dem Partner. Bewegungseingaben brechen den Start ab.|Rapprochement du partenaire. Une commande de déplacement annule le lancement.
Both players must stand and stop their emotes first. Pair launch is unavailable in combat, mounted or while casting.|先に二人とも立ち、エモートを止めてください。戦闘中・騎乗中・詠唱中はペア再生できません。|Beide müssen aufstehen und ihre Emotes beenden. Im Kampf, auf Reittieren oder beim Zaubern ist kein Paarstart möglich.|Les deux joueurs doivent se lever et arrêter leurs emotes. Le lancement est indisponible en combat, sur une monture ou pendant une incantation.
Both players must stand nearby, out of combat, unmounted and not casting.|二人とも近くで立ち、戦闘・騎乗・詠唱をしていない状態にしてください。|Beide müssen in der Nähe stehen: außerhalb des Kampfes, ohne Reittier und ohne zu zaubern.|Restez debout à proximité, hors combat, sans monture et sans lancer de sort.
Check the condition above. If it repeats, copy diagnostics from Settings.|上記の条件を確認してください。繰り返す場合は設定から診断情報をコピーしてください。|Prüfe die genannte Bedingung. Tritt es erneut auf, kopiere die Diagnose aus den Einstellungen.|Vérifiez la condition ci-dessus. Si le problème persiste, copiez le diagnostic dans les paramètres.
Check the selected Penumbra options and wait for Lightless synchronization.|Penumbraの設定を確認し、Lightlessの同期完了を待ってください。|Prüfe die gewählten Penumbra-Optionen und warte auf Lightless.|Vérifiez les options Penumbra et attendez la synchronisation Lightless.
Choose options compatible with your character model.|自分のキャラクターモデルに対応する設定を選んでください。|Wähle Optionen, die zum Modell deines Charakters passen.|Choisissez des options compatibles avec le modèle de votre personnage.
Choose your role. A unique alternative emote is suggested; verify same-slot role options yourself.|自分の役割を選んでください。別のエモートが一つなら自動で候補になります。同じスロットの役割設定は自分で確認してください。|Wähle deine Rolle. Gibt es nur eine andere Emote, wird sie vorgeschlagen. Rollen im selben Platz musst du selbst prüfen.|Choisissez votre rôle. Si une seule autre emote existe, elle est proposée. Vérifiez vous-même les rôles qui utilisent le même emplacement.
Click to reach the target pose. The icon restores automatically.|目的のポーズになるまでクリックしてください。アイコンは自動で元に戻ります。|Klicke bis zur gewünschten Pose. Das ursprüngliche Symbol erscheint automatisch wieder.|Cliquez jusqu’à la pose souhaitée. L’icône d’origine revient automatiquement.
Commands launched on the shared timer. Matching sit/idle phase is not guaranteed yet.|共通タイマーで再生しました。座り・待機ポーズの動きのタイミングはまだ完全には揃いません。|Befehle gemeinsam gestartet. Sitz- und Standanimationen laufen noch nicht garantiert phasengleich.|Commandes lancées ensemble. Les poses assises ou debout ne sont pas encore garanties parfaitement synchronisées.
Confirming position and facing after preparation…|準備後の位置と向きを確認中…|Position und Blickrichtung werden nach der Vorbereitung geprüft…|Vérification de la position et de l’orientation après préparation…
Drag by the title. Position is saved when you release the mouse.|タイトルをドラッグしてください。離すと位置を保存します。|Am Titel ziehen. Die Position wird beim Loslassen gespeichert.|Faites glisser le titre. La position est enregistrée au relâchement de la souris.
Each icon keeps its own mod options. Switching other emotes does not replace them.|各アイコンにMOD設定を保存します。他のエモートに切り替えても変わりません。|Jedes Symbol speichert seine eigenen Mod-Optionen. Andere Emotes ändern sie nicht.|Chaque icône conserve ses options. Jouer une autre emote ne les remplace pas.
Emote unavailable for untargeted pair playback.|ターゲットなしのペア再生ではこのエモートを使えません。|Diese Emote ist für den Paarstart ohne Ziel nicht verfügbar.|Cette emote n’est pas disponible pour un lancement à deux sans cible.
Enable pair mode on both clients. Right-click a player → Emote Shelf to invite; your partner accepts in the notification.|二人ともペア再生を有効にしてください。相手を右クリック→Emote Shelfで招待し、相手が通知から承諾します。|Aktiviert beide den Paarmodus. Rechtsklick auf den Spieler → Emote Shelf sendet eine Einladung; dein Partner nimmt sie in der Benachrichtigung an.|Activez le mode duo chez les deux joueurs. Clic droit sur le joueur → Emote Shelf envoie l’invitation ; le partenaire l’accepte dans la notification.
Hide is in the … menu to the right of the folder selector. Right-click a panel icon to open its mod.|非表示はフォルダー選択の右の「…」メニューにあります。パネルのアイコンを右クリックするとMODを開きます。|Ausblenden findest du im Menü „…“ rechts neben der Ordnerwahl. Rechtsklick auf ein Leistensymbol öffnet den Mod.|L’action Masquer se trouve dans le menu « … » à droite du dossier. Un clic droit sur une icône ouvre son mod.
Invite a partner through the context menu and wait for acceptance. Your partner must be nearby.|近くのパートナーを右クリックメニューから招待し、承諾を待ってください。|Lade einen Partner in deiner Nähe über das Kontextmenü ein und warte auf seine Zustimmung.|Invitez un partenaire à proximité par le menu contextuel et attendez son accord.
Invites you to a pair session. Accepting does not start an emote or move your character.|ペアへの招待です。承諾だけではエモートの再生や移動は始まりません。|Lädt dich zu einer Paarsitzung ein. Annehmen startet keine Emote und bewegt deinen Charakter nicht.|Vous invite à une session à deux. Accepter ne lance aucune emote et ne déplace pas votre personnage.
Launch coordination ended; this client did not receive the partner's detailed reason. Connection loss, game exit or a stalled game are possible, not confirmed. Server reports are correlated by attempt ID.|再生の調整が中断され、相手側の詳しい理由は届いていません。通信切断、ゲーム終了・停止の可能性がありますが、未確認です。サーバーの報告は試行IDで照合します。|Der gemeinsame Start wurde unterbrochen. Der genaue Grund vom Partner fehlt. Verbindungsabbruch, Spielende oder ein eingefrorenes Spiel sind möglich, aber nicht bestätigt. Serverberichte werden über die Versuchs-ID zugeordnet.|La coordination a été interrompue sans raison détaillée du partenaire. Une coupure réseau, une fermeture ou un blocage du jeu sont possibles, sans certitude. Les rapports serveur sont reliés par l’identifiant de tentative.
Lightless does not report handling this partner. Set up synchronization in Lightless first.|Lightlessでパートナーの同期が確認できません。先にLightlessで接続してください。|Lightless meldet diesen Partner nicht als synchronisiert. Richte zuerst die Synchronisierung ein.|Lightless ne signale pas ce partenaire comme synchronisé. Configurez d’abord la synchronisation.
Lightless no longer handles the partner; launch cancelled.|Lightlessがパートナーを同期しなくなったため、中止しました。|Lightless synchronisiert den Partner nicht mehr. Start abgebrochen.|Lightless ne synchronise plus le partenaire. Lancement annulé.
Lightless Sync is loaded. Wait for synchronization to finish on both sides before launch.|Lightless Syncは読み込み済みです。二人とも同期が終わってから再生してください。|Lightless Sync ist geladen. Wartet vor dem Start auf die Synchronisierung bei beiden Spielern.|Lightless Sync est chargé. Attendez la fin de la synchronisation des deux côtés avant de lancer.
Lightless Sync is required for both participants. Solo emotes do not require it.|ペア再生には二人ともLightless Syncが必要です。一人での再生には不要です。|Beide benötigen Lightless Sync. Für einzelne Emotes ist es nicht nötig.|Lightless Sync est requis chez les deux participants, mais pas pour jouer seul.
Lightless transfers mods/appearance, not our relay. We check that it handles the partner, but its API does not confirm that this animation finished downloading.|MODと外見を転送するのはLightlessです。パートナーの同期対象への登録は確認できますが、個々のアニメーションのダウンロード完了はAPIでは確認できません。|Mods und Aussehen überträgt Lightless, nicht unser Server. Wir prüfen, ob der Partner synchronisiert wird; die API bestätigt aber nicht den fertigen Download einer bestimmten Animation.|Lightless transfère les mods et l’apparence, pas notre serveur. Nous vérifions qu’il gère le partenaire, mais son API ne confirme pas le téléchargement complet d’une animation précise.
Once linked, Pair or Shift+icon proposes an animation. Your partner selects their role in the notification and accepts or declines. Keeping a target is not required. The Emote Shelf relay sees IP addresses and character/mod/selection hashes; character identity is not authenticated. Use with trusted partners only.|接続後、「ペアで再生」またはShift+アイコンで提案を送れます。相手は通知で役割を選び、承諾・辞退します。ターゲットの維持は不要です。サーバーはIPとキャラクター・MOD・設定のハッシュを扱いますが、本人確認はしません。信頼できる相手とだけ使ってください。|Nach dem Verbinden schlägst du mit „Zu zweit“ oder Umschalt+Symbol eine Animation vor. Dein Partner wählt eine Rolle und nimmt an oder lehnt ab. Ein Ziel ist nicht nötig. Der Server sieht IP-Adressen sowie Charakter-, Mod- und Auswahl-Hashes, prüft aber keine Identitäten. Nutze dies nur mit vertrauten Partnern.|Une fois connectés, « À deux » ou Maj+icône propose une animation. Le partenaire choisit son rôle et accepte ou refuse. Inutile de garder une cible. Le serveur voit les IP et les empreintes des personnages, mods et options, sans vérifier les identités. Utilisez cette fonction uniquement avec des partenaires de confiance.
Only accept an expected invitation from a trusted partner: the relay cannot verify character identity.|知っている相手からの、心当たりのある招待だけ承諾してください。サーバーは本人確認を行いません。|Nimm nur erwartete Einladungen vertrauter Partner an. Der Server kann die Charakteridentität nicht prüfen.|N’acceptez que les invitations attendues d’un partenaire de confiance : le serveur ne vérifie pas l’identité du personnage.
Pair cancelled: character state or distance is unsuitable.|キャラクターの状態または距離が条件に合わないため、中止しました。|Paarstart abgebrochen: Zustand oder Abstand der Charaktere passt nicht.|Lancement annulé : l’état ou la distance des personnages ne convient pas.
Pair cancelled: linked partner is not visible, or character/world/zone changed. A target is not required.|パートナーが見つからないか、キャラクター・ワールド・エリアが変わったため中止しました。ターゲットは不要です。|Paarstart abgebrochen: Partner nicht sichtbar oder Charakter, Welt oder Gebiet gewechselt. Ein Ziel ist nicht nötig.|Lancement annulé : partenaire invisible ou changement de personnage, monde ou zone. Aucune cible n’est nécessaire.
Pair cancelled: you moved or turned.|移動・回転したため、ペア再生を中止しました。|Paarstart abgebrochen: Du hast dich bewegt oder gedreht.|Lancement annulé : vous avez bougé ou tourné.
Pair launch of sit/idle requires experimental automatic pose selection. Manual mode remains manual.|座り・待機ポーズのペア再生には自動選択（試験機能）が必要です。手動モードでは自動で変更しません。|Sitz- und Standposen benötigen für den Paarstart die experimentelle Posenautomatik. Im manuellen Modus bleibt die Auswahl manuell.|Les poses assises ou debout à deux nécessitent le choix automatique expérimental. Le mode manuel reste manuel.
Pair launch requires loaded Lightless Sync. Solo playback does not.|ペア再生にはLightless Syncの読み込みが必要です。一人での再生には不要です。|Für den Paarstart muss Lightless Sync geladen sein. Allein geht es auch ohne.|Le lancement à deux nécessite Lightless Sync chargé. La lecture en solo n’en a pas besoin.
Partner connected. Shift+icon sends an animation proposal with role selection.|接続しました。Shift+アイコンで役割選択付きの再生提案を送れます。|Partner verbunden. Umschalt+Symbol sendet einen Animationsvorschlag mit Rollenwahl.|Partenaire connecté. Maj+icône envoie une proposition avec choix du rôle.
Partner did not enable required alignment. Update both plugins.|相手側が必須の位置合わせに対応していません。二人ともプラグインを更新してください。|Der Partner hat die erforderliche Ausrichtung nicht aktiviert. Aktualisiert beide Plugins.|L’alignement requis n’est pas activé chez le partenaire. Mettez les deux plugins à jour.
Position or facing diverged before start. Launch cancelled.|開始直前に位置または向きがずれたため、中止しました。|Position oder Blickrichtung wich vor dem Start ab. Start abgebrochen.|La position ou l’orientation a changé avant le départ. Lancement annulé.
Position/facing did not match after preparation. Launch cancelled.|準備後の位置または向きが合わなかったため、中止しました。|Position oder Blickrichtung stimmt nach der Vorbereitung nicht überein. Start abgebrochen.|La position ou l’orientation ne correspond pas après préparation. Lancement annulé.
Relay stopped responding; launch cancelled.|サーバーの応答が途絶えたため、中止しました。|Der Server antwortet nicht mehr. Start abgebrochen.|Le serveur ne répond plus. Lancement annulé.
Reports contain plugin version, random report/attempt IDs, technical failure code and checkpoint, relay/Lightless state, preparation flags, relative distance and angle. No full logs, names, chat, tokens, paths or absolute coordinates. The server sees your connection IP but does not store it in reports. The Emote Shelf administrator accesses reports via SSH. Retention is 14 days with hourly cleanup; storage is capped at 10,000 reports / 32 MiB. Network failures use bounded in-memory retries; exiting the game discards the queue.|送信内容はプラグインのバージョン、ランダムな報告・試行ID、エラーコードと発生段階、接続・Lightlessの状態、準備状態、相対距離と角度です。完全なログ、名前、チャット、トークン、パス、絶対座標は送りません。接続先にはIPが見えますが、報告には保存しません。Emote Shelf管理者がSSHで閲覧します。保管は14日間で毎時削除処理を行い、最大1万件・32 MiBです。通信失敗時はメモリ内で回数を制限して再送し、ゲーム終了時に待機分を破棄します。|Gesendet werden Plugin-Version, zufällige Berichts- und Versuchs-IDs, Fehlercode und Fehlerschritt, Verbindungs- und Lightless-Status, Vorbereitungsstatus sowie relativer Abstand und Winkel. Keine vollständigen Logs, Namen, Chats, Tokens, Pfade oder absoluten Koordinaten. Der Server sieht deine IP, speichert sie aber nicht im Bericht. Zugriff hat der Emote-Shelf-Administrator über SSH. Aufbewahrung: 14 Tage, stündliche Bereinigung, höchstens 10.000 Berichte / 32 MiB. Bei Netzfehlern gibt es begrenzte Wiederholungen im Arbeitsspeicher; beim Spielende verfällt die Warteschlange.|Les rapports contiennent la version, des identifiants aléatoires de rapport et de tentative, le code et l’étape de l’erreur, l’état de la connexion et de Lightless, la préparation, la distance relative et l’angle. Aucun journal complet, nom, chat, jeton, chemin ou coordonnée absolue. Le serveur voit votre IP sans l’enregistrer dans les rapports. L’administrateur Emote Shelf y accède par SSH. Conservation : 14 jours, nettoyage horaire, maximum 10 000 rapports / 32 Mio. Les erreurs réseau font l’objet de tentatives limitées en mémoire ; quitter le jeu vide la file.
Select the pose with Change pose. Automation is an experimental setting.|「ポーズを変える」で選択してください。自動選択は設定の試験機能です。|Wähle die Pose mit „Pose wechseln“. Die Automatik ist experimentell.|Choisissez la pose avec « Changer de pose ». Le choix automatique est expérimental.
Standing idle is unavailable: stop the previous looping emote or movement, then retry.|立ちポーズに切り替えられません。再生中のエモートや移動を止めてからやり直してください。|Standpose nicht verfügbar. Beende die laufende Emote oder Bewegung und versuche es erneut.|Pose debout indisponible. Arrêtez l’emote en boucle ou le déplacement, puis réessayez.
Start was missed or connection lost. Please retry.|開始時刻に間に合わなかったか、接続が切れました。やり直してください。|Startzeit verpasst oder Verbindung verloren. Bitte erneut versuchen.|Départ manqué ou connexion perdue. Réessayez.
The game cannot execute this emote yet. Retry standing without another emote.|今はこのエモートを実行できません。他のエモートを止めて立ってからやり直してください。|Das Spiel kann diese Emote gerade nicht ausführen. Steh auf, beende andere Emotes und versuche es erneut.|Le jeu ne peut pas encore jouer cette emote. Réessayez debout, sans autre emote en cours.
The icon panel is a separate window. Unlock it and drag its empty area to move it. Hide it here or with /eshelf hide.|アイコンパネルは独立したウィンドウです。固定を解除し、空いている部分をドラッグして移動できます。ここか /eshelf hide で非表示にできます。|Die Symbolleiste ist ein eigenes Fenster. Entsperre sie und ziehe eine freie Fläche zum Verschieben. Hier oder mit /eshelf hide ausblenden.|Le panneau d’icônes est une fenêtre séparée. Déverrouillez-le et faites glisser une zone vide pour le déplacer. Masquez-le ici ou avec /eshelf hide.
The last failure remains visible in the plugin window. Technical reports upload automatically as a required condition disclosed before first use. See Settings → Required diagnostics for collected fields, retention and delivery status. Verbose local diagnostics add network events. Unrecognized mods need a command only when the intended emote is missing from the browser.|前回の失敗理由はウィンドウに残ります。初回に表示される利用条件に従い、技術的な報告を自動送信します。送信内容・保管・送信状態は「設定→必須の診断データ送信」で確認できます。詳しいローカル診断では通信の記録も追加します。未識別MODのコマンド設定は、目的のエモートが一覧にない場合のみ必要です。|Der letzte Fehler bleibt im Plugin-Fenster sichtbar. Technische Berichte werden automatisch gesendet; diese Nutzungsbedingung wird vor dem ersten Start erklärt. Unter Einstellungen → Erforderliche Diagnose stehen Datenumfang, Aufbewahrung und Sendestatus. Ausführliche lokale Diagnose ergänzt Netzwerkereignisse. Nicht erkannte Mods brauchen nur dann einen Befehl, wenn die gewünschte Emote im Katalog fehlt.|La dernière erreur reste visible dans la fenêtre. Les rapports techniques sont envoyés automatiquement, conformément aux conditions présentées avant la première utilisation. Paramètres → Diagnostic obligatoire indique les données collectées, leur conservation et l’état d’envoi. Le diagnostic local détaillé ajoute les événements réseau. Un mod non reconnu n’a besoin d’une commande que si l’emote manque au catalogue.
The linked partner is not visible nearby. Check world, zone and pair connection.|接続中のパートナーが近くに見つかりません。ワールド・エリア・接続を確認してください。|Der verbundene Partner ist nicht in der Nähe sichtbar. Prüft Welt, Gebiet und Verbindung.|Le partenaire connecté n’est pas visible à proximité. Vérifiez le monde, la zone et la connexion.
The proposal is no longer active. Send a new one and wait for acceptance.|提案はもう有効ではありません。送り直して承諾を待ってください。|Der Vorschlag ist nicht mehr aktiv. Sende einen neuen und warte auf Zustimmung.|La proposition n’est plus active. Envoyez-en une nouvelle et attendez l’accord.
The relay response or start signal was late. Retry; reconnect the pair if it repeats.|サーバーの応答か開始信号が遅れました。繰り返す場合はペアを再接続してください。|Serverantwort oder Startsignal kam zu spät. Erneut versuchen; bei Wiederholung die Paarverbindung neu herstellen.|La réponse du serveur ou le signal de départ est arrivé trop tard. Réessayez ; reconnectez le duo si cela se reproduit.
The selected corner stays anchored when buttons are added. Above chat, grow upward; at the right edge, grow leftward. For a single column, set icons per row to 1.|ボタン追加時も選んだ隅は動きません。チャットの上では上向き、画面右端では左向きに追加してください。縦一列なら1行のアイコン数を1にします。|Die gewählte Ecke bleibt beim Hinzufügen fest. Über dem Chat nach oben wachsen lassen, am rechten Rand nach links. Für eine einzelne Spalte ein Symbol pro Zeile wählen.|Le coin choisi reste fixe lors de l’ajout de boutons. Au-dessus du chat, ajoutez les lignes vers le haut ; au bord droit, les colonnes vers la gauche. Pour une colonne, choisissez une icône par ligne.
The slot comes from the mod files. Enable automatic selection in Settings, or click Change pose manually. Plain /cpose cycles to the next pose. Selecting a numbered pose with /cpose N is not yet verified.|スロットはMODファイルから検出します。設定で自動選択を有効にするか、「ポーズを変える」で手動選択してください。/cpose は次のポーズに切り替えます。数字での直接指定には対応していません。|Der Platz wird aus den Mod-Dateien ermittelt. Aktiviere die Automatik in den Einstellungen oder nutze „Pose wechseln“. /cpose wechselt zur nächsten Pose; eine Zahl wählt keine bestimmte Pose aus.|L’emplacement est détecté dans les fichiers du mod. Activez le choix automatique dans les paramètres ou utilisez « Changer de pose ». /cpose passe à la pose suivante ; un numéro ne sélectionne pas une pose précise.
This is the partner's exact combination. Select another role/option.|相手と同じ組み合わせです。別の役割・設定を選んでください。|Das ist genau die Auswahl deines Partners. Wähle eine andere Rolle oder Option.|C’est exactement la combinaison du partenaire. Choisissez un autre rôle ou une autre option.
Unlock and drag the panel on the Panel tab. Above chat, grow upward; at the right edge, grow leftward. Set one icon per row for a column. Use the notification preview in pair settings to move the pair notice.|「パネル」で固定を解除してドラッグします。チャットの上では上向き、右端では左向きに追加します。縦一列なら1行に1アイコンを設定します。ペア通知は設定のプレビューで移動できます。|Entsperre die Leiste im Reiter „Leiste“ und ziehe sie. Über dem Chat nach oben, am rechten Rand nach links wachsen lassen. Für eine Spalte ein Symbol pro Zeile wählen. Die Paarbenachrichtigung lässt sich über ihre Vorschau in den Einstellungen verschieben.|Déverrouillez puis déplacez le panneau dans l’onglet Panneau. Faites-le grandir vers le haut au-dessus du chat, vers la gauche au bord droit. Une icône par ligne donne une colonne. Déplacez la notification du duo via son aperçu dans les paramètres.
Until accepted, the plugin does not play emotes, connect pairs or upload data. Close this window to leave it inactive; disable or uninstall it in Dalamud. After acceptance, disable the plugin to stop collection.|同意するまではエモート再生、ペア接続、データ送信を行いません。使わない場合はこのウィンドウを閉じ、Dalamudで無効化または削除できます。同意後に送信を止めるにはプラグインを無効にしてください。|Vor deiner Zustimmung spielt das Plugin keine Emotes ab, verbindet keine Partner und sendet keine Daten. Schließe das Fenster, um es inaktiv zu lassen; deaktiviere oder entferne es in Dalamud. Nach Zustimmung kannst du die Erfassung durch Deaktivieren des Plugins beenden.|Avant votre accord, le plugin ne joue aucune emote, ne connecte aucun duo et n’envoie rien. Fermez cette fenêtre pour le laisser inactif ; désactivez-le ou désinstallez-le dans Dalamud. Après acceptation, désactivez le plugin pour arrêter la collecte.
Using Emote Shelf requires automatic technical reports to anarchyclan.ru. Collection cannot be disabled while continuing to use the plugin.|Emote Shelfの利用にはanarchyclan.ruへの技術的な報告の自動送信が必要です。送信を無効にしたまま使用することはできません。|Die Nutzung von Emote Shelf setzt automatische technische Berichte an anarchyclan.ru voraus. Du kannst die Erfassung nicht abschalten und das Plugin weiterverwenden.|L’utilisation d’Emote Shelf nécessite l’envoi automatique de rapports techniques à anarchyclan.ru. Il n’est pas possible de désactiver cet envoi tout en continuant à utiliser le plugin.
Within 2 yalms. One stands, the other approaches. Post-preparation correction up to 0.1 yalms aims for ≤0.01; playback accepts a residual ≤0.1 yalms. Facing is checked separately. Accepting a launch authorizes alignment. Height and scale are unchanged.|2ヤルム以内で、一人は止まり、もう一人が近づきます。準備後は最大0.1ヤルムを微調整し、0.01以下を目指します。再生時の許容差は0.1以下で、向きは別に確認します。再生の承諾には位置合わせの許可も含みます。身長・倍率は変えません。|Innerhalb von 2 Yalmen bleibt einer stehen, der andere nähert sich. Nach der Vorbereitung wird bis zu 0,1 Yalme nachjustiert, mit dem Ziel ≤0,01. Zum Start sind ≤0,1 erlaubt; die Blickrichtung wird separat geprüft. Die Zustimmung zum Start erlaubt die Ausrichtung. Größe und Skalierung bleiben unverändert.|À moins de 2 yalms, l’un reste immobile et l’autre s’approche. Après préparation, la correction de 0,1 yalm maximum vise ≤0,01 ; le départ tolère ≤0,1. L’orientation est vérifiée séparément. Accepter le lancement autorise l’alignement. La taille et l’échelle restent inchangées.
You and your partner share a Penumbra collection: selected options may affect both. Different roles need correctly configured collections/mod synchronization.|二人に同じPenumbraコレクションが使われています。設定が両方に反映される可能性があります。異なる役割にはコレクションと同期の適切な設定が必要です。|Ihr verwendet dieselbe Penumbra-Sammlung. Gewählte Optionen können beide betreffen. Für verschiedene Rollen müssen Sammlungen und Mod-Synchronisierung passend eingerichtet sein.|Vous partagez la même collection Penumbra : les options peuvent affecter les deux personnages. Des rôles différents nécessitent des collections et une synchronisation correctement configurées.
""";
}
