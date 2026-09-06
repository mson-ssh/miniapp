# MiniApps — hướng dẫn cho AI làm việc trong WPF

Đọc `docs/README.md`, `docs/PROJECT-RULES.md` và `docs/CURRENT-STATE.md` trước khi sửa dự án. Đọc thêm `docs/ARCHITECTURE.md` khi thay đổi logic và `docs/DEVELOPMENT.md` trước khi build/chạy. Hướng dẫn của người dùng trong phiên hiện tại ưu tiên hơn các tài liệu này.

## Quy tắc bắt buộc

- Phạm vi phát triển hiện tại là MiniApps WPF, ưu tiên **.NET Framework 4.8**. Không sửa/chạy CLI `../Setup.ps1` như một bước mặc định.
- Chỉ đồng bộ tính năng, build hoặc kiểm thử **.NET 10** khi người dùng yêu cầu riêng. Dự án dùng chung source đa target: thay đổi net48 riêng cần điều kiện `NET48` hoặc MSBuild phù hợp; không coi “chưa build” là “source net10 không đổi”.
- Luôn chỉ định `-f net48` khi build/test hoặc gọi dotnet publish. Chỉ chạy `Publish.ps1` khi được yêu cầu phát hành: `-Target net48` giữ lại gói net10 cũ; `-Target both` cần yêu cầu cập nhật net10 riêng.
- Chỉ commit/push GitHub khi người dùng yêu cầu. Push source không đồng nghĩa cho phép tạo Release, upload ZIP hay đổi bootstrap sang release mới.
- Sau thay đổi tính năng, kiểm tra phù hợp rồi mở Developer preview net48. Không chạy cài đặt, debloat hay script cấu hình Windows thật trên máy phát triển để thử giao diện.
- Developer preview cho phép **lưu cấu hình thật** trong `ReleaseConfig`; không tự sửa/lưu cấu hình chỉ để thử UI.
- Giữ thay đổi có sẵn của người dùng; không reset/clean/xóa hàng loạt. Không ghi đè cấu hình hoặc artifact phát hành đang sử dụng.
- Xác minh remote trước push: repo WPF là `https://github.com/mson-ssh/miniapp.git` (số ít). Không suy ra từ tên thư mục hoặc remote CLI `miniapps.git`.
- Cập nhật tài liệu trạng thái sau thay đổi đáng kể; phân biệt đã code, đã test preview, đã test hệ thống thật và đã phát hành.

## Điểm vào trên máy mới

Mở **thư mục WPF làm workspace** để AI đọc hướng dẫn này ngay từ đầu. Nếu mở gốc repo, yêu cầu AI đọc `WPF/AGENTS.md` trước. Không phụ thuộc lịch sử chat, đường dẫn `F:` hoặc checkout tạm của máy cũ.
