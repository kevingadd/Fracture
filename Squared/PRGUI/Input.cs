﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Decorations;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.PRGUI.Input {
    public interface IInputSource {
        void SetContext (UIContext context);
        void Update (ref InputState previous, ref InputState current);
        void SetTextInputState (bool enabled);
        void Rasterize (UIOperationContext context, ref ImperativeRenderer renderer);
    }

    public struct KeyboardModifiers {
        public bool Control => LeftControl || RightControl;
        public bool Shift => LeftShift || RightShift;
        public bool Alt => LeftAlt || RightAlt;

        public bool LeftControl, RightControl, LeftShift, RightShift, LeftAlt, RightAlt;
    }

    public struct InputState {
        public List<Keys> HeldKeys;
        public Vector2 CursorPosition;
        public Vector2 ScrollDistance;
        public MouseButtons Buttons;
        public KeyboardModifiers Modifiers;
        public float WheelValue;
        public bool AreAnyKeysHeld, ActivateKeyHeld, KeyboardNavigationEnded;
    }

    public class KeyboardInputSource : IInputSource {
        public KeyboardState PreviousState, CurrentState;

        bool IsTextInputRegistered;
        Keys LastKeyEvent;
        double LastKeyEventFirstTime, LastKeyEventTime;
        UIContext Context;

        public KeyboardInputSource () {
            PreviousState = CurrentState = Keyboard.GetState();
        }

        public void SetContext (UIContext context) {
            if ((context != Context) && (Context != null))
                throw new InvalidOperationException("This source has already been used with another context");
            Context = context;
        }

        public void Update (ref InputState previous, ref InputState current) {
            PreviousState = CurrentState;
            var ks = CurrentState = Keyboard.GetState();

            current.ActivateKeyHeld |= ks.IsKeyDown(Keys.Space);
            bool lctrl = ks.IsKeyDown(Keys.LeftControl),
                rctrl = ks.IsKeyDown(Keys.RightControl),
                lalt = ks.IsKeyDown(Keys.LeftAlt),
                ralt = ks.IsKeyDown(Keys.RightAlt);
            current.Modifiers.LeftControl |= lctrl;
            current.Modifiers.RightControl |= rctrl;
            current.Modifiers.LeftShift |= ks.IsKeyDown(Keys.LeftShift);
            current.Modifiers.RightShift |= ks.IsKeyDown(Keys.RightShift);
            current.Modifiers.LeftAlt |= lalt;
            current.Modifiers.RightAlt |= ralt;

            if (Context.IsCompositionActive)
                return;

            var now = Context.Now;
            for (int i = 0; i < 255; i++) {
                var key = (Keys)i;

                bool shouldFilterKeyPress = false;
                var wasPressed = PreviousState.IsKeyDown(key);
                var isPressed = ks.IsKeyDown(key);
                if (isPressed)
                    current.HeldKeys.Add(key);

                if (isPressed || wasPressed) {
                    // Clumsily filter out keys that would generate textinput events
                    if (!lctrl && !rctrl && !lalt && !ralt) {
                        if ((key >= Keys.D0) && (key <= Keys.Z))
                            shouldFilterKeyPress = true;
                        else if ((key >= Keys.NumPad0) && (key <= Keys.Divide))
                            shouldFilterKeyPress = true;
                        else if ((key >= Keys.OemSemicolon) && (key <= Keys.OemBackslash))
                            shouldFilterKeyPress = true;
                    }
                }

                if (isPressed != wasPressed) {
                    Context.HandleKeyEvent(isPressed ? UIEvents.KeyDown : UIEvents.KeyUp, key, null);

                    if (isPressed && !shouldFilterKeyPress) {
                        // Modifier keys shouldn't break an active key repeat (i.e. you should be able to press/release shift)
                        if (!ks.IsKeyDown(LastKeyEvent) || !UIContext.ModifierKeys.Contains(key))
                            LastKeyEvent = key;

                        LastKeyEventTime = LastKeyEventFirstTime = now;
                        Context.HandleKeyEvent(UIEvents.KeyPress, key, null);
                    }
                } else if (isPressed && (LastKeyEvent == key)) {
                    if (
                        !UIContext.SuppressRepeatKeys.Contains(key) && 
                        !UIContext.ModifierKeys.Contains(key) &&
                        !shouldFilterKeyPress &&
                        Context.UpdateRepeat(now, LastKeyEventFirstTime, ref LastKeyEventTime)
                    ) {
                        Context.HandleKeyEvent(UIEvents.KeyPress, key, null);
                    }
                }
            }
        }

        public void SetTextInputState (bool enabled) {
            if (!enabled) {
                TextInputEXT.StopTextInput();
                return;
            }

            if (!IsTextInputRegistered) {
                IsTextInputRegistered = true;
                TextInputEXT.TextInput += TextInputEXT_TextInput;
                TextInputEXT.TextEditing += TextInputEXT_TextEditing;
            }
                TextInputEXT.StartTextInput();
        }

        private void TextInputEXT_TextInput (char ch) {
            // Control characters will be handled through the KeyboardState path
            if (char.IsControl(ch))
                return;

            Context.HandleKeyEvent(UIEvents.KeyPress, null, ch);
        }

        private void TextInputEXT_TextEditing (string text, int cursorPosition, int length) {
            if ((text == null) || (text.Length == 0)) {
                Context.TerminateComposition();
                return;
            }

            Context.UpdateComposition(text, cursorPosition, length);
        }

        public void Rasterize (UIOperationContext context, ref ImperativeRenderer renderer) {
        }
    }

    public class MouseInputSource : IInputSource {
        /// <summary>
        /// Mouse wheel movements are scaled by this amount
        /// </summary>
        public float MouseWheelScale = 1.0f / 2.4f;
        /// <summary>
        /// The mouse position is offset by this distance
        /// </summary>
        public Vector2 Offset;

        public MouseState PreviousState, CurrentState;
        private bool HasState;
        UIContext Context;

        public MouseInputSource () {
        }

        public void SetContext (UIContext context) {
            if ((context != Context) && (Context != null))
                throw new InvalidOperationException("This source has already been used with another context");
            Context = context;
        }

        public void Update (ref InputState previous, ref InputState current) {
            PreviousState = CurrentState;
            var mouseState = CurrentState = Mouse.GetState();
            if (!HasState)
                PreviousState = CurrentState;

            current.Buttons |= ((mouseState.LeftButton == ButtonState.Pressed) ? MouseButtons.Left : MouseButtons.None);
            current.Buttons |= ((mouseState.MiddleButton == ButtonState.Pressed) ? MouseButtons.Middle : MouseButtons.None);
            current.Buttons |= ((mouseState.RightButton == ButtonState.Pressed) ? MouseButtons.Right : MouseButtons.None);

            var prevPosition = new Vector2(PreviousState.X, PreviousState.Y) + Offset;
            if (PreviousState.ScrollWheelValue != CurrentState.ScrollWheelValue)
                current.WheelValue = mouseState.ScrollWheelValue * MouseWheelScale;

            if (!HasState) {
                HasState = true;
                return;
            }

            if ((CurrentState.X != PreviousState.X) || (CurrentState.Y != PreviousState.Y)) {
                current.CursorPosition = new Vector2(mouseState.X, mouseState.Y) + Offset;
                current.KeyboardNavigationEnded = true;
                Context.PromoteInputSource(this);
            }
        }

        public void SetTextInputState (bool enabled) {
        }

        public void Rasterize (UIOperationContext context, ref ImperativeRenderer renderer) {
        }
    }

    public class GamepadVirtualKeyboardAndCursor : IInputSource {
        public float SlowPxPerSecond = 96f,
            FastPxPerSecond = 1024f;
        public float AccelerationExponent = 1.75f,
            Deadzone = 0.05f;

        public GamePadState PreviousState, CurrentState;
        public PlayerIndex PlayerIndex;
        public bool EnableButtons = true,
            EnableStick = true;
        long PreviousUpdateTime;
        UIContext Context;
        Control SnapToControl;
        bool GenerateKeyPressForActivation = false;

        Keys LastKeyEvent;
        double LastKeyEventFirstTime, LastKeyEventTime;

        public GamepadVirtualKeyboardAndCursor (PlayerIndex playerIndex = PlayerIndex.One) {
            PlayerIndex = playerIndex;
            PreviousState = CurrentState = GamePad.GetState(PlayerIndex);
        }

        public void SetContext (UIContext context) {
            if ((context != Context) && (Context != null))
                throw new InvalidOperationException("This source has already been used with another context");
            Context = context;
        }

        private void ProcessStick (Vector2 stick, out float speed, out Vector2 direction) {
            var length = stick.Length();
            if ((length >= Deadzone) && EnableStick) {
                var ramp = Arithmetic.Saturate((float)Math.Pow(length - Deadzone, AccelerationExponent));
                speed = Arithmetic.Lerp(
                    SlowPxPerSecond, FastPxPerSecond, ramp
                );
                direction = stick * new Vector2(1, -1);
                direction.Normalize();
            } else {
                direction = Vector2.Zero;
                speed = 0f;
            }
        }

        private bool IsValidHoverTarget (Control hovering) {
            if (hovering == null)
                return false;

            // FIXME: Does focus beneficiary work if mouse input is disabled?
            return hovering.AcceptsMouseInput || (hovering.FocusBeneficiary != null);
        }

        private FuzzyHitTest FuzzyHitTest = new FuzzyHitTest();

        public void Update (ref InputState previous, ref InputState current) {
            PreviousState = CurrentState;
            var gs = CurrentState = GamePad.GetState(PlayerIndex);
            var now = Context.NowL;

            Vector2? newPosition = null;

            if (current.KeyboardNavigationEnded)
                SnapToControl = null;

            var elapsed = (float)((now - PreviousUpdateTime) / (double)Time.SecondInTicks);

            ProcessStick(PreviousState.ThumbSticks.Left, out float cursorSpeed, out Vector2 cursorDirection);
            ProcessStick(PreviousState.ThumbSticks.Right, out float scrollSpeed, out Vector2 scrollDirection);

            if (cursorSpeed > 0) {
                var motion = cursorSpeed * cursorDirection * elapsed;
                newPosition = new Vector2(
                    current.CursorPosition.X + motion.X,
                    current.CursorPosition.Y + motion.Y
                );
                current.KeyboardNavigationEnded = true;
                GenerateKeyPressForActivation = false;
                SnapToControl = null;
            }

            var focusedModal = Context.Focused as IModal;
            var effectiveSnapTarget = SnapToControl;

            if ((SnapToControl != null) && (Context.Focused != SnapToControl)) {
                if (focusedModal?.FocusDonor == SnapToControl)
                    effectiveSnapTarget = Context.Focused;
                else if (GenerateKeyPressForActivation)
                    SnapToControl = Context.Focused;
                else
                    SnapToControl = null;
            }

            if (scrollSpeed > 0) {
                var motion = scrollSpeed * scrollDirection * elapsed;
                current.ScrollDistance += motion;
                // FIXME: It would be ideal if this didn't need to happen, but scrolling 
                //  a listbox will cause very strange snap behavior as its selected item
                //  leaves or enters the view
                SnapToControl = null;
            }

            if (EnableButtons) {
                if (Context.Focused != null) {
                    if (Context.Focused.GetRect().Contains(current.CursorPosition))
                        current.ActivateKeyHeld |= (gs.Buttons.A == ButtonState.Pressed);
                }

                var mods = new KeyboardModifiers {
                    LeftControl = (gs.Buttons.Back == ButtonState.Pressed)
                };
                var shift = mods;
                shift.LeftShift = true;

                if (GenerateKeyPressForActivation) {
                    DispatchKeyEventsForButton(ref current, Keys.Space, mods, PreviousState.Buttons.A, gs.Buttons.A);
                } else {
                    if (gs.Buttons.A == ButtonState.Pressed)
                        current.Buttons |= MouseButtons.Left;
                }

                DispatchKeyEventsForButton(ref current, Keys.Escape, mods, PreviousState.Buttons.B, gs.Buttons.B);
                var wasArrowPressed = DispatchKeyEventsForButton(ref current, Keys.Up, mods, PreviousState.DPad.Up, gs.DPad.Up);
                wasArrowPressed |= DispatchKeyEventsForButton(ref current, Keys.Down, mods, PreviousState.DPad.Down, gs.DPad.Down);
                wasArrowPressed |= DispatchKeyEventsForButton(ref current, Keys.Left, mods, PreviousState.DPad.Left, gs.DPad.Left);
                wasArrowPressed |= DispatchKeyEventsForButton(ref current, Keys.Right, mods, PreviousState.DPad.Right, gs.DPad.Right);
                var focusChanged = DispatchKeyEventsForButton(ref current, Keys.Tab, shift, PreviousState.Buttons.LeftShoulder, gs.Buttons.LeftShoulder);
                focusChanged |= DispatchKeyEventsForButton(ref current, Keys.Tab, mods, PreviousState.Buttons.RightShoulder, gs.Buttons.RightShoulder);
                DispatchKeyEventsForButton(ref current, Keys.Apps, mods, PreviousState.Buttons.Y, gs.Buttons.Y);

                if (focusChanged)
                    SnapToControl = Context.Focused;

                if (focusChanged || wasArrowPressed)
                    GenerateKeyPressForActivation = true;
            }

            if (effectiveSnapTarget != null) {
                // Controls like menus update their selected item when the cursor moves over them,
                //  so if possible when performing a cursor snap (for pad input) we want to snap to
                //  a point on top of the current selection to avoid changing it, instead of the center
                //  of the new snap target
                var sb = effectiveSnapTarget as ISelectionBearer;
                var sc = sb?.SelectionRect;
                var targetRect = effectiveSnapTarget.GetRect(contentRect: true);
                if (sc.HasValue && sc.Value.Intersection(ref targetRect, out RectF union)) {
                    newPosition = union.Center;
                } else
                    newPosition = targetRect.Center;
            }

            Control hovering = Context.Hovering, mouseOverTarget = hovering;
            if (!IsValidHoverTarget(hovering)) {
                FuzzyHitTest.Run(Context, newPosition ?? current.CursorPosition);
                mouseOverTarget = null;
            }

            if (newPosition != null) {
                var x = Arithmetic.Clamp(newPosition.Value.X, 0, Context.CanvasSize.X);
                var y = Arithmetic.Clamp(newPosition.Value.Y, 0, Context.CanvasSize.Y);
                current.CursorPosition = new Vector2(x, y);
                Context.PromoteInputSource(this);
            }

            PreviousUpdateTime = now;
        }

        private bool DispatchKeyEventsForButton (ref InputState state, Keys key, ButtonState previous, ButtonState current) {
            return DispatchKeyEventsForButton(ref state, key, null, previous, current);
        }

        private bool DispatchKeyEventsForButton (ref InputState state, Keys key, KeyboardModifiers? modifiers, ButtonState previous, ButtonState current) {
            var held = current == ButtonState.Pressed;
            if (held)
                state.HeldKeys.Add(key);

            if (previous == current) {
                if (
                    held &&
                    (LastKeyEvent == key) &&
                    Context.UpdateRepeat(Context.Now, LastKeyEventFirstTime, ref LastKeyEventTime)
                ) {
                    LastKeyEventTime = Context.Now;
                    return Context.HandleKeyEvent(UIEvents.KeyPress, key, null, modifiers);
                } else
                    return false;
            } else if (held) {
                LastKeyEventTime = LastKeyEventFirstTime = Context.Now;
            }

            var transition = (current == ButtonState.Pressed)
                ? UIEvents.KeyDown
                : UIEvents.KeyUp;

            LastKeyEvent = key;
            var ok = Context.HandleKeyEvent(transition, key, null, modifiers);
            if (current == ButtonState.Released)
                ok |= Context.HandleKeyEvent(UIEvents.KeyPress, key, null, modifiers);

            return ok;
        }

        public void SetTextInputState (bool enabled) {
        }

        public void Rasterize (UIOperationContext context, ref ImperativeRenderer renderer) {
            if (Context.InputSources.IndexOf(this) != 0)
                return;

            var decorator = context.DecorationProvider.VirtualCursor;
            if (decorator == null)
                return;
            var pos = Context.CurrentInputState.CursorPosition;
            var padding = decorator.Padding;
            var total = padding + decorator.Margins;
            var settings = new DecorationSettings {
                Box = new RectF(pos - new Vector2(total.Left, total.Top), new Vector2(total.X, total.Y)),
                ContentBox = new RectF(pos - new Vector2(padding.Left, padding.Top), new Vector2(padding.X, padding.Y)),
                State = GenerateKeyPressForActivation ? ControlStates.Disabled : default(ControlStates)
            };
            decorator.Rasterize(context, ref renderer, settings);
        }
    }
}
