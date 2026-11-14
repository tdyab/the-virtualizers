using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerAvatar : NetworkBehaviour {
    public static readonly Color[] PlayerColors = {
        // Paintball paint: same colours as the lobby splats.
        new Color(1.00f, 0.42f, 0.07f), new Color(1.00f, 0.24f, 0.50f), new Color(0.12f, 0.78f, 1.00f),
        new Color(0.55f, 0.89f, 0.17f), new Color(1.00f, 0.86f, 0.10f) };
    public static readonly string[] PlayerNames = { "Orange", "Pink", "Blue", "Green", "Yellow" };

    [Header("Synced parts (each has OwnerNetworkTransform)")]
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;

    [Header("Computed locally (not synced)")]
    public Transform body;
    public float bodyDrop = 0.75f;

    [Header("Visuals")]
    public Renderer[] colorRenderers;
    public Renderer[] hideForOwner;
    public GameObject[] hideWhenDead;

    Color baseColor = Color.white;
    Coroutine flashRoutine;

    public override void OnNetworkSpawn() {
        if (IsOwner)
            foreach (var r in hideForOwner) if (r) r.enabled = false; }

    void LateUpdate() {
        if (IsOwner && LocalRig.Instance != null) {
            var rig = LocalRig.Instance;
            head.SetPositionAndRotation(rig.head.position, rig.head.rotation);
            leftHand.SetPositionAndRotation(rig.leftHand.position, rig.leftHand.rotation);
            rightHand.SetPositionAndRotation(rig.rightHand.position, rig.rightHand.rotation);
        }

        Vector3 forward = head.forward;
        forward.y = 0;
        if (forward.sqrMagnitude < 0.001f) forward = body.forward;
        body.SetPositionAndRotation(head.position + Vector3.down * bodyDrop, Quaternion.LookRotation(forward.normalized, Vector3.up));
    }

    public void ApplyColor(int index) {
        baseColor = PlayerColors[Mathf.Abs(index) % PlayerColors.Length];
        foreach (var r in colorRenderers) if (r) r.material.color = baseColor; }

    public void SetVisible(bool visible) {
        foreach (var go in hideWhenDead) if (go) go.SetActive(visible); }

    public void Flash() {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine()); }

    IEnumerator FlashRoutine() {
        foreach (var r in colorRenderers) if (r) r.material.color = Color.white;
        yield return new WaitForSeconds(0.12f);
        foreach (var r in colorRenderers) if (r) r.material.color = baseColor;
    }
}
