using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class SimpleKnifeMover : MonoBehaviour
{
    // === Serial Communication ===
    [Header("Serial Settings")]
    [Tooltip("Serial COM port name (e.g. COM6). Change at runtime to auto-reopen.")]
    public bool UseSerialInput = false;  
    public string portName = "COM6";
    public int baud = 115200;
    public int readTimeoutMs = 200;

    SerialPort sp;
    Thread reader;
    volatile bool running;
    readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();

    private bool _wasCutting = false;
    private float _lastSendTime = -999f;
    private float _sendCooldown = 0.05f; // seconds
    private readonly object _serialLock = new object();

    public Transform Scalpel;            // New: assign your Scalpel GameObject here
    public Transform ScalpelTip;            // New: assign your Scalpel GameObject here

    public Transform ScalpelPivot;
    Transform Tool => ScalpelPivot ? ScalpelPivot : (Scalpel ? Scalpel : transform);

    public Transform Skin;           // Assign your Skin plane
    public Camera Cam;               // Leave empty to use Camera.main
    public WoundPainter Painter;

    [SerializeField] private Vector3 startPos;
    [SerializeField] private Quaternion startRot;

    bool cutting;
    bool ignored;
    bool isStuck;
    Vector3 lastPos;
    Vector3 lastTipPos;

    // === Movement Settings === // Toggle between serial and keyboard control
    public float rotationSpeed = 150.0f;    // Keyboard rotation speed

    public float moveSpeed = 0.1f;       // Movement speed for keyboard controls
    public float StampInterval = 0.1f; // seconds between paint stamps
    public float StampStrength = 1f;
    public float MaxDepth = 0.0424f; // Maximum depth in meters (42.4mm)

    [Header("Skin Surface Detection")]
    public bool AutoDetectSkinSurface = true; // Auto-detect from Skin object position
    public float SkinSurfaceY = 0.8464f; // Manual override if AutoDetect is false

    public float CurrentDepthMeters { get; private set; } // this is used for incisionGame
    
    [Header("Gradient Settings")]
    public float InnerRadiusPercent = 0.5f; // Inner radius as percentage of brush radius (darker red)
    public float InnerStrengthMultiplier = 1.0f; // Multiplier for inner region
    public float OuterStrengthMultiplier = 0.5f; // Multiplier for outer region (lighter)

    [Header("Debug")]
    public float DepthThreshold = 0.001f; // Minimum depth to consider "cutting" (1mm)

    // Manual UV mapping based on skin vertices
    private Vector2 _skinMin = new Vector2(54.69f, -2.26f); // (minX, minZ)
    private Vector2 _skinMax = new Vector2(55.42f, -1.74f); // (maxX, maxZ)

    Transform skinTf;
    float _stampTimer = 0f;
    float _actualSkinSurfaceY; // The actual Y value used for calculations

    // === Serial Data Processing ===
    private const float MM_TO_M = 0.001f;
    private Vector3 originPos = new Vector3(55.0154f, 0.846f, -2.247592f);
    private Quaternion originRot;
    Vector3 accumMeters;
    private float prevX_mm, prevY_mm, prevZ_mm;
    private bool firstPacketReceived = false;

    [Header("Rotation (Serial)")]
    public bool useOriginRelativeRotation = true;  
    [Range(0f, 1f)] public float rotationLerp = 0.5f; // 1 = snap, <1 = smooth Slerp per update
    public float serialRotationScale = 1f;          // used only in incremental mode
    public bool serialAnglesAreDegrees = true;

    [Header("Translation (Serial Scale)")]
    [Tooltip("Per-axis multiplier applied to incoming serial deltas (robot frame) dx, dy, dz in millimeters. Use negative to invert.")]
    public float serialScaleX = 2.0f; // scales dx_mm
    public float serialScaleY = 1f; // scales dy_mm
    public float serialScaleZ = 1f; // scales dz_mm

    [Tooltip("Multiply yaw by this. Use -1 to flip yaw direction.")]
    public float yawSign = -1f;
    public float pitchSign = 1f; 
    public float rollSign = 1f;

    // Track serial orientation
    private float originYaw_deg, originPitch_deg, originRoll_deg; // from first packet
    private float prevYaw_deg, prevPitch_deg, prevRoll_deg;       // for incremental mode

    private Vector3 _initialScalpelPos;
    private Quaternion _initialScalpelRot;

    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            // Capture the current scene pose as the spawn (play-again) pose.
            var tool = Tool;
            startPos = tool.position;
            startRot = tool.rotation;
        }
    }

    void Awake()
    {
        var tool = Tool;
        if (Scalpel)
        {
            _initialScalpelPos = Scalpel.position;
            _initialScalpelRot = Scalpel.rotation;
        }
    }

    void Start()
    {
        if (!ScalpelTip) ScalpelTip = Scalpel ? Scalpel : transform;

        ResetKnifeAndWounds();
        
        var skinGO = GameObject.FindGameObjectWithTag("Skin");
        if (skinGO) skinTf = skinGO.transform;
        if (!Skin && skinTf) Skin = skinTf;

        // If OnValidate didn’t run (built player), capture pose now
        var tool = Tool;

        if (startRot == Quaternion.identity && startPos == Vector3.zero)
        {
            startPos = tool.position;
            startRot = tool.rotation;
        }

        // Do NOT reposition here; keep scene spawn
        lastPos = tool.position;
        tool.position = startPos;
        tool.rotation = startRot;
        originPos = startPos;
        originRot = startRot;
        lastTipPos = (ScalpelTip ? ScalpelTip.position : tool.position);

        _actualSkinSurfaceY = SkinSurfaceY;

        if (UseSerialInput) InitializeSerialPort();
    }

    public void PlayAgain()
    {
        ResetKnifeAndWounds();
    }

    public void ResetKnifeAndWounds()
    {
        var tool = Tool;

        // Reset transform and cached origins
        tool.position = startPos;
        tool.rotation = startRot;
        originPos = startPos;
        originRot = startRot;

        // Reset motion/state
        accumMeters = Vector3.zero;
        lastPos = tool.position;
        lastTipPos = (ScalpelTip != null) ? ScalpelTip.position : tool.position;

        cutting = false;
        ignored = false;
        isStuck = false;

        _stampTimer = 0f;
        _wasCutting = false;

        if (Painter) Painter.Clear();
    }

    bool CanPaintNow() => incisionGame.AllowPainting;

    void InitializeSerialPort()
    {

        sp = new SerialPort(portName, baud)
        {
            NewLine = "\n",
            ReadTimeout = readTimeoutMs,
            DtrEnable = true,
            RtsEnable = true,
            Encoding = Encoding.ASCII
        };

        try
        {
            sp.Open();
            running = true;
            reader = new Thread(ReadLoop) { IsBackground = true };
            reader.Start();
            Debug.Log($"Opened {sp.PortName} @ {baud} baud");
        }
        catch (Exception ex)
        {
            Debug.Log($"Failed to open serial port: {ex.Message}");
        }
    }

    void EnsureSerialForWrite()
    {
        if (sp != null && sp.IsOpen) return;

        sp = new SerialPort(portName, baud)
        {
            NewLine = "\n",
            ReadTimeout = readTimeoutMs,
            DtrEnable = true,
            RtsEnable = true,
            Encoding = Encoding.ASCII
        };

        try
        {
            sp.Open();
            Debug.Log($"Opened {sp.PortName} for TX @ {baud}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Serial open (TX) failed: {ex.Message}");
        }
    }

    void SendMsgIfAllowed()
    {
        if (Time.time - _lastSendTime < _sendCooldown) return;
        EnsureSerialForWrite();
        if (sp == null || !sp.IsOpen) return;

        try
        {
            lock (_serialLock)
            {
                sp.WriteLine("0x01");
            }
            _lastSendTime = Time.time;
            // Debug.Log("TX: hii");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Serial write failed: {ex.Message}");
        }
    }

    void ReadLoop()
    {
        while (running && sp != null && sp.IsOpen)
        {
            try
            {
                string line = sp.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    // Debug.Log($"📥 RAW RECEIVED: '{line.Trim()}'");
                    inbox.Enqueue(line.Trim());
                }
            }
            catch (TimeoutException) { /* ignore */ }
            catch (InvalidOperationException) { break; }
            catch (System.IO.IOException) { /* transient; continue */ }
        }
    }

    void ProcessSerialData()
    {

        if (incisionGame.FreezeTool)
        {
            //DrainSerialInbox();
            DrainSerialInboxKeepLast();
            return;
        }

        // Drain queue and keep only the most recent message
        string last = null;
        while (inbox.TryDequeue(out var msg)) last = msg;
        if (string.IsNullOrEmpty(last)) return;

        // Expected: [x, y, z, yaw, pitch, roll, ...]
        var parts = last.Trim('[', ']').Split(',');
        if (parts.Length < 6) return;

        // Fast, allocation-light parsing
        if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x_mm)) return;
        if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y_mm)) return;
        if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var z_mm)) return;

        if (!float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var yawIn)) return;
        if (!float.TryParse(parts[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pitchIn)) return;
        if (!float.TryParse(parts[5].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rollIn)) return;

        // Normalize to DEGREES internally
        float yaw   = serialAnglesAreDegrees ? yawIn   : yawIn   * Mathf.Rad2Deg;
        float pitch = serialAnglesAreDegrees ? pitchIn : pitchIn * Mathf.Rad2Deg;
        float roll  = serialAnglesAreDegrees ? rollIn  : rollIn  * Mathf.Rad2Deg;

        // Debug.Log(    $"x={x_mm:F2} y={y_mm:F2} z={z_mm:F2} mm | " +
        //               $"yaw={yawIn:F3} pitch={pitchIn:F3} roll={rollIn:F3} rad | " +
        //               $"yaw={yaw:F1} pitch={pitch:F1} roll={roll:F1} deg");

        var tool = Tool;
        if (!firstPacketReceived)
        {
            // translation
            prevX_mm = x_mm; prevY_mm = y_mm; prevZ_mm = z_mm;
            accumMeters = Vector3.zero;

            if (tool) { originPos = tool.position; originRot = tool.rotation; }

            // rotation
            originYaw_deg = yaw; originPitch_deg = pitch; originRoll_deg = roll;
            prevYaw_deg = yaw; prevPitch_deg = pitch; prevRoll_deg = roll;
          
            firstPacketReceived = true;
            return;
        }

        // Compute deltas (mm)
        float dx_mm = -(x_mm - prevX_mm);
        float dy_mm = (y_mm - prevY_mm);
        float dz_mm = (z_mm - prevZ_mm);
        prevX_mm = x_mm; prevY_mm = y_mm; prevZ_mm = z_mm;

        dx_mm *= serialScaleX;
        dy_mm *= serialScaleY;
        dz_mm *= serialScaleZ;

        // Map robot → Unity axes (tune if needed)
        Vector3 deltaUnity = new Vector3(
            dy_mm * MM_TO_M,   // Robot Y → Unity X
            dz_mm * MM_TO_M,   // Robot Z → Unity Y
            dx_mm * MM_TO_M    // Robot X → Unity Z
        );

        accumMeters += deltaUnity;

        if (tool) tool.position = originPos + accumMeters;
        if (tool)
        {
            if (useOriginRelativeRotation)
            {
                // Map absolute serial pose relative to first packet (no drift, “home” behavior)
                float offYaw   = Mathf.DeltaAngle(originYaw_deg,   yaw) * yawSign;  
                float offPitch = Mathf.DeltaAngle(originPitch_deg, pitch) * pitchSign;
                float offRoll  = Mathf.DeltaAngle(originRoll_deg,  roll * rollSign);

                Quaternion target = originRot * Quaternion.Euler(offPitch, offYaw, offRoll);
                tool.rotation = rotationLerp >= 1f
                    ? target
                    : Quaternion.Slerp(tool.rotation, target, rotationLerp);
            }
            else
            {
                float dYaw   = Mathf.DeltaAngle(prevYaw_deg,   yaw)   * serialRotationScale * yawSign;
                float dPitch = Mathf.DeltaAngle(prevPitch_deg, pitch) * serialRotationScale * pitchSign;
                float dRoll  = Mathf.DeltaAngle(prevRoll_deg,  roll)  * serialRotationScale * rollSign;

                prevYaw_deg = yaw; prevPitch_deg = pitch; prevRoll_deg = roll;
                tool.Rotate(new Vector3(dPitch, dYaw, dRoll), Space.Self);
            }
        }

        PaintWound();
    }

    void DrainSerialInbox()
    {
        while (inbox.TryDequeue(out _)) { }
    }

    void DrainSerialInboxKeepLast()
    {
        string last = null;
        while (inbox.TryDequeue(out var item))
        {
            last = item;
        }
        if (last != null) inbox.Enqueue(last);
    }

    void ManualKeyboardControl()
    {
        // Keyboard movement controls
        var tool = Tool;
        if (!tool) return;
        Vector3 movement = Vector3.zero;

        // A/D - Left/Right
        if (Input.GetKey(KeyCode.A)) movement.x -= 1f;
        if (Input.GetKey(KeyCode.D)) movement.x += 1f;

        // Q/E - Up/Down
        if (Input.GetKey(KeyCode.Q)) movement.y += 1f;
        if (Input.GetKey(KeyCode.E)) movement.y -= 1f;

        // W/S - Forward/Backward
        if (Input.GetKey(KeyCode.S)) movement.z -= 1f;
        if (Input.GetKey(KeyCode.W)) movement.z += 1f;

        // Apply movement
        tool.Translate(movement * moveSpeed * Time.deltaTime, Space.World);

        // Rotation controls (optional)
        float yaw = 0f, pitch = 0f, roll = 0f;
        if (Input.GetKey(KeyCode.J)) yaw = -1f;
        if (Input.GetKey(KeyCode.L)) yaw = 1f;
        if (Input.GetKey(KeyCode.I)) pitch = -1f;
        if (Input.GetKey(KeyCode.K)) pitch = 1f;
        if (Input.GetKey(KeyCode.U)) roll = -1f;
        if (Input.GetKey(KeyCode.O)) roll = 1f;

        Vector3 rotation = new Vector3(pitch, yaw, roll);
        tool.Rotate(rotation * rotationSpeed * Time.deltaTime, Space.Self);
        PaintWound();
    }

    void PaintWound()
    {
        if (!incisionGame.AllowPainting) return;
        // Automatically check depth and paint wound
        if (Painter)
        {
            // Custom depth calculation: depth = SkinSurfaceY - ScalpelTipY
            // If knife Y < SkinSurfaceY, it's below the surface (cutting)
            float ScalpelTipY = ScalpelTip.position.y;
            float rawDepth = 0.8764f - ScalpelTipY;
            float actualDepth = Mathf.Max(0f, rawDepth); // Only clamp negative values to 0
            
            CurrentDepthMeters = actualDepth; //for game

            bool CutSkin = actualDepth > 0f;
            string status = rawDepth < 0 ? "ABOVE SKIN (in air)" : "BELOW SKIN (CUTTING)";
            //Debug.Log($"ScalpelTip Y: {ScalpelTipY:F4} | Skin Surface: {0.8764} | Raw Depth: {rawDepth:F4} | Actual Depth: {actualDepth:F4} | {status}");

            // if (!_wasCutting && CutSkin)
            // {
            //     SendMsgIfAllowed();
            //     //Debug.Log("sent message to ard");
            // }

            _wasCutting = CutSkin;
            // Automatically paint if knife is below skin surface (depth > 0)
            if (actualDepth > 0f)
            {
                // Calculate depth percentage (0 to 1) for color intensity
                // Don't clamp actualDepth to MaxDepth - use full raw depth value
                float depthPercent = Mathf.Clamp01(actualDepth / MaxDepth);

                // Update depth color based on actual depth
                Painter.SetDepthFromTip(ScalpelTip.position, Painter.MaxDepthMeters);

                // Project knife tip position onto skin plane to get UV
                Plane skinPlane = new Plane(Skin.up, Skin.position);
                Vector3 projectedPos = skinPlane.ClosestPointOnPlane(ScalpelTip.position);

                float u = 1f - ((projectedPos.x - _skinMin.x) / (_skinMax.x - _skinMin.x));
                float v = 1f - ((projectedPos.z - _skinMin.y) / (_skinMax.y - _skinMin.y) + 0.2f);

                Vector2 uv = new Vector2(u, v);

                _stampTimer += Time.deltaTime;
                if (_stampTimer >= StampInterval)
                {
                    _stampTimer = 0f;

                    // Paint two circles: inner (darker) and outer (lighter)
                    float fullRadius = Painter.BrushRadiusMeters;
                    float innerRadius = fullRadius * InnerRadiusPercent;

                    // Paint inner circle (darker red) - strength increases with depth
                    float innerStrength = StampStrength * depthPercent * InnerStrengthMultiplier;
                    Painter.StampAtUV(uv, innerRadius, innerStrength);

                    // Paint outer circle (lighter red) - strength increases with depth
                    float outerStrength = StampStrength * depthPercent * OuterStrengthMultiplier;
                    Painter.StampAtUV(uv, fullRadius, outerStrength);

                    //  Debug.Log($"🔴 AUTO-STAMPED at UV ({u:F3}, {v:F3}) | Depth: {actualDepth:F4} ({depthPercent * 100f:F0}%) | Inner: {innerStrength:F2}, Outer: {outerStrength:F2}");
                }
            }
            else
            {
                // Reset stamp timer when not cutting
                _stampTimer = 0f;
            }
        }
    }

    void Update()
    {
        if (!ScalpelTip || !Skin) return;
        if (!Cam) Cam = Camera.main;
        if (incisionGame.FreezeTool)
        {
            //if (UseSerialInput) DrainSerialInbox(); // keep port alive, avoid queue growth
            if (UseSerialInput) DrainSerialInboxKeepLast();

            return;
        }


        if (UseSerialInput) {ProcessSerialData();}
        else                
        {ManualKeyboardControl();}
        
    }

}