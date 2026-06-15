using System;
using System.IO;
using System.Collections.Generic;

public class Record
{
	public string Type;
	public uint FormId;
	public List<SubRecord> SubRecords = new();

	public static Record Read(BinaryReader br, string type, uint size)
	{
		var r = new Record();
		r.Type = type;

		r.FormId = br.ReadUInt32();
		br.ReadUInt32(); // flags
		br.ReadUInt32(); // version

		long end = br.BaseStream.Position + (size - 12);

		while (br.BaseStream.Position < end)
		{
			string subType = new(br.ReadChars(4));
			ushort subSize = br.ReadUInt16();
			byte[] data = br.ReadBytes(subSize);

			r.SubRecords.Add(new SubRecord
			{
				Type = subType,
				Data = data
			});
		}

		return r;
	}
}
