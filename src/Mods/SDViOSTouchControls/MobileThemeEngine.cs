using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace SDViOSTouchControls
{
    /// <summary>
    /// Automatically themes all Mobile HUD elements and virtual touch buttons:
    /// 1) Vanilla (no UI mod): uses genuine LooseSprites/MobileAtlas_manually_made sprites.
    /// 2) Mobile-aware UI mod (patches MobileAtlas_manually_made): renders the mod's custom MobileAtlas directly.
    /// 3) PC-only UI mod (patches Maps/MenuTiles or LooseSprites/Cursors, e.g. Overgrown Flowery Interface / Cat Valley):
    ///    samples the active UI mod's color ramp from Game1.menuTexture and palette-maps MobileAtlas_manually_made
    ///    automatically, while drawing containers with Game1.menuTexture (inheriting flowers/vines/frames natively).
    /// </summary>
    public static class MobileThemeEngine
    {
        // Exact verified source rectangles inside LooseSprites/MobileAtlas_manually_made (256x512)
        public static readonly Rectangle SrcCheckBtn = new Rectangle(0, 0, 20, 20);
        public static readonly Rectangle SrcCloseBtn = new Rectangle(20, 0, 21, 21);
        public static readonly Rectangle SrcLeftArrow = new Rectangle(80, 0, 20, 19);
        public static readonly Rectangle SrcRightArrow = new Rectangle(100, 0, 20, 19);
        public static readonly Rectangle SrcQuestBtn = new Rectangle(0, 44, 16, 16);
        public static readonly Rectangle SrcMenuBtn = new Rectangle(16, 44, 16, 16);
        public static readonly Rectangle SrcMoneyBar = new Rectangle(32, 45, 65, 15);
        public static readonly Rectangle SrcToggleJoy = new Rectangle(98, 44, 16, 16);
        public static readonly Rectangle SrcCirclePad = new Rectangle(0, 116, 37, 37);
        public static readonly Rectangle SrcDayTimeBox = new Rectangle(0, 153, 95, 31);
        public static readonly Rectangle SrcToolbarBox = new Rectangle(95, 131, 34, 34);
        public static readonly Rectangle SrcSlotHighlightWhite = new Rectangle(0, 96, 20, 20);
        public static readonly Rectangle SrcSlotHighlightRed = new Rectangle(20, 95, 20, 21);

        // Cached region size covering all mobile HUD/control sprites in MobileAtlas_manually_made
        private const int AtlasRegionW = 175;
        private const int AtlasRegionH = 230;

        // Vanilla reference wood ramp (darkest outline -> brightest interior)
        private static readonly Color[] VanillaWoodRamp = new[]
        {
            new Color(91, 43, 42),    // 0: darkest outline
            new Color(133, 54, 5),    // 1: dark border
            new Color(177, 78, 5),    // 2: mid-dark bevel
            new Color(220, 123, 5),   // 3: mid wood
            new Color(247, 186, 0),   // 4: bright gold/trim
            new Color(255, 210, 132), // 5: light interior fill
            new Color(255, 232, 184)  // 6: brightest interior highlight
        };

        private static Texture2D? _rawMobileAtlas;
        private static Texture2D? _themedMobileAtlas;
        private static Texture2D? _lastTrackedMenuTex;
        private static Texture2D? _lastTrackedCursorsTex;
        private static bool _dirty = true;
        private static bool _atlasItselfModded = false;
        private static bool _pcUiModDetected = false;
        private static Color _themeTextColor = new Color(86, 22, 12);
        private static Color _themeAccentColor = new Color(220, 123, 5);
        private static Color _themeBorderColor = new Color(91, 43, 42);
        private static Color _themeFillColor = new Color(255, 210, 132);
        private static Color _themeCircleTint = Color.White;
        private static int _lastCheckTick = -999;

        public static bool IsAtlasModded => _atlasItselfModded;
        public static bool IsPcUiModDetected => _pcUiModDetected;
        public static Color ThemeTextColor => _themeTextColor;
        public static Color ThemeAccentColor => _themeAccentColor;
        public static Color ThemeBorderColor => _themeBorderColor;
        public static Color ThemeFillColor => _themeFillColor;
        public static Color ThemeCircleTint => _themeCircleTint;

        public static void MarkDirty()
        {
            _dirty = true;
        }

        public static void Dispose()
        {
            try
            {
                if (_themedMobileAtlas != null && !_themedMobileAtlas.IsDisposed)
                    _themedMobileAtlas.Dispose();
            }
            catch { }
            _themedMobileAtlas = null;
            _rawMobileAtlas = null;
            _dirty = true;
        }

        /// <summary>
        /// Safe to call from UpdateTicking (OUTSIDE SpriteBatch / RenderTarget drawing) so Texture2D.GetData()
        /// never switches OpenGL ES framebuffers mid-frame on iOS!
        /// </summary>
        public static void UpdateIfNeeded(GraphicsDevice? gd)
        {
            if (gd == null) return;

            var curMenu = Game1.menuTexture;
            var curCursors = Game1.mouseCursors;
            if (!ReferenceEquals(curMenu, _lastTrackedMenuTex) || !ReferenceEquals(curCursors, _lastTrackedCursorsTex))
            {
                _dirty = true;
            }
            else if (_rawMobileAtlas == null || _rawMobileAtlas.IsDisposed)
            {
                _dirty = true;
            }
            else if (_themedMobileAtlas != null && _themedMobileAtlas.IsDisposed)
            {
                _dirty = true;
            }

            if (_dirty && (Game1.ticks - _lastCheckTick >= 15 || _rawMobileAtlas == null))
            {
                _lastCheckTick = Game1.ticks;
                RebuildIfNeeded(gd);
            }
        }

        /// <summary>
        /// Fast draw-time getter (NEVER invokes Texture2D.GetData during SpriteBatch rendering).
        /// </summary>
        public static Texture2D? GetAtlas(GraphicsDevice? gd)
        {
            if (_themedMobileAtlas != null && !_themedMobileAtlas.IsDisposed)
                return _themedMobileAtlas;
            if (_rawMobileAtlas != null && !_rawMobileAtlas.IsDisposed)
                return _rawMobileAtlas;
            return null;
        }

        private static void RebuildIfNeeded(GraphicsDevice gd)
        {
            _dirty = false;
            _lastTrackedMenuTex = Game1.menuTexture;
            _lastTrackedCursorsTex = Game1.mouseCursors;

            try
            {
                _rawMobileAtlas = Game1.content.Load<Texture2D>("LooseSprites/MobileAtlas_manually_made");
            }
            catch
            {
                try
                {
                    _rawMobileAtlas = Game1.content.Load<Texture2D>("LooseSprites\\MobileAtlas_manually_made");
                }
                catch
                {
                    _rawMobileAtlas = null;
                }
            }

            if (_rawMobileAtlas == null || _rawMobileAtlas.IsDisposed)
                return;

            try
            {
                int rw = Math.Min(AtlasRegionW, _rawMobileAtlas.Width);
                int rh = Math.Min(AtlasRegionH, _rawMobileAtlas.Height);
                Color[] atlasPixels = new Color[rw * rh];
                _rawMobileAtlas.GetData(0, new Rectangle(0, 0, rw, rh), atlasPixels, 0, atlasPixels.Length);

                // 1. Check whether MobileAtlas_manually_made itself was modified by a Mobile UI mod
                _atlasItselfModded = DetectIfMobileAtlasModded(atlasPixels, rw, rh);

                // 2. Check whether Game1.menuTexture (Maps/MenuTiles) was modified by a PC UI mod
                Color[]? modRamp = ExtractUiModColorRamp(Game1.menuTexture, out bool menuModded);
                _pcUiModDetected = menuModded;

                if (modRamp != null && modRamp.Length >= 7)
                {
                    _themeBorderColor = modRamp[0];
                    _themeAccentColor = modRamp[3];
                    _themeFillColor = modRamp[5];
                    _themeTextColor = ComputeReadableTextColor(modRamp[5], modRamp[0]);
                    _themeCircleTint = _atlasItselfModded
                        ? Color.White
                        : Color.Lerp(Color.White, modRamp[5], 0.55f);
                }
                else
                {
                    _themeBorderColor = VanillaWoodRamp[0];
                    _themeAccentColor = VanillaWoodRamp[3];
                    _themeFillColor = VanillaWoodRamp[5];
                    _themeTextColor = new Color(86, 22, 12);
                    _themeCircleTint = Color.White;
                }

                // If MobileAtlas itself was modded OR no PC UI mod is installed -> use raw atlas directly!
                if (_atlasItselfModded || !_pcUiModDetected || modRamp == null)
                {
                    if (_themedMobileAtlas != null && !_themedMobileAtlas.IsDisposed)
                    {
                        try { _themedMobileAtlas.Dispose(); } catch { }
                    }
                    _themedMobileAtlas = null;
                    ModEntry.ModMonitor?.Log(
                        $"[MobileThemeEngine] Theme synced: MobileAtlasModded={_atlasItselfModded}, PcUiMod={_pcUiModDetected} (direct atlas).",
                        LogLevel.Info);
                    return;
                }

                // 3. Auto-palette-map MobileAtlas_manually_made wood/orange/circle pixels to match the PC UI mod!
                Color[] remapped = new Color[atlasPixels.Length];
                for (int y = 0; y < rh; y++)
                {
                    for (int x = 0; x < rw; x++)
                    {
                        int idx = y * rw + x;
                        Color c = atlasPixels[idx];
                        if (c.A == 0)
                        {
                            remapped[idx] = c;
                            continue;
                        }

                        // Special handling for the translucent circular Joystick/Button sprite (0, 116, 37, 37)
                        if (x >= SrcCirclePad.X && x < SrcCirclePad.Right && y >= SrcCirclePad.Y && y < SrcCirclePad.Bottom)
                        {
                            remapped[idx] = RemapCirclePadPixel(c, modRamp);
                            continue;
                        }

                        // Map warm wood / gold / amber / brown UI pixels to the active mod's color ramp
                        if (IsVanillaWoodUiColor(c, out float t))
                        {
                            Color mapped = SampleRamp(modRamp, t);
                            mapped.A = c.A;
                            remapped[idx] = mapped;
                        }
                        else
                        {
                            remapped[idx] = c;
                        }
                    }
                }

                if (_themedMobileAtlas == null || _themedMobileAtlas.IsDisposed || _themedMobileAtlas.Width != rw || _themedMobileAtlas.Height != rh)
                {
                    try { _themedMobileAtlas?.Dispose(); } catch { }
                    _themedMobileAtlas = new Texture2D(gd, rw, rh);
                }
                _themedMobileAtlas.SetData(remapped);

                ModEntry.ModMonitor?.Log(
                    $"[MobileThemeEngine] Auto-themed MobileAtlas to match installed PC UI mod (border={_themeBorderColor}, fill={_themeFillColor}).",
                    LogLevel.Info);
            }
            catch (Exception ex)
            {
                ModEntry.ModMonitor?.Log($"[MobileThemeEngine] Theme rebuild note: {ex.Message}", LogLevel.Trace);
            }
        }

        private static bool DetectIfMobileAtlasModded(Color[] px, int w, int h)
        {
            // Sample known vanilla pixels inside MenuBtn (16,44,16,16) and DayTimeBox (0,153,95,31)
            if (w < 120 || h < 180) return false;
            Color cMenuMid = px[50 * w + 18];     // Vanilla: (220, 123, 5)
            Color cMenuBorder = px[50 * w + 16];  // Vanilla: (91, 43, 42)
            Color cToolbarFill = px[148 * w + 112]; // Vanilla: warm light tan/orange inside ToolbarBox

            bool menuMidMatches = ColorDistSq(cMenuMid, new Color(220, 123, 5)) < 450;
            bool menuBorderMatches = ColorDistSq(cMenuBorder, new Color(91, 43, 42)) < 450;
            bool toolbarWarm = cToolbarFill.A > 200 && cToolbarFill.R > 210 && cToolbarFill.G > 140 && cToolbarFill.B < 170;

            return !(menuMidMatches && menuBorderMatches && toolbarWarm);
        }

        private static Color[]? ExtractUiModColorRamp(Texture2D? menuTex, out bool isModded)
        {
            isModded = false;
            if (menuTex == null || menuTex.IsDisposed || menuTex.Width < 60 || menuTex.Height < 316)
                return null;

            try
            {
                // Standard Stardew UI 9-slice box lives at (0, 256, 60, 60) inside Maps/MenuTiles
                Color[] box = new Color[60 * 60];
                menuTex.GetData(0, new Rectangle(0, 256, 60, 60), box, 0, box.Length);

                // Check if center interior pixel differs from vanilla warm tan ~(255, 210, 132) / (251, 200, 118)
                Color center = box[30 * 60 + 30];
                Color topBorder = box[2 * 60 + 30];
                Color innerEdge = box[10 * 60 + 30];

                bool vanillaCenter = ColorDistSq(center, new Color(255, 210, 132)) < 1600 ||
                                     ColorDistSq(center, new Color(252, 200, 118)) < 1600;
                bool vanillaBorder = ColorDistSq(topBorder, new Color(91, 43, 42)) < 1200 ||
                                     ColorDistSq(topBorder, new Color(133, 54, 5)) < 1200 ||
                                     ColorDistSq(innerEdge, new Color(220, 123, 5)) < 1600;

                isModded = !(vanillaCenter && vanillaBorder);

                // Gather opaque pixels from border ring and interior, bucket by luminance into a smooth 7-stop ramp
                Color[] buckets = new Color[7];
                int[] counts = new int[7];
                long[] sumR = new long[7];
                long[] sumG = new long[7];
                long[] sumB = new long[7];

                float minLum = 1f, maxLum = 0f;
                for (int y = 0; y < 60; y++)
                {
                    for (int x = 0; x < 60; x++)
                    {
                        Color c = box[y * 60 + x];
                        if (c.A < 200) continue;
                        float lum = Luminance(c);
                        if (lum < minLum) minLum = lum;
                        if (lum > maxLum) maxLum = lum;
                    }
                }
                float span = Math.Max(0.08f, maxLum - minLum);

                for (int y = 0; y < 60; y++)
                {
                    for (int x = 0; x < 60; x++)
                    {
                        Color c = box[y * 60 + x];
                        if (c.A < 200) continue;
                        float norm = Math.Clamp((Luminance(c) - minLum) / span, 0f, 0.999f);
                        int b = (int)(norm * 7f);
                        sumR[b] += c.R;
                        sumG[b] += c.G;
                        sumB[b] += c.B;
                        counts[b]++;
                    }
                }

                // Fill any empty buckets by interpolating neighbors
                for (int i = 0; i < 7; i++)
                {
                    if (counts[i] > 0)
                    {
                        buckets[i] = new Color((int)(sumR[i] / counts[i]), (int)(sumG[i] / counts[i]), (int)(sumB[i] / counts[i]), 255);
                    }
                }

                // Ensure valid endpoints
                if (counts[0] == 0) buckets[0] = topBorder.A > 180 ? topBorder : new Color(75, 55, 55);
                if (counts[6] == 0) buckets[6] = center.A > 180 ? center : new Color(240, 230, 220);
                for (int i = 1; i < 6; i++)
                {
                    if (counts[i] == 0)
                    {
                        buckets[i] = Color.Lerp(buckets[0], buckets[6], i / 6f);
                    }
                }

                // Make sure bucket[5] reflects the actual interior box fill cleanly
                if (center.A > 200)
                {
                    buckets[5] = Color.Lerp(buckets[5], center, 0.7f);
                    buckets[6] = Color.Lerp(center, Color.White, 0.22f);
                }

                return buckets;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsVanillaWoodUiColor(Color c, out float t)
        {
            t = 0f;
            // Warm Stardew UI wood/gold/brown/cream tones: R >= G >= B (with tolerance for dark outline (91,43,42))
            if (c.R < c.B + 6) return false;
            if (c.G > c.R + 10) return false;

            // Skip vivid reds (hearts/close X) or vivid greens (checkmarks), while keeping dark-orange wood bevels like (177, 78, 5)
            if (c.R > 165 && c.G < 52 && c.B < 52) return false;
            if (c.G > 135 && c.R < 100) return false;

            // Check distance to closest stop on vanilla wood ramp
            float minDist = float.MaxValue;
            int bestIdx = 0;
            for (int i = 0; i < VanillaWoodRamp.Length; i++)
            {
                float d = ColorDistSq(c, VanillaWoodRamp[i]);
                if (d < minDist)
                {
                    minDist = d;
                    bestIdx = i;
                }
            }

            if (minDist > 7500f) return false;

            // Map smoothly using normalized luminance within vanilla wood luminance range
            float lum = Luminance(c);
            float minL = Luminance(VanillaWoodRamp[0]);
            float maxL = Luminance(VanillaWoodRamp[6]);
            t = Math.Clamp((lum - minL) / Math.Max(0.05f, maxL - minL), 0f, 1f);
            // Blend slightly with nearest stop index to keep border crispness
            t = t * 0.7f + (bestIdx / 6f) * 0.3f;
            return true;
        }

        private static Color RemapCirclePadPixel(Color c, Color[] ramp)
        {
            float lum = Luminance(c);
            // Outer ring is darker, inner disc is lighter translucent
            Color mapped = lum < 0.28f
                ? Color.Lerp(ramp[1], ramp[3], 0.5f)
                : Color.Lerp(ramp[4], ramp[6], 0.65f);
            mapped.A = c.A;
            return mapped;
        }

        private static Color SampleRamp(Color[] ramp, float t)
        {
            float scaled = Math.Clamp(t, 0f, 1f) * (ramp.Length - 1);
            int i0 = (int)Math.Floor(scaled);
            int i1 = Math.Min(ramp.Length - 1, i0 + 1);
            float frac = scaled - i0;
            return Color.Lerp(ramp[i0], ramp[i1], frac);
        }

        private static float Luminance(Color c)
        {
            return (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;
        }

        private static int ColorDistSq(Color a, Color b)
        {
            int dr = a.R - b.R;
            int dg = a.G - b.G;
            int db = a.B - b.B;
            return dr * dr + dg * dg + db * db;
        }

        private static Color ComputeReadableTextColor(Color bg, Color border)
        {
            float bgLum = Luminance(bg);
            if (bgLum < 0.38f)
                return new Color(245, 240, 230); // Light text for dark UI mods (e.g. Dark UI / Starry Night)
            return Color.Lerp(border, new Color(40, 18, 12), 0.35f);
        }

        /// <summary>
        /// Draws a genuine Stardew Valley framed menu box using Game1.menuTexture (automatically inherits ALL UI mods!).
        /// </summary>
        public static void DrawMenuBox(SpriteBatch sb, Rectangle rect, float alpha = 1f, bool drawShadow = false)
        {
            if (rect.Width < 16 || rect.Height < 16) return;
            try
            {
                if (Game1.menuTexture != null && !Game1.menuTexture.IsDisposed)
                {
                    IClickableMenu.drawTextureBox(
                        sb,
                        Game1.menuTexture,
                        new Rectangle(0, 256, 60, 60),
                        rect.X,
                        rect.Y,
                        rect.Width,
                        rect.Height,
                        Color.White * alpha,
                        1f,
                        drawShadow
                    );
                    return;
                }
            }
            catch { }
        }

        /// <summary>
        /// Draws a small themed wooden button box using Game1.mouseCursors (automatically inherits ALL UI mods!).
        /// </summary>
        public static void DrawCursorBox(SpriteBatch sb, Rectangle rect, float alpha = 1f, bool pressed = false)
        {
            if (rect.Width < 12 || rect.Height < 12) return;
            try
            {
                if (Game1.mouseCursors != null && !Game1.mouseCursors.IsDisposed)
                {
                    Color tint = pressed ? new Color(215, 215, 215) * alpha : Color.White * alpha;
                    IClickableMenu.drawTextureBox(
                        sb,
                        Game1.mouseCursors,
                        new Rectangle(384, 396, 15, 15),
                        rect.X,
                        rect.Y,
                        rect.Width,
                        rect.Height,
                        tint,
                        3f,
                        false
                    );
                }
            }
            catch { }
        }
    }
}
