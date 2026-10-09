# Emote Shelf

Emote Shelf is a Dalamud plugin for playing emotes from different Penumbra mods without changing mod priorities by hand. It requires Penumbra.

Open it with `/eshelf` or `/es`. On **Emotes**, select a mod in the left pane; its emotes and variants appear on the right. Preview an option before adding it to the on-screen icon panel. Each icon saves its own Penumbra option selection and stays there after restarting the game. Variant bookmarks include both the mod and option-group names so similarly named options remain distinguishable. Click an icon to play; Ctrl+Shift+click removes it. Hide unwanted mods from the browser and restore them under **Settings → Hidden mods**.

Folders on the left are local visual categories. Use the buttons below the mod tree to create, rename or remove them. Drag a mod onto a folder, or choose a folder in the mod's right pane. This never moves Penumbra files or changes Penumbra's own categories.

Use **Panel** to change the icon grid, icon size, opacity, and position lock. Drag the grip beside a saved emote, or drag icons on the overlay, to reorder them. Locking removes the panel background while leaving its icons in place. The panel has no close button; hide it in the plugin or with `/es hide`. `/es show` brings it back, and `/es scan` refreshes the mod list.

When you play an icon, Emote Shelf temporarily gives its mod priority above the collection's other enabled mods, asks Penumbra to redraw your character, and starts the game emote. Before sending the command it checks both the temporary option settings and the effective animation file paths resolved by Penumbra; it refuses to play when another mod still owns a required file. The temporary selection remains until you play another icon, clear it in Settings, or unload the plugin. It does not change permanent Penumbra priorities.

For `/groundsit` replacements, the plugin reads `j_poseNN` filenames; standing-idle replacements use `poseNN`. Slot numbers are displayed without adding one. If already seated, it stands up before switching the mod and entering `/groundsit` again. A previous looping emote must finish or be stopped before standing idle is available.

Automatic pose selection is experimental: it stages the preceding saved pose and sends one normal `/cpose`, then waits for the actor to report the target slot. This follows the approach in [SecretTweaks ChangePoseDirect](https://github.com/Caraxi/SecretTweaks/blob/b3ecdf54f572acb65adb11b9403be420f91e97c8/ChangePoseDirect.cs); no additional plugin is required. Actual pose context is read using `GetPoseKind()`. A reported slot is not proof that the visible animation is correct. Turn off **Automatic pose selection** to use the adjacent **Change pose** button, with current and target slots shown. Manual input cancels a pending automatic pose selection.

Selected settings and mod ownership are checked, but exact physical filenames are not enforced: Penumbra may legitimately override animation files through higher-priority groups such as lip sync. If an emote is not unlocked, a replacement cannot unlock it. Music when switching directly between looping emotes remains a known issue under investigation; 0.2.9.0 does not claim to fix it. Live-game validation of the new direct-pose path is pending.

Unrecognized animation mods are tucked away under **Settings → Unrecognized mods**. Many replace combat files rather than a directly playable emote, so you can usually ignore them. `/cpose3` is not a game command. A manual bookmark is allowed only for a real game emote command.

The interface supports Russian, English, Japanese, German, and French. The source is built with the Dalamud SDK and `Penumbra.Api.dll`.
