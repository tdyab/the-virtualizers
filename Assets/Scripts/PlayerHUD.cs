using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Head-locked HUD for the LOCAL player. Put it on a World Space canvas that is a child of the VR camera.
/// Only reads PlayerState.Local and LaserGun, so no teammate scripts need to change.
public class PlayerHUD : MonoBehaviour
{
    [Header("Health (bottom-left)")]
    public Image healthFill;          // Image Type = Filled, Fill Method = Horizontal
    public TMP_Text healthText;

    [Header("Ammo (bottom-right)")]
    public TMP_Text ammoText;

    [Header("Feedback")]
    public Image damageFlash;         // large red Image, starts at alpha 0
    public float flashAlpha = 0.45f;
    public float flashFadeSpeed = 2.5f;
    public TMP_Text respawnText;      // centre of view, empty by default

    PlayerState me;
    LaserGun gun;
    int lastHealth;
    float deadSince = -1f;

    void Update()
    {
        if (PlayerState.Local != me) Bind(PlayerState.Local);
        // Not in a game yet: hide health / ammo instead of showing the prefab's "New Text" placeholders.
        ShowBars(me != null);
        if (me == null) return;

        // Health bar
        int hp = me.Health.Value;
        float t = hp / (float)PlayerState.MaxHealth;
        healthFill.fillAmount = Mathf.Lerp(healthFill.fillAmount, t, Time.deltaTime * 10f);
        healthFill.color = Color.Lerp(Color.red, Color.green, t);
        healthText.text = hp.ToString();

        // Red screen flash whenever health goes down
        if (hp < lastHealth) SetFlashAlpha(flashAlpha);
        lastHealth = hp;
        var c = damageFlash.color;
        c.a = Mathf.MoveTowards(c.a, 0f, flashFadeSpeed * Time.deltaTime);
        damageFlash.color = c;

        // Ammo = shots left
        float perShot = gun ? gun.chargePerShot : 1f;
        int shots = Mathf.FloorToInt(me.Charge.Value / perShot);
        int max = Mathf.RoundToInt(PlayerState.MaxCharge / perShot);
        ammoText.text = $"{shots} / {max}";
        ammoText.color = shots < max * 0.1f ? Color.red : Color.white;

        // Respawn countdown
        if (!me.IsAlive.Value)
        {
            if (deadSince < 0f) deadSince = Time.time;
            int left = Mathf.CeilToInt(me.respawnDelay - (Time.time - deadSince));
            respawnText.text = left > 0 ? $"Respawning in {left}" : "Respawning...";
        }
        else
        {
            deadSince = -1f;
            respawnText.text = "";
        }
    }

    void Bind(PlayerState s)
    {
        me = s;
        gun = s ? s.GetComponent<LaserGun>() : null;
        lastHealth = s ? s.Health.Value : 0;
        deadSince = -1f;
    }

    void ShowBars(bool on)
    {
        if (healthFill) healthFill.enabled = on;
        if (healthText) healthText.enabled = on;
        if (ammoText) ammoText.enabled = on;
        if (!on && respawnText) respawnText.text = "";
    }

    void SetFlashAlpha(float a)
    {
        var c = damageFlash.color;
        c.a = a;
        damageFlash.color = c;
    }
}