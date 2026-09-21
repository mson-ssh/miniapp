# MiniApps

MiniApps là ứng dụng WPF gọn nhẹ cho Windows 10/11 x64: cài bộ phần mềm cho máy mới, áp dụng thiết lập Windows, xem thông tin máy và chạy các tiện ích mở rộng. Ưu tiên .NET Framework 4.8 có sẵn trên Windows, có gói .NET 10 self-contained dự phòng.

## Cài nhanh bằng irm

1. Mở **PowerShell** (bấm `Win + X` → **Terminal** hoặc **Windows PowerShell**). Không cần mở bằng quyền Administrator.
2. Dán lệnh sau rồi nhấn Enter:

```powershell
irm https://raw.githubusercontent.com/mson-ssh/miniapp/main/WPF/bootstrap.ps1 | iex
```

3. Bấm **Yes** khi Windows hỏi quyền Administrator (UAC). MiniApps sẽ tự mở.

Lệnh hiện tải bản **v0.3.9** từ [GitHub Releases](https://github.com/mson-ssh/miniapp/releases/tag/v0.3.9).

### Lệnh làm gì

`irm` là bí danh của `Invoke-RestMethod`: tải script `bootstrap.ps1`, còn `iex` chạy script đó. Bootstrap:

- Kiểm tra máy: Windows 10 1809 (build 17763) trở lên, x64. Máy ARM64 hoặc x86 sẽ bị từ chối.
- Chọn gói: **net48** (khoảng 0,5 MB) nếu máy có .NET Framework 4.8; nếu không có, hoặc net48 chạy thử không được, dùng **net10** self-contained (khoảng 63 MB, không cần cài .NET).
- Tải gói từ GitHub Release, kiểm tra kích thước và SHA-256 theo manifest. Sai lệch thì dừng, không chạy gì.
- Giải nén vào thư mục phiên riêng trong `%TEMP%\MiniApps`, chạy thử ẩn để chắc gói khởi động được, rồi mở MiniApps.
- Khi đóng MiniApps, thư mục phiên được dọn đi. Mỗi lần chạy lệnh `irm` đều lấy bản phát hành hiện hành, không để lại bản cài cố định trên máy.

### Nếu lệnh báo lỗi

| Hiện tượng | Cách xử lý |
|---|---|
| `Could not create SSL/TLS secure channel` (thường gặp trên Windows 10 cũ) | Chạy lệnh có bật TLS 1.2: `[Net.ServicePointManager]::SecurityProtocol = 'Tls12'; irm https://raw.githubusercontent.com/mson-ssh/miniapp/main/WPF/bootstrap.ps1 \| iex` |
| Bấm **No** ở hộp thoại UAC | Chạy lại lệnh và chọn **Yes**. MiniApps cần quyền Administrator để cài phần mềm. |
| `requires Windows 10 1809 (build 17763) or later` | Máy quá cũ, cần cập nhật Windows. |
| `x64 only` / `ARM64` | MiniApps chỉ hỗ trợ Windows x64. |
| `SHA-256 mismatch` hoặc `size does not match` | File tải về bị hỏng hoặc bị chặn giữa đường. Kiểm tra mạng/proxy/antivirus rồi chạy lại. |
| `MiniApps could not start on this machine` | Cả hai gói đều không khởi động được. Xem log trong `%LocalAppData%\MiniApps\StartupLogs`. |

Chính sách Execution Policy không chặn lệnh này, vì script chạy từ bộ nhớ chứ không từ file `.ps1`.

## Chức năng

- **INSTALL SOFTWARE:** một nút cài toàn bộ catalog ứng dụng cùng các thiết lập Windows. Drop list chọn bộ văn phòng: Microsoft Office, WPS, OnlyOffice, Libre Office hoặc Null (không cài). Mỗi app có nút tải xuống để cài riêng. Chọn WPS, OnlyOffice hoặc Libre Office thì Microsoft Office đang có trên máy được gỡ trước. Debloatware Windows (Win11Debloat ở chế độ mặc định, có điểm khôi phục) chạy cùng lượt như một ứng dụng. Smart Skip bỏ qua phần mềm đã có. Thiết lập Windows gồm SMB, chia ổ đĩa, Execution Policy và các tối ưu khác.
- **INFORMATION:** Windows và bản quyền, tên máy, model, serial, CPU, RAM, GPU, ổ đĩa, màn hình. Nút Driver sao chép serial và mở trang hỗ trợ chính thức của hãng.
- **EXTEND:** Môi trường C++ (VS Code, MSYS2, MinGW-w64), hỏi xác nhận trước khi chạy; Share LAN mở trong cửa sổ PowerShell riêng.

Log nằm trong `%LocalAppData%\MiniApps` (`InstallLogs`, `ExtendLogs`, `StartupLogs`).

## Build và kiểm thử

```powershell
dotnet build ./WPF/MiniApps.Tests/MiniApps.Tests.csproj -c Release -f net48
./WPF/MiniApps.Tests/bin/Release/net48/MiniApps.Tests.exe

dotnet build ./WPF/MiniApps.Tests/MiniApps.Tests.csproj -c Release -f net10.0-windows
./WPF/MiniApps.Tests/bin/Release/net10.0-windows/MiniApps.Tests.exe
```

Xem [hướng dẫn WPF](WPF/README.md), [kiến trúc](WPF/docs/ARCHITECTURE.md), [quy trình phát triển](WPF/docs/DEVELOPMENT.md) và [trạng thái hiện tại](WPF/docs/CURRENT-STATE.md).
