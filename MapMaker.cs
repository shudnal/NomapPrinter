using BepInEx;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using static NomapPrinter.NomapPrinter;
using UnityEngine;
using System.Diagnostics;
using HarmonyLib;
using System.Text;

namespace NomapPrinter
{
    public static class MapMaker
    {
        public class WorldMapData
        {
            public const int textureSize = 4096; // original = 2048
            public const int pixelSize = 6; // original = 12

            public const string cacheFileName = "worldData";
            public const int version = 1;

            public long worldUID;

            public Thread[] threads;

            public bool initialized = false;

            public Texture2D m_mapTexture;
            public Texture2D m_forestTexture;
            public Texture2D m_heightmap;

            public WorldMapData(long worldUID)
            {
                this.worldUID = worldUID;
            }

            public IEnumerator Init()
            {
                if (initialized)
                {
                    threads = null;
                    yield break;
                }

                if (LoadFromCache())
                {
                    LogInfo("World data loaded from cache");
                    initialized = true;
                    yield break;
                }

                yield return new WaitUntil(() => WorldGenerator.instance != null && ZoneSystem.instance != null);

                Color32[] m_mapTextureArray = new Color32[textureSize * textureSize];
                Color[] m_forestTextureArray = new Color[textureSize * textureSize];
                Color32[] m_heightmapArray = new Color32[textureSize * textureSize];

                if (!initialized && threads == null)
                {
                    threads = new Thread[4]
                    {
                        new Thread(() => FillMapPart(0, 0, m_mapTextureArray, m_forestTextureArray, m_heightmapArray)),
                        new Thread(() => FillMapPart(0, 1, m_mapTextureArray, m_forestTextureArray, m_heightmapArray)),
                        new Thread(() => FillMapPart(1, 0, m_mapTextureArray, m_forestTextureArray, m_heightmapArray)),
                        new Thread(() => FillMapPart(1, 1, m_mapTextureArray, m_forestTextureArray, m_heightmapArray))
                    };

                    threads.Do(thread => thread.Start());
                }

                yield return new WaitWhile(() => threads != null && threads.Any(thread => thread.IsAlive));

                m_mapTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGB24, mipChain: false)
                {
                    name = "NomapPrinter_m_mapTexture",
                    wrapMode = TextureWrapMode.Clamp
                };

                m_forestTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "NomapPrinter_m_forestTexture",
                    wrapMode = TextureWrapMode.Clamp
                };

                m_heightmap = new Texture2D(textureSize, textureSize, TextureFormat.RGB24, mipChain: false)
                {
                    name = "NomapPrinter_m_heightmap",
                    wrapMode = TextureWrapMode.Clamp
                };

                m_mapTexture.SetPixels32(m_mapTextureArray);
                m_mapTexture.Apply();

                yield return null;

                m_forestTexture.SetPixels(m_forestTextureArray);
                m_forestTexture.Apply();

                yield return null;

                m_heightmap.SetPixels32(m_heightmapArray);
                m_heightmap.Apply();

                yield return null;

                threads = null;

                initialized = true;

                SaveToCache();
            }

            private void FillMapPart(int partX, int partY, Color32[] mapTextureArray, Color[] forestTextureArray, Color32[] heightmapArray)
            {
                for (int i = partX * textureSize / 2; i < (partX + 1) * textureSize / 2; i++)
                    for (int j = partY * textureSize / 2; j < (partY + 1) * textureSize / 2; j++)
                        FillMap(i, j, mapTextureArray, forestTextureArray, heightmapArray);
            }

            private void FillMap(int i, int j, Color32[] mapTextureArray, Color[] forestTextureArray, Color32[] heightmapArray)
            {
                float wy = (i - textureSize / 2) * pixelSize + pixelSize / 2f;
                float wx = (j - textureSize / 2) * pixelSize + pixelSize / 2f;

                int pos = i * textureSize + j;

                if (DUtils.Length(wx, wy) > worldSize.Value)
                {
                    mapTextureArray[pos] = MapGenerator.abyssColor;
                    return;
                }

                Heightmap.Biome biome = WorldGenerator.instance.GetBiome(wx, wy);
                float biomeHeight = WorldGenerator.instance.GetBiomeHeight(biome, wx, wy, out _);

                // Black outside the actual map
                if (biomeHeight < abyss_depth)
                {
                    mapTextureArray[pos] = MapGenerator.abyssColor;
                    return;
                }

                float height = biomeHeight - ZoneSystem.instance.m_waterLevel;

                forestTextureArray[pos] = GetMaskColor(wx, wy, height, biome);

                if (height > 0)
                    heightmapArray[pos] = new Color(height / Mathf.Pow(2, heightmapFactor + 1), 0f, 0f);
                else
                    heightmapArray[pos] = new Color(0f, 0f, height / -Mathf.Pow(2, heightmapFactor + (biome == Heightmap.Biome.Swamp ? 1 : 0)));

                mapTextureArray[pos] = GetPixelColor(biome, biomeHeight);
            }

            private string CacheFile()
            {
                return Path.Combine(CacheDirectory(), $"{ZNet.instance.GetWorldName()}_{cacheFileName}");
            }

            private bool LoadFromCache()
            {
                string filename = CacheFile();
                if (!File.Exists(filename))
                {
                    LogInfo($"File not found: {filename}");
                    return false;
                }

                byte[] data;
                try
                {
                    data = File.ReadAllBytes(filename);
                }
                catch (Exception e)
                {
                    LogWarning($"Error reading file ({filename})! Error: {e.Message}");
                    return false;
                }

                ZPackage zPackage = new ZPackage(data);
                if (version != zPackage.ReadInt())
                {
                    LogWarning($"World data ({filename}): Version mismatch");
                    return false;
                }

                try
                {
                    worldUID = zPackage.ReadLong();
                    if (textureSize != zPackage.ReadInt())
                        return false;

                    if (pixelSize != zPackage.ReadInt())
                        return false;

                    if (m_mapTexture == null)
                        m_mapTexture = new Texture2D(2, 2);

                    m_mapTexture.LoadImage(zPackage.ReadByteArray());

                    if (m_forestTexture == null)
                        m_forestTexture = new Texture2D(2, 2);

                    m_forestTexture.LoadImage(zPackage.ReadByteArray());

                    if (m_heightmap == null)
                        m_heightmap = new Texture2D(2, 2);

                    m_heightmap.LoadImage(zPackage.ReadByteArray());
                }
                catch (Exception e)
                {
                    LogWarning($"Error reading file ({filename})! Error: {e.Message}");
                    DestroyTextures();
                    return false;
                }

                return true;
            }

            private void SaveToCache()
            {
                ZPackage zPackage = new ZPackage();
                zPackage.Write(version);

                zPackage.Write(worldUID);
                zPackage.Write(textureSize);
                zPackage.Write(pixelSize);

                zPackage.Write(m_mapTexture.EncodeToPNG());
                zPackage.Write(m_forestTexture.EncodeToPNG());
                zPackage.Write(m_heightmap.EncodeToPNG());

                string filename = CacheFile();

                try
                {
                    File.WriteAllBytes(filename, zPackage.GetArray());
                }
                catch (Exception e)
                {
                    LogWarning($"Error writing file ({filename})! Error: {e.Message}");
                }
            }

            internal void DestroyTextures()
            {
                UnityEngine.Object.Destroy(m_mapTexture);
                UnityEngine.Object.Destroy(m_forestTexture);
                UnityEngine.Object.Destroy(m_heightmap);
            }
        }

        public class ExploredMapData
        {
            public const string exploredMapFileName = "mapData";
            public const int version = 2;

            public Texture2D exploredMap;
            public MapType exploredMapType;

            public void Init(Color32[] mapData, MapType mapType)
            {
                int resolution = (int)Math.Sqrt(mapData.Length);

                exploredMap = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, false);
                exploredMap.SetPixels32(mapData);
                exploredMap.Apply(false);

                exploredMapType = mapType;

                SaveExploredMap();
            }

            private string ExploredMapFileName()
            {
                return Path.Combine(CacheDirectory(), $"{exploredMapFileName}_{exploredMapType}");
            }

            private string[] ExploredMapFileNames(bool packed)
            {
                return new string[2] {
                    Path.Combine(configDirectory, $"{exploredMapType}.{worldUID}.explored.{(packed ? "zpack" : "png")}"),
                    Path.Combine(configDirectory, $"{exploredMapType}.{ZNet.instance.GetWorldName()}.explored.{(packed ? "zpack" : "png")}"),
                };
            }

            private bool LoadFromCustomFile()
            {
                if (!useCustomExploredLayer.Value)
                    return false;

                exploredMap = GetLayerTexture("explored", syncExploredLayerFromServer.Value);

                return exploredMap != null;
            }

            public bool LoadExploredMap()
            {
                if (exploredMap != null && exploredMapType == mapType.Value)
                    return true;

                ResetExploredMap();

                return LoadFromCustomFile() || LoadExploredMapFromFile();
            }

            public void ResetExploredMap()
            {
                if (exploredMap != null)
                    UnityEngine.Object.Destroy(exploredMap);

                exploredMap = null;
                exploredMapType = mapType.Value;
            }

            private bool LoadExploredMapFromFile()
            {
                string filename = ExploredMapFileName();

                byte[] data = GetPackedImageData(filename);
                if (data == null)
                    return false;

                try
                {
                    exploredMap = new Texture2D(2, 2);
                    return exploredMap.LoadImage(data);
                }
                catch (Exception e)
                {
                    LogWarning($"Error loading map image ({filename})! Error: {e.Message}");
                }

                return false;
            }

            public static byte[] GetPackedImageData(string filename)
            {
                if (!File.Exists(filename))
                {
                    LogInfo($"File not found: {filename}");
                    return null;
                }

                byte[] data;
                try
                {
                    data = File.ReadAllBytes(filename);
                }
                catch (Exception e)
                {
                    LogWarning($"Error reading file ({filename})! Error: {e.Message}");
                    return null;
                }

                ZPackage zPackage = new ZPackage(data);
                if (version != zPackage.ReadInt())
                {
                    LogWarning($"World map data ({filename}): Version mismatch");
                    return null;
                }

                data = zPackage.ReadByteArray();

                byte[] key = new UTF8Encoding().GetBytes(pluginID);
                for (int i = 0; i < data.Length; i++)
                    data[i] ^= key[i % key.Length];

                return data;
            }

            public static byte[] GetPackedImageData(byte[] data)
            {
                ZPackage zPackage = new ZPackage();
                zPackage.Write(version);

                byte[] key = new UTF8Encoding().GetBytes(pluginID);
                for (int i = 0; i < data.Length; i++)
                    data[i] ^= key[i % key.Length];

                zPackage.Write(data);

                return zPackage.GetArray();
            }

            private void SaveExploredMap()
            {
                if (exploredMap == null)
                    return;

                string filename = ExploredMapFileName();

                try
                {
                    File.WriteAllBytes(filename, GetPackedImageData(exploredMap.EncodeToPNG()));
                }
                catch (Exception e)
                {
                    LogWarning($"Error writing file ({filename})! Error: {e.Message}");
                }
            }
        }

        public static WorldMapData worldMapData;
        public static ExploredMapData exploredMapData = new ExploredMapData();

        public const float abyss_depth = -100f;
        public const string worldDataPrefix = "NomapPrinter_Exploration_";
        public const int heightmapFactor = 8;
        public const int graduationLinesDensity = 8;

        public static bool isWorking = false;
        private static IEnumerator worker;

        public static Texture2D mapTexture = new Texture2D(4096, 4096, TextureFormat.RGB24, false);

        private static readonly Dictionary<string, Color32[]> pinIcons = new Dictionary<string, Color32[]>();

        private static Texture2D iconSpriteTexture;   // Current sprite texture is not readable. Saving a cached copy the first time the variable is accessed 
        private static int iconSize = 32;

        private static long worldUID;

        private static Texture2D noClouds;

        private static bool[] exploration;

        public static void ResetExploredMap()
        {
            exploredMapData?.ResetExploredMap();
        }

        public static void ResetExploredMapOnTextureChange()
        {
            if (useCustomExploredLayer.Value)
                exploredMapData?.ResetExploredMap();
        }

        public static void GenerateMap()
        {
            if (!saveMapToFile.Value && !Game.m_noMap)
                return;

            if (isWorking && worker != null)
            {
                instance.StopCoroutine(worker);
                worker = null;
            }

            worldUID = ZNet.instance.GetWorldUID();

            SavePlayerExploration(Player.m_localPlayer, worldUID);

            worker = CreateMap();

            instance.StartCoroutine(worker);
        }

        public static void PregenerateMap()
        {
            worldUID = ZNet.instance.GetWorldUID();
            instance.StartCoroutine(CreateMap(pregeneration: true));
        }

        private static string CacheDirectory()
        {
            Directory.CreateDirectory(cacheDirectory);
            string directory = Path.Combine(cacheDirectory, worldUID.ToString());
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static IEnumerator CreateMap(bool pregeneration = false)
        {
            isWorking = true;

            InitIconSize();

            if (mapType.Value == MapType.Vanilla)
            {
                ShowMessage(messageStart.Value);
                yield return GetVanillaMap(2048 * (int)mapSize.Value);
            }
            else
            {
                if (!pregeneration) ShowMessage(messageStart.Value);

                if (!exploredMapData.LoadExploredMap())
                {
                    yield return PrepareTerrainData();

                    if (!pregeneration) ShowMessage(messageSaving.Value);

                    yield return MapGenerator.Initialize();

                    yield return PrepareMap(mapType.Value);

                    exploredMapData.Init(MapGenerator.Result, mapType.Value);
                }
                else
                {
                    LogInfo("World map data loaded from cache");
                }

                yield return new WaitUntil(() => Player.m_localPlayer != null);

                if (GetPlayerExploration(Player.m_localPlayer, worldUID))
                {
                    MapGenerator.SetMapTexture(exploredMapData.exploredMap);

                    if (useCustomUnderFogLayer.Value)
                        yield return OverlayMarkingsLayer(overFog: false);

                    if (useCustomFogLayer.Value)
                        OverrideFogTexture();

                    yield return MapGenerator.OverlayExplorationFog(exploration);

                    if (useCustomOverFogLayer.Value)
                        yield return OverlayMarkingsLayer(overFog: true);

                    yield return ApplyMapTexture(MapGenerator.Result);

                    ShowMessage(messageReady.Value);
                }

                MapGenerator.DeInitializeTextures();
            }

            if (!pregeneration && saveMapToFile.Value)
            {
                string filename = filePath.Value;

                if (filename.IsNullOrWhiteSpace())
                {
                    filename = Path.Combine(localPath, "screenshots", $"{mapType.Value}.{ZNet.instance.GetWorldName()}.png");
                }
                else
                {
                    FileAttributes attr = File.GetAttributes(filename);
                    if (attr.HasFlag(FileAttributes.Directory))
                        filename = Path.Combine(filename, $"{mapType.Value}.{ZNet.instance.GetWorldName()}.png");
                }

                string filepath = Path.GetDirectoryName(filename);

                LogInfo($"Writing {filename}");
                var internalThread = new Thread(() =>
                {
                    Directory.CreateDirectory(filepath);
                    File.WriteAllBytes(filename, ImageConversion.EncodeToPNG(mapTexture));
                });

                internalThread.Start();
                while (internalThread.IsAlive == true)
                {
                    yield return null;
                }

                ShowMessage($"{messageSavedTo.Value} {filepath}", MessageHud.MessageType.TopLeft);
            }

            LogInfo("Finished Map Draw");

            isWorking = false;
        }

        private static IEnumerator PrepareTerrainData()
        {
            yield return new WaitUntil(() => WorldGenerator.instance != null);

            Stopwatch stopwatch = Stopwatch.StartNew();

            long uid = worldUID == 0L ? WorldGenerator.m_instance.m_world.m_uid : worldUID;

            worldMapData?.DestroyTextures();

            LogInfo($"Preparing terrain data for world {uid} started");

            worldMapData = new WorldMapData(uid);

            yield return worldMapData.Init();

            LogInfo($"Terrain data for world {uid} is ready in {stopwatch.ElapsedMilliseconds,-4:F2} ms");
        }

        private static IEnumerator PrepareMap(MapType mapType)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            MapGenerator.InitializeTextures(worldMapData.m_mapTexture, worldMapData.m_forestTexture, worldMapData.m_heightmap);

            switch (mapType)
            {
                case MapType.BirdsEye:
                    yield return MapGenerator.GenerateSatelliteImage();
                    break;
                case MapType.Topographical:
                    yield return MapGenerator.GenerateTopographicalMap(graduationLinesDensity);
                    break;
                case MapType.Chart:
                    yield return MapGenerator.GenerateChartMap(graduationLinesDensity);
                    break;
                case MapType.OldChart:
                    yield return MapGenerator.GenerateOldMap(graduationLinesDensity);
                    break;
                default:
                    goto case MapType.Chart;
            }

            LogInfo($"Prepared map data {mapType} for world {worldUID} in {stopwatch.ElapsedMilliseconds,-4:F2} ms");
        }

        private static bool GetPlayerExploration(Player player, long worldUID)
        {
            if (!player.m_customData.TryGetValue(WorldDataName(worldUID), out string exploredMapBase64))
                return false;

            BitArray ba = new BitArray(Utils.Decompress(Convert.FromBase64String(exploredMapBase64)));

            if (exploration == null || exploration.Length != ba.Count)
                exploration = new bool[ba.Count];

            ba.CopyTo(exploration, 0);

            return true;
        }

        private static void SavePlayerExploration(Player player, long worldUID)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            exploration = Minimap.instance.m_explored;

            if (showSharedMap.Value)
                for (int i = 0; i < exploration.Length; i++)
                    exploration[i] = exploration[i] || Minimap.instance.m_exploredOthers[i];

            BitArray ba = new BitArray(exploration);

            byte[] bytes = new byte[(ba.Length - 1) / 8 + 1];
            ba.CopyTo(bytes, 0);

            SaveValue(WorldDataName(worldUID), Convert.ToBase64String(Utils.Compress(bytes)));

            LogInfo($"Exploration saved in {stopwatch.ElapsedMilliseconds,-4:F2} ms");

            void SaveValue(string key, string value)
            {
                if (player.m_customData.ContainsKey(key))
                    player.m_customData[key] = value;
                else
                    player.m_customData.Add(key, value);
            }
        }

        private static string WorldDataName(long worldUID)
        {
            return worldDataPrefix + worldUID.ToString();
        }

        private static IEnumerator GetVanillaMap(int resolution)
        {
            yield return new WaitUntil(() => Player.m_localPlayer != null && Minimap.instance != null);

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

            // Replace shared map toggle
            bool m_showSharedMapData = Minimap.instance.m_showSharedMapData;
            bool replaceSharedMapToggle = showSharedMap.Value != Minimap.instance.m_showSharedMapData;

            if (replaceSharedMapToggle)
                material.SetFloat("_SharedFade", showSharedMap.Value ? 1f : 0f);

            // Store fog pixels to restore later
            Color32[] fogTex = Minimap.instance.m_fogTexture.GetPixels32();

            bool HaveExploration = GetPlayerExploration(Player.m_localPlayer, worldUID);

            // Combine fog for shared map
            bool combineFog = !preserveSharedMapFog.Value && showSharedMap.Value;
            Color[] pixels = Minimap.instance.m_fogTexture.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
                if (HaveExploration && !exploration[i])
                    pixels[i] = Color.white;
                else if (combineFog && pixels[i].g == 0f && pixels[i].r != 0f)
                    pixels[i].r = pixels[i].g;

            Minimap.instance.m_fogTexture.SetPixels(pixels);
            Minimap.instance.m_fogTexture.Apply();

            GameObject mapPanelObject = InitMapPanel(material);

            RenderTexture renderTexture = new RenderTexture(resolution, resolution, 24);
            RenderTexture.active = renderTexture;

            EnvSetup env = EnvMan.instance.GetCurrentEnvironment();
            float m_sunAngle = env.m_sunAngle;
            float m_smoothDayFraction = EnvMan.instance.m_smoothDayFraction;
            Vector3 m_dirLight = EnvMan.instance.m_dirLight.transform.forward;

            EnvMan.instance.m_smoothDayFraction = 0.5f;
            env.m_sunAngle = 60f;
            EnvMan.instance.SetEnv(env, 1, 0, 0, 0, Time.fixedDeltaTime);
            EnvMan.instance.m_dirLight.transform.forward = Vector3.down;

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

            Texture2D mapWithClouds = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
            mapWithClouds.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            Color32[] mapClouds = mapWithClouds.GetPixels32();
            UnityEngine.Object.Destroy(mapWithClouds);

            material.SetTexture("_CloudTex", noClouds);
            camera.Render();

            // Return clouds
            material.SetTexture("_CloudTex", clouds);
            EnvMan.instance.m_smoothDayFraction = m_smoothDayFraction;
            env.m_sunAngle = m_sunAngle;
            EnvMan.instance.m_dirLight.transform.forward = m_dirLight;

            EnvMan.s_lastFrame--;
            EnvMan.instance.FixedUpdate();

            // Return shared map toggle
            if (replaceSharedMapToggle)
                material.SetFloat("_SharedFade", m_showSharedMapData ? 1f : 0f);

            if (combineFog)
            {
                Minimap.instance.m_fogTexture.SetPixels32(fogTex);
                Minimap.instance.m_fogTexture.Apply();
            }

            if (!wasOpen)
                Minimap.instance.SetMapMode(Minimap.MapMode.Small);

            mapTexture.Reinitialize(resolution, resolution, TextureFormat.RGB24, false);
            mapTexture.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);

            RenderTexture.active = null;

            UnityEngine.Object.Destroy(mapPanelObject);
            UnityEngine.Object.Destroy(cameraObject);
            UnityEngine.Object.Destroy(renderTexture);

            Color32[] mask = Minimap.instance.m_mapTexture.GetPixels32();
            Color32[] forest = Minimap.instance.m_forestMaskTexture.GetPixels32();
            int resolutionMask = Minimap.instance.m_mapTexture.height;
            Color32[] map = mapTexture.GetPixels32();
            float[] lerp = new float[resolution * resolution];

            var internalThread = new Thread(() =>
            {
                for (int i = 0; i < resolutionMask; i++)
                {
                    for (int j = 0; j < resolutionMask; j++)
                    {
                        int mapSizeFactor = resolution / resolutionMask;

                        int pos = i * resolutionMask + j;

                        if (forest[pos].g <= 0.1f || forest[pos].r > 0f || forest[pos].b > 0f)
                            continue;

                        lerp[i * mapSizeFactor * resolution + j * mapSizeFactor] = forest[pos].g;

                        // Get map data in a small radius
                        for (int di = -3 * mapSizeFactor; di <= 3 * mapSizeFactor; di++)
                        {
                            for (int dj = -3 * mapSizeFactor; dj <= 3 * mapSizeFactor; dj++)
                            {
                                int x = Mathf.Clamp(i * mapSizeFactor + di, 0, resolution - 1);
                                int y = Mathf.Clamp(j * mapSizeFactor + dj, 0, resolution - 1);
                                lerp[x * resolution + y] += 0.1f;
                            }
                        }
                    }
                }

                for (int i = 0; i < lerp.Length; i++)
                    if (lerp[i] != 0f)
                        map[i] = Color32.Lerp(map[i], mapClouds[i], lerp[i]);

            });

            ShowMessage(messageSaving.Value);

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }

            if (showPins.Value)
                yield return AddPinsOnMap(map, resolution);

            mapTexture.SetPixels32(map);
            mapTexture.Apply(false);

            MapViewer.SetMapIsReady();
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

        private static IEnumerator ApplyMapTexture(Color32[] map)
        {
            int mapResolution = (int)Math.Sqrt(map.Length);
            if (mapSize.Value == MapSize.Smooth)
            {
                int currentMapSize = mapResolution;
                mapResolution = currentMapSize * 2;
                Color32[] doublemap = new Color32[mapResolution * mapResolution];

                yield return DoubleMapSize(map, doublemap, currentMapSize, mapResolution);
                map = doublemap;
            }

            if (showPins.Value)
                yield return AddPinsOnMap(map, mapResolution);

            mapTexture.Reinitialize(mapResolution, mapResolution, TextureFormat.RGB24, false);
            mapTexture.SetPixels32(map);
            mapTexture.Apply(false);

            MapViewer.SetMapIsReady();
        }

        private static void OverrideFogTexture()
        {
            Texture2D fog = GetLayerTexture("fog", syncFogLayerFromServer.Value);

            if (fog == null)
                return;

            MapGenerator.SetFogTexture(fog);

            UnityEngine.Object.Destroy(fog);
        }

        private static IEnumerator OverlayMarkingsLayer(bool overFog = false)
        {
            Texture2D markings = GetLayerTexture(overFog ? "overfog" : "underfog", 
                                                 overFog ? syncOverFogLayerFromServer.Value : syncUnderFogLayerFromServer.Value);

            if (markings == null)
                yield break;

            yield return MapGenerator.OverlayTextureOnMap(markings);

            UnityEngine.Object.Destroy(markings);
        }

        private static string[] CustomTextureFileNames(string textureType, string ext)
        {
            return new string[2] {
                    $"{mapType.Value}.{ZNet.instance.GetWorldUID()}.{textureType}.{ext}",
                    $"{mapType.Value}.{ZNet.instance.GetWorldName()}.{textureType}.{ext}",
                };
        }

        private static Texture2D GetLayerTexture(string layer, bool loadFromServer)
        {
            if (!TryGetLayerData(layer, loadFromServer, out byte[] data, out string source))
                return null;

            Texture2D tex = new Texture2D(2, 2);
            if (data?.Length == 0 || !tex.LoadImage(data))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }

            LogInfo($"Loaded {layer} layer from {source}");
            return tex;
        }

        private static bool TryGetLayerData(string layer, bool loadFromServer, out byte[] data, out string source)
        {
            data = null; source = null;
            if (loadFromServer)
            {
                string mapDataBase64 = GetServerTextureString(layer);
                if (mapDataBase64.IsNullOrWhiteSpace())
                    return false;

                data = Convert.FromBase64String(mapDataBase64);
                source = "server";
                return true;
            }
            
            if (Directory.Exists(configDirectory))
            {
                foreach (FileInfo file in new DirectoryInfo(configDirectory).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    data = GetLayerFileData(file.Name, file.FullName, layer);
                    if (data != null)
                    {
                        source = $"file {file.Name}";
                        return true;
                    }
                }
            }
            return false;
        }

        private static string GetServerTextureString(string layer)
        {
            return layer switch
            {
                "explored" => customLayerExplored.Value,
                "fog" => customLayerFog.Value,
                "underfog" => customLayerUnderfog.Value,
                "overfog" => customLayerOverfog.Value,
                _ => ""
            };
        }

        private static byte[] GetLayerFileData(string name, string fullname, string layer)
        {
            foreach (string filename in CustomTextureFileNames(layer, ext: "png"))
                if (name.Equals(filename, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        return File.ReadAllBytes(fullname);
                    }
                    catch (Exception e)
                    {
                        LogWarning($"Error reading png file ({name})! Error: {e.Message}");
                    }
                }

            foreach (string filename in CustomTextureFileNames(layer, ext: "zpack"))
                if (name.Equals(filename, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        return ExploredMapData.GetPackedImageData(fullname);
                    }
                    catch (Exception e)
                    {
                        LogWarning($"Error reading packed file ({name})! Error: {e.Message}");
                    }
                }

            return null;
        }

        internal static string GetTextureString(string filename, string fullname, string layer)
        {
            byte[] data = GetLayerFileData(filename, fullname, layer);
            if (data != null)
            {
                LogInfo($"Loaded {layer} layer from {filename}");
                return Convert.ToBase64String(data);
            }

            return null;
        }

        private static IEnumerator DoubleMapSize(Color32[] map, Color32[] doublemap, int currentMapSize, int mapSize)
        {
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

                if (row % 50 == 0)
                    yield return null;
            }
        }

        private static Color GetPixelColor(Heightmap.Biome biome, float height)
        {
            if (height < abyss_depth)
                return Color.black;

            switch (biome)
            {
                case Heightmap.Biome.Meadows:
                    return new Color(0.573f, 0.655f, 0.361f);
                case Heightmap.Biome.AshLands:
                    return new Color(0.48f, 0.125f, 0.125f);
                case Heightmap.Biome.BlackForest:
                    return new Color(0.42f, 0.455f, 0.247f);
                case Heightmap.Biome.DeepNorth:
                    return new Color(0.85f, 0.85f, 1f);  // Blueish color
                case Heightmap.Biome.Plains:
                    return new Color(0.906f, 0.671f, 0.47f);
                case Heightmap.Biome.Swamp:
                    return new Color(0.639f, 0.447f, 0.345f);
                case Heightmap.Biome.Mountain:
                    return Color.white;
                case Heightmap.Biome.Mistlands:
                    return new Color(0.3f, 0.2f, 0.3f);
                case Heightmap.Biome.Ocean:
                    return Color.blue;
                default:
                    return Minimap.instance.GetPixelColor(biome);
            }
        }

        private static Color GetMaskColor(float wx, float wy, float height, Heightmap.Biome biome)
        {
            Color result = MapGenerator.clearMask;

            if (height <= 0)
            {
                float oceanGradient = Mathf.Clamp01(WorldGenerator.GetAshlandsOceanGradient(wx, wy)) * 0.25f;
                if (oceanGradient > 0f)
                {
                    result.a = Mathf.Min(0.5f + oceanGradient / 2f, 0.99f);
                    return result;
                }
                else
                {
                    oceanGradient = Mathf.Clamp01(GetDeepNorthOceanGradient(wx, wy)) * 0.5f;
                    if (oceanGradient > 0f)
                    {
                        result.a = Mathf.Min(oceanGradient / 2f, 0.49f);
                    }
                }

                return result;
            }

            switch (biome)
            {
                case Heightmap.Biome.Meadows:
                    result.r = (WorldGenerator.InForest(new Vector3(wx, 0f, wy)) ? 1 : 0);
                    break;
                case Heightmap.Biome.Plains:
                    result.r = ((WorldGenerator.GetForestFactor(new Vector3(wx, 0f, wy)) < 0.8f) ? 1 : 0);
                    break;
                case Heightmap.Biome.BlackForest:
                    result.r = mapType.Value == MapType.OldChart ? 1f : 0.75f;
                    break;
                case Heightmap.Biome.Mistlands:
                    {
                        float forestFactor = WorldGenerator.GetForestFactor(new Vector3(wx, 0f, wy));
                        result.g = 1f - Utils.SmoothStep(1.1f, 1.3f, forestFactor);
                        break;
                    }
                case Heightmap.Biome.AshLands:
                    {
                        WorldGenerator.instance.GetAshlandsHeight(wx, wy, out var mask, cheap: true);
                        result.b = mask.a;
                        break;
                    }
            }

            return result;
        }

        private static float GetDeepNorthOceanGradient(float x, float y)
        {
            double num = (double)WorldGenerator.WorldAngle(x, y - WorldGenerator.ashlandsYOffset) * 100.0;
            return (float)(((double)DUtils.Length(x, y - WorldGenerator.ashlandsYOffset) - ((double)WorldGenerator.ashlandsMinDistance + num)) / 300.0);
        }

        private static IEnumerator AddPinsOnMap(Color32[] map, int mapSize)
        {
            foreach (KeyValuePair<Vector3, string> pin in GetPinsToPrint())
            {
                // get position in relative float instead of vector
                Minimap.instance.WorldToMapPoint(pin.Key, out float mx, out float my);

                // filter icons outside of map circle
                if (mx >= 1 || my >= 1 || mx <= 0 || my <= 0)
                    continue;

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
                                iconPix = Color32.Lerp(iconPix, MapGenerator.yellowMap, 0.33f * iconPix.a / 255f);
                            }

                            map[pos] = Color32.Lerp(map[pos], iconPix, iconPix.a / 255f); // alpha is relative for more immersive effect
                            map[pos].a = byte.MaxValue;  // make opaque again
                        }
                    }
                }

                yield return null;
            }
        }

        private static bool IsExplored(int x, int y)
        {
            Color explorationPos = Minimap.instance.m_fogTexture.GetPixel(x, y);
            return IsExploredColor(explorationPos);
        }

        private static bool IsExploredColor(Color explorationPos)
        {
            return explorationPos.r == 0f || showSharedMap.Value && explorationPos.g == 0f;
        }

        private static List<KeyValuePair<Vector3, string>> GetPinsToPrint()
        {
            List<KeyValuePair<Vector3, string>> pinsToPrint = new List<KeyValuePair<Vector3, string>>();    // key - map position, value - icon name

            if (!showPins.Value)
                return pinsToPrint;

            if (Minimap.instance == null)
                return pinsToPrint;

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

            return pinsToPrint;
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

            UnityEngine.Object.Destroy(result);

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
                Color[] defaultPixels = Enumerable.Repeat(Color.clear, texWid * texHei).ToArray();
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

        private static void InitIconSize()
        {
            int newSize = 32;

            newSize = Mathf.CeilToInt(newSize * pinScale.Value);

            if (iconSize == newSize)
                return;

            pinIcons.Clear(); // Need to rebuild icon cache
            iconSize = newSize;
        }
    }
}
