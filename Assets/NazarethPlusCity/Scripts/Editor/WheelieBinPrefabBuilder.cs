using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NazarethPlusCity.EditorTools
{
    public static class WheelieBinPrefabBuilder
    {
        const string MeshPath = "Assets/NazarethPlusCity/Art/Props/WheelieBin/WheelieBinMeshes.asset";
        const string TextureFolder = "Assets/NazarethPlusCity/Art/Props/WheelieBin/Textures";
        const string MaterialFolder = "Assets/NazarethPlusCity/Art/Props/WheelieBin/Materials";

        public static GameObject BuildOrUpdate()
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            string glbPath = Path.Combine(project, "Data", "building_models", "nazareth-green-wheelie-bin-closed.glb");
            if (!File.Exists(glbPath))
            {
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlaceableIds.PrefabPath);
                if (existing != null)
                {
                    Debug.LogWarning("Wheelie bin GLB was not found at " + glbPath + ". The existing prefab was kept.");
                    return existing;
                }

                throw new FileNotFoundException("Wheelie bin GLB was not found at " + glbPath + ". No substitute prefab was created.", glbPath);
            }

            CityMaterialFactory.EnsureFolder("Assets/NazarethPlusCity/Art");
            CityMaterialFactory.EnsureFolder("Assets/NazarethPlusCity/Art/Props");
            CityMaterialFactory.EnsureFolder("Assets/NazarethPlusCity/Art/Props/WheelieBin");
            CityMaterialFactory.EnsureFolder(TextureFolder);
            CityMaterialFactory.EnsureFolder(MaterialFolder);
            byte[] file = File.ReadAllBytes(glbPath);
            ReadGlb(file, out string json, out byte[] bin);
            JsonMini root = JsonMini.Parse(json);
            var materials = BuildMaterials(root, bin);
            var meshes = BuildMeshes(root, bin);
            if (meshes.Count != 16)
                throw new InvalidOperationException("Expected 16 bin meshes, built " + meshes.Count + ".");
            GameObject prefab = AssemblePrefab(root, meshes, materials);
            Sprite thumbnail = BuildThumbnail(prefab);
            SaveCatalog(prefab, thumbnail);
            Debug.Log("Built wheelie bin prefab at " + PlaceableIds.PrefabPath + ".");
            return prefab;
        }

        static void ReadGlb(byte[] file, out string json, out byte[] bin)
        {
            if (file.Length < 20 || file[0] != 'g' || file[1] != 'l' || file[2] != 'T' || file[3] != 'F')
                throw new InvalidDataException("The bin file is not a glTF binary.");
            int jsonLength = BitConverter.ToInt32(file, 12);
            json = Encoding.UTF8.GetString(file, 20, jsonLength).Trim();
            int binHeader = 20 + jsonLength;
            int binLength = BitConverter.ToInt32(file, binHeader);
            bin = new byte[binLength];
            Buffer.BlockCopy(file, binHeader + 8, bin, 0, binLength);
        }

        static List<Material> BuildMaterials(JsonMini root, byte[] bin)
        {
            JsonMini images = root.Get("images");
            JsonMini textures = root.Get("textures");
            JsonMini views = root.Get("bufferViews");
            JsonMini materialNodes = root.Get("materials");
            string albedo = null;
            string normal = null;
            string smoothness = null;
            int albedoImage = -1;
            int normalImage = -1;
            int roughnessImage = -1;
            for (int i = 0; i < materialNodes.Count; i++)
            {
                JsonMini pbr = materialNodes[i].Get("pbrMetallicRoughness");
                if (pbr != null && pbr.Try("baseColorTexture", out JsonMini baseTex) && albedoImage < 0)
                    albedoImage = textures[baseTex.Get("index").Int].Get("source").Int;
                if (materialNodes[i].Try("normalTexture", out JsonMini normalTex) && normalImage < 0)
                    normalImage = textures[normalTex.Get("index").Int].Get("source").Int;
                if (pbr != null && pbr.Try("metallicRoughnessTexture", out JsonMini mr) && roughnessImage < 0)
                    roughnessImage = textures[mr.Get("index").Int].Get("source").Int;
            }

            if (albedoImage >= 0)
                albedo = ImportTexture(WriteImage(bin, images, views, albedoImage, TextureFolder + "/Bin_Albedo.png"), false, false);
            if (normalImage >= 0)
                normal = ImportTexture(WriteImage(bin, images, views, normalImage, TextureFolder + "/Bin_Normal.png"), true, false);
            if (roughnessImage >= 0)
                smoothness = BakeSmoothness(bin, images, views, roughnessImage);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader");
            if (shader == null)
                throw new FileNotFoundException("Universal Render Pipeline/Lit was not found. Bin materials were not replaced with another shader.");
            Texture albedoTexture = albedo != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(albedo) : null;
            Texture normalTexture = normal != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normal) : null;
            Texture smoothTexture = smoothness != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(smoothness) : null;
            var materials = new List<Material>();
            for (int i = 0; i < materialNodes.Count; i++)
            {
                JsonMini node = materialNodes[i];
                string name = node.Try("name", out JsonMini named) ? named.String : "Material" + i;
                string path = MaterialFolder + "/M_Bin_" + i.ToString("00") + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }

                material.shader = shader;
                JsonMini pbr = node.Get("pbrMetallicRoughness");
                Color color = Color.white;
                float metallic = 0f;
                float roughness = 0.6f;
                bool textured = false;
                if (pbr != null)
                {
                    if (pbr.Try("baseColorFactor", out JsonMini factor) && factor.Count >= 3)
                        color = new Color(Mathf.Min(1f, (float)factor[0].Number), Mathf.Min(1f, (float)factor[1].Number), Mathf.Min(1f, (float)factor[2].Number), 1f);
                    if (pbr.Try("metallicFactor", out JsonMini metal))
                        metallic = (float)metal.Number;
                    if (pbr.Try("roughnessFactor", out JsonMini rough))
                        roughness = (float)rough.Number;
                    textured = pbr.Get("baseColorTexture") != null;
                }

                material.SetColor("_BaseColor", color);
                material.SetFloat("_Metallic", textured ? 1f : metallic);
                material.SetFloat("_Smoothness", textured ? 1f : Mathf.Clamp01(1f - roughness));
                if (textured && albedoTexture != null)
                    material.SetTexture("_BaseMap", albedoTexture);
                if (textured && smoothTexture != null)
                {
                    material.SetTexture("_MetallicGlossMap", smoothTexture);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                }

                if (node.Get("normalTexture") != null && normalTexture != null)
                {
                    material.SetTexture("_BumpMap", normalTexture);
                    float scale = 1f;
                    if (node.Try("normalTexture", out JsonMini normalNode) && normalNode.Try("scale", out JsonMini scaleNode))
                        scale = (float)scaleNode.Number;
                    material.SetFloat("_BumpScale", scale);
                    material.EnableKeyword("_NORMALMAP");
                }

                EditorUtility.SetDirty(material);
                materials.Add(material);
            }

            return materials;
        }

        static string BakeSmoothness(byte[] bin, JsonMini images, JsonMini views, int imageIndex)
        {
            string source = WriteImage(bin, images, views, imageIndex, TextureFolder + "/Bin_RoughnessSource.png");
            AssetDatabase.ImportAsset(source, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(source) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("The roughness texture did not import from " + source + ".");
            importer.sRGBTexture = false;
            importer.textureType = TextureImporterType.Default;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(source);
            if (texture == null)
                throw new InvalidOperationException("The roughness texture could not be read from " + source + ".");
            Color[] pixels = texture.GetPixels();
            var baked = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
            for (int i = 0; i < pixels.Length; i++)
            {
                float smoothness = Mathf.Clamp01(1f - pixels[i].g);
                baked.SetPixel(i % texture.width, i / texture.width, new Color(0f, 0f, 0f, smoothness));
            }

            baked.Apply();
            string path = TextureFolder + "/Bin_Smoothness.png";
            File.WriteAllBytes(ToFull(path), baked.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(baked);
            AssetDatabase.DeleteAsset(source);
            return ImportTexture(path, false, true);
        }

        static string WriteImage(byte[] bin, JsonMini images, JsonMini views, int imageIndex, string assetPath)
        {
            JsonMini image = images[imageIndex];
            JsonMini view = views[image.Get("bufferView").Int];
            int offset = view.Try("byteOffset", out JsonMini byteOffset) ? byteOffset.Int : 0;
            int length = view.Get("byteLength").Int;
            var bytes = new byte[length];
            Buffer.BlockCopy(bin, offset, bytes, 0, length);
            File.WriteAllBytes(ToFull(assetPath), bytes);
            return assetPath;
        }

        static string ImportTexture(string assetPath, bool normal, bool linear)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal && !linear;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return assetPath;
        }

        static List<Mesh> BuildMeshes(JsonMini root, byte[] bin)
        {
            JsonMini meshNodes = root.Get("meshes");
            JsonMini accessors = root.Get("accessors");
            JsonMini views = root.Get("bufferViews");
            var meshes = new List<Mesh>();
            for (int i = 0; i < meshNodes.Count; i++)
            {
                JsonMini primitive = meshNodes[i].Get("primitives")[0];
                JsonMini attributes = primitive.Get("attributes");
                float[] position = ReadFloats(bin, accessors, views, attributes.Get("POSITION").Int, 3);
                float[] normal = attributes.Get("NORMAL") != null ? ReadFloats(bin, accessors, views, attributes.Get("NORMAL").Int, 3) : null;
                float[] uv = attributes.Get("TEXCOORD_0") != null ? ReadFloats(bin, accessors, views, attributes.Get("TEXCOORD_0").Int, 2) : null;
                float[] tangent = attributes.Get("TANGENT") != null ? ReadFloats(bin, accessors, views, attributes.Get("TANGENT").Int, 4) : null;
                int[] indices = ReadIndices(bin, accessors, views, primitive.Get("indices").Int);
                int count = position.Length / 3;
                var mesh = new Mesh();
                string meshName = meshNodes[i].Try("name", out JsonMini named) ? named.String : "BinMesh" + i;
                mesh.name = meshName;
                if (count > 65535)
                    mesh.indexFormat = IndexFormat.UInt32;
                var vertices = new Vector3[count];
                var normals = normal != null ? new Vector3[count] : null;
                var uvs = uv != null ? new Vector2[count] : null;
                var tangents = tangent != null ? new Vector4[count] : null;
                for (int v = 0; v < count; v++)
                {
                    vertices[v] = new Vector3(position[v * 3], position[v * 3 + 1], -position[v * 3 + 2]);
                    if (normals != null)
                        normals[v] = new Vector3(normal[v * 3], normal[v * 3 + 1], -normal[v * 3 + 2]);
                    if (uvs != null)
                        uvs[v] = new Vector2(uv[v * 2], 1f - uv[v * 2 + 1]);
                    if (tangents != null)
                        tangents[v] = new Vector4(tangent[v * 4], tangent[v * 4 + 1], -tangent[v * 4 + 2], -tangent[v * 4 + 3]);
                }

                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    int swap = indices[t];
                    indices[t] = indices[t + 2];
                    indices[t + 2] = swap;
                }

                mesh.vertices = vertices;
                if (normals != null)
                    mesh.normals = normals;
                else
                    mesh.RecalculateNormals();
                if (uvs != null)
                    mesh.uv = uvs;
                if (tangents != null)
                    mesh.tangents = tangents;
                mesh.triangles = indices;
                mesh.RecalculateBounds();
                meshes.Add(mesh);
            }

            if (meshes[0].vertexCount != 21708)
                throw new InvalidOperationException("The bin body has " + meshes[0].vertexCount + " vertices. The supplied model has 21708, so the prefab was not saved.");
            if (AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath) != null)
                AssetDatabase.DeleteAsset(MeshPath);
            AssetDatabase.CreateAsset(meshes[0], MeshPath);
            for (int i = 1; i < meshes.Count; i++)
                AssetDatabase.AddObjectToAsset(meshes[i], MeshPath);
            AssetDatabase.SaveAssets();
            return meshes;
        }

        static GameObject AssemblePrefab(JsonMini root, List<Mesh> meshes, List<Material> materials)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlaceableIds.PrefabPath) != null)
                AssetDatabase.DeleteAsset(PlaceableIds.PrefabPath);
            var rootObject = new GameObject("WheelieBinClosed");
            var model = new GameObject("Model");
            model.transform.SetParent(rootObject.transform, false);
            JsonMini meshNodes = root.Get("meshes");
            JsonMini nodes = root.Get("nodes");
            for (int i = 0; i < meshes.Count; i++)
            {
                string childName = meshNodes[i].Try("name", out JsonMini named) ? named.String : "Part" + i;
                var child = new GameObject(SafeObjectName(childName, i));
                child.transform.SetParent(model.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                var renderer = child.AddComponent<MeshRenderer>();
                int materialIndex = 0;
                JsonMini primitive = meshNodes[i].Get("primitives")[0];
                if (primitive.Try("material", out JsonMini materialNode))
                    materialIndex = materialNode.Int;
                renderer.sharedMaterial = materials[Mathf.Clamp(materialIndex, 0, materials.Count - 1)];
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            FitBottom(rootObject, model);
            var identity = rootObject.AddComponent<PlaceableAssetIdentity>();
            identity.catalogId = PlaceableIds.Bin;
            identity.assetType = PlaceableIds.BinType;
            identity.sourceFile = "nazareth-green-wheelie-bin-closed.glb";
            Bounds bounds = CombinedBounds(rootObject);
            identity.measuredWidthMeters = bounds.size.x;
            identity.measuredHeightMeters = bounds.size.y;
            identity.measuredDepthMeters = bounds.size.z;
            try
            {
                if (bounds.size.y < 0.9f || bounds.size.y > 1.3f)
                    throw new InvalidOperationException("Bin height is " + bounds.size.y.ToString("0.00") + " m, which is not the supplied wheelie bin. No substitute was saved.");
                var box = rootObject.AddComponent<BoxCollider>();
                box.center = bounds.center - rootObject.transform.position;
                box.size = bounds.size;
                var prefab = PrefabUtility.SaveAsPrefabAsset(rootObject, PlaceableIds.PrefabPath);
                if (nodes == null)
                    Debug.LogWarning("glTF nodes were missing; meshes were still assembled from the mesh list.");
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rootObject);
            }
        }

        static void FitBottom(GameObject rootObject, GameObject model)
        {
            Bounds bounds = CombinedBounds(rootObject);
            model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        }

        static Bounds CombinedBounds(GameObject rootObject)
        {
            var renderers = rootObject.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static Sprite BuildThumbnail(GameObject prefab)
        {
            string path = PlaceableIds.ThumbnailPath;
            bool rendered = false;
            try
            {
                rendered = TryRenderThumbnail(prefab, path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Bin thumbnail render failed, using a drawn thumbnail. " + exception.Message);
            }

            if (!rendered)
                WriteDrawnThumbnail(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite sprite)
                    return sprite;
            }

            throw new InvalidOperationException("The bin thumbnail did not import as a sprite.");
        }

        static bool TryRenderThumbnail(GameObject prefab, string assetPath)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var cameraObject = new GameObject("BinThumbnailCamera");
            var camera = cameraObject.AddComponent<Camera>();
            var lightObject = new GameObject("BinThumbnailLight");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Bounds bounds = CombinedBounds(instance);
            Vector3 center = bounds.center;
            float radius = Mathf.Max(0.2f, bounds.extents.magnitude);
            camera.transform.position = center + new Vector3(0.7f, 0.55f, 0.85f).normalized * radius * 2.4f;
            camera.transform.LookAt(center);
            camera.fieldOfView = 28f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.18f, 0.16f, 1f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 20f;
            var texture = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = texture;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(256, 256, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, 256f, 256f), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            bool varied = Variance(image) > 0.015f;
            if (varied)
                File.WriteAllBytes(ToFull(assetPath), image.EncodeToPNG());
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(lightObject);
            UnityEngine.Object.DestroyImmediate(instance);
            return varied;
        }

        static float Variance(Texture2D image)
        {
            Color32[] pixels = image.GetPixels32();
            double mean = 0.0;
            int step = 4;
            int count = 0;
            for (int i = 0; i < pixels.Length; i += step)
            {
                mean += pixels[i].r + pixels[i].g + pixels[i].b;
                count++;
            }

            mean /= Math.Max(1, count * 3);
            double variance = 0.0;
            for (int i = 0; i < pixels.Length; i += step)
            {
                double value = (pixels[i].r + pixels[i].g + pixels[i].b) / 3.0;
                double delta = value - mean;
                variance += delta * delta;
            }

            return (float)(variance / Math.Max(1, count));
        }

        static void WriteDrawnThumbnail(string assetPath)
        {
            var image = new Texture2D(128, 128, TextureFormat.RGB24, false);
            var background = new Color(0.16f, 0.18f, 0.16f, 1f);
            var body = new Color(0.18f, 0.45f, 0.22f, 1f);
            var lid = new Color(0.12f, 0.32f, 0.16f, 1f);
            var wheel = new Color(0.05f, 0.05f, 0.05f, 1f);
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    Color color = background;
                    bool inBody = x >= 34 && x <= 94 && y >= 28 && y <= 96;
                    bool inLid = x >= 30 && x <= 98 && y >= 96 && y <= 112;
                    bool leftWheel = (x - 42) * (x - 42) + (y - 24) * (y - 24) < 12 * 12;
                    bool rightWheel = (x - 86) * (x - 86) + (y - 24) * (y - 24) < 12 * 12;
                    if (inBody)
                        color = body;
                    if (inLid)
                        color = lid;
                    if (leftWheel || rightWheel)
                        color = wheel;
                    image.SetPixel(x, y, color);
                }
            }

            image.Apply();
            File.WriteAllBytes(ToFull(assetPath), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
        }

        static void SaveCatalog(GameObject prefab, Sprite thumbnail)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AssetCatalog>(PlaceableIds.CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AssetCatalog>();
                AssetDatabase.CreateAsset(catalog, PlaceableIds.CatalogPath);
            }

            if (catalog.entries == null)
                catalog.entries = new List<PlaceableAssetEntry>();
            PlaceableAssetEntry entry = catalog.Find(PlaceableIds.Bin);
            if (entry == null)
            {
                entry = new PlaceableAssetEntry();
                catalog.entries.Add(entry);
            }

            entry.catalogId = PlaceableIds.Bin;
            entry.displayName = "Bin";
            entry.category = PlaceableCategory.Bin;
            entry.assetType = PlaceableIds.BinType;
            entry.prefab = prefab;
            entry.thumbnail = thumbnail;
            entry.uniformScale = 1f;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }

        static float[] ReadFloats(byte[] bin, JsonMini accessors, JsonMini views, int accessorIndex, int components)
        {
            JsonMini accessor = accessors[accessorIndex];
            int count = accessor.Get("count").Int;
            JsonMini view = views[accessor.Get("bufferView").Int];
            int offset = (view.Try("byteOffset", out JsonMini viewOffset) ? viewOffset.Int : 0) + (accessor.Try("byteOffset", out JsonMini accessorOffset) ? accessorOffset.Int : 0);
            int stride = view.Try("byteStride", out JsonMini strideNode) && strideNode.Int > 0 ? strideNode.Int : components * 4;
            var values = new float[count * components];
            for (int i = 0; i < count; i++)
            {
                int at = offset + i * stride;
                for (int c = 0; c < components; c++)
                    values[i * components + c] = BitConverter.ToSingle(bin, at + c * 4);
            }

            return values;
        }

        static int[] ReadIndices(byte[] bin, JsonMini accessors, JsonMini views, int accessorIndex)
        {
            JsonMini accessor = accessors[accessorIndex];
            int count = accessor.Get("count").Int;
            int component = accessor.Get("componentType").Int;
            JsonMini view = views[accessor.Get("bufferView").Int];
            int offset = (view.Try("byteOffset", out JsonMini viewOffset) ? viewOffset.Int : 0) + (accessor.Try("byteOffset", out JsonMini accessorOffset) ? accessorOffset.Int : 0);
            int width = component == 5125 ? 4 : component == 5123 ? 2 : 1;
            int stride = view.Try("byteStride", out JsonMini strideNode) && strideNode.Int > 0 ? strideNode.Int : width;
            var indices = new int[count];
            for (int i = 0; i < count; i++)
            {
                int at = offset + i * stride;
                if (width == 4)
                    indices[i] = BitConverter.ToInt32(bin, at);
                else if (width == 2)
                    indices[i] = BitConverter.ToUInt16(bin, at);
                else
                    indices[i] = bin[at];
            }

            return indices;
        }

        static string SafeObjectName(string name, int index)
        {
            if (string.IsNullOrEmpty(name))
                return "Part" + index;
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == '/' || chars[i] == '\\' || chars[i] == ':' || chars[i] == '\0')
                    chars[i] = '_';
            }

            string cleaned = new string(chars).Trim();
            return string.IsNullOrEmpty(cleaned) ? "Part" + index : cleaned;
        }

        static string ToFull(string assetPath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
