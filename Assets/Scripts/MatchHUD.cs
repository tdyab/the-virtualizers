using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

// Match-level UI for the local player: lobby info, countdown, timer, kill feed, live ranking, game-over board.
// Creates itself at runtime and parents a world-space canvas to the main camera.
public class MatchHUD : MonoBehaviour {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create() {
        var go = new GameObject("MatchHUD");
        go.AddComponent<MatchHUD>();
        DontDestroyOnLoad(go);
    }

    public float distance = 1.0f;   // metres in front of the camera
    public int feedLines = 4;

    Canvas canvas;
    TMP_Text status, center, feed, board, live;
    GameManager subscribed;
    readonly Queue<string> feedQueue = new Queue<string>();
    float nextRefresh;

    void Awake() {
        var cgo = new GameObject("MatchCanvas", typeof(RectTransform));
        cgo.transform.SetParent(transform, false);
        canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)cgo.transform;
        rt.sizeDelta = new Vector2(1000, 600);
        rt.localScale = Vector3.one * 0.001f;

        status = MakeText("Status", new Vector2(0, 270), new Vector2(900, 60), 36, TextAlignmentOptions.Center);
        center = MakeText("Center", new Vector2(0, 60), new Vector2(900, 200), 110, TextAlignmentOptions.Center);
        feed   = MakeText("Feed", new Vector2(-300, 150), new Vector2(420, 160), 24, TextAlignmentOptions.TopLeft);
        live = MakeText("Live", new Vector2(1000, -500), new Vector2(380, 160), 30, TextAlignmentOptions.BottomRight);
        live.rectTransform.pivot = new Vector2(1f, 0f);
        board  = MakeText("Board", new Vector2(0, -40), new Vector2(800, 420), 38, TextAlignmentOptions.Center);
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

    void LateUpdate() {
        // Keep the canvas head-locked to the main camera.
        Camera cam = Camera.main;
        if (cam != null && canvas.transform.parent.parent != cam.transform) {
            transform.SetParent(cam.transform, false);
            transform.localPosition = new Vector3(0, 0, distance);
            transform.localRotation = Quaternion.identity;
        }

        GameManager gm = GameManager.Instance;
        if (gm != null && subscribed != gm) { gm.KillFeed += OnFeed; subscribed = gm; }

        if (Time.time < nextRefresh) return;
        nextRefresh = Time.time + 0.1f;

        if (gm == null || !gm.IsSpawned) {
            status.text = ""; center.text = ""; feed.text = ""; board.text = ""; live.text = "";
            return;
        }

        status.text = gm.StatusLine();
        feed.text = string.Join("\n", feedQueue);
        live.text = gm.State.Value == MatchState.Playing ? BuildLive(gm) : "";

        switch (gm.State.Value) {
            case MatchState.Lobby:
                center.text = "";
                board.text = $"Players {PlayerState.All.Count}/{gm.maxPlayers}\n" +
                             (PlayerState.All.Count < gm.minPlayers
                                 ? $"Need {gm.minPlayers} players to start"
                                 : "Host: press Enter (or A + X) to start");
                break;
            case MatchState.Countdown:
                center.text = Mathf.Max(1, Mathf.CeilToInt(gm.TimeLeft.Value)).ToString();
                board.text = "";
                break;
            case MatchState.Playing:
                center.text = "";
                board.text = "";
                break;
            case MatchState.Ended:
                center.text = "";
                board.text = BuildBoard(gm);
                break;
        }
    }

    string BuildLive(GameManager gm) {
    var sb = new StringBuilder();
    foreach (PlayerState p in gm.Ranked()) {
        bool me = p == PlayerState.Local;
        string hex = ColorUtility.ToHtmlStringRGB(p.PlayerColor);
        string row = $"{gm.RankOf(p)}. <color=#{hex}>{p.DisplayName}</color>  {p.Kills.Value}";
        sb.AppendLine(me ? $"<b>{row}</b>" : row);
    }
    return sb.ToString();
}

    string BuildBoard(GameManager gm) {
        var sb = new StringBuilder();
        sb.AppendLine(gm.WinnerId.Value == GameManager.NoWinner
            ? "<b>MATCH OVER</b>"
            : $"<b>{gm.NameOf(gm.WinnerId.Value)} WINS!</b>");
        sb.AppendLine();
        foreach (PlayerState p in gm.Ranked()) {
            bool me = p == PlayerState.Local;
            string hex = ColorUtility.ToHtmlStringRGB(p.PlayerColor);
            string row = $"{gm.RankOf(p)}. <color=#{hex}>{p.DisplayName}</color>   K {p.Kills.Value}   D {p.Deaths.Value}";
            sb.AppendLine(me ? $"<b>{row}   (YOU)</b>" : row);
        }
        sb.AppendLine();
        sb.AppendLine("Host: press Enter (or A + X) to play again");
        return sb.ToString();
    }

    void OnFeed(string msg) {
        feedQueue.Enqueue(msg);
        while (feedQueue.Count > feedLines) feedQueue.Dequeue();
    }

    void OnDestroy() { if (subscribed != null) subscribed.KillFeed -= OnFeed; }
}