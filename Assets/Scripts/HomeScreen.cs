using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

// "How to join" page shown before the headset is connected.
// Creates itself at runtime and floats in front of the head via HudAnchor, like MatchHUD.
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

    static readonly Color Ink = new Color(0.07f, 0.07f, 0.10f, 0.95f);
    Material uiMat, textMat;
    readonly System.Collections.Generic.List<(RectTransform rt, float rot, float phase)> splats = new System.Collections.Generic.List<(RectTransform, float, float)>();

    void Awake() {
        var cgo = new GameObject("HomeCanvas", typeof(RectTransform));
        cgo.transform.SetParent(transform, false);
        canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 15;
        var rt = (RectTransform)cgo.transform;
        rt.sizeDelta = new Vector2(1400, 1000);
        rt.localScale = Vector3.one * 0.001f;
        uiMat = new Material(Canvas.GetDefaultCanvasMaterial());
        uiMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);

        Splat(new Vector2(-470, 300), 260, 0, 15f);
        Splat(new Vector2(480, -300), 300, 2, -40f);
        Splat(new Vector2(500, 280), 150, 3, 70f);
        Splat(new Vector2(-490, -310), 170, 1, 200f);
        var rim = Panel("Rim", canvas.transform, Vector2.zero, new Vector2(976, 676));
        MatchHUD.Round(rim, "#FF6A13");
        var panel = Panel("Panel", canvas.transform, Vector2.zero, new Vector2(960, 660));
        MatchHUD.Round(panel, null);
        panel.GetComponent<Image>().color = Ink;

        title  = MakeText("Title",  new Vector2(0, 255), new Vector2(900, 90), 64, TextAlignmentOptions.Center);
        body   = MakeText("Body",   new Vector2(0, 10),  new Vector2(820, 360), 30, TextAlignmentOptions.TopLeft);
        status = MakeText("Status", new Vector2(0, -255), new Vector2(880, 90), 30, TextAlignmentOptions.Center);
        body.lineSpacing = 8f;

        title.text = "<b><color=#1FC7FF>VR</color> <color=#FF6A13>LASER</color> <color=#FF3D7F>TAG</color></b>";
        body.text =
            "<color=#FF6A13><b>HOW TO JOIN</b></color>\n\n" +
            "<color=#8BE32B><b>1</b></color>   Every headset needs Wi-Fi with internet.\n\n" +
            "<color=#8BE32B><b>2</b></color>   The first headset to open the game becomes the <b>HOST</b>.\n\n" +
            "<color=#8BE32B><b>3</b></color>   Everyone else joins automatically (up to 5).\n\n" +
            "<color=#8BE32B><b>4</b></color>   HOST: <color=#FFD54A><b>" + HostStartInput.HintText + "</b></color> in the lobby to start.";
    }

    void Splat(Vector2 pos, float size, int colorIndex, float rot) {
        var rt = Panel("Splat", canvas.transform, pos, new Vector2(size, size));
        var img = rt.GetComponent<Image>();
        img.sprite = PaintSplats.Splat(colorIndex + 1);
        img.color = PlayerAvatar.PlayerColors[colorIndex % PlayerAvatar.PlayerColors.Length];
        img.preserveAspect = true;
        rt.localEulerAngles = new Vector3(0, 0, rot);
        splats.Add((rt, rot, colorIndex * 1.7f));
    }

    RectTransform Panel(string name, Transform parent, Vector2 pos, Vector2 size) {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.material = uiMat;
        var r = (RectTransform)go.transform;
        r.sizeDelta = size;
        r.anchoredPosition = pos;
        return r;
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
        if (t.fontSharedMaterial != null) {
            if (textMat == null) {
                textMat = new Material(t.fontSharedMaterial);
                textMat.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            }
            t.fontSharedMaterial = textMat;
        }
        return t;
    }

    static bool Trigger(XRNode node) {
        var device = InputDevices.GetDeviceAtXRNode(node);
        return device.isValid
            && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool pressed)
            && pressed;
    }

    void LateUpdate() {
        // Float in front of the head (HudAnchor does the smooth follow).
        var anchor = HudAnchor.Get();
        if (anchor != null && transform.parent != anchor.transform) {
            transform.SetParent(anchor.transform, false);
            transform.localRotation = Quaternion.identity;
        }
        transform.localPosition = new Vector3(0, 0, distance);

        var nm = NetworkManager.Singleton;
        bool connected = nm != null && nm.IsListening;
        canvas.gameObject.SetActive(!connected);
        if (connected) return;

        float now = Time.time;
        foreach (var (srt, rot, phase) in splats) {
            srt.localScale = Vector3.one * (1f + 0.035f * Mathf.Sin(now * 1.6f + phase));
            srt.localEulerAngles = new Vector3(0, 0, rot + 3f * Mathf.Sin(now * 0.7f + phase));
        }

        if (bootstrap == null) bootstrap = FindFirstObjectByType<NetworkBootstrap>();
        if (bootstrap == null)
            status.text = "Waiting for network...";
        else if (bootstrap.mode == NetworkBootstrap.Mode.Lan)
            // LAN mode only connects with the H / J keys, which a headset doesn't have.
            status.text = "<color=#FF5A4A>Network mode is LAN: this headset can't connect by itself.</color>\n" +
                          "<size=24>Set NetworkBootstrap to Relay Session + Auto Connect for headset builds.</size>";
        else
            status.text = $"<color=#FFD54A>{bootstrap.Status}</color>\n<size=22><color=#8A8F99>Press the right trigger to retry</color></size>";

        bool pressed = Trigger(XRNode.RightHand);
        if (pressed && !wasPressed && bootstrap != null) bootstrap.ConnectButton();
        wasPressed = pressed;
    }
}