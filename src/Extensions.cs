﻿using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace everlaster
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    static class IEnumerableExtensions
    {
        public static string ToPrettyString<T>(this IEnumerable<T> enumerable, string separator = "\n")
        {
            var sb = new StringBuilder();
            return enumerable.ToPrettyString(sb, separator).ToString();
        }

        public static StringBuilder ToPrettyString<T>(this IEnumerable<T> enumerable, StringBuilder sb, string separator = "\n")
        {
            foreach(var item in enumerable)
            {
                sb.Append(item);
                sb.Append(separator);
            }

            return sb;
        }
    }

    static class StringBuilderExtensions
    {
        public static StringBuilder Clear(this StringBuilder sb)
        {
            sb.Length = 0;
            return sb;
        }
    }

    static class UIDynamicButtonExtensions
    {
        public static void AddListener(this UIDynamicButton uiDynamic, UnityAction action)
        {
            uiDynamic.button.onClick.AddListener(action);
        }
    }

    static class UIDynamicSliderExtensions
    {
        public static void HideButtons(this UIDynamicSlider uiDynamic)
        {
            uiDynamic.defaultButtonEnabled = false;
            uiDynamic.quickButtonsEnabled = false;
            var uiDynamicT = uiDynamic.gameObject.transform;

            {
                var sliderRectT = uiDynamicT.Find("Slider").GetComponent<RectTransform>();
                var pos = sliderRectT.anchoredPosition;
                sliderRectT.anchoredPosition = new Vector2(pos.x, pos.y - 22.5f);
            }

            {
                var layoutElement = uiDynamicT.GetComponent<LayoutElement>();
                layoutElement.minHeight -= 25f;
                layoutElement.preferredHeight -= 25f;
            }
        }

        public static void SetInteractable(this UIDynamicSlider uiDynamic, bool interactable)
        {
            uiDynamic.slider.interactable = interactable;
            var transform = uiDynamic.gameObject.transform;

            var defaultValueButton = transform.Find("DefaultValueButton");
            if(defaultValueButton.gameObject.activeSelf)
            {
                defaultValueButton.GetComponent<Button>().interactable = interactable;
            }

            foreach(Transform child in transform.Find("QuickButtonsGroup/QuickButtonsLeft"))
            {
                if(child.gameObject.activeSelf)
                {
                    child.GetComponent<Button>().interactable = interactable;
                }
            }

            foreach(Transform child in transform.Find("QuickButtonsGroup/QuickButtonsRight"))
            {
                if(child.gameObject.activeSelf)
                {
                    child.GetComponent<Button>().interactable = interactable;
                }
            }

            var valueInputField = transform.Find("ValueInputField");
            valueInputField.GetComponent<InputField>().interactable = interactable;
        }
    }
}
