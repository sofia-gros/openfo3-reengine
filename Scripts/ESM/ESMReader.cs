using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Godot;

namespace OpenFo3.ESM
{
    public class ESMRecord
    {
        public string Type { get; set; }
        public uint Size { get; set; }
        public uint Flags { get; set; }
        public uint FormId { get; set; }
        public uint Revision { get; set; }
        public ushort Version { get; set; }
        public ushort Unknown { get; set; }
        public long FileOffset { get; set; }
    }

    public class ESMReader : IDisposable
    {
        private BinaryReader _reader;
        private Stream _stream;
        private readonly object _lock = new();

        public ESMReader(string filePath)
        {
            _stream = File.OpenRead(filePath);
            _reader = new BinaryReader(_stream);
        }

        public Dictionary<uint, long> BuildFormIdIndex(IEnumerable<string> targetTypes)
        {
            var index = new Dictionary<uint, long>();
            var targetSet = new HashSet<string>(targetTypes);
            
            _stream.Position = 0;
            TraverseNodes(_stream.Length, targetSet, index);
            
            return index;
        }

        private void TraverseNodes(long end, HashSet<string> targetSet, Dictionary<uint, long> index)
        {
            while (_stream.Position < end && _stream.Position < _stream.Length)
            {
                long startOffset = _stream.Position;
                if (startOffset + 8 > _stream.Length) break;

                string type = ReadTag();
                uint size = _reader.ReadUInt32();

                if (type == "GRUP")
                {
                    // For GRUP, 'size' IS the total size including the 24-byte header.
                    long grupEnd = startOffset + size;
                    if (grupEnd > _stream.Length) grupEnd = _stream.Length;

                    _stream.Seek(16, SeekOrigin.Current); // Skip rest of GRUP header (24 bytes total)
                    TraverseNodes(grupEnd, targetSet, index);
                    _stream.Position = grupEnd;
                }
                else
                {
                    // Non-GRUP record
                    uint flags = _reader.ReadUInt32();
                    uint formId = _reader.ReadUInt32();
                    
                    if (targetSet.Contains(type))
                    {
                        index[formId] = startOffset;
                    }
                    
                    // FO3 Record header is 24 bytes. 'size' is the data size.
                    long nextOffset = startOffset + 24 + size;
                    if (nextOffset > _stream.Length) 
                    {
                        // GD.PrintErr($"[ESMReader] Record {type} at {startOffset:X} has size {size} exceeding file!");
                        break;
                    }
                    _stream.Position = nextOffset;
                }
            }
        }

        public ESMRecord GetRecordAtOffset(long offset)
        {
            lock (_lock)
            {
                try
                {
                    if (offset < 0 || offset > _stream.Length - 20)
                    {
                        // GD.PrintErr($"[ESMReader] Offset {offset:X8} is out of range for stream length {_stream.Length}");
                    }

                    _stream.Position = offset;
                    string type = ReadTag();
                    uint size = _reader.ReadUInt32();
                    uint flags = _reader.ReadUInt32();
                    uint formId = _reader.ReadUInt32();
                    uint revision = _reader.ReadUInt32();
                    ushort version = _reader.ReadUInt16();
                    ushort unknown = _reader.ReadUInt16();

                    return new ESMRecord
                    {
                        Type = type,
                        Size = size,
                        Flags = flags,
                        FormId = formId,
                        Revision = revision,
                        Version = version,
                        Unknown = unknown,
                        FileOffset = _stream.Position
                    };
                }
                catch (Exception e)
                {
                    // GD.PrintErr($"[ESMReader] FAILED GetRecordAtOffset at {offset:X8}: {e.Message}");
                    throw;
                }
            }
        }

        public byte[] ReadRecordData(ESMRecord record)
        {
            lock (_lock)
            {
                try
                {
                    long currentPos = _stream.Position;
                    _stream.Position = record.FileOffset;
                    if (record.FileOffset + record.Size > _stream.Length)
                    {
                        // GD.PrintErr($"[ESMReader] ERROR: Record 0x{record.FormId:X8} ({record.Type}) size {record.Size} goes beyond file length {_stream.Length} at offset {record.FileOffset}");
                    }
                    if (record.Size > 100 * 1024 * 1024) // 100MB Safety
                    {
                        // GD.PrintErr($"[ESMReader] CRITICAL: Record 0x{record.FormId:X8} has suspicious size {record.Size}. Skipping to prevent OOM.");
                        return Array.Empty<byte>();
                    }
                    byte[] data = _reader.ReadBytes((int)record.Size);
                    _stream.Position = currentPos;
                    return data;
                }
                catch (Exception e)
                {
                    // GD.PrintErr($"[ESMReader] CRITICAL ERROR reading 0x{record.FormId:X8} ({record.Type}) at {record.FileOffset}: {e.Message}");
                    throw;
                }
            }
        }

        public List<SubRecord> GetSubRecords(ESMRecord record)
        {
            var subRecords = new List<SubRecord>();
            byte[] data = ReadRecordData(record);
            
            if ((record.Flags & 0x00040000) != 0) 
            {
                uint uncompressedSize = BitConverter.ToUInt32(data, 0);
                byte[] compressedData = new byte[data.Length - 4];
                Array.Copy(data, 4, compressedData, 0, compressedData.Length);
                try
                {
                    if (uncompressedSize > 50 * 1024 * 1024) // 50MB Safety
                    {
                        // GD.PrintErr($"[ESMReader] CRITICAL: Record 0x{record.FormId:X8} uncompressed size {uncompressedSize} is too large.");
                        return subRecords;
                    }

                    // // GD.Print($"[ESMReader] Decompressing 0x{record.FormId:X8}: {data.Length} -> {uncompressedSize}");
                    using var ms = new MemoryStream(compressedData);
                    using var zs = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress);
                    using var decompressedMs = new MemoryStream();
                    zs.CopyTo(decompressedMs);
                    data = decompressedMs.ToArray();
                }
                catch (Exception e)
                {
                    // GD.PrintErr($"Failed to decompress record 0x{record.FormId:X8}: {e.Message}");
                    return subRecords;
                }
            }

            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                while (ms.Position < ms.Length)
                {
                    if (ms.Length - ms.Position < 6) break;
                    string type = Encoding.ASCII.GetString(br.ReadBytes(4));
                    ushort size = br.ReadUInt16();
                    
                    if (ms.Position + size > ms.Length)
                    {
                        // GD.PrintErr($"[ESMReader] ERROR: SubRecord {type} size {size} exceeds record data length {ms.Length} at pos {ms.Position}");
                        break;
                    }

                    byte[] subData = br.ReadBytes(size);
                    subRecords.Add(new SubRecord { Type = type, Data = subData });
                }
            }
            return subRecords;
        }

        private string ReadTag()
        {
            byte[] bytes = _reader.ReadBytes(4);
            return Encoding.ASCII.GetString(bytes);
        }

        public void Dispose()
        {
            _reader?.Dispose();
            _stream?.Dispose();
        }
    }
}