#!/usr/bin/env python3
"""Read-only checks of the mod's XML against an installed V3.2 config tree."""
import copy
import csv
import gzip
import json
import re
import struct
import sys
from pathlib import Path
from lxml import etree

repo = Path(__file__).resolve().parents[1]
game = Path(sys.argv[1]) if len(sys.argv) > 1 else Path.home() / '.local/share/Steam/steamapps/common/7 Days To Die'
merged = {}
checks = 0

def check(condition, message):
    global checks
    assert condition, message
    checks += 1

for patch_path in sorted((repo / 'package/Config').rglob('*.xml')):
    relative = patch_path.relative_to(repo / 'package/Config')
    vanilla_path = game / 'Data/Config' / relative
    root = etree.parse(str(vanilla_path))
    patch = etree.parse(str(patch_path))
    for operation in patch.getroot():
        if not isinstance(operation.tag, str):
            continue
        check(operation.tag == 'append', f'Unsupported patch operation {operation.tag}')
        targets = root.xpath(operation.attrib['xpath'])
        check(bool(targets), f'Zero matches: {relative}: {operation.attrib["xpath"]}')
        for target in targets:
            if isinstance(target, etree._ElementUnicodeResult):
                parent = target.getparent()
                parent.set(target.attrname, str(target) + operation.text.strip())
            else:
                for child in operation:
                    target.append(copy.deepcopy(child))
    merged[str(relative)] = root

blocks = merged['blocks.xml']
items = merged['items.xml']
recipes = merged['recipes.xml']
base = 'nearbyCraftStorageTerminal'
locker = 'nearbyCraftLoadoutLocker'
names = [base] + [base + f'Tier{tier}' for tier in range(2, 5)]
check(len(blocks.xpath(f'/blocks/block[@name="{locker}"]')) == 1, 'Unique loadout locker block')
check(items.xpath(f'/items/item[@name="{locker}"]') == [], 'No loadout locker item/block collision')
check(blocks.xpath(f'/blocks/block[@name="{locker}"]/property[@name="Model"]/@value') == ['NC_LoadoutLocker'], 'Custom tall locker model selected')
check(blocks.xpath(f'/blocks/block[@name="{locker}"]/property[@name="MultiBlockDim"]/@value') == ['1,2,1'], 'Locker occupies a stable 1x2 footprint')
check(blocks.xpath(f'/blocks/block[@name="{locker}"]/property[@class="CompositeFeatures"]/property[@class="TEFeatureStorage"]/property[@name="LootList"]/@value') == ['nearbyCraftTerminalInternal'], 'Locker has no item-bearing internal inventory')
check(not blocks.xpath(f'/blocks/block[@name="{locker}"]/property[@class="UpgradeBlock"]'), 'Loadout locker has no misleading capacity upgrade')
locker_recipe = recipes.xpath(f'/recipes/recipe[@name="{locker}"]')[0]
check(locker_recipe.get('craft_area') == 'workbench', 'Loadout locker requires a workbench')
check([(i.get('name'), int(i.get('count'))) for i in locker_recipe] == [
    ('resourceForgedIron', 12), ('resourceMechanicalParts', 6), ('resourceElectricParts', 4),
    ('resourceSpring', 4), ('resourceDuctTape', 2)], 'Balanced loadout locker recipe')
for ingredient in locker_recipe:
    check(bool(items.xpath(f'/items/item[@name="{ingredient.get("name")}"]')), f'Locker ingredient exists: {ingredient.get("name")}')
for name in names:
    check(len(blocks.xpath(f'/blocks/block[@name="{name}"]')) == 1, f'Unique block {name}')
    check(items.xpath(f'/items/item[@name="{name}"]') == [], f'No item/block name collision: {name}')
for name in [base, names[-1]]:
    delay = blocks.xpath(f'/blocks/block[@name="{name}"]/property[@class="CompositeFeatures"]/property[@class="TEFeaturePickup"]/property[@name="TakeDelay"]/@value')
    check(delay == ['15'], f'Pickup enabled with 15-second delay: {name}')
for name in names[1:3]:
    check(blocks.xpath(f'/blocks/block[@name="{name}"]/property[@name="Extends"]/@value') == [base], f'{name} inherits pickup feature')
for tier in range(2, 5):
    kit = f'nearbyCraftTerminalUpgrade{tier}'
    check(len(items.xpath(f'/items/item[@name="{kit}"]')) == 1, f'Kit exists: {kit}')
    previous = names[tier - 2]
    upgrade = blocks.xpath(f'/blocks/block[@name="{previous}"]/property[@class="UpgradeBlock"]')[0]
    properties = {p.get('name'): p.get('value') for p in upgrade}
    check(properties['ToBlock'] == names[tier - 1] and properties['Item'] == kit, f'Correct tier chain {tier}')
    check(properties['ItemCount'] == '1', f'Exactly one kit per tier {tier}')
    recipe = recipes.xpath(f'/recipes/recipe[@name="{kit}"]')[0]
    check(recipe.get('craft_area') == 'workbench', f'Workbench required: {kit}')
    for ingredient in recipe:
        check(bool(items.xpath(f'/items/item[@name="{ingredient.get("name")}"]')), f'Ingredient exists: {ingredient.get("name")}')
    expected = [20, 10, 20, 20] if tier == 2 else [40, 20, 40, 40] if tier == 3 else [80, 40, 80, 80]
    check([int(i.get('count')) for i in recipe] == expected, f'Upgrade costs tier {tier}')
    allow_lists = items.xpath('/items/item/property/property[@name="Allowed_upgrade_items"]/@value')
    check(len(allow_lists) >= 3 and all(kit in entry.split(',') for entry in allow_lists), f'Repair tools support {kit}')
top = blocks.xpath(f'/blocks/block[@name="{names[-1]}"]')[0]
check(not top.xpath('property[@class="UpgradeBlock"] | property[@name="Extends"]'), 'Top tier cannot inherit another upgrade')
for filename in ['blocks.xml', 'items.xml']:
    for tint in etree.parse(str(repo / 'package/Config' / filename)).xpath('//property[@name="CustomIconTint"]/@value'):
        check(bool(re.fullmatch('[0-9a-fA-F]{6}', tint)), 'Icon tint must be hex')
for prop in ['Class', 'Material', 'Shape', 'MaxDamage']:
    check(top.xpath(f'property[@name="{prop}"]/@value') == blocks.xpath(f'/blocks/block[@name="{base}"]/property[@name="{prop}"]/@value'), f'Top tier retains {prop}')

model_names = {
    base: 'NC_Storage_T1',
    names[1]: 'NC_Storage_T2',
    names[2]: 'NC_Storage_T3',
    names[3]: 'NC_Storage_T4',
    'nearbyCraftWorkshopController': 'NC_Workshop',
    locker: 'NC_LoadoutLocker',
}
for upright_name in (base, names[-1], 'nearbyCraftWorkshopController', locker):
    upright = blocks.xpath(f'/blocks/block[@name="{upright_name}"]')[0]
    check(upright.xpath('property[@name="OnlySimpleRotations"]/@value') == ['true'],
          f'{upright_name} stays upright when placed')
    check(not upright.xpath('property[@name="AllowedRotations"]'),
          f'{upright_name} cannot override upright-only placement')
for inherited_name in names[1:3]:
    inherited = blocks.xpath(f'/blocks/block[@name="{inherited_name}"]')[0]
    check(not inherited.xpath('property[@name="AllowedRotations"] | property[@name="OnlySimpleRotations"]'),
          f'{inherited_name} inherits the upright console rule')
asset_root = repo / 'package/Assets/NearbyCraftBlocks'
art_manifest = json.loads((repo / 'design/game_blocks/exports/manifest.json').read_text())
art_models = {entry['name']: entry for entry in art_manifest['models']}
check(set(art_models) == set(model_names.values()), 'All six authored models are represented')
for block_name, model_name in model_names.items():
    block = blocks.xpath(f'/blocks/block[@name="{block_name}"]')[0]
    check(block.xpath('property[@name="Model"]/@value') == [model_name], f'{block_name} selects its authored mesh')
    check(block.xpath('property[@name="ModelOffset"]/@value') == ['0,0,0'] or block_name in names[1:3],
          f'{block_name} has a floor-center model offset')
    check(block.xpath('property[@name="CustomIcon"]/@value') == [model_name + '_icon'], f'{block_name} selects its matching icon')
    check(block.xpath('property[@name="TintColor"]/@value') == ['ffffff'], f'{block_name} preserves baked texture color')
    check((repo / 'package/UIAtlases/ItemIconAtlas' / (model_name + '_icon.png')).is_file(),
          f'{block_name} icon is packaged')
    with gzip.open(asset_root / (model_name + '.mesh.json.gz'), 'rt') as handle:
        mesh_asset = json.load(handle)
    expected = art_models[model_name]
    check(mesh_asset['format'] == 1 and mesh_asset['name'] == model_name and mesh_asset['family'] == expected['family'],
          f'{model_name} runtime identity')
    check(mesh_asset['collider'] == expected['colliders'][0], f'{model_name} root collision matches Blender')
    check(len(mesh_asset['lods']) == 3, f'{model_name} has three LODs')
    for level, lod in enumerate(mesh_asset['lods']):
        vertices, normals, uv, triangles = (lod[key] for key in ('vertices', 'normals', 'uv', 'triangles'))
        check(len(triangles) // 3 == expected['triangles'][level], f'{model_name} LOD{level} triangle count')
        check(len(vertices) == len(normals) and len(uv) * 3 == len(vertices) * 2,
              f'{model_name} LOD{level} vertex attributes align')
        check(all(0 <= index < len(vertices) // 3 for index in triangles), f'{model_name} LOD{level} indices are valid')
        if level == 0:
            coordinates = (vertices[axis::3] for axis in range(3))
            actual = [(min(values), max(values)) for values in coordinates]
            low, high = expected['bounds_min'], expected['bounds_max']
            target = [(-high[0], -low[0]), (low[2], high[2]), (-high[1], -low[1])]
            check(all(abs(actual[axis][edge] - target[axis][edge]) < .001
                      for axis in range(3) for edge in range(2)),
                  f'{model_name} is metres, floor-pivoted and front-facing in Unity coordinates')
            if model_name == 'NC_Storage_T1':
                check(abs(vertices[0] - .396) < .0001 and triangles[:3] == [0, 2, 1],
                      'Storage mesh uses the non-mirrored Blender-to-game handedness')
for family in ('console', 'workshop', 'locker'):
    for suffix in ('BaseColor', 'Normal', 'MetallicSmoothness', 'Emission'):
        texture = asset_root / f'{family}_{suffix}.png'
        check(texture.is_file(), f'{family} {suffix} texture packaged')
        with texture.open('rb') as handle:
            header = handle.read(24)
        check(header[:8] == b'\x89PNG\r\n\x1a\n' and struct.unpack('>II', header[16:24]) == (1024, 1024),
              f'{family} {suffix} runtime texture is memory-bounded at 1024px')

with (repo / 'package/Config/Localization.csv').open(newline='') as handle:
    rows = list(csv.DictReader(handle))
check(all(None not in row and all(value is not None for value in row.values()) for row in rows), 'Localization CSV column counts')
keys = [row['Key'] for row in rows]
check(len(keys) == len(set(keys)), 'Unique localization keys')
for name in [locker] + names + [f'nearbyCraftTerminalUpgrade{tier}' for tier in range(2, 5)]:
    check(name in keys and name + 'Desc' in keys, f'Localized name/description: {name}')

for relative, root in merged.items():
    if relative.endswith('windows.xml'):
        check(len(root.xpath('/windows/window[@name="nearbyCraftStorageTerminal"]')) == 1, 'Terminal window unique')
        check(len(root.xpath('/windows/window[@name="nearbyCraftLoadoutLocker"]')) == 1, 'Loadout locker window unique')
        for index in range(1, 5):
            check(len(root.xpath(f'//button[@name="nearbyCraftLoadoutSave{index}"]')) == 1, f'Loadout {index} save control')
            check(len(root.xpath(f'//button[@name="nearbyCraftLoadoutApply{index}"]')) == 1, f'Loadout {index} apply control')
        check(len(root.xpath('//button[@name="nearbyCraftTerminalReserve"]')) == 1, 'Reserve control exists')
        for name, caption in [('nearbyCraftTerminalDeposit', 'DEPOSIT ALL'), ('nearbyCraftTerminalDepositMatching', 'MATCHING ONLY'), ('nearbyCraftTerminalReserve', 'KEEP'), ('nearbyCraftTerminalClearReserve', 'CLEAR KEEP')]:
            buttons = root.xpath(f'//button[@name="{name}"]')
            check(len(buttons) == 1 and buttons[0].xpath('label/@text') == [caption], f'Visible labelled control: {caption}')
            check(int(buttons[0].get('width')) >= 100 and int(buttons[0].get('height')) >= 32, f'Usable click target: {caption}')
session_source = (repo / 'src/StorageNetworkSession.cs').read_text()
check('LoadoutLockerManager.IsLocker' in session_source, 'Loadout locker is explicitly excluded from console storage scans')
controller_name = 'nearbyCraftWorkshopController'
controller = blocks.xpath(f'/blocks/block[@name="{controller_name}"]')
check(len(controller) == 1, 'Workshop controller block is unique')
check(not items.xpath(f'/items/item[@name="{controller_name}"]'), 'No workshop item/block ID collision')
check(controller[0].xpath('property[@name="Class"]/@value') == ['CompositeTileEntity'], 'Workshop uses native composite block class')
check(controller[0].xpath('property[@class="CompositeFeatures"]/property[@class="TEFeatureStorage"]/property[@name="LootList"]/@value') == ['nearbyCraftTerminalInternal'], 'Controller inventory is only an access node')
check(controller_name in keys and controller_name + 'Desc' in keys, 'Workshop block is localized')
workshop_recipe = recipes.xpath(f'/recipes/recipe[@name="{controller_name}"]')
check(len(workshop_recipe) == 1 and workshop_recipe[0].get('craft_area') == 'workbench', 'Controller is craftable at a workbench')
for ingredient in workshop_recipe[0]:
    check(bool(items.xpath(f'/items/item[@name="{ingredient.get("name")}"]')), f'Controller ingredient exists: {ingredient.get("name")}')
window = merged['XUi_InGame/windows.xml'].xpath('/windows/window[@name="nearbyCraftWorkshop"]')
check(len(window) == 1, 'Workshop window is unique')
group = merged['XUi_InGame/xui.xml'].xpath('/xui/window_group[@name="nearbycraft_workshop"]')
check(len(group) == 1 and group[0].get('controller') == 'NearbyCraft.XUiC_WorkshopWindowGroup, NearbyCraft', 'Workshop controller is registered')
check(group[0].xpath('window/@name') == ['nearbyCraftWorkshop'], 'Workshop group references its real window')
for name in ['workshopPrevious', 'workshopNext', 'workshopAdd', 'workshopRun', 'workshopLink',
             'workshopStorage', 'workshopOrders', 'workshopMachines', 'workshopSettings', 'workshopFuel', 'workshopHistory', 'workshopQty1',
             'workshopOnce', 'workshopStock', 'workshopClearDone', 'workshopQty10', 'workshopQty100', 'workshopQty1000', 'workshopQtyMax',
             'workshopAutoCraft', 'workshopPagePrevious', 'workshopPageNext'] + [f'workshop{action}{i}' for i in range(6) for action in ['Result', 'Toggle', 'Remove']]:
    check(len(window[0].xpath(f'.//button[@name="{name}"]')) == 1, f'Workshop button exists: {name}')
for name in ['workshopSearch', 'workshopAmount']:
    check(len(window[0].xpath(f'.//textfield[@name="{name}"]')) == 1, f'Workshop input exists: {name}')
for i in range(6):
    for binding in ['name', 'goal', 'detail', 'toggle']:
        check(bool(window[0].xpath(f'.//label[@text="{{workshop_{binding}{i}}}"]')), f'Workshop row binding: {binding}{i}')
check('WorkshopManager.IsController' in session_source, 'Workshop access node excluded from chest network')

# XUiView.pos is Vector2i, not a floating point vector. This catches the exact
# fractional-label regression that only appeared when loading the in-world UI.
for patch_path in sorted((repo / 'package/Config').rglob('*.xml')):
    patch = etree.parse(str(patch_path))
    for element in patch.xpath('//*[@pos]'):
        position = element.get('pos')
        check(bool(re.fullmatch(r'-?\d+,-?\d+', position)),
              f'XUi position must be two integers: {patch_path.name}: {element.get("name", element.tag)} = {position}')

terminal = merged['XUi_InGame/windows.xml'].xpath('/windows/window[@name="nearbyCraftStorageTerminal"]')[0]
check(len(terminal.xpath('.//button[@name="nearbyCraftTerminalProduction"]')) == 1, 'Storage console exposes production from the same block')
check(terminal.get('height') == '700'
      and terminal.xpath('./rect[@name="terminalSections"]/@pos') == ['0,-46']
      and terminal.xpath('./rect[@name="terminalSections"]/button[@name="nearbyCraftTerminalProduction"]/@width') == ['294'],
      'Storage and Production use a discoverable two-section navigation strip')
check(terminal.xpath('.//label[@text="{terminal_production_summary}"]')
      and window[0].xpath('.//button[@name="workshopStorage"]/label/@text') == ['STORAGE'],
      'Navigation exposes live production context and names the return destination')
check(not window[0].xpath('.//rect[@name="productionSearch"]/@visible'), 'Recipe picker remains available beside all views')
check(window[0].xpath('.//rect[@name="workshopOptions"]/@visible') == ['{workshop_settings_visible}'], 'Advanced controls are isolated in Options')
check(not window[0].xpath('.//button[starts-with(@name,"workshopLess") or starts-with(@name,"workshopMore")]'), 'No ambiguous per-row quantity controls')
for names_to_check in [
    ['workshopOrders', 'workshopMachines', 'workshopHistory', 'workshopSettings'],
    ['workshopAmount', 'workshopQty1', 'workshopQty10', 'workshopQty100', 'workshopQty1000', 'workshopQtyMax']
]:
    controls = [window[0].xpath(f'.//*[@name="{name}"]')[0] for name in names_to_check]
    previous_right = -1
    for control in controls:
        left, top = map(int, control.get('pos').split(','))
        right = left + int(control.get('width'))
        check(left > previous_right and right <= 1024, f'No overlapping tab/quantity controls: {control.get("name")}')
        previous_right = right
check(window[0].xpath('.//button[@name="workshopAdd"]/@enabled') == ['{workshop_add_ready}'], 'Invalid or locked requests disable Craft')
check(window[0].xpath('.//button[@name="workshopClearDone"]/@pos') == ['844,-600'], 'Archive control is outside job rows')
check(len(window[0].xpath('./rect/headerbg')) == 2, 'Production uses native game panel headers')
check(window[0].xpath('./rect[@name="productionHeader"]/@width') == ['366'], 'Request flow has a distinct crafting-list pane')
check(window[0].xpath('./rect[@name="workshopHeader"]/@width') == ['666'], 'Status views have a distinct workstation pane')
check(window[0].xpath('.//label[@text="RECIPES"]') and window[0].xpath('.//label[@text="NEW REQUEST"]'),
      'Production separates recipe discovery from creating a request')
check(window[0].xpath('.//button[@name="workshopAdd"]/@defaultcolor') == ['80,80,80,255'], 'Production action uses the vanilla neutral button palette')
check(window[0].xpath('.//button[@name="workshopAdd"]/@sound') == ['[recipe_click]'], 'Production action uses the native recipe sound')
check(bool(window[0].xpath('.//label[@text="{workshop_recipe}"]')), 'Selected recipe requirements are visible without a tooltip')
check(window[0].xpath('.//label[@text="{workshop_recipe}"]/@font_size') == ['17']
      and window[0].xpath('.//label[@text="{workshop_recipe}"]/@max_line_count') == ['2']
      and not window[0].xpath('.//label[@text="{workshop_recipe}"]/@overflow'),
      'Selected recipe requirements use two readable full-size lines')
check(len(window[0].xpath('./sprite[@globalopacitymod="0" and @color="36,36,36,240"]')) == 2,
      'Both production panes have legible dark backplates independent of world brightness')
check(window[0].xpath('.//button[@name="workshopCatalogMode"]/@defaultcolor') == ['{workshop_catalog_color}']
      and window[0].xpath('.//button[@name="workshopCatalogMode"]/@sprite') == ['ui_game_symbol_sort']
      and window[0].xpath('.//label[@name="workshopCatalogLabel"]/@text') == ['ALL RECIPES'],
      'Recipe catalog exposes a compact READY/ALL filter')
check(not window[0].xpath('.//label[contains(@text, "NEARBYCRAFT")]'), 'Production omits mod branding from the in-game screen')
check(not window[0].xpath('.//*[@name="workshopBackdrop"]'), 'Legacy dashboard backdrop was removed')
for name, binding in [('workshopPrevious', 'workshop_previous_ready'), ('workshopNext', 'workshop_next_ready'),
                      ('workshopPagePrevious', 'workshop_page_previous_ready'), ('workshopPageNext', 'workshop_page_next_ready')]:
    check(window[0].xpath(f'.//button[@name="{name}"]/@enabled') == ['{' + binding + '}'], 'End-of-list arrows are disabled')
check(window[0].xpath('.//rect[@name="machineOverview"]/@visible') == ['{workshop_machines_visible}'], 'Machine legend appears on machine page')
for index in range(6):
    for action in ['Remove']:
        check(window[0].xpath(f'.//button[@name="workshop{action}{index}"]/@visible') == ['{workshop_orders_visible}'], 'Order-only controls cannot obscure machine actions')
    check(window[0].xpath(f'.//rect[@name="workshopRow{index}"]/@visible') == [f'{{workshop_row_visible{index}}}'], 'Unused job slots are hidden')
    check(window[0].xpath(f'.//rect[@name="workshopRow{index}"]/@height') == ['74'], 'Job row provides readable three-line hierarchy')
    check(window[0].xpath(f'.//button[@name="workshopResult{index}"]/@visible') == [f'{{workshop_result_visible{index}}}'], 'Empty recipe results are hidden')
    check(bool(window[0].xpath(f'.//label[@text="{{workshop_phase{index}}}"]')), 'Every job has a clear state badge')
    check(window[0].xpath(f'.//label[@name="workshopEta{index}"]/@text') == [f'{{workshop_eta{index}}}']
          and window[0].xpath(f'.//label[@name="workshopEta{index}"]/@tooltip') == [f'{{workshop_eta_detail{index}}}'],
          'Every job exposes its live ETA and calculation assumptions')
    row = window[0].xpath(f'.//rect[@name="workshopRow{index}"]')[0]
    badge = row.xpath(f'./sprite[@name="workshopStateBadge{index}"]')[0]
    eta = row.xpath(f'./label[@name="workshopEta{index}"]')[0]
    action = row.xpath(f'./button[@name="workshopToggle{index}"]')[0]
    top = lambda element: -int(element.get('pos').split(',')[1])
    check(top(badge) + int(badge.get('height')) <= top(eta)
          and top(eta) + int(eta.get('height')) <= top(action)
          and top(action) + int(action.get('height')) <= int(row.get('height')),
          'Job ETA fits between its state badge and buttons without overlap')
    check(window[0].xpath(f'.//sprite[@name="workshopStateBadge{index}"]/@color') == [f'{{workshop_phase_color{index}}}'], 'State badge uses the live phase colour')
    check(window[0].xpath(f'.//label[@name="workshopPhase{index}"]/@font_size') == ['18']
          and window[0].xpath(f'.//label[@name="workshopGoal{index}"]/@font_size') == ['18']
          and window[0].xpath(f'.//label[@name="workshopDetail{index}"]/@font_size') == ['18'],
          'Job state, progress and activity text remain readable')
    check(not window[0].xpath(f'.//label[@name="workshopDetail{index}"]/@overflow')
          and window[0].xpath(f'.//label[@name="workshopDetail{index}"]/@tooltip') == [f'{{workshop_detail_full{index}}}'],
          'Long activity text clips at full size and exposes its full tooltip instead of shrinking')
for name in ['workbench', 'cementMixer', 'forge', 'campfire', 'chemistryStation', 'cntDewCollector', 'cntApiary', 'cntChickenCoop']:
    check(len(blocks.xpath(f'/blocks/block[@name="{name}"]')) == 1, f'Supported machine exists: {name}')
for name in ['resourceWood', 'resourceScrapIron', 'resourceScrapBrass', 'resourceScrapLead', 'resourceCrushedSand', 'resourceRockSmall', 'resourceClayLump',
             'drinkJarEmpty', 'resourceChickenFeed', 'resourceCropChrysanthemumPlant']:
    check(len(items.xpath(f'/items/item[@name="{name}"]')) == 1, f'Automatic machine input exists: {name}')
manifest = etree.parse(str(repo / 'package/ModInfo.xml'))
project = etree.parse(str(repo / 'NearbyCraft.csproj'))
check(manifest.xpath('/xml/Version/@value') == project.xpath('/Project/PropertyGroup/Version/text()'),
      'Release manifest matches project version')
check('and \'$(GameplayQA)\' != \'true\'' in (repo / 'NearbyCraft.csproj').read_text(), 'Gameplay QA builds cannot package a release')
check('and \'$(AssetQA)\' != \'true\'' in (repo / 'NearbyCraft.csproj').read_text(), 'Asset QA builds cannot package a release')
rules_source = (repo / 'src/WorkshopRules.cs').read_text()
scheduler_source = (repo / 'src/WorkshopScheduler.cs').read_text()
transactions_source = (repo / 'src/WorkshopTransactions.cs').read_text()
transfer_source = (repo / 'src/StorageTransferPlan.cs').read_text()
inventory_planner_source = (repo / 'src/WorkshopPlanner.cs').read_text()
check('MaximumNativeBatch = short.MaxValue' in rules_source and 'BatchLimit' not in rules_source,
      'Production uses the native queue multiplier instead of a ten-cycle cap')
check('QueueLargestBatch' in scheduler_source and 'QueueLargestBatch' in transactions_source,
      'Scheduler submits the largest safe transactional batch')
check('int ready = Math.Min(job.Batches, WorkshopMachines.MaterialBatches(station, recipe, false))' in scheduler_source
      and 'job.Batches -= queuedBatches' in scheduler_source and 'next " + job.Count + " still smelting' in scheduler_source,
      'Forges queue ready rolling chunks while retaining their exact unqueued remainder')
check('while (low < high)' in transactions_source and 'for (int amount = share; amount >= 1; amount--)' not in scheduler_source,
      'Large batch contraction uses logarithmic preflights rather than per-item retries')
check('LargestReservableBatch' in scheduler_source and 'FeedLargestForge' in scheduler_source,
      'Storage reservations and forge feeding contract adaptive batches safely')
planning_source = (repo / 'src/WorkshopProductionPlanner.cs').read_text()
reservation_source = (repo / 'src/WorkshopReservations.cs').read_text()
manager_source = (repo / 'src/WorkshopManager.cs').read_text()
check('MAX NOW' in (repo / 'src/WorkshopUi.cs').read_text()
      and 'workshopQtyMax' in etree.tostring(window[0], encoding='unicode'),
      'Request screen exposes maximum-now planning and compact MAX action')
check('Binary search' in planning_source and 'while (low < high)' in planning_source,
      'Maximum craftable amount uses bounded logarithmic planning')
check('WorkshopReservationBank' in reservation_source and 'WithdrawPreserving' in transfer_source
      and 'bank.Keep(' in inventory_planner_source and 'reservations, claim' in transactions_source,
      'Whole-job component claims constrain real native queue withdrawals')
check('raw.type == option.Recipe.itemValueType' in planning_source and 'inputType == recipe.itemValueType' in manager_source,
      'Self-consuming direct and forge recovery recipes are rejected')
check('!output.CanPlaceInContainer()' in manager_source and '!output.CanStack()' not in manager_source
      and 'FixedQualityOutputs' in manager_source and '"toolAnvil"' in manager_source,
      'Non-stackable machines and fixed-tier workstation tools are eligible without admitting variable-quality gear')
check('new ItemValue(batch.Recipe.itemValueType, batch.Quality, batch.Quality)' in transactions_source
      and 'Quality = (byte)Math.Max' in scheduler_source,
      'Native output quality is preserved through capacity reservation and queue submission')
for name, caption in [('nearbyCraftTerminalSort', 'NAME A-Z'), ('nearbyCraftTerminalSortCount', 'MOST ITEMS'), ('nearbyCraftTerminalSortType', 'ITEM TYPE')]:
    button = terminal.xpath(f'.//button[@name="{name}"]')
    check(len(button) == 1 and button[0].xpath('label/@text') == [caption], f'One-click visible quick sort: {caption}')
    check(int(button[0].get('width')) >= 150 and int(button[0].get('height')) >= 32, f'Usable quick-sort hit target: {caption}')
check(terminal.xpath('.//label[@name="resultCount"]/@text') == ['{terminal_result_count} ENTRIES'], 'Header counts grouped entries, not physical stacks')
check('catalog.Add(stack)' in session_source and 'entry.TransferStack()' in session_source, 'Session uses the tested grouped catalog and capped transfers')
check('totals[i] = entry.Count' in session_source, 'Grid receives the full aggregate separately from its physical transfer stack')
print(f'PASS: {checks} XML/localization assertions across {len(merged)} patched vanilla files.')
