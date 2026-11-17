using UnityEngine;

namespace IPZ.AirHockey
{
    /// Game sounds synthesised at startup, so the project needs no audio files.
    [RequireComponent(typeof(AudioSource))]
    public class GameAudio : MonoBehaviour
    {
        const int SampleRate = 44100;

        AudioSource source;
        AudioClip mallet, wall, goal, whistle, tick;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            mallet = Tone("Mallet", 0.09f, t => Mathf.Sin(2f * Mathf.PI * 520f * t) * Decay(t, 45f));
            wall = Tone("Wall", 0.07f, t => Mathf.Sin(2f * Mathf.PI * 240f * t) * Decay(t, 60f));
            tick = Tone("Tick", 0.12f, t => Mathf.Sin(2f * Mathf.PI * 880f * t) * Decay(t, 25f));
            goal = Tone("Goal", 0.55f, t =>
            {
                float pitch = t < 0.18f ? 660f : t < 0.36f ? 880f : 1100f;
                return Mathf.Sign(Mathf.Sin(2f * Mathf.PI * pitch * t)) * 0.45f * Decay(t, 3f);
            });
            whistle = Tone("Whistle", 0.9f, t =>
            {
                float warble = 2300f + 90f * Mathf.Sin(2f * Mathf.PI * 28f * t);
                return Mathf.Sin(2f * Mathf.PI * warble * t) * Mathf.Clamp01((0.9f - t) * 8f) * 0.7f;
            });
        }

        public void PlayMallet(float speed) => source.PlayOneShot(mallet, Mathf.Clamp(speed / 12f, 0.25f, 1f));
        public void PlayWall(float speed) => source.PlayOneShot(wall, Mathf.Clamp(speed / 12f, 0.2f, 0.9f));
        public void PlayTick() => source.PlayOneShot(tick, 0.7f);
        public void PlayGoal() => source.PlayOneShot(goal);
        public void PlayWhistle() => source.PlayOneShot(whistle);

        static float Decay(float t, float speed) => Mathf.Exp(-speed * t);

        static AudioClip Tone(string name, float seconds, System.Func<float, float> wave)
        {
            int count = Mathf.CeilToInt(seconds * SampleRate);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
                samples[i] = wave((float)i / SampleRate);

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
