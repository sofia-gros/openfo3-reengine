using System;
using System.Collections.Generic;

public class RefrExtractor
{
	public List<Refr> Extract(EsmNode cell)
	{
		var result = new List<Refr>();

		Walk(cell, result);

		return result;
	}

	private void Walk(EsmNode node, List<Refr> result)
	{
		if (node.Type == "REFR")
		{
			var r = Parse(node);
			if (r != null)
				result.Add(r);
		}

		foreach (var c in node.Children)
		{
			Walk(c, result);
		}
	}

	private Refr Parse(EsmNode node)
	{
		var r = new Refr();
		r.FormId = node.FormId;

		foreach (var s in node.SubRecords)
		{
			switch (s.Type)
			{
				case "DATA":
					if (s.Data.Length >= 24)
					{
						r.X = BitConverter.ToSingle(s.Data, 0);
						r.Y = BitConverter.ToSingle(s.Data, 4);
						r.Z = BitConverter.ToSingle(s.Data, 8);
						r.RotX = BitConverter.ToSingle(s.Data, 12);
						r.RotY = BitConverter.ToSingle(s.Data, 16);
						r.RotZ = BitConverter.ToSingle(s.Data, 20);
					}
					else if (s.Data.Length >= 12)
					{
						r.X = BitConverter.ToSingle(s.Data, 0);
						r.Y = BitConverter.ToSingle(s.Data, 4);
						r.Z = BitConverter.ToSingle(s.Data, 8);
					}
					break;

				case "NAME":
					r.BaseFormId = BitConverter.ToUInt32(s.Data, 0);
					break;
			}
		}

		return r;
	}
}
