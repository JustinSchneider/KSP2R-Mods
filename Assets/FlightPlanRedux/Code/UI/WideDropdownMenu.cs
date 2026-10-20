using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace FlightPlanRedux.UI
{
    /// <summary>
    /// A dropdown's menu sized to its longest choice (never narrower than the field). Unity opens popup menus exactly
    /// as wide as the field, cutting off longer choices.
    /// </summary>
    internal sealed class WideDropdownMenu : AbstractGenericMenu
    {
        private static readonly FieldInfo CreateMenuCallback = typeof(BasePopupField<string, string>)
            .GetField("createMenuCallback", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly GenericDropdownMenu _menu = new GenericDropdownMenu();

        public static void Apply(DropdownField field)
        {
            CreateMenuCallback?.SetValue(field, (Func<AbstractGenericMenu>)(() => new WideDropdownMenu()));
        }

        public override void AddItem(string itemName, bool isChecked, Action action) =>
            _menu.AddItem(itemName, isChecked, action);

        public override void AddItem(string itemName, bool isChecked, Action<object> action, object data) =>
            _menu.AddItem(itemName, isChecked, action, data);

        public override void AddDisabledItem(string itemName, bool isChecked) =>
            _menu.AddDisabledItem(itemName, isChecked);

        public override void AddSeparator(string path) => _menu.AddSeparator(path);

        public override void DropDown(Rect position, VisualElement targetElement,
            DropdownMenuSizeMode dropdownMenuSizeMode = DropdownMenuSizeMode.Auto)
        {
            _menu.DropDown(position, targetElement, DropdownMenuSizeMode.Auto);
            for (var e = _menu.contentContainer; e != null; e = e.parent)
            {
                if (!e.ClassListContains(GenericDropdownMenu.containerOuterUssClassName)) continue;
                e.style.minWidth = position.width;
                break;
            }
        }
    }
}
