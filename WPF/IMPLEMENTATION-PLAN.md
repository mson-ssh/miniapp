# MiniApps: kế hoạch triển khai bản chạy thật

## Đợt 1 — Điều phối cài đặt đồng thời (Sol, medium)

Trạng thái: đã triển khai và qua 38 kiểm thử cùng kiểm tra WPF preview. Cài đặt trên máy thật/VM thuộc đợt 4, chưa được xác nhận bởi các kiểm thử giả.

- Không đặt giới hạn số lượt tải; toàn bộ danh sách bắt đầu tải ngay và bộ cài nào kiểm tra xong được phép bắt đầu.
- EXE không dùng khóa cài toàn cục; số bộ cài đồng thời được giới hạn tự nhiên bởi danh sách đã chọn Office hoặc WPS.
- Các file MSI dùng một khóa riêng; EXE vẫn chạy trong lúc MSI hoạt động.
- Bộ cài trả 1618 được chờ và thử lại tối đa 3 lần sau lần đầu, với khoảng nghỉ 5/10/15 giây. Hết lượt thì báo lỗi.
- Hủy download và lượt chưa bắt đầu; không ngắt bộ cài đang chạy. Chờ tất cả bộ cài đã khởi chạy trước khi chạy thiết lập hoặc dọn file phiên.
- Tất cả thiết lập Windows bắt đầu đồng thời với ứng dụng và chạy song song với nhau; UI tổng hợp trong một thẻ Windows Setting.
- Giữ nguyên kiểm tra hash/header, nhận diện đã cài, lựa chọn Office/WPS và thông báo reboot.

Nghiệm thu bằng bộ cài giả: hơn 4 lượt tải cùng hoạt động; nhiều EXE và Windows Setting cùng chạy; không có hai MSI cùng chạy; EXE có thể chạy cùng MSI; 1618 được thử lại và có điểm dừng; Cancel chờ tác vụ đang chạy và không khởi động lượt mới. Không lấy tốc độ preview làm bằng chứng về tốc độ cài thật.

## Đợt 2 — Rà soát catalog và thiết lập

Trạng thái: phần mã nguồn đã hoàn tất. Catalog khớp URL/tham số của CLI; cấu hình có schema và tombstone cho mục mặc định đã xóa; Windows Setting đã có kiểm thử thành công một phần/lỗi/hủy. Winget, Debloat và Info.exe vẫn cần chạy trên VM ở đợt 4.

- Đối chiếu URL, tham số và nhận diện ứng dụng với CLI. Xác định EXE bọc MSI nào cần phân nhóm riêng nếu không trả 1618 đúng chuẩn.
- Xác minh Winget, Debloat và Info.exe trên VM, bao gồm thư mục đích và lỗi tải.
- Thiết kế migration cấu hình có phiên bản để thêm mặc định mới một lần, bảo toàn mục đã sửa/xóa và không tự khôi phục mục người dùng đã bỏ.
- Kiểm tra tổng hợp Windows Setting khi thành công một phần, lỗi hoặc hủy.

## Đợt 3 — Driver và bootstrap

Trạng thái: phần mã nguồn và fixture đã hoàn tất. CIM có giới hạn 15 giây, kết thúc cây PowerShell khi quá hạn và lọc placeholder BIOS. Bootstrap đã qua fixture kiểm tra checksum, giải nén, chờ tiến trình con và dọn phiên. Ma trận máy thật thuộc đợt 4.

- Đọc thông tin thật, xử lý BIOS thiếu dữ liệu, CIM treo và serial không hợp lệ; kiểm tra sao chép và liên kết hãng.
- Kiểm tra ZIP/checksum, UAC, chờ cây tiến trình và dọn phiên sau khi thoát.
- Bảo toàn cấu hình và Info.exe trên Desktop; không xuất bản release trước khi xác minh artifacts.

## Đợt 4 — Kiểm thử Windows 10/11 và bàn giao

- Dùng VM có snapshot, chưa cài .NET; kiểm tra cài thật với mạng lỗi, installer lỗi, reboot, hủy và đóng ứng dụng.
- Ghi rõ môi trường đã chạy và các trường hợp chưa kiểm chứng.
- Xuất ZIP self-contained, checksum, manifest và hướng dẫn triển khai.
- Sau mỗi đợt thay đổi: kiểm tra phù hợp, đóng gói và tự mở preview mới để người dùng xem.

Điều kiện thực hiện kiểm thử cài thật: có VM thử nghiệm phù hợp. Máy làm việc hiện tại chỉ dùng kiểm thử giả và preview.
