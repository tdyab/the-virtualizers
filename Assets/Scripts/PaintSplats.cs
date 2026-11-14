using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PaintSplats : MonoBehaviour
{
    [Header("Shot splats")]
    public float shotSplatSize = 0.32f;
    [Tooltip("Oldest shot splats are reused after this many.")]
    public int maxSplats = 160;

    [Header("Pre-painted field")]
    public bool prePaint = true;
    public int floorSplats = 70;
    public int wallSplats = 90;
    public int seed = 1337;

    static readonly Color[] FieldPaint = {
        new Color(1.00f, 0.20f, 0.45f), new Color(0.15f, 0.75f, 1.00f), new Color(1.00f, 0.55f, 0.05f),
        new Color(0.55f, 0.95f, 0.15f), new Color(0.75f, 0.30f, 1.00f), new Color(1.00f, 0.90f, 0.10f) };

    const int Variants = 6;
    static Sprite[] sprites;
    static Material material;
    static PaintSplats instance;

    readonly Queue<SpriteRenderer> shotSplats = new Queue<SpriteRenderer>();
    Transform fieldRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded += (s, m) => Instance.PaintField();
        Instance.PaintField();
    }

    static PaintSplats Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<PaintSplats>();
            if (instance == null) instance = new GameObject("PaintSplats (auto)").AddComponent<PaintSplats>();
            return instance;
        }
    }

    void Awake() { if (instance == null) instance = this; }

    public static Sprite Splat(int i)
    {
        if (sprites == null)
        {
            sprites = new Sprite[Variants];
            for (int v = 0; v < Variants; v++) sprites[v] = BuildSplat(9000 + v * 77);
        }
        return sprites[((i % Variants) + Variants) % Variants];
    }

    static Material Mat => material != null ? material
        : material = new Material(Shader.Find("Sprites/Default")) { name = "PaintSplat (runtime)" };

    public static void Shot(Vector3 start, Vector3 end, Color color)
    {
        Vector3 d = end - start;
        if (d.sqrMagnitude < 0.0001f) return;
        Vector3 dir = d.normalized;
        if (!Physics.Raycast(end - dir * 0.08f, dir, out RaycastHit hit, 0.2f, ~0, QueryTriggerInteraction.Ignore)) return;
        if (!Paintable(hit.collider)) return;
        Instance.AddShotSplat(hit, color);
    }

    void AddShotSplat(RaycastHit hit, Color color)
    {
        SpriteRenderer sr = shotSplats.Count >= maxSplats ? shotSplats.Dequeue() : MakeRenderer(transform);
        Place(sr, hit, shotSplatSize * Random.Range(0.8f, 1.25f), Random.Range(0, Variants), Random.value * 360f);
        sr.color = Color.Lerp(color, Color.white, 0.1f);
        sr.sortingOrder = 2;
        shotSplats.Enqueue(sr);
    }

    void PaintField()
    {
        if (!prePaint || fieldRoot != null) return;
        if (!FindFieldBounds(out Bounds b)) return;
        fieldRoot = new GameObject("FieldPaint").transform;
        fieldRoot.SetParent(transform, false);

        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble();
        Color Paint() => FieldPaint[rng.Next(FieldPaint.Length)];

        for (int i = 0; i < floorSplats; i++)
        {
            var from = new Vector3(Mathf.Lerp(b.min.x, b.max.x, R()), b.max.y + 5f, Mathf.Lerp(b.min.z, b.max.z, R()));
            float size = Mathf.Lerp(0.4f, 1.3f, R() * R());
            int v = rng.Next(Variants); float rot = R() * 360f; Color c = Paint();
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore)
                && Paintable(hit.collider))
                AddFieldSplat(hit, size, v, rot, c);
        }

        for (int i = 0; i < wallSplats; i++)
        {
            var from = new Vector3(Mathf.Lerp(b.min.x, b.max.x, R()), b.min.y + Mathf.Lerp(0.3f, 2.2f, R()), Mathf.Lerp(b.min.z, b.max.z, R()));
            float a = R() * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Lerp(-0.15f, 0.15f, R()), Mathf.Sin(a));
            float size = Mathf.Lerp(0.25f, 0.8f, R());
            int v = rng.Next(Variants); float rot = R() * 360f; Color c = Paint();
            if (Physics.Raycast(from, dir, out RaycastHit hit, 40f, ~0, QueryTriggerInteraction.Ignore)
                && Mathf.Abs(hit.normal.y) < 0.7f
                && Paintable(hit.collider))
                AddFieldSplat(hit, size, v, rot, c);
        }
    }

    void AddFieldSplat(RaycastHit hit, float size, int variant, float rot, Color c)
    {
        var sr = MakeRenderer(fieldRoot);
        Place(sr, hit, size, variant, rot);
        c.a = 0.92f;
        sr.color = c;
        sr.sortingOrder = 1;
    }

    static bool Paintable(Collider c)
    {
        if (c.GetComponentInParent<PlayerState>() != null || c.GetComponent<Hitbox>() != null) return false;
        var r = c.GetComponent<Renderer>();
        return r != null && r.enabled;
    }

    static bool FindFieldBounds(out Bounds bounds)
    {
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (r.sharedMaterial != null && r.sharedMaterial.name == "Floor")
            {
                bounds = r.bounds;
                bounds.Expand(new Vector3(-0.5f, 0f, -0.5f));
                return true;
            }
        }
        bounds = default;
        return false;
    }

    static SpriteRenderer MakeRenderer(Transform parent)
    {
        var go = new GameObject("Splat");
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sharedMaterial = Mat;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        return sr;
    }

    static void Place(SpriteRenderer sr, RaycastHit hit, float size, int variant, float rot)
    {
        sr.sprite = Splat(variant);
        var t = sr.transform;
        t.position = hit.point + hit.normal * 0.006f;
        t.rotation = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0, 0, rot);
        t.localScale = Vector3.one * size;
    }

    static Sprite BuildSplat(int seed)
    {
        const int N = 128;
        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble();

        var ph = new float[5]; var amp = new float[5];
        for (int k = 0; k < 5; k++) { ph[k] = R() * 6.283f; amp[k] = Mathf.Lerp(0.02f, 0.07f, R()); }
        int spikes = 5 + rng.Next(5);
        var spikeAng = new float[spikes]; var spikeLen = new float[spikes];
        for (int k = 0; k < spikes; k++) { spikeAng[k] = R() * 6.283f; spikeLen[k] = Mathf.Lerp(0.08f, 0.28f, R()); }
        int drops = 6 + rng.Next(8);
        var drop = new Vector3[drops];
        for (int k = 0; k < drops; k++)
        {
            float a = R() * 6.283f, d = Mathf.Lerp(0.62f, 0.95f, R());
            drop[k] = new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d, Mathf.Lerp(0.02f, 0.06f, R()));
        }

        var px = new Color32[N * N];
        float edge = 2.5f / N;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);

                float rad = 0.42f;
                for (int k = 0; k < 5; k++) rad += amp[k] * Mathf.Sin((k + 2) * a + ph[k]);
                for (int k = 0; k < spikes; k++)
                {
                    float da = Mathf.DeltaAngle(a * Mathf.Rad2Deg, spikeAng[k] * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    rad += spikeLen[k] * Mathf.Exp(-da * da * 160f);
                }
                float alpha = Mathf.Clamp01((rad - r) / edge + 0.5f);

                for (int k = 0; k < drops; k++)
                {
                    float dx = u - drop[k].x, dy = v - drop[k].y;
                    float dd = Mathf.Sqrt(dx * dx + dy * dy);
                    alpha = Mathf.Max(alpha, Mathf.Clamp01((drop[k].z - dd) / edge + 0.5f));
                }

                float shade = Mathf.Lerp(0.82f, 1f, Mathf.Clamp01((rad - r) / 0.12f));
                byte g = (byte)(255f * shade);
                px[y * N + x] = new Color32(g, g, g, (byte)(255f * alpha));
            }

        var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "PaintSplat" };
        tex.SetPixels32(px);
        tex.Apply(true, false);
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N, 0, SpriteMeshType.FullRect);
    }
}
