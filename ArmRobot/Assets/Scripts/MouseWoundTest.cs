using UnityEngine;

public class SimpleKnifeMover : MonoBehaviour
{
    public Transform CombatKnife;        // Parent of KnifeTip
    public Transform KnifeTip;       // Assign a small sphere/GameObject

    public Transform Skin;           // Assign your Skin plane
    public Camera Cam;               // Leave empty to use Camera.main
    public WoundPainter Painter; 

    public float moveSpeed = 0.1f;       // Movement speed for keyboard controls
    public float StampInterval = 0.1f; // seconds between paint stamps
    public float StampStrength = 1f;
    public float MaxDepth = 0.0424f; // Maximum depth in meters (42.4mm)
    
    [Header("Skin Surface Detection")]
    public bool AutoDetectSkinSurface = true; // Auto-detect from Skin object position
    public float SkinSurfaceY = 0.8764f; // Manual override if AutoDetect is false
    
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

    void Start()
    {
        if (!KnifeTip) KnifeTip = transform;
        var skinGO = GameObject.FindGameObjectWithTag("Skin");
        if (skinGO) skinTf = skinGO.transform;
        if (!Skin && skinTf) Skin = skinTf;
        
        // Auto-detect or use manual value
        if (AutoDetectSkinSurface && Skin != null)
        {
            _actualSkinSurfaceY = SkinSurfaceY;
            Debug.Log($"Auto-detected Skin Surface Y: {_actualSkinSurfaceY:F4} units");
        }
        else
        {
            _actualSkinSurfaceY = SkinSurfaceY;
            Debug.Log($"Using manual Skin Surface Y: {_actualSkinSurfaceY:F4} units");
        }
        
        Debug.Log($"=== INITIALIZATION ===");
        Debug.Log($"Skin position: {Skin.position}");
        Debug.Log($"KnifeTip position: {KnifeTip.position}");
        Debug.Log($"KnifeTip Y: {KnifeTip.position.y:F4} units");
        Debug.Log($"Actual Skin surface Y threshold: {_actualSkinSurfaceY:F4} units");
        Debug.Log($"Initial depth would be: {(_actualSkinSurfaceY - KnifeTip.position.y):F4} units");
        Debug.Log($"======================");
    }

    void Update()
    {
        if (!KnifeTip || !Skin) return;
        if (!Cam) Cam = Camera.main;

        // Keyboard movement controls
        Vector3 movement = Vector3.zero;
        
        // A/D - Left/Right
        if (Input.GetKey(KeyCode.A)) movement.x -= 1f;
        if (Input.GetKey(KeyCode.D)) movement.x += 1f;
        
        // W/S - Up/Down
        if (Input.GetKey(KeyCode.W)) movement.y += 1f;
        if (Input.GetKey(KeyCode.S)) movement.y -= 1f;
        
        // Q/E - Forward/Backward
        if (Input.GetKey(KeyCode.Q)) movement.z -= 1f;
        if (Input.GetKey(KeyCode.E)) movement.z += 1f;
        
        // Apply movement
        CombatKnife.Translate(movement * moveSpeed * Time.deltaTime, Space.World);
        
        // Set knife rotation
        CombatKnife.rotation = Quaternion.LookRotation(Skin.up) * Quaternion.Euler(160, 180, 90);

        // Automatically check depth and paint wound
        if (Painter)
        {
            // Custom depth calculation: depth = SkinSurfaceY - knifeTipY
            // If knife Y < SkinSurfaceY, it's below the surface (cutting)
            float knifeTipY = KnifeTip.position.y;
            float rawDepth = 0.8764f - knifeTipY;
            float actualDepth = Mathf.Max(0f, rawDepth); // Only clamp negative values to 0
            
            string status = rawDepth < 0 ? "ABOVE SKIN (in air)" : "BELOW SKIN (CUTTING)";
            Debug.Log($"KnifeTip Y: {knifeTipY:F4} | Skin Surface: {0.8764} | Raw Depth: {rawDepth:F4} | Actual Depth: {actualDepth:F4} | {status}");

            // Automatically paint if knife is below skin surface (depth > 0)
            if (actualDepth > 0f)
            {
                // Calculate depth percentage (0 to 1) for color intensity
                // Don't clamp actualDepth to MaxDepth - use full raw depth value
                float depthPercent = Mathf.Clamp01(actualDepth / MaxDepth);
                
                // Update depth color based on actual depth
                Painter.SetDepthFromTip(KnifeTip.position, Painter.MaxDepthMeters);
                
                // Project knife tip position onto skin plane to get UV
                Plane skinPlane = new Plane(Skin.up, Skin.position);
                Vector3 projectedPos = skinPlane.ClosestPointOnPlane(KnifeTip.position);
                
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
                    
                    Debug.Log($"🔴 AUTO-STAMPED at UV ({u:F3}, {v:F3}) | Depth: {actualDepth:F4} ({depthPercent * 100f:F0}%) | Inner: {innerStrength:F2}, Outer: {outerStrength:F2}");
                }
            }
            else
            {
                // Reset stamp timer when not cutting
                _stampTimer = 0f;
            }
        }
    }
}