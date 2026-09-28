# BaseWorks block art

The editable [BaseWorks_Blocks.blend](BaseWorks_Blocks.blend) contains the six shipped models: four Storage Console tiers, the workshop access panel, and the loadout locker. The source meshes have three levels of detail, packed textures, and floor-centered pivots. [Family preview](previews/family.png).

The game uses the compressed meshes and 1024 px atlases in `package/Assets/NearbyCraftBlocks` and the icons in `package/UIAtlases/ItemIconAtlas`. `NearbyCraft` remains the internal asset namespace for save compatibility.

## Rebuild the shipping assets

Use Blender 4.5, Python 3, Pillow, and NumPy. Run these commands from the repository root in order:

```bash
python3 design/game_blocks/make_textures.py
blender -b -t 8 --python-exit-code 1 --python design/game_blocks/build_models.py
blender -b -t 4 --python-exit-code 1 --python design/game_blocks/verify_models.py
blender -b -t 4 --python-exit-code 1 --python design/game_blocks/export_runtime.py
python3 design/game_blocks/prepare_runtime_textures.py
python3 tools/verify_package.py
```

`build_models.py` regenerates the editable `.blend`, six FBX exports, icons, and the manifest. `verify_models.py` checks FBX round trips and geometry. `export_runtime.py` and `prepare_runtime_textures.py` then update the packaged assets. The repository tracks the editable `.blend`, model manifest, family preview, and shipping assets. Intermediate FBX files, full-resolution atlas PNGs, validation output, and individual renders are generated locally and ignored.

The export reflects Blender X and reverses triangle winding so in-game text is readable while existing block fronts keep their saved direction. The six models use simple colliders and three LODs. Changing internal model names, block IDs, or `NearbyCraft` asset paths would require a save-compatibility migration.
