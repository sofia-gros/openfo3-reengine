using System;
using System.IO;
using System.Linq;
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
        public int CellX;
        public int CellY;
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
                            CellFormId = currentCell,
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

        /// Build a map from LAND formId → (cellX, cellY) by sequentially scanning the
        /// target WRLD's children in file order. This ensures each LAND gets the coordinates
        /// of the CELL that immediately precedes it in the tree.
        private int _cellSeenInWorld;
        private int _landSeenInWorld;

        public Dictionary<uint, (int, int)> BuildLandCoordinateMap(
            Dictionary<uint, RecordEntry> landIndex,
            Dictionary<uint, RecordEntry> cellIndex,
            uint worldFormId)
        {
            var coordMap = new Dictionary<uint, (int, int)>();
            _cellSeenInWorld = 0;
            _landSeenInWorld = 0;

            GD.Print($"[ESMReader] BuildLandCoordinateMap: world=0x{worldFormId:X8} landIndex={landIndex.Count} cellIndex={cellIndex.Count}");

            lock (_lock)
            {
                _stream.Position = 0;
                TraverseForLandCoords(_stream.Length, landIndex, cellIndex, worldFormId, coordMap, 0, 0, 0);
            }

            GD.Print($"[ESMReader] BuildLandCoordinateMap result: {coordMap.Count} LANDs mapped for world 0x{worldFormId:X8} (cellsSeen={_cellSeenInWorld} landsSeen={_landSeenInWorld})");
            foreach (var kvp in coordMap)
            {
                uint landFormId = kvp.Key;
                var entry = landIndex[landFormId];
                GD.Print($"[CoordMap] LAND 0x{landFormId:X8} CellFormId=0x{entry.CellFormId:X8} -> cell({kvp.Value.Item1},{kvp.Value.Item2})");
            }
            return coordMap;
        }

        private void TraverseForLandCoords(long end, Dictionary<uint, RecordEntry> landIndex,
            Dictionary<uint, RecordEntry> cellIndex, uint worldFormId,
            Dictionary<uint, (int, int)> coordMap, uint currentWorld, int lastCellX, int lastCellY)
        {
            while (_stream.Position < end && _stream.Position < _stream.Length)
            {
                long startOffset = _stream.Position;
                if (startOffset + 8 > _stream.Length) break;

                string type = ReadTag();
                uint size = _reader.ReadUInt32();

                if (type == "GRUP")
                {
                    uint label = _reader.ReadUInt32();
                    uint groupType = _reader.ReadUInt32();
                    uint nextWorld = currentWorld;

                    if (groupType == 1) // World Children
                        nextWorld = label;

                    // Cell Children GRUPs (types 6-10) have the parent CELL's formId as label.
                    // Look up the CELL's XCLC from cellIndex so LANDs inside get correct coordinates,
                    // even when multiple CELLs are clustered before their Cell Children GRUPs.
                    // IMPORTANT: Save and restore stream position because GetRecordAtOffset/GetSubRecords seek the stream.
                    if (groupType >= 6 && groupType <= 10)
                    {
                        long savedPos = _stream.Position;
                        if (cellIndex.TryGetValue(label, out var cellEntry))
                        {
                            var cellRecord = GetRecordAtOffset(cellEntry.Offset);
                            var subs = GetSubRecords(cellRecord);
                            foreach (var sub in subs)
                            {
                                if (sub.Type == "XCLC" && sub.Data.Length >= 8)
                                {
                                    lastCellX = BitConverter.ToInt32(sub.Data, 0);
                                    lastCellY = BitConverter.ToInt32(sub.Data, 4);
                                    break;
                                }
                            }
                        }
                        else if (currentWorld == worldFormId)
                        {
                            GD.Print($"[GRUP] CellChildren label=0x{label:X8} NOT FOUND in cellIndex for world 0x{worldFormId:X8}");
                        }
                        _stream.Position = savedPos;
                    }

                    long grupEnd = startOffset + size;
                    if (grupEnd > _stream.Length) grupEnd = _stream.Length;

                    _stream.Seek(8, SeekOrigin.Current);
                    TraverseForLandCoords(grupEnd, landIndex, cellIndex, worldFormId,
                        coordMap, nextWorld, lastCellX, lastCellY);
                    _stream.Position = grupEnd;
                }
                else
                {
                    uint flags = _reader.ReadUInt32();
                    uint formId = _reader.ReadUInt32();
                    long dataPos = startOffset + 24;

                    if (currentWorld == worldFormId)
                    {
                        if (type == "CELL")
                        {
                            _cellSeenInWorld++;
                            // Read XCLC via GetSubRecords for reliability
                            var cellRecord = GetRecordAtOffset(startOffset);
                            var subs = GetSubRecords(cellRecord);
                            foreach (var sub in subs)
                            {
                                if (sub.Type == "XCLC" && sub.Data.Length >= 8)
                                {
                                    lastCellX = BitConverter.ToInt32(sub.Data, 0);
                                    lastCellY = BitConverter.ToInt32(sub.Data, 4);
                                    break;
                                }
                            }
                        }
                        else if (type == "LAND" && landIndex.ContainsKey(formId))
                        {
                            _landSeenInWorld++;
                            coordMap[formId] = (lastCellX, lastCellY);
                        }
                    }

                    long nextOffset = startOffset + 24 + size;
                    if (nextOffset > _stream.Length) break;
                    _stream.Position = nextOffset;
                }
            }
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