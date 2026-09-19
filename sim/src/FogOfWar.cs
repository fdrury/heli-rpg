using System;

namespace Rotorwash.Sim;

/// <summary>
/// Grid-based fog of war. Pillar 4: "the map starts near-empty."
///
/// 128×128 cells over the 13 km content envelope (~101.6 m per cell). Each cell
/// is a single bit: revealed or not. Reveal happens when the aircraft flies within
/// a radius, so the map fills in through flight rather than through menus.
///
/// Pure .NET, no Godot dependency. Serialised as a packed byte array for save/load.
/// </summary>
public sealed class FogOfWar
{
    public const int GridSize = 128;
    public const float WorldExtent = 6500f;           // matches WorldMap.ContentHalfExtent
    public const float CellSize = WorldExtent * 2f / GridSize;   // ~101.6 m

    /// <summary>How far from the aircraft a cell is revealed, in metres.</summary>
    public const float RevealRadius = 500f;

    private readonly bool[] _revealed = new bool[GridSize * GridSize];

    /// <summary>Number of cells revealed.</summary>
    public int RevealedCount { get; private set; }

    /// <summary>Fraction of the grid that has been revealed, 0 to 1.</summary>
    public float RevealedFraction => RevealedCount / (float)(GridSize * GridSize);

    public bool IsRevealed(int cx, int cy)
    {
        if (cx < 0 || cx >= GridSize || cy < 0 || cy >= GridSize) return false;
        return _revealed[cy * GridSize + cx];
    }

    /// <summary>
    /// Reveal cells near an aircraft position. North/east in sim coordinates.
    /// Called once per frame from the game layer.
    /// </summary>
    public void Reveal(double north, double east)
    {
        int cx0 = WorldToCell(east);
        int cy0 = WorldToCell(-north);   // grid Y increases south, north decreases

        int cellRadius = (int)Math.Ceiling(RevealRadius / CellSize);

        int xMin = Math.Max(0, cx0 - cellRadius);
        int xMax = Math.Min(GridSize - 1, cx0 + cellRadius);
        int yMin = Math.Max(0, cy0 - cellRadius);
        int yMax = Math.Min(GridSize - 1, cy0 + cellRadius);

        float r2 = RevealRadius * RevealRadius;

        for (int y = yMin; y <= yMax; y++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                int idx = y * GridSize + x;
                if (_revealed[idx]) continue;

                // Cell centre in world coords
                float cellEast = CellToWorld(x);
                float cellSouth = CellToWorld(y);

                float de = (float)(cellEast - east);
                float dn = (float)(cellSouth + north);   // cellSouth = -north of cell

                if (de * de + dn * dn <= r2)
                {
                    _revealed[idx] = true;
                    RevealedCount++;
                }
            }
        }
    }

    /// <summary>Reveal the whole grid (for testing or debug).</summary>
    public void RevealAll()
    {
        for (int i = 0; i < _revealed.Length; i++)
        {
            if (!_revealed[i]) { _revealed[i] = true; RevealedCount++; }
        }
    }

    // --------------------------------------------------------- coordinate helpers

    /// <summary>World coordinate (east or south) → grid cell index.</summary>
    public static int WorldToCell(double world)
        => (int)Math.Clamp((world + WorldExtent) / CellSize, 0, GridSize - 1);

    /// <summary>Grid cell index → world coordinate (cell centre).</summary>
    public static float CellToWorld(int cell)
        => cell * CellSize - WorldExtent + CellSize * 0.5f;

    // ------------------------------------------------------------ serialisation

    /// <summary>Pack into a byte array (2048 bytes = 16384 bits).</summary>
    public byte[] ToBytes()
    {
        int byteCount = (GridSize * GridSize + 7) / 8;
        var bytes = new byte[byteCount];
        for (int i = 0; i < _revealed.Length; i++)
        {
            if (_revealed[i])
                bytes[i / 8] |= (byte)(1 << (i % 8));
        }
        return bytes;
    }

    /// <summary>Restore from a packed byte array.</summary>
    public void FromBytes(byte[] bytes)
    {
        RevealedCount = 0;
        int count = Math.Min(bytes.Length * 8, _revealed.Length);
        for (int i = 0; i < count; i++)
        {
            bool val = (bytes[i / 8] & (1 << (i % 8))) != 0;
            _revealed[i] = val;
            if (val) RevealedCount++;
        }
        // Clear any remaining cells
        for (int i = count; i < _revealed.Length; i++)
            _revealed[i] = false;
    }
}
