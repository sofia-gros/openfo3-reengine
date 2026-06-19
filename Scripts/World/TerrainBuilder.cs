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

        private ESMReader _esm;
        private Dictionary<uint, RecordEntry> _landIndex;
        private Dictionary<uint, RecordEntry> _ltexIndex;
        private Dictionary<uint, RecordEntry> _cellIndex;

        public TerrainBuilder(ESMReader esm)
        {
            _esm = esm;
            _landIndex = esm.BuildFormIdIndex(new[] { "LAND" });
            _ltexIndex = esm.BuildFormIdIndex(new[] { "LTEX" });
            _cellIndex = esm.BuildFormIdIndex(new[] { "CELL" });
        }

        public class TerrainTile
        {
            public ArrayMesh Mesh;
            public Vector2 CellCoord;
        }

        public List<TerrainTile> BuildTerrainForWorld(uint worldFormId, Vector2 megatonCenter,
            Func<string, Texture2D> loadTexture)
        {
            var tiles = new List<TerrainTile>();

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
                if (!TryGetCellCoord(landFormId, out int cellX, out int cellY))
                    continue;

                var tile = BuildTerrainTile(landFormId, cellX, cellY, loadTexture, megatonCenter);
                if (tile != null)
                    tiles.Add(tile);
            }

            return tiles;
        }

        private bool TryGetCellCoord(uint formId, out int cellX, out int cellY)
        {
            cellX = 0; cellY = 0;
            try
            {
                // LAND formId == CELL formId in FO3. Read XCLC from the CELL record.
                if (!_cellIndex.TryGetValue(formId, out var entry)) return false;
                var record = _esm.GetRecordAtOffset(entry.Offset);
                var subs = _esm.GetSubRecords(record);

                var xclc = subs.FirstOrDefault(s => s.Type == "XCLC");
                if (xclc == null || xclc.Data.Length < 8) return false;

                cellX = BitConverter.ToInt32(xclc.Data, 0);
                cellY = BitConverter.ToInt32(xclc.Data, 4);
                return true;
            }
            catch
            {
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

                // Parse height map — VHGT: baseHeight + 33x33 signed bytes (continuous row-major deltas)
                float baseHeight = BitConverter.ToSingle(vhgtData, 0);
                float[,] heights = new float[GridSize, GridSize];
                float currentHeight = baseHeight;

                int offset = 4;
                for (int row = 0; row < GridSize; row++)
                {
                    for (int col = 0; col < GridSize; col++)
                    {
                        if (row > 0 || col > 0)
                        {
                            if (offset < vhgtData.Length)
                            {
                                currentHeight += vhgtData[offset] / 8f;
                                offset++;
                            }
                        }
                        heights[row, col] = currentHeight;
                    }
                }

                // Parse normals (optional)
                Vector3[,] normals = null;
                if (vnmlData != null && vnmlData.Length >= GridSize * GridSize * 3)
                {
                    normals = new Vector3[GridSize, GridSize];
                    int nOff = 0;
                    for (int row = 0; row < GridSize; row++)
                    {
                        for (int col = 0; col < GridSize; col++)
                        {
                            float nx = (vnmlData[nOff] / 127.5f) - 1f;
                            float ny = (vnmlData[nOff + 1] / 127.5f) - 1f;
                            float nz = (vnmlData[nOff + 2] / 127.5f) - 1f;
                            normals[row, col] = new Vector3(nx, ny, nz).Normalized();
                            nOff += 3;
                        }
                    }
                }

                // Parse vertex colors (optional)
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

                // Build mesh
                return BuildTerrainMesh(heights, normals, colors, cellX, cellY, landFormId, megatonCenter);
            }
            catch (Exception e)
            {
                GD.PrintErr($"[TerrainBuilder] Error building tile for LAND 0x{landFormId:X8}: {e.Message}");
                return null;
            }
        }

        private TerrainTile BuildTerrainMesh(float[,] heights, Vector3[,] normals, Color[,] colors,
            int cellX, int cellY, uint landFormId, Vector2 megatonCenter)
        {
            int quadsPerSide = GridSize - 1;
            int totalVerts = GridSize * GridSize;
            int totalIndices = quadsPerSide * quadsPerSide * 6;

            Vector3[] verts = new Vector3[totalVerts];
            Vector3[] norms = new Vector3[totalVerts];
            Color[] cols = new Color[totalVerts];
            Vector2[] uvs = new Vector2[totalVerts];
            int[] indices = new int[totalIndices];

            // Cell origin in FO3 world coords
            float originX = cellX * CellSize;
            float originY = cellY * CellSize;

            for (int row = 0; row < GridSize; row++)
            {
                for (int col = 0; col < GridSize; col++)
                {
                    int idx = row * GridSize + col;

                    // FO3 coords: X=col, Y=row, Z=height
                    // Convert to Godot: (X, Z, -Y) offset by megatonCenter (same as REFR)
                    float godotX = (originX + col * (CellSize / quadsPerSide) - megatonCenter.X) * WorldScale;
                    float godotY = heights[row, col] * HeightScale;
                    float godotZ = -(originY + row * (CellSize / quadsPerSide) - megatonCenter.Y) * WorldScale;

                    verts[idx] = new Vector3(godotX, godotY, godotZ);

                    if (normals != null)
                    {
                        // FO3 normal (nx, ny, nz) -> Godot (nx, nz, -ny)
                        var n = normals[row, col];
                        norms[idx] = new Vector3(n.X, n.Z, -n.Y).Normalized();
                    }
                    else
                    {
                        norms[idx] = Vector3.Up;
                    }

                    cols[idx] = colors?[row, col] ?? new Color(0.5f, 0.5f, 0.2f);
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

            var mesh = new ArrayMesh();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;
            arrays[(int)Mesh.ArrayType.Normal] = norms;
            arrays[(int)Mesh.ArrayType.Color] = cols;
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
            arrays[(int)Mesh.ArrayType.Index] = indices;

            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            // Apply simple terrain material
            var mat = new StandardMaterial3D();
            mat.VertexColorUseAsAlbedo = true;
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mesh.SurfaceSetMaterial(0, mat);

            return new TerrainTile
            {
                Mesh = mesh,
                CellCoord = new Vector2(cellX, cellY),
            };
        }
    }
}
