using System;
using System.Collections;
using System.Threading;
using UnityEngine;
using static NomapPrinter.NomapPrinter;

namespace NomapPrinter
{
    internal static class MapGenerator
    {
        private const int _textureSize = 4096; // original = 2048

        public static int TextureSize
        {
            get => (int)(_textureSize * mapSizeMultiplier.Value);
        }

        private static bool[] _exploredData;
        private static Color32[] _mapTexture;
        private static Color[] _forestTexture;
        private static Color32[] _heightmap;
        private static Color32[] _result;

        private static Color32[] MapTexture
        {
            get
            {
                _mapTexture ??= new Color32[TextureSize * TextureSize];

                return _mapTexture;
            }

            set => _mapTexture = value;
        }

        private static Color[] ForestTexture
        {
            get
            {
                _forestTexture ??= new Color[TextureSize * TextureSize];

                return _forestTexture;
            }

            set => _forestTexture = value;
        }

        private static Color32[] Heightmap
        {
            get
            {
                _heightmap ??= new Color32[TextureSize * TextureSize];

                return _heightmap;
            }

            set => _heightmap = value;
        }

        private static bool[] ExploredData
        {
            get
            {
                _exploredData ??= new bool[TextureSize * TextureSize];

                return _exploredData;
            }

            set => _exploredData = value;
        }

        private static Color32[] space;
        private static int spaceRes;

        private static Color32[] fog;
        private static int fogRes;

        public static Color32[] Result
        {
            get
            {
                _result ??= new Color32[TextureSize * TextureSize];

                return _result;
            }

            set => _result = value;
        }

        public static readonly Color32 yellowMap = new Color32(203, 155, 87, byte.MaxValue);
        public static readonly Color32 oceanColor = new Color32(20, 100, 255, byte.MaxValue);
        public static readonly Color32 abyssColor = new Color32(0, 0, 0, byte.MaxValue);
        public static readonly Color32 lavaColor = new Color32(205, 51, 15, byte.MaxValue);
        public static readonly Color32 northColor = new Color32(170, 173, 194, byte.MaxValue);
        public static readonly Color32 mistColor = new Color32(217, 140, 166, byte.MaxValue); // 227, 172, 188
        public static readonly Color clearMask = new Color(0f, 0f, 0f, 1f);

        public static IEnumerator Initialize()
        {
            if (spaceRes == 0 && (mapType.Value == MapType.BirdsEye || mapType.Value == MapType.Topographical))
            {
                yield return new WaitUntil(() => Minimap.instance != null && Minimap.instance.m_mapLargeShader != null);

                Texture spaceTex = Minimap.instance.m_mapLargeShader.GetTexture("_SpaceTex");

                spaceRes = spaceTex.width;

                RenderTexture tmp = RenderTexture.GetTemporary(spaceRes, spaceRes, 24);

                Graphics.Blit(spaceTex, tmp);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = tmp;

                Texture2D tex = new Texture2D(spaceRes, spaceRes, TextureFormat.RGBA32, false, false);
                tex.ReadPixels(new Rect(0, 0, spaceRes, spaceRes), 0, 0);
                tex.Apply();

                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(tmp);

                space = tex.GetPixels32();

                UnityEngine.Object.Destroy(tex);
            }
        }

        public static void InitializeTextures(Texture2D biomes, Texture2D forests, Texture2D height)
        {
            MapTexture = biomes.GetPixels32();
            ForestTexture = forests.GetPixels();
            Heightmap = height.GetPixels32();
        }

        public static void DeInitializeTextures()
        {
            MapTexture = null;
            ForestTexture = null;
            Heightmap = null;
            Result = null;
            fog = null;
        }

        public static void SetFogTexture(Texture2D fog)
        {
            SetFogTexture(fog.GetPixels32());
        }

        public static void SetFogTexture(Color32[] newFog)
        {
            fog = new Color32[newFog.Length];
            fogRes = (int)Math.Sqrt(newFog.Length);
            newFog.CopyTo(fog, 0);
        }

        public static void SetMapTexture(Texture2D map)
        {
            SetMapTexture(map.GetPixels32());
        }

        public static void SetMapTexture(Color32[] map)
        {
            map.CopyTo(MapTexture, 0);
        }

        public static IEnumerator OverlayTextureOnMap(Texture2D texture)
        {
            texture.GetPixels32().CopyTo(Result, 0);

            yield return OverlayResultOnMap();
        }

        public static IEnumerator OverlayExplorationFog(bool[] exploration)
        {
            for (int i = 0; i < ExploredData.Length; i++)
                ExploredData[i] = false;

            if (exploration.Length == ExploredData.Length)
                exploration.CopyTo(ExploredData, 0);
            else
            {
                int targetSize = (int)Math.Sqrt(ExploredData.Length);
                int currentSize = (int)Math.Sqrt(exploration.Length);
                for (int row = 0; row < currentSize; row++)
                {
                    for (int col = 0; col < currentSize; col++)
                    {
                        if (!exploration[row * currentSize + col])
                            continue;

                        SetExploredData(row * 2 * targetSize + col * 2);
                        SetExploredData(row * 2 * targetSize + col * 2 + 1);
                        SetExploredData((row * 2 + 1) * targetSize + col * 2);
                        SetExploredData((row * 2 + 1) * targetSize + col * 2 + 1);

                        static void SetExploredData(int position)
                        {
                            if (position >= ExploredData.Length)
                                return;

                            ExploredData[position] = true;
                        }
                    }
                }
            }
            
            yield return StylizeFog();

            yield return OverlayResultOnMap();
        }

        public static IEnumerator GenerateOldMap(int graduationHeight)
        {
            yield return GenerateOceanTexture(Heightmap, MapTexture, 0.25f);
            Color32[] oceanTexture = Result;

            yield return ReplaceAbyssWithColor(MapTexture, abyssColor, yellowMap);    //Replace void with "Map colour"
            Color32[] outtex = Result;

            yield return OverlayTexture(outtex, oceanTexture);
            outtex = Result;

            yield return GetSolidColour(yellowMap);    //Yellowize map
            Color32[] offYellow = Result;
            yield return LerpTextures(outtex, offYellow);
            outtex = Result;
            yield return LerpTextures(outtex, offYellow);
            outtex = Result;
            yield return LerpTextures(outtex, offYellow);
            outtex = Result;

            yield return AddPerlinNoise(outtex, 128, 16);
            outtex = Result;

            yield return ApplyForestMaskTexture(outtex, ForestTexture, 0.95f);
            outtex = Result;

            yield return GenerateContourMap(Heightmap, graduationHeight, 128);
            Color32[] contours = Result;

            yield return OverlayTexture(outtex, contours);
        }

        public static IEnumerator GenerateChartMap(int graduationHeight)
        {
            yield return GenerateOceanTexture(Heightmap, MapTexture, 0.15f);
            Color32[] oceanTexture = Result;

            yield return ReplaceAbyssWithColor(MapTexture, abyssColor, yellowMap);    //Replace void with "Map colour"
            Color32[] outtex = Result;

            yield return OverlayTexture(outtex, oceanTexture);
            outtex = Result;

            yield return GetSolidColour(yellowMap);    //Yellowize map
            Color32[] offYellow = Result;

            yield return LerpTextures(outtex, offYellow);
            outtex = Result;

            yield return AddPerlinNoise(outtex, 128, 16);
            outtex = Result;

            yield return ApplyForestMaskTexture(outtex, ForestTexture);
            outtex = Result;

            yield return GenerateContourMap(Heightmap, graduationHeight, 128);
            Color32[] contours = Result;

            yield return OverlayTexture(outtex, contours);
        }

        public static IEnumerator GenerateSatelliteImage()
        {
            yield return GenerateOceanTexture(Heightmap, MapTexture);
            Color32[] oceanTexture = Result;

            yield return AddPerlinNoise(oceanTexture, 4, 64);
            oceanTexture = Result;

            yield return ReplaceAbyssWithSpace(MapTexture, abyssColor);    //Replace void with Space texture
            Color32[] outtex = Result;

            yield return OverlayTexture(outtex, oceanTexture);
            outtex = Result;

            yield return CreateShadowMap(Heightmap, 23);
            Color32[] shadowmap = Result;

            yield return DarkenTextureLinear(outtex, 20);
            outtex = Result;

            yield return ApplyForestMaskTexture(outtex, ForestTexture);
            outtex = Result;

            yield return GenerateContourMap(Heightmap, 128, 64);
            Color32[] contours = Result;

            yield return OverlayTexture(outtex, contours);
            outtex = Result;

            yield return OverlayTexture(outtex, shadowmap);
        }

        public static IEnumerator GenerateTopographicalMap(int graduationHeight)
        {
            yield return GenerateOceanTexture(Heightmap, MapTexture);
            Color32[] oceanTexture = Result;

            yield return AddPerlinNoise(oceanTexture, 4, 64);
            oceanTexture = Result;

            yield return ReplaceAbyssWithSpace(MapTexture, abyssColor);    //Replace void with Space texture
            Color32[] outtex = Result;

            yield return OverlayTexture(outtex, oceanTexture);
            outtex = Result;

            yield return CreateShadowMap(Heightmap, 23);
            Color32[] shadowmap = Result;

            yield return DarkenTextureLinear(outtex, 20);
            outtex = Result;

            yield return ApplyForestMaskTexture(outtex, ForestTexture);
            outtex = Result;

            yield return GenerateContourMap(Heightmap, graduationHeight, 128);
            Color32[] contours = Result;

            yield return OverlayTexture(outtex, contours);
            outtex = Result;

            yield return OverlayTexture(outtex, shadowmap);
        }

        private static IEnumerator OverlayResultOnMap()
        {
            var internalThread = new Thread(() =>
            {
                for (int i = 0; i < MapTexture.Length; i++)
                {
                    Result[i] = Color.Lerp((Color)MapTexture[i], (Color)Result[i], ((Color)Result[i]).a);
                    Result[i].a = (byte)(byte.MaxValue * Mathf.Clamp01(((Color)Result[i]).a + ((Color)MapTexture[i]).a));
                }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }

            SetMapTexture(Result);
        }

        private static IEnumerator OverlayTexture(Color32[] array1, Color32[] array2) //Tex2 on Tex1
        {
            Color32[] output = new Color32[array1.Length];

            var internalThread = new Thread(() =>
            {
                for (int i = 0; i < array1.Length; i++)
                {
                    float a = ((Color)array2[i]).a;
                    float b = ((Color)array1[i]).a;

                    Color workingColor = Color.Lerp((Color)array1[i], (Color)array2[i], a);
                    workingColor.a = a + b;
                    if (workingColor.a > 1) workingColor.a = 1;
                    output[i] = (Color32)workingColor;
                }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }

            Result = output;
        }

        private static IEnumerator LerpTextures(Color32[] array1, Color32[] array2)
        {
            Color32[] output = new Color32[TextureSize * TextureSize];

            var internalThread = new Thread(() =>
            {
                for (int i = 0; i < (TextureSize * TextureSize); i++)
                {
                    int a = array2[i].a - array1[i].a;

                    int finalA = Math.Min(array1[i].a + array2[i].a, 255);

                    int div = ((array1[i].a > array2[i].a) ? array1[i].a : array2[i].a) * 2;

                    float lerp = (((float)a) / div) + 0.5f;

                    output[i] = Color32.Lerp(array1[i], array2[i], lerp);
                    output[i].a = (byte)finalA;
                }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }

            Result = output;
        }

        private static IEnumerator DarkenTextureLinear(Color32[] array, byte d)
        {
            Color32[] output = new Color32[TextureSize * TextureSize];

            var internalThread = new Thread(() =>
            {
                for (int i = 0; i < TextureSize * TextureSize; i++)
                {
                    int bit;
                    bit = (array[i].r - d);
                    if (bit < 0) bit = 0;
                    output[i].r = (byte)bit;

                    bit = (array[i].g - d);
                    if (bit < 0) bit = 0;
                    output[i].g = (byte)bit;

                    bit = (array[i].b - d);
                    if (bit < 0) bit = 0;
                    output[i].b = (byte)bit;

                    output[i].a = array[i].a;
                }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }

            Result = output;
        }

        private static IEnumerator CreateShadowMap(Color32[] heightmap, byte intensity)
        {
            yield return CreateHardShadowMap(heightmap, intensity);
            Color32[] hardshadows = Result;

            yield return CreateSoftShadowMap(heightmap);
            Color32[] softshadows = Result;
            yield return LerpTextures(softshadows, hardshadows);
        }

        private static IEnumerator CreateSoftShadowMap(Color32[] input)
        {
            Color32[] output;

            output = new Color32[input.Length];

            var internalThread = new Thread(() =>
            {
                for (int i = 0; i < TextureSize; i++)
                {
                    for (int j = 0; j < TextureSize; j++)
                    {
                        int pos = i * TextureSize + j;
                        int pixel = i > 0 ? input[pos].r - input[(i - 1) * TextureSize + j].r : 0;

                        pixel *= 8;
                        byte abs = (byte)Math.Abs(pixel);
                        byte pix = pixel >= 0 ? byte.MaxValue : byte.MinValue;
                        output[pos] = new Color32(pix, pix, pix, abs);
                    }
                }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }

            Result = output;
        }

        private static IEnumerator CreateHardShadowMap(Color32[] input, byte intensity)
        {
            Color32[] output;

            output = new Color32[input.Length];

            var internalThread = new Thread(() =>
            {
                bool[] shaded = new bool[TextureSize * TextureSize];

                for (int i = 0; i < TextureSize * TextureSize; i++)
                    shaded[i] = false;

                for (int i = 0; i < TextureSize; i++)
                {
                    for (int j = 0; j < TextureSize; j++)
                    {
                        int pos = i * TextureSize + j;
                        if (shaded[pos] == false)
                        {
                            output[pos] = new Color32(255, 255, 255, 0);
                            int q = 1;
                            while ((i + q) < TextureSize)
                            {
                                if (input[pos].r > (input[(i + q) * TextureSize + j].r + (q * 2)))    //2/1 sun angle (the +q part at the end)
                                {
                                    shaded[(i + q) * TextureSize + j] = true;
                                }
                                else break;
                                q++;
                            }
                        }
                        else output[pos] = new Color32(0, 0, 0, intensity);
                    }
                }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }
            Result = output;
        }

        private static IEnumerator GenerateOceanTexture(Color32[] input, Color32[] biomeColor, float oceanLerpTarget = 0.1f)
        {
            Color32[] output = new Color32[input.Length];

            var internalThread = new Thread(() =>
            {
                for (int i = 0; i < TextureSize * TextureSize; i++)
                {
                    if (input[i].b == 0)
                    {
                        output[i] = Color.clear;
                        continue;
                    }

                    int correction = ((i / TextureSize) / (8 * 2)) - 128;           //correction goes from -128 to 127
                    int correction2 = ((i % TextureSize) / (8 * 2)) - 128;       //correction2 goes from -128 to 127
                    int correction3 = ((correction * correction) / 128) + ((correction2 * correction2) / 512);

                    if (correction < 0)
                    {
                        // South         83, 116, 196
                        output[i].r = (byte)(10 + correction3);
                        output[i].g = (byte)(136 - (correction3 / 4));
                        output[i].b = 193;
                    }
                    else
                    {
                        // North
                        output[i].r = (byte)(10 + (correction3 / 2));
                        output[i].g = 136;
                        output[i].b = (byte)(193 - (correction3 / 2));
                    }
                    output[i].a = (byte)Math.Min((input[i].b * 16) + 128, 255);

                    if (biomeColor[i] == Color.blue)
                        output[i] = Color32.Lerp(output[i], oceanColor, oceanLerpTarget);
                }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }

            Result = output;
        }

        private static IEnumerator GetPerlin(int tightness, byte damping)        //Damping reduces amplitude of noise
        {
            for (int i = 0; i < Result.Length; i++)
                Result[i] = new Color32(0, 0, 0, 0);

            var internalThread = new Thread(() =>
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    for (int y = 0; y < TextureSize; y++)
                    {
                        float sample = Mathf.PerlinNoise(((float)x) / tightness, ((float)y) / tightness);
                        sample = ((sample - 0.5f) / damping) + 0.5f;
                        Result[x * TextureSize + y] = new Color(sample, sample, sample, 0.2f);
                    }
                }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }
        }

        private static IEnumerator StylizeFog()
        {
            bool customFog = fog != null;
            if (!customFog)
            {
                yield return GetPerlin(128, 16);
                fog = Result;
                fogRes = TextureSize;
            }

            var internalThread = new Thread(() =>
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    for (int y = 0; y < TextureSize; y++)
                    {
                        int pos = x * TextureSize + y;

                        if (!ExploredData[pos])
                        {
                            Color32 fogPix = fog[x % fogRes * fogRes + y % fogRes];
                            if (customFog)
                                Result[pos] = fogPix;
                            else
                                Result[pos] = new Color32((byte)(yellowMap.r + (fogPix.r - 128)), (byte)(yellowMap.g + (fogPix.g - 128)), (byte)(yellowMap.b + (fogPix.b - 128)), 255);
                        }
                    }
                }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }
        }

        private static IEnumerator GenerateContourMap(Color32[] start, int graduations, byte alpha)
        {
            Color32[] input;
            Color32[] output;

            input = new Color32[start.Length];
            output = new Color32[input.Length];

            var internalThread = new Thread(() =>
            {
                for (int i = 0; i < (TextureSize * TextureSize); i++)    //Shift height values up by graduation so that coast is outlined with a contour line
                {
                    int newR = (start[i].b > 0) ? 0 : Math.Min(start[i].r + graduations, 255);
                    input[i].r = (byte)newR;
                }

                for (int y = 1; y < (TextureSize - 1); y++)
                {
                    int yCoord = y * TextureSize;
                    for (int x = 1; x < (TextureSize - 1); x++)
                    {
                        int testCoord = yCoord + x;    //Flattened 2D coords of pixel under test
                        int heightRef = input[yCoord + x].r / graduations;      //Which graduation does the height under test fall under?
                        output[testCoord] = Color.clear;     //Default color is clear

                        for (int i = -1; i < 2; i++)
                        {
                            int iCoord = i * TextureSize;
                            for (int j = -1; j < 2; j++)
                            {

                                if (!((i == 0) && (j == 0)))      //Don't check self
                                {
                                    int scanCoord = testCoord + iCoord + j; //Flattened 2D coords of adjacent pixel to be checked
                                    int testHeight = input[scanCoord].r / graduations;

                                    if (testHeight < heightRef)  //Is scanned adjacent coordinate in a lower graduation? //If so, this pixel is black
                                    {
                                        byte alpha2 = alpha;
                                        if ((heightRef % 5) - 1 != 0) alpha2 /= 2;  //Keep full alpha for every 5th graduation line. Half alpha for the rest 

                                        if ((i != 0) && (j != 0) && (output[testCoord].a != alpha2))       //Detected at diagonal
                                            output[testCoord] = new Color32(0, 0, 0, (byte)(alpha2 / 2));   //Gets half alpha for a smoother effect
                                        else                                                    //Detected at orthogonal
                                        {
                                            output[testCoord] = new Color32(0, 0, 0, (byte)(alpha2));   //Gets full alpha
                                            break;
                                        }

                                    }
                                }

                            }
                        }

                    }
                }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }
            Result = output;
        }

        private static IEnumerator AddPerlinNoise(Color32[] input, int tightness, byte damping)
        {
            var internalThread = new Thread(() =>
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    for (int y = 0; y < TextureSize; y++)
                    {
                        int pos = x * TextureSize + y;

                        Color start = input[pos];
                        float sample = Mathf.PerlinNoise(((float)x) / tightness, ((float)y) / tightness);
                        sample = ((sample - 0.5f) / damping);
                        Result[pos] = new Color(start.r + sample, start.g + sample, start.b + sample, start.a);
                    }
                }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }
        }

        private static IEnumerator GetSolidColour(Color32 TexColour)
        {
            Color32[] array = new Color32[TextureSize * TextureSize];

            var internalThread = new Thread(() =>
            {
                for (int i = 0; i < array.Length; i++)
                    array[i] = TexColour;
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }
            Result = array;
        }

        private static IEnumerator ReplaceAbyssWithColor(Color32[] input, Color32 from, Color32 to)
        {
            Color32[] output = new Color32[input.Length];

            var internalThread = new Thread(() =>
            {
                for (int i = 0; i < input.Length; i++)
                {
                    if ((input[i].r == from.r) && (input[i].g == from.g) && (input[i].b == from.b))
                        output[i] = to;
                    else
                        output[i] = input[i];
                }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }
            Result = output;
        }

        private static IEnumerator ReplaceAbyssWithSpace(Color32[] input, Color32 from)
        {
            Color32[] output = new Color32[input.Length];

            var internalThread = new Thread(() =>
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    for (int y = 0; y < TextureSize; y++)
                    {
                        int pos = x * TextureSize + y;

                        if ((input[pos].r == from.r) && (input[pos].g == from.g) && (input[pos].b == from.b))
                            output[pos] = space[x % spaceRes * spaceRes + y % spaceRes];
                        else
                            output[pos] = input[pos];
                    }
                }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }
            Result = output;
        }

        private static IEnumerator ApplyForestMaskTexture(Color32[] array, Color[] forestMask, float forestColorFactor = 0.9f)
        {
            Color32[] output = new Color32[TextureSize * TextureSize];

            var internalThread = new Thread(() =>
            {
                for (int i = 0; i < TextureSize * TextureSize; i++)
                {
                    if (forestMask[i] == clearMask)
                    {
                        output[i] = array[i];
                    }
                    else
                    {
                        // Forest darkening
                        if (forestMask[i].r > 0f)
                        { 
                            float factor = 1f - (1f - forestColorFactor) * forestMask[i].r;

                            output[i].r = (byte)(array[i].r * factor);
                            output[i].g = (byte)(array[i].g * factor);
                            output[i].b = (byte)(array[i].b * factor);
                        }

                        // Mistlands mist
                        if (forestMask[i].g > 0f)
                        {
                            float wy = ((float)(i / TextureSize) / TextureSize - 0.5f) * 400f;
                            float wx = ((float)(i % TextureSize) / TextureSize - 0.5f) * 400f;
                            output[i] = Color32.Lerp(array[i], mistColor, forestMask[i].g * GetMistlandsNoise(wx, wy) * 0.9f);
                        }

                        // Ashlands lava
                        if (forestMask[i].b > 0f)
                            output[i] = Color32.Lerp(array[i], lavaColor, forestMask[i].b);

                        // Ocean color
                        if (forestMask[i].a != 1f)
                        {
                            // Ashlands
                            if (forestMask[i].a >= 0.5f)
                                output[i] = Color32.Lerp(array[i], lavaColor, (forestMask[i].a - 0.5f) / 0.5f);

                            // DeepNorth
                            if (0f <= forestMask[i].a && forestMask[i].a < 0.5f)
                                output[i] = Color32.Lerp(array[i], northColor, forestMask[i].a / 0.5f);

                        }

                        output[i].a = array[i].a;
                    }
                }
            });

            internalThread.Start();
            while (internalThread.IsAlive == true)
            {
                yield return null;
            }

            Result = output;
        }

        public static float GetMistlandsNoise(float wx, float wy)
        {
            float e = 1 * Mathf.PerlinNoise(1 * wx, 1 * wy)
                    + 0.5f * Mathf.PerlinNoise(2 * wx, 2 * wy)
                    + 0.25f * Mathf.PerlinNoise(4 * wx, 4 * wy)
                    + 0.125f * Mathf.PerlinNoise(8 * wx, 8 * wy);
            return e / (1f + 0.5f + 0.25f + 0.125f);
        }
    }
}
