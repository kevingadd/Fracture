﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Event;
using Squared.Util.Text;

namespace Squared.PRGUI {
    public partial class UIContext : IDisposable {
        private class ScratchRenderTarget : IDisposable {
            public readonly UIContext Context;
            public readonly AutoRenderTarget Instance;
            public readonly UnorderedList<RectF> UsedRectangles = new UnorderedList<RectF>();
            public bool NeedClear = true;

            public ScratchRenderTarget (RenderCoordinator coordinator, UIContext context) {
                Context = context;
                Instance = new AutoRenderTarget(
                    coordinator, (int)context.CanvasSize.X, (int)context.CanvasSize.Y, 
                    false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8
                );
            }

            public void Update () {
                Instance.Resize((int)Context.CanvasSize.X, (int)Context.CanvasSize.Y);
            }

            public void Reset () {
                UsedRectangles.Clear();
                NeedClear = true;
            }

            public void Dispose () {
                Instance?.Dispose();
            }

            internal bool IsSpaceAvailable (ref RectF rectangle) {
                foreach (var used in UsedRectangles) {
                    if (used.Intersects(ref rectangle))
                        return false;
                }

                return true;
            }
        }

        private static readonly HashSet<Keys> SuppressRepeatKeys = new HashSet<Keys> {
            Keys.LeftAlt,
            Keys.LeftControl,
            Keys.LeftShift,
            Keys.RightAlt,
            Keys.RightControl,
            Keys.RightShift,
            Keys.Escape,
            Keys.CapsLock,
            Keys.Scroll,
            Keys.NumLock,
            Keys.Insert
        };

        /// <summary>
        /// Globally tracks whether text editing should be in insert or overwrite mode
        /// </summary>
        public bool TextInsertionMode = true;

        /// <summary>
        /// Tooltips will only appear after the mouse remains over a single control for this long (in seconds)
        /// </summary>
        public double TooltipAppearanceDelay = 0.3;
        /// <summary>
        /// If the mouse leaves tooltip-bearing controls for this long (in seconds) the tooltip appearance delay will reset
        /// </summary>
        public double TooltipDisappearDelay = 0.2;
        public float TooltipFadeDurationFast = 0.1f;
        public float TooltipFadeDuration = 0.2f;

        public float TooltipSpacing = 8;

        /// <summary>
        /// Double-clicks will only be tracked if this far apart or less (in seconds)
        /// </summary>
        public double DoubleClickWindowSize = 0.4;
        /// <summary>
        /// If the mouse is only moved this far (in pixels) it will be treated as no movement for the purposes of click detection
        /// </summary>
        public float MinimumMouseMovementDistance = 4;
        /// <summary>
        /// Mouse wheel movements are scaled by this amount
        /// </summary>
        public float MouseWheelScale = 1.0f / 2.4f;

        /// <summary>
        /// A key must be held for this long (in seconds) before repeating begins
        /// </summary>
        public double FirstKeyRepeatDelay = 0.4;
        /// <summary>
        /// Key repeating begins at this rate (in seconds)
        /// </summary>
        public double KeyRepeatIntervalSlow = 0.09;
        /// <summary>
        /// Key repeating accelerates to this rate (in seconds) over time
        /// </summary>
        public double KeyRepeatIntervalFast = 0.03;
        /// <summary>
        /// The key repeating rate accelerates over this period of time (in seconds)
        /// </summary>
        public double KeyRepeatAccelerationDelay = 4.5;

        /// <summary>
        /// Performance stats
        /// </summary>
        public int LastPassCount;

        /// <summary>
        /// Configures the size of the rendering canvas
        /// </summary>
        public Vector2 CanvasSize;

        /// <summary>
        /// Control events are broadcast on this bus
        /// </summary>
        public EventBus EventBus = new EventBus();

        /// <summary>
        /// The layout engine used to compute control sizes and positions
        /// </summary>
        public readonly LayoutContext Layout = new LayoutContext();

        /// <summary>
        /// Configures the appearance and size of controls
        /// </summary>
        public IDecorationProvider Decorations;

        /// <summary>
        /// The top-level controls managed by the layout engine. Each one gets a separate rendering layer
        /// </summary>
        public ControlCollection Controls { get; private set; }

        private Control _Focused, _MouseCaptured, _Hovering;

        private ConditionalWeakTable<Control, Control> TopLevelFocusMemory = new ConditionalWeakTable<Control, Control>();

        /// <summary>
        /// The control that currently has the mouse captured (if a button is pressed)
        /// </summary>
        public Control MouseCaptured {
            get => _MouseCaptured;
            private set {
                if ((value != null) && !value.AcceptsMouseInput)
                    throw new InvalidOperationException("Control cannot accept mouse input");
                var previous = _MouseCaptured;
                _MouseCaptured = value;
                // Console.WriteLine($"Mouse capture {previous} -> {value}");
            }
        }

        /// <summary>
        /// The control currently underneath the mouse cursor, as long as the mouse is not captured by another control
        /// </summary>
        public Control Hovering {
            get => _Hovering;
            private set {
                var previous = _Hovering;
                _Hovering = value;
                if (previous != value)
                    HandleHoverTransition(previous, value);
            }
        }

        /// <summary>
        /// The control currently underneath the mouse cursor
        /// </summary>
        public Control MouseOver { get; private set; }

        /// <summary>
        /// The control that currently has keyboard input focus
        /// </summary>
        public Control Focused {
            get => _Focused;
            set {
                if (!TrySetFocus(value, false))
                    TrySetFocus(null, true);
            }
        }

        public DefaultMaterialSet Materials { get; private set; }
        public ITimeProvider TimeProvider;

        private MouseButtons CurrentMouseButtons, LastMouseButtons;
        private float LastMouseWheelValue;
        private KeyboardModifiers CurrentModifiers;

        internal Vector2 LastMousePosition;
        private Vector2? MouseDownPosition;
        private KeyboardState LastKeyboardState;
        private bool SuppressNextCaptureLoss = false;
        private Control ReleasedCapture = null;

        private Vector2 LastClickPosition;
        private Control LastClickTarget;
        private double LastMouseDownTime, LastClickTime;
        private int SequentialClickCount;

        private Keys LastKeyEvent;
        private double LastKeyEventFirstTime, LastKeyEventTime;
        private double? FirstTooltipHoverTime;
        private double LastTooltipHoverTime;

        private Tooltip CachedTooltip;
        private Control PreviousTooltipAnchor;
        private bool IsTooltipVisible;
        private Controls.StaticText CachedCompositionPreview;

        private UnorderedList<ScratchRenderTarget> ScratchRenderTargets = new UnorderedList<ScratchRenderTarget>();
        private readonly static Dictionary<int, DepthStencilState> StencilEraseStates = new Dictionary<int, DepthStencilState>();
        private readonly static Dictionary<int, DepthStencilState> StencilWriteStates = new Dictionary<int, DepthStencilState>();
        private readonly static Dictionary<int, DepthStencilState> StencilTestStates = new Dictionary<int, DepthStencilState>();

        private bool IsTextInputRegistered = false;
        private bool IsCompositionActive = false;

        private float Now => (float)TimeProvider.Seconds;
        private long NowL => TimeProvider.Ticks;

        internal DepthStencilState GetStencilRestore (int targetReferenceStencil) {
            DepthStencilState result;
            if (StencilEraseStates.TryGetValue(targetReferenceStencil, out result))
                return result;

            result = new DepthStencilState {
                StencilEnable = true,
                StencilFunction = CompareFunction.Less,
                StencilPass = StencilOperation.Replace,
                StencilFail = StencilOperation.Keep,
                ReferenceStencil = targetReferenceStencil,
                DepthBufferEnable = false
            };

            StencilEraseStates[targetReferenceStencil] = result;
            return result;
        }

        internal DepthStencilState GetStencilWrite (int previousReferenceStencil) {
            DepthStencilState result;
            if (StencilWriteStates.TryGetValue(previousReferenceStencil, out result))
                return result;

            result = new DepthStencilState {
                StencilEnable = true,
                StencilFunction = CompareFunction.Equal,
                StencilPass = StencilOperation.IncrementSaturation,
                StencilFail = StencilOperation.Keep,
                ReferenceStencil = previousReferenceStencil,
                DepthBufferEnable = false
            };

            StencilWriteStates[previousReferenceStencil] = result;
            return result;
        }

        internal DepthStencilState GetStencilTest (int referenceStencil) {
            DepthStencilState result;
            if (StencilTestStates.TryGetValue(referenceStencil, out result))
                return result;

            result = new DepthStencilState {
                StencilEnable = true,
                StencilFunction = CompareFunction.LessEqual,
                StencilPass = StencilOperation.Keep,
                StencilFail = StencilOperation.Keep,
                ReferenceStencil = referenceStencil,
                StencilWriteMask = 0,
                DepthBufferEnable = false
            };

            StencilTestStates[referenceStencil] = result;
            return result;
        }

        public UIContext (DefaultMaterialSet materials, IGlyphSource font = null, ITimeProvider timeProvider = null)
            : this (
                materials: materials,
                decorations: new DefaultDecorations {
                    DefaultFont = font
                },
                timeProvider: timeProvider
            ) {
        }

        public UIContext (DefaultMaterialSet materials, IDecorationProvider decorations, ITimeProvider timeProvider = null) {
            Controls = new ControlCollection(this);
            Decorations = decorations;
            TimeProvider = TimeProvider ?? new DotNetTimeProvider();
            Materials = materials;
        }

        private Tooltip GetTooltipInstance () {
            if (CachedTooltip == null) {
                CachedTooltip = new Tooltip {
                    Opacity = 0
                };
                Controls.Add(CachedTooltip);
            }

            return CachedTooltip;
        }

        private Controls.StaticText GetCompositionPreviewInstance () {
            if (CachedCompositionPreview == null) {
                CachedCompositionPreview = new Controls.StaticText {
                    PaintOrder = 9999,
                    Wrap = false,
                    Multiline = false,
                    Intangible = true,
                    LayoutFlags = ControlFlags.Layout_Floating,
                    BackgroundColor = Color.White,
                    TextColor = Color.Black,
                    CustomDecorations = Decorations.CompositionPreview
                };
                Controls.Add(CachedCompositionPreview);
            }

            return CachedCompositionPreview;
        }

        public Control CaptureMouse (Control target) {
            var donor = Focused;
            if ((MouseCaptured != null) && (MouseCaptured != target) && (LastMouseButtons == MouseButtons.None))
                SuppressNextCaptureLoss = true;
            // HACK: If we used IsValidFocusTarget here, it would break scenarios where a control is capturing
            //  focus before being shown or being enabled
            if (target.AcceptsFocus)
                TrySetFocus(target, true);
            MouseCaptured = target;
            return donor;
        }

        public void ReleaseCapture (Control target, Control focusDonor) {
            if (Focused == target)
                Focused = focusDonor;
            if (Hovering == target)
                Hovering = null;
            if (MouseCaptured == target)
                MouseCaptured = null;
            ReleasedCapture = target;
        }

        public void UpdateLayout () {
            var context = MakeOperationContext();

            Layout.Clear();

            Layout.SetFixedSize(Layout.Root, CanvasSize);
            Layout.SetContainerFlags(Layout.Root, ControlFlags.Container_Row | ControlFlags.Container_Constrain_Size);

            foreach (var control in Controls)
                control.GenerateLayoutTree(context, Layout.Root);

            Layout.Update();
        }

        private void UpdateCaptureAndHovering (Vector2 mousePosition, Control exclude = null) {
            MouseOver = HitTest(mousePosition);
            if ((MouseOver != MouseCaptured) && (MouseCaptured != null))
                Hovering = null;
            else
                Hovering = MouseOver;
        }

        public void UpdateInput (
            MouseState mouseState, KeyboardState keyboardState,
            Vector2? mouseOffset = null
        ) {
            var previouslyHovering = Hovering;
            if ((Focused != null) && !Focused.IsValidFocusTarget)
                Focused = null;

            CurrentMouseButtons = ((mouseState.LeftButton == ButtonState.Pressed) ? MouseButtons.Left : MouseButtons.None) |
                ((mouseState.MiddleButton == ButtonState.Pressed) ? MouseButtons.Middle : MouseButtons.None) |
                ((mouseState.RightButton == ButtonState.Pressed) ? MouseButtons.Right : MouseButtons.None);

            var mousePosition = new Vector2(mouseState.X, mouseState.Y) + (mouseOffset ?? Vector2.Zero);
            var mouseWheelValue = mouseState.ScrollWheelValue * MouseWheelScale;

            UpdateCaptureAndHovering(mousePosition);
            var mouseEventTarget = MouseCaptured ?? Hovering;

            ProcessKeyboardState(ref LastKeyboardState, ref keyboardState);

            if (LastMousePosition != mousePosition) {
                if (CurrentMouseButtons != MouseButtons.None)
                    HandleMouseDrag(mouseEventTarget, mousePosition);
                else
                    HandleMouseMove(mouseEventTarget, mousePosition);
            }

            var previouslyCaptured = MouseCaptured;
            var processClick = false;

            if ((LastMouseButtons == MouseButtons.None) && (CurrentMouseButtons != MouseButtons.None)) {
                // FIXME: This one should probably always be Hovering
                HandleMouseDown(mouseEventTarget, mousePosition);
            } else if ((LastMouseButtons != MouseButtons.None) && (CurrentMouseButtons == MouseButtons.None)) {
                if (Hovering != null)
                    HandleMouseUp(mouseEventTarget, mousePosition);

                if (MouseCaptured != null)
                    processClick = true;

                if (!SuppressNextCaptureLoss)
                    MouseCaptured = null;
                else
                    SuppressNextCaptureLoss = false;
            } else if (LastMouseButtons != CurrentMouseButtons) {
                FireEvent(UIEvents.MouseButtonsChanged, mouseEventTarget, MakeMouseEventArgs(mouseEventTarget, mousePosition));
            }

            if (processClick) {
                // FIXME: if a menu is opened by a mousedown event, this will
                //  fire a click on the menu in response to its mouseup
                if (Hovering == previouslyCaptured)
                    HandleClick(previouslyCaptured, mousePosition);
                else
                    HandleDrag(previouslyCaptured, Hovering);
            }

            var mouseWheelDelta = mouseWheelValue - LastMouseWheelValue;

            if (mouseWheelDelta != 0)
                HandleScroll(previouslyCaptured ?? Hovering, mouseWheelDelta);

            UpdateTooltip((CurrentMouseButtons != MouseButtons.None));

            LastMouseButtons = CurrentMouseButtons;
            LastMouseWheelValue = mouseWheelValue;
            LastKeyboardState = keyboardState;
            LastMousePosition = mousePosition;
        }

        private bool IsTooltipActive {
            get {
                return (CachedTooltip != null) && CachedTooltip.Visible;
            }
        }

        private void ResetTooltipShowTimer () {
            FirstTooltipHoverTime = null;
        }

        private void UpdateTooltip (bool leftButtonPressed) {
            if (leftButtonPressed)
                return;

            var now = Now;
            var tooltipContent = default(AbstractString);
            if (Hovering != null)
                tooltipContent = Hovering.TooltipContent.Get(Hovering);

            if (!tooltipContent.IsNull) {
                if (!FirstTooltipHoverTime.HasValue)
                    FirstTooltipHoverTime = now;

                if (IsTooltipActive)
                    LastTooltipHoverTime = now;

                var hoveringFor = now - FirstTooltipHoverTime;
                var disappearTimeout = now - LastTooltipHoverTime;

                if ((hoveringFor >= TooltipAppearanceDelay) || (disappearTimeout < TooltipDisappearDelay))
                    ShowTooltip(Hovering, tooltipContent);
            } else {
                var shouldDismissInstantly = (Hovering != null) && IsTooltipActive && GetTooltipInstance().GetRect(Layout).Contains(LastMousePosition);
                // TODO: Instead of instantly hiding, maybe just fade the tooltip out partially?
                HideTooltip(shouldDismissInstantly);

                var elapsed = now - LastTooltipHoverTime;
                if (elapsed >= TooltipDisappearDelay)
                    ResetTooltipShowTimer();
            }
        }

        private void ProcessKeyboardState (ref KeyboardState previous, ref KeyboardState current) {
            CurrentModifiers = new KeyboardModifiers {
                LeftControl = current.IsKeyDown(Keys.LeftControl),
                RightControl = current.IsKeyDown(Keys.RightControl),
                LeftShift = current.IsKeyDown(Keys.LeftShift),
                RightShift = current.IsKeyDown(Keys.RightShift),
                LeftAlt = current.IsKeyDown(Keys.LeftAlt),
                RightAlt = current.IsKeyDown(Keys.RightAlt),
            };

            if (IsCompositionActive)
                return;

            var now = Now;
            for (int i = 0; i < 255; i++) {
                var key = (Keys)i;

                bool shouldFilterKeyPress = false;
                var wasPressed = previous.IsKeyDown(key);
                var isPressed = current.IsKeyDown(key);

                if (isPressed || wasPressed) {
                    // Clumsily filter out keys that would generate textinput events
                    if (!CurrentModifiers.Control && !CurrentModifiers.Alt) {
                        if ((key >= Keys.D0) && (key <= Keys.Z))
                            shouldFilterKeyPress = true;
                        else if ((key >= Keys.NumPad0) && (key <= Keys.Divide))
                            shouldFilterKeyPress = true;
                        else if ((key >= Keys.OemSemicolon) && (key <= Keys.OemBackslash))
                            shouldFilterKeyPress = true;
                    }
                }

                if (isPressed != wasPressed) {
                    HandleKeyEvent(isPressed ? UIEvents.KeyDown : UIEvents.KeyUp, key, null);

                    if (isPressed && !shouldFilterKeyPress) {
                        LastKeyEvent = key;
                        LastKeyEventTime = now;
                        LastKeyEventFirstTime = now;
                        HandleKeyEvent(UIEvents.KeyPress, key, null);
                    }
                } else if (isPressed && (LastKeyEvent == key)) {
                    double repeatSpeed = Arithmetic.Lerp(KeyRepeatIntervalSlow, KeyRepeatIntervalFast, (float)((now - LastKeyEventFirstTime) / KeyRepeatAccelerationDelay));
                    if (
                        ((now - LastKeyEventFirstTime) >= FirstKeyRepeatDelay) &&
                        ((now - LastKeyEventTime) >= repeatSpeed) &&
                        !SuppressRepeatKeys.Contains(key) && 
                        !shouldFilterKeyPress
                    ) {
                        LastKeyEventTime = now;
                        HandleKeyEvent(UIEvents.KeyPress, key, null);
                    }
                }
            }
        }

        private void HideTooltipForMouseInput () {
            ResetTooltipShowTimer();
            HideTooltip(true);
            FirstTooltipHoverTime = null;
            LastTooltipHoverTime = 0;
        }

        private void HideTooltip (bool instant) {
            if (CachedTooltip == null)
                return;

            if (instant)
                CachedTooltip.Opacity = 0;
            else if (IsTooltipVisible)
                CachedTooltip.Opacity = Tween<float>.StartNow(CachedTooltip.Opacity.Get(Now), 0, now: NowL, seconds: TooltipFadeDuration);
            IsTooltipVisible = false;
        }

        /// <summary>
        /// Use at your own risk! Performs immediate layout of a control and its children.
        /// The results of this are not necessarily accurate, but can be used to infer its ideal size for positioning.
        /// </summary>
        public void UpdateSubtreeLayout (Control subtreeRoot) {
            ControlKey parentKey;
            Control parent;
            if (!subtreeRoot.TryGetParent(out parent))
                parentKey = Layout.Root;
            else if (!parent.LayoutKey.IsInvalid)
                parentKey = parent.LayoutKey;
            else {
                // Just in case for some reason the control's parent also hasn't had layout happen...
                UpdateSubtreeLayout(parent);
                return;
            }

            var tempCtx = MakeOperationContext();
            subtreeRoot.GenerateLayoutTree(tempCtx, parentKey, subtreeRoot.LayoutKey.IsInvalid ? (ControlKey?)null : subtreeRoot.LayoutKey);
            Layout.UpdateSubtree(subtreeRoot.LayoutKey);
        }

        private void ShowTooltip (Control anchor, AbstractString text) {
            var instance = GetTooltipInstance();

            var textChanged = !instance.Text.Equals(text);

            var rect = Layout.GetRect(anchor.LayoutKey);

            if (textChanged || !IsTooltipVisible) {
                var idealMaxWidth = CanvasSize.X * 0.35f;

                instance.Text = text;
                // FIXME: Shift it around if it's already too close to the right side
                instance.MaximumWidth = idealMaxWidth;
                instance.Invalidate();

                UpdateSubtreeLayout(instance);
            }

            var instanceBox = instance.GetRect(Layout);
            var newX = Arithmetic.Clamp(rect.Left + (rect.Width / 2f) - (instanceBox.Width / 2f), TooltipSpacing, CanvasSize.X - instanceBox.Width - (TooltipSpacing * 2));
            var newY = rect.Extent.Y + TooltipSpacing;
            if ((instanceBox.Height + rect.Extent.Y) >= CanvasSize.Y)
                newY = rect.Top - instanceBox.Height - TooltipSpacing;
            instance.Margins = new Margins(newX, newY, 0, 0);

            var currentOpacity = instance.Opacity.Get(Now);
            if (!IsTooltipVisible)
                instance.Opacity = Tween<float>.StartNow(currentOpacity, 1f, (currentOpacity > 0.1 ? TooltipFadeDurationFast : TooltipFadeDuration), now: NowL);
            if ((anchor != PreviousTooltipAnchor) && (currentOpacity > 0))
                instance.Opacity = 1f;

            PreviousTooltipAnchor = anchor;
            IsTooltipVisible = true;
            UpdateSubtreeLayout(instance);
        }

        // Position is relative to the top-left corner of the canvas
        public Control HitTest (Vector2 position, bool acceptsMouseInputOnly = false, bool acceptsFocusOnly = false) {
            var sorted = Controls.InPaintOrder();
            for (var i = sorted.Count - 1; i >= 0; i--) {
                var control = sorted[i];
                var result = control.HitTest(Layout, position, acceptsMouseInputOnly, acceptsFocusOnly);
                if (result != null)
                    return result;
            }

            return null;
        }

        private UIOperationContext MakeOperationContext () {
            return new UIOperationContext {
                UIContext = this,
                Now = Now,
                Modifiers = CurrentModifiers,
                MouseButtonHeld = (LastMouseButtons != MouseButtons.None),
                MousePosition = LastMousePosition
            };
        }

        internal AutoRenderTarget GetScratchRenderTarget (RenderCoordinator coordinator, ref RectF rectangle, out bool needClear) {
            ScratchRenderTarget result = null;

            foreach (var rt in ScratchRenderTargets) {
                if (rt.IsSpaceAvailable(ref rectangle)) {
                    result = rt;
                    break;
                }
            }

            if (result == null) {
                result = new ScratchRenderTarget(coordinator, this);
                ScratchRenderTargets.Add(result);
            }

            needClear = result.NeedClear;
            result.NeedClear = false;
            result.UsedRectangles.Add(ref rectangle);
            return result.Instance;
        }

        internal void ReleaseScratchRenderTarget (AutoRenderTarget rt) {
            // FIXME: Do we need to do anything here?
        }

        public void Rasterize (Frame frame, AutoRenderTarget renderTarget, int layer, int prepassLayer) {
            var context = MakeOperationContext();

            foreach (var srt in ScratchRenderTargets)
                srt.Reset();

            using (var prepassGroup = BatchGroup.New(frame, prepassLayer))
            using (var rtBatch = BatchGroup.ForRenderTarget(frame, layer, renderTarget)) {
                var prepass = new ImperativeRenderer(prepassGroup, Materials);
                var renderer = new ImperativeRenderer(rtBatch, Materials) {
                    BlendState = BlendState.AlphaBlend
                };
                renderer.Clear(color: Color.Transparent, stencil: 0, layer: -999);

                var seq = Controls.InPaintOrder();
                foreach (var control in seq) {
                    // HACK: Each top-level control is its own group of passes. This ensures that they cleanly
                    //  overlap each other, at the cost of more draw calls.
                    var passSet = new RasterizePassSet(ref prepass, ref renderer, 0, 1);
                    passSet.Below.DepthStencilState =
                        passSet.Content.DepthStencilState =
                        passSet.Above.DepthStencilState = DepthStencilState.None;
                    control.Rasterize(context, ref passSet);
                    // HACK
                    prepass = passSet.Prepass;
                }

                LastPassCount = prepassGroup.Count + 1;
            }
        }

        public void Dispose () {
            Layout.Dispose();

            foreach (var rt in ScratchRenderTargets)
                rt.Dispose();

            ScratchRenderTargets.Clear();
        }
    }

    public struct RasterizePassSet {
        public ImperativeRenderer Prepass, Below, Content, Above;
        public int ReferenceStencil, NextReferenceStencil;

        public RasterizePassSet (ref ImperativeRenderer prepass, ref ImperativeRenderer container, int referenceStencil, int nextReferenceStencil) {
            Prepass = prepass;
            Below = container.MakeSubgroup();
            Content = container.MakeSubgroup();
            Above = container.MakeSubgroup();
            ReferenceStencil = referenceStencil;
            NextReferenceStencil = nextReferenceStencil;
        }
    }

    public class UIOperationContext {
        public UIContext UIContext;
        public DefaultMaterialSet Materials => UIContext.Materials;
        public IDecorationProvider DecorationProvider => UIContext.Decorations;
        public LayoutContext Layout => UIContext.Layout;
        public RasterizePasses Pass;
        public float Now { get; internal set; }
        public KeyboardModifiers Modifiers { get; internal set; }
        public bool MouseButtonHeld { get; internal set; }
        public Vector2 MousePosition { get; internal set; }

        public UIOperationContext Clone () {
            return new UIOperationContext {
                UIContext = UIContext,
                Pass = Pass,
                Now = Now,
                Modifiers = Modifiers,
                MouseButtonHeld = MouseButtonHeld,
                MousePosition = MousePosition
            };
        }
    }
}
