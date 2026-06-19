using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using OpenFo3.ESM;
using OpenFo3.NIF;
using OpenFo3.BSA;
using OpenFo3.World;

public partial class Megaton : Node3D
{
	private ConcurrentDictionary<string, ArrayMesh> _meshCache = new();
	private ConcurrentDictionary<string, NIFReader> _nifCache = new();
	private ConcurrentDictionary<string, Texture2D> _textureCache = new();

	private BSAReader _meshesBsa;
	private BSAReader _texturesBsa;
	private ESMReader _esm;

	private Dictionary<uint, RecordEntry> _masterFormIDIndex;
	private Dictionary<uint, RecordEntry> _refrFormIDIndex;
	private List<BSAFile> _meshFiles;
	private List<BSAFile> _textureFiles;

	private const int MaxObjectsToLoad = 5000;
	private const float WorldScale = 0.015f;

	private ConcurrentQueue<InstanceRequest> _instantiateQueue = new();
	private Vector2 _megatonCenter = new Vector2(-14200f, -3800f);
	private uint _megatonWorldId;

	private LightingLoader _lightingLoader;
	private TerrainBuilder _terrainBuilder;

	private struct InstanceRequest
	{
		public string Path;
		public Vector3 Position;
		public Vector3 Rotation;
		public uint FormId;
	}

	public override void _Process(double delta)
	{
		const int MaxPerFrame = 100;
		for (int i = 0; i < MaxPerFrame && _instantiateQueue.TryDequeue(out var req); i++)
		{
			CreateAndAddInstance(req);
		}
	}

	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		try
		{
			string meshesBsaPath = Path.Combine(GamePaths.DataPath, "Fallout - Meshes.bsa");
			_meshesBsa = new BSAReader(meshesBsaPath);
			_meshFiles = _meshesBsa.ExtractFileList();

			string texturesBsaPath = Path.Combine(GamePaths.DataPath, "Fallout - Textures.bsa");
			if (File.Exists(texturesBsaPath))
			{
				_texturesBsa = new BSAReader(texturesBsaPath);
				_textureFiles = _texturesBsa.ExtractFileList();
				GD.Print($"[Megaton] Loaded Textures BSA: {_textureFiles.Count} files.");
			}

			_esm = new ESMReader(GamePaths.EsmPath);
			_masterFormIDIndex = _esm.BuildFormIdIndex(new[]
			{
				"STAT",
				"DOOR",
				//"FURN",
				// "ACTI",
				// "MSTT",
				// "LIGH",
				// "TERM",
				// "CONT",
				// "MISC",
				// "WEAP",
				// "ARMO",
				// "CLOT",
				//"TREE",
				//"ALCH",
				//"INGR",
				//"BOOK",
				//"GRAS",
				//"LAND",
				//"DEBR",
				//"SCOL"
			});

			_refrFormIDIndex = _esm.BuildFormIdIndex(new[] { "REFR" });

			_lightingLoader = new LightingLoader(_esm);
			_terrainBuilder = new TerrainBuilder(_esm);

			float defaultLandHeight = 0;
			int nwCellX = 0, nwCellY = 0, seCellX = 0, seCellY = 0;

			uint megatonWorldId = 0;
			var wrldIndex = _esm.BuildFormIdIndex(new[] { "WRLD" });
			foreach (var kvp in wrldIndex)
			{
				var rec = _esm.GetRecordAtOffset(kvp.Value.Offset);
				var subs = _esm.GetSubRecords(rec);
				var edid = subs.FirstOrDefault(s => s.Type == "EDID");
				if (edid != null)
				{
					string name = Encoding.ASCII.GetString(edid.Data).TrimEnd('\0');
					if (name == "MegatonWorld")
					{
						megatonWorldId = rec.FormId;
						GD.Print($"[Megaton] Found MegatonWorld: 0x{megatonWorldId:X8}");

						var dnam = subs.FirstOrDefault(s => s.Type == "DNAM");
						if (dnam != null && dnam.Data.Length >= 8)
						{
							defaultLandHeight = BitConverter.ToSingle(dnam.Data, 0);
							GD.Print($"[Megaton] MegatonWorld Default Land Height = {defaultLandHeight}");
						}

						var mnam = subs.FirstOrDefault(s => s.Type == "MNAM");
						if (mnam != null && mnam.Data.Length >= 16)
						{
							nwCellX = BitConverter.ToInt16(mnam.Data, 8);
							nwCellY = BitConverter.ToInt16(mnam.Data, 10);
							seCellX = BitConverter.ToInt16(mnam.Data, 12);
							seCellY = BitConverter.ToInt16(mnam.Data, 14);
						}

						// Ensure minimum terrain coverage: at least 20x20 cells centered on (0,0)
						const int minHalf = 10;
						int centerX = (nwCellX + seCellX) / 2;
						int centerY = (nwCellY + seCellY) / 2;
						int halfExtX = Math.Max((seCellX - nwCellX) / 2, minHalf) + 5;
						int halfExtY = Math.Max((seCellY - nwCellY) / 2, minHalf) + 5;
						nwCellX = centerX - halfExtX;
						nwCellY = centerY - halfExtY;
						seCellX = centerX + halfExtX;
						seCellY = centerY + halfExtY;
						GD.Print($"[Megaton] MegatonWorld bounds (expanded): NW=({nwCellX},{nwCellY}) SE=({seCellX},{seCellY})");

						break;
					}
				}
			}

			if (megatonWorldId != 0)
			{
				_megatonWorldId = megatonWorldId;

				// Load terrain synchronously before async world load
				LoadTerrain(megatonWorldId, defaultLandHeight, nwCellX, nwCellY, seCellX, seCellY);

				// Load cell lighting
				LoadCellLighting(megatonWorldId);

				_ = Task.Run(() => LoadWorldAsync(megatonWorldId));
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Init error: {e.Message}");
		}
	}

	private void LoadTerrain(uint worldId, float defaultLandHeight,
		int nwCellX, int nwCellY, int seCellX, int seCellY)
	{
		try
		{
			var tiles = _terrainBuilder.BuildTerrainForWorld(worldId, _megatonCenter, LoadTexture,
				defaultLandHeight, nwCellX, nwCellY, seCellX, seCellY);
			GD.Print($"[Megaton] Loaded {tiles.Count} terrain tiles.");

			foreach (var tile in tiles)
			{
				var name = $"Terrain_{tile.CellCoord.X}_{tile.CellCoord.Y}";

				var inst = new MeshInstance3D();
				inst.Mesh = tile.Mesh;
				inst.Name = name;
				AddChild(inst);

				if (tile.CollisionShape != null)
				{
					var body = new StaticBody3D();
					body.Name = $"{name}_Collision";
					var colShape = new CollisionShape3D();
					colShape.Shape = tile.CollisionShape;
					body.AddChild(colShape);
					AddChild(body);
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Terrain load error: {e.Message}");
		}
	}

	private void LoadCellLighting(uint worldId)
	{
		try
		{
			// Find the persistent cell for Megaton
			var cellIndex = _esm.BuildFormIdIndex(new[] { "CELL" });
			foreach (var kvp in cellIndex)
			{
				if (kvp.Value.WorldFormId != worldId) continue;

				var lighting = _lightingLoader.GetCellLighting(kvp.Key);
				if (lighting != null)
				{
					GD.Print($"[Megaton] Found lighting for CELL 0x{kvp.Key:X8}");
					LightingLoader.ApplyCellLighting(this, lighting);
					break;
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Lighting load error: {e.Message}");
		}
	}

	private async Task LoadWorldAsync(uint targetWorldId)
	{
		var refrsToProcess = new List<long>();
		foreach (var kvp in _refrFormIDIndex)
		{
			if (kvp.Value.WorldFormId == targetWorldId)
				refrsToProcess.Add(kvp.Value.Offset);
		}

		GD.Print($"[Megaton] Found {refrsToProcess.Count} REFRs in Megaton hierarchy.");

		await Task.Run(() =>
		{
			Parallel.ForEach(refrsToProcess, offset =>
			{
				ProcessRecord(offset);
			});
		});

		GD.Print($"[Megaton] Parsing done. Queue: {_instantiateQueue.Count}");
	}

	private void ProcessRecord(long offset)
	{
		try
		{
			ESMRecord record;
			List<SubRecord> subs;

			lock (_esm)
			{
				record = _esm.GetRecordAtOffset(offset);
				subs = _esm.GetSubRecords(record);
			}

			var dataSub = subs.FirstOrDefault(s => s.Type == "DATA");
			var nameSub = subs.FirstOrDefault(s => s.Type == "NAME");
			if (dataSub == null || nameSub == null) return;

			uint formId = BitConverter.ToUInt32(nameSub.Data, 0);

			RecordEntry baseEntry;
			string nifPath;

			lock (_esm)
			{
				if (!_masterFormIDIndex.TryGetValue(formId, out baseEntry)) return;
				var baseRecord = _esm.GetRecordAtOffset(baseEntry.Offset);
				var baseSubs = _esm.GetSubRecords(baseRecord);
				var modl = baseSubs.FirstOrDefault(s => s.Type == "MODL");
				if (modl == null) return;
				nifPath = Encoding.ASCII.GetString(modl.Data).TrimEnd('\0').Replace('\\', '/');
			}

			if (!nifPath.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase)) nifPath = "meshes/" + nifPath;

			// Skip editor/debug markers (root-level Marker*.nif) and effect/light/shadow helpers
			string nifLower = nifPath.ToLowerInvariant();
			string fname = System.IO.Path.GetFileName(nifLower);
			bool isDebug =
				nifLower.StartsWith("meshes/marker") ||           // meshes/MarkerX.nif, Marker_Map.nif etc.
				nifLower.Contains("editormarker") ||              // meshes/.../EditorMarker.nif
				fname.Contains("shadow") ||                       // Shadow volume meshes
				fname.StartsWith("cone") ||                       // Cone-shaped debug helpers
				(nifLower.Contains("/effects/") && fname.StartsWith("fxlight")); // FXLightBeam*.NIF

			if (isDebug)
			{
				GD.Print($"[Megaton] Skipping debug marker: {nifPath}");
				return;
			}

			EnsureNifParsed(nifPath);

			float px = BitConverter.ToSingle(dataSub.Data, 0);
			float py = BitConverter.ToSingle(dataSub.Data, 4);
			float pz = BitConverter.ToSingle(dataSub.Data, 8);
			float rx = BitConverter.ToSingle(dataSub.Data, 12);
			float ry = BitConverter.ToSingle(dataSub.Data, 16);
			float rz = BitConverter.ToSingle(dataSub.Data, 20);

			_instantiateQueue.Enqueue(new InstanceRequest
			{
				Path = nifPath,
				Position = new Vector3((px - _megatonCenter.X) * WorldScale, pz * WorldScale, -(py - _megatonCenter.Y) * WorldScale),
				Rotation = new Vector3(rx, ry, rz),
				FormId = formId,
			});
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Error processing record at 0x{offset:X8}: {e.Message}");
		}
	}

	private void EnsureNifParsed(string path)
	{
		if (_nifCache.ContainsKey(path)) return;

		var file = _meshFiles.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
		if (file == null) return;

		byte[] nifData = _meshesBsa.ReadFileData(file);
		if (nifData == null) return;

		var nif = new NIFReader();
		nif.Parse(nifData);

		if (nif.Blocks.Count > 0)
		{
			_nifCache.TryAdd(path, nif);
		}
	}

	private void CreateAndAddInstance(InstanceRequest req)
	{
		var mesh = GetOrBuildMesh(req.Path);
		if (mesh == null) return;

		var inst = new MeshInstance3D { Mesh = mesh };

		// FO3 Intrinsic ZYX -> Godot: build R_Right(rx) * R_Forward(ry) * R_Up(rz)
		// Axis mapping: FO3 X->Godot X(Right), FO3 Y->Godot -Z(Forward), FO3 Z->Godot Y(Up)
		var basis = Basis.Identity;
		basis = basis.Rotated(Vector3.Up,      -req.Rotation.Z); // FO3 RotZ -> Godot Yaw
		basis = basis.Rotated(Vector3.Forward,  req.Rotation.Y); // FO3 RotY -> Godot Roll
		basis = basis.Rotated(Vector3.Right,    req.Rotation.X); // FO3 RotX -> Godot Pitch

		inst.Transform = new Transform3D(basis, req.Position);
		AddChild(inst);

		// Build collision if NIF has bhk blocks
		if (_nifCache.TryGetValue(req.Path, out var nif))
		{
			NIFCollisionBuilder.BuildCollision(nif, inst);
		}

		// Create light if base object is LIGH
		if (IsLightRef(req.FormId))
		{
			CreateLightForRef(req);
		}
	}

	private bool IsLightRef(uint formId)
	{
		string baseType = GetBaseObjectType(formId);
		return baseType == "LIGH";
	}

	private string GetBaseObjectType(uint formId)
	{
		if (_masterFormIDIndex.TryGetValue(formId, out var entry))
		{
			lock (_esm)
			{
				var rec = _esm.GetRecordAtOffset(entry.Offset);
				return rec.Type;
			}
		}
		return null;
	}

	private void CreateLightForRef(InstanceRequest req)
	{
		var lightData = _lightingLoader.ParseLight(req.FormId);
		if (lightData == null) return;

		var lightNode = LightingLoader.CreateLightNode(lightData, req.Position, req.Rotation);
		if (lightNode != null)
		{
			AddChild(lightNode);
		}
	}

	private ArrayMesh GetOrBuildMesh(string path)
	{
		if (_meshCache.TryGetValue(path, out var cached)) return cached;

		if (!_nifCache.TryGetValue(path, out var nif)) return null;

		var geom = NIFMeshBuilder.ExtractGeometry(nif);
		if (geom.Surfaces.Count == 0) return null;

		var mesh = NIFMeshBuilder.BuildArrayMesh(geom);
		if (mesh.GetSurfaceCount() > 0)
		{
			for (int i = 0; i < mesh.GetSurfaceCount(); i++)
			{
				string texPath = mesh.SurfaceGetName(i);

				// Use the new material builder with full shader info
				if (geom.Surfaces.Count > i)
				{
					var surface = geom.Surfaces[i];
					var mat = NIFMaterialBuilder.BuildMaterial(surface.Shader, surface.Alpha, LoadTexture);

					// Fallback: if no material was built, try simple texture
					if (mat.AlbedoTexture == null && !string.IsNullOrEmpty(texPath))
					{
						var tex = LoadTexture(texPath);
						if (tex != null)
						{
							mat.AlbedoTexture = tex;
						}
					}

					// RenderPriority forces consistent draw order matching NIF hierarchy,
					// preventing z-fighting between overlapping sibling meshes
					// (e.g. refrigerator body and door, table and drawers).
					mat.RenderPriority = i;

					mesh.SurfaceSetMaterial(i, mat);
				}
				else if (!string.IsNullOrEmpty(texPath))
				{
					var tex = LoadTexture(texPath);
					if (tex != null)
					{
						var mat = new StandardMaterial3D { AlbedoTexture = tex };
						mat.RenderPriority = i;
						mesh.SurfaceSetMaterial(i, mat);
					}
				}
			}

			_meshCache.TryAdd(path, mesh);
			return mesh;
		}

		return null;
	}

	private Texture2D LoadTexture(string path)
	{
		if (string.IsNullOrEmpty(path)) return null;

		path = path.Replace('\\', '/');

		string searchPath = path;
		if (!searchPath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
			searchPath = "textures/" + searchPath;

		if (_textureCache.TryGetValue(searchPath, out var cached)) return cached;

		if (_texturesBsa == null) return null;

		var file = _textureFiles.FirstOrDefault(f => f.Path.Equals(searchPath, StringComparison.OrdinalIgnoreCase));

		if (file == null && searchPath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
		{
			string altPath = searchPath.Substring(9);
			file = _textureFiles.FirstOrDefault(f => f.Path.Equals(altPath, StringComparison.OrdinalIgnoreCase));
		}

		if (file == null) return null;

		byte[] data = _texturesBsa.ReadFileData(file);
		if (data == null) return null;

		var img = new Image();
		Error err = img.LoadDdsFromBuffer(data);
		if (err == Error.Ok)
		{
			var tex = ImageTexture.CreateFromImage(img);
			_textureCache.TryAdd(searchPath, tex);
			return tex;
		}
		else
		{
			GD.PrintErr($"[Megaton] Failed to load DDS texture: {searchPath} (Error: {err})");
			return null;
		}
	}
}
