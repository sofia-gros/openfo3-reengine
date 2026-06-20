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

		private List<BSAReader> _bsaReaders = new();
		private BSAReader _texturesBsa;
		private ESMReader _esm;

		private Dictionary<uint, RecordEntry> _masterFormIDIndex;
		private Dictionary<uint, RecordEntry> _refrFormIDIndex;

		// Per-BSA file lists for efficient lookup
		private List<(BSAReader BSA, List<BSAFile> Files)> _meshBsaList = new();
		private List<(BSAReader BSA, List<BSAFile> Files)> _textureBsaList = new();

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
		public string BaseType;
		public float Scale;
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
			// Load all BSA archives from config
			string[] bsaPaths = GamePaths.GetBSAFilePaths();
			foreach (string bsaPath in bsaPaths)
			{
				try
				{
					var bsa = new BSAReader(bsaPath);
					var files = bsa.ExtractFileList();
					string name = System.IO.Path.GetFileNameWithoutExtension(bsaPath);

					if (name.IndexOf("mesh", StringComparison.OrdinalIgnoreCase) >= 0 ||
						name.IndexOf("misc", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						_meshBsaList.Add((bsa, files));
					}
					if (name.IndexOf("texture", StringComparison.OrdinalIgnoreCase) >= 0 ||
						name.IndexOf("misc", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						_textureBsaList.Add((bsa, files));
					}

					_bsaReaders.Add(bsa);
					GD.Print($"[Megaton] Loaded BSA: {name} ({files.Count} files)");
				}
				catch (Exception e)
				{
					GD.PrintErr($"[Megaton] Failed to load BSA {bsaPath}: {e.Message}");
				}
			}

			// Retain a direct reference to textures BSA for backward compat
			_texturesBsa = _bsaReaders.FirstOrDefault(b =>
				System.IO.Path.GetFileNameWithoutExtension(b.FilePath)
					.IndexOf("texture", StringComparison.OrdinalIgnoreCase) >= 0);

			_esm = new ESMReader(GamePaths.EsmPath);
			_masterFormIDIndex = _esm.BuildFormIdIndex(new[]
			{
				"STAT",
				"DOOR",
				"FURN",
				"ACTI",
				"MSTT",
				"LIGH",
				"TERM",
				"CONT",
				"MISC",
				"WEAP",
				"ARMO",
				"CLOT",
				"TREE",
				"ALCH",
				"INGR",
				"BOOK",
				"GRAS",
				"LAND",
				"DEBR",
				"SCOL",
				"KEYM",
				"ARMA",
				"NOTE",
				"PWAT",
				"TACT",
				"AMMO"
			});

			_refrFormIDIndex = _esm.BuildFormIdIndex(new[] { "REFR" });

			_lightingLoader = new LightingLoader(_esm);
			_terrainBuilder = new TerrainBuilder(_esm);

			float defaultLandHeight = 0;
			int nwCellX = 0, nwCellY = 0, seCellX = 0, seCellY = 0;

			uint megatonWorldId = 0;
			_megatonCenter = GamePaths.GetWorldCenter();
			string targetWorldName = GamePaths.GetTargetWorld();
			var wrldIndex = _esm.BuildFormIdIndex(new[] { "WRLD" });
			foreach (var kvp in wrldIndex)
			{
				var rec = _esm.GetRecordAtOffset(kvp.Value.Offset);
				var subs = _esm.GetSubRecords(rec);
				var edid = subs.FirstOrDefault(s => s.Type == "EDID");
				if (edid != null)
				{
					string name = Encoding.ASCII.GetString(edid.Data).TrimEnd('\0');
					if (name == targetWorldName)
					{
						megatonWorldId = rec.FormId;
						GD.Print($"[Megaton] Found world '{targetWorldName}': 0x{megatonWorldId:X8}");

						var dnam = subs.FirstOrDefault(s => s.Type == "DNAM");
						if (dnam != null && dnam.Data.Length >= 8)
						{
							defaultLandHeight = BitConverter.ToSingle(dnam.Data, 0);
							GD.Print($"[Megaton] World '{targetWorldName}' Default Land Height = {defaultLandHeight}");
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
						GD.Print($"[Megaton] World '{targetWorldName}' bounds (expanded): NW=({nwCellX},{nwCellY}) SE=({seCellX},{seCellY})");

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
			string nifPath = null;
			string baseType = null;
			List<SubRecord> baseSubs = null;

			lock (_esm)
			{
				if (!_masterFormIDIndex.TryGetValue(formId, out baseEntry)) return;
				var baseRecord = _esm.GetRecordAtOffset(baseEntry.Offset);
				baseType = baseRecord.Type;
				baseSubs = _esm.GetSubRecords(baseRecord);
				var modl = baseSubs.FirstOrDefault(s => s.Type == "MODL");
				if (modl != null)
				{
					nifPath = Encoding.ASCII.GetString(modl.Data).TrimEnd('\0').Replace('\\', '/');
				}
			}

			// Light-only objects (no mesh) still need a light node created
			if (nifPath == null && baseType != "LIGH") return;

			if (nifPath != null)
			{
				if (!nifPath.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase))
					nifPath = "meshes/" + nifPath;

				string nifLower = nifPath.ToLowerInvariant();
				string fname = System.IO.Path.GetFileName(nifLower);
				bool isDebug =
					nifLower.StartsWith("meshes/marker") ||
					nifLower.Contains("editormarker") ||
					fname.Contains("shadow") ||
					fname.StartsWith("cone") ||
					(nifLower.Contains("/effects/") && fname.StartsWith("fxlight"));

				if (isDebug)
				{
					GD.Print($"[Megaton] Skipping debug marker: {nifPath}");
					return;
				}

				EnsureNifParsed(nifPath);
			}

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
				BaseType = baseType,
				Scale = 1f,
			});

			// Emit SCOL part instances (ONAM + DATA)
			if (baseType == "SCOL")
			{
				// Re-read the SCOL base record subrecords (already in baseSubs)
				EmitScolParts(baseSubs, formId, px, py, pz, rx, ry, rz);
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Error processing record at 0x{offset:X8}: {e.Message}");
		}
	}

	private void EmitScolParts(List<SubRecord> baseSubs, uint scolFormId,
		float scolPx, float scolPy, float scolPz,
		float scolRx, float scolRy, float scolRz)
	{
		// SCOL parts are pairs of (ONAM, DATA) subrecords.
		// ONAM: uint formId of a STAT record.
		// DATA: array of 7-float placements (X,Y,Z, RX,RY,RZ, Scale).
		int i = 0;
		while (i < baseSubs.Count)
		{
			var sub = baseSubs[i];
			if (sub.Type == "ONAM")
			{
				if (sub.Data.Length < 4) { i++; continue; }
				uint statFormId = BitConverter.ToUInt32(sub.Data, 0);

				// Resolve STAT formId -> MODL path
				string partPath = null;
				lock (_esm)
				{
					if (_masterFormIDIndex.TryGetValue(statFormId, out var statEntry))
					{
						var statRec = _esm.GetRecordAtOffset(statEntry.Offset);
						var statSubs = _esm.GetSubRecords(statRec);
						var modl = statSubs.FirstOrDefault(s => s.Type == "MODL");
						if (modl != null)
						{
							partPath = Encoding.ASCII.GetString(modl.Data).TrimEnd('\0').Replace('\\', '/');
							if (!partPath.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase))
								partPath = "meshes/" + partPath;
						}
					}
				}

				if (partPath == null) { i++; continue; }

				// The DATA subrecord follows ONAM
				if (i + 1 >= baseSubs.Count || baseSubs[i + 1].Type != "DATA")
				{
					GD.Print($"[Megaton] SCOL part 0x{statFormId:X8} missing DATA, skipping");
					i++;
					continue;
				}

				var dataSub = baseSubs[i + 1];
				i += 2; // consume both ONAM and DATA

				if (dataSub.Data.Length < 28) continue;

				// Each placement is 7 floats (28 bytes). Multiple placements can exist.
				int numPlacements = dataSub.Data.Length / 28;
				for (int p = 0; p < numPlacements; p++)
				{
					int off = p * 28;
					float lx = BitConverter.ToSingle(dataSub.Data, off);
					float ly = BitConverter.ToSingle(dataSub.Data, off + 4);
					float lz = BitConverter.ToSingle(dataSub.Data, off + 8);
					float lrx = BitConverter.ToSingle(dataSub.Data, off + 12);
					float lry = BitConverter.ToSingle(dataSub.Data, off + 16);
					float lrz = BitConverter.ToSingle(dataSub.Data, off + 20);
					float scale = BitConverter.ToSingle(dataSub.Data, off + 24);

					// Combine FO3 transforms: world = scol_world + scol_rot * local
					// The SCOL may have rotation; build a rotation matrix in FO3 space.
					// FO3 Euler order: Z (up) * Y * X
					float cosRz = (float)Math.Cos(scolRz), sinRz = (float)Math.Sin(scolRz);
					float cosRy = (float)Math.Cos(scolRy), sinRy = (float)Math.Sin(scolRy);
					float cosRx = (float)Math.Cos(scolRx), sinRx = (float)Math.Sin(scolRx);

					// FO3 rotation matrix: Z * Y * X
					float r00 = cosRz * cosRy;
					float r01 = cosRz * sinRy * sinRx - sinRz * cosRx;
					float r02 = cosRz * sinRy * cosRx + sinRz * sinRx;
					float r10 = sinRz * cosRy;
					float r11 = sinRz * sinRy * sinRx + cosRz * cosRx;
					float r12 = sinRz * sinRy * cosRx - cosRz * sinRx;
					float r20 = -sinRy;
					float r21 = cosRy * sinRx;
					float r22 = cosRy * cosRx;

					float wx = scolPx + r00 * lx + r01 * ly + r02 * lz;
					float wy = scolPy + r10 * lx + r11 * ly + r12 * lz;
					float wz = scolPz + r20 * lx + r21 * ly + r22 * lz;

					// Combined rotation (approximate): scolRot * localRot
					// For simplicity, use the local rotation relative to SCOL.
					// FO3 Euler: localRot applied after scolRot in the local space.
					// We emit a separate instance with its own rotation in Godot space.
					float combinedRx = scolRx + lrx;
					float combinedRy = scolRy + lry;
					float combinedRz = scolRz + lrz;

					_instantiateQueue.Enqueue(new InstanceRequest
					{
						Path = partPath,
						Position = new Vector3(
							(wx - _megatonCenter.X) * WorldScale,
							wz * WorldScale,
							-(wy - _megatonCenter.Y) * WorldScale),
						Rotation = new Vector3(combinedRx, combinedRy, combinedRz),
						FormId = statFormId,
						BaseType = "STAT",
						Scale = scale,
					});
				}
			}
			else
			{
				i++;
			}
		}
	}

	private void EnsureNifParsed(string path)
	{
		if (_nifCache.ContainsKey(path)) return;

		// Search across all BSAs for the file by scanning combined mesh file list
		BSAFile match = null;
		BSAReader owner = null;
		for (int i = 0; i < _meshBsaList.Count && owner == null; i++)
		{
			var (bsa, files) = _meshBsaList[i];
			match = files.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
			if (match != null) owner = bsa;
		}

		if (owner == null) return;

		byte[] nifData = owner.ReadFileData(match);
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
		MeshInstance3D inst = null;

		if (!string.IsNullOrEmpty(req.Path))
		{
			var mesh = GetOrBuildMesh(req.Path);
			if (mesh == null && req.BaseType != "LIGH") return;

			if (mesh != null)
			{
				inst = new MeshInstance3D { Mesh = mesh };

				var basis = Basis.Identity;
				basis = basis.Rotated(Vector3.Up,      -req.Rotation.Z);
				basis = basis.Rotated(Vector3.Forward,  req.Rotation.Y);
				basis = basis.Rotated(Vector3.Right,    req.Rotation.X);
				basis = basis.Scaled(Vector3.One * req.Scale);

				inst.Transform = new Transform3D(basis, req.Position);
				AddChild(inst);

				if (_nifCache.TryGetValue(req.Path, out var nif))
				{
					NIFCollisionBuilder.BuildCollision(nif, inst);
				}
			}
		}

		if (req.BaseType == "LIGH")
		{
			CreateLightForRef(req);
		}
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

		if (_textureBsaList.Count == 0) return null;

		// Search across all texture BSAs for this file
		BSAFile file = null;
		BSAReader owner = null;
		foreach (var (bsa, files) in _textureBsaList)
		{
			file = files.FirstOrDefault(f => f.Path.Equals(searchPath, StringComparison.OrdinalIgnoreCase));
			if (file != null) { owner = bsa; break; }
		}

		// Try without "textures/" prefix
		if (file == null && searchPath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
		{
			string altPath = searchPath.Substring(9);
			foreach (var (bsa, files) in _textureBsaList)
			{
				file = files.FirstOrDefault(f => f.Path.Equals(altPath, StringComparison.OrdinalIgnoreCase));
				if (file != null) { owner = bsa; break; }
			}
		}

		if (file == null || owner == null) return null;

		byte[] data = owner.ReadFileData(file);
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
