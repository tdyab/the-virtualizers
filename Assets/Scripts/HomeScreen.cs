using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

// "How to join" page shown before the headset is connected.
// Creates itself at runtime and is head-locked like MatchHUD.
public class HomeScreen : MonoBehaviour {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create() {
        var go = new GameObject("HomeScreen");
        go.AddComponent<HomeScreen>();
        DontDestroyOnLoad(go);
    }

    public float distance = 1.5f;

    Canvas canvas;
    TMP_Text title, body, status;
    NetworkBootstrap bootstrap;
    bool wasPressed;

    void Awake() {
        var cgo = new GameObject("HomeCanvas", typeof(RectTransform));
        cgo.transform.SetParent(transform, false);
        canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)cgo.transform;
        rt.sizeDelta = new Vector2(1000, 700);
        rt.localScale = Vector3.one * 0.001f;

        var bg = new GameObject("Background", typeof(RectTransform));
        bg.transform.SetParent(canvas.transform, false);
        var img = bg.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.85f);
        img.raycastTarget = false;
        var brt = (RectTransform)bg.transform;
        brt.sizeDelta = new Vector2(1000, 700);

        title  = MakeText("Title",  new Vector2(0, 290), new Vector2(900, 80), 56, TextAlignmentOptions.Center);
        body   = MakeText("Body",   new Vector2(0, 20),  new Vector2(900, 440), 34, TextAlignmentOptions.TopLeft);
        status = MakeText("Status", new Vector2(0, -290), new Vector2(900, 90), 32, TextAlignmentOptions.Center);

        title.text = "<b>VR LASER TAG</b>";
        body.text =
            "<b>How to join</b>\n\n" +
            "1. All headsets on the same Wi-Fi with internet.\n\n" +
            "2. Open the app on ONE headset first.\n    Wait until it says HOST.\n\n" +
            "3. Open the app on the other headsets.\n    They join the host automatically (max 5).\n\n" +
            "4. The lobby shows how many players joined.\n    The HOST presses A + X to start (2+ players).";
    }

    TMP_Text MakeText(string name, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions align) {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = fontSize;
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        t.rectTransform.sizeDelta = size;
        t.rectTransform.anchoredPosition = pos;
        return t;
    }

    static bool Trigger(XRNode node) {
        var device = InputDevices.GetDeviceAtXRNode(node);
        return device.isValid
            && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool pressed)
            && pressed;
    }

    void LateUpdate() {
        // Head-lock to the camera.
        Camera cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();
        if (cam != null && transform.parent != cam.transform) {
            transform.SetParent(cam.transform, false);
            transform.localPosition = new Vector3(0, 0, distance);
            transform.localRotation = Quaternion.identity;
        }

        var nm = NetworkManager.Singleton;
        bool connected = nm != null && nm.IsListening;
        canvas.gameObject.SetActive(!connected);
        if (connected) return;

        if (bootstrap == null) bootstrap = FindFirstObjectByType<NetworkBootstrap>();
        status.text = bootstrap != null
            ? bootstrap.Status + "\n<size=24>Press the right trigger to retry</size>"
            : "Waiting for network...";

        bool pressed = Trigger(XRNode.RightHand);
        if (pressed && !wasPressed && bootstrap != null) bootstrap.ConnectButton();
        wasPressed = pressed;
    }
}