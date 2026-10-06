# BaseWorks

**Storage, production automation, and loadout management for 7 Days to Die V3.3 b18.** BaseWorks connects nearby chests to a Storage Console, lets you craft from accessible supplies, runs orders through native workstations, and swaps saved equipment profiles. It is for solo/local worlds with Easy Anti-Cheat disabled.

BaseWorks was previously called NearbyCraft. The installed folder, DLL, block IDs, and save files still use `NearbyCraft` so existing worlds and configuration remain compatible.

[Download the latest release](https://github.com/OpenNerdz/BaseWorks/releases/latest) · [Changes](https://github.com/OpenNerdz/BaseWorks/blob/main/CHANGELOG.md) · [Verification](https://github.com/OpenNerdz/BaseWorks/blob/main/TESTING.md)

## What it does

- **Connected storage:** Search, sort, and transfer items across 8–64 eligible chests through one console. Grouped entries show totals without changing real stack sizes. Deposit all supplies or only those matching items already in storage; reserve carried quantities with **KEEP**.
- **Nearby crafting:** Use accessible supplies from local player storage, vehicles, drones, collectors, workstation outputs, and nearby containers when crafting. Console chest limits do not restrict this feature.
- **Production:** Request a quantity or maintain a stock target. BaseWorks plans required intermediate recipes, reserves ingredients, feeds machines and forges, supplies wood fuel, and collects finished output. Jobs show progress, blocked reasons, completed history, and an approximate ETA.
- **Loadouts:** A linked locker stores four profiles per world for equipment, the toolbelt, and locked backpack slots. A swap commits only when every needed item exists and outgoing items fit in connected storage.
- **Original block art:** Four console tiers, a workshop access panel, and a loadout locker have custom models and icons. The game builds their prefabs from packaged meshes and texture atlases; no Unity asset bundle is needed.

The Storage Console's **PRODUCTION** section manages orders and machines. Existing Workshop Automation Controllers can remain as access panels linked to a console. DeepBore can optionally export mined output to a linked console.

## Install or update

1. Close the game. Back up the world, generated world, and existing `Mods/NearbyCraft` folder together.
2. Extract `BaseWorks-2.0.8-V3.3.zip` into `Mods`. The resulting manifest must be at `Mods/NearbyCraft/ModInfo.xml`.
3. Keep existing `config.json`, `loadouts.json`, `workshops.json`, and their `.bak` files when updating. Remove the obsolete `Localization.txt` from very old installations.
4. Start the game with Easy Anti-Cheat disabled. Try upgrades, production, and loadout swaps in a disposable world before using valuable items.

Do not remove or downgrade the mod while its custom blocks are placed. Restore the matching world and mod state together if you need to roll back. Version 2.0.7 changes the public name and release packaging; internal IDs and save formats are unchanged.

## Storage and production

Place a console near accessible player chests. Press **E** to open Storage, then choose **PRODUCTION** to browse recipes, create a **CRAFT** request, or **KEEP STOCKED**. The manager starts paused with no orders in a new world. Put ingredients and wood in connected chests and install required tools, such as anvils or crucibles, in their workstations.

The production screen has **JOBS**, **WORKSTATIONS**, **COMPLETED**, and **SETTINGS** pages. Up to 24 production entries and 24 machines can be managed by one controller. Earlier entries get the first opportunity in each scheduling pass. **WORKSTATIONS** lets you exclude a machine from feeding and collection without cancelling its native queue.

| Device | Automatic handling |
| --- | --- |
| Workbench and cement mixer | Ingredients, native queue, finished output |
| Forge | Raw materials, smelting, native recipes, wood fuel, output |
| Campfire and chemistry station | Ingredients, required tools, wood fuel, output |
| Dew collector, apiary, chicken coop | Native accepted inputs and finished output |

Collectors follow their native cycles; install bees, chickens, and upgrades yourself. Sky, water, catalyst, and game settings still apply. BaseWorks does not force chunks to stay loaded or simulate production while an area is unloaded. It supports ordinary non-quality ingredients and safe fixed-tier outputs. Quality-bearing weapons, armor, and tools remain manual crafts.

One-time jobs complete after their queued quantity reaches connected storage. Native queues already running are not cancelled when you pause, remove an order, or exclude a machine. An ETA is an estimate based on current timers and machines, not a guarantee of wall-clock completion. If a game crash or partial restore leaves an order out of step with the native queue, inspect the machine and restore a matching world/mod-state backup.

## Console tiers and loadouts

| Console tier | Maximum connected chests |
| --- | ---: |
| 1 | 8 |
| 2 | 16 |
| 3 | 32 |
| 4 | 64 |

The default chest and machine range is 15 blocks, configurable to 1–30. Upgrade kits are crafted at a workbench. Carry the next-tier kit, fully repair the console, close its UI, and use a repair tool's upgrade action. A fully repaired, player-placed console can be picked up inside an active land claim by holding **E**; re-placement rebuilds its connections.

The Loadout Locker is an access point for the nearest eligible console, not another container. It has no storage of its own. A profile covers equipment, the full toolbelt, and only locked backpack slots; unlocked backpack loot stays where it is. The swap validates item metadata, chest access, locks, capacity, and current player inventory before committing.

Storage controls: left-drag takes up to one normal stack from a grouped entry; right-drag takes half; shift-click uses the game's usual destination rules. **DEPOSIT ALL** includes eligible locked backpack supplies but leaves the toolbelt and personal reserves alone. **MATCHING ONLY** deposits only items already represented in unlocked chest slots and skips locked backpack slots. Hold an item on the cursor and choose **KEEP** to reserve that amount of its type.

## Scope and compatibility

- Storage transfers, loadout swaps, and automation are solo/local-world features. On remote servers or when another client joins a hosted game, nearby crafting falls back to vanilla and console transfers are disabled.
- Only loaded chunks are scanned. Machines must be near their manager; connected chests use the console's access rules, range, slot locks, and tier cap.
- Another mod that patches the same crafting or storage behavior, such as Beyond Storage, ProxiCraft, or Craft From Containers, may conflict.
- BaseWorks checks expected V3.3 crafting patch sites on startup. A mismatch disables its nearby crafting patches for that session; inspect the game log after a game update.
- Production order state is stored separately from native world state. Back up both together. DeepBore output linking is optional; miner fueling is not automated.

## Configuration

Edit `Mods/NearbyCraft/config.json` while the game is closed. `Range` and `TerminalRange` default to 15 blocks; `CacheMilliseconds` defaults to 250 ms. `RespectLockedSlots` protects chest locks. `PersonalReserves` maps internal item names to carried quantities, and the in-game **KEEP** control sets them without editing JSON. Production orders and links are in `workshops.json`; profiles are in `loadouts.json`. Each has a previous-revision `.bak`.

The optional Linux/Proton backup helper at `tools/backup_saves.py` is not installed or enabled by the mod. It backs up changed saves, generated worlds, and mods only while the game is closed. See its `--help` output and verify an archive with `python3 tools/backup_saves.py --verify /path/to/archive.zip` before restoring it.

## Build and verify

Building requires the installed V3.3 game assemblies and .NET SDK. The default game path is set in `NearbyCraft.csproj`; override it when needed:

```bash
dotnet build NearbyCraft.csproj -c Release -p:GamePath="/path/to/7 Days To Die"
bash tools/test_fast.sh --full
```

The Release target creates `dist/BaseWorks-2.0.8-V3.3.zip`. The archive contains the stable `NearbyCraft/` mod folder. The test matrix covers planners, persistence, transactions, API signatures, XML/package rules, and backups. Stub and metadata tests do not replace a native gameplay run; see [TESTING.md](https://github.com/OpenNerdz/BaseWorks/blob/main/TESTING.md) for the current status.

Editable Blender art and rebuild instructions are in [design/game_blocks](https://github.com/OpenNerdz/BaseWorks/tree/main/design/game_blocks). Runtime meshes and textures are in `package/`. The repository keeps the authored model source and shipping assets; generated exports and old QA screenshots are left out of the working tree.
