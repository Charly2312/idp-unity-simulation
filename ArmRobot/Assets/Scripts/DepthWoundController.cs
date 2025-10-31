using UnityEngine;

public class DepthWoundController : MonoBehaviour
{
    [Header("Skin References")]
    public Material skinMaterial;
    public RenderTexture woundMask;
    public Transform skinSurface; // Reference to your skin block

    [Header("Knife References")]
    public Transform knifeTip;
    public Transform knifeBase; // For direction calculation

    [Header("Incision Settings")]
    public float maxIncisionDepth = 0.1f;
    public float incisionWidth = 0.02f;
    public float redIntensity = 1.0f;

    private Texture2D drawTexture;
    private Color[] clearColors;

    void Start()
    {
        InitializeDrawTexture();
    }

    void InitializeDrawTexture()
    {
        drawTexture = new Texture2D(woundMask.width, woundMask.height);
        clearColors = new Color[woundMask.width * woundMask.height];
        for (int i = 0; i < clearColors.Length; i++)
        {
            clearColors[i] = Color.black;
        }
        ClearWoundMask();
    }

    void Update()
    {
        CalculateAndDrawIncision();
    }

    void CalculateAndDrawIncision()
    {
        // Calculate depth by distance from knife tip to skin surface
        float depth = CalculateKnifeDepth();

        if (depth > 0) // Knife is inside skin
        {
            Vector2 uv = WorldToUV(knifeTip.position);
            float normalizedDepth = Mathf.Clamp01(depth / maxIncisionDepth);

            // Draw wound based on depth - deeper cuts = more intense red
            DrawDepthBasedWound(uv, normalizedDepth);
            ApplyDrawTexture();

            // Update shader parameters in real-time
            UpdateShaderParameters(normalizedDepth);
        }
    }

    float CalculateKnifeDepth()
    {
        // Simple distance from knife tip to skin surface plane
        Plane skinPlane = new Plane(skinSurface.up, skinSurface.position);
        float distance = skinPlane.GetDistanceToPoint(knifeTip.position);

        // Negative distance means knife is inside skin
        return Mathf.Max(0, -distance);
    }

    Vector2 WorldToUV(Vector3 worldPos)
    {
        // Convert world position to skin UV coordinates
        Vector3 localPos = skinSurface.InverseTransformPoint(worldPos);
        return new Vector2(
            Mathf.Clamp01((localPos.x + 0.5f)),
            Mathf.Clamp01((localPos.z + 0.5f))
        );
    }

    void DrawDepthBasedWound(Vector2 center, float depth)
    {
        int centerX = (int)(center.x * drawTexture.width);
        int centerY = (int)(center.y * drawTexture.height);

        // Width increases with depth for more realistic incision
        float currentWidth = incisionWidth * (1 + depth * 0.5f);
        int radiusPixels = (int)(currentWidth * drawTexture.width * 0.5f);

        for (int x = -radiusPixels; x <= radiusPixels; x++)
        {
            for (int y = -radiusPixels; y <= radiusPixels; y++)
            {
                float distance = Mathf.Sqrt(x * x + y * y) / (float)radiusPixels;
                if (distance <= 1.0f)
                {
                    int texX = centerX + x;
                    int texY = centerY + y;

                    if (texX >= 0 && texX < drawTexture.width &&
                        texY >= 0 && texY < drawTexture.height)
                    {
                        // Deeper cuts = brighter red (closer to white in mask)
                        float intensity = depth * (1 - distance);
                        Color currentColor = drawTexture.GetPixel(texX, texY);
                        Color newColor = Color.Lerp(currentColor, Color.white, intensity);
                        drawTexture.SetPixel(texX, texY, newColor);
                    }
                }
            }
        }
    }

    void UpdateShaderParameters(float normalizedDepth)
    {
        // Update shader properties based on depth
        if (skinMaterial != null)
        {
            // Make red color more intense with depth
            skinMaterial.SetFloat("_RedIntensity", redIntensity * (0.5f + normalizedDepth));

            // Increase darkening for deeper cuts
            skinMaterial.SetFloat("_DarkenAmount", normalizedDepth * 0.8f);

            // Adjust penetration in shader
            skinMaterial.SetFloat("_Penetration", normalizedDepth * maxIncisionDepth);
        }
    }

    void ApplyDrawTexture()
    {
        drawTexture.Apply();
        Graphics.Blit(drawTexture, woundMask);
    }

    public void ClearWoundMask()
    {
        drawTexture.SetPixels(clearColors);
        drawTexture.Apply();
        Graphics.Blit(drawTexture, woundMask);

        // Reset shader parameters
        if (skinMaterial != null)
        {
            skinMaterial.SetFloat("_RedIntensity", redIntensity);
            skinMaterial.SetFloat("_DarkenAmount", 0.3f);
            skinMaterial.SetFloat("_Penetration", 0f);
        }
    }

    // Public method to get current incision depth for other systems
    public float GetCurrentDepth()
    {
        return CalculateKnifeDepth();
    }
}