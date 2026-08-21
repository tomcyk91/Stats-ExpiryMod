using UnityEngine;

namespace StatisticMod
{
    /// <summary>
    /// Centralny motyw interfejsu aplikacji Stats & Expiry.
    /// Pass 3: dopracowane kafelki, podsumowanie i analiza produktu.
    /// Kolory można później zmienić w jednym miejscu bez przebudowy layoutu.
    /// </summary>
    internal static class StatsAppTheme
    {
        // App shell
        internal static readonly Color AppFrame = new Color32(20, 34, 50, 255);
        internal static readonly Color Header = new Color32(35, 56, 76, 255);
        internal static readonly Color HeaderBorder = new Color32(74, 98, 118, 230);
        internal static readonly Color Body = new Color32(232, 239, 244, 255);
        internal static readonly Color Surface = new Color32(247, 250, 252, 255);
        internal static readonly Color SurfaceAlt = new Color32(221, 231, 239, 255);
        internal static readonly Color Border = new Color32(177, 194, 206, 210);

        // Interactive elements
        internal static readonly Color Button = new Color32(48, 91, 122, 255);
        internal static readonly Color ButtonHover = new Color32(58, 112, 149, 255);
        internal static readonly Color ButtonPressed = new Color32(38, 76, 105, 255);
        internal static readonly Color Accent = new Color32(67, 158, 200, 255);
        internal static readonly Color AccentHover = new Color32(81, 177, 220, 255);
        internal static readonly Color AccentSoft = new Color32(67, 158, 200, 64);
        internal static readonly Color InputBackground = new Color32(249, 251, 252, 255);
        internal static readonly Color Danger = new Color32(183, 73, 72, 255);
        internal static readonly Color DangerDark = new Color32(113, 45, 45, 180);

        // Text
        internal static readonly Color TextLight = new Color32(245, 248, 250, 255);
        internal static readonly Color TextDark = new Color32(42, 58, 71, 255);
        internal static readonly Color TextMuted = new Color32(107, 128, 143, 255);

        // Lists / scrolling
        internal static readonly Color ScrollTrack = new Color32(197, 211, 220, 220);
        internal static readonly Color ScrollThumb = new Color32(82, 132, 162, 245);
        internal static readonly Color DropdownBackground = new Color32(28, 48, 65, 252);
        internal static readonly Color DropdownItem = new Color32(255, 255, 255, 18);
        internal static readonly Color DropdownSelected = new Color32(57, 137, 179, 245);


        // Charts / product analysis - Pass 3
        internal static readonly Color ChartHeaderSurface = new Color32(229, 236, 241, 255);
        internal static readonly Color ChartBackground = new Color32(247, 249, 251, 255);
        internal static readonly Color ChartRangeInactive = new Color32(214, 222, 229, 255);

        // Cards - Pass 3
        internal static readonly Color TileBackground = new Color32(248, 251, 253, 255);
        internal static readonly Color TileBorder = new Color32(181, 198, 210, 235);
        internal static readonly Color TileIconBackground = new Color32(230, 238, 244, 255);
        internal static readonly Color TileIconBorder = new Color32(195, 210, 220, 235);
        internal static readonly Color TileTitle = new Color32(34, 52, 66, 255);
        internal static readonly Color TileText = new Color32(66, 82, 94, 255);
        internal static readonly Color TileMuted = new Color32(113, 132, 146, 255);
        internal static readonly Color TileSeparator = new Color32(207, 219, 227, 235);

        // Semantic colors tuned for light cards
        internal static readonly Color Info = new Color32(41, 124, 166, 255);
        internal static readonly Color Positive = new Color32(48, 139, 88, 255);
        internal static readonly Color Warning = new Color32(194, 119, 26, 255);
        internal static readonly Color Negative = new Color32(194, 67, 58, 255);
        internal static readonly Color Purple = new Color32(112, 86, 170, 255);

        internal const string InfoHex = "#297CA6";
        internal const string PositiveHex = "#308B58";
        internal const string WarningHex = "#C2771A";
        internal const string NegativeHex = "#C2433A";
        internal const string PurpleHex = "#7056AA";
        internal const string MutedHex = "#718492";

        // Effects
        internal static readonly Color Shadow = new Color32(0, 0, 0, 28);
        internal static readonly Color StrongShadow = new Color32(0, 0, 0, 48);

        // Tile geometry
        internal const float TileWidth = 205f;
        internal const float TileHeight = 96f;

        // Layout constants (normalized anchors)
        internal const float OuterLeft = 0.018f;
        internal const float OuterRight = 0.982f;
        internal const float HeaderBottom = 0.855f;
        internal const float HeaderTop = 0.965f;
        internal const float BodyBottom = 0.035f;
        internal const float BodyTop = 0.842f;
        internal const float ContentBottom = 0.055f;
        internal const float ContentTop = 0.825f;
    }
}
