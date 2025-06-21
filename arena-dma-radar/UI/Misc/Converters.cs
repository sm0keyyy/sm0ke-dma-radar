using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using eft_dma_shared.Common.Misc;
using Brushes = System.Windows.Media.Brushes;

namespace arena_dma_radar.Converters
{
    public class ColorHexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                try
                {
                    return (SolidColorBrush)(new BrushConverter().ConvertFrom(hex));
                }
                catch
                {
                    return Brushes.Transparent;
                }
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
                return brush.Color.ToString();

            return "#FFFFFFFF";
        }
    }
    
    public class ItemIconConverter : IValueConverter
    {
        private static readonly string IconPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "eft-dma-radar", "Assets", "Icons", "Items");

        public static async Task SaveItemIconAsPng(string itemId, string saveDir)
        {
            string webpUrl = $"https://assets.tarkov.dev/{itemId}-base-image.webp";
            string outputPath = Path.Combine(saveDir, $"{itemId}.png");

            // Don't re-download if a valid icon already exists
            if (File.Exists(outputPath))
            {
                var fileInfo = new FileInfo(outputPath);
                if (fileInfo.Length > 1024) // Skip re-download if file is >1KB (sanity check)
                    return;
            }

            try
            {
                using var httpClient = new HttpClient();
                var imageBytes = await httpClient.GetByteArrayAsync(webpUrl);

                using var memoryStream = new MemoryStream(imageBytes);
                using var codec = SKCodec.Create(memoryStream);
                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888);
                using var bitmap = new SKBitmap(info);

                if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
                    throw new Exception("Failed to decode WebP image");

                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                data.SaveTo(outputStream);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IconCache] Error downloading icon for {itemId}: {ex.Message}");
            }
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string itemId) return null;
        
            string path = Path.Combine(IconPath, $"{itemId}.png");
            if (!File.Exists(path)) return null;
        
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            //LoneLogging.WriteLine($"[IconCache] Loaded icon for {itemId} from {path}");
            return image;
        }
        
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isEnabled)
                return isEnabled ? 1.0 : 0.1;

            return 0.4;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DoubleGreaterThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            double threshold;
            double compareValue;

            if (!double.TryParse(parameter.ToString(), out threshold))
                return false;

            if (!double.TryParse(value.ToString(), out compareValue))
                return false;

            return compareValue > threshold;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DoubleLessThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            double threshold;
            double compareValue;

            if (!double.TryParse(parameter.ToString(), out threshold))
                return false;

            if (!double.TryParse(value.ToString(), out compareValue))
                return false;

            return compareValue < threshold;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DoubleLessThanOrEqualConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            double threshold;
            double compareValue;

            if (!double.TryParse(parameter.ToString(), out threshold))
                return false;

            if (!double.TryParse(value.ToString(), out compareValue))
                return false;

            return compareValue <= threshold;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
