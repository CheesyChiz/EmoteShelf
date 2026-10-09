# Emote Shelf

Emote Shelf is a Dalamud plugin for playing emotes from different Penumbra mods without changing mod priorities by hand. It requires Penumbra.

Open it with `/eshelf` or `/es`. On **Emotes**, select a mod in the left pane; its emotes and variants appear on the right. Preview an option before adding it to the on-screen icon panel. Each icon saves its own Penumbra option selection and stays there after restarting the game. Click an icon to play; Ctrl+Shift+click removes it. Hide unwanted mods from the browser and restore them under **Settings → Hidden mods**.

Folders on the left are local visual categories. Use the buttons below the mod tree to create, rename or remove them. Drag a mod onto a folder, or choose a folder in the mod's right pane. This never moves Penumbra files or changes Penumbra's own categories.

Use **Panel** to change the icon grid, icon size, opacity, and position lock. Drag the grip beside a saved emote, or drag icons on the overlay, to reorder them. Locking removes the panel background while leaving its icons in place. The panel has no close button; hide it in the plugin or with `/es hide`. `/es show` brings it back, and `/es scan` refreshes the mod list.

When you play an icon, Emote Shelf temporarily gives its mod priority above the collection's other enabled mods, asks Penumbra to redraw your character, and starts the game emote. Before sending the command it checks both the temporary option settings and the effective animation file paths resolved by Penumbra; it refuses to play when another mod still owns a required file. The temporary selection remains until you play another icon, clear it in Settings, or unload the plugin. It does not change permanent Penumbra priorities.

For `/groundsit` replacements, the plugin reads the actual `j_poseNN` filenames. `j_pose01` means game pose index 1 (one `/cpose` step after the default index 0), not index 0. If you are already sitting, it first stands up, then switches the mod and enters `/groundsit` again. Mods replacing several slots offer a selector containing only those slots; ambiguous files have an advanced manual override. Standing-idle `poseNN` files work the same way. Selecting a mod variant turns off other option groups that replace the same animation when the mod provides an off option. The plugin sends `/cpose` no faster than every 0.5 seconds and waits for the game to confirm each step before sending another; the visible transition may take longer. If an emote is not unlocked on your character, replacing its files cannot unlock it.

Unrecognized animation mods are tucked away under **Settings → Unrecognized mods**. Many replace combat files rather than a directly playable emote, so you can usually ignore them. `/cpose3` is not a game command; supported idle mods instead use repeated `/cpose`. A manual bookmark is allowed only for a real game emote command.

The interface supports Russian, English, Japanese, German, and French. The source is built with the Dalamud SDK and `Penumbra.Api.dll`.
