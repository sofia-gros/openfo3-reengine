using System;
using System.IO;
using System.Collections.Generic;
public class GroupRecord : Record
{
	public List<object> Children = new();

	public static GroupRecord Read(BinaryReader br, uint size)
	{
		var g = new GroupRecord();

		uint label = br.ReadUInt32();
		uint groupType = br.ReadUInt32();
		br.ReadBytes(12);

		long end = br.BaseStream.Position + (size - 20);

		while (br.BaseStream.Position < end)
		{
			string type = new(br.ReadChars(4));
			uint childSize = br.ReadUInt32();

			if (type == "GRUP")
			{
				br.BaseStream.Position -= 8;
				g.Children.Add(Read(br, childSize));
			}
			else
			{
				g.Children.Add(Record.Read(br, type, childSize));
			}
		}

		return g;
	}
}
