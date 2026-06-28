using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenFo3.BSA;
using OpenFo3.ESM;

namespace OpenFo3.Modding
{
	public class ModPlugin
	{
		public string FileName;
		public string FullPath;
		public uint FormIdStart; // Base form ID for this plugin
		public uint FormIdEnd;
		public bool IsMaster; // .esm
		public bool IsLight; // .esl
		public bool IsEnabled = true;
		public int LoadOrder;
		public List<ModPlugin> Masters = new();
		public List<string> MasterNames = new();
		public Dictionary<uint, uint> FormIdRemap = new(); // old -> new after merging
	}

	public class VirtualFileSystem
	{
		// Override priority: higher load order = higher priority
		private Dictionary<string, string> _fileOverrides = new(); // virtual path -> real path

		public void RegisterOverride(string virtualPath, string realPath)
		{
			virtualPath = NormalizePath(virtualPath);
			_fileOverrides[virtualPath] = realPath;
		}

		public string ResolvePath(string virtualPath)
		{
			virtualPath = NormalizePath(virtualPath);
			return _fileOverrides.TryGetValue(virtualPath, out var realPath) ? realPath : null;
		}

		public bool HasOverride(string virtualPath)
		{
			return _fileOverrides.ContainsKey(NormalizePath(virtualPath));
		}

		private string NormalizePath(string path)
		{
			return path.Replace('\\', '/').ToLowerInvariant().TrimStart('/');
		}
	}

	public partial class ModManager : Node
	{
		private List<ModPlugin> _plugins = new();
		private VirtualFileSystem _vfs = new();
		private string _dataPath;

		public IReadOnlyList<ModPlugin> Plugins => _plugins.AsReadOnly();
		public VirtualFileSystem VFS => _vfs;

		[Signal]
		public delegate void ModLoadOrderChangedEventHandler();

		[Signal]
		public delegate void ModEnabledEventHandler(string fileName, bool enabled);

		[Signal]
		public delegate void ModErrorEventHandler(string fileName, string error);

		public ModManager(string dataPath)
		{
			_dataPath = dataPath;
		}

		public void ScanForPlugins()
		{
			_plugins.Clear();
			if (!Directory.Exists(_dataPath)) return;

			// Scan for .esm, .esp, .esl files
			var patterns = new[] { "*.esm", "*.esp", "*.esl" };
			foreach (var pattern in patterns)
			{
				foreach (var file in Directory.GetFiles(_dataPath, pattern, SearchOption.TopDirectoryOnly))
				{
					string fileName = Path.GetFileName(file);
					string ext = Path.GetExtension(fileName).ToLowerInvariant();

					var plugin = new ModPlugin
					{
						FileName = fileName,
						FullPath = file,
						IsMaster = ext == ".esm",
						IsLight = ext == ".esl",
						LoadOrder = _plugins.Count,
					};

					// Read ESM header to detect masters
					try
					{
						DetectMasters(plugin);
					}
					catch (Exception e)
					{
						GD.PrintErr($"[ModManager] Error reading {fileName}: {e.Message}");
						EmitSignal(nameof(ModErrorEventHandler), fileName, e.Message);
						continue;
					}

					_plugins.Add(plugin);
					GD.Print($"[ModManager] Found plugin: {fileName} ({(plugin.IsMaster ? "ESM" : plugin.IsLight ? "ESL" : "ESP")})");
				}
			}

			// Sort: ESM first (by file order), then ESP/ESL
			SortLoadOrder();
			RemapFormIds();
			BuildVirtualFileSystem();
		}

		private void DetectMasters(ModPlugin plugin)
		{
			using var br = new BinaryReader(File.OpenRead(plugin.FullPath));
			string type = new(br.ReadChars(4));
			uint size = br.ReadUInt32();

			// Read TES4 header
			br.ReadUInt32(); // FormId
			br.ReadUInt32(); // flags
			br.ReadUInt32(); // version
			long end = br.BaseStream.Position + (size - 12);

			// Look for MAST subrecords
			while (br.BaseStream.Position < end)
			{
				string subType = new(br.ReadChars(4));
				ushort subSize = br.ReadUInt16();

				if (subType == "MAST")
				{
					string masterName = System.Text.Encoding.ASCII.GetString(br.ReadBytes(subSize)).TrimEnd('\0');
					plugin.MasterNames.Add(masterName);
				}
				else
				{
					br.ReadBytes(subSize);
				}
			}
		}

		private void SortLoadOrder()
		{
			// Simple topological sort by master dependencies
			// ESM files first, then ESPs
			var sorted = new List<ModPlugin>();
			var visited = new HashSet<string>();
			var visiting = new HashSet<string>();

			foreach (var plugin in _plugins)
			{
				TopologicalSort(plugin, sorted, visited, visiting);
			}

			// Update load order indices
			for (int i = 0; i < sorted.Count; i++)
			{
				sorted[i].LoadOrder = i;
			}

			_plugins = sorted;
		}

		private void TopologicalSort(ModPlugin plugin, List<ModPlugin> sorted, HashSet<string> visited, HashSet<string> visiting)
		{
			if (visited.Contains(plugin.FileName)) return;
			if (visiting.Contains(plugin.FileName))
			{
				GD.PrintErr($"[ModManager] Circular dependency detected involving {plugin.FileName}");
				return;
			}

			visiting.Add(plugin.FileName);

			// Process masters first
			foreach (var masterName in plugin.MasterNames)
			{
				var master = _plugins.Find(p => string.Equals(p.FileName, masterName, StringComparison.OrdinalIgnoreCase));
				if (master != null)
					TopologicalSort(master, sorted, visited, visiting);
			}

			visiting.Remove(plugin.FileName);
			visited.Add(plugin.FileName);
			sorted.Add(plugin);
		}

		private void RemapFormIds()
		{
			// Each plugin gets a FormId range based on load order
			// ESM/ESL: 0x000000-0x00FFFFFF per slot
			// ESP: 0xFF000000 + load order * 0x00100000
			foreach (var plugin in _plugins)
			{
				if (plugin.IsMaster || plugin.IsLight)
				{
					plugin.FormIdStart = 0x00000000u + (uint)(plugin.LoadOrder) * 0x01000000u;
					plugin.FormIdEnd = plugin.FormIdStart + 0x00FFFFFFu;
				}
				else
				{
					plugin.FormIdStart = 0xFF000000u + (uint)(plugin.LoadOrder) * 0x00100000u;
					plugin.FormIdEnd = plugin.FormIdStart + 0x000FFFFFu;
				}
			}
		}

		private void BuildVirtualFileSystem()
		{
			_vfs = new VirtualFileSystem();

			// Register overrides for each plugin (later plugins override earlier)
			foreach (var plugin in _plugins)
			{
				if (!plugin.IsEnabled) continue;

				// Register the plugin's BSA or loose files
				string bsaPath = Path.ChangeExtension(plugin.FullPath, ".bsa");
				if (File.Exists(bsaPath))
				{
					RegisterBSAOverrides(bsaPath, plugin);
				}

				// Register loose files in the plugin's directory
				string pluginDir = Path.GetDirectoryName(plugin.FullPath);
				if (Directory.Exists(pluginDir))
				{
					RegisterLooseFiles(pluginDir, plugin);
				}
			}
		}

		private void RegisterBSAOverrides(string bsaPath, ModPlugin plugin)
		{
			// BSA files are registered in order of load order
			// The higher the load order, the higher the priority
		}

		private void RegisterLooseFiles(string directory, ModPlugin plugin)
		{
			if (!Directory.Exists(directory)) return;

			foreach (var file in Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories))
			{
				string relativePath = Path.GetRelativePath(_dataPath, file).Replace('\\', '/');
				_vfs.RegisterOverride(relativePath, file);
			}
		}

		public void SetPluginEnabled(string fileName, bool enabled)
		{
			var plugin = _plugins.Find(p => p.FileName == fileName);
			if (plugin != null)
			{
				plugin.IsEnabled = enabled;
				EmitSignal(nameof(ModEnabledEventHandler), fileName, enabled);
				BuildVirtualFileSystem();
				EmitSignal(nameof(ModLoadOrderChangedEventHandler));
			}
		}

		public void MovePlugin(int fromIndex, int toIndex)
		{
			if (fromIndex < 0 || fromIndex >= _plugins.Count) return;
			if (toIndex < 0 || toIndex >= _plugins.Count) return;

			var plugin = _plugins[fromIndex];
			_plugins.RemoveAt(fromIndex);
			_plugins.Insert(toIndex, plugin);

			// Update load order indices
			for (int i = 0; i < _plugins.Count; i++)
				_plugins[i].LoadOrder = i;

			RemapFormIds();
			BuildVirtualFileSystem();
			EmitSignal(nameof(ModLoadOrderChangedEventHandler));
		}

		public uint RemapFormId(uint originalFormId, ModPlugin sourcePlugin)
		{
			// Remap form ID from source plugin's range to merged range
			if (sourcePlugin == null) return originalFormId;

			uint offset = originalFormId & 0x00FFFFFF;
			return sourcePlugin.FormIdStart + offset;
		}

		public ModPlugin GetPluginForFormId(uint formId)
		{
			foreach (var plugin in _plugins)
			{
				if (formId >= plugin.FormIdStart && formId <= plugin.FormIdEnd)
					return plugin;
			}
			return null;
		}

		// Save/load load order to JSON
		public void SaveLoadOrder(string path)
		{
			var data = new LoadOrderData
			{
				Plugins = _plugins.Select(p => new PluginEntry
				{
					FileName = p.FileName,
					IsEnabled = p.IsEnabled,
				}).ToList()
			};

			string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(path, json);
		}

		public void LoadLoadOrder(string path)
		{
			if (!File.Exists(path)) return;

			try
			{
				string json = File.ReadAllText(path);
				var data = JsonSerializer.Deserialize<LoadOrderData>(json);
				if (data?.Plugins == null) return;

				foreach (var entry in data.Plugins)
				{
					var plugin = _plugins.Find(p => p.FileName == entry.FileName);
					if (plugin != null)
						plugin.IsEnabled = entry.IsEnabled;
				}

				BuildVirtualFileSystem();
				EmitSignal(nameof(ModLoadOrderChangedEventHandler));
			}
			catch (Exception e)
			{
				GD.PrintErr($"[ModManager] Failed to load load order: {e.Message}");
			}
		}

		private class LoadOrderData
		{
			public List<PluginEntry> Plugins { get; set; } = new();
		}

		private class PluginEntry
		{
			public string FileName { get; set; }
			public bool IsEnabled { get; set; } = true;
		}
	}
}
