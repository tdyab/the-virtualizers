using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Start / restart button for the match.
//  * Host in VR: HOLD the start button (A on the right controller) for HoldSeconds.
//  * Editor / desktop: hold A on the keyboard, or press Enter (instant).
//  * Clients pressing it get a message that only the host can start.
// MatchHUD shows the hold progress and the Feedback text, so you can see in the headset that the press registered.
// Creates itself at runtime, so no scene changes are needed.
public class HostStartInput : MonoBehaviour {
    // ===== START BUTTON SETTINGS: the only place to change the start button =====
    public const string ButtonLabel = "A";                                   // used in every on-screen hint
    public const string VRBinding = "<XRController>{RightHand}/primaryButton"; // A on Quest Touch controllers
    public const string DesktopBinding = "<Keyboard>/enter";                 // editor testing
    public const string DesktopHoldBinding = "<Keyboard>/a";                 // editor: hold A on the keyboard too
    public const float HoldSeconds = 1f;                                     // 0 = start on a single press
    // =============================================================================

    public static string HintText => HoldSeconds > 0f ? $"Hold {ButtonLabel}" : $"Press {ButtonLabel}";

    // Read by MatchHUD.
    public static float HoldProgress { get; private set; }       // 0..1 while the host is holding the button
    public static string Feedback { get; private set; } = "";     // last message about the start button
    public static float FeedbackTime { get; private set; } = -99f;

    InputAction vrAction, desktopAction;
    float heldFor;
    bool firedThisHold;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create() {
        var go = new GameObject("HostStartInput");
        go.AddComponent<HostStartInput>();
        DontDestroyOnLoad(go);
    }

    void OnEnable() {
        vrAction = new InputAction("StartMatchVR", InputActionType.Button, VRBinding);
        vrAction.AddBinding(DesktopHoldBinding);   // keyboard A works the same way in the editor
        desktopAction = new InputAction("StartMatchDesktop", InputActionType.Button, DesktopBinding);
        vrAction.Enable();
        desktopAction.Enable();
    }

    void OnDisable() {
        vrAction?.Dispose();
        desktopAction?.Dispose();
    }

    static void Say(string message) {
        Feedback = message;
        FeedbackTime = Time.time;
        Debug.Log("[HostStartInput] " + message);
    }

    void Update() {
        var nm = NetworkManager.Singleton;
        var gm = GameManager.Instance;
        bool ready = nm != null && nm.IsListening && gm != null && gm.IsSpawned;
        if (!ready) { heldFor = 0f; HoldProgress = 0f; return; }

        bool canStartNow = gm.State.Value == MatchState.Lobby || gm.State.Value == MatchState.Ended;
        bool isHost = nm.IsServer;

        // --- VR: hold to start ---
        if (vrAction.WasPressedThisFrame()) {
            heldFor = 0f;
            firedThisHold = false;
            if (canStartNow && !isHost) Say("Only the HOST can start the match");
        }

        if (vrAction.IsPressed() && isHost && canStartNow && !firedThisHold) {
            heldFor += Time.deltaTime;
            HoldProgress = HoldSeconds > 0f ? Mathf.Clamp01(heldFor / HoldSeconds) : 1f;
            if (HoldProgress >= 1f) {
                firedThisHold = true;
                HoldProgress = 0f;
                TryStart(gm);
            }
        } else {
            heldFor = 0f;
            HoldProgress = 0f;
        }

        // --- Desktop: press to start ---
        if (desktopAction.WasPressedThisFrame() && canStartNow) {
            if (isHost) TryStart(gm);
            else Say("Only the HOST can start the match");
        }
    }

    static void TryStart(GameManager gm) {
        int count = PlayerState.All.Count;
        if (count < gm.minPlayers) {
            Say($"Need {gm.minPlayers} players to start ({count}/{gm.minPlayers})");
            return;   // GameManager would refuse too; this just explains why on the host's screen
        }
        Say(gm.State.Value == MatchState.Ended ? "Restarting match..." : "Starting match...");
        gm.StartOrRestartMatch();
    }
}
