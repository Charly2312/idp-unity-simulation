using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class CorrectWoundController : MonoBehaviour
{
    [Header("References")]
    public RenderTexture woundMask;
    public Transform knifeTip;

    private Material skinMaterialInstance;
    private Texture2D drawTexture;

    void Start()
    {
        // Get the MATERIAL INSTANCE from the Mesh Renderer
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            skinMaterialInstance = renderer.material; // This gets the INSTANCE
            Debug.Log("Using material instance: " + skinMaterialInstance.name);
        }
        else
        {
            Debug.LogError("No MeshRenderer found!");
        }

        InitializeTexture();
    }

    void InitializeTexture()
    {
        if (woundMask != null)
        {
            drawTexture = new Texture2D(woundMask.width, woundMask.height);
            ClearWoundMask();
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            DrawTestCircle();
        }
    }

    void DrawTestCircle()
    {
        if (woundMask == null || drawTexture == null) return;

        // Clear and draw test circle
        Color[] pixels = new Color[drawTexture.width * drawTexture.height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.black;
        drawTexture.SetPixels(pixels);

        // Draw circle
        int centerX = drawTexture.width / 2;
        int centerY = drawTexture.height / 2;
        int radius = drawTexture.width / 8;

        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                if (x * x + y * y <= radius * radius)
                {
                    int px = centerX + x;
                    int py = centerY + y;
                    if (px >= 0 && px < drawTexture.width && py >= 0 && py < drawTexture.height)
                    {
                        drawTexture.SetPixel(px, py, Color.white);
                    }
                }
            }
        }

        drawTexture.Apply();
        Graphics.Blit(drawTexture, woundMask);

        // IMPORTANT: Assign to the INSTANCE material, not the original
        if (skinMaterialInstance != null && skinMaterialInstance.HasProperty("_WoundMask"))
        {
            skinMaterialInstance.SetTexture("_WoundMask", woundMask);
            Debug.Log("✓ Assigned to INSTANCE material: " + skinMaterialInstance.name);
        }
    }

    void ClearWoundMask()
    {
        if (drawTexture == null) return;

        Color[] colors = new Color[drawTexture.width * drawTexture.height];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = Color.black;
        drawTexture.SetPixels(colors);
        drawTexture.Apply();
        Graphics.Blit(drawTexture, woundMask);
    }
}