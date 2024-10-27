/* /////////////////////////////////////////////////////////////////////////////////////////////////
Utils 2023-11-12 by MacGruber
Collection of various utility functions.
https://www.patreon.com/MacGruber_Laboratory

Credit: MacGruber_Utils.cs: https://hub.virtamate.com/resources/macgruber-utils.40744/ (CC BY)

///////////////////////////////////////////////////////////////////////////////////////////////// */

using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using AssetBundles;
using everlaster;
using SimpleJSON;
using System.Diagnostics.CodeAnalysis;

namespace MacGruber_Utils
{
    // ===========================================================================================

    // TriggerHandler implementation for easier handling of custom triggers.
    // Essentially call this in your plugin init code:
    //     StartCoroutine(SimpleTriggerHandler.LoadAssets());
    //
    // Credit to AcidBubbles for figuring out how to do custom triggers.
    class SimpleTriggerHandler : TriggerHandler
    {
        static SimpleTriggerHandler _obj;
        public static SimpleTriggerHandler instance => _obj ?? (_obj = new SimpleTriggerHandler());
        public static bool loaded { get; private set; }

        RectTransform _myTriggerActionsPrefab;
        RectTransform _myTriggerActionMiniPrefab;
        RectTransform _myTriggerActionDiscretePrefab;
        RectTransform _myTriggerActionTransitionPrefab;

        public static void LoadAssets() => SuperController.singleton.StartCoroutine(instance.LoadAssetsInternal());

        IEnumerator LoadAssetsInternal()
        {
            foreach(object x in LoadAsset("z_ui2", "TriggerActionsPanel", transform => _myTriggerActionsPrefab = transform))
                yield return x;
            foreach(object x in LoadAsset("z_ui2", "TriggerActionMiniPanel", transform => _myTriggerActionMiniPrefab = transform))
                yield return x;
            foreach(object x in LoadAsset("z_ui2", "TriggerActionDiscretePanel", transform => _myTriggerActionDiscretePrefab = transform))
                yield return x;
            foreach(object x in LoadAsset("z_ui2", "TriggerActionTransitionPanel", transform => _myTriggerActionTransitionPrefab = transform))
                yield return x;

            loaded = true;
        }

        static IEnumerable LoadAsset(string assetBundleName, string assetName, Action<RectTransform> assign)
        {
            var request = AssetBundleManager.LoadAssetAsync(assetBundleName, assetName, typeof(GameObject));
            if(request == null)
            {
                throw new NullReferenceException($"Request for {assetName} in {assetBundleName} assetbundle failed: Null request.");
            }

            yield return request;
            var go = request.GetAsset<GameObject>();
            if(go == null)
            {
                throw new NullReferenceException($"Request for {assetName} in {assetBundleName} assetbundle failed: Null GameObject.");
            }

            var prefab = go.GetComponent<RectTransform>();
            if(prefab == null)
            {
                throw new NullReferenceException($"Request for {assetName} in {assetBundleName} assetbundle failed: Null RectTansform.");
            }

            assign(prefab);
        }

        void TriggerHandler.RemoveTrigger(Trigger t) {} // nothing to do
        void TriggerHandler.DuplicateTrigger(Trigger t) {} // nothing to do

        RectTransform TriggerHandler.CreateTriggerActionsUI() => UnityEngine.Object.Instantiate(_myTriggerActionsPrefab);

        RectTransform TriggerHandler.CreateTriggerActionMiniUI() => UnityEngine.Object.Instantiate(_myTriggerActionMiniPrefab);

        RectTransform TriggerHandler.CreateTriggerActionDiscreteUI() => UnityEngine.Object.Instantiate(_myTriggerActionDiscretePrefab);

        RectTransform TriggerHandler.CreateTriggerActionTransitionUI()
        {
            var rt = UnityEngine.Object.Instantiate(_myTriggerActionTransitionPrefab);
            rt.GetComponent<TriggerActionTransitionUI>().startWithCurrentValToggle.gameObject.SetActive(false);
            return rt;
        }

        void TriggerHandler.RemoveTriggerActionUI(RectTransform rt) => UnityEngine.Object.Destroy(rt?.gameObject);
    }

    // Base class for easier handling of custom triggers.
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    abstract class CustomTrigger : Trigger
    {
        string _name;
        public string name
        {
            get { return _name; }
            private set
            {
                _name = value;
                _myNeedInit = true;
            }
        }

        bool _myNeedInit = true;
        readonly MVRScript _owner;
        UnityEventsListener _enabledListener;
        public Action<Transform> onInitPanel;
        public List<TriggerActionDiscrete> GetDiscreteActionsStart() => discreteActionsStart;
        public List<TriggerActionDiscrete> GetDiscreteActionsEnd() => discreteActionsEnd;

        // built in HasActions() req. VAM >= 1.22
        public bool IsEmpty() => discreteActionsStart.Count == 0 && transitionActions.Count == 0 && discreteActionsEnd.Count == 0;

        protected CustomTrigger(MVRScript owner, string name)
        {
            this.name = name;
            _owner = owner;
            handler = SimpleTriggerHandler.instance;
            SuperController.singleton.onAtomUIDRenameHandlers += OnAtomRename;
        }

        void OnAtomRename(string oldName, string newName) => SyncAtomNames();

        public void OpenPanel()
        {
            if(!SimpleTriggerHandler.loaded)
            {
                SuperController.LogError("CustomTrigger: You need to call SimpleTriggerHandler.LoadAssets() before use.");
                return;
            }

            triggerActionsParent = _owner.UITransform;
            InitTriggerUI();
            OpenTriggerActionsPanel();
            if(_myNeedInit)
            {
                var panel = triggerActionsPanel.Find("Panel");
                panel.Find("Header Text").GetComponent<Text>().text = name;
                InitPanel();
                _myNeedInit = false;
            }
        }

        // built in onCloseTriggerActionsPanel delegate req. VAM >= 1.22
        public Action panelDisabledHandlers;

        protected virtual void InitPanel()
        {
            var rect = triggerActionsPanel.GetComponent<RectTransform>();
            var pos = rect.anchoredPosition;
            rect.anchoredPosition = new Vector2(pos.x + 3.5f, pos.y - 10);
            var size = rect.sizeDelta;
            rect.sizeDelta = new Vector2(size.x + 20, size.y + 20);

            var panel = triggerActionsPanel.Find("Panel");
            panel.Find("Header Text").GetComponent<RectTransform>().sizeDelta = new Vector2(1000f, 50f);
            panel.Find("Trigger Name Text").gameObject.SetActive(false);
            onInitPanel?.Invoke(triggerActionsPanel);

            _enabledListener = triggerActionsPanel.gameObject.AddComponent<UnityEventsListener>();
            _enabledListener.disabledHandlers = panelDisabledHandlers;
        }

        public void RestoreFromJSON(JSONClass jc, string subScenePrefix, bool isMerge, bool setMissingToDefault)
        {
            if(jc.HasKey(name))
            {
                var triggerJson = jc[name].AsObject;
                if(triggerJson != null)
                {
                    base.RestoreFromJSON(triggerJson, subScenePrefix, isMerge);
                }
            }
            else if(setMissingToDefault)
            {
                base.RestoreFromJSON(new JSONClass());
            }
        }

        public void OnRemove() => SuperController.singleton.onAtomUIDRenameHandlers -= OnAtomRename;
    }

    sealed class EventTrigger : CustomTrigger
    {
        public EventTrigger(MVRScript owner, string name) : base(owner, name)
        {
        }

        protected override void InitPanel()
        {
            base.InitPanel();
            var content = triggerActionsPanel.Find("Content");
            content.Find("Tab1/Label").GetComponent<Text>().text = "Event Actions";
            content.Find("Tab2").gameObject.SetActive(false);
            content.Find("Tab3").gameObject.SetActive(false);
        }

        public void Trigger()
        {
            active = true;
            active = false;
        }
    }

    sealed class DualEventTrigger : CustomTrigger
    {
        readonly string _eventAName;
        readonly string _eventBName;

        public DualEventTrigger(MVRScript owner, string name, string eventAName, string eventBName) : base(owner, name)
        {
            _eventAName = eventAName;
            _eventBName = eventBName;
        }

        protected override void InitPanel()
        {
            base.InitPanel();
            var content = triggerActionsPanel.Find("Content");

            {
                var tabT = content.Find("Tab1");
                ModifyLabel(tabT, new Vector2(0, 12), new Vector2(0, 6), _eventAName);
            }

            content.Find("Tab2").gameObject.SetActive(false);

            {
                var tabT = content.Find("Tab3");
                var bgRectT = tabT.Find("Background").GetComponent<RectTransform>();
                var size = bgRectT.sizeDelta;
                bgRectT.sizeDelta = new Vector2(size.x + 120, size.y);
                var pos = bgRectT.anchoredPosition;
                bgRectT.anchoredPosition = new Vector2(pos.x - 300, pos.y);
                ModifyLabel(tabT, new Vector2(120, 12), new Vector2(-300, 6), _eventBName);
            }
        }

        static void ModifyLabel(Transform tabT, Vector2 offsetSize, Vector2 offsetPos, string text)
        {
            var labelT = tabT.Find("Label");
            labelT.GetComponent<Text>().text = text;
            var rectT = labelT.GetComponent<RectTransform>();
            var size = rectT.sizeDelta;
            rectT.sizeDelta = new Vector2(size.x + offsetSize.x, size.y + offsetSize.y);
            var pos = rectT.anchoredPosition;
            rectT.anchoredPosition = new Vector2(pos.x + offsetPos.x, pos.y + offsetPos.y);
        }

        public void TriggerA()
        {
            var tmpEndActions = discreteActionsEnd;
            discreteActionsEnd = new List<TriggerActionDiscrete>();
            Trigger();
            discreteActionsEnd = tmpEndActions;
        }

        public void TriggerB()
        {
            var tmpStartActions = discreteActionsStart;
            var tmpEndActions = discreteActionsEnd;
            discreteActionsStart = tmpEndActions;
            discreteActionsEnd = new List<TriggerActionDiscrete>();
            Trigger();
            discreteActionsStart = tmpStartActions;
            discreteActionsEnd = tmpEndActions;
        }

        void Trigger()
        {
            active = true;
            active = false;
        }
    }
}
