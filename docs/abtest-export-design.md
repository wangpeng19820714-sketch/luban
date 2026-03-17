# ABTest 增量导表功能落地方案

## 1. 文档目的

本文档用于说明在当前 Luban 工程中新增“ABTest 增量导表”能力的具体落地方案。

当前阶段仅输出设计文档，不进行功能开发。

目标是让导表工具在开启指定命令后：

- 正常生成一份完整代码
- 正常生成主数据
- 额外扫描每张表中的 `version` 列
- 将填写了版本号的记录按版本拆分
- 在指定导出目录下创建版本目录
- 将每个版本对应的增量数据导出到对应目录

注意：

- 只有数据做 ABTest 增量导出
- 代码仍然只生成一份
- 未填写 `version` 的记录仍只进入主数据，不进入任何 ABTest 增量目录

## 2. 需求理解

需求可以抽象为一套“主包 + 多版本增量包”的数据导出机制。

以某张表为例：

- 主导出目录中仍输出完整表数据
- 若某些记录的 `version` 列填写了 `A`、`B`、`202403` 等版本值
- 则工具额外输出：
  - `ABTestRoot/A/...`
  - `ABTestRoot/B/...`
  - `ABTestRoot/202403/...`

其中每个版本目录内，只包含该版本命中的记录，不包含未填写版本的记录。  
主数据目录仅包含未填写版本的基础记录。

## 3. 现有工程实现分析

基于当前仓库代码，导表流程已经天然分成“代码生成”和“数据导出”两条链路。

### 3.1 命令行入口

命令行入口位于：

- [Program.cs](/e:/MYSELF/luban/src/Luban/Program.cs#L35)

当前支持的核心参数包括：

- `--conf`
- `-t`
- `-c`
- `-d`
- `-x`

其中：

- `-c` 用于代码导出目标
- `-d` 用于数据导出目标
- `-x` 用于传入自定义扩展参数

这意味着 ABTest 功能适合继续沿用现有风格，通过 `-x abtest.xxx=yyy` 开启和配置。

### 3.2 代码与数据是分离生成的

导表主流程位于：

- [DefaultPipeline.cs](/e:/MYSELF/luban/src/Luban.Core/Pipeline/DefaultPipeline.cs#L109)

其中：

- `CodeTargets` 负责生成代码
- `DataTargets` 负责生成数据

因此“代码只生成一份、ABTest 仅影响数据导出”完全符合当前架构，不需要强行拆流程。

### 3.3 数据导出公共层

数据导出公共入口位于：

- [DataExporterBase.cs](/e:/MYSELF/luban/src/Luban.Core/DataTarget/DataExporterBase.cs#L30)

当前默认逻辑是：

- 遍历每张导出表
- 对每张表调用 `dataTarget.ExportTable(table, records)`
- 再由 `OutputSaver` 统一保存到目标目录

这说明 ABTest 功能最适合放在“数据导出编排层”，而不是直接写死在 `bin/json` 某一个具体格式里。

### 3.4 当前 `bin/json` 导出目标

当前内置数据目标：

- `bin` 实现在 [BinaryDataTarget.cs](/e:/MYSELF/luban/src/Luban.DataTarget.Builtin/Binary/BinaryDataTarget.cs#L27)
- `json` 实现在 [JsonDataTarget.cs](/e:/MYSELF/luban/src/Luban.DataTarget.Builtin/Json/JsonDataTarget.cs#L29)

这两个目标都只负责：

- 接收一张表的记录集合
- 序列化成对应格式
- 返回一个 `OutputFile`

因此 ABTest 需求不建议直接侵入 `BinaryDataTarget` / `JsonDataTarget` 的基础逻辑，而应尽量在更高层完成“按版本分组后多次调用导出”。

### 3.5 表数据获取位置

当前每张表的最终记录集合来自：

- [GenerationContext.cs](/e:/MYSELF/luban/src/Luban.Core/GenerationContext.cs#L214)

核心方法：

- `GetTableExportDataList(DefTable table)`

当前返回的是整张表的最终导出记录。

这意味着 ABTest 的版本筛选逻辑有两种落点：

1. 新增一套“按版本取记录”的公共方法
2. 在新的导出器中自行对 `GetTableExportDataList` 的结果再分桶

推荐第 2 种：

- 对现有行为侵入更小
- 不影响普通导表
- 更容易灰度上线

### 3.6 记录结构

记录结构位于：

- [Record.cs](/e:/MYSELF/luban/src/Luban.Core/Defs/Record.cs#L25)

记录中保存的是：

- `DBean Data`
- `Source`
- `Tags`

其中真正的数据字段在 `Data.Fields` 中，因此版本筛选时可以从记录对应的 Bean 字段里读取 `version`。

### 3.7 表结构

表定义位于：

- [DefTable.cs](/e:/MYSELF/luban/src/Luban.Core/Defs/DefTable.cs#L30)

每张表的值类型是 `ValueTType`，其底层 Bean 中包含所有字段定义。  
因此可以在导出前检查该表是否存在名为 `version` 的字段。

## 4. 功能方案设计

## 4.1 功能目标

新增一套“ABTest 增量数据导出”能力，满足：

- 默认关闭
- 开启后仅影响数据导出
- 主数据导出逻辑保持不变
- 额外生成版本增量目录
- 支持 `bin` 和 `json` 等现有数据格式

## 4.2 推荐命令设计

推荐通过 `-x` 配置项开启 ABTest：

- `-x abtest.enable=true`
- `-x abtest.versionField=version`
- `-x abtest.outputDataDir=xxx`

只要最终 `abtest.enable=true`，内部就会自动切换到 `abtest` 数据导出器。

## 4.3 推荐脚本使用方式

示例：

```bat
dotnet Luban.dll ^
  --conf luban.conf ^
  -t client ^
  -c cs-bin ^
  -d bin ^
  -d json ^
  -x abtest.enable=true ^
  -x outputCodeDir=%GEN_CODE_PATH% ^
  -x bin.outputDataDir=%GEN_DATA_PATH%/Bytes/ ^
  -x json.outputDataDir=%GEN_DATA_PATH%/Json/ ^
  -x abtest.versionField=version ^
  -x abtest.outputDataDir=%GEN_DATA_PATH%/ABTest/
```

说明：

- 主代码仍输出到 `outputCodeDir`
- 主数据仍输出到 `bin.outputDataDir`、`json.outputDataDir`
- 主数据仅导出未填写版本号的基础记录
- ABTest 增量数据统一输出到 `abtest.outputDataDir`

## 4.4 目录结构设计

建议输出结构如下：

```text
主数据目录
  Bytes/
    item.bytes
    hero.bytes
  Json/
    item.json
    hero.json

ABTest目录
  ABTest/
    A/
      Bytes/
        item.bytes
        hero.bytes
      Json/
        item.json
        hero.json
    B/
      Bytes/
        item.bytes
```

推荐规则：

- `abtest.outputDataDir` 为 ABTest 根目录
- 每个版本号作为一级目录
- 数据格式作为二级目录
- 文件名沿用原表导出文件名

这样和主数据目录结构保持一致，接入成本最低。

## 4.5 版本字段规则

默认版本字段名：

- `version`

建议支持通过配置覆盖：

- `abtest.versionField`

字段规则建议如下：

- 字段类型必须为 `string`
- 空字符串或空值表示“非 ABTest 记录”
- 非空字符串表示该记录属于某个版本
- 同一张表内，允许“相同 key，不同 version”的多条记录同时存在
- 同一张表内，若出现“相同 key，相同 version”的多条记录，必须报错并终止导出

示例：

| id | name | version |
|---|---|---|
| 1 | sword |   |
| 2 | gun | A |
| 3 | bow | B |
| 4 | wand | A |

则导出结果：

- 主数据：包含 1
- `ABTest/A`：包含 2、4
- `ABTest/B`：包含 3

### 4.5.1 ABTest 下的唯一性规则

ABTest 模式下，记录唯一性不再只看主键本身，而是看：

- `业务key + version`

具体规则：

- 允许同一张表内出现相同业务 key，但 `version` 不同的多条记录
- 不允许同一张表内出现相同业务 key，且 `version` 也相同的多条记录
- 一旦发现“相同 key + 相同 version”重复，必须报错并终止本次导表

示例 1：允许

| id | name | version |
|---|---|---|
| 1001 | sword_a | A |
| 1001 | sword_b | B |

说明：

- 同一个 key `1001`
- 但版本分别为 `A`、`B`
- 合法

示例 2：不允许

| id | name | version |
|---|---|---|
| 1001 | sword_a | A |
| 1001 | sword_b | A |

说明：

- 同一个 key `1001`
- 同一个版本 `A`
- 非法，必须报错

## 4.6 版本值格式约束

建议对版本号做目录名合法性校验。

推荐限制：

- 非空
- 去掉首尾空格后再使用
- 不允许包含 Windows 非法路径字符
- 不允许出现 `.`、`..`

如果命中非法版本号，建议直接报错终止导表，避免生成脏目录。

## 5. 推荐实现方案

## 5.1 不建议直接改 `bin/json` 的基础行为

原因：

- 这些类本质上只负责“序列化一张表”
- 如果把 ABTest 分桶逻辑塞进每个 DataTarget，会导致重复实现
- 后续支持更多格式时也要重复改造

所以推荐把 ABTest 能力放在“数据导出编排层”。

## 5.2 推荐新增一个 ABTest 专用 DataExporter

当前默认数据导出器是：

- [DefaultDataExporter.cs](/e:/MYSELF/luban/src/Luban.DataTarget.Builtin/DefaultDataExporter.cs#L25)

建议新增：

- `AbtestDataExporter`

注册名建议：

- `abtest`

职责：

1. 先执行原有主数据导出逻辑
2. 若未开启 ABTest，则行为等同默认导出器
3. 若开启 ABTest，则将未填写版本的记录作为主数据，将填写版本的记录按版本分组
4. 针对每个版本分组，复用现有 `DataTarget` 再导出一次
5. 将导出的文件路径重写为 `版本目录/格式目录/文件名`

推荐理由：

- 改动集中
- 不影响代码生成
- 不影响普通数据导出
- 可以复用现有 `bin/json/lua/xml/...` 的序列化逻辑

## 5.3 备选方案：在 `DefaultDataExporter` 内做分支

也可以直接修改默认导出器，在其中增加：

- 主数据正常导出
- 若开启 ABTest，则追加增量导出

优点：

- 调用链更短

缺点：

- 默认导出器逻辑会被拉复杂
- 普通导表和 ABTest 导表强耦合
- 后续维护风险更高

因此不作为首选。

## 5.4 推荐的核心实现步骤

### 步骤 1：扩展配置读取

在 [Program.cs](/e:/MYSELF/luban/src/Luban/Program.cs#L35) 的参数汇总阶段读取：

- `abtest.enable=true`

并在启动时自动切换到 `abtest` 数据导出器。

### 步骤 2：新增 ABTest 配置读取工具

建议新增一组配置读取封装，例如：

- `AbtestExportOptions`

建议包含：

- `Enable`
- `VersionFieldName`
- `OutputDataDir`
- `ValidateVersionDirName`

推荐默认值：

- `Enable = false`
- `VersionFieldName = version`

### 步骤 3：新增 ABTest 导出器

新增：

- `src/Luban.DataTarget.Builtin/AbtestDataExporter.cs`

注册：

- `[DataExporter("abtest")]`

行为：

1. 先复用默认导出主数据
2. 再执行版本增量导出

### 步骤 4：实现按表版本分桶

对每张导出表：

1. 读取 `ctx.GetTableExportDataList(table)`
2. 检查表是否存在 `version` 字段
3. 若不存在：
   - 该表仍按正常导表流程导出主数据
   - 不生成该表的 ABTest 增量数据
4. 若存在：
   - 遍历所有记录
   - 取出 `version` 字段值
   - 空值跳过
   - 非空值加入对应版本桶

### 步骤 5：复用现有 DataTarget 做版本导出

对每个版本桶：

1. 调用当前 `dataTarget.ExportTable(table, versionRecords)`
2. 拿到 `OutputFile`
3. 重写输出路径为：

```text
{version}/{formatDir}/{originFileName}
```

其中 `formatDir` 建议按 DataTarget 名决定：

- `bin -> Bytes`
- `json -> Json`

建议增加统一映射函数，不要写死在各处。

### 步骤 6：将版本输出保存到 ABTest 根目录

当前保存目录由：

- [OutputSaverBase.cs](/e:/MYSELF/luban/src/Luban.Core/OutputSaver/OutputSaverBase.cs#L29)

决定。

由于当前 `OutputSaver` 只按 target 名取一个输出目录，因此推荐做法是：

- 为 ABTest 导出构造一个专用的 `OutputFileManifest`
- 其 `TargetName` 使用类似：
  - `abtest.bin`
  - `abtest.json`

然后通过 `-x` 支持：

- `abtest.bin.outputDataDir=...`
- `abtest.json.outputDataDir=...`

另一种更简单的方案是：

- 在 ABTest 导出器中直接把相对路径拼成 `A/Bytes/item.bytes`
- 再把 manifest 输出目录统一设为 `abtest.outputDataDir`

推荐后者，原因是：

- 配置更少
- 用户更容易理解
- 不需要引入多个新的 target 命名空间

## 6. 具体配置建议

建议新增以下配置项：

### 必选项

- `abtest.outputDataDir`

说明：

- ABTest 增量数据根目录

### 可选项

- `abtest.enable`
- `abtest.versionField`
- `abtest.cleanUpOutputDir`
- `abtest.dirNameMap.bin`
- `abtest.dirNameMap.json`

推荐默认值：

```text
abtest.enable=false
abtest.versionField=version
abtest.cleanUpOutputDir=true
abtest.dirNameMap.bin=Bytes
abtest.dirNameMap.json=Json
```

## 7. 清理策略

ABTest 导出目录建议独立清理，不要和主数据目录共用。

推荐行为：

- 主数据目录仍按原逻辑清理
- 若开启 ABTest 且 `abtest.cleanUpOutputDir=true`
- 则在本次导出开始前清空整个 `abtest.outputDataDir`
- 无论本次是否实际生成 ABTest 增量文件，都执行上述清理

注意：

- 这里应只清空 ABTest 根目录
- 不应删除主数据目录

## 8. 校验与报错规则

建议增加以下校验：

### 8.1 字段存在性

若某张表不存在 `version` 字段：

- 不报错
- 该表仍按正常流程导出主数据
- 不生成该表的 ABTest 增量数据

原因：

- 不要求所有表都支持 ABTest

### 8.2 字段类型校验

若存在 `version` 字段，但类型不是 `string`：

- 直接报错终止

原因：

- 目录分桶逻辑需要稳定的字符串表示

### 8.3 非法目录名校验

若版本值包含非法路径字符：

- 直接报错终止

### 8.4 空值处理

若 `version` 为空字符串或空值：

- 不进入任何 ABTest 目录

### 8.5 ABTest 唯一性校验

若某张表存在 `version` 字段，则需要额外执行 ABTest 唯一性校验。

校验规则：

- 允许相同 key 出现在不同版本下
- 不允许相同 key 出现在同一版本下重复

建议唯一性判断键：

- `table business key + version`

其中业务 key 的判定规则建议为：

- `MAP` 表：使用表的主索引字段
- `LIST` 表：
  - 若配置了索引，则使用表索引
  - 若为联合索引，则使用联合 key
  - 若为多 key，则按当前表定义的完整 key 组合作为唯一性键
- `ONE` 表：
  - 因整张表理论上只有一条记录，可将其视为固定 key
  - 因此 `ONE` 表不允许在同一 version 下出现多条记录

若命中“相同 key + 相同 version”重复：

- 直接报错终止
- 错误信息中应至少带上：
  - 表名
  - key 值
  - version 值
  - 冲突记录来源文件

### 8.6 LIST 表处理

ABTest 仅做“记录筛选导出”，不涉及 patch 合并逻辑。  
因此 MAP、LIST、ONE 三种表模式理论上都支持，只要能够从记录中正确取到 `version` 字段。

需要特别说明：

- `ONE` 表如果唯一记录带版本号，则对应版本目录下导出这一条单例记录
- `LIST` 表按记录逐条筛选
- `MAP` 表按记录逐条筛选

## 9. 与现有功能的关系

## 9.1 与主导表的关系

- 主导表行为不变
- ABTest 只是额外多导一份增量数据

## 9.2 与代码生成的关系

- 代码生成完全不受影响
- 仍只生成一份

## 9.3 与 patch 的关系

当前 `TableDataInfo` 已支持主表 + patch 合并，见：

- [TableDataInfo.cs](/e:/MYSELF/luban/src/Luban.Core/Defs/TableDataInfo.cs#L42)

因此 ABTest 版本筛选应基于：

- `FinalRecords`

也就是“patch 合并后的最终结果”。

这样更符合最终投放数据的真实语义。

## 10. 推荐开发改动点

建议涉及文件如下：

- [Program.cs](/e:/MYSELF/luban/src/Luban/Program.cs#L35)
- [BuiltinOptionNames.cs](/e:/MYSELF/luban/src/Luban.Core/BuiltinOptionNames.cs#L23)
- [DataExporterBase.cs](/e:/MYSELF/luban/src/Luban.Core/DataTarget/DataExporterBase.cs#L25)
- [DefaultDataExporter.cs](/e:/MYSELF/luban/src/Luban.DataTarget.Builtin/DefaultDataExporter.cs#L25)
- 新增 `AbtestDataExporter.cs`
- 可能新增 `AbtestExportOptions.cs`
- 可能新增 `AbtestExportUtil.cs`

推荐尽量避免修改：

- `BinaryDataTarget.cs`
- `JsonDataTarget.cs`

除非只是补非常轻量的辅助接口。

## 11. 验收标准

功能完成后，至少满足以下验收项：

1. 不开启 ABTest 时，导表结果与当前完全一致。
2. 开启 ABTest 后，代码输出与当前完全一致，只生成一份。
3. 开启 ABTest 后，主数据仅包含未填写 `version` 的基础记录。
4. 带 `version` 的记录会按版本输出到对应目录。
5. 未带 `version` 的记录不会进入任何 ABTest 目录。
6. 不同版本之间数据彼此隔离。
7. 同一张表内允许相同 key 出现在不同 version 下。
8. 同一张表内若出现相同 key 且相同 version 的重复记录，会明确报错并终止导出。
9. 不含 `version` 字段的表不会报错，仍正常导出主数据，并且不会生成 ABTest 增量文件。
10. `version` 字段不是字符串时会明确报错。
11. 非法版本目录名会明确报错。
12. `bin` 和 `json` 均支持 ABTest 增量导出。

## 12. 测试建议

建议准备以下测试场景：

### 场景 1：普通表，无 `version` 字段

预期：

- 主数据正常导出
- 不生成 ABTest 数据

### 场景 2：有 `version` 字段，部分记录为空

预期：

- 主数据只包含未填写版本的记录
- ABTest 目录只包含非空版本记录

### 场景 3：同表含多个版本值

预期：

- 每个版本目录只导出自己的记录

### 场景 4：同 key，不同 version

预期：

- 导表成功
- 不同版本目录各自导出对应记录

### 场景 5：同 key，同 version

预期：

- 明确报错
- 导出失败

### 场景 6：`version` 字段类型错误

预期：

- 明确报错

### 场景 7：版本值非法

预期：

- 明确报错

### 场景 8：patch 覆盖后的记录带版本

预期：

- 按最终记录结果导出

## 13. 推荐结论

基于当前工程结构，最合适的落地方式是：

1. 新增 `abtest.*` 配置项。
2. 在数据导出层新增一个 ABTest 专用 `DataExporter`。
3. 主数据按现有逻辑导出。
4. ABTest 增量数据基于最终记录集按 `version` 字段分桶后复用现有 `DataTarget` 导出。
5. 代码生成链路不做任何改动。

这是当前仓库下改动最小、与现有架构最一致、后续可维护性最好的一种方案。

## 14. 当前待确认事项

正式开发前，还需要最终确认以下产品口径：

1. `version` 字段名是否固定为 `version`，还是必须允许自定义。
2. ABTest 目录结构是否固定为 `版本号/Bytes|Json/文件`。
3. 同一条记录是否未来可能支持多个版本值。
4. 版本值是否区分大小写。
5. ONE 表是否允许参与 ABTest。
6. 若某张表所有记录都没有版本值，是否完全不生成该表的 ABTest 文件。

如果这些点确认后无变化，就可以直接进入开发阶段。

## 15. 本轮新增确认口径

已确认新增规则：

- 当开启 ABTest 导表功能时，如果某张表未配置 `ver/version` 字段，这张表仍然按普通表正常导出主数据。
- 上述表不会报错。
- 上述表不会生成任何 ABTest 增量文件。
- 同一张表内允许相同 key 出现在不同 version 下。
- 同一张表内若出现相同 key 且相同 version 的重复记录，必须报错并终止导出。
- 当开启 ABTest 且某条记录填写了版本号时，这条记录不会进入主数据，只会进入对应版本的增量目录。

因此 ABTest 功能是“额外追加增量导出能力”，而不是“要求所有表都必须具备版本字段”。
