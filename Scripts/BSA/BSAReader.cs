using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Godot;

namespace OpenFo3.BSA
{
    public class BSAReader : IDisposable
    {
        private BinaryReader _reader;
        private Stream _stream;
        private readonly object _lock = new();

        public class Header
        {
            public uint FileID;
            public uint Version;
            public uint Offset;
            public uint ArchiveFlags;
            public uint FolderCount;
            public uint FileCount;
            public uint TotalFolderNameLength;
            public uint TotalFileNameLength;
            public uint FileFlags;
        }

        public Header BSAHeader { get; private set; }

        public BSAReader(string filePath)
        {
            _stream = File.OpenRead(filePath);
            _reader = new BinaryReader(_stream);
            ReadHeader();
        }

        private void ReadHeader()
        {
            BSAHeader = new Header
            {
                FileID = _reader.ReadUInt32(),
                Version = _reader.ReadUInt32(),
                Offset = _reader.ReadUInt32(),
                ArchiveFlags = _reader.ReadUInt32(),
                FolderCount = _reader.ReadUInt32(),
                FileCount = _reader.ReadUInt32(),
                TotalFolderNameLength = _reader.ReadUInt32(),
                TotalFileNameLength = _reader.ReadUInt32(),
                FileFlags = _reader.ReadUInt32()
            };

            if (BSAHeader.Version != 104 && BSAHeader.Version != 105)
            {
                // GD.PrintErr($"Unsupported BSA version: {BSAHeader.Version}");
            }
            else
            {
                // GD.Print($"[BSAReader] Loaded BSA Version {BSAHeader.Version}, Flags: 0x{BSAHeader.ArchiveFlags:X}");
            }
        }

        public List<BSAFile> ExtractFileList()
        {
            var files = new List<BSAFile>();

            // Folder Records
            var folderRecords = new List<FolderRecord>();
            for (int i = 0; i < BSAHeader.FolderCount; i++)
            {
                folderRecords.Add(new FolderRecord
                {
                    Hash = _reader.ReadUInt64(),
                    Count = _reader.ReadUInt32(),
                    Offset = _reader.ReadUInt32()
                });
            }

            // Folder details and File records
            var allFileRecords = new List<List<FileRecord>>();
            var folderNames = new List<string>();

            foreach (var folder in folderRecords)
            {
                // Each folder starts with its name (length-prefixed)
                byte nameLen = _reader.ReadByte();
                byte[] nameBytes = _reader.ReadBytes(nameLen - 1); // length includes null terminator?
                _reader.ReadByte(); // consume null terminator
                string folderName = Encoding.ASCII.GetString(nameBytes).Replace('\\', '/');
                folderNames.Add(folderName);

                var fileRecords = new List<FileRecord>();
                for (int i = 0; i < folder.Count; i++)
                {
                    fileRecords.Add(new FileRecord
                    {
                        Hash = _reader.ReadUInt64(),
                        Size = _reader.ReadUInt32(),
                        Offset = _reader.ReadUInt32()
                    });
                }
                allFileRecords.Add(fileRecords);
            }

            // File Names block
            var fileNames = new List<string>();
            for (int i = 0; i < BSAHeader.FileCount; i++)
            {
                var sb = new StringBuilder();
                char c;
                while ((c = (char)_reader.ReadByte()) != '\0')
                {
                    sb.Append(c);
                }
                fileNames.Add(sb.ToString());
            }

            // Combine into BSAFile objects
            int fileNameIdx = 0;
            for (int i = 0; i < folderNames.Count; i++)
            {
                foreach (var record in allFileRecords[i])
                {
                    files.Add(new BSAFile
                    {
                        Path = folderNames[i] + "/" + fileNames[fileNameIdx++],
                        Size = record.Size,
                        Offset = record.Offset,
                        Hash = record.Hash
                    });
                }
            }

            return files;
        }

        public byte[] ReadFileData(BSAFile file)
        {
            lock (_lock)
            {
                try
                {
                    _stream.Position = file.Offset;
                    
                    // DIAGNOSTIC: Print first 64 bytes
                    byte[] diag = _reader.ReadBytes(64);
                    _stream.Position = file.Offset;
                    string diagHex = "";
                    for(int d=0; d<64; d++) diagHex += $"{diag[d]:X2} ";
                    // GD.Print($"[BSAReader] DIAG RAW {file.Path}: {diagHex}");

                    uint rawSize = file.Size;
                    bool isCompressed = (BSAHeader.ArchiveFlags & 0x4) != 0;
                    
                    if ((rawSize & 0x40000000) != 0)
                    {
                        isCompressed = !isCompressed;
                        rawSize &= 0x3FFFFFFF;
                    }

                    if (isCompressed)
                    {
                        // FO3/v104 often has the uncompressed size (4 bytes) before the ZLib data.
                        // But sometimes there's a filename skip needed.
                        // Let's scavenger for the ZLib header (0x78) to be 100% sure.
                        long startOfBody = _stream.Position;
                        byte[] buffer = _reader.ReadBytes((int)Math.Min(rawSize, 512));
                        int zOffset = -1;
                        for (int i = 0; i < buffer.Length - 1; i++)
                        {
                            if (buffer[i] == 0x78 && (buffer[i+1] == 0x9C || buffer[i+1] == 0x01 || buffer[i+1] == 0xDA || buffer[i+1] == 0x5E))
                            {
                                zOffset = i;
                                break;
                            }
                        }

                        if (zOffset != -1)
                        {
                            _stream.Position = startOfBody + zOffset;
                            byte[] compressedData = _reader.ReadBytes((int)rawSize - zOffset);
                            
                            using (var ms = new MemoryStream(compressedData))
                            using (var resultMs = new MemoryStream())
                            using (var zs = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress))
                            {
                                zs.CopyTo(resultMs);
                                return resultMs.ToArray();
                            }
                        }
                        else
                        {
                            // If not found, maybe it's raw deflate or something else
                             _stream.Position = startOfBody;
                             // Just try standard offset (jump 4 bytes for uncompressed size)
                             _stream.Seek(4, SeekOrigin.Current);
                             byte[] compressedData = _reader.ReadBytes((int)rawSize - 4);
                             using (var ms = new MemoryStream(compressedData))
                             using (var resultMs = new MemoryStream())
                             using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
                             {
                                 ds.CopyTo(resultMs);
                                 return resultMs.ToArray();
                             }
                        }
                    }
                    else
                    {
                        return _reader.ReadBytes((int)rawSize);
                    }
                }
                catch (Exception e)
                {
                    // GD.PrintErr($"[BSAReader] FAILED ReadFileData for {file.Path}: {e.Message}");
                    return null;
                }
            }
        }

        private struct FolderRecord
        {
            public ulong Hash;
            public uint Count;
            public uint Offset;
        }

        private struct FileRecord
        {
            public ulong Hash;
            public uint Size;
            public uint Offset;
        }

        public void Dispose()
        {
            _reader?.Dispose();
            _stream?.Dispose();
        }
    }
}
