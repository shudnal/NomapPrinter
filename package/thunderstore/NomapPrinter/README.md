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
* Full gamepad support

## Pins default config
* pins only shows in explored part of the map
* traders' pins are always shown (especially handy for Hildir's quest pins)
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

## Custom map layers

You can use your own variants of 
* explored map layer
* fog texture
* under fog layer (for custom markings)
* over fog layer (for custom markings)

That layers should be placed into **\BepInEx\config\shudnal.NomapPrinter** directory as PNG files and named accordingly:
* "{MapType}.{WorldName}.explored.png" for explored map, nontransparent, resolution must be 4096x4096
* "{MapType}.{WorldName}.underfog.png" for under fog layer, transparent with only needed markings, resolution must be 4096x4096
* "{MapType}.{WorldName}.overfog.png" for over fog layer, transparent with only needed markings, resolution must be 4096x4096 
* "{MapType}.{WorldName}.fog.png" for fog texture, nontransparent, any resolution, the pattern will be repeated

If you have several worlds with similar name then instead on world name you can use numerical world ID. To get world ID without using any other mod you can generate map using that mod and check **\BepInEx\cache\shudnal.NomapPrinter** directory. There will be folders named as world ID. One of them will be your world.

Acceptable map types: 
* BirdsEye
* Topographical
* Chart 
* OldChart

### File repacking

You can use ingame console command `repackpng [filename]` to make png file nonhumanreadable to prevent clients from opening explored map or markings.

That command works for png files located in **\\BepInEx\\config\\shudnal.NomapPrinter**. You can cycle through files available in the folder by pressing TAB.

The command will create the file with extension *.zpack which you can use instead of any custom layer or fog png file.

### File synchronization from server

Layers files can be synchronized from server to all clients if placed into **\\BepInEx\\config\\shudnal.NomapPrinter** directory on the server. That directory will be created automatically on the server.

The total volume of one world files should not exceed ~7-8 MB otherwise it is better to place some files into shared config folder and exchange it via modpacks or manually placing it into clients' config folder. 

By default only underfog, overfog and fog textures will automatically be shared from server to clients on file change as they most likely have rather small size.

Custom layers could be either loaded from server or loaded from local config folder. If you have "share from server" option enabled for some layer and there is no corresponding file on the server then that layer will not be loaded from local config folder.

Safest solution is to place repacked explored map and fog texture into config folder into modpack and disable sharing it from server and only share markings layers from the server.

### Example of setting custom layers to work

For that example we have world named ***BraveNewWorld*** and want to use ***Birds Eye*** map type.

We want to use all custom layers and fog texture.

Explored map will not be changing often. Markings will be changed often. Fog will not be changed.

### Share explored map and fog texture in modpack

Aside of required files (icon.png, manifest.json, README.md) in modpack archive you should 
* create **\\config\\shudnal.NomapPrinter** folder 
* place **BirdsEye.BraveNewWorld.explored.png** and **BirdsEye.BraveNewWorld.fog.png** files in that folder

That files will be copied into **\\BepInEx\\config\\** on any client automatically when they install/update the modpack.

You can see working examples of modpack config folder in [RelicHeim](https://thunderstore.io/c/valheim/p/JewelHeim/RelicHeim/) or [EpicValheim](https://thunderstore.io/c/valheim/p/EpicValheim/EpicValheim/) modpacks.

### Share markings layers

Place underfog and overfog layers files
* **BirdsEye.BraveNewWorld.underfog.png**
* **BirdsEye.BraveNewWorld.overfog.png**

on the server into **\\BepInEx\\config\\shudnal.NomapPrinter**.

### Setup server config values

Disable sharing of fog texture in "Map custom layers - Fog texture" config section.

Disable sharing of explored map texture in "Map custom layers - Explored map" config section.

Make config look like this
```
[Map custom layers]
Explored map - Enable layer = true
Explored map - Share from server = false
Under fog - Enable layer = true
Under fog - Share from server = true
Over fog - Enable layer = true
Over fog - Share from server = true
Fog texture - Enable layer = true
Fog texture - Share from server = false
```

That way both markings layers will be synced from server and explored map and fog will be loaded from local config folder.

### How to get explored map layer of that mod style

To get full explored map you should
* relaunch the game
* login into the world using new character (to not mess the map on the main character)
* use "exploremap" command
* choose **Normal map** size, **Birds Eye** map type
* disable **Show map pins** option
* enable **Save to file** option and leave **Save to file path** empty
* generate map using map table
* file **BirdsEye.BraveNewWorld.png** will be placed into "**%appdata%\\..\\LocalLow\\IronGate\\Valheim\\screenshots\\**" directory
* copy that file into **\BepInEx\cache\shudnal.NomapPrinter** and rename into **BirdsEye.BraveNewWorld.explored.png**
* change that file how you like while saving its resolution and format

## Best mods to use with
* To place pins immersively in nomap mode you can use [AutoPinSigns](https://valheim.thunderstore.io/package/shudnal/AutoPinSigns/)
* To see pins without map you can use [Compass](https://thunderstore.io/c/valheim/p/shudnal/Compass/)

## Compatibility:
* This mod interacts with very little of the game code, conflicts with other mods are pretty unlikely
* It should be compatible with mods adding new biomes on map (if the biome color is set)
* It is compatible with EpicLoot map pins

## Configurating
The best way to handle configs is [Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/).

Or [Official BepInEx Configuration Manager](https://valheim.thunderstore.io/package/Azumatt/Official_BepInEx_ConfigurationManager/).

## Mirrors
[Nexus](https://www.nexusmods.com/valheim/mods/2505)

## Donation
[Buy Me a Coffee](https://buymeacoffee.com/shudnal)