using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

public class LocalRig : MonoBehaviour {
    public static LocalRig Instance {
        get; private set; }
    public XROrigin origin;
    public Transform head;
    public Transform leftHand;
    public Transform rightHand;
    public TeleportationProvider teleportProvider;
    public GameObject deathOverlay;

    [Header("Height safety")]
    public float standingHeight = 1.6f;
    public float minHeadHeight = 0.5f;

    float trackedTime;
    bool heightChecked;
    static readonly RaycastHit[] groundHits = new RaycastHit[8];

    void Awake() {
        Instance = this;
        if (deathOverlay) deathOverlay.SetActive(false);
    }

    public void TeleportTo(Vector3 feetPos, Vector3 faceDir) {
        faceDir.y = 0;
        if (faceDir.sqrMagnitude < 0.001f) faceDir = Vector3.forward;

        HeadCollision.AllowNextJump();
        var cc = origin.GetComponent<CharacterController>();
        if (cc) cc.enabled = false;

        Vector3 o = origin.transform.position;
        origin.transform.position = new Vector3(o.x, GroundHeight(feetPos), o.z);

        origin.MatchOriginUpCameraForward(Vector3.up, faceDir.normalized);
        Vector3 cam = origin.Camera.transform.position;
        origin.MoveCameraToWorldLocation(new Vector3(feetPos.x, cam.y, feetPos.z));

        if (cc) cc.enabled = true;
    }

    float GroundHeight(Vector3 feetPos) {
        int n = Physics.RaycastNonAlloc(feetPos + Vector3.up * 1.5f, Vector3.down, groundHits, 6f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++) {
            var c = groundHits[i].collider;
            if (c.transform.IsChildOf(origin.transform) || c.GetComponentInParent<PlayerState>() != null) continue;
            var r = c.GetComponent<Renderer>();
            if (r == null || !r.enabled) continue;
            if (groundHits[i].point.y > best) best = groundHits[i].point.y; }
        return float.IsNegativeInfinity(best) ? 0f : best;
    }

    void LateUpdate() {
        if (origin == null) return;

        if (origin.transform.position.y < -1f) {
            var cc = origin.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            Vector3 p = origin.transform.position;
            origin.transform.position = new Vector3(p.x, GroundHeight(new Vector3(p.x, 0f, p.z)), p.z);
            if (cc) cc.enabled = true; }

        if (heightChecked || Application.isEditor) return;
        if (origin.CameraInOriginSpaceHeight == 0f) return;
        trackedTime += Time.deltaTime;
        if (trackedTime < 1.5f) return;
        heightChecked = true;
        if (origin.CameraInOriginSpaceHeight < minHeadHeight) {
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = standingHeight; }
    }

    public void SetAlive(bool alive) {
        if (teleportProvider) teleportProvider.enabled = alive;
        if (deathOverlay) deathOverlay.SetActive(!alive); }

}
