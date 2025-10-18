using Microsoft.Extensions.ObjectPool;
using SkiaSharp;

namespace eft_dma_radar.UI.Misc
{
    /// <summary>
    /// Object pool for SKPaint instances to reduce GC pressure and allocation overhead.
    /// Automatically resets paint properties when returned to pool.
    /// </summary>
    public static class SkiaPaintPool
    {
        private static readonly ObjectPool<SKPaint> _strokePool;
        private static readonly ObjectPool<SKPaint> _fillPool;
        private static readonly ObjectPool<SKPaint> _textPool;

        static SkiaPaintPool()
        {
            var provider = new DefaultObjectPoolProvider();

            _strokePool = provider.Create(new StrokePaintPolicy());
            _fillPool = provider.Create(new FillPaintPolicy());
            _textPool = provider.Create(new TextPaintPolicy());
        }

        /// <summary>
        /// Gets a stroke paint from the pool. MUST be returned via ReturnStroke() when done.
        /// </summary>
        public static SKPaint GetStroke()
        {
            return _strokePool.Get();
        }

        /// <summary>
        /// Returns a stroke paint to the pool. Paint properties are reset.
        /// </summary>
        public static void ReturnStroke(SKPaint paint)
        {
            if (paint != null)
                _strokePool.Return(paint);
        }

        /// <summary>
        /// Gets a fill paint from the pool. MUST be returned via ReturnFill() when done.
        /// </summary>
        public static SKPaint GetFill()
        {
            return _fillPool.Get();
        }

        /// <summary>
        /// Returns a fill paint to the pool. Paint properties are reset.
        /// </summary>
        public static void ReturnFill(SKPaint paint)
        {
            if (paint != null)
                _fillPool.Return(paint);
        }

        /// <summary>
        /// Gets a text paint from the pool. MUST be returned via ReturnText() when done.
        /// </summary>
        public static SKPaint GetText()
        {
            return _textPool.Get();
        }

        /// <summary>
        /// Returns a text paint to the pool. Paint properties are reset.
        /// </summary>
        public static void ReturnText(SKPaint paint)
        {
            if (paint != null)
                _textPool.Return(paint);
        }

        /// <summary>
        /// Helper method to execute an action with a pooled stroke paint.
        /// Automatically returns paint to pool when done.
        /// </summary>
        public static void WithStroke(Action<SKPaint> action)
        {
            var paint = GetStroke();
            try
            {
                action(paint);
            }
            finally
            {
                ReturnStroke(paint);
            }
        }

        /// <summary>
        /// Helper method to execute an action with a pooled fill paint.
        /// Automatically returns paint to pool when done.
        /// </summary>
        public static void WithFill(Action<SKPaint> action)
        {
            var paint = GetFill();
            try
            {
                action(paint);
            }
            finally
            {
                ReturnFill(paint);
            }
        }

        /// <summary>
        /// Helper method to execute an action with a pooled text paint.
        /// Automatically returns paint to pool when done.
        /// </summary>
        public static void WithText(Action<SKPaint> action)
        {
            var paint = GetText();
            try
            {
                action(paint);
            }
            finally
            {
                ReturnText(paint);
            }
        }

        private class StrokePaintPolicy : IPooledObjectPolicy<SKPaint>
        {
            public SKPaint Create()
            {
                return new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    IsAntialias = true,
                    StrokeWidth = 1f
                };
            }

            public bool Return(SKPaint paint)
            {
                // Reset to default stroke settings
                paint.Style = SKPaintStyle.Stroke;
                paint.Color = SKColors.White;
                paint.StrokeWidth = 1f;
                paint.IsAntialias = true;
                paint.FilterQuality = SKFilterQuality.None;
                paint.BlendMode = SKBlendMode.SrcOver;
                paint.Shader = null;
                paint.ColorFilter = null;
                paint.ImageFilter = null;
                paint.PathEffect = null;
                paint.MaskFilter = null;

                return true; // Return to pool
            }
        }

        private class FillPaintPolicy : IPooledObjectPolicy<SKPaint>
        {
            public SKPaint Create()
            {
                return new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
            }

            public bool Return(SKPaint paint)
            {
                // Reset to default fill settings
                paint.Style = SKPaintStyle.Fill;
                paint.Color = SKColors.White;
                paint.IsAntialias = true;
                paint.FilterQuality = SKFilterQuality.None;
                paint.BlendMode = SKBlendMode.SrcOver;
                paint.Shader = null;
                paint.ColorFilter = null;
                paint.ImageFilter = null;
                paint.PathEffect = null;
                paint.MaskFilter = null;

                return true; // Return to pool
            }
        }

        private class TextPaintPolicy : IPooledObjectPolicy<SKPaint>
        {
            public SKPaint Create()
            {
                return new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true,
                    TextSize = 12f,
                    Typeface = SKTypeface.Default
                };
            }

            public bool Return(SKPaint paint)
            {
                // Reset to default text settings
                paint.Style = SKPaintStyle.Fill;
                paint.Color = SKColors.White;
                paint.TextSize = 12f;
                paint.IsAntialias = true;
                paint.FilterQuality = SKFilterQuality.None;
                paint.BlendMode = SKBlendMode.SrcOver;
                paint.Shader = null;
                paint.ColorFilter = null;
                paint.ImageFilter = null;
                paint.PathEffect = null;
                paint.MaskFilter = null;
                paint.TextAlign = SKTextAlign.Left;

                return true; // Return to pool
            }
        }
    }

    /// <summary>
    /// RAII-style wrapper for pooled SKPaint objects.
    /// Automatically returns paint to pool when disposed.
    /// </summary>
    public struct PooledPaint : IDisposable
    {
        private readonly SKPaint _paint;
        private readonly Action<SKPaint> _returnAction;
        private bool _disposed;

        public SKPaint Paint => _paint;

        public static PooledPaint GetStroke()
        {
            return new PooledPaint(SkiaPaintPool.GetStroke(), SkiaPaintPool.ReturnStroke);
        }

        public static PooledPaint GetFill()
        {
            return new PooledPaint(SkiaPaintPool.GetFill(), SkiaPaintPool.ReturnFill);
        }

        public static PooledPaint GetText()
        {
            return new PooledPaint(SkiaPaintPool.GetText(), SkiaPaintPool.ReturnText);
        }

        private PooledPaint(SKPaint paint, Action<SKPaint> returnAction)
        {
            _paint = paint;
            _returnAction = returnAction;
            _disposed = false;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _returnAction?.Invoke(_paint);
                _disposed = true;
            }
        }
    }
}
