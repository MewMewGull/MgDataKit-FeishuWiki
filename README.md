# MgDataKit Feishu Wiki

MgDataKit 的飞书 Wiki 数据源适配器。它通过随仓库分发的 `lark-cli` 读取飞书 Wiki 表格，并将结果交给 Core 的统一导入服务。

## 要求

- Unity 2022.3
- [MgDataKit Core](https://github.com/MewMewGull/MgDataKit-Core)
- Windows x64（当前内置 `lark-cli 1.0.60` 的支持平台）

## 安装

将仓库检出到 Unity 项目的下列目录：

```text
Assets/MgDataKit/Editor/Adapters/Feishu
```

程序集 `MgDataKit.Feishu.Editor` 仅在编辑器中启用。首次使用时通过 MgDataKit 菜单创建本地配置，再填写飞书应用与 Wiki 信息。

## 安全

本仓库不包含 `LarkProjectConfig.asset`、项目 Catalog、app secret 或访问 token。运行配置工具后，宿主项目会生成以下本地资产：

```text
Assets/MgDataKit/Project/LarkProjectConfig.asset
Assets/MgDataKit/Project/MgDataKitLarkTableCatalog.asset
```

其中配置资产会以明文保存 app secret。宿主仓库应忽略这两个资产及其 `.meta`，并避免提交整个 `Assets/MgDataKit/Project` 目录。

随仓库分发的 `lark-cli` 来自官方 [larksuite/cli v1.0.60](https://github.com/larksuite/cli/releases/tag/v1.0.60)。版本、哈希和许可证信息见 `THIRD-PARTY-NOTICES.md`。
