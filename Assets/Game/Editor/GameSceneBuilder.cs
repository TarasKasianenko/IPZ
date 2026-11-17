using System.IO;
using System.Linq;
using IPZ.Tracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace IPZ.AirHockey
{
    /// Generates Assets/Game/Game.unity with its textures and materials.
    /// Rebuilding overwrites manual edits made to that scene.
    public static class GameSceneBuilder
    {
        const string ScenePath = "Assets/Game/Game.unity";
        const string ArtFolder = "Assets/Game/Art";
        const float CentreY = -0.9f;
        const float FrameThickness = 0.55f;

        static readonly Color Surface = new Color(0.94f, 0.94f, 0.91f);
        static readonly Color Marking = new Color(0.80f, 0.13f, 0.13f);

        static Font font;

        [MenuItem("IPZ/Rebuild Game Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            if (!AssetDatabase.IsValidFolder(ArtFolder))
                AssetDatabase.CreateFolder("Assets/Game", "Art");

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var tableTexture = SaveTexture("Table", PaintTable(2048, 1024));
            var woodTexture = SaveTexture("Wood", PaintWood(512, 128));

            var surfaceMaterial = GetMaterial("TableSurface", "Universal Render Pipeline/Unlit", Color.white, tableTexture);
            var woodMaterial = GetMaterial("Wood", "Universal Render Pipeline/Lit", Color.white, woodTexture);
            var slotMaterial = GetMaterial("GoalSlot", "Universal Render Pipeline/Unlit", new Color(0.06f, 0.05f, 0.05f), null);
            var puckMaterial = GetMaterial("Puck", "Universal Render Pipeline/Lit", new Color(0.78f, 0.08f, 0.08f), null);
            var malletMaterial = GetMaterial("Mallet", "Universal Render Pipeline/Lit", new Color(0.85f, 0.10f, 0.10f), null);
            var knobMaterial = GetMaterial("MalletKnob", "Universal Render Pipeline/Lit", new Color(1f, 0.32f, 0.28f), null);
            var screenMaterial = GetMaterial("CameraScreen", "Universal Render Pipeline/Unlit", Color.white, null);
            var panelMaterial = GetTransparent("MessagePanel", "Universal Render Pipeline/Unlit", new Color(0.05f, 0.06f, 0.09f, 0.82f));
            var flashMaterial = GetTransparent("Flash", "Universal Render Pipeline/Unlit", Color.white);
            var trailMaterial = GetTransparent("PuckTrail", "Universal Render Pipeline/Particles/Unlit", Color.white);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);

            var camera = BuildCamera();
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(25f, -20f, 0f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;

            BuildTable(surfaceMaterial, woodMaterial, slotMaterial);

            var puck = MakeDisc("Puck", null, AirHockeyPhysics.PuckRadius, -0.2f, puckMaterial);
            var trail = puck.gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.28f;
            trail.widthMultiplier = AirHockeyPhysics.PuckRadius * 1.7f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.minVertexDistance = 0.04f;
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(new Color(0.9f, 0.12f, 0.12f), 0f), new GradientColorKey(new Color(0.9f, 0.12f, 0.12f), 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0.45f, 0f), new GradientAlphaKey(0f, 1f) },
            };
            trail.sharedMaterial = trailMaterial;
            trail.shadowCastingMode = ShadowCastingMode.Off;

            var malletA = MakeMallet("Mallet Left", malletMaterial, knobMaterial);
            var malletB = MakeMallet("Mallet Right", malletMaterial, knobMaterial);

            var score = MakeText(null, "Score", "0 : 0", 2.6f, new Vector3(0f, 4.9f, -4f), new Color(1f, 1f, 1f), FontStyle.Bold);
            var timer = MakeText(null, "Timer", "3:00", 1f, new Vector3(0f, 3.95f, -4f), new Color(0.75f, 0.78f, 0.84f));
            var names = MakeText(null, "Names", "", 1.1f, new Vector3(0f, 4.9f, -4f), new Color(0.75f, 0.78f, 0.84f));
            var hint = MakeText(null, "LostHandHint", "", 1f, new Vector3(0f, -5.68f, -4f), new Color(1f, 0.78f, 0.3f));

            var panel = MakeQuad("MessagePanel", null, new Vector3(0f, CentreY, -3f), new Vector2(15f, 4.4f), panelMaterial);
            var message = MakeText(panel.transform, "Message", "", 2.6f, new Vector3(0f, 0.9f, -1f), Color.white, FontStyle.Bold);
            var detail = MakeText(panel.transform, "Detail", "", 1.05f, new Vector3(0f, -0.9f, -1f), new Color(0.88f, 0.9f, 0.95f));
            FixScale(message.transform, panel.transform);
            FixScale(detail.transform, panel.transform);

            var flash = MakeQuad("GoalFlash", null, new Vector3(0f, 0f, -6f), new Vector2(60f, 40f), flashMaterial);
            flash.SetActive(false);

            var thumbnail = BuildThumbnail(screenMaterial, woodMaterial);

            var root = new GameObject("Game");
            root.AddComponent<HandTrackingReceiver>();
            root.AddComponent<AudioSource>();
            root.AddComponent<GameAudio>();
            var game = root.AddComponent<AirHockeyGame>();
            var cameraView = root.AddComponent<CameraThumbnail>();

            var viewSo = new SerializedObject(cameraView);
            viewSo.FindProperty("screen").objectReferenceValue = thumbnail;
            viewSo.ApplyModifiedPropertiesWithoutUndo();

            var so = new SerializedObject(game);
            so.FindProperty("gameCamera").objectReferenceValue = camera;
            so.FindProperty("puck").objectReferenceValue = puck;
            so.FindProperty("puckTrail").objectReferenceValue = trail;
            var malletList = so.FindProperty("mallets");
            malletList.arraySize = 2;
            malletList.GetArrayElementAtIndex(0).objectReferenceValue = malletA;
            malletList.GetArrayElementAtIndex(1).objectReferenceValue = malletB;
            so.FindProperty("scoreText").objectReferenceValue = score;
            so.FindProperty("timerText").objectReferenceValue = timer;
            so.FindProperty("namesText").objectReferenceValue = names;
            so.FindProperty("messageText").objectReferenceValue = message;
            so.FindProperty("detailText").objectReferenceValue = detail;
            so.FindProperty("hintText").objectReferenceValue = hint;
            so.FindProperty("messagePanel").objectReferenceValue = panel;
            so.FindProperty("flash").objectReferenceValue = flash.GetComponent<Renderer>();
            so.FindProperty("tableCentre").vector2Value = new Vector2(0f, CentreY);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            AssetDatabase.SaveAssets();
        }

        static Camera BuildCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, 0f, -20f);
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.11f, 0.14f);
            go.AddComponent<AudioListener>();
            return camera;
        }

        static void BuildTable(Material surface, Material wood, Material slot)
        {
            var table = new GameObject("Table").transform;
            table.position = new Vector3(0f, CentreY, 0f);

            float l = AirHockeyPhysics.HalfLength, w = AirHockeyPhysics.HalfWidth, g = AirHockeyPhysics.GoalHalfWidth, t = FrameThickness;
            MakeQuad("Surface", table, new Vector3(0f, 0f, 0.5f), new Vector2(l * 2f, w * 2f), surface);

            MakeBlock("Rail Top", table, new Vector3(0f, w + t * 0.5f, 0f), new Vector3((l + t) * 2f, t, 1f), wood);
            MakeBlock("Rail Bottom", table, new Vector3(0f, -w - t * 0.5f, 0f), new Vector3((l + t) * 2f, t, 1f), wood);

            float endHeight = w - g;
            float endY = g + endHeight * 0.5f;
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * (l + t * 0.5f);
                MakeBlock(side < 0 ? "End Left Upper" : "End Right Upper", table, new Vector3(x, endY, 0f), new Vector3(t, endHeight, 1f), wood);
                MakeBlock(side < 0 ? "End Left Lower" : "End Right Lower", table, new Vector3(x, -endY, 0f), new Vector3(t, endHeight, 1f), wood);
                MakeQuad(side < 0 ? "Goal Left" : "Goal Right", table, new Vector3(x, 0f, 0.4f), new Vector2(t, g * 2f), slot);
            }
        }

        static Renderer BuildThumbnail(Material screen, Material wood)
        {
            var position = new Vector3(9.15f, 4.78f, -1f);
            MakeBlock("Camera Frame", null, position + new Vector3(0f, 0f, 0.5f), new Vector3(2.75f, 2.1f, 0.5f), wood);
            return MakeQuad("Camera View", null, position, new Vector2(2.5f, 1.875f), screen).GetComponent<Renderer>();
        }

        static Transform MakeMallet(string name, Material body, Material knob)
        {
            var mallet = MakeDisc(name, null, AirHockeyPhysics.MalletRadius, -0.4f, body);
            var top = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            top.name = "Knob";
            Object.DestroyImmediate(top.GetComponent<Collider>());
            top.transform.SetParent(mallet, false);
            top.transform.localPosition = new Vector3(0f, 0f, -0.2f);
            top.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            top.transform.localScale = new Vector3(AirHockeyPhysics.MalletRadius * 0.95f, 0.1f, AirHockeyPhysics.MalletRadius * 0.95f);
            top.GetComponent<Renderer>().sharedMaterial = knob;
            return mallet;
        }

        /// A flat cylinder facing the camera, under an unrotated parent so game code can move it freely.
        static Transform MakeDisc(string name, Transform parent, float radius, float z, Material material)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(0f, 0f, z);

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Body";
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(root, false);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale = new Vector3(radius * 2f, 0.12f, radius * 2f);
            disc.GetComponent<Renderer>().sharedMaterial = material;
            return root;
        }

        static void MakeBlock(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            Object.DestroyImmediate(block.GetComponent<Collider>());
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = size;
            block.GetComponent<Renderer>().sharedMaterial = material;
        }

        static GameObject MakeQuad(string name, Transform parent, Vector3 position, Vector2 size, Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = position;
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            quad.GetComponent<Renderer>().sharedMaterial = material;
            return quad;
        }

        /// Undoes a parent's non-uniform scale, keeping the child's offset in world units.
        static void FixScale(Transform child, Transform parent)
        {
            var scale = parent.localScale;
            var position = child.localPosition;
            child.localPosition = new Vector3(position.x / scale.x, position.y / scale.y, position.z);
            child.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f);
        }

        static TextMesh MakeText(Transform parent, string name, string text, float size, Vector3 position, Color color,
            FontStyle style = FontStyle.Normal)
        {
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

        static Texture2D PaintTable(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            float l = AirHockeyPhysics.HalfLength, w = AirHockeyPhysics.HalfWidth;
            const float lineHalf = 0.05f;
            var pixels = new Color[width * height];

            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    float x = ((px + 0.5f) / width - 0.5f) * l * 2f;
                    float y = ((py + 0.5f) / height - 0.5f) * w * 2f;

                    float distance = Mathf.Abs(x);
                    distance = Mathf.Min(distance, Mathf.Abs(new Vector2(x, y).magnitude - 1.25f));
                    distance = Mathf.Min(distance, Mathf.Abs(new Vector2(x - l, y).magnitude - 2.3f));
                    distance = Mathf.Min(distance, Mathf.Abs(new Vector2(x + l, y).magnitude - 2.3f));

                    float pixel = l * 2f / width;
                    float ink = 1f - Mathf.Clamp01((distance - lineHalf) / pixel);
                    pixels[py * width + px] = Color.Lerp(Surface, Marking, ink);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static Texture2D PaintWood(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var dark = new Color(0.42f, 0.26f, 0.13f);
            var light = new Color(0.66f, 0.45f, 0.25f);
            var pixels = new Color[width * height];

            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    float u = (float)px / width, v = (float)py / height;
                    float grain = Mathf.PerlinNoise(u * 4f + 3f, v * 28f);
                    float rings = Mathf.Sin((v * 24f + grain * 9f) * Mathf.PI) * 0.5f + 0.5f;
                    pixels[py * width + px] = Color.Lerp(dark, light, rings * 0.6f + grain * 0.4f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static Texture2D SaveTexture(string name, Texture2D texture)
        {
            var path = $"{ArtFolder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material GetMaterial(string name, string shader, Color color, Texture texture)
        {
            var path = $"{ArtFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            if (texture != null)
                material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.35f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material GetTransparent(string name, string shader, Color color)
        {
            var material = GetMaterial(name, shader, color, null);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.All(s => s.path != ScenePath))
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
