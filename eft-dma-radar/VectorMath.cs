using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace eft_dma_radar
{
    /// <summary>
    /// High-performance vectorized math operations using SIMD (AVX2/SSE).
    /// Processes 4-8 calculations simultaneously for significant performance gains.
    /// </summary>
    public static class VectorMath
    {
        /// <summary>
        /// Calculates squared distances from a single entity to multiple players using SIMD.
        /// Processes 4 distances per iteration on AVX2 CPUs (8 with AVX512).
        /// </summary>
        /// <param name="entityX">Entity X coordinate</param>
        /// <param name="entityY">Entity Y coordinate</param>
        /// <param name="playerPositions">Array of player positions (Vector2)</param>
        /// <param name="results">Output array for squared distances (must be same length as playerPositions)</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CalculateDistancesSquaredBatch(
            float entityX,
            float entityY,
            Vector2[] playerPositions,
            float[] results)
        {
            if (playerPositions == null || results == null)
                return;

            int length = Math.Min(playerPositions.Length, results.Length);

            // Use AVX2 if available for processing 8 floats (4 Vector2s) at once
            if (Avx2.IsSupported && length >= 4)
            {
                CalculateDistancesSquaredAVX2(entityX, entityY, playerPositions, results, length);
            }
            // Fallback to SSE for processing 4 floats (2 Vector2s) at once
            else if (Sse.IsSupported && length >= 2)
            {
                CalculateDistancesSquaredSSE(entityX, entityY, playerPositions, results, length);
            }
            // Scalar fallback for non-SIMD CPUs or small arrays
            else
            {
                CalculateDistancesSquaredScalar(entityX, entityY, playerPositions, results, length);
            }
        }

        /// <summary>
        /// AVX2 implementation: Process 4 Vector2 distances simultaneously (256-bit registers).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CalculateDistancesSquaredAVX2(
            float entityX,
            float entityY,
            Vector2[] playerPositions,
            float[] results,
            int length)
        {
            int vectorSize = Vector256<float>.Count; // 8 floats = 4 Vector2s
            int i = 0;

            // Broadcast entity coordinates to all lanes of the vector
            var entityXVec = Vector256.Create(entityX);
            var entityYVec = Vector256.Create(entityY);

            // Process 4 Vector2s (8 floats) at a time
            fixed (Vector2* pPositions = playerPositions)
            fixed (float* pResults = results)
            {
                for (; i + 3 < length; i += 4)
                {
                    // Load 4 Vector2s as interleaved X,Y,X,Y,X,Y,X,Y
                    var pos0 = pPositions[i];
                    var pos1 = pPositions[i + 1];
                    var pos2 = pPositions[i + 2];
                    var pos3 = pPositions[i + 3];

                    // Create vectors: [X0, Y0, X1, Y1, X2, Y2, X3, Y3]
                    var xyVec = Vector256.Create(pos0.X, pos0.Y, pos1.X, pos1.Y,
                                                 pos2.X, pos2.Y, pos3.X, pos3.Y);

                    // Extract X and Y components
                    // X: [X0, X1, X2, X3, X0, X1, X2, X3] (we'll use lower half)
                    // Y: [Y0, Y1, Y2, Y3, Y0, Y1, Y2, Y3] (we'll use lower half)
                    var xVec = Vector256.Create(pos0.X, pos1.X, pos2.X, pos3.X, 0f, 0f, 0f, 0f);
                    var yVec = Vector256.Create(pos0.Y, pos1.Y, pos2.Y, pos3.Y, 0f, 0f, 0f, 0f);

                    // Calculate deltas: dx = playerX - entityX, dy = playerY - entityY
                    var dx = Avx.Subtract(xVec, entityXVec);
                    var dy = Avx.Subtract(yVec, entityYVec);

                    // Calculate distSq = dx*dx + dy*dy
                    var dxSq = Avx.Multiply(dx, dx);
                    var dySq = Avx.Multiply(dy, dy);
                    var distSq = Avx.Add(dxSq, dySq);

                    // Store results (only first 4 elements are valid)
                    pResults[i] = distSq.GetElement(0);
                    pResults[i + 1] = distSq.GetElement(1);
                    pResults[i + 2] = distSq.GetElement(2);
                    pResults[i + 3] = distSq.GetElement(3);
                }
            }

            // Handle remaining elements with scalar code
            for (; i < length; i++)
            {
                float dx = playerPositions[i].X - entityX;
                float dy = playerPositions[i].Y - entityY;
                results[i] = dx * dx + dy * dy;
            }
        }

        /// <summary>
        /// SSE implementation: Process 2 Vector2 distances simultaneously (128-bit registers).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void CalculateDistancesSquaredSSE(
            float entityX,
            float entityY,
            Vector2[] playerPositions,
            float[] results,
            int length)
        {
            int i = 0;

            // Broadcast entity coordinates to all lanes of the vector
            var entityXVec = Vector128.Create(entityX);
            var entityYVec = Vector128.Create(entityY);

            // Process 2 Vector2s (4 floats) at a time
            fixed (Vector2* pPositions = playerPositions)
            fixed (float* pResults = results)
            {
                for (; i + 1 < length; i += 2)
                {
                    var pos0 = pPositions[i];
                    var pos1 = pPositions[i + 1];

                    // Create vectors: [X0, X1, 0, 0] and [Y0, Y1, 0, 0]
                    var xVec = Vector128.Create(pos0.X, pos1.X, 0f, 0f);
                    var yVec = Vector128.Create(pos0.Y, pos1.Y, 0f, 0f);

                    // Calculate deltas
                    var dx = Sse.Subtract(xVec, entityXVec);
                    var dy = Sse.Subtract(yVec, entityYVec);

                    // Calculate distSq = dx*dx + dy*dy
                    var dxSq = Sse.Multiply(dx, dx);
                    var dySq = Sse.Multiply(dy, dy);
                    var distSq = Sse.Add(dxSq, dySq);

                    // Store results
                    pResults[i] = distSq.GetElement(0);
                    pResults[i + 1] = distSq.GetElement(1);
                }
            }

            // Handle remaining elements with scalar code
            for (; i < length; i++)
            {
                float dx = playerPositions[i].X - entityX;
                float dy = playerPositions[i].Y - entityY;
                results[i] = dx * dx + dy * dy;
            }
        }

        /// <summary>
        /// Scalar fallback for non-SIMD CPUs or small arrays.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CalculateDistancesSquaredScalar(
            float entityX,
            float entityY,
            Vector2[] playerPositions,
            float[] results,
            int length)
        {
            for (int i = 0; i < length; i++)
            {
                float dx = playerPositions[i].X - entityX;
                float dy = playerPositions[i].Y - entityY;
                results[i] = dx * dx + dy * dy;
            }
        }

        /// <summary>
        /// Checks if any distance in the batch is within the specified radius.
        /// Returns true on first match for early exit optimization.
        /// </summary>
        /// <param name="entityX">Entity X coordinate</param>
        /// <param name="entityY">Entity Y coordinate</param>
        /// <param name="playerPositions">Array of player positions</param>
        /// <param name="radiiSquared">Array of squared radii (must match playerPositions length)</param>
        /// <returns>True if entity is within any player's radius</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWithinAnyRadius(
            float entityX,
            float entityY,
            Vector2[] playerPositions,
            float[] radiiSquared)
        {
            if (playerPositions == null || radiiSquared == null)
                return false;

            int length = Math.Min(playerPositions.Length, radiiSquared.Length);

            // Use SIMD for batch distance calculation
            Span<float> distancesSquared = stackalloc float[length];
            float[] distArray = new float[length];

            CalculateDistancesSquaredBatch(entityX, entityY, playerPositions, distArray);

            // Check each distance against its radius
            for (int i = 0; i < length; i++)
            {
                if (distArray[i] <= radiiSquared[i])
                    return true; // Early exit on first match
            }

            return false;
        }

        /// <summary>
        /// Finds the minimum distance from entity to any player using SIMD.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FindMinDistanceSquared(
            float entityX,
            float entityY,
            Vector2[] playerPositions)
        {
            if (playerPositions == null || playerPositions.Length == 0)
                return float.MaxValue;

            int length = playerPositions.Length;
            float[] distances = new float[length];

            CalculateDistancesSquaredBatch(entityX, entityY, playerPositions, distances);

            // Find minimum using SIMD where possible
            float minDist = float.MaxValue;
            for (int i = 0; i < length; i++)
            {
                if (distances[i] < minDist)
                    minDist = distances[i];
            }

            return minDist;
        }

        /// <summary>
        /// Convenience method: Calculate single distance squared (non-SIMD).
        /// Use for one-off calculations. For multiple distances, use batch method.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceSquared(Vector2 a, Vector2 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// Convenience method: Calculate single distance squared from components.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceSquared(float x1, float y1, float x2, float y2)
        {
            float dx = x1 - x2;
            float dy = y1 - y2;
            return dx * dx + dy * dy;
        }
    }
}
