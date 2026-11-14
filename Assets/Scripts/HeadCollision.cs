using UnityEngine;

public class HeadCollision : MonoBehaviour
{
    [Tooltip("Radius around the head that can't enter walls or obstacles (metres).")]
    public float headRadius = 0.2f;
    [Tooltip("Head jumps larger than this in one frame are treated as teleports.")]
    public float teleportJump = 1.0f;

    static readonly Collider[] overlaps = new Collider[16];
    static readonly RaycastHit[] sweep = new RaycastHit[16];

    [Tooltip("How close (metres, horizontally) two players' heads can get.")]
    public float playerSpacing = 0.6f;

    [Tooltip("Body below this height above your feet is ignored (so the floor and pads don't block).")]
    public float stepHeight = 0.15f;

    [Tooltip("You can teleport over anything lower than this (metres above the floor). Taller things block the teleport.")]
    public float teleportOverHeight = 1.15f;

    Vector3 lastSafe, lastOriginPos;
    Quaternion lastOriginRot;
    bool hasSafe;
    static bool skipNext;

    public static void AllowNextJump() { skipNext = true; }

    static HeadCollision instance;
    readonly System.Collections.Generic.HashSet<Object> filtered = new System.Collections.Generic.HashSet<Object>();
    TeleportPathFilter filter;
    float nextFilterScan;

    public static bool CanTeleportTo(Vector3 targetFeet)
    {
        if (instance == null) return true;
        var rig = LocalRig.Instance;
        if (rig == null || rig.origin == null || rig.head == null) return true;
        Transform origin = rig.origin.transform;
        Vector3 head = rig.head.position;
        float feet = origin.position.y;
        Vector3 toHead = new Vector3(targetFeet.x, targetFeet.y + (head.y - feet), targetFeet.z);
        return !instance.TeleportBlocked(feet, head, targetFeet.y, toHead, origin);
    }

    void ScanTeleportTargets()
    {
        if (Time.unscaledTime < nextFilterScan) return;
        nextFilterScan = Time.unscaledTime + 1f;
        if (filter == null) filter = new TeleportPathFilter();
        foreach (var t in FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.BaseTeleportationInteractable>(FindObjectsSortMode.None))
        {
            if (t is UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor) continue;
            if (!filtered.Add(t)) continue;
            t.hoverFilters.Add(filter);
        }
    }

    class TeleportPathFilter : UnityEngine.XR.Interaction.Toolkit.Filtering.IXRHoverFilter
    {
        public bool canProcess => true;

        public bool Process(UnityEngine.XR.Interaction.Toolkit.Interactors.IXRHoverInteractor interactor,
                            UnityEngine.XR.Interaction.Toolkit.Interactables.IXRHoverInteractable interactable)
        {
            if (interactor is UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor ray
                && ray.TryGetCurrent3DRaycastHit(out RaycastHit hit))
                return CanTeleportTo(hit.point);
            return true;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<HeadCollision>() != null) return;
        var go = new GameObject("HeadCollision");
        go.AddComponent<HeadCollision>();
        DontDestroyOnLoad(go);
    }

    void Awake() { instance = this; }

    void LateUpdate()
    {
        ScanTeleportTargets();
        var rig = LocalRig.Instance;
        if (rig == null || rig.origin == null || rig.head == null) { hasSafe = false; return; }
        Transform origin = rig.origin.transform;
        Vector3 head = rig.head.position;

        if (!hasSafe || skipNext)
        {
            skipNext = false;
            if (!Blocked(head, origin.position.y, origin)) Remember(head, origin);
            return;
        }

        if (Flat(head - lastSafe).magnitude > teleportJump)
        {
            bool viaLadder = NearAnchor(head, 0.9f);
            if (!viaLadder && (Blocked(head, origin.position.y, origin)
                               || TeleportBlocked(lastOriginPos.y, lastSafe, origin.position.y, head, origin)))
            {
                var c = origin.GetComponent<CharacterController>();
                if (c) c.enabled = false;
                origin.SetPositionAndRotation(lastOriginPos, lastOriginRot);
                if (c) c.enabled = true;
                return;
            }
            Remember(head, origin);
            return;
        }

        if (!Blocked(head, origin.position.y, origin) && !Crossed(lastSafe, head, origin.position.y, origin)
            && !PlayerAt(head))
        {
            Remember(head, origin);
            return;
        }

        Vector3 back = Flat(lastSafe - head);
        var cc = origin.GetComponent<CharacterController>();
        if (cc) cc.enabled = false;
        origin.position += back;
        if (cc) cc.enabled = true;
    }

    void Remember(Vector3 head, Transform origin)
    {
        lastSafe = head;
        lastOriginPos = origin.position;
        lastOriginRot = origin.rotation;
        hasSafe = true;
    }

    bool TeleportBlocked(float fromFeet, Vector3 fromHead, float toFeet, Vector3 toHead, Transform origin)
    {
        if (PlayerAt(toHead)) return true;
        float feet = Mathf.Max(fromFeet, toFeet) + Mathf.Max(0f, teleportOverHeight - stepHeight);
        return Sweep(fromHead, toHead, feet, Mathf.Max(fromHead.y, toHead.y), headRadius * 0.75f, origin);
    }

    static bool NearAnchor(Vector3 pos, float radius)
    {
        foreach (var a in FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor>(FindObjectsSortMode.None))
        {
            Vector3 d = a.transform.position - pos;
            if (Flat(d).magnitude < radius && Mathf.Abs(d.y) < 2.5f) return true;
        }
        return false;
    }

    bool PlayerAt(Vector3 head)
    {
        var me = PlayerState.Local;
        foreach (var p in PlayerState.All)
        {
            if (p == null || p == me || !p.IsAlive.Value) continue;
            Vector3 other = p.HeadPosition;
            if (Mathf.Abs(other.y - head.y) > 1.2f) continue;
            if (Flat(other - head).magnitude < playerSpacing)
            {
                if (lastSafe != Vector3.zero && Flat(other - lastSafe).magnitude < playerSpacing) return false;
                return true;
            }
        }
        return false;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    void Body(Vector3 head, float feet, float headY, float r, out Vector3 bottom, out Vector3 top)
    {
        float low = feet + stepHeight + r;
        top = new Vector3(head.x, Mathf.Max(headY, low), head.z);
        bottom = new Vector3(head.x, Mathf.Min(low, top.y), head.z);
    }

    bool Sweep(Vector3 from, Vector3 to, float feet, float headY, float r, Transform origin)
    {
        Vector3 d = Flat(to - from);
        float len = d.magnitude;
        if (len < 0.0001f) return false;
        Body(from, feet, headY, r, out Vector3 b, out Vector3 t);
        int n = Physics.CapsuleCastNonAlloc(b, t, r, d / len, sweep, len, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (Solid(sweep[i].collider, origin)) return true;
        return false;
    }

    bool Blocked(Vector3 pos, float feet, Transform origin)
    {
        Body(pos, feet, pos.y, headRadius, out Vector3 b, out Vector3 t);
        int n = Physics.OverlapCapsuleNonAlloc(b, t, headRadius, overlaps, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (Solid(overlaps[i], origin)) return true;
        return false;
    }

    bool Crossed(Vector3 from, Vector3 to, float feet, Transform origin)
    {
        return Sweep(from, to, feet, Mathf.Max(from.y, to.y), headRadius * 0.9f, origin);
    }

    static bool Solid(Collider c, Transform origin)
    {
        if (c.transform.IsChildOf(origin)) return false;
        if (c.GetComponentInParent<PlayerState>() != null) return false;
        if (c.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.BaseTeleportationInteractable>() != null
            && c.GetComponent<Renderer>() is Renderer fr && fr.bounds.size.y < 0.3f) return false;
        var r = c.GetComponent<Renderer>();
        return r != null && r.enabled;
    }
}
