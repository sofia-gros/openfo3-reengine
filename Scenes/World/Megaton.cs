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

	private struct InstanceRequest
	{
		public string Path;
		public Vector3 Position;
		public Vector3 Rotation;
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
				"STAT", "DOOR", "FURN", "ACTI", "MSTT", "LIGH", "TERM",
				"CONT", "MISC", "WEAP", "ARMO", "CLOT", "TREE", "ALCH", "INGR", "BOOK",
				"NPC_", "CREA", "GRAS", "LAND", "DEBR", "SCOL"
			});

			_refrFormIDIndex = _esm.BuildFormIdIndex(new[] { "REFR" });

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
						break;
					}
				}
			}

			if (megatonWorldId != 0)
			{
				_ = Task.Run(() => LoadWorldAsync(megatonWorldId));
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Init error: {e.Message}");
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

		// FO3 Intrinsic ZYX == Extrinsic XYZ
		// FO3 X → Godot  X  (Vector3.Right)
		// FO3 Y → Godot -Z  (Vector3.Forward)  ※ Back(+Z) ではなく Forward(-Z)
		// FO3 Z → Godot  Y  (Vector3.Up)
		var basis = Basis.Identity;
		basis = basis.Rotated(Vector3.Right,   req.Rotation.X); // ① X軸まわり
		basis = basis.Rotated(Vector3.Forward, req.Rotation.Y); // ② FO3 Y → Godot -Z
		basis = basis.Rotated(Vector3.Up,      req.Rotation.Z); // ③ FO3 Z → Godot Y

		inst.Transform = new Transform3D(basis, req.Position);
		AddChild(inst);
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
			// Apply textures
			for (int i = 0; i < mesh.GetSurfaceCount(); i++)
			{
				string texPath = mesh.SurfaceGetName(i);
				GD.Print($"[Megaton] Surface {i} of {path} has TexturePath: '{texPath}'");
				
				if (!string.IsNullOrEmpty(texPath))
				{
					var tex = LoadTexture(texPath);
					if (tex != null)
					{
						var mat = new StandardMaterial3D { AlbedoTexture = tex };
						mesh.SurfaceSetMaterial(i, mat);
					}
					else
					{
						GD.Print($"[Megaton] Texture NOT found/loaded: {texPath} for mesh {path}");
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
		path = path.Replace('\\', '/');
		
		// Some NIFs have "textures/" prefix, some don't. 
		// Let's try matching both or normalization.
		string searchPath = path;
		if (!searchPath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
			searchPath = "textures/" + searchPath;

		if (_textureCache.TryGetValue(searchPath, out var cached)) return cached;

		if (_texturesBsa == null) return null;

		var file = _textureFiles.FirstOrDefault(f => f.Path.Equals(searchPath, StringComparison.OrdinalIgnoreCase));
		
		// If not found with "textures/", try without it just in case
		if (file == null && searchPath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
		{
			string altPath = searchPath.Substring(9);
			file = _textureFiles.FirstOrDefault(f => f.Path.Equals(altPath, StringComparison.OrdinalIgnoreCase));
		}

		if (file == null)
		{
			// GD.PrintErr($"[Megaton] Texture file not in BSA: {searchPath}");
			return null;
		}

		byte[] data = _texturesBsa.ReadFileData(file);
		if (data == null) return null;

		var img = new Image();
		Error err = img.LoadDdsFromBuffer(data);
		if (err == Error.Ok)
		{
			var tex = ImageTexture.CreateFromImage(img);
			_textureCache.TryAdd(searchPath, tex);
			GD.Print($"[Megaton] Successfully loaded texture: {searchPath}");
			return tex;
		}
		else
		{
			GD.PrintErr($"[Megaton] Failed to load DDS texture: {searchPath} (Error: {err})");
			return null;
		}
	}
}
