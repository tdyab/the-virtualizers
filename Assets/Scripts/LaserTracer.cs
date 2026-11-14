using System.Collections.Generic;
using UnityEngine;

/// Thin, glowing laser shots, like a shooter-game tracer instead of a thick solid bar.
///
/// For each shot:
///  * a bright white-hot BOLT streaks from the gun to the hit point very fast,
///  * it leaves a thin coloured TRAIL (the shooter's player colour) that fades out smoothly,
///  * a small glowing IMPACT flash appears where it lands (bigger when it hits a player).
///
/// Everything is unlit and soft-edged, so it reads as a clean thin beam in VR without looking heavy.
/// Usage: LaserTracer.Play(start, end, colour, hitPlayer). It creates itself the first time it is used.
/// To tweak it in the Inspector, add this component to an empty GameObject in the scene.
public class LaserTracer : MonoBehaviour
{
    [Header("Size (metres)")]
    [Tooltip("Width of the coloured trail. The bright centre is about a third of this.")]
    public float trailWidth = 0.026f;
    public float boltWidth = 0.024f;
    public float boltLength = 2.5f;
    public float impactSize = 0.18f;

    [Header("Timing")]
    [Tooltip("How fast the bolt travels (m/s). The arena is small, so it still feels instant.")]
    public float boltSpeed = 220f;
    [Tooltip("How long the trail takes to fade after the bolt lands.")]
    public float trailFade = 0.22f;
    public float impactTime = 0.14f;

    [Header("Look")]
    [Range(0f, 1f)] public float trailAlpha = 0.95f;
    [Tooltip("How much the bolt is pushed towards white (0 = player colour, 1 = pure white).")]
    [Range(0f, 1f)] public float boltWhiteness = 0.15f;

    static LaserTracer instance;
    static Material lineMaterial;
    static Sprite glowSprite;

    class Shot
    {
        public LineRenderer trail, bolt;
        public SpriteRenderer impact;
        public Vector3 start, dir;
        public float length, t, travelTime;
        public Color color;
        public bool hitPlayer, hitSomething, splatted;
    }

    readonly List<Shot> active = new List<Shot>();
    readonly Stack<Shot> pool = new Stack<Shot>();

    static LaserTracer Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<LaserTracer>();
            if (instance == null) instance = new GameObject("LaserTracer (auto)").AddComponent<LaserTracer>();
            return instance;
        }
    }

    void Awake() { if (instance == null) instance = this; }

    /// Shared soft-edged unlit material, also used to restyle the aim line.
    public static Material LineMaterial
    {
        get
        {
            if (lineMaterial == null)
            {
                // "Sprites/Default" is unlit, uses vertex colours and is always included in builds.
                lineMaterial = new Material(Shader.Find("Sprites/Default")) { name = "LaserLine (runtime)", mainTexture = BuildLineTexture() };
            }
            return lineMaterial;
        }
    }

    /// Plays one shot effect. hitSomething = false means the laser went to max range without hitting.
    public static void Play(Vector3 start, Vector3 end, Color color, bool hitPlayer, bool hitSomething = true)
    {
        Instance.Spawn(start, end, color, hitPlayer, hitSomething);
    }

    /// Makes an existing LineRenderer (e.g. the gun's aim line) thin, soft and unlit.
    public static void StyleLine(LineRenderer line, float width)
    {
        if (!line) return;
        line.sharedMaterial = LineMaterial;
        line.textureMode = LineTextureMode.Stretch;
        line.widthMultiplier = width;
        line.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
        line.numCapVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.alignment = LineAlignment.View;
    }

    void Spawn(Vector3 start, Vector3 end, Color color, bool hitPlayer, bool hitSomething)
    {
        Shot s = pool.Count > 0 ? pool.Pop() : Create();
        Vector3 d = end - start;
        s.start = start;
        s.length = d.magnitude;
        s.dir = s.length > 0.0001f ? d / s.length : Vector3.forward;
        s.t = 0f;
        s.travelTime = s.length / Mathf.Max(1f, boltSpeed);
        s.color = color;
        s.hitPlayer = hitPlayer;
        s.hitSomething = hitSomething;
        s.splatted = false;
        s.trail.enabled = s.bolt.enabled = true;
        s.impact.enabled = false;
        active.Add(s);
        Animate(s);
    }

    Shot Create()
    {
        var s = new Shot();
        s.trail = MakeLine("Trail");
        s.bolt = MakeLine("Bolt");
        var go = new GameObject("Impact");
        go.transform.SetParent(transform, false);
        s.impact = go.AddComponent<SpriteRenderer>();
        s.impact.sprite = GlowSprite;
        s.impact.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return s;
    }

    LineRenderer MakeLine(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        StyleLine(lr, 1f);
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.enabled = false;
        return lr;
    }

    void LateUpdate()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Shot s = active[i];
            s.t += Time.deltaTime;
            if (!Animate(s))
            {
                s.trail.enabled = s.bolt.enabled = s.impact.enabled = false;
                active.RemoveAt(i);
                pool.Push(s);
            }
        }
    }

    // Returns false when the shot effect is completely finished.
    bool Animate(Shot s)
    {
        float head = Mathf.Min(s.length, s.t * boltSpeed);   // how far the bolt has travelled
        Vector3 headPos = s.start + s.dir * head;
        float afterLand = s.t - s.travelTime;                 // < 0 while still flying

        // Trail: from the gun to the bolt, then fades and thins after landing.
        float fade = afterLand <= 0f ? 1f : 1f - afterLand / Mathf.Max(0.01f, trailFade);
        if (fade > 0f)
        {
            Color c = s.color; c.a = trailAlpha * fade * fade;
            Color c0 = c; c0.a *= 0.35f;                     // softer near the gun
            s.trail.startColor = c0;
            s.trail.endColor = c;
            s.trail.widthMultiplier = trailWidth * Mathf.Lerp(0.4f, 1f, fade);
            s.trail.SetPosition(0, s.start);
            s.trail.SetPosition(1, headPos);
        }
        s.trail.enabled = fade > 0f;

        // Bolt: short white-hot streak riding the front of the beam.
        bool flying = afterLand <= 0f;
        s.bolt.enabled = flying;
        if (flying)
        {
            Color hot = Color.Lerp(s.color, Color.white, boltWhiteness);
            Color tail = hot; tail.a = 0f;
            s.bolt.startColor = tail;
            s.bolt.endColor = hot;
            s.bolt.widthMultiplier = boltWidth;
            s.bolt.SetPosition(0, s.start + s.dir * Mathf.Max(0f, head - boltLength));
            s.bolt.SetPosition(1, headPos);
        }

        // Paintball: leave a paint splat on walls / floor / bunkers where the shot lands.
        if (!flying && !s.splatted)
        {
            s.splatted = true;
            if (s.hitSomething && !s.hitPlayer) PaintSplats.Shot(s.start, s.start + s.dir * s.length, s.color);
        }

        // Impact flash at the end point.
        float k = afterLand / impactTime;                     // 0 -> 1
        bool showImpact = s.hitSomething && afterLand >= 0f && k < 1f;
        s.impact.enabled = showImpact;
        if (showImpact)
        {
            Transform cam = LocalRig.Instance && LocalRig.Instance.head ? LocalRig.Instance.head
                : Camera.main ? Camera.main.transform : null;
            Vector3 end = s.start + s.dir * s.length;
            Vector3 pos = cam ? end + (cam.position - end).normalized * 0.03f : end;
            s.impact.transform.position = pos;
            if (cam) s.impact.transform.rotation = Quaternion.LookRotation(pos - cam.position, cam.up);
            float size = impactSize * (s.hitPlayer ? 1.8f : 1f) * Mathf.Lerp(0.6f, 1.3f, k);
            s.impact.transform.localScale = Vector3.one * size;
            Color c = Color.Lerp(s.color, Color.white, s.hitPlayer ? 0.25f : 0.1f);
            c.a = 1f - k;
            s.impact.color = c;
        }

        return fade > 0f || flying || showImpact;
    }

    // ---------- textures built in code ----------

    // Across the line's width: a narrow bright core with a soft glow falling off to fully transparent edges.
    static Texture2D BuildLineTexture()
    {
        const int h = 64;
        var tex = new Texture2D(4, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "LaserLine" };
        var px = new Color32[4 * h];
        for (int y = 0; y < h; y++)
        {
            float d = Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f;         // 0 centre -> 1 edge
            float core = Mathf.Clamp01(1f - d / 0.3f);
            float glow = Mathf.Exp(-d * d * 7f) * (1f - d);
            float a = Mathf.Clamp01(core * core * 0.8f + glow * 0.55f);
            float white = Mathf.Lerp(0.75f, 1f, core);                // centre slightly brighter
            byte g = (byte)(255f * white);
            for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color32(g, g, g, (byte)(255f * a));
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    static Sprite GlowSprite
    {
        get
        {
            if (glowSprite != null) return glowSprite;
            const int r = 64;
            var tex = new Texture2D(r, r, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "LaserImpact" };
            var px = new Color32[r * r];
            float c = (r - 1) * 0.5f;
            for (int y = 0; y < r; y++)
                for (int x = 0; x < r; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;   // 0 centre -> 1 edge
                    float a = Mathf.Clamp01(Mathf.Exp(-d * d * 6f) * (1f - d) + Mathf.Clamp01(1f - d / 0.18f));
                    px[y * r + x] = new Color32(255, 255, 255, (byte)(255f * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            glowSprite = Sprite.Create(tex, new Rect(0, 0, r, r), new Vector2(0.5f, 0.5f), r);
            return glowSprite;
        }
    }
}
