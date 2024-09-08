using System;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using MVR.Hub;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace everlaster
{
    sealed class AutoGetDependencies : MVRScript
    {
        UnityEventsListener _uiListener;
        bool _uiCreated;
        LogBuilder _logBuilder;
        JSONClass _metaJson;
        HubBrowse _hubBrowse;
        readonly List<PackageObj> _packages = new List<PackageObj>();
        readonly List<HubResourcePackageUI> _packageUIs = new List<HubResourcePackageUI>();
        readonly List<PackageObj> _missingPackages = new List<PackageObj>();
        readonly List<PackageObj> _pendingPackages = new List<PackageObj>();
        readonly List<PackageObj> _notOnHubPackages = new List<PackageObj>();
        bool _initialized;
        Coroutine _downloadCo;
        bool _hadError;

        JSONStorableBool _searchSubDependenciesBool;
        JSONStorableBool _alwaysCheckForUpdatesBool;
        JSONStorableAction _findDependenciesAction;
        JSONStorableString _infoString;
        JSONStorableBool _tempEnableHubBool;
        JSONStorableBool _autoAcceptPackagePluginsBool;
        JSONStorableFloat _timeoutFloat;
        JSONStorableAction _downloadAction;
        JSONStorableBool _logErrorsBool;

        public override void InitUI()
        {
            base.InitUI();
            if(UITransform == null)
            {
                return;
            }

            UITransform.Find("Scroll View").GetComponent<Image>().color = new Color(0.85f, 0.85f, 0.85f);
            _uiListener = UITransform.gameObject.AddComponent<UnityEventsListener>();
            _uiListener.enabledHandlers += UIEnabled;
        }

        void UIEnabled()
        {
            if(!_initialized)
            {
                return;
            }

            if(!_uiCreated)
            {
                CreateUI();
                _uiCreated = true;
            }

            if(_dependenciesFound)
            {
                UpdateInfo();
            }
        }

        public override void Init()
        {
            try
            {
                _logBuilder = new LogBuilder(nameof(AutoGetDependencies));
                if(containingAtom.type == "SessionPluginManager")
                {
                    _logBuilder.Error("Do not add as Session Plugin.");
                    enabledJSON.valNoCallback = false;
                    return;
                }

                _metaJson = FindLoadedSceneMetaJson();
                if(_metaJson == null)
                {
                    _logBuilder.Error("Invalid scene (must be from package).");
                    enabledJSON.valNoCallback = false;
                    return;
                }

                var coreControl = SuperController.singleton.GetAtomByUid("CoreControl");
                _hubBrowse = (HubBrowse) coreControl.GetStorableByID("HubBrowseController");
                if(_hubBrowse == null)
                {
                    _logBuilder.Error("HubBrowseController not found.");
                    enabledJSON.valNoCallback = false;
                    return;
                }

                _searchSubDependenciesBool = new JSONStorableBool("Search sub-dependencies", false);
                _alwaysCheckForUpdatesBool = new JSONStorableBool("Always check for updates", false);
                _findDependenciesAction = new JSONStorableAction("1. Find dependencies in meta.json", FindDependenciesCallback);
                _infoString = new JSONStorableString("Info", "");
                _tempEnableHubBool = new JSONStorableBool("Temp auto-enable Hub if needed", false);
                _autoAcceptPackagePluginsBool = new JSONStorableBool("Auto-accept package plugins", false);
                _timeoutFloat = new JSONStorableFloat("Timeout (seconds)", 120, 1, 600);
                _downloadAction = new JSONStorableAction("2. Download missing packages", DownloadMissingCallback);
                _logErrorsBool = new JSONStorableBool("Log errors", false);
                RegisterBool(_searchSubDependenciesBool);
                RegisterAction(_findDependenciesAction);
                RegisterAction(_downloadAction);
                RegisterBool(_logErrorsBool);
                RegisterBool(_tempEnableHubBool);
                RegisterBool(_autoAcceptPackagePluginsBool);
                _initialized = true;
            }
            catch(Exception e)
            {
                _logBuilder.Exception(e);
            }
        }

        static JSONClass FindLoadedSceneMetaJson()
        {
            string loadedScene = SuperController.singleton.LoadedSceneName;
            if(!loadedScene.Contains(":/"))
            {
                return null;
            }

            string metaJsonPath = loadedScene.Split(':')[0] + ":/meta.json";
            return SuperController.singleton.LoadJSON(metaJsonPath).AsObject;
        }

        void CreateUI()
        {
            {
                var title = CreateTextField(new JSONStorableString("title", "Auto Get Dependencies"));
                title.height = 60;
                var layoutElement = title.GetComponent<LayoutElement>();
                layoutElement.minHeight = 60;
                layoutElement.preferredHeight = 60;
                title.UItext.fontSize = 32;
                title.UItext.fontStyle = FontStyle.Bold;
                title.UItext.alignment = TextAnchor.LowerCenter;
                title.backgroundColor = Color.clear;
                var rectT = title.UItext.GetComponent<RectTransform>();
                var pos = rectT.anchoredPosition;
                pos.y = -15;
                rectT.anchoredPosition = pos;
            }

            CreateToggle(_searchSubDependenciesBool);
            CreateToggle(_alwaysCheckForUpdatesBool);
            {
                var button = CreateButton(_findDependenciesAction.name);
                button.height = 75;
                _findDependenciesAction.RegisterButton(button);
            }

            CreateSpacer().height = 15;
            CreateToggle(_tempEnableHubBool);
            CreateToggle(_autoAcceptPackagePluginsBool);
            CreateSlider(_timeoutFloat).valueFormat = "F0";
            {
                var button = CreateButton(_downloadAction.name);
                button.height = 75;
                _downloadAction.RegisterButton(button);
            }

            CreateSpacer().height = 15;
            CreateToggle(_logErrorsBool);

            {
                var infoField = Instantiate(manager.configurableTextFieldPrefab, UITransform);
                var uiDynamic = infoField.GetComponent<UIDynamicTextField>();
                uiDynamic.UItext.fontSize = 26;
                uiDynamic.backgroundColor = new Color(0.92f, 0.92f, 0.92f);
                var layoutElement = infoField.GetComponent<LayoutElement>();
                DestroyImmediate(layoutElement);
                var rectT = infoField.GetComponent<RectTransform>();
                rectT.pivot = Vector2.zero;
                rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 20, 1200);
                rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 545, 650);
                uiDynamic.UItext.horizontalOverflow = HorizontalWrapMode.Overflow;
                var scrollView = infoField.Find("Scroll View");
                var scrollRect = scrollView.GetComponent<ScrollRect>();
                scrollRect.horizontal = true;
                scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
                _infoString.dynamicText = uiDynamic;
            }
        }

        bool _dependenciesFound;

        void FindDependenciesCallback()
        {
            SuperController.singleton.RescanPackages();
            _packages.Clear();
            FindDependencies(_metaJson, _searchSubDependenciesBool.val);
            UpdateInfo();
            _dependenciesFound = true;
        }

        void FindDependencies(JSONClass json, bool recursive = false, int depth = 0)
        {
            try
            {
                if(_downloadCo != null)
                {
                    StopCoroutine(_downloadCo);
                }

                var dependenciesJc = json["dependencies"].AsObject;
                if(dependenciesJc == null)
                {
                    return;
                }

                foreach(string key in dependenciesJc.Keys)
                {
                    string trimmed = key.Trim();
                    string[] parts = trimmed.Split('.');
                    if(parts.Length != 3)
                    {
                        continue;
                    }

                    if(!_packages.Exists(obj => obj.name == trimmed))
                    {
                        _packages.Add(new PackageObj(trimmed, parts, depth));
                    }

                    if(recursive)
                    {
                        FindDependencies(dependenciesJc[key].AsObject, true, depth + 1);
                    }
                }
            }
            catch(Exception e)
            {
                _logBuilder.Exception(e);
            }
        }

        void UpdateInfo()
        {
            if(!_uiCreated)
            {
                return;
            }

            var sb = new StringBuilder();
            bool anyMissing = false;
            bool anyUpdateNeeded = false;
            AppendPackageInfo(
                sb,
                "Missing, download needed",
                new Color(0.75f, 0, 0),
                obj => anyMissing = !obj.exists
            );
            AppendPackageInfo(
                sb,
                "Found, check for update needed",
                new Color(0, 0, 0.50f),
                obj => anyUpdateNeeded = obj.exists && obj.requireLatest && (_alwaysCheckForUpdatesBool.val || anyMissing)
            );
            AppendPackageInfo(
                sb,
                "Found",
                new Color(0, 0.50f, 0),
                obj => obj.exists
            );

            if(!anyMissing && !anyUpdateNeeded)
            {
                sb.Append("\n\nAll dependencies installed. TODO configurable trigger");
                _logBuilder.Message("All dependencies installed. TODO configurable trigger");
            }

            _infoString.val = sb.ToString();
        }

        void AppendPackageInfo(StringBuilder sb, string title, Color titleColor, Func<PackageObj, bool> condition)
        {
            sb.AppendFormat("<size=28><color=#{0}><b>{1}:</b></color></size>\n\n", ColorUtility.ToHtmlStringRGBA(titleColor), title);
            int count = 0;
            string optionalColor = ColorUtility.ToHtmlStringRGBA(new Color(0.4f, 0.4f, 0.4f));
            foreach (var obj in _packages)
            {
                if (condition(obj))
                {
                    if(obj.depth > 0)
                    {
                        string indent = new string('\u00A0', obj.depth * 3);
                        sb.AppendFormat("{0}<color=#{1}>-\u00A0{2}</color>\n", indent, optionalColor, obj.name);
                    }
                    else
                    {
                        sb.AppendFormat("-\u00A0{0}\n", obj.name);
                    }
                    count++;
                }
            }
            if (count == 0) sb.Append("None.\n");
            sb.Append("\n");
        }

        void DownloadMissingCallback()
        {
            try
            {
                if(!_dependenciesFound)
                {
                    _infoString.val = "Must find dependencies first.";
                    if(_logErrorsBool.val) _logBuilder.Error("Must find dependencies first.");
                    return;
                }

                if(_packages.TrueForAll(obj => obj.exists))
                {
                    UpdateInfo();
                    // TODO trigger on download complete
                    return;
                }

                _downloadCo = StartCoroutine(DownloadMissingViaHubCo());
                _dependenciesFound = false;
            }
            catch(Exception e)
            {
                _logBuilder.Exception(e);
            }
        }

        void OnError(string message)
        {
            if (_logErrorsBool.val) _logBuilder.Error(message);
            _infoString.val = "";
            _hadError = true;
        }

        void OnException(Exception e)
        {
            if (_logErrorsBool.val) _logBuilder.Exception(e);
            _infoString.val = "";
            _hadError = true;
        }

        IEnumerator DownloadMissingViaHubCo()
        {
#region SetupVariables
            _packageUIs.Clear();
            _missingPackages.Clear();
            _pendingPackages.Clear();
            _notOnHubPackages.Clear();

            bool hubWasTempEnabled = false;
            Transform hubBrowsePanelT;
            Transform missingPackagesPanelT;
            Transform contentT;
            _hadError = false;

            try
            {
                _infoString.val = "Downloading missing packages...\n";
                if(_tempEnableHubBool.val && !_hubBrowse.HubEnabled)
                {
                    _hubBrowse.HubEnabled = true;
                    hubWasTempEnabled = true;
                }

                hubBrowsePanelT = _hubBrowse.UITransform;
                if (hubBrowsePanelT == null)
                {
                    OnError("HubBrowsePanel not found");
                    yield break;
                }

                missingPackagesPanelT = hubBrowsePanelT.Find("MissingPackagesPanel");
                if (missingPackagesPanelT == null)
                {
                    OnError("MissingPackagesPanel not found");
                    yield break;
                }

                contentT = missingPackagesPanelT.Find("InnerPanel/HubDownloads/Downloads/Viewport/Content");
                if (contentT == null)
                {
                    OnError("InnerPanel/HubDownloads/Downloads/Viewport/Content not found");
                    yield break;
                }
            }
            catch(Exception e)
            {
                if(_logErrorsBool.val) _logBuilder.Exception(e);
                yield break;
            }
#endregion SetupVariables

#region OpenMissingPackagesPanel
            var objectsVamWillDestroy = new List<GameObject>();
            foreach(Transform packageDownloadPanel in contentT)
            {
                if(packageDownloadPanel != null)
                {
                    objectsVamWillDestroy.Add(packageDownloadPanel.gameObject);
                }
            }

            _hubBrowse.CallAction("OpenMissingPackagesPanel");

            // wait for VAM to destroy package download panels from a previous scan
            {
                float timeout = Time.time + 5;
                while(objectsVamWillDestroy.Any(go => go != null) && Time.time < timeout)
                {
                    yield return null;
                }

                if(Time.time >= timeout)
                {
                    OnError("Timeout: VAM did not destroy previous package download panels");
                    yield break;
                }
            }

            // wait for package download panels to exist
            {
                float timeout = Time.time + 5;
                while(contentT.childCount == 0 && Time.time < timeout)
                {
                    yield return null;
                }

                if(Time.time >= timeout)
                {
                    OnError("Timeout: No package download panels found");
                    yield break;
                }
            }

            // wait for HubBrowsePanel to be active; probably unnecessary
            {
                float timeout = Time.time + 10;
                while(!hubBrowsePanelT.gameObject.activeInHierarchy && Time.time < timeout)
                {
                    yield return null;
                    hubBrowsePanelT = _hubBrowse.UITransform;
                }

                if(Time.time >= timeout)
                {
                    OnError("Timeout: HubBrowsePanel not active");
                    yield break;
                }
            }

            // wait for Hub to be enabled
            bool waitForRefresh = hubWasTempEnabled;
            if(!_hubBrowse.HubEnabled)
            {
                yield return null;
                var indicator = hubBrowsePanelT.Find("HubDisabledIndicator");
                if(indicator == null)
                {
                    OnError("HubDisabledIndicator not found");
                    yield break;
                }

                while(!_hubBrowse.HubEnabled)
                {
                    if(!indicator.gameObject.activeInHierarchy)
                    {
                        // TODO OnError?
                        _infoString.val = "";
                        yield break; // exiting - user kept Hub disabled
                    }

                    yield return null; // could take long
                }

                waitForRefresh = true;
            }

            // wait for refreshing panel to disappear
            if(waitForRefresh)
            {
                var refreshingPanelT = hubBrowsePanelT.Find("GetInfoRefrehsingPanel"); // sic
                if(refreshingPanelT == null)
                {
                    OnError("GetInfoRefrehsingPanel not found");
                    yield break;
                }

                yield return null;
                while(refreshingPanelT.gameObject.activeInHierarchy)
                {
                    yield return null; // could take long
                }
            }

            Vector3 originalPos;

            // hide panel
            {
                missingPackagesPanelT.SetParent(transform);
                _hubBrowse.Hide();
                SuperController.singleton.DeactivateWorldUI();
                originalPos = missingPackagesPanelT.transform.position;
                missingPackagesPanelT.transform.position = new Vector3(originalPos.x, originalPos.y - 1000, originalPos.z);
            }
#endregion OpenMissingPackagesPanel

#region DownloadLogic
            /*
             * round 1
             * - if .#, find container by exact name match
             * - if .latest, find container by base name match and select latest version
             *  - if In Library, flag as exists
             *  - if found but Not On Hub, flag as notOnHub
             *  - if on Hub, save exact package id
             *
             * round 2
             *
             */

            // populate lists
            {
                foreach(Transform packageDownloadPanel in contentT)
                {
                    var packageUI = packageDownloadPanel.GetComponent<HubResourcePackageUI>();
                    if(packageUI == null)
                    {
                        if(_logErrorsBool.val) _logBuilder.Error("HubResourcePackageUI component not found on panel");
                        continue;
                    }

                    _packageUIs.Add(packageUI);
                }

                _missingPackages.AddRange(_packages.Where(obj => !obj.exists));
                if(_alwaysCheckForUpdatesBool.val || _missingPackages.Count > 0)
                {
                    _missingPackages.AddRange(_packages.Where(obj => !_missingPackages.Contains(obj) && obj.requireLatest));
                }
            }

            // match missing packages to correct hub resources
            try
            {
                foreach(var obj in _missingPackages)
                {
                    int latestVersion = -1;
                    HubResourcePackageUI matchedUI = null;
                    HubResourcePackage matchedItem = null;

                    foreach(var ui in _packageUIs)
                    {
                        var connectedItem = ui.connectedItem;
                        if(connectedItem == null)
                        {
                            if(_logErrorsBool.val) _logBuilder.Error("HubResourcePackage not found for HubResourcePackageUI");
                            continue;
                        }

                        if(obj.requireLatest && obj.groupName == connectedItem.GroupName)
                        {
                            int version;
                            // overrides fallback in case already set to ".latest" since latestVersion is -1
                            if(int.TryParse(connectedItem.Version, out version) && version > latestVersion)
                            {
                                latestVersion = version;
                                matchedUI = ui;
                                matchedItem = connectedItem;
                            }

                            // fallback to ".latest" if no version match found; unrealistic as panel should not list both ".latest" and specific versions
                            if(latestVersion == -1 && (connectedItem.Version == "latest" || connectedItem.Version.EndsWith(".latest")))
                            {
                                matchedUI = ui;
                                matchedItem = connectedItem;
                            }
                        }
                        else if(obj.name == connectedItem.Name.Trim())
                        {
                            matchedUI = ui;
                            matchedItem = connectedItem;
                            break;
                        }
                    }

                    obj.RegisterHubItem(matchedUI, matchedItem, latestVersion);
                }
            }
            catch(Exception e)
            {
                Debug.Log($"Exception during matching: {e}");
                OnException(e);
                // TODO break or teardown?
            }

            // foreach(var obj in _missingPackages)
            // {
            //     Debug.Log(obj.ToString());
            //     Debug.Log(DevUtils.ObjectPropertiesString(obj.connectedItem));
            // }

            // download missing packages
            foreach(var obj in _missingPackages)
            {
                if(obj.error != null)
                {
                    if(_logErrorsBool.val) _logBuilder.Error($"Error in dependency {obj.name}: {obj.error}");
                    _hadError = true;
                    continue;
                }

                if(!obj.connectedItem.NeedsDownload)
                {
                    obj.CheckExists();
                }
                else if(obj.connectedItem.CanBeDownloaded)
                {
                    _pendingPackages.Add(obj);
                }
                else
                {
                    _notOnHubPackages.Add(obj);
                }
            }

            foreach(var obj in _pendingPackages)
            {
                if(obj.packageUI.downloadButton != null)
                {
                    Debug.Log("Would click download button for " + obj.name);
                }
                else
                {
                    Debug.Log("No download button for " + obj.name);
                }
                // obj.downloadButton.onClick.Invoke();
            }

            foreach(var obj in _notOnHubPackages)
            {
                Debug.Log($"Not on Hub: {obj.name}");
            }

            // TODO replace timeout with progress bar
            {
                float timeout = Time.time + _timeoutFloat.val;
                while(_pendingPackages.Any(obj => !obj.CheckExists()) && Time.time < timeout) // could take long
                {
                    yield return null;
                }

                if(_pendingPackages.Any(obj => !obj.exists))
                {
                    if(_logErrorsBool.val) _logBuilder.Error("Timed out before downloads finished.");
                    // error = true;
                }
            }

#endregion DownloadLogic

#region Teardown
            // restore panel parent and position, and hide
            {
                missingPackagesPanelT.transform.position = originalPos;
                missingPackagesPanelT.SetParent(hubBrowsePanelT);
                missingPackagesPanelT.gameObject.SetActive(false);
            }

            // wait for user confirm panels
            {
                yield return null;
                var activePanels = new List<GameObject>();
                HandlePanelsInAlertRoot(SuperController.singleton.normalAlertRoot, activePanels);
                HandlePanelsInAlertRoot(SuperController.singleton.worldAlertRoot, activePanels);
                while(activePanels.Count > 0)
                {
                    try
                    {
                        HandlePanelsInAlertRoot(SuperController.singleton.normalAlertRoot, activePanels);
                        HandlePanelsInAlertRoot(SuperController.singleton.worldAlertRoot, activePanels);

                        // cleanup
                        for(int i = activePanels.Count - 1; i >= 0; i--)
                        {
                            var go = activePanels[i];
                            if(go == null || !go.activeInHierarchy)
                            {
                                activePanels.RemoveAt(i);
                            }
                        }
                    }
                    catch(Exception e)
                    {
                        _logBuilder.Exception(e);
                        break;
                    }

                    yield return null;
                }
            }

            if(hubWasTempEnabled)
            {
                _hubBrowse.HubEnabled = false;
            }

            _downloadCo = null;
            // _findDependenciesAction.actionCallback(); // TODO appropriate?
            if(_packages.TrueForAll(obj => obj.exists))
            {
                // TODO trigger on download complete
            }
            else
            {
                // TODO fix
                const string info ="\n\nSomething went wrong (timeout or error).\n\nPackages may still be downloading in the background.\n\nTODO configurable trigger on timeout/error";
                _infoString.val += info;
                _logBuilder.Message(info);
                // TODO trigger on timeout/error
            }
#endregion Teardown
        }

        void HandlePanelsInAlertRoot(Transform root, List<GameObject> activePanels)
        {
            foreach(Transform child in root)
            {
                if(child.name != "OKCancelAlertPanel(Clone)")
                {
                    continue;
                }

                var textComponent = child.Find("Panel/Text").GetComponent<Text>();
                if(textComponent.text.Contains("'Allow Plugins Network Access'"))
                {
                    continue; // wrong panel, if this comes up the user must accept it manually
                }

                var go = child.gameObject;
                if(go.activeInHierarchy && !activePanels.Contains(go))
                {
                    activePanels.Add(go);
                    if(_autoAcceptPackagePluginsBool.val)
                    {
                        child.Find("Panel/OKButton").GetComponent<Button>().onClick.Invoke();
                    }
                }
            }
        }

        void OnDestroy()
        {
            if(_uiListener != null)
            {
                DestroyImmediate(_uiListener);
            }

            if(_downloadCo != null)
            {
                StopCoroutine(_downloadCo);
            }
        }
    }
}
