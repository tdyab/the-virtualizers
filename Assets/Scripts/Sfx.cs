using UnityEngine;

public static class Sfx {
    static AudioClip zap, hit, hurt, death, empty;

    // To use your own sound, put a file named zap / hit / hurt / death / empty in Assets/Resources/Sounds/.
    public static AudioClip Zap => zap ? zap : zap = Custom("zap") ?? Tone("zap", 0.15f, 1600f, 250f, 0.30f);
    public static AudioClip HitConfirm => hit ? hit : hit = Custom("hit") ?? Tone("hit", 0.08f, 900f, 1400f, 0.25f);
    public static AudioClip Hurt => hurt ? hurt : hurt = Custom("hurt") ?? Tone("hurt", 0.25f, 300f, 120f, 0.35f, 0.4f);
    public static AudioClip Death => death ? death : death = Custom("death") ?? Tone("death", 0.80f, 600f, 60f, 0.35f);
    public static AudioClip Empty => empty ? empty : empty = Custom("empty") ?? Tone("empty", 0.05f, 200f, 200f, 0.20f);

    static AudioClip Custom(string name) {
        var clip = Resources.Load<AudioClip>("Sounds/" + name);
        return clip != null ? clip : null; }

    static AudioClip Tone(string name, float seconds, float startHz, float endHz, float volume, float noise = 0f) {
        const int rate = 44100;                    // samples/sec
        int samples = Mathf.CeilToInt(rate * seconds);
        float[] data = new float[samples];
        double phase = 0;
        var rng = new System.Random(name.GetHashCode());

        for (int i = 0; i < samples; i++) {
            float t = (float)i / samples;                        // 0..1 progress
            float freq = Mathf.Lerp(startHz, endHz, t);          // pitch slide
            phase += 2 * Mathf.PI * freq / rate;
            float wave = Mathf.Sin((float)phase) > 0 ? 1f : -1f; // square wave
            float n = (float)(rng.NextDouble() * 2 - 1);         // white noise
            float envelope = (1f - t) * (1f - t);                // the fade out
            data[i] = (wave * (1 - noise) + n * noise) * envelope * volume;
        }
        AudioClip clip = AudioClip.Create(name, samples, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
