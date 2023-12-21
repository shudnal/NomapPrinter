using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using ServerSync;

namespace NomapPrinter
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    public class NomapPrinter : BaseUnityPlugin
    {
        const string pluginID = "shudnal.NomapPrinter";
        const string pluginName = "Nomap Printer";
        const string pluginVersion = "1.1.7";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

        private static ConfigEntry<bool> modEnabled;
        private static ConfigEntry<bool> configLocked;

        private static ConfigEntry<bool> loggingEnabled;

        private static ConfigEntry<bool> saveMapToFile;
        private static ConfigEntry<string> filePath;

        private static ConfigEntry<MapWindow> mapWindow;
        private static ConfigEntry<bool> allowInteractiveMapOnWrite;
        private static ConfigEntry<bool> showSharedMap;
        private static ConfigEntry<bool> preventPinAddition;

        private static ConfigEntry<float> showNearTheTableDistance;
        private static ConfigEntry<int> showMapBasePiecesRequirement;
        private static ConfigEntry<int> showMapComfortRequirement;

        private static ConfigEntry<MapStorage> mapStorage;
        private static ConfigEntry<string> localFolder;
        private static ConfigEntry<string> sharedFile;

        private static ConfigEntry<MapType> mapType;
        private static ConfigEntry<MapSize> mapSize;
        private static ConfigEntry<float> mapDefaultScale;
        private static ConfigEntry<float> mapMinimumScale;
        private static ConfigEntry<float> mapMaximumScale;

        private static ConfigEntry<int> heightmapFactor;
        private static ConfigEntry<int> graduationLinesDensity;
        private static ConfigEntry<float> pinScale;
        private static ConfigEntry<bool> preserveSharedMapFog;

        private static ConfigEntry<bool> showPins;
        private static ConfigEntry<bool> showExploredPins;
        private static ConfigEntry<bool> showMyPins;
        private static ConfigEntry<bool> showNonCheckedPins;
        private static ConfigEntry<bool> showMerchantPins;

        private static ConfigEntry<bool> showEveryPin;
        private static ConfigEntry<bool> showPinStart;
        private static ConfigEntry<bool> showPinTrader;
        private static ConfigEntry<bool> showPinHildir;
        private static ConfigEntry<bool> showPinHildirQuest;
        private static ConfigEntry<bool> showPinBoss;
        private static ConfigEntry<bool> showPinFire;
        private static ConfigEntry<bool> showPinHouse;
        private static ConfigEntry<bool> showPinHammer;
        private static ConfigEntry<bool> showPinPin;
        private static ConfigEntry<bool> showPinPortal;
        private static ConfigEntry<bool> showPinBed;
        private static ConfigEntry<bool> showPinDeath;
        private static ConfigEntry<bool> showPinEpicLoot;

        private static ConfigEntry<string> messageStart;
        private static ConfigEntry<string> messageSaving;
        private static ConfigEntry<string> messageReady;
        private static ConfigEntry<string> messageSavedTo;
        private static ConfigEntry<string> messageNotReady;
        private static ConfigEntry<string> messageNotEnoughBasePieces;
        private static ConfigEntry<string> messageNotEnoughComfort;

        private static ConfigEntry<bool> tablePartsSwap;

        private static readonly CustomSyncedValue<string> mapDataFromFile = new CustomSyncedValue<string>(configSync, "mapDataFromFile", "");

        public static World game_world;
        public static float abyss_depth = -100f;

        private static Color32[] m_mapTexture;
        private static Color[] m_forestTexture;
        private static Color32[] m_heightmap;
        private static bool[] m_exploration;
        private static bool[] m_mapData;

        MapGeneration maker;
        private static NomapPrinter instance;

        private static string localPath;

        private static readonly Dictionary<string, Color32[]> pinIcons = new Dictionary<string, Color32[]>();

        public static GameObject parentObject;
        public static GameObject mapContent;
        public static RectTransform content;

        public static Texture2D mapTexture = new Texture2D(4096, 4096, TextureFormat.RGB24, false);

        private static bool mapWindowInitialized = false;
        private static bool mapTextureIsReady = false;

        private bool _displayingWindow = false;

        private PropertyInfo _curLockState;
        private PropertyInfo _curVisible;
        private int _previousCursorLockState;
        private bool _previousCursorVisible;

        private static string saveFieldKey;

        public event EventHandler<ValueChangedEventArgs<bool>> DisplayingWindowChanged;

        private static DirectoryInfo pluginFolder;
        private static FileSystemWatcher fileSystemWatcher;
        private static string mapFileName;

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
            Small = 1,
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
            maker = new MapGeneration();

            pluginFolder = new DirectoryInfo(Assembly.GetExecutingAssembly().Location).Parent;

            ConfigInit();
            _ = configSync.AddLockingConfigEntry(configLocked);

            mapDataFromFile.ValueChanged += new Action(LoadMapFromSharedValue);

            Game.isModded = true;
        }

        void OnDestroy()
        {
            instance = null;
            harmony?.UnpatchSelf();
        }

        private void OnGUI()
        {
            if (DisplayingWindow)
            {
                // enable cursor if map is displaying
                SetUnlockCursor(0, true);

                if (Event.current.isMouse)
                {
                    if (Event.current.type == EventType.MouseDown)
                    {
                        if (Input.GetMouseButtonDown(1)) // Right click to reset zoom
                        {
                            ZoomMap(0);
                        }
                        else if (Input.GetMouseButtonDown(2)) // Middle click to reset position
                        {
                            CenterMap();
                        }
                    }

                    // Block every other mouse clicks
                    Input.ResetInputAxes();
                }
            }
        }

        private void Start()
        {
            var tCursor = typeof(Cursor);
            _curLockState = tCursor.GetProperty("lockState", BindingFlags.Static | BindingFlags.Public);
            _curVisible = tCursor.GetProperty("visible", BindingFlags.Static | BindingFlags.Public);
        }

        private void Update()
        {

            if (!modEnabled.Value)
                return;

            if (!mapWindowInitialized)
                return;

            if (!Game.m_noMap)
                return;

            if (CanOperateMap)
            {
                if (ZInput.GetButtonUp("Map") || ZInput.GetButtonUp("JoyMap"))
                {
                    if (!mapTextureIsReady)
                        ShowMessage(messageNotReady.Value);
                    else
                        DisplayingWindow = !DisplayingWindow;
                }
            }

            if (DisplayingWindow && ZInput.GetKeyDown(KeyCode.Escape))
                DisplayingWindow = false; 

            if (DisplayingWindow)
            {
                // enable scroll to change map scale
                float scrollIncrement = ZInput.GetAxis("Mouse ScrollWheel");
                if (scrollIncrement != 0)
                {
                    ZoomMap(scrollIncrement);
                }

                // disable everything else not disabled at OnGUI call
                Input.ResetInputAxes();
            }
        }

        private static void ZoomMap(float increment)
        {
            float scaleIncrement = increment / 2;
            if (scaleIncrement == 0)
                scaleIncrement = mapDefaultScale.Value - content.localScale.x;

            int sizeFactor = (mapSize.Value == MapSize.Small ? 1 : 2);
            int typeFactor = (mapType.Value == MapType.Vanilla ? 2 : 1);

            float minScale = Mathf.Max(mapMinimumScale.Value, 0.1f) * 2 * typeFactor / sizeFactor;
            float maxScale = Mathf.Min(mapMaximumScale.Value, 2f) * typeFactor * sizeFactor;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(content, Input.mousePosition, null, out Vector2 relativeMousePosition);
            
            Vector3 _scale = new Vector3(content.localScale.x, content.localScale.y, content.localScale.z);

            float scale = Mathf.Clamp(_scale.x + scaleIncrement, minScale, maxScale);
            _scale.Set(scale, scale, 0f);

            if (content.localScale.x != _scale.x)
            {
                content.localScale = _scale;
                content.anchoredPosition -= (relativeMousePosition * scaleIncrement);
            }
        }

        private static void CenterMap()
        {
            content.localPosition = Vector2.zero;
        }

        private static void ResetViewerContentSize()
        {
            if (content.sizeDelta.x != mapTexture.width)
            {
                content.sizeDelta = new Vector2(mapTexture.width, mapTexture.height);
                mapContent.GetComponent<Image>().sprite = Sprite.Create(mapTexture, new Rect(0, 0, mapTexture.width, mapTexture.height), Vector2.zero);

                ZoomMap(0);
                CenterMap();
            }
        }

        public static void Log(object message)
        {
            if (loggingEnabled.Value)
                instance.Logger.LogInfo(message);
        }

        public bool CanOperateMap
        {
            get
            {
                if (mapWindow.Value == MapWindow.Hide || mapWindow.Value == MapWindow.ShowOnInteraction)
                    return false;

                if (DisplayingWindow)
                    return true;

                Player localPlayer = Player.m_localPlayer;

                return !(localPlayer == null || localPlayer.IsDead() || localPlayer.InCutscene() || localPlayer.IsTeleporting()) &&
                        (Chat.instance == null || !Chat.instance.HasFocus()) &&
                        !Console.IsVisible() && !Menu.IsVisible() && TextViewer.instance != null &&
                        !TextViewer.instance.IsVisible() && !TextInput.IsVisible() && !Minimap.IsOpen();

            }
        }

        public bool DisplayingWindow
        {
            get => _displayingWindow;
            set
            {
                if (_displayingWindow == value) return;

                if (value && mapWindow.Value == MapWindow.ShowNearTheTable)
                {
                    value = false;

                    List<Piece> pieces = new List<Piece>(); ;
                    Piece.GetAllPiecesInRadius(Player.m_localPlayer.transform.position, showNearTheTableDistance.Value, pieces);
                    foreach (Piece piece in pieces)
                    {
                        value = piece.TryGetComponent<MapTable>(out MapTable table);

                        if (value)
                        {
                            break;
                        }
                    }

                    if (!value)
                        ShowMessage("$piece_toofar");
                }

                if (value && (mapWindow.Value == MapWindow.ShowNearTheTable || mapWindow.Value == MapWindow.ShowOnInteraction))
                {
                    if (value && showMapBasePiecesRequirement.Value > 0 && Player.m_localPlayer.GetBaseValue() < showMapBasePiecesRequirement.Value)
                    {
                        value = false;
                        ShowMessage(String.Format(messageNotEnoughBasePieces.Value, Player.m_localPlayer.GetBaseValue(), showMapBasePiecesRequirement.Value));
                    }


                    if (value && showMapComfortRequirement.Value > 0 && Player.m_localPlayer.GetComfortLevel() < showMapComfortRequirement.Value)
                    {
                        value = false;
                        ShowMessage(String.Format(messageNotEnoughComfort.Value, Player.m_localPlayer.GetComfortLevel(), showMapComfortRequirement.Value));
                    }
                }

                _displayingWindow = value;

                if (_displayingWindow)
                {
                    if (_curLockState != null)
                    {
                        _previousCursorLockState = (int)_curLockState.GetValue(null, null);
                        _previousCursorVisible = (bool)_curVisible.GetValue(null, null);
                    }
                }
                else
                {
                    if (!_previousCursorVisible || _previousCursorLockState != 0) // 0 = CursorLockMode.None
                        SetUnlockCursor(_previousCursorLockState, _previousCursorVisible);
                }

                parentObject.SetActive(_displayingWindow);

                DisplayingWindowChanged?.Invoke(this, new ValueChangedEventArgs<bool>(value));
            }
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

            mapType = config("Map style", "Map type", MapType.Chart, "Type of generated map");
            mapSize = config("Map style", "Map size", MapSize.Normal, "Resolution of generated map. More details means smoother lines but more data will be stored");
            mapDefaultScale = config("Map style", "Map zoom default scale", 0.7f, "Default scale of opened map, more is closer, less is farther.");
            mapMinimumScale = config("Map style", "Map zoom minimum scale", 0.25f, "Minimum scale of opened map, more is closer, less is farther.");
            mapMaximumScale = config("Map style", "Map zoom maximum scale", 1.0f, "Maximum scale of opened map, more is closer, less is farther.");

            heightmapFactor = config("Map style extended", "Heightmap factor", 8, "Heightmap details factor");
            graduationLinesDensity = config("Map style extended", "Graduation line density", 8, "Graduation lines density");
            pinScale = config("Map style extended", "Pin scale", 1.0f, "Pin scale");
            preserveSharedMapFog = config("Map style extended", "Preserve shared map fog tint for vanilla map", true, "Generate Vanilla map with shared map fog tint");

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

            MapGeneration.InitIconSize();

            SetupMapFileWatcher();
        }

        private void ConfigUpdate()
        {
            Config.Reload();
            ConfigInit();
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigEntry<T> configEntry = Config.Bind(group, name, defaultValue, description);

            SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = true) => config(group, name, defaultValue, new ConfigDescription(description), synchronizedSetting);

        private static void SetupMapFileWatcher()
        {
            if (mapStorage.Value != MapStorage.LoadFromSharedFile)
                return;

            if (sharedFile.Value.IsNullOrWhiteSpace())
                return;

            if (fileSystemWatcher != null)
            {
                fileSystemWatcher.Dispose();
                fileSystemWatcher = null;
            }

            fileSystemWatcher = new FileSystemWatcher()
            {
                Path = Path.GetDirectoryName(sharedFile.Value),
                Filter = Path.GetFileName(sharedFile.Value)
            };

            if (fileSystemWatcher.Path.IsNullOrWhiteSpace())
                fileSystemWatcher.Path = pluginFolder.FullName;

            mapFileName = Path.Combine(fileSystemWatcher.Path, fileSystemWatcher.Filter);

            fileSystemWatcher.Changed += new FileSystemEventHandler(ReadMapFromFile);
            fileSystemWatcher.Created += new FileSystemEventHandler(ReadMapFromFile);
            fileSystemWatcher.Renamed += new RenamedEventHandler(ReadMapFromFile);
            fileSystemWatcher.IncludeSubdirectories = true;
            fileSystemWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            fileSystemWatcher.EnableRaisingEvents = true;

            Log($"Setup watcher {mapFileName}");

            ReadMapFromFile(null, null);
        }

        private static void ReadMapFromFile(object sender, FileSystemEventArgs eargs)
        {
            string fileData = "";

            if (!File.Exists(mapFileName))
            {
                Log($"Can't find file ({mapFileName})!");
                return;
            }

            try
            {
                fileData = Convert.ToBase64String(File.ReadAllBytes(mapFileName));
            }
            catch (Exception e)
            {
                Log($"Error reading file ({mapFileName})! Error: {e.Message}");
            }

            mapDataFromFile.AssignLocalValue(fileData);
        }

        private static void LoadMapFromSharedValue()
        {
            if (LoadMapFromFileData())
            {
                ResetViewerContentSize();
                mapTextureIsReady = true;
            }
        }

        private static bool LoadMapFromFileData()
        {
            if (mapDataFromFile.Value.IsNullOrWhiteSpace())
                return false;

            try
            {
                mapTexture.LoadImage(Convert.FromBase64String(mapDataFromFile.Value));
                mapTexture.Apply();
            }
            catch (Exception ex)
            {
                Log($"Loading map error. Invalid printed map texture: {ex}");
                return false;
            }

            return true;
        }

        private static string LocalFileName(Player player)
        {
            return Path.Combine(localPath, localFolder.Value, $"shudnal.NomapPrinter.{player.GetPlayerName()}.{game_world.m_name}.png");
        }

        private static bool LoadMapFromLocalFile(Player player)
        {
            if (mapStorage.Value != MapStorage.LocalFolder)
                return false;

            string filename = LocalFileName(player);

            if (!File.Exists(filename))
            {
                Log($"Can't find file ({filename})!");
                return false;
            }

            try
            {
                Log($"Loading nomap data from {filename}");
                mapTexture.LoadImage(File.ReadAllBytes(filename));
                mapTexture.Apply();
            }
            catch (Exception ex)
            {
                Log($"Loading map error. Invalid printed map texture: {ex}");
                return false;
            }

            return true;
        }

        private static void SaveMapToLocalFile(Player player)
        {
            if (mapStorage.Value != MapStorage.LocalFolder)
                return;

            string filename = LocalFileName(player);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filename));

                Log($"Saving nomap data to {filename}");
                File.WriteAllBytes(filename, ImageConversion.EncodeToPNG(mapTexture));
            }
            catch (Exception ex)
            {
                Log($"Saving map to local file error:\n{ex}");
            }
        }

        private static void ShowMessage(string text, MessageHud.MessageType type = MessageHud.MessageType.Center)
        {
            // if someone doesn't want a message and cleared the value
            if (text.IsNullOrWhiteSpace())
                return;

            MessageHud.instance.ShowMessage(type, text, 1);
        }

        private void SetUnlockCursor(int lockState, bool cursorVisible)
        {
            if (_curLockState != null)
            {
                _curLockState.SetValue(null, lockState, null);
                _curVisible.SetValue(null, cursorVisible, null);
            }
        }

        private static void SaveValue(Player player, string key, string value)
        {
            if (player.m_customData.ContainsKey(key))
                player.m_customData[key] = value;
            else
                player.m_customData.Add(key, value);
        }

        private static void DeleteValue(Player player, string key)
        {
            player.m_customData.Remove(key);
        }

        private static bool LoadValue(Player player, string key, out string value)
        {
            if (player.m_customData.TryGetValue(key, out value))
                return true;

            return false;
        }

        private static bool LoadMapFromPlayer(Player player)
        {
            if (!modEnabled.Value)
                return false;

            if (player == null)
                return false;

            if (player != Player.m_localPlayer)
                return false;

            if (LoadMapFromLocalFile(player))
                return true;

            if (!LoadValue(player, saveFieldKey, out string texBase64))
                return false;

            try
            {
                mapTexture.LoadImage(Convert.FromBase64String(texBase64));
                mapTexture.Apply();
            }
            catch (Exception ex)
            {
                Log($"Loading map error. Invalid printed map texture: {ex}");
                DeleteMapFromPlayer(player);
                return false;
            }

            return true;
        }

        private static void DeleteMapFromPlayer(Player player)
        {
            if (!modEnabled.Value)
                return;

            if (player == null)
                return;

            if (player != Player.m_localPlayer)
                return;

            DeleteValue(player, saveFieldKey);
        }

        private static void SaveMapToPlayer(Player player)
        {
            if (!modEnabled.Value)
                return;

            if (player == null)
                return;

            if (player != Player.m_localPlayer)
                return;

            if (mapTextureIsReady && mapStorage.Value == MapStorage.Character)
            {
                try
                {
                    SaveValue(player, saveFieldKey, Convert.ToBase64String(mapTexture.EncodeToPNG()));
                }
                catch (Exception ex)
                {
                    Log($"Saving map error. Invalid printed map texture: {ex}");
                    DeleteMapFromPlayer(player);
                }
            }
            else
            {
                DeleteMapFromPlayer(player);
            }

            SaveMapToLocalFile(player);
        }

        public static void AddIngameView(Transform parentTransform)
        {

            // Parent object to set visibility
            parentObject = new GameObject("NomapPrinter_Parent", typeof(RectTransform));
            parentObject.transform.SetParent(parentTransform, false);
            parentObject.layer = LayerMask.NameToLayer("UI");

            // Parent rect with size of fullscreen
            RectTransform pRectTransform = parentObject.GetComponent<RectTransform>();
            pRectTransform.anchoredPosition = Vector2.zero;
            pRectTransform.anchorMin = Vector2.zero;
            pRectTransform.anchorMax = Vector2.one;
            pRectTransform.sizeDelta = Vector2.zero;

            // ScrollView object to operate the map
            GameObject mapScrollView = new GameObject("NomapPrinter_ScrollView", typeof(RectTransform));
            mapScrollView.layer = LayerMask.NameToLayer("UI");
            mapScrollView.transform.SetParent(parentObject.transform, false);

            // ScrollView rect with margin from screen edge
            RectTransform rtScrollView = mapScrollView.GetComponent<RectTransform>();
            rtScrollView.anchorMin = Vector2.zero;
            rtScrollView.anchorMax = Vector2.one;
            rtScrollView.sizeDelta = new Vector2(-300f, -200f);

            // Background image to make the borders visible
            mapScrollView.AddComponent<Image>().color = new Color(0, 0, 0, 0.4f);

            // ScrollRect component with inner scrolling logic
            ScrollRect svScrollRect = mapScrollView.AddComponent<ScrollRect>();

            // Viewport object of ScrollRect logic
            GameObject mapViewPort = new GameObject("NomapPrinter_ViewPort", typeof(RectTransform));
            mapViewPort.layer = LayerMask.NameToLayer("UI");
            mapViewPort.transform.SetParent(mapScrollView.transform, false);

            // Auto applied mask
            mapViewPort.AddComponent<RectMask2D>();
            mapViewPort.AddComponent<Image>().color = new Color(0, 0, 0, 0);

            // Viewport rect is on 6 pixels less then Scrollview to make borders
            RectTransform viewport = mapViewPort.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.sizeDelta = new Vector2(-12f, -12f);
            viewport.anchoredPosition = Vector2.zero;

            // Content object to contain the Map image
            mapContent = new GameObject("NomapPrinter_Map", typeof(RectTransform));
            mapContent.layer = LayerMask.NameToLayer("UI");

            // Map rect is full size. It must be child of Viewport
            content = mapContent.GetComponent<RectTransform>();
            content.SetParent(mapViewPort.transform);
            content.sizeDelta = new Vector2(mapTexture.width, mapTexture.height);
            content.anchoredPosition = Vector2.zero;

            ZoomMap(0);

            // Map image component
            Image mapImage = mapContent.AddComponent<Image>();
            mapImage.sprite = Sprite.Create(mapTexture, new Rect(0, 0, mapTexture.width, mapTexture.height), Vector2.zero);
            mapImage.preserveAspect = true;

            // Scroll rect settings
            svScrollRect.scrollSensitivity = 0;
            svScrollRect.content = content;
            svScrollRect.viewport = viewport;
            svScrollRect.horizontal = true;
            svScrollRect.vertical = true;
            svScrollRect.inertia = false;
            svScrollRect.movementType = ScrollRect.MovementType.Clamped;

            mapWindowInitialized = true;

            parentObject.SetActive(false);

            Log("Ingame drawed map added to hud");
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Save))]
        public static class Player_Save_SaveMapData
        {
            public static bool Prefix(Player __instance)
            {
                SaveMapToPlayer(__instance);
                return true;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        public static class Player_Load_LoadMapData
        {
            public static void Postfix(Player __instance)
            {
                if (LoadMapFromPlayer(__instance))
                {
                    ResetViewerContentSize();
                    mapTextureIsReady = true;
                }
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        class Hud_Awake_AddIngameView
        {
            static void Postfix(Hud __instance)
            {
                if (!modEnabled.Value)
                    return;

                if (GameObject.Find("NomapPrinter_Parent"))
                    return;

                AddIngameView(__instance.m_rootObject.transform); // Parent object is parent of HUD itself
            }
        }

        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.Initialize))]
        public static class WorldGenerator_Initialize_Patch
        {
            public static void Postfix(World world)
            {
                game_world = world;

                saveFieldKey = $"NomapPrinter_MapTexture_{world.m_name}";
            }
        }

        private static void GenerateMap()
        {
            if (!saveMapToFile.Value && !Game.m_noMap)
                return;

            instance.ConfigUpdate();

            if (!saveMapToFile.Value && mapStorage.Value == MapStorage.LoadFromSharedFile)
                return;

            if (!instance.maker.working)
                instance.StartCoroutine(instance.maker.Go());
        }

        private static void ShowInteractiveMap()
        {
            if (!Game.m_noMap)
                return;

            instance.ConfigUpdate();

            if (!allowInteractiveMapOnWrite.Value)
                return;

            Game.m_noMap = false;

            Minimap.instance.inputDelay = 1f;
            Minimap.instance.SetMapMode(Minimap.MapMode.Large);

            Game.m_noMap = true;
        }

        [HarmonyPatch(typeof(MapTable), nameof(MapTable.OnRead))]
        [HarmonyPriority(Priority.Last)]
        public static class MapTable_OnRead_ReadDiscoveriesInteraction
        {
            static void Postfix(MapTable __instance)
            {
                if (!modEnabled.Value)
                    return;

                if (!PrivateArea.CheckAccess(__instance.transform.position))
                {
                    return;
                }

                if (mapWindow.Value == MapWindow.ShowOnInteraction)
                {
                    if (!mapTextureIsReady)
                        ShowMessage(messageNotReady.Value);
                    else
                        instance.DisplayingWindow = true;
                    return;
                }

                if (tablePartsSwap.Value)
                {
                    ShowInteractiveMap();
                    return;
                }

                GenerateMap();
            }
        }

        [HarmonyPatch(typeof(MapTable), nameof(MapTable.OnWrite))]
        [HarmonyPriority(Priority.Last)]
        public static class MapTable_OnWrite_RecordDiscoveriesInteraction
        {
            static void Postfix(MapTable __instance)
            {
                if (!modEnabled.Value)
                    return;

                if (!PrivateArea.CheckAccess(__instance.transform.position))
                {
                    return;
                }

                if (tablePartsSwap.Value || mapWindow.Value == MapWindow.ShowOnInteraction)
                {
                    GenerateMap();
                    return;
                }

                ShowInteractiveMap();
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.IsOpen))]
        [HarmonyPriority(Priority.Last)]
        public static class Minimap_IsOpen_EmulateMinimapOpenStatus
        {
            static void Postfix(ref bool __result)
            {
                if (!modEnabled.Value)
                    return;

                __result = __result || instance.DisplayingWindow;
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.ShowPinNameInput))]
        [HarmonyPriority(Priority.Last)]
        public static class Minimap_ShowPinNameInput_PreventPinAddition
        {
            static bool Prefix()
            {
                if (!modEnabled.Value)
                    return true;

                return !(allowInteractiveMapOnWrite.Value && preventPinAddition.Value);
            }
        }

        private class MapGeneration : MonoBehaviour
        {
            public bool working = false;
            private static Texture2D iconSpriteTexture;   // Current sprite texture is not readable. Saving a cached copy the first time the variable is accessed 
            private static readonly List<KeyValuePair<Vector3, string>> pinsToPrint = new List<KeyValuePair<Vector3, string>>();    // key - map position, value - icon name
            private static int iconSize = 16;
            private static int textureSize;

            private static Texture2D noClouds;

            public IEnumerator Go()
            {
                working = true;

                if (mapType.Value != MapType.Vanilla)
                {
                    ShowMessage(messageStart.Value);

                    yield return PrepareTerrainData();

                    ShowMessage(messageSaving.Value);

                    MapImageGeneration.Initialize(m_mapTexture, m_forestTexture, m_heightmap, m_exploration, textureSize, m_mapData);

                    MapImageGeneration imageGen = new MapImageGeneration();

                    switch (mapType.Value)
                    {
                        case MapType.BirdsEye:
                            yield return imageGen.GenerateSatelliteImage();
                            break;
                        case MapType.Topographical:
                            yield return imageGen.GenerateTopographicalMap(graduationLinesDensity.Value);
                            break;
                        case MapType.Chart:
                            yield return imageGen.GenerateChartMap(graduationLinesDensity.Value);
                            break;
                        case MapType.OldChart:
                            yield return imageGen.GenerateOldMap(graduationLinesDensity.Value);
                            break;
                        default:
                            goto case MapType.Chart;
                    }

                    ApplyMapTexture(mapType.Value, imageGen.output);
                }
                else
                {
                    ShowMessage(messageSaving.Value);
                    yield return GetVanillaMap(2048 * (int) mapSize.Value);
                }

                if (saveMapToFile.Value)
                {
                    string filename = filePath.Value;

                    if (filename.IsNullOrWhiteSpace())
                    {
                        filename = Path.Combine(localPath, "screenshots", $"{mapType.Value}.{game_world.m_name}.png");
                    }
                    else
                    {
                        FileAttributes attr = File.GetAttributes(filename);
                        if (attr.HasFlag(FileAttributes.Directory))
                            filename = Path.Combine(filename, $"{mapType.Value}.{game_world.m_name}.png");
                    }

                    string filepath = Path.GetDirectoryName(filename);

                    var internalThread = new Thread(() =>
                    {
                        try
                        {
                            Directory.CreateDirectory(filepath);
                            Log($"Writing {filename}");
                            File.WriteAllBytes(filename, ImageConversion.EncodeToPNG(mapTexture));
                        }
                        catch (Exception ex)
                        {
                            Log(ex);
                        }
                    });

                    internalThread.Start();
                    while (internalThread.IsAlive == true)
                    {
                        yield return null;
                    }

                    ShowMessage($"{messageSavedTo.Value} {filepath}", MessageHud.MessageType.TopLeft);
                }

                Log("Finished Map Draw");
                ShowMessage(messageReady.Value);

                MapImageGeneration.DeInitialize();

                m_mapTexture = null;
                m_forestTexture = null;
                m_heightmap = null;
                m_exploration = null;
                m_mapData = null;

                working = false;
            }

            private IEnumerator PrepareTerrainData()
            {
                int mapSizeFactor = mapSize.Value == MapSize.Smooth ? 2 : 1;

                float m_pixelSize = Minimap.instance.m_pixelSize / mapSizeFactor;
                textureSize = Minimap.instance.m_textureSize * mapSizeFactor;

                int num = textureSize / 2;
                float num2 = m_pixelSize / 2f;

                Color32[] biomeColor = new Color32[textureSize * textureSize];
                Color[] forest = new Color[textureSize * textureSize];
                Color32[] heightmap = new Color32[textureSize * textureSize];
                bool[] exploration = new bool[textureSize * textureSize];

                bool[] mapData = new bool[textureSize * textureSize];

                var internalThread = new Thread(() =>
                {
                    for (int i = 0; i < textureSize; i++)
                    {
                        for (int j = 0; j < textureSize; j++)
                        {
                            int pos = i * textureSize + j;

                            exploration[pos] = IsExplored(j / mapSizeFactor, i / mapSizeFactor);

                            if (!exploration[pos])
                                continue;

                            // Get map data in a small radius
                            for (int di = -2 * mapSizeFactor; di <= 2 * mapSizeFactor; di++)
                                for (int dj = -2 * mapSizeFactor; dj <= 2 * mapSizeFactor; dj++)
                                    if ((i + di >= 0) && (j + dj >= 0) && (i + di < textureSize) && (j + dj < textureSize))
                                        mapData[(i + di) * textureSize + j + dj] = true;
                        }
                    }

                    for (int i = 0; i < textureSize; i++)
                    {
                        float wy = (i - num) * m_pixelSize + num2;

                        for (int j = 0; j < textureSize; j++)
                        {
                            float wx = (j - num) * m_pixelSize + num2;

                            int pos = i * textureSize + j;

                            if (!mapData[pos])
                                continue;

                            Heightmap.Biome biome = WorldGenerator.instance.GetBiome(wx, wy);
                            float biomeHeight = WorldGenerator.instance.GetBiomeHeight(biome, wx, wy, out Color mask);
                            float height = biomeHeight - ZoneSystem.instance.m_waterLevel;

                            forest[pos] = GetMaskColor(wx, wy, height, biome, mask);

                            // Black outside the actual map
                            if (biomeHeight < abyss_depth)
                            {
                                biomeColor[pos] = Color.black;
                                continue;
                            }

                            if (height > 0)
                                heightmap[pos] = new Color(height / Mathf.Pow(2, heightmapFactor.Value + 1), 0f, 0f);
                            else
                                heightmap[pos] = new Color(0f, 0f, height / -Mathf.Pow(2, heightmapFactor.Value + (biome == Heightmap.Biome.Swamp ? 1 : 0)));

                            //Biome data
                            biomeColor[pos] = GetPixelColor(biome, biomeHeight);

                        }
                    }
                });

                internalThread.Start();
                while (internalThread.IsAlive == true)
                {
                    yield return null;
                }

                m_mapTexture = biomeColor;
                m_forestTexture = forest;
                m_heightmap = heightmap;
                m_exploration = exploration;
                m_mapData = mapData;
            }

            private static IEnumerator GetVanillaMap(int resolution)
            {
                bool wasOpen = Minimap.instance.m_largeRoot.activeSelf;
                if (!wasOpen)
                {
                    bool nomap = Game.m_noMap;
                    
                    if (nomap)
                        Game.m_noMap = false;

                    Minimap.instance.inputDelay = 0.5f;
                    Minimap.instance.SetMapMode(Minimap.MapMode.Large);
                    Minimap.instance.CenterMap(Vector3.zero);

                    if (nomap)
                        Game.m_noMap = true;
                }

                if (noClouds == null)
                {
                    noClouds = new Texture2D(1, 1);
                    noClouds.SetPixels(new Color[1] { Color.clear });
                    noClouds.Apply(false);
                }

                Material material = Minimap.instance.m_mapLargeShader;

                // Disable clouds
                Texture clouds = material.GetTexture("_CloudTex");
                material.SetTexture("_CloudTex", noClouds);

                // Replace shared map toggle
                bool m_showSharedMapData = Minimap.instance.m_showSharedMapData;
                bool replaceSharedMapToggle = showSharedMap.Value != Minimap.instance.m_showSharedMapData;

                if (replaceSharedMapToggle)
                    material.SetFloat("_SharedFade", showSharedMap.Value ? 1f : 0f);

                // Combine fog for shared map
                Color[] fogTex = Minimap.instance.m_fogTexture.GetPixels();
                bool combineFog = !preserveSharedMapFog.Value && showSharedMap.Value;
                if (combineFog)
                {
                    Color[] pixels = Minimap.instance.m_fogTexture.GetPixels();
                    for (int i = 0; i < pixels.Length; i++)
                        if (pixels[i].g == 0f && pixels[i].r != 0f)
                            pixels[i].r = pixels[i].g;

                    Minimap.instance.m_fogTexture.SetPixels(pixels);
                    Minimap.instance.m_fogTexture.Apply();
                }

                GameObject mapPanelObject = InitMapPanel(material);

                RenderTexture renderTexture = new RenderTexture(resolution, resolution, 24);
                
                GameObject cameraObject = new GameObject()
                {
                    layer = 19
                };
               
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.targetTexture = renderTexture;
                camera.orthographic = true;
                camera.rect = new Rect(0, 0, resolution, resolution);
                camera.nearClipPlane = 0;
                camera.farClipPlane = 100;
                camera.orthographicSize = 50;
                camera.cullingMask = 1 << 19;
                camera.Render();

                EnvSetup env = EnvMan.instance.GetCurrentEnvironment();
                float m_sunAngle = env.m_sunAngle;
                float m_smoothDayFraction = EnvMan.instance.m_smoothDayFraction;
                Vector3 m_dirLight = EnvMan.instance.m_dirLight.transform.forward;

                EnvMan.instance.m_smoothDayFraction = 0.5f;
                env.m_sunAngle = 60f;
                EnvMan.instance.SetEnv(env, 1, 0, 0, 0, 0);
                EnvMan.instance.m_dirLight.transform.forward = Vector3.down;

                yield return new WaitForEndOfFrame();

                EnvMan.instance.m_smoothDayFraction = 0.5f;
                env.m_sunAngle = 60;
                EnvMan.instance.SetEnv(env, 1, 0, 0, 0, Time.fixedDeltaTime);

                RenderTexture.active = renderTexture;

                mapTexture.Reinitialize(resolution, resolution, TextureFormat.RGB24, false);
                mapTexture.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);

                if (showPins.Value)
                {
                    Color32[] map = mapTexture.GetPixels32();

                    GetPinsToPrint();
                    AddPinsOnMap(map, resolution);

                    mapTexture.SetPixels32(map);
                }

                mapTexture.Apply();

                ResetViewerContentSize();

                RenderTexture.active = null;

                Destroy(mapPanelObject);
                Destroy(cameraObject);
                Destroy(renderTexture);

                // Return clouds
                material.SetTexture("_CloudTex", clouds);
                EnvMan.instance.m_smoothDayFraction = m_smoothDayFraction;
                env.m_sunAngle = m_sunAngle;
                EnvMan.instance.m_dirLight.transform.forward = m_dirLight;

                // Return shared map toggle
                if (replaceSharedMapToggle)
                    material.SetFloat("_SharedFade", m_showSharedMapData ? 1f : 0f);

                if (combineFog)
                {
                    Minimap.instance.m_fogTexture.SetPixels(fogTex);
                    Minimap.instance.m_fogTexture.Apply();
                }

                if (!wasOpen)
                    Minimap.instance.SetMapMode(Minimap.MapMode.Small);
            }

            private static GameObject InitMapPanel(Material material)
            {
                Vector3[] vertices = new Vector3[4]
                {
                    new Vector3(-100 / 2, -100 / 2, 10),
                    new Vector3(100 / 2, -100 / 2, 10),
                    new Vector3(-100 / 2, 100 / 2, 10),
                    new Vector3(100 / 2, 100 / 2, 10)
                };

                int[] tris = new int[6]
                {
                    0,
                    2,
                    1,
                    2,
                    3,
                    1
                };
                Vector3[] normals = new Vector3[4]
                {
                    -Vector3.forward,
                    -Vector3.forward,
                    -Vector3.forward,
                    -Vector3.forward
                };

                Vector2[] uv = new Vector2[4]
                {
                    new Vector2(0, 0),
                    new Vector2(1, 0),
                    new Vector2(0, 1),
                    new Vector2(1, 1)
                };

                GameObject gameObject = new GameObject();

                MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;

                MeshFilter meshFilter = gameObject.AddComponent<MeshFilter>();
                meshFilter.mesh = new Mesh
                {
                    vertices = vertices,
                    triangles = tris,
                    normals = normals,
                    uv = uv
                };

                gameObject.layer = 19;

                return gameObject;
            }

            private static void ApplyMapTexture(MapType mapType, Color32[] map)
            {
                DoubleMapSize(ref map, out int mapResolution);

                if (mapSize.Value == MapSize.Normal) 
                    DoubleMapSize(ref map, out mapResolution);

                if (showPins.Value)
                {
                    GetPinsToPrint();
                    AddPinsOnMap(map, mapResolution);
                }

                mapTexture.Reinitialize(mapResolution, mapResolution, TextureFormat.RGB24, false);
                mapTexture.SetPixels32(map);
                mapTexture.Apply();

                ResetViewerContentSize();

                mapTextureIsReady = true;
            }

            private static void DoubleMapSize(ref Color32[] map, out int mapSize)
            {
                int currentMapSize = (int)Math.Sqrt(map.Length);
                mapSize = currentMapSize * 2;

                Color32[] doublemap = new Color32[mapSize * mapSize];

                for (int row = 0; row < currentMapSize; row++)
                {
                    for (int col = 0; col < currentMapSize; col++)
                    {
                        Color32 pix = map[row * currentMapSize + col];
                        doublemap[row * 2 * mapSize + col * 2] = pix;
                        doublemap[row * 2 * mapSize + col * 2 + 1] = pix;
                        doublemap[(row * 2 + 1) * mapSize + col * 2] = pix;
                        doublemap[(row * 2 + 1) * mapSize + col * 2 + 1] = pix;
                    }
                }

                map = doublemap;
            }

            private static Color GetPixelColor(Heightmap.Biome biome, float height)
            {
                if (height < abyss_depth)
                    return Color.black;

                switch (biome)
                {
                    case Heightmap.Biome.Meadows:
                        return Minimap.instance.m_meadowsColor;
                    case Heightmap.Biome.AshLands:
                        return Minimap.instance.m_ashlandsColor;
                    case Heightmap.Biome.BlackForest:
                        return Minimap.instance.m_blackforestColor;
                    case Heightmap.Biome.DeepNorth:
                        return new Color(0.85f, 0.85f, 1f);  // Blueish color
                    case Heightmap.Biome.Plains:
                        return Minimap.instance.m_heathColor;
                    case Heightmap.Biome.Swamp:
                        return Minimap.instance.m_swampColor;
                    case Heightmap.Biome.Mountain:
                        return Minimap.instance.m_mountainColor;
                    case Heightmap.Biome.Mistlands:
                        return new Color(0.30f, 0.20f, 0.30f);
                    case Heightmap.Biome.Ocean:
                        return Color.blue;
                    default:
                        return Color.black;
                }
            }

            private static Color GetMaskColor(float wx, float wy, float height, Heightmap.Biome biome, Color mask)
            {
                Color noForest = new Color(0f, 0f, 0f, 0f);
                Color forest = new Color(1f, 0f, 0f, 0f);
                Color blackforest = new Color(0.75f, 0f, 0f, 0f);

                if (height < 0)
                    return noForest;

                switch (biome)
                {
                    case Heightmap.Biome.Meadows:
                        return !WorldGenerator.InForest(new Vector3(wx, 0.0f, wy)) ? noForest : forest;
                    case Heightmap.Biome.Plains:
                        return !(WorldGenerator.GetForestFactor(new Vector3(wx, 0f, wy)) < 0.8f) ? noForest : forest;
                    case Heightmap.Biome.BlackForest:
                        return mapType.Value == MapType.OldChart ? forest : blackforest;
                    case Heightmap.Biome.Mistlands:
                        {
                            float forestFactor = WorldGenerator.GetForestFactor(new Vector3(wx, 0f, wy));
                            return new Color(0.5f + 0.5f * mask.a, 1f - Utils.SmoothStep(1.1f, 1.3f, forestFactor), 0.04f, 0f);
                        }
                    default:
                        return noForest;
                }
            }

            private static void AddPinsOnMap(Color32[] map, int mapSize)
            {
                foreach (KeyValuePair<Vector3, string> pin in pinsToPrint)
                {
                    // get position in relative float instead of vector
                    Minimap.instance.WorldToMapPoint(pin.Key, out float mx, out float my);

                    // filter icons outside of map circle
                    if (mx >= 1 || my >= 1 || mx <= 0 || my <= 0)
                    {
                        continue;
                    }

                    Color32[] iconPixels = pinIcons[pin.Value];
                    if (iconPixels != null)
                    {
                        // get icon position in array
                        int iconmx = Math.Max((int)(mx * mapSize) - (iconSize / 2), 0);
                        int iconmy = Math.Max((int)(my * mapSize) - (iconSize / 2), 0);

                        // overlay icon pixels to map array with lerp
                        for (int row = 0; row < iconSize; row++)
                        {
                            for (int col = 0; col < iconSize; col++)
                            {
                                int pos = (iconmy + row) * mapSize + iconmx + col;

                                Color32 iconPix = iconPixels[row * iconSize + col];
                                if (mapType.Value == MapType.Chart || mapType.Value == MapType.OldChart)
                                {
                                    // add yellow tint of chart maps, one iteration is enough for OldChart
                                    iconPix = Color32.Lerp(iconPix, MapImageGeneration.yellowMap, 0.33f * iconPix.a / 255f);
                                }

                                map[pos] = Color32.Lerp(map[pos], iconPix, iconPix.a / 255f); // alpha is relative for more immersive effect
                                map[pos].a = byte.MaxValue;  // make opaque again
                            }
                        }
                    }
                }
            }

            private static bool IsExplored(int x, int y)
            {
                Color explorationPos = Minimap.instance.m_fogTexture.GetPixel(x, y);
                return explorationPos.r == 0f || showSharedMap.Value && explorationPos.g == 0f;
            }

            private static void GetPinsToPrint()
            {
                pinsToPrint.Clear();

                if (!showPins.Value)
                    return;

                if (Minimap.instance == null)
                    return;

                foreach (Minimap.PinData pin in Minimap.instance.m_pins)
                {
                    if (pin.m_icon.name != "mapicon_start")
                    {
                        if (!showEveryPin.Value)
                        {
                            if (showNonCheckedPins.Value && pin.m_checked)
                                continue;

                            if (showMyPins.Value && pin.m_ownerID != 0L)
                                continue;

                            if (showExploredPins.Value)
                            {
                                Minimap.instance.WorldToPixel(pin.m_pos, out int px, out int py);
                                if (!IsExplored(px, py) && (!IsMerchantPin(pin.m_icon.name) || !showMerchantPins.Value))
                                    continue;
                            }
                        }
                    }

                    if (IsShowablePinIcon(pin))
                        pinsToPrint.Add(new KeyValuePair<Vector3, string>(pin.m_pos, pin.m_icon.name));

                }
            }

            private static bool IsIconConfiguredShowable(string pinIcon)
            {
                if (showEveryPin.Value)
                    return true;

                switch (pinIcon)
                {
                    case "mapicon_boss_colored":
                        return showPinBoss.Value;
                    case "mapicon_fire":
                        return showPinFire.Value;
                    case "mapicon_hammer":
                        return showPinHammer.Value;
                    case "mapicon_hildir":
                        return showPinHildir.Value;
                    case "mapicon_hildir1":
                        return showPinHildirQuest.Value;
                    case "mapicon_hildir2":
                        return showPinHildirQuest.Value;
                    case "mapicon_hildir3":
                        return showPinHildirQuest.Value;
                    case "mapicon_house":
                        return showPinHouse.Value;
                    case "mapicon_pin":
                        return showPinPin.Value;
                    case "mapicon_portal":
                        return showPinPortal.Value;
                    case "mapicon_start":
                        return showPinStart.Value;
                    case "mapicon_trader":
                        return showPinTrader.Value;
                    case "mapicon_bed":
                        return showPinBed.Value;
                    case "mapicon_death":
                        return showPinDeath.Value;
                    case "mapicon_eventarea":
                        return showPinEpicLoot.Value;
                    case "MapIconBounty":
                        return showPinEpicLoot.Value;
                    case "TreasureMapIcon":
                        return showPinEpicLoot.Value;
                }

                return false;
            }

            private static bool IsMerchantPin(string pinIcon)
            {
                switch (pinIcon)
                {
                    case "mapicon_hildir":
                        return true;
                    case "mapicon_hildir1":
                        return true;
                    case "mapicon_hildir2":
                        return true;
                    case "mapicon_hildir3":
                        return true;
                    case "mapicon_trader":
                        return true;
                    case "MapIconBounty":
                        return showPinEpicLoot.Value;
                    case "TreasureMapIcon":
                        return showPinEpicLoot.Value;
                    case "mapicon_eventarea":
                        return showPinEpicLoot.Value;
                }

                return false;
            }

            private static bool IsShowablePinIcon(Minimap.PinData pin)
            {
                if (pin.m_icon == null)
                    return false;

                bool showIcon = IsIconConfiguredShowable(pin.m_icon.name);

                if (showIcon && !pinIcons.ContainsKey(pin.m_icon.name) && !AddPinIconToCache(pin.m_icon))
                    return false;

                return showIcon;
            }

            private static bool AddPinIconToCache(Sprite icon)
            {
                Color32[] iconPixels = GetIconPixels(icon, iconSize, iconSize);

                if (iconPixels == null || iconPixels.Length <= 1)
                    return false;

                pinIcons.Add(icon.name, iconPixels);
                return true;
            }

            private static Color32[] GetIconPixels(Sprite icon, int targetX, int targetY)
            {
                Texture2D texture2D = GetTextureFromSprite(icon);
                if (texture2D == null)
                    return null;

                RenderTexture tmp = RenderTexture.GetTemporary(
                                                    targetX,
                                                    targetY,
                                                    24);

                Graphics.Blit(texture2D, tmp);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = tmp;

                Texture2D result = new Texture2D(targetX, targetY, TextureFormat.RGBA32, false, false);
                result.ReadPixels(new Rect(0, 0, targetX, targetY), 0, 0);
                result.Apply();

                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(tmp);

                Color32[] iconPixels = result.GetPixels32();

                Destroy(result);

                return iconPixels;
            }

            private static Texture2D GetTextureFromSprite(Sprite sprite)
            {
                if (sprite.texture == null)
                    return null;

                if (sprite.texture.width == 0 || sprite.texture.height == 0)
                    return null;

                if (sprite.rect.width != sprite.texture.width)
                {
                    int texWid = (int)sprite.rect.width;
                    int texHei = (int)sprite.rect.height;
                    Texture2D newTex = new Texture2D(texWid, texHei);
                    Color[] defaultPixels = Enumerable.Repeat<Color>(new Color(0, 0, 0, 0), texWid * texHei).ToArray();
                    Color[] pixels = GetIconSpriteTexture(sprite.texture).GetPixels((int)sprite.textureRect.x
                                                                                  , (int)sprite.textureRect.y
                                                                                  , (int)sprite.textureRect.width
                                                                                  , (int)sprite.textureRect.height);

                    newTex.SetPixels(defaultPixels);
                    newTex.SetPixels((int)sprite.textureRectOffset.x, (int)sprite.textureRectOffset.y, (int)sprite.textureRect.width, (int)sprite.textureRect.height, pixels);
                    newTex.Apply();
                    return newTex;
                }
                
                return GetReadableTexture(sprite.texture);
            }

            private static Texture2D GetIconSpriteTexture(Texture2D texture2D)
            {
                if (iconSpriteTexture == null)
                    iconSpriteTexture = GetReadableTexture(texture2D);

                return iconSpriteTexture;
            }

            private static Texture2D GetReadableTexture(Texture2D texture)
            {
                RenderTexture tmp = RenderTexture.GetTemporary(
                                                    texture.width,
                                                    texture.height,
                                                    24);

                // Blit the pixels on texture to the RenderTexture
                Graphics.Blit(texture, tmp);

                // Backup the currently set RenderTexture
                RenderTexture previous = RenderTexture.active;

                // Set the current RenderTexture to the temporary one we created
                RenderTexture.active = tmp;

                // Create a new readable Texture2D to copy the pixels to it
                Texture2D textureCopy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, false);

                // Copy the pixels from the RenderTexture to the new Texture
                textureCopy.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
                textureCopy.Apply();

                // Reset the active RenderTexture
                RenderTexture.active = previous;

                // Release the temporary RenderTexture
                RenderTexture.ReleaseTemporary(tmp);

                // "textureCopy" now has the same pixels from "texture" and it's readable
                Texture2D newTexture = new Texture2D(texture.width, texture.height);
                newTexture.SetPixels(textureCopy.GetPixels());
                newTexture.Apply();

                return newTexture;
            }

            public static void InitIconSize()
            {
                int newSize = 32;
                if (mapSize.Value == MapSize.Small)
                    newSize = 16;

                newSize = Mathf.CeilToInt(newSize * pinScale.Value);

                if (iconSize == newSize)
                    return;

                pinIcons.Clear(); // Need to rebuild icon cache
                iconSize = newSize;
            }

        }

        public sealed class ValueChangedEventArgs<TValue> : EventArgs
        {
            public ValueChangedEventArgs(TValue newValue)
            {
                NewValue = newValue;
            }

            public TValue NewValue { get; }
        }

    }
}
