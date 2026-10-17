using System.Collections.Generic;
using UnityEngine;

namespace DiceSoccer
{
    public sealed class AudioManager : MonoBehaviour
    {
        private static AudioManager _instance;
        private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();

        private AudioSource _bgmSource;
        private AudioSource _sfxSource;
        private AudioSource _crowdSource;
        private bool _soundEnabled = true;

        public static AudioManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("DiceSoccer_AudioManager");
                    _instance = go.AddComponent<AudioManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        public bool SoundEnabled
        {
            get => _soundEnabled;
            set
            {
                _soundEnabled = value;
                AudioListener.volume = _soundEnabled ? 1f : 0f;
                if (!_soundEnabled)
                {
                    _bgmSource?.Pause();
                    _crowdSource?.Pause();
                }
                else
                {
                    _bgmSource?.UnPause();
                    _crowdSource?.UnPause();
                }
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.loop = true;
            _bgmSource.playOnAwake = false;

            _crowdSource = gameObject.AddComponent<AudioSource>();
            _crowdSource.loop = true;
            _crowdSource.playOnAwake = false;
            _crowdSource.volume = 0.4f;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.loop = false;
            _sfxSource.playOnAwake = false;

            _soundEnabled = PlayerPrefs.GetInt("diceSoccer.sound", 1) != 0;
            AudioListener.volume = _soundEnabled ? 1f : 0f;
        }

        public void PlayBgm(string trackName = "MainTheme", float volume = 0.5f)
        {
            var clip = LoadClip(trackName);
            if (clip == null) return;

            if (_bgmSource.clip == clip && _bgmSource.isPlaying) return;

            _bgmSource.clip = clip;
            _bgmSource.volume = volume;
            if (_soundEnabled)
                _bgmSource.Play();
        }

        public void StopBgm()
        {
            _bgmSource?.Stop();
        }

        public void PlayCrowdChant(float volume = 0.35f)
        {
            var clip = LoadClip("StadiumCrowdChant");
            if (clip == null) return;

            _crowdSource.clip = clip;
            _crowdSource.volume = volume;
            if (_soundEnabled)
                _crowdSource.Play();
        }

        public void StopCrowd()
        {
            _crowdSource?.Stop();
        }

        public void PlayEffect(string effectName, float pitch = 1.0f, float volume = 1.0f)
        {
            if (!_soundEnabled) return;
            var clip = LoadClip(effectName);
            if (clip != null)
            {
                _sfxSource.pitch = pitch;
                _sfxSource.PlayOneShot(clip, volume);
            }
        }

        public void PlayTap()
        {
            int r = Random.Range(1, 4); // Tap1, Tap2, Tap3
            PlayEffect($"Tap{r}", Random.Range(0.95f, 1.05f), 0.7f);
        }

        public void PlayDiceRoll()
        {
            int r = Random.Range(1, 4); // DiceRoll1, DiceRoll2, DiceRoll3
            PlayEffect($"DiceRoll{r}", Random.Range(0.95f, 1.05f), 1.0f);
        }

        public void PlayWhistle()
        {
            PlayEffect("Whistle1", 1.0f, 0.9f);
        }

        public void PlayTripleWhistle()
        {
            PlayEffect("3Whistle1", 1.0f, 1.0f);
        }

        public void PlayGoal()
        {
            PlayEffect("KidsCheering", 1.0f, 1.0f);
            PlayEffect("CrowdWild", 1.0f, 0.8f);
        }

        public void PlaySave()
        {
            PlayEffect("CrowdApplause", 1.0f, 0.9f);
        }

        public void PlayDisappoint()
        {
            PlayEffect("CrowdDisappoint", 1.0f, 0.85f);
        }

        public void PlayTransform()
        {
            PlayEffect("PlayerTransform", 1.0f, 0.9f);
        }

        public void PlayCash()
        {
            PlayEffect("Cash1", 1.0f, 0.9f);
        }

        public void PlayBuy()
        {
            PlayEffect("Buy1", 1.0f, 0.9f);
        }

        public void PlayMedalCeremony()
        {
            PlayEffect("MedalCeremonyShort", 1.0f, 1.0f);
        }

        public void PlayMedalCeremonyLong()
        {
            PlayEffect("MedalCeremonyLong", 1.0f, 1.0f);
        }

        public void PlayPowerPickup()
        {
            PlayEffect("PowerPickup", 1.0f, 1.0f);
        }

        public void PlayCrowdApplause()
        {
            PlayEffect("StadiumCrowdApplause", 1.0f, 0.9f);
        }

        public void PlayKidsBooing()
        {
            PlayEffect("KidsBooing", 1.0f, 0.9f);
        }

        public void PlayHighlightReel()
        {
            PlayEffect("HighlightReelShort", 1.0f, 0.95f);
        }

        public void PlayPowerup(int num = 1)
        {
            num = Mathf.Clamp(num, 1, 8);
            if (num == 6 || num == 7) num = 1; // 6 and 7 don't exist in audio bundle
            PlayEffect($"Powerup{num}", 1.0f, 0.9f);
        }

        public void PlayBuff()
        {
            int r = Random.Range(1, 4);
            PlayEffect($"Buff{r}", 1.0f, 0.9f);
        }

        public void PlayDebuff()
        {
            int r = Random.Range(1, 3);
            PlayEffect($"Debuff{r}", 1.0f, 0.9f);
        }

        private AudioClip LoadClip(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (_clipCache.TryGetValue(name, out var cached) && cached != null)
                return cached;

            var clip = Resources.Load<AudioClip>($"Original/audio/{name}");
            if (clip != null)
            {
                _clipCache[name] = clip;
            }
            return clip;
        }
    }
}
