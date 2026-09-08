# Kiến trúc và dữ liệu

MiniApps là WPF C#/XAML theo MVVM, dùng control chuẩn đã style. Project đa target `net48;net10.0-windows`, x64. Người dùng net48 cần Framework 4.8; gói fallback net10 self-contained mang runtime theo. SDK build trên máy phát triển là yêu cầu khác với runtime trên máy khách.

Manifest ứng dụng đặt `requestedExecutionLevel=requireAdministrator`, nên Windows yêu cầu UAC trước khi mở Developer hoặc Public. Bootstrap tự nâng PowerShell trước khi tải/chạy; khi chạy executable trực tiếp, Windows nâng chính MiniApps. Kiểm tra Administrator ở từng service vẫn được giữ.

| Đường dẫn | Trách nhiệm |
| --- | --- |
| `MiniApps/App.xaml(.cs)` | Style, startup, edition, tham số cấu hình/preview |
| `MiniApps/MainWindow.xaml(.cs)` | Sidebar, màn hình, khóa đóng khi đang chạy |
| `MiniApps/ViewModels/MainViewModel.cs` | State UI, lệnh, điều phối Install/Optimize, bản nháp Setting |
| `MiniApps/Models/` | Catalog ứng dụng, Windows settings, các bản ghi tiến trình |
| `MiniApps/Services/DeploymentService.cs` | Smart Skip, download, kiểm tra bộ cài, điều phối EXE/MSI/Windows scripts, process |
| `MiniApps/Services/InstalledSoftwareDetector.net48.cs` | Inventory Registry/file và bằng chứng Smart Skip ba trạng thái cho net48 |
| `MiniApps/Services/SettingsStore.cs` | Đọc/validate/migrate/lưu atomic cấu hình |
| `MiniApps/Services/DeviceInfoService.cs` | Thông tin máy và URL hỗ trợ hãng |
| `MiniApps/Services/WindowsCompatibility.cs` | Build Windows và tương thích tác vụ |
| `MiniApps/Services/ProcessCompatibility.cs` | Quote tham số và chờ process đa runtime |
| `MiniApps/Scripts/` | Các tác vụ PowerShell dùng bởi Install Software |
| `ReleaseConfig/` | Cấu hình Developer chỉnh và Public đóng gói |
| `MiniApps.Tests/Program.cs` | Kiểm thử logic và WPF preview/render |
| `bootstrap.ps1` | Chọn runtime, xác minh/tải/giải nén, UAC, chạy và dọn phiên |

Lỗi khởi động net48 được ghi đầy đủ vào `%LocalAppData%\MiniApps\StartupLogs`; hộp thoại lỗi hiển thị đường dẫn log. Chế độ headless vẫn thoát bằng mã lỗi mà không mở hộp thoại.

## Edition và cấu hình

MSBuild `MiniAppsEdition=Developer` định nghĩa `MINIAPPS_DEVELOPER`. Public chỉ hiển thị Install Software và Driver, đồng thời chặn Setting. Developer hiển thị Install Software, Driver và Setting. Debloat/Optimize đã được gỡ khỏi source đóng gói và không có trong cả hai edition. Public đọc `ReleaseConfig` cạnh executable và yêu cầu cấu hình hợp lệ; không dùng cấu hình LocalAppData để ghi đè catalog phát hành.

`apps.json` dùng schema 1; `windows.json` dùng schema 3 và hỗ trợ migrate schema 1/2. Khi đọc cấu hình cũ, mục Debloat được loại trước khi validate. Envelope lưu Items và RemovedDefaultIds. Bản nháp UI tách khỏi catalog thực thi; lưu mới có hiệu lực. Script trong Windows settings là mã PowerShell thực thi tin cậy, không chỉ mô tả.

Developer dùng `--config-root` để chọn thư mục. `--developer-preview` mô phỏng thao tác hệ thống nhưng cho phép lưu cấu hình thật. `--preview` là chế độ mô phỏng; `--validate-config` kiểm tra cấu hình; `--initialize-release-config` chỉ có ở Developer và khởi tạo file thiếu.

## Luồng chạy

Install net48: chọn suite → kiểm tra quyền/khóa phiên → đọc một snapshot đăng ký phần mềm → Smart Skip theo Installed/NotInstalled/Unknown → tải app còn thiếu song song cùng Windows settings → đọc snapshot mới trước khi launch để giảm cài trùng → chạy installer → gom tiến trình → chờ tác vụ đang chạy → tổng kết/dọn. Installed được bỏ qua có bằng chứng; Unknown không tự cài đè và làm lượt có thể thử lại. Quyết định được ghi vào `%LocalAppData%\MiniApps\InstallLogs`. MSI có hàng đợi riêng; dừng hàng đợi không kill bộ cài đang chạy.

Bootstrap: kiểm tra OS/kiến trúc và Framework → chọn net48 hoặc net10 → manifest schema 2 → xác minh URL/kích thước/hash ZIP → giải nén/chạy → chờ cây process và dọn phiên có marker. Push source không thay đổi ZIP trong Release.
