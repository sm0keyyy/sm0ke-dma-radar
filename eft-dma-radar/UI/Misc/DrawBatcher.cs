using SkiaSharp;
using System.Buffers;

namespace eft_dma_radar.UI.Misc
{
    /// <summary>
    /// Batches similar draw calls together to reduce GPU state changes and improve rendering performance.
    /// Groups points, lines, and circles by paint style to minimize draw calls.
    /// </summary>
    public class DrawBatcher
    {
        private readonly List<SKPoint> _points = new(256);
        private readonly List<SKPoint> _linePoints = new(512);
        private readonly List<CircleData> _circles = new(128);
        private readonly List<LineData> _lines = new(256);

        private SKPaint _currentPointPaint;
        private SKPaint _currentLinePaint;
        private SKPaint _currentCirclePaint;

        /// <summary>
        /// Adds a point to the batch. Points with the same paint will be drawn together.
        /// </summary>
        public void AddPoint(SKPoint point, SKPaint paint)
        {
            // If paint changes, flush previous batch
            if (_currentPointPaint != null && !PaintsMatch(_currentPointPaint, paint))
            {
                FlushPoints();
            }

            _currentPointPaint = paint;
            _points.Add(point);
        }

        /// <summary>
        /// Adds a line to the batch. Lines with the same paint will be drawn together.
        /// </summary>
        public void AddLine(SKPoint start, SKPoint end, SKPaint paint)
        {
            if (_currentLinePaint != null && !PaintsMatch(_currentLinePaint, paint))
            {
                FlushLines();
            }

            _currentLinePaint = paint;
            _lines.Add(new LineData { Start = start, End = end });
        }

        /// <summary>
        /// Adds a circle to the batch. Circles with the same paint will be drawn together.
        /// </summary>
        public void AddCircle(SKPoint center, float radius, SKPaint paint)
        {
            if (_currentCirclePaint != null && !PaintsMatch(_currentCirclePaint, paint))
            {
                FlushCircles();
            }

            _currentCirclePaint = paint;
            _circles.Add(new CircleData { Center = center, Radius = radius });
        }

        /// <summary>
        /// Flushes all batched draw calls to the canvas.
        /// </summary>
        public void Flush(SKCanvas canvas)
        {
            FlushPoints(canvas);
            FlushLines(canvas);
            FlushCircles(canvas);
        }

        /// <summary>
        /// Clears all batched data without drawing.
        /// </summary>
        public void Clear()
        {
            _points.Clear();
            _linePoints.Clear();
            _circles.Clear();
            _lines.Clear();
            _currentPointPaint = null;
            _currentLinePaint = null;
            _currentCirclePaint = null;
        }

        private void FlushPoints(SKCanvas canvas = null)
        {
            if (_points.Count > 0 && _currentPointPaint != null && canvas != null)
            {
                // Use DrawPoints for batch rendering
                canvas.DrawPoints(SKPointMode.Points, _points.ToArray(), _currentPointPaint);
            }

            _points.Clear();
            _currentPointPaint = null;
        }

        private void FlushLines(SKCanvas canvas = null)
        {
            if (_lines.Count > 0 && _currentLinePaint != null && canvas != null)
            {
                // Convert lines to point array for batch rendering
                // SKCanvas.DrawPoints with Lines mode requires pairs of points
                var pointArray = ArrayPool<SKPoint>.Shared.Rent(_lines.Count * 2);
                try
                {
                    for (int i = 0; i < _lines.Count; i++)
                    {
                        pointArray[i * 2] = _lines[i].Start;
                        pointArray[i * 2 + 1] = _lines[i].End;
                    }

                    canvas.DrawPoints(SKPointMode.Lines, pointArray.AsSpan(0, _lines.Count * 2).ToArray(), _currentLinePaint);
                }
                finally
                {
                    ArrayPool<SKPoint>.Shared.Return(pointArray);
                }
            }

            _lines.Clear();
            _currentLinePaint = null;
        }

        private void FlushCircles(SKCanvas canvas = null)
        {
            if (_circles.Count > 0 && _currentCirclePaint != null && canvas != null)
            {
                // Circles must be drawn individually, but we batch state changes
                foreach (var circle in _circles)
                {
                    canvas.DrawCircle(circle.Center, circle.Radius, _currentCirclePaint);
                }
            }

            _circles.Clear();
            _currentCirclePaint = null;
        }

        private bool PaintsMatch(SKPaint p1, SKPaint p2)
        {
            // Fast paint comparison for batching purposes
            // Only check critical properties that affect batching
            return p1.Color == p2.Color &&
                   p1.Style == p2.Style &&
                   Math.Abs(p1.StrokeWidth - p2.StrokeWidth) < 0.01f &&
                   p1.BlendMode == p2.BlendMode;
        }

        private struct CircleData
        {
            public SKPoint Center;
            public float Radius;
        }

        private struct LineData
        {
            public SKPoint Start;
            public SKPoint End;
        }
    }

    /// <summary>
    /// RAII-style wrapper for DrawBatcher to ensure proper flushing.
    /// </summary>
    public struct BatchedDrawing : IDisposable
    {
        private readonly DrawBatcher _batcher;
        private readonly SKCanvas _canvas;
        private bool _disposed;

        public DrawBatcher Batcher => _batcher;

        public BatchedDrawing(SKCanvas canvas)
        {
            _batcher = new DrawBatcher();
            _canvas = canvas;
            _disposed = false;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _batcher.Flush(_canvas);
                _disposed = true;
            }
        }
    }
}
