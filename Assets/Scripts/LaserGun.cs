using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
[RequireComponent(typeof(PlayerState))]
// Runs after PlayerAvatar.LateUpdate (order 0), which moves the hand/muzzle to the tracked controller,
// so the aim ray, the aim line and the shot all use this frame's controller pose.
[DefaultExecutionOrder(50)]
public class LaserGun : NetworkBehaviour {
    [Header("References")]
    public Transform muzzle;
    public LineRenderer beam;
    public LineRenderer aimLine;

    [Header("Tuning")]
    public int damage = 25;
    public float chargePerShot = 20f;
    public float rechargePerSecond = 30f;
    public float rechargeDelay = 0.6f;
    public float fireInterval = 0.25f;
    public float range = 60f;
    public LayerMask shootMask;

    [Header("Shot visuals")]
    [Tooltip("On: thin glowing tracer (LaserTracer). Off: the old solid 'beam' LineRenderer.")]
    public bool useTracerFX = true;
    public float aimLineWidth = 0.006f;

    PlayerState state;
    InputAction fireAction;
    float nextLocalFireTime;
    float lastServerShotTime = -999f;

    // What the local player's aim ray is touching right now (owner only, updated every frame).
    // AimReticle reads these, so the crosshair, the aim line and the shot share ONE raycast.
    public bool AimHasHit { get; private set; }
    public RaycastHit AimHit { get; private set; }
    public Hitbox AimHitbox { get; private set; }
    public PlayerState AimTarget => AimHitbox != null && AimHitbox.Owner != null && AimHitbox.Owner.IsAlive.Value ? AimHitbox.Owner : null;
    public Vector3 AimPoint { get; private set; }

    static readonly RaycastHit[] hits = new RaycastHit[16];
    static readonly IComparer<RaycastHit> byDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

    void Awake() {
        state = GetComponent<PlayerState>(); }

    public override void OnNetworkSpawn() {
        if (beam) beam.enabled = false;
        if (aimLine) aimLine.enabled = IsOwner;
        if (useTracerFX && aimLine && IsOwner) LaserTracer.StyleLine(aimLine, aimLineWidth);
        if (!IsOwner) return;

        fireAction = new InputAction("Fire", InputActionType.Button);
        fireAction.AddBinding("<XRController>{RightHand}/triggerPressed");
        fireAction.AddBinding("<Keyboard>/space");
        fireAction.Enable();
    }

    public override void OnNetworkDespawn() {
        if (fireAction != null) { fireAction.Disable(); fireAction.Dispose(); fireAction = null; }
    }

    void Update() {
        if (IsServer) ServerRecharge();
    }

    void LateUpdate() {
        if (!IsOwner || fireAction == null) return;

        UpdateAim();
        if (fireAction.WasPressedThisFrame()) TryFire();
    }

    void TryFire() {
        if (!state.IsAlive.Value) return;
        if (Time.time < nextLocalFireTime) return;
        if (state.Charge.Value < chargePerShot) {
            AudioSource.PlayClipAtPoint(Sfx.Empty, muzzle.position, 1f); //click, and it signals that there's no ammo left
            return; }
        nextLocalFireTime = Time.time + fireInterval;

        AudioSource.PlayClipAtPoint(Sfx.Zap, muzzle.position, 0.8f);
        Haptics.Pulse(XRNode.RightHand, 0.5f, 0.05f);

        FireRpc(muzzle.position, muzzle.forward);
    }

    // Casts the aim ray from the gun, remembers which collider it reached, and draws the aim line to it.
    void UpdateAim() {
        AimHasHit = CastShot(muzzle.position, muzzle.forward, out RaycastHit hit, out Hitbox hitbox);
        AimHit = hit;
        AimHitbox = AimHasHit ? hitbox : null;
        AimPoint = AimHasHit ? hit.point : muzzle.position + muzzle.forward * range;

        if (!aimLine) return;
        aimLine.enabled = state.IsAlive.Value;   // no aim line while dead
        if (useTracerFX) {
            Color c = state.PlayerColor;              // faint line in your player colour, fading with distance
            aimLine.startColor = new Color(c.r, c.g, c.b, 0.45f);
            aimLine.endColor = new Color(c.r, c.g, c.b, 0.15f);
        }
        aimLine.SetPosition(0, muzzle.position);
        aimLine.SetPosition(1, AimPoint);
    }

    [Rpc(SendTo.Server)]
    void FireRpc(Vector3 origin, Vector3 direction, RpcParams rpcParams = default) {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (!state.IsAlive.Value) return;
        if (Time.time - lastServerShotTime < fireInterval * 0.8f) return;
        if (state.Charge.Value < chargePerShot) return;
        if (Vector3.Distance(origin, muzzle.position) > 1f) origin = muzzle.position;

        lastServerShotTime = Time.time;
        state.Charge.Value -= chargePerShot;
        direction.Normalize();

        Physics.SyncTransforms();

        Vector3 end = origin + direction * range;
        bool hitPlayer = false;
        if (CastShot(origin, direction, out RaycastHit hit, out Hitbox hitbox)) {
            end = hit.point;
            if (hitbox != null && hitbox.Owner.IsAlive.Value) {
                hitPlayer = true;
                if (GameManager.Instance.DamageEnabled)
                    hitbox.Owner.ServerTakeDamage(
                        Mathf.RoundToInt(damage * hitbox.damageMultiplier), OwnerClientId); }
        }
        ShowShotRpc(origin, end, hitPlayer);
    }

    void ServerRecharge() {
        if (!IsSpawned) return;
        if (Time.time - lastServerShotTime < rechargeDelay) return;
        if (state.Charge.Value < PlayerState.MaxCharge)
            state.Charge.Value = Mathf.Min(PlayerState.MaxCharge, state.Charge.Value + rechargePerSecond * Time.deltaTime);
    }

    bool CastShot(Vector3 origin, Vector3 dir, out RaycastHit hit, out Hitbox hitbox) {
        int n = Physics.RaycastNonAlloc(origin, dir, hits, range, shootMask, QueryTriggerInteraction.Collide);
        Array.Sort(hits, 0, n, byDistance);
        for (int i = 0; i < n; i++) {
            Collider col = hits[i].collider;
            Hitbox hb = col.GetComponent<Hitbox>();
            PlayerState player = col.GetComponentInParent<PlayerState>();
            if (player == state) continue;                  // never hit yourself (your hitboxes or hand colliders)
            if (player != null && hb == null) continue;     // other players' hands etc. don't block the shot
            if (hb == null && col.isTrigger) continue;      // trigger volumes are not cover
            hit = hits[i];
            hitbox = hb;
            return true;
        }
        hit = default;
        hitbox = null;
        return false;
    }


    [Rpc(SendTo.Everyone)]
    void ShowShotRpc(Vector3 start, Vector3 end, bool hitPlayer) {
        if (!IsOwner) AudioSource.PlayClipAtPoint(Sfx.Zap, start, 0.8f); // owner already heard it
        else if (hitPlayer) AudioSource.PlayClipAtPoint(Sfx.HitConfirm, muzzle.position, 1f);
        if (useTracerFX) {
            bool hitSomething = hitPlayer || (end - start).sqrMagnitude < (range - 0.05f) * (range - 0.05f);
            if (IsOwner && muzzle) start = muzzle.position;          // your own shot leaves your gun exactly
            LaserTracer.Play(start, end, state.PlayerColor, hitPlayer, hitSomething);
        }
        else if (beam) StartCoroutine(BeamRoutine(start, end));
    }

    IEnumerator BeamRoutine(Vector3 start, Vector3 end) {
        beam.SetPosition(0, start);
        beam.SetPosition(1, end);
        beam.enabled = true;
        yield return new WaitForSeconds(0.08f);
        beam.enabled = false;
    }
}
