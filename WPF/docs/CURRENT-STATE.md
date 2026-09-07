# Trạng thái bàn giao

## Bản sửa local sau v0.3.4 — lỗi khởi động gói Public

Máy khách xác nhận v0.3.4 thoát mã 2 vì WPF tìm `app\mainwindow.xaml`. Nguyên nhân là cây Win11Debloat đã được khai báo thành MSBuild `Content`; `Schemas\MainWindow.xaml` của upstream sinh `AssemblyAssociatedContentFileAttribute("mainwindow.xaml")` và che resource giao diện đã biên dịch của MiniApps. Bản sửa net48 chuyển cây vendor sang `None` có copy-to-output, nên XAML upstream vẫn có tại `Engine\Debloat\Schemas` dưới dạng dữ liệu nhưng không còn metadata WPF. Assembly Public đã kiểm tra không đăng ký external `mainwindow.xaml`, đồng thời vẫn có `MiniApps.g.resources/mainwindow.baml`.

Khởi động net48 nay ghi `Exception.ToString()` cùng thông tin runtime vào `%LocalAppData%\MiniApps\StartupLogs`; hộp thoại dùng tiêu đề “MiniApps - lỗi khởi động” và hiển thị đường dẫn log. Headless vẫn không mở hộp thoại. Build net48 sạch; 60 logic tests và 6 kiểm tra WPF đạt. Fixture engine/bridge/bootstrap đạt. Gói Public đã mở và đóng sạch từ output ngắn và đường dẫn lồng kiểu phiên `irm`; không chạy Optimize/cài đặt/thiết lập Windows thật. Developer preview được mở sau kiểm tra. Net10 chưa build/test/cập nhật. Chưa commit, push hoặc phát hành bản sửa này.

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

Điều chỉnh giao diện mới nhất: hàng Optimize dạng thẻ thông báo gọn, bỏ toàn bộ vòng tròn/dấu tích trạng thái; dùng chữ trạng thái và nền dịu, giữ hoạt ảnh chuyển hàng 220 ms. Không thay đổi engine thực thi.

Install một nút, tiến trình ba cột, Windows Setting mở rộng, tự nhận diện app đã cài; nút khóa sau lượt hoàn tất. Setting chỉ Developer, hai nhóm CRUD cấu hình. Driver đọc thông tin và dẫn URL hãng. Optimize net48 có danh sách 17 tác vụ tuần tự lấy trạng thái thật từ upstream: tác vụ đang chạy đứng đầu, tác vụ chờ theo sau, tác vụ hoàn tất trượt xuống cuối trong 220 ms; DONE/SKIP/ERROR và trường hợp thiếu kết quả được giữ đúng nghĩa. Giao diện không hiển thị restore point hoặc backup Registry, nhưng engine vẫn bỏ restore point và bảo toàn backup như trước. Thành công khóa nút trong phiên; lỗi cho thử lại. Developer preview mô phỏng đúng vòng đời và không thay đổi hệ thống.

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
