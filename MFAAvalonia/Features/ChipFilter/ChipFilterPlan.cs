using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MFAAvalonia.Features.ChipFilter;

public static class ChipFilterCatalog
{
    public static readonly string[] MainSkills =
    [
        "穿甲", "切割", "征服", "重击", "支援", "精力", "蓄能", "收割", "屏障", "铁壁",
        "灵巧", "暴怒", "致命", "腐蚀", "集中", "金刚", "痛击", "扩大", "物攻", "能量",
        "装填", "光幕", "钝化", "特防", "神威", "神力", "神速", "振奋", "消除", "重伤",
        "连击", "乘风", "压力", "反击", "协击", "引爆"
    ];

    public static readonly string[] SubSkills =
    ["攻击", "耐久", "防御", "速度", "瞄准", "暴伤", "命中", "坚韧"];

    public static readonly IReadOnlyDictionary<string, string[]> RecommendedSubSkills =
        new Dictionary<string, string[]>
        {
            ["切割"] = ["攻击", "暴伤", "瞄准", "命中"],
            ["协击"] = ["攻击", "暴伤", "瞄准"],
            ["反击"] = ["攻击", "暴伤", "瞄准"],
            ["致命"] = ["攻击", "暴伤", "瞄准"],
            ["收割"] = ["攻击", "暴伤", "瞄准"],
            ["灵巧"] = ["攻击", "暴伤", "瞄准"],
            ["消除"] = ["攻击", "暴伤", "瞄准"],
            ["神力"] = ["攻击", "暴伤", "瞄准"],
            ["神威"] = ["攻击", "暴伤", "瞄准"],
            ["穿甲"] = ["攻击", "瞄准", "暴伤"],
            ["能量"] = ["攻击", "瞄准", "暴伤"],
            ["物攻"] = ["攻击", "瞄准", "暴伤"],
            ["乘风"] = ["攻击", "瞄准", "暴伤"],
            ["压力"] = ["攻击", "瞄准", "暴伤"],
            ["连击"] = ["暴伤", "攻击", "瞄准"],
            ["征服"] = ["暴伤", "攻击", "瞄准"],
            ["扩大"] = ["暴伤", "攻击", "瞄准"],
            ["痛击"] = ["暴伤", "攻击", "瞄准"],
            ["腐蚀"] = ["攻击", "命中", "耐久"],
            ["集中"] = ["命中", "攻击", "耐久"],
            ["振奋"] = ["耐久", "防御", "坚韧"],
            ["重伤"] = ["耐久", "防御", "坚韧"],
            ["装填"] = ["速度", "耐久", "防御"],
            ["引爆"] = ["速度", "耐久", "防御"],
            ["精力"] = ["攻击", "瞄准", "暴伤"],
            ["重击"] = ["速度", "攻击"],
            ["屏障"] = ["耐久", "防御", "坚韧"],
            ["支援"] = ["耐久", "防御", "坚韧"],
            ["蓄能"] = ["耐久", "防御", "坚韧"],
            ["暴怒"] = ["速度", "耐久", "防御"],
            ["钝化"] = ["耐久", "防御", "坚韧"],
            ["金刚"] = ["耐久", "防御", "坚韧"],
            ["神速"] = ["耐久", "防御", "坚韧"],
            ["铁壁"] = ["耐久", "防御", "坚韧"],
            ["光幕"] = ["耐久", "防御", "坚韧"],
            ["特防"] = ["耐久", "防御", "坚韧"]
        };

    public static Dictionary<string, ChipSubSkillRule> CreateRecommendedConditions() =>
        MainSkills.ToDictionary(main => main, main => new ChipSubSkillRule
        {
            EffectiveSubSkills = RecommendedSubSkills[main].ToList(),
            MinimumTotalLevel = 3
        });
}

public static class ChipLockModes
{
    public const string Lock = "lock";
    public const string Conditional = "conditional";
    public const string Unlock = "unlock";
}

public sealed class ChipFilterPlan
{
    public int Version { get; set; } = 3;
    public string Name { get; set; } = "我的芯片筛选方案";
    public Dictionary<int, ChipLevelRule> Levels { get; set; } = [];

    public static ChipFilterPlan CreateDefault() => new()
    {
        Levels = new Dictionary<int, ChipLevelRule>
        {
            [1] = new() { Mode = ChipLockModes.Unlock },
            [2] = new()
            {
                Mode = ChipLockModes.Conditional,
                Conditions = ChipFilterCatalog.CreateRecommendedConditions()
            },
            [3] = new() { Mode = ChipLockModes.Lock }
        }
    };
}

public sealed class ChipLevelRule
{
    public string Mode { get; set; } = ChipLockModes.Unlock;
    public Dictionary<string, ChipSubSkillRule> Conditions { get; set; } = [];
}

public sealed class ChipSubSkillRule
{
    public int MinimumTotalLevel { get; set; } = 3;
    public List<string> EffectiveSubSkills { get; set; } = [];

    public ChipSubSkillRule Clone() => new()
    {
        MinimumTotalLevel = MinimumTotalLevel,
        EffectiveSubSkills = EffectiveSubSkills.ToList()
    };
}

public static class ChipFilterPlanStore
{
    public static string PlanPath => Path.Combine(ResolveProjectRoot(), "config", "chip_filter_plan.json");

    public static ChipFilterPlan Load()
    {
        if (!File.Exists(PlanPath))
            return ChipFilterPlan.CreateDefault();
        try
        {
            var plan = JsonSerializer.Deserialize<ChipFilterPlan>(File.ReadAllText(PlanPath), JsonOptions());
            return ChipFilterPlanCodec.Normalize(plan ?? ChipFilterPlan.CreateDefault());
        }
        catch
        {
            return ChipFilterPlan.CreateDefault();
        }
    }

    public static void Save(ChipFilterPlan plan)
    {
        var normalized = ChipFilterPlanCodec.Normalize(plan);
        Directory.CreateDirectory(Path.GetDirectoryName(PlanPath)!);
        var temp = PlanPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(normalized, JsonOptions()), Encoding.UTF8);
        File.Move(temp, PlanPath, true);
    }

    internal static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private static string ResolveProjectRoot()
    {
        var configured = Environment.GetEnvironmentVariable("LAA_PROJECT_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 5 && current != null; i++, current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "interface.json")))
                return string.Equals(current.Name, "gui", StringComparison.OrdinalIgnoreCase)
                    ? current.Parent?.FullName ?? current.FullName
                    : current.FullName;
        }
        return Directory.GetParent(AppContext.BaseDirectory)?.FullName ?? AppContext.BaseDirectory;
    }
}

public static class ChipFilterPlanCodec
{
    private const string Prefix = "LAA-CF3";

    public static ChipFilterPlan Normalize(ChipFilterPlan source)
    {
        var name = (source.Name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 40)
            throw new InvalidDataException("方案名称不能为空且不能超过40个字符");

        var defaults = ChipFilterPlan.CreateDefault();
        var result = new ChipFilterPlan { Name = name, Version = 3 };
        foreach (var level in new[] { 1, 2, 3 })
        {
            source.Levels.TryGetValue(level, out var sourceLevel);
            sourceLevel ??= defaults.Levels[level];
            if (sourceLevel.Mode is not (ChipLockModes.Lock or ChipLockModes.Conditional or ChipLockModes.Unlock))
                throw new InvalidDataException($"主词条{level}级的处理方式无效");

            var targetLevel = new ChipLevelRule { Mode = sourceLevel.Mode };
            if (sourceLevel.Mode == ChipLockModes.Conditional)
            {
                foreach (var main in ChipFilterCatalog.MainSkills)
                {
                    if (!sourceLevel.Conditions.TryGetValue(main, out var condition))
                        continue;
                    var normalizedCondition = new ChipSubSkillRule();
                    if (condition.MinimumTotalLevel is not (2 or 3 or 4 or 5 or 6))
                        throw new InvalidDataException($"主词条{level}级“{main}”的有效副词条总等级门槛无效");
                    normalizedCondition.MinimumTotalLevel = condition.MinimumTotalLevel;
                    normalizedCondition.EffectiveSubSkills = (condition.EffectiveSubSkills ?? [])
                        .Where(ChipFilterCatalog.SubSkills.Contains)
                        .Distinct()
                        .OrderBy(value => Array.IndexOf(ChipFilterCatalog.SubSkills, value))
                        .ToList();
                    if (normalizedCondition.EffectiveSubSkills.Count == 0)
                        throw new InvalidDataException($"主词条{level}级“{main}”至少需要一个有效副词条");
                    targetLevel.Conditions[main] = normalizedCondition;
                }
            }
            result.Levels[level] = targetLevel;
        }
        return result;
    }

    public static string Encode(ChipFilterPlan source)
    {
        var normalized = Normalize(source);
        var raw = JsonSerializer.SerializeToUtf8Bytes(normalized, ChipFilterPlanStore.JsonOptions());
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, true))
            zlib.Write(raw);
        var packed = output.ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(packed)).ToLowerInvariant()[..10];
        var payload = Convert.ToBase64String(packed).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Prefix}-{checksum}-{payload}";
    }

    public static ChipFilterPlan Decode(string code)
    {
        var compact = string.Concat((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c)));
        if (compact.Length > 32768)
            throw new InvalidDataException("方案码过长");
        var prefix = Prefix + "-";
        if (!compact.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("不是受支持的LAA-CF3芯片方案码");
        var split = compact[prefix.Length..].Split('-', 2);
        if (split.Length != 2)
            throw new InvalidDataException("方案码内容损坏");
        var payload = split[1].Replace('-', '+').Replace('_', '/');
        payload += new string('=', (4 - payload.Length % 4) % 4);
        var packed = Convert.FromBase64String(payload);
        var checksum = Convert.ToHexString(SHA256.HashData(packed)).ToLowerInvariant()[..10];
        if (!string.Equals(checksum, split[0], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("方案码校验失败");
        using var input = new MemoryStream(packed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = zlib.Read(buffer, 0, Math.Min(buffer.Length, 65537 - (int)output.Length));
            if (read == 0)
                break;
            output.Write(buffer, 0, read);
            if (output.Length > 65536)
                throw new InvalidDataException("方案数据过大");
        }
        var plan = JsonSerializer.Deserialize<ChipFilterPlan>(output.ToArray(), ChipFilterPlanStore.JsonOptions());
        return Normalize(plan ?? throw new InvalidDataException("方案码无法解析"));
    }
}
