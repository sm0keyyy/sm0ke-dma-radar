using RBush;
using SkiaSharp;
using eft_dma_shared.Common.Maps;
using System.Numerics;

namespace eft_dma_radar.UI.Misc
{
    /// <summary>
    /// Spatial indexing wrapper for efficient viewport culling using R-tree.
    /// Dramatically reduces iteration overhead when querying entities within viewport bounds.
    /// </summary>
    public class SpatialIndex<T> where T : IMapEntity
    {
        private readonly RBush<SpatialItem<T>> _tree;
        private int _version = 0;

        public SpatialIndex()
        {
            _tree = new RBush<SpatialItem<T>>();
        }

        /// <summary>
        /// Clears and rebuilds the spatial index from a collection of entities.
        /// Call this when the entity collection changes significantly.
        /// </summary>
        public void Rebuild(IEnumerable<T> entities, LoneMapConfig mapConfig)
        {
            _tree.Clear();

            if (entities == null)
                return;

            var items = new List<SpatialItem<T>>();

            foreach (var entity in entities)
            {
                var pos = entity.Position;
                var mapPos = pos.ToMapPos(mapConfig);

                // Create a small envelope around the point for the R-tree
                // Use a 1-unit buffer to ensure point queries work correctly
                var envelope = new Envelope(
                    mapPos.X - 1,
                    mapPos.Y - 1,
                    mapPos.X + 1,
                    mapPos.Y + 1
                );

                items.Add(new SpatialItem<T>(entity, envelope));
            }

            _tree.BulkLoad(items);
            _version++;
        }

        /// <summary>
        /// Updates a single entity's position in the spatial index.
        /// More efficient than full rebuild when only a few entities move.
        /// </summary>
        public void UpdateEntity(T entity, LoneMapConfig mapConfig)
        {
            // Remove old entry (if exists) and insert new one
            // Note: RBush doesn't support efficient updates, so we'd need to track items separately
            // For now, use Rebuild() for simplicity, or implement a dirty flag system
        }

        /// <summary>
        /// Queries entities within the viewport bounds defined by map parameters.
        /// Returns only entities that could potentially be visible on screen.
        /// </summary>
        public IEnumerable<T> QueryViewport(LoneMapParams mapParams, float canvasWidth, float canvasHeight, float margin = 50f)
        {
            // Calculate the viewport bounds in map coordinates
            // Add margin to catch entities just outside the viewport
            var bounds = mapParams.Bounds;

            var envelope = new Envelope(
                bounds.Left - margin,
                bounds.Top - margin,
                bounds.Right + margin,
                bounds.Bottom + margin
            );

            var results = _tree.Search(envelope);

            foreach (var item in results)
            {
                yield return item.Entity;
            }
        }

        /// <summary>
        /// Queries entities within a specific radius of a point (in map coordinates).
        /// </summary>
        public IEnumerable<T> QueryRadius(Vector2 center, float radius)
        {
            var envelope = new Envelope(
                center.X - radius,
                center.Y - radius,
                center.X + radius,
                center.Y + radius
            );

            var results = _tree.Search(envelope);

            // Additional distance check for circular query (R-tree returns rectangular results)
            foreach (var item in results)
            {
                var pos = item.Entity.Position;
                var dx = pos.X - center.X;
                var dy = pos.Z - center.Y; // Note: Unity uses Y-up, so Z is the horizontal
                var distSq = dx * dx + dy * dy;

                if (distSq <= radius * radius)
                {
                    yield return item.Entity;
                }
            }
        }

        /// <summary>
        /// Gets the total number of entities in the index.
        /// </summary>
        public int Count => _tree.Count;

        /// <summary>
        /// Version number incremented on each rebuild. Useful for cache invalidation.
        /// </summary>
        public int Version => _version;
    }

    /// <summary>
    /// Internal wrapper class that associates an entity with its spatial envelope.
    /// </summary>
    internal class SpatialItem<T> : ISpatialData where T : IMapEntity
    {
        public T Entity { get; }
        private readonly Envelope _envelope;

        public SpatialItem(T entity, Envelope envelope)
        {
            Entity = entity;
            _envelope = envelope;
        }

        public ref readonly Envelope Envelope => ref _envelope;
    }
}
