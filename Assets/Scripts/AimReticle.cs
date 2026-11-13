using UnityEngine;
using UnityEngine.XR;

/// Circular crosshair (two rings, four lines and a centre dot) for the LOCAL player's laser gun.
///
/// * It sits where the gun is actually pointing (the muzzle raycast) and always faces your eyes,
///   so what you see is exactly where the shot will land. It works in VR and in desktop mode.
/// * It stays the same size on screen however near or far the target is.
/// * It is WHITE normally and turns RED when pointing at a living player (optionally any surface).
/// * It pulses when you fire, shows an X hit marker when a shot does damage, and pulses bigger on a kill.
/// * It fades when you are out of charge and hides while you are dead.
///
/// Setup: none. If no AimReticle is in the scene, one is created automatically when the game starts.
/// To tweak the settings, add this component to an empty GameObject in the Arena scene instead.
/// It only READS LaserGun's aim data (AimHasHit / AimPoint / AimTarget) and PlayerState.
[DefaultExecutionOrder(100)]   // after LaserGun (50), which casts the aim ray this frame
public class AimReticle : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Optional: your own crosshair sprite (white on transparent). Leave empty to use the built-in circle reticle.")]
    public Sprite customSprite;
    public Color normalColor = Color.white;
    public Color targetColor = new Color(1f, 0.15f, 0.15f);
    [Tooltip("Size on screen, in degrees of your field of view.")]
    public float angularSize = 2.2f;
    [Range(0f, 1f)] public float outOfAmmoAlpha = 0.35f;
    public float colorChangeSpeed = 20f;

    [Header("When does it turn red?")]
    [Tooltip("Off: only living players. On: anything the laser would hit (walls, props...).")]
    public bool redOnAnySurface = false;

    [Header("Placement")]
    [Tooltip("How far away the reticle floats when the laser hits nothing.")]
    public float distanceWhenNothingHit = 15f;
    [Tooltip("Pulls the reticle slightly towards you so it never clips into walls.")]
    public float surfaceOffset = 0.03f;

    [Header("Feedback")]
    public float shotPulse = 0.35f;     // extra size for a moment after each shot
    public float killPulse = 0.8f;
    public float pulseRecoverSpeed = 4f;
    public float hitMarkerTime = 0.25f;
    public bool hapticOnTarget = true;  // tiny buzz when the reticle turns red
    [Tooltip("Hide the teammate's thin laser aim line so only the reticle shows.")]
    public bool hideAimLine = false;

    PlayerState me;
    LaserGun gun;
    Transform cam;

    SpriteRenderer reticle;
    SpriteRenderer hitMarker;
    float pulse;            // 0 = normal size
    float hitMarkerUntil;
    bool wasOnTarget;

    // Hit confirmation: remember who we were aiming at when a shot left the gun,
    // then watch whether their health goes down shortly after.
    PlayerState aimedPlayer;
    PlayerState pendingTarget;
    int pendingHealth;
    float pendingUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindAnyObjectByType<AimReticle>() == null)
            new GameObject("AimReticle (auto)").AddComponent<AimReticle>();
    }

    void Awake()
    {
        reticle = MakeSprite("Reticle", customSprite ? customSprite : BuildReticleSprite(), 0);
        hitMarker = MakeSprite("HitMarker", BuildHitMarkerSprite(), 1);
        reticle.transform.SetParent(transform, false);
        hitMarker.transform.SetParent(reticle.transform, false);
        hitMarker.enabled = false;
        reticle.enabled = false;
    }

    void OnDestroy() => Bind(null);

    void LateUpdate()   // after the rig, PlayerAvatar and LaserGun have updated this frame
    {
        if (PlayerState.Local != me) Bind(PlayerState.Local);
        cam = LocalRig.Instance && LocalRig.Instance.head ? LocalRig.Instance.head
            : Camera.main ? Camera.main.transform : null;

        bool show = me != null && gun != null && gun.muzzle != null && cam != null && me.IsAlive.Value;
        reticle.enabled = show;
        if (!show) { hitMarker.enabled = false; wasOnTarget = false; aimedPlayer = null; return; }

        if (hideAimLine && gun.aimLine) gun.aimLine.enabled = false;

        // 1. Where is the gun pointing? LaserGun already cast the aim ray this frame (it runs just before us),
        //    so the reticle, the aim line and the shot all use the exact same ray and the same collider.
        Vector3 origin = gun.muzzle.position;
        Vector3 dir = gun.muzzle.forward;
        bool hitSomething = gun.AimHasHit;
        aimedPlayer = gun.AimTarget;
        bool onTarget = aimedPlayer != null || (redOnAnySurface && hitSomething);

        if (onTarget && !wasOnTarget && hapticOnTarget) Haptics.Pulse(XRNode.RightHand, 0.15f, 0.03f);
        wasOnTarget = onTarget;

        // 2. Place it on the hit point (nudged towards the eyes) and face the eyes.
        Vector3 point = hitSomething ? gun.AimPoint : origin + dir * Mathf.Min(distanceWhenNothingHit, gun.range);
        Vector3 toCam = cam.position - point;
        point += toCam.normalized * Mathf.Min(surfaceOffset, toCam.magnitude * 0.5f);
        float dist = Mathf.Max(0.3f, Vector3.Distance(cam.position, point));

        Transform t = reticle.transform;
        t.SetPositionAndRotation(point, Quaternion.LookRotation(point - cam.position, cam.up));

        // 3. Constant on-screen size, plus the shot / kill pulse.
        pulse = Mathf.MoveTowards(pulse, 0f, pulseRecoverSpeed * Time.deltaTime);
        float size = 2f * dist * Mathf.Tan(angularSize * 0.5f * Mathf.Deg2Rad) * (1f + pulse);
        t.localScale = Vector3.one * size;

        // 4. Colour: red on target, faded when out of charge.
        bool canFire = me.Charge.Value >= gun.chargePerShot;
        Color want = onTarget ? targetColor : normalColor;
        want.a *= canFire ? 1f : outOfAmmoAlpha;
        reticle.color = Color.Lerp(reticle.color, want, 1f - Mathf.Exp(-colorChangeSpeed * Time.deltaTime));

        // 5. Hit marker.
        if (pendingTarget != null)
        {
            if (pendingTarget.Health.Value < pendingHealth || !pendingTarget.IsAlive.Value) { ShowHitMarker(); pendingTarget = null; }
            else if (Time.time > pendingUntil) pendingTarget = null;
        }
        hitMarker.enabled = Time.time < hitMarkerUntil;
        if (hitMarker.enabled)
        {
            float k = (hitMarkerUntil - Time.time) / hitMarkerTime;    // 1 -> 0
            hitMarker.color = new Color(targetColor.r, targetColor.g, targetColor.b, k);
            hitMarker.transform.localScale = Vector3.one * (1.2f - 0.2f * k);
        }
    }

    // ---------- reacting to the networked state ----------

    void Bind(PlayerState s)
    {
        if (me != null)
        {
            me.Charge.OnValueChanged -= OnChargeChanged;
            me.Kills.OnValueChanged -= OnKillsChanged;
        }
        me = s;
        gun = s ? s.GetComponent<LaserGun>() : null;
        pendingTarget = null;
        if (me != null)
        {
            me.Charge.OnValueChanged += OnChargeChanged;
            me.Kills.OnValueChanged += OnKillsChanged;
        }
    }

    // Charge only goes DOWN when the server accepted a shot, so this means "a shot was fired".
    void OnChargeChanged(float oldValue, float newValue)
    {
        if (gun == null || newValue > oldValue - gun.chargePerShot * 0.5f) return;
        pulse = Mathf.Max(pulse, shotPulse);
        if (aimedPlayer != null)
        {
            pendingTarget = aimedPlayer;
            pendingHealth = aimedPlayer.Health.Value;
            pendingUntil = Time.time + 0.5f;
        }
    }

    void OnKillsChanged(int oldValue, int newValue)
    {
        if (newValue <= oldValue) return;
        pulse = Mathf.Max(pulse, killPulse);
        ShowHitMarker();
        hitMarkerUntil = Time.time + hitMarkerTime * 2f;
    }

    void ShowHitMarker()
    {
        hitMarkerUntil = Time.time + hitMarkerTime;
        if (hapticOnTarget) Haptics.Pulse(XRNode.RightHand, 0.4f, 0.04f);
    }

    // ---------- building the sprites in code (no image files needed) ----------

    static SpriteRenderer MakeSprite(string name, Sprite sprite, int order)
    {
        var go = new GameObject(name);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 32000 + order;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        return sr;
    }

    const int Res = 256;

    // Inner ring + outer ring + four lines from the inner ring outwards + centre dot.
    static Sprite BuildReticleSprite() => Draw((x, y) =>
    {
        float r = Mathf.Sqrt(x * x + y * y);
        float d = Mathf.Abs(r - 50f) - 4.5f;                         // inner ring
        d = Mathf.Min(d, Mathf.Abs(r - 84f) - 4.5f);                 // outer ring
        d = Mathf.Min(d, Segment(x, y, 50f, 122f) - 4f);             // horizontal lines
        d = Mathf.Min(d, Segment(y, x, 50f, 122f) - 4f);             // vertical lines
        d = Mathf.Min(d, r - 11f);                                   // centre dot
        return d;
    });

    // Diagonal X ticks, drawn around the reticle when a shot lands.
    static Sprite BuildHitMarkerSprite() => Draw((x, y) =>
    {
        float u = (x + y) * 0.70710678f, v = (x - y) * 0.70710678f;   // rotate 45 degrees
        float d = Mathf.Min(Segment(u, v, 22f, 38f), Segment(v, u, 22f, 38f));
        return d - 4f;
    });

    // Distance to the two segments along the "along" axis between |along| = from..to.
    static float Segment(float along, float across, float from, float to)
    {
        float a = Mathf.Abs(along);
        float off = a < from ? from - a : a > to ? a - to : 0f;
        return Mathf.Sqrt(off * off + across * across);
    }

    // White shape with a soft dark outline so it reads on bright and dark walls.
    static Sprite Draw(System.Func<float, float, float> distance)
    {
        var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
        var px = new Color32[Res * Res];
        float c = (Res - 1) * 0.5f;
        for (int j = 0; j < Res; j++)
            for (int i = 0; i < Res; i++)
            {
                float d = distance(i - c, j - c);
                float fill = Mathf.Clamp01(0.5f - d);
                float outline = Mathf.Clamp01(0.5f - (d - 3f)) * 0.55f;
                float a = Mathf.Max(fill, outline);
                byte g = (byte)(a > 0f ? 255f * fill / a : 0f);
                px[j * Res + i] = new Color32(g, g, g, (byte)(255f * a));
            }
        tex.SetPixels32(px);
        tex.Apply(true, true);
        return Sprite.Create(tex, new Rect(0, 0, Res, Res), new Vector2(0.5f, 0.5f), Res);
    }
}
