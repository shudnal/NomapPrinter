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
To place pins immersively in nomap mode you can use [AutoPinSigns](https://valheim.thunderstore.io/package/shudnal/AutoPinSigns/)
To see pins without map you can use [Compass](https://www.nexusmods.com/valheim/mods/851)

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