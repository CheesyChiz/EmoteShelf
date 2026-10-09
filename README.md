# Emote Shelf

Emote Shelf is a Dalamud plugin for playing emotes from different Penumbra mods without changing mod priorities by hand. It requires Penumbra.

Open it with `/eshelf` or `/es`. On **Emotes**, select a mod in the left pane; its emotes and variants appear on the right. Preview an option before adding it to the on-screen icon panel. Each icon saves its own Penumbra option selection and stays there after restarting the game. Click an icon to play; Ctrl+Shift+click removes it. Hide unwanted mods from the browser and restore them under **Settings → Hidden mods**.

Folders on the left are local visual categories. Use the buttons below the mod tree to create, rename or remove them. Drag a mod onto a folder, or choose a folder in the mod's right pane. This never moves Penumbra files or changes Penumbra's own categories.

Use **Panel** to change the icon grid, icon size, opacity, and position lock. Drag the grip beside a saved emote, or drag icons on the overlay, to reorder them. Locking removes the panel background while leaving its icons in place. The panel has no close button; hide it in the plugin or with `/es hide`. `/es show` brings it back, and `/es scan` refreshes the mod list.

When you play an icon, Emote Shelf temporarily gives its mod priority above the collection's other enabled mods, asks Penumbra to redraw your character, and starts the game emote. It checks that Penumbra reports the temporary selection before sending the command. The temporary selection remains until you play another icon, clear it in Settings, or unload the plugin. It does not change permanent Penumbra priorities.

For `/groundsit` replacements, the plugin detects the replaced pose from the mod when possible. An optional manual pose selector is tucked away for ambiguous mods. It also recognizes single-slot standing-idle replacements such as `pose03` and cycles the normal `/cpose` command to that slot. Mod variants are separate from game pose slots: selecting one variant turns off other option groups that replace the same animation, when the mod provides an off option. Switching through poses can take a few seconds. Some emotes may still need in-game testing, especially idle and sitting animations that the game caches. If an emote is not unlocked on your character, a mod cannot play it merely by replacing its files.

Unrecognized animation mods are tucked away under **Settings → Unrecognized mods**. Many replace combat files rather than a directly playable emote, so you can usually ignore them. `/cpose3` is not a game command; supported single-slot idle mods instead use repeated `/cpose`. A manual bookmark is allowed only for a real game emote command.

The interface supports Russian, English, Japanese, German, and French. The source is built with the Dalamud SDK and `Penumbra.Api.dll`.
