using HandyControl.Data;
using HandyControl.Themes;
using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
namespace arena_dma_radar
{
    public partial class App : Application
{
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
        }

        internal void UpdateTheme(ApplicationTheme theme)
        {
            if (ThemeManager.Current.ApplicationTheme != theme)
                ThemeManager.Current.ApplicationTheme = theme;
        }

        internal void UpdateAccent(Brush accent)
        {
            if (ThemeManager.Current.AccentColor != accent)
                ThemeManager.Current.AccentColor = accent;
        }
    }
}
