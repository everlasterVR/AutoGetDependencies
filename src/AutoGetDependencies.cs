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
        bool _anyMissing;
        bool _anyUpdateNeeded;
        Coroutine _downloadCo;
        Coroutine _handleUserConfirmPanelsCo;
        bool _finished;
        string _error;
        float _progress;

        JSONStorableBool _searchSubDependenciesBool;
        JSONStorableBool _alwaysCheckForUpdatesBool;
        JSONStorableAction _findDependenciesAction;
        JSONStorableString _infoString;
        JSONStorableBool _tempEnableHubBool;
        JSONStorableBool _autoAcceptPackagePluginsBool;
        JSONStorableAction _downloadAction;
        JSONStorableFloat _progressFloat; // TODO select UISlider from scene to act as progress bar (copy UISliderSync)
        JSONStorableBool _logErrorsBool;
        // TODO action to trigger teardown in case of infinite yield return null

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

            if(_anyMissing || _anyUpdateNeeded)
            {
                UpdatePendingInfo();
            }
            else if(_finished)
            {
                UpdateFinishedInfo();
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
                RegisterBool(_searchSubDependenciesBool);

                _alwaysCheckForUpdatesBool = new JSONStorableBool("Always check for updates to '.latest'", false);
                RegisterBool(_alwaysCheckForUpdatesBool);

                _findDependenciesAction = new JSONStorableAction("1. Find dependencies in meta.json", FindDependenciesCallback);
                RegisterAction(_findDependenciesAction);

                _infoString = new JSONStorableString("Info", "");

                _tempEnableHubBool = new JSONStorableBool("Temp auto-enable Hub if needed", false);
                RegisterBool(_tempEnableHubBool);

                _autoAcceptPackagePluginsBool = new JSONStorableBool("Auto-accept package plugins", false);
                RegisterBool(_autoAcceptPackagePluginsBool);

                _downloadAction = new JSONStorableAction("2. Download missing packages", DownloadMissingCallback);
                RegisterAction(_downloadAction);

                _progressFloat = new JSONStorableFloat("Download progress (%)", 0, 0, 100);

                _logErrorsBool = new JSONStorableBool("Log errors", false);
                RegisterBool(_logErrorsBool);

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
            {
                var button = CreateButton(_downloadAction.name);
                button.height = 75;
                _downloadAction.RegisterButton(button);
            }

            {
                var uiDynamic = CreateSlider(_progressFloat);
                uiDynamic.valueFormat = "F0";
                uiDynamic.HideButtons();
                uiDynamic.SetInteractable(false);
            }

            CreateSpacer().height = 15;
            CreateToggle(_logErrorsBool);

            // TODO plugin usage info
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

        void FindDependenciesCallback()
        {
            if(_downloadCo != null)
            {
                StopCoroutine(_downloadCo);
            }

            _finished = false;
            _error = null;
            _progress = 0;
            _progressFloat.val = 0;
            SuperController.singleton.RescanPackages();
            _packages.Clear();
            FindDependencies(_metaJson, _searchSubDependenciesBool.val);

            // TODO find any disabled dependencies and call trigger

            _anyMissing = _packages.Exists(obj => !obj.exists);
            _anyUpdateNeeded = _packages.Exists(obj => obj.exists && obj.requireLatest && (_alwaysCheckForUpdatesBool.val || _anyMissing));
            if(_anyMissing || _anyUpdateNeeded)
            {
                Debug.Log("TODO trigger on dependencies found & pending download");
                // TODO trigger on dependencies found & pending download
                UpdatePendingInfo();
            }
            else
            {
                _progress = 1;
                _progressFloat.val = 100;
                Debug.Log("TODO trigger on success");
                // TODO trigger on success
                UpdateFinishedInfo();
            }
        }

        void FindDependencies(JSONClass json, bool recursive = false, int depth = 0)
        {
            try
            {
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

        void UpdatePendingInfo()
        {
            if(!_uiCreated)
            {
                return;
            }

            _infoString.dynamicText.UItext.horizontalOverflow = HorizontalWrapMode.Overflow;
            var sb = new StringBuilder();

            AppendPackagesInfo(
                sb,
                "Missing, download needed",
                new Color(0.75f, 0, 0),
                _packages,
                obj => !obj.exists
            );
            AppendPackagesInfo(
                sb,
                "Found, check for update needed",
                new Color(0, 0, 0.50f),
                _packages,
                obj => obj.exists && obj.requireLatest && (_alwaysCheckForUpdatesBool.val || _anyMissing)
            );
            AppendPackagesInfo(
                sb,
                "Found",
                new Color(0, 0.50f, 0),
                _packages,
                obj => obj.exists
            );

            _infoString.val = sb.ToString();
        }

        static void AppendPackagesInfo(StringBuilder sb, string title, Color titleColor, List<PackageObj> packages, Func<PackageObj, bool> condition)
        {
            sb.AppendFormat("<size=28><color=#{0}><b>{1}:</b></color></size>\n\n", ColorUtility.ToHtmlStringRGBA(titleColor), title);
            int count = 0;
            string optionalColor = ColorUtility.ToHtmlStringRGBA(new Color(0.4f, 0.4f, 0.4f));
            foreach (var obj in packages)
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

        void UpdateFinishedInfo()
        {
            if(!_uiCreated)
            {
                return;
            }

            _infoString.dynamicText.UItext.horizontalOverflow = HorizontalWrapMode.Wrap;
            var sb = new StringBuilder();

            sb.Append(
                _packages.TrueForAll(obj => obj.exists)
                    ? "All dependencies are successfully installed.\n"
                    : "Some dependencies are still missing.\n"
            );

            if(_error != null)
            {
                sb.Append("\nErrors:\n");
                sb.Append(_error);
                sb.Append("\n\n");
            }

            AppendPackagesInfo(
                sb,
                "Not on Hub",
                new Color(0.75f, 0, 0),
                _notOnHubPackages,
                obj => true
            );

            if(sb.Length > 16000)
            {
                const string truncated = "\n\n(too long, truncated)";
                sb.Length = 16000 - truncated.Length;
                sb.Append(truncated);
            }

            try
            {
                _infoString.val = sb.ToString();
            }
            catch(Exception e)
            {
                _logBuilder.Exception(e);
            }
        }

        void DownloadMissingCallback()
        {
            if(!_anyMissing && !_anyUpdateNeeded)
            {
                // TODO .. ?
                _infoString.val = "All dependencies are already installed.";
                if(_logErrorsBool.val) _logBuilder.Error("Must find dependencies first.");
                return;
            }

            _downloadCo = StartCoroutine(DownloadMissingViaHubCo());
        }

        void OnError(string message, bool teardown = true)
        {
            if (_logErrorsBool.val) _logBuilder.Error(message);
            _error = $"\n{message}";
            if(teardown)
            {
                Teardown();
            }
        }

        void OnException(Exception e, bool teardown = true)
        {
            if (_logErrorsBool.val) _logBuilder.Exception(e);
            _error += $"\n{e.Message}";
            if(teardown)
            {
                Teardown();
            }
        }

        bool _panelRelocated;
        bool _hubWasTempEnabled;
        Transform _hubBrowsePanelT;
        Transform _missingPackagesPanelT;
        Transform _contentT;
        Vector3 _originalPanelPos;

        IEnumerator DownloadMissingViaHubCo()
        {
#region SetupVariables
            _packageUIs.Clear();
            _missingPackages.Clear();
            _pendingPackages.Clear();
            _notOnHubPackages.Clear();
            _panelRelocated = false;
            _error = null;

            try
            {
                _infoString.val = "Downloading missing packages...\n";
                if(_tempEnableHubBool.val && !_hubBrowse.HubEnabled)
                {
                    _hubBrowse.HubEnabled = true;
                    _hubWasTempEnabled = true;
                }

                _hubBrowsePanelT = _hubBrowse.UITransform;
                if (_hubBrowsePanelT == null)
                {
                    OnError("HubBrowsePanel not found");
                    yield break;
                }

                _missingPackagesPanelT = _hubBrowsePanelT.Find("MissingPackagesPanel");
                if (_missingPackagesPanelT == null)
                {
                    OnError("MissingPackagesPanel not found");
                    yield break;
                }

                _contentT = _missingPackagesPanelT.Find("InnerPanel/HubDownloads/Downloads/Viewport/Content");
                if (_contentT == null)
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
            foreach(Transform packageDownloadPanel in _contentT)
            {
                if(packageDownloadPanel != null)
                {
                    objectsVamWillDestroy.Add(packageDownloadPanel.gameObject);
                }
            }

            _hubBrowse.CallAction("OpenMissingPackagesPanel");

            // hide panel
            {
                _missingPackagesPanelT.SetParent(transform);
                _hubBrowse.Hide();
                SuperController.singleton.DeactivateWorldUI();
                _originalPanelPos = _missingPackagesPanelT.transform.position;
                _missingPackagesPanelT.transform.position = new Vector3(_originalPanelPos.x, _originalPanelPos.y - 1000, _originalPanelPos.z);
                _panelRelocated = true;
            }

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
                while(_contentT.childCount == 0 && Time.time < timeout)
                {
                    yield return null;
                }

                if(Time.time >= timeout)
                {
                    OnError("Timeout: No package download panels found");
                    yield break;
                }
            }

            // wait for Hub to be enabled
            bool waitForRefresh = _hubWasTempEnabled;
            if(!_hubBrowse.HubEnabled)
            {
                yield return null;
                var indicator = _hubBrowsePanelT.Find("HubDisabledIndicator");
                if(indicator == null || indicator.gameObject == null)
                {
                    OnError("HubDisabledIndicator not found");
                    yield break;
                }

                while(!_hubBrowse.HubEnabled)
                {
                    if(!indicator.gameObject.activeInHierarchy)
                    {
                        Teardown();
                        yield break; // exiting - user kept Hub disabled
                    }

                    yield return null; // could take long
                }

                waitForRefresh = true;
            }

            // wait for refreshing panel to disappear
            if(waitForRefresh)
            {
                var refreshingPanelT = _hubBrowsePanelT.Find("GetInfoRefrehsingPanel"); // sic
                if(refreshingPanelT == null || refreshingPanelT.gameObject == null)
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
#endregion OpenMissingPackagesPanel

            /*
             * - if .#, find container by exact name match
             * - if .latest, find container by base name match and select latest version
             *  - if In Library, flag as exists
             *  - if found but Not On Hub, flag as notOnHub
             *  - if on Hub, save exact package id
             */

#region PreDownload
            // populate lists
            try
            {
                foreach(Transform packageDownloadPanel in _contentT)
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
            catch(Exception e)
            {
                OnException(e);
                yield break;
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
                OnException(e);
                yield break;
            }

            // split into groups
            try
            {
                foreach(var obj in _missingPackages)
                {
                    // Debug.Log(obj.ToString());
                    // Debug.Log(DevUtils.ObjectPropertiesString(obj.connectedItem));

                    if(obj.error != null)
                    {
                        string error = $"'{obj.name}' error: {obj.error}";
                        if(_logErrorsBool.val) _logBuilder.Error(error);
                        _error += $"\n{error}";
                        if(!obj.connectedItem.CanBeDownloaded)
                        {
                            _notOnHubPackages.Add(obj);
                        }

                        continue;
                    }

                    if(!obj.connectedItem.NeedsDownload)
                    {
                        _logBuilder.Debug($"{obj.name} item NeedsDownload=False");
                        continue;
                    }

                    if(obj.connectedItem.CanBeDownloaded)
                    {
                        _pendingPackages.Add(obj);
                    }
                    else
                    {
                        _notOnHubPackages.Add(obj);
                    }
                }
            }
            catch(Exception e)
            {
                OnException(e);
                yield break;
            }

#endregion PreDownload

#region Download
            int count = _pendingPackages.Count;
            if(count <= 0)
            {
                OnError("No packages can be downloaded.");
                yield break;
            }

            // setup callbacks and start downloads
            try
            {
                foreach(var obj in _pendingPackages)
                {
                    MVR.Hub.HubResourcePackage.DownloadStartCallback startCallback = _ => obj.downloadStarted = true;
                    MVR.Hub.HubResourcePackage.DownloadCompleteCallback completeCallback = (_, __) => obj.downloadComplete = true;
                    MVR.Hub.HubResourcePackage.DownloadErrorCallback errorCallback = (_, e) => obj.downloadError = e;
                    obj.connectedItem.downloadStartCallback += startCallback;
                    obj.connectedItem.downloadCompleteCallback += completeCallback;
                    obj.connectedItem.downloadErrorCallback += errorCallback;
                    obj.storeStartCallback = startCallback;
                    obj.storeCompleteCallback = completeCallback;
                    obj.storeErrorCallback = errorCallback;
                    obj.packageUI.downloadButton.onClick.Invoke();
                }
            }
            catch(Exception e)
            {
                OnException(e);
                yield break;
            }

            // wait for downloads to complete
            var wait = new WaitForSeconds(0.1f);
            while(true)
            {
                try
                {
                    float sum = 0;
                    bool allDone = true;
                    for(int i = _pendingPackages.Count - 1; i >= 0; i--)
                    {
                        var obj = _pendingPackages[i];
                        if(obj.downloadComplete)
                        {
                            sum += 1;
                            continue;
                        }

                        if(obj.downloadError != null)
                        {
                            if(_logErrorsBool.val) _logBuilder.Error($"'{obj.name}' error: {obj.downloadError}");
                            _error += $"\n'{obj.name}' error: {obj.downloadError}";
                            _pendingPackages.RemoveAt(i);
                            continue;
                        }

                        if(obj.downloadStarted)
                        {
                            var slider = obj.packageUI.progressSlider;
                            sum += Mathf.InverseLerp(slider.minValue, slider.maxValue, slider.value);
                        }

                        allDone = false;
                    }

                    _progress = sum / count;
                    _progressFloat.val = _progress * 100;
                    if(allDone)
                    {
                        break;
                    }
                }
                catch(Exception e)
                {
                    OnException(e, false);
                    break;
                }

                yield return wait;
            }
#endregion Download

            _handleUserConfirmPanelsCo = StartCoroutine(WaitForUserConfirmPanels());
            if(_error == null)
            {
                yield return _handleUserConfirmPanelsCo;
            }

            Teardown();
        }

#region Teardown
        IEnumerator WaitForUserConfirmPanels()
        {
            yield return null;
            var activePanels = new List<GameObject>();
            try
            {
                HandlePanelsInAlertRoot(SuperController.singleton.normalAlertRoot, activePanels);
                HandlePanelsInAlertRoot(SuperController.singleton.worldAlertRoot, activePanels);
            }
            catch(Exception e)
            {
                _logBuilder.Exception(e);
                _handleUserConfirmPanelsCo = null;
                yield break;
            }

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

            _handleUserConfirmPanelsCo = null;
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

        void Teardown()
        {
            if(_panelRelocated && _missingPackagesPanelT != null && _missingPackagesPanelT.gameObject != null && _hubBrowsePanelT != null)
            {
                _missingPackagesPanelT.position = _originalPanelPos;
                _missingPackagesPanelT.SetParent(_hubBrowsePanelT);
                _missingPackagesPanelT.gameObject.SetActive(false);
                _panelRelocated = false;
            }

            foreach(var obj in _missingPackages)
            {
                obj.CleanupCallbacks();
            }

            if(_hubWasTempEnabled)
            {
                _hubBrowse.HubEnabled = false;
                _hubWasTempEnabled = false;
            }

            foreach(var obj in _packages)
            {
                obj.CheckExists();
            }

            // suppress error triggers and not on Hub packages triggers if all packages happen to somehow exist regardless
            if(_packages.TrueForAll(obj => obj.exists))
            {
                // TODO trigger on success
            }
            else
            {
                if(_notOnHubPackages.Count > 0)
                {
                    // TODO trigger on not on Hub packages found
                    // TODO send list of not on Hub packages to UIText
                    // TODO provide a triggerable action for copying the not on Hub package names to clipboard
                }

                if(_error != null)
                {
                    // TODO trigger on errors
                }
            }

            _downloadCo = null;
            _finished = true;
            UpdateFinishedInfo();
        }
#endregion Teardown

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

            if(_handleUserConfirmPanelsCo != null)
            {
                StopCoroutine(_handleUserConfirmPanelsCo);
            }
        }
    }
}
