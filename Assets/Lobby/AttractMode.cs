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

        class Star
        {
            public Transform transform;
            public Renderer renderer;
            public Vector3 push;
            public Color color;
            public float size, depth, seed;
        }

        class Asteroid
        {
            public Transform transform;
            public Vector3 velocity, spinAxis;
            public float speed, spin;
        }

        class Planet
        {
            public Transform transform;
            public float spin;
        }

        class Ring
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
        [SerializeField] Material asteroidMaterial;
        [SerializeField] Material planetMaterial;
        [SerializeField] Color spaceColor = new Color(0.005f, 0.01f, 0.035f);
        [SerializeField] Color nebulaA = new Color(0.35f, 0.08f, 0.45f);
        [SerializeField] Color nebulaB = new Color(0.02f, 0.25f, 0.45f);

        [Header("Timing")]
        [SerializeField] float wakeSeconds = 1.2f;
        [SerializeField] float idleSeconds = 45f;

        [Header("Space")]
        [SerializeField] Vector2 halfExtents = new Vector2(11f, 6.2f);
        [SerializeField] int starCount = 160;
        [SerializeField] int asteroidCount = 10;
        [SerializeField] float driftSpeed = 0.6f;
        [SerializeField] float pushRadius = 3.5f;
        [SerializeField] float pushStrength = 9f;
        [SerializeField] float pullRadius = 6f;
        [SerializeField] float shootingStarEvery = 4f;

        [Header("Shockwaves")]
        [SerializeField] int ringCount = 14;
        [SerializeField] float ringSeconds = 1.2f;
        [SerializeField] float ringSpacing = 0.8f;

        [Header("Hands")]
        [SerializeField] float edgeMargin = 0.15f;
        [SerializeField] float cameraAspect = 4f / 3f;

        readonly Vector3[] points = new Vector3[HandTrackingReceiver.LandmarkCount];
        readonly Vector3[] hands = new Vector3[HandTrackingReceiver.HandCount];
        readonly bool[] pinching = new bool[HandTrackingReceiver.HandCount];
        readonly Vector3[] lastRingAt = new Vector3[HandTrackingReceiver.HandCount];
        HandTrackingReceiver receiver;
        MaterialPropertyBlock block;
        Star[] stars;
        Asteroid[] asteroids;
        Planet[] planets;
        Ring[] rings;
        Transform[] handGlows;
        Transform shootingStar;
        Renderer shootingStarRenderer;
        Vector3 shootingStarVelocity;
        float shootingStarAge = float.PositiveInfinity, nextShootingStar;
        Material nebulaNear, nebulaFar;
        Color promptColor;
        int handCount, nextRing;
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
            BuildSky();
            SpawnPlanets();
            SpawnStars();
            SpawnAsteroids();
            SpawnEffects();
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
                UpdateSky(dt);
                UpdateStars(dt);
                UpdateAsteroids(dt);
                UpdateShootingStar(dt);
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

        void BuildSky()
        {
            var background = new Material(backgroundMaterial);
            background.SetColor(BaseColor, spaceColor);
            CreateQuad("Space", background, new Vector3(0f, 0f, 20f), new Vector2(90f, 54f));

            nebulaFar = AdditiveMaterial(NebulaTexture(nebulaA, nebulaB, 1), new Color(0.55f, 0.55f, 0.55f));
            nebulaFar.mainTextureScale = new Vector2(1.2f, 0.8f);
            nebulaNear = AdditiveMaterial(NebulaTexture(nebulaB, nebulaA, 2), new Color(0.35f, 0.35f, 0.35f));
            nebulaNear.mainTextureScale = new Vector2(1.6f, 1f);
            CreateQuad("Nebula Far", nebulaFar, new Vector3(0f, 0f, 18f), new Vector2(80f, 48f));
            CreateQuad("Nebula Near", nebulaNear, new Vector3(0f, 0f, 16f), new Vector2(76f, 46f));
        }

        void SpawnPlanets()
        {
            planets = new[]
            {
                CreatePlanet("Planet Violet", new Vector3(-15f, 6.5f, 14f), 7f, new Color(0.55f, 0.35f, 0.9f), new Color(0.2f, 0.1f, 0.4f), false),
                CreatePlanet("Planet Teal", new Vector3(14f, -7f, 12f), 5f, new Color(0.3f, 0.85f, 0.75f), new Color(0.05f, 0.3f, 0.45f), true),
            };
        }

        Planet CreatePlanet(string name, Vector3 position, float size, Color light, Color dark, bool ringed)
        {
            var material = new Material(planetMaterial);
            material.SetTexture(BaseMap, PlanetTexture(light, dark));
            var sphere = CreatePart(attractRoot.transform, PrimitiveType.Sphere, material, position, Vector3.one * size,
                new Vector3(20f, 0f, -15f));
            sphere.name = name;
            if (ringed)
            {
                var ring = AdditiveMaterial(RingTexture(0.75f, 0.12f), new Color(0.35f, 0.6f, 0.6f));
                var quad = CreatePart(attractRoot.transform, PrimitiveType.Quad, ring, position, Vector3.one * size * 2.1f,
                    new Vector3(72f, 0f, -18f));
                quad.name = $"{name} Ring";
            }
            return new Planet { transform = sphere, spin = UnityEngine.Random.Range(4f, 8f) };
        }

        void SpawnStars()
        {
            var material = AdditiveMaterial(StarTexture(), Color.white);
            stars = new Star[starCount];
            for (int i = 0; i < starCount; i++)
            {
                float depth = UnityEngine.Random.value;
                float size = Mathf.Lerp(0.06f, 0.22f, depth * depth) * UnityEngine.Random.Range(0.7f, 1.3f);
                var transform = CreateQuad($"Star {i}", material, new Vector3(
                    UnityEngine.Random.Range(-halfExtents.x - 1f, halfExtents.x + 1f),
                    UnityEngine.Random.Range(-halfExtents.y - 1f, halfExtents.y + 1f),
                    Mathf.Lerp(8f, 0f, depth)), Vector2.one * size);
                float tint = UnityEngine.Random.value;
                stars[i] = new Star
                {
                    transform = transform,
                    renderer = transform.GetComponent<Renderer>(),
                    size = size,
                    depth = depth,
                    seed = UnityEngine.Random.Range(0f, 100f),
                    color = tint < 0.2f ? new Color(0.7f, 0.8f, 1.6f)
                        : tint < 0.35f ? new Color(1.6f, 1.2f, 0.8f)
                        : new Color(1.3f, 1.3f, 1.4f),
                };
            }
        }

        void SpawnAsteroids()
        {
            asteroids = new Asteroid[asteroidCount];
            for (int i = 0; i < asteroidCount; i++)
            {
                float size = UnityEngine.Random.Range(0.35f, 0.9f);
                var transform = CreatePart(attractRoot.transform, PrimitiveType.Sphere, asteroidMaterial, new Vector3(
                    UnityEngine.Random.Range(-halfExtents.x, halfExtents.x),
                    UnityEngine.Random.Range(-halfExtents.y, halfExtents.y),
                    UnityEngine.Random.Range(0.5f, 3f)),
                    new Vector3(size * UnityEngine.Random.Range(0.8f, 1.2f), size * UnityEngine.Random.Range(0.6f, 0.9f), size),
                    UnityEngine.Random.rotation.eulerAngles);
                transform.name = $"Asteroid {i}";
                for (int k = 0; k < 2; k++)
                {
                    var lump = CreatePart(transform, PrimitiveType.Sphere, asteroidMaterial,
                        UnityEngine.Random.onUnitSphere * 0.3f, Vector3.one * UnityEngine.Random.Range(0.5f, 0.75f), Vector3.zero);
                    lump.name = "Lump";
                }
                var direction = UnityEngine.Random.insideUnitCircle.normalized;
                float speed = driftSpeed * UnityEngine.Random.Range(0.5f, 1.2f);
                asteroids[i] = new Asteroid
                {
                    transform = transform,
                    speed = speed,
                    velocity = new Vector3(direction.x, direction.y * 0.5f, 0f) * speed,
                    spinAxis = UnityEngine.Random.onUnitSphere,
                    spin = UnityEngine.Random.Range(15f, 60f),
                };
            }
        }

        void SpawnEffects()
        {
            var ringMaterial = AdditiveMaterial(RingTexture(0.85f, 0.06f), Color.white);
            rings = new Ring[ringCount];
            for (int i = 0; i < ringCount; i++)
            {
                var transform = CreateQuad($"Shockwave {i}", ringMaterial, new Vector3(0f, 0f, -0.5f), Vector2.zero);
                transform.gameObject.SetActive(false);
                rings[i] = new Ring { transform = transform, renderer = transform.GetComponent<Renderer>() };
            }

            var glow = AdditiveMaterial(StarTexture(), new Color(0.9f, 0.85f, 1.4f));
            handGlows = new Transform[HandTrackingReceiver.HandCount];
            for (int i = 0; i < handGlows.Length; i++)
            {
                handGlows[i] = CreateQuad($"Hand {i}", glow, new Vector3(0f, 0f, -0.6f), Vector2.one * 1.8f);
                handGlows[i].gameObject.SetActive(false);
            }

            shootingStar = CreateQuad("Shooting Star", AdditiveMaterial(StreakTexture(), Color.white), Vector3.zero, new Vector2(4f, 0.12f));
            shootingStarRenderer = shootingStar.GetComponent<Renderer>();
            shootingStar.gameObject.SetActive(false);
            nextShootingStar = Time.unscaledTime + 2f;
        }

        void UpdateSky(float dt)
        {
            float time = Time.unscaledTime;
            nebulaFar.mainTextureOffset = new Vector2(time * 0.002f, time * 0.001f);
            nebulaNear.mainTextureOffset = new Vector2(time * 0.004f, -time * 0.0015f);
            foreach (var planet in planets)
                planet.transform.Rotate(Vector3.up, planet.spin * dt, Space.Self);
        }

        void UpdateStars(float dt)
        {
            float time = Time.unscaledTime;
            float spanX = halfExtents.x + 1f, spanY = halfExtents.y + 1f;
            foreach (var star in stars)
            {
                var p = star.transform.localPosition;
                p.x -= driftSpeed * (0.2f + star.depth) * dt;

                var target = Vector3.zero;
                for (int h = 0; h < handCount; h++)
                {
                    var offset = p - hands[h];
                    offset.z = 0f;
                    float distance = offset.magnitude;
                    if (distance < 1e-3f)
                        continue;
                    var away = offset / distance;
                    if (pinching[h] && distance < pullRadius)
                    {
                        float strength = 1f - distance / pullRadius;
                        var swirl = new Vector3(-away.y, away.x, 0f);
                        target += (swirl * 3f - away * 1.5f * Mathf.Clamp01(distance - 0.5f)) * strength;
                    }
                    else if (!pinching[h] && distance < pushRadius)
                        target += away * pushStrength * (1f - distance / pushRadius);
                }
                star.push = Vector3.Lerp(star.push, target, 1f - Mathf.Exp(-4f * dt));
                p += star.push * (0.4f + star.depth) * dt;

                if (p.x < -spanX) { p.x += 2f * spanX; p.y = UnityEngine.Random.Range(-spanY, spanY); }
                else if (p.x > spanX) p.x -= 2f * spanX;
                if (p.y < -spanY) p.y += 2f * spanY;
                else if (p.y > spanY) p.y -= 2f * spanY;
                star.transform.localPosition = p;

                float twinkle = 0.55f + 0.45f * Mathf.Sin(time * (1.5f + star.seed % 3f) + star.seed);
                float excite = Mathf.Clamp01(star.push.magnitude * 0.25f);
                star.transform.localScale = Vector3.one * star.size * (1f + excite);
                block.SetColor(BaseColor, star.color * (twinkle + excite));
                star.renderer.SetPropertyBlock(block);
            }
        }

        void UpdateAsteroids(float dt)
        {
            foreach (var a in asteroids)
            {
                var p = a.transform.localPosition;
                var desired = a.velocity.normalized * a.speed;
                for (int h = 0; h < handCount; h++)
                {
                    var offset = p - hands[h];
                    offset.z = 0f;
                    float distance = offset.magnitude;
                    if (distance < 1e-3f)
                        continue;
                    var away = offset / distance;
                    if (pinching[h] && distance < pullRadius)
                        desired += (new Vector3(-away.y, away.x, 0f) * 2.5f - away * Mathf.Clamp01(distance - 1.2f) * 2f)
                                   * (1f - distance / pullRadius);
                    else if (!pinching[h] && distance < pushRadius)
                        desired += away * pushStrength * (1f - distance / pushRadius);
                }

                a.velocity = Vector3.Lerp(a.velocity, desired, 1f - Mathf.Exp(-1.5f * dt));
                if (a.velocity.sqrMagnitude < 0.01f)
                    a.velocity = UnityEngine.Random.insideUnitCircle.normalized * a.speed;
                p += a.velocity * dt;
                p.x = Wrap(p.x, halfExtents.x + 1.5f);
                p.y = Wrap(p.y, halfExtents.y + 1.5f);
                a.transform.localPosition = p;
                a.transform.Rotate(a.spinAxis, (a.spin + a.velocity.magnitude * 20f) * dt, Space.World);
            }
        }

        void UpdateShootingStar(float dt)
        {
            float time = Time.unscaledTime;
            if (shootingStarAge > 1.2f && time >= nextShootingStar)
            {
                shootingStarAge = 0f;
                nextShootingStar = time + shootingStarEvery * UnityEngine.Random.Range(0.6f, 1.6f);
                float fromLeft = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                var start = new Vector3(fromLeft * UnityEngine.Random.Range(2f, halfExtents.x),
                    UnityEngine.Random.Range(1f, halfExtents.y), 6f);
                shootingStarVelocity = new Vector3(-fromLeft * 16f, -UnityEngine.Random.Range(4f, 8f), 0f);
                shootingStar.localPosition = start;
                float angle = Mathf.Atan2(shootingStarVelocity.y, shootingStarVelocity.x) * Mathf.Rad2Deg;
                shootingStar.localRotation = Quaternion.Euler(0f, 0f, angle);
                shootingStar.gameObject.SetActive(true);
            }
            if (shootingStarAge > 1.2f)
                return;

            shootingStarAge += dt;
            shootingStar.localPosition += shootingStarVelocity * dt;
            float fade = Mathf.Sin(Mathf.Clamp01(shootingStarAge / 1.2f) * Mathf.PI);
            block.SetColor(BaseColor, new Color(1.4f, 1.4f, 1.6f) * fade);
            shootingStarRenderer.SetPropertyBlock(block);
            if (shootingStarAge > 1.2f)
                shootingStar.gameObject.SetActive(false);
        }

        void UpdateHands(float dt)
        {
            float time = Time.unscaledTime;
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
                        lastRingAt[i] = hands[i];
                        SpawnRing(hands[i]);
                    }
                }
                if (!visible)
                    continue;

                var target = new Vector3(hands[i].x, hands[i].y, glow.localPosition.z);
                glow.localPosition = Vector3.Lerp(glow.localPosition, target, 1f - Mathf.Exp(-18f * dt));
                float pulse = 1f + 0.08f * Mathf.Sin(time * 6f);
                glow.localScale = Vector3.one * (pinching[i] ? 1.2f : 1.8f) * pulse;
                glow.localRotation = Quaternion.Euler(0f, 0f, time * (pinching[i] ? 120f : 15f));
                if ((hands[i] - lastRingAt[i]).magnitude > ringSpacing)
                {
                    lastRingAt[i] = hands[i];
                    SpawnRing(hands[i]);
                }
            }

            foreach (var ring in rings)
            {
                if (ring.age >= ringSeconds)
                    continue;
                ring.age += dt;
                float t = Mathf.Clamp01(ring.age / ringSeconds);
                if (t >= 1f)
                {
                    ring.transform.gameObject.SetActive(false);
                    continue;
                }
                ring.transform.localScale = Vector3.one * Mathf.Lerp(0.4f, 3.6f, 1f - (1f - t) * (1f - t));
                block.SetColor(BaseColor, new Color(0.55f, 0.45f, 0.95f) * (1f - t));
                ring.renderer.SetPropertyBlock(block);
            }
        }

        void SpawnRing(Vector3 position)
        {
            var ring = rings[nextRing];
            nextRing = (nextRing + 1) % rings.Length;
            ring.age = 0f;
            ring.transform.localPosition = new Vector3(position.x, position.y, -0.5f);
            ring.transform.localScale = Vector3.zero;
            ring.transform.gameObject.SetActive(true);
        }

        void UpdatePrompt()
        {
            float fill = Mathf.Clamp01(hold / wakeSeconds);
            progressFill.localScale = new Vector3(progressWidth * fill, progressFill.localScale.y, 1f);
            progressFill.localPosition = new Vector3(-progressWidth * 0.5f * (1f - fill), progressFill.localPosition.y,
                progressFill.localPosition.z);

            prompt.text = handCount > 0 ? "Trzymaj dłoń w górze…"
                : receiver.IsReceiving ? "Podnieś dłoń przed kamerą, aby zacząć"
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

        static Texture2D NebulaTexture(Color first, Color second, int seed)
        {
            var random = new System.Random(seed);
            var waves = new (int a, int b, float phase, float weight)[12];
            for (int i = 0; i < waves.Length; i++)
            {
                int octave = 1 + i / 3;
                waves[i] = (random.Next(-octave, octave + 1), random.Next(-octave, octave + 1) | 1,
                    (float)random.NextDouble() * 6.28f, 1f / octave);
            }

            float Field(float u, float v, int offset)
            {
                float sum = 0f, total = 0f;
                for (int i = offset; i < waves.Length; i += 2)
                {
                    var w = waves[i];
                    sum += Mathf.Sin(2f * Mathf.PI * (w.a * u + w.b * v) + w.phase) * w.weight;
                    total += w.weight;
                }
                return sum / total * 0.5f + 0.5f;
            }

            return Generate(256, 256, TextureWrapMode.Repeat, (u, v) =>
            {
                float a = Mathf.Pow(Mathf.Clamp01(Field(u, v, 0) * 1.6f - 0.55f), 1.8f);
                float b = Mathf.Pow(Mathf.Clamp01(Field(u, v, 1) * 1.6f - 0.6f), 1.8f);
                var c = first * a + second * b;
                c.a = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                return c;
            });
        }

        static Texture2D PlanetTexture(Color light, Color dark)
        {
            var random = new System.Random(light.GetHashCode());
            float phase = (float)random.NextDouble() * 6.28f;
            return Generate(256, 128, TextureWrapMode.Repeat, (u, v) =>
            {
                float bands = Mathf.Sin(v * 18f + Mathf.Sin(u * 6.28f * 3f + phase) * 0.8f) * 0.5f + 0.5f;
                float detail = Mathf.Sin(v * 47f + u * 6.28f * 2f + phase) * 0.15f;
                var c = Color.Lerp(dark, light, Mathf.Clamp01(bands + detail));
                c.a = 1f;
                return c;
            });
        }

        static Texture2D StarTexture() => Generate(64, 64, TextureWrapMode.Clamp, (u, v) =>
        {
            float x = Mathf.Abs(u - 0.5f) * 2f, y = Mathf.Abs(v - 0.5f) * 2f;
            float core = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y)), 3f);
            float spikes = Mathf.Clamp01(1f - x) * Mathf.Exp(-y * 25f) + Mathf.Clamp01(1f - y) * Mathf.Exp(-x * 25f);
            return Gray(Mathf.Clamp01(core + 0.5f * spikes * spikes));
        });

        static Texture2D RingTexture(float radius, float width) => Generate(128, 128, TextureWrapMode.Clamp,
            (u, v) => Gray(Mathf.Exp(-Mathf.Pow((Radius(u, v) - radius) / width, 2f))));

        static Texture2D StreakTexture() => Generate(128, 16, TextureWrapMode.Clamp, (u, v) =>
        {
            float across = Mathf.Exp(-Mathf.Pow((v - 0.5f) / 0.18f, 2f));
            return Gray(Mathf.Pow(u, 3f) * across);
        });

        static Color Gray(float value) => new Color(value, value, value, value);

        static float Radius(float u, float v) => Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;

        static Texture2D Generate(int width, int height, TextureWrapMode wrap, Func<float, float, Color> value)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true) { wrapMode = wrap };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = value((float)x / width, (float)y / height);
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

        static float Wrap(float value, float half) =>
            value > half ? value - 2f * half : value < -half ? value + 2f * half : value;

        float Remap(float value) => Mathf.Clamp01((value - edgeMargin) / (1f - 2f * edgeMargin));

        float Distance(Vector3 a, Vector3 b) => new Vector2((a.x - b.x) * cameraAspect, a.y - b.y).magnitude;
    }
}
