# AGENTS.md

## 适用范围

- 本文件适用于 `Packages/MochiUnity` 整个 Git 仓库。
- MochiUnity 是依赖`Mochi`的独立可复用 **Unity 扩展**框架；
- UPM 包名为 `com.mochi.unity`，运行时程序集名为 `Mochi.Unity`，编辑器程序集名为 `Mochi.Unity.Editor`。

## 目录职责

- `Base/`：基于 `MonoBehaviour` 的基础抽象。
- `GUI/`：面板、UI 层级、UI 管理器和事件触发组件。
- `Helpers/`：Unity 相关的通用辅助方法。
- `Logging/`：Mochi 日志系统的 Unity 输出、屏幕显示和文件写入适配。
- `SaveSystem/`：基于 Unity 持久化路径的存档清单与序列化存储。
- `TagSystem/`：标签定义、容器、字典资源和查询管理。
- `Editor/`：仅供 Unity Editor 使用的窗口、菜单、检查器和资源工具。
- `SourceGenerators/`：供运行时程序集引用的 Roslyn 源生成器二进制。
- `Mochi.Unity.asmdef`、`Editor/Mochi.Unity.Editor.asmdef`：运行时与编辑器程序集边界；`package.json`：UPM 包身份、版本、最低 Unity 版本和依赖。

## 架构边界

- 本仓库只承载依赖 Unity 的通用能力；
- 不依赖 Unity 的通用逻辑应放入 `Packages/Mochi`。
- 运行时代码不能直接引用 `UnityEditor`。编辑器功能放入 `Editor/` 和 `Mochi.Unity.Editor` 程序集；确需共存的编辑器分支必须使用 `UNITY_EDITOR` 隔离，并确认 Player 编译通过。
- 保持各模块低耦合。公共抽象优先放在拥有该概念的模块中，不要为单一调用方扩大公共 API。

## 实现约定

- 使用仓库现有 C# 风格：4 空格缩进、Allman 大括号、类型和公开成员用 PascalCase、局部变量和参数用 camelCase。
- 命名空间按照模块划分，Unity 扩展通常使用 `Mochi.Unity.*`；
- 新增行为时优先保持 API 小而明确；Unity 对象销毁、场景切换、重复初始化、空引用和异步取消要有可预测结果。
- Unity 对象和大多数 Unity API 只能在主线程访问；后台文件写入、异步加载和事件订阅必须正确处理退出、释放与异常。

## Unity 与包文件

- 新增、移动或删除 Unity 可见文件/目录时，同步处理对应 `.meta` 文件；不要随意重建已有 GUID。
- `package.json` 必须保持合法 JSON。只有发布语义发生变化时才更新版本，并同步核对 `com.mochi.core` 及其他 UPM 依赖版本。
- 修改程序集定义时保持程序集名称和运行时/Editor 边界兼容，确认引用 GUID、预编译引用和源生成器引用有效。
- 更新 `SourceGenerators/MochiGenerator.dll` 时保留其 `.meta` GUID 和 `RoslynAnalyzer` 标签，并验证生成代码目标仍为 `Mochi.Unity`。

## 测试与验证

- 修复缺陷或改变行为时，应先添加能复现问题的最小自动化测试；若仓库尚无合适测试程序集，可在 `Tests/` 下建立运行时或 Editor 测试程序集并引用对应 Mochi 程序集。
- 涉及运行时代码时，确认 `Mochi.Unity` 编译没有新增错误；涉及编辑器代码时，同时确认 `Mochi.Unity.Editor` 编译没有新增错误。
- 涉及包清单、程序集定义、源生成器、序列化或资源导入时，还要让 Unity 完成重新导入和编译；涉及公共 API 时，至少确认 Mochi 系列包及 Veyra 消费端没有新增错误。

## 版本控制

- 本目录是独立 Git 仓库；
- 只提交本任务相关文件，保留用户已有改动；提交前检查版本控制状态。
- 不提交 Unity/IDE 生成物。提交说明沿用简洁的 Conventional Commits 风格，例如 `fix: ...`、`feat: ...`、`refactor: ...`、`test: ...`、`docs: ...`、`chore: ...`。
