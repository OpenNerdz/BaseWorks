# Nearby Craft 2.0.6

## Production reliability and job estimates (2.0.6)

- Available components use the game's crafting ingredient rules, including items
  whose metadata prevents ordinary storage stacking. Items with installed mods
  and locked storage slots remain protected. Ready ingredients take priority over
  alternate recipes that would require making more components.
- Forge input balancing also splits existing unstarted stacks across spare lanes,
  preserving each active smelting item and its timer. Existing assignments are
  counted once and resume as rolling batches.
- Taking output through the native workstation UI counts toward the job's
  completion. Partial pickups and automatic collection can be combined; completion
  history records the manually collected quantity. Cancelling a queue does not
  count as collecting its output.
- Each job shows an approximate ETA using remaining native timers, staged crafting
  speeds, parallel machines, shared queues, ingredient production, smelting and
  collection. Waiting/paused jobs show a reason. Estimates assume the area stays
  loaded and automation can run; full-batch dependency estimates are conservative
  because rolling production may finish sooner. Hover for details.
- Recipe staging is reused within each scheduler tick. Forge probes reject material
  shortages before cloning inventories, distribution operates on chunks, and UI
  estimates refresh only on the Jobs page every two seconds with a time budget.

Existing saves and settings are compatible. Restart the game after updating.
Automated and game-assembly checks passed; this update's native UI/Harmony test
fixture still needs an isolated in-game run (see `TESTING.md`).

## Mirrored model details fixed (2.0.5)

The console body was upright, but its keypad, screen and printed labels were
mirrored in-game. The runtime mesh now uses the correct Blender-to-game
handedness and triangle winding, retaining the same front-facing direction and
saved block rotations. The workshop and locker use the same corrected export.

## Existing-block facing restoration (2.0.4)

Restored the custom mesh's original front-facing direction for consoles already
placed in a save. The upright-only placement rule from 2.0.3 remains for new
placements. Neither update rewrites saved block rotations.

## Upright placement rule (2.0.3)

The floor-standing console and
workshop also use upright-only placement, like the locker; their former
`Basic90` rule permitted additional placement rotations. The visual colliders
follow the meshes. Block identities, inventories and saved machine state are
unchanged. This rule did not address the mirrored print shown in the screenshot.

## Custom block models (2.0.2)

The storage console's four tiers, workshop controller and loadout locker now use
the original `design/game_blocks` Blender models and matching icons. As in
DeepBore, the game builds cached prefabs from Blender-exported meshes and PNG
textures at runtime, with no Unity Editor or asset bundle required. Each model
has three LODs, a simple collider and shared 1024px texture atlases. An isolated
main-menu engine smoke test passed all 50 model-hook, material and GPU-render
assertions. Placement, facing, interaction and upgrade visuals still need a
disposable-world check before relying on a main save.

## Frametime maintenance (2.0.1)

The storage terminal still checks nearby chests every two seconds, but now rebuilds its item catalog and redraws the grid only when a chest, slot lock, stack or overflow count actually changes. In-place stack changes are detected, so crafting and transfers remain visible. Workshop planning stages recipes reachable from enabled orders instead of preparing unrelated recipes every tick; an idle controller skips recipe staging altogether. This reduces recurring main-thread work without changing the two-second refresh interval or inventory transaction rules.

Recipe candidate estimates share one inventory count snapshot per scheduling decision, while actual transfers still validate live chest contents. For a low-volume timing breakdown, set `ProfileWorkshopTicks` to `true` in the installed `config.json`; the log then reports average and maximum workshop tick time plus storage, discovery, machine-service and scheduler phases every ten active ticks. Turn it off after profiling.

The Storage Console now has a prominent **Storage / Production** section switch below its title. It shows current job/paused context, and Production has a clearly named **STORAGE** return action. The production layout uses the game's own headers, icons and button treatment with darker readable panels: browse recipes on the left, set a **New Request** below, then track it in **Jobs**, **Workstations**, **Completed** or **Settings** on the right. Requirements separate the machine from its materials; helper text remains readable against bright outdoor scenes.

A local-world quality-of-life mod for **7 Days to Die V3.2 b10**. Craft using nearby supplies, manage storage and production from one console, and exchange activity loadouts. Easy Anti-Cheat must be off. This release adds automation only, not Radio Contracts or other quests.

## Continuous production overhaul (2.0.0)

Forge assignments now run as a pipeline. As soon as enough internal material has smelted for a useful
chunk, that chunk enters the native crafting queue while the remaining raw inputs continue smelting.
The exact unqueued remainder stays reserved and persists across settings reloads, so continuous
production neither waits for an entire large allocation nor duplicates it.

Production now supports safe non-stackable outputs such as Cement Mixers, Forges and Workbenches.
It also supports the vanilla fixed-tier forge tools Anvil, Cooking Pot and Cooking Grill while
preserving their native tier in queue, capacity and storage transactions. Variable-quality weapons,
armour and tools remain in the normal player crafting interface because automation must not guess a
quality.

The recipe browser opens on a compact **READY** view containing unlocked recipes for enabled nearby
machines. **ALL** exposes the complete supported catalog, and typing a search always searches that full
catalog so missing-machine and locked recipes remain discoverable. The request action now states when
a compatible workstation is missing instead of creating a silently blocked order. Recipe requirements
use two full-size lines, machine rows distinguish active crafting from the remaining smelting work, and
job status explains that rolling batches start as soon as materials are ready.

**Acceptance status:** 2.0.0 passed the complete standalone matrix and a 157-assertion isolated V3.2
engine run. The native fixture verified real partial-Cement rolling, exact remainder persistence,
Cement Mixer and Anvil catalog eligibility, variable-quality equipment exclusion, full production,
stock mode, history, console upgrades and save/unload/reload recovery. All production views were
captured at 1600x1000; the normal save was never opened. See TESTING.md for the evidence matrix.

## Whole-job production planning (1.9.0)

Production now builds a complete recursive bill of materials before assigning machines. The request
panel reports **MAX NOW** and the actual missing base component; its compact **MAX** shortcut selects
the largest currently feasible request. Requests above that amount remain valid: automation makes the
feasible portion and clearly waits for the shortage rather than silently stalling.

Stored components are logically reserved for their planned step and earlier job before transactions
commit. Shared inputs therefore cannot be consumed by one dependency when another dependency or the
final recipe needs them. Reservations are recalculated from real storage, pending native output and
forge material every scheduling pass; live inventories remain authoritative and every withdrawal still
uses the existing atomic snapshot validation.

The planner tries viable recipe alternatives, maximizes producible output first, and then chooses the
path with the shortest estimated completion time using available compatible machines. It rejects
dependency cycles and self-consuming/zero-gain recovery recipes. In particular, the vanilla forge
recipe that converts four glass units back to one Crushed Sand can no longer feed Crushed Sand into a
forge to manufacture itself; automated Sand production uses the productive cement-mixer recipe.

## Capacity-aware production batches (1.8.3)

Production no longer limits every machine to ten recipe cycles. Each free workstation now receives
the largest native batch that safely fits the outstanding request, available ingredients, workstation
output and connected storage. Faster compatible machines still receive proportionally more work.

Large-batch selection uses bounded binary preflights rather than one trial per item. Payment, fuel,
output reservations and queue placement still commit atomically, and occupied native queues remain
untouched. Requests beyond the game's 32,767-cycle native multiplier continue automatically in the
next capacity-aware batch.

## Native production screen and truthful queue state (1.8.2)

Production keeps every existing control but now follows the compact vanilla crafting layout: unbranded
game headers, a normal recipe toolbar, visible recipe requirements, neutral native buttons and a single
Queue/Workstations/History/Settings strip. Decorative onboarding chrome and unused spacing were removed.

An occupied mixer no longer makes an active order look stalled. The job row reports how many products
are currently crafting or awaiting collection, then gives the real condition for the next batch.

## Workbench craft-button fix (1.8.1)

Nearby ingredient totals and the Craft button now use the same native item-matching rule. Ordinary
components with harmless internal value differences no longer appear available while leaving Craft
disabled. Installed modifications remain protected, and exact matching for storage transfers and
loadouts is unchanged.

## Faster assignments, completed history (1.8.0)

**Search → click an item → choose a quantity → CRAFT.** That click starts automation; there is no
separate RUN step. Select **KEEP STOCKED** instead to maintain a network total. Recipe results are
clickable, show item icons and machine requirements, and put unlocked recipes with matching
connected machines first. Quantity shortcuts offer 1, 10, 100, 1000 and MAX; typing an exact amount works too.

The request panel stays on the left. **JOBS** on the right shows only real jobs, with plain-language
state badges: preparing, smelting, crafting, waiting, needs supplies, done or in stock. Each row shows
delivered/requested quantities and the amount still to queue, or the stored/desired stock count.
**COMPLETED** keeps the latest 60 finished one-time requests, with quantities and local completion
times. **Repeat** fills the request form; press Craft to confirm. New completed requests automatically
leave the active Jobs view. **Archive finished** moves older, untracked finished requests into history
with a legacy label. Stock targets stay active rather than filling history with recurring top-ups.
Removing an active request needs a second confirmation click. **MACHINES** shows assignments and status.
**OPTIONS** holds automatic fuel, intermediate crafting and optional controller linking; these
normally need no changes. Required recipe ingredients/tools are available on the selected item's tooltip.

The scheduler estimates native completion time using the recipe, player effects, installed machine
tools, existing smelted material and each forge input's current timer. Faster machines can receive
larger batches; small requests prefer the quickest available compatible machine. Available recipe
alternatives are compared by time per output. Matching/preloaded material breaks timing ties.
New raw supply is balanced across the available smelting slots, including two- and three-slot
layouts. Every missing material gets room first, so iron cannot crowd out required clay. Existing
stacks and their in-progress smelting timers are never rearranged. Active jobs are planned in visible
order, so an earlier request reserves scarce shared components before later requests.
Missing intermediate ingredients can prepare concurrently on different kinds of machine.

Each forge's planned product and quantity are remembered in `workshops.json` while it smelts and
count toward demand/output-space reservations. Repeated scans and ordinary settings reloads do not
forget those allocations. All required raw ingredients for a chosen batch must be obtainable before
any are fed; if only a smaller batch fits, the scheduler tries that. Supplying missing resources lets
work resume without reissuing the request. Removing/excluding a forge releases its assignment so
remaining work can use surviving machines; material in the removed/excluded machine is not teleported.

This is a bounded estimated-finish-time scheduler, not a global time/cost optimizer. It does not
search every possible dependency chain, choose arbitrary valuable items as smelting feed, move
smelted units between forges, interrupt native queues or assign permanent material-only forge roles.
It uses native speeds rather than accelerating timers. Existing tool upgrades must be installed manually.
Native inventory, queue and world-save limitations below still apply.

**Acceptance status:** 1.9.0 passed its complete standalone matrix and a 126-assertion isolated V3.2
engine run. The native fixture verified maximum/missing-material preview, the MAX action, shared
component reservations, rejection of the circular forge Sand recipe, real forge/mixer operation,
save/reload recovery and a capacity-aware 120-cycle mixer batch. The normal save was never opened.
Full evidence and remaining live coverage are documented in TESTING.md.
Native checks cover installed-tool timing, parallel forge inputs, exact output collection,
Completed/Repeat, stock replenishment and world save/unload/reload with smelting underway.
See TESTING.md for evidence, visual checks and remaining manual coverage.

## Ore-export efficiency (1.5.4)

Empty, fuel-only and reserved-output sources now exit before allocating a storage session.
Ore-export scans discover eligible containers without building/localizing/sorting the display
catalog. Ordinary console UI and workshop sessions retain their existing behavior; transactional
snapshots, access checks, chest caps and slot locks are unchanged. Pair with DeepBore 1.2.0 for
its reusable production buffers and open-hopper mining. The link range remains 200 blocks.

## Deep Bore output integration (new in 1.5.2)

Optional **DeepBore 1.1.1** miners can export ore into a Storage Console's connected boxes:
hold E → Link on the miner, then hold E → Link on your console within two minutes and 200 blocks.
Version 1.5.3 extends the miner-to-console link range; the console's chest-scanning range is unchanged.
The console's normal range, tier cap, ownership/access checks and configured slot-lock policy apply.
The source miner is excluded from export destinations; its fuel and locked output slots stay put.
Transfers wait for busy storage/UI, full destinations or unloaded chunks. There is no remote
refueling, forced chunk loading or change to ordinary console behavior. No configuration migration
is needed; preserve config.json, loadouts.json and workshops.json when updating.

## Quick sort and grouped storage (new in 1.5.1)

The Storage Console now shows **one entry per matching stackable supply**, with the total across connected, accessible, unlocked chest slots. For example, twenty stacks of the same ammunition appear as one ammo entry with the combined count. This is a display change: it does not move items between chests or increase real stack limits.

- **NAME A-Z:** one-click alphabetical sorting.
- **MOST ITEMS:** largest network totals first, not the size of a single physical stack.
- **ITEM TYPE:** sort by native item type ID, keeping variants of an item together.
- Search works alongside every sort mode. The active sort button is highlighted; the preference is saved, and selecting a sort returns to the first row. Put down any cursor stack before sorting.
- Left-click/drag or Shift-click takes **up to one normal stack**. Right-click takes half of that normal-sized stack (minimum one). Repeat to take more. Deposits continue using ordinary chest stack limits.
- Hover to see the exact total. Large totals have compact `k`/`M`/`B` labels; counts stay separate from physical transfer quantities.
- Equipment and non-stackable items remain individual. Different quality, durability, modifications, ammunition, metadata, item flags and painted-block variants are not flattened into ordinary supplies. Locked or inaccessible items are not included.

Also fixes the Workshop Controller's 24 fractional button-label coordinates that V3.2 rejected when loading the in-world UI. Existing controllers do not need replacing; fully restart the game to load the corrected XML.

## Unified storage and production

A **Storage Console is now also the machine manager**. No second block is required. Place one near
your chests and machines, press **E**, then use the **PRODUCTION** section beside Storage. **JOBS**, **WORKSTATIONS**, **COMPLETED** and **SETTINGS**
share the recipe picker; **STORAGE** returns to the connected storage view. Existing Workshop Automation Controllers remain
optional production access panels linked to a console; opening production from a console reuses
an existing linked controller when there is one.

1. Put ingredients and wood in the console's connected chests. Install workstation tools such as
   cooking pots, anvils and crucibles in the actual machines.
2. Open PRODUCTION. A new manager links to its own console and starts **paused with no orders**.
   Existing controllers keep their old stock targets.
3. Search a product and click its row. Its tooltip shows the station, base ingredients and required
   tool; native modifiers apply when work is queued. Locked results cannot be requested.
4. **CRAFT** requests that many *new* items, regardless of existing stock. Whole recipe
   yields can round the last batch up. Queuing the same product again adds to its remaining amount.
   **KEEP STOCKED** maintains that many items across connected storage and pending output.
   An item has one entry/mode at a time. Requesting it in the other mode replaces its unfinished
   plan; already queued native work still finishes. CRAFT always requests additional new production.
5. Requesting an item starts automation. The manager withdraws real inputs, queues native timed
   work, supplies fuel and collects products. The default **Craft missing ingredients** option also
   crafts missing intermediate ingredients,
   with cycle detection and an eight-level dependency limit. For example, stone can become sand
   and forge cement before being combined into concrete in a mixer.
6. Use **WORKSTATIONS** to inspect assignments, queues, fuel, positions and blocked states. Enable/Disable
   includes/excludes a machine from feeding, fuel and collection. Exclusion does not cancel native work.

There are **24 production entries and 24 machines per manager**, shown six per page. Earlier
entries get the first opportunity in each scheduling pass. Each native batch expands to the largest
safe amount and contracts when ingredients or output space limit production. KEEP STOCK never exceeds its stock goal, so
a deficit smaller than one full recipe yield waits. A one-time job's "to queue" count reaches zero
when its batches have been submitted. New one-time jobs enter Completed only after their recorded
queued quantity has actually been collected into connected storage and matching machine work has
cleared. Missing/excluded machines or cancelled native queues can leave **CHECK OUTPUT** instead
of a false completion. Receipts account for matching products returned by this network; they are
not provenance tracking for individual stacks. Existing 1.7.0 requests have no retroactive delivery
ledger and can be archived explicitly after their matching native work clears.

### Machines and inputs

| Device | Automatic handling |
| --- | --- |
| Workbench | Recipe inputs, native crafting queue, finished output; includes applicable hand recipes |
| Cement mixer | Sand/concrete recipes, inputs and finished output |
| Forge | Raw-material feeding, native smelting, spending actual smelted units, fuel, crafting and output |
| Campfire / chemistry station | Recipe inputs, required-tool checks, fuel, ignition and output |
| Dew collector | Empty jars and completed water |
| Apiary | Native accepted flowers and completed honey |
| Chicken coop | Chicken feed and completed eggs, feathers and chickens |

Collectors follow their native fixed production cycle while RUN is enabled; they do not have
recipe queues or per-product order targets. Install their bees/chickens and upgrades manually.
Sky, water, catalyst and game-settings requirements still apply. AUTO FUEL also controls collector
input replenishment. Finished output is collected even when this option is off.

**AUTO FUEL ON** draws only wood for combustion workstations. Fuel already placed in a station can
also burn normally. The manager loads at most about two minutes of fuel at a time (rounded to
whole wood items), relights machines with pending work, and switches off fires once both crafting
and smelting are idle. It does not burn tools, furniture, coal reserves or arbitrary inventory items.
With AUTO FUEL OFF, fuel/light combustion machines yourself.

Forges receive only ordinary iron, brass, lead, sand, small stone and clay from storage. Existing
smelted units and input currently smelting count toward demand, preventing repeated feed.
The manager never converts raw items directly into smelted units or bypasses smelting time.
With the game's smelter-disabled setting, native replacement recipes use their ordinary inputs.
Special modded smelting materials need manual supply.

### Scope and behavior

- Solo/local worlds only, with NearbyCraft enabled and the player alive. Loaded areas only; no
  forced chunk loading or separate offline simulation.
- Machines must be within `TerminalRange` of the manager (15 blocks by default, maximum 30).
  Chests use the linked console's range, access rules, slot locks and 8/16/32/64 tier cap.
- Inputs come from connected storage, never the backpack. Completed output from manually queued
  work is also collected. Existing workstation queues are allowed to finish before new work starts.
- Automation waits while connected storage is in use, a storage/loadout screen is open, or an item
  is held on the cursor. A machine in use is left alone while independent free machines continue.
  The production screen can stay open while it runs.
- Payment, fuel movement and native queue placement validate snapshots before committing.
  Full storage blocks new batches; output that later cannot fit stays in the machine.
- Only one running manager may claim a console/chest network, and each device is serviced by
  one manager per update. Conflict messages explain overlapping networks.
- PAUSE, removing an order or excluding a device never cancels paid-for native queues.
  Cancel those through the device itself. A cancelled MAKE ONCE batch is not automatically reissued;
  queue a replacement order if wanted.
- Settings, remaining one-time quantities, smelting assignments, links and exclusions are saved per world in
  `workshops.json` with a previous-revision `.bak`. Existing format-1 settings migrate with defaults.
  Failed progress writes stop automation for the session without rolling back a committed batch.
- The game saves world/chest/queue state separately from this settings file. **A crash or restoring
  only one side can leave a one-time order's remaining quantity out of step.** After a crash, inspect
  native queues and remaining quantities; restore matching world and mod-state backups together.
  This is not a cross-file crash-atomic crafting system.
- Console upgrades preserve orders. Pickup/destruction forgets that manager after the world update;
  re-placement starts fresh. Corrupt settings disable automation without overwriting the file.

Recipes must have stackable non-quality outputs and ordinary non-quality ingredients.
Quality-bearing weapons/armor/tools are not automated. Compatible native workstation subclasses
are discovered, but bespoke mod machinery, generators/electricity wiring and arbitrary mod devices
are not universal integrations. DeepBore retains its existing linked output export; this update
does not refuel its miners.

### Block appearance

The console family, workshop controller and loadout locker use the original scavenged-industrial
models in `design/game_blocks`: chipped paint, field repairs, dim displays and matching icons.
All four console tiers have distinct models. A narrowly scoped runtime loader follows the proven
DeepBore pattern: compressed Blender mesh data and shared 1024px texture atlases become cached
Unity prefabs without needing the Unity Editor or an asset bundle. If an asset fails to load,
the affected block falls back to its previous vanilla visual; inventory and machine data are
unchanged. The production interface uses the game's native headers, grey panels, black borders, fonts,
button states and green primary action. Its two-panel layout follows the base crafting and
workstation screens: find an item and set the request on the left, then monitor jobs and machines
on the right. Each job uses a large item name, outlined state badge, delivery/stock goal and a
full-size plain-language activity line; the longer diagnostic remains available as a tooltip.
Status colors remain limited to state badges. The 3D models are original Blender assets; no Unity
asset bundle is required. Verify this model update in a disposable world before using a main save.

## Storage consoles and upgrades

The original console is now **Tier 1**. Existing chests and their contents are not moved or deleted. If a console has more chests nearby than its tier permits, the nearest eligible chests connect first; equal distances use stable position ordering. Extra chests remain accessible normally and are reported as out of capacity.

| Build / upgrade | Chest limit | Forged steel | Mechanical parts | Electrical parts | Scrap polymers |
| --- | ---: | ---: | ---: | ---: | ---: |
| Craft Tier 1 console | 8 | 20 | 10 | 10 | 20 |
| Tier 1 → 2 kit | 16 | 20 | 10 | 20 | 20 |
| Tier 2 → 3 kit | 32 | 40 | 20 | 40 | 40 |
| Tier 3 → 4 kit | 64 | 80 | 40 | 80 | 80 |

All recipes are available at the workbench. **Carry one kit**, close the console, fully repair it, then use the repair/upgrade action of a stone axe, claw hammer or nailgun. The game's normal upgrade action consumes one kit. Upgrade sequentially; no Tier 5 exists. Kits cannot be sold to traders.

To move a player-placed console, fully repair it inside your active land-claim area, hold **E**, and choose **Take**. Pickup takes 15 seconds and returns the same console tier; placing it again rebuilds its nearby chest connections at the new location.

Costs double with each capacity step. Higher tiers consolidate more chests into one interface; they do not grant larger chests, extra loot, extended crafting reach or a scan of distant chunks. Building several cheaper Tier 1 consoles remains a valid alternative, but their inventories are separate views.

All tiers have the same **15-block default range**, configurable between 1 and 30. A fully upgraded console costs 160 steel, 80 mechanical parts, 150 electrical parts and 160 polymers in total, including its original construction. Steel and workbench requirements keep this a mid-game convenience without requiring rare end-game loot.

## Storage Network Loadout Locker

The new tall metal locker is an **interface to the nearest accessible Storage Console**, not another container. It has no usable inventory and is explicitly excluded from console chest scans. Place it within the configured `TerminalRange` of a console; it then uses that console's nearest-chest selection, tier cap, range, ownership, container locks and slot locks.

It saves four per-world profiles containing all 12 equipment/clothing/badge slots, the full toolbelt, and only the backpack slots you have locked. Unlocked backpack loot is preserved exactly. A profile is a template: saving never moves or duplicates items. Applying it reuses suitable items already in any managed player slot, withdraws only the missing items, and deposits only the outgoing surplus.

Each exchange is planned against cloned chest inventories. It succeeds only if the whole requested loadout exists and every outgoing item fits in unlocked connected storage. A missing item, full network, changed chest, changed player inventory, incompatible slot or inaccessible lock rejects the operation before chest counts commit. Existing profile overwrites require a second click within five seconds. Profiles are stored in `loadouts.json`, separated by world and save name, with exact item quality, durability, modifications, ammunition and other metadata plus an item-name check to reject stale IDs safely.

The locker is crafted at a workbench in 45 seconds from **12 forged iron, 6 mechanical parts, 4 electrical parts, 4 springs and 2 duct tape**. This is intentionally cheaper than a Storage Console because the locker cannot function without one and adds no storage capacity. It has no upgrade tiers: upgrading the linked console already increases the network available to the locker from 8 to 64 chests. Inside an active land claim, a fully repaired locker can be picked up after 15 seconds.

## Controls and new supply features

- Left-drag takes up to one normal stack from a grouped entry. Right-drag takes half of that amount (one item for a single-item stack). Shift-click moves up to one normal stack using the normal player inventory rules.
- Dragged items stay attached to the cursor while crossing other cells. Full/partial transfers use the amount actually transferred, and unlike-item swaps are all-or-nothing.
- Search by localized or internal name; quick-sort by name, total count or type with the three labelled buttons. Sorting and search auto-focus preferences are saved.
- **DEPOSIT ALL** (green button below the chest grid): left-click to deposit eligible backpack supplies into connected chests, **including supplies in locked backpack slots**. Your toolbelt and personal reserves stay untouched. The slot lock settings themselves are not changed; chest access/slot locks still apply.
- **MATCHING ONLY:** left-click to deposit only supplies already present in an unlocked connected chest slot. This mode still skips locked backpack slots. It fills matching stacks before empty slots. Ordinary stackable supplies match even when their hidden creation seeds differ.
- **KEEP:** carry a stack on the cursor and left-click KEEP to reserve that amount of its item type. With the same item held, left-click **CLEAR KEEP** to clear its reserve (right-clicking KEEP also works). The held stack is not consumed. Return it to your backpack afterward.
- Each deposit reports its moved item count in the footer for six seconds and in the game log. If nothing moves, the footer directs you to check space, locks and reserves.
- Reserves are totals across eligible backpack slots, not per-stack limits. DEPOSIT ALL counts both locked and unlocked backpack slots toward the reserve; MATCHING ONLY counts unlocked slots. Neither includes the toolbelt. Manual drag/shift-click transfers and crafting remain intentional actions and do not apply bulk-deposit reserves.
- Refresh manually with the computer icon, or let the console refresh every two seconds while idle. Automatic refresh pauses during a mouse gesture or while carrying a stack.
- Closing the console returns a carried stack using the game's normal inventory handling; if there is no room, vanilla may drop it at your feet.
- Move an item to your backpack before using it. Use actions are disabled on the console's virtual cells so food, drink, medicine, books and bundles always update a real inventory slot.

The console excludes land-claim maintenance storage, all console internal inventories, inaccessible locked containers, and containers in use. It closes if its block disappears, the player moves more than eight blocks away, or a remote player joins.

Nearby crafting still includes accessible player storage, workstation outputs, collectors, vehicles and drones within its own range. Console chest caps do **not** limit nearby crafting.

## Safety and compatibility

**Storage operations are solo/local-world only in this release.** The old client-side multiplayer transfers lacked server-authoritative coordination. On a remote server, or while another client is connected to your hosted game, nearby crafting falls back to vanilla and the console cannot transfer items. Installing the mod on every client does not make it multiplayer-safe.

Terminal operations plan on cloned inventories, validate the source contents and slot locks, then commit only changed slots. Nearby crafting stages player/workstation ingredients and connected storage as one payment; loadout and bulk-deposit player changes are applied with rollback before storage commits. Bulk deposit marks each affected chest once. Item metadata is retained rather than merging different variants. This is a synchronous local-world transaction, not a network transaction protocol.

Loadout swaps also refuse to run while an item is held on the cursor or the held tool/weapon is performing an action. Do not remove the mod or downgrade a save while its custom blocks are placed. As with any inventory mod, test the loadout workflow in a disposable world before trusting it with valuable equipment.

The mod checks the expected V3.2 crafting patch sites at startup. A mismatch removes all of this mod's Harmony patches and disables nearby crafting for that session. Review the game log before using it after a game update.

Do not combine with Beyond Storage, ProxiCraft, Craft From Containers or another mod that changes the same crafting/storage behavior. Remote repair and generator/miner refueling are not included. Workshop automation uses the native machine APIs described above.

## Installation and updating

1. Close the game and back up saves, generated worlds and the old mod.
2. Extract `NearbyCraft-2.0.6-V3.2.zip` into `Mods` so the manifest is `Mods/NearbyCraft/ModInfo.xml`.
3. Preserve your existing `config.json`, `loadouts.json`, `workshops.json` and their backups when updating. Missing settings use defaults.
4. Start the game with Easy Anti-Cheat disabled.
5. Test automation and the existing controls in a disposable world before using valuable supplies. Never remove/downgrade the mod with its custom blocks still placed; restore the matching pre-update world and mod together.

The terminal's original block name is retained, so existing consoles do not require replacement. The localization file is now correctly named `Localization.csv`; remove the obsolete `Localization.txt` when replacing an old installation.

## Configuration

Edit `Mods/NearbyCraft/config.json` while the game is closed.

- `Range` and `TerminalRange`: 1–30 blocks; default 15.
- `CacheMilliseconds`: 100–2000 ms; default 250.
- `RespectLockedSlots`: protects chest slot locks when enabled (default true). Independently, MATCHING ONLY protects backpack locks, while DEPOSIT ALL includes their contents.
- `PersonalReserves`: internal item names mapped to quantities, e.g. `{"ammo9mmBulletBall": 150}`. The KEEP control sets these without editing JSON.
- Existing source toggles, sorting and autofocus preferences are preserved.
- Loadout profiles are kept separately in `loadouts.json`; its `.bak` file is the previous saved revision. Keep both files when updating or moving the mod.
- Production orders, machine options and controller links are kept in `workshops.json`, with a previous-revision `.bak`. Use the in-game controller to edit them. `Enabled: false` in the main config disables automation as well as nearby crafting.

UI preference changes use a temporary file and retain the previous configuration as `config.json.bak`. Nearby crafting scans only loaded chunks when needed; ingredient removal reuses a batch snapshot instead of rescanning for every ingredient.

## Optional automatic backups on Linux/Proton

The companion `tools/backup_saves.py` runs independently of the game mod. Installing the mod does **not** install or enable a backup timer. If separately configured, the user timer can check every five minutes while logged in and back up changed data **only while the game is closed**. It includes Saves, GeneratedWorlds and Mods, verifies SHA-256 hashes, and keeps the configured archive count (default eight). It does not copy running saves or restore anything automatically.

- Configuration: `~/.config/NearbyCraft/backup.json`
- Backups: `~/.local/share/NearbyCraft/backups/`
- Status: `systemctl --user status nearbycraft-backup.timer`
- Run now: `systemctl --user start nearbycraft-backup.service`
- Disable: `systemctl --user disable --now nearbycraft-backup.timer`

Verify an archive with `python3 tools/backup_saves.py --verify /path/to/archive.zip`. For recovery, close the game, verify the archive, extract it to a separate folder, then copy the required save/world and matching mod files back to the paths recorded in `manifest.json`. Keep the current files separately first. An archive's `data/` folder corresponds to the configured game user-data root.

These are local backups, not protection from drive failure. Check that your configuration points to the currently used Proton/native save folder; update it if you switch platforms or move the game data.

## Build and verification

```bash
dotnet build -c Release -p:GamePath="/path/to/7 Days To Die"
dotnet run --project tests/TransferTests.csproj -c Release
dotnet run --project tests/MachineTransactions.csproj -c Release
dotnet run --project tests/SchedulerTests.csproj -c Release
dotnet run --project tests/WorkshopStoreTests.csproj -p:GamePath="/path/to/7 Days To Die"
dotnet run --project tests/PatchSites.csproj -- "/path/to/7 Days To Die"
python3 tools/verify_package.py "/path/to/7 Days To Die"
python3 -m unittest discover -s tests -p 'test_*.py' -v
```

The XML checker uses Python's lxml package and verifies integer XUi positions, including the workshop-label regression. Planner tests compile the production transfer/rules/catalog code against minimal item stubs, including the original 3,000 randomized scenarios, 2,000 workshop target/collection scenarios and 1,000 grouped-display/withdrawal scenarios. Catalog tests cover seed-aware grouping, metadata separation, read-only sorting/search, 64-bit totals and native-sized transfer limits. Patch-site tests inspect the installed game DLL without executing it. Backup tests include restore-to-temp, retention and refusal during gameplay. On a machine with only newer .NET runtimes, prefix test commands with `DOTNET_ROLL_FORWARD=Major`.

**Planner/stub tests do not replace an in-game UI/playtest.** The mouse drag/drop experience, visual layout and native upgrade animation still need live confirmation. See `TESTING.md` for the short acceptance checklist.

### 1.3.1 deposit hotfix

V3.2 routes left-click through `OnPress` with ID -1 and right-click through `OnRightPress` with ID -2. Version 1.3.0 subscribed only to the left-click event and incorrectly tested for mouse button 1, so its advertised right-click deposit-all action never ran. It also compared hidden item creation seeds when matching ordinary stackable supplies. The labelled left-click buttons and seed-aware supply matching fix those defects; snapshot validation still compares exact values to reject stale transfers. Regression tests cover both mistakes.

### 1.3.2 Deposit All backpack-lock behavior

DEPOSIT ALL now includes locked backpack slots as requested. MATCHING ONLY retains its lock protection. Personal reserves, toolbelt contents, chest access and chest slot-lock handling are unchanged. Regression tests check both modes, reserve accounting and item conservation.

### 1.3.3 terminal use-action fix

Use actions are disabled on the console's virtual item cells. Move food, drink, medicine, books or bundles to the backpack before using them. This prevents vanilla from applying an effect while decrementing only the terminal's read-only display instead of the real chest stack.

### 1.4.0 Storage Network Loadout Locker

Adds four loss-safe equipment, toolbelt and locked-backpack supply profiles through a dedicated tall locker access node. The locker holds no gear and cannot become an extra network chest. Whole swaps share the nearest Storage Console's actual tier-limited chest set and fail without committing when requested items or destination space are unavailable.

Research referenced the MIT-licensed CraftFromContainers and ProxiCraft projects and Apache-2.0-licensed Beyond Storage projects; this implementation is purpose-built.

### 1.5.0 Workshop Automation

Adds six configurable stock targets, native workstation queueing, ingredient withdrawal and finished-output collection through a linked Storage Console. Includes full-storage, pending-output, overlapping-network and busy-inventory guards. No quest content is included.

### 1.5.1 Grouped console and UI correction

Fixes all 24 invalid fractional workshop label positions. Adds three explicit quick-sort buttons, grouped supply totals and exact-total tooltips without increasing real item stack sizes. Equipment and differing item metadata remain separate. The grouping/filtering code is shared with new production-code tests.
