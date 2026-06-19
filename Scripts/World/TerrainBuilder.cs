using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenFo3.ESM;

namespace OpenFo3.World
{
    public class TerrainBuilder
    {
        private const int GridSize = 33;
        private const float CellSize = 4096f;
        private const float WorldScale = 0.015f;
        private const float HeightScale = 0.015f;

        // 地形の凹凸を強調する倍率（1.0 = 等倍）。
        // FO3の高さ単位（VHGT baseHeight + 累積デルタ）は絶対Z値であり、
        // REFRアセットのZ値と同一座標系。異なる倍率をかけると建物が地形に
        // めり込んだり浮いたりする。基本的に 1.0 のままにすること。
        private const float HeightExaggeration = 1.0f;

        private ESMReader _esm;
        private Dictionary<uint, RecordEntry> _landIndex;
        private Dictionary<uint, RecordEntry> _ltexIndex;
        private Dictionary<uint, RecordEntry> _cellIndex;
        private Dictionary<uint, RecordEntry> _txstIndex;

        public TerrainBuilder(ESMReader esm)
        {
            _esm = esm;
            _landIndex = esm.BuildFormIdIndex(new[] { "LAND" });
            _ltexIndex = esm.BuildFormIdIndex(new[] { "LTEX" });
            _cellIndex = esm.BuildFormIdIndex(new[] { "CELL" });
            _txstIndex = esm.BuildFormIdIndex(new[] { "TXST" });
            GD.Print($"[TerrainBuilder] LTEX index: {_ltexIndex.Count} entries, TXST index: {_txstIndex.Count} entries");
        }

        private string ResolveTexturePath(uint texFormId)
        {
            try
            {
                // Try LTEX -> TXST chain first
                if (_ltexIndex.TryGetValue(texFormId, out var ltexEntry))
                {
                    var ltexRecord = _esm.GetRecordAtOffset(ltexEntry.Offset);
                    var ltexSubs = _esm.GetSubRecords(ltexRecord);
                    var tnam = ltexSubs.FirstOrDefault(s => s.Type == "TNAM");
                    if (tnam != null && tnam.Data.Length >= 4)
                    {
                        uint txstFormId = BitConverter.ToUInt32(tnam.Data, 0);
                        if (_txstIndex.TryGetValue(txstFormId, out var txstEntry))
                        {
                            var txstRecord = _esm.GetRecordAtOffset(txstEntry.Offset);
                            var txstSubs = _esm.GetSubRecords(txstRecord);
                            var tx00 = txstSubs.FirstOrDefault(s => s.Type == "TX00");
                            if (tx00 != null)
                            {
                                string path = Encoding.ASCII.GetString(tx00.Data).TrimEnd('\0').Replace('\\', '/');
                                GD.Print($"[TerrainBuilder] Resolved 0x{texFormId:X8} -> {path}");
                                return path;
                            }
                        }
                    }
                }

                // Try TXST directly (some BTXT might reference TXST directly)
                if (_txstIndex.TryGetValue(texFormId, out var txstEntry2))
                {
                    var txstRecord = _esm.GetRecordAtOffset(txstEntry2.Offset);
                    var txstSubs = _esm.GetSubRecords(txstRecord);
                    var tx00 = txstSubs.FirstOrDefault(s => s.Type == "TX00");
                    if (tx00 != null)
                    {
                        string path = Encoding.ASCII.GetString(tx00.Data).TrimEnd('\0').Replace('\\', '/');
                        GD.Print($"[TerrainBuilder] Resolved 0x{texFormId:X8} directly as TXST -> {path}");
                        return path;
                    }
                }

                GD.Print($"[TerrainBuilder] Could not resolve 0x{texFormId:X8} (not in LTEX or TXST)");
                return null;
            }
            catch (Exception ex)
            {
                GD.Print($"[TerrainBuilder] ResolveTexturePath exception for 0x{texFormId:X8}: {ex.Message}");
                return null;
            }
        }

        public class TerrainTile
    {
        public ArrayMesh Mesh;
        public Shape3D CollisionShape;
        public Vector2 CellCoord;
    }

        public List<TerrainTile> BuildTerrainForWorld(uint worldFormId, Vector2 megatonCenter,
            Func<string, Texture2D> loadTexture,
            float defaultLandHeight = 0f,
            int fillCellMinX = 0, int fillCellMinY = 0,
            int fillCellMaxX = -1, int fillCellMaxY = -1)
        {
            var tiles = new List<TerrainTile>();
            var coveredCells = new HashSet<(int, int)>();
            var landCells = new List<uint>();

            foreach (var kvp in _landIndex)
            {
                if (kvp.Value.WorldFormId == worldFormId)
                {
                    landCells.Add(kvp.Key);
                }
            }

            GD.Print($"[TerrainBuilder] Found {landCells.Count} LAND records in world 0x{worldFormId:X8}");

            foreach (uint landFormId in landCells)
            {
                if (!_landIndex.TryGetValue(landFormId, out var landEntry))
                {
                    GD.Print($"[TerrainBuilder] LAND 0x{landFormId:X8} not found in index");
                    continue;
                }

                uint cellFormId = landEntry.CellFormId;
                if (!TryGetCellCoord(cellFormId, out int cellX, out int cellY))
                {
                    GD.Print($"[TerrainBuilder] Failed to get cell coords for CELL 0x{cellFormId:X8} (LAND 0x{landFormId:X8}) - skipping LAND tile");
                    continue;
                }

                coveredCells.Add((cellX, cellY));

                var tile = BuildTerrainTile(landFormId, cellX, cellY, loadTexture, megatonCenter);
                if (tile != null)
                {
                    tiles.Add(tile);
                    GD.Print($"[TerrainBuilder] Built tile for cell ({cellX}, {cellY})");
                }
                else
                {
                    GD.Print($"[TerrainBuilder] BuildTerrainTile returned null for cell ({cellX}, {cellY})");
                }
            }

            // Fill gaps with flat terrain
            bool hasValidBounds = fillCellMaxX >= fillCellMinX && fillCellMaxY >= fillCellMinY;
            if (!hasValidBounds && coveredCells.Count > 0)
            {
                fillCellMinX = int.MaxValue; fillCellMinY = int.MaxValue;
                fillCellMaxX = int.MinValue; fillCellMaxY = int.MinValue;
                foreach (var (x, y) in coveredCells)
                {
                    if (x < fillCellMinX) fillCellMinX = x;
                    if (x > fillCellMaxX) fillCellMaxX = x;
                    if (y < fillCellMinY) fillCellMinY = y;
                    if (y > fillCellMaxY) fillCellMaxY = y;
                }
                hasValidBounds = true;
            }

            if (hasValidBounds)
            {
                int landMinX = int.MaxValue, landMinY = int.MaxValue;
                int landMaxX = int.MinValue, landMaxY = int.MinValue;
                foreach (var (x, y) in coveredCells)
                {
                    if (x < landMinX) landMinX = x;
                    if (x > landMaxX) landMaxX = x;
                    if (y < landMinY) landMinY = y;
                    if (y > landMaxY) landMaxY = y;
                }

                int padding = coveredCells.Count > 0 ? 10 : 0;
                int fillMinX = Math.Max(landMinX - padding, fillCellMinX);
                int fillMinY = Math.Max(landMinY - padding, fillCellMinY);
                int fillMaxX = Math.Min(landMaxX + padding, fillCellMaxX);
                int fillMaxY = Math.Min(landMaxY + padding, fillCellMaxY);

                int totalCells = (fillMaxX - fillMinX + 1) * (fillMaxY - fillMinY + 1);
                if (totalCells > 5000)
                {
                    GD.Print($"[TerrainBuilder] WARNING: {totalCells} flat tiles exceeds 5000 cap — using padding=5");
                    padding = 5;
                    fillMinX = Math.Max(landMinX - padding, fillCellMinX);
                    fillMinY = Math.Max(landMinY - padding, fillCellMinY);
                    fillMaxX = Math.Min(landMaxX + padding, fillCellMaxX);
                    fillMaxY = Math.Min(landMaxY + padding, fillCellMaxY);
                }

                int flatBuilt = 0;
                for (int y = fillMinY; y <= fillMaxY; y++)
                {
                    for (int x = fillMinX; x <= fillMaxX; x++)
                    {
                        if (!coveredCells.Contains((x, y)))
                        {
                            var flatTile = BuildFlatTerrainTile(x, y, defaultLandHeight, megatonCenter);
                            if (flatTile != null)
                            {
                                tiles.Add(flatTile);
                                flatBuilt++;
                            }
                        }
                    }
                }
                GD.Print($"[TerrainBuilder] Built {flatBuilt} gap-fill flat terrain tiles at default height {defaultLandHeight} (area {totalCells} cells)");
            }

            // If absolutely NO tiles were generated (no LAND records + no flat fill),
            // force-generate flat terrain for the world cell bounds
            if (tiles.Count == 0 && fillCellMaxX >= fillCellMinX && fillCellMaxY >= fillCellMinY)
            {
                int totalCells = (fillCellMaxX - fillCellMinX + 1) * (fillCellMaxY - fillCellMinY + 1);
                int midX = (fillCellMinX + fillCellMaxX) / 2;
                int midY = (fillCellMinY + fillCellMaxY) / 2;
                int halfSize = 70;
                int minX = Math.Max(fillCellMinX, midX - halfSize);
                int maxX = Math.Min(fillCellMaxX, midX + halfSize);
                int minY = Math.Max(fillCellMinY, midY - halfSize);
                int maxY = Math.Min(fillCellMaxY, midY + halfSize);

                GD.Print($"[TerrainBuilder] No tiles generated! Force-generating flat terrain for cell range ({minX},{minY})-({maxX},{maxY})");

                int forceBuilt = 0;
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        var flatTile = BuildFlatTerrainTile(x, y, defaultLandHeight, megatonCenter);
                        if (flatTile != null)
                        {
                            tiles.Add(flatTile);
                            forceBuilt++;
                        }
                    }
                }
                GD.Print($"[TerrainBuilder] Force-built {forceBuilt} flat terrain tiles at default height {defaultLandHeight}");
            }

            return tiles;
        }

        private bool TryGetCellCoord(uint formId, out int cellX, out int cellY)
        {
            cellX = 0; cellY = 0;
            try
            {
                // LAND formId == CELL formId in FO3. Read XCLC from the CELL record.
                if (!_cellIndex.TryGetValue(formId, out var entry))
                {
                    GD.Print($"[TerrainBuilder] CELL not found in index for formId 0x{formId:X8}");
                    return false;
                }
                var record = _esm.GetRecordAtOffset(entry.Offset);
                var subs = _esm.GetSubRecords(record);

                GD.Print($"[TerrainBuilder] CELL record at 0x{entry.Offset:X8}, type={record.Type}, subCount={subs.Count}");

                var xclc = subs.FirstOrDefault(s => s.Type == "XCLC");
                if (xclc == null || xclc.Data.Length < 8)
                {
                    GD.Print($"[TerrainBuilder] No XCLC found in CELL 0x{formId:X8}");
                    return false;
                }

                cellX = BitConverter.ToInt32(xclc.Data, 0);
                cellY = BitConverter.ToInt32(xclc.Data, 4);
                GD.Print($"[TerrainBuilder] CELL 0x{formId:X8} XCLC=({cellX}, {cellY})");
                return true;
            }
            catch (Exception ex)
            {
                GD.Print($"[TerrainBuilder] TryGetCellCoord exception for 0x{formId:X8}: {ex.Message}");
                return false;
            }
        }

        private TerrainTile BuildTerrainTile(uint landFormId, int cellX, int cellY,
            Func<string, Texture2D> loadTexture, Vector2 megatonCenter)
        {
            try
            {
                if (!_landIndex.TryGetValue(landFormId, out var entry)) return null;
                var record = _esm.GetRecordAtOffset(entry.Offset);
                var subs = _esm.GetSubRecords(record);

                byte[] vhgtData = null, vnmlData = null, vclrData = null;
                List<byte[]> vtexData = new();

                foreach (var sub in subs)
                {
                    switch (sub.Type)
                    {
                        case "VHGT": vhgtData = sub.Data; break;
                        case "VNML": vnmlData = sub.Data; break;
                        case "VCLR": vclrData = sub.Data; break;
                        case "VTEX": vtexData.Add(sub.Data); break;
                    }
                }

                if (vhgtData == null) return null;

                // Parse BTXT (base textures) and resolve texture paths
                string terrainTexPath = null;
                foreach (var v in subs.Where(s => s.Type == "BTXT"))
                {
                    if (v.Data.Length < 8) continue;
                    uint texFormId = BitConverter.ToUInt32(v.Data, 0);
                    int quad = v.Data[4];
                    string path = ResolveTexturePath(texFormId);
                    if (path != null)
                    {
                        GD.Print($"[TerrainBuilder] BTXT quad={quad} tex=0x{texFormId:X8} -> {path}");
                        if (terrainTexPath == null)
                            terrainTexPath = path;
                    }
                }

                // Fallback: use GroundLitterHeavy01 if no BTXT resolved
                if (terrainTexPath == null)
                {
                    terrainTexPath = "Landscape/GroundLitterHeavy01.dds";
                    GD.Print($"[TerrainBuilder] Using fallback texture: {terrainTexPath}");
                }

                // Parse height map — VHGT: float baseHeight + 1089 unsigned bytes (33x33, row-major).
                // heights[0,0] = baseHeight. Remaining 1088 vertices: cumulative deltas (signed /8).
                // byte[0] is delta from baseHeight to (0,0), applied at (0,0); subsequent bytes
                // are deltas from previous vertex to current vertex in row-major order.
                float baseHeight = BitConverter.ToSingle(vhgtData, 0);
                float[,] heights = new float[GridSize, GridSize];
                float currentHeight = baseHeight;
                int vhgtOff = 4;
                for (int row = 0; row < GridSize; row++)
                {
                    for (int col = 0; col < GridSize; col++)
                    {
                        if (row > 0 || col > 0)
                        {
                            if (vhgtOff < vhgtData.Length)
                            {
                                currentHeight += (sbyte)vhgtData[vhgtOff] / 8f;
                                vhgtOff++;
                            }
                        }
                        heights[row, col] = currentHeight;
                    }
                }

                // Parse vertex colors (optional) — use VCLR if available
                Color[,] colors = null;
                if (vclrData != null && vclrData.Length >= GridSize * GridSize * 3)
                {
                    colors = new Color[GridSize, GridSize];
                    int cOff = 0;
                    for (int row = 0; row < GridSize; row++)
                    {
                        for (int col = 0; col < GridSize; col++)
                        {
                            colors[row, col] = new Color(
                                vclrData[cOff] / 255f,
                                vclrData[cOff + 1] / 255f,
                                vclrData[cOff + 2] / 255f
                            );
                            cOff += 3;
                        }
                    }
                }

                // Debug: print height range
                float minH = float.MaxValue, maxH = float.MinValue;
                for (int r = 0; r < GridSize; r++)
                    for (int c = 0; c < GridSize; c++)
                    {
                        float h = heights[r, c];
                        if (h < minH) minH = h;
                        if (h > maxH) maxH = h;
                    }
                float cellHMinGodot = minH * HeightScale;
                float cellHMaxGodot = maxH * HeightScale;
                GD.Print($"[TerrainBuilder] LAND 0x{landFormId:X8} cell({cellX},{cellY})" +
                    $" baseH={BitConverter.ToSingle(vhgtData, 0):F1}" +
                    $" rawH=[{minH:F1}, {maxH:F1}] (Δ={maxH - minH:F1})" +
                    $" godotY=[{cellHMinGodot:F2}, {cellHMaxGodot:F2}] (Δ={(maxH - minH) * HeightScale:F2})");

                // Build mesh
                return BuildTerrainMesh(heights, colors, cellX, cellY, landFormId, megatonCenter, loadTexture, terrainTexPath);
            }
            catch (Exception e)
            {
                GD.PrintErr($"[TerrainBuilder] Error building tile for LAND 0x{landFormId:X8}: {e.Message}");
                return null;
            }
        }

        private TerrainTile BuildTerrainMesh(float[,] heights, Color[,] vclrColors,
            int cellX, int cellY, uint landFormId, Vector2 megatonCenter,
            Func<string, Texture2D> loadTexture, string texPath)
        {
            int quadsPerSide = GridSize - 1;
            int totalVerts = GridSize * GridSize;
            int totalIndices = quadsPerSide * quadsPerSide * 6;

            Vector3[] verts = new Vector3[totalVerts];
            Vector3[] norms = new Vector3[totalVerts];
            Color[] cols = new Color[totalVerts];
            Vector2[] uvs = new Vector2[totalVerts];
            int[] indices = new int[totalIndices];

            // Compute height range once
            float hMin = float.MaxValue, hMax = float.MinValue;
            for (int r = 0; r < GridSize; r++)
                for (int c = 0; c < GridSize; c++)
                {
                    float h = heights[r, c];
                    if (h < hMin) hMin = h;
                    if (h > hMax) hMax = h;
                }
            float hRange = hMax - hMin;
            float hCenter = (hMin + hMax) * 0.5f;

            // Cell origin in FO3 world coords
            float originX = cellX * CellSize;
            float originY = cellY * CellSize;
            float step = CellSize / quadsPerSide;

            for (int row = 0; row < GridSize; row++)
            {
                for (int col = 0; col < GridSize; col++)
                {
                    int idx = row * GridSize + col;

                    // FO3 coords: X=col, Y=row, Z=height
                    // Convert to Godot: (X, Z, -Y) offset by megatonCenter (same as REFR)
                    float godotX = (originX + col * step - megatonCenter.X) * WorldScale;
                    // Center-preserving height exaggeration: keeps average Y unchanged
                    // while amplifying the variation to make terrain visible
                    float godotY = (hCenter + (heights[row, col] - hCenter) * HeightExaggeration) * HeightScale;
                    float godotZ = -(originY + row * step - megatonCenter.Y) * WorldScale;

                    verts[idx] = new Vector3(godotX, godotY, godotZ);

                    // Use VCLR vertex colors if available, otherwise debug height gradient
                    if (vclrColors != null)
                    {
                        cols[idx] = vclrColors[row, col];
                    }
                    else
                    {
                        float t = hRange > 0.001f ? (heights[row, col] - hMin) / hRange : 0.5f;
                        cols[idx] = new Color(1f - t, 0.2f, t);
                    }

                    uvs[idx] = new Vector2(col / (float)quadsPerSide, row / (float)quadsPerSide);
                }
            }

            // Build indices (row by row, triangle strips)
            int triIdx = 0;
            for (int row = 0; row < quadsPerSide; row++)
            {
                for (int col = 0; col < quadsPerSide; col++)
                {
                    int bl = row * GridSize + col;
                    int br = row * GridSize + col + 1;
                    int tl = (row + 1) * GridSize + col;
                    int tr = (row + 1) * GridSize + col + 1;

                    indices[triIdx++] = bl;
                    indices[triIdx++] = tl;
                    indices[triIdx++] = br;

                    indices[triIdx++] = br;
                    indices[triIdx++] = tl;
                    indices[triIdx++] = tr;
                }
            }

            // Compute normals from actual geometry (always, replaces VNML)
            for (int row = 0; row < quadsPerSide; row++)
            {
                for (int col = 0; col < quadsPerSide; col++)
                {
                    int bl = row * GridSize + col;
                    int br = row * GridSize + col + 1;
                    int tl = (row + 1) * GridSize + col;
                    int tr = (row + 1) * GridSize + col + 1;

                    Vector3 v0 = verts[bl], v1 = verts[tl], v2 = verts[br], v3 = verts[tr];

                    Vector3 n1 = (v1 - v0).Cross(v2 - v0);
                    n1 = n1.Normalized();
                    norms[bl] += n1;
                    norms[tl] += n1;
                    norms[br] += n1;

                    Vector3 n2 = (v1 - v2).Cross(v3 - v2);
                    n2 = n2.Normalized();
                    norms[br] += n2;
                    norms[tl] += n2;
                    norms[tr] += n2;
                }
            }
            for (int i = 0; i < totalVerts; i++)
            {
                if (norms[i].LengthSquared() > 0.0001f)
                    norms[i] = norms[i].Normalized();
                else
                    norms[i] = Vector3.Up;
            }

            var mesh = new ArrayMesh();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Normal] = norms;
            arrays[(int)Mesh.ArrayType.Color] = cols;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Index] = indices;

            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            // Apply terrain material
            var mat = new StandardMaterial3D();
            mat.VertexColorUseAsAlbedo = true;
            if (!string.IsNullOrEmpty(texPath) && loadTexture != null)
            {
                var tex = loadTexture(texPath);
                if (tex != null)
                {
                    mat.AlbedoTexture = tex;
                }
            }
            mesh.SurfaceSetMaterial(0, mat);

            // Debug: print final vertex Y range (after exaggeration & scaling)
            float vMinY = float.MaxValue, vMaxY = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                float y = verts[i].Y;
                if (y < vMinY) vMinY = y;
                if (y > vMaxY) vMaxY = y;
            }
            float vRangeY = vMaxY - vMinY;
            if (vRangeY < 0.01f)
                GD.Print($"[TerrainBuilder] *** WARNING: cell({cellX},{cellY}) vertex Y range is ONLY {vRangeY:F4} Godot units — terrain is effectively FLAT!");
            GD.Print($"[TerrainBuilder] cell({cellX},{cellY}) vertex Y: min={vMinY:F3} max={vMaxY:F3} range={vRangeY:F3}  (HeightExaggeration={HeightExaggeration}x, HeightScale={HeightScale})");

            // Build collision shape from mesh geometry
            var faceVerts = new Vector3[totalIndices];
            for (int i = 0; i < totalIndices; i++)
                faceVerts[i] = verts[indices[i]];
            var collisionShape = new ConcavePolygonShape3D();
            collisionShape.SetFaces(faceVerts);

            return new TerrainTile
            {
                Mesh = mesh,
                CollisionShape = collisionShape,
                CellCoord = new Vector2(cellX, cellY),
            };
        }

        private TerrainTile BuildFlatTerrainTile(int cellX, int cellY, float defaultHeight, Vector2 megatonCenter)
        {
            float originX = cellX * CellSize;
            float originY = cellY * CellSize;

            Vector3 v0 = new(
                (originX - megatonCenter.X) * WorldScale,
                defaultHeight * HeightScale,
                -(originY - megatonCenter.Y) * WorldScale);
            Vector3 v1 = new(
                (originX + CellSize - megatonCenter.X) * WorldScale,
                defaultHeight * HeightScale,
                -(originY - megatonCenter.Y) * WorldScale);
            Vector3 v2 = new(
                (originX + CellSize - megatonCenter.X) * WorldScale,
                defaultHeight * HeightScale,
                -((originY + CellSize) - megatonCenter.Y) * WorldScale);
            Vector3 v3 = new(
                (originX - megatonCenter.X) * WorldScale,
                defaultHeight * HeightScale,
                -((originY + CellSize) - megatonCenter.Y) * WorldScale);

            var verts = new Vector3[] { v0, v1, v2, v3 };
            var norms = new Vector3[4];
            var cols = new Color[4];
            var uvs = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                norms[i] = Vector3.Up;
                cols[i] = new Color(0.45f, 0.55f, 0.35f);
            }
            uvs[0] = new Vector2(0, 0);
            uvs[1] = new Vector2(CellSize / 256f, 0);
            uvs[2] = new Vector2(CellSize / 256f, CellSize / 256f);
            uvs[3] = new Vector2(0, CellSize / 256f);

            int[] indices = { 0, 1, 2, 0, 2, 3 };

            var mesh = new ArrayMesh();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Normal] = norms;
            arrays[(int)Mesh.ArrayType.Color] = cols;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Index] = indices;

            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            var mat = new StandardMaterial3D();
            mat.VertexColorUseAsAlbedo = true;
            mat.AlbedoColor = new Color(0.45f, 0.55f, 0.35f);
            mesh.SurfaceSetMaterial(0, mat);

            var faceVerts = new Vector3[indices.Length];
            for (int i = 0; i < indices.Length; i++)
                faceVerts[i] = verts[indices[i]];
            var collisionShape = new ConcavePolygonShape3D();
            collisionShape.SetFaces(faceVerts);

            return new TerrainTile
            {
                Mesh = mesh,
                CollisionShape = collisionShape,
                CellCoord = new Vector2(cellX, cellY),
            };
        }
    }
}
