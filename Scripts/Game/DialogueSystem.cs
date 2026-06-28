using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenFo3.ESM;

namespace OpenFo3.Game
{
    public class DialogueNode
    {
        public string Text;
        public string SpeakerName;
        public uint InfoFormId;
        public List<DialogueResponse> Responses = new();
        public DialogueCondition Condition;
        public string SoundFile;
        public bool IsPlayerResponse;
        public uint QuestFormId;
        public int QuestStage;
    }

    public class DialogueResponse
    {
        public string Text;
        public uint NextInfoFormId;
        public DialogueCondition Condition;
        public bool IsGoodbye;
        public bool IsSayOnce;
    }

    public class DialogueCondition
    {
        public SkillName? RequiredSkill;
        public int MinSkillValue;
        public SpecialAttribute? RequiredAttribute;
        public int MinAttributeValue;
        public uint? RequiredQuestFormId;
        public int RequiredQuestStage;
        public bool Invert;
    }

    public partial class DialogueSystem : Node
    {
        private ESMReader _esm;
        private uint _currentInfoFormId;
        private DialogueNode _currentNode;
        private bool _inDialogue;

        [Signal]
        public delegate void DialogueStartedEventHandler(string speakerName, string text);

        [Signal]
        public delegate void DialogueResponseAvailableEventHandler(string[] responses);

        [Signal]
        public delegate void DialogueEndedEventHandler();

        [Signal]
        public delegate void QuestStageUpdatedEventHandler(uint questFormId, int stage);

        public bool InDialogue => _inDialogue;

        public DialogueSystem(ESMReader esm)
        {
            _esm = esm;
        }

        public void StartDialogue(uint dialogFormId, uint speakerFormId)
        {
            _inDialogue = true;
            var dialogRecords = FindDialogRecords(dialogFormId);
            if (dialogRecords.Count == 0)
            {
                _inDialogue = false;
                return;
            }

            foreach (var dialog in dialogRecords)
            {
                var firstInfo = FindFirstAvailableInfo(dialog);
                if (firstInfo != null)
                {
                    _currentInfoFormId = firstInfo.InfoFormId;
                    PresentNode(firstInfo);
                    return;
                }
            }

            var fallback = FindFirstInfo(dialogRecords[0].FirstInfoFormId);
            if (fallback != null)
            {
                _currentInfoFormId = fallback.InfoFormId;
                PresentNode(fallback);
            }
            else
            {
                _inDialogue = false;
            }
        }

        private void PresentNode(DialogueNode node)
        {
            _currentNode = node;
            EmitSignal(nameof(DialogueStartedEventHandler), node.SpeakerName, node.Text);

            var validResponses = new List<string>();
            foreach (var response in node.Responses)
            {
                if (CheckCondition(response.Condition))
                    validResponses.Add(response.Text);
            }

            EmitSignal(nameof(DialogueResponseAvailableEventHandler), validResponses.ToArray());
        }

        public void SelectResponse(int index)
        {
            if (_currentNode == null || index < 0 || index >= _currentNode.Responses.Count) return;

            var response = _currentNode.Responses[index];
            if (!CheckCondition(response.Condition))
            {
                EmitSignal(nameof(DialogueEndedEventHandler));
                _inDialogue = false;
                return;
            }

            if (_currentNode.QuestFormId != 0 && _currentNode.QuestStage > 0)
            {
                EmitSignal(nameof(QuestStageUpdatedEventHandler), _currentNode.QuestFormId, _currentNode.QuestStage);
            }

            if (response.IsGoodbye)
            {
                EmitSignal(nameof(DialogueEndedEventHandler));
                _inDialogue = false;
                return;
            }

            if (response.NextInfoFormId != 0)
            {
                var nextNode = BuildDialogueNode(response.NextInfoFormId);
                if (nextNode != null)
                {
                    PresentNode(nextNode);
                    return;
                }
            }

            EmitSignal(nameof(DialogueEndedEventHandler));
            _inDialogue = false;
        }

        public void EndDialogue()
        {
            _inDialogue = false;
            _currentNode = null;
            EmitSignal(nameof(DialogueEndedEventHandler));
        }

        private List<RecordParsers.DialogData> FindDialogRecords(uint rootFormId)
        {
            return new List<RecordParsers.DialogData>();
        }

        private DialogueNode FindFirstAvailableInfo(RecordParsers.DialogData dialog)
        {
            return null;
        }

        private DialogueNode FindFirstInfo(uint dialogFormId)
        {
            return new DialogueNode
            {
                Text = "Hello. I don't have much to say.",
                SpeakerName = "NPC",
                InfoFormId = 0,
                Responses = new List<DialogueResponse>
                {
                    new() { Text = "Goodbye.", IsGoodbye = true }
                }
            };
        }

        private DialogueNode BuildDialogueNode(uint infoFormId)
        {
            return new DialogueNode
            {
                Text = "...",
                SpeakerName = "NPC",
                InfoFormId = infoFormId,
                Responses = new List<DialogueResponse>
                {
                    new() { Text = "Goodbye.", IsGoodbye = true }
                }
            };
        }

        private bool CheckCondition(DialogueCondition condition)
        {
            if (condition == null) return true;
            return true;
        }
    }
}
