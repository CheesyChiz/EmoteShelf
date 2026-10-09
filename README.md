# Emote Shelf

Emote Shelf is a Dalamud plugin for playing emotes from different Penumbra mods without changing mod priorities by hand. It requires Penumbra.

Open it with `/eshelf`. On **Emotes**, expand a mod, preview an emote, and add the variant you like to the on-screen icon panel. Each icon saves its own Penumbra option selection and stays there after restarting the game. Click an icon to play; Ctrl+Shift+click removes it. Hide unwanted mods from the browser and restore them under **Settings → Hidden mods**.

Use **Panel** to change the icon grid, icon size, opacity, and position lock. Locking removes the panel background while leaving its icons in place. The panel has no close button; hide it in the plugin or with `/eshelf hide`. `/eshelf show` brings it back, and `/eshelf scan` refreshes the mod list.

When you play an icon, Emote Shelf temporarily gives its mod priority above the collection's other enabled mods, asks Penumbra to redraw your character, and starts the game emote. It checks that Penumbra reports the temporary selection before sending the command. The temporary selection remains until you play another icon, clear it in Settings, or unload the plugin. It does not change permanent Penumbra priorities.

For `/groundsit` replacements, choose the target pose in the emote card when automatic detection is wrong. Switching through poses takes a few seconds. Some emotes may still need in-game testing, especially idle and sitting animations that the game caches. If an emote is not unlocked on your character, a mod cannot play it merely by replacing its files.

The interface supports Russian, English, Japanese, German, and French. The source is built with the Dalamud SDK and `Penumbra.Api.dll`.
