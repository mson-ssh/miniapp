# Kiến trúc và dữ liệu

MiniApps là WPF C#/XAML theo MVVM, dùng control chuẩn đã style. Project đa target `net48;net10.0-windows`, x64. Người dùng net48 cần Framework 4.8; gói fallback net10 self-contained mang runtime theo. SDK build trên máy phát triển là yêu cầu khác với runtime trên máy khách.

| Đường dẫn | Trách nhiệm |
| --- | --- |
| `MiniApps/App.xaml(.cs)` | Style, startup, edition, tham số cấu hình/preview |
| `MiniApps/MainWindow.xaml(.cs)` | Sidebar, màn hình, khóa đóng khi đang chạy |
| `MiniApps/ViewModels/MainViewModel.cs` | State UI, lệnh, điều phối Install/Optimize, bản nháp Setting |
| `MiniApps/Models/` | Catalog ứng dụng, Windows settings, các bản ghi tiến trình |
| `MiniApps/Services/DeploymentService.cs` | Download, kiểm tra bộ cài, điều phối EXE/MSI/Windows scripts, process |
| `MiniApps/Services/OptimizeService.net48.cs` | Vòng đời Optimize net48, quyền Admin/mutex, trạng thái, dọn tệp và kết quả |
| `MiniApps/Services/SettingsStore.cs` | Đọc/validate/migrate/lưu atomic cấu hình |
| `MiniApps/Services/DeviceInfoService.cs` | Thông tin máy và URL hỗ trợ hãng |
| `MiniApps/Services/WindowsCompatibility.cs` | Build Windows và tương thích tác vụ |
| `MiniApps/Services/ProcessCompatibility.cs` | Quote tham số và chờ process đa runtime |
| `MiniApps/Scripts/` | Các tác vụ PowerShell; Optimize mới là `Optimize-Defaults.ps1` |
| `ReleaseConfig/` | Cấu hình Developer chỉnh và Public đóng gói |
| `MiniApps.Tests/Program.cs` | Kiểm thử logic và WPF preview/render |
| `bootstrap.ps1` | Chọn runtime, xác minh/tải/giải nén, UAC, chạy và dọn phiên |

## Edition và cấu hình

MSBuild `MiniAppsEdition=Developer` định nghĩa `MINIAPPS_DEVELOPER`; Public chặn cả UI và các lệnh Setting. Public đọc `ReleaseConfig` cạnh executable và yêu cầu cấu hình hợp lệ; không dùng cấu hình LocalAppData để ghi đè catalog phát hành.

`apps.json` dùng schema 1; `windows.json` dùng schema 2 và hỗ trợ migrate schema 1. Envelope lưu Items và RemovedDefaultIds. Bản nháp UI tách khỏi catalog thực thi; lưu mới có hiệu lực. Script trong Windows settings là mã PowerShell thực thi tin cậy, không chỉ mô tả.

Developer dùng `--config-root` để chọn thư mục. `--developer-preview` mô phỏng thao tác hệ thống nhưng cho phép lưu cấu hình thật. `--preview` là chế độ mô phỏng; `--validate-config` kiểm tra cấu hình; `--initialize-release-config` chỉ có ở Developer và khởi tạo file thiếu.

## Luồng chạy

Install: chọn suite → kiểm tra quyền/khóa phiên → tải và chạy app song song cùng Windows settings → gom tiến trình → chờ tác vụ đang chạy → tổng kết/dọn. Debloat bị lọc khỏi Install theo ID hoặc Action. MSI có hàng đợi riêng; dừng hàng đợi không kill bộ cài đang chạy.

Optimize net48 thật: `MainViewModel` khóa thao tác xung đột và giao việc cho `IOptimizeService` → service kiểm tra Administrator/mutex → tạo thư mục phiên ngắn, riêng → script phát trạng thái Downloading/Preparing/Applying và `MINIAPPS_TASK_JSON` cho 17 tác vụ → tải upstream HTTPS ở commit đã ghim, không yêu cầu checksum theo quyết định sản phẩm → kiểm tra từng ZIP entry và bỏ root upstream khi giải nén an toàn vào `src` → bridge bọc đúng điểm gọi top-level của upstream → áp dụng Default đã bỏ restore point → giữ log/backup → xác minh mọi tác vụ có DONE/SKIP/ERROR → dọn và trả kết quả. UI đổi thứ tự hàng theo sự kiện thật; thiếu terminal event hoặc ERROR không được coi là thành công. Preview dùng `OptimizePreviewService`, mô phỏng tuần tự cùng protocol và không chạy PowerShell.

Bootstrap: kiểm tra OS/kiến trúc và Framework → chọn net48 hoặc net10 → manifest schema 2 → xác minh URL/kích thước/hash ZIP → giải nén/chạy → chờ cây process và dọn phiên có marker. Push source không thay đổi ZIP trong Release.
