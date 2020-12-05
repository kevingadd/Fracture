﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public delegate Control CreateControlForValueDelegate<T> (ref T value, Control existingControl);

    public class Dropdown<T> : StaticTextBase, Accessibility.IReadingTarget, IMenuListener, IValueControl<T> {
        private readonly Menu ItemsMenu = new Menu {
            DeselectOnMouseLeave = false
        };

        public string Description;

        public AbstractString Label;
        public CreateControlForValueDelegate<T> CreateControlForValue = null;
        public Func<T, AbstractString> FormatValue = null;
        private CreateControlForValueDelegate<T> DefaultCreateControlForValue;

        protected ItemListManager<T> Manager;

        public IEqualityComparer<T> Comparer {
            get => Manager.Comparer;
            set => Manager.Comparer = value;
        }
        public ItemList<T> Items => Manager.Items;

        // private bool NeedsUpdate = true;
        private bool MenuJustClosed = false;

        public int SelectedIndex => Manager.SelectedIndex;
        public T SelectedItem {
            get => Manager.SelectedItem;
            set {
                if (!Manager.TrySetSelectedItem(ref value))
                    return;
                Invalidate();
                FireEvent(UIEvents.ValueChanged, SelectedItem);
            }
        }

        AbstractString Accessibility.IReadingTarget.Text {
            get {
                if (Description != null)
                    return $"{Description}: {GetValueText()}";
                else if (TooltipContent)
                    return TooltipContent.Get(this);

                var irt = SelectedItem as Accessibility.IReadingTarget;
                if (irt != null)
                    return irt.Text;
                else
                    return GetValueText();
            }
        }

        T IValueControl<T>.Value {
            get => SelectedItem;
            set => SelectedItem = value;
        }

        void Accessibility.IReadingTarget.FormatValueInto (StringBuilder sb) {
            var irt = SelectedItem as Accessibility.IReadingTarget;
            if (irt != null)
                irt.FormatValueInto(sb);
            else
                sb.Append(GetValueText());
        }

        AbstractString GetValueText () {
            var stb = SelectedItem as StaticTextBase;
            if (FormatValue != null)
                return FormatValue(SelectedItem);
            else if (stb != null)
                return stb.Text;
            else
                return SelectedItem?.ToString();
        }

        public Dropdown ()
            : this (null) {
        }

        public Dropdown (IEqualityComparer<T> comparer = null) 
            : base () {
            AcceptsFocus = true;
            AcceptsMouseInput = true;
            Manager = new ItemListManager<T>(comparer ?? EqualityComparer<T>.Default);
            DefaultCreateControlForValue = _DefaultCreateControlForValue;
        }

        private Control _DefaultCreateControlForValue (ref T value, Control existingControl) {
            var st = (existingControl as StaticText) ?? new StaticText();
            var text =
                (FormatValue != null)
                    ? FormatValue(value)
                    : value.ToString();
            st.Text = text;
            st.Data.Set<T>(ref value);
            return st;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider.Dropdown;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            // if (NeedsUpdate)
                Update();

            return base.OnGenerateLayoutTree(context, parent, existingKey);
        }

        protected void Update () {
            // NeedsUpdate = false;
            if (Comparer.Equals(SelectedItem, default(T)) && (Items.Count > 0))
                SelectedItem = Items[0];

            if (Label != default(AbstractString))
                Text = Label;
            else
                Text = GetValueText();
        }

        protected Control UpdateMenu () {
            Control result = null;

            if (Description != null)
                ItemsMenu.Description = $"Menu {Description}";

            Items.GenerateControls(
                ItemsMenu.Children, CreateControlForValue ?? DefaultCreateControlForValue
            );

            return result;
        }

        private void ShowMenu () {
            // When the menu closes it will set this flag. If it was closed because a click occurred
            //  on us (outside of the menu), we will hit this point with the flag set and know not
            //  to reopen the menu
            // FIXME: Maybe we should clear it here?
            if (MenuJustClosed)
                return;

            UpdateMenu();

            // FIXME: The ideal behavior is for another click when open to close the dropdown
            if (ItemsMenu.IsActive)
                return;

            var box = GetRect(Context.Layout, contentRect: false);
            ItemsMenu.MinimumWidth = box.Width;
            var selectedControl = Manager.SelectedControl;
            ItemsMenu.Show(Context, this, selectedControl);
            MenuJustClosed = false;
        }

        protected override bool OnEvent<TArgs> (string name, TArgs args) {
            if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            else if (args is KeyEventArgs)
                return OnKeyEvent(name, (KeyEventArgs)(object)args);
            else
                return base.OnEvent(name, args);
        }

        private bool OnKeyEvent (string name, KeyEventArgs args) {
            switch (name) {
                case UIEvents.KeyPress:
                    Context.OverrideKeyboardSelection(this);
                    var oldSelection = SelectedItem;
                    var oldIndex = Items.IndexOf(ref oldSelection, Comparer);
                    switch (args.Key) {
                        case Keys.Up:
                        case Keys.Down:
                            if (Manager.TryAdjustSelection(
                                (args.Key == Keys.Up) ? -1 : 1,
                                out T newItem
                            ))
                                SelectedItem = newItem;
                            return true;
                        case Keys.Space:
                            ShowMenu();
                            return true;
                        default:
                            return false;
                    }
            }

            return false;
        }

        private bool OnMouseEvent (string name, MouseEventArgs args) {
            if (name == UIEvents.MouseDown) {
                ShowMenu();
                return true;
            }

            return false;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            if (ItemsMenu.IsActive)
                settings.State |= ControlStates.Pressed;
            base.OnRasterize(context, ref renderer, settings, decorations);

            // FIXME: There is probably a better place to clear this flag
            MenuJustClosed = false;
        }

        public override string ToString () {
            return $"Dropdown #{GetHashCode():X8} '{GetTrimmedText(GetValueText().ToString())}'";
        }

        void IMenuListener.Shown (Menu menu) {
        }

        void IMenuListener.Closed (Menu menu) {
            MenuJustClosed = true;
        }

        void IMenuListener.ItemSelected (Menu menu, Control item) {
        }

        void IMenuListener.ItemChosen (Menu menu, Control item) {
            if (Items.GetValueForControl(item, out T value))
                SelectedItem = value;
        }
    }
}
