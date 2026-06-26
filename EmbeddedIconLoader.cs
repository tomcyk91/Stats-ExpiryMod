using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace StatisticMod
{
    internal static class EmbeddedIconLoader
    {
        private static Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        internal static Sprite LoadPngSprite(string resourceNameContains, float pixelsPerUnit = 100f)
        {
            if (_cache.TryGetValue(resourceNameContains, out Sprite cached)) return cached;

            var asm = Assembly.GetExecutingAssembly();

            string resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(resourceNameContains, StringComparison.OrdinalIgnoreCase)
                                  || n.Contains(resourceNameContains, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(resName)) return null;

            using var stream = asm.GetManifestResourceStream(resName);
            if (stream == null) return null;

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] data = ms.ToArray();

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            // A5 FIX: Tarcza przed spaleniem przez procedurę UnloadUnusedAssets
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            // A6 FIX: Natywny most pamięci C++ dla tablicy bajtów
            ImageConversion.LoadImage(tex, (Il2CppStructArray<byte>)data);

            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit
            );
            // A5 FIX: Ochrona wycinanki przed czyszczeniem pamięci
            sprite.hideFlags = HideFlags.HideAndDontSave;

            _cache[resourceNameContains] = sprite;
            return sprite;
        }
    }
}