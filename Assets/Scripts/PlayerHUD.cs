using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class PlayerHUD : MonoBehaviour
{
    [Header("Old prefab elements (hidden, kept so the prefab still links)")]
    public Image healthFill;
    public TMP_Text healthText;
    public TMP_Text ammoText;

    [Header("Feedback")]
    public Image damageFlash;
    public float flashAlpha = 0.45f;
    public float flashFadeSpeed = 2.5f;
    public TMP_Text respawnText;

    [System.NonSerialized] public float distance = 1.2f;
    [System.NonSerialized] public Vector2 healthAngle = new Vector2(-22f, -14f);
    [System.NonSerialized] public Vector2 ammoAngle = new Vector2(-22f, -19.6f);

    const int Segments = 10;
    const float BarWidth = 210f, BarHeight = 14f;
    static readonly Color Ink = new Color(0.07f, 0.07f, 0.10f, 0.93f);
    static readonly Color Track = new Color(1f, 1f, 1f, 0.10f);
    static readonly Color Lime = new Color(0.22f, 0.62f, 0.16f);
    static readonly Color Orange = new Color(1f, 0.42f, 0.07f);
    static readonly Color Pink = new Color(1f, 0.24f, 0.5f);

    PlayerState me;
    LaserGun gun;
    int lastHealth;
    float deadSince = -1f;
    float shownHealth = 1f;

    Transform hudRoot;
    Canvas canvas;
    Material uiMat, textMat, numberMat;
    RectTransform healthCard, ammoCard, healthBarFill;
    Image healthRim, ammoRim, healthBadge, ammoBadge, healthFillImg;
    TMP_Text healthNumber, ammoNumber, ammoLabel, splatText;
    GameObject splatRoot;
    RectTransform splatBlob;
    Canvas splatCanvas;
    readonly Image[] segments = new Image[Segments];

    void Awake()
    {
        if (healthFill) healthFill.gameObject.SetActive(false);
        if (healthText) healthText.gameObject.SetActive(false);
        if (ammoText) ammoText.gameObject.SetActive(false);
        if (respawnText) respawnText.gameObject.SetActive(false);
        Build();
    }

    void OnDestroy() { if (hudRoot) Destroy(hudRoot.gameObject); }

    void Build()
    {
        hudRoot = new GameObject("PlayerVitals").transform;
        DontDestroyOnLoad(hudRoot.gameObject);
        var cgo = new GameObject("VitalsCanvas", typeof(RectTransform));
        cgo.transform.SetParent(hudRoot, false);
        canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 12;
        ((RectTransform)cgo.transform).sizeDelta = new Vector2(2000, 1400);
        uiMat = new Material(Canvas.GetDefaultCanvasMaterial());
        uiMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);

        healthCard = Card("Health", out healthRim);
        healthBadge = Badge(healthCard, MatchHUD.CircleSprite, Pink);
        var plus = Text("Plus", healthBadge.rectTransform, new Vector2(0, 1), new Vector2(44, 44), 34, TextAlignmentOptions.Center);
        plus.text = "<b>+</b>";
        Caption(healthCard, "HEALTH");
        var track = Pill("Track", healthCard, new Vector2(-6, -10), new Vector2(BarWidth, BarHeight), Track);
        healthBarFill = Pill("Fill", track.rectTransform, Vector2.zero, new Vector2(BarWidth, BarHeight), Lime).rectTransform;
        healthBarFill.anchorMin = healthBarFill.anchorMax = healthBarFill.pivot = new Vector2(0f, 0.5f);
        healthBarFill.anchoredPosition = Vector2.zero;
        healthFillImg = healthBarFill.GetComponent<Image>();
        healthNumber = Number(healthCard);

        ammoCard = Card("Ammo", out ammoRim);
        ammoBadge = Badge(ammoCard, PaintSplats.Splat(2), Orange);
        float seg = BarWidth / Segments;
        for (int i = 0; i < Segments; i++)
            segments[i] = Pill("Seg" + i, ammoCard, new Vector2(-6 - BarWidth / 2f + seg * (i + 0.5f), -10), new Vector2(seg - 3f, BarHeight), Orange);
        Caption(ammoCard, "AMMO");
        ammoNumber = Number(ammoCard);
        var scgo = new GameObject("SplatCanvas", typeof(RectTransform));
        scgo.transform.SetParent(hudRoot, false);
        splatCanvas = scgo.AddComponent<Canvas>();
        splatCanvas.renderMode = RenderMode.WorldSpace;
        splatCanvas.sortingOrder = 13;
        ((RectTransform)scgo.transform).sizeDelta = new Vector2(2000, 1400);
        splatRoot = new GameObject("Respawn", typeof(RectTransform));
        splatRoot.transform.SetParent(scgo.transform, false);
        splatBlob = Panel("Blob", splatRoot.transform, new Vector2(0, 20), new Vector2(520, 520));
        var blobImg = splatBlob.GetComponent<Image>();
        blobImg.sprite = PaintSplats.Splat(5);
        blobImg.preserveAspect = true;
        splatText = Text("Text", splatRoot.transform, new Vector2(0, 10), new Vector2(700, 260), 90, TextAlignmentOptions.Center);
        var outlined = new Material(splatText.fontSharedMaterial);
        outlined.EnableKeyword(ShaderUtilities.Keyword_Outline);
        outlined.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.2f);
        outlined.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(20, 18, 28, 255));
        outlined.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
        splatText.fontSharedMaterial = outlined;
        splatRoot.SetActive(false);

        ammoLabel = HudStyle.Txt("Label", ammoCard, new Vector2(-6, 14), new Vector2(BarWidth, 22), 14, TextAlignmentOptions.Right);
        ammoLabel.characterSpacing = 12f;
        ammoLabel.fontStyle = FontStyles.Bold;
    }

    void Caption(RectTransform card, string label)
    {
        HudStyle.Caption(label, card, new Vector2(-6, 14), new Vector2(BarWidth, 22), TextAlignmentOptions.Left);
    }

    TMP_Text Number(RectTransform card)
    {
        return HudStyle.Txt("Number", card, new Vector2(146, 0), new Vector2(76, 56), 34, TextAlignmentOptions.Center, true);
    }

    RectTransform Card(string name, out Image rim)
    {
        var glass = HudStyle.Panel(name, canvas.transform, Vector2.zero, new Vector2(390, 78), Orange);
        rim = HudStyle.Accent(glass);
        return glass;
    }

    Image Badge(RectTransform card, Sprite sprite, Color color)
    {
        var rt = Panel("Badge", card, new Vector2(-146, 0), new Vector2(44, 44));
        var img = rt.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.preserveAspect = true;
        return img;
    }

    Image Pill(string name, Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        var rt = Panel(name, parent, pos, size);
        var img = rt.GetComponent<Image>();
        img.sprite = MatchHUD.RoundedSpriteShared;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 24f / Mathf.Max(1f, Mathf.Min(size.x, size.y) * 0.5f);
        img.color = color;
        return img;
    }

    RectTransform Panel(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
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

    static Vector2 Deg(Vector2 a) => new Vector2(Mathf.Tan(a.x * Mathf.Deg2Rad), Mathf.Tan(a.y * Mathf.Deg2Rad)) * 1000f;

    void LateUpdate()
    {
        var anchor = HudAnchor.Get();
        if (anchor != null && hudRoot.parent != anchor.transform)
        {
            hudRoot.SetParent(anchor.transform, false);
            hudRoot.localRotation = Quaternion.identity;
        }
        hudRoot.localPosition = new Vector3(0, 0, distance);
        canvas.transform.localScale = Vector3.one * (distance * 0.001f);
        splatCanvas.transform.localScale = Vector3.one * (distance * 0.001f);
        ((RectTransform)healthCard.parent).anchoredPosition = Deg(healthAngle);
        ((RectTransform)ammoCard.parent).anchoredPosition = Deg(ammoAngle);
    }

    void Update()
    {
        if (PlayerState.Local != me) Bind(PlayerState.Local);
        var gm = GameManager.Instance;
        bool inMatch = gm != null && gm.IsSpawned
            && (gm.State.Value == MatchState.Playing || gm.State.Value == MatchState.Countdown);
        bool show = me != null && inMatch && !SettingsMenu.IsOpen;
        canvas.gameObject.SetActive(show);
        if (me == null) { splatRoot.SetActive(false); return; }

        Color mine = me.PlayerColor;
        healthRim.color = mine;
        ammoRim.color = mine;
        ammoBadge.color = mine;
        healthBadge.color = Pink;

        int hp = me.Health.Value;
        float t = hp / (float)PlayerState.MaxHealth;
        shownHealth = Mathf.Lerp(shownHealth, t, Time.deltaTime * 10f);
        healthBarFill.sizeDelta = new Vector2(Mathf.Max(BarHeight, BarWidth * shownHealth), BarHeight);
        healthFillImg.enabled = hp > 0;
        Color hc = t > 0.6f ? Lime : t > 0.3f ? Orange : Pink;
        if (t <= 0.3f && hp > 0) hc = Color.Lerp(hc, Color.white, 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 10f)));
        healthFillImg.color = hc;
        healthNumber.text = $"<b>{hp}</b>";
        healthNumber.color = t > 0.3f ? HudStyle.Text : Pink;


        if (damageFlash)
        {
            if (hp < lastHealth) SetFlashAlpha(flashAlpha);
            var c = damageFlash.color;
            c.a = Mathf.MoveTowards(c.a, 0f, flashFadeSpeed * Time.deltaTime);
            damageFlash.color = c;
        }
        lastHealth = hp;

        float perShot = gun ? Mathf.Max(0.01f, gun.chargePerShot) : 1f;
        int shots = Mathf.FloorToInt(me.Charge.Value / perShot);
        float frac = Mathf.Clamp01(me.Charge.Value / PlayerState.MaxCharge);
        bool low = frac < 0.2f;
        float blink = low ? 0.55f + 0.45f * Mathf.Sin(Time.time * 9f) : 1f;
        for (int i = 0; i < Segments; i++)
        {
            float fill = Mathf.Clamp01(frac * Segments - i);
            Color full = low ? Pink : mine;
            Color sc = Color.Lerp(Track, full, fill);
            sc.a = fill > 0f ? Mathf.Lerp(0.6f, 1f, fill) * blink + (1f - blink) * 0.3f : 1f;
            segments[i].color = sc;
        }
        ammoNumber.text = $"<b>{shots}</b>";
        ammoNumber.color = shots == 0 ? Pink : HudStyle.Text;
        ammoLabel.text = shots == 0 ? "<color=#FF3D7F>EMPTY</color>" : low ? "<color=#FF3D7F>LOW</color>" : "";

        if (!me.IsAlive.Value)
        {
            if (deadSince < 0f) deadSince = Time.time;
            float since = Time.time - deadSince;
            int left = Mathf.CeilToInt(me.respawnDelay - since);
            splatRoot.SetActive(true);
            float pop = Mathf.Clamp01(since / 0.25f);
            splatBlob.localScale = Vector3.one * (0.4f + 0.6f * (1f - (1f - pop) * (1f - pop)));
            splatBlob.localEulerAngles = new Vector3(0, 0, -12f);
            splatBlob.GetComponent<Image>().color = new Color(1f, 0.24f, 0.5f, 0.92f);
            splatText.text = left > 0
                ? $"<b>SPLAT!</b>\n<size=40%>BACK IN <color=#FFD54A>{left}</color></size>"
                : "<b>SPLAT!</b>\n<size=40%>RESPAWNING...</size>";
        }
        else
        {
            deadSince = -1f;
            splatRoot.SetActive(false);
        }
    }

    void Bind(PlayerState s)
    {
        me = s;
        gun = s ? s.GetComponent<LaserGun>() : null;
        lastHealth = s ? s.Health.Value : 0;
        shownHealth = 1f;
        deadSince = -1f;
    }

    void SetFlashAlpha(float a)
    {
        var c = damageFlash.color;
        c.a = a;
        damageFlash.color = c;
    }
}
