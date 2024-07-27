using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sanctums.Sanctum;

public static class SpriteManager
{
    private static readonly string m_iconPath = SanctumManager.m_folderPath + Path.DirectorySeparatorChar + "Icons";
    public static readonly Dictionary<string, Sprite?> m_customIcons = new();

    public static void LoadIcons()
    {
        if (!Directory.Exists(SanctumManager.m_folderPath)) Directory.CreateDirectory(SanctumManager.m_folderPath);
        if (!Directory.Exists(m_iconPath)) Directory.CreateDirectory(m_iconPath);
        string[] iconPaths = Directory.GetFiles(m_iconPath, "*.png");
        int count = 0;
        foreach (string path in iconPaths)
        {
            Sprite? icon = RegisterSprite(path);
            if (icon == null) continue;
            var name = Path.GetFileName(path);
            m_customIcons[name] = icon;
            ++count;
        }
        SanctumsPlugin.SanctumsLogger.LogDebug($"Loaded {count} icons");
    }
    
    private static Sprite? RegisterSprite(string fileName)
    {
        if (!File.Exists(fileName)) return null;

        byte[] fileData = File.ReadAllBytes(fileName);
        Texture2D texture = new Texture2D(4, 4);

        if (texture.LoadImage(fileData))
        {
            texture.name = fileName;
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);
        }

        return null;
    }
}