using System.IO;

public static class GamePaths
{
  public const string Fallout3Root =
	@"A:\SteamLibrary\steamapps\common\Fallout 3 goty";

  public static string DataPath =>
	Path.Combine(Fallout3Root, "Data");

  public static string EsmPath =>
	Path.Combine(DataPath, "Fallout3.esm");
}
