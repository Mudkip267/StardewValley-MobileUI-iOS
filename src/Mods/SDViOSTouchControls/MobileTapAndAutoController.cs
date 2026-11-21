using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Monsters;
using StardewValley.Objects;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace SDViOSTouchControls
{
    public enum MobileControlScheme
    {
        TapToMoveAndAutoAttack = 0,
        JoystickAndButtons = 1,
        TapToMoveAndJoystickAndButtons = 2,
        LegacyXboxPad = 3
    }

    internal enum TapActionKind
    {
        None = 0,
        WalkOnly = 1,
        Interact = 2,
        UseTool = 3,
        AttackMonster = 4
    }

    /// <summary>
    /// Genuine Stardew Valley Mobile gameplay controller:
    /// - Tap-to-Move using native PathFindController + destination tile highlight
    /// - Hold-and-drag steering
    /// - Auto-Tool selection (Pickaxe, Axe, Hoe, Scythe, WateringCan) + continuous swing until cleared
    /// - Auto-Interact (NPCs, Animals, Chests, Machines, Crops, Doors, Beds)
    /// - Auto-Attack nearby monsters in combat
    /// - 2-finger Pinch-to-Zoom
    /// </summary>
    public class MobileTapAndAutoController
    {
        public static MobileTapAndAutoController Instance { get; } = new MobileTapAndAutoController();

        // Active Tap-to-Move / Auto-Action state
        private bool _hasTarget = false;
        private Point _targetTile = Point.Zero;
        private TapActionKind _pendingAction = TapActionKind.None;
        private int _pendingToolSlot = -1;
        private bool _repeatToolUntilCleared = false;
        private int _actionCooldownTicks = 0;
        private int _targetVisualTicks = 0;
        private Monster? _targetMonster = null;

        // Hold-to-walk state
        private int _worldTouchId = -1;
        private Vector2 _worldTouchStartScreen = Vector2.Zero;
        private Vector2 _worldTouchCurrentScreen = Vector2.Zero;
        private double _worldTouchDownTime = 0;
        private bool _isHoldWalking = false;

        // Pinch-to-zoom state
        private bool _isPinching = false;
        private float _pinchStartDist = 0f;
        private float _pinchStartZoom = 1f;
        private int _pinchTouchId1 = -1;
        private int _pinchTouchId2 = -1;

        // Auto-Attack state
        private int _autoAttackCooldown = 0;

        // Exposed movement direction when hold-walking (merged cleanly in TouchVirtualPad)
        public Vector2 HoldWalkVector { get; private set; } = Vector2.Zero;
        public bool IsHoldWalking => _isHoldWalking;
        public bool IsPinching => _isPinching;
        public bool HasPendingWorldAction => _hasTarget;

        public void CancelAll()
        {
            _hasTarget = false;
            _pendingAction = TapActionKind.None;
            _pendingToolSlot = -1;
            _repeatToolUntilCleared = false;
            _targetMonster = null;
            _worldTouchId = -1;
            _isHoldWalking = false;
            HoldWalkVector = Vector2.Zero;
            CancelPinch();

            try
            {
                if (Game1.player != null && Game1.player.controller != null)
                {
                    Game1.player.controller = null;
                    Game1.player.Halt();
                }
            }
            catch { }
        }

        private void CancelPinch()
        {
            _isPinching = false;
            _pinchTouchId1 = -1;
            _pinchTouchId2 = -1;
        }

        public bool CanRunWorldControls()
        {
            return Game1.gameMode == 3
                && Game1.player != null
                && Game1.currentLocation != null
                && Game1.activeClickableMenu == null
                && !Game1.eventUp
                && !Game1.dialogueUp
                && !Game1.freezeControls
                && Game1.currentMinigame == null;
        }

        /// <summary>
        /// Processes non-HUD world touches for Pinch-to-Zoom and Tap-to-Move / Hold-to-Walk.
        /// Call from TouchVirtualPad.Update before Game1.Update().
        /// </summary>
        public void ProcessWorldTouches(
            IReadOnlyList<TouchLocation> worldTouches,
            Func<Vector2, Point> mapTouchToViewport,
            int viewportW,
            int viewportH,
            double curTime,
            TouchOverlaySettings settings)
        {
            HoldWalkVector = Vector2.Zero;

            if (!CanRunWorldControls())
            {
                _worldTouchId = -1;
                _isHoldWalking = false;
                CancelPinch();
                return;
            }

            // 1. Pinch-to-Zoom (2 fingers on world)
            if (settings.PinchToZoomEnabled && worldTouches.Count >= 2)
            {
                TouchLocation t1 = worldTouches[0];
                TouchLocation t2 = worldTouches[1];
                if (t1.State != TouchLocationState.Released && t2.State != TouchLocationState.Released)
                {
                    Point p1 = mapTouchToViewport(t1.Position);
                    Point p2 = mapTouchToViewport(t2.Position);
                    float dist = Vector2.Distance(new Vector2(p1.X, p1.Y), new Vector2(p2.X, p2.Y));
                    if (dist > 30f)
                    {
                        if (!_isPinching || t1.Id != _pinchTouchId1 || t2.Id != _pinchTouchId2)
                        {
                            _isPinching = true;
                            _pinchTouchId1 = t1.Id;
                            _pinchTouchId2 = t2.Id;
                            _pinchStartDist = dist;
                            _pinchStartZoom = Game1.options?.desiredBaseZoomLevel ?? 1.0f;
                            // Cancel single-finger tap so pinching doesn't walk
                            _worldTouchId = -1;
                            _isHoldWalking = false;
                        }
                        else if (_pinchStartDist > 10f && Game1.options != null)
                        {
                            float ratio = dist / _pinchStartDist;
                            float newZoom = Math.Clamp(_pinchStartZoom * ratio, 0.75f, 2.0f);
                            // Quantize slightly to reduce jittery tile seams
                            newZoom = (float)Math.Round(newZoom * 20f) / 20f;
                            if (Math.Abs(Game1.options.desiredBaseZoomLevel - newZoom) >= 0.04f)
                            {
                                Game1.options.desiredBaseZoomLevel = newZoom;
                                settings.TargetZoomLevel = newZoom;
                            }
                        }
                        return;
                    }
                }
            }
            else if (_isPinching)
            {
                CancelPinch();
            }

            // Check if Tap-to-Move is active in the current control scheme
            bool tapEnabled = settings.ControlScheme == MobileControlScheme.TapToMoveAndAutoAttack
                           || settings.ControlScheme == MobileControlScheme.TapToMoveAndJoystickAndButtons;

            if (!tapEnabled)
            {
                _worldTouchId = -1;
                _isHoldWalking = false;
                return;
            }

            // 2. Single-finger Tap-to-Move & Hold-to-Walk
            bool foundActive = false;
            for (int i = 0; i < worldTouches.Count; i++)
            {
                var touch = worldTouches[i];
                Point vpPt = mapTouchToViewport(touch.Position);
                Vector2 vpVec = new Vector2(vpPt.X, vpPt.Y);

                if (touch.State == TouchLocationState.Pressed && _worldTouchId == -1)
                {
                    _worldTouchId = touch.Id;
                    _worldTouchStartScreen = vpVec;
                    _worldTouchCurrentScreen = vpVec;
                    _worldTouchDownTime = curTime;
                    _isHoldWalking = false;
                    foundActive = true;

                    // If fishing rod is actively waiting/casting, tapping world acts like UseTool (cast/hook)
                    if (TryHandleFishingTap())
                    {
                        _worldTouchId = -1;
                        return;
                    }
                }
                else if (touch.Id == _worldTouchId)
                {
                    foundActive = true;
                    _worldTouchCurrentScreen = vpVec;

                    if (touch.State == TouchLocationState.Moved)
                    {
                        double heldSec = curTime - _worldTouchDownTime;
                        float movedDist = Vector2.Distance(_worldTouchStartScreen, _worldTouchCurrentScreen);
                        if (heldSec >= 0.22 || movedDist >= 32f)
                        {
                            _isHoldWalking = true;
                            // Cancel A* pathfinding while manually steering with finger hold
                            CancelPathOnly();
                            UpdateHoldWalkDirection(vpVec, viewportW, viewportH);
                        }
                    }
                    else if (touch.State == TouchLocationState.Released)
                    {
                        bool wasHoldWalk = _isHoldWalking;
                        _worldTouchId = -1;
                        _isHoldWalking = false;
                        HoldWalkVector = Vector2.Zero;

                        if (!wasHoldWalk)
                        {
                            // Crisp tap on world! Trigger Mobile Tap-to-Move + Auto-Tool/Interact!
                            HandleWorldTap(vpPt, viewportW, viewportH, settings);
                        }
                    }
                }
            }

            if (!foundActive && _worldTouchId != -1)
            {
                _worldTouchId = -1;
                _isHoldWalking = false;
                HoldWalkVector = Vector2.Zero;
            }
            else if (_isHoldWalking)
            {
                UpdateHoldWalkDirection(_worldTouchCurrentScreen, viewportW, viewportH);
            }
        }

        private void CancelPathOnly()
        {
            _hasTarget = false;
            _pendingAction = TapActionKind.None;
            _repeatToolUntilCleared = false;
            _targetMonster = null;
            try
            {
                if (Game1.player != null && Game1.player.controller != null)
                {
                    Game1.player.controller = null;
                }
            }
            catch { }
        }

        private void UpdateHoldWalkDirection(Vector2 vpVec, int viewportW, int viewportH)
        {
            if (Game1.player == null) return;
            Vector2 worldPos = ViewportToWorld(vpVec, viewportW, viewportH);
            Vector2 playerCenter = Game1.player.getStandingPosition();
            Vector2 delta = worldPos - playerCenter;
            if (delta.LengthSquared() > 28f * 28f)
            {
                delta.Normalize();
                HoldWalkVector = delta;
            }
            else
            {
                HoldWalkVector = Vector2.Zero;
            }
        }

        private bool TryHandleFishingTap()
        {
            var player = Game1.player;
            if (player?.CurrentTool is FishingRod rod)
            {
                if (rod.isFishing || rod.isNibbling || rod.isReeling || rod.isTimingCast || rod.showingTreasure || rod.fishCaught)
                {
                    // Let TouchVirtualPad pulse ButtonX/Click so fishing reel/hook works effortlessly with 1 tap anywhere
                    TouchVirtualPad.Instance.PulseUseToolFromMobile(3);
                    return true;
                }
            }
            return false;
        }

        private Vector2 ViewportToWorld(Vector2 vpPos, int viewportW, int viewportH)
        {
            // vpPos is in UI viewport coordinates (0..viewportW, 0..viewportH)
            // Game1.viewport is in world pixel coordinates sized for game zoomLevel!
            float normX = viewportW > 0 ? vpPos.X / viewportW : 0.5f;
            float normY = viewportH > 0 ? vpPos.Y / viewportH : 0.5f;
            float worldX = Game1.viewport.X + normX * Game1.viewport.Width;
            float worldY = Game1.viewport.Y + normY * Game1.viewport.Height;
            return new Vector2(worldX, worldY);
        }

        private void HandleWorldTap(Point vpPt, int viewportW, int viewportH, TouchOverlaySettings settings)
        {
            var player = Game1.player;
            var loc = Game1.currentLocation;
            if (player == null || loc == null || !player.CanMove) return;

            Vector2 worldPos = ViewportToWorld(new Vector2(vpPt.X, vpPt.Y), viewportW, viewportH);
            Point tile = new Point((int)Math.Floor(worldPos.X / 64f), (int)Math.Floor(worldPos.Y / 64f));

            _targetTile = tile;
            _targetVisualTicks = 45;
            _hasTarget = true;
            _pendingAction = TapActionKind.WalkOnly;
            _pendingToolSlot = -1;
            _repeatToolUntilCleared = false;
            _targetMonster = null;

            // 1. Check for Monster near tapped world point
            Monster? tappedMonster = FindMonsterNear(loc, worldPos, 72f);
            if (tappedMonster != null)
            {
                _targetMonster = tappedMonster;
                _pendingAction = TapActionKind.AttackMonster;
                if (settings.AutoToolEnabled)
                    _pendingToolSlot = FindBestMeleeWeaponSlot(player);
                StartPathToTarget(player, loc, tappedMonster.TilePoint, stopAdjacent: true);
                return;
            }

            // 2. Check for NPC / Pet / FarmAnimal near tapped world point
            if (HasFriendlyCharacterNear(loc, worldPos, tile))
            {
                _pendingAction = TapActionKind.Interact;
                StartPathToTarget(player, loc, tile, stopAdjacent: true);
                return;
            }

            // 3. Check for Building / Door / Warp / Shipping Bin
            if (IsDoorOrActionTile(loc, tile))
            {
                _pendingAction = TapActionKind.Interact;
                StartPathToTarget(player, loc, tile, stopAdjacent: true);
                return;
            }

            // 4. Inspect Objects, ResourceClumps, TerrainFeatures on the tapped tile
            Vector2 tileVec = new Vector2(tile.X, tile.Y);

            // 4a. ResourceClump (large stump, hollow log, boulder, meteorite, giant crop)
            ResourceClump? clump = FindResourceClumpAt(loc, tile);
            if (clump != null)
            {
                int idx = clump.parentSheetIndex.Value;
                bool isRock = (idx == ResourceClump.boulderIndex || idx == ResourceClump.meteoriteIndex ||
                               idx == ResourceClump.mineRock1Index || idx == ResourceClump.mineRock2Index ||
                               idx == ResourceClump.mineRock3Index || idx == ResourceClump.mineRock4Index ||
                               idx == ResourceClump.quarryBoulderIndex);
                _pendingAction = TapActionKind.UseTool;
                _repeatToolUntilCleared = true;
                if (settings.AutoToolEnabled)
                    _pendingToolSlot = isRock ? FindToolSlot<Pickaxe>(player) : FindToolSlot<Axe>(player);
                StartPathToTarget(player, loc, tile, stopAdjacent: true);
                return;
            }

            // 4b. Objects on tile (rocks, twigs, weeds, artifact spots, chests, machines, forageables)
            if (loc.objects.TryGetValue(tileVec, out var obj) && obj != null)
            {
                if (obj.QualifiedItemId == "(O)590" || obj.QualifiedItemId == "(O)SeedSpot")
                {
                    // Artifact spot or seed spot -> Hoe
                    _pendingAction = TapActionKind.UseTool;
                    if (settings.AutoToolEnabled)
                        _pendingToolSlot = FindToolSlot<Hoe>(player);
                    StartPathToTarget(player, loc, tile, stopAdjacent: true);
                    return;
                }
                if (obj.IsBreakableStone())
                {
                    _pendingAction = TapActionKind.UseTool;
                    _repeatToolUntilCleared = true;
                    if (settings.AutoToolEnabled)
                        _pendingToolSlot = FindToolSlot<Pickaxe>(player);
                    StartPathToTarget(player, loc, tile, stopAdjacent: true);
                    return;
                }
                if (obj.IsTwig())
                {
                    _pendingAction = TapActionKind.UseTool;
                    _repeatToolUntilCleared = true;
                    if (settings.AutoToolEnabled)
                        _pendingToolSlot = FindToolSlot<Axe>(player);
                    StartPathToTarget(player, loc, tile, stopAdjacent: true);
                    return;
                }
                if (obj.IsWeeds())
                {
                    _pendingAction = TapActionKind.UseTool;
                    if (settings.AutoToolEnabled)
                        _pendingToolSlot = FindScytheSlot(player);
                    StartPathToTarget(player, loc, tile, stopAdjacent: true);
                    return;
                }
                if (obj is Chest || obj.bigCraftable.Value || obj.IsSpawnedObject || obj.isForage() || obj.readyForHarvest.Value)
                {
                    _pendingAction = TapActionKind.Interact;
                    StartPathToTarget(player, loc, tile, stopAdjacent: true);
                    return;
                }
            }

            // 4c. TerrainFeatures (Trees, HoeDirt/Crops, Grass, Bushes)
            if (loc.terrainFeatures.TryGetValue(tileVec, out var tf) && tf != null)
            {
                if (tf is Tree || tf is FruitTree)
                {
                    if (tf is FruitTree ft && ft.fruit.Count > 0)
                    {
                        _pendingAction = TapActionKind.Interact;
                        StartPathToTarget(player, loc, tile, stopAdjacent: true);
                        return;
                    }
                    _pendingAction = TapActionKind.UseTool;
                    _repeatToolUntilCleared = true;
                    if (settings.AutoToolEnabled)
                        _pendingToolSlot = FindToolSlot<Axe>(player);
                    StartPathToTarget(player, loc, tile, stopAdjacent: true);
                    return;
                }
                if (tf is HoeDirt dirt)
                {
                    if (dirt.readyForHarvest())
                    {
                        bool needsScythe = false;
                        try
                        {
                            var mHm = dirt.crop?.GetType().GetMethod("GetHarvestMethod");
                            if (mHm != null && dirt.crop != null)
                            {
                                var hmVal = mHm.Invoke(dirt.crop, null);
                                if (hmVal != null && string.Equals(hmVal.ToString(), "Scythe", StringComparison.OrdinalIgnoreCase))
                                    needsScythe = true;
                            }
                        }
                        catch { }

                        if (needsScythe)
                        {
                            _pendingAction = TapActionKind.UseTool;
                            if (settings.AutoToolEnabled)
                                _pendingToolSlot = FindScytheSlot(player);
                        }
                        else
                        {
                            _pendingAction = TapActionKind.Interact;
                        }
                        StartPathToTarget(player, loc, tile, stopAdjacent: true);
                        return;
                    }
                    if (dirt.needsWatering() && dirt.state.Value == 0)
                    {
                        _pendingAction = TapActionKind.UseTool;
                        if (settings.AutoToolEnabled)
                            _pendingToolSlot = FindToolSlot<WateringCan>(player);
                        StartPathToTarget(player, loc, tile, stopAdjacent: true);
                        return;
                    }
                    // If player is holding seeds/fertilizer, plant on hoed dirt!
                    if (player.ActiveObject != null && (player.ActiveObject.Category == StardewValley.Object.SeedsCategory || player.ActiveObject.Category == StardewValley.Object.fertilizerCategory))
                    {
                        _pendingAction = TapActionKind.Interact;
                        StartPathToTarget(player, loc, tile, stopAdjacent: true);
                        return;
                    }
                }
                if (tf is Grass && player.CurrentTool is MeleeWeapon mw && mw.isScythe())
                {
                    _pendingAction = TapActionKind.UseTool;
                    StartPathToTarget(player, loc, tile, stopAdjacent: true);
                    return;
                }
            }

            // 4d. Water tile (fishing or refilling watering can)
            if (loc.isWaterTile(tile.X, tile.Y))
            {
                if (player.CurrentTool is FishingRod)
                {
                    _pendingAction = TapActionKind.UseTool;
                    StartPathToTarget(player, loc, tile, stopAdjacent: true);
                    return;
                }
                int wcSlot = FindToolSlot<WateringCan>(player);
                if (wcSlot >= 0 && player.Items[wcSlot] is WateringCan wc && wc.WaterLeft < wc.waterCanMax)
                {
                    _pendingAction = TapActionKind.UseTool;
                    if (settings.AutoToolEnabled) _pendingToolSlot = wcSlot;
                    StartPathToTarget(player, loc, tile, stopAdjacent: true);
                    return;
                }
            }

            // 4e. Holding Hoe and tapping an adjacent clear diggable tile -> till it!
            if (player.CurrentTool is Hoe && IsAdjacent(player.TilePoint, tile) && loc.doesTileHaveProperty(tile.X, tile.Y, "Diggable", "Back") != null)
            {
                _pendingAction = TapActionKind.UseTool;
                ExecutePendingAction(player, loc);
                return;
            }

            // 5. Default: Walk to tapped tile!
            _pendingAction = TapActionKind.WalkOnly;
            StartPathToTarget(player, loc, tile, stopAdjacent: false);
        }

        private void StartPathToTarget(Farmer player, GameLocation loc, Point targetTile, bool stopAdjacent)
        {
            Point playerTile = player.TilePoint;

            // Already in range for action? Execute immediately!
            if (stopAdjacent && IsAdjacentOrSame(playerTile, targetTile))
            {
                player.controller = null;
                ExecutePendingAction(player, loc);
                return;
            }
            if (!stopAdjacent && playerTile == targetTile)
            {
                _hasTarget = false;
                return;
            }

            try
            {
                Point dest = stopAdjacent ? FindBestAdjacentWalkableTile(loc, playerTile, targetTile) : targetTile;
                if (dest == playerTile && stopAdjacent)
                {
                    player.controller = null;
                    ExecutePendingAction(player, loc);
                    return;
                }

                var controller = new PathFindController(
                    player,
                    loc,
                    dest,
                    GetFacingDirection(dest, targetTile),
                    (Character c, GameLocation l) => OnPathCompleted(player, l)
                );

                if (controller.pathToEndPoint != null && controller.pathToEndPoint.Count > 0)
                {
                    player.controller = controller;
                }
                else
                {
                    // Fallback: if destination tile itself was blocked, try walking to closest adjacent tile
                    Point fallback = FindBestAdjacentWalkableTile(loc, playerTile, targetTile);
                    if (fallback != playerTile && fallback != dest)
                    {
                        var fbCtrl = new PathFindController(
                            player,
                            loc,
                            fallback,
                            GetFacingDirection(fallback, targetTile),
                            (Character c, GameLocation l) => OnPathCompleted(player, l)
                        );
                        if (fbCtrl.pathToEndPoint != null && fbCtrl.pathToEndPoint.Count > 0)
                        {
                            player.controller = fbCtrl;
                            return;
                        }
                    }
                    _hasTarget = false;
                }
            }
            catch
            {
                _hasTarget = false;
            }
        }

        private void OnPathCompleted(Farmer player, GameLocation loc)
        {
            player.controller = null;
            if (!_hasTarget) return;

            if (_pendingAction == TapActionKind.WalkOnly)
            {
                _hasTarget = false;
                return;
            }

            if (IsAdjacentOrSame(player.TilePoint, _targetTile))
            {
                ExecutePendingAction(player, loc);
            }
            else
            {
                _hasTarget = false;
            }
        }

        /// <summary>
        /// Called every game tick to continue repeating auto-tool actions, handle Auto-Attack, and decay target marker.
        /// </summary>
        public void UpdateTick(TouchOverlaySettings settings)
        {
            if (_targetVisualTicks > 0) _targetVisualTicks--;
            if (_actionCooldownTicks > 0) _actionCooldownTicks--;
            if (_autoAttackCooldown > 0) _autoAttackCooldown--;

            if (!CanRunWorldControls())
                return;

            var player = Game1.player;
            var loc = Game1.currentLocation;
            if (player == null || loc == null) return;

            // 1. Auto-Attack nearby monsters (official Stardew Mobile feature!)
            if (settings.AutoAttackEnabled &&
                (settings.ControlScheme == MobileControlScheme.TapToMoveAndAutoAttack ||
                 settings.ControlScheme == MobileControlScheme.TapToMoveAndJoystickAndButtons))
            {
                TryAutoAttackNearbyMonster(player, loc);
            }

            // 2. Continue repeating tool swing on rocks/trees/clumps until broken (just like Mobile!)
            if (_hasTarget && _repeatToolUntilCleared && _pendingAction == TapActionKind.UseTool &&
                player.controller == null && !player.UsingTool && _actionCooldownTicks <= 0)
            {
                if (IsAdjacentOrSame(player.TilePoint, _targetTile) && TargetStillNeedsTool(loc, _targetTile))
                {
                    ExecutePendingAction(player, loc);
                }
                else
                {
                    _hasTarget = false;
                    _repeatToolUntilCleared = false;
                }
            }
            else if (_hasTarget && _pendingAction == TapActionKind.AttackMonster && _targetMonster != null)
            {
                if (_targetMonster.Health <= 0 || !loc.characters.Contains(_targetMonster))
                {
                    _hasTarget = false;
                    _targetMonster = null;
                }
                else if (Vector2.Distance(player.getStandingPosition(), _targetMonster.getStandingPosition()) <= 118f)
                {
                    player.controller = null;
                    if (!player.UsingTool && _actionCooldownTicks <= 0)
                    {
                        ExecutePendingAction(player, loc);
                    }
                }
            }
        }

        private void ExecutePendingAction(Farmer player, GameLocation loc)
        {
            try
            {
                if (_pendingToolSlot >= 0 && _pendingToolSlot < player.MaxItems && player.Items[_pendingToolSlot] != null)
                {
                    player.CurrentToolIndex = _pendingToolSlot;
                }

                Vector2 targetWorldCenter = new Vector2(_targetTile.X * 64 + 32, _targetTile.Y * 64 + 32);
                if (_targetMonster != null)
                {
                    targetWorldCenter = _targetMonster.getStandingPosition();
                }

                player.faceGeneralDirection(targetWorldCenter, 0, false);

                if (_pendingAction == TapActionKind.Interact)
                {
                    Vector2 tileVec = new Vector2(_targetTile.X, _targetTile.Y);
                    bool handled = Game1.tryToCheckAt(tileVec, player);
                    if (!handled && loc.objects.TryGetValue(tileVec, out var obj) && obj is Chest chest)
                    {
                        chest.checkForAction(player);
                    }
                    _hasTarget = false;
                    _pendingAction = TapActionKind.None;
                    _actionCooldownTicks = 12;
                }
                else if (_pendingAction == TapActionKind.UseTool || _pendingAction == TapActionKind.AttackMonster)
                {
                    // Point simulated cursor to the target tile for tools that check cursor tile
                    if (!player.UsingTool && player.CanMove)
                    {
                        if (player.CurrentTool is MeleeWeapon weapon && !weapon.isScythe())
                        {
                            player.BeginUsingTool();
                            _actionCooldownTicks = 14;
                        }
                        else
                        {
                            player.BeginUsingTool();
                            _actionCooldownTicks = 18;
                        }
                    }

                    if (!_repeatToolUntilCleared && _pendingAction != TapActionKind.AttackMonster)
                    {
                        _hasTarget = false;
                        _pendingAction = TapActionKind.None;
                    }
                }
            }
            catch
            {
                _hasTarget = false;
                _repeatToolUntilCleared = false;
            }
        }

        private void TryAutoAttackNearbyMonster(Farmer player, GameLocation loc)
        {
            if (_autoAttackCooldown > 0 || player.UsingTool || !player.CanMove) return;
            if (loc.characters == null || loc.characters.Count == 0) return;

            Monster? nearest = null;
            float nearestDistSq = 122f * 122f; // ~1.9 tiles reach
            Vector2 pPos = player.getStandingPosition();

            for (int i = 0; i < loc.characters.Count; i++)
            {
                if (loc.characters[i] is Monster m && m.Health > 0 && !m.IsInvisible)
                {
                    // Skip invincible/underground monsters (e.g. Duggy hiding or Rock Crab shell if needed)
                    float dSq = Vector2.DistanceSquared(pPos, m.getStandingPosition());
                    if (dSq < nearestDistSq)
                    {
                        nearestDistSq = dSq;
                        nearest = m;
                    }
                }
            }

            if (nearest == null) return;

            int weaponSlot = FindBestMeleeWeaponSlot(player);
            if (weaponSlot < 0) return;

            if (player.CurrentToolIndex != weaponSlot)
                player.CurrentToolIndex = weaponSlot;

            player.faceGeneralDirection(nearest.getStandingPosition(), 0, false);
            player.BeginUsingTool();
            _autoAttackCooldown = 16;
        }

        /// <summary>
        /// Draws the subtle Stardew Mobile target-tile green/white box indicator when Tap-to-Move is active.
        /// </summary>
        public void DrawWorldIndicator(SpriteBatch sb, Texture2D? pixelTex)
        {
            if (pixelTex == null || !_hasTarget && _targetVisualTicks <= 0) return;
            if (!CanRunWorldControls()) return;

            try
            {
                var uiVp = Game1.uiViewport;
                var worldVp = Game1.viewport;
                if (worldVp.Width <= 0 || worldVp.Height <= 0) return;

                float scaleX = (float)uiVp.Width / worldVp.Width;
                float scaleY = (float)uiVp.Height / worldVp.Height;

                int sx = (int)((_targetTile.X * 64 - worldVp.X) * scaleX);
                int sy = (int)((_targetTile.Y * 64 - worldVp.Y) * scaleY);
                int sw = Math.Max(16, (int)(64 * scaleX));
                int sh = Math.Max(16, (int)(64 * scaleY));

                float alpha = _hasTarget ? 0.62f : Math.Clamp(_targetVisualTicks / 45f, 0f, 1f) * 0.55f;
                Color boxColor = _pendingAction == TapActionKind.AttackMonster
                    ? Color.OrangeRed * alpha
                    : Color.LimeGreen * alpha;

                int t = Math.Max(2, (int)(3 * scaleX));
                sb.Draw(pixelTex, new Rectangle(sx, sy, sw, t), boxColor);
                sb.Draw(pixelTex, new Rectangle(sx, sy + sh - t, sw, t), boxColor);
                sb.Draw(pixelTex, new Rectangle(sx, sy, t, sh), boxColor);
                sb.Draw(pixelTex, new Rectangle(sx + sw - t, sy, t, sh), boxColor);
            }
            catch { }
        }

        private static bool TargetStillNeedsTool(GameLocation loc, Point tile)
        {
            Vector2 v = new Vector2(tile.X, tile.Y);
            if (FindResourceClumpAt(loc, tile) != null) return true;
            if (loc.objects.TryGetValue(v, out var obj) && obj != null)
            {
                if (obj.IsBreakableStone() || obj.IsTwig() || obj.IsWeeds()) return true;
            }
            if (loc.terrainFeatures.TryGetValue(v, out var tf) && tf is Tree) return true;
            return false;
        }

        private static Monster? FindMonsterNear(GameLocation loc, Vector2 worldPos, float maxDist)
        {
            if (loc.characters == null) return null;
            float bestSq = maxDist * maxDist;
            Monster? best = null;
            for (int i = 0; i < loc.characters.Count; i++)
            {
                if (loc.characters[i] is Monster m && m.Health > 0 && !m.IsInvisible)
                {
                    float dSq = Vector2.DistanceSquared(worldPos, m.getStandingPosition());
                    if (dSq < bestSq)
                    {
                        bestSq = dSq;
                        best = m;
                    }
                }
            }
            return best;
        }

        private static bool HasFriendlyCharacterNear(GameLocation loc, Vector2 worldPos, Point tile)
        {
            if (loc.characters != null)
            {
                for (int i = 0; i < loc.characters.Count; i++)
                {
                    var c = loc.characters[i];
                    if (c != null && c is not Monster)
                    {
                        if (Vector2.DistanceSquared(worldPos, c.getStandingPosition()) <= 56f * 56f || c.TilePoint == tile)
                            return true;
                    }
                }
            }
            if (loc.animals != null)
            {
                foreach (var animal in loc.animals.Values)
                {
                    if (animal != null && Vector2.DistanceSquared(worldPos, animal.getStandingPosition()) <= 56f * 56f)
                        return true;
                }
            }
            return false;
        }

        private static bool IsDoorOrActionTile(GameLocation loc, Point tile)
        {
            try
            {
                if (loc.doesTileHaveProperty(tile.X, tile.Y, "Action", "Buildings") != null) return true;
                if (loc.doesTileHaveProperty(tile.X, tile.Y, "TouchAction", "Back") != null) return true;
                if (loc.isActionableTile(tile.X, tile.Y, Game1.player)) return true;
            }
            catch { }
            return false;
        }

        private static ResourceClump? FindResourceClumpAt(GameLocation loc, Point tile)
        {
            if (loc.resourceClumps == null) return null;
            for (int i = 0; i < loc.resourceClumps.Count; i++)
            {
                var rc = loc.resourceClumps[i];
                if (rc != null && rc.occupiesTile(tile.X, tile.Y))
                    return rc;
            }
            return null;
        }

        private static int FindToolSlot<T>(Farmer player) where T : Tool
        {
            int limit = Math.Min(player.MaxItems, player.Items.Count);
            for (int i = 0; i < limit; i++)
            {
                if (player.Items[i] is T)
                    return i;
            }
            return -1;
        }

        private static int FindScytheSlot(Farmer player)
        {
            int limit = Math.Min(player.MaxItems, player.Items.Count);
            for (int i = 0; i < limit; i++)
            {
                if (player.Items[i] is MeleeWeapon mw && mw.isScythe())
                    return i;
            }
            return -1;
        }

        private static int FindBestMeleeWeaponSlot(Farmer player)
        {
            int bestSlot = -1;
            int bestDamage = -1;
            int limit = Math.Min(player.MaxItems, player.Items.Count);
            for (int i = 0; i < limit; i++)
            {
                if (player.Items[i] is MeleeWeapon mw && !mw.isScythe())
                {
                    int dmg = mw.maxDamage.Value + mw.minDamage.Value;
                    if (dmg > bestDamage)
                    {
                        bestDamage = dmg;
                        bestSlot = i;
                    }
                }
            }
            return bestSlot;
        }

        private static bool IsAdjacent(Point a, Point b)
        {
            return Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1 && a != b;
        }

        private static bool IsAdjacentOrSame(Point a, Point b)
        {
            return Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1;
        }

        private static int GetFacingDirection(Point from, Point to)
        {
            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            if (Math.Abs(dx) > Math.Abs(dy))
                return dx > 0 ? 1 : 3;
            return dy >= 0 ? 2 : 0;
        }

        private static Point FindBestAdjacentWalkableTile(GameLocation loc, Point playerTile, Point targetTile)
        {
            Point[] offsets = new[]
            {
                new Point(0, 1),
                new Point(0, -1),
                new Point(-1, 0),
                new Point(1, 0),
                new Point(-1, 1),
                new Point(1, 1),
                new Point(-1, -1),
                new Point(1, -1)
            };

            Point best = targetTile;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < offsets.Length; i++)
            {
                Point cand = new Point(targetTile.X + offsets[i].X, targetTile.Y + offsets[i].Y);
                if (cand == playerTile) return playerTile;
                try
                {
                    var rect = new Rectangle(cand.X * 64 + 8, cand.Y * 64 + 8, 48, 48);
                    if (!loc.isCollidingPosition(rect, Game1.viewport, true, 0, false, Game1.player))
                    {
                        float dSq = Vector2.DistanceSquared(new Vector2(playerTile.X, playerTile.Y), new Vector2(cand.X, cand.Y));
                        if (dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            best = cand;
                        }
                    }
                }
                catch { }
            }
            return best;
        }
    }
}
