# Emote Shelf

Experimental Dalamud plugin for selecting Penumbra animation mods, saving role/options bookmarks, and coordinating playback with one linked partner. Open with `/es` or `/eshelf`.

## 0.3.9.0 testing build

Settings → Required plugins now includes fixed project links, repository copying, a shortcut to Dalamud's Experimental settings and an installer search for each dependency. Repository addition and installation use Dalamud's own confirmation controls; the plugin does not rewrite Dalamud configuration or install DLLs privately. Verified sources: Penumbra's xivdev repository, Caraxi's SimpleHeels repository, Etheirys' Brio repository and Lightless's official manifest/source repository. Avoid adding duplicate repositories.

The wide library window has a compact partner sidebar; narrow windows retain two panes. Pair controls and phase/resource status have a dedicated Partner tab. Mod inspection is collapsed, keeping playback actions prominent.

### Own resource verification and phase control

Both clients now need 0.3.9.0 for new pair launches. `/v6/sync` is bound to an accepted launch or its completed replay agreement and the current link capability. It exchanges at most 128 game asset paths and SHA-256 hashes (no local disk paths/file bodies), verification flags and primary loop time/duration. Peer samples become unusable after two seconds and are removed after three seconds; unlink removes them. Samples are not stored in diagnostic reports. The renewed first-use notice discloses this exchange.

After local preparation, each client hashes selected resources, verifies the peer's resources using the peer's locally resolved Penumbra collection, and waits for reciprocal verification before readiness. Selected-command PAPs are filtered through the game model redirect table; other emotes' PAPs are excluded. Applicable declared sound/VFX dependencies are also checked. Oversized manifests, unsupported/unresolved resources, stale clients or mismatches prevent launch with a resource-verification reason. A matching disk file and Penumbra mapping does **not** prove that the engine has already loaded that resource or started its audio channel. This is stronger evidence than Lightless's handled-actor list, not a fabricated download-complete IPC.

While the same accepted animation runs, each client measures its own unambiguous primary animation control. Equal-duration single-control loops can be compared with sample-age and round-trip estimates. Four consecutive deviations over 200 ms (low-latency samples only) request the existing agreed shared restart from one designated client, at most once every 30 seconds. Maintain pair phase can disable automatic restart. Blended tracks, unequal durations, stale samples and ambiguous states are not corrected. This is measured best-effort drift correction, not continuous frame-lock. Music restarts through the ordinary playback/redraw path; its actual channel position is not measured or seeked.

Automated checks cover packaging, consent, relay state, localization, pose rules and conversion. They do **not** establish in-game compatibility, audible music, or frame-perfect two-client playback. Both participants should use this version for transfer-and-launch and shared restart.

### Library and panel

Choose a mod and options on Emotes; Preview plays it, Add to panel saves that exact selection. Panel bookmarks can have role labels and aliases (`/es play alias`, `/es pair alias`). Ctrl+Shift+click removes an overlay bookmark; right-click opens its mod. The … menu hides a mod without deleting it or existing bookmarks. Restore hidden mods in Settings.

Folders are local visual categories, not filesystem changes. Panel settings control icon size, grid, growth direction, opacity and position lock. `/es show`, `/es hide`, `/es scan` are also available. Saved two-role presets retain each role's options and optional local adjustment.

Playback uses Penumbra temporary settings, leaving permanent priorities untouched. Clear temporary selection removes only Emote Shelf's overrides. Changed or missing saved options fail visibly; review and re-save the bookmark rather than silently choosing another role. Model compatibility filtering is conservative and can be bypassed with Show all variants.

Automatic pose selection is always enabled. Standing `poseNN` and ground-sit `j_poseNN` slots use their actual indices. Cycling is checked at 100 ms intervals, not a claim about game transition duration. Ground-sit switching does not reseat the player. Manual Change pose remains available. `/cpose 3` does not select a slot directly.

### Locked emotes and sound

Normal Preview, panel clicks and pair preparation automatically use a compatible unlocked carrier when possible. The plugin creates a separate content-addressed Penumbra copy, retargeting PAP animation names with NoireLib and preserving option groups and referenced sound/VFX resources. Original mods/bookmarks remain unchanged. Cached copies are verified before reuse and hidden from this plugin's library. Character, zone, collection changes or a newer click invalidate queued playback. This does not unlock an emote in the game. Unsupported timelines, missing resources or incompatible data fail with a reason; not every mod is convertible.

When an ordinary animation with selected SCD resources replaces an active emote loop, preparation waits for settings, requests redraw, requires the local-character redraw acknowledgment, then checks settings before dispatch. Sit, ground sit and standing pose paths are excluded. This is a candidate fix for lost audio, **not proof that music plays or stays synchronized**.

### Pair launch

Penumbra is required for playback; Lightless Sync is required for pair features. Enable pair mode on both clients in Partner and establish Lightless synchronization. Right-click the partner → Emote Shelf invitation; acceptance links one partner but does not authorize movement or playback. No party, room, or maintained target is required.

Pair or Shift+bookmark offers an animation. The partner chooses their role and accepts or declines. Start nearby, standing still, out of combat, unmounted. Alignment is mandatory: one actor anchors, the other approaches; fine correction aims for ≤0.01 yalms, while the final position check permits ≤0.1 yalms. Facing is checked separately. No character height, body scale or skeleton changes are made. Coordinates come from the locally visible linked partner, not a remote movement command.

Both clients must prepare and confirm readiness before a shared command timer is scheduled. Moving, zoning, losing the partner, expired timing, invalid options or failed preparation cancels. Diagnostics include bounded technical reasons. Lightless's handled-actor API does **not** confirm completed downloads of this specific animation: wait for synchronization first. Different roles require appropriately separated collections; a shared collection can affect both actors.

After both clients report a successful dispatch, Restart accepted animation on both clients can replay that same accepted selection with the same linked partner for up to 30 minutes, while that emote is still playing. A new role/animation needs new acceptance. Restart requires the actors to remain aligned and passes preparation/readiness checks again. This coordinates commands on both clients, not just a local visual reset. Network delay, game updates, transitions and sound loading can still cause phase differences. The separate local visual reset and `/es sync` affect only your screen.

### Transfer a mod, then launch together

From the sender's animation choose Send mod with playback offer, then choose the partner's emote, options and pose directly in the transfer window. Saved bookmarks are optional shortcuts, not a prerequisite. Choose the partner's body options rather than assuming your own race/model. Identical role selections are blocked. The mod is packaged automatically from its manifests and referenced assets; manual PMP export is not needed. The partner sees their role/options and accepts installation, activation, alignment and shared start in one action. Only after installation, activation and local role validation does the relay create the accepted pair attempt. Both clients then prepare, verify resources, align and start through the ordinary readiness barrier.

Plain transfers also support manually selected ZIP-based PMP files and never dispatch an emote command. The sender acknowledges redistribution responsibility once; the recipient acknowledges security risks on first receipt and accepts **every package** separately. Existing mods are never merged: imports have fresh directories and identities. Activation is bound to the character and Penumbra collection captured at acceptance. Enabling can immediately affect appearance, idle, sound and VFX, including other actors sharing that collection. Activation failure requests rollback of only this fresh import; failure to prepare a role leaves the installed mod available for inspection.

Limits: 128 MiB compressed, 512 MiB expanded, 4,096 entries, 128 MiB per asset, 2 MiB per JSON document. Traversal, links, duplicate paths, scripts/executables and missing referenced files are rejected. Archive validation is **not a malware or game-parser safety guarantee**. Only accept trusted senders. The server holds packages in bounded memory (128 MiB total, 32 offers), expires them after ten minutes, and removes them on rejection, unlink or completion. A full-size package reserves the entire transfer capacity until completion. Restart drops transfers. No persistent package archive or public package URL exists. See relay/TRANSFERS.md.

### SimpleHeels and Brio

Each optional integration is required only for its own features. On a bookmark or saved role, Save current SimpleHeels adjustment captures the current temporary XYZ/yaw offset. A saved adjustment is applied after launch and restored on end/cancel/new selection when still owned by Emote Shelf; a subsequent manual change is not overwritten. This is a visual offset, not network position or automatic body-size matching. Capture and restoration require live-game verification with your SimpleHeels configuration.

Brio API 3 is supported for saving the selected GPose actor's pose to a bookmark, applying it to the selected GPose actor and undoing the most recent application to that same actor. These operations require GPose, do not spawn actors or affect a world partner, and do not transmit pose JSON. Exiting GPose discards undo. `/es offset` and `/es livepose` open the integrations' tools.

### Reports, security and licensing

The first-use notice explains mandatory diagnostic uploads. Technical logs are English; interface and FAQ support Russian, English, Japanese, German and French. Reports contain bounded categories/context rather than names, local file paths or tokens. A crash or network loss can prevent a report; the UI must not invent a specific remote cause.

Relay identity is not authenticated game identity. The service sees IP addresses and deterministic, guessable character/mod identifiers; hashes are not anonymity. Session capabilities use Authorization headers. Accept only expected invitations from trusted partners. Pair coordination uses local validated actions; package launch metadata is separately validated and bound to exact acceptance. The relay runs behind HTTPS with bounded requests, sessions, rates and ephemeral capabilities.

Emote Shelf is AGPL-3.0. See LICENSE, THIRD-PARTY-NOTICES.md and BUILDING.md. Use package-release.ps1: its release includes matching SOURCE.zip and dependency licenses. Do not distribute the SDK latest.zip alone. The relay serves corresponding source at `/source.zip`.

## Evening game-test checklist

1. Solo locked emote: Preview, repeat, switch variants, cancel by selecting another animation. Verify original mod unchanged.
2. Ordinary musical emote: repeat and switch songs/mods; confirm audible sound. Verify sit/idle still use the fast path.
3. Pair without transfer: accept, approach from different headings, verify final direction, test cancellation and explicit reason.
4. Transfer with two saved roles: recipient accepts once, import activates only in the intended collection, both align/start. Try rejection and changed collection.
5. Shared restart: both remain in accepted animation; verify both restart. Stop on one client: restart must not silently begin a different action.
6. Capture/apply SimpleHeels adjustment; stop and verify restoration, then verify manual changes are preserved.
7. Brio in GPose: save, apply and undo on the same selected actor. Outside GPose these controls must be unavailable.

Run `dotnet run --project tests/PoseSlotTests.csproj` and `python -B -m unittest discover -s relay -p 'test_*.py'` for offline checks. Relay smoke checks use synthetic identities and do not test game playback.
