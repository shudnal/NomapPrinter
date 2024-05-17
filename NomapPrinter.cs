using BepInEx.Configuration;
using BepInEx;
using HarmonyLib;
using ServerSync;
using System;
using System.Linq;
using System.IO;

namespace NomapPrinter
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    public class NomapPrinter : BaseUnityPlugin
    {
        public const string pluginID = "shudnal.NomapPrinter";
        public const string pluginName = "Nomap Printer";
        public const string pluginVersion = "1.2.1";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> configLocked;

        public static ConfigEntry<bool> loggingEnabled;

        public static ConfigEntry<bool> saveMapToFile;
        public static ConfigEntry<string> filePath;

        public static ConfigEntry<MapWindow> mapWindow;
        public static ConfigEntry<bool> allowInteractiveMapOnWrite;
        public static ConfigEntry<bool> showSharedMap;
        public static ConfigEntry<bool> preventPinAddition;

        public static ConfigEntry<float> showNearTheTableDistance;
        public static ConfigEntry<int> showMapBasePiecesRequirement;
        public static ConfigEntry<int> showMapComfortRequirement;

        public static ConfigEntry<MapStorage> mapStorage;
        public static ConfigEntry<string> localFolder;
        public static ConfigEntry<string> sharedFile;

        public static ConfigEntry<MapType> mapType;
        public static ConfigEntry<MapSize> mapSize;
        public static ConfigEntry<float> mapDefaultScale;
        public static ConfigEntry<float> mapMinimumScale;
        public static ConfigEntry<float> mapMaximumScale;

        public static ConfigEntry<float> pinScale;
        public static ConfigEntry<bool> preserveSharedMapFog;
        public static ConfigEntry<float> worldSize;

        public static ConfigEntry<bool> showPins;
        public static ConfigEntry<bool> showExploredPins;
        public static ConfigEntry<bool> showMyPins;
        public static ConfigEntry<bool> showNonCheckedPins;
        public static ConfigEntry<bool> showMerchantPins;

        public static ConfigEntry<bool> showEveryPin;
        public static ConfigEntry<bool> showPinStart;
        public static ConfigEntry<bool> showPinTrader;
        public static ConfigEntry<bool> showPinHildir;
        public static ConfigEntry<bool> showPinHildirQuest;
        public static ConfigEntry<bool> showPinBoss;
        public static ConfigEntry<bool> showPinFire;
        public static ConfigEntry<bool> showPinHouse;
        public static ConfigEntry<bool> showPinHammer;
        public static ConfigEntry<bool> showPinPin;
        public static ConfigEntry<bool> showPinPortal;
        public static ConfigEntry<bool> showPinBed;
        public static ConfigEntry<bool> showPinDeath;
        public static ConfigEntry<bool> showPinEpicLoot;

        public static ConfigEntry<string> messageStart;
        public static ConfigEntry<string> messageSaving;
        public static ConfigEntry<string> messageReady;
        public static ConfigEntry<string> messageSavedTo;
        public static ConfigEntry<string> messageNotReady;
        public static ConfigEntry<string> messageNotEnoughBasePieces;
        public static ConfigEntry<string> messageNotEnoughComfort;

        public static ConfigEntry<bool> tablePartsSwap;

        public static readonly CustomSyncedValue<string> mapDataFromFile = new CustomSyncedValue<string>(configSync, "mapDataFromFile", "");

        public static NomapPrinter instance;

        public static string localPath;
        public static string cacheDirectory;

        public enum MapType
        {
            BirdsEye,
            Topographical,
            Chart,
            OldChart,
            Vanilla
        }

        public enum MapSize
        {
            Normal = 2,
            Smooth = 4
        }

        public enum MapStorage
        {
            Character,
            LocalFolder,
            LoadFromSharedFile
        }

        public enum MapWindow
        {
            Hide,
            ShowEverywhere,
            ShowNearTheTable,
            ShowOnInteraction
        }

        void Awake()
        {
            harmony.PatchAll();
            instance = this;

            ConfigInit();
            _ = configSync.AddLockingConfigEntry(configLocked);

            Game.isModded = true;
        }

        void Start()
        {
            MapViewer.Start();
        }

        void Update()
        {
            if (!modEnabled.Value)
                return;

            if (!Game.m_noMap)
                return;

            MapViewer.Update();
        }

        void OnGUI()
        {
            MapViewer.OnGUI();
        }

        void OnDestroy()
        {
            harmony?.UnpatchSelf();
            instance = null;
        }

        private void ConfigInit()
        {
            config("General", "NexusID", 2505, "Nexus mod ID for updates", false);

            modEnabled = config("General", "Enabled", true, "Print map on table interaction");
            configLocked = config("General", "Lock Configuration", defaultValue: true, "Configuration is locked and can be changed by server admins only.");

            loggingEnabled = config("Logging", "Enabled", false, "Enable logging. [Not Synced with Server]", false);

            mapWindow = config("Map", "Ingame map", MapWindow.ShowEverywhere, "Where to show ingame map");
            allowInteractiveMapOnWrite = config("Map", "Show interactive map on record discoveries", false, "Show interactive original game map on record discoveries part of map table used");
            showSharedMap = config("Map", "Show shared map", true, "Show parts of the map shared by others");
            preventPinAddition = config("Map", "Prevent adding pins on interactive map", false, "Prevent creating pin when using interactive map");

            showNearTheTableDistance = config("Map restrictions", "Show map near the table when distance is less than", defaultValue: 10f, "Distance to nearest map table for map to be shown");
            showMapBasePiecesRequirement = config("Map restrictions", "Show map when base pieces near the player is more than", defaultValue: 0, "Count of base pieces surrounding the player should be more than that for map to be shown");
            showMapComfortRequirement = config("Map restrictions", "Show map when player comfort is more than", defaultValue: 0, "Player comfort buff should be more than that for map to be shown");

            saveMapToFile = config("Map save", "Save to file", false, "Save generated map to file. Works in normal map mode. You can set exact file name or folder name [Not Synced with Server]", false);
            filePath = config("Map save", "Save to file path", "", "File path used to save generated map. [Not Synced with Server]", false);

            mapStorage = config("Map storage", "Data storage", MapStorage.Character, "Type of storage for map data. Default is save map data to character file.");
            localFolder = config("Map storage", "Local folder", "", "Save and load map data from local folder. If relative path is set then the folder will be created at ...\\AppData\\LocalLow\\IronGate\\Valheim");
            sharedFile = config("Map storage", "Shared file", "", "Load map from the file name instead of generating one. File should be available on the server.");

            mapStorage.SettingChanged += (sender, args) => MapViewer.SetupMapFileWatcher();
            sharedFile.SettingChanged += (sender, args) => MapViewer.SetupMapFileWatcher();

            mapType = config("Map style", "Map type", MapType.Chart, "Type of generated map");
            mapSize = config("Map style", "Map size", MapSize.Normal, "Resolution of generated map. More details means smoother lines but more data will be stored");
            mapDefaultScale = config("Map style", "Map zoom default scale", 0.7f, "Default scale of opened map, more is closer, less is farther.");
            mapMinimumScale = config("Map style", "Map zoom minimum scale", 0.25f, "Minimum scale of opened map, more is closer, less is farther.");
            mapMaximumScale = config("Map style", "Map zoom maximum scale", 2.0f, "Maximum scale of opened map, more is closer, less is farther.");

            pinScale = config("Map style extended", "Pin scale", 1.0f, "Pin scale");
            preserveSharedMapFog = config("Map style extended", "Preserve shared map fog tint for vanilla map", true, "Generate Vanilla map with shared map fog tint");
            worldSize = config("Map style extended", "World size", 10500f, "Land outside of that radius will be ignored");

            messageStart = config("Messages", "Drawing begin", "Remembering travels...", "Center message when drawing is started. [Not Synced with Server]", false);
            messageSaving = config("Messages", "Drawing end", "Drawing map...", "Center message when saving file is started. [Not Synced with Server]", false);
            messageReady = config("Messages", "Saved", "Map is ready", "Center message when file is saved. [Not Synced with Server]", false);
            messageSavedTo = config("Messages", "Saved to", "Map saved to", "Top left message with file name. [Not Synced with Server]", false);
            messageNotReady = config("Messages", "Not ready", "Map is not drawn yet", "Center message on trying to open a not ready map. [Not Synced with Server]", false);
            messageNotEnoughBasePieces = config("Messages", "Not enough base pieces", "Not enough base pieces ({0} of {1})", "Center message on trying to open a map with failed base pieces requirement check. [Not Synced with Server]", false);
            messageNotEnoughComfort = config("Messages", "Not enough comfort", "Not enough comfort ({0} of {1})", "Center message on trying to open a map with failed comfort requirement check. [Not Synced with Server]", false);

            showPins = config("Pins", "Show map pins", true, "Show pins on drawed map");
            showExploredPins = config("Pins", "Show only explored pins", true, "Only show pins on explored part of the map");
            showMerchantPins = config("Pins", "Show merchants pins always", true, "Show merchant pins even in unexplored part of the map");
            showMyPins = config("Pins", "Show only my pins", true, "Only show your pins on the map");
            showNonCheckedPins = config("Pins", "Show only unchecked pins", true, "Only show pins that doesn't checked (have no red cross)");

            showEveryPin = config("Pins list", "Show all pins", false, "Show all pins");
            showPinStart = config("Pins list", "Show Start pins", true, "Show Start pin on drawed map");
            showPinTrader = config("Pins list", "Show Haldor pins", true, "Show Haldor pin on drawed map");
            showPinHildir = config("Pins list", "Show Hildir pins", true, "Show Hildir pin on drawed map");
            showPinHildirQuest = config("Pins list", "Show Hildir quest pins", true, "Show Hildir quest pins on drawed map");
            showPinBoss = config("Pins list", "Show Boss pins", true, "Show Boss pins on drawed map");
            showPinFire = config("Pins list", "Show Fire pins", true, "Show Fire pins on drawed map");
            showPinHouse = config("Pins list", "Show House pins", true, "Show House pins on drawed map");
            showPinHammer = config("Pins list", "Show Hammer pins", true, "Show Hammer pins on drawed map");
            showPinPin = config("Pins list", "Show Pin pins", true, "Show Pin pins on drawed map");
            showPinPortal = config("Pins list", "Show Portal pins", true, "Show Portal pins on drawed map");
            showPinBed = config("Pins list", "Show Bed pins", false, "Show Bed pins on drawed map");
            showPinDeath = config("Pins list", "Show Death pins", false, "Show Death pins on drawed map");
            showPinEpicLoot = config("Pins list", "Show Epic Loot pins", true, "Show Epic Loot pins on drawed map");

            tablePartsSwap = config("Table", "Swap interaction behaviour on map table parts", false, "Make \"Read map\" part to open interactive map and \"Record discoveries\" part to generate map. +" +
                                                                                                     "\nDoesn't work in Show On Interaction map mode [Not Synced with Server]", false);

            localPath = Utils.GetSaveDataPath(FileHelpers.FileSource.Local);
            cacheDirectory = Path.Combine(Paths.CachePath, pluginID);
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigEntry<T> configEntry = Config.Bind(group, name, defaultValue, description);

            SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = true) => config(group, name, defaultValue, new ConfigDescription(description), synchronizedSetting);

        public static void LogInfo(object message)
        {
            if (loggingEnabled.Value)
                instance.Logger.LogInfo(message);
        }

        public static void LogWarning(object data)
        {
            if (loggingEnabled.Value)
                instance.Logger.LogWarning(data);
        }

        public static void ShowMessage(string text, MessageHud.MessageType type = MessageHud.MessageType.Center)
        {
            // if someone doesn't want a message and cleared the value
            if (text.IsNullOrWhiteSpace() || MessageHud.instance == null)
                return;

            MessageHud.instance.ShowMessage(type, text, 1);
        }

        [HarmonyPatch(typeof(MapTable), nameof(MapTable.OnRead), new Type[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData), typeof(bool) })]
        public static class MapTable_OnRead_ReadDiscoveriesInteraction
        {
            public static void Postfix(MapTable __instance)
            {
                if (!modEnabled.Value)
                    return;

                if (MapTable_OnWrite_RecordDiscoveriesInteraction.isCalled)
                    return;

                if (!PrivateArea.CheckAccess(__instance.transform.position))
                    return;

                if (mapWindow.Value == MapWindow.ShowOnInteraction)
                    MapViewer.ShowMap();
                else if (tablePartsSwap.Value)
                    MapViewer.ShowInteractiveMap();
                else if (mapStorage.Value != MapStorage.LoadFromSharedFile)
                    MapMaker.GenerateMap();
            }
        }

        [HarmonyPatch(typeof(MapTable), nameof(MapTable.OnWrite))]
        public static class MapTable_OnWrite_RecordDiscoveriesInteraction
        {
            public static bool isCalled = false;

            public static void Prefix()
            {
                isCalled = true;
            }

            public static void Postfix(MapTable __instance)
            {
                isCalled = false;

                if (!modEnabled.Value)
                    return;

                if (!PrivateArea.CheckAccess(__instance.transform.position))
                    return;

                if ((tablePartsSwap.Value || mapWindow.Value == MapWindow.ShowOnInteraction) && mapStorage.Value != MapStorage.LoadFromSharedFile)
                    MapMaker.GenerateMap();
                else
                    MapViewer.ShowInteractiveMap();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        public static class Player_Load_DeleteDeprecatedData
        {
            private const string dataPrefix = "NomapPrinter_MapTexture_";

            public static void Postfix(Player __instance)
            {
                __instance.m_customData.Where(data => data.Key.StartsWith(dataPrefix)).ToList().Do(data => __instance.m_customData.Remove(data.Key));
            }
        }
    }
}
