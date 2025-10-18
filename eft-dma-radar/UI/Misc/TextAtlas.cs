using SkiaSharp;
using System;
using System.Collections.Generic;

namespace eft_dma_radar.UI.Misc
{
    /// <summary>
    /// Ultra-high-performance texture atlas system that pre-renders ALL static game text AND icons to textures.
    /// Drawing pre-rendered images is 10-50x faster than rasterizing text/shapes every frame.
    ///
    /// Pre-renders at startup (~30-40MB total):
    /// - Distance text: "0m" to "500m" EVERY 1m (501 entries, ~5-8MB)
    /// - Height text: "-50m" to "+50m" every 1m (100 entries, ~1-2MB)
    /// - ALL loot item names: ~1500 items (ShortName + Name, ~15-20MB)
    /// - Player indicators: ~50 common text strings (~1MB)
    /// - Icons: X marker, arrows, circles, dots, crosses, squares, diamonds (~5-10MB)
    ///
    /// For dynamic text (changing player names), falls back to cached DrawText.
    /// </summary>
    public class TextAtlas : IDisposable
    {
        private readonly Dictionary<string, AtlasEntry> _entries = new();

        private struct AtlasEntry
        {
            public SKImage Image;       // Pre-rendered image
            public float TextWidth;     // Original text width for centering
            public float BaselineY;     // Y offset for baseline alignment
        }

        private TextAtlas()
        {
            // Private - use factory methods
        }

        /// <summary>
        /// Draws text from atlas, centered on the point.
        /// Supports color tinting - pass fillPaint to render in that color.
        /// If not in atlas, falls back to standard DrawText (rare for dynamic text).
        /// </summary>
        public void DrawCentered(SKCanvas canvas, string text, SKPoint centerPoint, SKPaint fillPaint = null)
        {
            if (_entries.TryGetValue(text, out var entry))
            {
                // Use pre-rendered image (10-50x faster!)
                var x = centerPoint.X - (entry.TextWidth / 2f);
                var y = centerPoint.Y - entry.BaselineY;

                // Apply color tint if fillPaint provided
                if (fillPaint != null && fillPaint.Color != SKColors.White)
                {
                    using var tintedPaint = new SKPaint
                    {
                        ColorFilter = SKColorFilter.CreateBlendMode(fillPaint.Color, SKBlendMode.Modulate),
                        IsAntialias = true
                    };
                    canvas.DrawImage(entry.Image, x, y, tintedPaint);
                }
                else
                {
                    canvas.DrawImage(entry.Image, x, y);
                }
            }
            else if (fillPaint != null)
            {
                // Fallback for dynamic text not in atlas (rare)
                var textWidth = fillPaint.MeasureText(text);
                var x = centerPoint.X - (textWidth / 2f);
                canvas.DrawText(text, x, centerPoint.Y, SKPaints.TextOutline);
                canvas.DrawText(text, x, centerPoint.Y, fillPaint);
            }
        }

        /// <summary>
        /// Draws text from atlas at exact position (not centered).
        /// Supports color tinting - pass fillPaint to render in that color.
        /// </summary>
        public void Draw(SKCanvas canvas, string text, SKPoint point, SKPaint fillPaint = null)
        {
            if (_entries.TryGetValue(text, out var entry))
            {
                var y = point.Y - entry.BaselineY;

                // Apply color tint if fillPaint provided
                if (fillPaint != null && fillPaint.Color != SKColors.White)
                {
                    using var tintedPaint = new SKPaint
                    {
                        ColorFilter = SKColorFilter.CreateBlendMode(fillPaint.Color, SKBlendMode.Modulate),
                        IsAntialias = true
                    };
                    canvas.DrawImage(entry.Image, point.X, y, tintedPaint);
                }
                else
                {
                    canvas.DrawImage(entry.Image, point.X, y);
                }
            }
            else if (fillPaint != null)
            {
                canvas.DrawText(text, point, SKPaints.TextOutline);
                canvas.DrawText(text, point, fillPaint);
            }
        }

        /// <summary>
        /// Gets text width from atlas (or 0 if not found).
        /// </summary>
        public float GetWidth(string text)
        {
            return _entries.TryGetValue(text, out var entry) ? entry.TextWidth : 0f;
        }

        /// <summary>
        /// Checks if text is in the atlas.
        /// </summary>
        public bool Contains(string text)
        {
            return _entries.ContainsKey(text);
        }

        /// <summary>
        /// Internal method to add a text entry to the atlas.
        /// </summary>
        private void AddEntry(string text, SKPaint fillPaint, SKPaint outlinePaint)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var textBounds = new SKRect();
            var textWidth = fillPaint.MeasureText(text, ref textBounds);

            // Calculate image size with padding for outline
            const float padding = 10f;
            var imgWidth = (int)Math.Ceiling(textBounds.Width + padding * 2);
            var imgHeight = (int)Math.Ceiling(textBounds.Height + padding * 2);

            imgWidth = Math.Max(imgWidth, 16);
            imgHeight = Math.Max(imgHeight, 16);

            // Render text to image surface
            using var surface = SKSurface.Create(new SKImageInfo(imgWidth, imgHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            var drawX = padding - textBounds.Left;
            var drawY = padding - textBounds.Top;

            // Draw outline + fill (2-pass for quality)
            canvas.DrawText(text, drawX, drawY, outlinePaint);
            canvas.DrawText(text, drawX, drawY, fillPaint);

            _entries[text] = new AtlasEntry
            {
                Image = surface.Snapshot(),
                TextWidth = textWidth,
                BaselineY = drawY
            };
        }

        /// <summary>
        /// Bulk adds multiple text entries.
        /// </summary>
        private void AddBatch(IEnumerable<string> texts, SKPaint fillPaint, SKPaint outlinePaint)
        {
            foreach (var text in texts)
            {
                if (!_entries.ContainsKey(text))
                {
                    AddEntry(text, fillPaint, outlinePaint);
                }
            }
        }

        public void Dispose()
        {
            foreach (var entry in _entries.Values)
            {
                entry.Image?.Dispose();
            }
            _entries.Clear();
        }

        /// <summary>
        /// Creates a distance atlas: "0m" to "500m" EVERY 1 meter (501 entries, ~5-8MB).
        /// Generates sprite for EVERY meter for accurate distance display.
        /// </summary>
        public static TextAtlas CreateDistanceAtlas(SKPaint textPaint)
        {
            var atlas = new TextAtlas();
            using var outlinePaint = CreateOutlinePaint(textPaint);

            // Generate EVERY meter from 0 to 500 for accurate distances
            for (int i = 0; i <= 500; i++)
            {
                atlas.AddEntry($"{i}m", textPaint, outlinePaint);
            }

            return atlas;
        }

        /// <summary>
        /// Creates a height atlas: "-50m" to "+50m" EVERY 1 meter (100 entries, ~1-2MB).
        /// Generates sprite for every meter of height difference.
        /// </summary>
        public static TextAtlas CreateHeightAtlas(SKPaint textPaint)
        {
            var atlas = new TextAtlas();
            using var outlinePaint = CreateOutlinePaint(textPaint);

            // Generate every meter from -50 to +50
            for (int i = -50; i <= 50; i++)
            {
                if (i == 0) continue;
                string text = i > 0 ? $"+{i}m" : $"{i}m";
                atlas.AddEntry(text, textPaint, outlinePaint);
            }

            return atlas;
        }

        /// <summary>
        /// Creates a loot item atlas with ALL item names from the game database.
        /// Pre-renders ~1500-2000 item names (~15-20MB).
        /// Call this ONCE after loading TarkovMarketDB.
        /// </summary>
        public static TextAtlas CreateLootAtlas(SKPaint textPaint, IEnumerable<string> itemNames)
        {
            var atlas = new TextAtlas();
            using var outlinePaint = CreateOutlinePaint(textPaint);

            atlas.AddBatch(itemNames, textPaint, outlinePaint);

            return atlas;
        }

        /// <summary>
        /// Creates a custom atlas for any set of strings.
        /// Use for player indicators, quest names, container names, etc.
        /// </summary>
        public static TextAtlas CreateCustomAtlas(SKPaint textPaint, IEnumerable<string> texts)
        {
            var atlas = new TextAtlas();
            using var outlinePaint = CreateOutlinePaint(textPaint);

            atlas.AddBatch(texts, textPaint, outlinePaint);

            return atlas;
        }

        /// <summary>
        /// Creates an icon atlas with pre-rendered geometric shapes (arrows, X, circles, etc.).
        /// Includes multiple sizes for different UI scales.
        /// </summary>
        public static TextAtlas CreateIconAtlas()
        {
            var atlas = new TextAtlas();

            // Create paints for icon rendering
            using var fillPaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White,  // Will be tinted at draw time
                Style = SKPaintStyle.Fill
            };

            using var outlinePaint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.Black,
                StrokeWidth = 2f,
                Style = SKPaintStyle.Stroke
            };

            // Pre-render icons at multiple sizes (for different UI scales and entity types)
            float[] sizes = new[] { 3f, 4f, 5f, 6f, 8f, 10f };

            foreach (var size in sizes)
            {
                // Up arrow
                atlas.AddIconEntry($"up_arrow_{size}",
                    (canvas, w, h) => DrawUpArrow(canvas, w / 2, h / 2, size),
                    fillPaint, outlinePaint, size);

                // Down arrow
                atlas.AddIconEntry($"down_arrow_{size}",
                    (canvas, w, h) => DrawDownArrow(canvas, w / 2, h / 2, size),
                    fillPaint, outlinePaint, size);

                // Circle (for same-height entities)
                atlas.AddIconEntry($"circle_{size}",
                    (canvas, w, h) => canvas.DrawCircle(w / 2, h / 2, size, fillPaint),
                    fillPaint, outlinePaint, size);

                // X marker (for corpses)
                atlas.AddIconEntry($"x_marker_{size}",
                    (canvas, w, h) => DrawXMarker(canvas, w / 2, h / 2, size),
                    fillPaint, outlinePaint, size);

                // Dot (for ESP)
                atlas.AddIconEntry($"dot_{size}",
                    (canvas, w, h) => canvas.DrawCircle(w / 2, h / 2, size, fillPaint),
                    fillPaint, outlinePaint, size);

                // Cross/Plus (for ESP)
                atlas.AddIconEntry($"cross_{size}",
                    (canvas, w, h) => DrawCross(canvas, w / 2, h / 2, size),
                    fillPaint, outlinePaint, size);

                // Square (for ESP)
                atlas.AddIconEntry($"square_{size}",
                    (canvas, w, h) => canvas.DrawRect(w / 2 - size, h / 2 - size, size * 2, size * 2, fillPaint),
                    fillPaint, outlinePaint, size);

                // Diamond (for ESP)
                atlas.AddIconEntry($"diamond_{size}",
                    (canvas, w, h) => DrawDiamond(canvas, w / 2, h / 2, size),
                    fillPaint, outlinePaint, size);
            }

            return atlas;
        }

        /// <summary>
        /// Internal method to add an icon entry to the atlas.
        /// </summary>
        private void AddIconEntry(string key, Action<SKCanvas, int, int> drawAction, SKPaint fillPaint, SKPaint outlinePaint, float size)
        {
            const float padding = 10f;
            var imgSize = (int)Math.Ceiling(size * 2 + padding * 2);
            imgSize = Math.Max(imgSize, 16);

            using var surface = SKSurface.Create(new SKImageInfo(imgSize, imgSize, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            // Draw the icon
            drawAction(canvas, imgSize, imgSize);

            _entries[key] = new AtlasEntry
            {
                Image = surface.Snapshot(),
                TextWidth = size * 2,  // For centering
                BaselineY = imgSize / 2
            };
        }

        /// <summary>
        /// Draws an up arrow shape.
        /// </summary>
        private static void DrawUpArrow(SKCanvas canvas, float centerX, float centerY, float size)
        {
            using var path = new SKPath();
            path.MoveTo(centerX, centerY - size);  // Top point
            path.LineTo(centerX + size, centerY + size);  // Bottom right
            path.LineTo(centerX - size, centerY + size);  // Bottom left
            path.Close();

            canvas.DrawPath(path, SKPaints.ShapeOutline);
            canvas.DrawPath(path, new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Fill });
        }

        /// <summary>
        /// Draws a down arrow shape.
        /// </summary>
        private static void DrawDownArrow(SKCanvas canvas, float centerX, float centerY, float size)
        {
            using var path = new SKPath();
            path.MoveTo(centerX, centerY + size);  // Bottom point
            path.LineTo(centerX + size, centerY - size);  // Top right
            path.LineTo(centerX - size, centerY - size);  // Top left
            path.Close();

            canvas.DrawPath(path, SKPaints.ShapeOutline);
            canvas.DrawPath(path, new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Fill });
        }

        /// <summary>
        /// Draws an X marker (for corpses).
        /// </summary>
        private static void DrawXMarker(SKCanvas canvas, float centerX, float centerY, float size)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White,
                StrokeWidth = 2f,
                Style = SKPaintStyle.Stroke
            };

            canvas.DrawLine(centerX - size, centerY + size, centerX + size, centerY - size, SKPaints.ShapeOutline);
            canvas.DrawLine(centerX - size, centerY - size, centerX + size, centerY + size, SKPaints.ShapeOutline);
            canvas.DrawLine(centerX - size, centerY + size, centerX + size, centerY - size, paint);
            canvas.DrawLine(centerX - size, centerY - size, centerX + size, centerY + size, paint);
        }

        /// <summary>
        /// Draws a cross/plus shape (for ESP).
        /// </summary>
        private static void DrawCross(SKCanvas canvas, float centerX, float centerY, float size)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColors.White,
                StrokeWidth = 1.5f,
                Style = SKPaintStyle.Stroke
            };

            canvas.DrawLine(centerX, centerY - size, centerX, centerY + size, paint);
            canvas.DrawLine(centerX - size, centerY, centerX + size, centerY, paint);
        }

        /// <summary>
        /// Draws a diamond shape (for ESP).
        /// </summary>
        private static void DrawDiamond(SKCanvas canvas, float centerX, float centerY, float size)
        {
            using var path = new SKPath();
            path.MoveTo(centerX, centerY - size);
            path.LineTo(centerX + size, centerY);
            path.LineTo(centerX, centerY + size);
            path.LineTo(centerX - size, centerY);
            path.Close();

            canvas.DrawPath(path, new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Fill });
        }

        /// <summary>
        /// Draws an icon from the atlas, centered on the point.
        /// Supports color tinting - pass fillPaint to render in that color.
        /// </summary>
        public void DrawIcon(SKCanvas canvas, string iconKey, SKPoint centerPoint, SKPaint fillPaint = null)
        {
            if (_entries.TryGetValue(iconKey, out var entry))
            {
                var x = centerPoint.X - (entry.TextWidth / 2f);
                var y = centerPoint.Y - (entry.TextWidth / 2f);  // Icons are square

                if (fillPaint != null && fillPaint.Color != SKColors.White)
                {
                    using var tintedPaint = new SKPaint
                    {
                        ColorFilter = SKColorFilter.CreateBlendMode(fillPaint.Color, SKBlendMode.Modulate),
                        IsAntialias = true
                    };
                    canvas.DrawImage(entry.Image, x, y, tintedPaint);
                }
                else
                {
                    canvas.DrawImage(entry.Image, x, y);
                }
            }
        }

        /// <summary>
        /// Helper to create outline paint matching the fill paint.
        /// </summary>
        private static SKPaint CreateOutlinePaint(SKPaint textPaint)
        {
            return new SKPaint
            {
                SubpixelText = true,
                IsAntialias = true,
                Color = SKColors.Black,
                TextSize = textPaint.TextSize,
                IsStroke = true,
                StrokeWidth = 4f,
                Style = SKPaintStyle.Stroke,
                Typeface = textPaint.Typeface
            };
        }
    }
}
