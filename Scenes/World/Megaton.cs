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
using OpenFo3.Player;
using OpenFo3.UI;

public partial class Megaton : Node3D
{
	private ConcurrentDictionary<string, ArrayMesh> _meshCache = new();
	private ConcurrentDictionary<string, NIFReader> _nifCache = new();
	private ConcurrentDictionary<string, Texture2D> _textureCache = new();
	private ConcurrentDictionary<string, bool> _textureHasAlpha = new();
	private ConcurrentDictionary<string, List<ParticleSystemEntry>> _particleCache = new();
	private ConcurrentDictionary<string, List<Node3D>> _skinnedCache = new();

	private Dictionary<string, Stack<MeshInstance3D>> _meshPool = new();
	private Dictionary<string, Stack<Node3D>> _propPool = new();
	private Dictionary<string, List<PropEntry>> _propEntries = new();
	private Camera3D _cachedCamera;
	private float PropLodDistance => PerformanceSettings.PropLodDistance;

	private List<BSAReader> _bsaReaders = new();
	private BSAReader _texturesBsa;
	private ESMReader _esm;

	private Dictionary<uint, RecordEntry> _masterFormIDIndex;
	private Dictionary<uint, RecordEntry> _refrFormIDIndex;
	private Dictionary<uint, RecordEntry> _achrIndex;
	private Dictionary<uint, RecordEntry> _acreIndex;

	private List<(BSAReader BSA, List<BSAFile> Files)> _meshBsaList = new();
	private List<(BSAReader BSA, List<BSAFile> Files)> _textureBsaList = new();

	private const float WorldScale = 0.015f;

	private ConcurrentQueue<InstanceRequest> _instantiateQueue = new();
	private LightingLoader _lightingLoader;
	private TerrainBuilder _terrainBuilder;
	private NavMeshBuilder _navMeshBuilder;
	private AudioManager _audioManager;
	private AnimationManager _animationManager;
	private PickHandler _pickHandler;
	private WorldSelectMenu _worldSelectMenu;
	private bool _hudVisible = true;
	private PlayerController _player;
	private MainMenu _mainMenu;
	private IntroSequence _introSequence;
	private bool _introActive;
	private bool _inGame;

	private struct InstanceRequest
	{
		public string Path;
		public Vector3 Position;
		public Vector3 Rotation;
		public uint FormId;
		public string BaseType;
		public float Scale;
		public uint WorldFormId;
		public uint CellFormId;
		public uint BaseFormId;
		public List<string> AnimPaths;
	}

	private struct WorldData
	{
		public uint FormId;
		public string Name;
		public float DefaultLandHeight;
		public int NwCellX, NwCellY, SeCellX, SeCellY;
		public Vector2 Center;
		public bool HasMnam;
	}

	private struct CellData
	{
		public uint FormId;
		public string Edid;
		public string Name;
	}

	private Dictionary<string, WorldData> _worldDataByName = new();
	private Dictionary<uint, string> _worldNameById = new();
	private Dictionary<string, Node3D> _worldContainers = new();
	private Dictionary<string, bool> _worldLoading = new();
	private List<string> _worldNameList = new();
	private Dictionary<string, CellData> _cellDataByEdid = new();
	private Dictionary<uint, string> _cellEdidById = new();
	private Dictionary<string, Node3D> _cellContainers = new();
	private Dictionary<string, bool> _cellLoading = new();
	private string _currentWorldName;
	private Label3D _worldLabel;
	private bool _initialized = false;
	private int _frameCount = 0;
	private bool _debugShowCollision = false;
	private bool _debugShowNavigation = false;
	private bool _debugShowPaths = false;
	private bool _prevDebugCollision = false;
	private bool _prevDebugNavigation = false;
	private bool _prevDebugPaths = false;

	private struct TerrainLodEntry
	{
		public MeshInstance3D Instance;
		public ArrayMesh FullMesh;
		public ArrayMesh LodMesh;
	}
	private Dictionary<string, List<TerrainLodEntry>> _terrainLodEntries = new();
	private float LodDistance => PerformanceSettings.TerrainLodDistance;

	private struct PropEntry
	{
		public MeshInstance3D MeshInstance;
		public Node3D Parent;
		public Vector3 Position;
		public float BoundingRadius;
		public string NifPath;
		public ArrayMesh FullMesh;
		public ArrayMesh LodMesh;
		public bool Valid;
	}

	private const float CellSize = 4096f;
	private const int MinTerrainHalf = 10;

	// Auto world-switching (open world)
	private int _autoSwitchCooldown = 0;
	private const int AutoSwitchFrameInterval = 30; // Frames between checks
	private const float WorldSwitchDistFo3 = 3000f;  // FO3 units (~45 Godot units)

	public override void _Process(double delta)
	{
		if (!_initialized) return;

		if (!_inGame)
		{
			UpdateDebugOverlay();
			_frameCount++;
			return;
		}

		int maxPerFrame = PerformanceSettings.MaxInstancesPerFrame;
		for (int i = 0; i < maxPerFrame && _instantiateQueue.TryDequeue(out var req); i++)
		{
			CreateAndAddInstance(req);
		}

		if (_hud != null && _currentWorldName != null)
			_hud.SetWorldName($"World: {_currentWorldName}");

		UpdateTerrainLod();
		UpdateFrustumCulling();
		UpdatePropLod();
		UpdateDebugOverlay();
		UpdateDebugVisualization();

		_frameCount++;
		if (_frameCount % 120 == 0)
			EnforceCacheLimits();
	}

	private void EnforceCacheLimits()
	{
		if (_meshCache.Count > PerformanceSettings.MaxMeshCacheSize)
		{
			int excess = _meshCache.Count - PerformanceSettings.MaxMeshCacheSize;
			var keys = _meshCache.Keys.Take(excess).ToList();
			foreach (var k in keys) _meshCache.TryRemove(k, out _);
		}
		if (_textureCache.Count > PerformanceSettings.MaxTextureCacheSize)
		{
			int excess = _textureCache.Count - PerformanceSettings.MaxTextureCacheSize;
			var keys = _textureCache.Keys.Take(excess).ToList();
			foreach (var k in keys) _textureCache.TryRemove(k, out _);
		}
		if (_nifCache.Count > PerformanceSettings.MaxNifCacheSize)
		{
			int excess = _nifCache.Count - PerformanceSettings.MaxNifCacheSize;
			var keys = _nifCache.Keys.Take(excess).ToList();
			foreach (var k in keys) _nifCache.TryRemove(k, out _);
		}
	}

	private void UpdateDebugOverlay()
	{
		if (_debugLabel == null) return;

		double fps = Engine.GetFramesPerSecond();
		int pending = _instantiateQueue.Count;
		int meshesCached = _meshCache.Count;
		int texturesCached = _textureCache.Count;
		int nifsCached = _nifCache.Count;

		string worldInfo = _currentWorldName ?? "(none)";
		int worldIdx = _worldNameList.IndexOf(_currentWorldName ?? "");

		Vector3 camPos = Vector3.Zero;
		var cam = GetViewport()?.GetCamera3D();
		if (cam != null) camPos = cam.GlobalPosition;

		string debugFlags = "";
		if (_debugShowCollision) debugFlags += " COL";
		if (_debugShowNavigation) debugFlags += " NAV";
		if (_debugShowPaths) debugFlags += " PATH";
		if (debugFlags.Length > 0) debugFlags = "  |" + debugFlags;

		_debugLabel.Text = $"OpenFo3: ReEngine  |  F1: Cam FPS/TPS  |  F2: Debug FreeCam\n" +
			$"FPS: {fps,4:F0}  |  World/Cell: {worldInfo}\n" +
			$"Queued: {pending}  |  Meshes: {meshesCached}  |  Textures: {texturesCached}  |  NIFs: {nifsCached}\n" +
			$"Camera: ({camPos.X:F1}, {camPos.Y:F1}, {camPos.Z:F1})" +
			$"{debugFlags}\n" +
			$"Ctrl+C:Collision  Ctrl+N:Nav  Ctrl+P:Path";
	}

	private void UpdateDebugVisualization()
	{
		// Editor debug visuals are not directly accessible via World3D in Redot 26.1.
		// Use Ctrl+{C,N,P} key toggles instead (handled in _Input).
		// Apply only when flags change (avoid expensive tree traversal every frame)
		bool colChanged = _debugShowCollision != _prevDebugCollision;
		bool navChanged = _debugShowNavigation != _prevDebugNavigation;
		bool pathChanged = _debugShowPaths != _prevDebugPaths;

		if (colChanged || navChanged || pathChanged)
		{
			if (_currentWorldName != null && _worldContainers.TryGetValue(_currentWorldName, out var container))
				ToggleDebuggableNodes(container);
			_prevDebugCollision = _debugShowCollision;
			_prevDebugNavigation = _debugShowNavigation;
			_prevDebugPaths = _debugShowPaths;
		}
	}

	private void UpdateDebugVisualizationNow()
	{
		if (_currentWorldName != null && _worldContainers.TryGetValue(_currentWorldName, out var container))
		{
			ToggleDebuggableNodes(container);
		}
	}

	private void ToggleDebuggableNodes(Node3D parent)
	{
		foreach (var child in parent.GetChildren())
		{
			if (child is CollisionShape3D cs)
			{
				cs.Disabled = !_debugShowCollision;
			}
			else if (child is NavigationRegion3D nav)
			{
				nav.Enabled = _debugShowNavigation;
			}

			// Recurse into containers and physics bodies
			if (child is Node3D childNode && child.GetChildCount() > 0)
			{
				ToggleDebuggableNodes(childNode);
			}
		}
	}

	private void UpdateTerrainLod()
	{
		if (_currentWorldName == null) return;
		if (!_terrainLodEntries.TryGetValue(_currentWorldName, out var entries)) return;

		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return;

		Vector3 camPos = cam.GlobalPosition;
		float lodDistSq = LodDistance * LodDistance;

		foreach (var entry in entries)
		{
			if (entry.Instance == null) continue;
			if (entry.LodMesh == null) continue;

			float distSq = entry.Instance.GlobalPosition.DistanceSquaredTo(camPos);
			bool useLod = distSq > lodDistSq;
			bool currentIsLod = entry.Instance.Mesh == entry.LodMesh;

			if (useLod && !currentIsLod)
				entry.Instance.Mesh = entry.LodMesh;
			else if (!useLod && currentIsLod)
				entry.Instance.Mesh = entry.FullMesh;
		}
	}

		public override void _Input(InputEvent @event)
		{
			if (!_initialized) return;
			if (!_inGame || _introActive) return;
			if (@event is InputEventKey key && key.Pressed && !key.Echo)
			{
				// World select menu (P)
				if (key.Keycode == Key.P && !key.CtrlPressed)
			{
				if (_worldSelectMenu != null)
					_worldSelectMenu.Toggle();
			}

			// HUD toggle (F10)
			if (key.Keycode == Key.F10)
			{
				_hudVisible = !_hudVisible;
				if (_hud != null)
					_hud.Visible = _hudVisible;
			}

			// Debug visualization toggles
			if (key.Keycode == Key.C && key.CtrlPressed)
			{
				_debugShowCollision = !_debugShowCollision;
				GD.Print($"[Megaton] Debug collision: {_debugShowCollision}");
				UpdateDebugVisualizationNow();
			}
			if (key.Keycode == Key.N && key.CtrlPressed)
			{
				_debugShowNavigation = !_debugShowNavigation;
				GD.Print($"[Megaton] Debug navigation: {_debugShowNavigation}");
				UpdateDebugVisualizationNow();
			}
			if (key.Keycode == Key.P && key.CtrlPressed)
			{
				_debugShowPaths = !_debugShowPaths;
				GD.Print($"[Megaton] Debug paths: {_debugShowPaths}");
				UpdateDebugVisualizationNow();
			}
		}

		if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
		{
			if (_pickHandler != null)
			{
				_pickHandler.PickObjectAtScreen(mouse.Position);
			}
		}
	}

	private Label _debugLabel;

	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		PerformanceSettings.Initialize();

		try
		{
			LoadBSAArchives();

			_esm = new ESMReader(GamePaths.EsmPath);
			_masterFormIDIndex = _esm.BuildFormIdIndex(new[]
			{
				"STAT", "DOOR", "FURN", "ACTI", "MSTT", "LIGH", "TERM",
				"CONT", "MISC", "WEAP", "ARMO", "CLOT", "TREE", "ALCH",
				"INGR", "BOOK", "GRAS", "LAND", "DEBR", "SCOL",
				"KEYM", "ARMA", "NOTE", "PWAT", "TACT", "AMMO",
				"NPC_", "CREA", "SOUN"
			});

			_refrFormIDIndex = _esm.BuildFormIdIndex(new[] { "REFR" });
			_achrIndex = _esm.BuildFormIdIndex(new[] { "ACHR" });
			_acreIndex = _esm.BuildFormIdIndex(new[] { "ACRE" });

			_lightingLoader = new LightingLoader(_esm);
			_terrainBuilder = new TerrainBuilder(_esm);
			_terrainBuilder.SetLandIndex(_masterFormIDIndex);
			_navMeshBuilder = new NavMeshBuilder(_esm);
			_audioManager = new AudioManager(_esm, _bsaReaders);
			_animationManager = new AnimationManager(_meshBsaList);
			_pickHandler = new PickHandler();
			_pickHandler.Name = "PickHandler";
			_pickHandler.ObjectPicked += OnObjectPicked;
			AddChild(_pickHandler);

			CreateDebugOverlay();
			DiscoverWorlds();
			DiscoverCells();

			CreateWorldLabel();
			CreateWorldSelectMenu();
			PopulateWorldSelectMenu();

			ShowMainMenu();

			_initialized = true;
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Init error: {e.Message}");
		}
	}

	private void ShowMainMenu()
	{
		_inGame = false;
		_introActive = false;

		_mainMenu = new MainMenu();
		_mainMenu.NewGame += OnNewGame;
		_mainMenu.ContinueGame += OnContinueGame;
		_mainMenu.LoadGame += OnLoadGame;
		_mainMenu.Settings += OnSettings;
		_mainMenu.Quit += OnQuit;
		AddChild(_mainMenu);
		_mainMenu.ShowMenu();

		Input.MouseMode = Input.MouseModeEnum.Visible;
		GD.Print("[Megaton] Main menu displayed");
	}

	private async void OnNewGame()
	{
		GD.Print("[Megaton] New Game selected. Loading Vault 101...");
		_mainMenu?.HideMenu();

		string cellName = "Vault101a";
		_currentWorldName = cellName;

		CreateHud();

		// Add temporary fade overlay during loading
		var canvas = GetNodeOrNull<CanvasLayer>("DebugOverlay");
		ColorRect fadeRect = null;
		if (canvas != null)
		{
			fadeRect = new ColorRect();
			fadeRect.Color = new Color(0, 0, 0, 1);
			fadeRect.MouseFilter = Control.MouseFilterEnum.Ignore;
			fadeRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			canvas.AddChild(fadeRect);
		}

		await LoadCellAsync(cellName);

		// Wait for asset instantiation queue to clear (up to 3 seconds)
		for (int i = 0; i < 180; i++)
		{
			if (_instantiateQueue.Count == 0) break;
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		// Spawn player inside Vault 101
		// Vault101aのプレイヤー開始地点: FO3内部座標から変換
		// Vault101a セルの中央付近（PlayerStartMarker位置）
		// FO3 DATA: X≈-15000, Y≈15000, Z≈-175 → Godot: X=-225, Y=-2.625, Z=-225
		// セルが原点ローカル座標（インテリアセルはCenter減算なし）なので WorldScale のみ適用
		Vector3 vault101StartPos = GetVault101StartPosition();
		CreatePlayer(vault101StartPos);

		// Fade in
		if (fadeRect != null)
		{
			var tween = CreateTween();
			tween.TweenProperty(fadeRect, "color", new Color(0, 0, 0, 0), 1.5f);
			tween.TweenCallback(Callable.From(() => fadeRect.QueueFree()));
		}

		_inGame = true;
		Input.MouseMode = Input.MouseModeEnum.Captured;
		GD.Print("[Megaton] Game started inside Vault 101.");
	}

	private void OnContinueGame()
	{
		GD.Print("[Megaton] Continue selected");
		_mainMenu.HideMenu();
		LoadLastSaveGame();
	}

	private void OnLoadGame()
	{
		GD.Print("[Megaton] Load selected");
		_mainMenu.HideMenu();
		LoadLastSaveGame();
	}

	private void OnSettings()
	{
		GD.Print("[Megaton] Settings selected");
	}

	private void OnQuit()
	{
		GD.Print("[Megaton] Quit selected");
		GetTree().Quit();
	}

	private void StartIntroSequence()
	{
		_introActive = true;
		_inGame = false;

		_introSequence = new IntroSequence();
		_introSequence.Name = "IntroSequence";
		_introSequence.IntroComplete += OnIntroComplete;
		AddChild(_introSequence);

		_introSequence.StartIntro();
		GD.Print("[Megaton] Intro sequence started");
	}

	private void OnIntroComplete(string playerName, int[] specialValues, bool isMale)
	{
		GD.Print($"[Megaton] Intro complete for {playerName}. Loading Capital Wasteland...");

		if (_introSequence != null)
		{
			_introSequence.QueueFree();
			_introSequence = null;
		}

		_introActive = false;

		// Load the Capital Wasteland world (Vault 101 entrance area)
		string worldName = _worldNameList.FirstOrDefault(n =>
			n.Contains("Wasteland", StringComparison.OrdinalIgnoreCase) ||
			n.Contains("WasteLand", StringComparison.OrdinalIgnoreCase));
		if (worldName == null)
			worldName = _worldNameList.Count > 0 ? _worldNameList[0] : null;

		if (worldName != null)
		{
			GD.Print($"[Megaton] Loading world: {worldName}");
			_ = LoadWorldAsync(worldName);
		}

		CreateHud();
		// Vault 101 entrance in Capital Wasteland: FO3 coords approx (-1560, -2800, 0)
		// Converted to Godot scale: (-23.4, 20, -42)
		CreatePlayer(new Vector3(-23, 25, -42));
		ApplyCharacterData(playerName, specialValues, isMale);

		_inGame = true;
		Input.MouseMode = Input.MouseModeEnum.Captured;

		GD.Print("[Megaton] Player is now in the Capital Wasteland");
	}

	private void ApplyCharacterData(string playerName, int[] specialValues, bool isMale)
	{
		GD.Print($"[Megaton] Character: {playerName}, Male={isMale}, SPECIAL=[{string.Join(",", specialValues)}]");
	}

	private void LoadLastSaveGame()
	{
		string targetWorld = GamePaths.GetTargetWorld();
		if (!_worldDataByName.ContainsKey(targetWorld))
		{
			targetWorld = _worldNameList.FirstOrDefault(n =>
				n.Contains("Wasteland", StringComparison.OrdinalIgnoreCase) ||
				n.Contains("WasteLand", StringComparison.OrdinalIgnoreCase));
			targetWorld ??= _worldNameList.Count > 0 ? _worldNameList[0] : null;
		}

		GD.Print($"[Megaton] Loading world: {targetWorld}");

		if (targetWorld != null)
			_ = LoadWorldAsync(targetWorld);

		CreateHud();
		CreatePlayer(new Vector3(0, 20, 0));
		_inGame = true;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private HudOverlay _hud;

	private void CreateHud()
	{
		if (_hud != null) return;
		_hud = new HudOverlay();
		_hud.Name = "HUD";
		AddChild(_hud);
		_hud.SetLocation("Megaton");
	}

	private void CreatePlayer(Vector3? spawnPosition = null)
	{
		var existingCam = GetNodeOrNull<Camera3D>("Camera3D");
		if (existingCam != null)
		{
			existingCam.QueueFree();
		}

		_player = new PlayerController();
		_player.Name = "PlayerController";
		AddChild(_player);

		if (_hud != null)
			_player.SetHud(_hud);

		Vector3 pos = spawnPosition ?? new Vector3(0, 20, 0);
		_player.Spawn(pos);

		GD.Print($"[Megaton] PlayerController created at {pos}");
	}

	/// <summary>
	/// Vault101aセルのプレイヤー開始位置を取得する。
	/// インテリアセルはワールド中心減算なし（FO3座標 × WorldScale のみ）。
	/// REFRのPlayerStartMarkerから推定した代表値を使用。
	/// </summary>
	private Vector3 GetVault101StartPosition()
	{
		// カメラで確認したVault101a内の有効なプレイヤー開始位置
		// Camera: (156.0, 44.0, -58.0) から設定
		return new Vector3(156f, 44f, -58f);
	}

	private void CreateWorldSelectMenu()
	{
		if (_worldSelectMenu != null) return;
		_worldSelectMenu = new WorldSelectMenu();
		_worldSelectMenu.Name = "WorldSelectMenu";
		AddChild(_worldSelectMenu);
	}

	private void PopulateWorldSelectMenu()
	{
		if (_worldSelectMenu == null) return;
		_worldSelectMenu.LoadWorlds(_worldNameList, (worldName) =>
		{
			SwitchToWorld(worldName);
		});
	}

	private void CreateDebugOverlay()
	{
		var canvas = new CanvasLayer();
		canvas.Name = "DebugOverlay";
		canvas.Layer = 10;
		AddChild(canvas);

		_debugLabel = new Label();
		_debugLabel.Name = "DebugLabel";
		_debugLabel.Position = new Vector2(10, 10);
		_debugLabel.HorizontalAlignment = HorizontalAlignment.Left;
		_debugLabel.VerticalAlignment = VerticalAlignment.Top;
		var settings = new LabelSettings();
		settings.FontSize = 14;
		settings.FontColor = new Color(0, 1, 0);
		settings.OutlineSize = 1;
		settings.OutlineColor = new Color(0, 0, 0);
		_debugLabel.LabelSettings = settings;
		canvas.AddChild(_debugLabel);
	}

	private void CreateWorldLabel()
	{
		_worldLabel = new Label3D();
		_worldLabel.Name = "WorldLabel";
		_worldLabel.Position = new Vector3(0, 30, 0);
		_worldLabel.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
		_worldLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_worldLabel.Modulate = new Color(1, 1, 0);
		_worldLabel.PixelSize = 0.02f;
		AddChild(_worldLabel);
	}

	private void LoadBSAArchives()
	{
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

		_texturesBsa = _bsaReaders.FirstOrDefault(b =>
			System.IO.Path.GetFileNameWithoutExtension(b.FilePath)
				.IndexOf("texture", StringComparison.OrdinalIgnoreCase) >= 0);
	}

	private void DiscoverWorlds()
	{
		var wrldIndex = _esm.BuildFormIdIndex(new[] { "WRLD" });
		GD.Print($"[Megaton] Discovering worlds: {wrldIndex.Count} WRLD records found");

		foreach (var kvp in wrldIndex)
		{
			try
			{
				var rec = _esm.GetRecordAtOffset(kvp.Value.Offset);
				var subs = _esm.GetSubRecords(rec);
				var edid = subs.FirstOrDefault(s => s.Type == "EDID");
				if (edid == null) continue;

				string name = Encoding.ASCII.GetString(edid.Data).TrimEnd('\0');
				if (string.IsNullOrEmpty(name)) continue;

				var dnam = subs.FirstOrDefault(s => s.Type == "DNAM");
				float defaultLandHeight = 0;
				if (dnam != null && dnam.Data.Length >= 8)
				{
					defaultLandHeight = BitConverter.ToSingle(dnam.Data, 0);
				}
				if (dnam != null && name == "MegatonWorld")
				{
					GD.Print($"[WRLD] MegatonWorld DNAM data[{dnam.Data.Length}]: {BitConverter.ToString(dnam.Data)}");
				}

				int nwX = 0, nwY = 0, seX = 0, seY = 0;
				bool hasMnam = false;
				var mnam = subs.FirstOrDefault(s => s.Type == "MNAM");
				if (mnam != null && mnam.Data.Length >= 16)
				{
					nwX = BitConverter.ToInt16(mnam.Data, 8);
					nwY = BitConverter.ToInt16(mnam.Data, 10);
					seX = BitConverter.ToInt16(mnam.Data, 12);
					seY = BitConverter.ToInt16(mnam.Data, 14);
					hasMnam = true;
				}

				Vector2 center;
				if (hasMnam)
				{
					float cx = (nwX + seX) * CellSize / 2f;
					float cy = (nwY + seY) * CellSize / 2f;
					center = new Vector2(cx, cy);
				}
				else
				{
					center = GamePaths.GetWorldCenter();
				}

				// Expand MNAM bounds for terrain coverage
				int ctrX = (nwX + seX) / 2;
				int ctrY = (nwY + seY) / 2;
				int hx = Math.Max((seX - nwX) / 2, MinTerrainHalf) + 5;
				int hy = Math.Max((seY - nwY) / 2, MinTerrainHalf) + 5;

				var wd = new WorldData
				{
					FormId = rec.FormId,
					Name = name,
					DefaultLandHeight = defaultLandHeight,
					NwCellX = ctrX - hx,
					NwCellY = ctrY - hy,
					SeCellX = ctrX + hx,
					SeCellY = ctrY + hy,
					Center = center,
					HasMnam = hasMnam,
				};

				_worldDataByName[name] = wd;
				_worldNameById[rec.FormId] = name;
				_worldNameList.Add(name);

				GD.Print($"[Megaton]   World: {name} (0x{rec.FormId:X8})" +
					$" center=({center.X:F0},{center.Y:F0})" +
					$" landH={defaultLandHeight}" +
					$" cells=({wd.NwCellX},{wd.NwCellY})-({wd.SeCellX},{wd.SeCellY})");
			}
			catch (Exception e)
			{
				GD.PrintErr($"[Megaton] Error discovering WRLD at 0x{kvp.Value.Offset:X8}: {e.Message}");
			}
		}

		// Log all discovered worlds for debugging
		GD.Print($"[Megaton] === DISCOVERED WORLDS ({_worldNameList.Count}) ===");
		for (int i = 0; i < _worldNameList.Count; i++)
			GD.Print($"  [{i + 1}] {_worldNameList[i]}");
		GD.Print("[Megaton] =================================");

		// Sort world list: put common worlds first for easy keyboard access
		_worldNameList = _worldNameList
			.OrderByDescending(n =>
			{
				if (n.Contains("Wasteland") || n.Contains("WasteLand") || n.Contains("WastelandWorld")) return 100;
				if (n.Contains("Megaton")) return 90;
				if (n.Contains("DC") || n.Contains("DCTallGrass")) return 80;
				if (n.Contains("Vault") || n.Contains("Vault101")) return 70;
				if (n.Contains("Interior")) return 60;
				return 0;
			})
			.ThenBy(n => n)
			.ToList();

		GD.Print($"[Megaton] Total worlds discovered: {_worldNameList.Count}");
	}

	private void DiscoverCells()
	{
		var cellIndex = _esm.BuildFormIdIndex(new[] { "CELL" });
		GD.Print($"[Megaton] Discovering interior cells: {cellIndex.Count} CELL records found");

		int interiorCount = 0;
		foreach (var kvp in cellIndex)
		{
			try
			{
				var rec = _esm.GetRecordAtOffset(kvp.Value.Offset);
				var subs = _esm.GetSubRecords(rec);
				var edid = subs.FirstOrDefault(s => s.Type == "EDID");
				if (edid == null) continue;

				string editName = Encoding.ASCII.GetString(edid.Data).TrimEnd('\0');
				if (string.IsNullOrEmpty(editName)) continue;

				// Determine if interior by checking DATA flags byte 0
				var data = subs.FirstOrDefault(s => s.Type == "DATA");
				bool isInterior = true;
				if (data != null && data.Data.Length >= 1)
					isInterior = (data.Data[0] & 0x01) != 0; // bit 0 = 1 means interior cell

				if (!isInterior) continue;

				var full = subs.FirstOrDefault(s => s.Type == "FULL");
				string displayName = full != null
					? Encoding.ASCII.GetString(full.Data).TrimEnd('\0')
					: editName;

				_cellDataByEdid[editName] = new CellData
				{
					FormId = rec.FormId,
					Edid = editName,
					Name = displayName,
				};
				_cellEdidById[rec.FormId] = editName;

				interiorCount++;
			}
			catch (Exception e)
			{
				GD.PrintErr($"[Megaton] Error discovering CELL at 0x{kvp.Value.Offset:X8}: {e.Message}");
			}
		}

		GD.Print($"[Megaton] Interior cells discovered: {interiorCount}");
	}

	private async Task LoadCellAsync(string cellEdid)
	{
		if (_cellLoading.ContainsKey(cellEdid) && _cellLoading[cellEdid]) return;
		if (_cellContainers.ContainsKey(cellEdid))
		{
			ShowCell(cellEdid);
			return;
		}

		await LoadCellInnerAsync(cellEdid);
		ShowCell(cellEdid);
	}

	private Task LoadCellInnerAsync(string cellEdid)
	{
		if (_cellLoading.ContainsKey(cellEdid) && _cellLoading[cellEdid]) return Task.CompletedTask;
		if (_cellContainers.ContainsKey(cellEdid)) return Task.CompletedTask;

		if (!_cellDataByEdid.TryGetValue(cellEdid, out var cd))
		{
			GD.PrintErr($"[Megaton] Unknown cell: {cellEdid}");
			return Task.CompletedTask;
		}

		_cellLoading[cellEdid] = true;

		var container = new Node3D();
		container.Name = $"Cell_{cellEdid}";
		_cellContainers[cellEdid] = container;
		AddChild(container);

		GD.Print($"[Megaton] Loading cell '{cellEdid}' (0x{cd.FormId:X8})...");

		// Load REFR, ACHR, ACRE for this cell
		LoadCellRefrs(cd);

		// Load lighting
		var lighting = _lightingLoader.GetCellLighting(cd.FormId);
		if (lighting != null)
		{
			GD.Print($"[Megaton] Applying lighting for cell '{cellEdid}'");
			LightingLoader.ApplyCellLighting(container, lighting);
		}

		// Load navmeshes for this cell
		var navMeshes = _navMeshBuilder.GetNavMeshesForCell(cd.FormId);
		if (navMeshes.Count > 0)
		{
			var navRegion = new NavigationRegion3D();
			foreach (var navData in navMeshes)
			{
				var navMesh = NavMeshBuilder.BuildNavigationMesh(navData, Vector2.Zero, WorldScale);
				if (navMesh != null)
				{
					navRegion.NavigationMesh = navMesh;
					break; // only one navmesh per cell for now
				}
			}
			container.AddChild(navRegion);
		}

		_cellLoading[cellEdid] = false;
		GD.Print($"[Megaton] Cell '{cellEdid}' loaded successfully.");
		return Task.CompletedTask;
	}

	private void ShowCell(string cellEdid)
	{
		// Hide all other cells
		foreach (var kvp in _cellContainers)
		{
			kvp.Value.Visible = kvp.Key == cellEdid;
		}

		// Also hide world containers
		foreach (var kvp in _worldContainers)
		{
			kvp.Value.Visible = false;
		}
	}

	private void LoadCellRefrs(CellData cd)
	{
		var refrsToProcess = new List<long>();
		foreach (var kvp in _refrFormIDIndex)
		{
			if (kvp.Value.CellFormId == cd.FormId)
				refrsToProcess.Add(kvp.Value.Offset);
		}

		int achrCount = 0, acreCount = 0;
		foreach (var kvp in _achrIndex)
		{
			if (kvp.Value.CellFormId == cd.FormId)
			{
				refrsToProcess.Add(kvp.Value.Offset);
				achrCount++;
			}
		}
		foreach (var kvp in _acreIndex)
		{
			if (kvp.Value.CellFormId == cd.FormId)
			{
				refrsToProcess.Add(kvp.Value.Offset);
				acreCount++;
			}
		}

		GD.Print($"[Megaton] Cell '{cd.Edid}': {refrsToProcess.Count} placements (REFR + {achrCount} ACHR + {acreCount} ACRE).");

		Parallel.ForEach(refrsToProcess, offset =>
		{
			ProcessCellRecord(offset, cd);
		});

		GD.Print($"[Megaton] Cell '{cd.Edid}': parsing done. Queue: {_instantiateQueue.Count}");
	}

	private void ProcessCellRecord(long offset, CellData cd)
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

			string recType = record.Type;
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

			if (nifPath == null && baseType != "LIGH" && baseType != "SOUN") return;

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

				if (isDebug) return;

				EnsureNifParsed(nifPath);
			}

			float px = BitConverter.ToSingle(dataSub.Data, 0);
			float py = BitConverter.ToSingle(dataSub.Data, 4);
			float pz = BitConverter.ToSingle(dataSub.Data, 8);
			float rx = 0, ry = 0, rz = 0;
			if (dataSub.Data.Length >= 24)
			{
				rx = BitConverter.ToSingle(dataSub.Data, 12);
				ry = BitConverter.ToSingle(dataSub.Data, 16);
				rz = BitConverter.ToSingle(dataSub.Data, 20);
			}

			var xclSub = subs.FirstOrDefault(s => s.Type == "XSCL");
			float scale = 1f;
			if (xclSub != null && xclSub.Data.Length >= 4)
				scale = BitConverter.ToSingle(xclSub.Data, 0);

			List<string> animPaths = null;
			if (baseType == "CREA" || baseType == "NPC_")
			{
				lock (_esm)
				{
					var kffz = baseSubs.FirstOrDefault(s => s.Type == "KFFZ");
					if (kffz != null && kffz.Data.Length > 0)
					{
						animPaths = new List<string>();
						int pos = 0;
						while (pos < kffz.Data.Length)
						{
							int end = Array.IndexOf(kffz.Data, (byte)0, pos);
							if (end < 0) end = kffz.Data.Length;
							string animPath = Encoding.ASCII.GetString(kffz.Data, pos, end - pos);
							if (!string.IsNullOrEmpty(animPath))
							{
								string p = animPath.Replace('\\', '/');
								if (!p.StartsWith("meshes/"))
									p = "meshes/" + p;
								animPaths.Add(p);
							}
							pos = end + 1;
						}
					}
				}
			}

			// Interior cells: no center subtraction
			_instantiateQueue.Enqueue(new InstanceRequest
			{
				Path = nifPath,
				Position = new Vector3(px * WorldScale, pz * WorldScale, -(py * WorldScale)),
				Rotation = new Vector3(rx, ry, rz),
				FormId = formId,
				BaseType = baseType,
				Scale = scale,
				CellFormId = cd.FormId,
				BaseFormId = formId,
				AnimPaths = animPaths,
			});

			if (baseType == "SCOL")
			{
				EmitCellScolParts(baseSubs, formId, px, py, pz, rx, ry, rz, cd);
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Error processing cell record at 0x{offset:X8}: {e.Message}");
		}
	}

	private void EmitCellScolParts(List<SubRecord> baseSubs, uint scolFormId,
		float scolPx, float scolPy, float scolPz,
		float scolRx, float scolRy, float scolRz, CellData cd)
	{
		int i = 0;
		while (i < baseSubs.Count)
		{
			var sub = baseSubs[i];
			if (sub.Type == "ONAM" && sub.Data.Length >= 4)
			{
				uint partFormId = BitConverter.ToUInt32(sub.Data, 0);
				if (i + 1 < baseSubs.Count && baseSubs[i + 1].Type == "DATA")
				{
					var data2 = baseSubs[i + 1];
					if (data2.Data.Length >= 24)
					{
						float partX = BitConverter.ToSingle(data2.Data, 0);
						float partY = BitConverter.ToSingle(data2.Data, 4);
						float partZ = BitConverter.ToSingle(data2.Data, 8);
						float partRx = BitConverter.ToSingle(data2.Data, 12);
						float partRy = BitConverter.ToSingle(data2.Data, 16);
						float partRz = BitConverter.ToSingle(data2.Data, 20);

						// Transform part by parent SCOL transform
						float cx = partX * Mathf.Cos(-scolRz) - partY * Mathf.Sin(-scolRz);
						float cy = partX * Mathf.Sin(-scolRz) + partY * Mathf.Cos(-scolRz);
						float worldX = scolPx + cx;
						float worldY = scolPy + cy;
						float worldZ = scolPz + partZ;
						float worldRx = scolRx + partRx;
						float worldRy = scolRy + partRy;
						float worldRz = scolRz + partRz;

						_instantiateQueue.Enqueue(new InstanceRequest
						{
							BaseFormId = partFormId,
							Position = new Vector3(worldX * WorldScale, worldZ * WorldScale, -(worldY * WorldScale)),
							Rotation = new Vector3(worldRx, worldRy, worldRz),
							Scale = 1f,
							CellFormId = cd.FormId,
						});
					}
				}
			}
			i++;
		}
	}

	private async Task LoadWorldAsync(string worldName)
	{
		if (_worldLoading.ContainsKey(worldName) && _worldLoading[worldName]) return;
		if (_worldContainers.ContainsKey(worldName))
		{
			ShowWorld(worldName);
			return;
		}

		await LoadWorldInnerAsync(worldName, visible: true);
		ShowWorld(worldName);
	}

	private async Task LoadWorldInnerAsync(string worldName, bool visible)
	{
		if (_worldLoading.ContainsKey(worldName) && _worldLoading[worldName]) return;
		if (_worldContainers.ContainsKey(worldName)) return;

		if (!_worldDataByName.TryGetValue(worldName, out var wd))
		{
			GD.PrintErr($"[Megaton] Unknown world: {worldName}");
			return;
		}

		_worldLoading[worldName] = true;
		GD.Print($"[Megaton] Loading world: {worldName} (0x{wd.FormId:X8})...");

		var container = new Node3D();
		container.Name = $"World_{worldName}";
		container.Visible = visible;
		AddChild(container);

		_worldContainers[worldName] = container;

		// Load terrain (synchronous, main thread)
		LoadTerrain(wd, container);

		// Load cell lighting (synchronous, main thread)
		LoadCellLighting(wd, container);

		// Load navmesh
		LoadNavMesh(wd, container);

		// Load ambient audio for this world
		LoadWorldAmbientAudio(wd, container);

		// Load REFRs on background thread
		await Task.Run(() => LoadWorldRefrs(wd));

		_worldLoading[worldName] = false;
		GD.Print($"[Megaton] Finished loading world: {worldName}");
	}

	private void LoadTerrain(WorldData wd, Node3D container)
	{
		try
		{
			var tiles = _terrainBuilder.BuildTerrainForWorld(wd.FormId, wd.Center, LoadTexture,
				wd.DefaultLandHeight, wd.NwCellX, wd.NwCellY, wd.SeCellX, wd.SeCellY);
			GD.Print($"[Megaton] World '{wd.Name}': loaded {tiles.Count} terrain tiles.");

			var lodList = new List<TerrainLodEntry>();

			foreach (var tile in tiles)
			{
				var name = $"Terrain_{tile.CellCoord.X}_{tile.CellCoord.Y}";

				var inst = new MeshInstance3D();
				inst.Mesh = tile.Mesh;
				inst.Name = name;
				container.AddChild(inst);

				lodList.Add(new TerrainLodEntry
				{
					Instance = inst,
					FullMesh = tile.Mesh,
					LodMesh = tile.LodMesh,
				});

				if (tile.CollisionShape != null)
				{
					var body = new StaticBody3D();
					body.Name = $"{name}_Collision";
					var colShape = new CollisionShape3D();
					colShape.Shape = tile.CollisionShape;
					body.AddChild(colShape);
					container.AddChild(body);
				}
			}

			_terrainLodEntries[wd.Name] = lodList;
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Terrain load error for '{wd.Name}': {e.Message}");
		}
	}

	private void LoadCellLighting(WorldData wd, Node3D container)
	{
		try
		{
			var cellIndex = _esm.BuildFormIdIndex(new[] { "CELL" });
			foreach (var kvp in cellIndex)
			{
				if (kvp.Value.WorldFormId != wd.FormId) continue;

				var lighting = _lightingLoader.GetCellLighting(kvp.Key);
				if (lighting != null)
				{
					GD.Print($"[Megaton] Found lighting for CELL 0x{kvp.Key:X8} in '{wd.Name}'");
					LightingLoader.ApplyCellLighting(container, lighting);
					break;
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Lighting load error for '{wd.Name}': {e.Message}");
		}
	}

	private void LoadNavMesh(WorldData wd, Node3D container)
	{
		try
		{
			var navMeshes = _navMeshBuilder.GetNavMeshesForWorld(wd.FormId);
			if (navMeshes.Count == 0)
			{
				GD.Print($"[Megaton] No navmeshes for world '{wd.Name}'");
				return;
			}

			int totalVerts = 0, totalPolys = 0;
			for (int i = 0; i < navMeshes.Count; i++)
			{
				var navData = navMeshes[i];
				NavigationMesh navMesh;
				try
				{
					navMesh = NavMeshBuilder.BuildNavigationMesh(navData, wd.Center, WorldScale);
				}
				catch
				{
					continue;
				}
				if (navMesh == null) continue;

				var region = new NavigationRegion3D();
				region.Name = $"NavMesh_{i}";
				region.NavigationMesh = navMesh;
				container.AddChild(region);
				totalVerts += navData.Vertices.Length;
				totalPolys += navData.Triangles.Length;
			}

			GD.Print($"[Megaton] Built {navMeshes.Count} navmesh regions for '{wd.Name}': " +
				$"{totalVerts} verts, {totalPolys} polys");
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] NavMesh load error for '{wd.Name}': {e.Message}");
		}
	}

	private void LoadWorldAmbientAudio(WorldData wd, Node3D container)
	{
		try
		{
			// Collect all SOUN records referenced in this world's REFR placements
			var ambientSounds = new List<SoundRecordData>();
			var seenFormIds = new HashSet<uint>();

			foreach (var kvp in _refrFormIDIndex)
			{
				if (kvp.Value.WorldFormId != wd.FormId) continue;

				try
				{
					var record = _esm.GetRecordAtOffset(kvp.Value.Offset);
					var subs = _esm.GetSubRecords(record);
					var nameSub = subs.FirstOrDefault(s => s.Type == "NAME");
					if (nameSub == null) continue;

					uint baseFormId = BitConverter.ToUInt32(nameSub.Data, 0);
					if (!_masterFormIDIndex.TryGetValue(baseFormId, out var baseEntry)) continue;

					var baseRecord = _esm.GetRecordAtOffset(baseEntry.Offset);
					if (baseRecord.Type != "SOUN") continue;

					if (seenFormIds.Contains(baseFormId)) continue;
					seenFormIds.Add(baseFormId);

					var soundData = _audioManager.ParseSoundRecord(baseFormId);
					if (soundData != null && !soundData.IsMenu && !soundData.IsDialogue)
						ambientSounds.Add(soundData);
				}
				catch { }
			}

			if (ambientSounds.Count > 0)
			{
				var ambientContainer = AudioManager.CreateAmbientSoundContainer(ambientSounds,
					(path) => _audioManager.LoadSound(path));
				if (ambientContainer != null)
					container.AddChild(ambientContainer);

				GD.Print($"[Megaton] Added {ambientSounds.Count} ambient sounds for '{wd.Name}'");
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Ambient audio load error for '{wd.Name}': {e.Message}");
		}
	}

	private void LoadWorldRefrs(WorldData wd)
	{
		var refrsToProcess = new List<long>();
		foreach (var kvp in _refrFormIDIndex)
		{
			if (kvp.Value.WorldFormId == wd.FormId)
				refrsToProcess.Add(kvp.Value.Offset);
		}

		int achrCount = 0, acreCount = 0;
		foreach (var kvp in _achrIndex)
		{
			if (kvp.Value.WorldFormId == wd.FormId)
			{
				refrsToProcess.Add(kvp.Value.Offset);
				achrCount++;
			}
		}
		foreach (var kvp in _acreIndex)
		{
			if (kvp.Value.WorldFormId == wd.FormId)
			{
				refrsToProcess.Add(kvp.Value.Offset);
				acreCount++;
			}
		}

		GD.Print($"[Megaton] World '{wd.Name}': {refrsToProcess.Count} placements (REFR + {achrCount} ACHR + {acreCount} ACRE).");

		Parallel.ForEach(refrsToProcess, offset =>
		{
			ProcessRecord(offset, wd);
		});

		GD.Print($"[Megaton] World '{wd.Name}': parsing done. Queue: {_instantiateQueue.Count}");
	}

		private void ProcessRecord(long offset, WorldData wd)
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

			string recType = record.Type;
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

			if (nifPath == null && baseType != "LIGH" && baseType != "SOUN") return;

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

				if (isDebug) return;

				EnsureNifParsed(nifPath);
			}

			float px = BitConverter.ToSingle(dataSub.Data, 0);
			float py = BitConverter.ToSingle(dataSub.Data, 4);
			float pz = BitConverter.ToSingle(dataSub.Data, 8);
			float rx = BitConverter.ToSingle(dataSub.Data, 12);
			float ry = BitConverter.ToSingle(dataSub.Data, 16);
			float rz = BitConverter.ToSingle(dataSub.Data, 20);

			var xclSub = subs.FirstOrDefault(s => s.Type == "XSCL");
			float scale = 1f;
			if (xclSub != null && xclSub.Data.Length >= 4)
			{
				scale = BitConverter.ToSingle(xclSub.Data, 0);
			}

			List<string> animPaths = null;
			if (baseType == "CREA" || baseType == "NPC_")
			{
				lock (_esm)
				{
					var kffz = baseSubs.FirstOrDefault(s => s.Type == "KFFZ");
					if (kffz != null && kffz.Data.Length > 0)
					{
						animPaths = new List<string>();
						int pos = 0;
						while (pos < kffz.Data.Length)
						{
							int end = Array.IndexOf(kffz.Data, (byte)0, pos);
							if (end < 0) end = kffz.Data.Length;
							string animPath = Encoding.ASCII.GetString(kffz.Data, pos, end - pos);
							if (!string.IsNullOrEmpty(animPath))
							{
								string p = animPath.Replace('\\', '/');
								if (!p.StartsWith("meshes/"))
									p = "meshes/" + p;
								animPaths.Add(p);
							}
							pos = end + 1;
						}
					}
				}
			}

			_instantiateQueue.Enqueue(new InstanceRequest
			{
				Path = nifPath,
				Position = new Vector3(
					(px - wd.Center.X) * WorldScale,
					pz * WorldScale,
					-(py - wd.Center.Y) * WorldScale),
				Rotation = new Vector3(rx, ry, rz),
				FormId = formId,
				BaseType = baseType,
				Scale = scale,
				WorldFormId = wd.FormId,
				BaseFormId = formId,
				AnimPaths = animPaths,
			});

			if (baseType == "SCOL")
			{
				EmitScolParts(baseSubs, formId, px, py, pz, rx, ry, rz, wd);
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[Megaton] Error processing REFR at 0x{offset:X8}: {e.Message}");
		}
	}

	private void EmitScolParts(List<SubRecord> baseSubs, uint scolFormId,
		float scolPx, float scolPy, float scolPz,
		float scolRx, float scolRy, float scolRz, WorldData wd)
	{
		int i = 0;
		while (i < baseSubs.Count)
		{
			var sub = baseSubs[i];
			if (sub.Type == "ONAM")
			{
				if (sub.Data.Length < 4) { i++; continue; }
				uint statFormId = BitConverter.ToUInt32(sub.Data, 0);

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

				if (i + 1 >= baseSubs.Count || baseSubs[i + 1].Type != "DATA")
				{
					i++;
					continue;
				}

				var dataSub = baseSubs[i + 1];
				i += 2;

				if (dataSub.Data.Length < 28) continue;

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

					float sRz = (float)Math.Sin(scolRz), cRz = (float)Math.Cos(scolRz);
					float sRy = (float)Math.Sin(scolRy), cRy = (float)Math.Cos(scolRy);
					float sRx = (float)Math.Sin(scolRx), cRx = (float)Math.Cos(scolRx);
					float s00 = cRz * cRy;
					float s01 = cRz * sRy * sRx - sRz * cRx;
					float s02 = cRz * sRy * cRx + sRz * sRx;
					float s10 = sRz * cRy;
					float s11 = sRz * sRy * sRx + cRz * cRx;
					float s12 = sRz * sRy * cRx - cRz * sRx;
					float s20 = -sRy;
					float s21 = cRy * sRx;
					float s22 = cRy * cRx;

					float lRz = (float)Math.Sin(lrz), lCz = (float)Math.Cos(lrz);
					float lRy = (float)Math.Sin(lry), lCy = (float)Math.Cos(lry);
					float lRx = (float)Math.Sin(lrx), lCx = (float)Math.Cos(lrx);
					float l00 = lCz * lCy;
					float l01 = lCz * lRy * lRx - lRz * lCx;
					float l02 = lCz * lRy * lCx + lRz * lRx;
					float l10 = lRz * lCy;
					float l11 = lRz * lRy * lRx + lCz * lCx;
					float l12 = lRz * lRy * lCx - lCz * lRx;
					float l20 = -lRy;
					float l21 = lCy * lRx;
					float l22 = lCy * lCx;

					float r00 = s00 * l00 + s01 * l10 + s02 * l20;
					float r01 = s00 * l01 + s01 * l11 + s02 * l21;
					float r02 = s00 * l02 + s01 * l12 + s02 * l22;
					float r10 = s10 * l00 + s11 * l10 + s12 * l20;
					float r11 = s10 * l01 + s11 * l11 + s12 * l21;
					float r12 = s10 * l02 + s11 * l12 + s12 * l22;
					float r20 = s20 * l00 + s21 * l10 + s22 * l20;
					float r21 = s20 * l01 + s21 * l11 + s22 * l21;
					float r22 = s20 * l02 + s21 * l12 + s22 * l22;

					float wx = scolPx + s00 * lx + s01 * ly + s02 * lz;
					float wy = scolPy + s10 * lx + s11 * ly + s12 * lz;
					float wz = scolPz + s20 * lx + s21 * ly + s22 * lz;

					float combinedRx = (float)Math.Atan2(r21, r22);
					float combinedRy = (float)Math.Asin(Math.Clamp(-r20, -1f, 1f));
					float combinedRz = (float)Math.Atan2(r10, r00);

					_instantiateQueue.Enqueue(new InstanceRequest
					{
						Path = partPath,
						Position = new Vector3(
							(wx - wd.Center.X) * WorldScale,
							wz * WorldScale,
							-(wy - wd.Center.Y) * WorldScale),
						Rotation = new Vector3(combinedRx, combinedRy, combinedRz),
						FormId = statFormId,
						BaseType = "STAT",
						Scale = scale,
						WorldFormId = wd.FormId,
					});
				}
			}
			else
			{
				i++;
			}
		}
	}

	private void SwitchToWorld(string worldName)
	{
		if (worldName == _currentWorldName) return;
		if (!_worldDataByName.ContainsKey(worldName))
		{
			GD.PrintErr($"[Megaton] Unknown world: {worldName}");
			return;
		}

		GD.Print($"[Megaton] Switching to world: {worldName}");

		if (_worldContainers.TryGetValue(worldName, out var container))
		{
			ShowWorld(worldName);
		}
		else
		{
			_ = LoadWorldAsync(worldName);
		}
	}

	private void ShowWorld(string worldName, bool repositionCamera = true)
	{
		if (_currentWorldName != null && _worldContainers.TryGetValue(_currentWorldName, out var oldContainer))
		{
			oldContainer.Visible = false;
		}

		_currentWorldName = worldName;

		if (_hud != null)
			_hud.SetWorldName($"World: {worldName}");

		if (_worldContainers.TryGetValue(worldName, out var container))
		{
			container.Visible = true;
		}

		if (_worldLabel != null)
		{
			int idx = _worldNameList.IndexOf(worldName);
			_worldLabel.Text = $"World [{idx + 1}]: {worldName}";
		}

		_cachedCamera = null;

		if (repositionCamera && _worldDataByName.TryGetValue(worldName, out var cameraWd))
		{
			RepositionCameraForWorld(cameraWd);
		}

		GD.Print($"[Megaton] Now showing world: {worldName}");
	}

	private void RepositionCameraForWorld(WorldData wd)
	{
		Node3D target = _player != null ? (Node3D)_player : GetViewport()?.GetCamera3D();
		if (target == null) return;

		float fo3X, fo3Y;

		// Check for configured start position
		if (TryReadStartPosition(wd.Name, out float startFo3X, out float startFo3Y))
		{
			fo3X = startFo3X;
			fo3Y = startFo3Y;
		}
		else
		{
			fo3X = (wd.NwCellX + wd.SeCellX) * CellSize / 2f;
			fo3Y = (wd.NwCellY + wd.SeCellY) * CellSize / 2f;
		}

		float godotX = (fo3X - wd.Center.X) * WorldScale;
		float godotZ = -(fo3Y - wd.Center.Y) * WorldScale;

		float godotY = wd.DefaultLandHeight * WorldScale + 20f;

		target.GlobalPosition = new Vector3(godotX, godotY, godotZ);

		GD.Print($"[Megaton] Player -> ({godotX:F1}, {godotY:F1}, {godotZ:F1}) for '{wd.Name}'");
	}

	private bool TryReadStartPosition(string worldName, out float fo3X, out float fo3Y)
	{
		fo3X = 0; fo3Y = 0;
		try
		{
			string configPath = ProjectSettings.GlobalizePath("res://config.json");
			if (!System.IO.File.Exists(configPath)) return false;

			string json = System.IO.File.ReadAllText(configPath);
			using var doc = System.Text.Json.JsonDocument.Parse(json);
			var root = doc.RootElement;
			if (!root.TryGetProperty("StartPositions", out var starts)) return false;
			if (!starts.TryGetProperty(worldName, out var pos)) return false;
			if (pos.TryGetProperty("X", out var x) && pos.TryGetProperty("Y", out var y))
			{
				fo3X = (float)x.GetDouble();
				fo3Y = (float)y.GetDouble();
				return true;
			}
		}
		catch { }
		return false;
	}

	private void UpdateAutoWorldSwitch()
	{
		if (_currentWorldName == null) return;

		// Check periodically
		if (_autoSwitchCooldown > 0) { _autoSwitchCooldown--; return; }

		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return;
		Vector3 camPos = cam.GlobalPosition;

		if (!_worldDataByName.TryGetValue(_currentWorldName, out var currentWd)) return;

		// Convert camera Godot position to absolute FO3 coordinates
		float camFo3X = camPos.X / WorldScale + currentWd.Center.X;
		float camFo3Y = -camPos.Z / WorldScale + currentWd.Center.Y;
		float camFo3Z = camPos.Y / WorldScale;

		// Find nearest exterior world (including current, with slight bias to prevent oscillation)
		string nearestWorld = _currentWorldName;
		float nearestDist = float.MaxValue;

		foreach (var kvp in _worldDataByName)
		{
			var wd = kvp.Value;
			if (!wd.HasMnam) continue;

			float dx = camFo3X - wd.Center.X;
			float dy = camFo3Y - wd.Center.Y;
			float dist = Mathf.Sqrt(dx * dx + dy * dy);

			// Tiny bias towards current world to prevent oscillation at equal distances
			if (kvp.Key == _currentWorldName)
				dist *= 0.99f;

			if (dist < nearestDist)
			{
				nearestDist = dist;
				nearestWorld = kvp.Key;
			}
		}

		// Pre-load nearby worlds (within 2x switch threshold) in the background
		float preloadThreshold = WorldSwitchDistFo3 * 2;
		foreach (var kvp in _worldDataByName)
		{
			if (!kvp.Value.HasMnam) continue;
			if (_worldContainers.ContainsKey(kvp.Key)) continue;
			if (_worldLoading.ContainsKey(kvp.Key) && _worldLoading[kvp.Key]) continue;

			float dx = camFo3X - kvp.Value.Center.X;
			float dy = camFo3Y - kvp.Value.Center.Y;
			float dist = Mathf.Sqrt(dx * dx + dy * dy);

			if (dist < preloadThreshold)
			{
				_ = LoadWorldInnerAsync(kvp.Key, visible: false);
			}
		}

		// Auto-switch if a different world is closer than threshold
		if (nearestWorld != _currentWorldName && nearestDist < WorldSwitchDistFo3)
		{
			AutoSwitchToWorld(nearestWorld, camFo3X, camFo3Y, camFo3Z);
		}
	}

	private void AutoSwitchToWorld(string worldName, float fo3X, float fo3Y, float fo3Z)
	{
		if (!_worldDataByName.TryGetValue(worldName, out var wd)) return;

		// If not loaded yet, switch asynchronously after loading
		if (!_worldContainers.ContainsKey(worldName))
		{
			_ = LoadWorldAndSwitchAutoAsync(worldName, fo3X, fo3Y, fo3Z);
			return;
		}

		// Convert FO3 coords to Godot coords in the target world
		float godotX = (fo3X - wd.Center.X) * WorldScale;
		float godotZ = -(fo3Y - wd.Center.Y) * WorldScale;
		float godotY = fo3Z * WorldScale;

		ShowWorld(worldName, repositionCamera: false);

		var cam = GetViewport()?.GetCamera3D();
		if (cam != null)
		{
			cam.GlobalPosition = new Vector3(godotX, godotY, godotZ);
		}

		// Cooldown to prevent rapid back-and-forth switching
		_autoSwitchCooldown = 60;
		GD.Print($"[Megaton] Auto-switched to world: {worldName} at ({godotX:F1}, {godotY:F1}, {godotZ:F1})");
	}

	private async Task LoadWorldAndSwitchAutoAsync(string worldName, float fo3X, float fo3Y, float fo3Z)
	{
		await LoadWorldInnerAsync(worldName, visible: false);
		AutoSwitchToWorld(worldName, fo3X, fo3Y, fo3Z);
	}

	private void CreateAndAddInstance(InstanceRequest req)
	{
		Node3D container;
		if (req.WorldFormId != 0 && _worldNameById.TryGetValue(req.WorldFormId, out var wName))
			container = _worldContainers.TryGetValue(wName, out var c) ? c : this;
		else
			container = this;

		MeshInstance3D inst = null;
		Node3D physicsBody = null;

		bool isNpc = req.BaseType == "NPC_" || req.BaseType == "CREA";

		if (!string.IsNullOrEmpty(req.Path))
		{
			var mesh = GetOrBuildMesh(req.Path);
			bool isSkinned = _skinnedCache.TryGetValue(req.Path, out var skinnedNodes) && skinnedNodes.Count > 0;

			// NPC/CREA handling: create NpcAgent with skinned mesh + AI
			if (isNpc)
			{
				var basis = Basis.Identity;
				basis = basis.Rotated(Vector3.Right,   req.Rotation.X);
				basis = basis.Rotated(Vector3.Forward, req.Rotation.Y);
				basis = basis.Rotated(Vector3.Up,      req.Rotation.Z);
				basis = basis.Scaled(Vector3.One * req.Scale);
				var transform = new Transform3D(basis, req.Position);

				var npcAgent = new NpcAgent();
				npcAgent.Name = $"NPC_{req.FormId:X8}";
				npcAgent.Transform = transform;
				npcAgent.NpcName = GetNpcNameShort(req.FormId);
				npcAgent.MovementSpeed = 1.5f + (float)new Random().NextDouble() * 1.5f;
				npcAgent.WanderRadius = 15f + (float)new Random().NextDouble() * 15f;
				container.AddChild(npcAgent);

				if (isSkinned)
				{
					foreach (var srcNode in skinnedNodes)
					{
						var clone = CloneNodeTree(srcNode);
						clone.Transform = Transform3D.Identity;
						npcAgent.AttachSkinnedMesh(clone);
						var meshInst = clone.FindChild("*", recursive: true) as MeshInstance3D;
						if (meshInst != null)
							TrackProp(meshInst, npcAgent, req.Position, req.Path, meshInst.Mesh as ArrayMesh, null);
					}
				}
				else if (mesh != null)
				{
					var meshInst2 = RentMeshInstance(req.Path, mesh, Transform3D.Identity, npcAgent);
					npcAgent.AttachSkinnedMesh(meshInst2);
					TrackProp(meshInst2, npcAgent, req.Position, req.Path, mesh, null);
				}

				if (req.AnimPaths != null && req.AnimPaths.Count > 0)
				{
					var anims = BuildAnimationsForNpc(req.Path, req.AnimPaths);
					npcAgent.LoadAnimations(anims);
				}

				goto AfterMesh;
			}

			// Check for skinned node hierarchy (skeleton + skinned meshes)
			if (isSkinned)
			{
				var basis = Basis.Identity;
				basis = basis.Rotated(Vector3.Right,   req.Rotation.X);
				basis = basis.Rotated(Vector3.Forward, req.Rotation.Y);
				basis = basis.Rotated(Vector3.Up,      req.Rotation.Z);
				basis = basis.Scaled(Vector3.One * req.Scale);
				var transform = new Transform3D(basis, req.Position);

				foreach (var srcNode in skinnedNodes)
				{
					var clone = CloneNodeTree(srcNode);
					clone.Transform = transform;
					container.AddChild(clone);
					var meshInst = clone.FindChild("*", recursive: true) as MeshInstance3D;
					if (meshInst != null)
						TrackProp(meshInst, clone, req.Position, req.Path, meshInst.Mesh as ArrayMesh, null);
				}
			}
			else if (mesh != null || req.BaseType == "LIGH")
			{
				if (mesh == null && req.BaseType != "LIGH") return;

				var basis = Basis.Identity;
				basis = basis.Rotated(Vector3.Right,   req.Rotation.X);
				basis = basis.Rotated(Vector3.Forward, req.Rotation.Y);
				basis = basis.Rotated(Vector3.Up,      req.Rotation.Z);
				basis = basis.Scaled(Vector3.One * req.Scale);
				var transform = new Transform3D(basis, req.Position);

				if (mesh != null && _nifCache.TryGetValue(req.Path, out var nif))
				{
					var colResult = NIFCollisionBuilder.BuildCollision(nif, null);
					if (colResult.HasValue)
					{
						physicsBody = colResult.Value.Body;
						physicsBody.Name = $"Body_{req.FormId:X8}";
						physicsBody.Transform = transform;

						inst = RentMeshInstance(req.Path, mesh, Transform3D.Identity, null);
						inst.Transform = Transform3D.Identity;
						physicsBody.AddChild(inst);

						container.AddChild(physicsBody);
						TrackProp(inst, physicsBody, req.Position, req.Path, mesh, null);
					}
				}

				if (inst == null && mesh != null)
				{
					inst = RentMeshInstance(req.Path, mesh, transform, container);
					TrackProp(inst, null, req.Position, req.Path, mesh, null);
				}
			}
		}

		AfterMesh:
		// Create particle systems from the NIF
		if (!string.IsNullOrEmpty(req.Path) && _particleCache.TryGetValue(req.Path, out var particles))
		{
			var basis = Basis.Identity;
			basis = basis.Rotated(Vector3.Right,   req.Rotation.X);
			basis = basis.Rotated(Vector3.Forward, req.Rotation.Y);
			basis = basis.Rotated(Vector3.Up,      req.Rotation.Z);
			basis = basis.Scaled(Vector3.One * req.Scale);
			var worldTransform = new Transform3D(basis, req.Position);

			foreach (var pEntry in particles)
			{
				var gp = new GpuParticles3D();
				var pm = new ParticleProcessMaterial();

				// Apply particle system transform (NIF local + world)
				var finalTransform = worldTransform * pEntry.Transform;
				gp.Transform = finalTransform;

				// Set texture from shader info
				string texPath = pEntry.ShaderInfo?.TexturePaths != null && pEntry.ShaderInfo.TexturePaths.Length > 0
					? pEntry.ShaderInfo.TexturePaths[0] : null;
				if (!string.IsNullOrEmpty(texPath))
				{
					var tex = LoadTexture(texPath);
					if (tex != null)
					{
						pm.Color = new Color(1, 1, 1, 1);
					}
				}

				// Alpha handling
				if (pEntry.AlphaInfo != null)
				{
					bool blend = (pEntry.AlphaInfo.Flags & 1) != 0;
					if (blend)
					{
						gp.DrawPasses = 1;
					}
				}

				gp.Amount = 64;
				gp.Lifetime = 2.0;
				gp.OneShot = false;
				gp.Emitting = true;
				gp.ProcessMaterial = pm;
				gp.LocalCoords = false;

				container.AddChild(gp);
			}
		}

		if (req.BaseType == "LIGH")
		{
			var lightData = _lightingLoader.ParseLight(req.FormId);
			if (lightData == null) return;

			var lightNode = LightingLoader.CreateLightNode(lightData, req.Position, req.Rotation);
			if (lightNode != null)
			{
				container.AddChild(lightNode);
			}
		}

		if (req.BaseType == "SOUN")
		{
			var soundData = _audioManager.ParseSoundRecord(req.FormId);
			if (soundData != null)
			{
				var player = AudioManager.CreateSoundPlayer(soundData, req.Position, req.Scale);
				if (player != null)
				{
					var stream = _audioManager.LoadSound(soundData.Filename);
					if (stream != null)
					{
						player.Stream = stream;
						player.Autoplay = true;
					}
					container.AddChild(player);
				}
			}
		}

		if (req.AnimPaths != null && req.AnimPaths.Count > 0)
		{
			TryLoadAnimations(req, container);
		}
	}

	private string GetNpcNameShort(uint formId)
	{
		if (!_masterFormIDIndex.TryGetValue(formId, out var entry)) return "NPC";

		try
		{
			var record = _esm.GetRecordAtOffset(entry.Offset);
			if (record.Type != "NPC_" && record.Type != "CREA") return "NPC";

			var subs = _esm.GetSubRecords(record);
			var full = subs.FirstOrDefault(s => s.Type == "FULL");
			if (full != null && full.Data.Length > 0)
			{
				return Encoding.ASCII.GetString(full.Data).TrimEnd('\0');
			}

			// Try editor ID
			var edid = subs.FirstOrDefault(s => s.Type == "EDID");
			if (edid != null && edid.Data.Length > 0)
			{
				return Encoding.ASCII.GetString(edid.Data).TrimEnd('\0');
			}
		}
		catch { }

		return $"NPC_{formId:X4}";
	}

	private void OnObjectPicked(Node3D pickedNode, Vector3 position, uint formId)
	{
		if (formId != 0)
		{
			GD.Print($"[Megaton] Picked object FormId=0x{formId:X8}: {pickedNode?.Name}");
			if (_hud != null)
				_hud.ShowInfo($"Selected: 0x{formId:X8}");
		}
		else if (pickedNode != null)
		{
			GD.Print($"[Megaton] Picked node: {pickedNode.Name}");
			if (_hud != null)
				_hud.ShowInfo($"Selected: {pickedNode.Name}");
		}
	}

	private List<(string Name, Animation Anim)> BuildAnimationsForNpc(string nifPath, List<string> animPaths)
	{
		var result = new List<(string Name, Animation Anim)>();

		if (!_nifCache.TryGetValue(nifPath, out var nif)) return result;
		var geom = NIFMeshBuilder.ExtractGeometry(nif);

		foreach (var kfPath in animPaths)
		{
			var anims = _animationManager.LoadKfAnimations(kfPath);
			if (anims == null) continue;

			foreach (var animData in anims)
			{
				if (geom.Skeleton == null) continue;

				try
				{
					var anim = KFAnimationLoader.BuildGodotAnimation(animData, geom.Skeleton, WorldScale);
					string name = animData.Name ?? System.IO.Path.GetFileNameWithoutExtension(kfPath);
					result.Add((name, anim));
				}
				catch (Exception e)
				{
					GD.PrintErr($"[Megaton] Failed to build anim '{animData.Name}': {e.Message}");
				}
			}
		}

		return result;
	}

	private void TryLoadAnimations(InstanceRequest req, Node3D container)
	{
		foreach (var kfPath in req.AnimPaths)
		{
			var anims = _animationManager.LoadKfAnimations(kfPath);
			if (anims == null || anims.Count == 0) continue;

			var skinnedNodes = container.GetChildren().OfType<Node3D>()
				.SelectMany(c => c.GetChildren().OfType<Skeleton3D>())
				.ToList();

			foreach (var skel in skinnedNodes)
			{
				int meshIdx = skel.GetChildCount() > 0 ? 0 : -1;
				if (meshIdx < 0) continue;

				var nifPath = req.Path;
				if (_nifCache.TryGetValue(nifPath, out var nifReader))
				{
					foreach (var anim in anims)
					{
						var geom = NIFMeshBuilder.ExtractGeometry(nifReader);
						if (geom.Skeleton != null)
						{
							_animationManager.AttachAnimationPlayer(skel, anim, geom.Skeleton);
							break;
						}
					}
				}
			}
		}
	}

	private void EnsureNifParsed(string path)
	{
		if (_nifCache.ContainsKey(path)) return;

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

	private ArrayMesh GetOrBuildMesh(string path)
	{
		if (_meshCache.TryGetValue(path, out var cached)) return cached;
		if (!_nifCache.TryGetValue(path, out var nif)) return null;

		// Classify NIF path for material override (Task 6)
		MaterialClass matClass = MaterialClassifier.ClassifyByPath(path);

		var geom = NIFMeshBuilder.ExtractGeometry(nif);

		// Cache particle systems
		if (geom.ParticleSystems.Count > 0)
		{
			_particleCache.TryAdd(path, geom.ParticleSystems);
		}

		// Build skeleton + skinned meshes if present
		if (geom.SkinnedSurfaces.Count > 0 && geom.Skeleton != null)
		{
			var skinnedNodes = NIFMeshBuilder.BuildSkinned(geom, LoadTexture);
			if (skinnedNodes.Count > 0)
			{
				_skinnedCache.TryAdd(path, skinnedNodes);
			}
		}

		if (geom.Surfaces.Count == 0) return null;

		var mesh = NIFMeshBuilder.BuildArrayMesh(geom);
		if (mesh.GetSurfaceCount() > 0)
		{
			for (int i = 0; i < mesh.GetSurfaceCount(); i++)
			{
				string texPath = mesh.SurfaceGetName(i);

				if (geom.Surfaces.Count > i)
				{
					var surface = geom.Surfaces[i];
					var mat = NIFMaterialBuilder.BuildMaterial(surface.Shader, surface.Alpha, LoadTexture, TextureHasAlpha);

					if (mat.AlbedoTexture == null && !string.IsNullOrEmpty(texPath))
					{
						var tex = LoadTexture(texPath);
						if (tex != null)
						{
							mat.AlbedoTexture = tex;
						}
					}

					// Apply material class override (Task 6: 材質分類)
					// Classify by NIF path, with shader type and havok material hints
					MaterialClass surfaceClass = matClass;
					if (surface.Shader != null)
					{
						var shaderBased = MaterialClassifier.ClassifyByShaderType(surface.Shader.ShaderType);
						if (shaderBased != MaterialClass.Unclassified)
							surfaceClass = shaderBased;
					}
					NIFMaterialBuilder.ApplyMaterialClass(mat, surfaceClass);

					mesh.SurfaceSetMaterial(i, mat);
				}
				else if (!string.IsNullOrEmpty(texPath))
				{
					var tex = LoadTexture(texPath);
					if (tex != null)
					{
					var mat = new StandardMaterial3D { AlbedoTexture = tex };
					NIFMaterialBuilder.ApplyMaterialClass(mat, matClass);
					mesh.SurfaceSetMaterial(i, mat);
					}
				}
			}

			_meshCache.TryAdd(path, mesh);
			return mesh;
		}

		return null;
	}

	private Texture2D _fallbackMagentaTex;
	private Texture2D _fallbackShadowTex;

	private Texture2D GetFallbackMagenta()
	{
		if (_fallbackMagentaTex == null)
		{
			var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
			img.Fill(new Color(1f, 0f, 1f));
			_fallbackMagentaTex = ImageTexture.CreateFromImage(img);
		}
		return _fallbackMagentaTex;
	}

	private Texture2D GetFallbackShadow()
	{
		if (_fallbackShadowTex == null)
		{
			var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
			img.Fill(new Color(0.05f, 0.05f, 0.05f, 0.6f));
			_fallbackShadowTex = ImageTexture.CreateFromImage(img);
		}
		return _fallbackShadowTex;
	}

	private bool IsShadowTexturePath(string path)
	{
		string lower = path.ToLowerInvariant();
		return lower.Contains("shadow") || lower.Contains("shdw") || lower.Contains("ambient_occlusion");
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

		BSAFile file = null;
		BSAReader owner = null;
		foreach (var (bsa, files) in _textureBsaList)
		{
			file = files.FirstOrDefault(f => f.Path.Equals(searchPath, StringComparison.OrdinalIgnoreCase));
			if (file != null) { owner = bsa; break; }
		}

		if (file == null && searchPath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
		{
			string altPath = searchPath.Substring(9);
			foreach (var (bsa, files) in _textureBsaList)
			{
				file = files.FirstOrDefault(f => f.Path.Equals(altPath, StringComparison.OrdinalIgnoreCase));
				if (file != null) { owner = bsa; break; }
			}
		}

		if (file == null || owner == null)
		{
			// Return fallback textures for shadow/known textures even if not found in BSA
			if (IsShadowTexturePath(searchPath))
			{
				var shadowTex = GetFallbackShadow();
				_textureCache.TryAdd(searchPath, shadowTex);
				return shadowTex;
			}
			return null;
		}

		byte[] data = owner.ReadFileData(file);
		if (data == null) return null;

		var img = new Image();
		Error err = img.LoadDdsFromBuffer(data);
		if (err == Error.Ok)
		{
			var tex = ImageTexture.CreateFromImage(img);
			_textureCache.TryAdd(searchPath, tex);

			Image.Format fmt = img.GetFormat();
				bool hasAlpha = fmt == Image.Format.Rgba8
					|| fmt == Image.Format.Rgba4444
					|| fmt == Image.Format.Dxt3
					|| fmt == Image.Format.Dxt5
					|| fmt == Image.Format.Dxt5RaAsRg
					|| fmt == Image.Format.BptcRgba
					|| fmt == Image.Format.Etc2Rgba8
					|| fmt == Image.Format.Etc2Rgb8A1
					|| fmt == Image.Format.Etc2RaAsRg;
			_textureHasAlpha.TryAdd(searchPath, hasAlpha);

			return tex;
		}

		// DDS load failed - try alternative approach for problematic formats
		// Some FO3 textures use custom DDS headers or unusual compression formats
		// Fall back to magenta error texture or shadow fallback
		if (IsShadowTexturePath(searchPath))
		{
			var shadowTex = GetFallbackShadow();
			_textureCache.TryAdd(searchPath, shadowTex);
			_textureHasAlpha.TryAdd(searchPath, true);
			return shadowTex;
		}

		GD.Print($"[Megaton] WARNING: Failed to load DDS texture '{searchPath}' (err={err}) - using fallback");
		var fallback = GetFallbackMagenta();
		_textureCache.TryAdd(searchPath, fallback);
		return fallback;
	}

	private bool TextureHasAlpha(string path)
	{
		path = path.Replace('\\', '/');
		string searchPath = path;
		if (!searchPath.StartsWith("textures/", StringComparison.OrdinalIgnoreCase))
			searchPath = "textures/" + searchPath;
		return _textureHasAlpha.TryGetValue(searchPath, out var hasAlpha) && hasAlpha;
	}

	private Node3D CloneNodeTree(Node3D src)
	{
		var clone = (Node3D)src.Duplicate(7);
		return clone;
	}

	private MeshInstance3D RentMeshInstance(string poolKey, ArrayMesh mesh, Transform3D transform, Node3D parent)
	{
		if (_meshPool.TryGetValue(poolKey, out var stack) && stack.Count > 0)
		{
			var inst = stack.Pop();
			inst.Mesh = mesh;
			inst.Visible = true;
			inst.Transform = transform;
			if (parent != null) parent.AddChild(inst);
			return inst;
		}
		var newInst = new MeshInstance3D { Mesh = mesh };
		newInst.Transform = transform;
		parent?.AddChild(newInst);
		return newInst;
	}

	private void ReturnMeshInstance(string poolKey, MeshInstance3D inst)
	{
		inst.GetParent()?.RemoveChild(inst);
		inst.Visible = false;
		if (!_meshPool.TryGetValue(poolKey, out var stack))
		{
			stack = new Stack<MeshInstance3D>();
			_meshPool[poolKey] = stack;
		}
		stack.Push(inst);
	}

	private void TrackProp(MeshInstance3D meshInst, Node3D parent, Vector3 position,
		string nifPath, ArrayMesh fullMesh, ArrayMesh lodMesh, float radius = 0f)
	{
		if (_currentWorldName == null) return;
		if (!_propEntries.TryGetValue(_currentWorldName, out var list))
		{
			list = new List<PropEntry>();
			_propEntries[_currentWorldName] = list;
		}
		list.Add(new PropEntry
		{
			MeshInstance = meshInst,
			Parent = parent,
			Position = position,
			BoundingRadius = radius,
			NifPath = nifPath,
			FullMesh = fullMesh,
			LodMesh = lodMesh,
			Valid = true,
		});
	}

	private void ReturnWorldPropsToPool(string worldName)
	{
		if (!_propEntries.TryGetValue(worldName, out var entries)) return;

		foreach (var entry in entries)
		{
			if (!entry.Valid) continue;
			if (entry.MeshInstance != null)
			{
				string key = entry.NifPath ?? "";
				ReturnMeshInstance(key, entry.MeshInstance);
			}
		}
		entries.Clear();
	}

	// FO3方式のチャンク読み込み: プレイヤー座標からの距離に基づき表示/非表示を切り替える。
	// カメラの向きは一切参照しない（IsPositionInFrustumは使用しない）。
	private const float PropCullDistance = 15000f;  // Godot単位（FO3約10000ユニット≒150m相当）

	private void UpdateFrustumCulling()
	{
		if (_currentWorldName == null) return;
		if (!_propEntries.TryGetValue(_currentWorldName, out var entries)) return;

		// プレイヤー（またはカメラ）の現在位置を取得
		Vector3 playerPos = Vector3.Zero;
		if (_player != null && IsInstanceValid(_player))
			playerPos = _player.GlobalPosition;
		else
		{
			var cam = GetCachedCamera();
			if (cam == null) return;
			playerPos = cam.GlobalPosition;
		}

		float cullDistSq = PropCullDistance * PropCullDistance;

		foreach (var entry in entries)
		{
			if (!entry.Valid || entry.MeshInstance == null) continue;
			// プレイヤーからの距離で表示判定（カメラ向き非依存）
			float distSq = entry.Position.DistanceSquaredTo(playerPos);
			bool visible = distSq <= cullDistSq;
			entry.MeshInstance.Visible = visible;
			if (entry.Parent != null && entry.Parent is StaticBody3D sb)
				sb.Visible = visible;
		}
	}

	private void UpdatePropLod()
	{
		if (_currentWorldName == null) return;
		if (!_propEntries.TryGetValue(_currentWorldName, out var entries)) return;

		var cam = GetCachedCamera();
		if (cam == null) return;
		Vector3 camPos = cam.GlobalPosition;
		float lodDistSq = PropLodDistance * PropLodDistance;

		foreach (var entry in entries)
		{
			if (!entry.Valid || entry.MeshInstance == null) continue;
			if (entry.LodMesh == null) continue;

			float distSq = entry.Position.DistanceSquaredTo(camPos);
			bool useLod = distSq > lodDistSq;
			bool currentIsLod = entry.MeshInstance.Mesh == entry.LodMesh;

			if (useLod && !currentIsLod)
				entry.MeshInstance.Mesh = entry.LodMesh;
			else if (!useLod && currentIsLod)
				entry.MeshInstance.Mesh = entry.FullMesh;
		}
	}

	private Camera3D GetCachedCamera()
	{
		if (_cachedCamera == null || !IsInstanceValid(_cachedCamera))
		{
			_cachedCamera = GetViewport()?.GetCamera3D();
		}
		return _cachedCamera;
	}
}
