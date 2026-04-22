using System.IO;
using UnityEditor;
using UnityEngine;

namespace TestingFloor.Editor {
    internal static class TestingFloorMenu {
        const string ResourcesFolder = "Assets/Resources";
        const string SettingsAssetPath = "Assets/Resources/TestingFloorSettings.asset";

        [MenuItem("Tools/Testing Floor/Create Settings Asset")]
        static void CreateSettingsAsset() {
            var existing = AssetDatabase.LoadAssetAtPath<TestingFloorSettings>(SettingsAssetPath);
            if (existing != null) {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log($"[TestingFloor] Settings asset already exists at {SettingsAssetPath}.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(ResourcesFolder)) {
                Directory.CreateDirectory(ResourcesFolder);
                AssetDatabase.Refresh();
            }

            var asset = ScriptableObject.CreateInstance<TestingFloorSettings>();
            AssetDatabase.CreateAsset(asset, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            Debug.Log($"[TestingFloor] Created settings asset at {SettingsAssetPath}. Fill in the write key.");
        }
    }
}
