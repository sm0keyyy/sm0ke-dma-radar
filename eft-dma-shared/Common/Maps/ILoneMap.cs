using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using System.Numerics;

namespace eft_dma_shared.Common.Maps
{
    public interface ILoneMap : IDisposable
    {
        /// <summary>
        /// Raw Map ID for this Map.
        /// </summary>
        string ID { get; }

        /// <summary>
        /// Configuration for this Map.
        /// </summary>
        LoneMapConfig Config { get; }

        /// <summary>
        /// Draw the Map on the provided Canvas.
        /// </summary>
        /// <param name="canvas"></param>
        /// <param name="playerHeight"></param>
        /// <param name="mapBounds"></param>
        /// <param name="windowBounds"></param>
        void Draw(SKCanvas canvas, float playerHeight, SKRect mapBounds, SKRect windowBounds);

        /// <summary>
        /// Get Parameters for this map.
        /// </summary>
        /// <param name="control"></param>
        /// <param name="zoom"></param>
        /// <param name="localPlayerMapPos"></param>
        /// <param name="lod0Threshold"></param>
        /// <param name="lod1Threshold"></param>
        /// <returns></returns>
        LoneMapParams GetParameters(SKGLElement element, int zoom, ref Vector2 localPlayerMapPos, int lod0Threshold = 70, int lod1Threshold = 85);
        LoneMapParams GetParametersE(SKSize control, float zoom, ref Vector2 localPlayerMapPos, int lod0Threshold = 70, int lod1Threshold = 85);
    }
}
