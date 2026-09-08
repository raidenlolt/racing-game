using UnityEngine;

namespace SpinMotion
{
    /// <summary>
    /// the visible and audible half of a boost, on the car itself. NitroSystem decides the level;
    /// this turns it into exhaust flames, a streak trail, an ignition whoosh and a running loop.
    /// runs on bots as well as the player, so a rival lighting up ahead is something you can see.
    ///
    /// the particle systems are instantiated once per exhaust at start-up from the prefabs the
    /// setup pass assigns, and only their emission and colour change after that.
    /// </summary>
    [RequireComponent(typeof(NitroSystem))]
    public class NitroExhaustFX : MonoBehaviour
    {
        [Header("Particles")]
        public ParticleSystem flamePrefab;
        public ParticleSystem streakPrefab;
        [Tooltip("Exhaust tips in the car's local space. Filled in by the setup pass from the body collider.")]
        public Vector3[] exhaustOffsets = { new Vector3(-0.4f, 0.4f, -2.4f), new Vector3(0.4f, 0.4f, -2.4f) };

        [Header("Per level: flame rate, streak rate, size scale")]
        public Vector3 levelOne = new Vector3(45f, 12f, 0.75f);
        public Vector3 levelTwo = new Vector3(80f, 24f, 1.0f);
        public Vector3 levelThree = new Vector3(130f, 40f, 1.35f);
        public Color levelOneColor = new Color(0.45f, 0.75f, 1f);
        public Color levelTwoColor = new Color(1f, 0.62f, 0.2f);
        public Color levelThreeColor = new Color(1f, 0.35f, 0.12f);

        [Header("Sound")]
        [Tooltip("Leave empty to use the synthesised stand-ins")]
        public AudioClip igniteClip;
        public AudioClip loopClip;
        [Range(0f, 1f)] public float volume = 0.7f;
        [Tooltip("Loop pitch at level one; rises a step per level")]
        public float basePitch = 0.95f;
        public float pitchPerLevel = 0.14f;

        private NitroSystem nitro;
        private ParticleSystem[] flames;
        private ParticleSystem[] streaks;
        private AudioSource loopSource;
        private AudioSource oneShotSource;
        private NitroLevel shownLevel = NitroLevel.None;
        private float baseFlameSize = 1f;
        private float baseStreakSize = 1f;

        private void Awake()
        {
            nitro = GetComponent<NitroSystem>();

            var count = exhaustOffsets != null ? exhaustOffsets.Length : 0;
            flames = new ParticleSystem[count];
            streaks = new ParticleSystem[count];
            for (int i = 0; i < count; i++)
            {
                flames[i] = Spawn(flamePrefab, exhaustOffsets[i], "Nitro Flame");
                streaks[i] = Spawn(streakPrefab, exhaustOffsets[i], "Nitro Streak");
            }
            if (flamePrefab != null) baseFlameSize = flamePrefab.main.startSize.constantMax;
            if (streakPrefab != null) baseStreakSize = streakPrefab.main.startSize.constantMax;

            loopSource = gameObject.AddComponent<AudioSource>();
            loopSource.playOnAwake = false;
            loopSource.loop = true;
            loopSource.spatialBlend = 1f;
            loopSource.rolloffMode = AudioRolloffMode.Linear;
            loopSource.minDistance = 6f;
            loopSource.maxDistance = 80f;
            loopSource.dopplerLevel = 0f;
            loopSource.volume = 0f;

            oneShotSource = gameObject.AddComponent<AudioSource>();
            oneShotSource.playOnAwake = false;
            oneShotSource.spatialBlend = 1f;
            oneShotSource.rolloffMode = AudioRolloffMode.Linear;
            oneShotSource.minDistance = 6f;
            oneShotSource.maxDistance = 80f;
            oneShotSource.dopplerLevel = 0f;

            ApplyLevel(NitroLevel.None);
        }

        private ParticleSystem Spawn(ParticleSystem prefab, Vector3 offset, string label)
        {
            if (prefab == null) return null;
            var ps = Instantiate(prefab, transform);
            ps.name = label;
            ps.transform.localPosition = offset;
            ps.transform.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            ps.Play();
            return ps;
        }

        private void Update()
        {
            var level = nitro.Level;
            if (level != shownLevel)
            {
                var wasActive = shownLevel != NitroLevel.None;
                ApplyLevel(level);
                if (level != NitroLevel.None && !wasActive) Ignite();
                if (level == NitroLevel.None && wasActive) Cut();
                shownLevel = level;
            }

            // the loop swells in rather than snapping on, and the perfect nitro reads hotter
            var targetVolume = nitro.IsActive ? volume * (nitro.IsPerfect ? 1f : 0.85f) : 0f;
            loopSource.volume = Mathf.MoveTowards(loopSource.volume, targetVolume, 3f * Time.deltaTime);
            if (loopSource.volume <= 0.001f && loopSource.isPlaying && !nitro.IsActive)
                loopSource.Stop();
        }

        private void ApplyLevel(NitroLevel level)
        {
            Vector3 settings;
            Color colour;
            switch (level)
            {
                case NitroLevel.One:   settings = levelOne;   colour = levelOneColor;   break;
                case NitroLevel.Two:   settings = levelTwo;   colour = levelTwoColor;   break;
                case NitroLevel.Three: settings = levelThree; colour = levelThreeColor; break;
                default:               settings = Vector3.zero; colour = Color.white;  break;
            }
            // a perfect run is level three power, so it gets the level three look with a hotter core
            if (nitro != null && nitro.IsPerfect) colour = Color.Lerp(levelThreeColor, Color.white, 0.35f);

            for (int i = 0; i < flames.Length; i++)
            {
                Configure(flames[i], settings.x, settings.z * baseFlameSize, colour);
                Configure(streaks[i], settings.y, settings.z * baseStreakSize, colour);
            }

            var step = level == NitroLevel.None ? 0 : (int)level - 1;
            loopSource.pitch = basePitch + pitchPerLevel * step;
        }

        private static void Configure(ParticleSystem ps, float rate, float size, Color colour)
        {
            if (ps == null) return;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var main = ps.main;
            main.startSize = size;
            main.startColor = new ParticleSystem.MinMaxGradient(colour, Color.Lerp(colour, Color.white, 0.5f));
        }

        private void Ignite()
        {
            oneShotSource.pitch = Random.Range(0.95f, 1.05f);
            oneShotSource.PlayOneShot(igniteClip != null ? igniteClip : ProceduralSfx.NitroIgniteClip(), volume);
            if (!loopSource.isPlaying)
            {
                loopSource.clip = loopClip != null ? loopClip : ProceduralSfx.NitroLoopClip();
                loopSource.Play();
            }
        }

        private void Cut()
        {
            // nothing to do: emission is already zero and the loop fades out in Update
        }
    }
}
