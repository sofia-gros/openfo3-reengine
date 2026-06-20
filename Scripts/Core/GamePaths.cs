using System;
using System.IO;
using System.Text.Json;
using Godot;

public static class GamePaths
{
    private static string _fallout3Root;
    private static string _dataPath;
    private static string _esmPath;
    private static string[] _bsaFilePaths = Array.Empty<string>();
    private static string _targetWorld = "MegatonWorld";
    private static double _worldCenterX = -14200.0;
    private static double _worldCenterY = -3800.0;

    private static bool _initialized = false;
    private static readonly object _lock = new();

    public static string Fallout3Root
    {
        get { EnsureInitialized(); return _fallout3Root; }
    }

    public static string DataPath
    {
        get { EnsureInitialized(); return _dataPath; }
    }

    public static string EsmPath
    {
        get { EnsureInitialized(); return _esmPath; }
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            Initialize();
            _initialized = true;
        }
    }

    private static void Initialize()
    {
        string configPath = ProjectSettings.GlobalizePath("res://config.json");
        string defaultRoot = @"A:\SteamLibrary\steamapps\common\Fallout 3 goty";

        try
        {
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("Fallout3Root", out var rootProp))
                {
                    string configured = rootProp.GetString();
                    if (!string.IsNullOrEmpty(configured))
                        defaultRoot = configured;
                }
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[GamePaths] Failed to read config.json: {e.Message}");
        }

        _fallout3Root = defaultRoot;
        _dataPath = Path.Combine(_fallout3Root, "Data");
        _esmPath = Path.Combine(_dataPath, "Fallout3.esm");

        GD.Print($"[GamePaths] Root: {_fallout3Root}");
        GD.Print($"[GamePaths] Data: {_dataPath}");
        GD.Print($"[GamePaths] ESM:  {_esmPath}");

        // Read BSA file list from config
        var bsaList = new System.Collections.Generic.List<string>();
        try
        {
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("BSA", out var bsaSection))
                {
                    foreach (var prop in bsaSection.EnumerateObject())
                    {
                        string fileName = prop.Value.GetString();
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            string fullPath = Path.Combine(_dataPath, fileName);
                            if (File.Exists(fullPath))
                            {
                                bsaList.Add(fullPath);
                                GD.Print($"[GamePaths] Registered BSA: {fileName}");
                            }
                            else
                            {
                                GD.Print($"[GamePaths] BSA not found, skipping: {fileName}");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[GamePaths] Failed to read BSA config: {e.Message}");
        }
        _bsaFilePaths = bsaList.ToArray();

        // Read World config
        try
        {
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("World", out var worldSection))
                {
                    if (worldSection.TryGetProperty("TargetWorld", out var tw))
                    {
                        string val = tw.GetString();
                        if (!string.IsNullOrEmpty(val)) _targetWorld = val;
                    }
                    if (worldSection.TryGetProperty("CenterX", out var cx))
                        _worldCenterX = cx.GetDouble();
                    if (worldSection.TryGetProperty("CenterY", out var cy))
                        _worldCenterY = cy.GetDouble();
                }
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[GamePaths] Failed to read World config: {e.Message}");
        }
    }

    public static string[] GetBSAFilePaths()
    {
        EnsureInitialized();
        return _bsaFilePaths;
    }

    public static string GetTargetWorld()
    {
        EnsureInitialized();
        return _targetWorld;
    }

    public static Vector2 GetWorldCenter()
    {
        EnsureInitialized();
        return new Vector2((float)_worldCenterX, (float)_worldCenterY);
    }
}
