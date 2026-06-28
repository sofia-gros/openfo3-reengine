using Godot;
using System;
using System.IO;

namespace OpenFo3.Audio
{
    public static class XWmaDecoder
    {
        public static byte[] DecodeToWav(byte[] xwmaData)
        {
            try
            {
                using var ms = new MemoryStream(xwmaData);
                using var br = new BinaryReader(ms);

                string riff = new(br.ReadChars(4));
                if (riff != "RIFF") return null;

                uint fileSize = br.ReadUInt32();
                string wave = new(br.ReadChars(4));
                if (wave != "WAVE") return null;

                uint channels = 1;
                uint sampleRate = 22050;
                ushort bitsPerSample = 16;
                byte[] audioData = null;

                while (br.BaseStream.Position < br.BaseStream.Length - 8)
                {
                    string chunkId = new(br.ReadChars(4));
                    uint chunkSize = br.ReadUInt32();

                    if (chunkId == "fmt ")
                    {
                        ushort formatTag = br.ReadUInt16();
                        channels = br.ReadUInt16();
                        sampleRate = br.ReadUInt32();
                        br.ReadUInt32();
                        br.ReadUInt16();
                        bitsPerSample = br.ReadUInt16();
                        br.ReadBytes((int)(chunkSize - 16));
                    }
                    else if (chunkId == "data")
                    {
                        audioData = br.ReadBytes((int)chunkSize);
                    }
                    else
                    {
                        br.ReadBytes((int)chunkSize);
                    }
                }

                if (audioData == null) return null;

                int dataSize = audioData.Length;
                byte[] wav = new byte[44 + dataSize];
                BitConverter.GetBytes(0x46464952).CopyTo(wav, 0);
                BitConverter.GetBytes(36 + (uint)dataSize).CopyTo(wav, 4);
                BitConverter.GetBytes(0x45564157).CopyTo(wav, 8);
                BitConverter.GetBytes(0x20746D66).CopyTo(wav, 12);
                BitConverter.GetBytes(16u).CopyTo(wav, 16);
                BitConverter.GetBytes((ushort)1).CopyTo(wav, 20);
                BitConverter.GetBytes((ushort)channels).CopyTo(wav, 22);
                BitConverter.GetBytes(sampleRate).CopyTo(wav, 24);
                BitConverter.GetBytes(sampleRate * channels * (bitsPerSample / 8)).CopyTo(wav, 28);
                BitConverter.GetBytes((ushort)(channels * (bitsPerSample / 8))).CopyTo(wav, 32);
                BitConverter.GetBytes((ushort)bitsPerSample).CopyTo(wav, 34);
                BitConverter.GetBytes(0x61746164).CopyTo(wav, 36);
                BitConverter.GetBytes((uint)dataSize).CopyTo(wav, 40);
                Buffer.BlockCopy(audioData, 0, wav, 44, dataSize);
                return wav;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsXWma(byte[] data)
        {
            if (data == null || data.Length < 12) return false;
            try
            {
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);
                string riff = new(br.ReadChars(4));
                if (riff != "RIFF") return false;
                br.ReadUInt32();
                string wave = new(br.ReadChars(4));
                if (wave != "WAVE") return false;
                while (br.BaseStream.Position < br.BaseStream.Length - 8)
                {
                    string chunkId = new(br.ReadChars(4));
                    uint chunkSize = br.ReadUInt32();
                    if (chunkId == "fmt ")
                    {
                        ushort formatTag = br.ReadUInt16();
                        return formatTag == 0x0162 || formatTag == 0x0163;
                    }
                    br.ReadBytes((int)chunkSize);
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
