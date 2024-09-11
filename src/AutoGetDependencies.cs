using MacGruber;
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
using UnityEngine.Events;
using UnityEngine.UI;

namespace everlaster
{
    sealed class AutoGetDependencies : MVRScript
    {
        UnityEventsListener _uiListener;
        Transform _scrollView;
        bool _uiCreated;
        public LogBuilder logBuilder { get; private set; }
        Bindings _bindings;
        string _loadedScene;
        JSONClass _metaJson;
        HubBrowse _hubBrowse;
        readonly static string _errorColor = ColorUtility.ToHtmlStringRGBA(new Color(0.75f, 0, 0));
        readonly static string _updateRequiredColor = ColorUtility.ToHtmlStringRGBA(new Color(0.44f, 0.44f, 0f));
        readonly static string _okColor = ColorUtility.ToHtmlStringRGBA(new Color(0, 0.50f, 0));
        readonly static string _subDependencyColor = ColorUtility.ToHtmlStringRGBA(Color.gray);
        const string FIND_DEPENDENCIES_COLOR = "#daf1ee";
        const string DOWNLOAD_DEPENDENCIES_COLOR = "#ebdaf1";
        readonly static Color _paleBlue = new Color(0.71f, 0.71f, 1.00f);
        readonly List<PackageObj> _packages = new List<PackageObj>();
        readonly List<PackageObj> _disabledPackages = new List<PackageObj>();
        readonly List<PackageObj> _versionErrorPackages = new List<PackageObj>();
        readonly List<string> _missingVamBundledPackageNames = new List<string>();
        readonly List<PackageObj> _missingPackages = new List<PackageObj>();
        readonly List<PackageObj> _updateRequiredPackages = new List<PackageObj>();
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
        JSONStorableBool _identifyDisabledPackagesBool;
        JSONStorableAction _identifyDependenciesAction;
        JSONStorableUrl _selectPackageUrl;
        JSONStorableString _usageString;
        JSONStorableString _pathString;
        JSONStorableString _infoString;
        UIDynamicButton _usageButton;
        UIDynamicButton _backButton;
        UIDynamicTextField _usageField;
        UIDynamicTextField _infoField;
        JSONStorableBool _tempEnableHubBool;
        JSONStorableBool _autoAcceptPackagePluginsBool;
        JSONStorableAction _downloadAction;
        JSONStorableStringChooser _progressBarChooser;
        public JSONStorableBool logErrorsBool { get; private set; }

        JSONStorableFloat _progressFloat;
        JSONStorableAction _forceFinishAction;
        JSONStorableAction _copyErrorsToClipboardAction;
        JSONStorableAction _copyNotOnHubToClipboardAction;
        JSONStorableAction _copyDisabledToClipboardAction;
        JSONStorableAction _navigateToPluginUIAction;

        readonly Dictionary<string, TriggerWrapper> _triggers = new Dictionary<string, TriggerWrapper>();
        TriggerWrapper _ifDownloadPendingTrigger;
        TriggerWrapper _ifDisabledPackagesDetectedTrigger;
        TriggerWrapper _ifAllDependenciesInstalledTrigger;
        TriggerWrapper _ifVamBundledPackagesMissingTrigger;
        TriggerWrapper _ifVamNotLatestTrigger;
        TriggerWrapper _ifSomePackagesNotInstalledTrigger;
        TriggerWrapper _ifNotOnHubPackagesDetectedTrigger;

        bool _isLatestVam;
        Atom _progressUIAtom;
        Slider _progressSlider;
        readonly List<Atom> _uiSliders = new List<Atom>();
        readonly List<Atom> _uiTexts = new List<Atom>();
        readonly List<UIPopup> _popups = new List<UIPopup>();

        public override void InitUI()
        {
            base.InitUI();
            if(UITransform == null)
            {
                return;
            }

            _scrollView = UITransform.Find("Scroll View");
            _scrollView.GetComponent<UnityEngine.UI.Image>().color = new Color(0.85f, 0.85f, 0.85f);

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
            logBuilder.Error(error);
            CreateTextField(new JSONStorableString("error", error)).backgroundColor = Color.clear;
            enabledJSON.valNoCallback = false;
        }

        public override void Init()
        {
            try
            {
                logBuilder = new LogBuilder(nameof(AutoGetDependencies));
                var coreControl = SuperController.singleton.GetAtomByUid("CoreControl");
                _hubBrowse = (HubBrowse) coreControl.GetStorableByID("HubBrowseController");
                if(_hubBrowse == null)
                {
                    OnInitError("HubBrowseController not found. Hub missing... ??");
                    return;
                }

                #if VAM_GT_1_22
                _isLatestVam = true;
                #else
                _isLatestVam = false;
                #endif

                _loadedScene = SuperController.singleton.LoadedSceneName;
                _metaJson = FindLoadedSceneMetaJson();

                _searchSubDependenciesBool = new JSONStorableBool("Search sub-dependencies", false, (bool _) => RefindDependencies());
                RegisterBool(_searchSubDependenciesBool);

                _alwaysCheckForUpdatesBool = new JSONStorableBool("Always check for updates to '.latest'", false, (bool _) => RefindDependencies());
                RegisterBool(_alwaysCheckForUpdatesBool);

                _identifyDisabledPackagesBool = new JSONStorableBool("Identify disabled packages", true, (bool _) => RefindDependencies());
                RegisterBool(_identifyDisabledPackagesBool);

                if(_metaJson == null)
                {
                    _selectPackageUrl = new JSONStorableUrl("Identify dependencies from meta.json", "", "json", "AddonPackages")
                    {
                        allowFullComputerBrowse = false,
                        allowBrowseAboveSuggestedPath = false,
                        showDirs = true,
                        beginBrowseWithObjectCallback = BeginLoadBrowse,
                        endBrowseWithObjectCallback = OnMetaJsonSelected,
                    };
                }
                else
                {
                    _identifyDependenciesAction = new JSONStorableAction("Identify dependencies from meta.json", () => FindDependenciesCallback());
                    RegisterAction(_identifyDependenciesAction);
                }

                _usageString = new JSONStorableString("Usage", "");
                _pathString = new JSONStorableString("Path", "");
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

                logErrorsBool = new JSONStorableBool("Log errors", false);
                RegisterBool(logErrorsBool);

                _progressFloat = new JSONStorableFloat("Download progress (%)", 0, 0, 100);

                _forceFinishAction = new JSONStorableAction("Force finish", ForceFinishCallback);
                RegisterAction(_forceFinishAction);

                _copyErrorsToClipboardAction = new JSONStorableAction("Copy errors to clipboard", CopyErrorsToClipboardCallback);
                RegisterAction(_copyErrorsToClipboardAction);

                _copyNotOnHubToClipboardAction = new JSONStorableAction("Copy 'Not on Hub' names to clipboard", () => CopyToClipboard(_notOnHubPackages));
                RegisterAction(_copyNotOnHubToClipboardAction);

                _copyDisabledToClipboardAction = new JSONStorableAction("Copy disabled names to clipboard", () => CopyToClipboard(_disabledPackages));
                RegisterAction(_copyDisabledToClipboardAction);

                _navigateToPluginUIAction = new JSONStorableAction("Open UI", () => this.SelectPluginUI());
                RegisterAction(_navigateToPluginUIAction);

                SimpleTriggerHandler.LoadAssets();
                _ifDownloadPendingTrigger = AddTrigger("If Download Pending", "If download pending...");
                _ifDisabledPackagesDetectedTrigger = AddTrigger("If Disabled Packages Detected", "If disabled packages detected...");
                _ifAllDependenciesInstalledTrigger = AddTrigger("If All Dependencies Installed", "If all dependencies installed...");
                _ifVamBundledPackagesMissingTrigger = AddTrigger("If VaM Bundled Packages Missing", "If VaM bundled packages missing...");
                _ifVamNotLatestTrigger = AddTrigger("If VaM Not Latest", "If VaM not latest (>= v1.22)...", false);
                _ifSomePackagesNotInstalledTrigger = AddTrigger("If Some Packages Not Installed", "If some packages not installed...");
                _ifNotOnHubPackagesDetectedTrigger = AddTrigger("If 'Not On Hub' Packages Detected", "If 'not on Hub' packages detected...");

                _uiSliders.AddRange(SuperController.singleton.GetAtoms().Where(atom => atom.type == "UISlider"));
                _uiTexts.AddRange(SuperController.singleton.GetAtoms().Where(atom => atom.type == "UIText"));
                SuperController.singleton.onAtomAddedHandlers += OnAtomAdded;
                SuperController.singleton.onAtomRemovedHandlers += OnAtomRemoved;
                SuperController.singleton.onAtomUIDRenameHandlers += OnAtomRenamed;
                RebuildUISliderOptions();
                RebuildUITextOptions();

                SuperController.singleton.BroadcastMessage("OnActionsProviderAvailable", this, SendMessageOptions.DontRequireReceiver);
                RegisterBindings();

                _initialized = true;
            }
            catch(Exception e)
            {
                logBuilder.Exception(e);
            }
        }

        void RefindDependencies()
        {
            if(_metaJson == null)
            {
                return;
            }

            FindDependenciesCallback(false);
        }

        JSONClass FindLoadedSceneMetaJson()
        {
            if(_loadedScene == null || !_loadedScene.Contains(":/"))
            {
                return null;
            }

            string metaJsonPath = _loadedScene.Split(':')[0] + ":/meta.json";
            _pathString.val = metaJsonPath;
            return SuperController.singleton.LoadJSON(metaJsonPath)?.AsObject;
        }

        void CreateUI()
        {
            _scrollView.GetComponent<ScrollRect>().vertical = false;
            var layout = leftUIContent.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 0.4f;

            CreateHeader("1. Identify Dependencies");
            CreateToggle(_searchSubDependenciesBool);
            CreateToggle(_alwaysCheckForUpdatesBool);
            CreateToggle(_identifyDisabledPackagesBool);

            if(_metaJson == null)
            {
                var uiDynamic = CreateButton(_selectPackageUrl.name);
                Color color;
                if(ColorUtility.TryParseHtmlString(FIND_DEPENDENCIES_COLOR, out color)) uiDynamic.buttonColor = color;
                uiDynamic.height = 80;
                uiDynamic.AddListener(() => _selectPackageUrl.FileBrowse());
            }
            else
            {
                var uiDynamic = CreateButton(_identifyDependenciesAction.name);
                Color color;
                if(ColorUtility.TryParseHtmlString(FIND_DEPENDENCIES_COLOR, out color)) uiDynamic.buttonColor = color;
                uiDynamic.height = 80;
                _identifyDependenciesAction.RegisterButton(uiDynamic);
            }


            CreateSpacer().height = 12;
            CreateTriggerMenuButton(_ifDownloadPendingTrigger);
            CreateTriggerMenuButton(_ifDisabledPackagesDetectedTrigger);
            CreateTriggerMenuButton(_ifAllDependenciesInstalledTrigger);
            CreateTriggerMenuButton(_ifVamBundledPackagesMissingTrigger);
            CreateTriggerMenuButton(_ifVamNotLatestTrigger);

            CreateSpacer().height = 6;
            CreateHeader("2. Download Dependencies");
            CreateToggle(_tempEnableHubBool);
            CreateToggle(_autoAcceptPackagePluginsBool);
            {
                var uiDynamic = CreateButton(_downloadAction.name);
                Color color;
                if(ColorUtility.TryParseHtmlString(DOWNLOAD_DEPENDENCIES_COLOR, out color)) uiDynamic.buttonColor = color;
                uiDynamic.height = 80;
                _downloadAction.RegisterButton(uiDynamic);
            }

            CreateSpacer().height = 12;
            ConfigurePopup(CreateScrollablePopup(_progressBarChooser), 470);
            {
                var uiDynamic = CreateSlider(_progressFloat);
                uiDynamic.valueFormat = "F0";
                uiDynamic.HideButtons();
                uiDynamic.SetInteractable(false);
            }

            CreateSpacer().height = 12;
            CreateTriggerMenuButton(_ifSomePackagesNotInstalledTrigger);
            CreateTriggerMenuButton(_ifNotOnHubPackagesDetectedTrigger);
            CreateSpacer().height = 12;
            CreateToggle(logErrorsBool);

            _usageButton = CreateTextToggleButton("Usage", ShowUsage);
            _usageButton.gameObject.SetActive(false);
            _backButton = CreateTextToggleButton("< Back", ShowInfo);
            _backButton.gameObject.SetActive(false);

            {
                const string usage = "AutoGetDependencies works in two stages:" +
                    "\n" +
                    "\n(1) Identify dependencies from the meta.json file." +
                    "\n(2) Download any missing dependencies." +
                    "\n" +
                    "\nUse the toggles configure the behavior of these two stages, set up triggers for different end conditions," +
                    " and then execute/trigger the identification and download actions in your scene logic (on scene load, via UIButton etc.)." +
                    "\n" +
                    "\n<size=32>1. Identify Dependencies</size>" +
                    "\n¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨" +
                    "\n<b>Search sub-dependencies</b>\nIdentify missing packages that are not direct dependencies of the scene's package." +
                    "\n" +
                    "\n<b>Always check for updates to '.latest'</b>\nFind dependencies with the latest version requirement and put them pending download," +
                    " even if some version of each package is already installed." +
                    "\n" +
                    "\n<b>Identify disabled packages</b>\nIdentify dependencies that are disabled by scanning the AddonPackages folder." +
                    "\n" +
                    "\nIn the resulting dependency list, sub-dependencies are greyed out (not strictly required for the scene)." +
                    "\n" +
                    "\n<size=28>Custom Triggers</size>" +
                    "\n¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨" +
                    "\nConfigure actions to execute when the corresponding condition is met." +
                    "\n" +
                    "\n<b>If download pending...</b> executes if there are missing packages or if updates are required." +
                    "\n" +
                    "\n<b>If disabled packages detected...</b> executes if any dependencies are disabled. Disabled packages must be manually enabled by the user." +
                    "\n" +
                    "\n<b>If all dependencies installed...</b> executes either if all dependencies are already installed to begin with, or if all dependencies" +
                    " are installed after downloading." +
                    "\n" +
                    "\n<b>If VaM bundled packages missing...</b> executes if any dependencies included in VAM are not found." +
                    " These need to be manually downloaded by running VaM_Updater.exe." +
                    "\n" +
                    "\n<b>If VaM not latest (>= v1.22)...</b> - inform users that they should update VaM if it's required by your scene" +
                    " (e.g. you're using VaM's built in lip sync)." +
                    "\n" +
                    "\nCustom triggers can additionally send information to a UIText in the scene (see Hub resource for a demo)." +
                    "\n" +
                    "\n<size=32>2. Download Dependencies</size>" +
                    "\n¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨" +
                    "\n<b>Temp auto-enable Hub if needed</b>\nTemporarily enable Hub for users who have it disabled." +
                    " If unchecked, VAM prompts the user to enable Hub before the download can continue." +
                    "\n" +
                    "\n<b>Auto-accept plugins from packages</b>\nAutomatically accept loading of scripts from packages which contain scripts." +
                    " If unchecked, VAM prompts the user to accept each package at the end of the download process." +
                    "\n" +
                    "\n<b>Progress Bar</b>\nSelect a UISlider in the scene to display download progress." +
                    "\n" +
                    "\n<size=28>Custom Triggers</size>" +
                    "\n¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨" +
                    "\n<b>'If some packages not installed...'</b> executes when there are still uninstalled/disabled packages after download is complete." +
                    "\n" +
                    "\n<b>'If 'Not on Hub' packages detected...'</b> executes if any missing dependencies couldn't be downloaded because they were not found on the Hub." +
                    "\n";

                _usageString.val = usage;
                _usageField = CreateInfoField(_usageString);
                var uiTextRectT = _usageField.UItext.GetComponent<RectTransform>();
                var uiTextPos = uiTextRectT.anchoredPosition;
                uiTextRectT.anchoredPosition = new Vector2(uiTextPos.x, uiTextPos.y - 10);
                var uiTextSize = uiTextRectT.sizeDelta;
                uiTextRectT.sizeDelta = new Vector2(uiTextSize.x - 8, uiTextSize.y - 20);
            }

            {
                _infoField = CreateInfoField(_infoString);
                var uiTextRectT = _infoField.UItext.GetComponent<RectTransform>();
                var uiTextPos = uiTextRectT.anchoredPosition;
                uiTextRectT.anchoredPosition = new Vector2(uiTextPos.x, uiTextPos.y - 75);
                var uiTextSize = uiTextRectT.sizeDelta;
                uiTextRectT.sizeDelta = new Vector2(uiTextSize.x - 8, uiTextSize.y - 85);
                _infoField.gameObject.SetActive(false);

                var pathFieldT = Instantiate(manager.configurableTextFieldPrefab, _infoField.transform);
                var uiDynamic = pathFieldT.GetComponent<UIDynamicTextField>();
                uiDynamic.UItext.fontSize = 26;
                uiDynamic.backgroundColor = Color.clear;
                var layoutElement = pathFieldT.GetComponent<LayoutElement>();
                DestroyImmediate(layoutElement);
                var rectT = pathFieldT.GetComponent<RectTransform>();
                rectT.pivot = Vector2.zero;
                rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 0, 75);
                rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 0, 650);
                _pathString.dynamicText = uiDynamic;
                Utils.DisableScroll(uiDynamic);
            }
        }

        void CreateHeader(string text)
        {
            var uiDynamic = CreateTextField(new JSONStorableString(Guid.NewGuid().ToString().Substring(0, 4), text));
            var layoutElement = uiDynamic.GetComponent<LayoutElement>();
            layoutElement.minHeight = 65;
            layoutElement.preferredHeight = 65;
            uiDynamic.UItext.fontSize = 30;
            uiDynamic.UItext.fontStyle = FontStyle.Bold;
            uiDynamic.backgroundColor = Color.clear;
            var rectT = uiDynamic.UItext.GetComponent<RectTransform>();
            var pos = rectT.anchoredPosition;
            pos.y = -15;
            rectT.anchoredPosition = pos;
            Utils.DisableScroll(uiDynamic);
        }

        void CreateTriggerMenuButton(TriggerWrapper trigger)
        {
            var uiDynamic = CreateButton(trigger.label);
            uiDynamic.AddListener(() =>
            {
                trigger.eventTrigger.OpenPanel();
                if(_usageString.dynamicText != null)
                {
                    _usageString.dynamicText.gameObject.SetActive(false);
                }
            });
            trigger.RegisterOnCloseCallback(() =>
            {
                if(_usageString.dynamicText != null)
                {
                    _usageString.dynamicText.gameObject.SetActive(true);
                }
            });
            var textComponent = uiDynamic.buttonText;
            textComponent.resizeTextForBestFit = true;
            textComponent.resizeTextMinSize = 24;
            textComponent.resizeTextMaxSize = 28;
            textComponent.alignment = TextAnchor.MiddleLeft;
            var textRectT = textComponent.GetComponent<RectTransform>();
            var pos = textRectT.anchoredPosition;
            pos.x += 15;
            textRectT.anchoredPosition = pos;
            trigger.button = uiDynamic;
            trigger.UpdateButton();
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

        UIDynamicButton CreateTextToggleButton(string label, UnityAction action)
        {
            var buttonT = Instantiate(manager.configurableButtonPrefab, UITransform);
            var uiDynamic = buttonT.GetComponent<UIDynamicButton>();
            uiDynamic.label = label;
            uiDynamic.buttonText.fontSize = 24;
            uiDynamic.AddListener(action);
            var rectT = buttonT.GetComponent<RectTransform>();
            rectT.pivot = Vector2.zero;
            rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 20, 36);
            rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 445, 100);
            var textRectT = uiDynamic.buttonText.GetComponent<RectTransform>();
            var pos = textRectT.anchoredPosition;
            textRectT.anchoredPosition = new Vector2(pos.x, pos.y + 2);
            return uiDynamic;
        }

        UIDynamicTextField CreateInfoField(JSONStorableString jss)
        {
            var textFieldT = Instantiate(manager.configurableTextFieldPrefab, UITransform);
            var uiDynamic = textFieldT.GetComponent<UIDynamicTextField>();
            uiDynamic.UItext.fontSize = 26;
            uiDynamic.backgroundColor = new Color(0.92f, 0.92f, 0.92f);
            var layoutElement = textFieldT.GetComponent<LayoutElement>();
            DestroyImmediate(layoutElement);
            var rectT = textFieldT.GetComponent<RectTransform>();
            rectT.pivot = Vector2.zero;
            rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 20, 1210);
            rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 545, 650);
            var scrollView = textFieldT.Find("Scroll View");
            var scrollRect = scrollView.GetComponent<ScrollRect>();
            scrollRect.horizontal = true;
            scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            jss.dynamicText = uiDynamic;
            return uiDynamic;
        }

        void ShowUsage()
        {
            if(!_uiCreated)
            {
                return;
            }

            _usageButton.gameObject.SetActive(false);
            _backButton.gameObject.SetActive(true);
            _infoField.gameObject.SetActive(false);
            _usageField.gameObject.SetActive(true);
        }

        void ShowInfo()
        {
            if(!_uiCreated)
            {
                return;
            }

            _usageButton.gameObject.SetActive(true);
            _backButton.gameObject.SetActive(false);
            _usageField.gameObject.SetActive(false);
            _infoField.gameObject.SetActive(true);
        }

        void FindDependenciesCallback(bool rescan = true)
        {
            if(_metaJson == null)
            {
                return;
            }

            if(_downloadCo != null)
            {
                StopCoroutine(_downloadCo);
            }

            _pending = false;
            _finished = false;
            _downloadErrorsSb.Clear();
            SetProgress(0);
            ShowInfo();

            if(rescan)
            {
                _infoString.val = "Rescanning packages";
                SuperController.singleton.RescanPackages();
                _infoString.val = "";
            }

            _packages.Clear();
            _disabledPackages.Clear();
            _versionErrorPackages.Clear();
            _missingVamBundledPackageNames.Clear();
            _missingPackages.Clear();
            _updateRequiredPackages.Clear();
            _installedPackages.Clear();

            try
            {
                FindDependenciesRecursive(_metaJson, _searchSubDependenciesBool.val);
            }
            catch(Exception e)
            {
                _infoString.val = $"<color=#{_errorColor}Error identifying dependencies!</color>";
                logBuilder.Exception("Error identifying dependencies", e);
                return;
            }

            if(_identifyDisabledPackagesBool.val)
            {
                try
                {
                    IdentifyDisabledPackages(_packages.ToDictionary(obj => obj.name, obj => obj));
                }
                catch(Exception e)
                {
                    logBuilder.Exception("Identifying disabled packages failed", e);
                    _disabledPackages.Clear();
                }
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
                            _updateRequiredPackages.Add(obj);
                            _installedPackages.RemoveAt(i);
                        }
                    }
                }

                _updateRequiredPackages.Reverse();
            }

            if(!_isLatestVam)
            {
                _ifVamNotLatestTrigger.Trigger();
            }

            if(_versionErrorPackages.Count > 0)
            {
                if(!_uiListener.active)
                {
                    logBuilder.Error($"Version errors in meta.json, see plugin UI on atom {containingAtom.uid} (Keybindings: AutoGetDependencies.OpenUI)");
                }
            }

            if(_disabledPackages.Count > 0)
            {
                _ifDisabledPackagesDetectedTrigger.Trigger();
                if(_ifDisabledPackagesDetectedTrigger.sendToText != null)
                {
                    _ifDisabledPackagesDetectedTrigger.sendToText.text = _disabledPackages.Select(obj => obj.name).ToPrettyString();
                }
            }

            if(_missingVamBundledPackageNames.Count > 0)
            {
                _ifVamBundledPackagesMissingTrigger.Trigger();
                if(_ifVamBundledPackagesMissingTrigger.sendToText != null)
                {
                    _ifVamBundledPackagesMissingTrigger.sendToText.text = _missingVamBundledPackageNames.ToPrettyString();
                }
            }

            if(_missingPackages.Count > 0 || _updateRequiredPackages.Count > 0)
            {
                _ifDownloadPendingTrigger.Trigger();
                if(_ifDownloadPendingTrigger.sendToText != null)
                {
                    var sb = new StringBuilder();
                    sb.Append("Missing, download required:\n\n");
                    if(_missingPackages.Count > 0)
                    {
                        _missingPackages.Select(obj => obj.name).ToPrettyString(sb);
                    }
                    else
                    {
                        sb.Append("None.\n");
                    }

                    if(_missingVamBundledPackageNames.Count > 0)
                    {
                        sb.AppendFormat("Missing VAM bundled packages:\n\n");
                        _missingVamBundledPackageNames.ToPrettyString(sb);
                        sb.Append("\n");
                    }

                    if(_updateRequiredPackages.Count > 0)
                    {
                        sb.Append("\nInstalled, check for update required:\n\n");
                        _updateRequiredPackages.Select(obj => obj.name).ToPrettyString(sb);
                        sb.Append("\n");
                    }

                    sb.Append("Installed, no update required:\n\n");
                    _installedPackages.Select(obj => obj.name).ToPrettyString(sb);
                    _ifDownloadPendingTrigger.sendToText.text = sb.ToString();
                }

                _pending = true;
                UpdatePendingInfo();
            }
            else
            {
                if(_versionErrorPackages.Count == 0 && _disabledPackages.Count == 0 && _missingVamBundledPackageNames.Count == 0)
                {
                    _ifAllDependenciesInstalledTrigger.Trigger();
                    if(_ifAllDependenciesInstalledTrigger.sendToText != null)
                    {
                        _ifAllDependenciesInstalledTrigger.sendToText.text = "All dependencies are installed!";
                    }
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

                if(_vamBundledPackageNames.Contains($"{parts[0]}.{parts[1]}"))
                {
                    _missingVamBundledPackageNames.Add(trimmed);
                }
                else if(!_packages.Exists(obj => obj.name == trimmed))
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

            ShowInfo();
            _infoString.dynamicText.UItext.horizontalOverflow = HorizontalWrapMode.Overflow;
            var sb = new StringBuilder();

            AppendShared(sb);
            AppendPackagesInfo(sb, "Missing, download required", _errorColor, _missingPackages);
            AppendPackagesInfo(sb, "Installed, check for update required", _updateRequiredColor, _updateRequiredPackages);
            AppendPackagesInfo(sb, "Installed, no update required", _okColor, _installedPackages);

            SetJssText(_infoString, sb);
            _usageButton.gameObject.SetActive(true);
        }

        static void AppendPackagesInfo(StringBuilder sb, string title, string titleColor, List<PackageObj> packages)
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

            ShowInfo();
            _infoString.dynamicText.UItext.horizontalOverflow = HorizontalWrapMode.Wrap;
            var sb = new StringBuilder();

            if(_packages.TrueForAll(obj => obj.existsAndIsValid))
            {
                sb.AppendFormat("<size=30><color=#{0}><b>All dependencies are installed!</b></color></size>\n\n", _okColor);
            }
            else
            {
                AppendShared(sb);
                if(_notOnHubPackages.Count > 0)
                {
                    AppendPackagesInfo(sb, "Packages not on Hub", _errorColor, _notOnHubPackages);
                }
                if(_downloadErrorsSb.Length > 0)
                {
                    sb.AppendFormat("<size=30><color=#{0}><b>Errors during download:</b></color></size>\n\n", _errorColor);
                    sb.Append(_downloadErrorsSb);
                    sb.Append("\n\n");
                }
                AppendPackagesInfo(sb, "Installed", _okColor, _packages.Where(obj => obj.existsAndIsValid).ToList());
            }

            SetJssText(_infoString, sb);
            _usageButton.gameObject.SetActive(true);
        }

        void AppendShared(StringBuilder sb)
        {
            if(!_isLatestVam && _ifVamNotLatestTrigger.eventTrigger.HasActions())
            {
                sb.Append("VAM is not in the latest version (>= v1.22).\n\n");
            }
            if(_missingVamBundledPackageNames.Count > 0)
            {
                sb.AppendFormat("<size=30><color=#{0}><b>Missing VAM bundled packages:</b></color></size>\n\n", _errorColor);
                _missingVamBundledPackageNames.ToPrettyString(sb);
                sb.Append("\n");
            }
            if(_versionErrorPackages.Count > 0)
            {
                AppendPackagesInfo(sb, "Version error in meta.json", _errorColor, _versionErrorPackages);
            }
            if(_disabledPackages.Count > 0)
            {
                AppendPackagesInfo(sb, "Disabled", _errorColor, _disabledPackages);
            }
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
                logBuilder.Exception(e);
            }
        }

        // see e.g. DAZCharacterTextureControl.BeginBrowse§
        void BeginLoadBrowse(JSONStorableUrl url)
        {
            url.shortCuts = FileManagerSecure.GetShortCutsForDirectory("");
        }

        void OnMetaJsonSelected(JSONStorableUrl url)
        {
            string path = url.val;
            if(string.IsNullOrEmpty(path))
            {
                _metaJson = null;
            }
            else
            {
                _pathString.val = path;
                if(!path.EndsWith("meta.json"))
                {
                    ShowInfo();
                    _infoString.val = $"<color=#{_errorColor}>Selected file is not a meta.json file.</color>";
                    return;
                }

                _metaJson = SuperController.singleton.LoadJSON(path)?.AsObject;
                FindDependenciesCallback();
            }

            url.val = "";
        }

        void DownloadMissingCallback()
        {
            if(!_pending)
            {
                if(logErrorsBool.val) logBuilder.Error("Download is not pending");
                return;
            }

            _pending = false;
            ShowInfo();
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
                foreach(var pair in _triggers)
                {
                    var trigger = pair.Value;
                    if(trigger.sendToAtom == atom)
                    {
                        trigger.uiTextChooser?.SetVal("");
                    }
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
            foreach(var pair in _triggers)
            {
                var chooser = pair.Value.uiTextChooser;
                if(chooser != null && chooser.val == oldUid)
                {
                    chooser.val = newUid;
                }
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
            foreach(var pair in _triggers)
            {
                var chooser = pair.Value.uiTextChooser;
                if(chooser != null)
                {
                    chooser.choices = options;
                    chooser.displayChoices = displayOptions;
                }
            }
        }

        void RegisterBindings()
        {
            if(_bindings != null)
            {
                return;
            }

            _bindings = new Bindings(nameof(AutoGetDependencies), new List<JSONStorableAction>
            {
                new JSONStorableAction("FindDependencies", () => FindDependenciesCallback()),
                new JSONStorableAction("DownloadMissing", DownloadMissingCallback),
                new JSONStorableAction("ForceFinish", ForceFinishCallback),
                new JSONStorableAction("CopyErrorsToClipboard", CopyErrorsToClipboardCallback),
                new JSONStorableAction("CopyNotOnHubToClipboard", () => CopyToClipboard(_notOnHubPackages)),
                new JSONStorableAction("CopyDisabledToClipboard", () => CopyToClipboard(_disabledPackages)),
                new JSONStorableAction("OpenUI", () => this.SelectPluginUI()),
            });
        }

        // https://github.com/vam-community/vam-plugins-interop-specs/blob/main/keybindings.md
        public void OnBindingsListRequested(List<object> bindingsList)
        {
            RegisterBindings();
            bindingsList.Add(_bindings.namespaceDict);
            bindingsList.AddRange(_bindings.GetActions());
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
                        logBuilder.Error($"UISlider '{option}' not found");
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
                logBuilder.Exception(e);
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

        TriggerWrapper AddTrigger(string name, string label, bool enableSendText = true)
        {
            var trigger = _triggers[name] = new TriggerWrapper(this, name, label);
            if(enableSendText)
            {
                {
                    var chooser = new JSONStorableStringChooser($"{name} UIText", new List<string>(), "", "Send To UIText");
                    chooser.setCallbackFunction = option => trigger.SelectUITextCallback(option, _uiTexts);
                    chooser.representsAtomUid = true;
                    RegisterStringChooser(chooser);
                    trigger.uiTextChooser = chooser;
                }

                trigger.eventTrigger.onInitPanel += panel =>
                {
                    var popupT = Instantiate(manager.configurableScrollablePopupPrefab, panel);
                    var popupRectT = popupT.GetComponent<RectTransform>();
                    popupRectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 62, 120f);
                    popupRectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Right, 15f, 545f);
                    var uiDynamic = popupT.GetComponent<UIDynamicPopup>();
                    uiDynamic.height = 100f;
                    uiDynamic.labelTextColor = Color.white;
                    uiDynamic.popupPanelHeight = 500;
                    uiDynamic.popup.selectColor = _paleBlue;
                    popupT.Find("Background").GetComponent<Image>().color = Color.clear;
                    trigger.RegisterOnCloseCallback(() => uiDynamic.popup.visible = false);
                    trigger.uiTextChooser.popup = uiDynamic.popup;
                };
            }

            return trigger;
        }

        void OnError(string message, bool teardown = true)
        {
            if(logErrorsBool.val) logBuilder.Error(message);
            _downloadErrorsSb.AppendLine(message);
            if(teardown)
            {
                Teardown();
            }
        }

        void OnException(string message, Exception e, bool teardown = true)
        {
            if(logErrorsBool.val) logBuilder.Exception(message, e);
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
                _usageString.val = "Downloading missing packages...\n";
                if(_tempEnableHubBool.val && !_hubBrowse.HubEnabled)
                {
                    _hubBrowse.HubEnabled = true;
                    _hubWasTempEnabled = true;
                }

                _hubBrowsePanelT = _hubBrowse.UITransform;
                if(_hubBrowsePanelT == null)
                {
                    OnError("HubBrowsePanel not found");
                    yield break;
                }

                _missingPackagesPanelT = _hubBrowsePanelT.Find("MissingPackagesPanel");
                if(_missingPackagesPanelT == null)
                {
                    OnError("MissingPackagesPanel not found");
                    yield break;
                }

                _contentT = _missingPackagesPanelT.Find("InnerPanel/HubDownloads/Downloads/Viewport/Content");
                if(_contentT == null)
                {
                    OnError("InnerPanel/HubDownloads/Downloads/Viewport/Content not found");
                    yield break;
                }
            }
            catch(Exception e)
            {
                if(logErrorsBool.val) logBuilder.Exception(e);
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
                        if(logErrorsBool.val) logBuilder.Error("HubResourcePackageUI component not found on panel");
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
            var packagesToDownload = _missingPackages.Concat(_updateRequiredPackages).ToList();
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
                            if(logErrorsBool.val) logBuilder.Error("HubResourcePackage not found for HubResourcePackageUI");
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
                        if(logErrorsBool.val) logBuilder.Error(error);
                        _downloadErrorsSb.AppendLine(error);
                    }

                    if(obj.connectedItem == null)
                    {
                        continue;
                    }

                    if(!obj.connectedItem.NeedsDownload)
                    {
                        logBuilder.Debug($"{obj.name} item NeedsDownload=False");
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
                            if(logErrorsBool.val) logBuilder.Error($"'{obj.name}' error: {obj.downloadError}");
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
                logBuilder.Exception(e);
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
                    logBuilder.Exception(e);
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
                _ifAllDependenciesInstalledTrigger.Trigger();
                if(_ifAllDependenciesInstalledTrigger.sendToText != null)
                {
                    _ifAllDependenciesInstalledTrigger.sendToText.text = "All dependencies are installed!";
                }
            }
            // failure path
            else
            {
                if(_notOnHubPackages.Count > 0)
                {
                    _ifNotOnHubPackagesDetectedTrigger.Trigger();
                    if(_ifNotOnHubPackagesDetectedTrigger.sendToText != null)
                    {
                        _ifNotOnHubPackagesDetectedTrigger.sendToText.text = _notOnHubPackages.Select(obj => obj.name).ToPrettyString();
                    }
                }

                _ifSomePackagesNotInstalledTrigger.Trigger();
                if(_ifSomePackagesNotInstalledTrigger.sendToText != null)
                {
                    _ifSomePackagesNotInstalledTrigger.sendToText.text = _downloadErrorsSb.ToString();
                }
            }

            _downloadCo = null;
            _finished = true;
            UpdateFinishedInfo();
        }
#endregion Teardown

        public override JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false)
        {
            var jc = base.GetJSON(includePhysical, includeAppearance, forceStore);

            try
            {
                needsStore = true;
                foreach(var pair in _triggers)
                {
                    var eventTrigger = pair.Value.eventTrigger;
                    jc[eventTrigger.Name] = eventTrigger.GetJSON(_subScenePrefix);
                }
            }
            catch(Exception e)
            {
                logBuilder.Exception(e);
            }

            return jc;
        }

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
        // TODO should be in LateRestoreFromJSON?
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

                foreach(var pair in _triggers)
                {
                    var chooser = pair.Value.uiTextChooser;
                    if(chooser != null && jc.HasKey(chooser.name))
                    {
                        var atom = SuperController.singleton.GetAtomByUid(jc[chooser.name].Value);
                        if(atom == null || atom.containingSubScene != subScene)
                        {
                            targetAtomInAnotherSubscene = true;
                        }
                    }
                }

                if(targetAtomInAnotherSubscene)
                {
                    subScenePrefix = containingAtom.uid.Replace(containingAtom.uidWithoutSubScenePath, "");
                }
            }
        }

        public override void LateRestoreFromJSON(JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true, bool setMissingToDefault = true)
        {
            base.LateRestoreFromJSON(jc, restorePhysical, restoreAppearance, setMissingToDefault);
            foreach(var pair in _triggers)
            {
                pair.Value.RestoreFromJSON(jc, _subScenePrefix, true, setMissingToDefault);
            }
        }

        void OnDestroy()
        {
            try
            {
                foreach(var pair in _triggers)
                {
                    pair.Value.Destroy();
                }

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
                SuperController.singleton.BroadcastMessage("OnActionsProviderDestroyed", this, SendMessageOptions.DontRequireReceiver);

                if(_downloadCo != null)
                {
                    StopCoroutine(_downloadCo);
                }

                if(_handleUserConfirmPanelsCo != null)
                {
                    StopCoroutine(_handleUserConfirmPanelsCo);
                }
            }
            catch(Exception e)
            {
                SuperController.LogError("AutoGetDependencies.OnDestroy: " + e);
            }
        }

        readonly HashSet<string> _vamBundledPackageNames = new HashSet<string>
        {
            "DJ.TanLines",
            "Jackaroo.SmartSuitJaR",
            "JayC_Re-animator.Hair_Curly_Bob",
            "MeshedVR.3PointLightSetup",
            "MeshedVR.AssetsPack",
            "MeshedVR.BonusScenes",
            "MeshedVR.DemoScenes",
            "MeshedVR.OlderContent",
            "MeshedVR.PresetsPack",
            "NoOC.Clothing_SailorLingerie",
            "NoStage3.Hair_Long_Upswept_Top_Bun",
            "Vince.Clothing_PleatedSkirtV2T",
            "Xstatic.MegaParticlePack",
        };
    }
}
