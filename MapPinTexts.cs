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
        private static TMP_FontAsset defaultFontAsset = null;

        internal static void ClearPinFont() => defaultFontAsset = null;

        private static void SetDefaultFontAsset()
        {
            TMP_FontAsset defaultFont = null;

            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font.name.Contains(pinTextFont.Value))
                {
                    defaultFontAsset = font;
                    return;
                }
                else if (defaultFont == null && font.name.Contains("Fallback"))
                    defaultFont = font;
            }

            if (defaultFontAsset == null)
            {
                defaultFontAsset = defaultFont;
                if (defaultFont != null)
                    LogInfo($"Unable to find font {pinTextFont.Value}, falling back to {defaultFont.name}");
                else
                    LogInfo($"Unable to find font {pinTextFont.Value} or fallback font");
            }
        }

        private static Color32[] RenderText(string text, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (TMP_Settings.defaultFontAsset == null)
                return null;

            if (text.IsNullOrWhiteSpace())
                return null;

            int layer = 31;

            var textGO = new GameObject("MapTMPText")
            {
                layer = layer
            };

            var tmp = textGO.AddComponent<TextMeshPro>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.text = text;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.richText = false;

            tmp.fontSize = pinTextSize.Value * 10;
            tmp.fontStyle = pinTextFontStyle.Value;
            tmp.color = pinTextFontColor.Value;

            tmp.ForceMeshUpdate();

            Bounds b = tmp.textBounds;

            width = Mathf.CeilToInt(b.size.x);
            height = Mathf.CeilToInt(b.size.y);

            textGO.transform.position = new Vector3(
                -b.center.x,
                -b.center.y,
                0);

            var camGO = new GameObject("MapTMPTextCam");
            camGO.layer = layer;

            var cam = camGO.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.cullingMask = 1 << layer;
            float textCenterY = (b.min.y + b.max.y) * 0.5f;
            cam.transform.position = new Vector3(0, textCenterY, -10);
            cam.orthographicSize = height / 2f;

            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            cam.Render();

            RenderTexture.active = rt;

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            RenderTexture.active = null;

            var pixels = tex.GetPixels32();

            Object.Destroy(textGO);
            Object.Destroy(camGO);
            Object.Destroy(tex);
            rt.Release();

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
                for (int x = 0; x < w; x++)
                {
                    Color32 tp = textPixels[y * w + x];
                    if (tp.a == 0)
                        continue;

                    int mx = startX + x;
                    int my = startY + y;

                    if (mx < 0 || my < 0 || mx >= mapSize || my >= mapSize)
                        continue;

                    int pos = my * mapSize + mx;

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

            /*DrawDebugRect(
                map,
                mapSize,
                startX,
                startY,
                w,
                h,
                new Color32(255, 0, 0, 255)
            );*/

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

        private static void DrawDebugRect(
            Color32[] map,
            int mapSize,
            int x,
            int y,
            int w,
            int h,
            Color32 color)
        {
            // top & bottom
            for (int i = 0; i < w; i++)
            {
                int tx = x + i;
                if (tx < 0 || tx >= mapSize)
                    continue;

                if (y >= 0 && y < mapSize)
                    map[y * mapSize + tx] = color;

                int by = y + h - 1;
                if (by >= 0 && by < mapSize)
                    map[by * mapSize + tx] = color;
            }

            // left & right
            for (int i = 0; i < h; i++)
            {
                int ty = y + i;
                if (ty < 0 || ty >= mapSize)
                    continue;

                if (x >= 0 && x < mapSize)
                    map[ty * mapSize + x] = color;

                int rx = x + w - 1;
                if (rx >= 0 && rx < mapSize)
                    map[ty * mapSize + rx] = color;
            }
        }

        internal static IEnumerator DrawPinTexts(Color32[] map, int mapSize, List<Tuple<int, int, string>> pinTexts)
        {
            TMP_FontAsset previousFont = TMP_Settings.defaultFontAsset;

            if (TMP_Settings.defaultFontAsset == null)
            {
                if (defaultFontAsset == null)
                {
                    defaultFontAsset = Minimap.instance.m_pinNamePrefab.GetComponentInChildren<TMP_Text>().font;

                    if (defaultFontAsset == null || !pinTextFont.Value.IsNullOrWhiteSpace())
                        SetDefaultFontAsset();
                }

            }

            foreach (var pin in pinTexts)
            {
                TMP_Settings.defaultFontAsset = defaultFontAsset;
                
                DrawTextOnMap(
                    map,
                    mapSize,
                    Localization.instance.Localize(pin.Item3),
                    pin.Item1,
                    pin.Item2 - MapMaker.iconSize - pinTextOffset.Value);
                
                TMP_Settings.defaultFontAsset = previousFont;

                yield return null;
            }
        }
    }
}
