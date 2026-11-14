using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkBootstrap : MonoBehaviour {
    public enum Mode {
        RelaySession, Lan }

    public Mode mode = Mode.RelaySession;
    public string sessionName = "cmps297ag-team1"; // CHANGE per group, same on all headsets
    public bool autoConnect = true;

    [Header("LAN fallback")]
    public string hostIp = "192.168.1.10";
    public ushort port = 7777;

    public string Status { get; private set; } = "Starting...";

    [Header("Host leaves")]
    [Tooltip("Seconds each remaining player waits per place in line before joining the new host.")]
    public float migrationStagger = 2.5f;

    ISession session;
    bool busy, quitting, migrating;
    int generation;
    int mySlot = -1;
    readonly System.Collections.Generic.List<int> otherSlots = new System.Collections.Generic.List<int>();
    int hostSlot = -1;

    string SessionId(int gen) => gen == 0 ? sessionName : $"{sessionName}-{gen}";

    async void Start() {
        NetworkManager.Singleton.OnClientStopped += OnStopped;
        if (autoConnect && mode == Mode.RelaySession) await ConnectWithRetry(-1);
    }

    void OnDestroy() {
        if (NetworkManager.Singleton != null) NetworkManager.Singleton.OnClientStopped -= OnStopped; }

    void TrackSlots() {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.IsServer) return;
        otherSlots.Clear();
        hostSlot = -1;
        foreach (var p in PlayerState.All) {
            if (p == null || p.SpawnIndex.Value < 0) continue;
            if (p == PlayerState.Local) mySlot = p.SpawnIndex.Value;
            else if (p.OwnerClientId == NetworkManager.ServerClientId) hostSlot = p.SpawnIndex.Value;
            else otherSlots.Add(p.SpawnIndex.Value); }
    }

    void OnStopped(bool wasHost) {
        var old = session;
        session = null;
        if (quitting) return;
        if (wasHost || mode != Mode.RelaySession || !autoConnect || migrating) {
            Status = "Disconnected. Press Connect.";
            return; }
        _ = MigrateToNewHost(old);
    }

    async Task MigrateToNewHost(ISession old) {
        migrating = true;
        try {
            int place = 0;
            foreach (int s in otherSlots) if (mySlot >= 0 && s < mySlot) place++;
            Status = place == 0 ? "Host left. You are the new host..." : "Host left. Joining the new host...";
            Debug.Log($"[NetworkBootstrap] host left; my slot {mySlot}, place in line {place}");

            if (old != null) { try { await old.LeaveAsync(); } catch (Exception) { } }
            while (NetworkManager.Singleton.ShutdownInProgress) await Task.Delay(100);

            generation++;
            await Task.Delay(TimeSpan.FromSeconds(0.5f + place * migrationStagger));
            await ConnectWithRetry(generation);
        }
        finally {
            migrating = false; }
    }

    async Task ConnectWithRetry(int gen) {
        for (int attempt = 0; attempt < 5 && !NetworkManager.Singleton.IsListening; attempt++) {
            await ConnectRelay(gen);
            if (NetworkManager.Singleton.IsListening || session != null) break;
            await Task.Delay(3000); }
    }

    public async void ConnectButton() {
        if (mode == Mode.RelaySession) await ConnectRelay(-1); }

    async Task ConnectRelay(int gen) {
        if (busy || NetworkManager.Singleton.IsListening) return;
        busy = true;
        try {
            if (UnityServices.State != ServicesInitializationState.Initialized){
                Status = "Signing in...";
                var options = new InitializationOptions();
#if UNITY_EDITOR
                options.SetProfile("editor" + UnityEngine.Random.Range(0, 99999));
#else
                options.SetProfile("player" + UnityEngine.Random.Range(0, 99999));
#endif
                await UnityServices.InitializeAsync(options); }

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            if (gen < 0) gen = await NewestGeneration();
            generation = gen;
            string id = SessionId(gen);
            Status = $"Joining '{sessionName}'...";
            var sessionOptions = new SessionOptions { MaxPlayers = 5, Name = sessionName }.WithRelayNetwork();
            session = await MultiplayerService.Instance.CreateOrJoinSessionAsync(id, sessionOptions);

            Status = session.IsHost ? $"HOST of '{sessionName}'" : $"Joined '{sessionName}'";
        }
        catch (Exception e) {
            Status = "Connect failed: " + e.Message;
            Debug.LogException(e); }
        finally {
            busy = false; }
    }

    async Task<int> NewestGeneration() {
        try {
            var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions { Count = 50 });
            int best = 0;
            foreach (var info in results.Sessions) {
                if (info.Name != sessionName || info.AvailableSlots <= 0) continue;
                int gen = 0;
                if (info.Id != sessionName) {
                    string prefix = sessionName + "-";
                    if (!info.Id.StartsWith(prefix) || !int.TryParse(info.Id.Substring(prefix.Length), out gen)) continue; }
                if (gen > best) best = gen; }
            return best;
        }
        catch (Exception e) {
            Debug.LogWarning("[NetworkBootstrap] session lookup failed, using the default session: " + e.Message);
            return 0; }
    }

    public void HostLan() {
        if (NetworkManager.Singleton.IsListening) return;
        var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
        utp.SetConnectionData("127.0.0.1", port, "0.0.0.0"); // listen on all network interfaces
        //NetworkManager.Singleton.StartHost();
        bool started = NetworkManager.Singleton.StartHost();

        Debug.Log(started
            ? "LAN HOST STARTED SUCCESSFULLY"
            : "LAN HOST FAILED TO START");
        //
        Status = $"LAN host on port {port}";
    }

    public void JoinLan() {
        if (NetworkManager.Singleton.IsListening) return;
        var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
        utp.SetConnectionData(hostIp, port);
        NetworkManager.Singleton.StartClient();
        Status = $"Connecting to {hostIp}...";
    }

    public void PrepareToQuit() { quitting = true; }

    async void OnApplicationQuit() {
        quitting = true;
        if (session != null) await session.LeaveAsync(); }


    //method to automatically connect to LAN (tirggeting HostLan and JoinLan) using 
    //unity's new Input System
    void Update()
    {
        TrackSlots();

        if (mode != Mode.Lan) return;

        if (Keyboard.current == null) return;

        if (Keyboard.current.hKey.wasPressedThisFrame)
            HostLan();

        if (Keyboard.current.jKey.wasPressedThisFrame)
            JoinLan();
    }
}
