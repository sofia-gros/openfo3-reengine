using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenFo3.BSA;
using OpenFo3.ESM;
using OpenFo3.World;

namespace OpenFo3.Audio
{
    public class ReverbZone
    {
        public string Name;
        public Vector3 Center;
        public float Radius;
        public float ReverbWet;
        public float ReverbDry;
        public float ReverbDecay;
        public float ReverbHfDamping;
        public uint ReverbType;
    }

    public class MusicTrack
    {
        public string Path;
        public AudioStream Stream;
        public string Category;
    }

    public class RadioStation
    {
        public string Name;
        public List<string> Songs = new();
        public List<AudioStream> SongStreams = new();
        public AudioStream DJStream;
        public AudioStream InterferenceStream;
        public float Frequency;
        public bool IsActive;
    }

    public partial class AudioExpansion : Node
    {
        private AudioManager _audioManager;
        private List<BSAReader> _bsaReaders;
        private List<ReverbZone> _reverbZones = new();
        private List<RadioStation> _stations = new();
        private int _currentStationIdx = -1;
        private bool _radioEnabled;

        private AudioStreamPlayer _musicPlayer;
        private AudioStreamPlayer _radioPlayer;
        private string _currentMusicCategory = "explore";
        private bool _isInCombat;

        private AudioEffectReverb _reverbEffect;
        private int _reverbBusIdx;

        private Dictionary<string, AudioStream> _footstepSounds = new();

        [Signal]
        public delegate void RadioTunedEventHandler(int stationIndex, string stationName);

        public AudioExpansion(AudioManager audioManager, List<BSAReader> bsaReaders)
        {
            _audioManager = audioManager;
            _bsaReaders = bsaReaders;
        }

        public override void _Ready()
        {
            CreateAudioBuses();
            CreateMusicPlayer();
            LoadFootstepSounds();
            DiscoverRadioStations();
        }

        private void CreateAudioBuses()
        {
            if (AudioServer.GetBusIndex("Music") < 0)
            {
                int idx = AudioServer.BusCount;
                AudioServer.AddBus(idx);
                AudioServer.SetBusName(idx, "Music");
            }

            if (AudioServer.GetBusIndex("Radio") < 0)
            {
                int idx = AudioServer.BusCount;
                AudioServer.AddBus(idx);
                AudioServer.SetBusName(idx, "Radio");
                var radioEffect = new AudioEffectLowPassFilter();
                radioEffect.CutoffHz = 4000f;
                AudioServer.AddBusEffect(idx, radioEffect);
            }

            if (AudioServer.GetBusIndex("SFX") < 0)
            {
                int idx = AudioServer.BusCount;
                AudioServer.AddBus(idx);
                AudioServer.SetBusName(idx, "SFX");
                _reverbEffect = new AudioEffectReverb();
                _reverbEffect.RoomSize = 0.2f;
                _reverbEffect.Damping = 0.5f;
                AudioServer.AddBusEffect(idx, _reverbEffect);
                _reverbBusIdx = idx;
            }

            if (AudioServer.GetBusIndex("Voice") < 0)
            {
                int idx = AudioServer.BusCount;
                AudioServer.AddBus(idx);
                AudioServer.SetBusName(idx, "Voice");
            }

            if (AudioServer.GetBusIndex("Ambience") < 0)
            {
                int idx = AudioServer.BusCount;
                AudioServer.AddBus(idx);
                AudioServer.SetBusName(idx, "Ambience");
            }
        }

        private void CreateMusicPlayer()
        {
            _musicPlayer = new AudioStreamPlayer();
            _musicPlayer.Name = "MusicPlayer";
            _musicPlayer.Bus = "Music";
            AddChild(_musicPlayer);

            _radioPlayer = new AudioStreamPlayer();
            _radioPlayer.Name = "RadioPlayer";
            _radioPlayer.Bus = "Radio";
            AddChild(_radioPlayer);
        }

        private void LoadFootstepSounds()
        {
            foreach (string material in new[] { "dirt", "concrete", "metal", "wood", "water", "gravel", "stone" })
            {
                var wav = new AudioStreamWav();
                wav.Data = GenerateFootstepWav(material);
                _footstepSounds[material] = wav;
            }

            _footstepSounds["default"] = new AudioStreamWav
            {
                Data = GenerateFootstepWav("default")
            };
        }

        private byte[] GenerateFootstepWav(string material)
        {
            float freq = material switch
            {
                "dirt" => 150f,
                "concrete" => 400f,
                "metal" => 800f,
                "wood" => 500f,
                "water" => 100f,
                "gravel" => 300f,
                "stone" => 600f,
                _ => 250f,
            };

            int sampleRate = 22050;
            float duration = 0.08f;
            int numSamples = (int)(sampleRate * duration);
            short[] samples = new short[numSamples];

            for (int i = 0; i < numSamples; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = Mathf.Max(0, 1f - t / duration) * Mathf.Max(0, 1f - t / duration);
                float noise = (float)(new Random().NextDouble() - 0.5) * 0.3f;
                float sample = (Mathf.Sin(t * freq * Mathf.Pi * 2) + noise) * envelope * 0.4f;
                samples[i] = (short)(sample * short.MaxValue);
            }

            int dataSize = numSamples * 2;
            byte[] wav = new byte[44 + dataSize];
            BitConverter.GetBytes(0x46464952).CopyTo(wav, 0);
            BitConverter.GetBytes(36 + dataSize).CopyTo(wav, 4);
            BitConverter.GetBytes(0x45564157).CopyTo(wav, 8);
            BitConverter.GetBytes(0x20746D66).CopyTo(wav, 12);
            BitConverter.GetBytes(16).CopyTo(wav, 16);
            BitConverter.GetBytes((ushort)1).CopyTo(wav, 20);
            BitConverter.GetBytes((ushort)1).CopyTo(wav, 22);
            BitConverter.GetBytes(sampleRate).CopyTo(wav, 24);
            BitConverter.GetBytes(sampleRate * 2).CopyTo(wav, 28);
            BitConverter.GetBytes((ushort)2).CopyTo(wav, 32);
            BitConverter.GetBytes((ushort)16).CopyTo(wav, 34);
            BitConverter.GetBytes(0x61746164).CopyTo(wav, 36);
            BitConverter.GetBytes(dataSize).CopyTo(wav, 40);
            Buffer.BlockCopy(samples, 0, wav, 44, dataSize);
            return wav;
        }

        private void DiscoverRadioStations()
        {
            var gnr = new RadioStation
            {
                Name = "Galaxy News Radio",
                Frequency = 104.5f,
                Songs = new List<string>
                {
                    "sound/music/radio/gnr/butcher_pete_way_back_home.ogg",
                    "sound/music/radio/gnr/billie_holliday_ive_been_around.ogg",
                    "sound/music/radio/gnr/bob_crosby_big_band_swing.ogg",
                    "sound/music/radio/gnr/cole_porter_anything_goes.ogg",
                    "sound/music/radio/gnr/ella_fitzgerald_love_me.ogg",
                }
            };
            _stations.Add(gnr);

            var enclave = new RadioStation
            {
                Name = "Enclave Radio",
                Frequency = 88.3f,
                Songs = new List<string> { "sound/music/radio/enclave/propaganda.ogg" },
            };
            _stations.Add(enclave);

            var agatha = new RadioStation
            {
                Name = "Agatha's Station",
                Frequency = 98.7f,
                Songs = new List<string> { "sound/music/radio/agatha/violin.ogg" },
            };
            _stations.Add(agatha);
        }

        public void PlayMusic(string category, bool forceRestart = false)
        {
            if (category == _currentMusicCategory && !forceRestart) return;
            _currentMusicCategory = category;

            string path = category switch
            {
                "explore" => "sound/music/explore.ogg",
                "combat" => "sound/music/combat.ogg",
                "death" => "sound/music/death.ogg",
                "vats" => "sound/music/vats.ogg",
                _ => null
            };

            if (path == null) return;
            var stream = _audioManager.LoadSound(path);
            if (stream == null) return;

            _musicPlayer.Stream = stream;
            _musicPlayer.Play();
        }

        public void SetCombatState(bool inCombat)
        {
            _isInCombat = inCombat;
            PlayMusic(inCombat ? "combat" : "explore");
        }

        public void ToggleRadio()
        {
            _radioEnabled = !_radioEnabled;
            if (_radioEnabled)
                TuneRadio(_currentStationIdx >= 0 ? _currentStationIdx : 0);
            else
                _radioPlayer.Stop();
        }

        public void TuneRadio(int stationIndex)
        {
            if (stationIndex < 0 || stationIndex >= _stations.Count)
            {
                _radioPlayer.Stop();
                return;
            }

            _currentStationIdx = stationIndex;
            var station = _stations[stationIndex];
            _radioEnabled = true;

            if (station.Songs.Count > 0)
            {
                string songPath = station.Songs[0];
                var stream = _audioManager.LoadSound(songPath);
                if (stream != null)
                {
                    _radioPlayer.Stream = stream;
                    _radioPlayer.Play();
                }
            }

            EmitSignal(nameof(RadioTunedEventHandler), stationIndex, station.Name);
        }

        public void CycleRadio(bool next)
        {
            if (_stations.Count == 0) return;
            int idx = _currentStationIdx;
            if (next)
                idx = (idx + 1) % _stations.Count;
            else
                idx = (idx - 1 + _stations.Count) % _stations.Count;
            TuneRadio(idx);
        }

        public void SetReverb(ReverbZone zone)
        {
            if (_reverbEffect == null) return;
            _reverbEffect.RoomSize = zone.ReverbType switch
            {
                0 => 0.1f,
                1 => 0.3f,
                2 => 0.6f,
                3 => 0.8f,
                4 => 0.5f,
                _ => 0.2f,
            };
            _reverbEffect.Damping = zone.ReverbHfDamping;
        }

        public void ClearReverb()
        {
            if (_reverbEffect == null) return;
            _reverbEffect.RoomSize = 0f;
            _reverbEffect.Damping = 0.5f;
        }

        public AudioStreamPlayer3D PlaySoundAtPosition(string path, Vector3 position, float volume = 0f)
        {
            var stream = _audioManager.LoadSound(path);
            if (stream == null) return null;

            var player = new AudioStreamPlayer3D();
            player.Stream = stream;
            player.GlobalPosition = position;
            player.MaxDistance = 30f;
            player.UnitSize = 5f;
            if (volume != 0f) player.VolumeDb = volume;
            GetTree().Root.AddChild(player);
            player.Play();

            player.Finished += () =>
            {
                if (player.Stream is AudioStreamWav wav && wav.Data != null)
                    player.QueueFree();
            };

            return player;
        }

        public void PlayFootstep(string surfaceMaterial, Vector3 position)
        {
            if (!_footstepSounds.TryGetValue(surfaceMaterial, out var stream))
                stream = _footstepSounds["default"];

            var player = new AudioStreamPlayer3D();
            player.Stream = stream;
            player.GlobalPosition = position;
            player.UnitSize = 3f;
            player.MaxDistance = 8f;
            player.VolumeDb = -8f;
            GetTree().Root.AddChild(player);
            player.Play();

            player.Finished += () => player.QueueFree();
        }

        public void ApplyOcclusion(AudioStreamPlayer3D player, float occlusionFactor)
        {
            if (occlusionFactor <= 0f) return;
            player.VolumeDb = Mathf.Lerp(0f, -30f, occlusionFactor);
        }

        public bool IsRadioOn => _radioEnabled;
        public RadioStation GetCurrentStation() => _currentStationIdx >= 0 ? _stations[_currentStationIdx] : null;
        public string CurrentMusicCategory => _currentMusicCategory;
    }
}
