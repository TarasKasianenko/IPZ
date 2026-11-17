using System.IO;
using System.Linq;
using IPZ.AirHockey;
using IPZ.Style;
using IPZ.Tracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace IPZ.Maze
{
    public static class MazeSceneBuilder
    {
        const string ScenePath = "Assets/Maze/Maze.unity";
        const string ArtFolder = "Assets/Maze/Art";
        const float HudZ = -3f;

        [MenuItem("IPZ/Rebuild Maze Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            if (!AssetDatabase.IsValidFolder(ArtFolder))
                AssetDatabase.CreateFolder("Assets/Maze", "Art");

            var floorMaterial = FloorMaterial();
            var wallMaterial = NeonStyle.GlowLit(ArtFolder, "Wall", ZutColors.Navy, ZutColors.Blue, 0.75f);
            var ballMaterial = NeonStyle.GlowLit(ArtFolder, "Ball", Color.white, Color.white, 0.6f);
            var holeMaterial = NeonStyle.Glow(ArtFolder, "Hole", Color.black, 0f);
            holeMaterial.SetColor("_BaseColor", Color.black);
            var holeRimMaterial = NeonStyle.Glow(ArtFolder, "HoleRim", ZutColors.Red, 2f);
            var finishMaterial = NeonStyle.Glow(ArtFolder, "Finish", ZutColors.Green, 2.2f);
            var startMaterial = NeonStyle.Glow(ArtFolder, "Start", ZutColors.Blue, 1.4f);
            var greenGlow = NeonStyle.Glow(ArtFolder, "Glow1", ZutColors.Player(0), 2f);
            var blueGlow = NeonStyle.Glow(ArtFolder, "Glow2", ZutColors.Player(1), 2f);
            var trackMaterial = NeonStyle.Glow(ArtFolder, "Track", ZutColors.Deep, 1f);
            trackMaterial.SetColor("_BaseColor", Color.Lerp(ZutColors.Deep, ZutColors.Blue, 0.25f));
            var screenMaterial = NeonStyle.Screen(ArtFolder);
            var panelMaterial = Transparent("MessagePanel", new Color(ZutColors.Night.r, ZutColors.Night.g, ZutColors.Night.b, 0.86f));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.6f);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(0f, -0.3f, -19f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 50f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            NeonStyle.SetUpCamera(camera);
            cameraObject.AddComponent<AudioListener>();

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(35f, 30f, 0f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;
            NeonStyle.AddBloom();

            var board = new GameObject("Board").transform;
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.transform.SetParent(board, false);
            ball.GetComponent<Renderer>().sharedMaterial = ballMaterial;

            var hud = new GameObject("HUD").transform;
            NeonStyle.Logo(hud, new Vector3(-11.9f, 6.45f, HudZ), 1.2f);
            var title = NeonStyle.Text(hud, "Title", "Labirynt", 1.5f, new Vector3(-10.9f, 6.45f, HudZ), Color.white, FontStyle.Bold);
            title.anchor = TextAnchor.MiddleLeft;
            var levelText = NeonStyle.Text(hud, "Level", "", 1f, new Vector3(-3.6f, 6.45f, HudZ), Color.white, FontStyle.Bold);
            var timerText = NeonStyle.Text(hud, "Timer", "", 1f, new Vector3(0f, 6.45f, HudZ), ZutColors.Muted);
            var fallsText = NeonStyle.Text(hud, "Falls", "", 1f, new Vector3(3.6f, 6.45f, HudZ), ZutColors.Light(ZutColors.Red));
            var hintText = NeonStyle.Text(hud, "LostHandHint", "", 0.9f, new Vector3(0f, -6.75f, HudZ), ZutColors.Light(ZutColors.Red));

            var yIndicator = Indicator(hud, "Tilt Y", new Vector3(-9f, -0.3f, HudZ), true, trackMaterial, greenGlow, out var yMarker);
            var yLabel = NeonStyle.Text(hud, "Y Label", "", 0.75f, new Vector3(-9f, 2.85f, HudZ), ZutColors.Light(ZutColors.Green), FontStyle.Bold);
            var xIndicator = Indicator(hud, "Tilt X", new Vector3(0f, -5.55f, HudZ), false, trackMaterial, blueGlow, out var xMarker);
            var xLabel = NeonStyle.Text(hud, "X Label", "", 0.75f, new Vector3(2.9f, -5.55f, HudZ), ZutColors.Light(ZutColors.Blue), FontStyle.Bold);
            xLabel.anchor = TextAnchor.MiddleLeft;
            NeonStyle.Text(yIndicator, "Axis", "Y", 0.9f, new Vector3(0f, -2.85f, 0f), ZutColors.Light(ZutColors.Green), FontStyle.Bold);
            NeonStyle.Text(xIndicator, "Axis", "X", 0.9f, new Vector3(-2.75f, 0f, 0f), ZutColors.Light(ZutColors.Blue), FontStyle.Bold);

            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "MessagePanel";
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.transform.SetParent(hud, false);
            panel.transform.localPosition = new Vector3(0f, 0f, HudZ - 1f);
            panel.transform.localScale = new Vector3(13f, 4.2f, 1f);
            panel.GetComponent<Renderer>().sharedMaterial = panelMaterial;
            foreach (float edge in new[] { 0.5f, -0.5f })
            {
                NeonStyle.GlowQuad("Line Green", panel.transform, new Vector3(-0.25f, edge, -0.01f), new Vector2(0.5f, 0.06f / 4.2f), greenGlow);
                NeonStyle.GlowQuad("Line Blue", panel.transform, new Vector3(0.25f, edge, -0.01f), new Vector2(0.5f, 0.06f / 4.2f), blueGlow);
            }
            var message = NeonStyle.Text(hud, "Message", "", 1.9f, new Vector3(0f, 0.75f, HudZ - 1.1f), Color.white, FontStyle.Bold);
            message.GetComponent<MeshRenderer>().sharedMaterial = NeonStyle.NeonText(ArtFolder, message.font, Color.white, 1.5f);
            var detail = NeonStyle.Text(hud, "Detail", "", 0.85f, new Vector3(0f, -0.9f, HudZ - 1.1f), ZutColors.Muted);
            message.transform.SetParent(panel.transform, true);
            detail.transform.SetParent(panel.transform, true);

            var thumbnailPosition = new Vector3(11.3f, 6.1f, HudZ);
            NeonStyle.GlowFrame("Camera Glow", hud, thumbnailPosition + new Vector3(0f, 0f, -0.02f), new Vector2(2.6f, 1.95f), 0.06f, blueGlow);
            var thumbnail = NeonStyle.GlowQuad("Camera View", hud, thumbnailPosition, new Vector2(2.5f, 1.875f), screenMaterial);

            var root = new GameObject("Maze");
            root.AddComponent<HandTrackingReceiver>();
            root.AddComponent<AudioSource>();
            root.AddComponent<GameAudio>();
            var game = root.AddComponent<MazeGame>();
            var cameraView = root.AddComponent<CameraThumbnail>();

            var viewSo = new SerializedObject(cameraView);
            viewSo.FindProperty("screen").objectReferenceValue = thumbnail.GetComponent<Renderer>();
            viewSo.ApplyModifiedPropertiesWithoutUndo();

            var so = new SerializedObject(game);
            so.FindProperty("board").objectReferenceValue = board;
            so.FindProperty("ball").objectReferenceValue = ball.transform;
            so.FindProperty("levelText").objectReferenceValue = levelText;
            so.FindProperty("timerText").objectReferenceValue = timerText;
            so.FindProperty("fallsText").objectReferenceValue = fallsText;
            so.FindProperty("messageText").objectReferenceValue = message;
            so.FindProperty("detailText").objectReferenceValue = detail;
            so.FindProperty("hintText").objectReferenceValue = hintText;
            so.FindProperty("yLabel").objectReferenceValue = yLabel;
            so.FindProperty("xLabel").objectReferenceValue = xLabel;
            so.FindProperty("messagePanel").objectReferenceValue = panel;
            so.FindProperty("yMarker").objectReferenceValue = yMarker;
            so.FindProperty("xMarker").objectReferenceValue = xMarker;
            so.FindProperty("floorMaterial").objectReferenceValue = floorMaterial;
            so.FindProperty("wallMaterial").objectReferenceValue = wallMaterial;
            so.FindProperty("holeMaterial").objectReferenceValue = holeMaterial;
            so.FindProperty("holeRimMaterial").objectReferenceValue = holeRimMaterial;
            so.FindProperty("finishMaterial").objectReferenceValue = finishMaterial;
            so.FindProperty("startMaterial").objectReferenceValue = startMaterial;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.All(s => s.path != ScenePath))
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
        }

        static Transform Indicator(Transform parent, string name, Vector3 position, bool vertical, Material track, Material glow,
            out Transform marker)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = position;
            var length = 4.6f;
            NeonStyle.GlowQuad("Track", root, Vector3.zero, vertical ? new Vector2(0.14f, length) : new Vector2(length, 0.14f), track);
            NeonStyle.GlowQuad("Centre", root, new Vector3(0f, 0f, -0.01f), vertical ? new Vector2(0.4f, 0.04f) : new Vector2(0.04f, 0.4f), track);
            marker = NeonStyle.GlowQuad("Marker", root, new Vector3(0f, 0f, -0.02f), new Vector2(0.42f, 0.42f), glow).transform;
            return root;
        }

        static Material FloorMaterial()
        {
            const int cols = 17, rows = 11, cellPixels = 64;
            int width = cols * cellPixels, height = rows * cellPixels;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            var grid = Color.Lerp(ZutColors.Deep, ZutColors.Blue, 0.28f);
            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    float u = (px + 0.5f) / width - 0.5f, v = (py + 0.5f) / height - 0.5f;
                    float vignette = Mathf.Clamp01(new Vector2(u * 1.6f, v * 1.6f).magnitude);
                    var colour = Color.Lerp(ZutColors.Deep, ZutColors.Night, vignette);
                    int gx = px % cellPixels, gy = py % cellPixels;
                    float line = Mathf.Min(Mathf.Min(gx, cellPixels - 1 - gx), Mathf.Min(gy, cellPixels - 1 - gy));
                    colour = Color.Lerp(grid, colour, Mathf.Clamp01(line / 1.5f));
                    colour.a = 1f;
                    pixels[py * width + px] = colour;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();

            var path = $"{ArtFolder}/Floor.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            var material = NeonStyle.Glow(ArtFolder, "Floor", Color.white, 1f);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            return material;
        }

        static Material Transparent(string name, Color color)
        {
            var material = NeonStyle.Glow(ArtFolder, name, Color.white, 1f);
            material.SetColor("_BaseColor", color);
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
    }
}
