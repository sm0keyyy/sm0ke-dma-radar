namespace eft_dma_radar.UI.Misc
{
    /// <summary>
    /// Defines Z-index constants for proper entity layering on the radar.
    /// Lower values render first (bottom), higher values render last (top).
    /// </summary>
    public static class RenderLayerConfig
    {
        // Layer 1: Background entities (100-199)
        public const int LAYER_BACKGROUND_BASE = 100;
        public const int MINES = 110;
        public const int SWITCHES = 120;
        public const int DOORS = 130;
        public const int QUEST_ZONES = 140;

        // Layer 2: Loot & Containers (200-299)
        public const int LAYER_LOOT_BASE = 200;
        public const int CONTAINERS = 210;
        public const int LOOT_ITEMS = 220;
        public const int QUEST_ITEMS = 230;

        // Layer 3: Players & AI (300-399)
        public const int LAYER_PLAYERS_BASE = 300;
        public const int GROUP_CONNECTIONS = 310;
        public const int PLAYERS_AI = 320;
        public const int EXPLOSIVES = 330;

        // Layer 4: Critical overlays (400+)
        public const int LAYER_OVERLAY_BASE = 400;
        public const int EXIT_POINTS = 410;
        public const int LOCAL_PLAYER = 420;
        public const int MOUSEOVER_TOOLTIP = 430;
        public const int PING_EFFECTS = 440;

        /// <summary>
        /// Gets a human-readable description of the layer for debugging.
        /// </summary>
        public static string GetLayerName(int zIndex)
        {
            return zIndex switch
            {
                >= LAYER_OVERLAY_BASE => "Overlay Layer",
                >= LAYER_PLAYERS_BASE => "Players/AI Layer",
                >= LAYER_LOOT_BASE => "Loot/Containers Layer",
                >= LAYER_BACKGROUND_BASE => "Background Layer",
                _ => "Unknown Layer"
            };
        }
    }
}
