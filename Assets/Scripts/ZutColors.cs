using UnityEngine;

namespace IPZ
{
    public static class ZutColors
    {
        public static readonly Color Green = Hex(0x00813E);
        public static readonly Color Navy = Hex(0x023E84);
        public static readonly Color Blue = Hex(0x1D62D6);
        public static readonly Color Red = Hex(0xE3000F);
        public static readonly Color White = Color.white;

        public static readonly Color Night = Hex(0x020B1E);
        public static readonly Color Deep = Hex(0x041A3D);
        public static readonly Color Muted = Hex(0xA9BCDD);

        public static Color Player(int player) => player == 0 ? Green : Blue;

        public static Color Light(Color color) => Color.Lerp(color, Color.white, 0.38f);

        public static Color Glow(Color color, float intensity)
        {
            var linear = color.linear;
            float peak = Mathf.Max(linear.r, Mathf.Max(linear.g, linear.b));
            if (peak <= 0f)
                return color;
            float scale = intensity / peak;
            var boosted = new Color(linear.r * scale, linear.g * scale, linear.b * scale).gamma;
            boosted.a = color.a;
            return boosted;
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
