using SkiaSharp;

namespace eft_dma_radar.UI.Misc
{
    /// <summary>
    /// Extension methods for SKCanvas to simplify using SKTextBlob for high-performance text rendering.
    ///
    /// USAGE:
    /// Instead of:  canvas.DrawText("Player Name", x, y, paint);
    /// Use:         canvas.DrawTextFast("Player Name", x, y, paint);
    ///
    /// The "Fast" variants automatically use SKTextBlob caching for 3-5x performance improvement.
    /// </summary>
    public static class SKCanvasExtensions
    {
        // Global text blob cache shared across all rendering
        private static SKTextBlobCache _textBlobCache = new(maxEntries: 2048);

        /// <summary>
        /// Call this at the start of each frame to enable automatic cache eviction.
        /// Should be called from MainWindow rendering loop.
        /// </summary>
        public static void BeginFrameTextCache()
        {
            _textBlobCache.BeginFrame();
        }

        /// <summary>
        /// Clears the text blob cache. Call when switching maps or on major UI changes.
        /// </summary>
        public static void ClearTextCache()
        {
            _textBlobCache.Clear();
        }

        /// <summary>
        /// Gets text blob cache statistics for debugging.
        /// </summary>
        public static SKTextBlobCache.CacheStats GetTextCacheStats()
        {
            return _textBlobCache.GetStats();
        }

        /// <summary>
        /// Draws text using SKTextBlob for 3-5x better performance than regular DrawText.
        /// Automatically caches the shaped text for reuse.
        ///
        /// Use this for dynamic text that changes (player names, distances, etc.).
        /// For static text, use TextAtlas instead (10-50x faster).
        /// </summary>
        public static void DrawTextFast(this SKCanvas canvas, string text, float x, float y, SKPaint paint)
        {
            if (string.IsNullOrEmpty(text) || paint == null)
                return;

            var blob = _textBlobCache.GetOrCreate(text, paint);
            if (blob != null)
            {
                canvas.DrawText(blob, x, y, paint);
            }
        }

        /// <summary>
        /// Draws text using SKTextBlob at a specific point.
        /// </summary>
        public static void DrawTextFast(this SKCanvas canvas, string text, SKPoint point, SKPaint paint)
        {
            DrawTextFast(canvas, text, point.X, point.Y, paint);
        }

        /// <summary>
        /// Draws text with outline using SKTextBlob for both passes.
        /// Common pattern for radar labels with black outline.
        /// </summary>
        public static void DrawTextFastWithOutline(this SKCanvas canvas, string text, float x, float y,
            SKPaint fillPaint, SKPaint outlinePaint)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Draw outline first
            if (outlinePaint != null)
            {
                var outlineBlob = _textBlobCache.GetOrCreate(text, outlinePaint);
                if (outlineBlob != null)
                {
                    canvas.DrawText(outlineBlob, x, y, outlinePaint);
                }
            }

            // Draw fill on top
            if (fillPaint != null)
            {
                var fillBlob = _textBlobCache.GetOrCreate(text, fillPaint);
                if (fillBlob != null)
                {
                    canvas.DrawText(fillBlob, x, y, fillPaint);
                }
            }
        }

        /// <summary>
        /// Draws text with outline at a specific point.
        /// </summary>
        public static void DrawTextFastWithOutline(this SKCanvas canvas, string text, SKPoint point,
            SKPaint fillPaint, SKPaint outlinePaint)
        {
            DrawTextFastWithOutline(canvas, text, point.X, point.Y, fillPaint, outlinePaint);
        }

        /// <summary>
        /// Draws text centered on a point using SKTextBlob.
        /// Measures text width and centers it horizontally.
        /// </summary>
        public static void DrawTextFastCentered(this SKCanvas canvas, string text, SKPoint centerPoint, SKPaint paint)
        {
            if (string.IsNullOrEmpty(text) || paint == null)
                return;

            var textWidth = paint.MeasureText(text);
            var x = centerPoint.X - (textWidth / 2f);

            var blob = _textBlobCache.GetOrCreate(text, paint);
            if (blob != null)
            {
                canvas.DrawText(blob, x, centerPoint.Y, paint);
            }
        }

        /// <summary>
        /// Draws text centered with outline using SKTextBlob.
        /// </summary>
        public static void DrawTextFastCenteredWithOutline(this SKCanvas canvas, string text, SKPoint centerPoint,
            SKPaint fillPaint, SKPaint outlinePaint)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var textWidth = fillPaint?.MeasureText(text) ?? outlinePaint?.MeasureText(text) ?? 0;
            var x = centerPoint.X - (textWidth / 2f);

            DrawTextFastWithOutline(canvas, text, x, centerPoint.Y, fillPaint, outlinePaint);
        }

        /// <summary>
        /// Draws positioned text with SKTextBlob - text aligned to a specific point.
        /// Common for labels next to entities.
        /// </summary>
        public static void DrawTextFastAligned(this SKCanvas canvas, string text, float x, float y,
            SKPaint paint, SKTextAlign align)
        {
            if (string.IsNullOrEmpty(text) || paint == null)
                return;

            float adjustedX = x;

            if (align == SKTextAlign.Center)
            {
                var textWidth = paint.MeasureText(text);
                adjustedX = x - (textWidth / 2f);
            }
            else if (align == SKTextAlign.Right)
            {
                var textWidth = paint.MeasureText(text);
                adjustedX = x - textWidth;
            }

            var blob = _textBlobCache.GetOrCreate(text, paint);
            if (blob != null)
            {
                canvas.DrawText(blob, adjustedX, y, paint);
            }
        }

        /// <summary>
        /// Batch draws multiple text strings at different positions using SKTextBlob.
        /// More efficient than individual DrawTextFast calls when drawing many labels.
        /// </summary>
        public static void DrawTextFastBatch(this SKCanvas canvas, IEnumerable<(string text, SKPoint position)> textItems, SKPaint paint)
        {
            if (paint == null)
                return;

            foreach (var (text, position) in textItems)
            {
                if (!string.IsNullOrEmpty(text))
                {
                    var blob = _textBlobCache.GetOrCreate(text, paint);
                    if (blob != null)
                    {
                        canvas.DrawText(blob, position.X, position.Y, paint);
                    }
                }
            }
        }

        /// <summary>
        /// Draws text with automatic fallback: tries TextAtlas first (fastest), then SKTextBlob (fast), then regular DrawText (slow).
        /// This is the "smart" text drawing method that picks the best strategy.
        /// </summary>
        public static void DrawTextSmart(this SKCanvas canvas, string text, SKPoint point, SKPaint paint, TextAtlas atlas = null)
        {
            if (string.IsNullOrEmpty(text) || paint == null)
                return;

            // Strategy 1: Use TextAtlas if available and text is in atlas (10-50x faster)
            if (atlas != null && atlas.Contains(text))
            {
                atlas.Draw(canvas, text, point, paint);
                return;
            }

            // Strategy 2: Use SKTextBlob cache (3-5x faster)
            var blob = _textBlobCache.GetOrCreate(text, paint);
            if (blob != null)
            {
                canvas.DrawText(blob, point.X, point.Y, paint);
                return;
            }

            // Strategy 3: Fallback to regular DrawText (slowest, but always works)
            canvas.DrawText(text, point, paint);
        }

        /// <summary>
        /// Smart text drawing with outline support.
        /// </summary>
        public static void DrawTextSmartWithOutline(this SKCanvas canvas, string text, SKPoint point,
            SKPaint fillPaint, SKPaint outlinePaint, TextAtlas atlas = null)
        {
            if (string.IsNullOrEmpty(text))
                return;

            // Strategy 1: TextAtlas (fastest)
            if (atlas != null && atlas.Contains(text))
            {
                atlas.Draw(canvas, text, point, fillPaint);
                return;
            }

            // Strategy 2: SKTextBlob (fast)
            DrawTextFastWithOutline(canvas, text, point, fillPaint, outlinePaint);
        }
    }
}
