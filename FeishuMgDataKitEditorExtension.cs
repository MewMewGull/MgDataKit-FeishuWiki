#if UNITY_EDITOR

using System;
using UnityEngine.UIElements;

namespace MgDataKit.Editor {
    internal static class FeishuEditorMenu {
        public const string WikiTree = MgDataKitEditorMenu.Root + "/数据/MgDataKit Wiki Tree...";

        public static class LarkCli {
            public const string MenuRoot = MgDataKitEditorMenu.Root + "/飞书 Lark CLI";
            public const string CheckInstallation = MenuRoot + "/检查内置 CLI";
            public const string ProjectConfig = MenuRoot + "/编辑项目飞书应用配置...";
            public const string ApplyProjectConfig = MenuRoot + "/应用项目配置到本机";
            public const string AuthStatus = MenuRoot + "/登录状态";
            public const string AuthLogin = MenuRoot + "/登录飞书账号...";
        }
    }

    public sealed class FeishuMgDataKitEditorExtension : IMgDataKitEditorExtension {
        public string Id => "feishu";
        public int Order => 100;

        public void Register(IMgDataKitEditorRegistry registry) {
            registry.RegisterAction(
                MgDataKitEditorActionSlot.LeftPaneActions,
                new MgDataKitEditorActionDefinition(
                    "feishu.wiki-tree",
                    "打开 Wiki 树",
                    "查看并刷新飞书 Wiki 目录与 Sheet 子节点",
                    100,
                    (_, __) => MgDataKitWikiTreeWindow.OpenWindow()));
            registry.RegisterView(
                MgDataKitEditorActionSlot.AssetEmptyState,
                new FeishuEmptyStateView());
        }

        private sealed class FeishuEmptyStateView : IMgDataKitEditorViewExtension {
            private HelpBox _helpBox;

            public string Id => "feishu.empty-state";
            public int Order => 100;

            public bool IsVisible(MgDataKitEditorContext context) {
                return context?.SelectedTypeEntry != null &&
                       string.Equals(
                           context.SelectedTypeEntry.SourceId,
                           "feishu",
                           StringComparison.OrdinalIgnoreCase) &&
                       (context.AssetEntries == null || context.AssetEntries.Count == 0);
            }

            public void Build(MgDataKitEditorContext context, VisualElement container) {
                _helpBox = new HelpBox(
                    "当前类型使用飞书数据源。请先创建或批量绑定 Asset。",
                    HelpBoxMessageType.Info);
                _helpBox.AddToClassList("mg-data-kit-help-box");
                container.Add(_helpBox);
            }

            public void Refresh(MgDataKitEditorContext context, VisualElement container) {
                if (_helpBox != null)
                    _helpBox.text = IsVisible(context)
                        ? "当前类型使用飞书数据源。请先创建或批量绑定 Asset。"
                        : string.Empty;
            }
        }
    }
}

#endif
