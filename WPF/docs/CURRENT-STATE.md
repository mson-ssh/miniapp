# Trạng thái bàn giao

## net10 đồng bộ với net48 — 2026-09-21, local

Bỏ mọi nhánh `#if NET48` ở giao diện: net10 giờ có trang INFORMATION (nhúng `tool/Info/info.ps1`, cửa sổ vừa khít dữ liệu), nút tải xuống màu đen ở danh sách Sẵn sàng, giống hệt net48. Trang Driver cũ của net10 bị gỡ cùng phần đọc máy riêng (`DeviceInfoService.ReadAsync`); `DeviceInfoService.Resolve` vẫn dùng cho nút Driver trong Information. Test chung một bộ: net48 và net10 đều 76 PASS; `--information-audit` trên net10 đọc đủ 12 dòng. Bản review net10 validate cấu hình và smoke test thoát 0. Khác biệt còn lại chỉ ở csproj: gói/tham chiếu riêng cho net48 (System.Text.Json, ReferenceAssemblies, System.Net.Http).

## Rà soát trước production — 2026-09-21, local

So với `miniapp/main` (1ea2240): 24 file sửa, 3 xóa, 17 mới (gồm `tool/Info` với `info.exe` 144 KB và `tool/Share LAN`). Build lại từ đầu net48 và net10 sạch 0 warning/0 error; net48 76 PASS, net10 70 PASS, 3/3 lần; Test-Bootstrap 8/8; Test-SplitDisk 12/12; test không sinh log. ReleaseConfig khớp hoàn toàn catalog mặc định (14 app, 11 Windows Setting, gồm cả script nhúng). 19 link tải trả 200 (14 bộ cài, info.exe, Win11Debloat, GitHub API winget, manifest v0.3.7, bootstrap raw). Sửa: `WorkFolderCleaner` lúc khởi động được bọc try/catch để không bao giờ chặn app mở; PROJECT-RULES ghi đúng ba trang của mỗi target. Khởi động thật (không preview) với `--startup-smoke-test` thoát 0, giữ thư mục backup Debloat.

Còn lại trước production: chưa phát hành (bootstrap vẫn v0.3.7, commit mới nhất trên GitHub là 1ea2240); `info.exe` trên R2 trùng bản build 18/09, cũ hơn `info.ps1` sửa 19/09; Share-LAN còn 4 lỗi đã xác định; chưa chạy thật Chia ổ, SMB, C++ trên máy nào (Debloat đã chạy thật 1 lần, mã 0).

## RAM gọn và cửa sổ vừa khít Information — 2026-09-19, local, net48

Dòng RAM (khe, loại, dung lượng, hãng, tốc độ) đổi từ cột co giãn theo bề ngang sang cột tự co theo nội dung, dùng chung độ rộng giữa các dòng (SharedSizeGroup), cách nhau 24 px. Khi Information hiện dữ liệu (lần đầu, Làm mới hoặc chuyển sang trang), `MainWindow.FitInformationHeight` đặt chiều cao cửa sổ bằng đúng nội dung: dài ra hoặc ngắn lại theo dữ liệu, tối thiểu 480, tối đa bằng vùng làm việc của màn hình (quá thì vẫn cuộn), giữ cửa sổ trong màn hình; bỏ qua khi cửa sổ đang phóng to. Rời Information thì trả lại chiều cao và MinHeight 660 trước đó. Chiều rộng không đổi. Test: RAM thẳng cột và còn chỗ trống cuối dòng; cửa sổ vừa khít (extent = viewport) và trả lại 810/660 khi sang trang khác; ảnh `information-fit.png`. net48 76 PASS, net10 70 PASS. Chưa commit/push.

## Loading Information ở giữa trang — 2026-09-19, local, net48

Thay thanh loading 2 px ở chân trang Information bằng khối `LoadingPanel` giữa trang: số phần trăm cỡ 48, thanh tiến trình 6 px và dòng "Đang đọc thông tin hệ thống…". Script đọc không báo tiến độ nên phần trăm chạy theo thời gian (bắt đầu 1%, mỗi 50 ms tiến thêm 3.4% phần còn lại, khoảng 90% sau 3.5 s, thời gian đọc đo được 3.3-4.5 s), dừng ở 99% cho tới khi có dữ liệu. Trong lúc đọc, nội dung được ẩn (kể cả khi Làm mới); đọc lỗi thì hiện lại dữ liệu cũ. Test WPF: giữ lượt đọc mở, phần trăm tăng dần, không vượt 99, đứng ở 99 sau 9.5 s, nhường chỗ cho dữ liệu khi đọc xong; ảnh `information-loading.png`. net48 74 PASS, net10 70 PASS (Information chỉ có trên net48). Chưa commit/push.

## Thanh điều hướng viết hoa — 2026-09-19, local

Theo yêu cầu, các mục sidebar viết hoa: INSTALL SOFTWARE, INFORMATION (net48) / DRIVER (net10), EXTEND. Tiêu đề trang giữ nguyên. Test điều hướng cập nhật; net48 73 PASS, net10 70 PASS. Chưa commit/push.

## Windows Setting Chia ổ đĩa — 2026-09-19, local

Theo yêu cầu người dùng, thêm thiết lập thứ 11 `Disk` ("Chia ổ đĩa"), script nhúng `Scripts/Split-Disk.ps1`, có trong catalog và `ReleaseConfig/windows.json` (nội dung trùng file nhúng); bỏ quy tắc cũ cấm chia ổ trong PROJECT-RULES. Giữ nguyên quy tắc của `$DiskScript` trong Setup.ps1: nhãn C: "OS", D: "LOCAL I", E: "LOCAL II"; nhóm 200-300 GB thêm D: 50.1 GB, 400-600 GB thêm D: 200.1 GB, 800-1100 GB thêm D: 400.1 GB và E: 200.1 GB; bỏ qua ổ trên 1100 GB, kích cỡ ngoài nhóm hoặc máy đã có D:/E:; dừng nếu C: còn từ 30 GB trở xuống; giải mã BitLocker (tối đa 60 phút) và tắt Hibernate trước khi thu nhỏ C:; format NTFS thử lại 10 lần, không dùng -Quick; gán lại chữ D:/E:. Thêm: hỏi `Get-PartitionSupportedSize` trước khi thu nhỏ; bỏ qua theo quy tắc là Hoàn tất, dừng do sự cố hoặc lỗi là Thất bại; `-DryRun` in kế hoạch; `ErrorActionPreference` Continue để lỗi stderr của manage-bde không dừng script. `Test-SplitDisk.ps1` thay mọi lệnh ổ đĩa bằng bản giả và chạy script thật qua 12 tình huống (ba nhóm với kích thước chính xác, các điều kiện an toàn, BitLocker, format lỗi, dry run). Dry run thật trên máy phát triển (NVMe 953.9 GB, đã có D:) báo bỏ qua. Thiết lập chạy song song với cài app như CLI. net48 73 PASS, net10 70 PASS, 3/3 lần. Chưa chia ổ thật trên máy nào; chưa commit/push.

## Thẻ EXTEND hiển thị gọn — 2026-09-19, local

Quy ước output cho script EXTEND: dòng bắt đầu ở cột 0 là tiến trình hiện trên thẻ, dòng thụt đầu dòng là output thô của công cụ, chỉ ghi nhật ký (`MainViewModel.IsProgressLine`). Install-CppEnvironment in tiếng Việt theo từng bước ("VS Code: đang tải/đang cài...", "Bộ biên dịch: gói 12/150 (gcc)", "PATH: ...", "Extension C/C++: ...") và dòng cuối là tổng kết "Xong: ..." hoặc "Chưa xong: ..."; bỏ hẳn các dòng thanh tiến trình của winget. Dòng "Kết thúc với mã X" chỉ ghi vào nhật ký. Debloat cũng gọn hơn (logo ASCII và dòng thụt của Win11Debloat không lên thẻ). Log người dùng chạy Debloat thật lúc 11:55 trên máy phát triển: mã 0, 2 app gỡ không được, restore point gần đây đã có nên không tạo mới. net48 72 PASS, net10 69 PASS, 3/3 lần. C++ chưa chạy thật. Chưa commit/push.

## EXTEND: Share LAN — 2026-09-19, local

Thêm thẻ thứ ba "Share LAN" dùng `tool/Share LAN/Share-LAN.ps1` (nhúng nguyên văn, resource `MiniApps.Scripts.Share-LAN.ps1`). Công cụ là menu tương tác (phím mũi tên, Read-Host, Get-Credential) nên không chạy ẩn: nút "Mở" ghi script ra `%LocalAppData%\MiniApps\Tools\Share-LAN.ps1` (ghi đè mỗi lần) và mở cửa sổ `powershell.exe -ExecutionPolicy Bypass -File` hiển thị, thừa hưởng quyền Administrator của MiniApps. Không hỏi xác nhận (công cụ có menu và Esc), không giữ khóa cài đặt nên vẫn mở được khi đang cài. `ExtensionItem.Interactive` phân biệt công cụ tương tác với phần mở rộng chạy ẩn. Đính chính: thư mục `tool` nằm trong `WPF/` (`..\tool` tính từ `MiniApps/`), nên `info.ps1` và `Share-LAN.ps1` đi cùng repo `miniapp`. net48 71 PASS, net10 68 PASS, 3/3 lần. Chưa mở thật từ bản build, chưa commit/push.

## Windows Setting SMB — 2026-09-19, local

Thêm thiết lập thứ 10 `Smb` ("SMB chia sẻ mạng"), script nhúng `Scripts/Enable-Smb.ps1`, có trong catalog và `ReleaseConfig/windows.json` (nội dung trùng file nhúng). Theo lựa chọn của người dùng cho cả ba tình huống. Client: bật insecure guest logons (cmdlet và policy `AllowInsecureGuestAuth`), bỏ bắt buộc ký SMB, bật tính năng `SMB1Protocol-Client`, tắt tự gỡ `SMB1Protocol-Deprecation`; SMB1 server luôn tắt. Server: bỏ bắt buộc ký SMB, đổi mạng Public đang kết nối sang Private (mạng Domain giữ nguyên), bật rule tường lửa Network Discovery và File and Printer Sharing theo resource id `@FirewallAPI.dll,-32752` / `-28502` (không phụ thuộc ngôn ngữ); rule chỉ Public giữ nguyên, rule nhiều profile được bỏ Public trước khi bật ("Any" thành Domain, Private); bật dịch vụ LanmanServer/Workstation, FDResPub, fdPHost, SSDPSRV, upnphost. Mỗi bước chạy độc lập, bước lỗi được liệt kê và làm thiết lập báo Thất bại; bật SMB1 có thể cần khởi động lại. Đã dò chỉ-đọc trên máy phát triển (Windows 10 19041): nhóm rule tồn tại, 12 rule chỉ Public không bị đụng, logic bỏ Public đúng cho mọi dạng profile gặp. **Chưa chạy thật**; cần thử trên máy Windows 11 24H2. net48 69 PASS, net10 66 PASS, 3/3 lần. Chưa commit/push.

## Thêm Windows Setting Execution Policy — 2026-09-19, local

Thêm thiết lập thứ 9 `ExecutionPolicy` ("Execution Policy: Bypass") vào `WindowsSettingsCatalog` và `ReleaseConfig/windows.json` (giữ định dạng ConvertTo-Json, CRLF, BOM). Script: `Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope LocalMachine -Force -ErrorAction Stop`; `-Force` để không có lời hỏi xác nhận trong tiến trình không tương tác. Nếu Group Policy đặt policy ở phạm vi cao hơn, lệnh báo lỗi và mục này hiện Thất bại (đúng thực tế). Đã thử bằng -WhatIf trong tiến trình -NonInteractive -ExecutionPolicy Bypass: không hỏi, thoát 0. net48 68 PASS, net10 65 PASS, 3/3 lần. Chưa commit/push.

## EXTEND chạy thật: Win11Debloat và môi trường C++ — 2026-09-19, local

Hai thẻ EXTEND có chức năng, cho cả net48 và net10. Nút chạy hỏi xác nhận (liệt kê thay đổi), kiểm tra Administrator, giữ mutex `Global\MiniApps.Deployment` nên khóa Install Software và phần mở rộng khác trong lúc chạy; thẻ hiện trạng thái, dòng output mới nhất, thanh chạy và nút "Mở nhật ký". `ExtensionService` ghi script nhúng vào `%TEMP%\MiniApps\work-<id>`, chạy `powershell.exe -NonInteractive -File` ẩn với stdin đã đóng (lời nhắc "press any key" thất bại thay vì treo), đọc output UTF-8, ghi log `%LocalAppData%\MiniApps\ExtendLogs\<id>-<thời điểm>.log`, không timeout/không kill, rồi xóa thư mục work.

`Scripts/Invoke-Win11Debloat.ps1`: ghim Win11Debloat `2026.08.24` từ `github.com/Raphire/Win11Debloat/archive/refs/tags/2026.08.24.zip`, SHA-256 `00D1487B…2BD4`, tải lại tối đa 3 lần, giải nén trực tiếp bằng ZipFile vào `<version>.partial` (bỏ thư mục gốc, chặn đường dẫn thoát ra ngoài, giữ đường dẫn ngắn dưới giới hạn 260 ký tự) rồi đổi tên thành `%LocalAppData%\MiniApps\Win11Debloat\2026.08.24`. Thư mục này được giữ vì Win11Debloat lưu `Backups\` (Registry) và `Logs\` trong đó; lần sau dùng lại, không tải lại; thư mục có sẵn mà thiếu script thì báo lỗi thay vì xóa. Chạy `Win11Debloat.ps1 -RunDefaults -Silent` bằng Windows PowerShell 5.1 trong tiến trình con (mặc định: tạo restore point, gỡ bộ app mặc định cho mọi tài khoản, 17 tinh chỉnh trong DefaultSettings.json, khởi động lại Explorer). `-DryRun` tải/kiểm/giải nén rồi in lệnh; đã chạy thử hai lần (lần hai dùng lại bản có sẵn), 403 file.

`Scripts/Install-CppEnvironment.ps1`: chuyển từ công cụ CLI. Kiểm tra VS Code, MSYS2, gcc/g++/gdb, PATH trước và bỏ qua phần đã có; thiếu winget thì gọi `Update-Winget.ps1`; winget `--source winget --exact --silent`; pacman `-Syu` hai lượt rồi `-S --needed base-devel mingw-w64-ucrt-x86_64-toolchain`, mỗi lệnh thử lại tối đa 3 lần; thêm `C:\msys64\ucrt64\bin` vào PATH máy; cài `ms-vscode.cpptools` và kiểm tra bằng `--list-extensions`. Kết quả từng bước dựa trên file thực có trên đĩa; thiếu bước nào thì thoát mã 1. `-DryRun` chỉ báo trạng thái; trên máy phát triển cả bốn phần đã có.

Test mới: danh sách phần mở rộng, service ghi đủ script/log/xóa work, PowerShell ẩn thật (UTF-8, ReadKey không treo, mã thoát), luồng WPF (từ chối xác nhận không chạy; đang chạy khóa mọi thao tác cài; kết thúc báo Hoàn tất). net48 67 PASS, net10 64 PASS, 3/3 lần; Test-Bootstrap 8/8; test không sinh log. **Chưa chạy Debloat hay cài C++ thật** trên máy phát triển; cần thử trên máy/VM thử nghiệm. Chưa commit/push.

## Trang EXTEND (giao diện) — 2026-09-19, local

Thêm mục `EXTEND` dưới Information/Driver (Page 2) cho cả net48 và net10. Trang liệt kê `MainViewModel.Extensions` (Debloatware Windows; Môi trường C++ gồm VS Code, MinGW-w64, extension C/C++ theo công cụ cũ của CLI) dạng thẻ có nhãn "Sắp có" và nút Cài đặt đang khóa (tooltip "Chức năng đang được phát triển"). Chưa có chức năng cài; thêm mục mới chỉ cần thêm một `ExtensionItem`. Test: điều hướng 3 trang, danh sách phần mở rộng, nút đang khóa, chụp `extend.png`. net48 64 PASS, net10 61 PASS, 3/3 lần; 0 log mới. Chưa commit/push.

## Dọn bộ cài sớm và dọn thư mục work cũ — 2026-09-19, local

Mỗi bộ cài bị xóa ngay khi app đó xong thay vì đợi hết lượt; file còn bị bộ cài giữ thì ghi log và để bước dọn cuối lượt xử lý. Khi khởi động (không preview), `WorkFolderCleaner.RemoveStale` xóa `%TEMP%\MiniApps\work-<32 hex>` còn sót, chỉ khi giữ được mutex `Global\MiniApps.Deployment`, bỏ qua reparse point, thư mục đang khóa, và thư mục chứa `debloat-*` hoặc `Backups` (backup Registry của bản Debloat cũ). Tên mutex dùng chung `DeploymentService.DeploymentLockName`. Log không có cơ chế xoay vòng theo yêu cầu; test đặt `DeploymentService.InstallLogDirectory` vào thư mục tạm nên không sinh log trên máy. Test mới: xóa bộ cài sau thành công/lỗi, bộ cài còn bị giữ, dọn thư mục cũ (mutex bận, thư mục khóa, tên lạ, thư mục có backup). net48 62 PASS, net10 59 PASS, 3/3 lần; 0 log mới.

Dọn tay trên máy phát triển: đã xóa 7 thư mục phiên bootstrap cũ ngày 07/09, 1 phiên ngày 18/09 và 2 thư mục `work-*` ngày 15/09 (không còn tiến trình chạy từ đó). Giữ lại `work-eb61a4f32f3749debb92a2ba6c99281c`: do Debloat cũ tạo bằng quyền Admin, chứa `Win11Debloat-2026.08.24\Backups`; cần người dùng quyết định. Không xóa log. Chưa commit/push.

## Làm đẹp drop list — 2026-09-19, local

Style ComboBox chung trong `Theme.xaml` được thay bằng template riêng: nền trắng, bo góc 5, mũi tên nét mảnh xoay khi mở, viền xanh khi rê chuột/focus/mở, mờ khi bị khóa; danh sách xổ xuống bo góc có bóng đổ, dòng chọn in đậm màu Accent kèm dấu tích vẽ bằng Path. Drop list cao bằng nút Cài đặt (bind `ActualHeight` của `InstallButton`). Test WPF kiểm tra ô đóng hiện đúng nhãn và danh sách mở đủ 5 lựa chọn, chụp `suite-dropdown-open.png`. net48 59 PASS, net10 56 PASS. Chưa commit/push.

## Drop list chọn bộ văn phòng — 2026-09-19, local

Cạnh nút Cài đặt có drop list `SuitePicker` (Microsoft Office, WPS, OnlyOffice, Libre Office, Null). Mặc định Null: lượt cài chính chỉ gồm 10 app không thuộc bộ văn phòng nào. Hộp thoại chọn Office (`OfficeChoiceDialog`, `OfficeChoice`) đã bị gỡ; lựa chọn trong drop list quyết định app nào vào lượt cài và danh sách cập nhật ngay khi đổi. Drop list bị khóa khi đang chạy và sau khi lượt cài đã bắt đầu. Catalog và `ReleaseConfig/apps.json` thêm `onlyoffice` và `libreoffice`; Smart Skip có luật riêng cho hai app, loại trừ Help Pack. Hai bộ cài vượt giới hạn 300 MB/file của R2 nên tải từ nhà phát hành: OnlyOffice dùng link luôn trỏ bản mới nhất `download.onlyoffice.com/.../DesktopEditors_x64.exe` (Inno Setup, `/VERYSILENT /NORESTART /SUPPRESSMSGBOXES`, không ghim hash vì nội dung thay đổi theo bản); LibreOffice ghim bản 26.2.6 tại kho lưu trữ vĩnh viễn `downloadarchive.documentfoundation.org/libreoffice/old/26.2.6.3/...` (MSI, `/qn /norestart`, SHA-256 `f9877032…2660`, trùng file phát hành ở `stable`). Ngày 2026-09-19 đã tải thử cả hai (mỗi file ~356 MB), header và hash khớp; chưa cài thật. Muốn nâng LibreOffice thì đổi URL và hash sang build mới trong kho lưu trữ. Build net48 và net10 sạch; net48 59 PASS, net10 56 PASS (khác nhau vì Information chỉ có trên net48), 3/3 lần chạy. Chưa commit/push/phát hành.

## Dot sát tên Windows và thông số màn hình tối đa — 2026-09-19, local, net48

Dot kích hoạt đặt ngay sau tên phiên bản Windows. Độ phân giải tối đa và tần số quét tối đa cùng một dòng. Information (-AsJson) dùng EnumDisplaySettings của màn hình chính: chọn độ phân giải có diện tích pixel lớn nhất và tần số cao nhất trong các mode Windows báo qua kết nối hiện tại; hai mức độc lập, có tooltip giải thích. Không lấy tần số từ màn hình khác, không mặc định 60 Hz khi truy vấn thất bại; thiếu dữ liệu hiển thị —. Bổ sung các trường còn thiếu cuối cấu trúc DEVMODE của helper để khớp cấu trúc native.

Build net48 sạch; 51 logic tests và 8 thông báo PASS WPF/structured đạt trước phần bổ sung trường native; script cuối đọc thật thành công (1920×1080, 60 Hz). Đã xem ảnh render vị trí dot và dòng màn hình. Publish output riêng cuối thành công, validate cấu hình và mở Public cho người dùng. Chưa xác minh trên máy đa màn hình hoặc các màn hình có độ phân giải/tần số cao hơn; không đổi thiết lập hiển thị, không build net10, chưa commit/push/phát hành.

## Refresh icon và dot kích hoạt Windows — 2026-09-19, local, net48

Nút Làm mới dùng Path vector Refresh, giữ tooltip và tên accessibility. Trạng thái Windows đổi sang dot xanh lá nếu đã kích hoạt, vàng nếu chưa xác nhận kích hoạt; tooltip giữ diễn giải trạng thái. Chỉ sửa view Information net48. Build sạch; 51 logic tests và 8 thông báo PASS WPF/structured đạt, đã xem render. Validate cấu hình và mở output Public riêng; không build net10, không cài đặt/thiết lập Windows, chưa commit/push/phát hành.

## Gộp Driver vào Information — 2026-09-19, local, net48

Sidebar net48 chỉ còn Install Software (Page 0) và Information (Page 1). Nút Driver nằm cạnh Serial in đậm: sao chép Serial đang hiển thị, sau đó mở URL hỗ trợ HTTPS chính thức qua mapping hãng của DeviceInfoService. Script Information trả thêm Manufacturer, không quét thiết bị lần thứ hai. Thiếu Serial thì báo lỗi; clipboard lỗi thì không mở web; không nhận diện hãng thì báo đã sao chép nhưng chưa có trang hỗ trợ. Làm mới tạm khóa nút Driver. Preview chỉ mô phỏng, không đụng clipboard/mở browser. Net10 giữ trang Driver cũ nhờ điều kiện NET48.

Build net48 sạch; 51 logic tests và 8 thông báo PASS WPF/structured đạt. Test mới dùng callback giả để xác minh thứ tự copy → open, mapping Dell, clipboard lỗi, thiếu Serial và thiếu hãng. Đã xem render ở 940×660, audit script nhúng đọc máy thật đạt. Output Public riêng validate cấu hình và được mở để người dùng tự test; chưa bấm nút Driver thật, chưa chạy installer/Windows settings. Không build net10, chưa commit/push/phát hành.

## Serial, Làm mới và định dạng RAM — 2026-09-19, local, net48

Serial in đậm; hiện nút Làm mới góc trên bên phải (Sao chép vẫn ẩn). RAM từng module chia cột SLOT n: / loại / dung lượng / hãng / tốc độ, giữ cỡ chữ 14. Dòng tổng theo thứ tự form factor, loại, tổng dung lượng, hãng, tốc độ; ví dụ ONBOARD DDR4 16GB Micron 2400Mhz. Nhiều giá trị khác nhau được giữ và ngăn bằng dấu /; không cố định ONBOARD hoặc hãng. Nhãn tốc độ đổi thành Mhz theo mẫu người dùng, giữ số liệu script trả về (không đổi tốc độ phần cứng).

Build net48 sạch, 50 logic tests và 8 thông báo PASS WPF/structured đạt; đã xem render ở cửa sổ nhỏ. Output Public riêng đã validate cấu hình và mở để người dùng xem. Không build net10, không chạy cài đặt/Windows settings, chưa commit/push/phát hành.

## Nhãn RAM Slot 1, Slot 2 — 2026-09-19, local, net48

Theo yêu cầu, nhãn RAM trên Information dùng Slot 1, Slot 2, … theo thứ tự module, thay tên Channel/DeviceLocator của BIOS. Chỉ đổi trình bày, không thay dữ liệu thu thập. Build net48 sạch; 50 logic tests và 8 thông báo PASS WPF/structured đạt; validate cấu hình và mở output Public riêng. Không build net10, không cài đặt/thiết lập Windows, chưa commit/push/phát hành.

## Thu gọn RAM — 2026-09-19, local, net48

RAM hiển thị tổng dung lượng và thông số chung trên một dòng; bên dưới mỗi khe một dòng dung lượng + hãng. Bỏ tiêu đề bốn cột, đường phân cách và giảm padding. Nếu các thanh có loại/tốc độ/form factor khác nhau, giữ thông số riêng ở từng dòng để không mất dữ liệu. Giữ cỡ chữ 14. Build net48 sạch; 50 logic tests và 8 thông báo PASS WPF/structured đạt, đã xem render cửa sổ nhỏ. Validate cấu hình và mở Public output riêng. Không build net10 hoặc chạy cài đặt/Windows settings; chưa commit/push/phát hành.

## Hệ thống Information hai thông tin mỗi dòng — 2026-09-19, local, net48

Gộp phần Hệ thống thành hai dòng: Windows + Host, Model + Serial. Bản quyền vẫn nằm dưới giá trị Windows; giữ xuống dòng cho giá trị dài và cỡ chữ hiện tại. Chỉ sửa XAML net48. Build sạch, 50 logic tests và 8 thông báo PASS WPF/structured đạt; đã xem render ở cửa sổ nhỏ, validate cấu hình và mở Public output riêng. Không chạy cài đặt/Windows settings, không build net10, chưa commit/push/phát hành.

## Biểu tượng cài riêng — 2026-09-19, local, net48

Thay tam giác ở Install Software bằng biểu tượng tải xuống màu đen dựng lại bằng Path vector WPF theo ảnh người dùng. Cập nhật lời hướng dẫn thành nút tải xuống; giữ InstallOneCommand và chức năng cài riêng. Giá trị icon/hướng dẫn có điều kiện NET48; net10 giữ biểu tượng tam giác. Build net48 sạch, 50 logic tests và 8 thông báo PASS WPF/structured đạt; đã xem render Install Software, validate cấu hình và mở output Public riêng. Không cài app/thiết lập Windows, không build net10, chưa commit/push/phát hành.

## Danh sách RAM, GPU và ổ đĩa có cột — 2026-09-19, local, net48

Information hiển thị mỗi thanh RAM và GPU thành một dòng có cột riêng. RAM gồm tổng dung lượng, khe, dung lượng, loại/tốc độ/form factor và hãng; GPU gồm tên, loại, VRAM và công suất nếu đọc được. Mỗi ổ vật lý có model/dung lượng/kết nối; phân vùng bên dưới có GB trống/tổng và thanh tỷ lệ đã dùng. Script bổ sung RamTotal/RamItems/GpuItems và trường cấu trúc trong StorageItems, giữ các chuỗi hiển thị cũ cho standalone. Không tách dữ liệu từ ký tự cây. Thiếu trường hiển thị dấu —; dung lượng không hợp lệ không vẽ thanh tỷ lệ. Fallback WMI không khai báo kết nối NVMe cho bảng mới khi chưa xác minh. Hai nút Sao chép/Đọc lại tiếp tục ẩn, chữ nội dung giữ cỡ 14.

Build net48 sạch; 50 logic tests cùng 8 thông báo PASS WPF/structured đạt, có kiểm tra nhiều linh kiện, phân vùng và biên dung lượng rỗng/đầy/không hợp lệ. Đã xem render 1120×810, 940×660 và phần ổ đĩa sau cuộn. Script đọc thật trước đó trả 2 RAM, 1 GPU, 1 ổ, 4 phân vùng; audit từ assembly nhúng chạy lại thành công ngày 19/09. Output Public riêng đã validate cấu hình và mở cho người dùng tự test. Không chạy installer/Windows settings; chưa test trên phần cứng khác, chưa build net10, chưa commit/push/phát hành.

## Ẩn nút và tăng chữ Information — 2026-09-18, local, net48

Ẩn cụm Sao chép/Đọc lại bằng Collapsed theo yêu cầu; vẫn tự đọc khi mở trang. Tăng cỡ chữ nội dung từ 13 lên 14, nhãn từ 12 lên 13, tiêu đề nhóm và chữ phụ tăng 1. Giữ khả năng chọn văn bản và cuộn. Build net48 sạch, 50 logic tests và 7 thông báo PASS WPF đạt; đã xem render, validate cấu hình và mở output Public riêng. Không build net10, không chạy cài đặt/Windows settings, chưa commit/push/phát hành.

## Information dạng bảng thông số — 2026-09-18, local, net48

Theo phản hồi giao diện còn rối, thay bố cục hai cột và các thẻ màu bằng một bảng đơn sắc. Ba nhóm Hệ thống, Phần cứng, Màn hình dùng tiêu đề nền xám nhẹ; nhãn và giá trị thẳng hàng, đường phân cách mảnh, chữ đồng nhất. Giữ toàn bộ nội dung nhiều dòng, sao chép, đọc lại và thời điểm đọc. Chỉ sửa XAML Information net48.

Build sạch, 50 logic tests và 7 thông báo PASS WPF đạt. Đã xem render fixture nhiều dòng ở 1120×810 và 940×660; cửa sổ nhỏ cuộn dọc. Output riêng đã validate cấu hình và mở Public để người dùng xem. Không chạy cài đặt/Windows settings, không build net10, chưa commit/push/phát hành.

## Information có thiết kế riêng — 2026-09-18, local, net48

Theo phản hồi người dùng, bỏ bố cục đầu trang tương tự Driver. Information dùng hai cột: cột chính gồm các thẻ CPU, RAM, GPU với nhãn màu nhẹ, dòng thông số chính và chi tiết; tiếp theo là lưu trữ. Cột phụ gồm Windows, định danh máy, màn hình và thời điểm đọc. Style nằm riêng trong InformationView; Driver không đổi. InformationRow bổ sung Summary/Details để trình bày, dữ liệu gốc và sao chép toàn bộ vẫn được giữ.

Build net48 sạch 0 warning/0 error; 50 logic tests và 7 thông báo PASS WPF đạt. Đã xem ảnh fixture nhiều dòng ở 1120×810 và 940×660, bố cục tự xuống dòng và cuộn. Output Public riêng đã validate cấu hình và mở để người dùng xem. Không chạy installer/Windows settings, không build net10, chưa commit/push/phát hành.

## Làm gọn giao diện Information — 2026-09-18, local, net48

Trang Information chuyển từ bảng 12 dòng sang các khối: thiết bị (model, tên máy, serial, Windows/bản quyền), CPU/RAM/GPU, ổ đĩa và màn hình. Nền sáng, nhãn xanh nhẹ, khoảng cách gọn; giữ chọn văn bản, sao chép toàn bộ, đọc lại và thời điểm dữ liệu. Thông tin nhiều dòng tự xuống dòng và cuộn theo chiều dọc; trạng thái tải/lỗi ở chân trang. Chỉ sửa view net48 và kiểm tra render liên quan, không thay logic đọc phần cứng hay cấu hình catalog.

Build net48 sạch; 50 logic tests cùng 7 thông báo PASS WPF đạt. Đã xem ảnh render với fixture nhiều thanh RAM, hai GPU, hai ổ đĩa ở 1120×810 và 940×660. Bản Public mới đã validate cấu hình và được mở để người dùng xem. Không chạy cài đặt/thiết lập Windows; không build net10; chưa commit/push/phát hành.

## Thêm Information bên dưới Driver — 2026-09-18, local, net48

Thêm trang Information trong MiniApps, tái sử dụng `Get-SystemData` của `tool/Info/info.ps1` qua chế độ mới `-AsJson`. Script được nhúng vào assembly net48, không phụ thuộc đường dẫn máy phát triển; script chạy riêng không tham số vẫn giữ giao diện cũ. Trang hiển thị 12 dòng gồm hệ điều hành/bản quyền, thiết bị, CPU, RAM, GPU, ổ đĩa và màn hình; tải nền khi mở, có Đọc lại và Sao chép. Thời điểm đọc là snapshot, không có timer tự làm mới. Có timeout 90 giây và hủy process đọc khi đóng ứng dụng. Preview dùng dữ liệu giả. Tính năng được giới hạn bằng NET48/MSBuild; chưa build/test hay đồng bộ net10.

Kiểm tra: build net48 sạch 0 warning/0 error; 50 logic tests và 7 thông báo PASS WPF đạt, gồm điều hướng Information và bảng dữ liệu. Test WPF dùng ShutdownMode.OnExplicitShutdown để cửa sổ kiểm tra sau không bị application shutdown khi cửa sổ đầu đóng. Đã kiểm tra ảnh render trang Information. Script parse đạt và đã đọc phần cứng thật qua cả `-AsJson` lẫn service/script nhúng (12 dòng), không lưu giá trị thiết bị vào tài liệu. Output `artifacts/review-information-net48-20260918-180231` đã validate cấu hình mã 0 và mở Public để người dùng xem. Không chạy cài đặt hoặc thiết lập Windows; chưa commit/push/phát hành. Chưa kiểm tra trên máy phần cứng khác hoặc chạy lại toàn bộ giao diện standalone của script gốc.

## Cài riêng từng app từ panel Sẵn sàng — 2026-09-18, local

Theo yêu cầu người dùng, panel "Sẵn sàng cài đặt" hiện toàn bộ catalog (`ReadyApps`, gồm cả Office 2024 và WPS) dạng thẻ 3 cột. Mỗi thẻ có trạng thái, thanh tiến trình và nút tam giác vẽ bằng `Path`. Nút gọi `InstallOneCommand`, chỉ cài đúng app đó qua `DeploymentService`: không hỏi Office/WPS, không chạy Windows Setting, vẫn qua Smart Skip, dùng chung mutex, kiểm tra Admin và Dừng hàng đợi. Trong lúc chạy, nút Cài đặt chính và các nút tam giác khác bị khóa; chạy xong, nút Cài đặt chính vẫn dùng được. Phần dùng chung giữa cài toàn bộ và cài lẻ (thư mục tạm, marker phiên, mô phỏng) được tách thành `RunDeploymentAsync`. Test thêm "Ready list offers every catalog app" và "WPF single app install from the ready list"; net48 và net10 đều build sạch, 56 PASS, 3/3 lần chạy. Chưa có trong gói v0.3.8 đã build (commit `1ea2240`); chưa commit/push.

## Bootstrap tự chuyển sang net10 — 2026-09-18, local

Sau khi giải nén, bootstrap chạy thử gói ở chế độ ẩn bằng `MiniApps.exe --validate-config` (`Test-MiniAppsPackage`, giới hạn 60 giây, quá hạn thì dừng tiến trình chạy thử). Máy chọn net48 mà chạy thử thất bại thì tải net10 và thử tiếp (`Get-MiniAppsTargetOrder`); cả hai thất bại thì báo lỗi, không cài gì. Hash hoặc kích thước sai vẫn dừng ngay, không chuyển gói. Hai hàm mới được đưa vào lệnh nâng quyền. `Test-Bootstrap.ps1` đạt 8/8 nhóm, thêm thứ tự fallback và chạy thử với fixture thoát 0 / khác 0 / không khởi động được / treo. Chạy thử với hai bản build thật net48 và net10 đều thành công (~0,3–0,4 giây). Chưa kiểm thử toàn luồng fallback qua mạng vì cần Release thật; chưa commit/push/phát hành, bootstrap vẫn trỏ `v0.3.7`.

## Đồng bộ net10 theo net48 — 2026-09-18, local

Theo yêu cầu người dùng, mọi nhánh `#if NET48` trong source và test đã được gỡ: net10 giờ dùng đúng code của net48. Cụ thể: Smart Skip ba trạng thái (`InstalledSoftwareDetector.net48.cs` đổi tên thành `InstalledSoftwareDetector.cs`), kiểm tra lại trước khi chạy bộ cài, log `InstallLogs`, nút Cài đặt chỉ khóa khi không có lỗi, tổng kết có số bỏ qua/chưa xác minh, `StartupFailureLog` và cờ `--startup-smoke-test`. Nhánh net10 cũ (so tên Registry bằng regex, Office chỉ khớp "2024") đã bị xóa. Chỉ còn shim `#if NETFRAMEWORK` cho `IsExternalInit`.

Kiểm tra: build sạch 0 warning/0 error trên cả hai target; mỗi target đạt 49 logic tests và 5 WPF checks, 3/3 lần chạy, danh sách test giống hệt nhau. `--smart-skip-audit` (chỉ đọc) trên máy phát triển cho kết quả giống hệt giữa net48 và net10: 11 Installed (gồm Office Home & Student 2021, VC++ v14 x64/x86), WPS NotInstalled, 0 lỗi đọc. Output `artifacts/review-sync-net48-20260918-170905` (15 file) và `artifacts/review-sync-net10-20260918-170905` (404 file, self-contained) đều có `--validate-config` và `--preview --startup-smoke-test` thoát mã 0; bản net10 thật đã mở để người dùng xem. Lưu ý: test giả lập Smart Skip ghi log fixture vào `%LocalAppData%\MiniApps\InstallLogs` thật (có từ trước với net48, giờ cả net10); log fixture của các lần chạy hôm nay đã được dọn. Chưa commit/push/phát hành; gói net10 trên Release vẫn là v0.3.6.

## Gỡ edition Developer, chỉ còn Public — 2026-09-18, local

Theo yêu cầu người dùng, source chỉ còn một bản. Cờ MSBuild `MiniAppsEdition` và `MINIAPPS_DEVELOPER`, `BuildEdition.cs`, `Run-Developer.ps1`, nhánh `-Developer` của `Preview.ps1` và các tham số `--initialize-release-config` / `--developer-preview` đã bị gỡ. Theo đó, trang Setting (CRUD catalog) cùng phần ghi của `SettingsStore` (`Save`, `SaveWindows`) cũng bị gỡ; ứng dụng chỉ đọc `ReleaseConfig`, catalog sửa trực tiếp trong JSON. Phần Optimize còn sót (`OptimizeService.net48.cs`, `AnimatedStackPanel`, model Optimize, trang XAML, `RunExecutableAsync`) vốn chỉ vào được qua Developer nên cũng bị gỡ. Điều hướng giờ còn hai trang: `Page` 0 = Install Software, 1 = Driver.

Style dùng chung được tách từ `App.xaml` sang `Theme.xaml`. Test WPF dùng một `Application` trơn nạp `Theme.xaml` thay vì `MiniApps.App`: constructor của `Application` tự xếp `OnStartup` vào dispatcher, trước đây lời gọi đó âm thầm mở thêm một cửa sổ Developer thật trong lúc test, còn với bản Public thì nó báo thiếu `ReleaseConfig` rồi tắt app giữa chừng.

Kiểm tra: build net48 sạch 0 warning/0 error; 49 logic tests và 5 WPF checks đạt 3/3 lần chạy (các test Setting/Optimize/Developer đã bỏ cùng tính năng). Output `artifacts/review-public-only-net48-20260918-165332` có 15 file; `--validate-config` và `--preview --startup-smoke-test` đều thoát mã 0; bản thật đã được mở để người dùng xem. Backup source trước thay đổi: `artifacts/backup-before-remove-developer-20260918.zip`. Không chạy Install hay Windows settings thật; chưa commit/push/phát hành. Release `v0.3.7` trên bootstrap vẫn là bản cũ.

net10 (theo yêu cầu người dùng, cùng ngày): build sạch 0 warning/0 error với SDK 10.0.400; 42 logic tests và 5 WPF checks đạt 3/3 lần chạy. 7 test chênh so với net48 là các tính năng chỉ có trên net48 (Smart Skip ba trạng thái, log lỗi khởi động). Publish self-contained win-x64 vào `artifacts/review-public-only-net10-20260918-170122` (404 file, 141 MB); `--validate-config` thoát mã 0 và bản thật đã mở, cửa sổ phản hồi. net10 không có cờ `--startup-smoke-test`. Gói net10 trên Release vẫn là bản v0.3.6 cũ, chưa thay.

## Quy trình Public mặc định — 2026-09-18

Theo yêu cầu người dùng, từ nay chỉ làm việc và mở Public net48 theo mặc định. Developer được giữ nguyên, ẩn khỏi quy trình làm việc, không xóa; chỉ mở lại khi người dùng yêu cầu. Quy định này thay thế các hướng dẫn mở Developer mặc định trong những mục lịch sử bên dưới. Build Public dùng `MiniAppsEdition=Public`, output riêng và bản sao `ReleaseConfig` cạnh executable.

Trong phiên này đã build, validate cấu hình và mở Public net48 thành công để người dùng xem; cửa sổ có phản hồi. Thay đổi quy trình chỉ cập nhật tài liệu, không sửa logic ứng dụng, không chạy bộ cài hoặc Windows settings, không build net10, không commit/push/phát hành.

## Gỡ Debloat/Optimize — source trên main

Thẻ Optimize Windows bị ẩn trong cả Developer và Public. Engine helper, cây Win11Debloat vendor, script/fixture engine và mục Debloat trong Windows Setting được gỡ khỏi source đóng gói. Cấu hình Windows tăng lên schema 3; cấu hình schema 1/2 được đọc tương thích và tự loại mục Debloat trong bộ nhớ. Developer còn Install Software, Driver, Setting; Public còn Install Software và Driver. Build net48 và net10 sạch 0 warning/0 error; net48 đạt 71 logic tests cùng 7 WPF checks, net10 đạt 53 logic tests cùng 6 WPF checks. Output net48 mới có 13 file cho Developer và 15 file cho Public, không có tệp Debloat/Optimize; Public validate config thoát mã 0. Bản Developer net48 thật đã được mở để người dùng kiểm tra. Không chạy Install hoặc thay đổi Windows thật. Source đã được push lên `main` tại commit `3497963c`. Chưa tạo Release mới; bootstrap và lệnh `irm` vẫn dùng bản `v0.3.7`.

## Phát hành v0.3.7 — Optimize engine một EXE

Developer net48 nay đóng gói toàn bộ fork Win11Debloat và tám script điều phối vào resource ZIP của `MiniApps.OptimizeEngine.exe`. Service gọi helper bằng `--run --silent`; helper xác minh payload trước khi giải nén, chuyển tiếp protocol stdout/stderr và trả nguyên mã thoát cho UI. Output Developer không còn cây `Engine/Debloat` hoặc script Optimize rời. Output Public loại hoàn toàn helper và payload Optimize, phù hợp giao diện chỉ có Install Software và Driver.

Kiểm tra hiện tại: helper 392192 bytes, payload 352 tệp; `--verify`, `--version`, kiểm tra payload xác định và fixture EXE độc lập đều đạt. Build net48 sạch 0 warning/0 error; 71 logic tests, 6 WPF checks, 77 kiểm tra bundle và các fixture parallel/task bridge/Appx worker/WinGet worker/timeout đều đạt. Build sạch net10 đạt; 53 logic tests và 5 WPF checks đạt để xác nhận source dùng chung không bị ảnh hưởng. Output kiểm tra Developer có 14 file; Public có 15 file và ZIP thử 502198 bytes, giảm từ 368 file của gói v0.3.6 xuống cấu trúc gọn. Public validate config và startup smoke đều thoát mã 0. Bản Developer net48 thật đã được mở từ output riêng để người dùng kiểm thử. Không fixture nào chạy Optimize hoặc thay đổi Windows thật.

Đã phát hành source engine tại commit `5548dd2a0a32d72db61985067700338944e8fb8f`. ZIP net48 có 501811 bytes, SHA-256 `c1c7bfe1c2aa1f6e89065d70e45543bf85eb0a20b105bb23c9966de7bfebc69a`. Gói net10 của v0.3.6 được tái sử dụng nguyên byte: 63080335 bytes, SHA-256 `e1cbb8c392061b92d19fa0a9118e8351a298d4b11a9d4233bd4058723801f515`. Release công khai đủ hai ZIP, hai checksum và manifest; cả hai ZIP đã được tải lại từ GitHub và khớp manifest. Bootstrap được chuyển sang `v0.3.7` sau bước xác minh. Không chạy Optimize hoặc thay đổi Windows thật trong quá trình phát hành.

## Phát hành v0.3.6 — 2026-09-08

Đã phát hành source tại commit `f617fb0c1a91722faffc0c9b9e3709d754ac5797` cùng hai gói Public mới. ZIP net48 có 878546 bytes, SHA-256 `fb94ae0ea2d63d2b0a7a0eec21e4130f80ddfb2cf1d02dd2e8c74613fb527fba`; ZIP net10 self-contained có 63080335 bytes, SHA-256 `e1cbb8c392061b92d19fa0a9118e8351a298d4b11a9d4233bd4058723801f515`. Release công khai đủ hai ZIP, hai checksum và `manifest-win-x64.json`; cả hai ZIP đã được tải lại từ GitHub và khớp hash/kích thước. Bootstrap được chuyển sang `v0.3.6` sau bước xác minh. Lệnh `irm` nay nhận giao diện Public chỉ có Install Software và Driver. Không chạy Install, Optimize hoặc Windows Setting thật trong quá trình phát hành.

## Phạm vi giao diện Public — local net48 và net10

Public nay chỉ hiển thị hai thẻ **Install Software** và **Driver**. Điều hướng lẫn lệnh của Optimize Windows và Setting đều bị chặn trong ViewModel, kể cả khi cố gán trang trực tiếp; Developer vẫn có đủ bốn thẻ. Build sạch 0 warning/0 error trên cả hai target; net48 đạt 71 logic tests và 6 WPF checks, net10 đạt 53 logic tests và 5 WPF checks. Hai output Public trong `artifacts/public-nav-review-20260908-094220` đều xác thực cấu hình với mã 0; Public net48 smoke test mở/đóng mã 0 và preview thật đã được mở để người dùng kiểm tra. Developer net48 cũng đã được build/mở bằng `Run-Developer.ps1`. Không chạy cài đặt, Optimize hoặc Windows Setting thật. Source đã commit/push lên nhánh `main`; chưa tạo Release hoặc upload artifact.

## Smart Skip Install Software — local net48

Install net48 đã chuyển sang detector ba trạng thái `Installed`, `NotInstalled`, `Unknown`. Mỗi lượt đọc inventory Registry một lần cho bước trước tải và một lần dùng chung trước khi launch; quyết định từng app có lý do/bằng chứng trong `%LocalAppData%\MiniApps\InstallLogs`. Unknown không tải hoặc chạy installer và được tính là lỗi để người dùng có thể thử lại. Matcher đã bao phủ đủ 12 app mặc định; Zoom Outlook Plugin, Chrome Beta, WPS updater và K-Lite helper không còn được nhận nhầm là app chính. ID built-in bị đổi sang tên sản phẩm khác không âm thầm dùng rule cũ.

Kiểm tra read-only trên máy phát triển xác nhận Office `HomeStudent2021Retail`, Visual C++ v14.51 x64 và x86 đều là Installed; EVKey, Chrome, K-Lite, Telegram, UltraViewer, WinRAR, Zalo và Zoom cũng được nhận đúng; WPS là NotInstalled; không có lỗi đọc inventory. Build net48 sạch 0 warning/0 error; 71 logic tests và 6 WPF checks đạt. Không chạy bộ cài hay Windows Setting thật; net10 chưa build/test; chưa commit/push/phát hành.

## Sửa lỗi chờ kéo dài và protocol Optimize — local net48

Helper hai lane nay dùng `System.Diagnostics.Process` và đọc stdout/stderr theo dòng hoàn chỉnh bằng `ReadLineAsync`. Mã thoát thật của từng lane được giữ nguyên; protocol bị ghi thành nhiều nhịp và đoạn cuối không có newline không còn bị mất hoặc phát sớm. Nếu lane sau không khởi động được, mọi lane đã khởi động vẫn được drain và chờ kết thúc trước khi trả lỗi. Lane Features gọi `-RunDefaults`, sau đó chỉ tách `RemoveApps/Apps`, nên tiếp tục dùng lọc `MinVersion`, `MaxVersion` và Modern Standby của upstream.

WinGet trong chế độ MiniApps đã chuyển khỏi runspace `Stop()` sang worker OS process riêng. Worker dùng chung mutex package với Appx, ghi JSON/log và có deadline 120 giây; quá hạn được báo `OVERDUE`, worker không bị kill và Install/Optimize mới bị chặn cho tới khi mutex được nhả. Nhận diện cài sẵn Office nay yêu cầu Office 2024; VC++ yêu cầu đúng dòng 2015–2022 và kiến trúc, tránh bỏ qua bộ cài vì một phiên bản cũ hoặc Microsoft 365 khác đang tồn tại. Phạm vi nhóm giao diện mới được giữ ở net48; net10 chưa build/test.

Đã xác minh: build net48 sạch 0 warning/0 error; 65 logic tests và 6 WPF checks; bundle từ output đạt 74 kiểm tra; fixture parallel, archive, Appx worker, WinGet worker, timeout và bridge đều đạt. Tất cả fixture đều không chạy Optimize/cài đặt hoặc thay đổi cấu hình Windows thật. Chưa push/phát hành; chưa chạy Optimize thật sau sửa.

## MiniApps Debloat Windows 11 — bản viết lại tích hợp local

Optimize net48 nay chạy `RemoveApps` và 16 tính năng cùng lúc trong hai engine lane của một lượt. Giao diện chia thành **Debloatware** (gỡ phần mềm) và **Optimize Windows** (tùy chỉnh/tắt tính năng), nhưng vẫn dùng một nút chạy chung. Lane Features chịu trách nhiệm Registry backup; lane RemoveApps không tạo backup trùng. Output được hợp nhất thành 17 tác vụ và diagnostics giữ hai PID engine. Các thao tác Appx phụ trong cả hai lane vẫn dùng chung mutex, nên chỉnh Registry/Windows chạy song song nhưng App Repository chỉ có một writer. Build net48 sạch; 65 logic tests, 6 WPF checks, 67 kiểm tra bundle, fixture hai lane, timeout, Appx worker, bridge và bootstrap đều đạt. Đây là thay đổi local, chưa chạy Optimize thật sau sửa, chưa push/phát hành.

## Optimize local — rà soát 3 tầng

Tầng 1 đã triển khai và xác nhận cho net48: bản sao engine ghi Registry backup trực tiếp vào `%LocalAppData%\MiniApps\OptimizeLogs\<lượt>\Backups`; runtime ghi PID runner/engine/Appx worker, timestamps, stage, exit code và số mục thành công/bỏ qua/lỗi/quá hạn/chưa chạy. Build net48 sạch 0 cảnh báo/0 lỗi; 64 logic tests, 6 kiểm tra WPF và 60 kiểm tra bundle từ output đạt. Fixture monitor xác nhận không kill worker, fixture Appx pattern không khớp xác nhận JSON/mutex, fixture bridge giữ `OVERDUE`, và bootstrap đều đạt. Các fixture không gỡ ứng dụng hoặc đổi thiết lập Windows. Bản sửa mới chưa được chạy Optimize thật; bằng chứng nguyên nhân đến từ lượt người dùng chạy trước khi sửa tại log `20260907-141430-db784a9ebe2f41cf854702e18143b841`.

Tầng 2 đã đạt trên output Public net48 mới `artifacts/review3-public-clean-net48-20260907-111506`: package/resource và helper Optimize đạt 46 kiểm tra; cấu hình đóng gói được xác thực với mã 0; bản Public đủ 365 tệp được sao chép vào đường dẫn lồng kiểu phiên `irm`, mở cửa sổ WPF thật bằng cờ smoke an toàn rồi tự đóng sau sự kiện Loaded với mã 0 và không để lại tiến trình. Manifest Admin được xác nhận qua UAC; bản Developer thật đã mở từ output riêng của `Run-Developer.ps1`. Tầng 3 vẫn chờ người dùng bấm Optimize trên máy kiểm thử và cung cấp thư mục log của lượt chạy; AI không tự chạy thay đổi hệ thống.

## Quy trình local — Developer thật

Theo yêu cầu người dùng, sau thay đổi sẽ build/mở bản Developer net48 thật bằng `Run-Developer.ps1`, không dùng Developer preview làm bước chạy mặc định. Script tạo output riêng, khởi tạo/đọc `ReleaseConfig` rồi mở MiniApps không có cờ preview. Các nút Install và Optimize vì vậy có thể chạy thật; AI chỉ mở ứng dụng, người dùng trực tiếp quyết định thao tác. `Preview.ps1` được giữ cho kiểm thử mô phỏng. Chưa push thay đổi quy trình này.

MiniApps local hiện yêu cầu quyền Administrator ngay khi khởi động qua application manifest `requireAdministrator`; Windows hiển thị UAC cho cả Developer và Public. Thay đổi quyền này mới ở local, chưa push/phát hành.

## Phát hành v0.3.5 — 2026-09-07

Đã phát hành theo yêu cầu test máy khách. Source release: `065fb8af489dae37a00348f5fc0acb2fe7e68adf`. ZIP net48: 859705 bytes, SHA-256 `52a57a5c5ad74a91e2e4c454d7f23c06d7ff643e3cf255afb9e22672abb3e49f`. Đã xác minh digest/size hai ZIP trên GitHub, tải lại net48 kiểm tra hash và 37 điều kiện của engine/assembly. Bootstrap chuyển sang v0.3.5 sau khi asset được xác minh. Máy khách trước đó xác nhận v0.3.4 thoát mã 2 vì WPF tìm `app\mainwindow.xaml`. Nguyên nhân là cây Win11Debloat đã được khai báo thành MSBuild `Content`; `Schemas\MainWindow.xaml` của upstream sinh `AssemblyAssociatedContentFileAttribute("mainwindow.xaml")` và che resource giao diện đã biên dịch của MiniApps. Bản sửa net48 chuyển cây vendor sang `None` có copy-to-output, nên XAML upstream vẫn có tại `Engine\Debloat\Schemas` dưới dạng dữ liệu nhưng không còn metadata WPF. Assembly Public đã kiểm tra không đăng ký external `mainwindow.xaml`, đồng thời vẫn có `MiniApps.g.resources/mainwindow.baml`.

Khởi động net48 nay ghi `Exception.ToString()` cùng thông tin runtime vào `%LocalAppData%\MiniApps\StartupLogs`; hộp thoại dùng tiêu đề “MiniApps - lỗi khởi động” và hiển thị đường dẫn log. Headless vẫn không mở hộp thoại. Build net48 sạch; 60 logic tests và 6 kiểm tra WPF đạt. Fixture engine/bridge/bootstrap đạt. Gói Public đã mở và đóng sạch từ output ngắn và đường dẫn lồng kiểu phiên `irm`; không chạy Optimize/cài đặt/thiết lập Windows thật. Developer preview được mở sau kiểm tra. Net10 giữ nguyên byte của v0.3.0, không rebuild/test.

Cảnh báo session cũ v0.3.3 được giữ xử lý bảo thủ: bootstrap không kill tiến trình và không xóa phiên khi Windows báo thư mục còn được sử dụng. Phiên báo lỗi thực tế đã được giải phóng ở lần kiểm tra sau; không còn đủ bằng chứng về PID/handle để sửa vòng đời tiến trình một cách an toàn. Backup Registry nằm ngoài cây dọn vẫn được bảo toàn. Cần thu PID/command line hoặc handle tại thời điểm tái hiện nếu cảnh báo tiếp tục xuất hiện.

## Phát hành v0.3.4 — 2026-09-07

Đã phát hành theo yêu cầu test máy khách. Source release: `0a4869f0ce846fb5cccddc048dc86b3f515267e9`. ZIP net48: 862076 bytes, SHA-256 `0164182cc2e48861be789f2a3704004a6a8580d4ea5f1e3ae7329fe9cc46165d`. Đã xác minh digest/size của hai ZIP trên GitHub, tải lại net48 kiểm tra hash, engine và MIT LICENSE; bootstrap chuyển sang v0.3.4 sau xác minh. Optimize **net48** đã chuyển từ tải và giải nén ZIP GitHub lúc chạy sang source vendor của commit `6012b02ea282f23ea943946206762fd430025c6f` tại `ThirdParty/Win11Debloat`. Build net48 đóng gói cây runtime cần thiết vào `Engine/Debloat`; mỗi lượt tạo một bản sao làm việc ngắn, riêng trước khi bỏ duy nhất `CreateRestorePoint` khỏi Default và gắn task bridge. Cây vendor giữ MIT `LICENSE` và `UPSTREAM.md`; không sửa source upstream. Luồng active chỉ có Preparing → Applying, không còn Downloading hay gọi GitHub cho source upstream.

Backup Registry và log bền vững vẫn dùng thư mục phiên `%LocalAppData%\MiniApps\OptimizeLogs`; nếu không chuyển được backup thì Optimize báo lỗi và service không xóa cây làm việc còn chứa backup. `Test-OptimizeBundledEngine.ps1` đạt 31 kiểm tra: provenance/license, dependency, không còn download active, profile khác Default đúng một mục, copy riêng, đường dẫn legacy, lỗi bundle thiếu, parse entrypoint sau khi bắn bridge và kiểm tra output Public. Build net48 sạch; 59 logic tests, 6 WPF checks và fixture bridge đều đạt. Preview Developer net48 đã mở từ `artifacts/developer-net48-bundled-final-20260907-092919-85a073e4`; không chạy Optimize thật. ZIP Public tăng 365193 bytes so với v0.3.3. Net10 giữ nguyên byte của v0.3.0, không rebuild/test. Cần test thực trên VM Windows 10/11 sau phát hành.

## Phát hành v0.3.3 — 2026-09-07

Đã phát hành bản sửa đường dẫn giải nén Optimize cho net48 theo yêu cầu test irm. Source release: `d13d50a02f7bd7c8cf8c30054c31cbff23cdab45`. ZIP net48: 496883 bytes, SHA-256 `feed9f0b4e956c9e992c37ddb4ed572030e059c9aafe5fef2fbd1f99e5a2ea78`. Đã xác minh digest/size cả hai ZIP trên GitHub và tải lại net48 kiểm tra hash. Bootstrap chuyển sang v0.3.3 sau xác minh asset. Net10 giữ nguyên byte của v0.3.0, không rebuild. Fixture archive, bridge và bootstrap đạt; chưa chạy Optimize thật trên máy phát triển. Đoạn “bản sửa local tiếp theo” dưới đây mô tả lịch sử trước khi phát hành v0.3.3.

## Bản sửa v0.3.2 — 2026-09-07

Đã phát hành: source `3025994bf376f87e13532f0bc264e957121ecb32`, ZIP net48 494415 bytes, SHA-256 `6541d26aca87cdf292297a738a8687c9ed9f4db546ef8839499c21aa11cf1100`. Đã xác minh metadata hash/size của asset trên GitHub và tải lại net48. Bootstrap chuyển sang v0.3.2 sau khi asset có sẵn. Các mục release bên dưới là lịch sử.

Theo yêu cầu người dùng: Optimize net48 bỏ SHA-256 của ZIP Win11Debloat, giữ commit HTTPS cố định và các bước chuẩn bị bắt buộc. Luồng UI/preview là Downloading → Preparing → Applying. Log tạo theo phiên trước khi tải; lỗi PowerShell gốc được lưu và hiển thị. Bootstrap vẫn kiểm tra SHA-256/kích thước gói MiniApps. Build net48 sạch, 59 logic tests và 6 kiểm tra WPF đạt, bao gồm lỗi chuẩn bị và retry; fixture bridge và cú pháp PowerShell đạt. Không chạy tối ưu thật trên máy phát triển. Net10 tiếp tục dùng nguyên artifact v0.3.0, không rebuild.

Bản sửa local tiếp theo xử lý lỗi `Expand-Archive` của lượt `irm` khi đường dẫn file upstream đạt 270 ký tự: service dùng leaf phiên ngắn `o-<12 ký tự>` và helper mới giải nén an toàn, bỏ root ZIP đã ghim vào `src`. Helper preflight root/traversal/containment/tên trùng hoặc không hợp lệ/giới hạn đường dẫn trước khi ghi, staging toàn bộ rồi mới đưa vào đích và không để lỗi cleanup che lỗi gốc. Fixture nested giảm đường dẫn tái hiện từ 286 xuống 204 ký tự; ZIP thật đã ghim giải nén extraction-only 403 file, dài nhất 221 ký tự. Build net48 sạch, 59 logic tests, 6 kiểm tra WPF, fixture archive và bridge đều đạt; không chạy entry point Win11Debloat hay thay đổi hệ thống. Chưa build/test net10, chưa commit, push hoặc phát hành.

## Phát hành v0.3.1

Đã phát hành v0.3.1 theo yêu cầu thử `irm`: net48 gồm Optimize dạng thông báo có hoạt ảnh, task bridge và tài liệu bàn giao. Source release: `904e372f4115ffb222fd097be5f364d2fee089cb`. ZIP net48: 494015 bytes, SHA-256 `95e02e63907227e0b0f870cca68c9b7be4b31506216e1ed6f03f6b1d7ebdfaf9`. Gói net10 tái sử dụng nguyên byte từ v0.3.0 (SHA-256 `828341fa007fb89f1d246758eaa9dae93e835b283464c2d5068be785b3e42b17`), không build lại. Remote asset size/hash và tải lại ZIP net48 đã xác minh. Bootstrap chuyển sang v0.3.1 sau khi asset tồn tại. Các thông tin v0.3.0 bên dưới là lịch sử trước lần phát hành này.

Cập nhật: 2026-09-06. Đây là snapshot; kiểm tra Git và code khi tiếp tục, không coi thông tin release dưới đây là vĩnh viễn.

## Mã nguồn và bản phát hành

- Repo WPF: `https://github.com/mson-ssh/miniapp.git`, nhánh `main`.
- Push code gần nhất đã xác minh: `db9479e8a67c964a7ba123a6a9ed52902f77b29b` — Optimize Default net48 không restore point, khóa nút Install và tài liệu liên quan.
- Version source: 0.3.1 đang phát triển. Bootstrap vẫn trỏ Release **v0.3.0**. Push source không làm bản `irm` nhận tính năng mới.
- Optimize mới được giới hạn net48; chưa đồng bộ/build/phát hành net10 cho thay đổi đó. Service thực thi và preview được biên dịch dưới `NET48`; XAML và hợp đồng trạng thái vẫn nằm trong project đa target nên cần chủ động thiết kế bản net10 khi được yêu cầu. Một số thay đổi Install/preview dùng source chung đã có trước quy tắc tách runtime mới; chưa có hai nhánh source độc lập.
- Bộ tài liệu `AGENTS.md` và `docs/` này được tạo cục bộ sau lần push trên; chưa push trong tác vụ tài liệu.

## Đã làm và đã kiểm tra

Điều chỉnh giao diện mới nhất: hàng Optimize dạng thẻ thông báo gọn, chia rõ Debloatware và Optimize Windows, dùng chữ trạng thái cùng nền dịu.

Install một nút, tiến trình ba cột, Windows Setting mở rộng, tự nhận diện app đã cài; nút khóa sau lượt hoàn tất. Setting chỉ Developer, hai nhóm CRUD cấu hình. Driver đọc thông tin và dẫn URL hãng. Optimize net48 hiển thị trạng thái thật của 17 tác vụ trong hai vùng; DONE/SKIP/ERROR/OVERDUE và trường hợp thiếu kết quả được giữ đúng nghĩa. Giao diện không hiển thị restore point hoặc backup Registry, nhưng engine vẫn bỏ restore point và bảo toàn backup. Thành công khóa nút trong phiên; lỗi cho thử lại. Developer preview mô phỏng đúng vòng đời và không thay đổi hệ thống.

Lần kiểm tra tính năng gần nhất: build net48 sạch; 59 kiểm thử logic và 6 kiểm tra WPF đều đạt, gồm xác nhận hàng vừa hoàn tất đang có animation khi chuyển xuống cuối. Fixture PowerShell xác minh START/DONE/ERROR/SKIP và bảo toàn giá trị trả về của upstream; script upstream sau khi gắn bridge đã được parse cú pháp, không thực thi. Không build/test net10 hoặc chạy cài đặt/Optimize thật trên máy phát triển.

## Việc cần lưu ý khi tiếp tục

- Test Optimize trên VM Windows 10/11 đúng edition/build: quyền Admin, thiếu mạng/hash sai, lỗi upstream, backup tồn tại sau cleanup, đăng xuất/restart, tác vụ không hỗ trợ.
- Tiến độ Optimize thật chỉ thay đổi theo protocol `MINIAPPS_TASK_JSON` của 17 tác vụ; không nội suy bằng timer. Script bridge được kiểm tra bằng `Test-OptimizeTaskBridge.ps1`, nhưng thay đổi hệ thống thực vẫn chưa được chạy trên máy phát triển.
- Install và Optimize dùng chung trạng thái bận/mutex: có thể chuyển thẻ Install/Optimize/Driver, nhưng không khởi chạy cả hai luồng đồng thời. Setting bị khóa khi chạy. Nút dừng hàng đợi chỉ hiện cho Install và không hủy Optimize; cần chờ process kết thúc.
- `RuntimeLabel` còn chuỗi version 0.3.0 trong khi csproj là 0.3.1; cần xử lý ở tác vụ tính năng/version tiếp theo, không coi nhãn UI là bằng chứng release.
- README WPF còn đoạn mô tả Debloat script cũ và Optimize preview; tham khảo tài liệu Optimize mới cùng code khi có khác biệt.
- Nếu đóng gói net48 riêng mà giữ net10, cần làm rõ quy trình manifest/asset; `Publish.ps1` hiện build cả hai.

## Cách cập nhật snapshot

Sau mỗi thay đổi đáng kể, ghi phạm vi target, file/luồng thay đổi, kiểm thử thực sự chạy và giới hạn. Sau push ghi commit đã xác minh; sau release ghi version/asset và bootstrap thực tế. Không viết “đã phát hành” chỉ vì build hoặc commit thành công.
