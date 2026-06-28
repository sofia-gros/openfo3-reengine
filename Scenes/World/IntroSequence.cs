using Godot;
using System;
using System.Collections.Generic;
using OpenFo3.Player;
using OpenFo3.UI;
using OpenFo3.World;

public partial class IntroSequence : Node3D
{
	[Signal]
	public delegate void IntroCompleteEventHandler(string playerName, int[] specialValues, bool isMale);

	private ChildController _child;
	private CharacterCreation _creationUI;
	private Node3D _vaultScene;
	private CanvasLayer _uiLayer;
	private Label _subtitleLabel;
	private Label _instructionLabel;
	private Label _dialogueLabel;
	private ColorRect _blackOverlay;
	private ColorRect _alarmOverlay;

	// Dialogue queue for conversations
	private Queue<string> _dialogueQueue = new();
	private bool _inDialogue;
	private Action _onDialogueEnd;

	// Event-driven phases
	private enum Phase { Creation, Birthday, Childhood, GoatTest, Escape, Wasteland, Complete }
	private Phase _currentPhase = Phase.Creation;
	private bool _dadTalked;
	private bool _amataTalked;
	private bool _goatComplete;
	private bool _escapeStarted;
	private bool _alarmActive;

	private string _playerName = "Lone Wanderer";
	private int[] _specialValues = { 5, 5, 5, 5, 5, 5, 5 };
	private bool _isMale = true;

	// Interactables
	private Interactable _dadInteractable;
	private Interactable _amataInteractable;
	private Interactable _goatTerminal;
	private Interactable _escapeDoor;
	private Interactable _guest1Interactable;
	private Interactable _guest2Interactable;

	public override void _Ready()
	{
		_uiLayer = new CanvasLayer();
		_uiLayer.Name = "IntroUILayer";
		_uiLayer.Layer = 350;
		AddChild(_uiLayer);

		_blackOverlay = new ColorRect();
		_blackOverlay.Color = new Color(0, 0, 0, 1);
		_blackOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
		_blackOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_uiLayer.AddChild(_blackOverlay);

		_alarmOverlay = new ColorRect();
		_alarmOverlay.Color = new Color(0.8f, 0, 0, 0);
		_alarmOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
		_alarmOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_uiLayer.AddChild(_alarmOverlay);

		_subtitleLabel = new Label();
		_subtitleLabel.Name = "SubtitleLabel";
		_subtitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_subtitleLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
		_subtitleLabel.Position = new Vector2(0, 700);
		_subtitleLabel.LabelSettings = new LabelSettings
		{
			FontSize = 20,
			FontColor = new Color(1, 1, 1, 0),
			OutlineSize = 1,
			OutlineColor = new Color(0, 0, 0, 0.8f),
		};
		_uiLayer.AddChild(_subtitleLabel);

		_instructionLabel = new Label();
		_instructionLabel.Name = "InstructionLabel";
		_instructionLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_instructionLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
		_instructionLabel.Position = new Vector2(0, 760);
		_instructionLabel.LabelSettings = new LabelSettings
		{
			FontSize = 16,
			FontColor = new Color(0, 1f, 0.3f, 0),
		};
		_uiLayer.AddChild(_instructionLabel);

		_dialogueLabel = new Label();
		_dialogueLabel.Name = "DialogueLabel";
		_dialogueLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_dialogueLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
		_dialogueLabel.Position = new Vector2(0, 640);
		_dialogueLabel.LabelSettings = new LabelSettings
		{
			FontSize = 22,
			FontColor = new Color(1, 1, 0.8f, 0),
			OutlineSize = 2,
			OutlineColor = new Color(0, 0, 0, 0.9f),
		};
		_uiLayer.AddChild(_dialogueLabel);

		_creationUI = new CharacterCreation();
		_creationUI.CharacterFinalized += OnCharacterFinalized;
		AddChild(_creationUI);
	}

	public async void StartIntro()
	{
		GD.Print("[IntroSequence] Starting Fallout 3 opening sequence...");
		_currentPhase = Phase.Creation;

		var tween = CreateTween();
		tween.TweenProperty(_blackOverlay, "color", new Color(0, 0, 0, 0), 2.0f);
		await ToSignal(tween, Tween.SignalName.Finished);

		ShowSubtitle("VAULT 101 — BIRTH", 3.0f);
		_creationUI.ShowCreation();
	}

	private void OnCharacterFinalized(string name, int[] special, bool male)
	{
		if (_currentPhase == Phase.Creation)
		{
			_playerName = name;
			_specialValues = special;
			_isMale = male;
			_creationUI.HideAll();
			ShowSubtitle("", 0);

			CreateChildController();
			BuildVault101Scene();

			_child.SetStage(LifeStage.Child);
			_child.Position = new Vector3(0, 0.5f, -30);

			ShowSubtitle($"{_playerName}'s 10th Birthday — Vault 101", 4.0f);
			ShowInstruction("WASD: Move | E: Interact");

			_currentPhase = Phase.Birthday;
			Input.MouseMode = Input.MouseModeEnum.Captured;
		}
		else if (_currentPhase == Phase.GoatTest)
		{
			// GOAT test completed — move to escape
			_creationUI.HideAll();
			SetupEscapePhase();
		}
	}

	private void CreateChildController()
	{
		_child = new ChildController();
		_child.Name = "ChildController";
		AddChild(_child);

		var cam = new Camera3D();
		cam.Name = "Camera3D";
		cam.Current = true;
		_child.AddChild(cam);
	}

	public override void _Input(InputEvent @event)
	{
		if (_inDialogue && @event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.E)
		{
			AdvanceDialogue();
		}
	}

	public override void _Process(double delta)
	{
		if (_alarmActive)
		{
			float intensity = 0.15f + Mathf.Sin(Time.GetTicksMsec() * 0.01f) * 0.08f;
			_alarmOverlay.Color = new Color(0.8f, 0, 0, intensity);
		}
	}

	// ── Dialogue system ────────────────────────────────────────────

	private void StartDialogue(string[] lines, Action onEnd = null)
	{
		_dialogueQueue.Clear();
		foreach (var line in lines)
			_dialogueQueue.Enqueue(line);
		_onDialogueEnd = onEnd;
		_inDialogue = true;
		ShowNextDialogue();
	}

	private void ShowNextDialogue()
	{
		if (_dialogueQueue.Count > 0)
		{
			string line = _dialogueQueue.Dequeue();
			_dialogueLabel.Text = line;
			var tween = CreateTween();
			_dialogueLabel.LabelSettings.FontColor = new Color(1, 1, 0.8f, 0);
			tween.TweenProperty(_dialogueLabel.LabelSettings, "font_color", new Color(1, 1, 0.8f, 1), 0.3f);
			ShowInstruction("[E] Continue");
		}
		else
		{
			EndDialogue();
		}
	}

	private void AdvanceDialogue()
	{
		ShowNextDialogue();
	}

	private void EndDialogue()
	{
		_inDialogue = false;
		_dialogueLabel.LabelSettings.FontColor = new Color(1, 1, 0.8f, 0);
		_instructionLabel.LabelSettings.FontColor = new Color(0, 1f, 0.3f, 0);
		_onDialogueEnd?.Invoke();
		_onDialogueEnd = null;
	}

	// ── Birthday Party Phase ──────────────────────────────────────

	private void OnDadInteracted()
	{
		if (_dadTalked) return;
		_dadTalked = true;

		string[] dialogue = {
			$"{_playerName}! Come here, son. Happy 10th birthday.",
			"I know things are tough in the vault, but you're growing up strong.",
			"I want you to have this. It's a BB gun. Be careful with it.",
			"Someday... you might need to use it for real. I hope not.",
            "Now go enjoy your party. Talk to Amata and the others."
		};
		StartDialogue(dialogue, CheckBirthdayComplete);
	}

	private void OnAmataInteracted()
	{
		if (_amataTalked) return;
		_amataTalked = true;

		string[] dialogue = {
			$"Hey {_playerName}! Happy birthday!",
			"Can you believe we're already 10? Time flies in the vault.",
			"I've been reading about the outside world. It sounds amazing.",
            "Maybe someday we'll see it together. Happy birthday, friend."
		};
		StartDialogue(dialogue, CheckBirthdayComplete);
	}

	private void OnGuestInteracted(int guestNum)
	{
		string[][] guestDialogues = {
			new[] { "Happy birthday! You're growing up so fast.", "I remember when you were just a baby in your father's arms." },
			new[] { "The Overseer says you're one of the brightest kids in the vault.", "Keep studying hard, and you'll go far." },
		};
		int idx = Math.Clamp(guestNum, 0, guestDialogues.Length - 1);
		StartDialogue(guestDialogues[idx]);
	}

	private void CheckBirthdayComplete()
	{
		if (_dadTalked && _amataTalked && _currentPhase == Phase.Birthday)
		{
			ShowSubtitle("10 years later...", 3.0f);
			ShowInstruction("Explore the vault. Talk to people.", 5.0f);

			_currentPhase = Phase.Childhood;
			_child.SetStage(LifeStage.Teen);
			_child.Position = new Vector3(2, 0.5f, -1);

			// Move interactables to childhood positions
			SetupChildhoodInteractables();
		}
	}

	// ── Childhood Phase ──────────────────────────────────────────

	private void SetupChildhoodInteractables()
	{
		// Already set up in vault scene, just ensure interactables are enabled
		_dadInteractable?.SetEnabled(true);
		_amataInteractable?.SetEnabled(true);

		// Add GOAT terminal now so player can find it
		_goatTerminal?.SetEnabled(true);
		_dadInteractable.InteractionText = "Talk (Dad)";
	}

	// The GOAT terminal interaction (also triggered by Childhood phase proximity)
	// Childhood advances when player reaches the GOAT room
	private void OnGoatTerminalInteracted()
	{
		_child.SetStage(LifeStage.Teen);
		_child.Position = new Vector3(0, 0.5f, 3);
		ShowSubtitle("G.O.A.T. Test — Age 16", 4.0f);

		_creationUI.ShowGoatTest();
		_currentPhase = Phase.GoatTest;
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	// ── GOAT Test Phase ─────────────────────────────────────────

	// CharacterFinalized is emitted again when GOAT is done
	private void OnGoatComplete()
	{
		_creationUI.HideAll();
		SetupEscapePhase();
	}

	// ── Escape Phase ─────────────────────────────────────────────

	private void SetupEscapePhase()
	{
		_child.SetStage(LifeStage.Adult);
		_child.Position = new Vector3(0, 0.5f, -30);

		_alarmActive = true;
		ShowSubtitle("VAULT 101 — \"Escape!\"", 0);

		// Amata calls player
		_amataInteractable.InteractionText = "Talk (Amata)";
		_amataInteractable.Position = new Vector3(-2, 0, -28);
		_amataInteractable.SetEnabled(true);

		_escapeDoor.SetEnabled(true);
		_escapeDoor.InteractionText = "Open (Exit)";

		_currentPhase = Phase.Escape;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private void OnEscapeAmataInteracted()
	{
		string[] dialogue = {
			$"{_playerName}! Over here! Thank goodness you're okay.",
			"The vault is in chaos. The Overseer has gone mad.",
			"Here — take my Pip-Boy. You'll need it out there.",
			"I'm staying to help the others. Go! Find your dad!",
            "The exit door is just down the hall. Hurry!"
		};
		_escapeStarted = true;
		StartDialogue(dialogue);
	}

	private void OnEscapeDoorInteracted()
	{
		if (!_escapeStarted)
		{
			ShowInstruction("Talk to Amata first!", 3.0f);
			return;
		}

		_alarmActive = false;
		_alarmOverlay.Color = new Color(0.8f, 0, 0, 0);

		var tween = CreateTween();
		tween.TweenProperty(_blackOverlay, "color", new Color(0, 0, 0, 1), 2.0f);
		tween.TweenCallback(Callable.From(() =>
		{
			ShowSubtitle("", 0);
			_currentPhase = Phase.Complete;
			EmitSignal(SignalName.IntroComplete, _playerName, _specialValues, _isMale);
		}));
	}

	// ── UI helpers ───────────────────────────────────────────────

	public void ShowSubtitle(string text, float duration)
	{
		if (duration <= 0)
		{
			_subtitleLabel.Text = text;
			_subtitleLabel.LabelSettings.FontColor = text == ""
				? new Color(1, 1, 1, 0)
				: new Color(1, 1, 1, 1);
			return;
		}

		var tween = CreateTween();
		_subtitleLabel.Text = text;
		_subtitleLabel.LabelSettings.FontColor = new Color(1, 1, 1, 0);
		tween.TweenProperty(_subtitleLabel.LabelSettings, "font_color", new Color(1, 1, 1, 1), 0.5f);
		tween.TweenInterval(duration - 0.5f);
		tween.TweenProperty(_subtitleLabel.LabelSettings, "font_color", new Color(1, 1, 1, 0), 1.0f);
	}

	public void ShowInstruction(string text, float duration = 4.0f)
	{
		var tween = CreateTween();
		_instructionLabel.Text = text;
		_instructionLabel.LabelSettings.FontColor = new Color(0, 1f, 0.3f, 0);
		tween.TweenProperty(_instructionLabel.LabelSettings, "font_color", new Color(0, 1f, 0.3f, 0.8f), 0.5f);
		tween.TweenInterval(duration - 0.5f);
		tween.TweenProperty(_instructionLabel.LabelSettings, "font_color", new Color(0, 1f, 0.3f, 0), 1.0f);
	}

	// ── Vault 101 Scene ──────────────────────────────────────────

	private void BuildVault101Scene()
	{
		_vaultScene = new Node3D();
		_vaultScene.Name = "Vault101Scene";
		AddChild(_vaultScene);

		BuildPartyRoom(new Vector3(0, 0, -35));
		BuildVaultCorridor(new Vector3(0, 0, 0), 30);
		BuildGoatRoom(new Vector3(0, 0, 35));
		BuildEscapeSection(new Vector3(0, 0, 75));
	}

	private void BuildVaultCorridor(Vector3 origin, float length)
	{
		float w = 4, h = 3;
		var corridor = new Node3D();
		corridor.Position = origin;
		_vaultScene.AddChild(corridor);

		NewBox(corridor, new Vector3(w, 0.2f, length), new Vector3(0, -0.1f, length / 2), new Color(0.35f, 0.35f, 0.35f), true);
		NewBox(corridor, new Vector3(w, 0.2f, length), new Vector3(0, h, length / 2), new Color(0.3f, 0.3f, 0.3f));
		NewBox(corridor, new Vector3(0.2f, h, length), new Vector3(-w / 2, h / 2, length / 2), new Color(0.4f, 0.4f, 0.42f), true);
		NewBox(corridor, new Vector3(0.2f, h, length), new Vector3(w / 2, h / 2, length / 2), new Color(0.4f, 0.4f, 0.42f), true);

		for (float z = 2; z < length; z += 5)
		{
			var light = new OmniLight3D();
			light.Position = new Vector3(0, h - 0.5f, z);
			light.LightEnergy = 0.8f;
			light.LightColor = new Color(0.9f, 0.85f, 0.7f);
			light.OmniRange = 6;
			corridor.AddChild(light);
		}
	}

	private void BuildPartyRoom(Vector3 origin)
	{
		var room = new Node3D();
		room.Name = "PartyRoom";
		room.Position = origin;
		_vaultScene.AddChild(room);

		NewBox(room, new Vector3(10, 0.2f, 10), new Vector3(0, -0.1f, 5), new Color(0.35f, 0.35f, 0.35f), true);
		NewBox(room, new Vector3(0.2f, 3, 10), new Vector3(-5, 1.5f, 5), new Color(0.4f, 0.4f, 0.42f), true);
		NewBox(room, new Vector3(0.2f, 3, 10), new Vector3(5, 1.5f, 5), new Color(0.4f, 0.4f, 0.42f), true);
		NewBox(room, new Vector3(10, 3, 0.2f), new Vector3(0, 1.5f, 0), new Color(0.4f, 0.4f, 0.42f), true);
		NewBox(room, new Vector3(10, 0.2f, 10), new Vector3(0, 3, 5), new Color(0.3f, 0.3f, 0.3f));

		var light = new OmniLight3D();
		light.Position = new Vector3(0, 2.5f, 5);
		light.LightEnergy = 1.2f;
		light.LightColor = new Color(0.95f, 0.9f, 0.8f);
		light.OmniRange = 12;
		room.AddChild(light);

		// Table + Cake
		NewBox(room, new Vector3(2, 0.8f, 1), new Vector3(0, 0.4f, 4), new Color(0.5f, 0.35f, 0.2f), true);
		NewCyl(room, 0.3f, 0.4f, 0.4f, new Vector3(0, 0.85f, 4), new Color(0.9f, 0.7f, 0.7f));

		// Party hats
		for (int i = 0; i < 4; i++)
			NewCyl(room, 0.3f, 0, 0.15f, new Vector3(-3 + i * 2, 0.15f, 6), new Color(1, 1, 0) * (i % 2 == 0 ? 0.8f : 1.0f));

		// Guest NPC markers
		for (int i = 0; i < 3; i++)
			NewCyl(room, 1.6f, 0.3f, 0.3f, new Vector3(-3 + i * 2, 0.8f, 3), Color.FromHsv(0.08f + i * 0.05f, 0.4f, 0.6f));

		// Guest interactables
		_guest1Interactable = MakeInteractable(new Vector3(-3, 0.5f, 3), 1.5f, "Talk", () => OnGuestInteracted(0));
		_guest2Interactable = MakeInteractable(new Vector3(-1, 0.5f, 3), 1.5f, "Talk", () => OnGuestInteracted(1));

		// Dad NPC + interactable
		NewCyl(room, 1.8f, 0.35f, 0.35f, new Vector3(2, 0.9f, 4), new Color(0.4f, 0.4f, 0.5f));
		_dadInteractable = MakeInteractable(new Vector3(2, 0.5f, 4), 2.0f, "Talk (Dad)", OnDadInteracted);

		// Amata NPC + interactable
		NewCyl(room, 1.5f, 0.25f, 0.25f, new Vector3(-1, 0.75f, 2), new Color(0.6f, 0.4f, 0.4f));
		_amataInteractable = MakeInteractable(new Vector3(-1, 0.5f, 2), 2.0f, "Talk (Amata)", OnAmataInteracted);
	}

	private void BuildGoatRoom(Vector3 origin)
	{
		var room = new Node3D();
		room.Name = "GoatRoom";
		room.Position = origin;
		_vaultScene.AddChild(room);

		NewBox(room, new Vector3(6, 0.2f, 6), new Vector3(0, -0.1f, 3), new Color(0.35f, 0.35f, 0.35f), true);
		NewBox(room, new Vector3(0.2f, 3, 6), new Vector3(-3, 1.5f, 3), new Color(0.4f, 0.4f, 0.42f), true);
		NewBox(room, new Vector3(0.2f, 3, 6), new Vector3(3, 1.5f, 3), new Color(0.4f, 0.4f, 0.42f), true);
		NewBox(room, new Vector3(6, 3, 0.2f), new Vector3(0, 1.5f, 0), new Color(0.4f, 0.4f, 0.42f), true);
		NewBox(room, new Vector3(6, 0.2f, 6), new Vector3(0, 3, 3), new Color(0.3f, 0.3f, 0.3f));

		var light = new OmniLight3D();
		light.Position = new Vector3(0, 2.5f, 3);
		light.LightEnergy = 1.0f;
		light.LightColor = new Color(0.95f, 0.9f, 0.8f);
		light.OmniRange = 8;
		room.AddChild(light);

		// Desk with terminal
		NewBox(room, new Vector3(1.5f, 0.6f, 0.8f), new Vector3(0, 0.3f, 2), new Color(0.4f, 0.4f, 0.4f), true);
		NewBox(room, new Vector3(0.6f, 0.4f, 0.3f), new Vector3(0, 0.7f, 2.3f), new Color(0.2f, 0.6f, 0.2f));

		_goatTerminal = MakeInteractable(new Vector3(0, 0.5f, 2), 2.0f, "Use (GOAT Terminal)", OnGoatTerminalInteracted);
		_goatTerminal.SetEnabled(false);
	}

	private void BuildEscapeSection(Vector3 origin)
	{
		var escape = new Node3D();
		escape.Name = "EscapeSection";
		escape.Position = origin;
		_vaultScene.AddChild(escape);

		BuildVaultCorridor(Vector3.Zero, 8);

		// Exit door
		NewCyl(escape, 3.5f, 1.5f, 1.5f, new Vector3(0, 1.5f, 8), new Color(0.8f, 0.5f, 0.2f));
		NewBox(escape, new Vector3(4, 3, 0.2f), new Vector3(0, 1.5f, 8.5f), new Color(0.5f, 0.5f, 0.5f), true);

		// Exit light
		var light = new OmniLight3D();
		light.Position = new Vector3(0, 2, 7);
		light.LightColor = new Color(1, 0.8f, 0.5f);
		light.LightEnergy = 2.0f;
		light.OmniRange = 10;
		escape.AddChild(light);

		_escapeDoor = MakeInteractable(new Vector3(0, 0.5f, 7.5f), 2.0f, "Open (Exit)", OnEscapeDoorInteracted);
		_escapeDoor.SetEnabled(false);
	}

	// ── Helpers ───────────────────────────────────────────────────

	private static void NewBox(Node3D parent, Vector3 size, Vector3 pos, Color color, bool collide = false)
	{
		var mi = new MeshInstance3D();
		mi.Mesh = new BoxMesh { Size = size };
		mi.Position = pos;
		mi.MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.9f };
		parent.AddChild(mi);

		if (collide)
		{
			var body = new StaticBody3D();
			body.Position = pos;
			parent.AddChild(body);
			var shape = new CollisionShape3D();
			shape.Shape = new BoxShape3D { Size = size };
			body.AddChild(shape);
		}
	}

	private static void NewCyl(Node3D parent, float h, float topR, float botR, Vector3 pos, Color color)
	{
		var mi = new MeshInstance3D();
		mi.Mesh = new CylinderMesh { Height = h, TopRadius = topR, BottomRadius = botR };
		mi.Position = pos;
		mi.MaterialOverride = new StandardMaterial3D { AlbedoColor = color };
		parent.AddChild(mi);
	}

	private Interactable MakeInteractable(Vector3 position, float radius, string text, Action onInteract)
	{
		var ia = new Interactable();
		ia.Name = $"Interact_{text}";
		ia.Position = position;
		ia.InteractionText = text;
		ia.Interacted += () => onInteract?.Invoke();

		var collision = new CollisionShape3D();
		collision.Shape = new SphereShape3D { Radius = radius };
		ia.AddChild(collision);

		_vaultScene.AddChild(ia);
		return ia;
	}
}
