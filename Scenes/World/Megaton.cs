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

public partial class Megaton : Node3D
{
	private ConcurrentDictionary<string, ArrayMesh> _meshCache = new();
	private OpenFo3.BSA.BSAReader _bsa;
	private ESMReader _esm;

	private Dictionary<uint, long> _masterFormIDIndex;
	private Dictionary<uint, long> _refrFormIDIndex;
	private List<OpenFo3.BSA.BSAFile> _bsaFiles;

	private const int MaxObjectsToLoad = 5000;
	private const float CellSize = 8192.0f;
	private const float WorldScale = 0.015f;

	private ConcurrentQueue<InstanceRequest> _instantiateQueue = new();
	private Vector2 _megatonCenter = new Vector2(-14200f, -3800f);
	private float _loadRadius = 120000f;

	private struct InstanceRequest
	{
		public string Path;
		public Vector3 Position;
		public Vector3 Rotation; // In Degrees (rx, ry, rz)
		public ArrayMesh Mesh;
	}

	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		// GD.Print("[Megaton] Ready. Starting Async Load...");

		try
		{
			string bsaPath = Path.Combine(GamePaths.DataPath, "Fallout - Meshes.bsa");
			_bsa = new OpenFo3.BSA.BSAReader(bsaPath);
			_bsaFiles = _bsa.ExtractFileList();

			_esm = new ESMReader(GamePaths.EsmPath);
			_masterFormIDIndex = _esm.BuildFormIdIndex(new[]
			{
				"STAT","DOOR","FURN","ACTI","MSTT","LIGH","TERM",
				"CONT","MISC","WEAP","ARMO","CLOT","TREE","ALCH","INGR","BOOK"
			});

			_refrFormIDIndex = _esm.BuildFormIdIndex(new[] { "REFR" });

			// Start background loading
			_ = Task.Run(() => LoadWorldAsync());
		}
		catch (Exception e)
		{
			// GD.PrintErr(e);
		}
	}

	public override void _Process(double delta)
	{
		// Throttled Instantiation: Spend max 4ms per frame adding objects
		var startTime = Time.GetTicksMsec();
		while (_instantiateQueue.TryDequeue(out var request))
		{
			CreateAndAddInstance(request);
			if (Time.GetTicksMsec() - startTime > 4) break;
		}
	}

	private async Task LoadWorldAsync()
	{
		// GD.Print("[Megaton] Loading world records into chunks...");

		// 1. Group by Chunks
		var chunks = new Dictionary<Vector2I, List<long>>();
		int totalRefrs = 0;
		foreach (var kvp in _refrFormIDIndex)
		{
			var offset = kvp.Value;
			ESMRecord record;
			List<SubRecord> subs;

			lock (_esm)
			{
				record = _esm.GetRecordAtOffset(offset);
				subs = _esm.GetSubRecords(record);
			}

			var dataSub = subs.FirstOrDefault(s => s.Type == "DATA");
			if (dataSub == null) continue;

			float px = BitConverter.ToSingle(dataSub.Data, 0);
			float py = BitConverter.ToSingle(dataSub.Data, 4);

			Vector2 pos2 = new(px, py);
			if (pos2.DistanceTo(_megatonCenter) > _loadRadius) continue;

			var chunkCoords = new Vector2I((int)(px / CellSize), (int)(py / CellSize));
			if (!chunks.ContainsKey(chunkCoords)) chunks[chunkCoords] = new List<long>();
			chunks[chunkCoords].Add(offset);
			
			totalRefrs++;
			if (totalRefrs >= MaxObjectsToLoad) break;
		}

		GD.Print($"[Megaton] Found {totalRefrs} REFRs in {chunks.Count} chunks.");

		// 2. Process Chunks (Parallel NIF Loading)
		await Task.Run(() =>
		{
			Parallel.ForEach(chunks, chunkKvp =>
			{
				foreach (var offset in chunkKvp.Value)
				{
					ProcessRecord(offset);
				}
			});
		});

		GD.Print($"[Megaton] Done. Queue: {_instantiateQueue.Count}, MeshCache: {_meshCache.Count}");
		// Give _Process time to drain the queue, then check child count
		await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);
		GD.Print($"[Megaton] Scene children: {GetChildCount()} (after 2s drain)");
		// Sample positions to understand spread
		var positions = new List<Vector3>();
		for (int i = 0; i < GetChildCount(); i++)
			if (GetChild(i) is Node3D n3d) positions.Add(n3d.GlobalPosition);
		if (positions.Count > 0)
		{
			var minP = positions[0]; var maxP = positions[0];
			foreach (var p in positions) { 
				minP = new Vector3(Math.Min(minP.X,p.X), Math.Min(minP.Y,p.Y), Math.Min(minP.Z,p.Z));
				maxP = new Vector3(Math.Max(maxP.X,p.X), Math.Max(maxP.Y,p.Y), Math.Max(maxP.Z,p.Z));
			}
			GD.Print($"[Megaton] Position range: {minP} to {maxP}");
			GD.Print($"  First: {positions[0]}  Middle: {positions[positions.Count/2]}  Last: {positions[^1]}");
		}
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
			
			long baseOffset;
			string nifPath;

			lock (_esm)
			{
				if (!_masterFormIDIndex.TryGetValue(formId, out baseOffset)) return;
				var baseRecord = _esm.GetRecordAtOffset(baseOffset);
				var baseSubs = _esm.GetSubRecords(baseRecord);
				var modl = baseSubs.FirstOrDefault(s => s.Type == "MODL");
				if (modl == null) return;
				nifPath = Encoding.ASCII.GetString(modl.Data).TrimEnd('\0').Replace('\\', '/');
			}

			if (!nifPath.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase)) nifPath = "meshes/" + nifPath;

			var mesh = GetOrLoadMesh(nifPath);
			if (mesh == null) return;

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
				Mesh = mesh
			});
		}
		catch (Exception e)
		{
			// GD.PrintErr($"[Megaton] Error processing record at {offset:X8}: {e.Message}");
		}
	}

	private void CreateAndAddInstance(InstanceRequest req)
	{
		var inst = new MeshInstance3D { Mesh = req.Mesh };
		inst.Position = req.Position;

		// FO3 stores rotations in RADIANS (not degrees)
		var basis = Basis.Identity;
		basis = basis.Rotated(Vector3.Up, req.Rotation.Z);
		basis = basis.Rotated(Vector3.Right, req.Rotation.X);
		basis = basis.Rotated(Vector3.Back, req.Rotation.Y);
		inst.Transform = new Transform3D(basis, inst.Position);

		AddChild(inst);
	}

	private ArrayMesh GetOrLoadMesh(string path)
	{
		if (_meshCache.TryGetValue(path, out var cached)) return cached;

		var file = _bsaFiles.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
		if (file == null) { GD.PrintErr($"[Megaton] NIF not in BSA: {path}"); return null; }

		byte[] nifData = _bsa.ReadFileData(file);
		if (nifData == null) return null;

		var nif = new NIFReader();
		nif.Parse(nifData);

		ArrayMesh mesh = NIFMeshBuilder.Build(nif);
		if (mesh.GetSurfaceCount() > 0)
		{
			_meshCache.TryAdd(path, mesh);
			// GD.Print($"[Megaton] Loaded mesh: {path} surfaces={mesh.GetSurfaceCount()}");
			return mesh;
		}

		// GD.PrintErr($"[Megaton] No surfaces: {path} blocks={nif.Blocks.Count}");
		return null;
	}
}
