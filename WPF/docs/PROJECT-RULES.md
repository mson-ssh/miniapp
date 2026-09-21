# Quy tắc dự án

## Runtime và quyền phát hành

MiniApps phải yêu cầu quyền Administrator ngay khi khởi động. Người dùng từ chối UAC thì ứng dụng không chạy; các kiểm tra quyền bên trong tác vụ vẫn được giữ để phòng vệ.

1. Thêm/sửa/xóa tính năng trên net48 trước.
2. Chỉ cập nhật net10 khi người dùng yêu cầu. Hai target hiện dùng chung project; phải kiểm soát thay đổi source chung và điều kiện biên dịch.
3. Chỉ commit/push khi được yêu cầu cho công việc đó. Yêu cầu push trước đây không phải quyền tự push mọi thay đổi sau này.
4. Tạo Release/upload artifact cần yêu cầu phát hành. Không đổi URL bootstrap tới release chưa tồn tại.
5. Từ 2026-09-18 chỉ còn một bản Public; edition Developer đã bị gỡ theo yêu cầu người dùng. Sau sửa tính năng, build/test net48 phù hợp và mở ứng dụng từ output riêng, kèm bản sao `ReleaseConfig`. AI không tự bấm thực thi cài đặt; người dùng quyết định thao tác thật trong giao diện.

## Giao diện và hành vi

- WPF, minimalist, sáng dịu, ít nút. Không dùng font icon/emoji làm icon; SVG được phép khi cần. Không lấy Ninite làm mẫu hiện tại.
- Thanh điều hướng viết hoa. Giao diện net48 và net10 giống nhau: INSTALL SOFTWARE, INFORMATION và EXTEND. Information đọc dữ liệu từ `tool/Info/info.ps1`, có Làm mới; nút Driver cạnh Serial sao chép Serial rồi mở hỗ trợ chính thức của hãng. Nút Sao chép toàn bộ vẫn ẩn. Không có trang Setting; catalog được sửa trực tiếp trong `ReleaseConfig/*.json`.
- Install Software có một nút Cài đặt cho toàn bộ danh sách. Cạnh nút Cài đặt có drop list chọn bộ văn phòng: Microsoft Office / WPS / OnlyOffice / Libre Office / Null (mặc định, không cài bộ nào); không còn hộp thoại chọn Office. Drop list khóa khi đang chạy hoặc sau khi lượt cài đã bắt đầu. Chọn WPS/OnlyOffice/Libre Office thì gỡ Microsoft Office đang có trên máy (tác vụ Windows "Gỡ Microsoft Office", không nằm trong `windows.json`). Panel Sẵn sàng liệt kê mọi app trong catalog (gồm cả bốn bộ văn phòng), mỗi app có nút tải xuống để cài riêng app đó: không chạy Windows Setting, vẫn qua Smart Skip, và không khóa nút Cài đặt chính.
- Tiến trình ứng dụng chia ba cột. Windows Setting là một bản ghi có thể mở rộng để xem từng tác vụ. Không có phần chọn Windows Setting trước khi chạy.
- Cài nhiều ứng dụng nhất có thể đồng thời; Windows Setting bắt đầu cùng lượt. MSI cần tuần tự do Windows Installer; không áp giới hạn bốn app.
- Lượt cài hoàn tất thì nút xám, bị khóa trong phiên. Lượt hủy hoặc lỗi cấp phiên cho thử lại; lỗi từng app được báo trong kết quả.
- Nhận diện ứng dụng đã cài là tính năng tự động.
- Nút Driver trong Information sao chép Serial và mở URL hãng. Chưa tự tải/cài driver.
- Trang EXTEND chứa các phần mở rộng chạy riêng khi cần, luôn hỏi xác nhận trước và dùng chung khóa cài đặt (không chạy cùng lúc với Install Software). Debloat không còn ở EXTEND (chuyển sang Install Software từ 2026-09-21), hiển thị như một thẻ ứng dụng trong Sẵn sàng cài đặt (nút tải xuống chạy riêng Debloat) và trong Tiến trình cài đặt, được đếm cùng ứng dụng: Windows Setting "Debloatware Windows" (`Win11Debloat`) chạy Win11Debloat 2026.08.24 ghim SHA-256 ở chế độ mặc định `-RunDefaults -Silent` trong mỗi lượt cài chính, song song với cài app; bản giải nén được giữ tại `%LocalAppData%\MiniApps\Win11Debloat` vì chứa backup Registry. Môi trường C++ cài VS Code, MSYS2, MinGW-w64 UCRT64, PATH máy và extension C/C++, bỏ qua phần đã có.

## Ranh giới thực thi

Đích triển khai của dự án là Windows 10 1809/build 17763 trở lên và Windows 11 x64. Đây là baseline dự án, không khẳng định mọi edition/runtime đều còn được nhà cung cấp hỗ trợ. Từng Windows setting có kiểm tra tương thích riêng.

Chia ổ đĩa là Windows Setting theo đúng quy tắc của `$DiskScript` trong CLI (nhóm 256 GB / 512 GB / 1 TB, bỏ qua ổ trên 1100 GB hoặc máy đã có D:/E:, dừng nếu C: còn từ 30 GB trở xuống, tắt BitLocker và Hibernate trước khi thu nhỏ C:); thay đổi các mốc này phải sửa cả `Test-SplitDisk.ps1`. Không tự thêm thao tác tắt bảo mật hoặc tối ưu khác từ CLI. Không kill installer đang chạy để làm tiến trình trông hoàn tất. Chỉ dọn thư mục phiên do MiniApps sở hữu; bảo toàn backup và dữ liệu người dùng.
