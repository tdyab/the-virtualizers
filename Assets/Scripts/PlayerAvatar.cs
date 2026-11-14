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

    [Header("Guns (one per player colour, same order as PlayerColors)")]
    public GameObject[] gunPrefabs;
    public float gunLength = 0.7f;
    public Transform muzzle;

    [Header("Visuals")]
    public Renderer[] colorRenderers;
    public Renderer[] hideForOwner;
    public GameObject[] hideWhenDead;

    Color baseColor = Color.white;
    GameObject gun;
    Material gunMaterial;
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
        foreach (var r in colorRenderers) if (r) r.material.color = baseColor;
        BuildGun(Mathf.Abs(index)); }

    void BuildGun(int index) {
        if (gunPrefabs == null || gunPrefabs.Length == 0 || rightHand == null) return;
        GameObject prefab = gunPrefabs[index % gunPrefabs.Length];
        if (prefab == null) return;
        if (gun) Destroy(gun);

        gun = Instantiate(prefab);
        gun.name = "Gun";
        foreach (var c in gun.GetComponentsInChildren<Collider>()) Destroy(c);

        var filter = gun.GetComponentInChildren<MeshFilter>();
        Bounds b = filter ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        bool barrelForward = b.max.z > -b.min.z;
        float length = Mathf.Max(0.01f, b.size.z);
        float scale = gunLength / length;

        gun.transform.SetParent(rightHand, false);
        Vector3 hs = rightHand.lossyScale;
        gun.transform.localScale = new Vector3(scale / hs.x, scale / hs.y, scale / hs.z);
        gun.transform.localRotation = barrelForward ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
        gun.transform.localPosition = Vector3.zero;

        if (gunMaterial == null) {
            var src = gun.GetComponentInChildren<Renderer>().sharedMaterial;
            gunMaterial = new Material(src) { name = "Gun (player colour)" }; }
        gunMaterial.SetTexture("_BaseMap", null);
        gunMaterial.SetColor("_BaseColor", baseColor);
        gunMaterial.SetFloat("_Smoothness", 0.65f);
        gunMaterial.EnableKeyword("_EMISSION");
        gunMaterial.SetColor("_EmissionColor", baseColor * 0.25f);
        foreach (var r in gun.GetComponentsInChildren<Renderer>()) {
            r.sharedMaterial = gunMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }

        var handRenderer = rightHand.GetComponent<Renderer>();
        if (handRenderer) handRenderer.enabled = false;

        if (muzzle != null) {
            float tip = barrelForward ? b.max.z : -b.min.z;
            muzzle.position = rightHand.position + rightHand.forward * (tip * scale + 0.02f) + rightHand.up * (b.center.y * scale); }
    }

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
