using System;
using System.Collections;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
public enum MatchState : byte {Lobby, Countdown, Playing, Ended}

public class GameManager : NetworkBehaviour {
    public static GameManager Instance {
        get; private set; }
    public const ulong NoWinner = ulong.MaxValue;

    [Header("Rules")]
    public int killsToWin = 10;
    public float matchSeconds = 180f;
    public float countdownSeconds = 3f;
    public float overtimeSeconds = 30f;
    public int minPlayers = 2;
    public int maxPlayers = 5;

    public NetworkVariable<MatchState> State = new NetworkVariable<MatchState>(MatchState.Lobby);
    public NetworkVariable<float> TimeLeft = new NetworkVariable<float>(0f);
    public NetworkVariable<ulong> WinnerId = new NetworkVariable<ulong>(NoWinner);
    public NetworkVariable<bool> Overtime = new NetworkVariable<bool>(false);
    public NetworkVariable<bool> IsTie = new NetworkVariable<bool>(false);

    public event Action<string> KillFeed;
    public bool DamageEnabled => State.Value == MatchState.Playing;

    void Awake() { Instance = this; }

    void Start() {
        // Server-side check that rejects a join when the match is full.
        // Requires "Connection Approval" ticked on the NetworkManager.
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
    }

    void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request,
                       NetworkManager.ConnectionApprovalResponse response) {
        bool full = NetworkManager.Singleton.ConnectedClientsIds.Count >= maxPlayers;
        response.Approved = !full;
        response.CreatePlayerObject = !full;
        if (full) response.Reason = "Match is full";
    }

    public override void OnNetworkSpawn() {
        if (!IsServer) return;
        NetworkManager.OnClientDisconnectCallback += OnClientLeft;
        StopAllCoroutines();
        State.Value = MatchState.Lobby;
        TimeLeft.Value = 0f;
        WinnerId.Value = NoWinner;
        Overtime.Value = false;
        IsTie.Value = false; }

    public override void OnNetworkDespawn() {
        if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientLeft; }

    public void StartOrRestartMatch() {
        if (!IsServer) { Debug.LogWarning("Only the host can start the match."); return; }
        if (State.Value == MatchState.Countdown || State.Value == MatchState.Playing) return;
        if (PlayerState.All.Count < minPlayers) {
            AnnounceRpc($"Need at least {minPlayers} players");
            return;
        }
        StartCoroutine(MatchRoutine());
    }

    public void ForceEndMatch() {
        if (IsServer && State.Value == MatchState.Playing) ServerEndMatch(Leader()); }

    IEnumerator MatchRoutine() {
        foreach (var p in PlayerState.All.ToList()) p.ServerResetForNewMatch(); // scores 0, back to own spawn
        WinnerId.Value = NoWinner;
        Overtime.Value = false;
        IsTie.Value = false;

        State.Value = MatchState.Countdown;
        TimeLeft.Value = countdownSeconds;
        while (TimeLeft.Value > 0) {
            yield return null;
            if (PlayerState.All.Count < minPlayers) {
                State.Value = MatchState.Lobby;
                AnnounceRpc("Not enough players, countdown cancelled");
                yield break;
            }
            TimeLeft.Value -= Time.deltaTime;
        }

        State.Value = MatchState.Playing;
        TimeLeft.Value = matchSeconds;
        AnnounceRpc("FIGHT!");
        while (State.Value == MatchState.Playing && TimeLeft.Value > 0) {
            yield return null;
            TimeLeft.Value -= Time.deltaTime; }

        if (State.Value == MatchState.Playing && TopTied()) {
            Overtime.Value = true;
            TimeLeft.Value = overtimeSeconds;
            AnnounceRpc($"DRAW! {Mathf.RoundToInt(overtimeSeconds)}s OVERTIME");
            while (State.Value == MatchState.Playing && TimeLeft.Value > 0) {
                yield return null;
                TimeLeft.Value -= Time.deltaTime; }
        }

        if (State.Value == MatchState.Playing) {
            if (TopTied()) ServerEndTie();
            else ServerEndMatch(Leader()); }
    }

    public void ServerRegisterKill(ulong killerId, ulong victimId) {
        if (!IsServer) return;
        PlayerState killer = Find(killerId);
        if (killer != null && killerId != victimId && State.Value == MatchState.Playing) {
            killer.Kills.Value++;
            AnnounceRpc($"{NameOf(killerId)} tagged {NameOf(victimId)}");
            if (killer.Kills.Value >= killsToWin) ServerEndMatch(killerId);
        }
    }

    public bool TopTied() {
        var r = Ranked();
        return r.Count >= 2 && r[0].Kills.Value == r[1].Kills.Value;
    }

    void ServerEndTie() {
        WinnerId.Value = NoWinner;
        IsTie.Value = true;
        State.Value = MatchState.Ended;
        AnnounceRpc("IT'S A TIE!");
    }

    void ServerEndMatch(ulong winner) {
        IsTie.Value = false;
        WinnerId.Value = winner;
        State.Value = MatchState.Ended;
        AnnounceRpc(winner == NoWinner ? "Match over" : $"{NameOf(winner)} WINS!");
    }

    void OnClientLeft(ulong clientId) {
        AnnounceRpc($"{NameOf(clientId)} disconnected");
        StartCoroutine(CheckPlayersNextFrame());
    }

    IEnumerator CheckPlayersNextFrame() {
        yield return null;
        if (State.Value == MatchState.Playing && PlayerState.All.Count < minPlayers)
            ServerEndMatch(Leader());
    }

 // Single source of truth for ranking: most kills first, then fewest deaths.
public System.Collections.Generic.List<PlayerState> Ranked() {
    return PlayerState.All
        .OrderByDescending(p => p.Kills.Value)
        .ThenBy(p => p.Deaths.Value)
        .ToList();
}

// 1-based rank; players with the same kills and deaths share a rank.
public int RankOf(PlayerState player) {
    int better = PlayerState.All.Count(p =>
        p.Kills.Value > player.Kills.Value ||
        (p.Kills.Value == player.Kills.Value && p.Deaths.Value < player.Deaths.Value));
    return better + 1;
}

ulong Leader() {
    var best = Ranked().FirstOrDefault();
    return best != null ? best.OwnerClientId : NoWinner;
}

    PlayerState Find(ulong clientId) => PlayerState.All.FirstOrDefault(p => p.OwnerClientId == clientId);
    public string NameOf(ulong clientId) => Find(clientId)?.DisplayName ?? $"Player {clientId}";

    [Rpc(SendTo.Everyone)]
    void AnnounceRpc(FixedString128Bytes message) {
        KillFeed?.Invoke(message.ToString()); }

    public string StatusLine() {
        switch (State.Value) {
            case MatchState.Lobby: return "Lobby - waiting for host to start";
            case MatchState.Countdown: return $"Starting in {Mathf.CeilToInt(TimeLeft.Value)}...";
            case MatchState.Playing:
                int t = Mathf.Max(0, Mathf.CeilToInt(TimeLeft.Value));
                return Overtime.Value ? $"OVERTIME {t}s" : $"Time {t / 60}:{t % 60:00}   First to {killsToWin}";
            case MatchState.Ended:
                if (IsTie.Value) return "It's a tie!";
                return WinnerId.Value == NoWinner ? "Match over" : $"WINNER: {NameOf(WinnerId.Value)}";
        }
        return "";
    }
}