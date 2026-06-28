using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenFo3.Audio;
using OpenFo3.BSA;
using OpenFo3.ESM;

namespace OpenFo3.World
{
    public class SoundRecordData
    {
        public uint FormId;
        public string Filename;
        public uint Flags;
        public float MinDistance;
        public float MaxDistance;
        public bool Loop;
        public bool IsDialogue;
        public bool IsRandom;
        public bool IsMenu;
    }

    public class AudioManager
    {
        private ESMReader _esm;
        private Dictionary<uint, RecordEntry> _sounIndex;
        private List<BSAReader> _bsaReaders = new();
        private Dictionary<string, AudioStream> _loadedAudio = new();

        public AudioManager(ESMReader esm, List<BSAReader> bsaReaders)
        {
            _esm = esm;
            _bsaReaders = bsaReaders;
            _sounIndex = esm.BuildFormIdIndex(new[] { "SOUN" });
            GD.Print($"[AudioManager] SOUN index: {_sounIndex.Count} entries");
        }

        public SoundRecordData ParseSoundRecord(uint formId)
        {
            if (!_sounIndex.TryGetValue(formId, out var entry)) return null;

            try
            {
                var record = _esm.GetRecordAtOffset(entry.Offset);
                var subs = _esm.GetSubRecords(record);

                string filename = null;
                uint flags = 0;
                uint minDistRaw = 0, maxDistRaw = 100;

                foreach (var sub in subs)
                {
                    switch (sub.Type)
                    {
                        case "FNAM":
                            filename = Encoding.ASCII.GetString(sub.Data).TrimEnd('\0');
                            break;
                        case "SNDD":
                            if (sub.Data.Length >= 48)
                            {
                                minDistRaw = sub.Data[0];
                                maxDistRaw = sub.Data[1];
                                int flagsOffset = 8;
                                if (flagsOffset + 4 <= sub.Data.Length)
                                    flags = BitConverter.ToUInt32(sub.Data, flagsOffset);
                            }
                            break;
                        case "SNDX":
                            if (sub.Data.Length >= 16)
                            {
                                minDistRaw = sub.Data[0];
                                maxDistRaw = sub.Data[1];
                                flags = BitConverter.ToUInt32(sub.Data, 8);
                            }
                            break;
                    }
                }

                if (string.IsNullOrEmpty(filename)) return null;

                filename = filename.Replace('\\', '/').ToLowerInvariant();
                if (!filename.StartsWith("sound/"))
                    filename = "sound/" + filename;

                return new SoundRecordData
                {
                    FormId = formId,
                    Filename = filename,
                    Flags = flags,
                    MinDistance = minDistRaw * 5f,
                    MaxDistance = maxDistRaw * 100f,
                    Loop = (flags & 0x10) != 0,
                    IsDialogue = (flags & 0x100) != 0,
                    IsRandom = (flags & 0x2) != 0,
                    IsMenu = (flags & 0x20) != 0,
                };
            }
            catch (Exception e)
            {
                GD.PrintErr($"[AudioManager] Error parsing SOUN 0x{formId:X8}: {e.Message}");
                return null;
            }
        }

        public AudioStream LoadSound(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            path = path.Replace('\\', '/').ToLowerInvariant();

            if (_loadedAudio.TryGetValue(path, out var cached))
                return cached;

            BSAFile match = default;
            BSAReader owner = null;
            foreach (var bsa in _bsaReaders)
            {
                if (bsa.FindFile(path, out match)) { owner = bsa; break; }
            }

            if (owner == null)
                return null;

            byte[] data = owner.ReadFileData(match);
            if (data == null || data.Length < 16)
                return null;

            AudioStream stream = DecodeAudioData(data, path);
            if (stream != null)
                _loadedAudio[path] = stream;

            return stream;
        }

        private AudioStream DecodeAudioData(byte[] data, string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

            if (ext == ".wav")
            {
                // Some .wav files in FO3 are actually xWMA format
                if (XWmaDecoder.IsXWma(data))
                {
                    byte[] decoded = XWmaDecoder.DecodeToWav(data);
                    if (decoded == null) return null;
                    return new AudioStreamWav { Data = decoded };
                }
                return new AudioStreamWav { Data = data };
            }

            if (ext == ".xwm" || ext == ".wma")
            {
                byte[] decoded = XWmaDecoder.DecodeToWav(data);
                if (decoded == null)
                {
                    GD.Print($"[AudioManager] Failed to decode xWMA for '{path}'");
                    return null;
                }
                return new AudioStreamWav { Data = decoded };
            }

            if (ext == ".ogg" || ext == ".mp3")
            {
                GD.Print($"[AudioManager] Unsupported format '{ext}' for '{path}': OGG/MP3 not supported via byte array in Redot 26.1");
                return null;
            }

            GD.Print($"[AudioManager] Unknown audio format '{ext}' for '{path}'");
            return null;
        }

        public static AudioStreamPlayer3D CreateSoundPlayer(SoundRecordData sound, Vector3 position, float scale = 1f)
        {
            if (sound == null) return null;

            var player = new AudioStreamPlayer3D();
            player.Name = $"Sound_{sound.FormId:X8}";
            player.Position = position;
            player.MaxDistance = Mathf.Max(sound.MaxDistance * 0.015f * scale, 1f);
            player.UnitSize = Mathf.Clamp(sound.MinDistance * 0.015f * scale, 0.1f, 100f);

            if (sound.IsDialogue)
            {
                player.MaxDb = 0f;
                player.UnitSize = 50f;
            }

            return player;
        }

        public static Node3D CreateAmbientSoundContainer(List<SoundRecordData> ambientSounds, Func<string, AudioStream> loadSound)
        {
            var container = new Node3D();
            container.Name = "AmbientSounds";

            foreach (var sound in ambientSounds)
            {
                if (sound.IsMenu || sound.IsDialogue) continue;

                var stream = loadSound(sound.Filename);
                if (stream == null) continue;

                var player = new AudioStreamPlayer3D();
                player.Stream = stream;
                player.Name = $"Ambient_{sound.FormId:X8}";
                player.MaxDistance = Mathf.Max(sound.MaxDistance * 0.015f, 10f);
                player.UnitSize = Mathf.Clamp(sound.MinDistance * 0.015f, 0.5f, 50f);
                player.Autoplay = true;

                if (sound.Loop)
                    player.Finished += () => player.Play();

                container.AddChild(player);
            }

            return container;
        }
    }
}
