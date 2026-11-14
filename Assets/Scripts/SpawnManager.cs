using System.Collections.Generic;
using UnityEngine;

public class SpawnManager : MonoBehaviour {
    public static SpawnManager Instance { get; private set; }

    public Transform arenaCenter;
    public Transform[] spawnPoints;

    [Header("Circle generator (editor tool)")]
    public int count = 5;
    public float radius = 8f;

    readonly Dictionary<int, ulong> claimed = new Dictionary<int, ulong>(); // slot -> clientId
    void Awake() {
        Instance = this; }

    void Start() {
        for (int i = 0; i < spawnPoints.Length; i++) {
            var pad = spawnPoints[i].GetComponentInChildren<Renderer>();
            if (!pad) continue;
            Color c = PlayerAvatar.PlayerColors[i % PlayerAvatar.PlayerColors.Length];
            pad.material.color = c;
            pad.material.EnableKeyword("_EMISSION");
            pad.material.SetColor("_EmissionColor", c * 1.4f); }
    }

    public int ClaimIndex(ulong clientId) {
        for (int i = 0; i < spawnPoints.Length; i++)
            if (!claimed.ContainsKey(i)) { claimed[i] = clientId; return i; }
        return (int)(clientId % (ulong)spawnPoints.Length); // more players than slots
    }

    public void Release(int index, ulong clientId) {
        if (claimed.TryGetValue(index, out ulong owner) && owner == clientId) claimed.Remove(index); }

    public Transform Get(int index) => spawnPoints[Mathf.Clamp(index, 0, spawnPoints.Length - 1)];

    public Vector3 FacingCenter(Vector3 from) =>
        (arenaCenter ? arenaCenter.position : Vector3.zero) - from;

    public Transform GetSafest(PlayerState forPlayer) {
        Transform best = spawnPoints[0];
        float bestScore = -1f;
        foreach (Transform sp in spawnPoints) {
            float nearest = float.MaxValue;
            foreach (PlayerState p in PlayerState.All) {
                if (p == forPlayer || !p.IsAlive.Value) continue;
                nearest = Mathf.Min(nearest, Vector3.Distance(sp.position, p.HeadPosition)); }
            if (nearest > bestScore) {
                bestScore = nearest; best = sp; }
        }
        return best;
    }

    [ContextMenu("Generate Circle Spawn Points")]
    void GenerateCircle() {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        Vector3 c = arenaCenter ? arenaCenter.position : Vector3.zero;
        spawnPoints = new Transform[count];
        for (int i = 0; i < count; i++) {
            float angle = i * Mathf.PI * 2f / count;
            Vector3 pos = c + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius;

            var sp = new GameObject($"Spawn_{i + 1}");
            sp.transform.SetParent(transform);
            sp.transform.position = pos;
            sp.transform.rotation = Quaternion.LookRotation(c - pos);

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "Pad";
            DestroyImmediate(pad.GetComponent<Collider>());
            pad.transform.SetParent(sp.transform, false);
            pad.transform.localScale = new Vector3(1f, 0.01f, 1f);
            pad.transform.localPosition = new Vector3(0, 0.011f, 0);

            spawnPoints[i] = sp.transform;
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }
}
