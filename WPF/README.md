# MiniApps Desktop

MiniApps là ứng dụng WPF dành cho Windows 10/11 x64. Ứng dụng yêu cầu quyền Administrator ngay khi khởi động và ưu tiên .NET Framework 4.8 có sẵn trên Windows.

## Cài nhanh bằng PowerShell

```powershell
irm https://raw.githubusercontent.com/mson-ssh/miniapp/main/WPF/bootstrap.ps1 | iex
```

`irm` là bí danh của `Invoke-RestMethod`. Bootstrap tự yêu cầu UAC, chọn gói net48 khi máy có .NET Framework 4.8 hoặc dùng gói net10 self-contained dự phòng, xác minh manifest/kích thước/SHA-256 rồi mới chạy.

Lệnh trên hiện tải Release đã xác minh `v0.3.5`. Các thay đổi mới hơn trên nhánh `main` chỉ có trong source cho đến khi một Release mới được tạo và `bootstrap.ps1` được cập nhật sau khi kiểm tra đủ asset.

## Giao diện và edition

- **Public:** chỉ hiển thị **Install Software** và **Driver**.
- **Developer:** hiển thị đủ **Install Software**, **Optimize Windows**, **Driver** và **Setting**.
- Cả hai edition đều dùng manifest `requireAdministrator`.
- Public đọc cấu hình đóng gói trong `ReleaseConfig`; Developer có thể chỉnh cấu hình thật trong workspace.

## Chức năng

### Install Software

Một lần bấm sẽ chọn Office 2024 hoặc WPS rồi xử lý toàn bộ catalog cùng các Windows Setting đã cấu hình. EXE có thể chạy song song; MSI được xếp hàng tuần tự theo giới hạn của Windows Installer.

Trên net48, Smart Skip đọc inventory phần mềm theo ba trạng thái `Installed`, `NotInstalled`, `Unknown`, kiểm tra lại trước khi mở installer và ghi bằng chứng vào `%LocalAppData%\MiniApps\InstallLogs`. Trạng thái không xác minh được sẽ không tự cài đè.

### Driver

Hiển thị host, hãng, model và serial; cho phép sao chép serial và mở trang hỗ trợ chính thức của nhà sản xuất. MiniApps chưa tự tải hoặc cài driver.

### Optimize Windows

Chỉ hiển thị trong Developer. Luồng net48 tích hợp source Win11Debloat đã vendor, chia thành **Debloatware** và **Optimize Windows** nhưng chạy chung một lượt. Hai lane có thể chạy đồng thời; các thao tác Appx/WinGet dùng worker và mutex riêng để tránh nhiều writer trên App Repository.

Mỗi lượt lưu log, diagnostics và Registry backup trong `%LocalAppData%\MiniApps\OptimizeLogs`. Tác vụ quá hạn được báo đúng trạng thái; MiniApps không kill worker hệ thống còn hoạt động.

### Setting

Chỉ có trong Developer, dùng để quản lý catalog ứng dụng và Windows Setting. Lưu cấu hình không tự chạy cài đặt hoặc thay đổi Windows.

## Build và kiểm thử

Mở thư mục `WPF/` làm workspace và đọc [AGENTS.md](AGENTS.md) cùng [tài liệu dự án](docs/README.md) trước khi sửa.

```powershell
dotnet build ./MiniApps.Tests/MiniApps.Tests.csproj -c Release -f net48
./MiniApps.Tests/bin/Release/net48/MiniApps.Tests.exe

dotnet build ./MiniApps.Tests/MiniApps.Tests.csproj -c Release -f net10.0-windows
./MiniApps.Tests/bin/Release/net10.0-windows/MiniApps.Tests.exe

./Run-Developer.ps1 -Target net48
```

`Run-Developer.ps1` mở bản Developer net48 với cấu hình thật trong `ReleaseConfig`. `Preview.ps1` dành cho mô phỏng. Không bấm Install hoặc Optimize trên máy phát triển nếu không chủ động muốn áp dụng thay đổi thật.

## Phát hành

`Publish.ps1` tạo ZIP, checksum và manifest trong `artifacts/`. Push source không tự tạo GitHub Release, không upload asset và không đổi bản mà lệnh `irm` đang tải.

```powershell
./Publish.ps1 -Target net48 -Runtime win-x64 -Version <version>
```

Chỉ phát lệnh `irm` cho người dùng sau khi các asset của Release tồn tại và đã được xác minh. Xem [DEVELOPMENT.md](docs/DEVELOPMENT.md), [ARCHITECTURE.md](docs/ARCHITECTURE.md), [OPTIMIZE.md](docs/OPTIMIZE.md) và [CURRENT-STATE.md](docs/CURRENT-STATE.md) để biết chi tiết.
