using UnityEngine.UI;

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
