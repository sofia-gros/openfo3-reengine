using System;
using System.IO;
using System.Collections.Generic;

public class ESMFile
{
	public List<Record> Records = new();

	public void Load(string path)
	{
		using var br = new BinaryReader(File.OpenRead(path));

		while (br.BaseStream.Position < br.BaseStream.Length)
		{
			string type = new(br.ReadChars(4));
			uint size = br.ReadUInt32();

			if (type == "GRUP")
			{
				Records.Add(GroupRecord.Read(br, size));
			}
			else
			{
				Records.Add(Record.Read(br, type, size));
			}
		}
	}
}
