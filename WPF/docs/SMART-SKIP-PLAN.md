# Kế hoạch sửa Smart Skip — bàn giao cho Sol

Ngày: 2026-09-07. Trạng thái: đã triển khai và kiểm thử local cho WPF net48. Không thay cấu hình ReleaseConfig để thử, không chạy bộ cài hoặc Optimize thật.

Kết quả triển khai: detector mới nằm tại `MiniApps/Services/InstalledSoftwareDetector.net48.cs`; `DeploymentService` dùng snapshot đầu lượt, phân biệt Installed/NotInstalled/Unknown, ghi bằng chứng vào `%LocalAppData%\MiniApps\InstallLogs` và kiểm tra lại bằng một snapshot mới trước khi launch các installer đã tải. Matcher của 12 app mặc định đã được siết theo họ sản phẩm. Office dùng Click-to-Run SKU hoặc đăng ký suite hợp lệ; VC++ dùng runtime v14 riêng x64/x86. Unknown không tải hoặc chạy installer và làm lượt có thể thử lại.

## Kết luận kiểm tra

Điểm quyết định nằm ở `DeploymentService.RunAsync`: mỗi app gọi `IsInstalled` trước download; true phát `Đã cài · bỏ qua` và kết thúc app. `IsInstalled` kiểm tra ba đường executable đặc biệt rồi quét Uninstall HKLM/HKCU, cả Registry32/Registry64. Từng entry chỉ lấy DisplayName để so regex; không lấy bằng chứng sản phẩm, phiên bản, scope hoặc kiến trúc độc lập. Exception đọc Registry đi vào catch của tác vụ, làm app thất bại; không phải tự động cài khi exception.

Office và VC++ bị bỏ sót do lần sửa trước siết tên quá chặt. Dữ liệu thực xác nhận Office Home and Student 2021 (ClickToRun ProductReleaseIds HomeStudent2021Retail) và VC++ v14 Redistributable 14.51.36247 cho cả x64/x86; runtime keys có Installed=1. Ba mục đều không khớp rule hiện tại.

| App | Nhận diện hiện tại / quan sát | Việc cần sửa |
| --- | --- | --- |
| EVKey | File cố định C:\EVKey\EVKey64.exe hoặc EVKey.exe; regex EVKey. File x64 có trên máy | Không hardcode ổ C nếu đường triển khai theo SystemDrive; xác minh executable/product metadata khi có; không quét toàn ổ để tìm portable |
| Chrome | Prefix Google Chrome; entry chính trên máy khớp | Phân biệt bản chính với Beta/Dev/Canary và thành phần phụ |
| K-Lite | Tìm chuỗi K-Lite Codec Pack ở bất cứ vị trí nào; Standard trên máy khớp | Khớp họ sản phẩm ở đầu tên, các edition chính được chấp nhận; loại helper/update giả lập |
| Telegram | File AppData và prefix Telegram Desktop; máy có cả hai | Xác nhận đúng app/scope; executable cũ hoặc sai sản phẩm không đủ chứng minh |
| UltraViewer | Prefix UltraViewer; version 6.6.124 trên máy khớp | Chấp nhận hậu tố version hợp lệ, không chấp nhận mọi sản phẩm cùng prefix |
| WinRAR | Prefix WinRAR; 7.23 (64-bit) trên máy khớp | Chấp nhận phiên bản/kiến trúc; phân biệt app chính với phụ trợ |
| Zalo | File LocalAppData và prefix Zalo; máy có cả hai | Chấp nhận version, loại app liên quan và bằng chứng file không hợp lệ |
| Zoom | Prefix Zoom hoặc Zoom Workplace; Workplace trên máy khớp | Lỗi xác nhận bằng chuỗi giả lập: Zoom Outlook Plugin cũng khớp với ID zoom; phải loại plugin/VDI component không phải client được chọn |
| Office | Bắt buộc Microsoft Office ... 2024 | Chấp nhận bộ Office desktop đã cài, gồm 2021 và Microsoft 365; xác minh SKU ClickToRun hoặc Office MSI; không dùng component/Visio/Project đơn lẻ làm bằng chứng suite |
| WPS | Prefix WPS Office; chưa thấy entry phù hợp trên máy | Khớp suite chính, loại updater/component; không suy ra WPS đã cài chỉ vì có Microsoft Office |
| VC++ x64 | Bắt buộc tên 2015-2022 Redistributable (x64) | Runtime v14 Installed=1 đúng kiến trúc là nguồn chính; chấp nhận tên v14 và alias đã kiểm chứng khi fallback |
| VC++ x86 | Tương tự x64 | Xác định riêng x86; có x64 không được bỏ qua x86; VC++ 2012/2013 không đủ |
| App tùy chỉnh | Regex từ Name: tên chính xác, số phiên bản hoặc ngoặc | Không đoán từ tên UI không đủ rõ; hỗ trợ kết quả Unknown; tránh dùng rule built-in khi người dùng tái sử dụng ID cho sản phẩm khác |

Các tên `Zoom Outlook Plugin`, `Google Chrome Beta`, `WPS Office Update`, `Example K-Lite Codec Pack Helper` đã được thử với regex hiện tại và đều khớp. Đây là bằng chứng về matcher, không phải khẳng định các entry đó đang tồn tại trên máy.

## Hợp đồng hành vi

- Smart Skip là tránh cài lại ứng dụng đã hiện diện; không tự nâng phiên bản. Office cũ hơn 2024 vẫn được bỏ qua nếu đã xác nhận là bộ Office desktop.
- VC++ xét riêng runtime v14 x64 và x86. Trong phạm vi này, runtime v14 hợp lệ hiện có được bỏ qua; không suy ra thiếu chỉ vì khác nhãn marketing. Nếu sau này muốn nâng tối thiểu phải thêm yêu cầu phiên bản rõ ràng vào catalog, không hardcode một bản mới nhất.
- Giữ lựa chọn Office/WPS hiện có: chọn suite nào chỉ kiểm tra suite đó; Cancel không bắt đầu kiểm tra/cài đặt. Không tự đổi lựa chọn vì phát hiện suite khác.
- Trả về Installed / NotInstalled / Unknown, kèm lý do và bằng chứng. Installed mới bỏ qua thành công; NotInstalled mới cho tải/cài; Unknown báo không xác minh được và không tự cài đè app đó. Những app khác tiếp tục.
- Phạm vi là machine + tài khoản thực thi. Không tự đọc mọi user hive hoặc coi app của tài khoản khác là app của người dùng hiện tại. Nếu nâng quyền bằng tài khoản khác, ghi rõ scope đang kiểm tra; hỗ trợ target user riêng là công việc khác.

## Thứ tự đã triển khai

### 1. Tách detector và inventory net48

Tạo `Services/InstalledSoftwareDetector.net48.cs` với các kiểu dữ liệu đặt dưới NET48: InstalledSoftwareEntry, InstalledSoftwareSnapshot, DetectionResult và reader có thể thay bằng fixture. Các field cần thiết gồm DisplayName, DisplayVersion, Publisher, InstallLocation, key/hive/view, scope và bằng chứng executable/registration nếu có.

Đọc inventory một lần mỗi lượt trên background thread sau lựa chọn suite/khóa phiên. Quét bốn nguồn Registry có sẵn, giải phóng handle, xử lý lỗi từng key để các entry khác vẫn đọc được. Key không tồn tại khác với key không đọc được: không tìm thấy trong inventory thiếu dữ liệu phải là Unknown nếu chưa có bằng chứng độc lập đủ mạnh. Không dùng Win32_Product, không gọi WinGet, không dò toàn ổ đĩa trong Smart Skip.

Snapshot dùng trong lượt, không cache vĩnh viễn. Lượt retry đọc mới. Hỗ trợ cancellation giữa các bước đọc; không đặt timer rồi coi timeout là NotInstalled. Lỗi một app không khiến tất cả app khác thất bại.

### 2. Sửa Office và VC++ trước

Office: đọc ClickToRun ở cả registry views, phân loại ProductReleaseIds bằng danh sách SKU suite rõ ràng; hỗ trợ tên Office/Microsoft 365 và đăng ký MSI khi không có C2R. Loại language pack, update, Click-to-Run Extensibility, licensing component, Project/Visio độc lập. Đối chiếu đường cài/executable khi có; entry mâu thuẫn hoặc chỉ còn dấu vết không kết luận Installed. Không kiểm tra kích hoạt bản quyền để quyết định Smart Skip.

VC++: đọc Runtimes\x64 và Runtimes\x86 dưới VisualStudio\14.0\VC qua Registry32/64; Installed phải bằng 1 và dữ liệu phải phù hợp runtime v14/kiến trúc. Có fallback cho đăng ký Redistributable đầy đủ với alias 2015-2022/v14; Minimum hoặc Additional component riêng lẻ không đủ kết luận bộ runtime hoàn chỉnh. Không mở rộng regex sang mọi Visual C++.

Viết fixture lỗi trước, chứng minh matcher cũ không đáp ứng và detector mới đáp ứng dữ liệu thực đã xác nhận.

### 3. Hoàn thiện chín app còn lại và custom

Định nghĩa rule riêng cho từng họ sản phẩm trong bảng. Normalize khoảng trắng/case, chỉ cho hậu tố phiên bản, kiến trúc hoặc edition đã định nghĩa; không dùng prefix tùy ý. Publisher và executable metadata là bằng chứng bổ sung, không bắt buộc mù quáng vì có installer không điền Publisher/InstallLocation.

File fallback EVKey/Zalo/Telegram phải kiểm tra có đúng executable sản phẩm, không chỉ File.Exists. Dùng đường chuẩn theo môi trường hoặc đường đăng ký đã đọc, không tự chạy executable để xác nhận. Thiếu metadata không đồng nghĩa app chưa cài; dùng nguồn khác hoặc Unknown.

Built-in rule cần danh tính cấu hình nhất quán. Nếu một ID cũ bị đổi sang tên/URL sản phẩm khác, không im lặng dùng detector sản phẩm cũ để skip. Pha này không tự suy đoán từ URL; trả Unknown khi có xung đột rõ. App tùy chỉnh dùng tên sản phẩm chính xác và hậu tố được giới hạn; nếu cần rule tường minh thì thiết kế cấu hình additive sau, không tự ghi đè schema/cấu hình đang dùng.

### 4. Nối vào Install và ghi lý do

Chỉ nhánh NET48 của DeploymentService dùng detector mới; net10 giữ nhánh cũ. Không thay chữ ký public hiện có dùng chung một cách làm đổi hành vi net10. Chỗ tạo inventory phải nằm trong scope bắt lỗi của lượt cài.

UI hiển thị `Đang kiểm tra`, `Đã cài · bỏ qua` hoặc `Không xác minh được`; không tính Unknown là thành công. Giữ cơ chế tải/cài song song cho app thiếu, MSI tuần tự và Windows Setting như hiện tại. Bổ sung log bền vững dưới LocalAppData/MiniApps/InstallLogs cho quyết định từng app: ID, trạng thái, tên/phiên bản đã phát hiện, nguồn bằng chứng, lý do, lỗi đọc liên quan; không dump toàn bộ Registry hoặc khóa license.

Trước launch sau download, đọc lại bằng chứng mục tiêu để tránh cài nếu app được cài bên ngoài trong khoảng chờ; không quét lại toàn bộ danh sách cho từng app. Đây là kiểm tra giảm race, không cam kết khóa được installer bên ngoài. Nếu phát hiện app mới thì bỏ qua launch; nếu Unknown thì không launch.

Tổng kết phân biệt số app cài mới, bỏ qua, không xác minh được và lỗi. Kiểm tra cơ chế nút khóa cuối phiên: lỗi detector không được biến thành thông báo mọi app đã hoàn tất. Không tự thiết kế nút retry mới trong pha này; giữ hành vi phiên được quy định và ghi rõ các app chưa xử lý.

### 5. Test và bàn giao

Mỗi app có ít nhất một ca dương và âm thực sự phân biệt sản phẩm; dùng reader giả cho Registry/files thay vì đọc máy chạy CI. Bao phủ bốn nguồn Registry, source thiếu, AccessDenied, entry stale, metadata thiếu, ID đổi case, cấu hình custom xung đột và scope per-user.

Office: 2021, 2024, Microsoft 365 suite đều skip; component/Visio/Project riêng không skip. VC++: đúng dữ liệu v14 14.51.36247 cả x64/x86, chỉ một kiến trúc, chỉ VC++ 2013, Installed=0, thiếu version và component rời. Zoom mặc định ID zoom phải loại Outlook Plugin (test hiện tại chỉ kiểm tra ID custom). Chrome phải phân biệt Stable/Beta. WPS/K-Lite phải loại tên phụ trợ giả lập.

Integration: Installed -> 0 download, 0 launch, có lý do; NotInstalled -> cài một lần; Unknown -> 0 launch và trạng thái riêng; một app lỗi đọc không chặn app khác; snapshot đọc một lần; retry đọc mới; kiểm tra lại trước launch; Cancel -> không launch; Office/WPS vẫn đúng lựa chọn. Fake runner hiện tại chỉ trả true cho một app không đủ chứng minh detector.

Build/test chỉ `dotnet build ./MiniApps.Tests/MiniApps.Tests.csproj -c Release -f net48`, sau đó chạy test executable. Thêm kiểm tra read-only dùng detector mới trên máy phát triển: Office, VC++ x64/x86 phải Installed; các app còn lại đối chiếu inventory. Không bấm Install thật để xác minh skip. Sau khi đạt, cập nhật CURRENT-STATE/ARCHITECTURE, mở Developer thật bằng Run-Developer.ps1 -Target net48 để người dùng kiểm thử. Không build net10, commit/push hay phát hành.

## Điều kiện hoàn tất

Ba lỗi thực Office 2021 + VC++ v14 x64/x86 không còn tải hoặc launch; mọi quyết định skip có bằng chứng; không skip nhầm plugin/component hoặc sai kiến trúc; nguồn đọc lỗi không bị coi là thiếu app để cài đè; toàn bộ 12 mục mặc định và custom có coverage; thay đổi hành vi chỉ áp dụng net48.
