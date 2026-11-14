using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class SettingsMenu : MonoBehaviour
{
    public const string OpenBindingVR = "<XRController>{LeftHand}/secondaryButton";
    public const string OpenBindingVRMenu = "<XRController>{LeftHand}/menuButton";
    public const string OpenBindingDesktop = "<Keyboard>/tab";
    public const string SelectBindingVR = "<XRController>{LeftHand}/primaryButton";
    public const string SelectBindingDesktop = "<Keyboard>/enter";
    public const string NavBindingVR = "<XRController>{LeftHand}/primary2DAxis";

    public static bool IsOpen { get; private set; }

    public float distance = 1.1f;

    enum Item { Resume, Volume, Haptics, Restart, EndMatch, Quit }
    static readonly Item[] Items = { Item.Resume, Item.Volume, Item.Haptics, Item.Restart, Item.EndMatch, Item.Quit };

    const string Lime = "#8BE32B", Dim = "#8A8F99";
    static string Orange => HudStyle.MineHex;
    Image menuRim, yButton;
    static readonly Color Ink = new Color(0.07f, 0.07f, 0.10f, 0.95f);

    Canvas canvas;
    GameObject root, pill;
    public Vector2 buttonAngle = new Vector2(-26f, 23f);
    TMP_Text title, list, hint, status;
    Material uiMat, textMat;
    InputAction openAction, selectAction, navAction, upKey, downKey, leftKey, rightKey;
    int selected;
    float nextNav;
    string statusText = "";
    float statusUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<SettingsMenu>() != null) return;
        var go = new GameObject("SettingsMenu");
        go.AddComponent<SettingsMenu>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        AudioListener.volume = PlayerPrefs.GetFloat("volume", 1f);
        Haptics.Enabled = PlayerPrefs.GetInt("haptics", 1) == 1;
        Build();
        SetOpen(false);
    }

    void OnEnable()
    {
        openAction = new InputAction("OpenSettings", InputActionType.Button);
        openAction.AddBinding(OpenBindingVR);
        openAction.AddBinding(OpenBindingVRMenu);
        openAction.AddBinding(OpenBindingDesktop);
        selectAction = new InputAction("SelectSetting", InputActionType.Button);
        selectAction.AddBinding(SelectBindingVR);
        selectAction.AddBinding(SelectBindingDesktop);
        navAction = new InputAction("NavSettings", InputActionType.Value, NavBindingVR);
        upKey = new InputAction("Up", InputActionType.Button, "<Keyboard>/upArrow");
        downKey = new InputAction("Down", InputActionType.Button, "<Keyboard>/downArrow");
        leftKey = new InputAction("Left", InputActionType.Button, "<Keyboard>/leftArrow");
        rightKey = new InputAction("Right", InputActionType.Button, "<Keyboard>/rightArrow");
        foreach (var a in new[] { openAction, selectAction, navAction, upKey, downKey, leftKey, rightKey }) a.Enable();
    }

    void OnDisable()
    {
        foreach (var a in new[] { openAction, selectAction, navAction, upKey, downKey, leftKey, rightKey }) a?.Dispose();
        IsOpen = false;
    }

    void Build()
    {
        var cgo = new GameObject("SettingsCanvas", typeof(RectTransform));
        cgo.transform.SetParent(transform, false);
        canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 20;
        ((RectTransform)cgo.transform).sizeDelta = new Vector2(2000, 1400);

        uiMat = new Material(Canvas.GetDefaultCanvasMaterial());
        uiMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);

        root = new GameObject("Settings", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        var r = (RectTransform)root.transform;

        Splat(r, new Vector2(-330, 250), 200, 0, 20f);
        Splat(r, new Vector2(350, -250), 240, 2, -35f);
        Splat(r, new Vector2(360, 230), 130, 3, 60f);

        var rim = Panel("Rim", r, Vector2.zero, new Vector2(676, 596));
        MatchHUD.Round(rim, Orange);
        menuRim = rim.GetComponent<Image>();
        var panel = Panel("Panel", r, Vector2.zero, new Vector2(660, 580));
        MatchHUD.Round(panel, null);
        panel.GetComponent<Image>().color = Ink;

        title = Text("Title", panel, new Vector2(0, 235), new Vector2(600, 70), 46, TextAlignmentOptions.Center);
        title.text = $"<color={Orange}><b>SETTINGS</b></color>";
        list = Text("List", panel, new Vector2(0, 20), new Vector2(560, 360), 32, TextAlignmentOptions.TopLeft);
        list.lineSpacing = 22f;

        status = Text("Status", panel, new Vector2(0, -190), new Vector2(600, 40), 24, TextAlignmentOptions.Center);
        hint = Text("Hint", panel, new Vector2(0, -245), new Vector2(620, 40), 20, TextAlignmentOptions.Center);
        hint.text = $"<color={Dim}>Thumbstick: move / change    X: select    Y: close</color>";

        var pillGlass = HudStyle.Panel("SettingsButton", canvas.transform, Vector2.zero, new Vector2(214, 58));
        var pillRt = (RectTransform)pillGlass.parent;
        var btn = Panel("YButton", pillGlass, new Vector2(-70, 0), new Vector2(36, 36));
        var btnImg = btn.GetComponent<Image>();
        btnImg.sprite = MatchHUD.CircleSprite;
        btnImg.color = new Color(1f, 0.42f, 0.07f);
        yButton = btnImg;
        var keyText = HudStyle.Txt("Y", btn, new Vector2(0, 1), new Vector2(36, 36), 22, TextAlignmentOptions.Center, true);
        keyText.text = Application.isEditor && !UnityEngine.XR.XRSettings.isDeviceActive ? "<size=55%>TAB</size>" : "Y";
        keyText.color = new Color(0.07f, 0.07f, 0.10f);
        HudStyle.Caption("Settings", pillGlass, new Vector2(26, 0), new Vector2(140, 30), TextAlignmentOptions.Left).color = new Color(1f, 1f, 1f, 0.85f);
        pill = pillRt.gameObject;
    }

    void Splat(Transform parent, Vector2 pos, float size, int colorIndex, float rot)
    {
        var rt = Panel("Splat", parent, pos, new Vector2(size, size));
        var img = rt.GetComponent<Image>();
        img.sprite = PaintSplats.Splat(colorIndex + 1);
        img.color = PlayerAvatar.PlayerColors[colorIndex % PlayerAvatar.PlayerColors.Length];
        img.preserveAspect = true;
        rt.localEulerAngles = new Vector3(0, 0, rot);
    }

    RectTransform Panel(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = Ink;
        img.raycastTarget = false;
        img.material = uiMat;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
    }

    TMP_Text Text(string name, Transform parent, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = fontSize;
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = size;
        t.rectTransform.anchoredPosition = pos;
        if (t.fontSharedMaterial != null)
        {
            if (textMat == null)
            {
                textMat = new Material(t.fontSharedMaterial);
                textMat.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
            }
            t.fontSharedMaterial = textMat;
        }
        return t;
    }

    void SetOpen(bool open)
    {
        IsOpen = open;
        root.SetActive(open);
        if (pill != null) pill.SetActive(!open);
        if (open) { selected = 0; statusText = ""; }
    }

    void Update()
    {
        if (openAction.WasPressedThisFrame()) SetOpen(!IsOpen);
        if (!IsOpen) return;

        int vertical = 0, horizontal = 0;
        Vector2 stick = navAction.ReadValue<Vector2>();
        if (Time.unscaledTime >= nextNav)
        {
            if (stick.y > 0.6f) vertical = -1;
            else if (stick.y < -0.6f) vertical = 1;
            else if (stick.x > 0.6f) horizontal = 1;
            else if (stick.x < -0.6f) horizontal = -1;
            if (vertical != 0 || horizontal != 0) nextNav = Time.unscaledTime + 0.25f;
        }
        if (stick.magnitude < 0.3f) nextNav = 0f;
        if (upKey.WasPressedThisFrame()) vertical = -1;
        if (downKey.WasPressedThisFrame()) vertical = 1;
        if (leftKey.WasPressedThisFrame()) horizontal = -1;
        if (rightKey.WasPressedThisFrame()) horizontal = 1;

        selected = (selected + vertical + Items.Length) % Items.Length;
        if (horizontal != 0) Adjust(Items[selected], horizontal);
        if (selectAction.WasPressedThisFrame()) Activate(Items[selected]);
    }

    void Adjust(Item item, int dir)
    {
        if (item == Item.Volume)
        {
            AudioListener.volume = Mathf.Clamp01(Mathf.Round((AudioListener.volume + dir * 0.1f) * 10f) / 10f);
            PlayerPrefs.SetFloat("volume", AudioListener.volume);
            AudioSource.PlayClipAtPoint(Sfx.HitConfirm, Camera.main ? Camera.main.transform.position : Vector3.zero, 1f);
        }
        else if (item == Item.Haptics) Activate(item);
    }

    void Activate(Item item)
    {
        var gm = GameManager.Instance;
        var nm = NetworkManager.Singleton;
        bool online = gm != null && gm.IsSpawned && nm != null && nm.IsListening;
        bool host = online && nm.IsServer;

        switch (item)
        {
            case Item.Resume:
                SetOpen(false);
                break;
            case Item.Volume:
                Adjust(item, AudioListener.volume >= 1f ? -10 : 1);
                break;
            case Item.Haptics:
                Haptics.Enabled = !Haptics.Enabled;
                PlayerPrefs.SetInt("haptics", Haptics.Enabled ? 1 : 0);
                if (Haptics.Enabled) Haptics.Pulse(UnityEngine.XR.XRNode.LeftHand, 0.6f, 0.1f);
                break;
            case Item.Restart:
                if (!host) { Say("Only the HOST can restart"); break; }
                if (gm.State.Value == MatchState.Countdown) { Say("Match is already starting"); break; }
                if (PlayerState.All.Count < gm.minPlayers) { Say($"Need {gm.minPlayers} players to start"); break; }
                if (gm.State.Value == MatchState.Playing) gm.ForceEndMatch();
                gm.StartOrRestartMatch();
                SetOpen(false);
                break;
            case Item.EndMatch:
                if (!host) { Say("Only the HOST can end the match"); break; }
                if (gm.State.Value != MatchState.Playing) { Say("No match is running"); break; }
                gm.ForceEndMatch();
                SetOpen(false);
                break;
            case Item.Quit:
                PlayerPrefs.Save();
                var bootstrap = FindAnyObjectByType<NetworkBootstrap>();
                if (bootstrap != null) bootstrap.PrepareToQuit();
                if (nm != null && nm.IsListening) nm.Shutdown();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                break;
        }
    }

    void Say(string msg) { statusText = msg; statusUntil = Time.unscaledTime + 2.5f; }

    void LateUpdate()
    {
        var anchor = HudAnchor.Get();
        if (anchor != null && transform.parent != anchor.transform)
        {
            transform.SetParent(anchor.transform, false);
            transform.localRotation = Quaternion.identity;
        }
        transform.localPosition = new Vector3(0, 0, distance);
        canvas.transform.localScale = Vector3.one * (distance * 0.001f);
        if (yButton != null) yButton.color = HudStyle.Mine;
        if (menuRim != null) menuRim.color = HudStyle.Mine;
        if (title != null) title.text = $"<color={Orange}><b>SETTINGS</b></color>";
        if (pill != null)
            ((RectTransform)pill.transform).anchoredPosition =
                new Vector2(Mathf.Tan(buttonAngle.x * Mathf.Deg2Rad), Mathf.Tan(buttonAngle.y * Mathf.Deg2Rad)) * 1000f;
        if (!IsOpen) return;

        var gm = GameManager.Instance;
        var nm = NetworkManager.Singleton;
        bool host = gm != null && gm.IsSpawned && nm != null && nm.IsServer;
        bool playing = gm != null && gm.IsSpawned && gm.State.Value == MatchState.Playing;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Items.Length; i++)
        {
            bool sel = i == selected;
            string label = Items[i] switch
            {
                Item.Resume => "Resume",
                Item.Volume => $"Volume   <color={Orange}>{new string('|', Mathf.RoundToInt(AudioListener.volume * 10))}</color><color=#3A3F4A>{new string('|', 10 - Mathf.RoundToInt(AudioListener.volume * 10))}</color>  {Mathf.RoundToInt(AudioListener.volume * 100)}%",
                Item.Haptics => $"Vibration   {(Haptics.Enabled ? $"<color={Lime}>ON</color>" : $"<color={Dim}>OFF</color>")}",
                Item.Restart => host ? (playing ? "Restart match" : "Start match") : $"<color={Dim}>Restart match (host)</color>",
                Item.EndMatch => host && playing ? "End match" : $"<color={Dim}>End match</color>",
                _ => "Quit game",
            };
            sb.Append(sel ? $"<color={Orange}>></color> <b>{label}</b>" : $"   {label}");
            sb.Append('\n');
        }
        list.text = sb.ToString();

        if (Time.unscaledTime > statusUntil) statusText = "";
        status.text = statusText == "" ? "" : $"<color=#FFD54A>{statusText}</color>";
    }
}
