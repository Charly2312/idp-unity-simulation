using UnityEngine;

public class KnifeTipStamp : MonoBehaviour
{
    public WoundPainter WoundPainter;
    public Transform Skin;          // same as WoundPainter.skin
    public float StampInterval = 0.02f;  // seconds
    public float StampStrength = 0.9f;

    float _t;

    void Update()
    {
        if (!WoundPainter || !Skin) return;

        // 1) Update penetration ? shader
        WoundPainter.SetDepthFromTip(transform.position, WoundPainter.MaxDepthMeters);

        // 2) If penetrating, drop stamps along the path
        float signed = Vector3.Dot(Skin.up, transform.position - Skin.position); // >0 above
        if (signed < 0f) // below plane
        {
            _t += Time.deltaTime;
            if (_t >= StampInterval)
            {
                _t = 0f;
                WoundPainter.StampAtWorld(transform.position, WoundPainter.BrushRadiusMeters, StampStrength);
            }
        }
    }
}
