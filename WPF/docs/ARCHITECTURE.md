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

Điều chỉnh tiếp ngày 2026-09-28: net48 không còn tự co cửa sổ theo Information; cả ba trang giữ chung kích thước hiện tại, kể cả sau Làm mới. Quy tắc này thay thế kích thước 780/480 mô tả trong lịch sử phía dưới.

Từ 2026-10-03, `InformationService` đọc trực tiếp trong tiến trình MiniApps, không chạy PowerShell, không dùng WMI/CIM hay wmic (wmic đã bị gỡ khỏi Windows mới): HOST từ `Environment.MachineName`; Windows từ registry `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` (`ProductName`, build ≥ 22000 đổi "Windows 10" thành "Windows 11" vì Windows 11 vẫn ghi ProductName là Windows 10); Manufacturer và Serial từ bảng SMBIOS type 1 qua `GetSystemFirmwareTable('RSMB')` (cùng nguồn WMI dùng cho Win32_ComputerSystem.Manufacturer/Win32_BIOS.SerialNumber); bản quyền qua `SLIsGenuineLocal` (slwga.dll) với ApplicationId Windows. Mỗi phần lỗi trả — hoặc "Chưa xác nhận kích hoạt", không báo lỗi bản quyền. Đọc trên thread nền, giới hạn 15 giây; đo trên máy phát triển khoảng 10 ms (bộ đọc PowerShell cũ khoảng 1 s, chậm hơn trên máy mới cài vì SoftwareLicensingProduct). `InformationReport` chỉ gồm Host, Windows, Activated, Maker, Serial, ReadAt; `DriverUrl` suy ra từ Maker qua DeviceInfoService. Giao diện chỉ hiển thị HOST/Serial/Driver và mục Hệ điều hành (Phiên bản, Bản quyền; tô màu khi chưa xác nhận kích hoạt); khi đang đọc chỉ hiện dòng trạng thái, không còn màn hình đếm %. Đã gỡ bộ parser/hiển thị phần cứng cũ (InformationHardware.cs, các mục RAM/GPU/ổ đĩa/màn hình, Sao chép toàn bộ) và Scripts/Read-Information.ps1. Cửa sổ giữ cùng kích thước với các trang khác. Driver sao chép Serial trước rồi mở URL hãng. Preview dùng `InformationService.Preview`, không đọc phần cứng.

Trang tải khi mở lần đầu, có Làm mới; không tự làm mới theo timer.

`tool/Info/info.ps1` là công cụ standalone (info.exe), không nhúng vào MiniApps. Chế độ `-AsJson` dùng lại `Get-SystemData`, bỏ cửa sổ riêng và trả JSON; chạy script không có tham số vẫn giữ giao diện gốc. Dữ liệu gồm Windows/bản quyền, tên máy, model, serial, CPU, RAM, GPU, ổ đĩa, độ phân giải, tần số quét tối đa và thời điểm đọc.

## Cấu hình

Chỉ có một edition Public; net48 và net10 cùng hiển thị Install Software, Information và EXTEND. Ứng dụng đọc `ReleaseConfig` cạnh executable và yêu cầu cấu hình hợp lệ; không dùng cấu hình LocalAppData để ghi đè catalog phát hành. Ứng dụng không ghi cấu hình.

`apps.json` dùng schema 1; `windows.json` dùng schema 3 và hỗ trợ migrate schema 1/2. Khi đọc cấu hình cũ, mục Debloat (engine cũ, Id/Action `Debloat`) được loại trước khi validate; Debloat hiện tại là action `Win11Debloat` khác tên nên không bị loại. Envelope lưu Items và RemovedDefaultIds. `RemovedDefaultIds` giữ các mục mặc định đã bị bỏ để migrate không tự thêm lại. Script trong Windows settings là mã PowerShell thực thi tin cậy, không chỉ mô tả.

`--preview` là chế độ mô phỏng; `--validate-config --config-root <thư mục>` kiểm tra cấu hình và thoát.

## Luồng chạy

HỦY (2026-09-28, net10 đồng bộ 2026-09-30): MainViewModel đổi nút chính thành HỦY đỏ trong cả lượt cài chung/cài riêng; xác nhận cảnh báo không rollback, sau đó hủy token. DeploymentProcessGroup tạo Windows Job riêng cho từng tác vụ (installer/Windows Setting) trong lượt; tác vụ kết thúc trước khi hủy thì Job được thả khỏi nhóm nên HỦY sau đó không tắt app nền nó để lại. MainViewModel gọi Cancel ngoài luồng giao diện. Khi đang hủy, MainWindow.OnClosing hỏi ConfirmExitWhileCancelling: đồng ý thì cho đóng dù nhóm chưa rỗng (lối thoát khi tiến trình treo); mutex bị bỏ lại được TryAcquire/WorkFolderCleaner xử lý như AbandonedMutex, marker phiên bootstrap giữ nguyên để không dọn phiên đó. PowerShell chờ event gate; chỉ mở gate sau AssignProcessToJobObject thành công, nên command và con cháu được quản lý từ trước lúc thực thi. TerminateJobObject dừng nhóm, không dò PID/kill theo tên; DrainCancellationAsync chờ ActiveProcesses bằng 0 trước khi nhả khóa/dọn cuối lượt. Không đặt KILL_ON_JOB_CLOSE để hoàn tất bình thường vẫn giữ app chạy nền như EVKey. Log đọc UTF-8; sau shell thoát chỉ drain pipe tối đa 500 ms vì app nền có thể giữ handle. Dịch vụ Windows dùng chung hoặc tác vụ được broker khởi chạy ngoài Job không thuộc phạm vi hủy; không đảm bảo rollback hay ngừng toàn bộ công việc cấp hệ điều hành. Hành vi "dừng hàng đợi không kill" trong mô tả lịch sử bên dưới không còn áp dụng cho target nào. EXTEND không đổi.

EXTEND CLEAN (cả hai target): MainViewModel xác nhận + giữ mutex → ExtensionService chuyển sang CleanService → kiểm tra SID phiên đăng nhập, TEMP/phạm vi → khóa profile từng trình duyệt (lockfile/parent.lock), bỏ qua trình duyệt đang mở → CleanEngine dọn allowlist → FirefoxHistory dùng SQLite transaction bảo toàn dấu trang → thống kê/log. Không nhúng script; dùng winsqlite3.dll của Windows cho cả hai target. Xem CLEAN.md.

Install: chọn suite (WPS/OnlyOffice/LibreOffice thêm tác vụ `WindowsSettingsCatalog.RemoveOffice()` chạy `Scripts/Remove-Office.ps1` cùng các Windows settings) → kiểm tra quyền/khóa phiên → đọc một snapshot đăng ký phần mềm → Smart Skip theo Installed/NotInstalled/Unknown → tải app còn thiếu song song cùng Windows settings (request đầu gửi `Range: bytes=0-`; server trả 206 thì file từ 16 MB chia 4 luồng byte-range ghi thẳng vào đúng vị trí, mỗi đoạn đứt kết nối được tải tiếp từ byte cuối, bỏ cuộc sau 3 lần liên tiếp không thêm byte nào; server trả 200 thì tải một luồng như cũ, lỗi thì tải lại từ đầu tối đa 3 lần; mỗi lần đọc phải có dữ liệu trong 90 giây; file sai định dạng/SHA-256 được tải lại một lần) → đọc snapshot mới trước khi launch để giảm cài trùng → chạy installer → gom tiến trình → chờ tác vụ đang chạy → tổng kết/dọn. Installed được bỏ qua có bằng chứng; Unknown không tự cài đè và làm lượt có thể thử lại. Quyết định được ghi vào `%LocalAppData%\MiniApps\InstallLogs`. MSI có hàng đợi riêng; dừng hàng đợi không kill bộ cài đang chạy. Bộ cài chạy bằng `Start-Process -PassThru` rồi `$p.WaitForExit()`, không dùng `-Wait` (vì `-Wait` chờ cả app mà bộ cài tự mở, như Zalo/WPS, tới khi người dùng đóng app). Sau khi bộ cài thoát, `DeploymentProcessGroup` chờ tiếp các tiến trình trong Job của tác vụ đó còn chạy từ thư mục work (giai đoạn tự giải nén vào %TEMP%), không chờ tiến trình ở nơi khác và ghi tên chúng vào log ("không chờ tiến trình nó để lại"). App có `WaitInstallerOnly` (EVKey: WinRAR SFX giải nén vào `C:\EVKey` rồi mở EVKey64.exe chạy nền) bỏ luôn bước chờ thư mục work. Dọn dẹp: mỗi bộ cài bị xóa ngay khi app đó xong (thành công, lỗi hoặc bỏ qua sau khi tải); cuối lượt xóa cả thư mục `%TEMP%\MiniApps\work-<id>`. Khi khởi động (không phải preview), `WorkFolderCleaner` xóa các `work-<32 hex>` còn sót nếu giữ được mutex cài đặt, bỏ qua thư mục đang bị khóa và thư mục chứa `debloat-*`/`Backups` của bản Debloat cũ. Log trong `%LocalAppData%\MiniApps` không tự xóa; test ghi log vào thư mục tạm qua `DeploymentService.InstallLogDirectory`.

Bootstrap: kiểm tra OS/kiến trúc và Framework → chọn net48 hoặc net10 → manifest schema 2 → xác minh URL/kích thước/hash ZIP → giải nén → chạy thử ẩn `--validate-config` (net48 lỗi hoặc treo quá 60 giây thì tải net10 và thử tiếp; hash/kích thước sai thì dừng, không chuyển gói) → chạy → chờ cây process và dọn phiên có marker. Push source không thay đổi ZIP trong Release.
