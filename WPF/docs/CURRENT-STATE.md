# Trạng thái bàn giao

## Phát hành v0.3.1

Đang chuẩn bị phát hành theo yêu cầu thử `irm`: net48 gồm Optimize dạng thông báo có hoạt ảnh, task bridge và tài liệu bàn giao. Gói net10 tái sử dụng nguyên byte từ v0.3.0 (SHA-256 `828341fa007fb89f1d246758eaa9dae93e835b283464c2d5068be785b3e42b17`), không build lại. Các thông tin v0.3.0 bên dưới là lịch sử trước lần phát hành này.

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
