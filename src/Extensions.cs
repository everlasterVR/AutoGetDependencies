using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/*
 * AutoGetDependencies v1.0
 * Licensed under CC BY https://creativecommons.org/licenses/by/4.0/
 * (c) 2024 everlaster
 * https://patreon.com/everlaster
 */
namespace everlaster
{
    static class AtomExtensions
    {
        public static IEnumerator SelectTabCo(this Atom atom, string tabName, Action postAction = null)
        {
            if(SuperController.singleton.gameMode != SuperController.GameMode.Edit)
            {
                SuperController.singleton.gameMode = SuperController.GameMode.Edit;
            }

            SuperController.singleton.SelectController(atom.mainController, false, false);
            SuperController.singleton.ShowMainHUDAuto();

            float timeout = Time.unscaledTime + 1;
            while(Time.unscaledTime < timeout)
            {
                yield return null;
                var selector = atom.gameObject.GetComponentInChildren<UITabSelector>();
                if(selector)
                {
                    selector.SetActiveTab(tabName);
                    yield return null;
                    postAction?.Invoke();
                    break;
                }
            }
        }
    }

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

    static class MVRScriptExtensions
    {
        public static string GetPluginId(this MVRScript script)
        {
            int index = script.name.IndexOf('_');
            return index == -1 ? script.name : script.name.Substring(0, index);
        }

        public static void SelectPluginUI(this MVRScript script, Action postAction = null) =>
            script.StartCoroutine(SelectPluginUICo(script, postAction));

        static IEnumerator SelectPluginUICo(MVRScript script, Action postAction = null)
        {
            if(script.UITransform != null && script.UITransform.gameObject.activeInHierarchy)
            {
                if(script.enabled) postAction?.Invoke();
                yield break;
            }

            yield return script.containingAtom.SelectTabCo("Plugins");

            float timeout = Time.unscaledTime + 1;
            while(Time.unscaledTime < timeout)
            {
                yield return null;

                if(script.UITransform == null)
                {
                    continue;
                }

                /* Close any currently open plugin UI before opening this plugin's UI */
                foreach(Transform scriptController in script.manager.pluginContainer)
                {
                    var mvrScript = scriptController.gameObject.GetComponent<MVRScript>();
                    if(mvrScript != script && mvrScript != null && mvrScript.UITransform != null)
                    {
                        mvrScript.UITransform.gameObject.SetActive(false);
                    }
                }

                if(script.enabled)
                {
                    script.UITransform.gameObject.SetActive(true);
                    postAction?.Invoke();
                    yield break;
                }
            }
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
                var sliderRectT = (RectTransform) uiDynamicT.Find("Slider");
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

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedType.Global")]
    static class UIDynamicTextFieldExtensions
    {
        public static void DisableScroll(this UIDynamicTextField uiDynamic)
        {
            var scrollViewT = uiDynamic.transform.Find("Scroll View");
            var scrollBarHorizontalT = scrollViewT.Find("Scrollbar Horizontal");
            if(scrollBarHorizontalT != null)
            {
                UnityEngine.Object.Destroy(scrollBarHorizontalT.gameObject);
            }

            var scrollRect = scrollViewT.GetComponent<ScrollRect>();
            scrollRect.vertical = false;
        }
    }
}
