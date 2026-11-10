#:package ClosedXML@0.105.1
#:property Nullable=enable
#:property ImplicitUsings=enable
// taskexcel.cs — 任务流水线 Excel 工具（tasks/tasks.xlsx）
// 用法: dotnet run taskexcel.cs <init|read|add|update|log|stats|doc> [选项]
// 规范详见 ../references/excel-schema.md

using ClosedXML.Excel;

Console.OutputEncoding = System.Text.Encoding.UTF8;

string[] Types = { "feature", "bug", "test", "docs", "refactor", "chore" };
string[] Statuses = { "待处理", "进行中", "待审核", "已完成", "已跳过", "阻塞" };
string[] Priorities = { "P0", "P1", "P2", "P3" };
string[] Stages = { "分析", "拆分", "执行", "审核", "返工", "回写", "提交", "阻塞", "跳过" };
string[] Results = { "成功", "失败", "通过", "不通过", "—" };
string[] TaskHeaders = { "编号", "类型", "状态", "优先级", "标题", "描述", "验收标准", "关联编号", "执行者", "结果摘要", "提交哈希", "更新时间" };
string[] LogHeaders = { "记录编号", "任务编号", "时间", "阶段", "执行者", "结果", "说明", "关联产物" };

if (args.Length == 0) { Usage(); return 1; }

var root = FindRoot();
var xlsxPath = Path.Combine(root, "tasks", "tasks.xlsx");
var recordsDir = Path.Combine(root, "tasks", "records");

try
{
    return args[0].ToLowerInvariant() switch
    {
        "init" => CmdInit(ParseOpts(args), xlsxPath),
        "read" => CmdRead(ParseOpts(args), xlsxPath),
        "add" => CmdAdd(ParseOpts(args), xlsxPath),
        "update" => CmdUpdate(ParseOpts(args), xlsxPath),
        "log" => CmdLog(ParseOpts(args), xlsxPath),
        "stats" => CmdStats(xlsxPath),
        "doc" => CmdDoc(ParseOpts(args), xlsxPath, recordsDir),
        _ => UnknownCmd(args[0]),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine("错误: " + ex.Message);
    return 1;
}

// ---------- 命令实现 ----------

int CmdInit(Dictionary<string, string> o, string path)
{
    if (File.Exists(path) && !o.ContainsKey("force"))
    {
        Console.Error.WriteLine("已存在 " + path + "（如需重建请加 --force）");
        return 1;
    }
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var wb = new XLWorkbook();

    // Dict 工作表：枚举与规则单一来源
    var dict = wb.AddWorksheet("Dict");
    FillDict(dict, 1, "类型", Types);
    FillDict(dict, 2, "状态", Statuses);
    FillDict(dict, 3, "优先级", Priorities);
    FillDict(dict, 4, "阶段", Stages);
    FillDict(dict, 5, "结果", Results);
    dict.Cell(1, 7).Value = "提交前缀映射";
    dict.Cell(2, 7).Value = "feature → feat；bug → fix；test → test；docs → docs；refactor → refactor；chore → chore";
    dict.Cell(1, 9).Value = "状态机";
    dict.Cell(2, 9).Value = "待处理→(分析拆分)→进行中→(执行完成)→待审核→(通过)→已完成；不通过≤2轮返工→进行中；超限→阻塞；任何状态→已跳过(须写原因)";
    StyleHeader(dict, autoFilter: false);

    // Tasks 工作表
    var ws = wb.AddWorksheet("Tasks");
    for (int c = 0; c < TaskHeaders.Length; c++) ws.Cell(1, c + 1).Value = TaskHeaders[c];
    StyleHeader(ws, autoFilter: true);
    SetDropdown(ws, "B", dict, "A", Types.Length);
    SetDropdown(ws, "C", dict, "B", Statuses.Length);
    SetDropdown(ws, "D", dict, "C", Priorities.Length);
    SetWidths(ws, 12, 11, 10, 9, 42, 48, 48, 13, 15, 42, 12, 17);

    if (o.ContainsKey("sample"))
    {
        var now = Now();
        WriteRow(ws, 2, "T20260001", "docs", "待处理", "P1", "README 增加 FAQ 常见问题章节",
            "README 目前没有 FAQ 章节；补充使用中的常见问题与解答，方便新用户上手",
            "README 含 FAQ 章节；至少 5 个问答；与现有内容不重复；markdown 链接有效", "", "task-executor", "", "", now);
        WriteRow(ws, 3, "T20260002", "test", "待处理", "P1", "为公开类型补充边界测试用例",
            "在 tests/Zvec.NET.Tests 中为 src/Zvec.NET 的公开类型补充边界/异常路径用例",
            "新增≥3 个边界用例；dotnet test 全绿；用例命名与现有风格一致", "", "task-executor", "", "", now);
        WriteRow(ws, 4, "T20260003", "chore", "待处理", "P2", "删除误入库的杂散文件 tests/Zvec.NET.Tests/new",
            "该文件内容仅一行 FIXED，疑为误提交，污染仓库",
            "文件已删除；dotnet test 全绿", "", "task-executor", "", "", now);
    }

    // Log 工作表
    var log = wb.AddWorksheet("Log");
    for (int c = 0; c < LogHeaders.Length; c++) log.Cell(1, c + 1).Value = LogHeaders[c];
    StyleHeader(log, autoFilter: true);
    SetDropdown(log, "D", dict, "D", Stages.Length);
    SetDropdown(log, "F", dict, "E", Results.Length);
    SetWidths(log, 11, 13, 17, 9, 15, 9, 52, 30);

    wb.SaveAs(path);
    Console.WriteLine("已创建 " + path + (o.ContainsKey("sample") ? "（含示例任务）" : ""));
    return 0;
}

int CmdRead(Dictionary<string, string> o, string path)
{
    using var wb = OpenBook(path);
    var rows = ReadTasks(wb.Worksheet("Tasks")).ToList();

    if (o.TryGetValue("id", out var id))
    {
        var t = rows.FirstOrDefault(r => r.Id == id) ?? throw new Exception("任务不存在: " + id);
        Console.WriteLine($"# {t.Id} {t.Title}");
        Console.WriteLine($"- 类型: {t.Type} ｜ 状态: {t.Status} ｜ 优先级: {t.Priority} ｜ 执行者: {t.Executor} ｜ 更新: {t.Updated}");
        Console.WriteLine($"- 描述: {t.Desc}");
        Console.WriteLine($"- 验收标准: {t.Accept}");
        Console.WriteLine($"- 关联编号: {t.Parent} ｜ 结果摘要: {t.Result} ｜ 提交哈希: {t.Commit}");
        return 0;
    }

    IEnumerable<TaskRow> q = rows;
    if (!o.ContainsKey("all"))
    {
        var st = o.GetValueOrDefault("status", "待处理");
        q = q.Where(r => r.Status == st);
        Console.WriteLine($"筛选: 状态={st}（--all 查看全部）");
    }
    if (o.TryGetValue("type", out var ty)) q = q.Where(r => r.Type == ty);
    var list = q.OrderBy(r => Array.IndexOf(Priorities, r.Priority)).ThenBy(r => r.Id).ToList();
    Console.WriteLine($"共 {list.Count} 个任务\n");
    Console.WriteLine("| 编号 | 类型 | 状态 | 优先级 | 标题 | 执行者 |");
    Console.WriteLine("|------|------|------|--------|------|--------|");
    foreach (var t in list)
        Console.WriteLine($"| {t.Id} | {t.Type} | {t.Status} | {t.Priority} | {t.Title} | {t.Executor} |");
    return 0;
}

int CmdAdd(Dictionary<string, string> o, string path)
{
    var type = Req(o, "type");
    var title = Req(o, "title");
    var desc = Req(o, "desc");
    var accept = Req(o, "accept");
    if (!Types.Contains(type)) throw new Exception($"类型非法: {type}（{string.Join("/", Types)}）");
    var priority = o.GetValueOrDefault("priority", "P2");
    if (!Priorities.Contains(priority)) throw new Exception($"优先级非法: {priority}（{string.Join("/", Priorities)}）");

    using var wb = OpenBook(path);
    var ws = wb.Worksheet("Tasks");
    var rows = ReadTasks(ws).ToList();
    string id;
    if (o.TryGetValue("parent", out var parent))
    {
        if (rows.All(r => r.Id != parent)) throw new Exception("父任务不存在: " + parent);
        var n = rows.Where(r => r.Id.StartsWith(parent + "."))
                    .Select(r => r.Id[(parent.Length + 1)..])
                    .Where(s => int.TryParse(s, out _))
                    .Select(int.Parse).DefaultIfEmpty(0).Max() + 1;
        id = $"{parent}.{n}";
    }
    else
    {
        var year = DateTime.Now.Year;
        var seq = rows.Where(r => r.Id.Length == 9 && r.Id.StartsWith($"T{year}"))
                      .Select(r => int.Parse(r.Id[5..]))
                      .DefaultIfEmpty(0).Max() + 1;
        id = $"T{year}{seq:0000}";
    }

    var r0 = ws.LastRowUsed()?.RowNumber() + 1 ?? 2;
    WriteRow(ws, r0, id, type, "待处理", priority, title, desc, accept,
             o.GetValueOrDefault("parent", ""), o.GetValueOrDefault("executor", "task-executor"), "", "", Now());
    wb.Save();
    Console.WriteLine($"已新增 {id}: {title}");
    return 0;
}

int CmdUpdate(Dictionary<string, string> o, string path)
{
    var id = Req(o, "id");
    using var wb = OpenBook(path);
    var ws = wb.Worksheet("Tasks");
    var r0 = FindRow(ws, id);
    if (o.TryGetValue("status", out var st))
    {
        if (!Statuses.Contains(st)) throw new Exception($"状态非法: {st}（{string.Join("/", Statuses)}）");
        ws.Cell(r0, 3).Value = st;
    }
    if (o.TryGetValue("result", out var re)) ws.Cell(r0, 10).Value = re;
    if (o.TryGetValue("executor", out var ex)) ws.Cell(r0, 9).Value = ex;
    if (o.TryGetValue("commit", out var ch)) ws.Cell(r0, 11).Value = ch;
    ws.Cell(r0, 12).Value = Now();
    wb.Save();
    var changes = string.Join(", ", o.Keys.Where(k => k != "id").Select(k => $"{k}={o[k]}"));
    Console.WriteLine($"已更新 {id}: {changes}");
    return 0;
}

int CmdLog(Dictionary<string, string> o, string path)
{
    var id = Req(o, "id");
    var stage = Req(o, "stage");
    var result = Req(o, "result");
    var note = Req(o, "note");
    if (!Stages.Contains(stage)) throw new Exception($"阶段非法: {stage}（{string.Join("/", Stages)}）");
    if (!Results.Contains(result)) throw new Exception($"结果非法: {result}（{string.Join("/", Results)}）");

    using var wb = OpenBook(path);
    FindRow(wb.Worksheet("Tasks"), id); // 确认任务存在
    var log = wb.Worksheet("Log");
    var rn = 1;
    for (int r = 2; r <= (log.LastRowUsed()?.RowNumber() ?? 1); r++)
    {
        var v = log.Cell(r, 1).GetString().Trim();
        if (v.StartsWith("R") && int.TryParse(v[1..], out var n)) rn = Math.Max(rn, n + 1);
    }
    var r0 = log.LastRowUsed()?.RowNumber() + 1 ?? 2;
    WriteRow(log, r0, $"R{rn:0000}", id, Now(), stage,
             o.GetValueOrDefault("executor", "orchestrator"), result, note, o.GetValueOrDefault("ref", ""));
    wb.Save();
    Console.WriteLine($"已记录 R{rn:0000} [{stage}/{result}] {id}: {note}");
    return 0;
}

int CmdStats(string path)
{
    using var wb = OpenBook(path);
    var rows = ReadTasks(wb.Worksheet("Tasks")).ToList();
    Console.WriteLine($"# 任务统计（共 {rows.Count} 个）");
    Console.WriteLine("\n## 按状态\n");
    Console.WriteLine("| 状态 | 数量 |\n|------|------|");
    foreach (var s in Statuses) Console.WriteLine($"| {s} | {rows.Count(r => r.Status == s)} |");
    Console.WriteLine("\n## 按类型\n");
    Console.WriteLine("| 类型 | 数量 |\n|------|------|");
    foreach (var t in Types) Console.WriteLine($"| {t} | {rows.Count(r => r.Type == t)} |");
    return 0;
}

int CmdDoc(Dictionary<string, string> o, string path, string recordsDir)
{
    var id = Req(o, "id");
    using var wb = OpenBook(path);
    var t = ReadTasks(wb.Worksheet("Tasks")).FirstOrDefault(r => r.Id == id) ?? throw new Exception("任务不存在: " + id);
    var file = Path.Combine(recordsDir, t.Id + ".md");
    if (File.Exists(file)) throw new Exception("已存在 " + file + "（不覆盖，请手动编辑）");
    Directory.CreateDirectory(recordsDir);
    var accepts = t.Accept.Split('；', ';');
    var list = string.Join("\n", accepts.Select((a, i) => $"{i + 1}. {a}"));
    var content = $"""
        # {t.Id} {t.Title}

        > 状态：{t.Status} ｜ 类型：{t.Type} ｜ 优先级：{t.Priority} ｜ 执行者：{t.Executor} ｜ 创建：{Now()}

        ## 1. 任务内容

        **描述**：{t.Desc}

        **验收标准**：

        {list}

        **关联编号**：{(string.IsNullOrEmpty(t.Parent) ? "—" : t.Parent)}

        ## 2. 分析与设计

        （执行者填写：方案要点、涉及模块与文件、拆分子任务及理由）

        ## 3. 执行过程

        | 时间 | 动作 | 详情 |
        |------|------|------|

        ## 4. 审核记录

        （审核者只追加、不修改上文；每轮一个小节 `### 4.N`）

        ## 5. 结论

        （结果摘要、提交哈希、遗留问题）

        """;
    File.WriteAllText(file, content, new System.Text.UTF8Encoding(false));
    Console.WriteLine("已生成 " + file);
    return 0;
}

// ---------- 辅助 ----------

int UnknownCmd(string cmd) { Console.Error.WriteLine("未知命令: " + cmd); Usage(); return 1; }

IEnumerable<TaskRow> ReadTasks(IXLWorksheet ws)
{
    for (int r = 2; r <= (ws.LastRowUsed()?.RowNumber() ?? 1); r++)
    {
        var id = ws.Cell(r, 1).GetString().Trim();
        if (id.Length == 0) continue;
        yield return new TaskRow(id, G(2), G(3), G(4), G(5), G(6), G(7), G(8), G(9), G(10), G(11), G(12));
        string G(int c) => ws.Cell(r, c).GetString().Trim();
    }
}

void WriteRow(IXLWorksheet ws, int r, params string[] v)
{
    for (int c = 0; c < v.Length; c++) ws.Cell(r, c + 1).Value = v[c];
}

int FindRow(IXLWorksheet ws, string id)
{
    for (int r = 2; r <= (ws.LastRowUsed()?.RowNumber() ?? 1); r++)
        if (ws.Cell(r, 1).GetString().Trim() == id) return r;
    throw new Exception("任务不存在: " + id);
}

XLWorkbook OpenBook(string path)
{
    if (!File.Exists(path)) throw new Exception("未找到 " + path + "，请先执行 init");
    return new XLWorkbook(path);
}

string FindRoot()
{
    var dir = new DirectoryInfo(Environment.CurrentDirectory);
    while (dir is not null)
    {
        if (dir.EnumerateFileSystemInfos(".git").Any()) return dir.FullName;
        dir = dir.Parent!;
    }
    return Environment.CurrentDirectory;
}

void FillDict(IXLWorksheet dict, int col, string name, string[] values)
{
    dict.Cell(1, col).Value = name;
    for (int i = 0; i < values.Length; i++) dict.Cell(2 + i, col).Value = values[i];
}

void SetDropdown(IXLWorksheet ws, string col, IXLWorksheet dict, string dictCol, int count)
{
    var dv = ws.Range(col + "2:" + col + "1000").CreateDataValidation();
    dv.List(dict.Range(dictCol + "2:" + dictCol + (count + 1)), true);
}

void StyleHeader(IXLWorksheet ws, bool autoFilter)
{
    var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;
    var header = ws.Range(1, 1, 1, lastCol);
    header.Style.Font.Bold = true;
    header.Style.Font.FontColor = XLColor.White;
    header.Style.Fill.BackgroundColor = XLColor.FromHtml("#2F5496");
    ws.SheetView.FreezeRows(1);
    if (autoFilter)
    {
        var lastRow = Math.Max(2, ws.LastRowUsed()?.RowNumber() ?? 2);
        ws.Range(1, 1, lastRow, lastCol).SetAutoFilter();
    }
}

void SetWidths(IXLWorksheet ws, params int[] widths)
{
    for (int i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];
}

string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm");

string Req(Dictionary<string, string> o, string key)
    => o.TryGetValue(key, out var v) && v.Length > 0 ? v : throw new Exception("缺少必填参数 --" + key);

Dictionary<string, string> ParseOpts(string[] args)
{
    var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 1; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--")) continue;
        var key = args[i][2..];
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) o[key] = args[++i];
        else o[key] = "true";
    }
    return o;
}

void Usage()
{
    Console.WriteLine("""
        taskexcel.cs — 任务流水线 Excel 工具
          dotnet run taskexcel.cs init [--sample] [--force]   创建 tasks/tasks.xlsx（--sample 含示例任务）
          dotnet run taskexcel.cs read [--status X|--type Y|--id Z|--all]  读取任务（默认状态=待处理）
          dotnet run taskexcel.cs add --type T --title X --desc Y --accept Z [--priority P] [--parent ID] [--executor E]
          dotnet run taskexcel.cs update --id X [--status S] [--result R] [--executor E] [--commit H]
          dotnet run taskexcel.cs log --id X --stage S --result R --note N [--ref W] [--executor E]
          dotnet run taskexcel.cs stats                           按状态/类型统计
          dotnet run taskexcel.cs doc --id X                      生成 tasks/records/T########.md 骨架
        """);
}

record TaskRow(string Id, string Type, string Status, string Priority, string Title,
               string Desc, string Accept, string Parent, string Executor, string Result,
               string Commit, string Updated);
