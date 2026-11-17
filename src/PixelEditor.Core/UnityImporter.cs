// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 mrl4an
namespace PixelEditor.Core;

public static partial class AnimationExport
{
    /// <summary>File name of the Unity editor script written next to the sheet.</summary>
    public const string UnityScriptFileName = "PixelEditorSheetImporter.cs";

    /// <summary>
    /// A self-contained Unity editor script (2021.3 LTS and newer, no packages): reads the simple JSON written by <see cref="SimpleJson"/>,
    /// slices the PNG by the grid and creates one AnimationClip per animation. It was NOT compiled or run inside Unity by the authors:
    /// the tests only check that the text is emitted and mentions the JSON fields.
    /// </summary>
    public const string UnityImporterScript = """
// Pixel Editor sheet importer for Unity (2021.3 LTS and newer).
// Put this file anywhere in your project (it is wrapped in UNITY_EDITOR; Assets/Editor is the usual place).
// Select the .json file exported by Pixel Editor (plain JSON) that lies next to its PNG, then use
// Assets > Pixel Editor > Import sheet from JSON.
// It configures the PNG (Sprite, Multiple, Point filter, no compression, Pixels Per Unit = frame height),
// slices it by the grid into sprites named <animation>_<frame> and creates one AnimationClip per animation.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class PixelEditorSheetImporter
{
    [Serializable] class FrameData { public int column; public float delay; }
    [Serializable] class AnimationData { public string name; public int row; public bool loop; public FrameData[] frames; }
    [Serializable] class SheetData
    {
        public string image;
        public int frameWidth;
        public int frameHeight;
        public int columns;
        public int rows;
        public AnimationData[] animations;
    }

    const string MenuPath = "Assets/Pixel Editor/Import sheet from JSON";

    [MenuItem(MenuPath, true)]
    static bool Validate()
    {
        return SelectedJsonPath() != null;
    }

    [MenuItem(MenuPath)]
    static void ImportSelected()
    {
        var path = SelectedJsonPath();
        if (path == null) return;
        try { Import(path); }
        catch (Exception ex) { Debug.LogError("Pixel Editor import failed: " + ex.Message); }
    }

    static string SelectedJsonPath()
    {
        var obj = Selection.activeObject;
        if (obj == null) return null;
        var path = AssetDatabase.GetAssetPath(obj);
        return !string.IsNullOrEmpty(path) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    static void Import(string jsonPath)
    {
        var data = JsonUtility.FromJson<SheetData>(File.ReadAllText(jsonPath));
        if (data == null || string.IsNullOrEmpty(data.image) || data.frameWidth < 1 || data.frameHeight < 1 || data.animations == null)
            throw new InvalidDataException("This JSON is not a Pixel Editor sheet description.");

        var dir = Path.GetDirectoryName(jsonPath).Replace('\\', '/');
        var texturePath = dir + "/" + data.image;
        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null) throw new FileNotFoundException("The PNG was not found next to the JSON: " + texturePath);

        // texture settings and the grid slices (Unity rects start at the bottom-left corner, the sheet rows start at the top)
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.spritePixelsPerUnit = data.frameHeight;

        var metas = new List<SpriteMetaData>();
        foreach (var anim in data.animations)
            for (int f = 0; f < anim.frames.Length; f++)
            {
                metas.Add(new SpriteMetaData
                {
                    name = anim.name + "_" + f,
                    rect = new Rect(anim.frames[f].column * data.frameWidth, (data.rows - 1 - anim.row) * data.frameHeight, data.frameWidth, data.frameHeight),
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                });
            }
#pragma warning disable 618   // TextureImporter.spritesheet is obsolete in newer Unity versions but still works there without extra packages
        importer.spritesheet = metas.ToArray();
#pragma warning restore 618
        importer.SaveAndReimport();

        var sprites = new Dictionary<string, Sprite>();
        foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(texturePath))
            if (asset is Sprite sp) sprites[sp.name] = sp;

        var baseName = Path.GetFileNameWithoutExtension(texturePath);
        foreach (var anim in data.animations)
        {
            var clipPath = dir + "/" + baseName + "_" + Sanitize(anim.name) + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            bool isNew = clip == null;
            if (isNew) clip = new AnimationClip();
            clip.frameRate = 60f;

            // every frame starts at the sum of the previous delays; the last key repeats the last sprite at the end time so its delay is kept
            var keys = new List<ObjectReferenceKeyframe>();
            float time = 0f;
            Sprite last = null;
            for (int f = 0; f < anim.frames.Length; f++)
            {
                sprites.TryGetValue(anim.name + "_" + f, out last);
                keys.Add(new ObjectReferenceKeyframe { time = time, value = last });
                time += Mathf.Max(anim.frames[f].delay, 0.001f);
            }
            keys.Add(new ObjectReferenceKeyframe { time = time, value = last });

            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys.ToArray());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = anim.loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (isNew) AssetDatabase.CreateAsset(clip, clipPath); else EditorUtility.SetDirty(clip);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Pixel Editor: imported " + data.animations.Length + " animation(s) from " + jsonPath);
    }
}
#endif
""";
}
