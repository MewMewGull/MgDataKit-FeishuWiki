#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 替代 <see cref="EditorUtility.DisplayDialog"/> 展示只读文本，避免 Unity 将弹窗内容写入 Console Warning。
/// </summary>
public sealed class LarkCliMessageWindow : EditorWindow {
    string _message;

    public static void Show(string title, string message) {
        var window = CreateInstance<LarkCliMessageWindow>();
        window.titleContent = new GUIContent(title);
        window._message = message ?? string.Empty;
        window.minSize = new Vector2(440f, 280f);
        window.maxSize = new Vector2(720f, 640f);
        window.ShowUtility();
    }

    void OnGUI() {
        EditorGUILayout.Space(8f);
        using (var scroll = new EditorGUILayout.ScrollViewScope(_scrollPosition)) {
            _scrollPosition = scroll.scrollPosition;
            EditorGUILayout.LabelField(_message, LarkCliMessageStyles.WordWrapped);
        }

        EditorGUILayout.Space(8f);
        if (GUILayout.Button("确定", GUILayout.Height(28f)))
            Close();
    }

    Vector2 _scrollPosition;

    static class LarkCliMessageStyles {
        static GUIStyle _wordWrapped;

        public static GUIStyle WordWrapped {
            get {
                if (_wordWrapped != null)
                    return _wordWrapped;

                _wordWrapped = new GUIStyle(EditorStyles.label) {
                    wordWrap = true,
                    richText = false,
                    fontSize = 12,
                };
                return _wordWrapped;
            }
        }
    }
}
#endif
