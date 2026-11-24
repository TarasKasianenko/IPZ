using System;
using IPZ.Tracking;
using UnityEngine;
using UnityEngine.Rendering;

namespace IPZ.Lobby
{
    [RequireComponent(typeof(HandTrackingReceiver))]
    public class AttractMode : MonoBehaviour
    {
        const int Wrist = 0, ThumbTip = 4, IndexTip = 8, MiddleKnuckle = 9;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static bool startedOnce;

        class Fish
        {
            public Transform root, tail;
            public Vector3 velocity;
            public float speed, phase, yaw, seed;
        }

        class Bubble
        {
            public Transform transform;
            public float speed, size, seed;
        }

        class Ripple
        {
            public Transform transform;
            public Renderer renderer;
            public float age = float.PositiveInfinity;
        }

        [Header("Scene")]
        [SerializeField] Camera view;
        [SerializeField] GameObject menuRoot;
        [SerializeField] GameObject attractRoot;
        [SerializeField] TextMesh prompt;
        [SerializeField] Transform progressFill;
        [SerializeField] float progressWidth = 8f;

        [Header("Materials")]
        [SerializeField] Material backgroundMaterial;
        [SerializeField] Material additiveMaterial;
        [SerializeField] Material[] fishMaterials;
        [SerializeField] Material eyeMaterial;
        [SerializeField] Color deepColor = new Color(0.01f, 0.04f, 0.12f);
        [SerializeField] Color shallowColor = new Color(0.03f, 0.25f, 0.4f);

        [Header("Timing")]
        [SerializeField] float wakeSeconds = 1.2f;
        [SerializeField] float idleSeconds = 45f;

        [Header("Fish")]
        [SerializeField] int fishCount = 16;
        [SerializeField] Vector2 halfExtents = new Vector2(10f, 5.2f);
        [SerializeField] float swimSpeed = 1.4f;
        [SerializeField] float fleeRadius = 3.5f;
        [SerializeField] float fleeSpeed = 7f;
        [SerializeField] float feedRadius = 7f;

        [Header("Bubbles and ripples")]
        [SerializeField] int bubbleCount = 40;
        [SerializeField] int rippleCount = 14;
        [SerializeField] float rippleSeconds = 1.4f;
        [SerializeField] float rippleSpacing = 0.7f;

        [Header("Hands")]
        [SerializeField] float edgeMargin = 0.15f;
        [SerializeField] float cameraAspect = 4f / 3f;

        readonly Vector3[] points = new Vector3[HandTrackingReceiver.LandmarkCount];
        readonly Vector3[] hands = new Vector3[HandTrackingReceiver.HandCount];
        readonly bool[] pinching = new bool[HandTrackingReceiver.HandCount];
        readonly Vector3[] lastRippleAt = new Vector3[HandTrackingReceiver.HandCount];
        HandTrackingReceiver receiver;
        MaterialPropertyBlock block;
        Fish[] fish;
        Bubble[] bubbles;
        Ripple[] ripples;
        Transform[] handGlows, rays;
        Material causticsA, causticsB;
        Color promptColor;
        int handCount, nextRipple;
        float hold, lastActivity;
        Vector3 lastMouse;

        public bool IsActive { get; private set; }
        public event Action Started;

        void Awake()
        {
            receiver = GetComponent<HandTrackingReceiver>();
            block = new MaterialPropertyBlock();
            promptColor = prompt.color;
            lastMouse = Input.mousePosition;
            lastActivity = Time.unscaledTime;
            BuildWater();
            SpawnFish();
            SpawnBubbles();
            SpawnRipplesAndGlows();
            SetActive(!startedOnce);
            startedOnce = true;
        }

        void Update()
        {
            ReadHands();
            bool mouseUsed = ReadMouse(out bool clicked);

            if (IsActive)
            {
                hold = handCount > 0 ? hold + Time.unscaledDeltaTime : Mathf.Max(0f, hold - 2f * Time.unscaledDeltaTime);
                if (hold >= wakeSeconds || clicked)
                {
                    SetActive(false);
                    return;
                }
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                UpdateWater();
                UpdateFish(dt);
                UpdateBubbles(dt);
                UpdateHands(dt);
                UpdatePrompt();
            }
            else
            {
                if (handCount > 0 || mouseUsed)
                    lastActivity = Time.unscaledTime;
                if (Time.unscaledTime - lastActivity > idleSeconds)
                    SetActive(true);
            }
        }

        void SetActive(bool active)
        {
            IsActive = active;
            hold = 0f;
            lastActivity = Time.unscaledTime;
            attractRoot.SetActive(active);
            menuRoot.SetActive(!active);
            if (active)
                Started?.Invoke();
        }

        void ReadHands()
        {
            handCount = 0;
            for (int i = 0; i < HandTrackingReceiver.HandCount; i++)
            {
                if (!receiver.TryGetHand(i, points))
                    continue;
                var palm = (points[Wrist] + points[MiddleKnuckle]) * 0.5f;
                hands[handCount] = ToWorld(new Vector2(Remap(palm.x), Remap(palm.y)));
                float size = Distance(points[Wrist], points[MiddleKnuckle]);
                pinching[handCount] = size > 1e-4f && Distance(points[ThumbTip], points[IndexTip]) / size < 0.3f;
                handCount++;
            }
        }

        bool ReadMouse(out bool clicked)
        {
            clicked = Input.GetMouseButtonDown(0) || Input.anyKeyDown;
            bool moved = Input.mousePosition != lastMouse;
            lastMouse = Input.mousePosition;
            return clicked || moved || Input.mouseScrollDelta.y != 0f;
        }

        void BuildWater()
        {
            var background = new Material(backgroundMaterial);
            background.SetTexture(BaseMap, GradientTexture());
            CreateQuad("Water", background, new Vector3(0f, 0f, 12f), new Vector2(64f, 38f));

            var caustics = CausticsTexture();
            causticsA = AdditiveMaterial(caustics, new Color(0.10f, 0.28f, 0.32f));
            causticsA.mainTextureScale = new Vector2(3f, 1.8f);
            causticsB = AdditiveMaterial(caustics, new Color(0.06f, 0.18f, 0.26f));
            causticsB.mainTextureScale = new Vector2(2.2f, 1.3f);
            CreateQuad("Caustics A", causticsA, new Vector3(0f, 0f, 9f), new Vector2(54f, 32f));
            CreateQuad("Caustics B", causticsB, new Vector3(0f, 0f, 8.5f), new Vector2(54f, 32f));

            var ray = AdditiveMaterial(RayTexture(), new Color(0.16f, 0.32f, 0.36f));
            rays = new Transform[6];
            for (int i = 0; i < rays.Length; i++)
            {
                float x = Mathf.Lerp(-15f, 15f, i / (rays.Length - 1f)) + UnityEngine.Random.Range(-1.5f, 1.5f);
                rays[i] = CreateQuad($"Light Ray {i}", ray, new Vector3(x, 4f, 10f),
                    new Vector2(UnityEngine.Random.Range(2f, 4.5f), 26f));
            }
        }

        void SpawnFish()
        {
            fish = new Fish[fishCount];
            for (int i = 0; i < fishCount; i++)
            {
                var material = fishMaterials[i % fishMaterials.Length];
                var root = new GameObject($"Fish {i}").transform;
                root.SetParent(attractRoot.transform, false);
                root.localPosition = new Vector3(
                    UnityEngine.Random.Range(-halfExtents.x, halfExtents.x),
                    UnityEngine.Random.Range(-halfExtents.y, halfExtents.y),
                    UnityEngine.Random.Range(0f, 4f));
                root.localScale = Vector3.one * UnityEngine.Random.Range(0.6f, 1.3f);

                CreatePart(root, PrimitiveType.Sphere, material, Vector3.zero, new Vector3(1f, 0.45f, 0.3f), Vector3.zero);
                CreatePart(root, PrimitiveType.Cube, material, new Vector3(0.02f, 0.2f, 0f), new Vector3(0.24f, 0.24f, 0.03f),
                    new Vector3(0f, 0f, 45f));
                CreatePart(root, PrimitiveType.Sphere, eyeMaterial, new Vector3(0.3f, 0.06f, 0.12f), Vector3.one * 0.08f, Vector3.zero);
                CreatePart(root, PrimitiveType.Sphere, eyeMaterial, new Vector3(0.3f, 0.06f, -0.12f), Vector3.one * 0.08f, Vector3.zero);

                var tail = new GameObject("Tail").transform;
                tail.SetParent(root, false);
                tail.localPosition = new Vector3(-0.42f, 0f, 0f);
                CreatePart(tail, PrimitiveType.Cube, material, new Vector3(-0.16f, 0f, 0f), new Vector3(0.32f, 0.32f, 0.04f),
                    new Vector3(0f, 0f, 45f));

                bool left = UnityEngine.Random.value < 0.5f;
                fish[i] = new Fish
                {
                    root = root,
                    tail = tail,
                    speed = swimSpeed * UnityEngine.Random.Range(0.7f, 1.3f),
                    phase = UnityEngine.Random.Range(0f, 10f),
                    seed = UnityEngine.Random.Range(0f, 100f),
                    yaw = left ? 180f : 0f,
                    velocity = new Vector3(left ? -1f : 1f, 0f, 0f) * swimSpeed,
                };
            }
        }

        void SpawnBubbles()
        {
            var material = AdditiveMaterial(BubbleTexture(), new Color(0.5f, 0.75f, 0.85f));
            bubbles = new Bubble[bubbleCount];
            for (int i = 0; i < bubbleCount; i++)
            {
                var size = UnityEngine.Random.Range(0.08f, 0.28f);
                var transform = CreateQuad($"Bubble {i}", material, new Vector3(
                    UnityEngine.Random.Range(-halfExtents.x - 2f, halfExtents.x + 2f),
                    UnityEngine.Random.Range(-halfExtents.y - 1f, halfExtents.y + 1f),
                    UnityEngine.Random.Range(-0.5f, 5f)), Vector2.one * size);
                bubbles[i] = new Bubble
                {
                    transform = transform,
                    size = size,
                    speed = UnityEngine.Random.Range(0.5f, 1.4f),
                    seed = UnityEngine.Random.Range(0f, 100f),
                };
            }
        }

        void SpawnRipplesAndGlows()
        {
            var ring = AdditiveMaterial(RingTexture(), Color.white);
            ripples = new Ripple[rippleCount];
            for (int i = 0; i < rippleCount; i++)
            {
                var transform = CreateQuad($"Ripple {i}", ring, new Vector3(0f, 0f, -0.5f), Vector2.zero);
                transform.gameObject.SetActive(false);
                ripples[i] = new Ripple { transform = transform, renderer = transform.GetComponent<Renderer>() };
            }

            var glow = AdditiveMaterial(DotTexture(), new Color(0.25f, 0.6f, 0.7f));
            handGlows = new Transform[HandTrackingReceiver.HandCount];
            for (int i = 0; i < handGlows.Length; i++)
            {
                handGlows[i] = CreateQuad($"Hand {i}", glow, new Vector3(0f, 0f, -0.6f), Vector2.one * 1.6f);
                handGlows[i].gameObject.SetActive(false);
            }
        }

        void UpdateWater()
        {
            float time = Time.unscaledTime;
            causticsA.mainTextureOffset = new Vector2(time * 0.012f, time * 0.007f);
            causticsB.mainTextureOffset = new Vector2(-time * 0.009f, time * 0.011f);
            for (int i = 0; i < rays.Length; i++)
                rays[i].localRotation = Quaternion.Euler(0f, 0f, 12f + 5f * Mathf.Sin(time * 0.25f + i * 1.7f));
        }

        void UpdateFish(float dt)
        {
            float time = Time.unscaledTime;
            foreach (var f in fish)
            {
                var p = f.root.localPosition;
                float angle = (Mathf.PerlinNoise(f.seed, time * 0.08f) - 0.5f) * Mathf.PI * 3f;
                var desired = new Vector3(Mathf.Cos(angle), 0.35f * Mathf.Sin(angle), 0f) * f.speed;
                if (Mathf.Abs(p.x) > halfExtents.x)
                    desired.x = -Mathf.Sign(p.x) * f.speed;
                if (Mathf.Abs(p.y) > halfExtents.y)
                    desired.y = -Mathf.Sign(p.y) * f.speed * 0.6f;

                for (int h = 0; h < handCount; h++)
                {
                    var offset = p - hands[h];
                    offset.z = 0f;
                    float distance = offset.magnitude;
                    if (distance < 1e-3f)
                        continue;
                    var away = offset / distance;
                    if (pinching[h] && distance < feedRadius)
                        desired += -away * f.speed * 2f * Mathf.Clamp01((distance - 0.6f) / 1.5f);
                    else if (!pinching[h] && distance < fleeRadius)
                        desired += away * fleeSpeed * (1f - distance / fleeRadius);
                }

                foreach (var other in fish)
                {
                    if (other == f)
                        continue;
                    var offset = p - other.root.localPosition;
                    offset.z = 0f;
                    float distance = offset.magnitude;
                    if (distance < 1.1f && distance > 1e-3f)
                        desired += offset / distance * (1.1f - distance);
                }

                f.velocity = Vector3.Lerp(f.velocity, desired, 1f - Mathf.Exp(-2f * dt));
                p += f.velocity * dt;
                f.root.localPosition = p;

                float speed = f.velocity.magnitude;
                if (Mathf.Abs(f.velocity.x) > 0.15f)
                    f.yaw = Mathf.MoveTowards(f.yaw, f.velocity.x < 0f ? 180f : 0f, 360f * dt);
                float pitch = Mathf.Clamp(Mathf.Atan2(f.velocity.y, Mathf.Abs(f.velocity.x)) * Mathf.Rad2Deg, -35f, 35f);
                f.root.localRotation = Quaternion.Euler(0f, f.yaw, pitch);

                f.phase += dt * (5f + speed * 4f);
                f.tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(f.phase) * 35f, Mathf.Sin(f.phase) * 10f);
            }
        }

        void UpdateBubbles(float dt)
        {
            float time = Time.unscaledTime;
            float top = halfExtents.y + 2f;
            foreach (var b in bubbles)
            {
                var p = b.transform.localPosition;
                p.y += b.speed * dt;
                p.x += Mathf.Sin(time * 2f + b.seed) * 0.3f * dt;
                for (int h = 0; h < handCount; h++)
                {
                    var offset = p - hands[h];
                    offset.z = 0f;
                    float distance = offset.magnitude;
                    if (distance < 1.5f && distance > 1e-3f)
                        p += offset / distance * (1.5f - distance) * 3f * dt;
                }
                if (p.y > top)
                {
                    p.y = -top;
                    p.x = UnityEngine.Random.Range(-halfExtents.x - 2f, halfExtents.x + 2f);
                }
                b.transform.localPosition = p;
            }
        }

        void UpdateHands(float dt)
        {
            for (int i = 0; i < handGlows.Length; i++)
            {
                bool visible = i < handCount;
                var glow = handGlows[i];
                if (glow.gameObject.activeSelf != visible)
                {
                    glow.gameObject.SetActive(visible);
                    if (visible)
                    {
                        glow.localPosition = new Vector3(hands[i].x, hands[i].y, glow.localPosition.z);
                        lastRippleAt[i] = hands[i];
                        SpawnRipple(hands[i]);
                    }
                }
                if (!visible)
                    continue;

                var target = new Vector3(hands[i].x, hands[i].y, glow.localPosition.z);
                glow.localPosition = Vector3.Lerp(glow.localPosition, target, 1f - Mathf.Exp(-18f * dt));
                glow.localScale = Vector3.one * (pinching[i] ? 1.1f : 1.6f);
                if ((hands[i] - lastRippleAt[i]).magnitude > rippleSpacing)
                {
                    lastRippleAt[i] = hands[i];
                    SpawnRipple(hands[i]);
                }
            }

            foreach (var ripple in ripples)
            {
                if (ripple.age >= rippleSeconds)
                    continue;
                ripple.age += dt;
                float t = Mathf.Clamp01(ripple.age / rippleSeconds);
                if (t >= 1f)
                {
                    ripple.transform.gameObject.SetActive(false);
                    continue;
                }
                ripple.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 3.2f, 1f - (1f - t) * (1f - t));
                block.SetColor(BaseColor, new Color(0.3f, 0.65f, 0.75f) * (1f - t));
                ripple.renderer.SetPropertyBlock(block);
            }
        }

        void SpawnRipple(Vector3 position)
        {
            var ripple = ripples[nextRipple];
            nextRipple = (nextRipple + 1) % ripples.Length;
            ripple.age = 0f;
            ripple.transform.localPosition = new Vector3(position.x, position.y, -0.5f);
            ripple.transform.localScale = Vector3.zero;
            ripple.transform.gameObject.SetActive(true);
        }

        void UpdatePrompt()
        {
            float fill = Mathf.Clamp01(hold / wakeSeconds);
            progressFill.localScale = new Vector3(progressWidth * fill, progressFill.localScale.y, 1f);
            progressFill.localPosition = new Vector3(-progressWidth * 0.5f * (1f - fill), progressFill.localPosition.y,
                progressFill.localPosition.z);

            prompt.text = handCount > 0 ? "Trzymaj dłoń w wodzie…"
                : receiver.IsReceiving ? "Włóż dłoń do wody, aby zacząć"
                : "Kliknij, aby zacząć";
            var color = promptColor;
            color.a = handCount > 0 ? 1f : 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 2.5f);
            prompt.color = color;
        }

        Material AdditiveMaterial(Texture texture, Color tint)
        {
            var material = new Material(additiveMaterial);
            material.SetTexture(BaseMap, texture);
            material.SetColor(BaseColor, tint);
            return material;
        }

        Transform CreateQuad(string name, Material material, Vector3 position, Vector2 size)
        {
            var quad = CreatePart(attractRoot.transform, PrimitiveType.Quad, material, position, new Vector3(size.x, size.y, 1f), Vector3.zero);
            quad.name = name;
            return quad;
        }

        static Transform CreatePart(Transform parent, PrimitiveType type, Material material, Vector3 position, Vector3 scale,
            Vector3 rotation)
        {
            var part = GameObject.CreatePrimitive(type);
            Destroy(part.GetComponent<Collider>());
            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(rotation);
            part.transform.localScale = scale;
            return part.transform;
        }

        Texture2D GradientTexture()
        {
            var texture = new Texture2D(1, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 128; y++)
                texture.SetPixel(0, y, Color.Lerp(deepColor, shallowColor, Mathf.Pow(y / 127f, 1.6f)));
            texture.Apply();
            return texture;
        }

        static Texture2D CausticsTexture()
        {
            const int size = 256;
            var waves = new[] { (3, 1, 1, 2), (-2, 3, 2, -1), (1, -3, -3, 1) };
            return Generate(size, size, TextureWrapMode.Repeat, (u, v) =>
            {
                float sum = 0f;
                foreach (var (a, b, c, d) in waves)
                {
                    float warp = 1.4f * Mathf.Sin(2f * Mathf.PI * (c * u + d * v));
                    sum += 1f - Mathf.Abs(Mathf.Sin(2f * Mathf.PI * (a * u + b * v) + warp));
                }
                return Mathf.Pow(sum / waves.Length, 5f);
            });
        }

        static Texture2D RayTexture() => Generate(32, 128, TextureWrapMode.Clamp,
            (u, v) => Mathf.Pow(Mathf.Sin(Mathf.PI * u), 2f) * v * v);

        static Texture2D DotTexture() => Generate(64, 64, TextureWrapMode.Clamp,
            (u, v) => Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 2.2f));

        static Texture2D RingTexture() => Generate(128, 128, TextureWrapMode.Clamp, (u, v) =>
        {
            float r = Radius(u, v);
            return Mathf.Exp(-Mathf.Pow((r - 0.85f) / 0.06f, 2f));
        });

        static Texture2D BubbleTexture() => Generate(64, 64, TextureWrapMode.Clamp, (u, v) =>
        {
            float r = Radius(u, v);
            float rim = Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.1f, 2f));
            float shine = Mathf.Exp(-Mathf.Pow(Vector2.Distance(new Vector2(u, v), new Vector2(0.36f, 0.66f)) / 0.08f, 2f));
            return Mathf.Clamp01(rim + 0.12f * (1f - r) + shine) * (r < 1f ? 1f : 0f);
        });

        static float Radius(float u, float v) => Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;

        static Texture2D Generate(int width, int height, TextureWrapMode wrap, Func<float, float, float> value)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true) { wrapMode = wrap };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float c = Mathf.Clamp01(value((float)x / width, (float)y / height));
                pixels[y * width + x] = new Color(c, c, c, c);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        Vector3 ToWorld(Vector2 viewport)
        {
            var ray = view.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));
            var root = attractRoot.transform;
            float distance = (root.position.z - ray.origin.z) / ray.direction.z;
            return root.InverseTransformPoint(ray.GetPoint(distance));
        }

        float Remap(float value) => Mathf.Clamp01((value - edgeMargin) / (1f - 2f * edgeMargin));

        float Distance(Vector3 a, Vector3 b) => new Vector2((a.x - b.x) * cameraAspect, a.y - b.y).magnitude;
    }
}
