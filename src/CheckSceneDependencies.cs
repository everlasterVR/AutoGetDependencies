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
    sealed class CheckDependencies : Script
    {
        public override bool ShouldIgnore() => false;

        JSONClass _metaJson;
        HubDownloader _downloader;
        readonly Dictionary<string, bool> _packages = new Dictionary<string, bool>();

        JSONStorableBool _searchSubDependenciesBool;
        JSONStorableAction _findDependenciesAction;
        JSONStorableAction _downloadAction;
        JSONStorableString _infoString;

        public override void Init()
        {
            try
            {
                logBuilder = new LogBuilder(nameof(CheckDependencies));
                if(containingAtom.type == "SessionPluginManager")
                {
                    logBuilder.Error("Do not add as Session Plugin.", false);
                    enabledJSON.valNoCallback = false;
                    return;
                }

                if(IsDuplicate())
                {
                    return;
                }

                _metaJson = FindLoadedSceneMetaJson();
                if(_metaJson == null)
                {
                    logBuilder.Error("Invalid scene (must be from package).", false);
                    enabledJSON.valNoCallback = false;
                    return;
                }

                _downloader = HubDownloader.singleton;
                if(_downloader == null)
                {
                    logBuilder.Error("HubDownloader not found.", false);
                    enabledJSON.valNoCallback = false;
                    return;
                }

                _searchSubDependenciesBool = new JSONStorableBool("Search sub-dependencies", true);
                _findDependenciesAction = new JSONStorableAction("1. Find dependencies", FindDependencies);
                _downloadAction = new JSONStorableAction("2. Download missing", DownloadMissing);
                _infoString = new JSONStorableString("Info", "");
                RegisterBool(_searchSubDependenciesBool);
                RegisterAction(_findDependenciesAction);
                RegisterAction(_downloadAction);

                CheckDownloaderEnabled();
                initialized = true;
            }
            catch(Exception e)
            {
                logBuilder.Exception(e);
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

        bool CheckDownloaderEnabled(bool resetInfo = false)
        {
            if(!_downloader.HubDownloaderEnabled)
            {
                _infoString.val = "Package Downloader not enabled. Go to User Preferences -> Security, and check Enable Package Downloader";
                return false;
            }

            if(resetInfo)
            {
                _infoString.val = "";
            }

            return true;
        }

        protected override void CreateUI()
        {
            var title = CreateTextField(new JSONStorableString("title", "Check Scene Depencencies"));
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
            this.CreateButton(_findDependenciesAction);
            this.CreateButton(_downloadAction);
            CreateTextField(_infoString, true).height = 1200;
        }

        protected override void OnUIEnabled()
        {
            CheckDownloaderEnabled(true);
        }

        void FindDependencies()
        {
            _packages.Clear();
            if(!CheckDownloaderEnabled())
            {
                return;
            }

            FindDependencies(_metaJson, _searchSubDependenciesBool.val);
            var sb = new StringBuilder();
            sb.Append("Found packages (highlight missing):\n\n");
            foreach(var pair in _packages)
            {
                sb.AppendLine(pair.Value ? pair.Key : $"<b>{pair.Key}</b>");
            }

            _infoString.val = sb.ToString();
        }

        void FindDependencies(JSONClass json, bool recursive = false, int depth = 0)
        {
            try
            {
                var dependenciesJc = json["dependencies"].AsObject;
                if(dependenciesJc == null)
                {
                    if(depth == 0)
                    {
                        logBuilder.Error("Invalid meta.json.", false);
                    }

                    return;
                }

                foreach(string key in dependenciesJc.Keys)
                {
                    if(!_packages.ContainsKey(key))
                    {
                        _packages.Add(key, FileManagerSecure.PackageExists(key));
                    }

                    if(recursive)
                    {
                        FindDependencies(dependenciesJc[key].AsObject, true, depth + 1);
                    }
                }
            }
            catch(Exception e)
            {
                logBuilder.Exception(e);
            }
        }

        Coroutine _updateStatusCo;

        void DownloadMissing()
        {
            try
            {
                if(!CheckDownloaderEnabled())
                {
                    return;
                }

                string[] missingIds = _packages.Where(pair => !pair.Value).Select(pair => pair.Key).ToArray();
                if(missingIds.Length == 0)
                {
                    _infoString.val = "All missing packages already downloaded.";
                    return;
                }

                bool result = _downloader.DownloadPackages(
                    () => {},
                    e => logBuilder.Error($"DownloadAll Request failed with error:\n{e}", false),
                    missingIds
                );

                if(result)
                {
                    _updateStatusCo = StartCoroutine(UpdateStatusCo(missingIds));
                }
                else
                {
                    _infoString.val = "Failed to download missing packages.";
                }
            }
            catch(Exception e)
            {
                logBuilder.Exception(e);
            }
        }

        IEnumerator UpdateStatusCo(string[] missingIds)
        {
            float start = Time.unscaledTime;
            _infoString.val = "Downloading missing packages...\n";

            while(true)
            {
                yield return new WaitForSeconds(0.1f);

                bool allDownloaded = true;
                foreach(string id in missingIds)
                {
                    bool downloaded = FileManagerSecure.PackageExists(id);
                    if(downloaded)
                    {
                        _infoString.val += $"\nPackage {id} downloaded [{Time.unscaledTime - start:F1}s].";
                    }

                    allDownloaded = allDownloaded && downloaded;
                    _packages[id] = downloaded;
                }

                if(allDownloaded)
                {
                    _infoString.val += $"\n<b>All missing packages downloaded [{Time.unscaledTime - start:F1}s]</b>.";
                    break;
                }
            }
        }

        protected override void DoDestroy()
        {
            if(_updateStatusCo != null)
            {
                StopCoroutine(_updateStatusCo);
            }
        }
    }
}
