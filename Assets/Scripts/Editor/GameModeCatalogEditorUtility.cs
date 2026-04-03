#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 기본 GameMode 카탈로그 에셋 생성 유틸리티입니다.
/// </summary>
public static class GameModeCatalogEditorUtility
{
    #region Constants

    private const string DATA_ROOT = "Assets/Data";
    private const string MODE_FOLDER = "Assets/Data/GameModes";
    private const string CATALOG_PATH = "Assets/Data/GameModeCatalog.asset";

    #endregion

    #region Menu

    [MenuItem("Tools/Project VOID/Generate Default GameMode Catalog")]
    public static void GenerateDefaultCatalog()
    {
        EnsureFolder(DATA_ROOT);
        EnsureFolder(MODE_FOLDER);

        GameModeDefinition normal4 = CreateOrUpdateDefinition(
            "Assets/Data/GameModes/Mode_Normal4.asset",
            GameMode.FourPlayer,
            "normal_4",
            "일반 4인",
            4,
            4,
            "normal_4",
            "normal",
            false,
            false,
            false,
            new[] { "fourplayer", "four_player", "four" });

        GameModeDefinition normal8 = CreateOrUpdateDefinition(
            "Assets/Data/GameModes/Mode_Normal8.asset",
            GameMode.EightPlayer,
            "normal_8",
            "일반 8인",
            8,
            8,
            "normal_8",
            "normal",
            false,
            false,
            false,
            new[] { "eightplayer", "eight_player", "eight" });

        GameModeDefinition ranked8 = CreateOrUpdateDefinition(
            "Assets/Data/GameModes/Mode_Ranked8.asset",
            GameMode.Ranked,
            "ranked_8",
            "랭크 8인",
            8,
            8,
            "ranked_8",
            "ranked",
            true,
            false,
            false,
            new[] { "ranked", "ranked8" });

        GameModeDefinition custom = CreateOrUpdateDefinition(
            "Assets/Data/GameModes/Mode_Custom.asset",
            GameMode.Custom,
            "custom",
            "커스텀",
            1,
            8,
            "custom",
            "custom",
            false,
            false,
            true,
            new[] { "customroom", "custom_room" });

        GameModeDefinition practice = CreateOrUpdateDefinition(
            "Assets/Data/GameModes/Mode_Practice.asset",
            GameMode.PracticeRange,
            "practice",
            "연습장",
            1,
            1,
            "practice",
            "practice",
            false,
            true,
            false,
            new[] { "practicerange", "practice_range" });

        GameModeCatalog catalog = AssetDatabase.LoadAssetAtPath<GameModeCatalog>(CATALOG_PATH);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<GameModeCatalog>();
            AssetDatabase.CreateAsset(catalog, CATALOG_PATH);
        }

        var catalogSerialized = new SerializedObject(catalog);
        var definitionsProp = catalogSerialized.FindProperty("_definitions");
        definitionsProp.arraySize = 5;
        definitionsProp.GetArrayElementAtIndex(0).objectReferenceValue = normal4;
        definitionsProp.GetArrayElementAtIndex(1).objectReferenceValue = normal8;
        definitionsProp.GetArrayElementAtIndex(2).objectReferenceValue = ranked8;
        definitionsProp.GetArrayElementAtIndex(3).objectReferenceValue = custom;
        definitionsProp.GetArrayElementAtIndex(4).objectReferenceValue = practice;
        catalogSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[GameModeCatalogEditorUtility] 기본 GameModeCatalog 생성/갱신 완료");
    }

    #endregion

    #region Private Methods

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        string folderName = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }

    private static GameModeDefinition CreateOrUpdateDefinition(
        string assetPath,
        GameMode mode,
        string modeId,
        string displayName,
        int minPlayers,
        int maxPlayers,
        string sessionTag,
        string queueBucket,
        bool isRanked,
        bool isPractice,
        bool isCustom,
        string[] aliases)
    {
        GameModeDefinition definition = AssetDatabase.LoadAssetAtPath<GameModeDefinition>(assetPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<GameModeDefinition>();
            AssetDatabase.CreateAsset(definition, assetPath);
        }

        var serialized = new SerializedObject(definition);
        serialized.FindProperty("_mode").enumValueIndex = (int)mode;
        serialized.FindProperty("_modeId").stringValue = modeId;
        serialized.FindProperty("_displayName").stringValue = displayName;
        serialized.FindProperty("_minPlayers").intValue = minPlayers;
        serialized.FindProperty("_maxPlayers").intValue = maxPlayers;
        serialized.FindProperty("_isRanked").boolValue = isRanked;
        serialized.FindProperty("_isPractice").boolValue = isPractice;
        serialized.FindProperty("_isCustom").boolValue = isCustom;
        serialized.FindProperty("_sessionTag").stringValue = sessionTag;
        serialized.FindProperty("_queueBucket").stringValue = queueBucket;

        SerializedProperty aliasesProp = serialized.FindProperty("_legacyAliases");
        aliasesProp.arraySize = aliases.Length;
        for (int i = 0; i < aliases.Length; i++)
        {
            aliasesProp.GetArrayElementAtIndex(i).stringValue = aliases[i];
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(definition);
        return definition;
    }

    #endregion
}
#endif
