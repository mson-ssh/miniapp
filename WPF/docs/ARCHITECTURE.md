# Kiến trúc và dữ liệu

MiniApps là WPF C#/XAML theo MVVM, dùng control chuẩn đã style. Project đa target `net48;net10.0-windows`, x64. Người dùng net48 cần Framework 4.8; gói fallback net10 self-contained mang runtime theo. SDK build trên máy phát triển là yêu cầu khác với runtime trên máy khách. Hai target dùng chung source; tính năng Information mới được giới hạn net48 bằng `#if NET48` và điều kiện MSBuild. Các luồng cài đặt hiện có vẫn dùng chung.

Manifest ứng dụng đặt `requestedExecutionLevel=requireAdministrator`, nên Windows yêu cầu UAC trước khi mở ứng dụng. Bootstrap tự nâng PowerShell trước khi tải/chạy; khi chạy executable trực tiếp, Windows nâng chính MiniApps. Kiểm tra Administrator ở từng service vẫn được giữ.

| Đường dẫn | Trách nhiệm |
| --- | --- |
| `MiniApps/App.xaml(.cs)`, `Theme.xaml` | Startup, tham số cấu hình/preview; style dùng chung nằm trong `Theme.xaml` |
| `MiniApps/MainWindow.xaml(.cs)` | Sidebar, màn hình, khóa đóng khi đang chạy |
| `MiniApps/ViewModels/MainViewModel.cs` | State UI, lệnh, điều phối Install và EXTEND |
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

## Information

RAM/GPU/ổ đĩa dùng dữ liệu JSON có cấu trúc (`RamItems`, `GpuItems`, `StorageItems`) từ script, ánh xạ qua `InformationHardware.cs`. Mỗi linh kiện là một dòng có cột; phân vùng giữ số byte để tính tỷ lệ đã sử dụng, không parse chuỗi trình bày. Thông số thiếu dùng dấu —, số dung lượng không hợp lệ không hiển thị progress bar. Nút Sao chép toàn bộ ẩn; có Làm mới ở góc phải và Driver cạnh Serial. Driver sao chép Serial trước rồi mở URL hãng qua DeviceInfoService, dùng Manufacturer từ cùng snapshot Information. Dữ liệu tự đọc khi mở trang, vẫn chọn được văn bản.

`InformationView.xaml(.cs)` hiển thị trang thứ hai trên cả net48 và net10. `InformationService` chạy PowerShell ẩn và bất đồng bộ, dùng script `tool/Info/info.ps1` được nhúng trong assembly của cả hai target. Chế độ `-AsJson` dùng lại `Get-SystemData`, bỏ cửa sổ riêng và trả JSON; chạy script không có tham số vẫn giữ giao diện gốc. Dữ liệu gồm Windows/bản quyền, tên máy, model, serial, CPU, RAM, GPU, ổ đĩa, độ phân giải, tần số quét tối đa và thời điểm đọc. Trang tải khi mở lần đầu, có đọc lại và sao chép; không tự làm mới theo timer. Giới hạn đọc 90 giây, hủy process đọc do trang sở hữu khi đóng MiniApps. Preview dùng dữ liệu giả, không đọc phần cứng.

## Cấu hình

Chỉ có một edition Public; net48 và net10 cùng hiển thị Install Software, Information và EXTEND. Ứng dụng đọc `ReleaseConfig` cạnh executable và yêu cầu cấu hình hợp lệ; không dùng cấu hình LocalAppData để ghi đè catalog phát hành. Ứng dụng không ghi cấu hình.

`apps.json` dùng schema 1; `windows.json` dùng schema 3 và hỗ trợ migrate schema 1/2. Khi đọc cấu hình cũ, mục Debloat được loại trước khi validate. Envelope lưu Items và RemovedDefaultIds. `RemovedDefaultIds` giữ các mục mặc định đã bị bỏ để migrate không tự thêm lại. Script trong Windows settings là mã PowerShell thực thi tin cậy, không chỉ mô tả.

`--preview` là chế độ mô phỏng; `--validate-config --config-root <thư mục>` kiểm tra cấu hình và thoát.

## Luồng chạy

Install: chọn suite → kiểm tra quyền/khóa phiên → đọc một snapshot đăng ký phần mềm → Smart Skip theo Installed/NotInstalled/Unknown → tải app còn thiếu song song cùng Windows settings → đọc snapshot mới trước khi launch để giảm cài trùng → chạy installer → gom tiến trình → chờ tác vụ đang chạy → tổng kết/dọn. Installed được bỏ qua có bằng chứng; Unknown không tự cài đè và làm lượt có thể thử lại. Quyết định được ghi vào `%LocalAppData%\MiniApps\InstallLogs`. MSI có hàng đợi riêng; dừng hàng đợi không kill bộ cài đang chạy. Dọn dẹp: mỗi bộ cài bị xóa ngay khi app đó xong (thành công, lỗi hoặc bỏ qua sau khi tải); cuối lượt xóa cả thư mục `%TEMP%\MiniApps\work-<id>`. Khi khởi động (không phải preview), `WorkFolderCleaner` xóa các `work-<32 hex>` còn sót nếu giữ được mutex cài đặt, bỏ qua thư mục đang bị khóa và thư mục chứa `debloat-*`/`Backups` của bản Debloat cũ. Log trong `%LocalAppData%\MiniApps` không tự xóa; test ghi log vào thư mục tạm qua `DeploymentService.InstallLogDirectory`.

Bootstrap: kiểm tra OS/kiến trúc và Framework → chọn net48 hoặc net10 → manifest schema 2 → xác minh URL/kích thước/hash ZIP → giải nén → chạy thử ẩn `--validate-config` (net48 lỗi hoặc treo quá 60 giây thì tải net10 và thử tiếp; hash/kích thước sai thì dừng, không chuyển gói) → chạy → chờ cây process và dọn phiên có marker. Push source không thay đổi ZIP trong Release.
