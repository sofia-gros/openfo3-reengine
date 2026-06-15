using System.Collections.Generic;

public class CellExtractor
{
	public List<EsmNode> FindCells(List<EsmNode> nodes)
	{
		List<EsmNode> result = new();

		Find(nodes, result);

		return result;
	}

	private void Find(List<EsmNode> nodes, List<EsmNode> result)
	{
		foreach (var n in nodes)
		{
			if (n.Type == "CELL")
			{
				result.Add(n);
			}

			if (n.Children != null && n.Children.Count > 0)
			{
				Find(n.Children, result);
			}
		}
	}
}
