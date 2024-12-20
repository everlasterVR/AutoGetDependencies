#define VAM_GT_1_21
#define VAM_GT_1_22
using MacGruber_Utils;
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

/*
 * AutoGetDependencies v1.0
 * Licensed under CC BY https://creativecommons.org/licenses/by/4.0/
 * (c) 2024 everlaster
 * https://patreon.com/everlaster
 */
namespace everlaster
{
    sealed class AutoGetDependencies : MVRScript
    {
        UnityEventsListener _uiListener;
        Transform _scrollView;
        bool _uiCreated;
        public readonly LogBuilder logBuilder = new LogBuilder(nameof(AutoGetDependencies));
        Bindings _bindings;
        JSONClass _metaJson;
        HubBrowse _hubBrowse;
        bool _isSessionPlugin;
        bool _initialized;
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
        readonly List<PackageObj> _missingVamBundledPackages = new List<PackageObj>();
        readonly List<PackageObj> _missingPackages = new List<PackageObj>();
        readonly List<PackageObj> _updateRequiredPackages = new List<PackageObj>();
        readonly List<PackageObj> _installedPackages = new List<PackageObj>();
        readonly List<HubResourcePackageUI> _packageUIs = new List<HubResourcePackageUI>();
        readonly List<PackageObj> _notOnHubPackages = new List<PackageObj>();
        Coroutine _downloadCo;
        Coroutine _handleUserConfirmPanelsCo;
        bool _metaRead;
        bool _pending;
        bool _finished;
        bool _forceStopped;
        readonly StringBuilder _downloadErrorsSb = new StringBuilder();
        float _progress;

        JSONStorableBool _rescanPackagesOnSelectBool;
        JSONStorableBool _searchSubDependenciesBool;
        JSONStorableBool _alwaysCheckForUpdatesBool;
        JSONStorableBool _identifyDisabledPackagesBool;
        JSONStorableAction _recheckDependenciesAction;
        JSONStorableAction _scanLoadedSceneMetaJson;
        JSONStorableAction _selectMetaJsonAction;
        JSONStorableUrl _selectMetaJsonUrl;
        JSONStorableString _usageString;
        JSONStorableString _pathString;
        JSONStorableString _infoString;
        UIDynamicButton _usageButton;
        UIDynamicButton _backButton;
        JSONStorableBool _autoDownloadIfPendingBool;
        JSONStorableBool _tempEnableHubBool;
        JSONStorableBool _autoAcceptPackagePluginsBool;
        JSONStorableAction _recheckInternetConnectionAction;
        JSONStorableAction _downloadAction;
        JSONStorableStringChooser _progressBarChooser;
        public JSONStorableBool logErrorsBool { get; private set; }

        JSONStorableFloat _progressFloat;
        JSONStorableAction _stopDownloadAction;
        JSONStorableAction _navigateToPluginUIAction;

        readonly Dictionary<string, TriggerWrapper> _triggers = new Dictionary<string, TriggerWrapper>();
        TriggerWrapper _ifDownloadPendingTrigger;
        TriggerWrapper _ifDisabledPackagesDetectedTrigger;
        TriggerWrapper _ifNoInternetConnectionTrigger;
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
            #if !VAM_GT_1_21
            {
                return;
            }
            #endif

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

            try
            {
                if(!_uiCreated)
                {
                    CreateUI();
                    _uiCreated = true;
                }

                UpdateInfo();
            }
            catch(Exception e)
            {
                logBuilder.Exception(e);
            }
        }

        public override void Init()
        {
            try
            {
                #if !VAM_GT_1_21
                {
                    AlertWithRemoveCallback("AutoGetDependencies requires VaM v1.21 or newer");
                    return;
                }
                #endif

                var coreControl = SuperController.singleton.GetAtomByUid("CoreControl");
                _hubBrowse = (HubBrowse) coreControl.GetStorableByID("HubBrowseController");
                if(_hubBrowse == null)
                {
                    AlertWithRemoveCallback("AutoGetDependencies: HubBrowseController not found.");
                    return;
                }

                #if VAM_GT_1_22
                _isLatestVam = true;
                #else
                _isLatestVam = false;
                #endif

                _isSessionPlugin = containingAtom.type == "SessionPluginManager";
                _usageString = new JSONStorableString("Usage", "");
                _pathString = new JSONStorableString("Path", "");
                _infoString = new JSONStorableString("Info", "");

                _metaJson = FindLoadedSceneMetaJson();

                _rescanPackagesOnSelectBool = new JSONStorableBool("Rescan packages on select meta json", false);
                RegisterBool(_rescanPackagesOnSelectBool);

                _searchSubDependenciesBool = new JSONStorableBool("Search sub-dependencies", false, (bool _) => RecheckDependencies());
                RegisterBool(_searchSubDependenciesBool);

                _alwaysCheckForUpdatesBool = new JSONStorableBool("Always check for updates to '.latest'", false, (bool _) => RecheckDependencies());
                RegisterBool(_alwaysCheckForUpdatesBool);

                _identifyDisabledPackagesBool = new JSONStorableBool("Identify disabled packages", false, (bool _) => RecheckDependencies());
                RegisterBool(_identifyDisabledPackagesBool);

                _recheckDependenciesAction = new JSONStorableAction("Recheck dependencies", RecheckDependencies);
                RegisterAction(_recheckDependenciesAction);

                _selectMetaJsonUrl = new JSONStorableUrl("_selectMetaJsonUrl", "", "json", "AddonPackages")
                {
                    allowFullComputerBrowse = false,
                    allowBrowseAboveSuggestedPath = true,
                    showDirs = true,
                    endBrowseWithObjectCallback = OnMetaJsonSelected,
                };

                _selectMetaJsonAction = new JSONStorableAction("Select meta json", () =>
                {
                    SuperController.singleton.ShowMainHUDAuto();
                    _selectMetaJsonUrl.FileBrowse();
                });
                RegisterAction(_selectMetaJsonAction);

                _scanLoadedSceneMetaJson = new JSONStorableAction("Scan loaded scene meta json", () =>
                {
                    _metaJson = FindLoadedSceneMetaJson();
                    FindDependenciesCallback();
                });
                RegisterAction(_scanLoadedSceneMetaJson);

                _autoDownloadIfPendingBool = new JSONStorableBool("Auto-download if pending", false);
                RegisterBool(_autoDownloadIfPendingBool);

                _tempEnableHubBool = new JSONStorableBool("Temp auto-enable Hub if needed", false);
                RegisterBool(_tempEnableHubBool);

                _autoAcceptPackagePluginsBool = new JSONStorableBool("Auto-accept plugins from packages", false);
                RegisterBool(_autoAcceptPackagePluginsBool);

                _recheckInternetConnectionAction = new JSONStorableAction("Recheck internet connection", RecheckInternetConnection);
                RegisterAction(_recheckInternetConnectionAction);

                _downloadAction = new JSONStorableAction("Download missing dependencies", DownloadMissingCallback);
                RegisterAction(_downloadAction);

                _progressBarChooser = new JSONStorableStringChooser("Progress Bar", new List<string>(), "", "Progress Bar");
                _progressBarChooser.setCallbackFunction = SelectProgressBarCallback;
                _progressBarChooser.representsAtomUid = true;
                RegisterStringChooser(_progressBarChooser);

                logErrorsBool = new JSONStorableBool("Log errors", false);
                RegisterBool(logErrorsBool);

                _progressFloat = new JSONStorableFloat("Download progress (%)", 0, 0, 100);

                _stopDownloadAction = new JSONStorableAction("Stop download", () => _forceStopped = true);
                RegisterAction(_stopDownloadAction);

                _navigateToPluginUIAction = new JSONStorableAction("Open UI", () => this.SelectPluginUI());
                RegisterAction(_navigateToPluginUIAction);

                SimpleTriggerHandler.LoadAssets();

                _ifDownloadPendingTrigger = AddTrigger("If Download Pending", "If download pending...", "Packages\nMissing", "Only Check For Updates");
                _ifDisabledPackagesDetectedTrigger = AddTrigger("If Disabled Packages Detected", "If disabled packages detected...");
                _ifNoInternetConnectionTrigger = AddTrigger("If No Internet Connection", "If no internet connection...", "Not Connected\nActions", "Connection\nRestored Actions", false);
                _ifAllDependenciesInstalledTrigger = AddTrigger("If All Dependencies Installed", "If all dependencies installed...");
                _ifVamBundledPackagesMissingTrigger = AddTrigger("If VaM Bundled Packages Missing", "If VaM bundled packages missing...");
                _ifSomePackagesNotInstalledTrigger = AddTrigger("If Some Packages Not Installed", "If some packages not installed...");
                if(!_isSessionPlugin)
                {
                    _ifVamNotLatestTrigger = AddTrigger("If VaM Not Latest", "If VaM not latest (>= v1.22)...", false);
                }
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

        void AlertWithRemoveCallback(string error)
        {
            #if VAM_GT_1_21
            {
                var sb = new StringBuilder();
                sb.Append($"\n\n<color=#{ColorUtility.ToHtmlStringRGB(new Color(0.45f, 0.125f, 0.125f))}>")
                    .Append($"<size=44>{error}</size>")
                    .Append("</color>");

                SuperController.singleton.Alert(sb.ToString(), () => manager.RemovePluginWithUID(this.GetPluginId()));
            }
            #else
            {
                logBuilder.Error(error);
                var textField = CreateTextField(new JSONStorableString("error", error));
                textField.backgroundColor = Color.clear;
                var layoutElement = textField.GetComponent<LayoutElement>();
                layoutElement.minHeight = 1000;
                layoutElement.preferredHeight = 1000;
                enabledJSON.valNoCallback = false;
            }
            #endif
        }

        void RecheckDependencies()
        {
            if(_metaJson == null || !_metaRead)
            {
                return;
            }

            FindDependenciesCallback(false);
        }

        JSONClass FindLoadedSceneMetaJson()
        {
            #if VAM_GT_1_21
            {
                string loadedScene = SuperController.singleton.LoadedSceneName;
                if(loadedScene == null || !loadedScene.Contains(":/"))
                {
                    return null;
                }

                string metaJsonPath = loadedScene.Split(':')[0] + ":/meta.json";
                _pathString.val = metaJsonPath;
                return SuperController.singleton.LoadJSON(metaJsonPath)?.AsObject;
            }
            #else
            {
                return null;
            }
            #endif
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

            {
                var uiDynamic = CreateButton("Select meta.json");
                Color color;
                if(ColorUtility.TryParseHtmlString(FIND_DEPENDENCIES_COLOR, out color)) uiDynamic.buttonColor = color;
                uiDynamic.height = 62;
                _selectMetaJsonAction.RegisterButton(uiDynamic);
            }
            {
                var uiDynamic = CreateButton("Scan loaded scene meta.json");
                Color color;
                if(ColorUtility.TryParseHtmlString(FIND_DEPENDENCIES_COLOR, out color)) uiDynamic.buttonColor = color;
                uiDynamic.height = 62;
                _scanLoadedSceneMetaJson.RegisterButton(uiDynamic);
            }

            CreateSpacer().height = 10;
            CreateTriggerMenuButton(_ifDownloadPendingTrigger);
            CreateTriggerMenuButton(_ifDisabledPackagesDetectedTrigger);
            CreateTriggerMenuButton(_ifNoInternetConnectionTrigger);
            CreateTriggerMenuButton(_ifAllDependenciesInstalledTrigger);
            CreateTriggerMenuButton(_ifVamBundledPackagesMissingTrigger);
            CreateTriggerMenuButton(_ifVamNotLatestTrigger);

            CreateSpacer().height = 5;
            CreateHeader("2. Download Dependencies");
            if(_isSessionPlugin)
            {
                CreateToggle(_autoDownloadIfPendingBool);
            }
            CreateToggle(_tempEnableHubBool);
            CreateToggle(_autoAcceptPackagePluginsBool);
            {
                var uiDynamic = CreateButton(_downloadAction.name);
                Color color;
                if(ColorUtility.TryParseHtmlString(DOWNLOAD_DEPENDENCIES_COLOR, out color)) uiDynamic.buttonColor = color;
                uiDynamic.height = 62;
                _downloadAction.RegisterButton(uiDynamic);
            }

            CreateSpacer().height = 10;

            {
                var popup = CreateScrollablePopup(_progressBarChooser);
                popup.height = 75;
                ConfigurePopup(popup, 340);
            }

            UIDynamicSlider progressSlider;
            {
                progressSlider = CreateSlider(_progressFloat);
                progressSlider.valueFormat = "F0";
                progressSlider.HideButtons();
                progressSlider.SetInteractable(false);
                var sliderT = (RectTransform) progressSlider.gameObject.transform.Find("Slider");
                var pos = sliderT.anchoredPosition;
                var size = sliderT.sizeDelta;
                sliderT.anchoredPosition = new Vector2(pos.x - 30, pos.y);
                sliderT.sizeDelta = new Vector2(size.x - 60, size.y);
            }
            {
                var buttonT = (RectTransform) Instantiate(manager.configurableButtonPrefab, progressSlider.transform);
                var uiDynamic = buttonT.GetComponent<UIDynamicButton>();
                uiDynamic.label = "Stop";
                uiDynamic.buttonText.fontSize = 24;
                var layoutElement = buttonT.GetComponent<LayoutElement>();
                DestroyImmediate(layoutElement);
                buttonT.pivot = Vector2.zero;
                buttonT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Right, 8, 64);
                buttonT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Bottom, 8, 40);
                _stopDownloadAction.RegisterButton(uiDynamic);
            }

            CreateSpacer().height = 10;
            CreateTriggerMenuButton(_ifSomePackagesNotInstalledTrigger);
            CreateTriggerMenuButton(_ifNotOnHubPackagesDetectedTrigger);
            CreateSpacer().height = 10;

            CreateSpacer(true).height = 1135;
            CreateToggle(logErrorsBool, true);

            _usageButton = CreateTextToggleButton("Usage", ShowUsage);
            _usageButton.gameObject.SetActive(false);
            _backButton = CreateTextToggleButton("< Back", ShowInfo);
            _backButton.gameObject.SetActive(false);

            {
                const string usage1 =
                    "AutoGetDependencies works in two stages:" +
                    "\n" +
                    "\n(1) Select meta.json to identify dependencies from" +
                    "\n(2) Download any missing dependencies" +
                    "\n";
                const string usage2 =
                    "\nUse the toggles configure the behavior of these two stages, set up triggers for different end conditions," +
                    " and then execute/trigger the identification and download actions in your scene logic (on scene load, via UIButton etc.)." +
                    "\n";
                const string usage3 =
                    "\n<size=32>1. Identify Dependencies</size>" +
                    "\n¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨" +
                    "\n<b>Search sub-dependencies</b>\nIdentify missing packages that are not direct dependencies of the package." +
                    " In the resulting dependency list, sub-dependencies are greyed out (not strictly required)." +
                    "\n" +
                    "\n<b>Always check for updates to '.latest'</b>\nFind dependencies with the latest version requirement and put them pending download," +
                    " even if some version of each package is already installed." +
                    "\n" +
                    "\n<b>Identify disabled packages</b>\nIdentify dependencies that are disabled by scanning the AddonPackages folder." +
                    " Uses up RAM and can take a while if there are lots of packages." +
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
                    "\n";
                const string usage4 =
                    "\n<b>If VaM not latest (>= v1.22)...</b> - inform users that they should update VaM if it's required by your scene" +
                    " (e.g. when using VaM's built in lip sync)." +
                    "\n";
                const string usage5 =
                    "\nCustom triggers can additionally send information to a UIText in the scene (see the Hub resource and the demo scene)." +
                    "\n" +
                    "\n<size=32>2. Download Dependencies</size>" +
                    "\n¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨¨";
                const string usage6 =
                    "\n<b>Auto-download if pending</b>\nAutomatically start the download process if missing dependencies are detected during the meta.json scan." +
                    "\n";
                const string usage7 =
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

                _usageString.val = _isSessionPlugin
                    ? usage1 + usage3 + usage5 + usage6 + usage7
                    : usage1 + usage2 + usage3 + usage4 + usage5 + usage7;
                var usageField = CreateInfoField(_usageString);
                var rectT = usageField.gameObject.GetComponent<RectTransform>();
                rectT.pivot = Vector2.zero;
                rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 20, 1138);
                rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 545, 650);
                var uiTextRectT = usageField.UItext.GetComponent<RectTransform>();
                var uiTextPos = uiTextRectT.anchoredPosition;
                uiTextRectT.anchoredPosition = new Vector2(uiTextPos.x, uiTextPos.y - 10);
                var uiTextSize = uiTextRectT.sizeDelta;
                uiTextRectT.sizeDelta = new Vector2(uiTextSize.x - 8, uiTextSize.y - 20);
            }

            {
                var pathFieldT = Instantiate(manager.configurableTextFieldPrefab, UITransform);
                var uiDynamic = pathFieldT.GetComponent<UIDynamicTextField>();
                uiDynamic.UItext.fontSize = 26;
                uiDynamic.backgroundColor = new Color(0.92f, 0.92f, 0.92f);
                var layoutElement = pathFieldT.GetComponent<LayoutElement>();
                DestroyImmediate(layoutElement);
                var rectT = pathFieldT.GetComponent<RectTransform>();
                rectT.pivot = Vector2.zero;
                rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 20, 68);
                rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 545, 650);
                _pathString.dynamicText = uiDynamic;
                uiDynamic.DisableScroll();
                pathFieldT.gameObject.SetActive(false);
            }

            {
                var infoField = CreateInfoField(_infoString);
                var rectT = infoField.GetComponent<RectTransform>();
                rectT.pivot = Vector2.zero;
                rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 85, 1056);
                rectT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 545, 650);
                var uiTextRectT = infoField.UItext.GetComponent<RectTransform>();
                var uiTextPos = uiTextRectT.anchoredPosition;
                uiTextRectT.anchoredPosition = new Vector2(uiTextPos.x, uiTextPos.y - 5);
                var uiTextSize = uiTextRectT.sizeDelta;
                uiTextRectT.sizeDelta = new Vector2(uiTextSize.x - 8, uiTextSize.y - 10);
                infoField.gameObject.SetActive(false);
            }
            {
                var parent = (RectTransform) _infoString.dynamicText.transform;
                var buttonT = (RectTransform) Instantiate(manager.configurableButtonPrefab, parent);
                var uiDynamic = buttonT.GetComponent<UIDynamicButton>();
                uiDynamic.label = "Copy to clipboard";
                uiDynamic.buttonText.fontSize = 24;
                uiDynamic.AddListener(() =>
                {
                    for(int i = 0; i < _infoSbAlt.Length; i++)
                    {
                        if(_infoSbAlt[i] == '\u00A0')
                        {
                            _infoSbAlt[i] = ' ';
                        }
                    }

                    GUIUtility.systemCopyBuffer = _pathString.val + "\n\n" + _infoSbAlt;
                });
                var layoutElement = buttonT.GetComponent<LayoutElement>();
                DestroyImmediate(layoutElement);
                buttonT.pivot = Vector2.zero;
                buttonT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Bottom, -16, 32);
                buttonT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, (parent.rect.size.x - 300) / 2, 300);
            }

            _showingUsage = true;
        }

        void CreateHeader(string text)
        {
            var uiDynamic = CreateTextField(new JSONStorableString(Guid.NewGuid().ToString().Substring(0, 4), text));
            var layoutElement = uiDynamic.GetComponent<LayoutElement>();
            layoutElement.minHeight = 56;
            layoutElement.preferredHeight = 56;
            uiDynamic.UItext.fontSize = 30;
            uiDynamic.UItext.fontStyle = FontStyle.Bold;
            uiDynamic.backgroundColor = Color.clear;
            var rectT = uiDynamic.UItext.GetComponent<RectTransform>();
            var pos = rectT.anchoredPosition;
            pos.y = -10;
            rectT.anchoredPosition = pos;
            uiDynamic.DisableScroll();
        }

        void CreateTriggerMenuButton(TriggerWrapper trigger)
        {
            if(trigger == null)
            {
                return;
            }

            var uiDynamic = CreateButton(trigger.label);
            uiDynamic.AddListener(() =>
            {
                trigger.customTrigger.OpenPanel();
                HideUsageAndInfo();
            });
            trigger.customTrigger.panelDisabledHandlers += SyncShowUsageOrInfo;
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
            trigger.UpdateButtonLabel();
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
            var buttonT = (RectTransform) Instantiate(manager.configurableButtonPrefab, UITransform);
            var uiDynamic = buttonT.GetComponent<UIDynamicButton>();
            uiDynamic.label = label;
            uiDynamic.buttonText.fontSize = 24;
            uiDynamic.AddListener(action);
            buttonT.pivot = Vector2.zero;
            buttonT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, 20, 36);
            buttonT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 445, 100);
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
            var scrollView = textFieldT.Find("Scroll View");
            var scrollRect = scrollView.GetComponent<ScrollRect>();
            scrollRect.horizontal = true;
            scrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            jss.dynamicText = uiDynamic;
            return uiDynamic;
        }

        bool _showingUsage;

        void ShowUsage()
        {
            if(!_uiCreated)
            {
                return;
            }

            _showingUsage = true;
            _usageButton.gameObject.SetActive(false);
            if(_metaRead)
            {
                _backButton.gameObject.SetActive(true);
            }

            _pathString.dynamicText.gameObject.SetActive(false);
            _infoString.dynamicText.gameObject.SetActive(false);
            _usageString.dynamicText.gameObject.SetActive(true);
        }

        void ShowInfo()
        {
            if(!_uiCreated)
            {
                return;
            }

            _showingUsage = false;
            _usageButton.gameObject.SetActive(true);
            _backButton.gameObject.SetActive(false);
            _usageString.dynamicText.gameObject.SetActive(false);
            _pathString.dynamicText.gameObject.SetActive(true);
            _infoString.dynamicText.gameObject.SetActive(true);
        }

        void SyncShowUsageOrInfo()
        {
            if(_showingUsage)
            {
                ShowUsage();
            }
            else
            {
                ShowInfo();
            }
        }

        void HideUsageAndInfo()
        {
            _usageButton.gameObject.SetActive(false);
            _backButton.gameObject.SetActive(false);
            _usageString.dynamicText.gameObject.SetActive(false);
            _pathString.dynamicText.gameObject.SetActive(false);
            _infoString.dynamicText.gameObject.SetActive(false);
        }

        void FindDependenciesCallback(bool rescan = true)
        {
            if(_metaJson == null)
            {
                _metaRead = false;
                return;
            }

            if(_downloadCo != null)
            {
                StopCoroutine(_downloadCo);
            }

            _pending = false;
            _finished = false;
            _forceStopped = false;
            _downloadErrorsSb.Clear();
            SetProgress(0);
            ShowInfo();

            if(rescan && _rescanPackagesOnSelectBool.val)
            {
                _infoString.val = "Rescanning packages...\n(disable this behavior via trigger)";
                SuperController.singleton.RescanPackages();
                _infoString.val = "";
            }

            _packages.Clear();
            _disabledPackages.Clear();
            _versionErrorPackages.Clear();
            _missingVamBundledPackages.Clear();
            _missingPackages.Clear();
            _updateRequiredPackages.Clear();
            _installedPackages.Clear();
            foreach(var pair in _triggers)
            {
                pair.Value.ResetText();
            }

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

            if(!_isLatestVam)
            {
                _ifVamNotLatestTrigger?.Trigger();
            }

            if(_packages.Count == 0)
            {
                _finished = true;
            }
            else
            {
                OnDependenciesFound();
            }

            UpdateInfo();
            _metaRead = true;

            if(_pending && _autoDownloadIfPendingBool.val)
            {
                DownloadMissingCallback();
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
                    _packages.Add(new PackageObj(trimmed, parts, depth > 0, _vamBundledPackageNames));
                }

                if(recursive)
                {
                    FindDependenciesRecursive(dependenciesJc[key].AsObject, true, depth + 1);
                }
            }
        }

        void OnDependenciesFound()
        {
            try
            {
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

                // populate lists
                {
                    foreach(var obj in _packages)
                    {
                        if(obj.versionError != null) _versionErrorPackages.Add(obj);
                        else if(obj.disabled) _disabledPackages.Add(obj);
                        else if(!obj.exists && obj.isVamBundled) _missingVamBundledPackages.Add(obj);
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

                if(_missingVamBundledPackages.Count > 0)
                {
                    TriggerAndSendText(_ifVamBundledPackagesMissingTrigger, _missingVamBundledPackages);
                }

                if(_versionErrorPackages.Count > 0 && !_uiListener.active)
                {
                    logBuilder.Error($"Version errors in meta.json, see plugin UI on atom {containingAtom.uid} (Keybindings: AutoGetDependencies.OpenUI)");
                }

                if(_disabledPackages.Count > 0)
                {
                    TriggerAndSendText(_ifDisabledPackagesDetectedTrigger, _disabledPackages);
                }

                if(_missingPackages.Count > 0 || _updateRequiredPackages.Count > 0)
                {
                    if(Application.internetReachability == NetworkReachability.NotReachable)
                    {
                        _ifNoInternetConnectionTrigger.TriggerA();
                    }
                    else
                    {
                        if(_missingPackages.Count > 0)
                        {
                            _ifDownloadPendingTrigger.TriggerA();
                        }
                        else if(_updateRequiredPackages.Count > 0)
                        {
                            _ifDownloadPendingTrigger.TriggerB();
                        }

                        _ifDownloadPendingTrigger.SendText(() =>
                        {
                            var sb = new StringBuilder();
                            AppendPackagesInfoForUIText(sb, "Missing, download required:", _missingPackages);
                            AppendPackagesInfoForUIText(sb, "Installed, check for update required:", _updateRequiredPackages);
                            AppendPackagesInfoForUIText(sb, "Installed, no update required:", _installedPackages);
                            return sb.ToString();
                        });
                    }

                    _pending = true;
                }
                else
                {
                    if(_packages.TrueForAll(obj => obj.existsAndIsValid))
                    {
                        TriggerAndSendText(_ifAllDependenciesInstalledTrigger, _packages);
                    }

                    _finished = true;
                }
            }
            catch(Exception e)
            {
                logBuilder.Exception(e);
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

        static void TriggerAndSendText(TriggerWrapper trigger, List<PackageObj> packages)
        {
            trigger.Trigger();
            trigger.SendText(packages.Select(obj => obj.name).ToPrettyString());
        }

        readonly StringBuilder _infoSb = new StringBuilder();
        readonly StringBuilder _infoSbAlt = new StringBuilder();

        void UpdateInfo()
        {
            if(!_uiCreated || !(_pending || _finished))
            {
                return;
            }

            ShowInfo();
            if(_pending) _infoString.dynamicText.UItext.horizontalOverflow = HorizontalWrapMode.Overflow;
            else if(_finished) _infoString.dynamicText.UItext.horizontalOverflow = HorizontalWrapMode.Wrap;

            _infoSb.Clear();
            _infoSbAlt.Clear();

            if(_packages.Count == 0)
            {
                _infoSb.Append("Package has no dependencies.\n\n");
                _infoSbAlt.Append("Package has no dependencies.\n\n");
            }
            else if(_packages.TrueForAll(obj => obj.existsAndIsValid) && (_finished || _updateRequiredPackages.Count == 0))
            {
                AppendPackagesInfo(_infoSb, _infoSbAlt, "All dependencies are installed!", _okColor, _packages);
            }
            else
            {
                if(_forceStopped)
                {
                    _infoSb.Append("<color=#FF0000><b>Downloading was interrupted.</b></color>\n\n");
                }

                if(!_isLatestVam && !_ifVamNotLatestTrigger.customTrigger.IsEmpty())
                {
                    _infoSb.Append("VAM is not in the latest version (>= v1.22).\n\n");
                    _infoSbAlt.Append("VAM is not in the latest version (>= v1.22).\n\n");
                }

                if(_missingVamBundledPackages.Count > 0)
                {
                    AppendPackagesInfo(_infoSb, _infoSbAlt, "Missing VAM bundled packages:", _errorColor, _missingVamBundledPackages);
                }

                if(_versionErrorPackages.Count > 0)
                {
                    AppendPackagesInfo(_infoSb, _infoSbAlt, "Version error in meta.json:", _errorColor, _versionErrorPackages);
                }

                if(_disabledPackages.Count > 0)
                {
                    AppendPackagesInfo(_infoSb, _infoSbAlt, "Disabled:", _errorColor, _disabledPackages);
                }

                if(_pending)
                {
                    if(Application.internetReachability == NetworkReachability.NotReachable)
                    {
                        _infoSb.AppendFormat("<color=#{0}>No internet connection!</color>\n\n", _errorColor);
                        _infoSbAlt.Append("No internet connection!\n\n");
                    }
                    else
                    {
                        AppendPackagesInfo(_infoSb, _infoSbAlt, "Missing, download required:", _errorColor, _missingPackages);
                        AppendPackagesInfo(_infoSb, _infoSbAlt, "Installed, check for update required:", _updateRequiredColor, _updateRequiredPackages);
                        AppendPackagesInfo(_infoSb, _infoSbAlt, "Installed, no update required:", _okColor, _installedPackages);
                    }
                }
                else if(_finished)
                {
                    if(_notOnHubPackages.Count > 0)
                    {
                        AppendPackagesInfo(_infoSb, _infoSbAlt, "Packages not on Hub:", _errorColor, _notOnHubPackages);
                    }
                    if(_downloadErrorsSb.Length > 0)
                    {
                        _infoSb.AppendFormat("<size=30><color=#{0}><b>Errors during download:</b></color></size>\n\n", _errorColor);
                        _infoSb.Append(_downloadErrorsSb);
                        _infoSb.Append("\n\n");
                        _infoSbAlt.Append("Errors during download:\n\n");
                        _infoSbAlt.Append(_downloadErrorsSb);
                        _infoSbAlt.Append("\n\n");
                    }
                    AppendPackagesInfo(_infoSb, _infoSbAlt, "Installed:", _okColor, _packages.Where(obj => obj.existsAndIsValid).ToList());
                }
            }

            try
            {
                if(_infoSb.Length > 16000)
                {
                    if(_infoSbAlt.Length > 16000)
                    {
                        const string truncated = "\n\n(too long, truncated)\n\n";
                        _infoSbAlt.Length = 16000 - truncated.Length;
                        _infoSbAlt.Append(truncated);
                    }

                    _infoString.val = _infoSbAlt.ToString();
                }
                else
                {
                    _infoString.val = _infoSb.ToString();
                }
            }
            catch(Exception e)
            {
                logBuilder.Exception(e);
            }

            _usageButton.gameObject.SetActive(true);
        }

        static void AppendPackagesInfo(StringBuilder sb, StringBuilder sbAlt, string title, string titleColor, List<PackageObj> packages)
        {
            sb.AppendFormat("<size=30><color=#{0}><b>{1}</b></color></size>\n\n", titleColor, title);
            sbAlt.AppendFormat("{0}\n\n", title);
            if(packages.Count == 0)
            {
                sb.Append("None.\n\n");
                sbAlt.Append("None.\n\n");
                return;
            }

            foreach(var obj in packages)
            {
                if(obj.isSubDependency)
                {
                    sb.AppendFormat("<color=#{0}>-\u00A0{1}</color>\n", _subDependencyColor, obj.name);
                }
                else
                {
                    sb.AppendFormat("-\u00A0{0}\n", obj.name);
                }

                sbAlt.AppendFormat("-\u00A0{0}\n", obj.name);
            }

            sb.Append("\n");
            sbAlt.Append("\n");
        }

        static void AppendPackagesInfoForUIText(StringBuilder sb, string title, List<PackageObj> packages)
        {
            sb.AppendFormat("{0}\n\n", title);
            if(packages.Count == 0)
            {
                sb.Append("None.\n\n");
                return;
            }

            foreach(var obj in packages)
            {
                sb.AppendFormat("{0}\n", obj.name);
            }
            sb.Append("\n");
        }

        void OnMetaJsonSelected(JSONStorableUrl url)
        {
            _metaRead = false;
            _metaJson = null;

            try
            {
                string path = url.val;
                if(string.IsNullOrEmpty(path))
                {
                    url.val = "";
                    return;
                }

                _pathString.val = path;
                if(!path.EndsWith("meta.json"))
                {
                    ShowInfo();
                    _infoString.val = $"<color=#{_errorColor}>Selected file is not a meta.json file.</color>";
                    if(!_uiListener.active)
                    {
                        logBuilder.Error("Selected file is not a meta.json file");
                    }

                    return;
                }

                _metaJson = SuperController.singleton.LoadJSON(path)?.AsObject;
                url.val = "";
                FindDependenciesCallback();
            }
            catch(Exception e)
            {
                logBuilder.Exception(e);
            }
        }

        public void RecheckInternetConnection()
        {
            if(Application.internetReachability == NetworkReachability.NotReachable)
            {
                if(logErrorsBool.val) logBuilder.Error("No internet connection");
                return;
            }

            _ifNoInternetConnectionTrigger.TriggerB();
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
                new JSONStorableAction("SelectMetaJson", () => _selectMetaJsonAction.actionCallback()),
                new JSONStorableAction("ScanLoadedSceneMetaJson", () => _scanLoadedSceneMetaJson.actionCallback()),
                new JSONStorableAction("DownloadMissing", () => _downloadAction.actionCallback()),
                new JSONStorableAction("StopDownload", () => _stopDownloadAction.actionCallback()),
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

        TriggerWrapper AddTrigger(string name, string label, bool enableSendText = true)
        {
            var trigger = _triggers[name] = new TriggerWrapper(this, name, label);
            if(enableSendText)
            {
                EnableSendText(trigger, 62f);
            }

            return trigger;
        }

        TriggerWrapper AddTrigger(string name, string label, string eventAName, string eventBName, bool enableSendText = true)
        {
            var trigger = _triggers[name] = new TriggerWrapper(this, name, label, eventAName, eventBName);
            if(enableSendText)
            {
                EnableSendText(trigger, 2f);
            }

            return trigger;
        }

        void EnableSendText(TriggerWrapper trigger, float popupTopInset)
        {
            {
                var chooser = new JSONStorableStringChooser($"{trigger.customTrigger.name} UIText", new List<string>(), "", "Send To UIText");
                chooser.setCallbackFunction = option => trigger.SelectUITextCallback(option, _uiTexts);
                chooser.representsAtomUid = true;
                RegisterStringChooser(chooser);
                trigger.uiTextChooser = chooser;
            }

            trigger.customTrigger.onInitPanel += triggerActionsPanel =>
            {
                var popupT = (RectTransform) Instantiate(manager.configurableScrollablePopupPrefab, triggerActionsPanel);
                popupT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, popupTopInset, 120f);
                popupT.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Right, 15f, 545f);
                var uiDynamic = popupT.GetComponent<UIDynamicPopup>();
                uiDynamic.height = 100f;
                uiDynamic.labelTextColor = Color.white;
                uiDynamic.popupPanelHeight = 500;
                uiDynamic.popup.selectColor = _paleBlue;
                popupT.Find("Background").GetComponent<Image>().color = Color.clear;
                trigger.customTrigger.panelDisabledHandlers += () => uiDynamic.popup.visible = false;
                trigger.uiTextChooser.popup = uiDynamic.popup;
            };

            trigger.RegisterCopyToClipboardAction();
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

            // setup callbacks
            try
            {
                foreach(var obj in pendingPackages)
                {
                    MVR.Hub.HubResourcePackage.DownloadQueuedCallback queuedCallback = _ => obj.downloadQueued = true;
                    obj.connectedItem.downloadQueuedCallback += queuedCallback;
                    obj.storeQueuedCallback = queuedCallback;

                    MVR.Hub.HubResourcePackage.DownloadStartCallback startCallback = _ => obj.downloadStarted = true;
                    obj.connectedItem.downloadStartCallback += startCallback;
                    obj.storeStartCallback = startCallback;

                    // ReSharper disable once TooWideLocalVariableScope
                    MVR.Hub.HubResourcePackage.DownloadCompleteCallback completeCallback;

                    #if VAM_GT_1_22
                    {
                        completeCallback = (_, __) => obj.downloadComplete = true;
                        obj.connectedItem.downloadCompleteCallback += completeCallback;
                        obj.storeCompleteCallback = completeCallback;
                    }
                    #else
                    {
                        completeCallback = _ => obj.downloadComplete = true;
                        obj.connectedItem.downloadCompleteCallback += completeCallback;
                        obj.storeCompleteCallback = completeCallback;
                    }
                    #endif

                    MVR.Hub.HubResourcePackage.DownloadErrorCallback errorCallback = (_, e) => obj.downloadError = e;
                    obj.connectedItem.downloadErrorCallback += errorCallback;
                    obj.storeErrorCallback = errorCallback;
                }
            }
            catch(Exception e)
            {
                OnException("Start downloads", e);
                yield break;
            }

            // start downloads and wait for downloads to complete
            var wait = new WaitForSeconds(0.1f);
            const int batchSize = 3;
            while(true)
            {
                if(_forceStopped)
                {
                    break;
                }

                try
                {
                    float sum = 0;
                    bool allDone = true;
                    int activeDownloads = 0;
                    for(int i = pendingPackages.Count - 1; i >= 0; i--)
                    {
                        if(_forceStopped)
                        {
                            break;
                        }

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

                        if(!obj.downloadQueued)
                        {
                            if(activeDownloads >= batchSize)
                            {
                                allDone = false;
                                continue;
                            }

                            obj.QueueDownload();
                        }

                        if(obj.downloadStarted)
                        {
                            var slider = obj.packageUI.progressSlider;
                            sum += Mathf.InverseLerp(slider.minValue, slider.maxValue, slider.value);
                        }

                        activeDownloads++;
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
            if(_downloadErrorsSb.Length == 0)
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
            try
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
                    TriggerAndSendText(_ifAllDependenciesInstalledTrigger, _packages);
                    if(_missingVamBundledPackages.Count > 0)
                    {
                        _ifVamBundledPackagesMissingTrigger.ResetText();
                    }

                    if(_disabledPackages.Count > 0)
                    {
                        _ifDisabledPackagesDetectedTrigger.ResetText();
                    }

                    if(_missingPackages.Count > 0 || _updateRequiredPackages.Count > 0)
                    {
                        _ifDownloadPendingTrigger.ResetText();
                    }
                }
                // failure path
                else
                {
                    if(_notOnHubPackages.Count > 0)
                    {
                        TriggerAndSendText(_ifNotOnHubPackagesDetectedTrigger, _notOnHubPackages);
                    }

                    _ifSomePackagesNotInstalledTrigger.Trigger();
                    _ifSomePackagesNotInstalledTrigger.SendText(_downloadErrorsSb.Length > 0 ? _downloadErrorsSb.ToString() : "No errors.");
                }

                _ifDownloadPendingTrigger.ResetText();
                _downloadCo = null;
                _finished = true;
                UpdateInfo();
            }
            catch(Exception e)
            {
                logBuilder.Exception(e);
            }
        }
#endregion Teardown

        public override JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false)
        {
            var jc = base.GetJSON(includePhysical, includeAppearance, forceStore);

            try
            {
                if(includePhysical || forceStore)
                {
                    needsStore = _triggers.Any(pair => !pair.Value.customTrigger.IsEmpty()) || forceStore;
                    foreach(var pair in _triggers)
                    {
                        pair.Value.StoreJSON(jc, _subScenePrefix);
                    }
                }
            }
            catch(Exception e)
            {
                logBuilder.Exception(e);
            }

            return jc;
        }

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
            if(!physicalLocked && restorePhysical && !IsCustomPhysicalParamLocked("trigger"))
            {
                foreach(var pair in _triggers)
                {
                    pair.Value.RestoreFromJSON(jc, _subScenePrefix, mergeRestore, setMissingToDefault);
                }
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
