**ConfigEditUnitPresets**
----
A library mod for [Phantom Brigade (Alpha)](https://braceyourselfgames.com/phantom-brigade/) which enables using the ConfigEdit/ConfigOverride mechanism to modify or add UnitPresets configurations.

It is compatible with game version **0.20.0**. All library mods are fragile and susceptible to breakage whenever a new version is released.

UnitPresets configurations are essentially recipes for how to assemble units (mechs or tanks) from a collection of parts. By tinkering with these presets you can change what gear a unit will be spawned with or even create new varieties of units.

However, the ConfigEdit/ConfigOverride mod system does not permit modifying UnitPresets. This can be easily worked around by adding a slash to the end of the `DataContainerUnitPreset` line in `Configs/paths.yaml` but that means modifying a file in the game installation directory which defeats the point of the ConfigEdit/ConfigOverride mod system.

There's yet another obstacle to modifying UnitPresets. The `ModManager` has a bug in the code that processes the ConfigEdit operations which causes it to incorrectly modify deeply nested data structures. For example, removing one of the part preference tags from a UnitPreset configuration would be specified by a ConfigEdit operation similar to

```
- 'partTagPreferences.body.0.tags.mnf_vhc_03: !-'
```

When the `ModManager` sees this operation, it will remove the `body` key from the `partTagPreferences` object rather than the `mnf_vhc_03` key from the `tags` object as intended, thereby obliterating all tags for body parts on this UnitPreset.

This mod both enables using ConfigEdits/ConfigOverrides on UnitPresets but also fixes the ConfigEdit operation bug.

**Technical Discussion**
----
The `ModManager` is not checking to see if it has reached the end of the field path before applying the add/remove operations. This is most problematic for a remove operation because that can destroy an entire chunk of a data structure which then causes later operations on that chunk to fail. It is less of a problem for an add operation because the code ignores attempts to add things that already exist.

An example of a ConfigEdit that causes trouble is this one to modify the `vhc_tank_generic_far_1` UnitPreset:

```
removed: false
edits:
- 'partTagPreferences.body.0.tags.mnf_vhc_03: !-'
- 'partTagPreferences.body.0.tags.tank_turret_aa: false !+'
- 'partTagPreferences.body.0.tags.tank_turret_cannon: false !+'
```

This will cause the following to be emitted to the `Player.log` when executed by the `ModManager`:

```
Mod 0 (TankVariety) edits config vhc_tank_generic_far_1 of type DataContainerUnitPreset, partTagPreferences.body.0.tags.mnf_vhc_03 | Removing key body (step 1) from target dictionary
Mod 0 (TankVariety) edits config vhc_tank_generic_far_1 of type DataContainerUnitPreset, partTagPreferences.body.0.tags.tank_turret_aa | Adding key body (step 1) to target dictionary
Mod 0 (TankVariety) edits config vhc_tank_generic_far_1 of type DataContainerUnitPreset, partTagPreferences.body.0.tags.tank_turret_aa | Adding new entry of type DataBlockPartTagFilter to end of the list (step 2)
Mod 0 (TankVariety) attempts to edit config vhc_tank_generic_far_1 of type DataContainerUnitPreset, field partTagPreferences.body.0.tags.tank_turret_aa | Can't proceed past tank_turret_aa (step 4), current target reference is null
Mod 0 (TankVariety) attempts to edit config vhc_tank_generic_far_1 of type DataContainerUnitPreset, field partTagPreferences.body.0.tags.tank_turret_cannon | Key body already exists, ignoring the command to add it
Mod 0 (TankVariety) edits config vhc_tank_generic_far_1 of type DataContainerUnitPreset, partTagPreferences.body.0.tags.tank_turret_cannon | Inserting new entry of type DataBlockPartTagFilter to index 0 of the list (step 2)
Mod 0 (TankVariety) attempts to edit config vhc_tank_generic_far_1 of type DataContainerUnitPreset, field partTagPreferences.body.0.tags.tank_turret_cannon | Can't proceed past tank_turret_cannon (step 4), current target reference is null
```

In the first log line, you can see that it is applying the operation to the first child node instead of the terminal one. This is incorrect.

This mod defers applying both add and remove operations until it has reached the end of the field path. This means to add some property to a new chunk of a deeply nested data structure, you will have to add all the intermediate containers explicitly. Say, for example, I wanted to add a second set of tags to the `body` list in the example above. I'd have to write

```
- 'partTagPreferences.body.1: !d'
- 'partTagPreferences.body.1.tags: !d'
- 'partTagPreferences.body.1.tags.mnf_vhc_04: true !+'
```

to build up the second node in the `body` list.

It might be a convenience for an add operation to do this automatically but I prefer things to be explicit. It is clear that I intended to add a new chunk here and this isn't some copy-paste mistake.

This mod does not patch the `ModManager` itself. I lifted out the methods I needed to process the ConfigEdit operations and then hooked the central heartbeat to guarantee the UnitPresets are patched almost immediately after the `ModManager` finishes loading all mods.

It might be possible to patch the `ProcessFieldEdit` method that has the bug because the logic to process ConfigEdits/ConfigOverrides is deferred until the corresponding data linker is first accessed. I chose not to do that. Part of the reason is that I rebuilt the code I lifted out of the disassembly to make it more understandable for me and that means it's less optimized than the existing code. Another part is that I want to limit the damage this mod will do in case it has a bug. This code will only be triggered for UnitPreset configurations and not any of the other types of data containers.
