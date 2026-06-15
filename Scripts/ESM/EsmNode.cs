using System.Collections.Generic;

public class EsmNode
{
	public string Type;

	public uint Size;
	public uint FormId;

	public uint Label;
	public uint GroupType;

	public List<SubRecord> SubRecords = new();

	public List<EsmNode> Children = new();
}
