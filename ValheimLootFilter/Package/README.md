# Loot Filter

Tired of your inventory filling up with stones, resin, and greydwarf eyes? Loot Filter lets you choose exactly which items are excluded from auto-pickup — right from an in-game UI.

## Features

- **In-game filter panel** showing every item you have discovered, grouped by category with their icons
- **One click to toggle**: click an item to exclude it from auto-pickup (marked red with a ✕), click again to re-enable
- **Inventory button**: a Loot Filter button is added to the top bar of your inventory, next to the Trophies button
- **Hotkey**: press `L` to open/close the filter panel anywhere
- **Persistent**: your exclusions are saved to the mod config and survive restarts
- Excluded items can still be picked up manually — only auto-pickup is blocked

## Usage

1. Open your inventory and click the **Loot Filter** button in the top bar (or press `L`)
2. Click any item to exclude it from auto-pickup — it turns red with a ✕ marker
3. Click it again to allow auto-pickup again
4. Close the panel and enjoy a cleaner inventory

Exclusions are stored in `BepInEx/config/com.ukie.ValheimLootFilter.cfg` and can also be edited there by hand (comma-separated prefab names).

## Installation (manual)

1. Install [BepInEx](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) and [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/)
2. Drop `ValheimLootFilter.dll` into `BepInEx/plugins`

## AI usage disclaimer

Parts of this mod's code were written with the assistance of AI tools (Claude). All AI-generated code has been reviewed and tested in-game by a human before release.

## Known issues

## Changelog

### 0.0.2

- Loot Filter button injected into the inventory top bar (vanilla-styled, custom icon)
- Dynamic top-bar layout that fits all buttons inside the section — plays nice with other mods that add buttons
- Filter panel with category grouping and item icons

### 0.0.1

- Initial release: exclude items from auto-pickup via config
