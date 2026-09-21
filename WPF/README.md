# MiniApps Desktop

MiniApps là ứng dụng WPF dành cho Windows 10/11 x64. Ứng dụng yêu cầu quyền Administrator ngay khi khởi động và ưu tiên .NET Framework 4.8 có sẵn trên Windows.

## Cài nhanh bằng PowerShell

```powershell
irm https://raw.githubusercontent.com/mson-ssh/miniapp/main/WPF/bootstrap.ps1 | iex
```

`irm` là bí danh của `Invoke-RestMethod`. Bootstrap tự yêu cầu UAC, chọn gói net48 khi máy có .NET Framework 4.8 hoặc dùng gói net10 self-contained dự phòng (kể cả khi net48 chạy thử không được), xác minh manifest/kích thước/SHA-256 rồi mới chạy.

Lệnh trên hiện tải Release đã xác minh `v0.3.7`. Bootstrap chỉ được chuyển sang phiên bản mới sau khi đủ hai ZIP, hai checksum và manifest trên GitHub Release đã được tải lại để kiểm tra.

## Giao diện

- Chỉ có một bản, giống nhau trên net48 và net10: ba trang **Install Software**, **Information** và **Extend**.
- Manifest `requireAdministrator`.
- Đọc cấu hình đóng gói trong `ReleaseConfig` cạnh executable. Muốn đổi catalog thì sửa `ReleaseConfig/apps.json` và `windows.json` rồi phát hành lại.

## Chức năng

### Install Software

Chọn bộ văn phòng ở drop list cạnh nút Cài đặt (Microsoft Office, WPS, OnlyOffice, Libre Office hoặc Null — mặc định, không cài bộ nào), rồi một lần bấm xử lý toàn bộ catalog cùng các Windows Setting đã cấu hình. Trước khi chạy, danh sách app hiện kèm nút tải xuống để cài riêng từng app. EXE có thể chạy song song; MSI được xếp hàng tuần tự theo giới hạn của Windows Installer.

Smart Skip (cả net48 và net10) đọc inventory phần mềm theo ba trạng thái `Installed`, `NotInstalled`, `Unknown`, kiểm tra lại trước khi mở installer và ghi bằng chứng vào `%LocalAppData%\MiniApps\InstallLogs`. Trạng thái không xác minh được sẽ không tự cài đè.

### Information

Đọc thông tin máy bằng `tool/Info/info.ps1` nhúng trong ứng dụng: Windows/bản quyền, tên máy, model, serial, CPU, RAM, GPU, ổ đĩa, màn hình. Nút Driver cạnh Serial sao chép serial rồi mở trang hỗ trợ chính thức của hãng. MiniApps chưa tự tải hoặc cài driver.

## Build và kiểm thử

Mở thư mục `WPF/` làm workspace và đọc [AGENTS.md](AGENTS.md) cùng [tài liệu dự án](docs/README.md) trước khi sửa.

```powershell
dotnet build ./MiniApps.Tests/MiniApps.Tests.csproj -c Release -f net48
./MiniApps.Tests/bin/Release/net48/MiniApps.Tests.exe

dotnet build ./MiniApps.Tests/MiniApps.Tests.csproj -c Release -f net10.0-windows
./MiniApps.Tests/bin/Release/net10.0-windows/MiniApps.Tests.exe
```

`Preview.ps1` dành cho mô phỏng. Không bấm Install trên máy phát triển nếu không chủ động muốn áp dụng thay đổi thật.

## Phát hành

`Publish.ps1` tạo ZIP, checksum và manifest trong `artifacts/`. Push source không tự tạo GitHub Release, không upload asset và không đổi bản mà lệnh `irm` đang tải.

```powershell
./Publish.ps1 -Target net48 -Runtime win-x64 -Version <version>
```

Chỉ phát lệnh `irm` cho người dùng sau khi các asset của Release tồn tại và đã được xác minh. Xem [DEVELOPMENT.md](docs/DEVELOPMENT.md), [ARCHITECTURE.md](docs/ARCHITECTURE.md) và [CURRENT-STATE.md](docs/CURRENT-STATE.md) để biết chi tiết.
