using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenFo3.ESM;

namespace OpenFo3.Game
{
    public class QuestState
    {
        public uint FormId;
        public string Name;
        public string Description;
        public bool IsActive;
        public bool IsCompleted;
        public int CurrentStage;
        public int MaxStages;
        public List<QuestObjective> Objectives = new();
        public List<int> CompletedStages = new();
    }

    public class QuestObjective
    {
        public int Index;
        public string Text;
        public bool IsCompleted;
        public string Target;
        public Vector3? TargetPosition;
    }

    public partial class QuestManager : Node
    {
        private ESMReader _esm;
        private Dictionary<uint, QuestState> _quests = new();
        private uint _activeQuestFormId;

        [Signal]
        public delegate void QuestStartedEventHandler(uint questFormId, string name);

        [Signal]
        public delegate void QuestCompletedEventHandler(uint questFormId, string name);

        [Signal]
        public delegate void QuestStageChangedEventHandler(uint questFormId, int stage);

        [Signal]
        public delegate void QuestObjectiveUpdatedEventHandler(uint questFormId, int objectiveIndex, string text);

        public IReadOnlyDictionary<uint, QuestState> AllQuests => _quests;
        public QuestState ActiveQuest => _activeQuestFormId != 0 && _quests.TryGetValue(_activeQuestFormId, out var q) ? q : null;

        public QuestManager(ESMReader esm)
        {
            _esm = esm;
        }

        public void RegisterQuest(uint formId, string name, string description)
        {
            if (_quests.ContainsKey(formId)) return;

            _quests[formId] = new QuestState
            {
                FormId = formId,
                Name = name,
                Description = description,
                IsActive = false,
                IsCompleted = false,
                CurrentStage = 0,
            };
        }

        public void StartQuest(uint formId)
        {
            if (!_quests.TryGetValue(formId, out var quest)) return;
            if (quest.IsCompleted) return;

            quest.IsActive = true;
            quest.CurrentStage = 1;
            _activeQuestFormId = formId;
            quest.CompletedStages.Clear();

            EmitSignal(nameof(QuestStartedEventHandler), formId, quest.Name);
        }

        public void AdvanceQuestStage(uint formId, int stage)
        {
            if (!_quests.TryGetValue(formId, out var quest)) return;
            if (!quest.IsActive) return;

            quest.CurrentStage = stage;
            quest.CompletedStages.Add(stage);

            EmitSignal(nameof(QuestStageChangedEventHandler), formId, stage);

            foreach (var obj in quest.Objectives)
            {
                if (!obj.IsCompleted)
                {
                    SetObjective(formId, obj.Index, obj.Text);
                    break;
                }
            }
        }

        public void SetObjective(uint questFormId, int objectiveIndex, string text)
        {
            if (!_quests.TryGetValue(questFormId, out var quest)) return;

            var obj = quest.Objectives.FirstOrDefault(o => o.Index == objectiveIndex);
            if (obj == null)
            {
                obj = new QuestObjective { Index = objectiveIndex, Text = text };
                quest.Objectives.Add(obj);
            }
            obj.Text = text;

            EmitSignal(nameof(QuestObjectiveUpdatedEventHandler), questFormId, objectiveIndex, text);
        }

        public void CompleteObjective(uint questFormId, int objectiveIndex)
        {
            if (!_quests.TryGetValue(questFormId, out var quest)) return;

            var obj = quest.Objectives.FirstOrDefault(o => o.Index == objectiveIndex);
            if (obj != null)
                obj.IsCompleted = true;
        }

        public void CompleteQuest(uint formId)
        {
            if (!_quests.TryGetValue(formId, out var quest)) return;

            quest.IsActive = false;
            quest.IsCompleted = true;
            quest.CurrentStage = -1;

            _activeQuestFormId = 0;

            EmitSignal(nameof(QuestCompletedEventHandler), formId, quest.Name);
        }

        public bool IsQuestActive(uint formId)
        {
            return _quests.TryGetValue(formId, out var q) && q.IsActive;
        }

        public bool IsQuestCompleted(uint formId)
        {
            return _quests.TryGetValue(formId, out var q) && q.IsCompleted;
        }

        public int GetQuestStage(uint formId)
        {
            return _quests.TryGetValue(formId, out var q) ? q.CurrentStage : 0;
        }

        public QuestState GetQuest(uint formId)
        {
            return _quests.TryGetValue(formId, out var q) ? q : null;
        }

        public List<QuestState> GetActiveQuests()
        {
            return _quests.Values.Where(q => q.IsActive && !q.IsCompleted).ToList();
        }

        public List<QuestState> GetCompletedQuests()
        {
            return _quests.Values.Where(q => q.IsCompleted).ToList();
        }

        public void RegisterFallout3Quests()
        {
            RegisterQuest(0x00000001, "Escape!", "Escape from Vault 101 following the overseer's instructions.");
            RegisterQuest(0x00000002, "Following in His Footsteps", "Track down your father in the Capital Wasteland.");
            RegisterQuest(0x00000003, "Galaxy News Radio", "Help Three Dog broadcast across the wasteland.");
            RegisterQuest(0x00000004, "Scientific Pursuits", "Follow the lead to Rivet City and Dr. Madison Li.");
            RegisterQuest(0x00000005, "Tranquility Lane", "Enter the Tranquility Lane simulation to extract information.");
            RegisterQuest(0x00000006, "The Waters of Life", "Help Project Purity and learn your father's true goal.");
            RegisterQuest(0x00000007, "Picking Up the Trail", "Track the Enclave to find your father.");
            RegisterQuest(0x00000008, "The American Dream", "Infiltrate the Enclave base at Raven Rock.");
            RegisterQuest(0x00000009, "Take It Back!", "Activate Project Purity and defeat the Enclave.");
        }
    }
}
