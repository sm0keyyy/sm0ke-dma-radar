using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Forms;
using eft_dma_shared.Common.Misc;

namespace arena_dma_radar.UI.Misc
{
    public class MonitorHelper
    {
        public class MonitorInfo
        {
            public Rect Bounds { get; set; }
            public bool IsPrimary { get; set; }
        }

        public static List<MonitorInfo> GetAllMonitors()
        {
            var result = new List<MonitorInfo>();
            try
            {
                foreach (var screen in Screen.AllScreens)
                {
                    result.Add(new MonitorInfo
                    {
                        Bounds = new Rect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height),
                        IsPrimary = screen.Primary
                    });

                    LoneLogging.WriteLine($"[MonitorHelper] Monitor: {screen.Bounds.Width}x{screen.Bounds.Height}, Primary: {screen.Primary}");
                }
            }
            catch (Exception ex)
            {
                LoneLogging.WriteLine($"[MonitorHelper] Failed to fetch monitors: {ex}");
            }

            return result;
        }
    }

}
