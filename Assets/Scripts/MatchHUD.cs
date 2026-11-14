using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Match-level UI for the local player:
//   Lobby     : big player count, who is host, how to start, and a "how to play" card.
//   Countdown : big 3-2-1, then FIGHT!
//   Playing   : timer at the top, kill feed top-left, live score list bottom-right.
//   Ended     : game-over leaderboard (winner highlighted, your row marked, "play again" for the host).
// Creates itself at runtime and floats in front of the head via HudAnchor (smooth follow).
//
// Placement is in DEGREES from the centre of your view, so it is the same on Quest 2 and Quest 3
// whatever the distance. Canvas units are milliradians: 1000 units = 45 degrees, fontSize 28 is about 1.6 degrees tall.
public class MatchHUD : MonoBehaviour {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create() {
        var go = new GameObject("MatchHUD");
        go.AddComponent<MatchHUD>();
        DontDestroyOnLoad(go);
    }

    [Header("Placement (degrees: x = right, y = up, from the centre of your view)")]
    [Tooltip("How far away the HUD floats, in metres. Sizes are angular, so this only changes focus depth.")]
    public float distance = 1.2f;
    [Tooltip("Top-left corner of the live score list. Quest comfortable reading zone is about +/-25 degrees.")]
    public Vector2 rankingAngle = new Vector2(12f, -6f);
    [Tooltip("Top-left corner of the kill feed.")]
    public Vector2 feedAngle = new Vector2(-27f, 15f);
    public float timerPitch = 19f;
    public float toastPitch = -20f;

    [Header("Behaviour")]
    [Tooltip("Draw the HUD on top of walls, so it stays readable when you hide close to cover.")]
    public bool alwaysOnTop = true;
    public int feedLines = 4;
    public float feedSeconds = 6f;
    public float fightSeconds = 1.2f;
    public float toastSeconds = 2.5f;

    static readonly Color PanelColor = new Color(0.04f, 0.06f, 0.10f, 0.86f);
    const string Accent = "#FFD54A";   // yellow: host, winner, prompts

    Canvas canvas;
    Material uiMat, textMat;

    // Playing
    TMP_Text timer, feed, ranking, center;
    RectTransform rankingBg;
    // Lobby
    GameObject lobbyPanel;
    TMP_Text lobbyTitle, lobbyCount, lobbyNames, lobbyRole, controls;
    // Game over
    GameObject endPanel;
    TMP_Text endTitle, endReason, endTable, endFooter;
    // Start button feedback
    RectTransform holdBar, holdFill;
    TMP_Text toast;

    GameManager subscribed;
    readonly Queue<(string msg, float time)> feedQueue = new Queue<(string, float)>();
    float nextRefresh;
    MatchState lastState = MatchState.Lobby;
    float fightUntil = -1f;

    // ---------------------------------------------------------------- building

    static Vector2 Deg(float yaw, float pitch) =>
        new Vector2(Mathf.Tan(yaw * Mathf.Deg2Rad), Mathf.Tan(pitch * Mathf.Deg2Rad)) * 1000f;

    void Awake() {
        var cgo = new GameObject("MatchCanvas", typeof(RectTransform));
        cgo.transform.SetParent(transform, false);
        canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;
        ((RectTransform)cgo.transform).sizeDelta = new Vector2(2000, 1400);

        if (alwaysOnTop) {
            uiMat = new Material(Canvas.GetDefaultCanvasMaterial());
            uiMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        }

        Transform root = canvas.transform;

        // --- Playing ---
        timer = MakeText("Timer", root, Deg(0, timerPitch), new Vector2(700, 90), 46, TextAlignmentOptions.Center);
        feed = MakeText("Feed", root, Deg(feedAngle.x, feedAngle.y), new Vector2(460, 200), 24, TextAlignmentOptions.TopLeft);
        feed.rectTransform.pivot = new Vector2(0f, 1f);

        rankingBg = MakePanel("RankingBg", root, Deg(rankingAngle.x, rankingAngle.y), new Vector2(330, 200));
        rankingBg.pivot = new Vector2(0f, 1f);
        rankingBg.anchoredPosition = Deg(rankingAngle.x, rankingAngle.y) + new Vector2(-14, 10);
        ranking = MakeText("Ranking", root, Deg(rankingAngle.x, rankingAngle.y), new Vector2(300, 260), 26, TextAlignmentOptions.TopLeft);
        ranking.rectTransform.pivot = new Vector2(0f, 1f);

        // --- Lobby: two columns, status on the left, how to play on the right ---
        var lobby = MakePanel("LobbyPanel", root, Vector2.zero, new Vector2(960, 580));
        lobbyPanel = lobby.gameObject;
        lobbyTitle = MakeText("Title", lobby, new Vector2(-240, 255), new Vector2(420, 50), 30, TextAlignmentOptions.Center);
        lobbyCount = MakeText("Count", lobby, new Vector2(-240, 125), new Vector2(420, 170), 150, TextAlignmentOptions.Center);
        lobbyNames = MakeText("Names", lobby, new Vector2(-240, -70), new Vector2(420, 140), 24, TextAlignmentOptions.Top);
        lobbyRole = MakeText("Role", lobby, new Vector2(-240, -205), new Vector2(440, 100), 32, TextAlignmentOptions.Center);
        MakePanel("Divider", lobby, new Vector2(0, 0), new Vector2(3, 500)).GetComponent<Image>().color = new Color(1, 1, 1, 0.15f);
        controls = MakeText("Controls", lobby, new Vector2(245, -10), new Vector2(450, 540), 22, TextAlignmentOptions.TopLeft);

        // --- Game over ---
        var end = MakePanel("EndPanel", root, Vector2.zero, new Vector2(900, 580));
        endPanel = end.gameObject;
        endTitle = MakeText("Title", end, new Vector2(0, 220), new Vector2(860, 100), 72, TextAlignmentOptions.Center);
        endReason = MakeText("Reason", end, new Vector2(0, 150), new Vector2(860, 40), 26, TextAlignmentOptions.Center);
        endTable = MakeText("Table", end, new Vector2(0, -20), new Vector2(760, 280), 30, TextAlignmentOptions.TopLeft);
        endFooter = MakeText("Footer", end, new Vector2(0, -205), new Vector2(860, 50), 32, TextAlignmentOptions.Center);

        feed.transform.SetAsLastSibling();   // kill feed stays readable over the panels

        // --- Countdown / FIGHT (on top of everything) ---
        center = MakeText("Center", root, Deg(0, 3), new Vector2(900, 300), 220, TextAlignmentOptions.Center);

        // --- Start button feedback ---
        holdBar = MakePanel("HoldBar", root, new Vector2(0, -272), new Vector2(360, 18));
        holdBar.GetComponent<Image>().color = new Color(1, 1, 1, 0.2f);
        holdFill = MakePanel("Fill", holdBar, Vector2.zero, new Vector2(360, 18));
        holdFill.GetComponent<Image>().color = new Color(1f, 0.84f, 0.29f, 1f);
        holdFill.anchorMin = holdFill.anchorMax = holdFill.pivot = new Vector2(0f, 0.5f);
        holdFill.anchoredPosition = Vector2.zero;
        toast = MakeText("Toast", root, Deg(0, toastPitch), new Vector2(900, 60), 30, TextAlignmentOptions.Center);

        HideAll();
    }

    RectTransform MakePanel(string name, Transform parent, Vector2 pos, Vector2 size) {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = PanelColor;
        img.raycastTarget = false;
        if (uiMat != null) img.material = uiMat;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        return rt;
    }

    TMP_Text MakeText(string name, Transform parent, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions align) {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = fontSize;
        t.alignment = align;
        t.richText = true;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;
        t.rectTransform.sizeDelta = size;
        t.rectTransform.anchoredPosition = pos;
        if (alwaysOnTop && t.fontSharedMaterial != null) {
            if (textMat == null) {
                textMat = new Material(t.fontSharedMaterial);
                textMat.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
            }
            t.fontSharedMaterial = textMat;
        }
        return t;
    }

    void HideAll() {
        timer.text = feed.text = ranking.text = center.text = toast.text = "";
        rankingBg.gameObject.SetActive(false);
        lobbyPanel.SetActive(false);
        endPanel.SetActive(false);
        holdBar.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------- updating

    void LateUpdate() {
        // Float in front of the head (HudAnchor does the smooth follow).
        var anchor = HudAnchor.Get();
        if (anchor != null && transform.parent != anchor.transform) {
            transform.SetParent(anchor.transform, false);
            transform.localRotation = Quaternion.identity;
        }
        transform.localPosition = new Vector3(0, 0, distance);
        canvas.transform.localScale = Vector3.one * (distance * 0.001f);   // 1 unit = 1 milliradian
        ApplyPlacement();

        GameManager gm = GameManager.Instance;
        if (gm != null && subscribed != gm) {
            if (subscribed != null) subscribed.KillFeed -= OnFeed;
            gm.KillFeed += OnFeed;
            subscribed = gm;
        }

        var nm = NetworkManager.Singleton;
        if (gm == null || !gm.IsSpawned || nm == null || !nm.IsListening) { HideAll(); return; }

        MatchState state = gm.State.Value;
        if (lastState == MatchState.Countdown && state == MatchState.Playing) fightUntil = Time.time + fightSeconds;
        lastState = state;

        AnimateEveryFrame(gm, state, nm.IsServer);

        if (Time.time < nextRefresh) return;
        nextRefresh = Time.time + 0.1f;
        RefreshText(gm, state, nm.IsServer);
    }

    // Re-applied every frame so you can tune the angles live in the Inspector (Play mode, select "MatchHUD").
    void ApplyPlacement() {
        timer.rectTransform.anchoredPosition = Deg(0, timerPitch);
        feed.rectTransform.anchoredPosition = Deg(feedAngle.x, feedAngle.y);
        ranking.rectTransform.anchoredPosition = Deg(rankingAngle.x, rankingAngle.y);
        rankingBg.anchoredPosition = Deg(rankingAngle.x, rankingAngle.y) + new Vector2(-14, 10);
        toast.rectTransform.anchoredPosition = Deg(0, toastPitch);
    }

    void AnimateEveryFrame(GameManager gm, MatchState state, bool isHost) {
        // Big centre text: 3-2-1 then FIGHT!
        if (state == MatchState.Countdown) {
            float t = Mathf.Max(0f, gm.TimeLeft.Value);
            int n = Mathf.Max(1, Mathf.CeilToInt(t));
            float frac = t - Mathf.Floor(t);                       // 1 -> 0 during each second
            center.text = $"<size=25%>GET READY</size>\n<b>{n}</b>";
            center.rectTransform.localScale = Vector3.one * (1f + 0.35f * frac);
            center.color = Color.white;
        } else if (Time.time < fightUntil) {
            float k = (fightUntil - Time.time) / fightSeconds;      // 1 -> 0
            center.text = "<b>FIGHT!</b>";
            center.rectTransform.localScale = Vector3.one * (1f + 0.4f * k * k);
            center.color = new Color(1f, 0.3f, 0.25f, Mathf.Clamp01(k * 3f));
        } else {
            center.text = "";
        }

        // Hold-to-start progress bar (host only, lobby or game over).
        float p = HostStartInput.HoldProgress;
        bool showBar = isHost && p > 0f && (state == MatchState.Lobby || state == MatchState.Ended);
        holdBar.gameObject.SetActive(showBar);
        if (showBar) holdFill.sizeDelta = new Vector2(360f * p, 18f);

        // Short confirmation / error message about the start button.
        float age = Time.time - HostStartInput.FeedbackTime;
        toast.text = age < toastSeconds ? HostStartInput.Feedback : "";
        toast.alpha = Mathf.Clamp01((toastSeconds - age) * 2f);
    }

    void RefreshText(GameManager gm, MatchState state, bool isHost) {
        // Kill feed: last few messages, each disappears after feedSeconds.
        while (feedQueue.Count > 0 && Time.time - feedQueue.Peek().time > feedSeconds) feedQueue.Dequeue();
        var sb = new StringBuilder();
        foreach (var e in feedQueue) sb.AppendLine(e.msg);
        feed.text = sb.ToString();

        // Timer (only while playing).
        if (state == MatchState.Playing) {
            int t = Mathf.Max(0, Mathf.CeilToInt(gm.TimeLeft.Value));
            string col = t <= 30 ? "#FF5A4A" : "#FFFFFF";
            timer.text = $"<color={col}><b>{t / 60}:{t % 60:00}</b></color>\n<size=50%>FIRST TO {gm.killsToWin} TAGS</size>";
        } else timer.text = "";

        // Live score list (only while playing).
        bool showRanking = state == MatchState.Playing;
        rankingBg.gameObject.SetActive(showRanking);
        ranking.text = showRanking ? BuildRanking(gm) : "";
        if (showRanking) rankingBg.sizeDelta = new Vector2(330, 52 + 34 * PlayerState.All.Count);

        lobbyPanel.SetActive(state == MatchState.Lobby);
        if (state == MatchState.Lobby) BuildLobby(gm, isHost);

        endPanel.SetActive(state == MatchState.Ended);
        if (state == MatchState.Ended) BuildEnd(gm, isHost);
    }

    // ---------------------------------------------------------------- content

    static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    string BuildRanking(GameManager gm) {
        var sb = new StringBuilder("<size=75%><color=#FFFFFF99>SCORE</color></size>\n");
        foreach (PlayerState p in gm.Ranked()) {
            string row = $"{gm.RankOf(p)}<pos=12%><color={Hex(p.PlayerColor)}>{p.DisplayName}</color><pos=82%>{p.Kills.Value}";
            sb.AppendLine(p == PlayerState.Local ? $"<b>{row}</b>" : row);
        }
        return sb.ToString();
    }

    void BuildLobby(GameManager gm, bool isHost) {
        int count = PlayerState.All.Count;
        bool enough = count >= gm.minPlayers;

        lobbyTitle.text = enough ? $"<color={Accent}>READY TO START</color>" : "WAITING FOR PLAYERS";
        lobbyCount.text = $"<b>{count}</b><size=45%><color=#FFFFFF88> / {gm.maxPlayers}</color></size>\n<size=18%>PLAYERS</size>";

        var names = new StringBuilder();
        foreach (PlayerState p in PlayerState.All) {
            string me = p == PlayerState.Local ? "  <size=80%>(YOU)</size>" : "";
            names.AppendLine($"<color={Hex(p.PlayerColor)}>{p.DisplayName}</color>{me}");
        }
        lobbyNames.text = names.ToString();

        if (isHost) {
            lobbyRole.text = enough
                ? $"<color={Accent}>You are the HOST</color>\n<b>{HostStartInput.HintText} to start</b>"
                : $"<color={Accent}>You are the HOST</color>\nWaiting for {gm.minPlayers - count} more player{(gm.minPlayers - count == 1 ? "" : "s")}";
        } else {
            lobbyRole.text = "Waiting for the host\nto start the match";
        }

        controls.text = BuildControls(gm);
    }

    // Built from the real values on the player prefab and GameManager, so it stays correct if they are tuned.
    string BuildControls(GameManager gm) {
        PlayerState me = PlayerState.Local;
        LaserGun gun = me != null ? me.GetComponent<LaserGun>() : null;
        int hits = gun != null && gun.damage > 0 ? Mathf.CeilToInt(PlayerState.MaxHealth / (float)gun.damage) : 4;
        int shots = gun != null && gun.chargePerShot > 0 ? Mathf.FloorToInt(PlayerState.MaxCharge / gun.chargePerShot) : 0;
        string refill = gun != null ? $"{gun.rechargeDelay:0.#}" : "2";
        string respawn = me != null ? $"{me.respawnDelay:0.#}" : "3";
        int mins = Mathf.RoundToInt(gm.matchSeconds / 60f);

        // <indent> keeps wrapped lines lined up under the description column.
        string Row(string label, string text) => $"<b>{label}</b><indent=33%>{text}</indent>\n";
        return
            $"<b><color={Accent}>HOW TO PLAY</color></b>\n\n" +
            Row("SHOOT", "Right trigger") +
            Row("AIM", "Point the right controller") +
            Row("TELEPORT", "Push a thumbstick forward, aim the arc, let go") +
            Row("CANCEL", "Grip while aiming a teleport") +
            Row("TURN", "Thumbstick left / right") + "\n" +
            Row("AMMO", $"{shots} shots, refills after {refill}s without shooting") +
            Row("HEALTH", $"{hits} hits and you are tagged, back in {respawn}s") +
            Row("WIN", $"First to {gm.killsToWin} tags, or most tags after {mins}:00 (tie: fewer deaths)") + "\n" +
            Row("START", $"Host: {HostStartInput.HintText} ({gm.minPlayers}-{gm.maxPlayers} players)");
    }

    void BuildEnd(GameManager gm, bool isHost) {
        ulong winnerId = gm.WinnerId.Value;
        PlayerState winner = null;
        foreach (PlayerState p in PlayerState.All) if (p.OwnerClientId == winnerId) winner = p;

        if (winnerId == GameManager.NoWinner || winner == null) {
            endTitle.text = "<b>MATCH OVER</b>";
            endReason.text = "";
        } else {
            bool iWon = winner == PlayerState.Local;
            endTitle.text = iWon
                ? $"<b><color={Accent}>YOU WIN!</color></b>"
                : $"<b><color={Hex(winner.PlayerColor)}>{winner.DisplayName}</color> WINS!</b>";
            endReason.text = winner.Kills.Value >= gm.killsToWin
                ? $"First to {gm.killsToWin} tags"
                : "Most tags when the match ended";
        }

        var sb = new StringBuilder();
        sb.AppendLine("<size=75%><color=#FFFFFF88>#<pos=9%>PLAYER<pos=62%>TAGS<pos=80%>DEATHS</color></size>");
        foreach (PlayerState p in gm.Ranked()) {
            bool me = p == PlayerState.Local;
            string name = $"<color={Hex(p.PlayerColor)}>{p.DisplayName}</color>" + (me ? "  <size=70%>(YOU)</size>" : "");
            string row = $"{gm.RankOf(p)}<pos=9%>{name}<pos=62%>{p.Kills.Value}<pos=80%>{p.Deaths.Value}";
            if (me) row = $"<b>{row}</b>";
            if (p == winner) row = $"<mark=#FFD54A30>{row}</mark>";
            sb.AppendLine(row);
        }
        endTable.text = sb.ToString();

        endFooter.text = isHost
            ? $"<color={Accent}><b>{HostStartInput.HintText} to play again</b></color>"
            : "Waiting for the host to start the next match";
    }

    void OnFeed(string msg) {
        if (msg == "FIGHT!") return;   // shown big in the centre instead
        feedQueue.Enqueue((msg, Time.time));
        while (feedQueue.Count > feedLines) feedQueue.Dequeue();
    }

    void OnDestroy() { if (subscribed != null) subscribed.KillFeed -= OnFeed; }
}
