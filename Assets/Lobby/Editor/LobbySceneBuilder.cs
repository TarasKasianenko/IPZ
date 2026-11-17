using System.Linq;
using IPZ.AirHockey;
using IPZ.Style;
using IPZ.Tracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace IPZ.Lobby
{
    /// Generates Assets/Lobby/Lobby.unity. Rebuilding overwrites manual edits made to that scene.
    public static class LobbySceneBuilder
    {
        const string ScenePath = "Assets/Lobby/Lobby.unity";
        const string MaterialFolder = "Assets/Lobby/Materials";

        static readonly (string text, MenuAction action, bool primary)[] Items =
        {
            ("Gra 1: Air Hockey", MenuAction.Play, true),
            ("Gra 2: Labirynt", MenuAction.PlayMaze, true),
            ("Gracze: 2", MenuAction.Players, false),
            ("Gracz 1: kamera —", MenuAction.CameraP1Next, false),
            ("Gracz 2: kamera —", MenuAction.CameraP2Next, false),
            ("Jak grać", MenuAction.HowToPlay, false),
            ("Test dłoni", MenuAction.HandCheck, false),
            ("Kamery", MenuAction.Cameras, false),
            ("Dźwięk: włączony", MenuAction.Sound, false),
            ("Pełny ekran", MenuAction.Fullscreen, false),
            ("O projekcie", MenuAction.About, false),
            ("Wyjście", MenuAction.Quit, false),
        };

        static Font font;
        static Material buttonMaterial, panelMaterial, floorMaterial, cursorMaterial, screenMaterial, frameMaterial;
        static Material rimMaterial, greenGlow, blueGlow;

        [MenuItem("IPZ/Rebuild Lobby Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            buttonMaterial = GetMaterial("Button", ZutColors.Deep, 0.25f);
            panelMaterial = GetMaterial("Panel", ZutColors.Deep, 0.2f);
            floorMaterial = GetMaterial("Floor", ZutColors.Night, 0.2f);
            cursorMaterial = NeonStyle.Glow(MaterialFolder, "Cursor", Color.white, 1f);
            rimMaterial = NeonStyle.Glow(MaterialFolder, "ButtonRim", ZutColors.Blue, 1.5f);
            greenGlow = NeonStyle.Glow(MaterialFolder, "GlowGreen", ZutColors.Green, 2f);
            blueGlow = NeonStyle.Glow(MaterialFolder, "GlowBlue", ZutColors.Blue, 2f);
            screenMaterial = NeonStyle.Screen(MaterialFolder);
            frameMaterial = GetMaterial("CameraFrame", ZutColors.Night, 0.1f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.46f, 0.56f);

            var camera = BuildCamera();
            BuildEnvironment();
            NeonStyle.AddBloom();

            var menu = new GameObject("Lobby");
            menu.AddComponent<HandTrackingReceiver>();
            menu.AddComponent<HandPointer>();
            var cameraSettings = menu.AddComponent<CameraSettingsPanel>();
            var lobby = menu.AddComponent<LobbyMenu>();

            var title = MakeText(menu.transform, "Title", "Gesture Games", 2.6f, new Vector3(0.9f, 4.55f, 0f), Color.white, FontStyle.Bold);
            title.GetComponent<MeshRenderer>().sharedMaterial = NeonStyle.NeonText(MaterialFolder, font, Color.white, 1.5f);
            NeonStyle.Logo(menu.transform, new Vector3(-6.1f, 4.55f, 0f), 1.45f);
            NeonStyle.GlowQuad("Underline Green", menu.transform, new Vector3(-1.8f, 3.98f, 0f), new Vector2(5.35f, 0.07f), greenGlow);
            NeonStyle.GlowQuad("Underline Blue", menu.transform, new Vector3(3.6f, 3.98f, 0f), new Vector2(5.35f, 0.07f), blueGlow);
            var status = MakeText(menu.transform, "Status", "", 1f, new Vector3(0f, 3.5f, 0f), Color.white);
            var hint = "Złącz palce, aby nacisnąć. Trzymaj złączone i przesuwaj, aby przewijać.";
            MakeText(menu.transform, "Hint", hint, 0.8f, new Vector3(0f, -5.25f, 0f), ZutColors.Muted);
            var toast = MakeText(menu.transform, "Toast", "", 0.8f, new Vector3(0f, 2.4f, -1.5f), ZutColors.Red);

            var wheel = new GameObject("Wheel").transform;
            wheel.SetParent(menu.transform);
            wheel.localPosition = new Vector3(0f, -0.6f, 0f);
            var buttons = Items.Select(i => MakeButton(wheel, i.text, i.action, i.primary, new Vector3(5.8f, 1.05f, 0.5f), 1.02f)).ToArray();

            var panel = BuildPanel(menu.transform, out var infoTitle, out var infoBody, out var back);
            BuildCameraPanel(menu.transform, cameraSettings);

            var cursor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cursor.name = "HandCursor";
            cursor.transform.SetParent(menu.transform);
            cursor.transform.localScale = Vector3.one * 0.28f;
            Object.DestroyImmediate(cursor.GetComponent<Collider>());
            cursor.GetComponent<Renderer>().sharedMaterial = cursorMaterial;
            cursor.SetActive(false);

            var so = new SerializedObject(lobby);
            so.FindProperty("menuCamera").objectReferenceValue = camera;
            so.FindProperty("wheel").objectReferenceValue = wheel;
            so.FindProperty("backButton").objectReferenceValue = back;
            so.FindProperty("infoPanel").objectReferenceValue = panel;
            so.FindProperty("infoTitle").objectReferenceValue = infoTitle;
            so.FindProperty("infoBody").objectReferenceValue = infoBody;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("toastText").objectReferenceValue = toast;
            so.FindProperty("cursor").objectReferenceValue = cursor.GetComponent<Renderer>();
            var list = so.FindProperty("items");
            list.arraySize = buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            AssetDatabase.SaveAssets();
        }

        static Camera BuildCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.position = new Vector3(0f, 0f, -10f);
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.fieldOfView = 60f;
            NeonStyle.SetUpCamera(camera);
            go.AddComponent<AudioListener>();
            return camera;
        }

        static void BuildEnvironment()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.None;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0f, -5.4f, 10f);
            floor.transform.localScale = new Vector3(6f, 1f, 6f);
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

            for (int i = -3; i <= 3; i++)
            {
                var line = NeonStyle.GlowQuad($"Floor Line {i}", null, new Vector3(i * 4f, -5.38f, 14f), new Vector2(0.05f, 40f),
                    i < 0 ? greenGlow : i > 0 ? blueGlow : rimMaterial);
                line.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        static GameObject BuildPanel(Transform parent, out TextMesh title, out TextMesh body, out MenuButton3D back)
        {
            var panel = new GameObject("InfoPanel");
            panel.transform.SetParent(parent);
            panel.transform.localPosition = new Vector3(0f, -0.85f, -0.5f);

            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Board";
            board.transform.SetParent(panel.transform, false);
            board.transform.localScale = new Vector3(15f, 7.6f, 0.3f);
            Object.DestroyImmediate(board.GetComponent<Collider>());
            board.GetComponent<Renderer>().sharedMaterial = panelMaterial;
            NeonStyle.GlowFrame("Glow", panel.transform, new Vector3(0f, 0f, -0.16f), new Vector2(15f, 7.6f), 0.06f, blueGlow);

            title = MakeText(panel.transform, "Title", "", 1.4f, new Vector3(0f, 3.1f, -0.2f), Color.white, FontStyle.Bold);
            NeonStyle.GlowQuad("Title Line", panel.transform, new Vector3(0f, 2.58f, -0.17f), new Vector2(5f, 0.05f), greenGlow);
            body = MakeText(panel.transform, "Body", "", 0.76f, new Vector3(0f, 0.05f, -0.2f), Color.white);

            back = MakeButton(panel.transform, "Wstecz", MenuAction.Back, true, new Vector3(4f, 0.9f, 0.4f));
            back.transform.localPosition = new Vector3(0f, -3.05f, -0.35f);
            return panel;
        }

        static void BuildCameraPanel(Transform parent, CameraSettingsPanel settings)
        {
            var panel = new GameObject("CameraPanel");
            panel.transform.SetParent(parent);
            panel.transform.localPosition = new Vector3(0f, -0.8f, -0.5f);

            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Board";
            board.transform.SetParent(panel.transform, false);
            board.transform.localScale = new Vector3(17f, 7.6f, 0.3f);
            NeonStyle.GlowFrame("Glow", panel.transform, new Vector3(0f, 0f, -0.16f), new Vector2(17f, 7.6f), 0.06f, blueGlow);
            Object.DestroyImmediate(board.GetComponent<Collider>());
            board.GetComponent<Renderer>().sharedMaterial = panelMaterial;

            MakeText(panel.transform, "Title", "Kamery", 1.3f, new Vector3(-6.2f, 3.3f, -0.2f), Color.white, FontStyle.Bold);
            var back = MakeButton(panel.transform, "Wstecz", MenuAction.Back, true, new Vector3(2.8f, 0.8f, 0.4f), 0.9f);
            back.transform.localPosition = new Vector3(6.6f, 3.3f, -0.35f);

            var p1 = BuildCameraColumn(panel.transform, -4.2f, "Gracz 1", ZutColors.Player(0), 5054,
                MenuAction.CameraP1Prev, MenuAction.CameraP1Next, out var p1Screen, out var p1Label, out _);
            var p2 = BuildCameraColumn(panel.transform, 4.2f, "Gracz 2", ZutColors.Player(1), 5055,
                MenuAction.CameraP2Prev, MenuAction.CameraP2Next, out var p2Screen, out var p2Label, out var p2Placeholder);

            var scan = MakeButton(panel.transform, "Szukaj kamer", MenuAction.CameraScan, false, new Vector3(3.6f, 0.75f, 0.4f), 0.85f);
            scan.transform.localPosition = new Vector3(0f, -2.85f, -0.35f);

            var status = MakeText(panel.transform, "Status", "", 0.8f, new Vector3(0f, 3.3f, -0.2f), Color.white);

            var so = new SerializedObject(settings);
            so.FindProperty("panel").objectReferenceValue = panel;
            so.FindProperty("p1Label").objectReferenceValue = p1Label;
            so.FindProperty("p2Label").objectReferenceValue = p2Label;
            so.FindProperty("p2Placeholder").objectReferenceValue = p2Placeholder;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("p1Screen").objectReferenceValue = p1Screen;
            so.FindProperty("p2Screen").objectReferenceValue = p2Screen;
            var buttons = p1.Concat(p2).Append(scan).Append(back).ToArray();
            var list = so.FindProperty("buttons");
            list.arraySize = buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            panel.SetActive(false);
        }

        static MenuButton3D[] BuildCameraColumn(Transform parent, float x, string title, Color color, int previewPort,
            MenuAction prev, MenuAction next, out GameObject screen, out TextMesh label, out TextMesh placeholder)
        {
            var column = new GameObject(title).transform;
            column.SetParent(parent, false);
            column.localPosition = new Vector3(x, 0f, 0f);

            MakeText(column, "Title", title, 1.1f, new Vector3(0f, 2.45f, -0.2f), ZutColors.Light(color), FontStyle.Bold);

            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            Object.DestroyImmediate(frame.GetComponent<Collider>());
            frame.transform.SetParent(column, false);
            frame.transform.localPosition = new Vector3(0f, 0.35f, -0.2f);
            frame.transform.localScale = new Vector3(4.8f, 3.6f, 0.1f);
            frame.GetComponent<Renderer>().sharedMaterial = frameMaterial;
            NeonStyle.GlowFrame("Glow", column, new Vector3(0f, 0.35f, -0.32f), new Vector2(4.5f, 3.4f), 0.07f,
                color == ZutColors.Green ? greenGlow : blueGlow);

            screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Preview";
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            screen.transform.SetParent(column, false);
            screen.transform.localPosition = new Vector3(0f, 0.35f, -0.3f);
            screen.transform.localScale = new Vector3(4.4f, 3.3f, 1f);
            var renderer = screen.GetComponent<Renderer>();
            renderer.sharedMaterial = screenMaterial;
            var thumbnail = screen.AddComponent<CameraThumbnail>();
            var thumbnailSo = new SerializedObject(thumbnail);
            thumbnailSo.FindProperty("port").intValue = previewPort;
            thumbnailSo.FindProperty("screen").objectReferenceValue = renderer;
            thumbnailSo.ApplyModifiedPropertiesWithoutUndo();

            placeholder = MakeText(column, "Placeholder", "", 0.8f, new Vector3(0f, 0.35f, -0.35f), ZutColors.Muted);

            var left = MakeButton(column, "<", prev, false, new Vector3(1f, 0.75f, 0.4f), 1f);
            left.transform.localPosition = new Vector3(-1.9f, -1.8f, -0.35f);
            var right = MakeButton(column, ">", next, false, new Vector3(1f, 0.75f, 0.4f), 1f);
            right.transform.localPosition = new Vector3(1.9f, -1.8f, -0.35f);
            label = MakeText(column, "Selected", "", 0.9f, new Vector3(0f, -1.8f, -0.3f), Color.white);
            return new[] { left, right };
        }

        static MenuButton3D MakeButton(Transform parent, string text, MenuAction action, bool primary, Vector3 size,
            float labelSize = 1.25f)
        {
            var root = new GameObject(text);
            root.transform.SetParent(parent, false);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(visual, false);
            body.transform.localScale = size;
            var renderer = body.GetComponent<Renderer>();
            renderer.sharedMaterial = buttonMaterial;

            var label = MakeText(visual, "Label", text, labelSize, new Vector3(0f, 0f, -size.z * 0.5f - 0.03f), Color.white,
                primary ? FontStyle.Bold : FontStyle.Normal);
            NeonStyle.GlowFrame("Glow", visual, new Vector3(0f, 0f, -size.z * 0.5f - 0.01f), new Vector2(size.x, size.y), 0.06f, rimMaterial);
            var glow = visual.Find("Glow").GetComponentsInChildren<Renderer>();

            var button = root.AddComponent<MenuButton3D>();
            var so = new SerializedObject(button);
            so.FindProperty("action").enumValueIndex = (int)action;
            so.FindProperty("visual").objectReferenceValue = visual;
            so.FindProperty("body").objectReferenceValue = renderer;
            so.FindProperty("label").objectReferenceValue = label;
            var glowList = so.FindProperty("glow");
            glowList.arraySize = glow.Length;
            for (int i = 0; i < glow.Length; i++)
                glowList.GetArrayElementAtIndex(i).objectReferenceValue = glow[i];
            if (primary)
            {
                so.FindProperty("color").colorValue = ZutColors.Green;
                so.FindProperty("hoverColor").colorValue = Color.Lerp(ZutColors.Green, Color.white, 0.18f);
                so.FindProperty("pressedColor").colorValue = ZutColors.Navy;
                so.FindProperty("glowColor").colorValue = ZutColors.Green;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return button;
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

        static Material GetMaterial(string name, Color color, float smoothness, string shader = "Universal Render Pipeline/Lit")
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/Lobby", "Materials");

            var path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
