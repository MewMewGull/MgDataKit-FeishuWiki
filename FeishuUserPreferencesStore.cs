#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace MgDataKit.Editor {
    [Serializable]
    internal sealed class FeishuUserPreferencesData {
        public bool HasPlayBeforeFeishuSyncOverride;
        public bool PlayBeforeFeishuSyncEnabled;
    }

    internal static class FeishuUserPreferencesStore {
        private const string RelativePath = "UserSettings/MgDataKit.Feishu.user.json";
        private static FeishuUserPreferencesData _data;

        public static FeishuUserPreferencesData Data => _data ??= Load();

        public static bool GetPlayBeforeFeishuSyncEnabled() {
            if (Data.HasPlayBeforeFeishuSyncOverride)
                return Data.PlayBeforeFeishuSyncEnabled;

            return LarkProjectConfigStore.GetOrNull()?.playBeforeFeishuSyncEnabled ?? false;
        }

        public static void SetPlayBeforeFeishuSyncOverride(bool? value) {
            Data.HasPlayBeforeFeishuSyncOverride = value.HasValue;
            if (value.HasValue)
                Data.PlayBeforeFeishuSyncEnabled = value.Value;
            Save();
        }

        public static void Save() {
            string path = GetAbsolutePath(RelativePath);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(Data, true));
        }

        private static FeishuUserPreferencesData Load() {
            string path = GetAbsolutePath(RelativePath);
            if (!File.Exists(path))
                return new FeishuUserPreferencesData();

            try {
                return JsonUtility.FromJson<FeishuUserPreferencesData>(File.ReadAllText(path)) ??
                       new FeishuUserPreferencesData();
            } catch (Exception ex) {
                Debug.LogWarning($"[MgDataKit] 读取飞书本机设置失败，将使用项目默认值：{ex.Message}");
                return new FeishuUserPreferencesData();
            }
        }

        private static string GetAbsolutePath(string relativePath) {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
        }
    }
}
#endif
