﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class StaticText : Control {
        public const bool DiagnosticText = false;

        public Tween<Vector4>? TextColorPLinear = null;
        private bool _TextColorEventFired;
        public Material TextMaterial = null;
        public DynamicStringLayout Content = new DynamicStringLayout();
        private bool _AutoSizeWidth = true, _AutoSizeHeight = true;

        private float? AutoSizeComputedWidth, AutoSizeComputedHeight;

        public StaticText ()
            : base () {
            // Multiline = false;
        }

        bool _ScaleToFit;

        public bool ScaleToFit {
            get => _ScaleToFit;
            set {
                if (_ScaleToFit == value)
                    return;
                _ScaleToFit = true;
                Invalidate();
            }
        }

        public Tween<Color>? TextColor {
            set => UpdateColor(ref TextColorPLinear, value);
        }

        public Tween<pSRGBColor>? TextColorPSRGB {
            set => UpdateColor(ref TextColorPLinear, value);
        }

        public bool Multiline {
            get => Content.LineLimit != 1;
            set {
                Content.LineLimit = value ? int.MaxValue : 1;
            }
        }

        public bool AutoSizeWidth {
            get => _AutoSizeWidth;
            set {
                if (_AutoSizeWidth == value)
                    return;
                _AutoSizeWidth = value;
                Content.Invalidate();
            }
        }

        public bool AutoSizeHeight {
            get => _AutoSizeHeight;
            set {
                if (_AutoSizeHeight == value)
                    return;
                _AutoSizeHeight = value;
                Content.Invalidate();
            }
        }

        public bool AutoSize {
            set {
                AutoSizeWidth = AutoSizeHeight = value;
            }
        }

        public bool Wrap {
            get {
                return Content.WordWrap;
            }
            set {
                Content.CharacterWrap = value;
                Content.WordWrap = value;
            }
        }

        public HorizontalAlignment TextAlignment {
            get {
                return Content.Alignment;
            }
            set {
                Content.Alignment = value;
            }
        }

        public AbstractString Text {
            get {
                return Content.Text;
            }
            set {
                Content.Text = value;
            }
        }

        protected override void ComputeFixedSize (out float? fixedWidth, out float? fixedHeight) {
            base.ComputeFixedSize(out fixedWidth, out fixedHeight);
            /*
            if (Context == null)
                return;
            */
            // var parentRect = Context.Layout.GetRect(parent);

            if (AutoSizeWidth && !FixedWidth.HasValue)
                fixedWidth = AutoSizeComputedWidth ?? fixedWidth;
            if (AutoSizeHeight && !FixedHeight.HasValue)
                fixedHeight = AutoSizeComputedHeight ?? fixedHeight;
        }

        private void ComputeAutoSize (UIOperationContext context) {
            // FIXME: If we start out constrained (by our parent size, etc) we will compute
            //  a compressed auto-size value here, and it will never be updated even if our parent
            //  gets bigger

            AutoSizeComputedHeight = AutoSizeComputedWidth = null;
            if (!AutoSizeWidth && !AutoSizeHeight)
                return;

            var decorations = GetDecorations(context.DecorationProvider);
            var textDecorations = GetTextDecorations(context.DecorationProvider);
            UpdateFont(context, textDecorations);

            // HACK: If we know that our size is going to be constrained by layout settings, apply that in advance
            //  when computing auto-size to reduce the odds that our layout will be changed once full UI layout happens
            var textWidthLimit = ComputeTextWidthLimit(context, decorations);
            if (textWidthLimit.HasValue && !ScaleToFit)
                Content.LineBreakAtX = textWidthLimit;

            var computedPadding = ComputePadding(context, decorations);
            var layout = Content.Get();
            if (AutoSizeWidth)
                AutoSizeComputedWidth = layout.UnconstrainedSize.X + computedPadding.Size.X;
            if (AutoSizeHeight)
                AutoSizeComputedHeight = layout.Size.Y + computedPadding.Size.Y;
        }

        public void Invalidate () {
            Content.LineBreakAtX = null;
            Content.Invalidate();
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            ComputeAutoSize(context);
            var result = base.OnGenerateLayoutTree(context, parent, existingKey);
            return result;
        }

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            return provider?.StaticText;
        }

        protected float? ComputeTextWidthLimit (UIOperationContext context, IDecorator decorations) {
            var limit = FixedWidth ?? MaximumWidth;
            var computedPadding = ComputePadding(context, decorations);
            if (limit.HasValue)
                return limit.Value - computedPadding.X;
            else
                return null;
        }

        protected pSRGBColor? GetTextColor (long now) {
            var v4 = AutoFireTweenEvent(now, UIEvents.BackgroundColorTweenEnded, ref TextColorPLinear, ref _TextColorEventFired);
            if (!v4.HasValue)
                return null;
            return pSRGBColor.FromPLinear(v4.Value);
        }

        private float ComputeScaleToFit (ref StringLayout layout, ref RectF box, ref Margins margins) {
            if (!ScaleToFit)
                return 1;

            float availableWidth = Math.Max(box.Width - margins.X, 0);
            float availableHeight = Math.Max(box.Height - margins.Y, 0);

            float scaleFactor = 1;
            if (layout.UnconstrainedSize.X > availableWidth)
                scaleFactor = Math.Min(scaleFactor, availableWidth / (layout.UnconstrainedSize.X + 0.1f));
            if (layout.UnconstrainedSize.Y > availableHeight)
                scaleFactor = Math.Min(scaleFactor, availableHeight / (layout.UnconstrainedSize.Y + 0.1f));
            return scaleFactor;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            if (context.Pass != RasterizePasses.Content)
                return;

            var computedPadding = ComputePadding(context, decorations);

            var wasConstrainedByParent = (AutoSizeComputedWidth > settings.Box.Width);
            if (
                (
                    !AutoSizeWidth || Wrap || 
                    // If our size was constrained by our parent, act as if auto-size was disabled. This handles
                    //  the scenario where a menu item's text is too big for the menu
                    wasConstrainedByParent
                ) && !ScaleToFit
            ) {
                // If auto-size is disabled or wrapping is enabled, we need to enable wrapping/breaking at
                //  our rightmost edge to ensure that our text doesn't overflow outside of our boundaries
                // If wrapping is disabled entirely, the overflowing text will be suppressed by the text
                //  layout engine, otherwise it will be wrapped (potentially changing our layout, oops)
                var limit = Math.Max(0, (settings.Box.Width - computedPadding.X));
                var textWidthLimit = ComputeTextWidthLimit(context, decorations) ?? limit;
                Content.LineBreakAtX = textWidthLimit;
            } else {
                // This ensures that if we go from constrained to unconstrained, our layout is updated
                Content.LineBreakAtX = null;
            }

            var overrideColor = GetTextColor(context.UIContext.TimeProvider.Ticks);
            Material material;
            var textDecorations = GetTextDecorations(context.DecorationProvider);
            GetTextSettings(context, textDecorations, settings.State, out material, ref overrideColor);

            settings.Box.SnapAndInset(out Vector2 a, out Vector2 b);

            Vector2 textOffset = Vector2.Zero, textScale = Vector2.One;
            decorations?.GetContentAdjustment(context, settings.State, out textOffset, out textScale);
            textOffset += a + new Vector2(computedPadding.Left, computedPadding.Top);

            var layout = Content.Get();
            textScale *= ComputeScaleToFit(ref layout, ref settings.Box, ref computedPadding);

            var scaledSize = layout.Size * textScale;

            // Recenter the text if it's been scaled by the decorator
            float extraSpaceY = Math.Max(settings.Box.Height - scaledSize.Y - computedPadding.Y, 0);
            textOffset.Y += Math.Min(extraSpaceY, (layout.Size.Y - scaledSize.Y));

            var xSpace = (b.X - a.X) - scaledSize.X - computedPadding.X;
            switch (Content.Alignment) {
                case HorizontalAlignment.Left:
                    break;
                case HorizontalAlignment.Center:
                    textOffset.X += (xSpace / 2f);
                    break;
                case HorizontalAlignment.Right:
                    textOffset.X += xSpace;
                    break;
            }

            if (false)
                renderer.RasterizeRectangle(
                    textOffset, textOffset + layout.Size, 0f, Color.Black
                );

            renderer.Layer += 1;

            renderer.DrawMultiple(
                layout.DrawCalls, offset: textOffset.Floor(),
                material: material, samplerState: RenderStates.Text, multiplyColor: overrideColor?.ToColor(),
                scale: textScale
            );
        }

        protected void UpdateFont (UIOperationContext context, IDecorator decorations) {
            pSRGBColor? temp2 = null;
            GetTextSettings(context, decorations, default(ControlStates), out Material temp, ref temp2);
        }

        protected void GetTextSettings (UIOperationContext context, IDecorator decorations, ControlStates state, out Material material, ref pSRGBColor? color) {
            decorations.GetTextSettings(context, state, out material, out IGlyphSource font, ref color);
            if (Content.GlyphSource == null)
                Content.GlyphSource = font;
            if (TextMaterial != null)
                material = TextMaterial;
        }

        private string GetTrimmedText () {
            var s = Text.ToString().Replace('\r', ' ').Replace('\n', ' ') ?? "";
            if (s.Length > 16)
                return s.Substring(0, 16) + "...";
            else
                return s;
        }

        public override string ToString () {
            return $"{GetType().Name} #{GetHashCode():X8} '{GetTrimmedText()}'";
        }
    }
}
