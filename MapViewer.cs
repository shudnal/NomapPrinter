using System.Collections.Generic;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using static NomapPrinter.NomapPrinter;
using BepInEx;
using HarmonyLib;
using System.IO;

namespace NomapPrinter
{
    internal static class MapViewer
    {
        private static bool mapWindowInitialized = false;
        public static GameObject parentObject;
        public static GameObject mapContent;
        public static RectTransform content;
        public static RectTransform viewport;

        public static Texture2D mapTexture = MapMaker.mapTexture;
        private static bool mapTextureIsReady = false;

        private static bool _displayingWindow = false;

        private static PropertyInfo _curLockState;
        private static PropertyInfo _curVisible;
        private static int _previousCursorLockState;
        private static bool _previousCursorVisible;

        private static DirectoryInfo pluginFolder;
        private static FileSystemWatcher fileSystemWatcher;

        private const string objectRootName = "NomapPrinter_Parent";
        private const string objectScrollViewName = "NomapPrinter_ScrollView";
        private const string objectViewPortName = "NomapPrinter_ViewPort";
        private const string objectMapName = "NomapPrinter_Map";
        
        private static readonly int layerUI = LayerMask.NameToLayer("UI");

        public static int hiddenFrames;

        private static bool CanOpenMap() => mapWindow.Value != MapWindow.Hide && mapWindow.Value != MapWindow.ShowOnInteraction;

        private static bool ForceCloseMap(Player player) => player == null || player.IsDead() || player.InCutscene() || player.IsTeleporting();

        private static bool DisplayingWindow
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
            }
        }

        public static void Start()
        {
            pluginFolder = new DirectoryInfo(Assembly.GetExecutingAssembly().Location).Parent;

            mapDataFromFile.ValueChanged += new Action(LoadMapFromSharedValue);

            var tCursor = typeof(Cursor);
            _curLockState = tCursor.GetProperty("lockState", BindingFlags.Static | BindingFlags.Public);
            _curVisible = tCursor.GetProperty("visible", BindingFlags.Static | BindingFlags.Public);
        }

        public static void Update()
        {
            if (!mapWindowInitialized)
                return;

            if (ZInput.VirtualKeyboardOpen)
                return;

            Player localPlayer = Player.m_localPlayer;
            if (ForceCloseMap(localPlayer))
            {
                DisplayingWindow = false;
                return;
            }

            if (DisplayingWindow)
                hiddenFrames = 0;
            else
                hiddenFrames++;

            if (DisplayingWindow)
            {
                if (ZInput.GetKeyDown(KeyCode.Escape) 
                 || ZInput.GetButtonDown("Map")
                 || ZInput.GetButtonDown("JoyMap") && (!ZInput.GetButton("JoyLTrigger") || !ZInput.GetButton("JoyLBumper"))
                 || ZInput.GetButtonDown("JoyButtonB"))
                    DisplayingWindow = false;
            }
            else if (CanOpenMap())
            {
                if (localPlayer.TakeInput() && (ZInput.GetButtonDown("Map")
                                            || (ZInput.GetButtonDown("JoyMap") && (!ZInput.GetButton("JoyLTrigger") || !ZInput.GetButton("JoyLBumper")) && !ZInput.GetButton("JoyAltKeys"))))
                    ShowMap();
            }

            if (DisplayingWindow)
            {
                if (ZInput.IsGamepadActive())
                    UpdateGamepad(Time.deltaTime);

                if (ZInput.IsMouseActive())
                    UpdateMouse();
            }
        }

        public static void UpdateMouse()
        {
            // enable cursor if map is displaying
            SetUnlockCursor(0, true);

            if (ZInput.GetMouseButton(1))//(UnityInput.Current.GetMouseButtonDown(1)) // Right click to reset zoom
                ZoomMap(0);
            
            if (ZInput.GetMouseButton(2)) // (UnityInput.Current.GetMouseButtonDown(2)) // Middle click to reset position
                CenterMap();
            
            // enable scroll to change map scale
            float scrollIncrement = ZInput.GetMouseScrollWheel() * 0.02f;
            if (scrollIncrement != 0)
                ZoomMap(scrollIncrement);
        }

        public static void UpdateGamepad(float dt)
        {
            if (ZInput.GetButton("JoyRStick")) // Right stick to reset zoom
                ZoomMap(0);
            
            if (ZInput.GetButton("JoyLStick")) // Left click to reset position
                CenterMap();

            if (ZInput.GetButton("JoyLTrigger"))
                ZoomMap(-mapGamepadZoomSpeed.Value * dt * 1.5f);

            if (ZInput.GetButton("JoyRTrigger"))
                ZoomMap(mapGamepadZoomSpeed.Value * dt * 1.5f);

            MoveMap(-ZInput.GetJoyLeftStickX(smooth: true), ZInput.GetJoyLeftStickY(), dt);
        }

        public static void ShowMap()
        {
            if (!IsMapReady())
                ShowMessage(messageNotReady.Value);
            else
                DisplayingWindow = true;
        }

        public static void ShowInteractiveMap()
        {
            if (!Game.m_noMap)
                return;

            if (!allowInteractiveMapOnWrite.Value)
                return;

            Game.m_noMap = false;

            Minimap.instance.inputDelay = 1f;
            Minimap.instance.SetMapMode(Minimap.MapMode.Large);

            Game.m_noMap = true;
        }

        public static void SetupSharedMapFileWatcher()
        {
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

            string mapFileName = Path.Combine(fileSystemWatcher.Path, fileSystemWatcher.Filter);

            fileSystemWatcher.Changed += new FileSystemEventHandler(MapFileChanged);
            fileSystemWatcher.Created += new FileSystemEventHandler(MapFileChanged);
            fileSystemWatcher.Renamed += new RenamedEventHandler(MapFileChanged);
            fileSystemWatcher.IncludeSubdirectories = true;
            fileSystemWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            fileSystemWatcher.EnableRaisingEvents = mapStorage.Value == MapStorage.LoadFromSharedFile;

            LogInfo($"Watcher {(fileSystemWatcher.EnableRaisingEvents ? "active" : "inactive")}: {mapFileName}");

            AssignMapDataFromSharedFile(mapFileName);
        }

        public static void SetMapIsReady(bool ready = true)
        {
            mapTextureIsReady = ready;
            ResetContent();
        }

        public static bool IsMapReady() => mapTextureIsReady;

        private static void SetUnlockCursor(int lockState, bool cursorVisible)
        {
            if (_curLockState != null)
            {
                _curLockState.SetValue(null, lockState, null);
                _curVisible.SetValue(null, cursorVisible, null);
            }
        }

        private static void ZoomMap(float increment)
        {
            if (increment.Equals(0))
            {
                content.localScale = Vector3.one;
                return;
            }

            if (ZInput.IsMouseActive() && !RectTransformUtility.RectangleContainsScreenPoint(viewport, UnityInput.Current.mousePosition))
                return;

            float scaleIncrement = increment / 2;
            float minScale = Mathf.Max(mapMinimumScale.Value, mapSize.Value == MapSize.Normal ? 0.4f: 0.25f);
            float maxScale = Mathf.Min(mapMaximumScale.Value, 2f);

            float scale = Mathf.Clamp(content.localScale.x + scaleIncrement, minScale, maxScale);
            
            if (content.localScale.x != scale)
            {
                content.localScale = new Vector2(scale, scale);
                if (ZInput.IsMouseActive())
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(content, UnityInput.Current.mousePosition, null, out Vector2 relativeMousePosition);
                    content.localPosition -= new Vector3(relativeMousePosition.x, relativeMousePosition.y) * scaleIncrement;
                }
                else if (ZInput.IsGamepadActive())
                {
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(content, new Vector2(Screen.width / 2, Screen.height / 2), null, out Vector2 relativeMousePosition);
                    content.localPosition -= new Vector3(relativeMousePosition.x, relativeMousePosition.y) * scaleIncrement;
                }
            }
        } 

        private static void CenterMap()
        {
            content.localPosition = Vector3.zero;
            content.anchoredPosition = Vector3.zero;
        }

        private static void MoveMap(float deltaX, float deltaY, float dt)
        {
            content.localPosition += new Vector3(deltaX, deltaY) * dt * 1000f * mapGamepadMoveSpeed.Value;
        }

        private static void ResetContent()
        {
            if (!mapWindowInitialized)
                return;

            content.sizeDelta = new Vector2(mapTexture.width, mapTexture.height);
            mapContent.GetComponent<Image>().sprite = Sprite.Create(mapTexture, new Rect(0, 0, mapTexture.width, mapTexture.height), Vector2.zero);

            ZoomMap(0);
            CenterMap();
        }

        private static void AddIngameView(Transform parentTransform)
        {
            // Parent object to set visibility
            parentObject = new GameObject(objectRootName, typeof(RectTransform))
            {
                layer = layerUI
            };
            parentObject.transform.SetParent(parentTransform, false);

            // Parent rect with size of fullscreen
            RectTransform pRectTransform = parentObject.GetComponent<RectTransform>();
            pRectTransform.anchoredPosition = Vector2.zero;
            pRectTransform.anchorMin = Vector2.zero;
            pRectTransform.anchorMax = Vector2.one;
            pRectTransform.sizeDelta = Vector2.zero;

            // ScrollView object to operate the map
            GameObject mapScrollView = new GameObject(objectScrollViewName, typeof(RectTransform))
            {
                layer = layerUI
            };
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
            GameObject mapViewPort = new GameObject(objectViewPortName, typeof(RectTransform))
            {
                layer = layerUI
            };
            mapViewPort.transform.SetParent(mapScrollView.transform, false);

            // Auto applied mask
            mapViewPort.AddComponent<RectMask2D>();
            mapViewPort.AddComponent<Image>().color = new Color(0, 0, 0, 0);

            // Viewport rect is on 6 pixels less then Scrollview to make borders
            viewport = mapViewPort.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.sizeDelta = new Vector2(-12f, -12f);
            viewport.anchoredPosition = Vector2.zero;

            // Content object to contain the Map image
            mapContent = new GameObject(objectMapName, typeof(RectTransform))
            {
                layer = layerUI
            };
            mapContent.transform.SetParent(mapViewPort.transform);

            // Map rect is full size. It must be child of Viewport
            content = mapContent.GetComponent<RectTransform>();
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

            LogInfo("Ingame drawed map added to hud");
        }

        private static void MapFileChanged(object sender, FileSystemEventArgs eargs)
        {
            AssignMapDataFromSharedFile(eargs.FullPath);
        }

        private static void AssignMapDataFromSharedFile(string filename)
        {
            string fileData = "";

            if (File.Exists(filename))
            {
                try
                {
                    fileData = Convert.ToBase64String(File.ReadAllBytes(filename));
                }
                catch (Exception e)
                {
                    LogInfo($"Error reading file ({filename})! Error: {e.Message}");
                }
            }
            else
            {
                LogInfo($"Can't find file ({filename})!");
            }

            mapDataFromFile.AssignValueSafe(fileData);
        }

        private static void LoadMapFromSharedValue()
        {
            if (LoadMapFromSharedFile())
                SetMapIsReady();
        }

        private static bool LoadMapFromSharedFile()
        {
            if (mapStorage.Value != MapStorage.LoadFromSharedFile || mapDataFromFile.Value.IsNullOrWhiteSpace())
                return false;

            try
            {
                mapTexture.LoadImage(Convert.FromBase64String(mapDataFromFile.Value));
                mapTexture.Apply();
            }
            catch (Exception ex)
            {
                LogInfo($"Loading map error. Invalid printed map texture: {ex}");
                return false;
            }

            return true;
        }

        private static string SanitizeFilePart(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim('.') == string.Empty)
                return fallback;

            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');

            return value;
        }

        private static string LocalFileName(Player player)
        {
            string playerName = SanitizeFilePart(player?.GetPlayerName(), "UnknownPlayer");
            string worldName = SanitizeFilePart(ZNet.instance?.GetWorldName(), "UnknownWorld");
            string folderName = localFolder.Value.IsNullOrWhiteSpace() ? pluginID : localFolder.Value;

            return Path.Combine(localPath, folderName, $"{pluginID}.{playerName}.{worldName}.png");
        }

        private static bool LoadMapFromLocalFile(Player player)
        {
            string filename = LocalFileName(player);

            if (!File.Exists(filename))
            {
                LogInfo($"Can't find file ({filename})!");
                return false;
            }

            try
            {
                LogInfo($"Loading nomap data from {filename}");
                mapTexture.LoadImage(File.ReadAllBytes(filename));
                mapTexture.Apply();
            }
            catch (Exception ex)
            {
                LogInfo($"Loading map error. Invalid printed map texture: {ex}");
                return false;
            }

            return true;
        }

        private static void SaveMapToLocalFile(Player player)
        {
            if (!modEnabled.Value)
                return;

            if (player == null || player != Player.m_localPlayer)
                return;

            if (mapStorage.Value != MapStorage.LocalFolder)
                return;

            if (!IsMapReady())
                return;

            string filename = LocalFileName(player);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filename));

                LogInfo($"Saving nomap data to {filename}");
                File.WriteAllBytes(filename, ImageConversion.EncodeToPNG(mapTexture));
            }
            catch (Exception ex)
            {
                LogInfo($"Saving map to local file error:\n{ex}");
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Save))]
        public static class Player_Save_SaveMapData
        {
            public static void Prefix(Player __instance)
            {
                SaveMapToLocalFile(__instance);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        public static class Player_Load_LoadMapData
        {
            public static void Postfix(Player __instance)
            {
                if (!modEnabled.Value)
                    return;

                if (__instance == null || __instance != Player.m_localPlayer)
                    return;

                if (mapStorage.Value == MapStorage.LocalFolder && LoadMapFromLocalFile(__instance))
                    SetMapIsReady();
                else if (mapStorage.Value == MapStorage.Character)
                    MapMaker.PregenerateMap();
            }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        public static class Hud_Awake_AddIngameView
        {
            public static void Postfix(Hud __instance)
            {
                if (!modEnabled.Value)
                    return;

                if (!__instance.m_rootObject.transform.Find(objectRootName))
                    AddIngameView(__instance.m_rootObject.transform);

                SetupSharedMapFileWatcher();
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.IsOpen))]
        public static class Minimap_IsOpen_EmulateMinimapOpenStatus
        {
            public static void Postfix(ref bool __result)
            {
                if (!modEnabled.Value)
                    return;

                if (!Game.m_noMap)
                    return;

                if (!__result && mapWindowInitialized)
                {
                    if (!DisplayingWindow)
                        __result = hiddenFrames <= 2;
                    else
                        __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.ShowPinNameInput))]
        public static class Minimap_ShowPinNameInput_PreventPinAddition
        {
            public static bool Prefix()
            {
                if (!modEnabled.Value)
                    return true;

                if (!Game.m_noMap)
                    return true;

                return !(allowInteractiveMapOnWrite.Value && preventPinAddition.Value);
            }
        }
    }
}
