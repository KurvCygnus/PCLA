# PCLA 发布清单

## 通道

| 通道 | 版本串 | 触发 | 产物 |
| --- | --- | --- | --- |
| release | `0.1.0` | tag `v0.1.0` | GitHub Release + zip + `SHA256SUMS.txt` |
| nightly | `0.1.0-nightly-<短哈希>` | 每日 02:00 CST 或手动 | Actions artifact |
| snapshot | `0.1.0-snapshot` | CI / 手动 | Actions artifact |

## 正式发布

1. 更新 `Version.props` 的 `PclaSemVer` 与 `CHANGELOG.md`，提交。
2. 打 tag `v<PclaSemVer>` 并推送。
3. 等 `release.yml` 完成，检查 zip 内容、`SHA256SUMS.txt` 与 Release notes。
4. 在干净目录解压 zip，启动 `PCLA.exe`：标题、版本、关于页署名/赞助/许可链接正确。
5. 确认 `%APPDATA%\PCLA`、`<exe>\PCLA`、`HKCU\Software\PCLADebug` 创建，旧 `PCL` 数据未被修改。
6. 确认发布包不含 `PCL_MS_CLIENT_ID` 与 `PCL_CURSEFORGE_API_KEY`，不含旧名 exe 与旧 pack URI。
7. 首次公开发布前确认 Visual Companion 图标已替换；未替换时 Release notes 标注"图标待替换"。

## 预览

nightly/snapshot 只验证 Actions artifact 的版本串、zip 内容与校验和；不创建 Release、不打 tag。

## 本地检查

- `dotnet test tests\PCLCS.Tests\PCLCS.Tests.csproj -c Release`：192 通过 + 1 跳过（含新增版本/迁移用例）。
- `MeloongCore.Tests` 不进 CI：TUnit 在 .NET 10 下不可用，且已有 `FileUtilsTest.cs:7` 失败；本地运行 `MeloongCore\Tests\bin\Release\net48\MeloongCore.Tests.exe`。
- 凭据只从 `PCL_MS_CLIENT_ID` / `PCL_CURSEFORGE_API_KEY` 环境变量读取，不嵌入产物。
