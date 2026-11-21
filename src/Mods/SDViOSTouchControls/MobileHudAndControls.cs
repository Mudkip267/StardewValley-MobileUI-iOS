using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;
using StardewValley;
using StardewValley.Menus;

namespace SDViOSTouchControls
{
    /// <summary>
    /// Official Stardew Valley Mobile iOS HUD, Virtual Controls, and Customize Bar:
    /// - Vertical scrollable Left Toolbar (auto-themed via MobileThemeEngine)
    /// - Compact Top-Right Day / Time / Money HUD + [!] Quest + [≡] Menu buttons
    /// - Top-Left [+ .] Joystick Toggle button (tap = toggle Joystick+X/A, hold = Mobile Settings)
    /// - Translucent pixel-art circular Joystick + 2 buttons (X Use, A Check)
    /// - Official Mobile Customize Controls Top Bar (Select control, Diameter slider, Default / X / OK + Drag & Drop)
    /// </summary>
    public class MobileHudAndControls
    {
        public static MobileHudAndControls Instance { get; } = new MobileHudAndControls();

        // Runtime mode flags
        public bool IsCustomizeBarOpen { get; private set; } = false;
        public bool IsMobileSettingsOpen { get; private set; } = false;

        // Exposed virtual inputs consumed by TouchVirtualPad
        public Vector2 JoystickVector { get; private set; } = Vector2.Zero;
        public bool ButtonXPressed { get; private set; } = false;
        public bool ButtonAPressed { get; private set; } = false;

        // Vertical Toolbar state
        private Rectangle _toolbarRect;
        private float _toolbarScrollY = 0f;
        private float _toolbarVelocityY = 0f;
        private int _toolbarTouchId = -1;
        private float _toolbarTouchStartY = 0f;
        private float _toolbarTouchLastY = 0f;
        private bool _toolbarDragged = false;
        private int _toolbarSlotSize = 72;

        // Top-right HUD rectangles
        private Rectangle _dayTimeBoxRect;
        private Rectangle _questBtnRect;
        private Rectangle _menuBtnRect;

        // Top-left Toggle Joystick button (+ .)
        private Rectangle _toggleJoyBtnRect;
        private int _toggleTouchId = -1;
        private double _togglePressStartTime = 0;
        private bool _toggleLongPressFired = false;

        // Mobile circular Joystick + X + A geometry
        private Vector2 _joyCenter;
        private float _joyRadius = 96f;
        private Vector2 _joyThumbPos;
        private Vector2 _btnXCenter;
        private float _btnXRadius = 56f;
        private Vector2 _btnACenter;
        private float _btnARadius = 56f;

        private int _joyTouchId = -1;
        private int _btnXTouchId = -1;
        private int _btnATouchId = -1;

        // Customize Bar state (matches official Stardew Mobile top bar)
        private int _customSelectedTarget = 0; // 0 = Joystick (ĐI), 1 = Button X (SỬ DỤNG), 2 = Button A (KIỂM TRA)
        private Rectangle _customBarRect;
        private Rectangle _customTabJoyRect;
        private Rectangle _customTabXRect;
        private Rectangle _customTabARect;
        private Rectangle _customSliderLeftBtn;
        private Rectangle _customSliderRightBtn;
        private Rectangle _customSliderTrackRect;
        private Rectangle _customDefaultBtn;
        private Rectangle _customCancelBtn;
        private Rectangle _customOkBtn;
        private int _customDragTouchId = -1;
        private int _customDragMode = 0; // 1=slider, 2=joy, 3=btnX, 4=btnA

        // Backup values for Cancel in Customize Bar
        private float _bkJoyDiam, _bkXDiam, _bkADiam;
        private float _bkJoyX, _bkJoyY, _bkXX, _bkXY, _bkAX, _bkAY;

        // Mobile Settings Modal layout
        private Rectangle _settingsBoxRect;
        private Rectangle _settingsCloseBtn;
        private readonly Rectangle[] _settingsRowBtns = new Rectangle[9];

        private int _vpW = 1792;
        private int _vpH = 828;

        public bool IsMobileJoystickShown(TouchOverlaySettings s)
        {
            if (IsCustomizeBarOpen) return true;
            return s.ControlScheme == MobileControlScheme.JoystickAndButtons
                || s.ControlScheme == MobileControlScheme.TapToMoveAndJoystickAndButtons
                || s.MobileJoystickToggleOn;
        }

        public bool IsWorldHudActive(TouchOverlaySettings s)
        {
            return s.MobileUIEnabled
                && s.ControlScheme != MobileControlScheme.LegacyXboxPad
                && Game1.gameMode == 3
                && Game1.player != null
                && Game1.displayHUD
                && !Game1.eventUp
                && Game1.activeClickableMenu == null
                && Game1.currentMinigame == null;
        }

        public void ResetTransientState()
        {
            JoystickVector = Vector2.Zero;
            ButtonXPressed = false;
            ButtonAPressed = false;
            _joyTouchId = -1;
            _btnXTouchId = -1;
            _btnATouchId = -1;
            _toolbarTouchId = -1;
            _toggleTouchId = -1;
            _customDragTouchId = -1;
            _customDragMode = 0;
        }

        public void OpenCustomizeBar(TouchOverlaySettings s)
        {
            IsMobileSettingsOpen = false;
            IsCustomizeBarOpen = true;
            _customSelectedTarget = 0;
            _bkJoyDiam = s.MobileJoystickDiameter;
            _bkXDiam = s.MobileBtnXDiameter;
            _bkADiam = s.MobileBtnADiameter;
            _bkJoyX = s.JoystickNormX;
            _bkJoyY = s.JoystickNormY;
            _bkXX = s.MobileBtnXNormX;
            _bkXY = s.MobileBtnXNormY;
            _bkAX = s.MobileBtnANormX;
            _bkAY = s.MobileBtnANormY;
            UpdateLayout(_vpW, _vpH, s);
        }

        public void OpenMobileSettings(TouchOverlaySettings s)
        {
            IsCustomizeBarOpen = false;
            IsMobileSettingsOpen = true;
            UpdateLayout(_vpW, _vpH, s);
        }

        public void CloseModals()
        {
            IsCustomizeBarOpen = false;
            IsMobileSettingsOpen = false;
            _customDragTouchId = -1;
            _customDragMode = 0;
        }

        public void UpdateLayout(int width, int height, TouchOverlaySettings s)
        {
            if (width <= 0) width = 1792;
            if (height <= 0) height = 828;
            _vpW = width;
            _vpH = height;

            float scale = Math.Clamp(height / 720f, 0.75f, 1.85f);

            // 1. Vertical Left Toolbar (exact Stardew Mobile proportion)
            _toolbarSlotSize = Math.Clamp((int)Math.Round(s.MobileToolbarSlotSize * scale), 52, 116);
            int tbWidth = _toolbarSlotSize + 8;
            _toolbarRect = new Rectangle(0, 0, tbWidth, height);

            // 2. Top-Left [+ .] Joystick Toggle button right next to vertical toolbar
            int toggleSize = Math.Clamp((int)Math.Round(52f * scale), 42, 74);
            _toggleJoyBtnRect = new Rectangle(_toolbarRect.Right + (int)(12 * scale), (int)(12 * scale), toggleSize, toggleSize);

            // 3. Top-Right Compact Mobile Day/Time/Money HUD (95x31 aspect ratio)
            int hudPixelScale = Math.Max(3, (int)Math.Round(3.4f * scale));
            int dtW = MobileThemeEngine.SrcDayTimeBox.Width * hudPixelScale;
            int dtH = MobileThemeEngine.SrcDayTimeBox.Height * hudPixelScale;
            int rightMargin = (int)(10 * scale);
            int topMargin = (int)(10 * scale);
            _dayTimeBoxRect = new Rectangle(width - dtW - rightMargin, topMargin, dtW, dtH);

            // [!] Quest button and [≡] Menu button stacked neatly below top-right money box
            int sideBtnSize = Math.Clamp((int)Math.Round(56f * scale), 44, 78);
            int sideBtnX = width - sideBtnSize - rightMargin;
            int questY = _dayTimeBoxRect.Bottom + (int)(10 * scale);
            _questBtnRect = new Rectangle(sideBtnX, questY, sideBtnSize, sideBtnSize);
            _menuBtnRect = new Rectangle(sideBtnX, _questBtnRect.Bottom + (int)(10 * scale), sideBtnSize, sideBtnSize);

            // 4. Translucent Circular Joystick + 2 Buttons (X = Use, A = Check)
            _joyRadius = Math.Clamp(s.MobileJoystickDiameter * 0.5f * scale, 48f, 190f);
            _btnXRadius = Math.Clamp(s.MobileBtnXDiameter * 0.5f * scale, 32f, 140f);
            _btnARadius = Math.Clamp(s.MobileBtnADiameter * 0.5f * scale, 32f, 140f);

            _joyCenter = new Vector2(
                Math.Clamp(s.JoystickNormX * width, _toolbarRect.Right + _joyRadius + 8, width - _joyRadius - 8),
                Math.Clamp(s.JoystickNormY * height, _joyRadius + 8, height - _joyRadius - 8)
            );
            _btnXCenter = new Vector2(
                Math.Clamp(s.MobileBtnXNormX * width, _toolbarRect.Right + _btnXRadius + 8, width - _btnXRadius - 8),
                Math.Clamp(s.MobileBtnXNormY * height, _btnXRadius + 8, height - _btnXRadius - 8)
            );
            _btnACenter = new Vector2(
                Math.Clamp(s.MobileBtnANormX * width, _toolbarRect.Right + _btnARadius + 8, width - _btnARadius - 8),
                Math.Clamp(s.MobileBtnANormY * height, _btnARadius + 8, height - _btnARadius - 8)
            );

            if (_joyTouchId == -1)
                _joyThumbPos = _joyCenter;

            // 5. Official Mobile Customize Controls Top Bar layout (matching Image 2!)
            int barMarginX = Math.Max(20, (int)(32 * scale));
            int barW = width - barMarginX * 2;
            int barH = Math.Clamp((int)(190 * scale), 150, 235);
            _customBarRect = new Rectangle(barMarginX, (int)(8 * scale), barW, barH);

            int pad = (int)(16 * scale);
            int tabSize = Math.Clamp((int)(56 * scale), 44, 72);
            int tabY = _customBarRect.Y + pad;
            _customTabJoyRect = new Rectangle(_customBarRect.X + pad, tabY, tabSize, tabSize);
            _customTabXRect = new Rectangle(_customTabJoyRect.Right + (int)(8 * scale), tabY, tabSize, tabSize);
            _customTabARect = new Rectangle(_customTabXRect.Right + (int)(8 * scale), tabY, tabSize, tabSize);

            int iconBtnSize = Math.Clamp((int)(52 * scale), 42, 68);
            _customCancelBtn = new Rectangle(_customBarRect.Right - pad - iconBtnSize, tabY, iconBtnSize, iconBtnSize);
            _customOkBtn = new Rectangle(_customCancelBtn.X, _customCancelBtn.Bottom + (int)(10 * scale), iconBtnSize, iconBtnSize);
            int defW = Math.Clamp((int)(148 * scale), 110, 185);
            _customDefaultBtn = new Rectangle(_customCancelBtn.X - (int)(16 * scale) - defW, tabY + (int)(4 * scale), defW, (int)(44 * scale));

            int sliderY = _customBarRect.Bottom - pad - iconBtnSize;
            int arrowW = Math.Clamp((int)(48 * scale), 38, 62);
            _customSliderLeftBtn = new Rectangle(_customBarRect.X + pad, sliderY, arrowW, iconBtnSize);
            _customSliderRightBtn = new Rectangle(_customOkBtn.X - (int)(18 * scale) - arrowW, sliderY, arrowW, iconBtnSize);
            _customSliderTrackRect = new Rectangle(
                _customSliderLeftBtn.Right + (int)(12 * scale),
                sliderY + (int)(12 * scale),
                Math.Max(80, _customSliderRightBtn.Left - _customSliderLeftBtn.Right - (int)(24 * scale)),
                Math.Max(20, iconBtnSize - (int)(24 * scale))
            );

            // 6. Mobile Settings Modal layout
            int mw = Math.Min((int)(620 * scale), width - 48);
            int mh = Math.Min((int)(560 * scale), height - 36);
            _settingsBoxRect = new Rectangle((width - mw) / 2, (height - mh) / 2, mw, mh);
            _settingsCloseBtn = new Rectangle(_settingsBoxRect.Right - (int)(48 * scale), _settingsBoxRect.Y + (int)(12 * scale), (int)(36 * scale), (int)(36 * scale));

            int rowX = _settingsBoxRect.X + (int)(24 * scale);
            int rowW = mw - (int)(48 * scale);
            int rowStartY = _settingsBoxRect.Y + (int)(62 * scale);
            int rowH = Math.Clamp((mh - (int)(84 * scale)) / _settingsRowBtns.Length - 6, 34, 52);
            int rowStep = rowH + 6;
            for (int i = 0; i < _settingsRowBtns.Length; i++)
            {
                _settingsRowBtns[i] = new Rectangle(rowX, rowStartY + i * rowStep, rowW, rowH);
            }
        }

        /// <summary>
        /// Processes all touches when Mobile UI is active.
        /// Consumes touches hitting Mobile HUD / Joystick / X / A / Customize Bar / Settings Modal,
        /// and passes remaining open-world touches to MobileTapAndAutoController.
        /// Returns true if Mobile Controls handled the frame (so Legacy Xbox Pad should skip).
        /// </summary>
        public bool UpdateTouches(
            IReadOnlyList<TouchLocation> touches,
            Func<Vector2, Point> mapTouchToViewport,
            GameTime gameTime,
            TouchOverlaySettings s)
        {
            if (s.ControlScheme == MobileControlScheme.LegacyXboxPad || !s.MobileUIEnabled)
            {
                ResetTransientState();
                return false;
            }

            double curTime = gameTime.TotalGameTime.TotalSeconds;
            ButtonXPressed = false;
            ButtonAPressed = false;

            // Smooth toolbar inertia scrolling
            if (_toolbarTouchId == -1 && Math.Abs(_toolbarVelocityY) > 0.25f)
            {
                _toolbarScrollY += _toolbarVelocityY;
                _toolbarVelocityY *= 0.88f;
                ClampToolbarScroll();
            }

            // 1. Customize Controls Top Bar active -> consumes all touches
            if (IsCustomizeBarOpen)
            {
                JoystickVector = Vector2.Zero;
                ProcessCustomizeBarTouches(touches, mapTouchToViewport, s);
                return true;
            }

            // 2. Mobile Settings Modal active -> consumes all touches
            if (IsMobileSettingsOpen)
            {
                JoystickVector = Vector2.Zero;
                ProcessMobileSettingsTouches(touches, mapTouchToViewport, s);
                return true;
            }

            // If we're not in open world gameplay (e.g. Title screen or an in-game Menu/Dialog is open),
            // let TouchVirtualPad handle menu taps/point-and-click normally!
            if (!IsWorldHudActive(s))
            {
                ResetTransientState();
                MobileTapAndAutoController.Instance.CancelAll();
                return false;
            }

            // 3. Active Gameplay: classify touches into HUD / Virtual Pad / Open World
            bool showJoy = IsMobileJoystickShown(s);
            bool joyPresent = false;
            bool btnXPresent = false;
            bool btnAPresent = false;
            bool tbPresent = false;
            bool togglePresent = false;

            List<TouchLocation> worldTouches = new List<TouchLocation>(4);

            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                Point pt = mapTouchToViewport(touch.Position);
                Vector2 ptVec = new Vector2(pt.X, pt.Y);

                // 3a. Continue active toolbar scroll touch
                if (touch.Id == _toolbarTouchId)
                {
                    tbPresent = true;
                    HandleToolbarTouch(touch, pt);
                    continue;
                }

                // 3b. Continue active [+ .] toggle touch (tap vs long-press)
                if (touch.Id == _toggleTouchId)
                {
                    togglePresent = true;
                    if (touch.State == TouchLocationState.Released)
                    {
                        if (!_toggleLongPressFired && _toggleJoyBtnRect.Contains(pt))
                        {
                            s.MobileJoystickToggleOn = !s.MobileJoystickToggleOn;
                            s.Save();
                            try { Game1.playSound("smallSelect"); } catch { }
                        }
                        _toggleTouchId = -1;
                        _toggleLongPressFired = false;
                    }
                    else if (!_toggleLongPressFired && (curTime - _togglePressStartTime) >= 0.55)
                    {
                        _toggleLongPressFired = true;
                        _toggleTouchId = -1;
                        OpenMobileSettings(s);
                        try { Game1.playSound("bigSelect"); } catch { }
                    }
                    continue;
                }

                // 3c. Continue active Joystick touch
                if (showJoy && touch.Id == _joyTouchId)
                {
                    joyPresent = true;
                    if (touch.State == TouchLocationState.Released)
                    {
                        _joyTouchId = -1;
                        _joyThumbPos = _joyCenter;
                        JoystickVector = Vector2.Zero;
                    }
                    else
                    {
                        UpdateJoystickDrag(ptVec);
                        MobileTapAndAutoController.Instance.CancelAll();
                    }
                    continue;
                }

                // 3d. Continue active Button X touch
                if (showJoy && touch.Id == _btnXTouchId)
                {
                    btnXPresent = true;
                    if (touch.State == TouchLocationState.Released)
                    {
                        _btnXTouchId = -1;
                    }
                    else
                    {
                        ButtonXPressed = true;
                    }
                    continue;
                }

                // 3e. Continue active Button A touch
                if (showJoy && touch.Id == _btnATouchId)
                {
                    btnAPresent = true;
                    if (touch.State == TouchLocationState.Released)
                    {
                        _btnATouchId = -1;
                    }
                    else
                    {
                        ButtonAPressed = true;
                    }
                    continue;
                }

                // 3f. Newly pressed touch -> hit test Mobile HUD & Controls first!
                if (touch.State == TouchLocationState.Pressed)
                {
                    // Vertical Left Toolbar
                    if (_toolbarRect.Contains(pt))
                    {
                        _toolbarTouchId = touch.Id;
                        tbPresent = true;
                        HandleToolbarTouch(touch, pt);
                        continue;
                    }

                    // Top-Left [+ .] Toggle Button
                    if (s.ShowMobileToggleButton && _toggleJoyBtnRect.Contains(pt))
                    {
                        _toggleTouchId = touch.Id;
                        _togglePressStartTime = curTime;
                        _toggleLongPressFired = false;
                        togglePresent = true;
                        continue;
                    }

                    // Top-Right [!] Quest Button
                    if (_questBtnRect.Contains(pt))
                    {
                        MobileTapAndAutoController.Instance.CancelAll();
                        try
                        {
                            Game1.activeClickableMenu = new QuestLog();
                            Game1.playSound("bigSelect");
                        }
                        catch { }
                        continue;
                    }

                    // Top-Right [≡] Menu Button
                    if (_menuBtnRect.Contains(pt))
                    {
                        MobileTapAndAutoController.Instance.CancelAll();
                        try
                        {
                            TouchVirtualPad.Instance.PlayerRequestedMenuOpen = true;
                            Game1.activeClickableMenu = new GameMenu();
                            Game1.playSound("bigSelect");
                        }
                        catch { }
                        continue;
                    }

                    // Top-Right Day/Time/Money box (absorbs accidental taps; tapping it does not walk)
                    if (_dayTimeBoxRect.Contains(pt))
                    {
                        continue;
                    }

                    // Circular Joystick + X + A buttons (when shown)
                    if (showJoy)
                    {
                        if (Vector2.Distance(ptVec, _joyCenter) <= _joyRadius * 1.15f && _joyTouchId == -1)
                        {
                            _joyTouchId = touch.Id;
                            joyPresent = true;
                            UpdateJoystickDrag(ptVec);
                            MobileTapAndAutoController.Instance.CancelAll();
                            continue;
                        }
                        if (Vector2.Distance(ptVec, _btnXCenter) <= _btnXRadius * 1.15f && _btnXTouchId == -1)
                        {
                            _btnXTouchId = touch.Id;
                            btnXPresent = true;
                            ButtonXPressed = true;
                            MobileTapAndAutoController.Instance.CancelAll();
                            continue;
                        }
                        if (Vector2.Distance(ptVec, _btnACenter) <= _btnARadius * 1.15f && _btnATouchId == -1)
                        {
                            _btnATouchId = touch.Id;
                            btnAPresent = true;
                            ButtonAPressed = true;
                            MobileTapAndAutoController.Instance.CancelAll();
                            continue;
                        }
                    }
                }

                // Otherwise it's an open-world touch (Tap-to-Move / Hold-to-Walk / Pinch-to-Zoom)
                worldTouches.Add(touch);
            }

            // Cleanup released/lost touches
            if (!joyPresent) { _joyTouchId = -1; _joyThumbPos = _joyCenter; JoystickVector = Vector2.Zero; }
            if (!btnXPresent) { _btnXTouchId = -1; }
            if (!btnAPresent) { _btnATouchId = -1; }
            if (!tbPresent) { _toolbarTouchId = -1; }
            if (!togglePresent) { _toggleTouchId = -1; _toggleLongPressFired = false; }

            // Process open-world Tap-to-Move, Hold-to-Walk, Pinch-to-Zoom
            MobileTapAndAutoController.Instance.ProcessWorldTouches(
                worldTouches, mapTouchToViewport, _vpW, _vpH, curTime, s);

            // Update Auto-Attack & continuous Auto-Tool swings
            MobileTapAndAutoController.Instance.UpdateTick(s);

            return true;
        }

        private void UpdateJoystickDrag(Vector2 ptVec)
        {
            Vector2 delta = ptVec - _joyCenter;
            float dist = delta.Length();
            if (dist > _joyRadius)
            {
                delta.Normalize();
                _joyThumbPos = _joyCenter + delta * _joyRadius;
                JoystickVector = delta;
            }
            else if (dist > 0.01f)
            {
                _joyThumbPos = ptVec;
                JoystickVector = delta / _joyRadius;
            }
            else
            {
                _joyThumbPos = _joyCenter;
                JoystickVector = Vector2.Zero;
            }
        }

        private void HandleToolbarTouch(TouchLocation touch, Point pt)
        {
            if (touch.State == TouchLocationState.Pressed)
            {
                _toolbarTouchStartY = pt.Y;
                _toolbarTouchLastY = pt.Y;
                _toolbarDragged = false;
                _toolbarVelocityY = 0f;
            }
            else if (touch.State == TouchLocationState.Moved)
            {
                float dy = pt.Y - _toolbarTouchLastY;
                _toolbarTouchLastY = pt.Y;
                if (Math.Abs(pt.Y - _toolbarTouchStartY) > 12f)
                {
                    _toolbarDragged = true;
                }
                if (_toolbarDragged)
                {
                    _toolbarScrollY -= dy;
                    _toolbarVelocityY = -dy * 0.65f;
                    ClampToolbarScroll();
                }
            }
            else if (touch.State == TouchLocationState.Released)
            {
                if (!_toolbarDragged)
                {
                    // Crisp tap on a toolbar slot!
                    int slotIdx = (int)Math.Floor((pt.Y + _toolbarScrollY) / _toolbarSlotSize);
                    var player = Game1.player;
                    if (player != null && slotIdx >= 0 && slotIdx < player.MaxItems)
                    {
                        if (player.CurrentToolIndex == slotIdx && player.ActiveObject != null && player.ActiveObject.Edibility != -300)
                        {
                            // Tapping already-selected edible item prompts to eat (just like Mobile!)
                            try { Game1.pressActionButton(Game1.GetKeyboardState(), Game1.input.GetMouseState(), Game1.input.GetGamePadState()); } catch { }
                        }
                        else
                        {
                            player.CurrentToolIndex = slotIdx;
                            try { Game1.playSound("stoneStep"); } catch { }
                        }
                    }
                }
                _toolbarTouchId = -1;
            }
        }

        private void ClampToolbarScroll()
        {
            int maxItems = Game1.player?.MaxItems ?? 12;
            float totalH = maxItems * _toolbarSlotSize;
            float maxScroll = Math.Max(0f, totalH - _vpH);
            _toolbarScrollY = Math.Clamp(_toolbarScrollY, 0f, maxScroll);
        }

        private void ProcessCustomizeBarTouches(IReadOnlyList<TouchLocation> touches, Func<Vector2, Point> mapTouch, TouchOverlaySettings s)
        {
            bool activePresent = false;
            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                Point pt = mapTouch(touch.Position);
                Vector2 ptVec = new Vector2(pt.X, pt.Y);

                if (touch.Id == _customDragTouchId)
                {
                    activePresent = true;
                    if (touch.State == TouchLocationState.Released)
                    {
                        _customDragTouchId = -1;
                        _customDragMode = 0;
                    }
                    else
                    {
                        ApplyCustomizeDrag(ptVec, s);
                    }
                    continue;
                }

                if (touch.State != TouchLocationState.Pressed) continue;

                // Top Bar Tabs: ĐI (0), SỬ DỤNG (1), KIỂM TRA (2)
                if (_customTabJoyRect.Contains(pt)) { _customSelectedTarget = 0; PlayClick(); continue; }
                if (_customTabXRect.Contains(pt)) { _customSelectedTarget = 1; PlayClick(); continue; }
                if (_customTabARect.Contains(pt)) { _customSelectedTarget = 2; PlayClick(); continue; }

                // Slider Arrows
                if (_customSliderLeftBtn.Contains(pt)) { AdjustSelectedDiameter(s, -12f); PlayClick(); continue; }
                if (_customSliderRightBtn.Contains(pt)) { AdjustSelectedDiameter(s, +12f); PlayClick(); continue; }

                // Slider Track drag
                Rectangle sliderHit = new Rectangle(_customSliderTrackRect.X - 12, _customSliderTrackRect.Y - 18, _customSliderTrackRect.Width + 24, _customSliderTrackRect.Height + 36);
                if (sliderHit.Contains(pt))
                {
                    _customDragTouchId = touch.Id;
                    _customDragMode = 1;
                    activePresent = true;
                    ApplyCustomizeDrag(ptVec, s);
                    continue;
                }

                // Default / Cancel / OK buttons
                if (_customDefaultBtn.Contains(pt))
                {
                    s.ResetMobileControlLayoutDefaults();
                    UpdateLayout(_vpW, _vpH, s);
                    PlayClick();
                    continue;
                }
                if (_customCancelBtn.Contains(pt))
                {
                    s.MobileJoystickDiameter = _bkJoyDiam;
                    s.MobileBtnXDiameter = _bkXDiam;
                    s.MobileBtnADiameter = _bkADiam;
                    s.JoystickNormX = _bkJoyX;
                    s.JoystickNormY = _bkJoyY;
                    s.MobileBtnXNormX = _bkXX;
                    s.MobileBtnXNormY = _bkXY;
                    s.MobileBtnANormX = _bkAX;
                    s.MobileBtnANormY = _bkAY;
                    UpdateLayout(_vpW, _vpH, s);
                    IsCustomizeBarOpen = false;
                    PlayClick();
                    continue;
                }
                if (_customOkBtn.Contains(pt))
                {
                    s.Save();
                    IsCustomizeBarOpen = false;
                    try { Game1.playSound("coin"); } catch { }
                    continue;
                }

                // Drag on-screen Joystick, Button X, or Button A directly!
                if (!_customBarRect.Contains(pt))
                {
                    if (Vector2.Distance(ptVec, _joyCenter) <= _joyRadius * 1.2f)
                    {
                        _customSelectedTarget = 0;
                        _customDragTouchId = touch.Id;
                        _customDragMode = 2;
                        activePresent = true;
                    }
                    else if (Vector2.Distance(ptVec, _btnXCenter) <= _btnXRadius * 1.2f)
                    {
                        _customSelectedTarget = 1;
                        _customDragTouchId = touch.Id;
                        _customDragMode = 3;
                        activePresent = true;
                    }
                    else if (Vector2.Distance(ptVec, _btnACenter) <= _btnARadius * 1.2f)
                    {
                        _customSelectedTarget = 2;
                        _customDragTouchId = touch.Id;
                        _customDragMode = 4;
                        activePresent = true;
                    }
                }
            }

            if (!activePresent)
            {
                _customDragTouchId = -1;
                _customDragMode = 0;
            }
        }

        private void ApplyCustomizeDrag(Vector2 ptVec, TouchOverlaySettings s)
        {
            if (_customDragMode == 1) // Slider
            {
                float norm = Math.Clamp((ptVec.X - _customSliderTrackRect.X) / Math.Max(1f, _customSliderTrackRect.Width), 0f, 1f);
                float minD = _customSelectedTarget == 0 ? 120f : 80f;
                float maxD = _customSelectedTarget == 0 ? 340f : 240f;
                float val = minD + norm * (maxD - minD);
                SetSelectedDiameter(s, val);
                UpdateLayout(_vpW, _vpH, s);
            }
            else if (_customDragMode == 2) // Joystick position
            {
                s.JoystickNormX = Math.Clamp(ptVec.X / _vpW, 0.08f, 0.92f);
                s.JoystickNormY = Math.Clamp(ptVec.Y / _vpH, 0.28f, 0.94f);
                UpdateLayout(_vpW, _vpH, s);
            }
            else if (_customDragMode == 3) // Button X position
            {
                s.MobileBtnXNormX = Math.Clamp(ptVec.X / _vpW, 0.08f, 0.94f);
                s.MobileBtnXNormY = Math.Clamp(ptVec.Y / _vpH, 0.28f, 0.94f);
                UpdateLayout(_vpW, _vpH, s);
            }
            else if (_customDragMode == 4) // Button A position
            {
                s.MobileBtnANormX = Math.Clamp(ptVec.X / _vpW, 0.08f, 0.94f);
                s.MobileBtnANormY = Math.Clamp(ptVec.Y / _vpH, 0.28f, 0.94f);
                UpdateLayout(_vpW, _vpH, s);
            }
        }

        private float GetSelectedDiameter(TouchOverlaySettings s) => _customSelectedTarget switch
        {
            0 => s.MobileJoystickDiameter,
            1 => s.MobileBtnXDiameter,
            _ => s.MobileBtnADiameter
        };

        private void SetSelectedDiameter(TouchOverlaySettings s, float val)
        {
            if (_customSelectedTarget == 0) s.MobileJoystickDiameter = Math.Clamp(val, 120f, 340f);
            else if (_customSelectedTarget == 1) s.MobileBtnXDiameter = Math.Clamp(val, 80f, 240f);
            else s.MobileBtnADiameter = Math.Clamp(val, 80f, 240f);
        }

        private void AdjustSelectedDiameter(TouchOverlaySettings s, float delta)
        {
            SetSelectedDiameter(s, GetSelectedDiameter(s) + delta);
            UpdateLayout(_vpW, _vpH, s);
        }

        private void ProcessMobileSettingsTouches(IReadOnlyList<TouchLocation> touches, Func<Vector2, Point> mapTouch, TouchOverlaySettings s)
        {
            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                if (touch.State != TouchLocationState.Pressed) continue;
                Point pt = mapTouch(touch.Position);

                if (_settingsCloseBtn.Contains(pt) || !_settingsBoxRect.Contains(pt))
                {
                    IsMobileSettingsOpen = false;
                    s.Save();
                    PlayClick();
                    return;
                }

                // Row 0: Control Scheme cycle
                if (_settingsRowBtns[0].Contains(pt))
                {
                    s.ControlScheme = (MobileControlScheme)(((int)s.ControlScheme + 1) % 4);
                    if (s.ControlScheme == MobileControlScheme.JoystickAndButtons) s.MobileJoystickToggleOn = true;
                    else if (s.ControlScheme == MobileControlScheme.TapToMoveAndAutoAttack) s.MobileJoystickToggleOn = false;
                    s.Save();
                    PlayClick();
                }
                // Row 1: Auto-Tool toggle
                else if (_settingsRowBtns[1].Contains(pt))
                {
                    s.AutoToolEnabled = !s.AutoToolEnabled;
                    s.Save();
                    PlayClick();
                }
                // Row 2: Auto-Attack toggle
                else if (_settingsRowBtns[2].Contains(pt))
                {
                    s.AutoAttackEnabled = !s.AutoAttackEnabled;
                    s.Save();
                    PlayClick();
                }
                // Row 3: Pinch-to-Zoom toggle
                else if (_settingsRowBtns[3].Contains(pt))
                {
                    s.PinchToZoomEnabled = !s.PinchToZoomEnabled;
                    s.Save();
                    PlayClick();
                }
                // Row 4: Toolbar slot size cycle (Compact / Normal / Large)
                else if (_settingsRowBtns[4].Contains(pt))
                {
                    s.MobileToolbarSlotSize = s.MobileToolbarSlotSize < 70 ? 78 : (s.MobileToolbarSlotSize < 88 ? 94 : 62);
                    UpdateLayout(_vpW, _vpH, s);
                    s.Save();
                    PlayClick();
                }
                // Row 5: Customize Joystick & Buttons (opens official top bar!)
                else if (_settingsRowBtns[5].Contains(pt))
                {
                    OpenCustomizeBar(s);
                    PlayClick();
                }
                // Row 6: Quick Save State
                else if (_settingsRowBtns[6].Contains(pt))
                {
                    IsMobileSettingsOpen = false;
                    QuickSaveManager.Instance?.QuickSave();
                    PlayClick();
                }
                // Row 7: Mod Hotkeys Drawer / Legacy Settings
                else if (_settingsRowBtns[7].Contains(pt))
                {
                    IsMobileSettingsOpen = false;
                    TouchVirtualPad.Instance.IsHotkeyDrawerOpen = true;
                    PlayClick();
                }
                // Row 8: Close & Save
                else if (_settingsRowBtns[8].Contains(pt))
                {
                    IsMobileSettingsOpen = false;
                    s.Save();
                    try { Game1.playSound("coin"); } catch { }
                }
            }
        }

        private static void PlayClick()
        {
            try { Game1.playSound("smallSelect"); } catch { }
        }

        private static bool IsVietnameseLocale()
        {
            try
            {
                string? title = Game1.content?.LoadString("Strings\\StringsFromCSFiles:DayTimeMoneyBox.cs.10378");
                if (title != null && (title.Contains("Thứ") || title.Contains("Ngày")))
                    return true;
                string dayStr = Game1.shortDayDisplayNameFromDayOfSeason(1);
                if (!string.IsNullOrEmpty(dayStr) && (dayStr.Contains("Th") || dayStr.Contains("CN") || dayStr.Contains("Hai")))
                    return true;
            }
            catch { }
            return false;
        }

        // =========================================================================
        // RENDERING (Official Stardew Valley Mobile iOS Look + Auto UI-Mod Theme!)
        // =========================================================================

        public void Draw(SpriteBatch sb, Texture2D? pixelTex, TouchOverlaySettings s)
        {
            if (s.ControlScheme == MobileControlScheme.LegacyXboxPad || !s.MobileUIEnabled)
                return;

            var gd = sb.GraphicsDevice;
            if (gd != null)
            {
                var vp = gd.Viewport;
                if (vp.Width > 0 && vp.Height > 0 && (_vpW != vp.Width || _vpH != vp.Height))
                    UpdateLayout(vp.Width, vp.Height, s);
            }

            if (!IsWorldHudActive(s) && !IsCustomizeBarOpen && !IsMobileSettingsOpen)
                return;

            Texture2D? atlas = MobileThemeEngine.GetAtlas(gd);

            // 1. Subtle world tap target tile indicator (behind HUD)
            if (IsWorldHudActive(s))
            {
                MobileTapAndAutoController.Instance.DrawWorldIndicator(sb, pixelTex);
            }

            // 2. Vertical Scrollable Left Toolbar (hide during Customize Bar just like Image 2!)
            if (IsWorldHudActive(s) && !IsCustomizeBarOpen)
            {
                DrawVerticalToolbar(sb, atlas, pixelTex);
            }

            // 3. Top-Left [+ .] Joystick Toggle button
            if (IsWorldHudActive(s) && !IsCustomizeBarOpen && s.ShowMobileToggleButton)
            {
                DrawToggleJoystickButton(sb, atlas);
            }

            // 4. Top-Right Compact Mobile Day / Time / Money HUD + [!] Quest + [≡] Menu
            if (IsWorldHudActive(s) || IsCustomizeBarOpen)
            {
                DrawCompactTopRightHud(sb, atlas, pixelTex);
            }

            // 5. Translucent Circular Pixel-Art Joystick + X + A Buttons
            if (IsMobileJoystickShown(s))
            {
                DrawCircularPadControls(sb, atlas, pixelTex, s);
            }

            // 6. Official Mobile Customize Controls Top Bar (Image 2!)
            if (IsCustomizeBarOpen)
            {
                DrawCustomizeBar(sb, atlas, pixelTex, s);
            }

            // 7. Mobile Controls Settings Modal
            if (IsMobileSettingsOpen)
            {
                DrawMobileSettingsModal(sb, atlas, pixelTex, s);
            }
        }

        private void DrawVerticalToolbar(SpriteBatch sb, Texture2D? atlas, Texture2D? pixelTex)
        {
            var player = Game1.player;
            if (player == null) return;

            int maxItems = Math.Max(12, player.MaxItems);
            ClampToolbarScroll();

            // Outer vertical toolbar strip background (uses Game1.menuTexture when PC UI mod active, or warm wood from atlas)
            if (pixelTex != null)
            {
                sb.Draw(pixelTex, _toolbarRect, MobileThemeEngine.ThemeFillColor * 0.92f);
                // Crisp right border line matching Mobile vertical bar
                sb.Draw(pixelTex, new Rectangle(_toolbarRect.Right - 4, 0, 4, _toolbarRect.Height), MobileThemeEngine.ThemeBorderColor);
                sb.Draw(pixelTex, new Rectangle(_toolbarRect.Right - 6, 0, 2, _toolbarRect.Height), MobileThemeEngine.ThemeAccentColor);
            }

            int startSlot = Math.Max(0, (int)Math.Floor(_toolbarScrollY / _toolbarSlotSize));
            int visibleCount = (_vpH / _toolbarSlotSize) + 2;
            int endSlot = Math.Min(maxItems - 1, startSlot + visibleCount);

            for (int i = startSlot; i <= endSlot; i++)
            {
                int slotY = (int)Math.Round(i * _toolbarSlotSize - _toolbarScrollY);
                if (slotY + _toolbarSlotSize < 0 || slotY > _vpH) continue;

                Rectangle slotRect = new Rectangle(4, slotY + 2, _toolbarSlotSize - 4, _toolbarSlotSize - 4);
                bool isSelected = (player.CurrentToolIndex == i);

                // Draw themed slot cell
                if (atlas != null)
                {
                    sb.Draw(atlas, slotRect, MobileThemeEngine.SrcToolbarBox, Color.White * (isSelected ? 1.0f : 0.88f));
                }
                else
                {
                    MobileThemeEngine.DrawCursorBox(sb, slotRect, isSelected ? 1.0f : 0.85f);
                }

                // Selected item circular glow + crisp highlight (exact Stardew Mobile style from Image 1!)
                if (isSelected && atlas != null)
                {
                    Rectangle hlRect = new Rectangle(slotRect.X - 2, slotRect.Y - 2, slotRect.Width + 4, slotRect.Height + 4);
                    sb.Draw(atlas, hlRect, MobileThemeEngine.SrcCirclePad, Color.Black * 0.32f);
                    sb.Draw(atlas, slotRect, MobileThemeEngine.SrcSlotHighlightRed, Color.White * 0.85f);
                }

                // Draw Item inside slot
                if (i < player.Items.Count)
                {
                    var item = player.Items[i];
                    if (item != null)
                    {
                        try
                        {
                            float itemScale = Math.Clamp((_toolbarSlotSize - 12) / 64f, 0.72f, 1.35f);
                            int drawX = slotRect.X + (slotRect.Width - (int)(64 * itemScale)) / 2;
                            int drawY = slotRect.Y + (slotRect.Height - (int)(64 * itemScale)) / 2;
                            item.drawInMenu(
                                sb,
                                new Vector2(drawX, drawY),
                                itemScale,
                                1f,
                                0.88f,
                                StackDrawType.Draw,
                                Color.White,
                                drawShadow: true
                            );
                        }
                        catch { }
                    }
                }
            }
        }

        private void DrawToggleJoystickButton(SpriteBatch sb, Texture2D? atlas)
        {
            bool pressed = _toggleTouchId != -1;
            Color tint = pressed ? new Color(210, 210, 210) : Color.White;
            if (atlas != null)
            {
                sb.Draw(atlas, _toggleJoyBtnRect, MobileThemeEngine.SrcToggleJoy, tint);
            }
            else
            {
                MobileThemeEngine.DrawCursorBox(sb, _toggleJoyBtnRect, 1f, pressed);
            }
        }

        private void DrawCompactTopRightHud(SpriteBatch sb, Texture2D? atlas, Texture2D? pixelTex)
        {
            // 1. Compact Day / Time / Money wooden frame (exact MobileAtlas DayTimeBox 95x31)
            if (atlas != null)
            {
                sb.Draw(atlas, _dayTimeBoxRect, MobileThemeEngine.SrcDayTimeBox, Color.White);
            }
            else
            {
                MobileThemeEngine.DrawMenuBox(sb, _dayTimeBoxRect);
            }

            // Text measurements inside the 95x31 box:
            // Top-left box (Day): x=3..46, y=3..14  -> normalized [0.03..0.49, 0.09..0.46]
            // Top-right box (Time): x=50..91, y=3..14 -> normalized [0.53..0.96, 0.09..0.46]
            // Bottom-right box (Money): x=44..91, y=18..28 -> normalized [0.45..0.96, 0.56..0.92]
            SpriteFont? font = Game1.dialogueFont ?? Game1.smallFont;
            if (font != null)
            {
                try
                {
                    string dayText = $"{Game1.shortDayDisplayNameFromDayOfSeason(Game1.dayOfMonth)}, {Game1.dayOfMonth}";
                    string timeText = Game1.getTimeOfDayString(Game1.timeOfDay);
                    string moneyText = (Game1.player?.Money ?? 0).ToString();

                    Rectangle dayArea = new Rectangle(
                        _dayTimeBoxRect.X + (int)(_dayTimeBoxRect.Width * 0.04f),
                        _dayTimeBoxRect.Y + (int)(_dayTimeBoxRect.Height * 0.08f),
                        (int)(_dayTimeBoxRect.Width * 0.45f),
                        (int)(_dayTimeBoxRect.Height * 0.40f)
                    );
                    Rectangle timeArea = new Rectangle(
                        _dayTimeBoxRect.X + (int)(_dayTimeBoxRect.Width * 0.52f),
                        _dayTimeBoxRect.Y + (int)(_dayTimeBoxRect.Height * 0.08f),
                        (int)(_dayTimeBoxRect.Width * 0.44f),
                        (int)(_dayTimeBoxRect.Height * 0.40f)
                    );
                    Rectangle moneyArea = new Rectangle(
                        _dayTimeBoxRect.X + (int)(_dayTimeBoxRect.Width * 0.43f),
                        _dayTimeBoxRect.Y + (int)(_dayTimeBoxRect.Height * 0.54f),
                        (int)(_dayTimeBoxRect.Width * 0.52f),
                        (int)(_dayTimeBoxRect.Height * 0.40f)
                    );

                    Color textCol = MobileThemeEngine.ThemeTextColor;
                    DrawFittedText(sb, font, dayText, dayArea, textCol, rightAlign: false);
                    DrawFittedText(sb, font, timeText, timeArea, textCol, rightAlign: false);

                    // Classic crisp dark-red money digits (matching Images 1, 3, 4!)
                    Color moneyCol = new Color(150, 18, 12);
                    DrawFittedText(sb, font, moneyText, moneyArea, moneyCol, rightAlign: true);
                }
                catch { }
            }

            // 2. [!] Quest Button (with subtle pulse when a quest notification is active)
            if (atlas != null)
            {
                bool hasQuest = (Game1.player?.questLog?.Count ?? 0) > 0;
                sb.Draw(atlas, _questBtnRect, MobileThemeEngine.SrcQuestBtn, Color.White * (hasQuest ? 1f : 0.85f));
                sb.Draw(atlas, _menuBtnRect, MobileThemeEngine.SrcMenuBtn, Color.White);
            }
            else
            {
                MobileThemeEngine.DrawCursorBox(sb, _questBtnRect);
                MobileThemeEngine.DrawCursorBox(sb, _menuBtnRect);
            }
        }

        private void DrawCircularPadControls(SpriteBatch sb, Texture2D? atlas, Texture2D? pixelTex, TouchOverlaySettings s)
        {
            float baseAlpha = IsCustomizeBarOpen ? 0.58f : Math.Clamp(s.Opacity * 0.72f, 0.25f, 0.75f);

            // 1. Translucent pixel-art circular Joystick (exact look of Images 2 & 3!)
            Rectangle joyRect = new Rectangle(
                (int)(_joyCenter.X - _joyRadius),
                (int)(_joyCenter.Y - _joyRadius),
                (int)(_joyRadius * 2),
                (int)(_joyRadius * 2)
            );
            Color joyTint = (IsCustomizeBarOpen && _customSelectedTarget == 0)
                ? Color.Lerp(MobileThemeEngine.ThemeCircleTint, Color.LightGreen, 0.35f) * 0.72f
                : MobileThemeEngine.ThemeCircleTint * baseAlpha;

            if (atlas != null)
            {
                sb.Draw(atlas, joyRect, MobileThemeEngine.SrcCirclePad, joyTint);

                // Subtle inner thumb dot when dragging or customizing
                float thumbR = _joyRadius * 0.34f;
                Rectangle thumbRect = new Rectangle(
                    (int)(_joyThumbPos.X - thumbR),
                    (int)(_joyThumbPos.Y - thumbR),
                    (int)(thumbR * 2),
                    (int)(thumbR * 2)
                );
                float thumbAlpha = _joyTouchId != -1 ? Math.Min(0.85f, baseAlpha + 0.25f) : baseAlpha * 0.55f;
                sb.Draw(atlas, thumbRect, MobileThemeEngine.SrcCirclePad, Color.White * thumbAlpha);
            }

            // 2. Button X (SỬ DỤNG — Use Tool / Attack)
            Rectangle btnXRect = new Rectangle(
                (int)(_btnXCenter.X - _btnXRadius),
                (int)(_btnXCenter.Y - _btnXRadius),
                (int)(_btnXRadius * 2),
                (int)(_btnXRadius * 2)
            );
            float xAlpha = ButtonXPressed ? Math.Min(0.9f, baseAlpha + 0.3f) : baseAlpha;
            Color xTint = (IsCustomizeBarOpen && _customSelectedTarget == 1)
                ? Color.Lerp(MobileThemeEngine.ThemeCircleTint, Color.LightSkyBlue, 0.35f) * 0.78f
                : MobileThemeEngine.ThemeCircleTint * xAlpha;
            if (atlas != null)
                sb.Draw(atlas, btnXRect, MobileThemeEngine.SrcCirclePad, xTint);

            // 3. Button A (KIỂM TRA — Check / Interact)
            Rectangle btnARect = new Rectangle(
                (int)(_btnACenter.X - _btnARadius),
                (int)(_btnACenter.Y - _btnARadius),
                (int)(_btnARadius * 2),
                (int)(_btnARadius * 2)
            );
            float aAlpha = ButtonAPressed ? Math.Min(0.9f, baseAlpha + 0.3f) : baseAlpha;
            Color aTint = (IsCustomizeBarOpen && _customSelectedTarget == 2)
                ? Color.Lerp(MobileThemeEngine.ThemeCircleTint, Color.LightGreen, 0.35f) * 0.78f
                : MobileThemeEngine.ThemeCircleTint * aAlpha;
            if (atlas != null)
                sb.Draw(atlas, btnARect, MobileThemeEngine.SrcCirclePad, aTint);

            // Subtle X / A labels only while customizing so gameplay stays ultra-clean like Image 3!
            if (IsCustomizeBarOpen && Game1.smallFont != null)
            {
                DrawFittedText(sb, Game1.smallFont, "X", btnXRect, Color.White * 0.9f, rightAlign: false);
                DrawFittedText(sb, Game1.smallFont, "A", btnARect, Color.White * 0.9f, rightAlign: false);
            }
        }

        private void DrawCustomizeBar(SpriteBatch sb, Texture2D? atlas, Texture2D? pixelTex, TouchOverlaySettings s)
        {
            bool vi = IsVietnameseLocale();
            SpriteFont? font = Game1.smallFont ?? Game1.dialogueFont;

            // Main top bar box drawn directly with Game1.menuTexture (automatically matches installed UI mod!)
            MobileThemeEngine.DrawMenuBox(sb, _customBarRect, 0.98f, drawShadow: true);

            // 3 selector tabs (ĐI / SỬ DỤNG / KIỂM TRA)
            DrawCustomTab(sb, pixelTex, font, _customTabJoyRect, vi ? "ĐI:" : "MOVE:", "◎", _customSelectedTarget == 0);
            DrawCustomTab(sb, pixelTex, font, _customTabXRect, vi ? "SỬ DỤNG:" : "USE:", "X", _customSelectedTarget == 1);
            DrawCustomTab(sb, pixelTex, font, _customTabARect, vi ? "KIỂM TRA:" : "CHECK:", "A", _customSelectedTarget == 2);

            // Diameter label
            int diamVal = (int)Math.Round(GetSelectedDiameter(s));
            string diamLabel = vi ? $"Đường kính nút: {diamVal}" : $"Button Diameter: {diamVal}";
            if (font != null)
            {
                Vector2 labelPos = new Vector2(_customTabJoyRect.X, _customTabJoyRect.Bottom + 8);
                sb.DrawString(font, diamLabel, labelPos, MobileThemeEngine.ThemeTextColor);
            }

            // Left / Right slider arrows
            if (atlas != null)
            {
                sb.Draw(atlas, _customSliderLeftBtn, MobileThemeEngine.SrcLeftArrow, Color.White);
                sb.Draw(atlas, _customSliderRightBtn, MobileThemeEngine.SrcRightArrow, Color.White);
            }

            // Slider track & draggable thumb
            if (pixelTex != null)
            {
                sb.Draw(pixelTex, _customSliderTrackRect, MobileThemeEngine.ThemeBorderColor);
                Rectangle innerTrack = new Rectangle(_customSliderTrackRect.X + 3, _customSliderTrackRect.Y + 3, _customSliderTrackRect.Width - 6, _customSliderTrackRect.Height - 6);
                sb.Draw(pixelTex, innerTrack, Color.Lerp(MobileThemeEngine.ThemeFillColor, Color.Gray, 0.25f));

                float minD = _customSelectedTarget == 0 ? 120f : 80f;
                float maxD = _customSelectedTarget == 0 ? 340f : 240f;
                float norm = Math.Clamp((GetSelectedDiameter(s) - minD) / (maxD - minD), 0f, 1f);
                int thumbW = Math.Max(28, _customSliderTrackRect.Height + 12);
                int thumbH = _customSliderTrackRect.Height + 20;
                int thumbX = _customSliderTrackRect.X + (int)(norm * (_customSliderTrackRect.Width - thumbW));
                int thumbY = _customSliderTrackRect.Y - 10;
                MobileThemeEngine.DrawCursorBox(sb, new Rectangle(thumbX, thumbY, thumbW, thumbH));
            }

            // Default button
            MobileThemeEngine.DrawCursorBox(sb, _customDefaultBtn);
            if (font != null)
            {
                DrawFittedText(sb, font, vi ? "Mặc định" : "Default", _customDefaultBtn, MobileThemeEngine.ThemeTextColor, rightAlign: false);
            }

            // [X] Cancel and [OK] Save buttons
            if (atlas != null)
            {
                sb.Draw(atlas, _customCancelBtn, MobileThemeEngine.SrcCloseBtn, Color.White);
                sb.Draw(atlas, _customOkBtn, MobileThemeEngine.SrcCheckBtn, Color.White);
            }
            else
            {
                MobileThemeEngine.DrawCursorBox(sb, _customCancelBtn);
                MobileThemeEngine.DrawCursorBox(sb, _customOkBtn);
            }
        }

        private void DrawCustomTab(SpriteBatch sb, Texture2D? pixelTex, SpriteFont? font, Rectangle rect, string topLabel, string symbol, bool selected)
        {
            if (pixelTex == null) return;
            sb.Draw(pixelTex, rect, new Color(24, 28, 38) * 0.92f);
            Color border = selected ? Color.Red : MobileThemeEngine.ThemeBorderColor;
            int b = selected ? 3 : 2;
            sb.Draw(pixelTex, new Rectangle(rect.X, rect.Y, rect.Width, b), border);
            sb.Draw(pixelTex, new Rectangle(rect.X, rect.Bottom - b, rect.Width, b), border);
            sb.Draw(pixelTex, new Rectangle(rect.X, rect.Y, b, rect.Height), border);
            sb.Draw(pixelTex, new Rectangle(rect.Right - b, rect.Y, b, rect.Height), border);

            if (font != null)
            {
                Rectangle topR = new Rectangle(rect.X + 2, rect.Y + 3, rect.Width - 4, rect.Height / 2 - 2);
                Rectangle botR = new Rectangle(rect.X + 2, rect.Y + rect.Height / 2 - 2, rect.Width - 4, rect.Height / 2);
                DrawFittedText(sb, font, topLabel, topR, Color.White, rightAlign: false);
                DrawFittedText(sb, font, symbol, botR, symbol == "A" ? Color.LightGreen : Color.LightSkyBlue, rightAlign: false);
            }
        }

        private void DrawMobileSettingsModal(SpriteBatch sb, Texture2D? atlas, Texture2D? pixelTex, TouchOverlaySettings s)
        {
            if (pixelTex != null)
                sb.Draw(pixelTex, new Rectangle(0, 0, _vpW, _vpH), Color.Black * 0.45f);

            bool vi = IsVietnameseLocale();
            SpriteFont? font = Game1.smallFont ?? Game1.dialogueFont;

            MobileThemeEngine.DrawMenuBox(sb, _settingsBoxRect, 0.98f, drawShadow: true);

            if (font != null)
            {
                string title = vi ? "Cài Đặt Điều Khiển Mobile" : "Mobile Controls Settings";
                sb.DrawString(font, title, new Vector2(_settingsBoxRect.X + 26, _settingsBoxRect.Y + 18), MobileThemeEngine.ThemeTextColor);
            }

            if (atlas != null)
                sb.Draw(atlas, _settingsCloseBtn, MobileThemeEngine.SrcCloseBtn, Color.White);
            else
                MobileThemeEngine.DrawCursorBox(sb, _settingsCloseBtn);

            string schemeLabel = s.ControlScheme switch
            {
                MobileControlScheme.TapToMoveAndAutoAttack => vi ? "Chạm di chuyển & Tự đánh" : "Tap-to-Move & Auto-Attack",
                MobileControlScheme.JoystickAndButtons => vi ? "Cần điều khiển & 2 nút" : "Joystick & 2 Buttons",
                MobileControlScheme.TapToMoveAndJoystickAndButtons => vi ? "Chạm + Cần điều khiển & Nút" : "Tap-to-Move + Joystick & Buttons",
                _ => vi ? "Tay cầm Xbox (Kiểu cũ)" : "Legacy Xbox Pad"
            };

            string tbSizeLabel = s.MobileToolbarSlotSize < 70
                ? (vi ? "Nhỏ" : "Compact")
                : (s.MobileToolbarSlotSize < 88 ? (vi ? "Vừa" : "Normal") : (vi ? "Lớn" : "Large"));

            string[] labels = new[]
            {
                (vi ? "Kiểu điều khiển: " : "Control Mode: ") + schemeLabel,
                (vi ? "Tự đổi công cụ (Auto-Tool): " : "Auto-Tool: ") + (s.AutoToolEnabled ? (vi ? "BẬT" : "ON") : (vi ? "TẮT" : "OFF")),
                (vi ? "Tự đánh quái (Auto-Attack): " : "Auto-Attack: ") + (s.AutoAttackEnabled ? (vi ? "BẬT" : "ON") : (vi ? "TẮT" : "OFF")),
                (vi ? "Thu phóng 2 ngón (Pinch Zoom): " : "Pinch-to-Zoom: ") + (s.PinchToZoomEnabled ? (vi ? "BẬT" : "ON") : (vi ? "TẮT" : "OFF")),
                (vi ? "Cỡ thanh công cụ dọc: " : "Vertical Toolbar Size: ") + tbSizeLabel,
                vi ? "Chỉnh vị trí & đường kính nút..." : "Customize Joystick & Buttons...",
                vi ? "Lưu nhanh giữa ngày (QuickSave)" : "Quick Save State",
                vi ? "Mở bảng phím tắt Mod (Hotkeys)" : "Open Mod Hotkeys Drawer",
                vi ? "Lưu & Đóng" : "Save & Close"
            };

            for (int i = 0; i < _settingsRowBtns.Length; i++)
            {
                MobileThemeEngine.DrawCursorBox(sb, _settingsRowBtns[i]);
                if (font != null)
                {
                    DrawFittedText(sb, font, labels[i], _settingsRowBtns[i], MobileThemeEngine.ThemeTextColor, rightAlign: false);
                }
            }
        }

        private static void DrawFittedText(SpriteBatch sb, SpriteFont font, string text, Rectangle area, Color color, bool rightAlign)
        {
            if (string.IsNullOrEmpty(text) || area.Width <= 4 || area.Height <= 4) return;
            Vector2 size = font.MeasureString(text);
            if (size.X <= 0.1f || size.Y <= 0.1f) return;

            float scaleX = (area.Width - 6f) / size.X;
            float scaleY = (area.Height - 4f) / size.Y;
            float scale = Math.Clamp(Math.Min(scaleX, scaleY), 0.35f, 1.35f);

            float drawW = size.X * scale;
            float drawH = size.Y * scale;
            float x = rightAlign
                ? area.Right - drawW - 4f
                : area.X + (area.Width - drawW) * 0.5f;
            float y = area.Y + (area.Height - drawH) * 0.5f;

            sb.DrawString(font, text, new Vector2(x, y), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.99f);
        }
    }
}
