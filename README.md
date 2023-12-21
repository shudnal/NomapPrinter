![](https://staticdelivery.nexusmods.com/mods/3667/images/2505/2505-1693921543-26561571.png)

# Nomap Printer
In nomap mode reading the Cartography Table will generate a simplified map and save it to player save file to be shown by pressing usual map key.

This mod was designed to enhance nomap playthroughs for players to look at their current exploration progress without the spoiler of live updates.

This mod could be handy if you want some map reference for your travels but don't want to draw map yourself in paint or w/e.


If you don't want this generated map and have your own file you can set its name in "Load map from file" and it will be shared to all clients from the server.

File can be set as full qualified name or just file name without path. Latter one should be placed near dll file in any subdirectory.

## Main features:
* generates static map based on algorithms of [MapPrinter](https://valheim.thunderstore.io/package/ASpy/MapPrinter/) mod by ASpy (all credits to him)
* shows that map at ingame window, only in nomap mode (map updates only on table interaction)
* map is saved between session at savefile
* option to save generated map to file
* map generated in 4096x4096 resolution with option to generate 8192x8192 (better visuals, more size on savefile)
* 4 different styles of map with topographical lines
* configurable pins on map
* pins config is server synced

## Pins default config
* pins only shows in explored part of the map
* Haldor and Hildir pins are always shown (especially handy for Hildir's quest pins)
* only show your own pins (no shared pins)
* pins that checked (red crossed) are not shown
* Bed and death pins are not shown

## Map can be
* opened by Map bind key (default M)
* closed by the same key or Escape
* dragged by left mouse click and drag
* zoomed by mouse wheel
* set to default zoom by right mouse click
* centered at spawn point by middle mouse click

## Best mods to use with
* To place pins immersively in nomap mode you can use [AutoPinSigns](https://valheim.thunderstore.io/package/shudnal/AutoPinSigns/)
* To see pins without map you can use [Compass](https://www.nexusmods.com/valheim/mods/851)

## Compatibility:
* This mod interacts with very little of the game code, conflicts with other mods are pretty unlikely
* Gamepad isn't supported for ingame map window
* I didn't test map saving between sessions with ServerCharacters mod

## Configurating
The best way to handle configs is configuration manager. Choose one that works for you:

https://www.nexusmods.com/site/mods/529

https://valheim.thunderstore.io/package/Azumatt/Official_BepInEx_ConfigurationManager/

## Mirrors
[Nexus](https://www.nexusmods.com/valheim/mods/2505)

[Thunderstore](https://valheim.thunderstore.io/package/shudnal/NomapPrinter/)

## Changelog

v 1.1.7
* option to prevent interactive map pins addition

v 1.1.6
* patch 0.217.38

v 1.1.5
* fixed creating pin on interactive map showing
* map style server synced

v 1.1.4
* error fix

v 1.1.3
* ward restrictions

v 1.1.2
* adjustments for Vanilla map style
* pin scale option

v 1.1.1
* Vanilla map style
* mistlands color tweaks

v 1.1.0
* greatly reduced time of map forming for partial explored maps
* added forests and oceans
* map options revamped
* added new Smooth level of map details
* UX improvements
* option to swap map table sections
* more map restrictions
* strict way to set map storage
* option to disable shared map

v 1.0.14
* fix for nonexistent folder for "Store map in local folder" options

v 1.0.13
* proper implementation for option to save map data in local file instead of character save file

v 1.0.12
* option to allow opening interactive map on record discoveries
* option to save map data in local file instead of character save file

v 1.0.11
* show all pins is disabled by default to prevent default death pins from showing

v 1.0.10
* fix for pins without texture

v 1.0.9
* patch 0.217.22, server sync fix

v 1.0.8
* patch 0.217.22

v 1.0.7
 * option to not showing the map ingame

v 1.0.6
 * external map file support

v 1.0.5
 * EpicLoot pins support

v 1.0.4
 * overlapping pins fix

v 1.0.3
 * option to restrict map opening only when near the table

v 1.0.2
 * Initial release
