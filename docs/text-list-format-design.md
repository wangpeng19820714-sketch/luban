# text-list 导出格式改造方案

## 1. 文档目的

本文档用于说明当前 Luban 工程中 `text-list` 导出目标的格式改造方案。

本轮只产出设计文档，不修改源码。

目标是把当前“只输出文本 key 列表”的结果，改造成：

```text
[tablename]_[字段名]_[ver]_[key]\ttext
```

其中：

- 左侧为新的文本唯一键
- 中间使用制表符 `\t` 分隔
- 右侧为该字段当前填写的文本内容

## 2. 需求描述

当前业务脚本：

- [gen_text.bat](e:\MYSELF\GameXpert\Shared\Luban\Data\策划配置\gen_text.bat)

通过：

```bat
-d text-list ^
-x outputDataDir=text_key ^
-x l10n.textListFile=text_key.txt ^
```

从所有配置表中抽取文本字段，输出一个文本清单文件。

现在希望调整导出格式：

- 每行输出 `key<TAB>text`
- `key` 的构成为 `[tablename]_[字段名]_[ver]_[key]`
- 若当前记录没有 `ver`，则回退为 `[tablename]_[字段名]_[key]`

其中：

- `tablename` 表示配置表名
- `字段名` 表示当前文本字段名
- `ver` 表示当前记录的 ABTest 版本号
- `key` 表示该条记录的业务主键

示例：

```text
itemtable_name_1001	钻石
itemtable_desc_v1_1001	高级货币
herotable_title_v2_2001	战士
```

## 3. 当前实现分析

当前 `text-list` 的实现位于：

- [TextKeyListDataTarget.cs](/e:/MYSELF/luban/src/Luban.L10N/DataTarget/TextKeyListDataTarget.cs)
- [TextKeyListCollectorVisitor.cs](/e:/MYSELF/luban/src/Luban.L10N/DataTarget/TextKeyListCollectorVisitor.cs)
- [TextKeyCollection.cs](/e:/MYSELF/luban/src/Luban.L10N/DataTarget/TextKeyCollection.cs)

### 3.1 当前导出行为

在 [TextKeyListDataTarget.cs](/e:/MYSELF/luban/src/Luban.L10N/DataTarget/TextKeyListDataTarget.cs#L41) 中：

- 遍历所有表
- 用 `TextKeyListCollectorVisitor` 收集文本
- 最终把所有 key 排序后按换行输出

当前核心逻辑是：

```csharp
var keys = textCollection.Keys.ToList();
keys.Sort((a, b) => string.Compare(a, b, StringComparison.Ordinal));
var content = string.Join("\n", keys);
```

也就是说，当前输出只有一列，没有文本值。

### 3.2 当前采集行为

在 [TextKeyListCollectorVisitor.cs](/e:/MYSELF/luban/src/Luban.L10N/DataTarget/TextKeyListCollectorVisitor.cs#L77) 中：

- 只要字段类型是 `string`
- 且字段带有 `text` tag
- 就把 `data.Value` 原样加入集合

当前逻辑是：

```csharp
if (data != null && type.HasTag("text"))
{
    x.AddKey(data.Value);
}
```

这说明当前系统实际上采集的是：

- 文本字段内容本身

而不是：

- 表名
- 字段名
- 记录 key
- 文本值

### 3.3 当前数据结构不足

在 [TextKeyCollection.cs](/e:/MYSELF/luban/src/Luban.L10N/DataTarget/TextKeyCollection.cs#L23) 中，当前仅保存：

- `HashSet<string> _keys`

这只能表示“一个字符串集合”，无法表达：

- 组合 key
- 原始文本值
- 重复 key 冲突信息

因此如果要输出 `key<TAB>text`，必须扩展采集结构。

## 4. 目标格式设计

## 4.1 行格式

建议输出格式固定为：

```text
<text_key>\t<text_value>
```

示例：

```text
itemtable_name_1	钻石
itemtable_desc_1	货币道具
```

## 4.2 key 组成规则

建议 key 格式：

```text
[tablename]_[fieldname]_[version]_[recordkey]
```

如果当前记录没有版本号，则格式回退为：

```text
[tablename]_[fieldname]_[recordkey]
```

示例：

- `itemtable_name_1`
- `itemtable_desc_v1_1`

## 4.3 字段含义

- `tablename`
  建议使用导出表名的小写形式，优先保持与当前输出文件名风格一致
- `fieldname`
  使用 schema 中该字段的字段名，例如 `name`、`desc`
- `version`
  使用当前记录的 `ver` 字段值；若为空，则这一段不参与 key 组合
- `recordkey`
  使用该条记录在表中的业务 key

## 4.4 分隔符

建议固定使用：

- 制表符 `\t`

原因：

- 兼容 Excel/TSV 导入
- 比空格更稳定
- 文本中即使包含普通空格也不会影响解析

## 5. 关键实现问题

## 5.1 当前 visitor 拿不到表名、字段名、记录 key

现在的 `TextKeyListCollectorVisitor` 是一个纯值访问器，只在字段值层面工作。  
它能拿到：

- `DString data`
- `TType type`

但它拿不到：

- 当前表名
- 当前字段定义对象
- 当前记录对象

因此如果继续沿用现在这套 visitor 结构，没法直接拼出：

```text
tablename_fieldname_version_recordkey
```

这意味着实现上不能只改一两行字符串拼接，而是要把“文本采集”的上下文从“值级别”提升到“记录级别”。

另外还需要额外拿到：

- 当前记录是否存在 `ver` 字段
- 当前记录的 `ver` 值

## 5.2 需要确定 record key 的来源

需求里的 `[key]` 必须落到明确规则，否则实现会有歧义。

建议规则：

- `MAP` 表：使用主索引字段值
- `LIST` 表：
  - 若有单索引，使用该索引值
  - 若为联合索引，建议按 `k1|k2|k3` 拼接，或转成 `field1=value1_field2=value2`
- `ONE` 表：
  - 建议固定为 `one`

如果不先定这个规则，代码实现时会对 `LIST` / `ONE` 表出现口径不一致。

## 5.3 需要处理重复 key

新的输出结构不再是“纯文本值集合”，而是“组合 key -> 文本值”映射。

因此要明确重复策略：

- 若相同组合 key，对应文本值也相同：可视为重复数据，允许去重
- 若相同组合 key，对应文本值不同：必须报错

推荐采用上述规则，这样比简单覆盖更安全。

## 5.4 需要考虑文本中的换行和制表符

如果右侧文本内容里本身包含：

- `\t`
- `\r`
- `\n`

则生成的文本文件会破坏“每行一条”的结构。

建议明确一种处理策略：

1. 保留原样，不额外处理
2. 导出时转义成 `\\t`、`\\n`
3. 发现后直接报错

我更推荐第 2 种：

- 对工具更友好
- 不会破坏文件结构

## 6. 推荐实现方案

## 6.1 不建议继续只用 `TextKeyListCollectorVisitor` 收集纯值

原因：

- 当前 visitor 只能在字段值级别工作
- 很难拿到“表名 + 字段名 + 记录 key”完整上下文

推荐改为：

- 在 `TextKeyListDataTarget` 里按表、按记录遍历
- 在记录遍历过程中显式检查哪些字段带 `text` tag
- 直接构造 `key -> text` 条目

这样改动更直观，也更容易控制输出格式。

## 6.2 推荐新增一条记录结构

建议新增类似结构：

```csharp
public sealed class TextListEntry
{
    public string Key { get; init; }
    public string Value { get; init; }
}
```

或者直接在 collection 中保存：

- `Dictionary<string, string>`

其中：

- key = `tablename_fieldname_version_recordkey` 或 `tablename_fieldname_recordkey`
- value = 文本内容

## 6.3 推荐在 DataTarget 层直接生成

建议把文本列表生成逻辑集中在：

- [TextKeyListDataTarget.cs](/e:/MYSELF/luban/src/Luban.L10N/DataTarget/TextKeyListDataTarget.cs)

实现步骤：

1. 遍历所有导出表
2. 遍历表的最终记录
3. 找出所有带 `text` tag 的字符串字段
4. 读取当前记录的 `ver` 值
5. 计算当前记录的业务 key
6. 若 `ver` 非空，则组合生成 `tablename_fieldname_version_recordkey`
7. 若 `ver` 为空，则组合生成 `tablename_fieldname_recordkey`
6. 把文本值存入 collection
7. 排序后按 `key<TAB>value` 输出

## 6.4 推荐排序规则

建议仍按 key 做字典序排序：

```text
itemtable_desc_1
itemtable_name_1
itemtable_name_2
```

这样输出稳定，便于 diff。

## 7. 建议改动点

推荐涉及文件：

- [TextKeyListDataTarget.cs](/e:/MYSELF/luban/src/Luban.L10N/DataTarget/TextKeyListDataTarget.cs)
- [TextKeyCollection.cs](/e:/MYSELF/luban/src/Luban.L10N/DataTarget/TextKeyCollection.cs)

可选改动：

- [TextKeyListCollectorVisitor.cs](/e:/MYSELF/luban/src/Luban.L10N/DataTarget/TextKeyListCollectorVisitor.cs)

推荐方案下，`TextKeyListCollectorVisitor` 甚至可以不再作为核心实现入口，而只保留兼容或被简化。

## 8. 配置兼容建议

现有脚本：

- [gen_text.bat](e:\MYSELF\GameXpert\Shared\Luban\Data\策划配置\gen_text.bat)

中这几项可以继续沿用：

```bat
-d text-list ^
-x abtest.enable=true ^
-x abtest.versionField=ver ^
-x outputDataDir=text_key ^
-x l10n.textListFile=text_key.txt ^
```

也就是说：

- 输出目标名不变
- 输出文件名配置不变
- `text-list` 若用于包含 ABTest 版本数据的工程，必须显式传入 `-x abtest.enable=true` 和 `-x abtest.versionField=ver`
- 只改变文件内容格式

这样业务接入成本最低。

## 9. 验收标准

功能完成后，至少满足：

1. `text-list` 仍然能正常导出到 `l10n.textListFile` 指定文件。
2. 每一行格式为 `key<TAB>text`。
3. 有 `ver` 时，`key` 由 `tablename_fieldname_ver_recordkey` 构成。
4. 无 `ver` 时，`key` 由 `tablename_fieldname_recordkey` 构成。
4. 同一个表内多个 `text` 字段可分别导出。
5. 输出结果稳定排序。
6. 相同组合 key 且文本不同会明确报错。
7. 对现有 `text-list` 脚本配置兼容，不要求增加新参数。

## 10. 待确认口径

正式开发前，建议把这几个点定死：

1. `tablename` 用 schema 表名、Excel 名，还是输出文件名风格的小写名。
2. `ver` 字段名是否固定按 `ver` 读取，还是允许复用 `version`。
3. `[key]` 对于 `LIST` 表和 `ONE` 表如何表示。
4. 文本中若包含换行或制表符，是否做转义。
5. 相同组合 key 且文本相同，是否允许静默去重。
6. 是否需要兼容旧格式，或另开一个新的 `dataTarget`。

## 11. 推荐结论

从当前源码结构看，这个需求可以落地，而且不需要改动主导表链路。

最合适的方式是：

1. 保留现有 `text-list` 目标和脚本配置。
2. 将 `text-list` 的输出内容从“纯文本 key 列表”升级成“`key<TAB>text` 列表”。
3. 在 `TextKeyListDataTarget` 中按“表 + 记录 + 字段”收集完整上下文。
4. 有 `ver` 时使用 `tablename_fieldname_ver_recordkey`，无 `ver` 时使用 `tablename_fieldname_recordkey`。

如果这些口径确认后无变化，就可以直接进入开发阶段。
