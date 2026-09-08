# MiniApps

MiniApps là ứng dụng WPF gọn nhẹ dành cho Windows 10/11 x64, ưu tiên .NET Framework 4.8 và có gói .NET 10 self-contained dự phòng.

## Cài nhanh

Mở PowerShell và chạy:

```powershell
irm https://raw.githubusercontent.com/mson-ssh/miniapp/main/WPF/bootstrap.ps1 | iex
```

`irm` là bí danh của `Invoke-RestMethod`. Bootstrap tự yêu cầu quyền Administrator, chọn runtime phù hợp, xác minh manifest/kích thước/SHA-256 của gói rồi mới chạy.

Lệnh trên hiện tải Release đã xác minh `v0.3.6`. Bootstrap chỉ được chuyển sang phiên bản mới sau khi đủ hai ZIP, hai checksum và manifest trên GitHub Release đã được tải lại để kiểm tra.

## Chức năng

- **Install Software:** cài catalog ứng dụng, chọn Office 2024 hoặc WPS, chạy EXE song song và xếp hàng MSI. Smart Skip net48 kiểm tra phần mềm đã cài và ghi bằng chứng trước khi quyết định tải/chạy installer.
- **Driver:** đọc host, hãng, model, serial và mở trang hỗ trợ chính thức của nhà sản xuất; chưa tự tải hoặc cài driver.
- **Optimize Windows:** tích hợp Win11Debloat cho net48, chạy Debloatware cùng các tùy chỉnh Windows trong một lượt có log, diagnostics, backup và kiểm soát worker quá hạn.
- **Setting:** quản lý catalog ứng dụng và Windows Setting trong bản Developer.

## Edition

- **Public:** chỉ hiển thị **Install Software** và **Driver**.
- **Developer:** hiển thị đủ **Install Software**, **Optimize Windows**, **Driver** và **Setting**.
- Cả hai edition đều yêu cầu Administrator ngay khi khởi động.

## Build và kiểm thử

```powershell
dotnet build ./WPF/MiniApps.Tests/MiniApps.Tests.csproj -c Release -f net48
./WPF/MiniApps.Tests/bin/Release/net48/MiniApps.Tests.exe

dotnet build ./WPF/MiniApps.Tests/MiniApps.Tests.csproj -c Release -f net10.0-windows
./WPF/MiniApps.Tests/bin/Release/net10.0-windows/MiniApps.Tests.exe

./WPF/Run-Developer.ps1 -Target net48
```

Xem [hướng dẫn WPF](WPF/README.md), [kiến trúc](WPF/docs/ARCHITECTURE.md), [quy trình phát triển](WPF/docs/DEVELOPMENT.md) và [trạng thái hiện tại](WPF/docs/CURRENT-STATE.md).
