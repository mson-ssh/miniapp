# Kiến trúc và dữ liệu

MiniApps là WPF C#/XAML theo MVVM, dùng control chuẩn đã style. Project đa target `net48;net10.0-windows`, x64. Người dùng net48 cần Framework 4.8; gói fallback net10 self-contained mang runtime theo. SDK build trên máy phát triển là yêu cầu khác với runtime trên máy khách. Hai target dùng cùng một source, không còn nhánh `#if NET48` riêng (trừ shim `IsExternalInit` cho .NET Framework), nên hành vi giống nhau.

Manifest ứng dụng đặt `requestedExecutionLevel=requireAdministrator`, nên Windows yêu cầu UAC trước khi mở ứng dụng. Bootstrap tự nâng PowerShell trước khi tải/chạy; khi chạy executable trực tiếp, Windows nâng chính MiniApps. Kiểm tra Administrator ở từng service vẫn được giữ.

| Đường dẫn | Trách nhiệm |
| --- | --- |
| `MiniApps/App.xaml(.cs)`, `Theme.xaml` | Startup, tham số cấu hình/preview; style dùng chung nằm trong `Theme.xaml` |
| `MiniApps/MainWindow.xaml(.cs)` | Sidebar, màn hình, khóa đóng khi đang chạy |
| `MiniApps/ViewModels/MainViewModel.cs` | State UI, lệnh, điều phối Install, trang Driver |
| `MiniApps/Models/` | Catalog ứng dụng, Windows settings, các bản ghi tiến trình |
| `MiniApps/Services/DeploymentService.cs` | Smart Skip, download, kiểm tra bộ cài, điều phối EXE/MSI/Windows scripts, process |
| `MiniApps/Services/InstalledSoftwareDetector.cs` | Inventory Registry/file và bằng chứng Smart Skip ba trạng thái |
| `MiniApps/Services/SettingsStore.cs` | Đọc/validate/migrate cấu hình (chỉ đọc) |
| `MiniApps/Services/DeviceInfoService.cs` | Thông tin máy và URL hỗ trợ hãng |
| `MiniApps/Services/WindowsCompatibility.cs` | Build Windows và tương thích tác vụ |
| `MiniApps/Services/ProcessCompatibility.cs` | Quote tham số và chờ process đa runtime |
| `MiniApps/Scripts/` | Các tác vụ PowerShell dùng bởi Install Software |
| `ReleaseConfig/` | Cấu hình đóng gói cạnh executable, sửa trực tiếp file JSON |
| `MiniApps.Tests/Program.cs` | Kiểm thử logic và WPF preview/render |
| `bootstrap.ps1` | Chọn runtime, xác minh/tải/giải nén, UAC, chạy và dọn phiên |

Lỗi khởi động được ghi đầy đủ vào `%LocalAppData%\MiniApps\StartupLogs`; hộp thoại lỗi hiển thị đường dẫn log. Chế độ headless vẫn thoát bằng mã lỗi mà không mở hộp thoại.

## Cấu hình

Chỉ có một bản build, hiển thị Install Software và Driver. Ứng dụng đọc `ReleaseConfig` cạnh executable và yêu cầu cấu hình hợp lệ; không dùng cấu hình LocalAppData để ghi đè catalog phát hành. Ứng dụng không ghi cấu hình.

`apps.json` dùng schema 1; `windows.json` dùng schema 3 và hỗ trợ migrate schema 1/2. Khi đọc cấu hình cũ, mục Debloat được loại trước khi validate. Envelope lưu Items và RemovedDefaultIds. `RemovedDefaultIds` giữ các mục mặc định đã bị bỏ để migrate không tự thêm lại. Script trong Windows settings là mã PowerShell thực thi tin cậy, không chỉ mô tả.

`--preview` là chế độ mô phỏng; `--validate-config --config-root <thư mục>` kiểm tra cấu hình và thoát.

## Luồng chạy

Install: chọn suite → kiểm tra quyền/khóa phiên → đọc một snapshot đăng ký phần mềm → Smart Skip theo Installed/NotInstalled/Unknown → tải app còn thiếu song song cùng Windows settings → đọc snapshot mới trước khi launch để giảm cài trùng → chạy installer → gom tiến trình → chờ tác vụ đang chạy → tổng kết/dọn. Installed được bỏ qua có bằng chứng; Unknown không tự cài đè và làm lượt có thể thử lại. Quyết định được ghi vào `%LocalAppData%\MiniApps\InstallLogs`. MSI có hàng đợi riêng; dừng hàng đợi không kill bộ cài đang chạy.

Bootstrap: kiểm tra OS/kiến trúc và Framework → chọn net48 hoặc net10 → manifest schema 2 → xác minh URL/kích thước/hash ZIP → giải nén → chạy thử ẩn `--validate-config` (net48 lỗi hoặc treo quá 60 giây thì tải net10 và thử tiếp; hash/kích thước sai thì dừng, không chuyển gói) → chạy → chờ cây process và dọn phiên có marker. Push source không thay đổi ZIP trong Release.
