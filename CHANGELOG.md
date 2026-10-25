# 更新日志

## 0.1.0

- 改名 `Plain Craft Launcher (PCL) Alternative (PCLA)`，隔离数据目录、注册表与单例。
- 首次启动从旧 PCL 数据目录一次性导入，不覆盖、不删除旧数据。
- 版本单点化：`Version.props` + `tools/version.ps1`，支持 release / nightly / snapshot。
- 清理赞助解锁、土豆码、虚假更新/遥测入口；主题固定 210 蓝。
- 建立可复用构建、CI、正式发布与预览工作流，发布包附 `LICENCE` 与 SHA-256。
- 内部：统一用户数据迁移、Fabric 列表解析与 Java 探测的错误边界，新增全局崩溃文件兜底。
