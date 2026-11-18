using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NazarethPlusCity.EditorTools
{
    public static class WoodenLadderPrefabBuilder
    {
        public static GameObject BuildOrUpdate()
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            string glbPath = Path.Combine(project, "Data", "building_models", "Assets", LadderIds.SourceFile);
            if (!File.Exists(glbPath))
                glbPath = Path.Combine(project, "Data", "building_models", LadderIds.SourceFile);
            if (!File.Exists(glbPath))
            {
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(LadderIds.PrefabPath);
                if (existing != null)
                {
                    Debug.LogWarning("Ladder GLB was not found at " + glbPath + ". The existing prefab was kept.");
                    return existing;
                }

                throw new FileNotFoundException("Ladder GLB was not found.", glbPath);
            }

            CityMaterialFactory.EnsureFolder("Assets/NazarethPlusCity/Art");
            CityMaterialFactory.EnsureFolder("Assets/NazarethPlusCity/Art/Props");
            CityMaterialFactory.EnsureFolder("Assets/NazarethPlusCity/Art/Props/WoodenLadder");
            CityMaterialFactory.EnsureFolder(LadderIds.TextureFolder);
            CityMaterialFactory.EnsureFolder(LadderIds.MaterialFolder);

            byte[] file = File.ReadAllBytes(glbPath);
            ReadGlb(file, out string json, out byte[] bin);
            JsonMini root = JsonMini.Parse(json);
            Material material = BuildMaterial(root, bin);
            Mesh mesh = BuildMesh(root, bin);
            GameObject prefab = AssemblePrefab(mesh, material, out float sectionHeight, out float width, out float thickness);
            WriteThumbnail(prefab);
            UpdateSettings(sectionHeight, width, thickness);
            Debug.Log("Built wooden ladder section prefab at " + LadderIds.PrefabPath
                + ". Usable height " + sectionHeight.ToString("0.000") + " m, width "
                + width.ToString("0.000") + " m, thickness " + thickness.ToString("0.000") + " m.");
            return prefab;
        }

        static void ReadGlb(byte[] file, out string json, out byte[] bin)
        {
            if (file.Length < 20 || file[0] != 'g' || file[1] != 'l' || file[2] != 'T' || file[3] != 'F')
                throw new InvalidDataException("The ladder file is not a glTF binary.");
            int jsonLength = BitConverter.ToInt32(file, 12);
            json = Encoding.UTF8.GetString(file, 20, jsonLength).Trim('\0');
            int binHeader = 20 + jsonLength;
            int binLength = BitConverter.ToInt32(file, binHeader);
            bin = new byte[binLength];
            Buffer.BlockCopy(file, binHeader + 8, bin, 0, binLength);
        }

        static Material BuildMaterial(JsonMini root, byte[] bin)
        {
            JsonMini images = root.Get("images");
            JsonMini textures = root.Get("textures");
            JsonMini views = root.Get("bufferViews");
            JsonMini materialNodes = root.Get("materials");
            int albedoImage = -1;
            int normalImage = -1;
            int roughnessImage = -1;
            Color color = new Color(0.55f, 0.38f, 0.22f, 1f);
            float metallic = 0f;
            float roughness = 0.72f;
            if (materialNodes != null && materialNodes.Count > 0)
            {
                JsonMini node = materialNodes[0];
                JsonMini pbr = node.Get("pbrMetallicRoughness");
                if (pbr != null)
                {
                    if (pbr.Try("baseColorFactor", out JsonMini factor) && factor.Count >= 3)
                        color = new Color(Mathf.Min(1f, (float)factor[0].Number), Mathf.Min(1f, (float)factor[1].Number), Mathf.Min(1f, (float)factor[2].Number), 1f);
                    if (pbr.Try("metallicFactor", out JsonMini metal))
                        metallic = (float)metal.Number;
                    if (pbr.Try("roughnessFactor", out JsonMini rough))
                        roughness = (float)rough.Number;
                    if (pbr.Try("baseColorTexture", out JsonMini baseTex) && textures != null)
                        albedoImage = textures[baseTex.Get("index").Int].Get("source").Int;
                    if (pbr.Try("metallicRoughnessTexture", out JsonMini mr) && textures != null)
                        roughnessImage = textures[mr.Get("index").Int].Get("source").Int;
                }

                if (node.Try("normalTexture", out JsonMini normalTex) && textures != null)
                    normalImage = textures[normalTex.Get("index").Int].Get("source").Int;
            }

            string albedo = null;
            string normal = null;
            if (albedoImage >= 0 && images != null)
                albedo = ImportTexture(WriteImage(bin, images, views, albedoImage, LadderIds.TextureFolder + "/Ladder_Albedo.png"), false);
            if (normalImage >= 0 && images != null)
                normal = ImportTexture(WriteImage(bin, images, views, normalImage, LadderIds.TextureFolder + "/Ladder_Normal.png"), true);

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader");
            if (shader == null)
                throw new FileNotFoundException("Universal Render Pipeline/Lit was not found for the ladder material.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(LadderIds.MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, LadderIds.MaterialPath);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", Mathf.Clamp01(1f - roughness));
            if (albedo != null)
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
            if (normal != null)
            {
                material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
                material.EnableKeyword("_NORMALMAP");
            }

            if (roughnessImage >= 0)
                Debug.Log("Ladder roughness texture index " + roughnessImage + " was present; URP smoothness uses the roughness factor.");

            EditorUtility.SetDirty(material);
            return material;
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

        static string ImportTexture(string assetPath, bool normal)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return assetPath;
        }

        static Mesh BuildMesh(JsonMini root, byte[] bin)
        {
            JsonMini meshNodes = root.Get("meshes");
            JsonMini accessors = root.Get("accessors");
            JsonMini views = root.Get("bufferViews");
            if (meshNodes == null || meshNodes.Count < 1)
                throw new InvalidOperationException("The ladder GLB has no meshes.");
            JsonMini primitive = meshNodes[0].Get("primitives")[0];
            JsonMini attributes = primitive.Get("attributes");
            float[] position = ReadFloats(bin, accessors, views, attributes.Get("POSITION").Int, 3);
            float[] normal = attributes.Get("NORMAL") != null ? ReadFloats(bin, accessors, views, attributes.Get("NORMAL").Int, 3) : null;
            float[] uv = attributes.Get("TEXCOORD_0") != null ? ReadFloats(bin, accessors, views, attributes.Get("TEXCOORD_0").Int, 2) : null;
            int[] indices = ReadIndices(bin, accessors, views, primitive.Get("indices").Int);
            int count = position.Length / 3;
            var mesh = new Mesh { name = "WoodenLadderSection" };
            if (count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            var vertices = new Vector3[count];
            var normals = normal != null ? new Vector3[count] : null;
            var uvs = uv != null ? new Vector2[count] : null;
            for (int v = 0; v < count; v++)
            {
                // glTF Z-up source with 0.01 scene scale; longest axis is ladder length (X).
                Vector3 p = new Vector3(position[v * 3], position[v * 3 + 1], -position[v * 3 + 2]) * LadderIds.SourceUniformScale;
                // Stand the ladder up: source X (length) -> Unity Y, source Y (thickness) -> Unity Z, source Z (width) -> Unity X.
                // DenseRungScale compresses height so every section keeps close rung spacing (not only the short final piece).
                vertices[v] = new Vector3(p.z, p.x * LadderIds.DenseRungScale, p.y);
                if (normals != null)
                {
                    Vector3 n = new Vector3(normal[v * 3], normal[v * 3 + 1], -normal[v * 3 + 2]);
                    Vector3 oriented = new Vector3(n.z, n.x, n.y);
                    oriented.y *= LadderIds.DenseRungScale > 0.0001f ? (1f / LadderIds.DenseRungScale) : 1f;
                    normals[v] = oriented.normalized;
                }

                if (uvs != null)
                    uvs[v] = new Vector2(uv[v * 2], 1f - uv[v * 2 + 1]);
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
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            if (AssetDatabase.LoadAssetAtPath<Mesh>(LadderIds.MeshPath) != null)
                AssetDatabase.DeleteAsset(LadderIds.MeshPath);
            AssetDatabase.CreateAsset(mesh, LadderIds.MeshPath);
            AssetDatabase.SaveAssets();
            return mesh;
        }

        static GameObject AssemblePrefab(Mesh mesh, Material material, out float sectionHeight, out float width, out float thickness)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(LadderIds.PrefabPath) != null)
                AssetDatabase.DeleteAsset(LadderIds.PrefabPath);
            var root = new GameObject("WoodenLadderSection");
            var model = new GameObject("Model");
            model.transform.SetParent(root.transform, false);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = model.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            Bounds bounds = renderer.bounds;
            model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            bounds = renderer.bounds;
            sectionHeight = bounds.size.y;
            width = bounds.size.x;
            thickness = bounds.size.z;

            var bottom = new GameObject("BottomSnap");
            bottom.transform.SetParent(root.transform, false);
            bottom.transform.localPosition = Vector3.zero;
            var top = new GameObject("TopSnap");
            top.transform.SetParent(root.transform, false);
            top.transform.localPosition = new Vector3(0f, sectionHeight, 0f);
            var climb = new GameObject("ClimbCenter");
            climb.transform.SetParent(root.transform, false);
            climb.transform.localPosition = new Vector3(0f, sectionHeight * 0.5f, -0.05f);

            var section = root.AddComponent<LadderSection>();
            section.sectionHeight = sectionHeight;
            section.climbWidth = width;
            section.climbDepth = Mathf.Max(0.25f, thickness + 0.15f);
            section.bottomSnap = bottom.transform;
            section.topSnap = top.transform;
            section.climbCenter = climb.transform;

            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, sectionHeight * 0.5f, 0f);
            box.size = new Vector3(width, sectionHeight, Mathf.Max(0.2f, thickness + 0.1f));

            try
            {
                float expected = 2.4203f * LadderIds.DenseRungScale;
                if (sectionHeight < expected * 0.85f || sectionHeight > expected * 1.15f)
                    throw new InvalidOperationException("Ladder section height is " + sectionHeight.ToString("0.00")
                        + " m after dense import. Expected about " + expected.ToString("0.00") + " m.");
                return PrefabUtility.SaveAsPrefabAsset(root, LadderIds.PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void UpdateSettings(float sectionHeight, float width, float thickness)
        {
            var settings = AssetDatabase.LoadAssetAtPath<LadderSystemSettings>(LadderIds.SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LadderSystemSettings>();
                AssetDatabase.CreateAsset(settings, LadderIds.SettingsPath);
            }

            settings.sourceMeshHeightMeters = sectionHeight / Mathf.Max(0.0001f, LadderIds.DenseRungScale);
            settings.denseRungScale = LadderIds.DenseRungScale;
            settings.sectionHeightMeters = sectionHeight;
            settings.sectionWidthMeters = width;
            settings.sectionThicknessMeters = thickness;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        static void WriteThumbnail(GameObject prefab)
        {
            var image = new Texture2D(128, 128, TextureFormat.RGB24, false);
            var background = new Color(0.16f, 0.15f, 0.13f, 1f);
            var wood = new Color(0.55f, 0.38f, 0.22f, 1f);
            var rail = new Color(0.35f, 0.24f, 0.14f, 1f);
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    Color color = background;
                    bool left = x >= 40 && x <= 48 && y >= 16 && y <= 112;
                    bool right = x >= 80 && x <= 88 && y >= 16 && y <= 112;
                    bool rung = false;
                    for (int r = 0; r < 6; r++)
                    {
                        int ry = 24 + r * 14;
                        if (x >= 40 && x <= 88 && y >= ry && y <= ry + 4)
                            rung = true;
                    }

                    if (left || right)
                        color = rail;
                    if (rung)
                        color = wood;
                    image.SetPixel(x, y, color);
                }
            }

            image.Apply();
            File.WriteAllBytes(ToFull(LadderIds.ThumbnailPath), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            AssetDatabase.ImportAsset(LadderIds.ThumbnailPath, ImportAssetOptions.ForceUpdate);
            if (prefab == null)
                return;
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

        static string ToFull(string assetPath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
