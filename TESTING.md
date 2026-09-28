# Verification

BaseWorks 2.0.7 changes branding, documentation, and repository contents. The game-facing internal identifiers and gameplay code remain those of NearbyCraft 2.0.6.

## Automated checks

Run from the repository root with the V3.2 b10 game installed:

```bash
bash tools/test_fast.sh --full
dotnet build NearbyCraft.csproj -c Release
unzip -t dist/BaseWorks-2.0.7-V3.2.zip
```

The full script builds the real game-targeted DLL and runs scheduler, machine transaction, persistence, inventory-planner, patch-site, XML/package, and backup checks. It also compiles the opt-in gameplay fixture without packaging it. The Release target packages only the normal DLL and required mod assets under `NearbyCraft/`.

For 2.0.7, the full matrix passed 4,373 scheduler assertions, 45 machine transaction assertions, 200 persistence assertions, 536,906 transfer/rule assertions, 57/57 native API metadata checks, 773 XML/package assertions, and two backup tests. Both normal and opt-in gameplay builds had zero warnings and errors. The normal Release build also had zero warnings and errors; the renamed ZIP passed its integrity and content checks.

## Native gameplay status

The last isolated native gameplay runs predate 2.0.6. The current production payment, ETA, and manual-output changes have not yet been tested inside the game. The opt-in fixture compiles, but its gameplay run should wait until the normal game is closed so it can use a disposable world and dedicated user-data folder. A native run is still needed for Harmony binding, the storage/production UI, model appearance, pickup and upgrades, and save/reload behavior.

Use a disposable world before updating a main save. Back up the world and `Mods/NearbyCraft` together. A crash or partial restore can leave the native machine queue and `workshops.json` order counts out of step.

## Release checklist

1. Confirm `package/ModInfo.xml` displays **BaseWorks** while keeping internal `<Name value="NearbyCraft" />` and version 2.0.7.
2. Run the full automated matrix and a normal Release build with zero warnings and errors.
3. Confirm the ZIP contains `NearbyCraft/ModInfo.xml`, `NearbyCraft/NearbyCraft.dll`, configuration XML, six runtime meshes, texture atlases, and six icons. It must contain no opt-in QA fixture.
4. Test in a disposable solo world when the game is available: open Storage and Production, request and collect output, inspect the ETA and blocked states, use the loadout locker, upgrade and pick up the console, then save and reload.
5. Compare installed mod files and the world backup before trying the update in a main save.

Game-free tests exercise native assembly metadata and stubs. They do not prove UI behavior or real machine transactions in the running game.
