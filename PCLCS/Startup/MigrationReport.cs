using PCLCS.Results;

namespace PCLCS.Startup;

/// <summary>单个迁移区块的结果。</summary>
public sealed class MigrationSectionResult {
    public MigrationSectionResult(MigrationSection section, Result<Unit, MigrationError> outcome) {
        Section = section;
        Outcome = outcome;
    }

    public MigrationSection Section { get; }
    public Result<Unit, MigrationError> Outcome { get; }
}

/// <summary>一次迁移的完整报告；Startup 只用于 RunDefault 初始化失败。</summary>
public sealed class MigrationReport {
    public MigrationReport(IReadOnlyList<MigrationSectionResult> sections) {
        Sections = sections;
    }

    public IReadOnlyList<MigrationSectionResult> Sections { get; }
    public bool HasFailures => Sections.Any(section => section.Outcome.IsFailure);
}
