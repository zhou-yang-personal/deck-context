# DeckContext Work Development Bootstrap Prompt

你现在负责继续开发 GitHub 项目：

`zhou-yang-personal/deck-context`

项目目标：将 PowerPoint `.pptx` 转换为结构化、可追溯、LLM-friendly 的 Context，用于后续把历史 PPT / PPT 素材提供给 ChatGPT 等 LLM 做深度分析和 PPT 优化。

---

# 一、开始任何工作前的强制步骤

每次开始新的需求设计、架构调整、代码修改、UI 修改、重构、测试、PR Review 或 Release 前：

1. 读取仓库根目录：`AGENTS.md`。
2. 读取当前相关基线：
   - `docs/requirements/v1-baseline.md`
   - `docs/architecture/5-view-architecture-v0.1.md`
3. 检查当前 Git 分支和已有文件。
4. 确认当前工作基于 `dev`。
5. 如果发现仓库状态、代码实现与基线文档冲突，先指出冲突，再处理，不得自行覆盖已经确认的需求。
6. 不得因为“同类产品通常都有”而自行增加产品功能。

如果无法读取 `AGENTS.md`，停止设计和开发。

---

# 二、Git 分支规则

当前分支策略：

- `main`：稳定、已基线版本。
- `dev`：主要开发和集成分支。

默认：

- 所有日常实现工作在 `dev` 上完成；
- 不直接修改 `main`；
- 不自动把 `dev` 合并到 `main`；
- 只有我明确要求“基线 / 发布 / 合并 main”时，才准备进入 `main`；
- 如确有必要创建 feature branch，必须从 `dev` 分出，并最终回到 `dev`。

每个阶段完成后形成语义清楚的 Git commit。

不要为了制造进度提交空目录、占位代码或没有实际行为变化的 commit。

---

# 三、V1 产品边界

V1 唯一核心目标：

`PPTX → Structured LLM Context`

确认需要处理：

1. Slide metadata
2. Text
3. Text formatting 中影响语义层级的属性
4. Object type
5. Geometry / Layout
6. Native PowerPoint Table
7. Native PowerPoint Chart
8. Chart Series / Categories / Values
9. Chart source formula / range
10. Embedded Excel Workbook
11. Chart 与 Workbook 的 relationship / provenance
12. Image object / media reference
13. Markdown Context
14. JSON Context
15. Extraction Diagnostics / Traceability

当前没有确认，因此不得自行开发：

- 登录 / 用户系统
- 云同步
- Web 后端
- 多人协作
- Vector DB
- Semantic Search
- Knowledge Base UI
- PPT 历史版本 Diff
- Office Add-in
- 自动修改 PPT
- 自动生成 PPT
- 自动上传 ChatGPT
- Workspace / Project 管理
- 必选云 OCR
- 必选 Vision API

图片像素内容理解目前是 Deferred Decision。

V1 当前只要求：

- 能识别图片对象；
- 能提取 / 引用图片资源；
- 能记录 geometry / source / status；
- 定义类似 `IImageTextProvider` 的扩展接口。

在我没有明确选择方案以前，不得自行接入 OpenAI Vision、Azure OCR、Tesseract、本地多模态模型或其他 Provider。

---

# 四、技术基线

默认技术选型已经确定：

- OS：Windows
- Runtime：.NET 10 LTS
- Language：C#
- Desktop UI：WPF
- PPTX / OOXML：Open XML SDK
- Deep OOXML：System.Xml.Linq / 原始 XML
- JSON：System.Text.Json
- PowerPoint Interop：Optional Adapter
- OCR / Vision：Optional Adapter
- Persistent Database：V1 不需要

核心解析路径必须在以下条件下仍然可用：

- 没有 Microsoft PowerPoint
- 没有 Internet
- 没有 OCR
- 没有 Vision Provider
- 没有数据库

不要擅自改成 Electron、Tauri、React、Python 主解析服务或 Web App，除非有明确技术证据说明现有基线无法满足需求，并先说明原因和最小调整方案。

---

# 五、架构红线

Normalized Intermediate Representation（IR）是整个系统中心。

必须坚持：

`PPTX`
→ `Package / Relationship Reader`
→ `Slide / Object Extractors`
→ `Normalized IR`
→ `Markdown / JSON Exporters`
→ `Extraction Report`

禁止：

- Markdown Exporter 重新解析 PPTX；
- JSON Exporter 重新解析 PPTX；
- UI 直接处理 Open XML XML Node；
- Domain 依赖 WPF；
- Domain 依赖 PowerPoint Interop；
- 用截图 OCR 替代能够从 native OOXML 获取的 Chart / Table / Text 数据；
- 用 Data Label 猜测本来可以从 Embedded Excel 获取的数据；
- 在构建 IR 前把 Chart / Table 扁平化成普通文本；
- 对无法确认的数据进行猜测或静默修复。

所有无法解析、部分解析、歧义内容必须进入 Diagnostics。

---

# 六、开发计划

不要一次性开发整个 V1。严格按阶段推进。

## Phase 0 — Engineering Bootstrap

目标：建立可持续开发、自动验证、自动生成 Windows 验证包的工程骨架。

实现：

- `.sln`
- 基础项目结构
- dependency direction
- 基础 test projects
- `.gitignore`
- 必需配置
- GitHub Actions 开发构建 Workflow

建议逻辑项目：

- `DeckContext.App`
- `DeckContext.Application`
- `DeckContext.Domain`
- `DeckContext.OpenXml`
- `DeckContext.Export`

Optional Adapter 未实现前不要为了架构图创建大量空项目。

Phase 0 必须建立 `.github/workflows/dev-build.yml` 或等价 Workflow，至少支持：

- push 到 `dev`
- `workflow_dispatch`
- setup .NET
- restore
- Release build
- automated tests
- publish `win-x64`
- 生成可运行的 Windows 包
- upload GitHub Actions Artifact

开发阶段优先使用 self-contained Windows publish；不要提前引入 Installer / MSIX / Code Signing。

验收：

- Solution clean build 成功；
- Test framework 可运行；
- 项目依赖符合 5-view architecture；
- GitHub Actions 在 `dev` 上成功完成 build + test + publish + artifact；
- 用户不需要本地 SDK 即可下载运行验证包。

---

## Phase 1 — Core IR + Package / Slide Foundation

首先建立最小可演进 IR，包括：

- Deck
- Slide
- Element
- Identity
- SourceReference
- Geometry
- ExtractionStatus
- Diagnostics

建立：

- PPTX Package Reader
- Presentation / Slide enumeration
- Relationship Resolver 基础能力

目标：真正读取一个 `.pptx`，建立 `Deck → Slides → Elements`。

验收：

- Slide 数量准确；
- Slide identity 可追溯；
- Slide size 可获取；
- Relationship foundation 可工作；
- JSON serialization 基础稳定；
- malformed / unsupported 情况能够产生 diagnostics。

---

## Phase 2 — Text + Geometry / Layout

Text 至少实现：

- Shape text
- Paragraph
- Run
- text content
- 必要 font metadata
- font size
- bold
- semantic-relevant color

Geometry 至少实现：

- x
- y
- width
- height
- normalized coordinates
- z-order
- group relationship 基础

Native geometry 与后续推导的 layout description 必须分离。

不得在 IR 中把“左上区域”“主证据”等推断包装成 source fact。

验收 fixture：

- `text-only.pptx`
- `layout-basic.pptx`
- `groups-basic.pptx`

---

## Phase 3 — Native PowerPoint Tables

实现：

- Table identity
- row / column
- cell text
- merged cell relationship
- geometry
- materially relevant formatting
- diagnostics

Native Table 在 IR 中保持结构化，不得直接扁平化为 prose。

验收 fixture：`table-basic.pptx`

必须验证行列数量、cell 内容、merged cell、object provenance、deterministic JSON / Markdown。

---

## Phase 4 — Native PowerPoint Charts

这是 V1 的关键复杂阶段。

实现：

- Chart type
- Chart title
- Series
- Series name
- Categories
- Values
- Legend
- Axes
- Unit / range where available
- Data Labels
- Source formula / range
- relationship

原则：

- 首先获取 Native Chart XML；
- 不能只读取页面显示的数据标签；
- Open XML SDK 无法完整暴露的信息可以直接解析 OOXML；
- 所有 XML 解析最终映射回统一 IR。

验收 fixture：`chart-basic.pptx`

测试必须断言真实 Categories / Series / Values，而不是只验证成功创建 ChartElement。

---

## Phase 5 — Embedded Excel

实现完整链路：

`Chart`
→ `Relationship`
→ `Embedded Workbook`
→ `Worksheet`
→ `Cells / Ranges`
→ `Chart Source Formula`

必须保留 provenance。

不能只把 embedded `.xlsx` 导出而失去和 Chart 的关系。

需要验证：

- workbook relationship
- worksheet
- category range
- value range
- series mapping
- formula
- actual cell values

验收 fixture：`chart-embedded-workbook.pptx`

完成本阶段后必须设置一次真实 PPT Manual Verification Gate。

---

## Phase 6 — Images / Media

当前只实现已经确认部分：

- Image object
- object identity
- media relationship
- media extraction/reference
- geometry
- crop / transform 中必要信息
- extraction status

建立 `IImageTextProvider`，默认可以是 `None / NotConfigured`。

此时必须明确输出 `Image content not analyzed`，禁止自动生成图片内容描述。

---

## Phase 7 — Markdown / JSON / Diagnostics

从同一个 IR 生成：

### `deck.context.md`

目标：Human / LLM-friendly，而不是 XML dump。

推荐结构：

- Deck Summary
- Slide
- Slide Title/Text
- Layout/Object Structure
- Tables
- Charts
- Embedded Data
- Images
- Diagnostics

### `deck.context.json`

完整 machine-readable IR projection。

### `extraction-report.json`

至少包含：

- Source File
- Slide
- Element
- Extractor
- Severity
- Message
- status
- skipped / partial / recovered

要求：相同输入产生 deterministic output，Markdown 和 JSON 不得走不同解析路径。

---

## Phase 8 — WPF UI

只有核心 extraction 已经稳定以后再完善 UI。

V1 UI 只围绕：

1. Select / Drop PPTX
2. Start extraction
3. Progress
4. Warnings / Errors
5. Result
6. Export Context
7. Open output location

不要扩展为 Document Manager，不增加 library、workspace、search、dashboard、knowledge base 或 project management。

Phase 8 完成后必须设置 Manual Verification Gate。

---

## Phase 9 — Integration & V1 Acceptance

使用多个 fixture + 至少一个真实或接近真实复杂度的 PPT 进行端到端验证。

V1 Acceptance 必须证明：

1. 能解析 Text
2. 能解析 Geometry/Layout
3. 能解析 Native Table
4. 能解析 Native Chart
5. 能获得 Series / Category / Values
6. 能追踪 Embedded Excel
7. 能获取 Workbook source data
8. 能识别 Image object
9. 不会虚构 Image semantics
10. Markdown 和 JSON 来自同一 IR
11. Unsupported object 能进入 Diagnostics
12. 单 Object 解析失败时能够 Partial Degradation
13. 输出可以真正作为 LLM 输入阅读

Build success 本身不算验收通过。

Phase 9 必须设置 Manual Verification Gate。

---

# 七、每阶段开发纪律

每个 Phase 必须执行以下闭环：

## 1. Inspect

先阅读：

- `AGENTS.md`
- 相关 requirement
- architecture
- 当前实现
- 当前 tests

## 2. Plan

先说明：

- 本阶段目标
- 修改哪些模块
- 哪些不修改
- 核心数据模型
- 验收方式

不要在没有理解当前代码的情况下直接重构。

## 3. Implement

只实现当前 Phase。

遇到可能扩需求的问题，判断其属于：

A. 当前 Requirement 已明确
B. Architecture 为实现 Requirement 必需
C. 新 Product Requirement

A/B 可以继续；C 不得自行扩展，必须指出。

## 4. Test

至少根据阶段需要覆盖：

- unit tests
- fixture-driven tests
- deterministic output tests
- malformed / unsupported path tests

## 5. Review

检查：

- Data fidelity
- Provenance
- IR consistency
- Dependency direction
- Diagnostics
- Regression
- Scope creep

## 6. Commit

形成清晰 commit。

禁止使用 `update`、`fix stuff`、`changes` 等无语义 commit message。

## 7. CI

提交到 `dev` 后检查 GitHub Actions。

CI 未通过时不得宣称阶段完成。

## 8. Report

每阶段结束输出：

- Phase
- 完成目标
- 修改文件
- 核心设计
- 自动测试结果
- CI 状态
- 已知限制
- Diagnostics / Unsupported 状态
- Git commit SHA
- `Manual Verification Required: Yes / No`
- 下一阶段建议

若 `Manual Verification Required: No` 且自动测试/CI 已充分证明阶段结果，可以继续下一 Phase。

若 `Manual Verification Required: Yes`，必须先执行下述 Manual Verification Gate，并等待我的验证反馈后再将该 Gate 标记为 Accepted。

---

# 八、GitHub Actions 自动构建与 Manual Verification Gate

本项目采用：

`Implement → Automated Test → CI Build → Downloadable Windows Package → Manual Verification → Continue`

作为需要人工验证阶段的强制闭环。

## 8.1 用户不参与本地编译

不得要求我为了验证：

- 本地 clone / pull 源码；
- 安装 .NET SDK；
- 打开 Visual Studio；
- 手工执行 `dotnet build`；
- 手工执行 `dotnet publish`；
- 自己打 ZIP；
- 自己处理运行依赖。

凡需要我手工验证的版本，都必须先由 GitHub Actions 自动生成可以直接下载运行的 Windows 构建产物。

## 8.2 Artifact 命名与追溯

推荐 Artifact：

`DeckContext-dev-win-x64-{short-sha}`

例如：

`DeckContext-dev-win-x64-a81cf32`

Artifact 和阶段报告必须同时记录：

- Branch
- Full Commit SHA
- Short Commit SHA
- Build configuration
- Runtime target
- Workflow Run
- Automated Test Result

禁止让我验证无法确认对应源码版本的构建包。

## 8.3 CI Gate

只有以下条件全部成功，才可以交给我人工验证：

- Restore
- Release Build
- Automated Tests
- Publish
- Artifact Upload

任何一项失败：

状态为 `CI Failed`。

不得声称阶段完成，不得用旧 Artifact 冒充本轮版本，必须修复并重新 CI。

## 8.4 Manual Verification Required 判定

每阶段必须明确：

`Manual Verification Required: Yes / No`

优先用自动测试验证可确定行为。

通常应人工验证：

- WPF UI
- Drag & Drop
- 文件选择
- Progress / status
- Export directory
- 打开输出目录
- Windows 文件交互
- PowerPoint Interop
- 复杂真实 PPT Chart 解析
- Embedded Excel 的真实 PPT 解析
- Markdown 是否真正适合作为 ChatGPT / LLM 素材
- 打包后的应用在目标 Windows 环境能否正常启动

至少必须安排：

### Gate A — Chart + Embedded Excel

Phase 4–5 完成后，至少一次真实 PPT 人工验证。

重点：

- 是否漏系列；
- Categories / Values 是否正确；
- Embedded Workbook 是否一致；
- source formula / range 是否正确；
- Markdown 是否正确表达数据；
- 是否出现错误推断。

### Gate B — UI

Phase 8 后必须人工验证。

### Gate C — V1 Acceptance

Phase 9 必须人工验证。

## 8.5 人工验证阶段报告格式

当 `Manual Verification Required: Yes` 时，必须提供：

### Build

- Branch
- Commit
- Workflow Run
- CI Status
- Automated Tests

### Download

提供可直接点击的 GitHub Actions Artifact 下载链接，并给出 Artifact Name。

不得只说“请去 Actions 页面寻找构建产物”。

能获得具体 Artifact URL 时必须直接给链接。

### Run

提供最短操作步骤，例如：

1. 下载 ZIP
2. 解压
3. 运行 `DeckContext.exe`
4. 选择测试 PPT
5. 点击 Extract

不要让我运行任何开发命令。

### Manual Verification Checklist

只列本轮真正需要观察的内容，例如：

- [ ] 应用正常启动
- [ ] PPT 可以打开 / 载入
- [ ] Slide 数量正确
- [ ] 指定 Chart 数据正确
- [ ] Embedded Excel 数值正确
- [ ] `deck.context.md` 正常生成
- [ ] 输出可以理解原 PPT 页面结构
- [ ] 没有明显漏对象

### Expected Result

明确说明正常情况下应该看到什么。

### Known Limitations

本轮已知但不属于 Bug 的限制必须提前说明。

## 8.6 人工验证反馈处理

如果我验证通过：

记录 `Manual Verification: Passed`，再继续后续依赖阶段。

如果发现问题：

记录 `Manual Verification: Failed`，然后：

1. 分析问题；
2. 修复；
3. 自动测试；
4. 再次 CI；
5. 生成新的 Artifact；
6. 给新的下载链接。

禁止让我使用旧包验证新代码。

## 8.7 Artifact 与 Release 区分

开发阶段使用 GitHub Actions Artifact，用于快速验证 `dev` 某个具体 Commit。

只有我明确要求发布版本、Baseline、Release 或合并 `main` 时，才考虑 GitHub Release / Release Asset / 固定版本号。

不要把每次开发验证都创建成 GitHub Release。

---

# 九、测试原则

本项目不能主要依靠大型真实 PPT 做自动测试。

逐步建立小而明确的 fixture：

- `text-only.pptx`
- `layout-basic.pptx`
- `table-basic.pptx`
- `chart-basic.pptx`
- `chart-embedded-workbook.pptx`
- `images-basic.pptx`
- `groups-basic.pptx`
- `unsupported-object.pptx`

每个 Fixture 必须知道输入是什么、预期 IR 是什么。

测试不要只验证 `NotNull`，应验证真实数据，例如：

- Slide count
- text
- coordinates
- table cells
- chart categories
- chart values
- source formula
- worksheet
- cell value
- relationship id
- diagnostic code

真实客户 PPT / 历史 PPT 用于 integration/manual verification，不替代精确 fixture tests。

---

# 十、错误处理原则

解析失败尽可能按以下粒度隔离：

`Deck → Slide → Element → Sub-resource`

原则：

- PPTX package 整体损坏：可以 Failed；
- 单页问题：其他页面继续；
- 单对象问题：其他对象继续；
- 单 Chart 问题：其他对象继续；
- Embedded Workbook 问题：Chart 标记 Partial；
- Vision Provider 问题：只影响 image semantics。

状态建议统一：

- NotStarted
- Running
- Succeeded
- Partial
- Failed
- Unsupported

不要 catch 后静默忽略异常。

---

# 十一、质量优先级

本项目按以下顺序优化：

1. 数据正确
2. 来源可追溯
3. 不虚构
4. 结构完整
5. 稳定降级
6. 输出确定性
7. LLM 可读性
8. 性能
9. UI 美观

不要为了性能提前做复杂并行。

不要为了 Markdown 好看牺牲原始结构。

不要为了“看起来支持”而推测无法确认的数据。

---

# 十二、文档同步

如果实现过程中发现：

- Requirement 被我明确修改；
- Architecture 确认调整；
- IR 核心契约发生变化；
- 技术基线改变；
- 开发 / CI / 验证规则改变；

必须同步更新对应 repository docs / `AGENTS.md`。

但不要因为实现细节变化频繁修改 Requirement Baseline。

始终区分：

- Product Requirement
- Architecture Decision
- Development Constraint
- Implementation Detail

---

# 十三、代码交付约束

如果在聊天中直接交付代码修改：

- 基于仓库原始目录结构；
- 只打包本轮实际修改文件；
- 保留 repository-relative path；
- 生成可下载 ZIP；
- 不包含无关生成文件。

交付说明至少包括：

- 修改目标
- 文件清单
- 核心逻辑
- 使用方式
- 自动测试方法和结果
- CI 状态
- 手工验证方式（如需要）
- Artifact 链接（如需要）
- 下载链接

涉及版本号时，统一更新应用、配置、打包和文档中的相关版本标记。

---

# 十四、架构调整原则

如果实际 PPT / OOXML 行为证明当前 Architecture Baseline 有问题，不要为了遵守文档而写错误实现。

先给出：

- 实际证据
- 当前设计为什么不成立
- 最小调整方案
- 对 Requirements 的影响
- 对 Architecture 的影响
- 对 Tests 的影响

如果只是实现细节调整，不升级为 Product Requirement。

---

# 十五、当前立即执行任务

现在不要直接一次性实现整个 V1。

首先：

1. Fetch / inspect `zhou-yang-personal/deck-context`。
2. 切换 / 确认当前工作基于 `dev`。
3. 阅读：
   - `AGENTS.md`
   - `docs/requirements/v1-baseline.md`
   - `docs/architecture/5-view-architecture-v0.1.md`
4. 检查 repository 当前真实状态。
5. 基于真实状态建立 Phase 0 → Phase 9 的执行 Backlog，至少包括：
   - Phase / Milestone
   - Task
   - Dependency
   - Acceptance Criteria
   - Test Fixture
   - Risk
   - Manual Verification Required
6. 不凭空增加产品需求。
7. 完成 Backlog 后开始 Phase 0 Engineering Bootstrap。
8. Phase 0 必须把 GitHub Actions 自动构建 / 测试 / Windows Artifact 链路建立起来。
9. Phase 0 自动测试和 CI 通过后 commit 到 `dev` 并汇报结果。
10. 若 Phase 0 不需要我手工验证，可继续 Phase 1。
11. 后续阶段如果 `Manual Verification Required: No` 且自动验证充分，可继续推进。
12. 遇到 Manual Verification Gate 时，必须给我可下载 Windows Artifact、最短运行步骤、验证清单和预期结果，然后等待我的验证反馈。
13. 遇到新的 Product Requirement 或会改变架构基线的重大不确定性时，停止扩展并明确报告。

最终目标不是快速堆功能，而是建立一个长期可靠、source-backed、traceable、可验证的 PowerPoint Context Extraction Pipeline，使真实历史 PPT 能稳定转换为 ChatGPT / LLM 可深度利用的结构化素材。
