using UnityEngine;
using System;
using Random = UnityEngine.Random;
using TMPro;

/// Incision mini-game controller:
/// - Random path (line) on the skin plane constrained by provided boundary colliders
/// - Green start and red end markers
/// - Score based on progress and lateral error while cutting below skin surface
/// - Ends on: depth > MaxDepth, completed, or timer == 0
/// - End screen with 2 buttons, clickable by mouse or by "penetrating" with the scalpel tip
public class incisionGame : MonoBehaviour
{
    [Header("References")]
    public Transform Skin;                 // The skin plane object (position/normal used)
    public Transform ScalpelTip;           // Tip transform used for depth and contact
    public Camera Cam;                     // Optional, defaults to Camera.main
    public WoundPainter Painter;           // Your existing painter (optional but recommended)
    public TextMeshProUGUI TimerText;

    [Header("Depth Settings")]
    public float SkinSurfaceY = 0.8764f;   // Skin surface height (world Y)
    public float MaxDepth = 0.0424f;       // Game-over depth in meters

    [Header("Boundary Constraints (XZ plane)")]
    public BoxCollider SkinBoundary;       // Required: skin boundary area (world)
    public BoxCollider ControllerWorkspace;// Optional: intersect with skin boundary to keep path within workspace
    [Tooltip("Extra inset (meters) applied to the usable rect on each side to keep the path away from edges.")]
    public float ExtraBoundaryInset = 0.02f;   // NEW: shrink usable area further

    [Header("Path Settings")]
    [Range(0.1f, 0.9f)]
    public float PathLengthPercent = 0.3f; // 30% of the smaller side of usable area
    public float LateralTolerance = 0.01f; // meters off the path allowed for "good cutting"
    [Tooltip("How loose we are for counting forward progress vs. tolerance. 2 = 2x LateralTolerance.")]
    public float ProgressToleranceMultiplier = 2f; // NEW: allow some leeway for progress
    public float MarkerRadius = 0.01f;     // visual start/end sphere radius
    [Header("Path Orientation")]
    [Tooltip("Max deviation from vertical (camera up projected onto skin) in degrees.")]
    public float MaxVerticalDeviationDeg = 15f;

    [Header("Visuals")]
    public Color PathColor = Color.white;
    public float PathWidth = 0.005f;
    [Tooltip("Lift the line and markers slightly above the skin to avoid z-fighting.")]
    public float LineYOffset = 0.001f;

    [Header("Round Settings")]
    public float RoundSeconds = 60f;
    float _roundStartTime;

    [Tooltip("Ignore depth fail for a short time right after the round starts.")]
    public float StartGraceSeconds = 1.0f;

    [Header("Painter Hooks (optional)")]
    public bool ClearPainterOnStart = true;     // Clears painter at round start
    public bool ClearPainterOnReset = true;     // Clears painter on Play Again
    [Tooltip("Try to find RenderTextures on Painter via reflection and clear them if no method is provided.")]
    public bool TryRenderTextureClearFallback = true; // NEW

    [Header("Robotic Controller Hook (optional)")]
    public SimpleKnifeMover RobotMover;         // If assigned, you can re-home/origin only when Play Again is clicked

    [Header("End UI Settings")]
    [Tooltip("Distance in front of camera for end buttons.")]
    public float EndUIButtonDistance = 0.7f;
    [Tooltip("Vertical spacing between stacked end buttons.")]
    public float EndUIButtonVerticalGap = 0.12f;
    [Tooltip("Optional name of a public method on Painter used to clear the texture.")]
    public string PainterClearMethodName = "Clear";

    enum GameState { Idle, Playing, Completed, GameOver, TimeUp, EndScreen, FreeMode, Freeze }

    [Header("Path Direction & Start Highlight")]
    [Tooltip("If true, the start (green) dot is placed 'above' the end (red) dot relative to the camera view (top-to-bottom cut).")]
    public bool ForceTopToBottom = true;
    [Tooltip("Radius around the start dot to consider as a successful start touch (meters, XZ on skin).")]
    public float StartTouchRadius = 0.015f;
    public Color StartHighlightColor = Color.yellow;
    [Tooltip("Scale factor for the yellow highlight sphere relative to the green dot.")]
    public float StartHighlightScale = 1.6f;

    GameState _state = GameState.Idle;

    // Timer
    float _timeLeft;

    // Path
    LineRenderer _line;
    GameObject _startDot, _endDot;
    Vector3 _pStart, _pEnd, _dir; // world space points on the skin plane
    float _pathLen;

    // Progress / scoring
    float _maxProgress;         // max t along the path reached (0.._pathLen)
    double _sumAbsError;        // sum of lateral distances
    int _samples;               // samples counted while cutting
    float _lastDepth;

    // End UI overlay (3D interactable)
    PenetrationButton _btnPlayAgain, _btnMainMenu;
    PenetrationButton _btnStart; 

    enum PendingChoice { None, PlayAgain, FreeMode }
    PendingChoice _pendingChoice = PendingChoice.None;

    string _endReason;
    float _finalScore;

    // Usable rect in XZ (world)
    struct RectXZ { public float minX, maxX, minZ, maxZ; }
    RectXZ _usable;

    GameObject _startHighlight;
    bool _startTouched = false;
    public static bool AllowPainting = false;
    public static bool FreezeTool = false; // knife movement pause

    void Awake()
    {
        if (!Cam) Cam = Camera.main;
        EnsureLineRenderer();
    }

    void Start()
    {
        if (!Skin || !ScalpelTip || SkinBoundary == null)
        {
            Debug.LogError("incisionGame: Assign Skin, ScalpelTip, and SkinBoundary in the inspector.");
            _state = GameState.Idle;
            return;
        }

        EnterMainMenu(); // Start in main menu-like idle; you can call StartRound() from a UI/menu
        StartRound();
    }

    void EnsureLineRenderer()
    {
        _line = gameObject.GetComponent<LineRenderer>();
        if (!_line) _line = gameObject.AddComponent<LineRenderer>();
        _line.positionCount = 2;
        _line.useWorldSpace = true;
        _line.widthMultiplier = PathWidth;
        _line.alignment = LineAlignment.View;         // face camera
        _line.numCapVertices = 4;
        _line.numCornerVertices = 4;

        // Pick a widely available unlit shader (URP/HDRP/Legacy fallbacks)
        Shader s =
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Color") ??
            Shader.Find("Sprites/Default");
        _line.material = new Material(s);
        _line.startColor = PathColor;
        _line.endColor = PathColor;

        _line.sortingOrder = 100; // draw over most geometry
        _line.enabled = false;
    }

    void Update()
    {
        if (!Cam) Cam = Camera.main;
        if (_state == GameState.Playing)

        if (Input.GetKeyDown(KeyCode.R))
        {
            OnResetKey();
            return;
        }

       if (_state == GameState.Playing)
        {
            // Update timer
            _timeLeft -= Time.deltaTime;
            if (_timeLeft <= 0f)
            {
                _timeLeft = 0f;
                EndRound(GameState.TimeUp, "Time is up");
                return;
            }

            if (TimerText)
            {
                int secs = Mathf.CeilToInt(_timeLeft);
                int mm = secs / 60;
                int ss = secs % 60;
                TimerText.text = $"{mm:00}:{ss:00}";
            }

            // Depth
            float tipY = ScalpelTip.position.y;
            float rawDepth = SkinSurfaceY - tipY; // positive means below surface
            float depth = Mathf.Max(0f, rawDepth);
            _lastDepth = depth;

            if (Time.time - _roundStartTime > StartGraceSeconds && depth > MaxDepth)
            {
                EndRound(GameState.GameOver, $"Depth exceeded MaxDepth ({MaxDepth * 1000f:F0} mm).");
                return;
            }

            if (depth > MaxDepth)
            {
                EndRound(GameState.GameOver, $"Depth exceeded MaxDepth ({MaxDepth * 1000f:F0} mm).");
                return;
            }

            // Project tip to skin plane and update progress/error
            Vector3 pt = ProjectToSkinPlane(ScalpelTip.position);
            float tAlong, lateral;
            ComputePathMetrics(pt, out tAlong, out lateral);

            // Start touch detection: tip cutting near start dot
            if (!_startTouched)
            {
                float distXZ = Vector2.Distance(
                    new Vector2(pt.x, pt.z),
                    new Vector2(_pStart.x, _pStart.z));

                if (depth > 0f && distXZ <= StartTouchRadius)
                {
                    _startTouched = true;
                    if (_startHighlight) _startHighlight.SetActive(true);
                }
            }

            // Count sample if actually cutting
            if (depth > 0f)
            {
                _sumAbsError += Mathf.Abs(lateral);
                _samples++;

                float progressTol = LateralTolerance * Mathf.Max(1f, ProgressToleranceMultiplier);
                if (Mathf.Abs(lateral) <= progressTol)
                {
                    _maxProgress = Mathf.Max(_maxProgress, Mathf.Clamp(tAlong, 0f, _pathLen));
                }
            }

            // Completed?
            if (_maxProgress >= _pathLen - 1e-4f)
            {
                EndRound(GameState.Completed, "Incision completed!");
                return;
            }
        }
        else if (_state == GameState.FreeMode)
        {
            // No timer in free mode
            if (TimerText) TimerText.text = "";

            // Allow painting freely in free mode
            AllowPainting = true;
            if (Painter && !Painter.enabled) Painter.enabled = true;
        }
        else
        {
            // Any menu/freeze/idle: no painting, no timer
            if (TimerText) TimerText.text = "";
            AllowPainting = false;
        }
    }

    void OnGUI()
    {
        // Simple, lightweight UI for timer and status
        if (_state == GameState.Playing)
        {
            var style = new GUIStyle(GUI.skin.label);
            style.fontSize = 22;
            style.alignment = TextAnchor.UpperRight;
            string timeStr = $"{Mathf.CeilToInt(_timeLeft)}s";
            GUI.Label(new Rect(Screen.width - 150, 10, 140, 30), timeStr, style);
        }
        else if (_state == GameState.EndScreen || _state == GameState.Completed || _state == GameState.GameOver || _state == GameState.TimeUp)
        {
            var box = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.UpperCenter };
            GUI.Box(new Rect(Screen.width / 2f - 160, 30, 320, 100),
                $"Round Ended: {_endReason}\nScore: {_finalScore:F1}", box);
        }
    }

    // Public control: start a round (you can call this from your main menu)
    public void StartRound()
    {
        BuildUsableRectXZ();
        GeneratePath();
        SetupMarkers();
        ResetScore();

        if (ClearPainterOnStart && Painter != null)
        {
            // Your WoundPainter should expose a clear/reset method. Replace with your method name.
            TryClearPainterTexture();
        }

        _timeLeft = RoundSeconds;
        _roundStartTime = Time.time; 
        _state = GameState.Playing;
        incisionGame.AllowPainting = true;
        _line.enabled = true;

        HideEndButtons();
        _endReason = string.Empty;

        AllowPainting = true;
        if (Painter) Painter.enabled = true;
    }

    // Public control: enter "free mode" – no path, just play/reset skin with R in your other script
    public void EnterFreeMode()
    {
        HideEndButtons();
        _line.enabled = false;
        DestroyMarkers();

        _state = GameState.FreeMode;
    }

    // Called when the round ends (for any reason)
    void EndRound(GameState endState, string reason)
    {
        _state = endState;
        _endReason = reason;
        incisionGame.AllowPainting = false;

        // ...existing scoring...
        ShowEndButtons(); // shows Play Again + Free Mode
        _state = GameState.EndScreen;

        AllowPainting = false;
        if (Painter) Painter.enabled = false;
    }

    void ResetScore()
    {
        _maxProgress = 0f;
        _sumAbsError = 0d;
        _samples = 0;
        _finalScore = 0f;
        _startTouched = false;
        if (_startHighlight) _startHighlight.SetActive(false);
    }

    // Projects any point to the skin plane defined by SkinSurfaceY
    Vector3 ProjectToSkinPlane(Vector3 p)
    {
        return new Vector3(p.x, SkinSurfaceY, p.z);
    }

    void ComputePathMetrics(Vector3 p, out float tAlong, out float lateral)
    {
        // Consider path vector in XZ plane
        Vector2 s = new Vector2(_pStart.x, _pStart.z);
        Vector2 e = new Vector2(_pEnd.x, _pEnd.z);
        Vector2 d = (e - s).normalized;

        Vector2 pt = new Vector2(p.x, p.z);
        Vector2 v = pt - s;

        tAlong = Vector2.Dot(v, d); // along [0.._pathLen]
        Vector2 closest = s + Mathf.Clamp(tAlong, 0f, _pathLen) * d;
        lateral = Vector2.Distance(pt, closest);
    }

    void BuildUsableRectXZ()
    {
        // Start with skin boundary rect
        Bounds sb = SkinBoundary.bounds;
        RectXZ rect = new RectXZ
        {
            minX = sb.min.x,
            maxX = sb.max.x,
            minZ = sb.min.z,
            maxZ = sb.max.z
        };

        // Intersect with controller workspace if provided
        if (ControllerWorkspace != null)
        {
            Bounds wb = ControllerWorkspace.bounds;
            rect.minX = Mathf.Max(rect.minX, wb.min.x);
            rect.maxX = Mathf.Min(rect.maxX, wb.max.x);
            rect.minZ = Mathf.Max(rect.minZ, wb.min.z);
            rect.maxZ = Mathf.Min(rect.maxZ, wb.max.z);
        }

        // Base padding to avoid clipping edges + user-configurable shrink
        float basePad = 0.005f;
        rect.minX += basePad; rect.maxX -= basePad;
        rect.minZ += basePad; rect.maxZ -= basePad;

        // NEW: extra inset to further shrink usable area
        float inset = Mathf.Max(0f, ExtraBoundaryInset);
        rect.minX += inset; rect.maxX -= inset;
        rect.minZ += inset; rect.maxZ -= inset;

        // Clamp in case shrink collapsed the rect
        float minWidth = 0.02f, minHeight = 0.02f; // 2 cm fallback
        float w = Mathf.Max(0f, rect.maxX - rect.minX);
        float h = Mathf.Max(0f, rect.maxZ - rect.minZ);
        if (w < minWidth || h < minHeight)
        {
            // Re-center to a small safe rect at the skin center
            Vector3 c = sb.center;
            rect.minX = c.x - minWidth * 0.5f;
            rect.maxX = c.x + minWidth * 0.5f;
            rect.minZ = c.z - minHeight * 0.5f;
            rect.maxZ = c.z + minHeight * 0.5f;
        }

        _usable = rect;
    }

    void GeneratePath()
    {
        // Compute path length = 30% of the smaller side of usable rect
        float width = Mathf.Max(0f, _usable.maxX - _usable.minX);
        float height = Mathf.Max(0f, _usable.maxZ - _usable.minZ);
        float minSide = Mathf.Max(0.001f, Mathf.Min(width, height));
        _pathLen = Mathf.Clamp01(PathLengthPercent) * minSide;
    // Camera up projected to XZ as our "vertical" on skin
        if (!Cam) Cam = Camera.main;
        Vector3 upXZ3 = Cam ? new Vector3(Cam.transform.up.x, 0f, Cam.transform.up.z) : Vector3.forward;
        if (upXZ3.sqrMagnitude < 1e-6f) upXZ3 = Vector3.forward;
        upXZ3.Normalize();
        Vector2 upXZ = new Vector2(upXZ3.x, upXZ3.z);

        // Constrain direction within ±MaxVerticalDeviationDeg from vertical
        float maxDeg = Mathf.Clamp(MaxVerticalDeviationDeg, 0f, 89f);
        float jitterDeg = Random.Range(-maxDeg, maxDeg);
        float rad = jitterDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
        Vector2 dir2 = new Vector2(
            upXZ.x * cos - upXZ.y * sin,
            upXZ.x * sin + upXZ.y * cos
        ).normalized;

        Vector2 absDir = new Vector2(Mathf.Abs(dir2.x), Mathf.Abs(dir2.y));

        // Ensure the whole segment fits: choose start so start+dir*len remains inside rect
        float marginX = absDir.x * _pathLen;
        float marginZ = absDir.y * _pathLen;

        float minX = _usable.minX;
        float maxX = _usable.maxX - marginX;
        float minZ = _usable.minZ;
        float maxZ = _usable.maxZ - marginZ;

        if (maxX <= minX || maxZ <= minZ)
        {
            // Fallback: center small horizontal segment
            Vector3 c = new Vector3(((_usable.minX + _usable.maxX) * 0.5f), SkinSurfaceY, ((_usable.minZ + _usable.maxZ) * 0.5f));
            _pStart = new Vector3(c.x - _pathLen * 0.5f, SkinSurfaceY, c.z);
            _pEnd   = new Vector3(c.x + _pathLen * 0.5f, SkinSurfaceY, c.z);
        }
        else
        {
            float sx = Random.Range(minX, maxX);
            float sz = Random.Range(minZ, maxZ);

            _pStart = new Vector3(sx, SkinSurfaceY, sz);
            Vector3 delta = new Vector3(dir2.x, 0f, dir2.y) * _pathLen;
            _pEnd = _pStart + delta;
        }

        // Enforce top-to-bottom ordering relative to camera view (optional)
        if (ForceTopToBottom)
        {
            Vector3 viewUp = Cam ? Cam.transform.up : Vector3.up;
            Vector3 upOnSkin = new Vector3(viewUp.x, 0f, viewUp.z);
            if (upOnSkin.sqrMagnitude < 1e-6f) upOnSkin = Vector3.forward;
            upOnSkin.Normalize();

            float sDot = Vector3.Dot(_pStart, upOnSkin);
            float eDot = Vector3.Dot(_pEnd,   upOnSkin);
            if (sDot < eDot)
            {
                var tmp = _pStart; _pStart = _pEnd; _pEnd = tmp;
            }
        }

        _dir = (_pEnd - _pStart).normalized;

        var y = SkinSurfaceY + LineYOffset;
        Vector3 v0 = new Vector3(_pStart.x, y, _pStart.z);
        Vector3 v1 = new Vector3(_pEnd.x,   y, _pEnd.z);

        // Update line renderer
        _line.startColor = PathColor;
        _line.endColor = PathColor;
        _line.widthMultiplier = PathWidth;
        _line.SetPosition(0, v0);
        _line.SetPosition(1, v1);
        _line.enabled = true;
    }

    void SetupMarkers()
    {
        DestroyMarkers();
        float y = SkinSurfaceY + LineYOffset;

        _startDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _startDot.name = "IncisionStart (Green)";
        _startDot.transform.position = new Vector3(_pStart.x, y, _pStart.z);
        _startDot.transform.localScale = Vector3.one * (MarkerRadius * 2f);
        SetColor(_startDot, Color.green);
        MakeTrigger(_startDot);

        // Yellow highlight (initially hidden)
        _startHighlight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _startHighlight.name = "StartHighlight (Yellow)";
        _startHighlight.transform.position = new Vector3(_pStart.x, y, _pStart.z);
        _startHighlight.transform.localScale = Vector3.one * (MarkerRadius * 2f * StartHighlightScale);
        SetColor(_startHighlight, StartHighlightColor);
        var shCol = _startHighlight.GetComponent<Collider>();
        if (shCol) shCol.enabled = false; // no physics on highlight
        if (_startHighlight.TryGetComponent<MeshRenderer>(out var shMr))
        {
            // Make it slightly transparent-looking (simple approach)
            shMr.material.EnableKeyword("_EMISSION");
            shMr.material.SetColor("_EmissionColor", StartHighlightColor * 0.5f);
            shMr.material.color = StartHighlightColor;
        }
        _startHighlight.SetActive(false); // hidden until touched

        _endDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _endDot.name = "IncisionEnd (Red)";
        _endDot.transform.position = new Vector3(_pEnd.x, y, _pEnd.z);
        _endDot.transform.localScale = Vector3.one * (MarkerRadius * 2f);
        SetColor(_endDot, Color.red);
        MakeTrigger(_endDot);

        _startTouched = false;
    }

    void DestroyMarkers()
    {
        if (_startDot) Destroy(_startDot);
        if (_startHighlight) Destroy(_startHighlight);
        if (_endDot) Destroy(_endDot);
        _startDot = _endDot = _startHighlight = null;
    }

    static void SetColor(GameObject go, Color c)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = c;
            mr.material = mat;
        }
    }

    static void MakeTrigger(GameObject go)
    {
        var col = go.GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    void ShowModeButtons()
    {
        ShowEndButtons(); // reuse
    }

    void EnterFreezeOverlay()
    {
        // Freeze tool movement and hide choice buttons; show a single Start button
        FreezeTool = true;
        AllowPainting = false;
        if (TimerText) TimerText.text = "";

        HideEndButtons();
        ShowStartButton();
        _state = GameState.Freeze;
    }
        void ShowStartButton()
    {
        if (!Cam) Cam = Camera.main;
        Vector3 center = Cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, EndUIButtonDistance));
        Quaternion face = Quaternion.LookRotation(center - Cam.transform.position, Cam.transform.up);

        _btnStart = PenetrationButton.Create("Start", center, face, OnStartClicked, ScalpelTip);
        if (_btnStart) _btnStart.transform.localScale = new Vector3(0.18f, 0.06f, 0.02f);
    }

    void HideStartButton()
    {
        if (_btnStart) Destroy(_btnStart.gameObject);
        _btnStart = null;
    }

    void ShowEndButtons()
    {
        HideEndButtons();
        if (!Cam) Cam = Camera.main;
        if (!Cam)
        {
            Debug.LogWarning("incisionGame: No camera found for end buttons.");
            return;
        }
        Vector3 center = Cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, EndUIButtonDistance));
        Quaternion face = Quaternion.LookRotation(center - Cam.transform.position, Cam.transform.up);

        Vector3 up = Cam.transform.up;
        float gap = EndUIButtonVerticalGap * 0.5f;

        _btnPlayAgain = PenetrationButton.Create("Play Again",
            center + up * gap, face, OnPlayAgainClicked, ScalpelTip);

        // Rename to Free Mode
        _btnMainMenu = PenetrationButton.Create("Free Mode",
            center - up * gap, face, OnMainMenuClicked, ScalpelTip);

        if (_btnPlayAgain) _btnPlayAgain.transform.localScale = new Vector3(0.18f, 0.06f, 0.02f);
        if (_btnMainMenu) _btnMainMenu.transform.localScale = new Vector3(0.18f, 0.06f, 0.02f);
    }

    void HideEndButtons()
    {
        if (_btnPlayAgain) Destroy(_btnPlayAgain.gameObject);
        if (_btnMainMenu) Destroy(_btnMainMenu.gameObject);
        _btnPlayAgain = _btnMainMenu = null;
    }

    void OnPlayAgainClicked()
    {
        // Prepare for restart, then enter freeze and show Start
        if (RobotMover) RobotMover.ResetKnifeAndWounds();
        if (Painter && ClearPainterOnReset) Painter.Clear();

        _pendingChoice = PendingChoice.PlayAgain;
        EnterFreezeOverlay();
    }

    void OnMainMenuClicked()
    {
        // Rename to Free Mode: same flow
        if (RobotMover) RobotMover.ResetKnifeAndWounds();
        if (Painter && ClearPainterOnReset) Painter.Clear();

        _pendingChoice = PendingChoice.FreeMode;
        EnterFreezeOverlay();
    }

    void OnStartClicked()
    {
        // Unfreeze and go to chosen mode
        FreezeTool = false;

        if (_pendingChoice == PendingChoice.PlayAgain)
        {
            if (RobotMover) RobotMover.PlayAgain();
            StartRound();
        }
        else if (_pendingChoice == PendingChoice.FreeMode)
        {
            // Free mode: clear lines/markers and allow manual painting toggle if needed
            EnterFreeMode();
        }

        _pendingChoice = PendingChoice.None;
        HideStartButton();
    }

    void OnResetKey()
    {
        // User requests reset flow anywhere
        FreezeTool = true;
        AllowPainting = false;

        // Reset painter/tool visuals
        ResetRobotAndPainter();

        // Show choice buttons (Play Again / Free Mode)
        HideEndButtons();
        ShowModeButtons(); // reuse end buttons layout
        _state = GameState.EndScreen;
    }

    void EnterMainMenu()
    {
        HideEndButtons();
        _line.enabled = false;
        DestroyMarkers();
        ResetScore();
        _timeLeft = 0f;
        _state = GameState.Idle;
        AllowPainting = false;
        if (Painter) Painter.enabled = false;
    }
    
    void TryClearPainterTexture()
    {
        if (Painter == null) return;

        bool cleared = false;
        try
        {
            var t = Painter.GetType();
            var m = t.GetMethod(PainterClearMethodName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (m != null)
            {
                m.Invoke(Painter, null);
                cleared = true;
                Debug.Log($"incisionGame: Painter cleared via method '{PainterClearMethodName}'.");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"incisionGame: Painter clear method exception: {e.Message}");
        }
        
        if (!cleared && TryRenderTextureClearFallback)
        {
            try
            {
                if (ClearAnyRenderTexturesOnPainter(Painter))
                {
                    cleared = true;
                    Debug.Log("incisionGame: Painter cleared via RenderTexture fallback.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"incisionGame: RT fallback failed: {e.Message}");
            }
        }

        if (!cleared)
        {
            // Fallback suggestion (uncomment & adapt if you expose a RenderTexture or material):
            // Example (pseudo):
            // if (Painter.RenderTex) {
            //     var active = RenderTexture.active;
            //     Graphics.SetRenderTarget(Painter.RenderTex);
            //     GL.Clear(true, true, Color.clear);
            //     RenderTexture.active = active;
            //     cleared = true;
            // }
            if (!cleared)
                Debug.LogWarning("incisionGame: Painter not cleared. Provide a public Clear() or set PainterClearMethodName.");
        }
    }

    void ResetRobotAndPainter()
    {
        // Clear all wound paint
        if (Painter != null)
        {
            if (!Painter.enabled) Painter.enabled = true;
            try
            {
                Painter.Clear(); // direct call
            }
            catch
            {
                TryClearPainterTexture(); // fallback
            }
        }
        
        if (RobotMover != null)
        {
            RobotMover.ResetKnifeAndWounds();
        }

        // Reset local game state
        ResetScore();
        AllowPainting = false;    
    }

    bool ClearAnyRenderTexturesOnPainter(object obj)
    {
        if (obj == null) return false;
        bool any = false;
        var t = obj.GetType();

        // Fields
        var fields = t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        foreach (var f in fields)
        {
            if (typeof(RenderTexture).IsAssignableFrom(f.FieldType))
            {
                var rt = f.GetValue(obj) as RenderTexture;
                if (rt != null) { ClearRT(rt); any = true; }
            }
        }

        // Properties
        var props = t.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        foreach (var p in props)
        {
            if (p.CanRead && typeof(RenderTexture).IsAssignableFrom(p.PropertyType))
            {
                var rt = p.GetValue(obj, null) as RenderTexture;
                if (rt != null) { ClearRT(rt); any = true; }
            }
        }

        return any;
    }

    void ClearRT(RenderTexture rt)
    {
        var active = RenderTexture.active;
        Graphics.SetRenderTarget(rt);
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = active;
    }
}

/// 3D "button" that can be clicked by mouse or by penetrating with the scalpel tip.
/// It becomes a simple cube with text. On penetration beyond a small threshold, it triggers once.
public class PenetrationButton : MonoBehaviour
{
    public Action OnClick;
    public Transform Tip;
    public float TriggerDepth = 0.004f; // meters of penetration needed
    public float Cooldown = 0.75f;

    float _lastTriggerTime;
    TextMesh _label;

    public static PenetrationButton Create(string text, Vector3 pos, Quaternion rot, Action onClick, Transform tip)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"Button - {text}";
        go.transform.position = pos;
        go.transform.rotation = rot;
        go.transform.localScale = new Vector3(0.12f, 0.04f, 0.02f);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.9f, 0.9f, 0.9f);
            mr.material = mat;
        }

        var col = go.GetComponent<Collider>();
        col.isTrigger = true;

        var btn = go.AddComponent<PenetrationButton>();
        btn.OnClick = onClick;
        btn.Tip = tip;

        // Add text label
        GameObject label = new GameObject("Label");
        label.transform.SetParent(go.transform, worldPositionStays: false);
        label.transform.localPosition = new Vector3(0f, 0f, -0.013f); // slightly in front
        label.transform.localRotation = Quaternion.identity;
        var tm = label.AddComponent<TextMesh>();
        tm.text = text;
        tm.alignment = TextAlignment.Center;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = Color.black;
        tm.fontSize = 64;
        tm.characterSize = 0.015f;
        btn._label = tm;

        return btn;
    }

    void OnMouseDown()
    {
        // Mouse click support
        Trigger();
    }

    void OnTriggerStay(Collider other)
    {
        if (!Tip) return;
        // Trigger when the configured tip (or its children) penetrates this button
        if (other.transform == Tip || other.transform.IsChildOf(Tip))
        {
            float depth = ComputePenetrationDepth();
            if (depth >= TriggerDepth) Trigger();
        }
    }

    float ComputePenetrationDepth()
    {
        if (!Tip) return 0f;
        // Measure along button's -forward axis (assuming front face is -Z)
        Vector3 local = transform.InverseTransformPoint(Tip.position);
        // Front face (towards camera) is at z = -0.5 in local cube space, back face at +0.5
        float halfZ = 0.5f;
        float z = local.z; // z deeper into the cube is positive
        // Penetration beyond the front face
        float frontFaceZ = -halfZ + 0.0f;
        float localPen = z - frontFaceZ; // if > 0, we've gone past the front face
        // Convert to world meters: account for local scale Z
        float scaleZ = transform.lossyScale.z;
        float meters = localPen * scaleZ;
        return Mathf.Max(0f, meters);
    }

    void Trigger()
    {
        if (Time.time - _lastTriggerTime < Cooldown) return;
        _lastTriggerTime = Time.time;
        OnClick?.Invoke();

        // Visual feedback
        var mr = GetComponent<MeshRenderer>();
        if (mr)
        {
            mr.material.color = Color.Lerp(mr.material.color, new Color(0.7f, 1f, 0.7f), 0.5f);
        }
    }
}