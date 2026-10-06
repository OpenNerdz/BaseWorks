# Changelog

## 2.0.8

- Updated for 7 Days to Die V3.3 b18. Version 2.0.7 does not load on V3.3: the game removed `ITileEntityLootable`.
- Chests, vehicle bags and drone bags now use V3.3's `ItemStackGrid`. Transfers overwrite each grid-bound slot in place, so the game still saves and syncs the change. Vehicle bags are no longer reshaped on commit.
- Moved cursor writes to `SetCurrentStack`, updated the loadout locker to equip per slot with `SetSlotItem`, and moved loadout item serialization to pooled binary streams.
- No gameplay logic, block IDs, config files or save formats changed.

## 2.0.7 — BaseWorks

- Renamed the public project, in-game display name, and release archive to BaseWorks. Kept the `NearbyCraft` installed folder, DLL, block IDs, config files, and save format for compatibility.
- Reorganized the repository around the active runtime mod, editable Blender source, tests, and a concise README. Removed generated art copies, superseded design concepts, old screenshots, and historical review files from the current tree.
- No gameplay logic changed from 2.0.6.

## 2.0.6

- Fixed production ingredient payment for items accepted by native crafting rules but carrying metadata that prevents ordinary stack merging.
- Improved forge lane balancing, manual output accounting, alternate-recipe selection, and conservative job completion estimates.
- Reduced repeated recipe staging and inventory work during scheduler ticks.

## 2.0.2–2.0.5

- Added original models and icons for the console tiers, workshop panel, and loadout locker.
- Corrected upright placement, facing, and mirrored texture details while preserving block and save identities.

## 2.0.0–2.0.1

- Added rolling forge production, safe fixed-tier outputs, recipe discovery, and lower recurring scan cost.

## 1.8.0–1.9.0

- Added whole-job dependency planning, reservation of shared inputs, adaptive native batches, delivery tracking, and completed history.

## 1.4.0–1.7.0

- Added the loadout locker, connected console storage, workshop automation, and DeepBore linked output support.

Earlier releases and detailed historical acceptance notes remain available through the repository history and GitHub releases.
