using System.Diagnostics;
using System.IO;
using UnityEngine;
using static System.Net.Mime.MediaTypeNames;
using Debug = UnityEngine.Debug;
using Application = UnityEngine.Application;


public class CreateSkinTexture : MonoBehaviour
{
    [Header("Texture Settings")]
    public string textureName = "SkinTexture";
    public Color skinColor = new Color(0.9f, 0.7f, 0.6f);
    public int width = 4;
    public int height = 4;
    public bool createOnStart = true;

    void Start()
    {
        if (createOnStart)
        {
            CreateTexture();
        }
    }

    [ContextMenu("Create Skin Texture")]
    public void CreateTexture()
    {
        // Create new texture
        Texture2D skinTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

        // Fill with skin color
        Color[] pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = skinColor;
        }
        skinTexture.SetPixels(pixels);
        skinTexture.Apply();

        // Convert to PNG bytes
        byte[] pngData = skinTexture.EncodeToPNG();

        // Save to project
        string path = Application.dataPath + "/" + textureName + ".png";
        File.WriteAllBytes(path, pngData);

        Debug.Log("Skin texture created at: " + path);

        // Refresh asset database to see the new texture
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif

        DestroyImmediate(skinTexture);
    }
}