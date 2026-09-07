# Optimize Windows — profile đã tích hợp

Nguồn: [Raphire/Win11Debloat](https://github.com/Raphire/Win11Debloat/tree/6012b02ea282f23ea943946206762fd430025c6f), commit `6012b02ea282f23ea943946206762fd430025c6f`.

Script MiniApps: `MiniApps/Scripts/Optimize-Defaults.ps1`, chỉ đóng gói cho net48. Script tải ZIP qua HTTPS từ đúng commit đã ghim và theo quyết định sản phẩm không kiểm tra SHA-256 của ZIP upstream. Nó không tự chuyển sang master/latest khi tải hoặc chuẩn bị lỗi. Xác minh kích thước/SHA-256 của gói phát hành MiniApps trong bootstrap vẫn được giữ nguyên.

Để tương thích giới hạn đường dẫn của PowerShell 5.1/.NET Framework, service tạo thư mục phiên ngắn `o-<12 ký tự>` và `Expand-OptimizeArchive.ps1` bỏ thư mục gốc dài đã ghim của ZIP khi giải nén vào `src`. Helper kiểm tra toàn bộ danh sách entry trước khi ghi: chỉ chấp nhận đúng thư mục gốc dự kiến, chặn đường dẫn tuyệt đối/traversal/ADS/tên Windows không hợp lệ, trùng tên không phân biệt hoa thường, xung đột file-thư mục, đường dẫn vượt giới hạn và dữ liệu nở quá mức. Nội dung được giải nén từng entry vào thư mục staging cùng phiên rồi mới đổi tên thành `src`; lỗi chuẩn bị không để lại cây đích một phần và lỗi dọn staging không che lỗi gốc.

Script đọc `Config/DefaultSettings.json` schema 1.0 rồi loại mục `CreateRestorePoint`. Sau đó chạy upstream với `-RunDefaults -Silent -SkipExplorerRestart -LogPath ...`. Dùng danh sách Apps Default của cùng commit, không dùng Default Lite và không bổ sung danh sách gỡ riêng.

16 thiết lập còn lại:

```text
DisableTelemetry
DisableSuggestions
DisableEdgeAds
DisableLockscreenTips
DisableBing
DisableStoreSearchSuggestions
DisableCopilot
DisableRecall
DisableClickToDo
DisableAISvcAutoStart
DisableWidgets
HideChat
ShowKnownFileExt
DisableDragTray
Hide3dObjects
DisableModernStandbyNetworking
```

Một nút áp dụng toàn bộ profile qua một process upstream. UI net48 liệt kê `RemoveApps` và 16 thiết lập theo thứ tự thực thi, không có checkbox hoặc nút riêng. Tác vụ đang chạy đứng đầu, tác vụ chờ theo sau; DONE/SKIP/ERROR trượt xuống cuối trong 220 ms. Không tách upstream thành nhiều instance vì chúng dùng chung trạng thái/backup. Giữ việc bỏ qua tính năng không tương thích theo upstream; hoàn tất profile không đảm bảo mọi tính năng đã được áp dụng trên mọi build Windows.

Không tạo restore point, nhưng vẫn giữ backup Registry. Trước khi tải, mỗi lượt tạo một thư mục riêng trong `%LocalAppData%\MiniApps\OptimizeLogs` để giữ transcript, lỗi chuẩn bị và stderr. Backup upstream ban đầu nằm trong `Backups` của source tải về và được chuyển vào cùng thư mục lượt chạy trước khi dọn. Nếu không chuyển được, luồng Optimize cố giữ thư mục chứa backup. Cần kiểm tra thêm tương tác dọn phiên bootstrap khi chạy thật.

Script phát protocol nội bộ cho bốn giai đoạn Downloading, Preparing, Applying và Completed, cùng dòng JSON `MINIAPPS_TASK_JSON` có event QUEUED/START/DONE/SKIP/ERROR. Bridge được chèn ngay trước lời gọi top-level `Invoke-AllChanges` đã ghim; giữ nguyên kết quả boolean của hàm upstream. UI thật không dùng timer để nội suy: Applying và hàng đang chạy dùng thanh không xác định, còn tổng số hoàn tất chỉ đếm terminal event. ERROR, thiếu terminal event, hoặc không xác minh được kết quả gỡ ứng dụng đều làm lượt chạy thất bại ngay cả khi process trả mã 0. Marker Completed của script không được đưa lên UI trước khi service xác minh đủ 17 kết quả. Output PowerShell ngoài protocol được giữ có giới hạn và đưa vào thông báo lỗi. `Test-OptimizeTaskBridge.ps1` kiểm tra protocol bằng fixture và không chạy thay đổi hệ thống. Chưa test thực thi thật trên VM; không tuyên bố đã xác minh hoàn toàn.

Install và Optimize không bắt đầu đồng thời trong cùng cửa sổ. Người dùng vẫn chuyển qua Install app và Driver khi Optimize chạy; Setting bị khóa. Nút “Dừng hàng đợi” chỉ thuộc luồng Install và không xuất hiện trong Optimize.

Khi nâng upstream: đọc lại Default và danh sách Apps, kiểm tra thay đổi schema, điểm gắn bridge, tham số, backup và error handling; cập nhật commit đã ghim rồi test net48 bằng fixture và VM được chỉ định. Không chạy script downloaded trên máy phát triển để xác minh profile.

Kiểm tra extraction-only ngày 2026-09-07: fixture đường dẫn `irm` giảm đường dẫn dài nhất được tái hiện từ 286 xuống 204 ký tự; các trường hợp traversal, root lạ, alias khác hoa thường, absolute path, ADS, quá dài và ZIP hỏng đều bị từ chối trước khi tạo file đích. ZIP thật của commit đã ghim giải nén 403 file, đường dẫn dài nhất 221 ký tự. Không chạy entry point upstream hoặc thay đổi hệ thống trong các kiểm tra này.
