using UnityEngine;
using Application = UnityEngine.Application;

[ExecuteAlways]
public class WoundPainter : MonoBehaviour
{
    [Header("Mask")]
    public int MaskSize = 1024;         // RT resolution
    public float MaxDepthMm = 3f;       // depth that equals Depth01=1
    public float BrushRadiusMm = 40.0f;    // radius of stain in mm
    public bool PreviewUV = false;      // (unused here, for your own gizmos)

    [Header("Refs")]
    public Renderer skinRenderer;       // skin mesh renderer (material will be instanced)
    public Transform skin;              // plane/mesh; its Up is skin normal

    Material _mat;                      // instanced skin material
    Material _paintMat;                 // Hidden/WoundPaint
    RenderTexture _maskRT;
    Vector3 _planeNormal; float _planeD;

    void OnEnable() { Init(); }
    void OnValidate() { Init(); }

    void Init()
    {
        if (!skinRenderer) skinRenderer = GetComponent<Renderer>();
        if (skinRenderer)
        {
            _mat = Application.isPlaying ? skinRenderer.material : skinRenderer.sharedMaterial;

            // Ensure the renderer actually uses the instance in play mode
            if (Application.isPlaying) skinRenderer.material = _mat;
        }
        
        Debug.Log($"WoundPainter Init: BrushRadiusMm={BrushRadiusMm}, BrushRadiusMeters={BrushRadiusMeters}");
        
        if (_maskRT == null || _maskRT.width != MaskSize)
        {
            if (_maskRT) _maskRT.Release();
            _maskRT = new RenderTexture(MaskSize, MaskSize, 0, RenderTextureFormat.R8) { useMipMap = false, filterMode = FilterMode.Bilinear };
            Graphics.Blit(Texture2D.blackTexture, _maskRT);
        }

        if (_mat) _mat.SetTexture("_WoundMask", _maskRT);
        if (_paintMat == null)
        {
            Shader brushShader = Shader.Find("Hidden/WoundPaint");
            if (brushShader == null)
            {
                Debug.LogError("Hidden/WoundPaint shader NOT FOUND! Stamping will fail.");
            }
            else
            {
                _paintMat = new Material(brushShader);
                Debug.Log("Hidden/WoundPaint shader loaded successfully");
            }
        }

        if (skin)
        {
            _planeNormal = skin.up.normalized;
            _planeD = -Vector3.Dot(_planeNormal, skin.position);
        }
    }

    public void SetDepthFromTip(Vector3 tipWorldPos, float maxDepthMeters)
    {
        if (!_mat || !skin) return;
        float signed = Vector3.Dot(_planeNormal, tipWorldPos) + _planeD;  // >0 above plane
        float depth = Mathf.Max(0f, -signed);                              // meters below plane
        float d01 = Mathf.Clamp01(depth / Mathf.Max(1e-5f, maxDepthMeters));
        _mat.SetFloat("_Depth01", d01);
    }

    public void StampAtWorld(Vector3 worldPos, float radiusMeters, float strength01 = 1f)
    {
        if (!_paintMat || !_maskRT || !skin) return;

        // convert world → skin local
        Vector3 local = skin.InverseTransformPoint(worldPos);

        float sizeX = 10f * Mathf.Abs(skin.lossyScale.x);
        float sizeZ = 10f * Mathf.Abs(skin.lossyScale.z);

        Vector2 uv = new Vector2(-local.x / 10f + 0.5f, -local.z / 10f + 0.5f);

        float planeWorldX = 10f * Mathf.Abs(skin.lossyScale.x); // 10 * 0.7 = 7m
        float planeWorldZ = 10f * Mathf.Abs(skin.lossyScale.z); // 10 * 0.5 = 5m
        float avgWorld = (planeWorldX + planeWorldZ) * 0.5f;    // keep brush roughly circular
        float rUV = Mathf.Abs(radiusMeters / Mathf.Max(1e-6f, avgWorld));

        DoStampUV(uv, rUV, strength01);
        Debug.Log($"World: {worldPos}, Local: ({local.x:F2}, {local.z:F2}), UV: ({uv.x:F3}, {uv.y:F3})");

    }
    public void StampAtUV(Vector2 uv, float radiusMeters, float strength01 = 1f)
    {
        if (!_paintMat || !_maskRT || !skin) return;

    // Calculate radius in UV space
    float planeWorldX = 10f * Mathf.Abs(skin.lossyScale.x);
    float planeWorldZ = 10f * Mathf.Abs(skin.lossyScale.z);
    float avgWorld = (planeWorldX + planeWorldZ) * 0.5f;
    float rUV = Mathf.Abs(radiusMeters / Mathf.Max(1e-6f, avgWorld));

    // Use UV directly without any transformation
    DoStampUV(uv, rUV, strength01);
    Debug.Log($"StampAtUV: UV=({uv.x:F3},{uv.y:F3}), radiusMeters={radiusMeters:F4}, rUV={rUV:F6}");
    }

    void DoStampUV(Vector2 uv, float rUV, float strength01)
    {
        _paintMat.SetTexture("_MainTex", _maskRT);
        _paintMat.SetVector("_BrushPos", new Vector4(uv.x, uv.y, 0, 0));
        _paintMat.SetFloat("_BrushRadius", rUV);
        _paintMat.SetFloat("_Strength", Mathf.Clamp01(strength01));
        Debug.Log($"Shader params: _BrushPos=({uv.x:F3},{uv.y:F3}), _BrushRadius={rUV:F6}, _Strength={Mathf.Clamp01(strength01):F2}");

        var tmp = RenderTexture.GetTemporary(_maskRT.descriptor);
        Graphics.Blit(_maskRT, tmp, _paintMat);
        Graphics.Blit(tmp, _maskRT);
        RenderTexture.ReleaseTemporary(tmp);
    }

    void OnDisable()
    {
        if (_maskRT) { _maskRT.Release(); _maskRT = null; }
        if (_paintMat)
        {
            if (Application.isPlaying) Destroy(_paintMat);
            else DestroyImmediate(_paintMat);
            _paintMat = null;
        }
    }
    // Helpers for your inspector units:
    public float MaxDepthMeters => MaxDepthMm / 1000f;
    public float BrushRadiusMeters => BrushRadiusMm / 1000f;
}
