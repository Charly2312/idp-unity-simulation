using UnityEngine;

public class SimpleKnifeMover : MonoBehaviour
{
    public Transform CombatKnife;        // Parent of KnifeTip
    public Transform KnifeTip;       // Assign a small sphere/GameObject

    public Transform Skin;           // Assign your Skin plane
    public Camera Cam;               // Leave empty to use Camera.main
    public WoundPainter Painter; 

    public float HoverHeight = 0.4f;  // meters above skin when not pressing space
    public float DepthSpeed = 0.01f;   // meters/second when holding space
    public float StampInterval = 0.1f; // seconds between paint stamps
    public float StampStrength = 1f;
    public float BladeCheckDistance = 0.5f; // raycast distance from blade

    float _currentDepth = 0f;
    float _stampTimer = 0f;

    void Update()
    {
        if (!KnifeTip || !Skin) return;
        if (!Cam) Cam = Camera.main;
        if (!Cam) return;

        if (!GetMouseOnSkin(out var mouseHitPos, out RaycastHit mouseHit)) return;

        Vector3 targetPos = mouseHitPos;

        if (Input.GetKey(KeyCode.Space))
        {
            _currentDepth += DepthSpeed * Time.deltaTime;
            targetPos -= Skin.up * _currentDepth;
            Debug.Log($"Spacebar held - Depth: {_currentDepth:F4}m");

            CombatKnife.position = targetPos;
            CombatKnife.rotation = Quaternion.LookRotation(Skin.up) * Quaternion.Euler(160, 180, 90);

           if (Painter)
            {
                // Raycast from blade edge to find where it hits skin
                Vector3 bladePos = KnifeTip.position;
                Vector3 bladeDir = -Skin.up;
                
                Ray bladeRay = new Ray(bladePos + Skin.up * 0.1f, bladeDir); // start slightly above
                
                if (Physics.Raycast(bladeRay, out RaycastHit bladeHit, BladeCheckDistance))
                {
                    if (bladeHit.transform == Skin || bladeHit.transform.IsChildOf(Skin))
                    {
                        Debug.DrawLine(bladePos, bladeHit.point, Color.red, 0.1f);
                        
                        // Stamp at the blade intersection point
                        if (bladeHit.collider is MeshCollider && bladeHit.textureCoord != Vector2.zero)
                        {
                            Vector2 rawUV = bladeHit.textureCoord;
                            Debug.Log($"Raw UV from mesh: {rawUV}, Hit world pos: {bladeHit.point}");
                            
                            // Try different UV mappings to fix scaling:
                            Vector2 uv = rawUV; // Start with no modification
                            
                            // If scaling is wrong, try these one at a time:
                            // uv = new Vector2(rawUV.x * 2f, rawUV.y * 2f); // Scale up 2x
                            // uv = new Vector2(rawUV.x * 0.5f, rawUV.y * 0.5f); // Scale down 2x
                            
                            _stampTimer += Time.deltaTime;
                            if (_stampTimer >= StampInterval)
                            {
                                _stampTimer = 0f;
                                Painter.StampAtUV(uv, Painter.BrushRadiusMeters, StampStrength);
                                Debug.Log($"STAMPED with UV {uv} (from raw {rawUV})");
                            }
                        }
                    }
                }
            }
        }
        else
        {
            _currentDepth = 0f;
            _stampTimer = 0f;
            targetPos += Skin.up * HoverHeight;
            
            CombatKnife.position = targetPos;
            CombatKnife.rotation = Quaternion.LookRotation(Skin.up) * Quaternion.Euler(160, 180, 90); 
        }
    }

    bool GetMouseOnSkin(out Vector3 hitPos, out RaycastHit hit)
    {
        hitPos = default; hit = default;
        Ray ray = Cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out hit, 100f))
        {
            if (hit.transform == Skin || hit.transform.IsChildOf(Skin))
            {
                hitPos = hit.point;
                Debug.DrawLine(ray.origin, hitPos, Color.green, 0.02f);
                return true;
            }
        }
        // Fallback infinite plane
        Plane plane = new Plane(Skin.up, Skin.position);
        if (plane.Raycast(ray, out float t))
        {
            hitPos = ray.GetPoint(t);
            return true;
        }
        return false;
    }
}