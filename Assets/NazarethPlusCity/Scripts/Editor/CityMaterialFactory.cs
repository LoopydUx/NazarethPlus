using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NazarethPlusCity.EditorTools
{
    public sealed class CityMaterials
    {
        public Material[] Stone;
        public Material Roof;
        public Material Asphalt;
        public Material Ground;
        public Material Foundation;
        public Material Outline;
        public Material Skybox;
    }

    public static class CityMaterialFactory
    {
        const string TextureFolder = "Assets/NazarethPlusCity/Textures";
        const string MaterialFolder = "Assets/NazarethPlusCity/Materials";

        static readonly Color[] StoneTints =
        {
            new Color(1.00f, 0.98f, 0.94f),
            new Color(0.96f, 0.93f, 0.86f),
            new Color(0.93f, 0.89f, 0.80f),
            new Color(1.04f, 1.00f, 0.93f),
            new Color(0.90f, 0.88f, 0.82f),
            new Color(0.99f, 0.92f, 0.82f)
        };

        public static CityMaterials CreateOrUpdate()
        {
            EnsureFolder("Assets/NazarethPlusCity");
            EnsureFolder(TextureFolder);
            EnsureFolder(MaterialFolder);

            var stoneAlbedo = Albedo("StoneAlbedo_v1", 512, PaintStone);
            var stoneNormal = Normal("StoneNormal_v1", 512, PaintStoneHeight);
            var roofAlbedo = Albedo("RoofAlbedo_v1", 512, PaintRoof);
            var roofNormal = Normal("RoofNormal_v1", 512, PaintRoofHeight);
            var asphaltAlbedo = Albedo("AsphaltAlbedo_v1", 512, PaintAsphalt);
            var asphaltNormal = Normal("AsphaltNormal_v1", 512, PaintAsphaltHeight);
            var groundAlbedo = Albedo("GroundAlbedo_v1", 512, PaintGround);
            var groundNormal = Normal("GroundNormal_v1", 512, PaintGroundHeight);

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader");
            if (shader == null)
                throw new FileNotFoundException("URP Lit shader was not found. The project should use the Universal Render Pipeline.");

            var materials = new CityMaterials { Stone = new Material[StoneTints.Length] };
            for (int i = 0; i < StoneTints.Length; i++)
                materials.Stone[i] = Lit("M_Limestone_" + i, shader, stoneAlbedo, stoneNormal, StoneTints[i], 0.16f, 0.35f);
            materials.Roof = Lit("M_ConcreteRoof", shader, roofAlbedo, roofNormal, Color.white, 0.22f, 0.25f);
            materials.Asphalt = Lit("M_Asphalt", shader, asphaltAlbedo, asphaltNormal, Color.white, 0.07f, 0.45f);
            materials.Ground = Lit("M_Earth", shader, groundAlbedo, groundNormal, Color.white, 0.1f, 0.3f);
            materials.Foundation = Lit("M_Foundation", shader, stoneAlbedo, stoneNormal, new Color(0.72f, 0.70f, 0.66f), 0.12f, 0.45f);

            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var outline = new Material(unlit != null ? unlit : shader) { name = "M_BoundaryOutline" };
            outline.SetColor("_BaseColor", new Color(0.15f, 0.85f, 1f, 1f));
            materials.Outline = SaveMaterial(outline, MaterialFolder + "/M_BoundaryOutline.mat");

            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader == null)
                throw new FileNotFoundException("Built-in procedural skybox shader was not found.");
            var sky = new Material(skyShader) { name = "M_DaylightSky" };
            sky.SetColor("_SkyTint", new Color(0.40f, 0.62f, 0.96f));
            sky.SetColor("_GroundColor", new Color(0.24f, 0.30f, 0.38f));
            sky.SetFloat("_AtmosphereThickness", 0.7f);
            sky.SetFloat("_Exposure", 1.1f);
            sky.SetFloat("_SunSize", 0.04f);
            sky.SetFloat("_SunSizeConvergence", 5f);
            materials.Skybox = SaveMaterial(sky, MaterialFolder + "/M_DaylightSky.mat");
            return materials;
        }

        static Material Lit(string name, Shader shader, Texture albedo, Texture normal, Color color, float smoothness, float normalScale)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetTexture("_BaseMap", albedo);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", normalScale);
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_Surface", 0f);
            material.doubleSidedGI = false;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material SaveMaterial(Material material, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
                return material;
            }

            existing.shader = material.shader;
            existing.CopyPropertiesFromMaterial(material);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static Texture2D Albedo(string name, int size, System.Func<int, int, int, Color> paint)
        {
            string path = TextureFolder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, true, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = paint(x, y, size);
            texture.SetPixels(pixels);
            texture.Apply(true, false);
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        static Texture2D Normal(string name, int size, System.Func<int, int, int, float> height)
        {
            string path = TextureFolder + "/" + name + ".png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false, true);
            var pixels = new Color[size * size];
            float strength = 1.6f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float left = height(x - 1, y, size);
                    float right = height(x + 1, y, size);
                    float down = height(x, y - 1, size);
                    float up = height(x, y + 1, size);
                    var normal = new Vector3((left - right) * strength, (down - up) * strength, 1f).normalized;
                    pixels[y * size + x] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Color PaintStone(int x, int y, int size)
        {
            float n = Fbm(x * 0.035f, y * 0.035f);
            int row = y / 72;
            bool joint = ((x + (row & 1) * 36) % 110) < 2;
            bool course = (y % 72) < 2;
            float groove = (joint || course) ? 0.045f : 0f;
            return new Color(0.58f + n * 0.06f - groove, 0.50f + n * 0.05f - groove, 0.38f + n * 0.04f - groove);
        }

        static float PaintStoneHeight(int x, int y, int size)
        {
            int row = Mathf.Abs(y) / 72;
            bool joint = ((Mathf.Abs(x) + (row & 1) * 36) % 110) < 2;
            bool course = (Mathf.Abs(y) % 72) < 2;
            return (joint || course) ? 0.15f : 0.55f + Fbm(x * 0.05f, y * 0.05f) * 0.2f;
        }

        static Color PaintRoof(int x, int y, int size)
        {
            float n = Fbm(x * 0.08f, y * 0.08f);
            return new Color(0.34f + n * 0.03f, 0.34f + n * 0.03f, 0.33f + n * 0.025f);
        }

        static float PaintRoofHeight(int x, int y, int size)
        {
            return 0.5f + Fbm(x * 0.12f, y * 0.12f) * 0.15f;
        }

        static Color PaintAsphalt(int x, int y, int size)
        {
            float n = Fbm(x * 0.15f, y * 0.15f);
            float grain = Hash(x, y);
            return new Color(0.045f + n * 0.02f + grain * 0.01f, 0.045f + n * 0.02f, 0.048f + n * 0.018f);
        }

        static float PaintAsphaltHeight(int x, int y, int size)
        {
            return 0.45f + Hash(x * 3, y * 5) * 0.25f;
        }

        static Color PaintGround(int x, int y, int size)
        {
            float n = Fbm(x * 0.02f, y * 0.02f);
            return new Color(0.24f + n * 0.05f, 0.18f + n * 0.04f, 0.11f + n * 0.03f);
        }

        static float PaintGroundHeight(int x, int y, int size)
        {
            return 0.4f + Fbm(x * 0.03f, y * 0.03f) * 0.35f;
        }

        static float Fbm(float x, float y)
        {
            float value = 0f;
            float amplitude = 0.5f;
            float frequency = 1f;
            for (int i = 0; i < 4; i++)
            {
                value += amplitude * Noise(x * frequency, y * frequency);
                amplitude *= 0.5f;
                frequency *= 2f;
            }

            return value;
        }

        static float Noise(float x, float y)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float tx = x - x0;
            float ty = y - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Hash(x0, y0);
            float b = Hash(x0 + 1, y0);
            float c = Hash(x0, y0 + 1);
            float d = Hash(x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, ty), ty);
        }

        static float Hash(int x, int y)
        {
            int n = x * 374761393 + y * 668265263;
            n = (n ^ (n >> 13)) * 1274126177;
            return ((n ^ (n >> 16)) & 0x7fffffff) / (float)int.MaxValue;
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
