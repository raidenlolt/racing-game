using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
/// <summary>
/// Unity Standard Assets car audio code, replace with your own
/// </summary>
namespace SpinMotion
{
    [RequireComponent(typeof (CarController))]
    public class CarAudio : MonoBehaviour
    {
        public enum EngineAudioOptions 
        {
            Simple,
            FourChannel 
        }

        public EngineAudioOptions engineSoundStyle = EngineAudioOptions.FourChannel;
        public AudioClip lowAccelClip;
        public AudioClip lowDecelClip;
        public AudioClip highAccelClip;
        public AudioClip highDecelClip;
        [Tooltip("Not played by the engine: the car sweeping past, heard when it drives onto the menu stage. Lives here so every sound a car makes travels with its prefab.")]
        public AudioClip passbyClip;
        public float pitchMultiplier = 1f;
        public float lowPitchMin = 1f;
        public float lowPitchMax = 6f;
        public float highPitchMultiplier = 0.25f;
        public float maxRolloffDistance = 500;
        public float dopplerLevel = 1;
        public bool useDoppler = true;

        /// <summary>
        /// master gain on the engine, 0..1. RaceFinishSequence fades the pack out with it when the
        /// race ends so the bots do not sit revving under the results panel, and back in on restart
        /// </summary>
        public float EngineGain { get { return gain; } }
        private float gain = 1f;
        private float gainTarget = 1f;
        private float gainPerSecond = 0f;

        /// <summary>eases the engine to a gain over a number of seconds (0 for at once)</summary>
        public void FadeEngine(float target, float seconds)
        {
            gainTarget = Mathf.Clamp01(target);
            if (seconds <= 0f) { gain = gainTarget; gainPerSecond = 0f; }
            else gainPerSecond = Mathf.Abs(gainTarget - gain) / seconds;
        }

        private AudioSource m_LowAccel;
        private AudioSource m_LowDecel;
        private AudioSource m_HighAccel;
        private AudioSource m_HighDecel;
        private bool m_StartedSound;
        private CarController m_CarController;

        private void StartSound()
        {        
            m_CarController = GetComponent<CarController>();
            m_HighAccel = SetUpEngineAudioSource(highAccelClip);

            if (engineSoundStyle == EngineAudioOptions.FourChannel)
            {
                m_LowAccel = SetUpEngineAudioSource(lowAccelClip);
                m_LowDecel = SetUpEngineAudioSource(lowDecelClip);
                m_HighDecel = SetUpEngineAudioSource(highDecelClip);
            }

            m_StartedSound = true;
        }

        private void Update()
        {
            if (!m_StartedSound)
            {
                StartSound();
            }

            if (gainPerSecond > 0f && !Mathf.Approximately(gain, gainTarget))
            {
                gain = Mathf.MoveTowards(gain, gainTarget, gainPerSecond * Time.unscaledDeltaTime);
                if (Mathf.Approximately(gain, gainTarget)) gainPerSecond = 0f;
            }

            // the sources are 3D, so distance is Unity's job; what is left here is stopping the loops
            // outright on a car the listener cannot hear at all, instead of running them at zero.
            // seven cars with four loops each were 28 voices, most of them silent
            float camDist = 0f;
            if (Camera.main != null)
                camDist = (Camera.main.transform.position - transform.position).sqrMagnitude;
            var audible = camDist <= maxRolloffDistance * maxRolloffDistance && gain > 0f;
            if (audible != sourcesRunning)
            {
                sourcesRunning = audible;
                foreach (var source in Sources())
                {
                    if (audible) source.UnPause();
                    else source.Pause();
                }
            }
            if (!audible) return;

            float volumeFactor = gain;

            if (m_StartedSound)
            {
                float pitch = ULerp(lowPitchMin, lowPitchMax, m_CarController.Revs);
                pitch = Mathf.Min(lowPitchMax, pitch);

                if (engineSoundStyle == EngineAudioOptions.Simple)
                {
                    m_HighAccel.pitch = pitch * pitchMultiplier * highPitchMultiplier;
                    m_HighAccel.dopplerLevel = useDoppler ? dopplerLevel : 0;
                    m_HighAccel.volume = volumeFactor;
                }
                else
                {
                    m_LowAccel.pitch = pitch * pitchMultiplier;
                    m_LowDecel.pitch = pitch * pitchMultiplier;
                    m_HighAccel.pitch = pitch * highPitchMultiplier * pitchMultiplier;
                    m_HighDecel.pitch = pitch * highPitchMultiplier * pitchMultiplier;

                    float accFade = Mathf.Abs(m_CarController.AccelInput);
                    float decFade = 1 - accFade;

                    float highFade = Mathf.InverseLerp(0.2f, 0.8f, m_CarController.Revs);
                    float lowFade = 1 - highFade;

                    highFade = 1 - ((1 - highFade) * (1 - highFade));
                    lowFade = 1 - ((1 - lowFade) * (1 - lowFade));
                    accFade = 1 - ((1 - accFade) * (1 - accFade));
                    decFade = 1 - ((1 - decFade) * (1 - decFade));

                    m_LowAccel.volume = lowFade * accFade * volumeFactor;
                    m_LowDecel.volume = lowFade * decFade * volumeFactor;
                    m_HighAccel.volume = highFade * accFade * volumeFactor;
                    m_HighDecel.volume = highFade * decFade * volumeFactor;

                    m_HighAccel.dopplerLevel = useDoppler ? dopplerLevel : 0;
                    m_LowAccel.dopplerLevel = useDoppler ? dopplerLevel : 0;
                    m_HighDecel.dopplerLevel = useDoppler ? dopplerLevel : 0;
                    m_LowDecel.dopplerLevel = useDoppler ? dopplerLevel : 0;
                }
            }
        }

        private bool sourcesRunning = true;

        private IEnumerable<AudioSource> Sources()
        {
            if (m_HighAccel != null) yield return m_HighAccel;
            if (m_LowAccel != null) yield return m_LowAccel;
            if (m_LowDecel != null) yield return m_LowDecel;
            if (m_HighDecel != null) yield return m_HighDecel;
        }

        private AudioSource SetUpEngineAudioSource(AudioClip clip)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = 0;
            source.loop = true;
            source.time = Random.Range(0f, clip.length);
            source.Play();
            // a real 3D source: full up to minDistance, silent at maxRolloffDistance, so Unity's
            // own attenuation and voice management handle the pack instead of a hand-rolled fade
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 5;
            source.maxDistance = maxRolloffDistance;
            source.dopplerLevel = 0;
            return source;
        }

        private static float ULerp(float from, float to, float value)
        {
            return (1.0f - value) * from + value * to;
        }
    }
}
