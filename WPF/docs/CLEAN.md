# CLEAN

Theo yêu cầu ngày 2026-09-28: EXTEND thêm CLEAN, chỉ tài khoản Windows hiện tại. Không chạy tự động khi mở MiniApps hoặc cài phần mềm. Nút Dọn dẹp hỏi xác nhận, dùng chung mutex Install/EXTEND, ghi nhật ký ExtendLogs. Người dùng trực tiếp chạy và kiểm tra trên máy thử; AI không dọn dữ liệu thật để kiểm thử.

## Phạm vi và bảo toàn

- Dọn các phần tử bên trong `%TEMP%` của tài khoản. Hiện chỉ chấp nhận đường dẫn chuẩn `AppData\Local\Temp` nằm dưới UserProfile; TEMP đổi chỗ, profile chuyển qua junction hoặc đường dẫn quá rộng sẽ bị từ chối trước khi xóa. Không xóa thư mục TEMP gốc.
- Giữ thư mục MiniApps, thư mục chứa ứng dụng/phiên bootstrap đang chạy, Backup/Backups, debloat-* và file .reg/.bak trong TEMP. File khóa, không có quyền hoặc liên kết reparse được giữ, có ghi log. Không ép tắt process, không sửa ACL/attributes để ép xóa.
- Profile chuẩn Chrome (stable/beta/dev/canary), Edge (stable/beta/dev/canary), Cốc Cốc, Brave (stable/beta/nightly), Vivaldi, Chromium và Opera/Opera GX. Nhận diện thư mục profile bằng Preferences; chỉ xóa danh sách cho phép của lịch sử và cache, không xóa toàn bộ profile. Firefox dùng Profiles và Path đăng ký trong profiles.ini, chỉ chấp nhận bên trong UserProfile.
- Xóa thêm Favicons (Chromium) và favicons.sqlite (Firefox) vì chúng lưu URL đã truy cập; icon của dấu trang sẽ tải lại khi mở trang. Firefox xóa thêm moz_historyvisits_extra và dòng mồ côi của moz_places_extra; lỗi checkpoint WAL sau COMMIT không bị coi là lỗi dọn.
- Giữ Bookmarks, Login Data, Cookies/Network\Cookies, Web Data, Local Storage, IndexedDB, Sessions; Firefox giữ cookies.sqlite, logins.json, key4.db, bookmarkbackups và sessionstore. Không dọn thư mục Downloads. Danh sách tải trong History của Chromium bị xóa nhưng file tải xuống giữ nguyên.
- Cache gồm Cache, Code Cache, GPU/shader/Dawn caches, Service Worker CacheStorage/ScriptCache; không xóa cơ sở dữ liệu đăng ký service worker hoặc dữ liệu website khác. Firefox dọn cache2/startupCache/thumbnails ở profile và vị trí Local tương ứng.

## Firefox và tính toàn vẹn

Không xóa places.sqlite vì chứa cả dấu trang. Dùng winsqlite3.dll có sẵn trong Windows (chỉ tải DLL từ System32), khóa parent.lock và giao dịch SQLite EXCLUSIVE. Kiểm tra schema, quick_check, không chấp nhận trigger lạ/hard link; xóa visits và metadata lịch sử, chỉ xóa URL không có tham chiếu/dấu trang. So digest toàn bộ moz_bookmarks trước/sau; lỗi trước COMMIT thì ROLLBACK. Checkpoint WAL sau commit. Không tải công cụ ngoài hoặc thêm package runtime.

Đây là dọn lịch sử cục bộ, không phải xóa an toàn chống khôi phục forensic. Lịch sử trên tài khoản đồng bộ hoặc trong phiên/tab đã lưu không được xóa; đồng bộ có thể đưa dữ liệu trở lại. Profile portable/tùy chỉnh Chromium không tự suy đoán, Firefox ngoài UserProfile không được đụng tới. Browser/schema khác không được cam kết hỗ trợ.

## Kiểm tra và kết quả

`CleanTests.cs` tạo dữ liệu giả dưới thư mục test, không dùng profile thật: allowlist/temp, nhiều profile/trình duyệt, dữ liệu đăng nhập/dấu trang còn nguyên, browser mở, TEMP sai, file khóa, junction, Firefox thành công/rollback/schema lạ/DB hỏng/khóa/hard link/vượt tài khoản, chạy lặp. WPF preview kiểm tra xác nhận, khóa và hoàn tất không gọi cleaner thật.

Runtime đối chiếu SID với Explorer cùng phiên trước khi dọn: nếu UAC dùng tài khoản admin khác, CLEAN từ chối. Không tìm thấy Explorer hoặc không xác minh được cũng từ chối. Cập nhật 2026-09-28: không còn chặn toàn bộ khi thấy tiến trình theo tên (Edge Startup boost gần như luôn chạy nền; tên `browser` quá chung). Mỗi trình duyệt được kiểm riêng bằng khóa profile của chính nó: Chromium giữ `User Data\lockfile`, Firefox giữ `parent.lock` khi còn chạy (kể cả nền). Mở được khóa độc quyền thì CLEAN giữ khóa đó (delete-on-close) suốt lúc dọn trình duyệt này, nên trình duyệt không mở lên giữa chừng; không mở được thì bỏ qua trình duyệt đó, ghi log, vẫn dọn TEMP và trình duyệt khác. Không ép tắt tiến trình.

Mã 0: các mục được hỗ trợ xử lý xong; mã 2: chưa dọn hết/bị chặn (trình duyệt đang mở, liên kết, schema lạ…), xem nhật ký. File TEMP đang bị chương trình khác mở (sharing/lock violation) là bình thường, được đếm riêng là "đang được dùng" và không làm kết quả thành mã 2. Giới hạn được bảo vệ chủ động (backup/MiniApps) không tính là lỗi. Số MB là dung lượng file đã xóa, không tính phần giảm của SQLite. Không có cơ chế khôi phục các file đã xóa.

Nguồn đối chiếu cấu trúc dữ liệu:

- [Chromium user data directories](https://chromium.googlesource.com/chromium/src/+/HEAD/docs/user_data_dir.md).
- [Mozilla profile data](https://support.mozilla.org/en-US/kb/profiles-where-firefox-stores-user-data).
- [Mozilla History API](https://firefox-source-docs.mozilla.org/browser/places/History.html).

Chưa kiểm thử dọn trên profile trình duyệt thật hay UAC bằng tài khoản khác; cần người dùng thử trên máy/VM có dữ liệu thử và đóng trình duyệt trước.
