using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InvestigationController : MonoBehaviour
{
    // -------------------------
    // Enums
    // -------------------------
    public enum ToolType { Polaroid, SpiritBox, Thermometer }
    public enum Direction { Up, Down, Left, Right }
    public enum RoomId
    {
        ElevatorExit,      // center
        HallwayLeft,       // left branch hallway
        HallwayRight,      // right branch hallway
        RoomA, RoomB,      // doors off HallwayLeft
        RoomC, RoomD       // doors off HallwayRight
    }

    [Serializable]
    public class RoomData
    {
        public RoomId id;
        [TextArea] public string description;
        public ToolType? correctTool;   // null = no evidence in this room
        public string evidenceName;     // e.g. "Burning Cross Photo"
        public bool canGoUp;
        public bool canGoDown;  // usually true if there is a previous room
        public bool canGoLeft;
        public bool canGoRight;
        // Optional: background sprite if you add later
        // public Sprite background;
    }

    // -------------------------
    // UI References (assign in Inspector)
    // -------------------------
    [Header("Text")]
    public TMP_Text roomDescriptionText;
    public TMP_Text timerText;
    public TMP_Text feedbackText;

    [Header("Tool Buttons")]
    public Button polaroidButton;
    public Button spiritBoxButton;
    public Button thermometerButton;

    [Header("Movement Buttons")]
    public Button moveUpButton;
    public Button moveDownButton;
    public Button moveLeftButton;
    public Button moveRightButton;

    [Header("Optional: Start/End Panels")]
    public GameObject startPanel; // can be null if you don't have one
    public GameObject endPanel;   // can be null if you don't have one
    public TMP_Text endPanelText; // can be null if you don't have one

    [Header("Timing")]
    public float totalTimeSeconds = 120f;
    public float collectTimeSeconds = 5f;

    [Header("Audio (Optional)")]
    public AudioSource sfxSource;            // for one-shots
    public AudioClip uiClickSfx;
    public AudioClip wrongToolSfx;
    public AudioClip successSfx;
    public AudioClip collectStartSfx;
    public AudioClip timeLowBeepSfx;
    public AudioClip timeUpSfx;

    [Header("Background Art")]
    public Image backgroundImage;

    public Sprite initialHallwaySprite;
    public Sprite hallwayEndSprite;

    public Sprite doorASprite;
    public Sprite doorBSprite;
    public Sprite doorCSprite;
    public Sprite doorDSprite;

    public Sprite roomPlainSprite;
    public Sprite roomBurningCrossSprite;
    public Sprite roomOrbsSprite;
    public Sprite roomDisheveledSprite;


    // -------------------------
    // Internal State
    // -------------------------
    private Dictionary<RoomId, RoomData> rooms;
    private RoomId currentRoom = RoomId.ElevatorExit;

    // Which door you're "facing" while in each hallway
    private bool leftHallFacingA = true;   // false = facing B
    private bool rightHallFacingC = true;  // false = facing D

    // Robust backtracking: stack instead of single previousRoom
    private Stack<RoomId> history = new Stack<RoomId>();

    private float timeRemaining;
    private bool started = false;
    private bool isCollecting = false;
    private bool ended = false;

    private HashSet<ToolType> collected = new HashSet<ToolType>();
    private bool lowTimeBeeped = false;

    void Start()
    {
        // If you didn’t create an AudioSource yet, the script still works.
        if (feedbackText != null) feedbackText.text = "";

        BuildRooms();
        timeRemaining = totalTimeSeconds;

        // Hook up button events if you want to do it via code
        if (polaroidButton != null) polaroidButton.onClick.AddListener(() => UseTool(ToolType.Polaroid));
        if (spiritBoxButton != null) spiritBoxButton.onClick.AddListener(() => UseTool(ToolType.SpiritBox));
        if (thermometerButton != null) thermometerButton.onClick.AddListener(() => UseTool(ToolType.Thermometer));

        if (moveUpButton != null) moveUpButton.onClick.AddListener(() => Move(Direction.Up));
        if (moveDownButton != null) moveDownButton.onClick.AddListener(() => Move(Direction.Down));
        if (moveLeftButton != null) moveLeftButton.onClick.AddListener(() => Move(Direction.Left));
        if (moveRightButton != null) moveRightButton.onClick.AddListener(() => Move(Direction.Right));

        // Start state
        if (startPanel != null)
        {
            startPanel.SetActive(true);
            SetInteractable(false);
            UpdateRoomUI(); // show hallway text behind panel if desired
            UpdateTimerUI();
        }
        else
        {
            // Auto-start if no start panel
            BeginInvestigation();
        }
    }

    void Update()
    {
        if (!started || ended) return;

        timeRemaining -= Time.deltaTime;
        if (timeRemaining < 0f) timeRemaining = 0f;

        UpdateTimerUI();

        // Low time beep at 30s (optional)
        if (!lowTimeBeeped && timeRemaining <= 30f)
        {
            lowTimeBeeped = true;
            PlaySfx(timeLowBeepSfx);
            SetFeedback("30 seconds left!");
        }

        if (timeRemaining <= 0f)
        {
            EndGame(false, "Time’s up.\nThe presence is escalating.");
        }
    }

    // -------------------------
    // Public UI hooks
    // -------------------------
    public void BeginInvestigation()
    {
        if (ended) return;

        started = true;
        if (startPanel != null) startPanel.SetActive(false);

        timeRemaining = totalTimeSeconds;
        lowTimeBeeped = false;

        currentRoom = RoomId.ElevatorExit;
        history.Clear();

        leftHallFacingA = true;
        rightHallFacingC = true;

        collected.Clear();
        isCollecting = false;

        SetFeedback("Find the evidence. Use the right tool in the right room.");
        SetInteractable(true);
        UpdateRoomUI();
        UpdateTimerUI();
    }

    // If you add a Start button on your start panel,
    // connect its OnClick() to InvestigationController.BeginInvestigation().
    // -------------------------

    private void BuildRooms()
    {
        rooms = new Dictionary<RoomId, RoomData>();

        rooms[RoomId.ElevatorExit] = new RoomData
        {
            id = RoomId.ElevatorExit,
            description = "You step out of the elevator into a dim hallway.\nYou can head left or right.",
            correctTool = null,
            evidenceName = "",
            canGoLeft = true,
            canGoRight = true,
            canGoUp = false,
            canGoDown = false
        };

        rooms[RoomId.HallwayLeft] = new RoomData
        {
            id = RoomId.HallwayLeft,
            description = "Left Hallway: Two doors sit across from you.",
            correctTool = null,
            evidenceName = "",
            canGoLeft = true,   // swap facing A/B
            canGoRight = true,  // swap facing A/B
            canGoUp = true,     // enter selected door
            canGoDown = true    // back to elevator exit
        };

        rooms[RoomId.HallwayRight] = new RoomData
        {
            id = RoomId.HallwayRight,
            description = "Right Hallway: Two doors sit across from you.",
            correctTool = null,
            evidenceName = "",
            canGoLeft = true,   // swap facing C/D
            canGoRight = true,  // swap facing C/D
            canGoUp = true,     // enter selected door
            canGoDown = true    // back to elevator exit
        };

        // Evidence rooms
        rooms[RoomId.RoomA] = new RoomData
        {
            id = RoomId.RoomA,
            description = "There is a burning cross outside the window.\nIt hovers in total silence.",
            correctTool = ToolType.Polaroid,
            evidenceName = "Burning Cross Photo",
            canGoDown = true
        };

        rooms[RoomId.RoomB] = new RoomData
        {
            id = RoomId.RoomB,
            description = "This room is noticeably more disheveled than the others.\nDrawers ripped open. Furniture shifted.",
            correctTool = ToolType.SpiritBox,
            evidenceName = "Spirit Box Response",
            canGoDown = true
        };

        rooms[RoomId.RoomC] = new RoomData
        {
            id = RoomId.RoomC,
            description = "Ethereal particles drift through the air.\nGhost orbs shimmer at the edge of your vision.",
            correctTool = ToolType.Thermometer,
            evidenceName = "Temperature Drop",
            canGoDown = true
        };

        rooms[RoomId.RoomD] = new RoomData
        {
            id = RoomId.RoomD,
            description = "This room seems… plain.\nNo obvious signs. Just the oppressive quiet.",
            correctTool = null,
            evidenceName = "",
            canGoDown = true
        };
    }

    private void Move(Direction dir)
    {
        if (!started || ended) return;
        if (isCollecting)
        {
            SetFeedback("You can't move while collecting evidence.");
            return;
        }

        PlaySfx(uiClickSfx);

        // DOWN = go back via history stack
        if (dir == Direction.Down)
        {
            if (history.Count > 0)
            {
                currentRoom = history.Pop();
                UpdateRoomUI();
            }
            else
            {
                SetFeedback("There's nowhere to go back to.");
            }
            return;
        }

        // State machine navigation:
        switch (currentRoom)
        {
            case RoomId.ElevatorExit:
                // Only Left/Right allowed
                if (dir == Direction.Left)
                {
                    history.Push(currentRoom);
                    currentRoom = RoomId.HallwayLeft;
                    SetFeedback("Entered left hallway.");
                }
                else if (dir == Direction.Right)
                {
                    history.Push(currentRoom);
                    currentRoom = RoomId.HallwayRight;
                    SetFeedback("Entered right hallway.");
                }
                else
                {
                    SetFeedback("You can only go left or right from here.");
                }
                UpdateRoomUI();
                return;

            case RoomId.HallwayLeft:
                // Left/Right toggles facing door; Up enters it
                if (dir == Direction.Left)
                {
                    leftHallFacingA = true;
                    SetFeedback("Facing Door A.");
                }
                else if (dir == Direction.Right)
                {
                    leftHallFacingA = false;
                    SetFeedback("Facing Door B.");
                }
                else if (dir == Direction.Up)
                {
                    history.Push(currentRoom);
                    currentRoom = leftHallFacingA ? RoomId.RoomA : RoomId.RoomB;
                    SetFeedback("Entering room...");
                }
                UpdateRoomUI();
                return;

            case RoomId.HallwayRight:
                if (dir == Direction.Left)
                {
                    rightHallFacingC = true;
                    SetFeedback("Facing Door C.");
                }
                else if (dir == Direction.Right)
                {
                    rightHallFacingC = false;
                    SetFeedback("Facing Door D.");
                }
                else if (dir == Direction.Up)
                {
                    history.Push(currentRoom);
                    currentRoom = rightHallFacingC ? RoomId.RoomC : RoomId.RoomD;
                    SetFeedback("Entering room...");
                }
                UpdateRoomUI();
                return;

            case RoomId.RoomA:
            case RoomId.RoomB:
            case RoomId.RoomC:
            case RoomId.RoomD:
                // In rooms, only Down works (handled above)
                SetFeedback("You're in the room. Use a tool or go back.");
                return;
        }
    }

    private void UseTool(ToolType tool)
    {
        if (!started || ended) return;
        if (isCollecting) return;

        PlaySfx(uiClickSfx);

        if (currentRoom == RoomId.ElevatorExit || currentRoom == RoomId.HallwayLeft || currentRoom == RoomId.HallwayRight)
        {
            PlaySfx(wrongToolSfx);
            SetFeedback("You should enter a room before using that tool.");
            return;
        }

        RoomData room = rooms[currentRoom];

        // If already collected for that tool, prevent re-collect
        if (collected.Contains(tool))
        {
            SetFeedback($"{tool} evidence already collected.");
            return;
        }

        // Room has no evidence
        if (room.correctTool == null)
        {
            PlaySfx(wrongToolSfx);
            SetFeedback("Nothing happens… this room feels quiet.");
            return;
        }

        // Wrong tool for this room
        if (room.correctTool.Value != tool)
        {
            PlaySfx(wrongToolSfx);
            SetFeedback("That tool isn’t picking anything up here.");
            return;
        }

        // Correct tool + correct room => collect
        StartCoroutine(CollectEvidenceCoroutine(tool, room.evidenceName));
    }

    private IEnumerator CollectEvidenceCoroutine(ToolType tool, string evidenceName)
    {
        isCollecting = true;
        SetInteractable(false, keepDownArrow: true); // allow Down arrow? you can decide—I'll lock movement too.

        PlaySfx(collectStartSfx);

        float t = collectTimeSeconds;
        while (t > 0f)
        {
            if (ended) yield break; // safety
            SetFeedback($"Collecting {evidenceName}… {Mathf.CeilToInt(t)}");
            yield return null;
            t -= Time.deltaTime;
        }

        collected.Add(tool);
        PlaySfx(successSfx);
        SetFeedback($"Evidence found: {evidenceName}!");

        isCollecting = false;
        SetInteractable(true);

        // Win condition: all three collected
        if (collected.Contains(ToolType.Polaroid) &&
            collected.Contains(ToolType.SpiritBox) &&
            collected.Contains(ToolType.Thermometer))
        {
            EndGame(true, "All evidence collected.\nGet out.");
        }
        else
        {
            UpdateRoomUI(); // refresh button states, arrows, etc.
        }
    }

    private void EndGame(bool win, string message)
    {
        if (ended) return;
        ended = true;
        started = false;
        isCollecting = false;

        if (!win) PlaySfx(timeUpSfx);

        SetInteractable(false);
        if (endPanel != null)
        {
            endPanel.SetActive(true);
            if (endPanelText != null) endPanelText.text = message;
        }
        else
        {
            SetFeedback(message);
        }
    }

    private void UpdateRoomUI()
    {
        if (roomDescriptionText != null)
        {
            string baseDesc = rooms[currentRoom].description;

            if (currentRoom == RoomId.HallwayLeft)
                baseDesc += leftHallFacingA ? "\n\nSelected: Door A (Left)" : "\n\nSelected: Door B (Right)";

            if (currentRoom == RoomId.HallwayRight)
                baseDesc += rightHallFacingC ? "\n\nSelected: Door C (Left)" : "\n\nSelected: Door D (Right)";

            roomDescriptionText.text = baseDesc;
        }

        // Timer UI separate
        UpdateArrowAvailability();
        UpdateToolButtonStates();
    }

    private void UpdateTimerUI()
    {
        if (timerText == null) return;

        int seconds = Mathf.CeilToInt(timeRemaining);
        int m = seconds / 60;
        int s = seconds % 60;
        timerText.text = $"{m:0}:{s:00}";
    }

    private void UpdateArrowAvailability()
    {
        if (moveUpButton == null || moveDownButton == null || moveLeftButton == null || moveRightButton == null) return;

        bool canUp = false, canDown = false, canLeft = false, canRight = false;

        switch (currentRoom)
        {
            case RoomId.ElevatorExit:
                canLeft = true;
                canRight = true;
                canUp = false;
                canDown = false;
                break;

            case RoomId.HallwayLeft:
            case RoomId.HallwayRight:
                canLeft = true;   // select door
                canRight = true;  // select door
                canUp = true;     // enter selected
                canDown = true;   // back
                break;

            case RoomId.RoomA:
            case RoomId.RoomB:
            case RoomId.RoomC:
            case RoomId.RoomD:
                canDown = true;   // back to hallway
                break;
        }

        moveLeftButton.interactable = !ended && started && !isCollecting && canLeft;
        moveRightButton.interactable = !ended && started && !isCollecting && canRight;
        moveUpButton.interactable = !ended && started && !isCollecting && canUp;
        moveDownButton.interactable = !ended && started && !isCollecting && canDown;
    }

    private void UpdateToolButtonStates()
    {
        if (polaroidButton != null)
            polaroidButton.interactable = started && !ended && !isCollecting && !collected.Contains(ToolType.Polaroid);

        if (spiritBoxButton != null)
            spiritBoxButton.interactable = started && !ended && !isCollecting && !collected.Contains(ToolType.SpiritBox);

        if (thermometerButton != null)
            thermometerButton.interactable = started && !ended && !isCollecting && !collected.Contains(ToolType.Thermometer);
    }

    private void SetInteractable(bool enabled, bool keepDownArrow = false)
    {
        // Tools
        if (polaroidButton != null) polaroidButton.interactable = enabled;
        if (spiritBoxButton != null) spiritBoxButton.interactable = enabled;
        if (thermometerButton != null) thermometerButton.interactable = enabled;

        // Movement (we’ll re-apply availability rules right after)
        if (moveUpButton != null) moveUpButton.interactable = enabled;
        if (moveLeftButton != null) moveLeftButton.interactable = enabled;
        if (moveRightButton != null) moveRightButton.interactable = enabled;
        if (moveDownButton != null) moveDownButton.interactable = enabled || (keepDownArrow && history.Count > 0);

        UpdateArrowAvailability();
        UpdateToolButtonStates();
    }

    private void SetFeedback(string msg)
    {
        if (feedbackText != null) feedbackText.text = msg;
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null) return;
        if (sfxSource == null) return;
        sfxSource.PlayOneShot(clip);
    }
}
