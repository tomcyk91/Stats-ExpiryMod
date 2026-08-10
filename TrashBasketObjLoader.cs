using BepInEx;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SmartExpiration
{
    internal static class TrashBasketObjMeshLoader
    {
        public static GameObject LoadObjTextAsGameObject(string objText, string objectName, Color color)
        {
            if (string.IsNullOrWhiteSpace(objText))
            {
                return null;
            }

            using (StringReader reader = new StringReader(objText))
            {
                return LoadObjFromReader(reader, objectName, color);
            }
        }

        public static GameObject LoadObjAsGameObject(string path, string objectName, Color color)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            using (StreamReader reader = new StreamReader(path))
            {
                return LoadObjFromReader(reader, objectName, color);
            }
        }

        private static GameObject LoadObjFromReader(TextReader reader, string objectName, Color color)
        {
            if (reader == null)
            {
                return null;
            }

            List<Vector3> vertices = new List<Vector3>(20000);
            List<int> triangles = new List<int>(20000);
            CultureInfo culture = CultureInfo.InvariantCulture;

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                line = line.Trim();
                if (line.Length < 2 || line[0] == '#') continue;

                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;

                    float x = float.Parse(parts[1], culture);
                    float y = float.Parse(parts[2], culture);
                    float z = float.Parse(parts[3], culture);
                    vertices.Add(new Vector3(x, y, z));
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;

                    int first = ParseObjVertexIndex(parts[1], vertices.Count);
                    int previous = ParseObjVertexIndex(parts[2], vertices.Count);

                    for (int i = 3; i < parts.Length; i++)
                    {
                        int current = ParseObjVertexIndex(parts[i], vertices.Count);
                        if (first >= 0 && previous >= 0 && current >= 0)
                        {
                            triangles.Add(first);
                            triangles.Add(previous);
                            triangles.Add(current);
                        }
                        previous = current;
                    }
                }
            }

            if (vertices.Count < 3 || triangles.Count < 3)
            {
                StatisticMod.Plugin.Log?.LogWarning("[TrashBasket] OBJ koszyka nie zawiera poprawnej siatki. Vertices=" + vertices.Count + ", triangles=" + (triangles.Count / 3));
                return null;
            }

            List<int> basketTriangles = new List<int>(triangles.Count);
            List<int> handleTriangles = new List<int>(512);

            // Nie wybieramy już rączki po prostym zakresie X/Z/Y, bo łapało to także lewą krawędź koszyka.
            // Model OBJ ma rączkę jako osobną wyspę siatki: rozpoznajemy ją po spójnych komponentach wierzchołków.
            SplitBasketAndHandleTrianglesByConnectedComponent(vertices, triangles, basketTriangles, handleTriangles);

            GameObject root = new GameObject(objectName);
            root.hideFlags = HideFlags.HideAndDontSave;

            Material redMaterial = CreateObjMaterial(objectName + "_Red_Material", color);
            BuildObjMeshChild(root.transform, objectName + "_RedMesh", vertices, basketTriangles, redMaterial);

            if (handleTriangles.Count >= 3)
            {
                Material handleMaterial = CreateObjMaterial(objectName + "_ExistingHandle_Black_Material", new Color(0.0f, 0.0f, 0.0f, 1.0f));
                BuildObjMeshChild(root.transform, objectName + "_ExistingLeftHandle_BlackMesh", vertices, handleTriangles, handleMaterial);
            }

            StatisticMod.Plugin.DebugLog("[TrashBasket] OBJ koszyka wczytany. Vertices=" + vertices.Count + ", triangles=" + (triangles.Count / 3) + ", handleTriangles=" + (handleTriangles.Count / 3));
            return root;
        }

        private static bool IsExistingLeftHandleTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            // Model po obrocie Y=90 ma szeroki bok do gracza.
            // Lewa widoczna strona odpowiada ujemnemu Z w oryginalnym OBJ.
            // Główna część rączki jest najwyższą, wąską belką tej strony.
            // Dodatkowo łapiemy małe końcówki przy przednim/lewym narożniku,
            // które były odrobinę niżej i zostawały czerwone.
            Vector3 center = (a + b + c) / 3.0f;
            float maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));

            bool leftVisibleSide = center.z <= -0.160f;

            // Główna górna belka/rączka.
            bool upperHandleBand = center.y >= 0.187f && maxY >= 0.195f;

            // Mała brakująca część przy końcówce rączki na przedniej/lewej stronie.
            // Uwaga: zawężone do samego skraju X i samej górnej krawędzi,
            // żeby znowu nie malować pionowej krawędzi koszyka.
            bool handleEndConnector = center.z <= -0.205f
                                      && Mathf.Abs(center.x) >= 0.120f
                                      && center.y >= 0.187f
                                      && maxY >= 0.190f;

            return leftVisibleSide && (upperHandleBand || handleEndConnector);
        }

        private sealed class ObjComponentStats
        {
            public int Count;
            public float MinX = float.MaxValue;
            public float MaxX = float.MinValue;
            public float MinY = float.MaxValue;
            public float MaxY = float.MinValue;
            public float MinZ = float.MaxValue;
            public float MaxZ = float.MinValue;

            public void Add(Vector3 v)
            {
                Count++;
                if (v.x < MinX) MinX = v.x;
                if (v.x > MaxX) MaxX = v.x;
                if (v.y < MinY) MinY = v.y;
                if (v.y > MaxY) MaxY = v.y;
                if (v.z < MinZ) MinZ = v.z;
                if (v.z > MaxZ) MaxZ = v.z;
            }
        }

        private static void SplitBasketAndHandleTrianglesByConnectedComponent(List<Vector3> vertices, List<int> triangles, List<int> basketTriangles, List<int> handleTriangles)
        {
            if (vertices == null || triangles == null || basketTriangles == null || handleTriangles == null)
            {
                return;
            }

            try
            {
                Dictionary<string, int> coordToId = new Dictionary<string, int>(vertices.Count);
                List<Vector3> uniqueCoords = new List<Vector3>(vertices.Count);
                int[] coordIds = new int[vertices.Count];

                for (int i = 0; i < vertices.Count; i++)
                {
                    string key = ObjCoordKey(vertices[i]);
                    int id;
                    if (!coordToId.TryGetValue(key, out id))
                    {
                        id = uniqueCoords.Count;
                        coordToId[key] = id;
                        uniqueCoords.Add(vertices[i]);
                    }
                    coordIds[i] = id;
                }

                int[] parent = new int[uniqueCoords.Count];
                for (int i = 0; i < parent.Length; i++) parent[i] = i;

                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    int ai = triangles[i];
                    int bi = triangles[i + 1];
                    int ci = triangles[i + 2];
                    if (ai < 0 || ai >= vertices.Count || bi < 0 || bi >= vertices.Count || ci < 0 || ci >= vertices.Count) continue;

                    ObjUnion(parent, coordIds[ai], coordIds[bi]);
                    ObjUnion(parent, coordIds[bi], coordIds[ci]);
                    ObjUnion(parent, coordIds[ci], coordIds[ai]);
                }

                Dictionary<int, ObjComponentStats> statsByRoot = new Dictionary<int, ObjComponentStats>();
                for (int i = 0; i < uniqueCoords.Count; i++)
                {
                    int root = ObjFind(parent, i);
                    ObjComponentStats stats;
                    if (!statsByRoot.TryGetValue(root, out stats))
                    {
                        stats = new ObjComponentStats();
                        statsByRoot[root] = stats;
                    }
                    stats.Add(uniqueCoords[i]);
                }

                Dictionary<int, bool> handleRoots = new Dictionary<int, bool>();
                foreach (KeyValuePair<int, ObjComponentStats> pair in statsByRoot)
                {
                    ObjComponentStats s = pair.Value;
                    float widthX = s.MaxX - s.MinX;
                    float heightY = s.MaxY - s.MinY;
                    float depthZ = s.MaxZ - s.MinZ;

                    // W tym modelu właściwa rączka jest osobnym, małym komponentem:
                    // ok. 66 unikalnych punktów, wysoko położona, bardzo niska w osi Y,
                    // z zakresem X około 0.29 i Z około 0.23.
                    // Główna skrzynka ma tysiące punktów i zaczyna się od Y=0, więc nie spełni tych warunków.
                    bool looksLikeExistingHandle = s.Count <= 500
                                                   && s.MinY >= 0.150f
                                                   && s.MaxY >= 0.195f
                                                   && heightY <= 0.080f
                                                   && widthX >= 0.150f
                                                   && depthZ >= 0.080f
                                                   && depthZ <= 0.350f;

                    if (looksLikeExistingHandle)
                    {
                        handleRoots[pair.Key] = true;
                    }
                }

                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    int ai = triangles[i];
                    int bi = triangles[i + 1];
                    int ci = triangles[i + 2];

                    bool isHandle = false;
                    if (ai >= 0 && ai < vertices.Count && bi >= 0 && bi < vertices.Count && ci >= 0 && ci < vertices.Count)
                    {
                        int rootA = ObjFind(parent, coordIds[ai]);
                        int rootB = ObjFind(parent, coordIds[bi]);
                        int rootC = ObjFind(parent, coordIds[ci]);
                        isHandle = rootA == rootB && rootB == rootC && handleRoots.ContainsKey(rootA);
                    }

                    if (isHandle)
                    {
                        handleTriangles.Add(ai);
                        handleTriangles.Add(bi);
                        handleTriangles.Add(ci);
                    }
                    else
                    {
                        basketTriangles.Add(ai);
                        basketTriangles.Add(bi);
                        basketTriangles.Add(ci);
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log?.LogWarning("[TrashBasket] Nie udało się wydzielić rączki koszyka jako komponentu OBJ, używam całego modelu jako czerwonego koszyka: " + ex.Message);
                basketTriangles.Clear();
                handleTriangles.Clear();
                basketTriangles.AddRange(triangles);
            }
        }

        private static string ObjCoordKey(Vector3 v)
        {
            int x = Mathf.RoundToInt(v.x * 100000.0f);
            int y = Mathf.RoundToInt(v.y * 100000.0f);
            int z = Mathf.RoundToInt(v.z * 100000.0f);
            return x.ToString(CultureInfo.InvariantCulture) + "|" + y.ToString(CultureInfo.InvariantCulture) + "|" + z.ToString(CultureInfo.InvariantCulture);
        }

        private static int ObjFind(int[] parent, int x)
        {
            int root = x;
            while (parent[root] != root)
            {
                root = parent[root];
            }
            while (parent[x] != x)
            {
                int next = parent[x];
                parent[x] = root;
                x = next;
            }
            return root;
        }

        private static void ObjUnion(int[] parent, int a, int b)
        {
            int rootA = ObjFind(parent, a);
            int rootB = ObjFind(parent, b);
            if (rootA != rootB)
            {
                parent[rootB] = rootA;
            }
        }

        private static void BuildObjMeshChild(Transform parent, string name, List<Vector3> vertices, List<int> triangles, Material material)
        {
            if (parent == null || vertices == null || triangles == null || triangles.Count < 3)
            {
                return;
            }

            Mesh mesh = new Mesh();
            mesh.name = name + "_Mesh";
            mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (vertices.Count > 65000)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GameObject child = new GameObject(name);
            child.hideFlags = HideFlags.HideAndDontSave;
            child.transform.SetParent(parent, false);

            MeshFilter filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.material = material;
        }

        private static Material CreateObjMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader);
            material.name = materialName;
            material.hideFlags = HideFlags.DontUnloadUnusedAsset;
            try
            {
                material.color = color;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                if (material.HasProperty("_Cull")) material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0.0f);
                if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0.0f);
                if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 1);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.0f);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.0f);
                if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.0f);
                material.renderQueue = 2000;
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHATEST_ON");
            }
            catch { }
            return material;
        }

        private static int ParseObjVertexIndex(string token, int vertexCount)
        {
            if (string.IsNullOrEmpty(token)) return -1;
            int slash = token.IndexOf('/');
            string raw = slash >= 0 ? token.Substring(0, slash) : token;
            if (string.IsNullOrEmpty(raw)) return -1;

            int index;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                return -1;
            }

            if (index > 0) return index - 1;
            if (index < 0) return vertexCount + index;
            return -1;
        }
    }


    internal static class TrashBasketObjLoader
    {
        private const string ObjFileName = "basket.obj";
        private static string _cachedEmbeddedText;
        private static GameObject _cachedTemplate;
        private static string _cachedSource;

        public static GameObject LoadBasket(string objectName, out string source)
        {
            source = string.Empty;

            if (_cachedTemplate != null)
            {
                GameObject clone = UnityEngine.Object.Instantiate(_cachedTemplate);
                clone.name = objectName;
                clone.hideFlags = HideFlags.HideAndDontSave;
                clone.SetActive(true);
                source = _cachedSource;
                return clone;
            }

            try
            {
                string embedded = GetEmbeddedObjText();
                if (!string.IsNullOrWhiteSpace(embedded))
                {
                    GameObject fromResource = TrashBasketObjMeshLoader.LoadObjTextAsGameObject(
                        embedded,
                        objectName,
                        new Color(0.58f, 0.000f, 0.000f, 1.0f));

                    if (fromResource != null)
                    {
                        source = "Embedded Resource: " + ObjFileName;
                        ApplyPlayerShoppingPaint(fromResource);
                        CacheTemplate(fromResource, source);
                        return fromResource;
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log?.LogWarning("[TrashBasket] Nie udało się wczytać osadzonego basket.obj: " + ex.Message);
            }

            string path = ResolveExternalPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                StatisticMod.Plugin.Log?.LogWarning(
                    "[TrashBasket] Brak basket.obj. Dodaj go jako Embedded Resource albo umieść obok DLL / w folderze assets.");
                return null;
            }

            GameObject fromFile = TrashBasketObjMeshLoader.LoadObjAsGameObject(
                path,
                objectName,
                new Color(0.58f, 0.000f, 0.000f, 1.0f));

            if (fromFile != null)
            {
                source = path;
                ApplyPlayerShoppingPaint(fromFile);
                CacheTemplate(fromFile, source);
            }

            return fromFile;
        }

        private static void CacheTemplate(GameObject sourceObject, string source)
        {
            if (sourceObject == null || _cachedTemplate != null) return;

            try
            {
                _cachedTemplate = UnityEngine.Object.Instantiate(sourceObject);
                _cachedTemplate.name = "ExpiredProductsBasket_OBJ_Template";
                _cachedTemplate.hideFlags = HideFlags.HideAndDontSave;
                _cachedTemplate.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_cachedTemplate);
                _cachedSource = source;
            }
            catch
            {
                _cachedTemplate = null;
                _cachedSource = string.Empty;
            }
        }

        private static string GetEmbeddedObjText()
        {
            if (!string.IsNullOrEmpty(_cachedEmbeddedText)) return _cachedEmbeddedText;

            Assembly assembly = typeof(StatisticMod.Plugin).Assembly;
            string[] names = assembly.GetManifestResourceNames();
            string selected = null;

            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (name.Equals(ObjFileName, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("." + ObjFileName, StringComparison.OrdinalIgnoreCase))
                {
                    selected = name;
                    break;
                }
            }

            if (string.IsNullOrEmpty(selected)) return null;

            using (Stream stream = assembly.GetManifestResourceStream(selected))
            {
                if (stream == null) return null;
                using (StreamReader reader = new StreamReader(stream))
                {
                    _cachedEmbeddedText = reader.ReadToEnd();
                }
            }

            StatisticMod.Plugin.DebugLog("[TrashBasket] Odczytano osadzony model OBJ: " + selected);
            return _cachedEmbeddedText;
        }

        private static string ResolveExternalPath()
        {
            var candidates = new List<string>();

            try
            {
                string assemblyDir = Path.GetDirectoryName(typeof(StatisticMod.Plugin).Assembly.Location);
                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    candidates.Add(Path.Combine(assemblyDir, ObjFileName));
                    candidates.Add(Path.Combine(assemblyDir, "assets", ObjFileName));
                    candidates.Add(Path.Combine(assemblyDir, "Assets", ObjFileName));
                }
            }
            catch { }

            try
            {
                candidates.Add(Path.Combine(Paths.PluginPath, ObjFileName));
                candidates.Add(Path.Combine(Paths.PluginPath, "StatsandExpiryMod", ObjFileName));
                candidates.Add(Path.Combine(Paths.PluginPath, "StatsandExpiryMod", "assets", ObjFileName));
            }
            catch { }

            for (int i = 0; i < candidates.Count; i++)
            {
                try
                {
                    if (File.Exists(candidates[i])) return candidates[i];
                }
                catch { }
            }

            return candidates.Count > 0 ? candidates[0] : ObjFileName;
        }

        private static void ApplyPlayerShoppingPaint(GameObject basket)
        {
            if (basket == null) return;

            try
            {
                Renderer[] renderers = basket.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null || renderer.material == null) continue;
                    ApplyOpaqueMatteMaterial(renderer.material);
                }
            }
            catch { }
        }

        private static void ApplyOpaqueMatteMaterial(Material material)
        {
            if (material == null) return;

            try
            {
                material.renderQueue = 2000;
                material.SetOverrideTag("RenderType", "Opaque");
                material.SetOverrideTag("Queue", "Geometry");

                Color color = material.color;
                color.a = 1f;
                material.color = color;

                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
                if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
                if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
                if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 1);
                if (material.HasProperty("_ZTest")) material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
                if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0f);
                if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 0f);
                if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections", 0f);
                if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", Color.black);

                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHATEST_ON");
            }
            catch { }
        }
    }
}
