# NearbyCraft block models

The new replacement models are in **[game_blocks](game_blocks/README.md)**.
Open [`game_blocks/NearbyCraft_Blocks.blend`](game_blocks/NearbyCraft_Blocks.blend)
for editable meshes and packed textures, or view the
[family render](game_blocks/previews/family.png). The new models include all four
storage tiers, three LODs each, FBX exports, PBR textures and a Unity bundle project.
The workshop nameplate UVs preserve the lettering's original proportions.

The older concept below is retained for reference. The game bundle for the new
models still needs Unity Editor and native game verification; it is not installed.

## Previous block-family concept

Open `nearbycraft_block_family_concept.blend` in Blender 4.5 or inspect the two PNG
renders. The scene contains separate, named collections for the Storage Console
(Tier 1 and Tier 4 examples), Workshop Controller, and two-block Loadout Locker.
Tier 2/3 would reuse the console shell with their existing green/blue accents.

Design intent: scavenged, field-repaired hardware that belongs in a 7 Days to
Die survivor base, not a pristine sci-fi control room. The machines share
chipped olive paint, rusty cut steel, faded stencils, exposed bolts, patched
plywood and loosely routed cables. Light comes from weak pilot lamps and old
monochrome displays, not glowing accent strips. The storage console has four
upgrade lamps; the higher-tier example adds a visibly retrofitted relay box.
The workshop controller has machine lanes and an override dial. The locker
has mismatched replacement doors, a crude padlock hasp and a tall 1x2-block
silhouette. Floor, plinths, labels and lighting are presentation-only objects.

This is a **Blender design preview**, not an installed game model. No block XML,
save data, or asset bundles have been changed. Model import/scale, collisions,
materials, icons and in-game performance require a separate implementation and
native test after design approval.
