using arena_dma_radar.Arena.ArenaPlayer;
using arena_dma_radar.UI.ESP;
using arena_dma_radar.UI.Misc;
using arena_dma_radar.UI.Radar;
using eft_dma_shared.Common.Maps;
using eft_dma_shared.Common.Misc;
using eft_dma_shared.Common.Misc.Data;
using eft_dma_shared.Common.Players;
using eft_dma_shared.Common.Unity;
using SkiaSharp;
using System.Collections.Generic;

namespace arena_dma_radar.Arena.GameWorld.Interactive
{
    public class ArenaPresetRefillContainer : IMouseoverEntity, IMapEntity, IWorldEntity, IESPEntity
    {
        private static Config Config => Program.Config;

        public static EntityTypeSettings Settings => Config.EntityTypeSettings.GetSettings("RefillContainer");
        public static EntityTypeSettingsESP ESPSettings => ESP.Config.EntityTypeESPSettings.GetSettings("RefillContainer");

        private static readonly uint[] TransformChain =
        [
            ObjectClass.MonoBehaviourOffset,
            MonoBehaviour.GameObjectOffset,
            GameObject.ComponentsOffset,
            0x8
        ];

        private const float HEIGHT_INDICATOR_THRESHOLD = 1.85f;

        public ulong Base { get; private set; }
        private Vector3 _position;
        public ref Vector3 Position => ref _position;
        public Vector2 MouseoverPosition { get; set; }

        public ArenaPresetRefillContainer(ulong ptr)
        {
            Base = ptr;
            LoadPosition();
        }

        private void LoadPosition()
        {
            try
            {
                var transformInternal = Memory.ReadPtrChain(Base, TransformChain, false);
                var transform = new UnityTransform(transformInternal);
                _position = transform.UpdatePosition();
            }
            catch { }
        }

        public void Draw(SKCanvas canvas, LoneMapParams mapParams, ILocalPlayer localPlayer)
        {
            EntityTypeSettings entitySettings = Settings;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > entitySettings.RenderDistance)
                return;

            var label = "Refill Container";
            var paints = GetPaints();
            var heightDiff = Position.Y - localPlayer.Position.Y;
            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);
            SKPaints.ShapeOutline.StrokeWidth = 2f;

            float distanceYOffset;
            float nameXOffset = 7f * MainWindow.UIScale;
            float nameYOffset;

            if (heightDiff > HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetUpArrow(5);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paints.Item1);
                distanceYOffset = 18f * MainWindow.UIScale;
                nameYOffset = 6f * MainWindow.UIScale;
            }
            else if (heightDiff < -HEIGHT_INDICATOR_THRESHOLD)
            {
                using var path = point.GetDownArrow(5);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paints.Item1);
                distanceYOffset = 12f * MainWindow.UIScale;
                nameYOffset = 1f * MainWindow.UIScale;
            }
            else
            {
                var size = 5 * MainWindow.UIScale;
                canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
                canvas.DrawCircle(point, size, paints.Item1);
                distanceYOffset = 16f * MainWindow.UIScale;
                nameYOffset = 4f * MainWindow.UIScale;
            }

            if (entitySettings.ShowName)
            {
                point.Offset(nameXOffset, nameYOffset);
                if (!string.IsNullOrEmpty(label))
                {
                    canvas.DrawText(label, point, SKPaints.TextOutline);
                    canvas.DrawText(label, point, paints.Item2);
                }
            }

            if (entitySettings.ShowDistance)
            {
                var distText = $"{(int)dist}m";
                var distWidth = paints.Item2.MeasureText($"{(int)dist}");
                var distPoint = new SKPoint(
                    point.X - (distWidth / 2) - nameXOffset,
                    point.Y + distanceYOffset - nameYOffset
                );
                canvas.DrawText(distText, distPoint, SKPaints.TextOutline);
                canvas.DrawText(distText, distPoint, paints.Item2);
            }
        }

        public void DrawESP(SKCanvas canvas, LocalPlayer localPlayer)
        {
            EntityTypeSettingsESP espSettings = ESPSettings;

            var dist = Vector3.Distance(localPlayer.Position, Position);
            if (dist > espSettings.RenderDistance)
                return;

            if (!CameraManagerBase.WorldToScreen(ref _position, out var scrPos))
                return;

            var paints = GetESPPaints();
            var label = "Refill Container";
            var scale = ESP.Config.FontScale;

            switch (espSettings.RenderMode)
            {
                case EntityRenderMode.None:
                    break;

                case EntityRenderMode.Dot:
                    var dotSize = 3f * scale;
                    canvas.DrawCircle(scrPos.X, scrPos.Y, dotSize, paints.Item1);
                    break;

                case EntityRenderMode.Cross:
                    var crossSize = 5f * scale;
                    using (var thickPaint = new SKPaint
                    {
                        Color = paints.Item1.Color,
                        StrokeWidth = 1.5f * scale,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke
                    })
                    {
                        canvas.DrawLine(
                            scrPos.X - crossSize, scrPos.Y - crossSize,
                            scrPos.X + crossSize, scrPos.Y + crossSize,
                            thickPaint);
                        canvas.DrawLine(
                            scrPos.X - crossSize, scrPos.Y + crossSize,
                            scrPos.X + crossSize, scrPos.Y - crossSize,
                            thickPaint);
                    }
                    break;

                case EntityRenderMode.Plus:
                    var plusSize = 5f * scale;
                    using (var thickPaint = new SKPaint
                    {
                        Color = paints.Item1.Color,
                        StrokeWidth = 1.5f * scale,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke
                    })
                    {
                        canvas.DrawLine(
                            scrPos.X, scrPos.Y - plusSize,
                            scrPos.X, scrPos.Y + plusSize,
                            thickPaint);
                        canvas.DrawLine(
                            scrPos.X - plusSize, scrPos.Y,
                            scrPos.X + plusSize, scrPos.Y,
                            thickPaint);
                    }
                    break;

                case EntityRenderMode.Square:
                    var boxHalf = 3f * scale;
                    var boxPt = new SKRect(
                        scrPos.X - boxHalf, scrPos.Y - boxHalf,
                        scrPos.X + boxHalf, scrPos.Y + boxHalf);
                    canvas.DrawRect(boxPt, paints.Item1);
                    break;

                case EntityRenderMode.Diamond:
                default:
                    var diamondSize = 3.5f * scale;
                    using (var diamondPath = new SKPath())
                    {
                        diamondPath.MoveTo(scrPos.X, scrPos.Y - diamondSize);
                        diamondPath.LineTo(scrPos.X + diamondSize, scrPos.Y);
                        diamondPath.LineTo(scrPos.X, scrPos.Y + diamondSize);
                        diamondPath.LineTo(scrPos.X - diamondSize, scrPos.Y);
                        diamondPath.Close();
                        canvas.DrawPath(diamondPath, paints.Item1);
                    }
                    break;
            }

            if (espSettings.ShowName || espSettings.ShowDistance)
            {
                var textY = scrPos.Y + 16f * scale;
                var textPt = new SKPoint(scrPos.X, textY);

                textPt.DrawESPText(
                    canvas,
                    this,
                    localPlayer,
                    espSettings.ShowDistance,
                    paints.Item2,
                    espSettings.ShowName ? label : null
                );
            }
        }

        public void DrawMouseover(SKCanvas canvas, LoneMapParams mapParams, LocalPlayer localPlayer)
        {
            var lines = new List<string>
            {
                "Refill Container"
            };

            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines);
        }

        private ValueTuple<SKPaint, SKPaint> GetPaints()
        {
            return new(SKPaints.PaintRefillContainer, SKPaints.TextRefillContainer);
        }

        public ValueTuple<SKPaint, SKPaint> GetESPPaints()
        {
            return new(SKPaints.PaintRefillContainerESP, SKPaints.TextRefillContainerESP);
        }
    }
}