using MacGruber;
using SimpleJSON;
using System;
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
        public UIDynamicButton button;

        public JSONStorableStringChooser uiTextChooser;
        public Atom sendToAtom;
        public Text sendToText;

        public TriggerWrapper(AutoGetDependencies script, string name, string label)
        {
            _script = script;
            eventTrigger = new EventTrigger(script, name);
            eventTrigger.onCloseTriggerActionsPanel += UpdateButton;
            UpdateButton();
            this.label = label;
        }

        public void RegisterOnCloseCallback(Trigger.OnCloseTriggerActionsPanel callback) => eventTrigger.onCloseTriggerActionsPanel += callback;

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
                sendToText = null;

                if (!string.IsNullOrEmpty(option))
                {
                    var uiTextObj = uiTexts.Find(atom => atom.uid == option);
                    if (uiTextObj == null)
                    {
                        _script.logBuilder.Error($"UIText '{option}' not found");
                        uiTextChooser.valNoCallback = sendToAtom != null ? sendToAtom.uid ?? "" : "";
                        return;
                    }

                    sendToText = uiTextObj.reParentObject.transform.Find("object/rescaleObject/Canvas/Holder/Text").GetComponent<Text>();
                    sendToAtom = uiTextObj;
                }
            }
            catch (Exception e)
            {
                _script.logBuilder.Exception(e);
            }
        }

        public void Trigger()
        {
            try
            {
                if(ValidateTrigger(eventTrigger))
                {
                    Debug.Log($"Triggering {eventTrigger.Name}");
                    eventTrigger.Trigger();
                }
            }
            catch(Exception e)
            {
                _script.logBuilder.Exception(e);
            }
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
    }
}
