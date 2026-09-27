"""Prepare the supplied Unity project, or stage an addon AFTER its bundle exists.

python3 design/game_blocks/prepare_unity.py
python3 design/game_blocks/prepare_unity.py --stage StandaloneWindows64
No installed mod files or saves are modified.
"""
from pathlib import Path
import argparse
import hashlib
import json
import shutil
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parent
NAME='ZZ_NearbyCraft_Models'
MODELS={
    'nearbyCraftStorageTerminal':'NC_Storage_T1',
    'nearbyCraftStorageTerminalTier2':'NC_Storage_T2',
    'nearbyCraftStorageTerminalTier3':'NC_Storage_T3',
    'nearbyCraftStorageTerminalTier4':'NC_Storage_T4',
    'nearbyCraftWorkshopController':'NC_Workshop',
    'nearbyCraftLoadoutLocker':'NC_LoadoutLocker',
}


def write_xml(path,root):
    ET.indent(root,space='  ')
    ET.ElementTree(root).write(path,encoding='utf-8',xml_declaration=True)


def prepare():
    dest=ROOT/'unity'/'Assets'/'NearbyCraft'
    for name in ['Models','Textures']: (dest/name).mkdir(parents=True,exist_ok=True)
    for model in MODELS.values():shutil.copy2(ROOT/'exports'/f'{model}.fbx',dest/'Models')
    for family in ['console','workshop','locker']:
        for suffix in ['BaseColor','Normal','MetallicSmoothness','Emission']:
            shutil.copy2(ROOT/'textures'/f'{family}_{suffix}.png',dest/'Textures')
    shutil.copy2(ROOT/'exports'/'manifest.json',dest/'manifest.json')
    print(f'Unity project prepared: {ROOT / "unity"}')


def stage(target):
    folder=ROOT/'built'/target
    receipt_path=folder/'build-receipt.json'
    bundle=folder/'nearbycraftblocks.unity3d'
    if not receipt_path.exists() or not bundle.exists():
        raise SystemExit(f'No verified Unity build for {target}. Open the supplied Unity project in 2022.3.62f2 and run NearbyCraft > Build first.')
    receipt=json.loads(receipt_path.read_text())
    if receipt.get('target')!=target or receipt.get('unity_version')!='2022.3.62f2' or receipt.get('prefab_count')!=6:
        raise SystemExit('Build receipt does not match the requested target and six-model set.')
    if not bundle.read_bytes().startswith(b'UnityFS\0'):
        raise SystemExit('The output is not a Unity asset bundle.')
    validation=json.loads((ROOT/'exports'/'validation.json').read_text())
    if validation.get('status')!='passed':raise SystemExit('Run Blender model verification before staging.')
    dest=ROOT/'staged'/NAME
    for name in ['Config','Resources','UIAtlases/ItemIconAtlas']:(dest/name).mkdir(parents=True,exist_ok=True)
    shutil.copy2(bundle,dest/'Resources'/bundle.name)
    mod=ET.Element('xml')
    for key,value in [('Name',NAME),('DisplayName','NearbyCraft custom block models'),('Version','0.1.0'),('Description','Original Blender block family for NearbyCraft; requires NearbyCraft.'),('Author','NearbyCraft')]:
        ET.SubElement(mod,key,{'value':value})
    write_xml(dest/'ModInfo.xml',mod)
    config=ET.Element('configs')
    for block,model in MODELS.items():
        xpath=f"/blocks/block[@name='{block}']"
        values={'Model':f'#@modfolder({NAME}):Resources/{bundle.name}?{model}',
                'ModelOffset':'0,0,0','TintColor':'ffffff',
                'CustomIcon':model+'_icon','CustomIconTint':'ffffff'}
        for key in values:ET.SubElement(config,'remove',{'xpath':xpath+f"/property[@name='{key}']"})
        append=ET.SubElement(config,'append',{'xpath':xpath})
        for key,value in values.items():ET.SubElement(append,'property',{'name':key,'value':value})
        shutil.copy2(ROOT/'exports'/f'{model}_icon.png',dest/'UIAtlases'/'ItemIconAtlas')
    write_xml(dest/'Config'/'blocks.xml',config)
    receipt['bundle_sha256']=hashlib.sha256(bundle.read_bytes()).hexdigest()
    receipt['in_game_validation']='required; not performed by staging'
    (dest/'build-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
    (dest/'README.txt').write_text('Requires the existing NearbyCraft mod. Copy this complete folder alongside NearbyCraft in Mods.\nUse the platform build matching the game executable: Windows for Proton, Linux for native Linux.\nBefore a main save, test all four rotations, E activation, pickup, repair, storage upgrades and both locker heights in a disposable world.\nThis addon changes models and icons only. Removing this visual addon restores the base NearbyCraft appearances. Keep NearbyCraft installed.\n')
    print(f'Staged addon, not installed: {dest}')


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stage',choices=['StandaloneWindows64','StandaloneLinux64'])
    args=parser.parse_args()
    if args.stage:stage(args.stage)
    else:prepare()
