namespace PCLCS.Results;

public enum MigrationSection {
    Files,
    Portable,
    Registry,
    Startup,
}

/// <summary>旧数据导入的单个区块失败；Path 是目标位置，便于日志定位。</summary>
public sealed class MigrationError : PclaError {
    public MigrationError(MigrationSection section, string? path, string message, Exception? cause = null)
        : base(CodeFor(section), message, PclaErrorKind.Expected, cause) {
        Section = section;
        Path = path;
    }

    public MigrationSection Section { get; }
    public string? Path { get; }

    private static string CodeFor(MigrationSection section) => section switch {
        MigrationSection.Files => "migration.files",
        MigrationSection.Portable => "migration.portable",
        MigrationSection.Registry => "migration.registry",
        MigrationSection.Startup => "migration.startup",
        _ => "migration.unknown",
    };
}
