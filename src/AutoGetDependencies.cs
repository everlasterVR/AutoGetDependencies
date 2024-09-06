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

// Licensed under Creative Commons Attribution 4.0 International https://creativecommons.org/licenses/by/4.0/
// (c) 2024 everlaster
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
        JSONStorableAction _downloadAction;
        JSONStorableString _infoString;
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
                _findDependenciesAction = new JSONStorableAction("1. Find dependencies", FindDependenciesCallback);
                _downloadAction = new JSONStorableAction("2. Download missing", DownloadMissingCallback);
                _infoString = new JSONStorableString("Info", "");
                _logErrorsBool = new JSONStorableBool("Log errors", false);
                RegisterBool(_searchSubDependenciesBool);
                RegisterAction(_findDependenciesAction);
                RegisterAction(_downloadAction);
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
            _findDependenciesAction.RegisterButton(CreateButton(_findDependenciesAction.name));
            _downloadAction.RegisterButton(CreateButton(_downloadAction.name));
            CreateTextField(_infoString, true).height = 1200;
            CreateToggle(_logErrorsBool);
        }

        bool _firstTimeFind;

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

            _infoString.val = sb.ToString();
            _firstTimeFind = true;
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
                if(!_firstTimeFind)
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
            var hubBrowsePanelT = _hubBrowse.UITransform;

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
                    yield break;
                }
            }

            // wait for Hub to be enabled
            if(!_hubBrowse.HubEnabled)
            {
                yield return null;
                var indicator = hubBrowsePanelT.Find("HubDisabledIndicator");
                while(!_hubBrowse.HubEnabled)
                {
                    if(!indicator.gameObject.activeInHierarchy)
                    {
                        yield break; // exiting - user kept Hub disabled
                    }

                    yield return null;
                }

                // continuing - hub enabled by user
                var refreshingPanelT = hubBrowsePanelT.Find("GetInfoRefrehsingPanel"); // sic
                yield return null;
                while(refreshingPanelT.gameObject.activeInHierarchy)
                {
                    yield return null; // could take long
                }
            }

            // execute main part
            {
                var missingPackagesPanelT = hubBrowsePanelT.Find("MissingPackagesPanel");
                missingPackagesPanelT.SetParent(transform);
                _hubBrowse.Hide();
                SuperController.singleton.DeactivateWorldUI();

                var position = missingPackagesPanelT.transform.position;
                missingPackagesPanelT.transform.position = new Vector3(position.x, position.y - 1000, position.z);
                ParseMissingPackagesUI(missingPackagesPanelT);

                // download missing packages
                {
                    var pendingPackages = _packages.Where(obj => obj.pending).ToList();
                    foreach(var obj in pendingPackages)
                    {
                        obj.downloadButton.onClick.Invoke();
                    }

                    while(!pendingPackages.TrueForAll(obj => obj.CheckExists()))
                    {
                        yield return null; // could take long
                    }
                }

                missingPackagesPanelT.transform.position = position;
                missingPackagesPanelT.SetParent(hubBrowsePanelT);
                missingPackagesPanelT.gameObject.SetActive(false);


                // TODO if any missing -> error -> trigger
            }

            // finish
            {
                _downloadCo = null;
                _findDependenciesAction.actionCallback();
                if(_packages.TrueForAll(obj => obj.exists))
                {
                    Debug.Log("All packages downloaded.");
                    // TODO trigger on complete
                }
                else
                {
                    Debug.Log("Some packages not downloaded.");
                    // TODO trigger on timeout/error
                }
            }
        }

        void ParseMissingPackagesUI(Transform missingPackagesPanelT)
        {
            try
            {
                var contentT = missingPackagesPanelT.Find("InnerPanel/HubDownloads/Downloads/Viewport/Content");
                if(contentT == null)
                {
                    if(_logErrorsBool.val) _logBuilder.Error("Content transform not found");
                    return;
                }

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
                        if(_logErrorsBool.val) _logBuilder.Error($"{id}: Container transform not found");
                        continue;
                    }

                    var downloadButtonT = containerT.Find("DownloadButton");
                    if(downloadButtonT == null)
                    {
                        if(_logErrorsBool.val) _logBuilder.Error($"{id}: DownloadButton transform not found");
                        continue;
                    }

                    if(!downloadButtonT.gameObject.activeSelf)
                    {
                        if(_logErrorsBool.val) _logBuilder.Error($"{id}: DownloadButton not active - resource not on Hub?");
                        continue;
                    }

                    var downloadButton = downloadButtonT.GetComponent<Button>();
                    if(downloadButton == null)
                    {
                        if(_logErrorsBool.val) _logBuilder.Error($"{id}: DownloadButton has no Button component");
                        continue;
                    }

                    obj.pending = true;
                    obj.downloadButton = downloadButton;
                }
            }
            catch(Exception e)
            {
                _logBuilder.Exception(e);
            }
        }

        Transform FindLatestContainerByPackageBaseName(Transform content, string packageId)
        {
            string packageBaseName = packageId.Replace(".latest", "");
            Transform latestContainer = null;
            int latestVersion = -1;
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

        void Update()
        {
            if(!_initialized)
            {
                return;
            }

            // TODO timeout coroutine
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
