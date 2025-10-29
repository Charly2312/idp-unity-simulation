using UnityEngine;
using static System.Net.Mime.MediaTypeNames;
using Application = UnityEngine.Application;

[ExecuteAlways]
public class WoundPainter : MonoBehaviour
{
    [Header("Mask")]
    public int MaskSize = 1024;         // RT resolution
    public float MaxDepthMm = 3f;       // depth that equals Depth01=1
    public float BrushRadiusMm = 2f;    // radius of stain in mm
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
        if (skinRenderer && (_mat == null || !Application.isPlaying))
            _mat = skinRenderer.sharedMaterial; // instance

        if (_maskRT == null || _maskRT.width != MaskSize)
        {
            if (_maskRT) _maskRT.Release();
            _maskRT = new RenderTexture(MaskSize, MaskSize, 0, RenderTextureFormat.R8) { useMipMap = false, filterMode = FilterMode.Bilinear };
            Graphics.Blit(Texture2D.blackTexture, _maskRT);
        }

        if (_mat) _mat.SetTexture("_WoundMask", _maskRT);
        if (_paintMat == null) _paintMat = new Material(Shader.Find("Hidden/WoundPaint"));

        if (skin)
        {
            _planeNormal = skin.up.normalized;
            _planeD = -Vector3.Dot(_planeNormal, skin.position);
        }
    }

    // --- Public API ---
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

        // convert world → skin local → uv (assumes Plane mesh scaled 1 with UV0 = XY in 0..1)
        Vector3 local = skin.InverseTransformPoint(worldPos);
        // map plane XY in [-0.5..0.5] to [0..1] (works for Unity Plane)
        Vector2 uv = new Vector2(local.x + 0.5f, local.z + 0.5f);

        _paintMat.SetTexture("_MainTex", _maskRT);
        _paintMat.SetVector("_BrushPos", new Vector4(uv.x, uv.y, 0, 0));

        // convert radius from meters to uv (Unity Plane is 10x10 units by default; scaled 1 => 10m)
        float planeMeters = 10f; // Unity Plane size
        float rUV = Mathf.Abs(radiusMeters / planeMeters);
        _paintMat.SetFloat("_BrushRadius", rUV);
        _paintMat.SetFloat("_Strength", Mathf.Clamp01(strength01));

        RenderTexture tmp = RenderTexture.GetTemporary(_maskRT.descriptor);
        Graphics.Blit(_maskRT, tmp, _paintMat);
        Graphics.Blit(tmp, _maskRT);
        RenderTexture.ReleaseTemporary(tmp);
    }

    // Helpers for your inspector units:
    public float MaxDepthMeters => MaxDepthMm / 1000f;
    public float BrushRadiusMeters => BrushRadiusMm / 1000f;
}
