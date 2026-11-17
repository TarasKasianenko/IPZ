using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IPZ.Style
{
    public static class NeonStyle
    {
        const string ProfilePath = "Assets/Settings/NeonVolume.asset";
        const string LogoPath = "Assets/Branding/ZutLogo.png";
        const string LogoMaterialPath = "Assets/Branding/ZutLogo.mat";

        public static void SetUpCamera(Camera camera)
        {
            camera.allowHDR = true;
            camera.backgroundColor = ZutColors.Night;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        }

        public static void AddBloom()
        {
            var volume = new GameObject("Neon Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = GetProfile();
        }

        static VolumeProfile GetProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            if (!profile.TryGet<Bloom>(out var bloom))
            {
                bloom = profile.Add<Bloom>(true);
                bloom.name = "Bloom";
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }
            bloom.active = true;
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(2.8f);
            bloom.scatter.Override(0.78f);
            bloom.highQualityFiltering.Override(true);
            EditorUtility.SetDirty(bloom);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        public static Material Glow(string folder, string name, Color color, float intensity)
        {
            var path = $"{folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find("Universal Render Pipeline/Unlit");
            material.SetColor("_BaseColor", ZutColors.Glow(color, intensity));
            EditorUtility.SetDirty(material);
            return material;
        }

        public static Material GlowLit(string folder, string name, Color baseColor, Color glowColor, float intensity)
        {
            const string shader = "Universal Render Pipeline/Lit";
            var path = $"{folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find(shader);
            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Smoothness", 0.45f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", intensity > 0f ? ZutColors.Glow(glowColor, intensity) : Color.black);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(material);
            return material;
        }

        public static TextMesh Text(Transform parent, string name, string text, float size, Vector3 position, Color color,
            FontStyle style = FontStyle.Normal)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = font;
            mesh.fontSize = 96;
            mesh.characterSize = 0.05f * size;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontStyle = style;
            mesh.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return mesh;
        }

        public static GameObject GlowQuad(string name, Transform parent, Vector3 position, Vector2 size, Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = position;
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            quad.GetComponent<Renderer>().sharedMaterial = material;
            quad.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return quad;
        }

        public static void GlowFrame(string name, Transform parent, Vector3 centre, Vector2 size, float thickness, Material material)
        {
            var frame = new GameObject(name).transform;
            frame.SetParent(parent, false);
            frame.localPosition = centre;
            float hx = size.x * 0.5f, hy = size.y * 0.5f;
            GlowQuad("Top", frame, new Vector3(0f, hy, 0f), new Vector2(size.x + thickness, thickness), material);
            GlowQuad("Bottom", frame, new Vector3(0f, -hy, 0f), new Vector2(size.x + thickness, thickness), material);
            GlowQuad("Left", frame, new Vector3(-hx, 0f, 0f), new Vector2(thickness, size.y + thickness), material);
            GlowQuad("Right", frame, new Vector3(hx, 0f, 0f), new Vector2(thickness, size.y + thickness), material);
        }

        public static Material NeonText(string folder, Font font, Color color, float intensity)
        {
            var path = $"{folder}/NeonText.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(font.material);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = font.material.shader;
            material.mainTexture = font.material.mainTexture;
            material.SetColor("_Color", ZutColors.Glow(color, intensity));
            EditorUtility.SetDirty(material);
            return material;
        }

        public static Texture2D NoSignal()
        {
            const string path = "Assets/Branding/NoSignal.png";
            if (!File.Exists(path))
            {
                var texture = new Texture2D(4, 4, TextureFormat.RGB24, false);
                var pixels = new Color[16];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = ZutColors.Night;
                texture.SetPixels(pixels);
                File.WriteAllBytes(path, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static Material Screen(string folder)
        {
            var path = $"{folder}/CameraScreen.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find("Universal Render Pipeline/Unlit");
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", NoSignal());
            EditorUtility.SetDirty(material);
            return material;
        }

        public static GameObject Logo(Transform parent, Vector3 position, float height)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(LogoPath);
            if (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(LogoMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, LogoMaterialPath);
            }
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", ZutColors.Glow(Color.white, 2.5f));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);

            float aspect = (float)texture.width / texture.height;
            return GlowQuad("ZUT Logo", parent, position, new Vector2(height * aspect, height), material);
        }
    }
}
