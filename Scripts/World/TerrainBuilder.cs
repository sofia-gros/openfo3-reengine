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
        // 地形高さ強調倍率。2〜3倍程度で凸凹が視認しやすくなる。
        // ただし大きくしすぎると建物の位置とズレるため注意。
        // 衝突判定はかならず HeightScale のみ使用（Exaggeration は視覚用）。
        private float _heightExaggeration = 1.0f;
        // 衝突判定用の高さ強調倍率（常に1.0、視覚と分離）
        private const float CollisionHeightExaggeration = 1.0f;

        private ESMReader _esm;
        private Dictionary<uint, RecordEntry> _landIndex;
        private Dictionary<uint, RecordEntry> _ltexIndex;
        private Dictionary<uint, RecordEntry> _cellIndex;
        private Dictionary<uint, RecordEntry> _txstIndex;

        public TerrainBuilder(ESMReader esm)
        {
            _esm = esm;
            _ltexIndex = esm.BuildFormIdIndex(new[] { "LTEX" });
            _cellIndex = esm.BuildFormIdIndex(new[] { "CELL" });
            _txstIndex = esm.BuildFormIdIndex(new[] { "TXST" });
            GD.Print($"[TerrainBuilder] LTEX index: {_ltexIndex.Count} entries, TXST index: {_txstIndex.Count} entries");
        }

        public void SetLandIndex(Dictionary<uint, RecordEntry> masterIndex)
        {
            _landIndex = masterIndex;
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
        public ArrayMesh LodMesh;
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

            GD.Print($"[TerrainBuilder] Looking for LAND records in world 0x{worldFormId:X8}");

            foreach (var kvp in _landIndex)
            {
                if (kvp.Value.WorldFormId == worldFormId)
                {
                    landCells.Add(kvp.Key);
                }
            }

            // Build LAND→coordinate map by scanning the WRLD's children in order.
            // Uses cellIndex to resolve coordinates inside Cell Children GRUPs.
            var landCoords = _esm.BuildLandCoordinateMap(_landIndex, _cellIndex, worldFormId);
            GD.Print($"[TerrainBuilder] Found {landCells.Count} LAND records in world, {landCoords.Count} have cell coords");

            // Cross-check: compare BuildLandCoordinateMap results against direct XCLC parsing
            foreach (uint landFormId in landCells)
            {
                if (!_landIndex.TryGetValue(landFormId, out var landEntry)) continue;
                if (!landCoords.TryGetValue(landFormId, out var coord)) continue;

                int simpleX = 0, simpleY = 0;
                bool simpleOk = false;
                if (_cellIndex.TryGetValue(landEntry.CellFormId, out var cellEntry))
                {
                    var cellRec = _esm.GetRecordAtOffset(cellEntry.Offset);
                    var cellSubs = _esm.GetSubRecords(cellRec);
                    var xclc = cellSubs.FirstOrDefault(s => s.Type == "XCLC");
                    if (xclc != null && xclc.Data.Length >= 8)
                    {
                        simpleX = BitConverter.ToInt32(xclc.Data, 0);
                        simpleY = BitConverter.ToInt32(xclc.Data, 4);
                        simpleOk = true;
                    }
                }

                bool mismatch = simpleOk && (simpleX != coord.Item1 || simpleY != coord.Item2);
                GD.Print($"[CrossCheck] LAND 0x{landFormId:X8}: mapCoord=({coord.Item1},{coord.Item2}) " +
                    $"cellFormId=0x{landEntry.CellFormId:X8} simpleXCLC={(simpleOk ? $"({simpleX},{simpleY})" : "N/A(not in cellIndex)")}" +
                    (mismatch ? "  *** MISMATCH ***" : ""));
            }

            foreach (uint landFormId in landCells)
            {
                if (!_landIndex.TryGetValue(landFormId, out var landEntry))
                {
                    GD.Print($"[TerrainBuilder] LAND 0x{landFormId:X8} not found in index");
                    continue;
                }

                if (!landCoords.TryGetValue(landFormId, out var coord))
                {
                    GD.Print($"[TerrainBuilder] LAND 0x{landFormId:X8} has no cell coordinate mapping - skipping");
                    continue;
                }
                int cellX = coord.Item1;
                int cellY = coord.Item2;

                coveredCells.Add((cellX, cellY));

				var quadTiles = BuildQuadrantTiles(landFormId, cellX, cellY, loadTexture, megatonCenter, defaultLandHeight);
				if (quadTiles != null && quadTiles.Count > 0)
				{
					tiles.AddRange(quadTiles);
					GD.Print($"[TerrainBuilder] Built {quadTiles.Count} quadrant tiles for cell ({cellX}, {cellY})");
				}
				else
				{
					GD.Print($"[TerrainBuilder] BuildQuadrantTiles returned null/empty for cell ({cellX}, {cellY})");
				}
            }

            // Fill gaps between real LAND tiles with flat terrain
            if (coveredCells.Count > 0)
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

                int padding = 5;
                int fillMinX = int.MaxValue, fillMinY = int.MaxValue;
                int fillMaxX = int.MinValue, fillMaxY = int.MinValue;
                if (fillCellMaxX >= fillCellMinX && fillCellMaxY >= fillCellMinY)
                {
                    fillMinX = Math.Max(landMinX - padding, fillCellMinX);
                    fillMinY = Math.Max(landMinY - padding, fillCellMinY);
                    fillMaxX = Math.Min(landMaxX + padding, fillCellMaxX);
                    fillMaxY = Math.Min(landMaxY + padding, fillCellMaxY);
                }
                else
                {
                    fillMinX = landMinX - padding;
                    fillMinY = landMinY - padding;
                    fillMaxX = landMaxX + padding;
                    fillMaxY = landMaxY + padding;
                }

                int totalFill = (fillMaxX - fillMinX + 1) * (fillMaxY - fillMinY + 1);
                if (totalFill > 3000)
                {
                    GD.Print($"[TerrainBuilder] WARNING: {totalFill} flat tiles exceeds 3000 cap — using padding=2");
                    padding = 2;
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
                GD.Print($"[TerrainBuilder] Built {flatBuilt} gap-fill flat tiles around {coveredCells.Count} LAND tiles");
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

		private List<TerrainTile> BuildQuadrantTiles(uint landFormId, int cellX, int cellY,
		    Func<string, Texture2D> loadTexture, Vector2 megatonCenter, float defaultLandHeight)
		{
		    try
		    {
		        if (!_landIndex.TryGetValue(landFormId, out var entry)) return null;
		        var record = _esm.GetRecordAtOffset(entry.Offset);
		        if (record.Type != "LAND")
		        {
		            GD.Print($"[TerrainBuilder] WARNING: LAND 0x{landFormId:X8} record type is '{record.Type}' not 'LAND' (offset=0x{entry.Offset:X8})");
		        }
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

		        // Parse BTXT entries
		        var btxtures = subs.Where(s => s.Type == "BTXT").ToArray();
		        if (btxtures.Length == 0)
		        {
		            GD.Print($"[TerrainBuilder] LAND 0x{landFormId:X8} cell({cellX},{cellY}) has no BTXT — skipping");
		            return null;
		        }

		        // Resolve texture paths per quadrant
		        string[] quadTexPaths = new string[4];
		        bool hasAnyTexture = false;
		        foreach (var v in btxtures)
		        {
		            if (v.Data.Length < 8) continue;
		            uint texFormId = BitConverter.ToUInt32(v.Data, 0);
		            int quad = v.Data[4];
		            if (quad >= 0 && quad < 4)
		            {
		                quadTexPaths[quad] = ResolveTexturePath(texFormId);
		                if (quadTexPaths[quad] != null)
		                {
		                    hasAnyTexture = true;
		                    GD.Print($"[TerrainBuilder] BTXT quad={quad} tex=0x{texFormId:X8} -> {quadTexPaths[quad]}");
		                }
		            }
		        }

		        if (!hasAnyTexture)
		        {
		            GD.Print($"[TerrainBuilder] LAND 0x{landFormId:X8} cell({cellX},{cellY}) BTXT resolution failed — using fallback on all quadrants");
		            for (int i = 0; i < 4; i++)
		                quadTexPaths[i] = "Landscape/GroundLitterHeavy01.dds";
		        }

		        // For quadrants without a BTXT, copy from a neighboring quadrant or use fallback
		        for (int i = 0; i < 4; i++)
		        {
		            if (quadTexPaths[i] == null)
		            {
		                // Try to copy from another quadrant
		                for (int j = 0; j < 4; j++)
		                {
		                    if (quadTexPaths[j] != null)
		                    {
		                        quadTexPaths[i] = quadTexPaths[j];
		                        break;
		                    }
		                }
		                if (quadTexPaths[i] == null)
		                    quadTexPaths[i] = "Landscape/GroundLitterHeavy01.dds";
		            }
		        }

		        // Parse height map
		        float baseHeight = BitConverter.ToSingle(vhgtData, 0) * 8f;
		        float[,] heights = new float[GridSize, GridSize];

		        sbyte[,] deltas = new sbyte[GridSize, GridSize];
		        int vhgtOff = 4;
		        for (int row = 0; row < GridSize; row++)
		        {
		            for (int col = 0; col < GridSize; col++)
		            {
		                if (vhgtOff < vhgtData.Length)
		                    deltas[row, col] = (sbyte)vhgtData[vhgtOff++];
		            }
		        }

		        float rowOffset = 0;
		        for (int row = 0; row < GridSize; row++)
		        {
		            if (row > 0) {
		                rowOffset += deltas[row, 0] * 8f;
		            }
		            heights[row, 0] = baseHeight + rowOffset;
		            
		            float colOffset = rowOffset;
		            for (int col = 1; col < GridSize; col++)
		            {
		                colOffset += deltas[row, col] * 8f;
		                heights[row, col] = baseHeight + colOffset;
		            }
		        }

		        // Parse vertex colors
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

		        // Debug print
		        float minH = float.MaxValue, maxH = float.MinValue;
		        for (int r = 0; r < GridSize; r++)
		            for (int c = 0; c < GridSize; c++)
		            {
		                float h = heights[r, c];
		                if (h < minH) minH = h;
		                if (h > maxH) maxH = h;
		            }
		        GD.Print($"[TerrainBuilder] LAND 0x{landFormId:X8} cell({cellX},{cellY})" +
		            $" landH={defaultLandHeight:F1} baseHeight={baseHeight:F1}" +
		            $" rawH=[{minH:F1}, {maxH:F1}] (Δ={maxH - minH:F1})" +
		            $" quadrants=[{quadTexPaths[0] ?? "none"},{quadTexPaths[1] ?? "none"},{quadTexPaths[2] ?? "none"},{quadTexPaths[3] ?? "none"}]");

		        // Build collision shape for the full cell
		        var collisionShape = BuildCellCollision(heights, cellX, cellY, megatonCenter);

		        // Build one tile per quadrant
		        var tiles = new List<TerrainTile>();
		        for (int qi = 0; qi < 4; qi++)
		        {
		            string texPath = quadTexPaths[qi];
		            if (texPath == null) continue;

		            var mesh = BuildQuadrantMesh(heights, colors, cellX, cellY, megatonCenter, loadTexture, texPath, qi);
		            var lodMesh = BuildQuadrantLodMesh(heights, colors, cellX, cellY, megatonCenter, loadTexture, texPath, qi);

		            tiles.Add(new TerrainTile
		            {
		                Mesh = mesh,
		                LodMesh = lodMesh,
		                CollisionShape = qi == 0 ? collisionShape : null,
		                CellCoord = new Vector2(cellX, cellY),
		            });
		        }

		        return tiles;
		    }
		    catch (Exception e)
		    {
		        GD.PrintErr($"[TerrainBuilder] Error building quadrants for LAND 0x{landFormId:X8}: {e.Message}");
		        return null;
		    }
		}

		private ArrayMesh BuildQuadrantMesh(float[,] heights, Color[,] vclrColors,
		    int cellX, int cellY, Vector2 megatonCenter,
		    Func<string, Texture2D> loadTexture, string texPath, int quadIdx)
		{
		    const int qSize = 17; // 17x17 vertices per quadrant
		    const int qQuads = 16;
		    int totalVerts = qSize * qSize;
		    int totalIndices = qQuads * qQuads * 6;

		    int rowStart = (quadIdx / 2) * qQuads;
		    int colStart = (quadIdx % 2) * qQuads;

		    Vector3[] verts = new Vector3[totalVerts];
		    Vector3[] norms = new Vector3[totalVerts];
		    Color[] cols = new Color[totalVerts];
		    Vector2[] uvs = new Vector2[totalVerts];
		    int[] indices = new int[totalIndices];

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

		    float originX = cellX * CellSize;
		    float originY = cellY * CellSize;
		    float step = CellSize / (GridSize - 1);

		    for (int lr = 0; lr < qSize; lr++)
		    {
		        for (int lc = 0; lc < qSize; lc++)
		        {
		            int gr = rowStart + lr;
		            int gc = colStart + lc;
		            int idx = lr * qSize + lc;

		            float godotX = (originX + gc * step - megatonCenter.X) * WorldScale;
		            float godotY = heights[gr, gc] * HeightScale;
		            float godotZ = -(originY + gr * step - megatonCenter.Y) * WorldScale;

		            verts[idx] = new Vector3(godotX, godotY, godotZ);

		            if (vclrColors != null)
		                cols[idx] = vclrColors[gr, gc];
		            else
		            {
		                float t = hRange > 0.001f ? (heights[gr, gc] - hMin) / hRange : 0.5f;
		                cols[idx] = new Color(1f - t, 0.2f, t);
		            }

		            // UV: local to quadrant
		            float u = lc / (float)qQuads * 8f; // 8 repeats per quadrant (half of 16)
		            float v = lr / (float)qQuads * 8f;
		            uvs[idx] = new Vector2(u, v);
		        }
		    }

		    // Build indices
		    int triIdx = 0;
		    for (int lr = 0; lr < qQuads; lr++)
		    {
		        for (int lc = 0; lc < qQuads; lc++)
		        {
		            int bl = lr * qSize + lc;
		            int br = lr * qSize + lc + 1;
		            int tl = (lr + 1) * qSize + lc;
		            int tr = (lr + 1) * qSize + lc + 1;

		            indices[triIdx++] = bl;
		            indices[triIdx++] = tl;
		            indices[triIdx++] = br;
		            indices[triIdx++] = br;
		            indices[triIdx++] = tl;
		            indices[triIdx++] = tr;
		        }
		    }

		    // Compute normals
		    for (int lr = 0; lr < qQuads; lr++)
		    {
		        for (int lc = 0; lc < qQuads; lc++)
		        {
		            int bl = lr * qSize + lc;
		            int br = lr * qSize + lc + 1;
		            int tl = (lr + 1) * qSize + lc;
		            int tr = (lr + 1) * qSize + lc + 1;

		            Vector3 v0 = verts[bl], v1 = verts[tl], v2 = verts[br], v3 = verts[tr];

		            Vector3 n1 = (v2 - v0).Cross(v1 - v0); // CW(bl->tl->br) with Upward normal
		            n1 = n1.Normalized();
		            norms[bl] += n1;
		            norms[tl] += n1;
		            norms[br] += n1;

		            Vector3 n2 = (v3 - v2).Cross(v1 - v2); // CW(br->tl->tr) with Upward normal
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

		    var mat = new StandardMaterial3D();
		    if (!string.IsNullOrEmpty(texPath) && loadTexture != null)
		    {
		        var tex = loadTexture(texPath);
		        if (tex != null)
		            mat.AlbedoTexture = tex;
		        else
		            mat.AlbedoColor = new Color(0.5f, 0.5f, 0.3f);
		    }
		    else
		    {
		        mat.AlbedoColor = new Color(0.5f, 0.5f, 0.3f);
		    }
		    mesh.SurfaceSetMaterial(0, mat);

		    return mesh;
		}

		private ArrayMesh BuildQuadrantLodMesh(float[,] heights, Color[,] vclrColors,
		    int cellX, int cellY, Vector2 megatonCenter,
		    Func<string, Texture2D> loadTexture, string texPath, int quadIdx)
		{
		    const int qSize = 17;
		    const int lodStep = 2;
		    int lodGridSize = (qSize - 1) / lodStep + 1; // 9
		    int lodQuads = lodGridSize - 1;
		    int totalVerts = lodGridSize * lodGridSize;
		    int totalIndices = lodQuads * lodQuads * 6;

		    int rowStart = (quadIdx / 2) * 16;
		    int colStart = (quadIdx % 2) * 16;

		    Vector3[] verts = new Vector3[totalVerts];
		    Vector3[] norms = new Vector3[totalVerts];
		    Color[] cols = new Color[totalVerts];
		    Vector2[] uvs = new Vector2[totalVerts];
		    int[] indices = new int[totalIndices];

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

		    float originX = cellX * CellSize;
		    float originY = cellY * CellSize;
		    float step = CellSize / (GridSize - 1);

		    for (int lr = 0; lr < lodGridSize; lr++)
		    {
		        for (int lc = 0; lc < lodGridSize; lc++)
		        {
		            int gr = rowStart + lr * lodStep;
		            int gc = colStart + lc * lodStep;
		            int idx = lr * lodGridSize + lc;

		            float godotX = (originX + gc * step - megatonCenter.X) * WorldScale;
		            float godotY = heights[gr, gc] * HeightScale;
		            float godotZ = -(originY + gr * step - megatonCenter.Y) * WorldScale;

		            verts[idx] = new Vector3(godotX, godotY, godotZ);

		            if (vclrColors != null)
		                cols[idx] = vclrColors[gr, gc];
		            else
		            {
		                float t = hRange > 0.001f ? (heights[gr, gc] - hMin) / hRange : 0.5f;
		                cols[idx] = new Color(1f - t, 0.2f, t);
		            }

		            float u = (lc * lodStep) / 16f * 8f;
		            float v = (lr * lodStep) / 16f * 8f;
		            uvs[idx] = new Vector2(u, v);
		        }
		    }

		    int triIdx = 0;
		    for (int lr = 0; lr < lodQuads; lr++)
		    {
		        for (int lc = 0; lc < lodQuads; lc++)
		        {
		            int bl = lr * lodGridSize + lc;
		            int br = lr * lodGridSize + lc + 1;
		            int tl = (lr + 1) * lodGridSize + lc;
		            int tr = (lr + 1) * lodGridSize + lc + 1;

		            indices[triIdx++] = bl;
		            indices[triIdx++] = tl;
		            indices[triIdx++] = br;
		            indices[triIdx++] = br;
		            indices[triIdx++] = tl;
		            indices[triIdx++] = tr;
		        }
		    }

		    for (int lr = 0; lr < lodQuads; lr++)
		    {
		        for (int lc = 0; lc < lodQuads; lc++)
		        {
		            int bl = lr * lodGridSize + lc;
		            int br = lr * lodGridSize + lc + 1;
		            int tl = (lr + 1) * lodGridSize + lc;
		            int tr = (lr + 1) * lodGridSize + lc + 1;

		            Vector3 v0 = verts[bl], v1 = verts[tl], v2 = verts[br], v3 = verts[tr];
		            Vector3 n1 = (v2 - v0).Cross(v1 - v0).Normalized();
		            Vector3 n2 = (v3 - v2).Cross(v1 - v2).Normalized();
		            norms[bl] += n1; norms[tl] += n1; norms[br] += n1;
		            norms[br] += n2; norms[tl] += n2; norms[tr] += n2;
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

		    var mat = new StandardMaterial3D();
		    if (!string.IsNullOrEmpty(texPath) && loadTexture != null)
		    {
		        var tex = loadTexture(texPath);
		        if (tex != null)
		            mat.AlbedoTexture = tex;
		        else
		            mat.AlbedoColor = new Color(0.5f, 0.5f, 0.3f);
		    }
		    else
		    {
		        mat.AlbedoColor = new Color(0.5f, 0.5f, 0.3f);
		    }
		    mesh.SurfaceSetMaterial(0, mat);

		    return mesh;
		}

		private ConcavePolygonShape3D BuildCellCollision(float[,] heights,
		    int cellX, int cellY, Vector2 megatonCenter)
		{
		    int totalVerts = GridSize * GridSize;
		    int quadsPerSide = GridSize - 1;
		    int totalIndices = quadsPerSide * quadsPerSide * 6;

		    float hMin = float.MaxValue, hMax = float.MinValue;
		    for (int r = 0; r < GridSize; r++)
		        for (int c = 0; c < GridSize; c++)
		        {
		            float h = heights[r, c];
		            if (h < hMin) hMin = h;
		            if (h > hMax) hMax = h;
		        }
		    float hCenter = (hMin + hMax) * 0.5f;

		    float originX = cellX * CellSize;
		    float originY = cellY * CellSize;
		    float step = CellSize / quadsPerSide;

		    var colVerts = new Vector3[totalVerts];
		    for (int row = 0; row < GridSize; row++)
		    {
		        for (int col = 0; col < GridSize; col++)
		        {
		            int idx = row * GridSize + col;
		            float godotX = (originX + col * step - megatonCenter.X) * WorldScale;
		            float godotY = heights[row, col] * HeightScale;
		            float godotZ = -(originY + row * step - megatonCenter.Y) * WorldScale;
		            colVerts[idx] = new Vector3(godotX, godotY, godotZ);
		        }
		    }

		    int[] indices = new int[totalIndices];
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

		    var faceVerts = new Vector3[totalIndices];
		    for (int i = 0; i < totalIndices; i++)
		        faceVerts[i] = colVerts[indices[i]];

		    var collisionShape = new ConcavePolygonShape3D();
		    collisionShape.SetFaces(faceVerts);
		    return collisionShape;
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
