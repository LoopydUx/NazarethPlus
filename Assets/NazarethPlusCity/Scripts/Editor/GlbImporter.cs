using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NazarethPlusCity.EditorTools
{
    /// <summary>
    /// glTF binary reader shared with the wheelie-bin prefab, plus a building prefab builder.
    /// Meshes are converted the same way as the bin: glTF Y-up right-handed becomes Unity Y-up by flipping Z and winding.
    /// </summary>
    public static class GlbImporter
    {
        public const int FormatVersion = 1;

        public static void Read(byte[] file, out string json, out byte[] bin)
        {
            if (file == null || file.Length < 20 || file[0] != 'g' || file[1] != 'l' || file[2] != 'T' || file[3] != 'F')
                throw new InvalidDataException("The file is not a glTF binary.");
            int jsonLength = BitConverter.ToInt32(file, 12);
            if (jsonLength < 0 || 20 + jsonLength > file.Length)
                throw new InvalidDataException("The glTF JSON chunk is outside the file.");
            json = System.Text.Encoding.UTF8.GetString(file, 20, jsonLength).Trim();
            int binHeader = 20 + jsonLength;
            if (binHeader + 8 > file.Length)
                throw new InvalidDataException("The glTF binary chunk is missing.");
            int binLength = BitConverter.ToInt32(file, binHeader);
            if (binLength < 0 || binHeader + 8 + binLength > file.Length)
                throw new InvalidDataException("The glTF binary chunk is outside the file.");
            bin = new byte[binLength];
            Buffer.BlockCopy(file, binHeader + 8, bin, 0, binLength);
        }

        public static float[] ReadFloats(byte[] bin, JsonMini accessors, JsonMini views, int accessorIndex, int components)
        {
            JsonMini accessor = accessors[accessorIndex];
            int componentType = accessor.Get("componentType").Int;
            if (componentType != 5126)
                throw new InvalidDataException("Accessor " + accessorIndex + " is not float32.");
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

        public static int[] ReadIndices(byte[] bin, JsonMini accessors, JsonMini views, int accessorIndex)
        {
            JsonMini accessor = accessors[accessorIndex];
            int count = accessor.Get("count").Int;
            int component = accessor.Get("componentType").Int;
            JsonMini view = views[accessor.Get("bufferView").Int];
            int offset = (view.Try("byteOffset", out JsonMini viewOffset) ? viewOffset.Int : 0) + (accessor.Try("byteOffset", out JsonMini accessorOffset) ? accessorOffset.Int : 0);
            int width = component == 5125 ? 4 : component == 5123 ? 2 : component == 5121 ? 1 : 0;
            if (width == 0)
                throw new InvalidDataException("Index accessor " + accessorIndex + " uses component type " + component + ".");
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

        public static GameObject ImportBuildingPrefab(string sourceFile, byte[] file, string assetFolder, string contentHash)
        {
            Read(file, out string json, out byte[] bin);
            JsonMini root = JsonMini.Parse(json);
            if (AssetDatabase.IsValidFolder(assetFolder))
                AssetDatabase.DeleteAsset(assetFolder);
            CityMaterialFactory.EnsureFolder(assetFolder);
            string textureFolder = assetFolder + "/Textures";
            string materialFolder = assetFolder + "/Materials";
            CityMaterialFactory.EnsureFolder(textureFolder);
            CityMaterialFactory.EnsureFolder(materialFolder);
            var materials = BuildMaterials(root, bin, textureFolder, materialFolder);
            var parts = BuildParts(root, bin, materials.Count);
            if (parts.Count == 0)
                throw new InvalidOperationException(sourceFile + " produced no triangle meshes.");
            string meshPath = assetFolder + "/Meshes.asset";
            var collision = new Mesh { name = "Collision" };
            int vertices = 0;
            var collisionVertices = new List<Vector3>();
            var collisionIndices = new List<int>();
            for (int i = 0; i < parts.Count; i++)
            {
                vertices += parts[i].Vertices.Count;
                int offset = collisionVertices.Count;
                collisionVertices.AddRange(parts[i].Vertices);
                for (int t = 0; t < parts[i].Indices.Count; t++)
                    collisionIndices.Add(parts[i].Indices[t] + offset);
            }

            collision.indexFormat = collisionVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            collision.vertices = collisionVertices.ToArray();
            collision.triangles = collisionIndices.ToArray();
            collision.RecalculateBounds();
            AssetDatabase.CreateAsset(collision, meshPath);
            var unityMeshes = new List<Mesh>(parts.Count);
            for (int i = 0; i < parts.Count; i++)
            {
                var mesh = parts[i].ToMesh();
                AssetDatabase.AddObjectToAsset(mesh, collision);
                unityMeshes.Add(mesh);
            }

            AssetDatabase.SaveAssets();
            var rootObject = new GameObject(Path.GetFileNameWithoutExtension(sourceFile));
            try
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    var child = new GameObject(parts[i].Name);
                    child.transform.SetParent(rootObject.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = unityMeshes[i];
                    var renderer = child.AddComponent<MeshRenderer>();
                    Material material = materials[Mathf.Clamp(parts[i].MaterialIndex, 0, materials.Count - 1)];
                    CityGeometry.ConfigureRenderer(renderer, material, null);
                }

                var collider = rootObject.AddComponent<MeshCollider>();
                collider.convex = false;
                collider.sharedMesh = collision;
                var visual = rootObject.AddComponent<BuildingModelVisual>();
                visual.sourceFile = sourceFile;
                visual.contentHash = contentHash;
                visual.importVersion = FormatVersion;
                visual.materialCount = parts.Count;
                visual.vertexCount = vertices;
                string prefabPath = assetFolder + "/Model.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(rootObject, prefabPath);
                if (prefab == null)
                    throw new InvalidOperationException("Unity did not save the building prefab at " + prefabPath + ".");
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rootObject);
            }
        }

        sealed class PartDraft
        {
            public string Name;
            public int MaterialIndex;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<Vector4> Tangents = new List<Vector4>();
            public readonly List<int> Indices = new List<int>();
            public bool HasNormals;
            public bool HasUvs;
            public bool HasTangents;

            public Mesh ToMesh()
            {
                var mesh = new Mesh { name = Name };
                mesh.indexFormat = Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.vertices = Vertices.ToArray();
                if (HasNormals && Normals.Count == Vertices.Count)
                    mesh.normals = Normals.ToArray();
                else
                    mesh.RecalculateNormals();
                if (HasUvs && Uvs.Count == Vertices.Count)
                    mesh.uv = Uvs.ToArray();
                if (HasTangents && Tangents.Count == Vertices.Count)
                    mesh.tangents = Tangents.ToArray();
                else if (mesh.uv != null && mesh.uv.Length == mesh.vertexCount)
                    mesh.RecalculateTangents();
                mesh.triangles = Indices.ToArray();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        static List<PartDraft> BuildParts(JsonMini root, byte[] bin, int materialCount)
        {
            JsonMini nodes = root.Get("nodes");
            JsonMini meshes = root.Get("meshes");
            JsonMini accessors = root.Get("accessors");
            JsonMini views = root.Get("bufferViews");
            JsonMini materials = root.Get("materials");
            if (meshes == null || accessors == null || views == null)
                throw new InvalidDataException("The glTF file has no meshes.");
            var parts = new List<PartDraft>();
            var byMaterial = new Dictionary<int, PartDraft>();
            var placed = new List<PlacedMesh>();
            if (nodes != null)
                CollectSceneMeshes(root, nodes, placed);
            if (placed.Count == 0)
            {
                for (int i = 0; i < meshes.Count; i++)
                    placed.Add(new PlacedMesh { Mesh = i, Matrix = Matrix4x4.identity });
            }

            for (int p = 0; p < placed.Count; p++)
            {
                JsonMini mesh = meshes[placed[p].Mesh];
                JsonMini primitives = mesh.Get("primitives");
                if (primitives == null)
                    continue;
                Matrix4x4 matrix = placed[p].Matrix;
                bool keepWinding = matrix.determinant < 0f;
                Matrix4x4 normalMatrix = matrix.inverse.transpose;
                for (int prim = 0; prim < primitives.Count; prim++)
                {
                    JsonMini primitive = primitives[prim];
                    int mode = primitive.Try("mode", out JsonMini modeNode) ? modeNode.Int : 4;
                    if (mode != 4)
                        continue;
                    JsonMini attributes = primitive.Get("attributes");
                    if (attributes == null || attributes.Get("POSITION") == null)
                        continue;
                    float[] position = ReadFloats(bin, accessors, views, attributes.Get("POSITION").Int, 3);
                    float[] normal = attributes.Get("NORMAL") != null ? ReadFloats(bin, accessors, views, attributes.Get("NORMAL").Int, 3) : null;
                    float[] uv = attributes.Get("TEXCOORD_0") != null ? ReadFloats(bin, accessors, views, attributes.Get("TEXCOORD_0").Int, 2) : null;
                    float[] tangent = attributes.Get("TANGENT") != null ? ReadFloats(bin, accessors, views, attributes.Get("TANGENT").Int, 4) : null;
                    int[] indices = primitive.Get("indices") != null
                        ? ReadIndices(bin, accessors, views, primitive.Get("indices").Int)
                        : Sequential(position.Length / 3);
                    int materialIndex = 0;
                    if (primitive.Try("material", out JsonMini materialNode))
                        materialIndex = materialNode.Int;
                    if (materialCount > 0)
                        materialIndex = Mathf.Clamp(materialIndex, 0, materialCount - 1);
                    if (!byMaterial.TryGetValue(materialIndex, out PartDraft draft))
                    {
                        string materialName = materials != null && materialIndex < materials.Count && materials[materialIndex].Try("name", out JsonMini named)
                            ? named.String
                            : "Material" + materialIndex;
                        draft = new PartDraft
                        {
                            Name = SafeName(materialIndex.ToString("00") + "_" + materialName, parts.Count),
                            MaterialIndex = materialIndex
                        };
                        byMaterial.Add(materialIndex, draft);
                        parts.Add(draft);
                    }

                    int offset = draft.Vertices.Count;
                    int count = position.Length / 3;
                    for (int v = 0; v < count; v++)
                    {
                        Vector3 point = matrix.MultiplyPoint3x4(new Vector3(position[v * 3], position[v * 3 + 1], position[v * 3 + 2]));
                        point.z = -point.z;
                        draft.Vertices.Add(point);
                        if (normal != null)
                        {
                            Vector3 n = normalMatrix.MultiplyVector(new Vector3(normal[v * 3], normal[v * 3 + 1], normal[v * 3 + 2]));
                            n.z = -n.z;
                            draft.Normals.Add(n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.up);
                            draft.HasNormals = true;
                        }

                        if (uv != null)
                        {
                            draft.Uvs.Add(new Vector2(uv[v * 2], 1f - uv[v * 2 + 1]));
                            draft.HasUvs = true;
                        }

                        if (tangent != null)
                        {
                            Vector3 t = matrix.MultiplyVector(new Vector3(tangent[v * 4], tangent[v * 4 + 1], tangent[v * 4 + 2]));
                            t.z = -t.z;
                            float w = -tangent[v * 4 + 3];
                            draft.Tangents.Add(new Vector4(t.x, t.y, t.z, w));
                            draft.HasTangents = true;
                        }
                    }

                    for (int t = 0; t + 2 < indices.Length; t += 3)
                    {
                        int a = indices[t];
                        int b = indices[t + 1];
                        int c = indices[t + 2];
                        if (!keepWinding)
                        {
                            int swap = a;
                            a = c;
                            c = swap;
                        }

                        draft.Indices.Add(a + offset);
                        draft.Indices.Add(b + offset);
                        draft.Indices.Add(c + offset);
                    }
                }
            }

            return parts;
        }

        sealed class PlacedMesh
        {
            public int Mesh;
            public Matrix4x4 Matrix;
        }

        static void CollectSceneMeshes(JsonMini root, JsonMini nodes, List<PlacedMesh> placed)
        {
            JsonMini scenes = root.Get("scenes");
            if (scenes == null || scenes.Count == 0)
                return;
            int sceneIndex = root.Try("scene", out JsonMini sceneNode) ? sceneNode.Int : 0;
            sceneIndex = Mathf.Clamp(sceneIndex, 0, scenes.Count - 1);
            JsonMini roots = scenes[sceneIndex].Get("nodes");
            if (roots == null)
                return;
            for (int i = 0; i < roots.Count; i++)
                Walk(nodes, roots[i].Int, Matrix4x4.identity, placed);
        }

        static void Walk(JsonMini nodes, int index, Matrix4x4 parent, List<PlacedMesh> placed)
        {
            if (index < 0 || index >= nodes.Count)
                return;
            JsonMini node = nodes[index];
            Matrix4x4 local = NodeMatrix(node);
            Matrix4x4 world = parent * local;
            if (node.Try("mesh", out JsonMini meshNode))
            {
                placed.Add(new PlacedMesh
                {
                    Mesh = meshNode.Int,
                    Matrix = world
                });
            }

            JsonMini children = node.Get("children");
            if (children == null)
                return;
            for (int i = 0; i < children.Count; i++)
                Walk(nodes, children[i].Int, world, placed);
        }

        static Matrix4x4 NodeMatrix(JsonMini node)
        {
            if (node.Try("matrix", out JsonMini matrix) && matrix.Count >= 16)
            {
                return new Matrix4x4
                {
                    m00 = (float)matrix[0].Number, m10 = (float)matrix[1].Number, m20 = (float)matrix[2].Number, m30 = (float)matrix[3].Number,
                    m01 = (float)matrix[4].Number, m11 = (float)matrix[5].Number, m21 = (float)matrix[6].Number, m31 = (float)matrix[7].Number,
                    m02 = (float)matrix[8].Number, m12 = (float)matrix[9].Number, m22 = (float)matrix[10].Number, m32 = (float)matrix[11].Number,
                    m03 = (float)matrix[12].Number, m13 = (float)matrix[13].Number, m23 = (float)matrix[14].Number, m33 = (float)matrix[15].Number
                };
            }

            Vector3 translation = node.Try("translation", out JsonMini t) && t.Count >= 3
                ? new Vector3((float)t[0].Number, (float)t[1].Number, (float)t[2].Number)
                : Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            if (node.Try("rotation", out JsonMini r) && r.Count >= 4)
                rotation = new Quaternion((float)r[0].Number, (float)r[1].Number, (float)r[2].Number, (float)r[3].Number);
            Vector3 scale = node.Try("scale", out JsonMini s) && s.Count >= 3
                ? new Vector3((float)s[0].Number, (float)s[1].Number, (float)s[2].Number)
                : Vector3.one;
            return Matrix4x4.TRS(translation, rotation, scale);
        }

        static int[] Sequential(int count)
        {
            var indices = new int[count];
            for (int i = 0; i < count; i++)
                indices[i] = i;
            return indices;
        }

        static List<Material> BuildMaterials(JsonMini root, byte[] bin, string textureFolder, string materialFolder)
        {
            JsonMini materialNodes = root.Get("materials");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader");
            if (shader == null)
                throw new FileNotFoundException("Universal Render Pipeline/Lit was not found.");
            if (materialNodes == null || materialNodes.Count == 0)
            {
                var fallback = new Material(shader) { name = "Default" };
                AssetDatabase.CreateAsset(fallback, materialFolder + "/M_00_Default.mat");
                return new List<Material> { fallback };
            }

            JsonMini images = root.Get("images");
            JsonMini textures = root.Get("textures");
            JsonMini views = root.Get("bufferViews");
            var cache = new Dictionary<string, Texture2D>();
            var materials = new List<Material>(materialNodes.Count);
            for (int i = 0; i < materialNodes.Count; i++)
            {
                JsonMini node = materialNodes[i];
                string name = node.Try("name", out JsonMini named) ? named.String : "Material" + i;
                string path = materialFolder + "/M_" + i.ToString("00") + "_" + SafeFile(name) + ".mat";
                var material = new Material(shader) { name = SafeName(name, i) };
                JsonMini pbr = node.Get("pbrMetallicRoughness");
                Color color = Color.white;
                float metallic = 0f;
                float roughness = 0.6f;
                if (pbr != null)
                {
                    if (pbr.Try("baseColorFactor", out JsonMini factor) && factor.Count >= 3)
                        color = new Color(Mathf.Clamp01((float)factor[0].Number), Mathf.Clamp01((float)factor[1].Number), Mathf.Clamp01((float)factor[2].Number), 1f);
                    if (pbr.Try("metallicFactor", out JsonMini metal))
                        metallic = Mathf.Clamp01((float)metal.Number);
                    if (pbr.Try("roughnessFactor", out JsonMini rough))
                        roughness = Mathf.Clamp01((float)rough.Number);
                }

                material.SetColor("_BaseColor", color);
                material.SetFloat("_Metallic", metallic);
                material.SetFloat("_Smoothness", Mathf.Clamp01(1f - roughness));
                material.SetFloat("_Cull", node.Try("doubleSided", out JsonMini sided) && sided.Bool ? 0f : 2f);
                Texture2D albedo = LoadTexture(bin, images, textures, views, pbr != null ? pbr.Get("baseColorTexture") : null, textureFolder, cache, false, false);
                Texture2D normal = LoadTexture(bin, images, textures, views, node.Get("normalTexture"), textureFolder, cache, true, true);
                Texture2D smoothness = LoadSmoothness(bin, images, textures, views, pbr != null ? pbr.Get("metallicRoughnessTexture") : null, textureFolder, cache);
                if (albedo != null)
                {
                    material.SetTexture("_BaseMap", albedo);
                    material.EnableKeyword("_BASEMAP"); // no-op if the shader does not use it; _BaseMap is enough for URP Lit
                }

                if (smoothness != null)
                {
                    material.SetTexture("_MetallicGlossMap", smoothness);
                    material.SetFloat("_Metallic", 1f);
                    material.SetFloat("_Smoothness", 1f);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                }

                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    float scale = 1f;
                    JsonMini normalNode = node.Get("normalTexture");
                    if (normalNode != null && normalNode.Try("scale", out JsonMini scaleNode))
                        scale = (float)scaleNode.Number;
                    material.SetFloat("_BumpScale", scale);
                    material.EnableKeyword("_NORMALMAP");
                }

                AssetDatabase.CreateAsset(material, path);
                materials.Add(material);
            }

            return materials;
        }

        static Texture2D LoadTexture(byte[] bin, JsonMini images, JsonMini textures, JsonMini views, JsonMini textureNode, string folder, Dictionary<string, Texture2D> cache, bool normal, bool linear)
        {
            if (textureNode == null || images == null || textures == null)
                return null;
            int textureIndex = textureNode.Get("index").Int;
            if (textureIndex < 0 || textureIndex >= textures.Count)
                return null;
            int imageIndex = textures[textureIndex].Get("source").Int;
            string key = imageIndex + (normal ? ":n" : linear ? ":l" : ":c");
            if (cache.TryGetValue(key, out Texture2D cached))
                return cached;
            string extension = ImageExtension(bin, images, views, imageIndex);
            string path = folder + "/Image_" + imageIndex.ToString("00") + (normal ? "_Normal" : linear ? "_Linear" : "_Color") + extension;
            WriteImage(bin, images, views, imageIndex, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Texture did not import from " + path + ".");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal && !linear;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            cache[key] = texture;
            return texture;
        }

        static Texture2D LoadSmoothness(byte[] bin, JsonMini images, JsonMini textures, JsonMini views, JsonMini textureNode, string folder, Dictionary<string, Texture2D> cache)
        {
            if (textureNode == null || images == null || textures == null)
                return null;
            int textureIndex = textureNode.Get("index").Int;
            if (textureIndex < 0 || textureIndex >= textures.Count)
                return null;
            int imageIndex = textures[textureIndex].Get("source").Int;
            string key = imageIndex + ":mr";
            if (cache.TryGetValue(key, out Texture2D cached))
                return cached;
            string sourcePath = folder + "/Image_" + imageIndex.ToString("00") + "_RoughnessSource" + ImageExtension(bin, images, views, imageIndex);
            WriteImage(bin, images, views, imageIndex, sourcePath);
            AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Roughness texture did not import from " + sourcePath + ".");
            importer.sRGBTexture = false;
            importer.textureType = TextureImporterType.Default;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            if (texture == null)
                throw new InvalidOperationException("Roughness texture could not be read from " + sourcePath + ".");
            Color[] pixels = texture.GetPixels();
            var baked = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
            for (int i = 0; i < pixels.Length; i++)
            {
                float metallic = Mathf.Clamp01(pixels[i].b);
                float smoothness = Mathf.Clamp01(1f - pixels[i].g);
                baked.SetPixel(i % texture.width, i / texture.width, new Color(metallic, 0f, 0f, smoothness));
            }

            baked.Apply();
            string path = folder + "/Image_" + imageIndex.ToString("00") + "_MetallicSmoothness.png";
            File.WriteAllBytes(ToFull(path), baked.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(baked);
            AssetDatabase.DeleteAsset(sourcePath);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var bakedImporter = (TextureImporter)AssetImporter.GetAtPath(path);
            bakedImporter.sRGBTexture = false;
            bakedImporter.mipmapEnabled = true;
            bakedImporter.SaveAndReimport();
            var result = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            cache[key] = result;
            return result;
        }

        static void WriteImage(byte[] bin, JsonMini images, JsonMini views, int imageIndex, string assetPath)
        {
            JsonMini image = images[imageIndex];
            if (!image.Try("bufferView", out JsonMini viewNode))
                throw new InvalidDataException("Image " + imageIndex + " is not embedded in the GLB.");
            JsonMini view = views[viewNode.Int];
            int offset = view.Try("byteOffset", out JsonMini byteOffset) ? byteOffset.Int : 0;
            int length = view.Get("byteLength").Int;
            var bytes = new byte[length];
            Buffer.BlockCopy(bin, offset, bytes, 0, length);
            File.WriteAllBytes(ToFull(assetPath), bytes);
        }

        static string ImageExtension(byte[] bin, JsonMini images, JsonMini views, int imageIndex)
        {
            JsonMini image = images[imageIndex];
            if (image.Try("mimeType", out JsonMini mime) && mime.String != null && mime.String.IndexOf("jpeg", StringComparison.OrdinalIgnoreCase) >= 0)
                return ".jpg";
            if (!image.Try("bufferView", out JsonMini viewNode))
                return ".png";
            JsonMini view = views[viewNode.Int];
            int offset = view.Try("byteOffset", out JsonMini byteOffset) ? byteOffset.Int : 0;
            if (offset + 2 < bin.Length && bin[offset] == 0xFF && bin[offset + 1] == 0xD8)
                return ".jpg";
            return ".png";
        }

        static string SafeName(string name, int index)
        {
            if (string.IsNullOrEmpty(name))
                return "Part" + index;
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c == '/' || c == '\\' || c == ':' || c == '\0')
                    chars[i] = '_';
            }

            string cleaned = new string(chars).Trim();
            return string.IsNullOrEmpty(cleaned) ? "Part" + index : cleaned;
        }

        static string SafeFile(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Material";
            var chars = new char[name.Length];
            int n = 0;
            for (int i = 0; i < name.Length && n < 48; i++)
            {
                char c = name[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    chars[n++] = c;
                else if (char.IsWhiteSpace(c) && n > 0 && chars[n - 1] != '_')
                    chars[n++] = '_';
            }

            return n == 0 ? "Material" : new string(chars, 0, n);
        }

        static string ToFull(string assetPath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
