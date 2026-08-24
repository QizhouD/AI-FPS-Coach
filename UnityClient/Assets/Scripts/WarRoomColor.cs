using UnityEngine;

namespace FpsAiCoach
{
    /// <summary>
    /// Colour authoring helpers for the war-room look.
    ///
    /// The project renders in Linear colour space, where two different conventions apply:
    /// shader colour properties assigned through <see cref="Material.SetColor"/> are consumed
    /// as linear values, while Canvas graphics (Image / TextMeshProUGUI vertex colours) are
    /// converted from sRGB by the UI shaders. Every palette entry is therefore authored once as
    /// sRGB and passed through <see cref="ForMaterial"/> or <see cref="ForUi"/> at the point of
    /// use, so a swatch specified as #00E5FF resolves to exactly #00E5FF on screen in both.
    /// </summary>
    public static class WarRoomColor
    {
        /// <summary>Parses "#RRGGBB" or "#RRGGBBAA" into an sRGB colour.</summary>
        public static Color Hex(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out var parsed))
                return parsed;

            Debug.LogWarning($"WarRoomColor: could not parse '{hex}', falling back to magenta.");
            return Color.magenta;
        }

        /// <summary>Value to hand to <see cref="Material.SetColor"/> for the active colour space.</summary>
        public static Color ForMaterial(Color srgb)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? srgb.linear : srgb;
        }

        /// <summary>Value to hand to a Canvas graphic, which expects sRGB in every colour space.</summary>
        public static Color ForUi(Color srgb)
        {
            return srgb;
        }

        /// <summary>Emission value: linear colour scaled by an intensity that may exceed 1.</summary>
        public static Color ForEmission(Color srgb, float intensity)
        {
            var linear = ForMaterial(srgb);
            return new Color(
                linear.r * intensity,
                linear.g * intensity,
                linear.b * intensity,
                1f);
        }

        /// <summary>Scales RGB while leaving alpha alone. Used to dim accent lines without shifting hue.</summary>
        public static Color Scaled(Color color, float factor)
        {
            return new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
        }

        /// <summary>Replaces alpha, keeping RGB. Keeps call sites free of struct copies.</summary>
        public static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }
    }
}
