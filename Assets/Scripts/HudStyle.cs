using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public static class HudStyle
{
    public static readonly Color Glass = new Color(0.055f, 0.06f, 0.08f, 0.82f);
    public static readonly Color Border = new Color(1f, 1f, 1f, 0.10f);
    public static readonly Color Track = new Color(1f, 1f, 1f, 0.10f);
    public static readonly Color Label = new Color(1f, 1f, 1f, 0.50f);
    public static readonly Color Text = new Color(0.96f, 0.97f, 1f, 1f);
    public static readonly Color Good = new Color(0.22f, 0.62f, 0.16f);
    public static readonly Color Warn = new Color(1f, 0.62f, 0.15f);
    public static readonly Color Danger = new Color(1f, 0.27f, 0.36f);
    public const string LabelHex = "#FFFFFF80";
    public const string DangerHex = "#FF455C";
    public const string WarnHex = "#FF9E26";

    static readonly Color DefaultAccent = new Color(1f, 0.42f, 0.07f);

    public static Color Mine
    {
        get
        {
            var me = PlayerState.Local;
            return me != null && me.SpawnIndex.Value >= 0 ? me.PlayerColor : DefaultAccent;
        }
    }

    public static string MineHex => "#" + ColorUtility.ToHtmlStringRGB(Mine);

    static Material uiMat, textMat, numberMat;

    public static Material UiMat
    {
        get
        {
            if (uiMat == null)
            {
                uiMat = new Material(Canvas.GetDefaultCanvasMaterial()) { name = "HUD UI (on top)" };
                uiMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            }
            return uiMat;
        }
    }

    public static Image Rect(string name, Transform parent, Vector2 pos, Vector2 size, Color color, float radius)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.material = UiMat;
        img.color = color;
        if (radius > 0f)
        {
            img.sprite = MatchHUD.RoundedSpriteShared;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 24f / radius;
        }
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return img;
    }

    public static RectTransform Panel(string name, Transform parent, Vector2 pos, Vector2 size, Color? accent = null)
    {
        var border = Rect(name, parent, pos, size, Border, 14f).rectTransform;
        var glass = Rect("Glass", border, Vector2.zero, size - new Vector2(3f, 3f), Glass, 13f).rectTransform;
        glass.anchorMin = Vector2.zero; glass.anchorMax = Vector2.one;
        glass.sizeDelta = new Vector2(-3f, -3f);
        glass.anchoredPosition = Vector2.zero;
        if (accent.HasValue)
        {
            var bar = Rect("Accent", glass, Vector2.zero, new Vector2(5f, 0f), accent.Value, 2.5f).rectTransform;
            bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(0f, 1f);
            bar.pivot = new Vector2(0f, 0.5f);
            bar.sizeDelta = new Vector2(5f, -22f);
            bar.anchoredPosition = new Vector2(10f, 0f);
        }
        return glass;
    }

    public static Image Accent(RectTransform glass) => glass.Find("Accent")?.GetComponent<Image>();

    public static TMP_Text Txt(string name, Transform parent, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions align, bool number = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = fontSize;
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.color = Text;
        t.rectTransform.sizeDelta = size;
        t.rectTransform.anchoredPosition = pos;
        if (t.fontSharedMaterial != null)
        {
            if (number)
            {
                if (numberMat == null)
                {
                    numberMat = new Material(t.fontSharedMaterial) { name = "HUD Number" };
                    numberMat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.12f);
                    numberMat.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
                }
                t.fontSharedMaterial = numberMat;
            }
            else
            {
                if (textMat == null)
                {
                    textMat = new Material(t.fontSharedMaterial) { name = "HUD Text" };
                    textMat.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
                }
                t.fontSharedMaterial = textMat;
            }
        }
        return t;
    }

    public static TMP_Text Caption(string text, Transform parent, Vector2 pos, Vector2 size, TextAlignmentOptions align)
    {
        var t = Txt("Caption", parent, pos, size, 14f, align);
        t.text = text.ToUpperInvariant();
        t.color = Label;
        t.characterSpacing = 12f;
        t.fontStyle = FontStyles.Bold;
        return t;
    }
}
