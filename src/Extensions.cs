using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace everlaster
{
    static class StringBuilderExtensions
    {
        public static StringBuilder Clear(this StringBuilder sb)
        {
            sb.Length = 0;
            return sb;
        }
    }

    static class UIDynamicSliderExtensions
    {
        public static void HideButtons(this UIDynamicSlider uiDynamic)
        {
            uiDynamic.defaultButtonEnabled = false;
            uiDynamic.quickButtonsEnabled = false;
            var uiDynamicT = uiDynamic.gameObject.transform;

            var panelRectT = uiDynamicT.Find("Panel").GetComponent<RectTransform>();
            var size = panelRectT.sizeDelta;
            panelRectT.sizeDelta = new Vector2(size.x, size.y - 25f);

            var textRectT = uiDynamicT.Find("Text").GetComponent<RectTransform>();
            var pos = textRectT.anchoredPosition;
            textRectT.anchoredPosition = new Vector2(pos.x, pos.y - 12f);

            var valueInputFieldRectT = uiDynamicT.Find("ValueInputField").GetComponent<RectTransform>();
            pos = valueInputFieldRectT.anchoredPosition;
            valueInputFieldRectT.anchoredPosition = new Vector2(pos.x, pos.y - 12f);

            var sliderRectT = uiDynamicT.Find("Slider").GetComponent<RectTransform>();
            pos = sliderRectT.anchoredPosition;
            sliderRectT.anchoredPosition = new Vector2(pos.x, pos.y - 10f);

            var uiDynamicRectT = uiDynamicT.GetComponent<RectTransform>();
            pos = uiDynamicRectT.anchoredPosition;
            uiDynamicRectT.anchoredPosition = new Vector2(pos.x, pos.y - 10f);
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
