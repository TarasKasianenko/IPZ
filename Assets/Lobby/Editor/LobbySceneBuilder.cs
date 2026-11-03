using System.Linq;
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
            ("Graj", MenuAction.Play, true),
            ("Gracze: 2", MenuAction.Players, false),
            ("Jak grać", MenuAction.HowToPlay, false),
            ("Test dłoni", MenuAction.HandCheck, false),
            ("Dźwięk: włączony", MenuAction.Sound, false),
            ("Pełny ekran", MenuAction.Fullscreen, false),
            ("O projekcie", MenuAction.About, false),
            ("Wyjście", MenuAction.Quit, false),
        };

        static Font font;
        static Material buttonMaterial, panelMaterial, floorMaterial, cursorMaterial;

        [MenuItem("IPZ/Rebuild Lobby Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            buttonMaterial = GetMaterial("Button", new Color(0.16f, 0.19f, 0.27f), 0.25f);
            panelMaterial = GetMaterial("Panel", new Color(0.11f, 0.13f, 0.18f), 0.2f);
            floorMaterial = GetMaterial("Floor", new Color(0.05f, 0.06f, 0.09f), 0.2f);
            cursorMaterial = GetMaterial("Cursor", Color.white, 0.5f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.5f);

            var camera = BuildCamera();
            BuildEnvironment();

            var menu = new GameObject("Lobby");
            menu.AddComponent<HandTrackingReceiver>();
            menu.AddComponent<HandPointer>();
            var lobby = menu.AddComponent<LobbyMenu>();

            MakeText(menu.transform, "Title", "IPZ", 3.2f, new Vector3(0f, 4.55f, 0f), Color.white, FontStyle.Bold);
            var status = MakeText(menu.transform, "Status", "", 1f, new Vector3(0f, 3.55f, 0f), Color.white);
            var hint = "Złącz palce, aby nacisnąć. Trzymaj złączone i przesuwaj, aby przewijać.";
            MakeText(menu.transform, "Hint", hint, 0.8f, new Vector3(0f, -4.9f, 0f), new Color(0.62f, 0.66f, 0.74f));
            var toast = MakeText(menu.transform, "Toast", "", 0.8f, new Vector3(0f, 2.4f, -1.5f), new Color(1f, 0.82f, 0.4f));

            var wheel = new GameObject("Wheel").transform;
            wheel.SetParent(menu.transform);
            wheel.localPosition = new Vector3(0f, -0.6f, 0f);
            var buttons = Items.Select(i => MakeButton(wheel, i.text, i.action, i.primary, new Vector3(5.8f, 1.05f, 0.5f))).ToArray();

            var panel = BuildPanel(menu.transform, out var infoTitle, out var infoBody, out var back);

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
            camera.backgroundColor = new Color(0.04f, 0.05f, 0.08f);
            camera.fieldOfView = 60f;
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
        }

        static GameObject BuildPanel(Transform parent, out TextMesh title, out TextMesh body, out MenuButton3D back)
        {
            var panel = new GameObject("InfoPanel");
            panel.transform.SetParent(parent);
            panel.transform.localPosition = new Vector3(0f, -0.8f, -0.5f);

            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "Board";
            board.transform.SetParent(panel.transform, false);
            board.transform.localScale = new Vector3(12.5f, 6.6f, 0.3f);
            Object.DestroyImmediate(board.GetComponent<Collider>());
            board.GetComponent<Renderer>().sharedMaterial = panelMaterial;

            title = MakeText(panel.transform, "Title", "", 1.5f, new Vector3(0f, 2.55f, -0.2f), Color.white, FontStyle.Bold);
            body = MakeText(panel.transform, "Body", "", 0.95f, new Vector3(0f, 0.3f, -0.2f), new Color(0.9f, 0.92f, 0.96f));

            back = MakeButton(panel.transform, "Wstecz", MenuAction.Back, true, new Vector3(4f, 0.95f, 0.4f));
            back.transform.localPosition = new Vector3(0f, -2.4f, -0.35f);
            return panel;
        }

        static MenuButton3D MakeButton(Transform parent, string text, MenuAction action, bool primary, Vector3 size)
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

            var label = MakeText(visual, "Label", text, 1.25f, new Vector3(0f, 0f, -size.z * 0.5f - 0.02f), Color.white,
                primary ? FontStyle.Bold : FontStyle.Normal);

            var button = root.AddComponent<MenuButton3D>();
            var so = new SerializedObject(button);
            so.FindProperty("action").enumValueIndex = (int)action;
            so.FindProperty("visual").objectReferenceValue = visual;
            so.FindProperty("body").objectReferenceValue = renderer;
            so.FindProperty("label").objectReferenceValue = label;
            if (primary)
            {
                so.FindProperty("color").colorValue = new Color(0.07f, 0.45f, 0.88f);
                so.FindProperty("hoverColor").colorValue = new Color(0.16f, 0.58f, 1f);
                so.FindProperty("pressedColor").colorValue = new Color(0.04f, 0.33f, 0.68f);
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

        static Material GetMaterial(string name, Color color, float smoothness)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/Lobby", "Materials");

            var path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
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
