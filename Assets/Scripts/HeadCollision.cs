using UnityEngine;

public class HeadCollision : MonoBehaviour
{
    [Tooltip("Radius around the head that can't enter obstacles (metres).")]
    public float headRadius = 0.2f;

    static readonly Collider[] overlaps = new Collider[16];
    SphereCollider probe;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var go = new GameObject("HeadCollision");
        go.AddComponent<HeadCollision>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        probe = gameObject.AddComponent<SphereCollider>();
        probe.isTrigger = true;
        probe.enabled = false;
    }

    void LateUpdate()
    {
        var rig = LocalRig.Instance;
        if (rig == null || rig.origin == null || rig.head == null) return;
        Transform origin = rig.origin.transform;
        Vector3 headPos = rig.head.position;
        probe.radius = headRadius;

        int n = Physics.OverlapSphereNonAlloc(headPos, headRadius, overlaps, ~0, QueryTriggerInteraction.Ignore);
        Vector3 push = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            Collider col = overlaps[i];
            if (col == probe || col.transform.IsChildOf(origin)) continue;
            if (col.GetComponentInParent<PlayerState>() != null) continue;
            var rend = col.GetComponent<Renderer>();
            if (rend == null || !rend.enabled) continue;
            if (Physics.ComputePenetration(probe, headPos, Quaternion.identity,
                                           col, col.transform.position, col.transform.rotation,
                                           out Vector3 dir, out float dist))
            {
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f) push += dir.normalized * dist;
            }
        }
        if (push != Vector3.zero) origin.position += push;
    }
}
