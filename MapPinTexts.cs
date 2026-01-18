using BepInEx;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using static NomapPrinter.NomapPrinter;
using Object = UnityEngine.Object;

namespace NomapPrinter
{
    internal static class MapPinTexts
    {
        private const int TextLayer = 31;
        private const string TextObjectName = "MapTMPText";
        private const string TextCamObjectName = "MapTMPTextCam";

        private static TMP_FontAsset defaultFontAsset = null;

        private static GameObject textGO;
        private static TextMeshPro tmp;
        private static GameObject camGO;
        private static Camera cam;
        private static RenderTexture rt;
        private static Texture2D readTex;

        private static readonly Dictionary<string, CachedText> textCache = new Dictionary<string, CachedText>();

        private struct CachedText
        {
            public Color32[] Pixels;
            public int Width;
            public int Height;
        }

        internal static void ClearPinFont()
        {
            defaultFontAsset = null;
            textCache.Clear();
        }

        private static void EnsureFontAsset()
        {
            if (defaultFontAsset != null)
                return;

            TMP_FontAsset fontCandidate = null;

            if (Minimap.instance?.m_pinNamePrefab?.GetComponentInChildren<TMP_Text>()?.font is TMP_FontAsset minimapFont)
                fontCandidate = minimapFont;

            if (!pinTextFont.Value.IsNullOrWhiteSpace())
            {
                foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (font.name.Contains(pinTextFont.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        defaultFontAsset = font;
                        return;
                    }

                    if (fontCandidate == null && font.name.Contains("Fallback", StringComparison.OrdinalIgnoreCase))
                        fontCandidate = font;
                }
            }

            if (defaultFontAsset == null)
            {
                defaultFontAsset = fontCandidate;
                
                if (!pinTextFont.Value.IsNullOrWhiteSpace())
                    if (defaultFontAsset != null)
                        LogInfo($"Unable to find font {pinTextFont.Value}, falling back to {defaultFontAsset.name}");
                    else
                        LogInfo($"Unable to find font {pinTextFont.Value} or fallback font");
            }
        }

        private static void EnsureRenderObjects()
        {
            if (textGO == null)
            {
                textGO = new GameObject(TextObjectName)
                {
                    layer = TextLayer
                };

                TMP_FontAsset previousFont = TMP_Settings.defaultFontAsset;
                TMP_Settings.defaultFontAsset = defaultFontAsset;

                tmp = textGO.AddComponent<TextMeshPro>();

                TMP_Settings.defaultFontAsset = previousFont;

                tmp.alignment = TextAlignmentOptions.Center;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.richText = false;
            }

            if (camGO == null)
            {
                camGO = new GameObject(TextCamObjectName)
                {
                    layer = TextLayer
                };
                cam = camGO.AddComponent<Camera>();
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.clear;
                cam.cullingMask = 1 << TextLayer;
            }
        }

        private static void ChangeRenderObjectsVisibility(bool active = true)
        {
            textGO?.SetActive(active);
            camGO?.SetActive(active);
        }

        private static void EnsureRenderTargets(int width, int height)
        {
            if (width <= 0 || height <= 0)
                return;

            if (rt == null || rt.width != width || rt.height != height)
            {
                rt?.Release();
                rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            }

            if (readTex == null || readTex.width != width || readTex.height != height)
            {
                if (readTex != null)
                    Object.Destroy(readTex);

                readTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            }
        }

        private static string BuildCacheKey(string text)
        {
            return $"{text}|{pinTextSize.Value}|{pinTextFontStyle.Value}|{pinTextFontColor.Value.r},{pinTextFontColor.Value.g},{pinTextFontColor.Value.b},{pinTextFontColor.Value.a}";
        }

        private static Color32[] RenderText(string text, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (text.IsNullOrWhiteSpace())
                return null;

            if (defaultFontAsset == null)
                return null;

            string key = BuildCacheKey(text);
            if (textCache.TryGetValue(key, out CachedText cached))
            {
                width = cached.Width;
                height = cached.Height;
                return cached.Pixels;
            }

            tmp.font = defaultFontAsset;
            tmp.text = text;
            tmp.fontSize = pinTextSize.Value * 10;
            tmp.fontStyle = pinTextFontStyle.Value;
            tmp.color = pinTextFontColor.Value;

            tmp.ForceMeshUpdate();

            Bounds b = tmp.textBounds;
            width = Mathf.CeilToInt(b.size.x);
            height = Mathf.CeilToInt(b.size.y);

            if (width <= 0 || height <= 0)
                return null;

            textGO.transform.position = new Vector3(
                -b.center.x,
                -b.center.y,
                0);

            EnsureRenderTargets(width, height);

            float textCenterY = (b.min.y + b.max.y) * 0.5f;
            cam.transform.position = new Vector3(0, textCenterY, -10);
            cam.orthographicSize = height / 2f;
            cam.targetTexture = rt;

            RenderTexture.active = rt;
            cam.Render();

            readTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readTex.Apply();

            RenderTexture.active = null;

            Color32[] pixels = readTex.GetPixels32();

            textCache[key] = new CachedText
            {
                Pixels = pixels,
                Width = width,
                Height = height
            };

            return pixels;
        }

        private static void DrawTextLayer(
            Color32[] map,
            int mapSize,
            Color32[] textPixels,
            int w,
            int h,
            int startX,
            int startY,
            Color32 color)
        {
            for (int y = 0; y < h; y++)
            {
                int my = startY + y;
                if (my < 0 || my >= mapSize)
                    continue;

                int rowOffset = my * mapSize;
                int srcRowOffset = y * w;

                for (int x = 0; x < w; x++)
                {
                    int mx = startX + x;
                    if (mx < 0 || mx >= mapSize)
                        continue;

                    Color32 tp = textPixels[srcRowOffset + x];
                    if (tp.a == 0)
                        continue;

                    int pos = rowOffset + mx;

                    Color32 final = color;
                    final.a = (byte)(tp.a * color.a / 255);

                    map[pos] = Color32.Lerp(map[pos], final, final.a / 255f);
                    map[pos].a = 255;
                }
            }
        }

        private static void DrawTextOnMap(
            Color32[] map,
            int mapSize,
            string text,
            int centerX,
            int topY)
        {
            Color32[] textPixels = RenderText(text, out int w, out int h);
            if (textPixels == null)
                return;

            int startX = centerX - w / 2;
            int startY = topY;

            // shadow
            DrawTextLayer(map, mapSize, textPixels, w, h,
                startX + 1, startY - 1,
                new Color32(0, 0, 0, 120));

            // outline
            for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                {
                    if (ox == 0 && oy == 0)
                        continue;

                    DrawTextLayer(map, mapSize, textPixels, w, h,
                        startX + ox, startY + oy,
                        new Color32(0, 0, 0, 200));
                }

            // text
            DrawTextLayer(map, mapSize, textPixels, w, h,
                startX, startY,
                Color.white);
        }

        internal static IEnumerator DrawPinTexts(Color32[] map, int mapSize, List<Tuple<int, int, string>> pinTexts)
        {
            EnsureFontAsset();
            EnsureRenderObjects();
            
            ChangeRenderObjectsVisibility(true);

            foreach (var pin in pinTexts)
            {
                string localized = Localization.instance.Localize(pin.Item3);

                DrawTextOnMap(
                    map,
                    mapSize,
                    localized,
                    pin.Item1,
                    pin.Item2);

                yield return null;
            }

            ChangeRenderObjectsVisibility(false);
        }
    }
}
