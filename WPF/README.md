# MiniApps Desktop — WPF v0.3.0 / dual runtime

Bản nâng cấp độc lập trong `WPF/`; không sửa hoặc thực thi `../Setup.ps1`.

## Giao diện

- C# / XAML, MVVM, WPF control chuẩn được style nhẹ. Không WebView2, không font icon, không thư viện UI bên ngoài.
- Nền sáng dịu `#F3F4F6`, sidebar `#ECEEF1`, panel `#FAFAFB`, xanh nhấn `#3B6EA8`.
- Sidebar Developer: **Install app**, **Optimize Windows**, **Driver**, **Setting**. Bản Public qua `irm` chỉ có **Install app**, **Optimize Windows** và **Driver**.
- Install app: một nút Cài đặt mở hộp thoại chỉ có “Bạn muốn sử dụng ứng dụng nào:” với **Office 2024 / WPS / Cancel** (cả bản thật và preview). Cancel, Esc hoặc đóng hộp thoại không tải/chạy tác vụ nào. Chọn Office/WPS bắt đầu toàn bộ catalog tương ứng, không có checkbox ứng dụng hay xác nhận thừa lần hai. Danh sách chỉ hiện sau khi bắt đầu dưới dạng lưới ba cột; mỗi bản ghi có tên, trạng thái và tiến trình tải/cài. Bộ cài không có % dùng thanh chạy không xác định. Log trong phiên.
- Driver: đọc Host, hãng, model và serial bằng CIM của Windows (registry làm phương án dự phòng). Serial có thể sao chép. Tên hãng được chuẩn hóa và ánh xạ tới URL HTTPS hỗ trợ chính thức của Dell, HP, Lenovo, ASUS, Acer, MSI, Microsoft Surface hoặc Samsung; hãng chưa biết không được tự suy đoán URL. Nút mở trang do người dùng chủ động bấm; MiniApps chưa tự tải/cài driver và không gửi thông tin máy lên dịch vụ riêng.
- Install app không hiển thị danh sách hoặc checkbox thiết lập Windows trước khi chạy. Toàn bộ thiết lập đã lưu chạy mặc định sau xác nhận Office/WPS, **ngoại trừ Debloat** đã được tách khỏi luồng Install. Bộ lọc nhận diện Debloat theo cả ID và Action để cấu hình lưu cũ cũng không đưa tác vụ này vào lượt cài. Trong tiến trình, các tác vụ còn lại được tổng hợp thành một bản ghi **Windows Setting**; bấm vào bản ghi để xổ/thu danh sách từng thiết lập, trạng thái và phần trăm riêng. PowerShell không cung cấp tiến độ nội bộ nên mỗi mục chuyển từ 0% sang 100% khi kết thúc. Cancel không chạy gì. Quản lý danh sách ở Setting → Các thiết lập; lưu cấu hình chưa chạy tác vụ.
- Optimize Windows hiện là **preview giao diện** với bốn nhóm: gỡ ứng dụng thừa, quyền riêng tư & quảng cáo, Copilot & AI, giao diện Windows. Nút Tối ưu Windows chỉ mô phỏng trạng thái và tiến trình trong bộ nhớ; không gọi PowerShell/DeploymentService, không tải công cụ, không sửa Registry và không gỡ ứng dụng.
- Setting chỉ có trong edition Developer và có hai tab **Ứng dụng** và **Thiết lập Windows**, hiển thị số lượng mục, tìm kiếm riêng và hỗ trợ thêm/sửa/xóa. Danh sách nằm bên trái, biểu mẫu chỉnh sửa nằm bên phải. Trạng thái **Đã lưu/Chưa lưu thay đổi**, **Hủy thay đổi**, **Lưu thay đổi** và `Ctrl+S` chỉ tác động tab đang mở; đổi tab vẫn giữ bản nháp và bản nháp chưa lưu không ảnh hưởng lượt cài.
- Ứng dụng: giao diện Setting chỉ gồm tên, URL và tham số cài đặt. MiniApps tự nhận diện ứng dụng đã cài từ tên hiển thị trong Windows và quy tắc tích hợp cho catalog mặc định; người dùng không phải cấu hình regex. SHA-256 và nhóm Office/WPS hiện có được giữ trong cấu hình nội bộ để bảo toàn catalog. Ứng dụng thêm mới là ứng dụng thông thường. Xóa khỏi danh sách không gỡ app đã cài. Có thể lưu danh sách rỗng; xóa Office/WPS nghĩa là suite đó không còn gói nào trong lượt chạy.
- Thiết lập Windows: tên, mô tả tác động và trình soạn **Câu lệnh PowerShell** lớn, dùng font Consolas. Debloat cũ hiển thị cảnh báo đã tách khỏi Install app và Optimize còn ở preview. Engine chạy đúng nội dung Script đã lưu, không chuyển sang tác vụ tích hợp khác. Xóa không hoàn tác hệ thống. Mọi mục đã lưu đều có trong lượt tiếp theo; không có trạng thái chọn/bỏ chọn từng lượt. Cấu hình cũ chỉ lưu Action được bổ sung câu lệnh mặc định trong bộ nhớ; chỉ ghi xuống đĩa khi Lưu. Nội dung script đã sửa được giữ nguyên.
- Cấu hình phát hành nằm trong `WPF/ReleaseConfig/apps.json` và `windows.json`. Developer đọc/ghi hai file bằng thao tác lưu atomic; Public chỉ đọc bản sao nằm trong gói đã xác minh và không đọc cấu hình `%LocalAppData%` của máy khách. File lưu `SchemaVersion`, danh sách cấu hình và ID mặc định đã xóa; cấu hình thiếu, lỗi hoặc schema mới hơn làm Public dừng trước khi cài.
- Script tùy chỉnh là **mã thực thi tin cậy do người dùng cấu hình**, chạy với quyền Admin trong Windows PowerShell 5.1 sau lựa chọn và xác nhận Office/WPS. Không chạy khi lưu hoặc preview. Không dùng Read-Host/prompt; kiểm tra `$LASTEXITCODE` sau lệnh native và dùng `throw`/`exit` khác 0 khi lỗi. Script ghi UTF-8 BOM trong thư mục phiên và chạy như file, không ghép vào chuỗi lệnh. Không tự phân tích tác động hay bảo đảm rollback của script người dùng; script lỗi cú pháp sẽ báo khi chạy.

## Bản Developer và Public

> Trạng thái: đã triển khai từ `v0.3.0`.

- **Developer** chỉ dùng trên máy phát triển, build `net48` với `MiniAppsEdition=Developer`. Sidebar có đủ **Install app**, **Optimize Windows**, **Driver** và **Setting**. Setting tiếp tục quản lý hai nhóm **Ứng dụng** và **Thiết lập Windows**.
- **Public** là bản người dùng nhận qua `irm`, build với `MiniAppsEdition=Public`. Sidebar chỉ có **Install app**, **Optimize Windows** và **Driver**. ViewModel cũng từ chối điều hướng tới Setting và vô hiệu hóa toàn bộ lệnh thêm/sửa/xóa/lưu; bản Public không có tham số dòng lệnh để bật lại Setting.
- Cấu hình phát hành chuẩn nằm trong `WPF/ReleaseConfig/apps.json` và `WPF/ReleaseConfig/windows.json`. Bản Developer đọc/ghi hai file này; bản Public chỉ đọc bản sao đã được đóng gói cùng ứng dụng và không dùng cấu hình `%LocalAppData%` của máy khách.
- `Preview.ps1 -Developer` build/mở đúng bản net48 Developer và truyền đường dẫn cấu hình trong workspace. `Publish.ps1` luôn build Public, kiểm tra schema, ID trùng, URL HTTPS, script rỗng và SHA-256 trước khi tạo gói; cấu hình thiếu hoặc không hợp lệ làm publish thất bại.
- File cấu hình được ghi atomic. Bản Public dừng trước khi cài nếu cấu hình đóng gói thiếu hoặc hỏng, thay vì âm thầm dùng catalog khác.
- Luồng chỉnh cấu hình được triển khai và kiểm thử trên `net48`. Mã nguồn dual-runtime vẫn dùng chung; cả gói net48 và fallback net10 phát hành đều được build dưới edition Public.

Điều kiện nghiệm thu: Developer có bốn mục sidebar và lưu được cấu hình; Public chỉ có ba mục, không thể truy cập Setting bằng UI hoặc bằng cách gán trang; Public không tạo/sửa cấu hình trong `%LocalAppData%`; cấu hình Developer đã lưu xuất hiện đúng trong bản Public; các test Install app, tiến trình, Driver và Optimize hiện tại vẫn đạt.

## Build và xem thử

Máy phát triển dùng .NET SDK 10 để build chung source cho `net48` và `net10.0-windows`. Máy khách dùng **Windows 10 1809/build 17763 trở lên hoặc Windows 11**. Bootstrap đọc .NET Framework trong Registry: máy có Release `>= 528040` tải gói `net48` nhỏ; máy thiếu Framework 4.8 tải gói .NET 10 self-contained. Fallback mang runtime trong thư mục phiên và không cài runtime vào Windows.

```powershell
dotnet build WPF/MiniApps/MiniApps.csproj -c Release
./WPF/Publish.ps1
./WPF/Preview.ps1 -Target net48
./WPF/Preview.ps1 -Target net10
./WPF/Preview.ps1 -Target net48 -Developer # mở Setting và WPF/ReleaseConfig
dotnet build WPF/MiniApps.Tests -c Release
./WPF/MiniApps.Tests/bin/Release/net48/MiniApps.Tests.exe
dotnet ./WPF/MiniApps.Tests/bin/Release/net10.0-windows/MiniApps.Tests.dll
powershell -NoProfile -ExecutionPolicy Bypass -File WPF/Test-Bootstrap.ps1
```

`--preview` sử dụng dữ liệu giả cho lượt cài, không mạng, không chạy installer, không áp dụng thiết lập Windows và không lưu Setting xuống đĩa. Nút xuất log chỉ ghi khi người dùng chọn file.

Mở `MiniApps.exe` trực tiếp không tự xin quyền Admin, cho phép xem giao diện an toàn. Nút Cài đặt yêu cầu Admin; bootstrap sản xuất sẽ xin UAC trước khi chạy. Chạy UAC bằng tài khoản khác sẽ dùng profile/config của tài khoản đó.

## Đóng gói / phát hành

```powershell
./WPF/Publish.ps1 -Runtime win-x64 -Version 0.3.0
# Nếu dotnet không có trong PATH:
./WPF/Publish.ps1 -Dotnet 'C:\path\to\dotnet.exe'
```

Đầu ra trong `WPF/artifacts/`:

- `MiniApps-net48-win-x64.zip` và `.sha256`: gói nhỏ cho máy có .NET Framework 4.8+.
- `MiniApps-net10-win-x64.zip` và `.sha256`: gói self-contained cho máy thiếu Framework 4.8.
- `manifest-win-x64.json`: schema 2 chứa đúng hai asset, target, kiến trúc, URL release cố định, kích thước và checksum.

Năm file dành cho cùng GitHub Release **v0.3.0** trong `mson-ssh/miniapp`: hai ZIP, hai checksum và một manifest. Bản đầu chỉ hỗ trợ x64; bootstrap báo rõ với x86/ARM64. Script không tự upload/push/release. Gói hiện chưa ký Authenticode; SHA-256 chống sai/hỏng nội dung, không thay thế chữ ký của nhà phát hành.

Sau khi source bootstrap và release assets đã được phát hành, bootstrap dùng URL release `v0.3.0` cố định để manifest và các gói luôn thuộc cùng một phiên bản:

```powershell
irm https://raw.githubusercontent.com/mson-ssh/miniapp/main/WPF/bootstrap.ps1 | iex
```

**Không phát lệnh này cho khách trước khi assets tồn tại.** Khi đổi repo, sửa ReleaseBase và kiểm tra allowlist URL manifest trong bootstrap, cùng URL xuất ra trong Publish.ps1.

Baseline triển khai: Windows 10 1809/build 17763 trở lên hoặc Windows 11 x64. Có Framework 4.8 thì dùng net48; thiếu Framework thì dùng net10 self-contained. Microsoft chỉ còn hỗ trợ .NET 10 trên các edition Windows 10 1809 còn nằm trong ma trận hỗ trợ tương ứng (đặc biệt LTSC); không coi mọi edition 1809 đã hết vòng đời là được chứng nhận. Cần kiểm thử gói fallback trên VM đúng edition trước khi phát hành cho máy khách. Từng thiết lập tích hợp vẫn kiểm tra build riêng; tác vụ không tương thích báo **Bỏ qua · Windows không hỗ trợ** và không làm dừng tác vụ khác.

## Luồng bootstrap và dọn dẹp

1. Hàm bootstrap tự nâng quyền bằng chính nội dung đã tải, không tải lại code ở mỗi trang.
2. Tạo `%TEMP%\MiniApps\<GUID>`, marker sở hữu và khóa file của phiên.
3. Đọc Framework Release và chọn target trước khi tải. Tải manifest schema 2, kiểm tra target/kiến trúc/URL/kích thước/checksum rồi tải đúng ZIP từ URL release theo phiên bản. Không chạy nếu kích thước hoặc SHA-256 sai.
4. Giải nén, chuyển TEMP/TMP của tiến trình con vào thư mục phiên, mở WPF; chờ cả cây tiến trình bằng `Start-Process -Wait`.
5. Thoát xong xóa đúng thư mục GUID có marker, từ chối xóa reparse point/junction. Không đụng app đã cài hay cấu hình người dùng.
6. Lần chạy sau dọn phiên cũ đã nhả khóa và không có dấu hiệu đang cài. Phiên crash còn marker `installing` được giữ thận trọng để không xóa file tiến trình con còn dùng; cần kiểm tra thủ công trước khi xóa.

Nếu mất điện, kill bootstrap, policy chặn hoặc file bị khóa: không thể bảo đảm xóa ngay. MiniApps dọn thư mục do nó quản lý, **không cam kết xóa cache/log mà installer bên thứ ba tự ghi ngoài thư mục phiên**. Không quét xóa bộ nhớ đệm toàn Windows.

## Điều phối và ranh giới an toàn

- Không đặt giới hạn số lượt tải/cài ứng dụng: toàn bộ danh sách bắt đầu cùng lúc và mỗi EXE chạy ngay sau khi tải, kiểm tra xong. Tất cả Windows Setting cũng bắt đầu đồng thời với lượt ứng dụng. File MSI dùng hàng đợi riêng chỉ cài một gói mỗi lần do giới hạn của Windows Installer; EXE và Windows Setting vẫn chạy cùng MSI. Mã 1618 được thử lại tối đa 3 lần sau 5/10/15 giây; hết lượt báo lỗi.
- Download có giới hạn đình trệ 90 giây, tối đa 3 lần cùng URL, kiểm tra định dạng EXE/MSI và SHA-256 nếu khai báo. URL mặc định kế thừa từ catalog cũ; cần kiểm chứng artifact và bổ sung hash trước phát hành.
- Registry Smart Skip + đường dẫn EVKey/Zalo/Telegram. Đây là kiểm tra có app, **không phải kiểm tra đã ở phiên bản mới nhất**.
- Installer chạy qua Windows PowerShell có sẵn, không cần PowerShell 7. Tham số được quote thành literal, không `iex` URL hay argument của app.
- Dừng hàng đợi hủy download/tác vụ chưa chạy; không kill installer đang chạy. Khóa đóng cửa sổ cho tới khi bộ cài kết thúc. Sau 30 phút ghi cảnh báo nhưng vẫn chờ, không tuyên bố thành công giả hoặc phá cài đặt.
- Named mutex ngăn hai cửa sổ MiniApps cùng cài đặt. Không điều phối được installer do phần mềm khác trên máy tự chạy.
- Exit 0: xong; 3010/1641: cần reboot; mã khác: thất bại. Reboot do bộ cài tự thực hiện có thể gián đoạn việc dọn dẹp.
- Chọn WPS không tự gỡ Office; không tự refresh Winget hoặc chạy tác vụ trước xác nhận.
- Mặc định chạy các thiết lập còn trong cấu hình: desktop icons, timezone, DNS, Fast Startup, power timeout, password expiry, Winget và tải info.exe. Debloatware không còn được chạy từ Install app. Desktop có thể cần refresh để thấy biểu tượng mới.
- Info.exe: tải bản dựng từ R2 về Desktop qua file tạm, kiểm tra header `MZ` trước khi ghi đè và dọn file tạm sau đó. Tác vụ không tự mở info.exe và không dùng nhánh biên dịch ps2exe của CLI cũ.
- Winget (update): cập nhật/cài chính WinGet và dependency App Installer từ release ổn định Microsoft, kiểm tra SHA-256 metadata release và phiên bản sau cài; không chạy `winget upgrade --all`. Có thể tải hàng trăm MB; app MiniApps vẫn không cần runtime cài ngoài.
- Debloatware: Win11Debloat 2026.08.24, `-RunDefaults -Silent -SkipExplorerRestart`. Gỡ app cài sẵn và đổi thiết lập theo profile upstream; xem mô tả tác động và câu lệnh trong Setting trước khi cài. Hộp thoại Office/WPS không chứa cảnh báo bổ sung. Tải ZIP tag chính thức vào TEMP phiên, không dùng `irm | iex` cho tác vụ này. Cần đăng xuất/restart; không hứa hoàn tác tự động. Chưa có hash pin cho ZIP Debloat, nguồn tin cậy là HTTPS GitHub tag của upstream.
- Winget và các thiết lập không phải Debloat bắt đầu cùng lượt ứng dụng nếu còn trong danh sách đã lưu; tất cả được tính vào bản ghi tiến trình Windows Setting. Preview chỉ mô phỏng, không cập nhật WinGet hay thay đổi máy phát triển.
- Chưa chuyển chia ổ, BitLocker, tắt UAC/SMB signature, xóa System Restore từ bản CLI. Không đưa lại thao tác rủi ro vào app cài đặt một cách ngầm định.
- Các bộ cài tương tác vẫn mở cửa sổ riêng; gói SFX tự bật ứng dụng thường trú (đặc biệt EVKey) cần kiểm thử trên VM: engine chờ cây tiến trình nên có thể cần đóng ứng dụng được bộ cài bật lên.

## Cấu trúc

- `MiniApps/MainWindow.xaml`: bố cục, data binding và 4 trang.
- `MiniApps/App.xaml`: bảng màu và style chung.
- `MiniApps/ViewModels/MainViewModel.cs`: trạng thái, command và xác nhận lượt cài.
- `MiniApps/Models/Catalog.cs`: catalog và validation.
- `MiniApps/Services/SettingsStore.cs`: cấu hình JSON, lưu atomic.
- `MiniApps/Services/DeploymentService.cs`: download, kiểm tra file, Smart Skip và chạy process.
- `MiniApps/Models/WindowsSettingDefinition.cs`: câu lệnh mặc định, validation và định nghĩa thiết lập; script Winget/Debloat được nhúng từ `Scripts/` khi build. `Configure-Windows.ps1` còn giữ để tham khảo, engine không dùng để điều phối thiết lập.
- `MiniApps.Tests`: test harness không dependency ngoài; HTTP/installer giả và render trực tiếp visual tree.
- `Test-Bootstrap.ps1`: EXE fixture và tiến trình con để kiểm tra tải local–giải nén–chờ–dọn. Không cài app.

## Trước khi giao máy khách

Kiểm thử trên VM snapshot Windows 10/11 chưa cài .NET: UAC accept/cancel, mạng mất/chậm, URL sai/hash sai, Office tương tác, EVKey SFX, reboot, installer treo, dừng hàng đợi, hai phiên đồng thời, settings hỏng, tên user có dấu/khoảng trắng, DPI 100/125/150/200%, dọn sau crash. Không dùng máy đang có dữ liệu quan trọng để thử tác vụ hệ thống.

Tài liệu nền: [WPF](https://github.com/dotnet/wpf), [self-contained deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/), [WPF threading](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model).
