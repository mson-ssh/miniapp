# Quy tắc dự án

## Runtime và quyền phát hành

1. Thêm/sửa/xóa tính năng trên net48 trước.
2. Chỉ cập nhật net10 khi người dùng yêu cầu. Hai target hiện dùng chung project; phải kiểm soát thay đổi source chung và điều kiện biên dịch.
3. Chỉ commit/push khi được yêu cầu cho công việc đó. Yêu cầu push trước đây không phải quyền tự push mọi thay đổi sau này.
4. Tạo Release/upload artifact cần yêu cầu phát hành. Không đổi URL bootstrap tới release chưa tồn tại.
5. Sau sửa tính năng, build/test net48 phù hợp và chạy Developer preview. Không tự thực thi tối ưu/cài đặt thật trên máy phát triển.

## Giao diện và hành vi

- WPF, minimalist, sáng dịu, ít nút. Không dùng font icon/emoji làm icon; SVG được phép khi cần. Không lấy Ninite làm mẫu hiện tại.
- Public: Install app, Optimize Windows, Driver. Developer thêm Setting; Setting bị khóa khi có tác vụ đang chạy.
- Install app có một nút, không chọn từng ứng dụng/thiết lập. Hộp thoại: “Bạn muốn sử dụng ứng dụng nào:” với Office 2024 / WPS / Cancel. Cancel không khởi chạy tác vụ.
- Tiến trình ứng dụng chia ba cột. Windows Setting là một bản ghi có thể mở rộng để xem từng tác vụ. Không có phần chọn Windows Setting trước khi chạy.
- Cài nhiều ứng dụng nhất có thể đồng thời; Windows Setting bắt đầu cùng lượt. MSI cần tuần tự do Windows Installer; không áp giới hạn bốn app.
- Lượt cài hoàn tất thì nút xám, bị khóa trong phiên. Lượt hủy hoặc lỗi cấp phiên cho thử lại; lỗi từng app được báo trong kết quả.
- Nhận diện ứng dụng đã cài là tính năng tự động. Setting có hai nhóm ứng dụng/thiết lập, hỗ trợ thêm/sửa/xóa; không có mục Nâng cao.
- Driver đọc Host, hãng, model, serial có thể copy và mở URL hãng. Chưa tự tải/cài driver.
- Optimize tách khỏi Install app; dùng Default Win11Debloat, chỉ bỏ restore point. Một lần bấm chạy cả profile; engine upstream giữ thứ tự nội bộ. Xem [OPTIMIZE.md](OPTIMIZE.md).

## Ranh giới thực thi

Đích triển khai của dự án là Windows 10 1809/build 17763 trở lên và Windows 11 x64. Đây là baseline dự án, không khẳng định mọi edition/runtime đều còn được nhà cung cấp hỗ trợ. Từng Windows setting có kiểm tra tương thích riêng.

Không tự thêm thao tác chia ổ, tắt bảo mật hoặc tối ưu khác từ CLI. Không kill installer đang chạy để làm tiến trình trông hoàn tất. Chỉ dọn thư mục phiên do MiniApps sở hữu; bảo toàn backup và dữ liệu người dùng.
