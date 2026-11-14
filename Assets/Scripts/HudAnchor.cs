using UnityEngine;

// A point that sits on the player's head and turns with it, a little lazily ("smooth follow").
// MatchHUD and HomeScreen parent their canvases to it.
//  * Position snaps to the head every frame, so the HUD never drifts away when you walk or teleport.
//  * Rotation eases towards where you look, so small head movements don't drag the HUD around.
//    Big jumps (teleport, respawn facing) snap instantly.
// Creates itself at runtime, so no scene changes are needed.
[DefaultExecutionOrder(200)]   // after the XR rig and the camera have moved this frame
public class HudAnchor : MonoBehaviour {
    public static HudAnchor Instance { get; private set; }

    [Tooltip("Off = rigid head lock. On = the HUD lags slightly behind head rotation.")]
    public bool smoothFollow = true;
    [Tooltip("How fast the HUD catches up with your view. Higher = stiffer. 4 to 10 feels good.")]
    public float followSpeed = 6f;
    [Tooltip("If you turn further than this in one frame (teleport, respawn), snap instead of easing.")]
    public float snapAngle = 60f;
    [Tooltip("Ignore head roll (tilting your head sideways) so text stays level.")]
    public bool keepLevel = true;

    Transform cam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create() {
        if (Instance != null) return;
        var go = new GameObject("HudAnchor");
        go.AddComponent<HudAnchor>();
        DontDestroyOnLoad(go);
    }

    public static HudAnchor Get() {
        if (Instance == null) Create();
        return Instance;
    }

    void Awake() { Instance = this; }
    void OnEnable() { Application.onBeforeRender += FollowPosition; }
    void OnDisable() { Application.onBeforeRender -= FollowPosition; }

    Transform FindCamera() {
        if (Camera.main != null) return Camera.main.transform;
        var any = FindFirstObjectByType<Camera>();
        return any != null ? any.transform : null;
    }

    Quaternion TargetRotation() {
        if (!keepLevel) return cam.rotation;
        Vector3 f = cam.forward;
        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(f, up)) > 0.99f) up = cam.up;   // looking straight up or down
        return Quaternion.LookRotation(f, up);
    }

    void LateUpdate() {
        if (cam == null) {
            cam = FindCamera();
            if (cam == null) return;
            transform.SetPositionAndRotation(cam.position, TargetRotation());
        }

        Quaternion target = TargetRotation();
        if (!smoothFollow || Quaternion.Angle(transform.rotation, target) > snapAngle)
            transform.rotation = target;
        else
            transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));

        transform.position = cam.position;
    }

    // The headset updates the camera once more just before rendering; follow it so the HUD doesn't jitter.
    void FollowPosition() {
        if (cam != null) transform.position = cam.position;
    }
}
