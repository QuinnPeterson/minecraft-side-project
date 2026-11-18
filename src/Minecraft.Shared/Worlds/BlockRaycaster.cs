using Minecraft.Shared.Blocks;

namespace Minecraft.Shared.Worlds;

/// <summary>
/// Walks a voxel grid along a ray using the Amanatides &amp; Woo DDA algorithm.
/// Used by the client to find which block the player is looking at.
/// </summary>
public static class BlockRaycaster
{
    /// <summary>
    /// Returns the first solid block within <paramref name="maxDistance"/>, or
    /// false when the ray only travels through air/out-of-bounds space.
    /// </summary>
    public static bool TryHit(
        World world,
        float originX,
        float originY,
        float originZ,
        float directionX,
        float directionY,
        float directionZ,
        float maxDistance,
        out int blockX,
        out int blockY,
        out int blockZ)
    {
        return TryHitWithFace(
            world,
            originX, originY, originZ,
            directionX, directionY, directionZ,
            maxDistance,
            out blockX, out blockY, out blockZ,
            out _, out _, out _);
    }

    /// <summary>
    /// Same as <see cref="TryHit"/> but also returns the face the ray entered,
    /// as a unit axis direction. Placing uses target + face to pick the cell.
    /// </summary>
    public static bool TryHitWithFace(
        World world,
        float originX,
        float originY,
        float originZ,
        float directionX,
        float directionY,
        float directionZ,
        float maxDistance,
        out int blockX,
        out int blockY,
        out int blockZ,
        out int faceX,
        out int faceY,
        out int faceZ)
    {
        blockX = blockY = blockZ = 0;
        faceX = faceY = faceZ = 0;

        var x = (int)MathF.Floor(originX);
        var y = (int)MathF.Floor(originY);
        var z = (int)MathF.Floor(originZ);

        var stepX = directionX >= 0f ? 1 : -1;
        var stepY = directionY >= 0f ? 1 : -1;
        var stepZ = directionZ >= 0f ? 1 : -1;

        var deltaX = directionX == 0f ? float.PositiveInfinity : Math.Abs(1f / directionX);
        var deltaY = directionY == 0f ? float.PositiveInfinity : Math.Abs(1f / directionY);
        var deltaZ = directionZ == 0f ? float.PositiveInfinity : Math.Abs(1f / directionZ);

        var nextX = (directionX >= 0f ? x + 1f - originX : originX - x) * deltaX;
        var nextY = (directionY >= 0f ? y + 1f - originY : originY - y) * deltaY;
        var nextZ = (directionZ >= 0f ? z + 1f - originZ : originZ - z) * deltaZ;

        var traveled = 0f;

        for (var step = 0; step < 64 && traveled <= maxDistance; step++)
        {
            if (world.InBounds(x, y, z))
            {
                var block = world.GetBlock(x, y, z);

                if (block != BlockId.Air && BlockRegistry.Get(block).IsSolid)
                {
                    blockX = x;
                    blockY = y;
                    blockZ = z;
                    return true;
                }
            }

            if (nextX <= nextY && nextX <= nextZ)
            {
                traveled = nextX;
                x += stepX;
                nextX += deltaX;
                faceX = -stepX;
                faceY = 0;
                faceZ = 0;
            }
            else if (nextY <= nextZ)
            {
                traveled = nextY;
                y += stepY;
                nextY += deltaY;
                faceX = 0;
                faceY = -stepY;
                faceZ = 0;
            }
            else
            {
                traveled = nextZ;
                z += stepZ;
                nextZ += deltaZ;
                faceX = 0;
                faceY = 0;
                faceZ = -stepZ;
            }
        }

        return false;
    }
}
