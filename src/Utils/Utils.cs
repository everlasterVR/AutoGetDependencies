using UnityEngine.UI;

/*
 * AutoGetDependencies v1.0
 * Licensed under CC BY https://creativecommons.org/licenses/by/4.0/
 * (c) 2024 everlaster
 * https://patreon.com/everlaster
 */
namespace everlaster
{
    static class Utils
    {
        public static void DisableScroll(UIDynamicTextField uiDynamic)
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
