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
using SimpleJSON;
using System.Diagnostics.CodeAnalysis;

namespace MacGruber
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

        string _secondaryName;
        public string secondaryName
        {
            get { return _secondaryName; }
            private set
            {
                _secondaryName = value;
                _myNeedInit = true;
            }
        }

        bool _myNeedInit = true;
        readonly MVRScript _owner;
        public Action<Transform> onInitPanel;
        public List<TriggerActionDiscrete> GetDiscreteActionsStart() => discreteActionsStart;

        protected CustomTrigger(MVRScript owner, string name, string secondary = null)
        {
            this.name = name;
            secondaryName = secondary;
            _owner = owner;
            handler = SimpleTriggerHandler.instance;
            SuperController.singleton.onAtomUIDRenameHandlers += OnAtomRename;
        }

        void OnAtomRename(string oldName, string newName) => SyncAtomNames();

        GameObject _ownerCloseButton;
        GameObject _ownerScrollView;

        public void OpenPanel()
        {
            if(!SimpleTriggerHandler.loaded)
            {
                SuperController.LogError("CustomTrigger: You need to call SimpleTriggerHandler.LoadAssets() before use.");
                return;
            }

            triggerActionsParent = _owner.UITransform;
            _ownerCloseButton = triggerActionsParent.Find("CloseButton").gameObject;
            _ownerScrollView = triggerActionsParent.Find("Scroll View").gameObject;
            InitTriggerUI();
            OpenTriggerActionsPanel();
            if(_myNeedInit)
            {
                var panel = triggerActionsPanel.Find("Panel");
                panel.Find("Header Text").GetComponent<Text>().text = name;
                var secondaryHeader = panel.Find("Trigger Name Text");
                secondaryHeader.gameObject.SetActive(!string.IsNullOrEmpty(secondaryName));
                secondaryHeader.GetComponent<Text>().text = secondaryName;

                InitPanel();
                _myNeedInit = false;
            }
        }

        public override void OpenTriggerActionsPanel()
        {
            _ownerCloseButton.SetActive(false);
            _ownerScrollView.SetActive(false);
            base.OpenTriggerActionsPanel();
            var contentT = triggerActionsPanel.Find("Content");
            if(contentT != null)
            {
                foreach(Transform childT in contentT)
                {
                    if(!childT.name.Contains("Tab1") && childT.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(childT.gameObject);
                    }
                }
            }
        }

        public override void CloseTriggerActionsPanel()
        {
            base.CloseTriggerActionsPanel();
            if(_ownerCloseButton != null)
            {
                _ownerCloseButton.SetActive(true);
            }

            if(_ownerScrollView != null)
            {
                _ownerScrollView.SetActive(true);
            }
        }

        protected abstract void InitPanel();

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
        public EventTrigger(MVRScript owner, string name, string secondary = null) : base(owner, name, secondary)
        {
        }

        protected override void InitPanel()
        {
            var panel = triggerActionsPanel.Find("Panel");
            panel.Find("Header Text").GetComponent<RectTransform>().sizeDelta = new Vector2(1000f, 50f);
            onInitPanel?.Invoke(triggerActionsPanel);
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
}
