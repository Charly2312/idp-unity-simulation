using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class ConnectionTest : MonoBehaviour
{
    [Header("DRAG THESE IN INSPECTOR")]
    public Material skinMaterial;
    public RenderTexture woundMask;
    public Transform knifeTip;

    void Start()
    {
        Debug.Log("=== CONNECTION TEST STARTED ===");
        CheckConnections();
    }

    void Update()
    {
        // Press SPACE to test
        if (Input.GetKeyDown(KeyCode.Space))
        {
            TestAllConnections();
        }
    }

    void CheckConnections()
    {
        Debug.Log("1. Checking Material: " + (skinMaterial != null ? skinMaterial.name : "NULL"));
        Debug.Log("2. Checking RenderTexture: " + (woundMask != null ? woundMask.name : "NULL"));
        Debug.Log("3. Checking Knife: " + (knifeTip != null ? knifeTip.name : "NULL"));

        // Check if skin object has the right material
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Debug.Log("4. Skin Object Material: " + (renderer.material != null ? renderer.material.name : "WRONG MATERIAL!"));
        }
    }

    void TestAllConnections()
    {
        Debug.Log("=== TESTING ALL CONNECTIONS ===");

        // 1. Force draw on render texture
        DrawTestCircle();

        // 2. Force assign to material
        if (skinMaterial != null && woundMask != null)
        {
            // Try different possible property names
            if (skinMaterial.HasProperty("_WoundMask"))
            {
                skinMaterial.SetTexture("_WoundMask", woundMask);
                Debug.Log("✓ Assigned to _WoundMask");
            }
            else if (skinMaterial.HasProperty("_WoundTexture"))
            {
                skinMaterial.SetTexture("_WoundTexture", woundMask);
                Debug.Log("✓ Assigned to _WoundTexture");
            }
            else if (skinMaterial.HasProperty("_MainTex"))
            {
                skinMaterial.SetTexture("_MainTex", woundMask);
                Debug.Log("✓ Assigned to _MainTex");
            }
            else
            {
                Debug.LogError("✗ No known texture property found in shader!");
            }

            // Force intensity
            if (skinMaterial.HasProperty("_WoundIntensity"))
            {
                skinMaterial.SetFloat("_WoundIntensity", 1f);
                Debug.Log("✓ Set intensity to 1");
            }
        }

        Debug.Log("=== TEST COMPLETE ===");
        Debug.Log("LOOK AT YOUR 3D SKIN OBJECT - IS THERE A RED CIRCLE?");
    }

    void DrawTestCircle()
    {
        if (woundMask == null) return;

        Texture2D tex = new Texture2D(woundMask.width, woundMask.height);

        // Fill with black
        Color[] pixels = new Color[tex.width * tex.height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.black;
        tex.SetPixels(pixels);

        // Draw white circle
        int centerX = tex.width / 2;
        int centerY = tex.height / 2;
        int radius = tex.width / 8;

        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                if (x * x + y * y <= radius * radius)
                {
                    int px = centerX + x;
                    int py = centerY + y;
                    if (px >= 0 && px < tex.width && py >= 0 && py < tex.height)
                    {
                        tex.SetPixel(px, py, Color.white);
                    }
                }
            }
        }

        tex.Apply();
        Graphics.Blit(tex, woundMask);
        Destroy(tex);

        Debug.Log("✓ Drew circle on Render Texture");
    }
}