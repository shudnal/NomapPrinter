using System;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;
using static NomapPrinter.NomapPrinter;
using Object = UnityEngine.Object;

namespace NomapPrinter
{
    // One read-only overlay per map generation. No references to live pin UI are kept.
    internal sealed class MapPinChecks
    {
        private readonly Color32[] pixels;
        private readonly int width;
        private readonly int height;
        private readonly Color tint;
        private readonly Vector2 origin;
        private readonly Vector2 axisX;
        private readonly Vector2 axisY;
        private readonly Vector2 minimum;
        private readonly Vector2 maximum;
        private readonly float inverseDeterminant;

        private MapPinChecks(Image image, RectTransform pinTransform, Sprite sprite)
        {
            // Resolve the direct child's anchors without laying out or modifying the prefab.
            // The prefab's own rectangle may be empty before its UI is instantiated.
            float referenceSize = Minimap.instance.m_pinSizeLarge;
            if (referenceSize <= 0f)
                throw new InvalidOperationException("The map pin has an invalid reference size.");

            Vector2 pinSize = Vector2.one * referenceSize;
            Rect pinRect = new Rect(-Vector2.Scale(pinSize, pinTransform.pivot), pinSize);
            RectTransform checkedTransform = image.rectTransform;
            Vector2 anchorSpan = checkedTransform.anchorMax - checkedTransform.anchorMin;
            Vector2 checkedSize = Vector2.Scale(pinSize, anchorSpan) + checkedTransform.sizeDelta;
            Rect rect = new Rect(-Vector2.Scale(checkedSize, checkedTransform.pivot), checkedSize);
            if (rect.width <= 0f || rect.height <= 0f)
                throw new InvalidOperationException("The checked pin overlay has an empty rectangle.");

            Vector2 anchor = checkedTransform.anchorMin + Vector2.Scale(anchorSpan, checkedTransform.pivot);
            Vector2 position = pinRect.min + Vector2.Scale(pinSize, anchor) + checkedTransform.anchoredPosition;
            Matrix4x4 toPin = Matrix4x4.TRS(position, checkedTransform.localRotation, checkedTransform.localScale);

            Vector2 spriteSize = sprite.rect.size;
            if (spriteSize.x <= 0f || spriteSize.y <= 0f)
                throw new InvalidOperationException("The checked pin overlay has an empty sprite.");

            // Match the simple UI image's aspect ratio and transparent sprite padding.
            if (image.preserveAspect)
            {
                float aspect = spriteSize.x / spriteSize.y;
                if (aspect > rect.width / rect.height)
                {
                    float newHeight = rect.width / aspect;
                    rect.y += (rect.height - newHeight) * image.rectTransform.pivot.y;
                    rect.height = newHeight;
                }
                else
                {
                    float newWidth = rect.height * aspect;
                    rect.x += (rect.width - newWidth) * image.rectTransform.pivot.x;
                    rect.width = newWidth;
                }
            }

            Vector4 padding = DataUtility.GetPadding(sprite);
            Vector2 bottomLeft = new Vector2(rect.xMin + rect.width * padding.x / spriteSize.x,
                                            rect.yMin + rect.height * padding.y / spriteSize.y);
            Vector2 topRight = new Vector2(rect.xMax - rect.width * padding.z / spriteSize.x,
                                          rect.yMax - rect.height * padding.w / spriteSize.y);
            origin = ToPinPoint(toPin, pinRect, bottomLeft);
            axisX = ToPinPoint(toPin, pinRect, new Vector2(topRight.x, bottomLeft.y)) - origin;
            axisY = ToPinPoint(toPin, pinRect, new Vector2(bottomLeft.x, topRight.y)) - origin;
            float determinant = axisX.x * axisY.y - axisX.y * axisY.x;
            if (Mathf.Abs(determinant) < 0.000001f)
                throw new InvalidOperationException("The checked pin overlay has an invalid transform.");

            inverseDeterminant = 1f / determinant;
            minimum = Vector2.Min(Vector2.Min(origin, origin + axisX), Vector2.Min(origin + axisY, origin + axisX + axisY));
            maximum = Vector2.Max(Vector2.Max(origin, origin + axisX), Vector2.Max(origin + axisY, origin + axisX + axisY));
            tint = image.color;
            pixels = ReadSpritePixels(sprite, out width, out height);
        }

        internal static MapPinChecks Create()
        {
            if (IsHeadless)
                return null;

            try
            {
                GameObject prefab = Minimap.instance?.m_pinPrefab;
                RectTransform pinTransform = prefab != null ? prefab.GetComponent<RectTransform>() : null;
                Image image = prefab != null ? prefab.transform.Find("Checked")?.GetComponent<Image>() : null;
                Sprite sprite = image != null ? image.overrideSprite : null;
                if (sprite == null && image != null)
                    sprite = image.sprite;
                if (pinTransform == null || sprite == null || sprite.texture == null)
                    throw new InvalidOperationException("The map pin prefab has no Checked image or sprite.");

                // The prefab child can be inactive. Only PinData.m_checked decides whether to print it.
                return new MapPinChecks(image, pinTransform, sprite);
            }
            catch (Exception exception)
            {
                // The caller tries once per generation. Keep the map and its pins if the asset is unavailable.
                LogWarning($"Could not read checked pin overlay. Printing pins without cross marks.\n{exception}");
                return null;
            }
        }

        private static Vector2 ToPinPoint(Matrix4x4 toPin, Rect pinRect, Vector2 point)
        {
            Vector3 local = toPin.MultiplyPoint3x4(point);
            return new Vector2((local.x - pinRect.xMin) / pinRect.width, (local.y - pinRect.yMin) / pinRect.height);
        }

        private static Color32[] ReadSpritePixels(Sprite sprite, out int width, out int height)
        {
            Texture2D texture = sprite.texture;
            Vector4 uv = DataUtility.GetOuterUV(sprite);
            Vector2 scale = new Vector2(uv.z - uv.x, uv.w - uv.y);
            width = Mathf.RoundToInt(Mathf.Abs(scale.x) * texture.width);
            height = Mathf.RoundToInt(Mathf.Abs(scale.y) * texture.height);
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("The checked pin overlay has an empty texture region.");

            RenderTexture previous = RenderTexture.active;
            bool previousSRGBWrite = GL.sRGBWrite;
            RenderTexture target = null;
            Texture2D readable = null;
            try
            {
                // Read this sprite's actual atlas, not MapMaker's cached pin atlas.
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(texture, target, scale, new Vector2(uv.x, uv.y));
                RenderTexture.active = target;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                return readable.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSRGBWrite;
                if (target != null)
                    RenderTexture.ReleaseTemporary(target);
                if (readable != null)
                    Object.Destroy(readable);
            }
        }

        internal void Draw(Color32[] map, int mapSize, int centerX, int centerY, int size)
        {
            if (size <= 0 || tint.a <= 0f)
                return;

            // Use the same origin as the base icon, including odd icon sizes.
            int left = centerX - size / 2;
            int bottom = centerY - size / 2;
            int firstX = Mathf.Clamp(Mathf.FloorToInt(left + minimum.x * size), 0, mapSize);
            int lastX = Mathf.Clamp(Mathf.CeilToInt(left + maximum.x * size), 0, mapSize);
            int firstY = Mathf.Clamp(Mathf.FloorToInt(bottom + minimum.y * size), 0, mapSize);
            int lastY = Mathf.Clamp(Mathf.CeilToInt(bottom + maximum.y * size), 0, mapSize);

            for (int y = firstY; y < lastY; y++)
            {
                for (int x = firstX; x < lastX; x++)
                {
                    Vector2 point = new Vector2((x + 0.5f - left) / size, (y + 0.5f - bottom) / size) - origin;
                    float u = (point.x * axisY.y - point.y * axisY.x) * inverseDeterminant;
                    float v = (axisX.x * point.y - axisX.y * point.x) * inverseDeterminant;
                    if (u < 0f || u > 1f || v < 0f || v > 1f)
                        continue;

                    // Preserve the Checked image's own tint; do not apply the pin's Hildir color.
                    Color32 pixel = Sample(u, v) * tint;
                    if (pixel.a == 0)
                        continue;

                    int index = y * mapSize + x;
                    map[index] = Color32.Lerp(map[index], pixel, pixel.a / 255f);
                    map[index].a = byte.MaxValue;
                }
            }
        }

        private Color Sample(float u, float v)
        {
            float x = Mathf.Clamp(u * width - 0.5f, 0f, width - 1);
            float y = Mathf.Clamp(v * height - 0.5f, 0f, height - 1);
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            int x1 = Math.Min(x0 + 1, width - 1);
            int y1 = Math.Min(y0 + 1, height - 1);
            Color lower = Color.Lerp(pixels[y0 * width + x0], pixels[y0 * width + x1], x - x0);
            Color upper = Color.Lerp(pixels[y1 * width + x0], pixels[y1 * width + x1], x - x0);
            return Color.Lerp(lower, upper, y - y0);
        }
    }
}
