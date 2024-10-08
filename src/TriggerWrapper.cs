using MacGruber_Utils;
using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/*
 * AutoGetDependencies v1.0
 * Licensed under CC BY https://creativecommons.org/licenses/by/4.0/
 * (c) 2024 everlaster
 * https://patreon.com/everlaster
 */
namespace everlaster
{
    sealed class TriggerWrapper
    {
        readonly AutoGetDependencies _script;
        readonly EventTrigger _eventTrigger;
        readonly DualEventTrigger _dualEventTrigger;
        public readonly CustomTrigger customTrigger;
        public readonly string label;

        TextAnchor _storeTextAlignment;
        Vector2 _storeTextPos;
        Vector2 _storeTextSize;
        UIDynamicButton _button;
        Color _defaultButtonColor;
        Coroutine _flashButtonCo;

        public UIDynamicButton button
        {
            get { return _button;}
            set { _button = value; _defaultButtonColor = value.buttonColor; }
        }

        public JSONStorableStringChooser uiTextChooser;
        public Atom sendToAtom;
        JSONStorableString _sendToString;
        Text _sendToText;
        bool _textSent;  // exists only for the demo scene

        public TriggerWrapper(AutoGetDependencies script, string name, string label)
        {
            _script = script;
            _eventTrigger = new EventTrigger(script, name);
            _eventTrigger.onCloseTriggerActionsPanel += UpdateButtonLabel;
            customTrigger = _eventTrigger;
            UpdateButtonLabel();
            this.label = label;
        }

        public TriggerWrapper(AutoGetDependencies script, string name, string label, string eventAName, string eventBName)
        {
            _script = script;
            _dualEventTrigger = new DualEventTrigger(script, name, eventAName, eventBName);
            _dualEventTrigger.onCloseTriggerActionsPanel += UpdateButtonLabel;
            customTrigger = _dualEventTrigger;
            UpdateButtonLabel();
            this.label = label;
        }

        public void RegisterOnCloseCallback(Trigger.OnCloseTriggerActionsPanel callback) =>
            customTrigger.onCloseTriggerActionsPanel += callback;

        public void RegisterCopyToClipboardAction()
        {
            var action = new JSONStorableAction($"{customTrigger.name}: Copy to clipboard", () =>
            {
                if(_sendToString != null) GUIUtility.systemCopyBuffer = _sendToString.val;
            });
            _script.RegisterAction(action);
        }

        public void UpdateButtonLabel()
        {
            if(button == null)
            {
                return;
            }

            int count = 0;

            var discreteActionsStart = customTrigger.GetDiscreteActionsStart();
            if(discreteActionsStart != null)
            {
                count += discreteActionsStart.Count;
            }

            var discreteActionsEnd = customTrigger.GetDiscreteActionsEnd();
            if(discreteActionsEnd != null)
            {
                count += discreteActionsEnd.Count;
            }

            button.label = label + (count > 0 ? $" ({count})" : "");
        }

        public void SelectUITextCallback(string option, List<Atom> uiTexts)
        {
            try
            {
                sendToAtom = null;
                _sendToString = null;
                if(_sendToText != null)
                {
                    RestoreUIText();
                }
                _sendToText = null;

                if (!string.IsNullOrEmpty(option))
                {
                    var uiTextAtom = uiTexts.Find(atom => atom.uid == option);
                    if (uiTextAtom == null)
                    {
                        _script.logBuilder.Error($"UIText '{option}' not found");
                        uiTextChooser.valNoCallback = sendToAtom != null ? sendToAtom.uid ?? "" : "";
                        return;
                    }

                    _sendToString = uiTextAtom.GetStorableByID("Text").GetStringJSONParam("text");
                    _sendToText = uiTextAtom.reParentObject.transform.Find("object/rescaleObject/Canvas/Holder/Text").GetComponent<Text>();
                    ConfigureUIText();
                    sendToAtom = uiTextAtom;
                }
            }
            catch (Exception e)
            {
                _script.logBuilder.Exception(e);
            }
        }

        void ConfigureUIText()
        {
            _storeTextAlignment = _sendToText.alignment;
            _sendToText.alignment = TextAnchor.UpperLeft;
            var rectT = _sendToText.rectTransform;
            var pos = rectT.anchoredPosition;
            var size = rectT.sizeDelta;
            _storeTextPos = pos;
            _storeTextSize = size;
            pos.x += 10;
            pos.y -= 10;
            size.x -= 20;
            size.y -= 20;
            rectT.anchoredPosition = pos;
            rectT.sizeDelta = size;
        }

        void RestoreUIText()
        {
            _sendToText.alignment = _storeTextAlignment;
            var rectT = _sendToText.rectTransform;
            rectT.anchoredPosition = _storeTextPos;
            rectT.sizeDelta = _storeTextSize;
        }

        public void Trigger()
        {
            try
            {
                if(_eventTrigger == null)
                {
                    _script.logBuilder.Debug("Trigger: eventTrigger is null");
                    return;
                }

                if(ValidateTrigger())
                {
                    _eventTrigger.Trigger();
                    FlashButton();
                }
            }
            catch(Exception e)
            {
                _script.logBuilder.Exception(e);
            }
        }

        public void TriggerA()
        {
            try
            {
                if(_dualEventTrigger == null)
                {
                    _script.logBuilder.Debug("TriggerA: dualEventTrigger is null");
                    return;
                }

                if(ValidateTriggerA())
                {
                    _dualEventTrigger.TriggerA();
                    FlashButton();
                }
            }
            catch(Exception e)
            {
                _script.logBuilder.Exception(e);
            }
        }

        public void TriggerB()
        {
            try
            {
                if(_dualEventTrigger == null)
                {
                    _script.logBuilder.Debug("TriggerB: dualEventTrigger is null");
                    return;
                }

                if(ValidateTriggerB())
                {
                    _dualEventTrigger.TriggerB();
                    FlashButton();
                }
            }
            catch(Exception e)
            {
                _script.logBuilder.Exception(e);
            }
        }

        void FlashButton()
        {
            if(button != null)
            {
                if(_flashButtonCo != null)
                {
                    _script.StopCoroutine(_flashButtonCo);
                }

                _flashButtonCo = _script.StartCoroutine(FlashButtonCo());
            }
        }

        IEnumerator FlashButtonCo()
        {
            button.buttonColor = Color.green;
            yield return new WaitForSeconds(0.50f);
            const float duration = 1.0f;
            float elapsed = 0f;
            while(elapsed < duration)
            {
                button.buttonColor = Color.Lerp(Color.green, _defaultButtonColor, elapsed / duration);
                elapsed += Time.deltaTime;
                yield return null;
            }

            button.buttonColor = _defaultButtonColor;
            _flashButtonCo = null;
        }

        bool ValidateTrigger()
        {
            string error = DiscreteActionsValidationResult(_eventTrigger.GetDiscreteActionsStart());
            if(error != null && _script.logErrorsBool.val)
            {
                _script.logBuilder.Error($"{_eventTrigger.name}: {error}");
                return false;
            }

            return true;
        }

        bool ValidateTriggerA()
        {
            string error = DiscreteActionsValidationResult(_dualEventTrigger.GetDiscreteActionsStart());
            if(error != null && _script.logErrorsBool.val)
            {
                _script.logBuilder.Error($"{_dualEventTrigger.name}: {error}");
                return false;
            }

            return true;
        }

        bool ValidateTriggerB()
        {
            string error = DiscreteActionsValidationResult(_dualEventTrigger.GetDiscreteActionsEnd());
            if(error != null && _script.logErrorsBool.val)
            {
                _script.logBuilder.Error($"{_dualEventTrigger.name}: {error}");
                return false;
            }

            return true;
        }

        static string DiscreteActionsValidationResult(List<TriggerActionDiscrete> actions)
        {
            if(actions == null)
            {
                return null;
            }

            string error = null;
            foreach(var action in actions)
            {
                if(action.receiverAtom == null)
                {
                    error = $"Action '{action.name}' receiverAtom was null";
                    break;
                }

                var atom = SuperController.singleton.GetAtomByUid(action.receiverAtom.uid);
                if(atom == null)
                {
                    error = $"Action '{action.name}' receiverAtom '{action.receiverAtom.uid}' not found in scene";
                    break;
                }

                if(action.receiver == null)
                {
                    error = $"Action '{action.name}' receiver was null";
                    break;
                }

                var storable = atom.GetStorableByID(action.receiver.storeId);
                if(storable == null)
                {
                    error = $"Action '{action.name}' receiver '{action.receiver.storeId}' not found on atom '{atom.name}'";
                    break;
                }
            }

            return error;
        }

        public void SendText(string val)
        {
            _sendToString?.SetVal(val);
            _textSent = true;
        }

        public void SendText(Func<string> buildString)
        {
            _sendToString?.SetVal(buildString());
            _textSent = true;
        }

        public void ResetText()
        {
            if(_textSent)
            {
                _sendToString?.SetVal("");
            }
        }

        public void RestoreFromJSON(JSONClass jc, string subscenePrefix, bool mergeRestore, bool setMissingToDefault = true)
        {
            try
            {
                customTrigger.RestoreFromJSON(jc, subscenePrefix, mergeRestore, setMissingToDefault);
                if(_script.logErrorsBool.val)
                {
                    if(_eventTrigger != null)
                    {
                        ValidateTrigger();
                    }
                    else if(_dualEventTrigger != null)
                    {
                        ValidateTriggerA();
                        ValidateTriggerB();
                    }
                }

                UpdateButtonLabel();
            }
            catch(Exception e)
            {
                _script.logBuilder.Exception(e);
            }
        }

        public void Destroy()
        {
            customTrigger.OnRemove();
            if(_sendToText != null)
            {
                RestoreUIText();
            }
        }

        public void StoreJSON(JSONClass jc, string subScenePrefix)
        {
            if(customTrigger.HasActions())
            {
                jc[customTrigger.name] = customTrigger.GetJSON(subScenePrefix);
            }
        }
    }
}
