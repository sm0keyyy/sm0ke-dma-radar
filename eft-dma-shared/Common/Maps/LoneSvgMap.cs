using eft_dma_shared.Common.Misc;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using Svg.Skia;
using System.IO.Compression;
using System.Numerics;

namespace eft_dma_shared.Common.Maps
{
    /// <summary>
    /// SVG Map Implementation.
    /// </summary>
    public sealed class LoneSvgMap : ILoneMap
    {
        private readonly LoneMapConfig.LoadedLayer[] _layers;

        // Performance: Cache layer filtering to avoid LINQ allocations every frame
        private float _cachedPlayerHeight = float.NaN;
        private LoneMapConfig.LoadedLayer[] _cachedVisibleLayers = Array.Empty<LoneMapConfig.LoadedLayer>();
        private const float HEIGHT_CHANGE_THRESHOLD = 0.5f; // Only recalculate if height changes > 0.5 units

        // Pre-computed metadata to avoid repeated calculations
        private bool _hasMultipleLayers;
        private int _lastLayerIndex;
        private bool _anyLayerDimsBase;

        public string ID { get; }
        public LoneMapConfig Config { get; }

        public LoneSvgMap(ZipArchive zip, string id, LoneMapConfig config)
        {
            ID = id;
            Config = config;
            var layers = new List<LoneMapConfig.LoadedLayer>();
            try
            {
                using var paint = new SKPaint()
                {
                    IsAntialias = true,
                    FilterQuality = SKFilterQuality.High
                };
                foreach (var layer in config.MapLayers) // Load resources for new map
                {
                    using var stream = zip.Entries.First(x => x.Name
                            .Equals(layer.Filename,
                                StringComparison.OrdinalIgnoreCase))
                        .Open();
                    using var svg = SKSvg.CreateFromStream(stream);
                    // Create an image info with the desired dimensions
                    var scaleInfo = new SKImageInfo(
                        (int)Math.Round(svg.Picture!.CullRect.Width * config.SvgScale),
                        (int)Math.Round(svg.Picture!.CullRect.Height * config.SvgScale));
                    // Create a surface to draw on
                    using (var surface = SKSurface.Create(scaleInfo))
                    {
                        // Clear the surface
                        surface.Canvas.Clear(SKColors.Transparent);
                        // Apply the scale and draw the SVG picture
                        surface.Canvas.Scale(config.SvgScale);
                        surface.Canvas.DrawPicture(svg.Picture, paint);
                        layers.Add(new LoneMapConfig.LoadedLayer(surface.Snapshot(), layer));
                    }
                }
                _layers = layers.Order().ToArray();

                // Pre-compute metadata to avoid repeated calculations in Draw()
                _hasMultipleLayers = _layers.Length > 1;
                _lastLayerIndex = _layers.Length - 1;
            }
            catch
            {
                foreach (var layer in layers) // Unload any partially loaded layers
                {
                    layer.Dispose();
                }
                throw;
            }
        }

        public void Draw(SKCanvas canvas, float playerHeight, SKRect mapBounds, SKRect windowBounds)
        {
            using var _ = PerformanceProfiler.Instance.BeginSection("  Map Layer Rendering");

            // Check if we need to recalculate visible layers
            if (float.IsNaN(_cachedPlayerHeight) ||
                Math.Abs(playerHeight - _cachedPlayerHeight) > HEIGHT_CHANGE_THRESHOLD)
            {
                // Recalculate and cache visible layers
                _cachedVisibleLayers = _layers
                    .Where(layer => layer.IsHeightInRange(playerHeight))
                    .ToArray(); // Note: .Order() removed - layers already sorted at load time
                _cachedPlayerHeight = playerHeight;

                // Update metadata for paint selection
                _anyLayerDimsBase = _cachedVisibleLayers.Any(x => !x.DimBaseLayer);
            }

            // Use cached layers - dramatically faster than LINQ operations every frame
            for (int i = 0; i < _cachedVisibleLayers.Length; i++)
            {
                var layer = _cachedVisibleLayers[i];
                SKPaint paint;

                // Determine paint based on layer position and dimming settings
                if (_cachedVisibleLayers.Length > 1 && i != _cachedVisibleLayers.Length - 1 &&
                    !(layer.IsBaseLayer && _anyLayerDimsBase))
                {
                    paint = SharedPaints.PaintBitmapAlpha;
                }
                else
                {
                    paint = SharedPaints.PaintBitmap;
                }
                canvas.DrawImage(layer.Image, mapBounds, windowBounds, paint);
            }
        }

        /// <summary>
        /// Provides miscellaneous map parameters used throughout the entire render.
        /// </summary>
        public LoneMapParams GetParameters(SKGLElement element, int zoom, ref Vector2 localPlayerMapPos, int lod0Threshold = 70, int lod1Threshold = 85)
        {
            var zoomWidth = _layers[0].Image.Width * (.01f * zoom);
            var zoomHeight = _layers[0].Image.Height * (.01f * zoom);

            // Get the size of the element using the CanvasSize property
            var canvasSize = element.CanvasSize;

            var bounds = new SKRect(localPlayerMapPos.X - zoomWidth / 2,
                    localPlayerMapPos.Y - zoomHeight / 2,
                    localPlayerMapPos.X + zoomWidth / 2,
                    localPlayerMapPos.Y + zoomHeight / 2)
                .AspectFill(canvasSize);

            // Performance optimization: Calculate LOD level based on zoom
            // Lower zoom value = more zoomed IN = MORE detail needed (LOD 0)
            // Higher zoom value = more zoomed OUT = LESS detail needed (LOD 1/2)
            // Zoom range: 1-100 (1=closest, 100=farthest)
            int lodLevel = zoom >= lod1Threshold ? 2 : (zoom >= lod0Threshold ? 1 : 0);

            return new LoneMapParams
            {
                Map = Config,
                Bounds = bounds,
                XScale = canvasSize.Width / bounds.Width, // Set scale for this frame
                YScale = canvasSize.Height / bounds.Height, // Set scale for this frame
                LODLevel = lodLevel
            };
        }
        public LoneMapParams GetParametersE(SKSize control, float zoom, ref Vector2 localPlayerMapPos, int lod0Threshold = 70, int lod1Threshold = 85)
        {
            var zoomWidth = _layers[0].Image.Width * (.01f * zoom);
            var zoomHeight = _layers[0].Image.Height * (.01f * zoom);

            var bounds = new SKRect(localPlayerMapPos.X - zoomWidth / 2,
                    localPlayerMapPos.Y - zoomHeight / 2,
                    localPlayerMapPos.X + zoomWidth / 2,
                    localPlayerMapPos.Y + zoomHeight / 2)
                .AspectFill(control);

            // Performance optimization: Calculate LOD level based on zoom
            // Lower zoom value = more zoomed IN = MORE detail needed (LOD 0)
            // Higher zoom value = more zoomed OUT = LESS detail needed (LOD 1/2)
            // Zoom range: 1-100 (1=closest, 100=farthest)
            int lodLevel = zoom >= lod1Threshold ? 2 : (zoom >= lod0Threshold ? 1 : 0);

            return new LoneMapParams
            {
                Map = Config,
                Bounds = bounds,
                XScale = control.Width / bounds.Width, // Set scale for this frame
                YScale = control.Height / bounds.Height, // Set scale for this frame
                LODLevel = lodLevel
            };
        }
        public void Dispose()
        {
            for (int i = 0; i < _layers.Length; i++)
            {
                _layers[i]?.Dispose();
                _layers[i] = null;
            }
        }
    }
}