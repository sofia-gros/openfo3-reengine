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

    public struct RecordEntry
    {
        public long Offset;
        public uint WorldFormId;
        public uint CellFormId;
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

        public Dictionary<uint, RecordEntry> BuildFormIdIndex(IEnumerable<string> targetTypes)
        {
            var index = new Dictionary<uint, RecordEntry>();
            var targetSet = new HashSet<string>(targetTypes);
            
            _totalSeen = 0;
            _stream.Position = 0;
            TraverseNodes(_stream.Length, targetSet, index, 0, 0);
            
            GD.Print($"[ESMReader] BuildFormIdIndex({string.Join(",", targetTypes)}): {index.Count} found, {_totalSeen} total nodes seen");
            return index;
        }

        private int _totalSeen = 0;
        private void TraverseNodes(long end, HashSet<string> targetSet, Dictionary<uint, RecordEntry> index, uint currentWorld, uint currentCell)
        {
            while (_stream.Position < end && _stream.Position < _stream.Length)
            {
                long startOffset = _stream.Position;
                if (startOffset + 8 > _stream.Length) break;

                string type = ReadTag();
                uint size = _reader.ReadUInt32();
                _totalSeen++;

                if (type == "GRUP")
                {
                    uint label = _reader.ReadUInt32();
                    uint groupType = _reader.ReadUInt32();
                    
                    // Update context based on Group Type
                    uint nextWorld = currentWorld;
                    uint nextCell = currentCell;

                    if (groupType == 1) // World Children
                    {
                        nextWorld = label;
                    }
                    else if (groupType >= 6 && groupType <= 10) // Cell Children
                    {
                        nextCell = label;
                    }

                    // For GRUP, 'size' IS the total size including the 24-byte header.
                    long grupEnd = startOffset + size;
                    if (grupEnd > _stream.Length) grupEnd = _stream.Length;

                    _stream.Seek(8, SeekOrigin.Current); // Skip rest of GRUP header (already read 16 bytes: 4 tag, 4 size, 4 label, 4 type)
                    TraverseNodes(grupEnd, targetSet, index, nextWorld, nextCell);
                    _stream.Position = grupEnd;
                }
                else
                {
                    // Non-GRUP record
                    uint flags = _reader.ReadUInt32();
                    uint formId = _reader.ReadUInt32();
                    
                    if (targetSet.Contains(type))
                    {
                        index[formId] = new RecordEntry 
                        { 
                            Offset = startOffset,
                            WorldFormId = currentWorld,
                            CellFormId = currentCell
                        };
                    }
                    
                    // FO3 Record header is 24 bytes. 'size' is the data size.
                    long nextOffset = startOffset + 24 + size;
                    if (nextOffset > _stream.Length) 
                    {
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
                catch (Exception)
                {
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
                    if (record.Size > 100 * 1024 * 1024) return Array.Empty<byte>();
                    byte[] data = _reader.ReadBytes((int)record.Size);
                    _stream.Position = currentPos;
                    return data;
                }
                catch (Exception)
                {
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
                    using var ms = new MemoryStream(compressedData);
                    using var zs = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress);
                    using var decompressedMs = new MemoryStream();
                    zs.CopyTo(decompressedMs);
                    data = decompressedMs.ToArray();
                }
                catch (Exception)
                {
                    return subRecords;
                }
            }

            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                uint? nextSizeOverride = null;

                while (ms.Position < ms.Length)
                {
                    if (ms.Length - ms.Position < 6) break;
                    string type = Encoding.ASCII.GetString(br.ReadBytes(4));
                    ushort size = br.ReadUInt16();
                    
                    if (type == "XXXX")
                    {
                        nextSizeOverride = br.ReadUInt32();
                        continue;
                    }

                    uint actualSize = nextSizeOverride ?? size;
                    nextSizeOverride = null;

                    if (ms.Position + actualSize > ms.Length) break;

                    byte[] subData = br.ReadBytes((int)actualSize);
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