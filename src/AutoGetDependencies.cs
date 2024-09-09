using MVR.FileManagementSecure;
using System;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using MVR.Hub;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        readonly string _errorColor = ColorUtility.ToHtmlStringRGBA(new Color(0.75f, 0, 0));
        readonly string _updateNeededColor = ColorUtility.ToHtmlStringRGBA(new Color(0.44f, 0.44f, 0f));
        readonly string _okColor = ColorUtility.ToHtmlStringRGBA(new Color(0, 0.50f, 0));
        readonly string _subDependencyColor = ColorUtility.ToHtmlStringRGBA(new Color(0.4f, 0.4f, 0.4f));
        readonly Color _paleBlue = new Color(0.71f, 0.71f, 1.00f);
        readonly List<PackageObj> _packages = new List<PackageObj>();
        readonly List<PackageObj> _disabledPackages = new List<PackageObj>();
        readonly List<PackageObj> _versionErrorPackages = new List<PackageObj>();
        readonly List<PackageObj> _missingPackages = new List<PackageObj>();
        readonly List<PackageObj> _updateNeededPackages = new List<PackageObj>();
        readonly List<PackageObj> _installedPackages = new List<PackageObj>();
        readonly List<HubResourcePackageUI> _packageUIs = new List<HubResourcePackageUI>();
        readonly List<PackageObj> _notOnHubPackages = new List<PackageObj>();
        bool _initialized;
        Coroutine _downloadCo;
        Coroutine _handleUserConfirmPanelsCo;
        bool _pending;
        bool _finished;
        readonly StringBuilder _downloadErrorsSb = new StringBuilder();
        float _progress;

        JSONStorableBool _searchSubDependenciesBool;
        JSONStorableBool _alwaysCheckForUpdatesBool;
        JSONStorableAction _findDependenciesAction;
        JSONStorableString _infoString;
        JSONStorableBool _tempEnableHubBool;
        JSONStorableBool _autoAcceptPackagePluginsBool;
        JSONStorableAction _downloadAction;
        JSONStorableStringChooser _progressBarChooser;
        JSONStorableBool _logErrorsBool;
        JSONStorableStringChooser _notOnHubUITextChooser;
        JSONStorableStringChooser _disabledUITextChooser;

        JSONStorableFloat _progressFloat;
        JSONStorableAction _forceFinishAction;
        JSONStorableAction _copyErrorsToClipboardAction;
        JSONStorableAction _copyNotOnHubToClipboardAction;
        JSONStorableAction _copyDisabledToClipboardAction;
        // JSONStorableAction _navigateToPluginUIAction; // TODO
        // TODO special handling for include in VAM packages
        // TODO check VAM version latest

        Atom _progressUIAtom;
        Slider _progressSlider;
        readonly List<Atom> _uiSliders = new List<Atom>();
        Atom _notOnHubUIAtom;
        Text _notOnHubText;
        Atom _disabledUIAtom;
        Text _disabledText;
        readonly List<Atom> _uiTexts = new List<Atom>();
        readonly List<UIPopup> _popups = new List<UIPopup>();

        public override void InitUI()
        {
            base.InitUI();
            if(UITransform == null)
            {
                return;
            }

            UITransform.Find("Scroll View").GetComponent<UnityEngine.UI.Image>().color = new Color(0.85f, 0.85f, 0.85f);

            enabledJSON.setCallbackFunction = _ => { };
            enabledJSON.setJSONCallbackFunction = _ => { };
            if(enabledJSON.toggle != null)
            {
                enabledJSON.toggle.interactable = false;
            }

            _uiListener = UITransform.gameObject.AddComponent<UnityEventsListener>();
            _uiListener.enabledHandlers += UIEnabled;
            _uiListener.disabledHandlers += () => OnBlurPopup(null);
            _uiListener.clickHandlers += _ => OnBlurPopup(null);
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

            if(_pending)
            {
                UpdatePendingInfo();
            }
            else if(_finished)
            {
                UpdateFinishedInfo();
            }
        }

        void OnInitError(string error)
        {
            _logBuilder.Error(error);
            CreateTextField(new JSONStorableString("error", error)).backgroundColor = Color.clear;
            enabledJSON.valNoCallback = false;
        }

        public override void Init()
        {
            try
            {
                _logBuilder = new LogBuilder(nameof(AutoGetDependencies));
                if(containingAtom.type == "SessionPluginManager")
                {
                    OnInitError("Do not add as Session Plugin.");
                    return;
                }

                _metaJson = FindLoadedSceneMetaJson();
                if(_metaJson == null)
                {
                    OnInitError("Invalid scene (must be from package).");
                    return;
                }

                var coreControl = SuperController.singleton.GetAtomByUid("CoreControl");
                _hubBrowse = (HubBrowse) coreControl.GetStorableByID("HubBrowseController");
                if(_hubBrowse == null)
                {
                    OnInitError("HubBrowseController not found. Hub missing... ??");
                    return;
                }

                _searchSubDependenciesBool = new JSONStorableBool("Search sub-dependencies", false);
                RegisterBool(_searchSubDependenciesBool);

                _alwaysCheckForUpdatesBool = new JSONStorableBool("Always check for updates to '.latest'", false);
                RegisterBool(_alwaysCheckForUpdatesBool);

                _findDependenciesAction = new JSONStorableAction("Identify dependencies from meta.json", FindDependenciesCallback);
                RegisterAction(_findDependenciesAction);

                _infoString = new JSONStorableString("Info", "");

                _tempEnableHubBool = new JSONStorableBool("Temp auto-enable Hub if needed", false);
                RegisterBool(_tempEnableHubBool);

                _autoAcceptPackagePluginsBool = new JSONStorableBool("Auto-accept plugins from packages", false);
                RegisterBool(_autoAcceptPackagePluginsBool);

                _downloadAction = new JSONStorableAction("Download missing dependencies", DownloadMissingCallback);
                RegisterAction(_downloadAction);

                _progressBarChooser = new JSONStorableStringChooser("Progress Bar", new List<string>(), "", "Progress Bar");
                _progressBarChooser.setCallbackFunction = SelectProgressBarCallback;
                _progressBarChooser.representsAtomUid = true;
                RegisterStringChooser(_progressBarChooser);

                _logErrorsBool = new JSONStorableBool("Log errors", false);
                RegisterBool(_logErrorsBool);

                _notOnHubUITextChooser = new JSONStorableStringChooser("Not on Hub List", new List<string>(), "", "Not\u00A0on\u00A0Hub List");
                _notOnHubUITextChooser.setCallbackFunction = SelectNotOnHubUITextCallback;
                _notOnHubUITextChooser.representsAtomUid = true;
                RegisterStringChooser(_notOnHubUITextChooser);

                _disabledUITextChooser = new JSONStorableStringChooser("Disabled List", new List<string>(), "", "Disabled List");
                _disabledUITextChooser.setCallbackFunction = SelectDisabledUITextCallback;
                _disabledUITextChooser.representsAtomUid = true;
                RegisterStringChooser(_disabledUITextChooser);

                _progressFloat = new JSONStorableFloat("Download progress (%)", 0, 0, 100);

                _forceFinishAction = new JSONStorableAction("Force finish", ForceFinishCallback);
                RegisterAction(_forceFinishAction);

                _copyErrorsToClipboardAction = new JSONStorableAction("Copy errors to clipboard", CopyErrorsToClipboardCallback);
                RegisterAction(_copyErrorsToClipboardAction);

                _copyNotOnHubToClipboardAction = new JSONStorableAction("Copy 'Not on Hub' names to clipboard", () => CopyToClipboard(_notOnHubPackages));
                RegisterAction(_copyNotOnHubToClipboardAction);

                _copyDisabledToClipboardAction = new JSONStorableAction("Copy disabled names to clipboard", () => CopyToClipboard(_disabledPackages));
                RegisterAction(_copyDisabledToClipboardAction);

                _uiSliders.AddRange(SuperController.singleton.GetAtoms().Where(atom => atom.type == "UISlider"));
                _uiTexts.AddRange(SuperController.singleton.GetAtoms().Where(atom => atom.type == "UIText"));
                SuperController.singleton.onAtomAddedHandlers += OnAtomAdded;
                SuperController.singleton.onAtomRemovedHandlers += OnAtomRemoved;
                SuperController.singleton.onAtomUIDRenameHandlers += OnAtomRenamed;
                RebuildUISliderOptions();
                RebuildUITextOptions();

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
            return SuperController.singleton.LoadJSON(metaJsonPath)?.AsObject;
        }

        void CreateUI()
        {
            var layout = leftUIContent.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 5;

            CreateHeader("1. Identify Dependencies");
            CreateToggle(_searchSubDependenciesBool);
            CreateToggle(_alwaysCheckForUpdatesBool);
            {
                var uiDynamic = CreateButton(_findDependenciesAction.name);
                uiDynamic.height = 75;
                _findDependenciesAction.RegisterButton(uiDynamic);
            }

            CreateSpacer().height = 10;
            CreateTriggerMenuButton("On download pending...");
            // CreateTriggerMenuButton("On disabled packages found...");
            CreateTriggerMenuButton("On all dependencies installed...");

            CreateSpacer().height = 5;
            CreateHeader("2. Download Dependencies");
            CreateToggle(_tempEnableHubBool);
            CreateToggle(_autoAcceptPackagePluginsBool);
            {
                var uiDynamic = CreateButton(_downloadAction.name);
                uiDynamic.height = 75;
                _downloadAction.RegisterButton(uiDynamic);
            }

            CreateSpacer().height = 5;
            ConfigurePopup(CreateScrollablePopup(_progressBarChooser), 470);
            {
                var uiDynamic = CreateSlider(_progressFloat);
                uiDynamic.valueFormat = "F0";
                uiDynamic.HideButtons();
                uiDynamic.SetInteractable(false);
            }

            CreateSpacer().height = 10;
            CreateTriggerMenuButton("On download failed...");
            CreateToggle(_logErrorsBool);
            ConfigurePopup(CreateScrollablePopup(_notOnHubUITextChooser), 470, 0, true);
            ConfigurePopup(CreateScrollablePopup(_disabledUITextChooser), 470, 0, true);

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
                rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 20, 1210);
                rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 545, 650);
                var uiTextRectT = uiDynamic.UItext.GetComponent<RectTransform>();
                var uiTextPos = uiTextRectT.anchoredPosition;
                uiTextPos.y -= 10;
                uiTextRectT.anchoredPosition = uiTextPos;
                var uiTextSize = uiTextRectT.sizeDelta;
                uiTextSize.y -= 10;
                uiTextRectT.sizeDelta = uiTextSize;
                uiDynamic.UItext.horizontalOverflow = HorizontalWrapMode.Overflow;
                var scrollView = infoField.Find("Scroll View");
                var scrollRect = scrollView.GetComponent<ScrollRect>();
                scrollRect.horizontal = true;
                scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
                _infoString.dynamicText = uiDynamic;
            }
        }

        void CreateHeader(string text)
        {
            var uiDynamic = CreateTextField(new JSONStorableString(Guid.NewGuid().ToString().Substring(0, 4), text));
            var layoutElement = uiDynamic.GetComponent<LayoutElement>();
            layoutElement.minHeight = 55;
            layoutElement.preferredHeight = 55;
            uiDynamic.UItext.fontSize = 30;
            uiDynamic.UItext.fontStyle = FontStyle.Bold;
            uiDynamic.backgroundColor = Color.clear;
            var rectT = uiDynamic.UItext.GetComponent<RectTransform>();
            var pos = rectT.anchoredPosition;
            pos.y = -15;
            rectT.anchoredPosition = pos;
        }

        void CreateTriggerMenuButton(string text)
        {
            var uiDynamic = CreateButton(text);
            uiDynamic.buttonText.alignment = TextAnchor.MiddleLeft;
            var textRectT = uiDynamic.buttonText.GetComponent<RectTransform>();
            var pos = textRectT.anchoredPosition;
            pos.x += 15;
            textRectT.anchoredPosition = pos;
        }

        void ConfigurePopup(UIDynamicPopup uiDynamic, float height, float offsetX = 0, bool upwards = false)
        {
            var popup = uiDynamic.popup;
            popup.labelText.color = Color.black;
            popup.selectColor = _paleBlue;

            if(height > 0f)
            {
                uiDynamic.popupPanelHeight = height;
            }

            float offsetY = upwards ? height + 60 : 0;
            popup.popupPanel.offsetMin += new Vector2(offsetX, offsetY);
            popup.popupPanel.offsetMax += new Vector2(offsetX, offsetY);
            popup.onOpenPopupHandlers += () => OnBlurPopup(uiDynamic.popup);
            _popups.Add(popup);
        }

        void OnBlurPopup(UIPopup openedPopup)
        {
            for(int i = 0; i < _popups.Count; i++)
            {
                var popup = _popups[i];
                if(popup != openedPopup)
                {
                    popup.visible = false;
                }
            }
        }

        void FindDependenciesCallback()
        {
            if(_downloadCo != null)
            {
                StopCoroutine(_downloadCo);
            }

            _pending = false;
            _finished = false;
            _downloadErrorsSb.Clear();
            SetProgress(0);
            SuperController.singleton.RescanPackages();

            _packages.Clear();
            _disabledPackages.Clear();
            _versionErrorPackages.Clear();
            _missingPackages.Clear();
            _updateNeededPackages.Clear();
            _installedPackages.Clear();

            try
            {
                FindDependenciesRecursive(_metaJson, _searchSubDependenciesBool.val);
            }
            catch(Exception e)
            {
                _logBuilder.Exception("Finding pcakages failed", e);
            }

            try
            {
                var packagesDict = _packages.ToDictionary(obj => obj.name, obj => obj);
                GC.Collect();
                float startMemory = GC.GetTotalMemory(false) / (1024f * 1024f);
                IdentifyDisabledPackages(packagesDict);
                float endMemory = GC.GetTotalMemory(false) / (1024f * 1024f);
                _logBuilder.Message($"FindDisabledPackages increased heap size by {endMemory - startMemory:0.00} MB");
            }
            catch(Exception e)
            {
                _logBuilder.Exception("Identifying disabled packages failed", e);
                _disabledPackages.Clear();
            }

            // TODO for .latest packages, identify if the latest installed version is disabled?
            // - Mark as "soft disabled", i.e. optional for the user to enable
            // - Unknown before download if Hub has a newer version

            // populate lists
            {
                foreach(var obj in _packages)
                {
                    if(obj.versionError != null) _versionErrorPackages.Add(obj);
                    else if(obj.disabled) _disabledPackages.Add(obj);
                    else if(!obj.exists) _missingPackages.Add(obj);
                    else _installedPackages.Add(obj);
                }

                if(_alwaysCheckForUpdatesBool.val || _missingPackages.Count > 0)
                {
                    for(int i = _installedPackages.Count - 1; i >= 0; i--)
                    {
                        var obj = _installedPackages[i];
                        if(obj.requireLatest)
                        {
                            _updateNeededPackages.Add(obj);
                            _installedPackages.RemoveAt(i);
                        }
                    }
                }

                _updateNeededPackages.Reverse();
            }

            if(_versionErrorPackages.Count > 0)
            {
                if(!_uiListener.active)
                {
                    _logBuilder.Error("Version error in meta.json, see plugin UI");
                }
            }

            if(_disabledPackages.Count > 0)
            {
                Debug.Log("TODO trigger on disabled packages found"); // TODO
                if(_disabledText != null)
                {
                    _disabledText.text = _disabledPackages.Select(obj => obj.name).ToPrettyString();
                }
            }

            if(_missingPackages.Count > 0 || _updateNeededPackages.Count > 0)
            {
                if(_versionErrorPackages.Count == 0)
                {
                    Debug.Log("TODO trigger on dependencies found & pending download"); // TODO
                }

                _pending = true;
                UpdatePendingInfo();
            }
            else
            {
                if(_versionErrorPackages.Count == 0)
                {
                    Debug.Log("TODO trigger on success"); // TODO
                }

                _finished = true;
                UpdateFinishedInfo();
            }
        }

        void SetProgress(float value)
        {
            _progress = value;
            _progressFloat.val = value * 100;
            if(_progressSlider != null)
            {
                _progressSlider.normalizedValue = value;
            }
        }

        void FindDependenciesRecursive(JSONClass json, bool recursive = false, int depth = 0)
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
                    _packages.Add(new PackageObj(trimmed, parts, depth > 0));
                }

                if(recursive)
                {
                    FindDependenciesRecursive(dependenciesJc[key].AsObject, true, depth + 1);
                }
            }
        }

        static void IdentifyDisabledPackages(Dictionary<string, PackageObj> packagesDict)
        {
            var queue = new Queue<string>();
            queue.Enqueue("AddonPackages");

            var disabledPackages = new HashSet<string>();
            var packageFileMap = new Dictionary<string, List<string>>();
            var regex = new Regex(@"\.(\d+)\.var$", RegexOptions.Compiled);

            while(queue.Count > 0)
            {
                string dir = queue.Dequeue();
                string[] subDirs = FileManagerSecure.GetDirectories(dir);
                for(int i = 0; i < subDirs.Length; i++)
                {
                    queue.Enqueue(subDirs[i]);
                }

                string[] files = FileManagerSecure.GetFiles(dir);
                for(int i = 0; i < files.Length; i++)
                {
                    string file = files[i];
                    if(file.EndsWith(".disabled"))
                    {
                        // Extract the package name (e.g., "Author.Package.13.var" from "Author.Package.13.var.disabled")
                        string packageName = file.Substring(dir.Length + 1, file.Length - dir.Length - 1 - 9);
                        if(packageName.EndsWith(".var"))
                        {
                            string basePackageName = packageName.Substring(0, packageName.Length - 4);
                            PackageObj packageObj;
                            if(packagesDict.TryGetValue(basePackageName, out packageObj))
                            {
                                packageObj.disabled = true;
                            }

                            disabledPackages.Add(basePackageName);
                        }
                    }
                    else if(regex.IsMatch(file))
                    {
                        // Extract the group name (e.g., "Author.Package" from "Author.Package.13.var")
                        int secondToLastIndex = file.LastIndexOf('.', file.Length - 5);
                        string groupName = file.Substring(dir.Length + 1, secondToLastIndex - dir.Length - 1);
                        if(!packageFileMap.ContainsKey(groupName))
                        {
                            packageFileMap[groupName] = new List<string>();
                        }
                        packageFileMap[groupName].Add(file);
                    }
                }
            }

            List<string> vammoan;
            packageFileMap.TryGetValue("hazmhox.vammoan", out vammoan);
            Debug.Log(vammoan == null ? "vammoan not found" : $"vammoan found: {vammoan.ToPrettyString()}");

            // After processing all directories and files, check the 'latest' packages
            foreach(var pair in packagesDict)
            {
                if(!pair.Key.EndsWith(".latest"))
                {
                    continue;
                }

                // Get the base package group name (e.g., "Author.Package")
                string groupName = pair.Key.Substring(0, pair.Key.Length - 7);
                if(packageFileMap.ContainsKey(groupName))
                {
                    bool allDisabled = true;
                    var group = packageFileMap[groupName];
                    for(int i = 0; i < group.Count; i++)
                    {
                        string file = group[i];
                        int idx = file.LastIndexOf('\\');
                        string specificPackageName = file.Substring(idx + 1, file.Length - idx - 5); // Extract "Author.Package.13"
                        if(!disabledPackages.Contains(specificPackageName))
                        {
                            allDisabled = false;
                            break;
                        }
                    }

                    if(allDisabled)
                    {
                        pair.Value.disabled = true;
                    }
                }
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

            if(_versionErrorPackages.Count > 0)
            {
                AppendPackagesInfo(sb, "Version error in meta.json", _errorColor, _versionErrorPackages);
            }
            if(_disabledPackages.Count > 0)
            {
                AppendPackagesInfo(sb, "Disabled", _errorColor, _disabledPackages);
            }
            AppendPackagesInfo(sb, "Missing, download needed", _errorColor, _missingPackages);
            AppendPackagesInfo(sb, "Installed, check for update needed", _updateNeededColor, _updateNeededPackages);
            AppendPackagesInfo(sb, "Installed, no update needed", _okColor, _installedPackages);

            SetJssText(_infoString, sb);
        }

        void AppendPackagesInfo(StringBuilder sb, string title, string titleColor, List<PackageObj> packages)
        {
            sb.AppendFormat("<size=30><color=#{0}><b>{1}:</b></color></size>\n\n", titleColor, title);
            if(packages.Count == 0)
            {
                sb.Append("None.\n\n");
                return;
            }

            foreach (var obj in packages)
            {
                if(obj.isSubDependency)
                {
                    sb.AppendFormat("<color=#{0}>-\u00A0{1}</color>\n", _subDependencyColor, obj.name);
                }
                else
                {
                    sb.AppendFormat("-\u00A0{0}\n", obj.name);
                }
            }
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

            if(_packages.TrueForAll(obj => obj.existsAndIsValid))
            {
                sb.AppendFormat("<size=30><color=#{0}><b>All dependencies are installed!</b></color></size>\n\n", _okColor);
            }
            else
            {
                if(_versionErrorPackages.Count > 0)
                {
                    AppendPackagesInfo(sb, "Version error in meta.json", _errorColor, _versionErrorPackages);
                }
                if(_disabledPackages.Count > 0)
                {
                    AppendPackagesInfo(sb, "Disabled", _errorColor, _disabledPackages);
                }
                if(_notOnHubPackages.Count > 0)
                {
                    AppendPackagesInfo(sb, "Packages not on Hub", _errorColor, _notOnHubPackages);
                }
                if(_downloadErrorsSb != null)
                {
                    sb.AppendFormat("<size=30><color=#{0}><b>Errors during download:</b></color></size>\n\n", _errorColor);
                    sb.Append(_downloadErrorsSb);
                    sb.Append("\n\n");
                }
                AppendPackagesInfo(sb, "Installed", _okColor, _packages.Where(obj => obj.existsAndIsValid).ToList());
            }

            SetJssText(_infoString, sb);
        }

        void SetJssText(JSONStorableString jss, StringBuilder sb)
        {
            try
            {
                // TODO test
                if(sb.Length > 16000)
                {
                    const string truncated = "\n\n(too long, truncated)";
                    sb.Length = 16000 - truncated.Length;
                    sb.Append(truncated);
                }
                jss.val = sb.ToString();
            }
            catch(Exception e)
            {
                _logBuilder.Exception(e);
            }
        }

        void DownloadMissingCallback()
        {
            if(!_pending)
            {
                if(_logErrorsBool.val) _logBuilder.Error("Download is not pending");
                return;
            }

            _pending = false;
            _downloadCo = StartCoroutine(DownloadMissingViaHubCo());
        }


        void OnAtomAdded(Atom atom)
        {
            if(atom.type == "UISlider" && !_uiSliders.Contains(atom))
            {
                _uiSliders.Add(atom);
                RebuildUISliderOptions();
            }
            else if(atom.type == "UIText" && !_uiTexts.Contains(atom))
            {
                _uiTexts.Add(atom);
                RebuildUITextOptions();
            }
        }

        void OnAtomRemoved(Atom atom)
        {
            if(_uiSliders.Contains(atom))
            {
                _uiSliders.Remove(atom);
                RebuildUISliderOptions();

                if(_progressUIAtom == atom)
                {
                    _progressBarChooser.val = "";
                }
            }
            else if(_uiTexts.Contains(atom))
            {
                _uiTexts.Remove(atom);
                RebuildUITextOptions();

                if(_notOnHubUIAtom == atom)
                {
                    _notOnHubUITextChooser.val = "";
                }

                if(_disabledUIAtom == atom)
                {
                    _disabledUITextChooser.val = "";
                }
            }
        }

        void OnAtomRenamed(string oldUid, string newUid)
        {
            RebuildUISliderOptions();
            RebuildUITextOptions();
            if(_progressBarChooser.val == oldUid)
            {
                _progressBarChooser.valNoCallback = newUid;
            }
            if(_notOnHubUITextChooser.val == oldUid)
            {
                _notOnHubUITextChooser.valNoCallback = newUid;
            }
            if(_disabledUITextChooser.val == oldUid)
            {
                _disabledUITextChooser.valNoCallback = newUid;
            }
        }

        void RebuildUISliderOptions()
        {
            var options = new List<string> { "" };
            var displayOptions = new List<string> { "None" };
            options.AddRange(_uiSliders.Select(atom => atom.uid));
            displayOptions.AddRange(_uiSliders.Select(atom => atom.uid));
            _progressBarChooser.choices = options;
            _progressBarChooser.displayChoices = displayOptions;
        }

        void RebuildUITextOptions()
        {
            var options = new List<string> { "" };
            var displayOptions = new List<string> { "None" };
            options.AddRange(_uiTexts.Select(atom => atom.uid));
            displayOptions.AddRange(_uiTexts.Select(atom => atom.uid));
            _notOnHubUITextChooser.choices = options;
            _notOnHubUITextChooser.displayChoices = displayOptions;
            _disabledUITextChooser.choices = options;
            _disabledUITextChooser.displayChoices = displayOptions;
        }

        float _tmpAlpha;

        void SelectProgressBarCallback(string option)
        {
            try
            {
                _progressUIAtom = null;
                if(_progressSlider != null)
                {
                    RestoreSlider();
                    _progressSlider = null;
                }

                if(option != "")
                {
                    string prevOption = _progressUIAtom != null ? _progressUIAtom.uid : "";
                    var uiSlider = _uiSliders.Find(atom => atom.uid == option);
                    if(uiSlider == null)
                    {
                        _logBuilder.Error($"UISlider '{option}' not found");
                        _progressBarChooser.valNoCallback = prevOption;
                        return;
                    }

                    var sliderT = uiSlider.reParentObject.transform.Find("object/rescaleObject/Canvas/Holder/Slider");
                    _progressSlider = sliderT.GetComponent<Slider>();
                    _progressSlider.interactable = false;
                    _progressSlider.normalizedValue = _progress;
                    var sliderHandleImg = sliderT.Find("Handle Slide Area/Handle").GetComponent<UnityEngine.UI.Image>();
                    var color  = sliderHandleImg.color;
                    _tmpAlpha = color.a;
                    color.a = 0;
                    sliderHandleImg.color = color;
                    _progressUIAtom = uiSlider;
                }
            }
            catch(Exception e)
            {
                _logBuilder.Exception(e);
            }
        }

        void RestoreSlider()
        {
            _progressSlider.interactable = true;
            var sliderHandleImg = _progressSlider.transform.Find("Handle Slide Area/Handle").GetComponent<UnityEngine.UI.Image>();
            var color = sliderHandleImg.color;
            color.a = _tmpAlpha;
            sliderHandleImg.color = color;
        }

        void SelectNotOnHubUITextCallback(string option)
        {
            try
            {
                _notOnHubUIAtom = null;
                _notOnHubText = null;

                if(option != "")
                {
                    string prevOption = _notOnHubUIAtom != null ? _notOnHubUIAtom.uid : "";
                    var uiText = _uiTexts.Find(atom => atom.uid == option);
                    if(uiText == null)
                    {
                        _logBuilder.Error($"UIText '{option}' not found");
                        _notOnHubUITextChooser.valNoCallback = prevOption;
                        return;
                    }

                    var holderT = uiText.reParentObject.transform.Find("object/rescaleObject/Canvas/Holder");
                    _notOnHubText = holderT.Find("Text").GetComponent<Text>();
                    _notOnHubUIAtom = uiText;
                }
            }
            catch(Exception e)
            {
                _logBuilder.Exception(e);
            }
        }

        void SelectDisabledUITextCallback(string option)
        {
            try
            {
                _disabledUIAtom = null;
                _disabledText = null;

                if(option != "")
                {
                    string prevOption = _disabledUIAtom != null ? _disabledUIAtom.uid : "";
                    var uiText = _uiTexts.Find(atom => atom.uid == option);
                    if(uiText == null)
                    {
                        _logBuilder.Error($"UIText '{option}' not found");
                        _disabledUITextChooser.valNoCallback = prevOption;
                        return;
                    }

                    var holderT = uiText.reParentObject.transform.Find("object/rescaleObject/Canvas/Holder");
                    _disabledText = holderT.Find("Text").GetComponent<Text>();
                    _disabledUIAtom = uiText;
                }
            }
            catch(Exception e)
            {
                _logBuilder.Exception(e);
            }
        }

        // TODO test
        void ForceFinishCallback()
        {
            if(_downloadCo != null)
            {
                StopCoroutine(_downloadCo);
                Teardown();
            }
        }

        // TODO test
        void CopyErrorsToClipboardCallback()
        {
            var sb = new StringBuilder();
            if(_versionErrorPackages.Count > 0)
            {
                sb.Append("Version errors in meta.json:\n\n");
                foreach(var obj in _versionErrorPackages)
                {
                    sb.AppendFormat("{0}: {1}\n", obj.name, obj.versionError);
                }
                sb.Append("\n");
            }

            sb.Append(_downloadErrorsSb);
            if(sb.Length > 0)
            {
                GUIUtility.systemCopyBuffer = sb.ToString();
            }
        }

        // TODO test
        static void CopyToClipboard(List<PackageObj> packages)
        {
            if(packages.Count > 0)
            {
                GUIUtility.systemCopyBuffer = packages.Select(obj => obj.name).ToPrettyString();
            }
        }

        void OnError(string message, bool teardown = true)
        {
            if (_logErrorsBool.val) _logBuilder.Error(message);
            _downloadErrorsSb.AppendLine(message);
            if(teardown)
            {
                Teardown();
            }
        }

        void OnException(string message, Exception e, bool teardown = true)
        {
            if (_logErrorsBool.val) _logBuilder.Exception(message, e);
            _downloadErrorsSb.AppendFormat("{0}: {1}\n", message, e.Message);
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
            _notOnHubPackages.Clear();
            _panelRelocated = false;
            _downloadErrorsSb.Clear();

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
            // Find package UIs
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
            }
            catch(Exception e)
            {
                OnException("Find package UIs", e);
                yield break;
            }

            // Match missing packages to correct hub pacakge UIs
            var packagesToDownload = _missingPackages.Concat(_updateNeededPackages).ToList();
            try
            {
                foreach(var obj in packagesToDownload)
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
                OnException("Match to package UI", e);
                yield break;
            }

            var pendingPackages = new List<PackageObj>();

            // detect errors from RegisterHubItem, split into pending and not on Hub packages
            try
            {
                foreach(var obj in packagesToDownload)
                {
                    if(obj.hubItemError != null)
                    {
                        string error = $"'{obj.name}' error: {obj.hubItemError}";
                        if(_logErrorsBool.val) _logBuilder.Error(error);
                        _downloadErrorsSb.AppendLine(error);
                    }

                    if(obj.connectedItem == null)
                    {
                        continue;
                    }

                    if(!obj.connectedItem.NeedsDownload)
                    {
                        _logBuilder.Debug($"{obj.name} item NeedsDownload=False");
                        continue;
                    }

                    if(obj.connectedItem.CanBeDownloaded)
                    {
                        pendingPackages.Add(obj);
                    }
                    else
                    {
                        _notOnHubPackages.Add(obj);
                    }
                }
            }
            catch(Exception e)
            {
                OnException("Process packages to download", e);
                yield break;
            }

#endregion PreDownload

#region Download
            int count = pendingPackages.Count;
            if(count <= 0) // probably because only checking for updates and none found
            {
                Teardown();
                yield break;
            }

            // setup callbacks and start downloads
            try
            {
                foreach(var obj in pendingPackages)
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
                OnException("Start downloads", e);
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
                    for(int i = pendingPackages.Count - 1; i >= 0; i--)
                    {
                        var obj = pendingPackages[i];
                        if(obj.downloadComplete)
                        {
                            sum += 1;
                            continue;
                        }

                        if(obj.downloadError != null)
                        {
                            if(_logErrorsBool.val) _logBuilder.Error($"'{obj.name}' error: {obj.downloadError}");
                            _downloadErrorsSb.AppendFormat("'{0}' error: {1}\n", obj.name, obj.downloadError);
                            pendingPackages.RemoveAt(i);
                            continue;
                        }

                        if(obj.downloadStarted)
                        {
                            var slider = obj.packageUI.progressSlider;
                            sum += Mathf.InverseLerp(slider.minValue, slider.maxValue, slider.value);
                        }

                        allDone = false;
                    }

                    SetProgress(sum / count);
                    if(allDone)
                    {
                        break;
                    }
                }
                catch(Exception e)
                {
                    OnException("Downloading", e, false);
                    break;
                }

                yield return wait;
            }
#endregion Download

            _handleUserConfirmPanelsCo = StartCoroutine(WaitForUserConfirmPanels());
            if(_downloadErrorsSb == null)
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
            // cleanup
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
            }

            foreach(var obj in _packages)
            {
                obj.SyncExists();
            }

            // success path; suppresses error triggers and not on Hub packages triggers if all packages happen to somehow exist regardless
            if(_packages.TrueForAll(obj => obj.existsAndIsValid))
            {
                Debug.Log("TODO trigger on success"); // TODO
            }
            // failure path
            else
            {
                if(_notOnHubPackages.Count > 0)
                {
                    Debug.Log("TODO trigger on not on Hub packages found"); // TODO
                    if(_notOnHubText != null)
                    {
                        _notOnHubText.text = _notOnHubPackages.Select(obj => obj.name).ToPrettyString();
                    }
                }

                Debug.Log("TODO trigger on failure"); // TODO
            }

            _downloadCo = null;
            _finished = true;
            UpdateFinishedInfo();
        }
#endregion Teardown

        // TODO test
        public override void RestoreFromJSON(
            JSONClass jc,
            bool restorePhysical = true,
            bool restoreAppearance = true,
            JSONArray presetAtoms = null,
            bool setMissingToDefault = true
        )
        {
            FixRestoreFromSubscene(jc);
            base.RestoreFromJSON(jc, restorePhysical, restoreAppearance, presetAtoms, setMissingToDefault);
            subScenePrefix = null;
        }

        /* Ensure loading a SubScene file sets the correct value to JSONStorableStringChooser. */
        // TODO test just always setting subscenePrefix?
        void FixRestoreFromSubscene(JSONClass jc)
        {
            var subScene = containingAtom.containingSubScene;
            if(subScene != null)
            {
                bool targetAtomInAnotherSubscene = false;
                if(jc.HasKey(_progressBarChooser.name))
                {
                    var atom = SuperController.singleton.GetAtomByUid(jc[_progressBarChooser.name].Value);
                    if(atom == null || atom.containingSubScene != subScene)
                    {
                        targetAtomInAnotherSubscene = true;
                    }
                }

                if(jc.HasKey(_notOnHubUITextChooser.name))
                {
                    var atom = SuperController.singleton.GetAtomByUid(jc[_notOnHubUITextChooser.name].Value);
                    if(atom == null || atom.containingSubScene != subScene)
                    {
                        targetAtomInAnotherSubscene = true;
                    }
                }

                if(targetAtomInAnotherSubscene)
                {
                    subScenePrefix = containingAtom.uid.Replace(containingAtom.uidWithoutSubScenePath, "");
                }
            }
        }

        void OnDestroy()
        {
            if(_progressSlider != null)
            {
                RestoreSlider();
            }

            if(_uiListener != null)
            {
                DestroyImmediate(_uiListener);
            }

            SuperController.singleton.onAtomAddedHandlers -= OnAtomAdded;
            SuperController.singleton.onAtomRemovedHandlers -= OnAtomRemoved;
            SuperController.singleton.onAtomUIDRenameHandlers -= OnAtomRenamed;

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
