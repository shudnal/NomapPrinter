![](https://staticdelivery.nexusmods.com/mods/3667/images/2505/2505-1693921543-26561571.png)

# Nomap Printer
In nomap mode reading the Cartography Table will generate a simplified map to be shown by pressing Map hotkey.

This mod was designed to enhance nomap playthroughs for players to look at their current exploration progress without the spoiler of live updates.

This mod could be handy if you want some map reference for your travels but don't want to draw map yourself.


If you don't want this generated map and have your own file you can set its name in "Shared file", change Map storage config to "Load from shared file" and it will be shared to all clients from the server.

File can be set as full qualified name or just file name without path. Latter one should be placed near dll file in any subdirectory.

Map content will be automatically updated on file change.

## Main features:
* generates static map based originally on algorithms of [MapPrinter](https://valheim.thunderstore.io/package/ASpy/MapPrinter/) mod by ASpy (all credits to him)
* shows that map at ingame window, only in nomap mode (map updates only on table interaction)
* map is saved between session
* option to save generated map to file
* map generated in 4096x4096 resolution with option to generate 8192x8192 (smoother visuals at the cost of longer draw and larger size)
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

## Custom explored map

You can use your own variant of explored map. It will be overlayed by exploration fog using style you chose.

Map data must be stored in 4096x4096 PNG file named "{MapType}.{WorldName}.explored.png" and placed into **\BepInEx\config\shudnal.NomapPrinter** directory.

If you have several worlds with similar name then instead on world name you can use numerical world ID. To get world ID without using any other mod you can generate map using that mod and check **\BepInEx\cache\shudnal.NomapPrinter** directory. There will be folders named as world ID. One of them will be your world.

For example you have world named ***BraveNewWorld*** and you use ***Birds Eye*** map type.
To get full explored map you have to 
* relaunch the game
* login into the world and use "exploremap" command
* choose **Normal map** size, **Birds Eye** map type
* disable **Show map pins** option
* enable **Save to file** option and leave **Save to file path** empty
* generate map using map table
* file **BirdsEye.BraveNewWorld.png** will be placed into "**%appdata%\\..\\LocalLow\\IronGate\\Valheim\\screenshots\\**" directory
* copy that file into **\BepInEx\cache\shudnal.NomapPrinter** and rename into **BirdsEye.BraveNewWorld.explored.png**
* change that file how you like while saving its resolution and format
* that file will be loaded as explored map layer when you generate BirdsEye map type while in BraveNewWorld world
* that file will not be reloaded until game relaunch

## Best mods to use with
* To place pins immersively in nomap mode you can use [AutoPinSigns](https://valheim.thunderstore.io/package/shudnal/AutoPinSigns/)
* To see pins without map you can use [Compass](https://www.nexusmods.com/valheim/mods/851)

## Compatibility:
* This mod interacts with very little of the game code, conflicts with other mods are pretty unlikely
* Gamepad is not supported for ingame map window
* It should be compatible with mods adding new biomes on map (if the biome color is set)
* It is compatible with EpicLoot map pins

## Configurating
The best way to handle configs is [Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/).

Or [Official BepInEx Configuration Manager](https://valheim.thunderstore.io/package/Azumatt/Official_BepInEx_ConfigurationManager/).

## Mirrors
[Nexus](https://www.nexusmods.com/valheim/mods/2505)