using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

public class DesktopMovement : MonoBehaviour
{
    public Transform head;
    public Transform leftController;
    public Transform rightController;

    public float mouseSensitivity = 0.15f;

    [Header("Teleport")]
    public float teleportRange = 20f;
    [Tooltip("Steepest floor you can land on (degrees).")]
    public float maxSlope = 35f;
    public Color markerColor = new Color(0.3f, 0.9f, 1f);

    private float pitch;
    private LineRenderer marker;
    private bool hasTarget;
    private Vector3 target;
    private bool sceneHasTeleportAreas;
    static readonly RaycastHit[] hits = new RaycastHit[16];
    static readonly System.Collections.Generic.IComparer<RaycastHit> byDistance = System.Collections.Generic.Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

    void Start()
    {
        foreach (var move in FindObjectsByType<ContinuousMoveProvider>(FindObjectsSortMode.None))
            move.enabled = false;
        sceneHasTeleportAreas = FindAnyObjectByType<BaseTeleportationInteractable>() != null;

        foreach (var anchor in FindObjectsByType<TeleportationAnchor>(FindObjectsSortMode.None))
        {
            if (anchor.name != "Anchor_Bottom" || anchor.transform.parent == null) continue;
            Transform top = anchor.transform.parent.Find("Anchor_Top");
            if (top != null) anchor.teleportAnchorTransform = top;
        }
    }

    void Update()
    {
        if (Keyboard.current == null || head == null) return;

        if (Mouse.current != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            transform.Rotate(Vector3.up, delta.x * mouseSensitivity);
            pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, -80f, 80f);
            head.localRotation = Quaternion.Euler(pitch, 0, 0);
        }

        hasTarget = false;
        int n = Physics.RaycastNonAlloc(head.position, head.forward, hits, teleportRange, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, 0, n, byDistance);
        for (int i = 0; i < n; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider.GetComponentInParent<PlayerState>() != null || hit.collider.transform.IsChildOf(transform)) continue;
            var rend = hit.collider.GetComponent<Renderer>();
            if (rend == null || !rend.enabled) continue;

            Transform ladder = FindLadder(hit.collider.transform);
            if (ladder != null)
            {
                Transform top = ladder.Find("Anchor_Top"), bottom = ladder.Find("Anchor_Bottom");
                if (top != null && bottom != null)
                {
                    bool upThere = head.position.y > top.position.y + 0.5f;
                    target = upThere ? bottom.position : top.position;
                    hasTarget = true;
                    break;
                }
            }

            bool flat = Vector3.Angle(hit.normal, Vector3.up) <= maxSlope;
            bool allowed = !sceneHasTeleportAreas || hit.collider.GetComponentInParent<BaseTeleportationInteractable>() != null;
            if (flat && allowed) { hasTarget = true; target = hit.point; }
            break;
        }
        UpdateMarker();

        bool teleportPressed = Keyboard.current.tKey.wasPressedThisFrame
            || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        if (teleportPressed && hasTarget)
        {
            Vector3 offset = target - head.position;
            transform.position = new Vector3(transform.position.x + offset.x, target.y, transform.position.z + offset.z);
        }
    }

    static Transform FindLadder(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.name == "Ladder") return t;
        return null;
    }

    void UpdateMarker()
    {
        if (marker == null)
        {
            var go = new GameObject("TeleportMarker");
            marker = go.AddComponent<LineRenderer>();
            LaserTracer.StyleLine(marker, 0.04f);
            marker.alignment = LineAlignment.TransformZ;
            marker.useWorldSpace = false;
            marker.loop = true;
            const int n = 40;
            marker.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                marker.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.35f);
            }
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        marker.enabled = hasTarget;
        if (!hasTarget) return;
        marker.transform.position = target + Vector3.up * 0.02f;
        float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 6f);
        marker.transform.localScale = Vector3.one * pulse;
        marker.startColor = marker.endColor = markerColor;
    }

    void OnDisable()
    {
        if (marker) marker.enabled = false;
    }

    void LateUpdate()
    {
        if (head == null) return;

        if (leftController != null)
        {
            leftController.position = head.TransformPoint(new Vector3(-0.3f, -0.3f, 0.6f));
            leftController.rotation = head.rotation;
        }

        if (rightController != null)
        {
            rightController.position = head.TransformPoint(new Vector3(0.3f, -0.3f, 0.6f));
            rightController.rotation = head.rotation;
        }
    }
}
