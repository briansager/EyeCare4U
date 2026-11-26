# Third-party notices for the portable candidate

These notices cover redistributed dependencies, not the EyeCare4U project. **The owner has not selected an EyeCare4U license. That decision blocks a public release.** No project license is granted or inferred here.

| Redistributed component | Version | Upstream license / included notice |
| --- | --- | --- |
| Microsoft.Data.Sqlite and Microsoft.Data.Sqlite.Core | 11.0.0 | MIT; .NET Foundation and Contributors; `licenses/Microsoft.Data.Sqlite-LICENSE.txt` |
| SQLitePCLRaw bundle/config/core/provider | 3.0.5 | Apache-2.0; SourceGear, LLC (package copyright 2014–2025); `licenses/SQLitePCLRaw-LICENSE.txt` and upstream `SQLitePCLRaw-NOTICE.txt` |
| SQLite native library | 3.53.4 | Package license declares public domain; `licenses/SQLite-LICENSE.txt` and the SQLite notice above |
| .NET runtime and Windows Desktop (WPF/Windows Forms) | 10.0.15 | MIT plus upstream third-party notices; runtime-pack licenses/notices and WPF/Windows Forms notice files in `licenses/` |

Microsoft.Data.Sqlite source/license: https://github.com/dotnet/efcore/tree/v11.0.0
SQLitePCLRaw source/license/NOTICE: https://github.com/ericsink/SQLitePCL.raw/tree/ed046114d5a30534e13294d94d78eb73de896ad4
SQLite copyright statement: https://sqlite.org/copyright.html
.NET runtime: https://github.com/dotnet/runtime/tree/v10.0.15
WPF notices: https://github.com/dotnet/wpf/blob/v10.0.15/THIRD-PARTY-NOTICES.TXT
Windows Forms notices: https://github.com/dotnet/winforms/blob/v10.0.15/THIRD-PARTY-NOTICES.TXT

Upstream license/notice files are included without editing. Runtime-pack license/notices are copied from the exact restored packs during packaging. The app icon is the repository's original asset. Windows system sound aliases are referenced; no external audio files are redistributed.

Build/test tools are not shipped: .NET SDK 10.0.404 (MIT plus its upstream notices), Microsoft.NET.Test.Sdk 17.14.1 (MIT), xunit 2.9.3 (Apache-2.0), xunit.runner.visualstudio 3.1.4 (Apache-2.0) and their test-only transitives. The test-only xunit v2 package is legacy/security-fixes-only per official NuGet metadata; it is retained because the existing suite works and no vulnerability was reported, without a feature-only migration to v3. Exact runtime and test dependency versions/hashes are in committed packages.lock.json files.

The package script preserves required upstream notices; this inventory is not a substitute for the owner's public-release license review.
