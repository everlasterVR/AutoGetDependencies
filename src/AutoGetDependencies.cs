using System;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using MVR.FileManagementSecure;
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
        bool _initialized;
        Coroutine _downloadCo;

        JSONStorableBool _searchSubDependenciesBool;
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

                _searchSubDependenciesBool = new JSONStorableBool("Search sub-dependencies", true);
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
            var title = CreateTextField(new JSONStorableString("title", "Auto Get Dependencies"));
            title.height = 60;
            var layoutElement = title.GetComponent<LayoutElement>();
            layoutElement.minHeight = 60;
            layoutElement.preferredHeight = 60;
            title.UItext.fontSize = 32;
            title.UItext.fontStyle = FontStyle.Bold;
            title.UItext.alignment = TextAnchor.LowerCenter;
            title.backgroundColor = Color.clear;
            var rect = title.UItext.GetComponent<RectTransform>();
            var pos = rect.anchoredPosition;
            pos.y = -15;
            rect.anchoredPosition = pos;

            CreateToggle(_searchSubDependenciesBool);
            {
                var button = CreateButton(_findDependenciesAction.name);
                button.height = 75;
                _findDependenciesAction.RegisterButton(button);
            }

            CreateSpacer().height = 15;
            CreateTextField(_infoString, true).height = 1200;
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
        }

        bool _dependenciesFound;

        void FindDependenciesCallback()
        {
            _packages.Clear();
            FindDependencies(_metaJson, _searchSubDependenciesBool.val);
            var sb = new StringBuilder();
            sb.Append("Found packages (highlight missing):\n\n");
            foreach(var obj in _packages)
            {
                sb.AppendLine(obj.exists ? obj.id : $"<b>{obj.id}</b>");
            }

            if(_packages.TrueForAll(obj => obj.exists))
            {
                sb.Append("\n\nAll dependencies installed. TODO configurable trigger");
                _logBuilder.Message("All dependencies installed. TODO configurable trigger");
            }

            _infoString.val = sb.ToString();
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
                    if(depth == 0)
                    {
                        _infoString.val = "No dependencies found: invalid meta.json.";
                    }

                    return;
                }

                foreach(string key in dependenciesJc.Keys)
                {
                    string trimmed = key.Trim();
                    var package = _packages.FirstOrDefault(obj => obj.id == trimmed);
                    if(package == null)
                    {
                        _packages.Add(new PackageObj(trimmed, FileManagerSecure.PackageExists(trimmed)));
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

        void DownloadMissingCallback()
        {
            try
            {
                if(!_dependenciesFound)
                {
                    _logBuilder.Message("Must find dependencies first.");
                    return;
                }

                if(_packages.TrueForAll(obj => obj.exists))
                {
                    _infoString.val = "All dependencies are already downloaded.";
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

        /*
         * HubBrowsePanel/MissingPackagesPanel/InnerPanel/HubDownloads/Downloads/Viewport/Content
         *      PackageDownloadPanel(Clone) (multiple, one per package)
         *          HorizontalLayout
         *              MainContainer
         *                  ResourceButton | Text
         *                  DownloadButton | Button
         */
        IEnumerator DownloadMissingViaHubCo()
        {
            _infoString.val = "Downloading missing packages...\n";
            _hubBrowse.CallAction("OpenMissingPackagesPanel");
            bool hubWasTempEnabled = false;
            if(_tempEnableHubBool.val && !_hubBrowse.HubEnabled)
            {
                _hubBrowse.HubEnabled = true;
                hubWasTempEnabled = true;
            }

            var hubBrowsePanelT = _hubBrowse.UITransform;
            if(hubBrowsePanelT == null)
            {
                if(_logErrorsBool.val) _logBuilder.Error("HubBrowsePanel not found");
                _infoString.val = "";
                yield break;
            }

            // wait for HubBrowsePanel to be active
            {
                float timeout = Time.time + 10;
                while((hubBrowsePanelT == null || !hubBrowsePanelT.gameObject.activeInHierarchy) && Time.time < timeout)
                {
                    yield return null;
                    hubBrowsePanelT = _hubBrowse.UITransform;
                }

                if(Time.time >= timeout)
                {
                    if(_logErrorsBool.val) _logBuilder.Error("Timeout: HubBrowsePanel not found or active");
                    _infoString.val = "";
                    yield break;
                }
            }

            bool waitForRefresh = false;
            if(hubWasTempEnabled)
            {
                var refreshingPanelT = hubBrowsePanelT.Find("GetInfoRefrehsingPanel"); // sic
                yield return null;
                while(refreshingPanelT.gameObject.activeInHierarchy)
                {
                    yield return null; // could take long
                }

                waitForRefresh = true;
            }

            if(!_hubBrowse.HubEnabled)
            {
                yield return null;
                var indicator = hubBrowsePanelT.Find("HubDisabledIndicator");
                while(!_hubBrowse.HubEnabled)
                {
                    if(!indicator.gameObject.activeInHierarchy)
                    {
                        _infoString.val = "";
                        yield break; // exiting - user kept Hub disabled
                    }

                    yield return null;
                }

                waitForRefresh = true;
            }

            // wait for refreshing panel to disappear
            if(waitForRefresh)
            {
                var refreshingPanelT = hubBrowsePanelT.Find("GetInfoRefrehsingPanel"); // sic
                yield return null;
                while(refreshingPanelT.gameObject.activeInHierarchy)
                {
                    yield return null; // could take long
                }
            }

            bool error = false;
            // execute main part
            {
                var missingPackagesPanelT = hubBrowsePanelT.Find("MissingPackagesPanel");
                missingPackagesPanelT.SetParent(transform);
                _hubBrowse.Hide();

                SuperController.singleton.DeactivateWorldUI();
                var position = missingPackagesPanelT.transform.position;
                missingPackagesPanelT.transform.position = new Vector3(position.x, position.y - 1000, position.z);

                var contentT = missingPackagesPanelT.Find("InnerPanel/HubDownloads/Downloads/Viewport/Content");
                if(contentT == null)
                {
                    if(_logErrorsBool.val) _logBuilder.Error("Content transform not found");
                    error = true;
                }

                if(!error)
                {
                    float timeout = Time.time + _timeoutFloat.val;
                    string errorStr = ParseMissingPackagesUI(contentT);
                    while(errorStr != null && Time.time < timeout)
                    {
                        yield return null;
                        errorStr = ParseMissingPackagesUI(contentT);
                    }

                    if(errorStr != null)
                    {
                        if(_logErrorsBool.val) _logBuilder.Error(errorStr);
                        error = true;
                    }

                    // download missing packages
                    if(!error)
                    {
                        var pendingPackages = _packages.Where(obj => obj.pending).ToList();
                        foreach(var obj in pendingPackages)
                        {
                            obj.downloadButton.onClick.Invoke();
                        }

                        while(!pendingPackages.TrueForAll(obj => obj.CheckExists()) && Time.time < timeout) // could take long
                        {
                            yield return null;
                        }

                        if(!pendingPackages.TrueForAll(obj => obj.exists))
                        {
                            if(_logErrorsBool.val) _logBuilder.Error("Timed out before downloads finished.");
                            // error = true;
                        }
                    }
                }

                missingPackagesPanelT.transform.position = position;
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

            // finish
            {
                if(hubWasTempEnabled)
                {
                    _hubBrowse.HubEnabled = false;
                }

                _downloadCo = null;
                _findDependenciesAction.actionCallback();
                if(_packages.TrueForAll(obj => obj.exists))
                {
                    // TODO trigger on download complete
                }
                else
                {
                    const string info ="\n\nSomething went wrong (timeout or error). Packages may still be downloading in the background. TODO configurable trigger on timeout/error";
                    _infoString.val += info;
                    _logBuilder.Message(info);
                    // TODO trigger on timeout/error
                }
            }
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

        string ParseMissingPackagesUI(Transform contentT)
        {
            try
            {
                foreach(var obj in _packages)
                {
                    if(obj.exists)
                    {
                        continue;
                    }

                    string id = obj.id;
                    var containerT = id.EndsWith(".latest")
                        ? FindLatestContainerByPackageBaseName(contentT, id)
                        : FindContainerByPackageId(contentT, id);

                    if(containerT == null)
                    {
                        return $"{id}: Container transform not found";
                    }

                    var downloadButtonT = containerT.Find("DownloadButton");
                    if(downloadButtonT == null)
                    {
                        return $"{id}: DownloadButton transform not found";
                    }

                    if(!downloadButtonT.gameObject.activeSelf)
                    {
                        return $"{id}: DownloadButton not active - resource not on Hub?";
                    }

                    var downloadButton = downloadButtonT.GetComponent<Button>();
                    if(downloadButton == null)
                    {
                        return $"{id}: DownloadButton has no Button component";
                    }

                    obj.pending = true;
                    obj.downloadButton = downloadButton;
                }
            }
            catch(Exception e)
            {
                _logBuilder.Exception(e);
            }

            return null;
        }

        Transform FindLatestContainerByPackageBaseName(Transform contentT, string packageId)
        {
            string packageBaseName = packageId.Replace(".latest", "");
            Transform latestContainer = null;
            int latestVersion = -1;
            foreach(Transform child in contentT)
            {
                if(child.name != "PackageDownloadPanel(Clone)")
                {
                    continue;
                }

                var container = child.Find("HorizontalLayout/MainContainer");
                if(container == null)
                {
                    continue;
                }

                var resourceButtonText = container.Find("ResourceButton/Text");
                if(resourceButtonText == null)
                {
                    continue;
                }

                string resourceId = resourceButtonText.GetComponent<Text>().text;
                int versionSeparatorIdx = resourceId.LastIndexOf(".", StringComparison.Ordinal);
                if(versionSeparatorIdx == -1)
                {
                    _logBuilder.Debug($"{packageId}: Invalid Hub resource: {packageId}");
                    continue;
                }

                string resourceBaseName = resourceId.Substring(0, versionSeparatorIdx);
                if(resourceBaseName == packageBaseName)
                {
                    string versionText = resourceId.Substring(versionSeparatorIdx + 1);
                    int version;
                    if(!int.TryParse(versionText, out version))
                    {
                        _logBuilder.Debug($"{packageId}: Invalid Hub resource version: {versionText}");
                        continue;
                    }

                    if(version > latestVersion)
                    {
                        latestVersion = version;
                        latestContainer = container;
                    }
                }
            }

            return latestContainer;
        }

        static Transform FindContainerByPackageId(Transform content, string packageId)
        {
            foreach(Transform child in content)
            {
                if(child.name != "PackageDownloadPanel(Clone)")
                {
                    continue;
                }

                var container = child.Find("HorizontalLayout/MainContainer");
                if(container == null)
                {
                    continue;
                }

                var resourceButtonText = container.Find("ResourceButton/Text");
                if(resourceButtonText == null)
                {
                    continue;
                }

                if(resourceButtonText.GetComponent<Text>().text == packageId)
                {
                    return container;
                }
            }

            return null;
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
