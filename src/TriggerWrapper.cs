﻿using MacGruber;
using SimpleJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace everlaster
{
    sealed class TriggerWrapper
    {
        readonly AutoGetDependencies _script;
        public readonly EventTrigger eventTrigger;
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
            eventTrigger = new EventTrigger(script, name);
            eventTrigger.onCloseTriggerActionsPanel += UpdateButton;
            UpdateButton();
            this.label = label;
        }

        public void RegisterOnCloseCallback(Trigger.OnCloseTriggerActionsPanel callback) => eventTrigger.onCloseTriggerActionsPanel += callback;

        public void RegisterCopyToClipboardAction()
        {
            var action = new JSONStorableAction($"{eventTrigger.Name}: Copy to clipboard", () =>
            {
                if(_sendToString != null) GUIUtility.systemCopyBuffer = _sendToString.val;
            });
            _script.RegisterAction(action);
        }

        public void UpdateButton()
        {
            if(button == null)
            {
                return;
            }

            string newLabel = label;
            int count = eventTrigger.GetDiscreteActionsStart().Count;
            if(count > 0)
            {
                newLabel += $" <b>({count})</b>";
            }

            button.label = newLabel;
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
                if(ValidateTrigger(eventTrigger))
                {
                    eventTrigger.Trigger();
                    if(button != null)
                    {
                        if(_flashButtonCo != null)
                        {
                            _script.StopCoroutine(_flashButtonCo);
                        }

                        _flashButtonCo = _script.StartCoroutine(FlashButton());
                    }
                }
            }
            catch(Exception e)
            {
                _script.logBuilder.Exception(e);
            }
        }

        IEnumerator FlashButton()
        {
            button.buttonColor = Color.green;
            yield return new WaitForSeconds(0.50f);
            const float duration = 1.0f;
            float elapsed = 0f;
            while(elapsed < duration)
            {
                button.buttonColor = Color.Lerp(Color.green, _defaultButtonColor, elapsed / duration);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            button.buttonColor = _defaultButtonColor;
            _flashButtonCo = null;
        }

        bool ValidateTrigger(EventTrigger trigger)
        {
            string error = null;
            foreach(var action in trigger.GetDiscreteActionsStart())
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

            if(error != null && _script.logErrorsBool.val)
            {
                _script.logBuilder.Error($"{trigger.Name}: receiverAtom was null");
                return false;
            }

            return true;
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
                if(jc.HasKey(eventTrigger.Name))
                {
                    eventTrigger.RestoreFromJSON(jc[eventTrigger.Name].AsObject, subscenePrefix, mergeRestore);
                }
                else if(setMissingToDefault)
                {
                    eventTrigger.RestoreFromJSON(new JSONClass());
                }

                UpdateButton();
            }
            catch(Exception e)
            {
                _script.logBuilder.Exception(e);
            }
        }

        public void Destroy()
        {
            eventTrigger.OnRemove();
            if(_sendToText != null)
            {
                RestoreUIText();
            }
        }
    }
}
