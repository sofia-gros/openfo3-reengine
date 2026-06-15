using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Godot;

namespace OpenFo3.ESM
{
	public class EsmParser
	{
		public List<EsmNode> AllRecords { get; } = new List<EsmNode>();

		public void Parse(string path)
		{
			AllRecords.Clear();
			
			using (var br = new BinaryReader(File.OpenRead(path)))
			{
				ReadNodes(br, br.BaseStream.Length);
			}

			int refrCount = 0;
			foreach(var n in AllRecords) if(n.Type == "REFR") refrCount++;

			// GD.Print($"[EsmParser] Parsing Complete. Total Records: {AllRecords.Count}, Total REFRs: {refrCount}");
		}

		private void ReadNodes(BinaryReader br, long end)
		{
			while (br.BaseStream.Position < end)
			{
				long start = br.BaseStream.Position;
				if (end - start < 24) break;

				string type = Encoding.ASCII.GetString(br.ReadBytes(4));
				uint size = br.ReadUInt32();

				if (type == "GRUP")
				{
					uint label = br.ReadUInt32();
					uint groupType = br.ReadUInt32();
					br.ReadBytes(8); 

					long grupEnd = start + size; 
					ReadNodes(br, grupEnd);
					br.BaseStream.Position = grupEnd; 
				}
				else
				{
					uint flags = br.ReadUInt32();
					uint formId = br.ReadUInt32();
					br.ReadBytes(8); 

					long recordDataEnd = start + 24 + size; 

					byte[] recordData;
					if ((flags & 0x00040000) != 0) 
					{
						uint uncompressedSize = br.ReadUInt32();
						byte[] compressedData = br.ReadBytes((int)size - 4);
						try
						{
							using var ms = new MemoryStream(compressedData);
							using var zs = new ZLibStream(ms, CompressionMode.Decompress);
							using var decompressedMs = new MemoryStream();
							zs.CopyTo(decompressedMs);
							recordData = decompressedMs.ToArray();
						}
						catch
						{
							recordData = Array.Empty<byte>();
						}
					}
					else
					{
						recordData = br.ReadBytes((int)size);
					}

					var subRecords = ParseSubRecords(recordData);

					var node = new EsmNode
					{
						Type = type,
						Size = size,
						FormId = formId,
						SubRecords = subRecords
					};

					AllRecords.Add(node);
					br.BaseStream.Position = recordDataEnd; 
				}
			}
		}

		private List<SubRecord> ParseSubRecords(byte[] data)
		{
			var subRecords = new List<SubRecord>();
			if (data == null || data.Length == 0) return subRecords;

			using var ms = new MemoryStream(data);
			using var br = new BinaryReader(ms);

			while (ms.Position < ms.Length)
			{
				if (ms.Length - ms.Position < 6) break;

				string type = Encoding.ASCII.GetString(br.ReadBytes(4));
				ushort size = br.ReadUInt16();

				if (ms.Position + size > ms.Length) break;

				byte[] subData = br.ReadBytes(size);
				subRecords.Add(new SubRecord { Type = type, Data = subData });
			}

			return subRecords;
		}
	}
}
