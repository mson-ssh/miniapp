# Optimize Windows — profile đã tích hợp

Nguồn: [Raphire/Win11Debloat](https://github.com/Raphire/Win11Debloat/tree/6012b02ea282f23ea943946206762fd430025c6f), commit `6012b02ea282f23ea943946206762fd430025c6f`.

Script MiniApps: `MiniApps/Scripts/Optimize-Defaults.ps1`, chỉ dùng cho net48. Fork hẹp từ Win11Debloat đã ghim được lưu trong `ThirdParty/Win11Debloat`. Build `MiniApps.OptimizeEngine` tạo ZIP xác định gồm cây vendor và tám script điều phối, rồi nhúng ZIP vào một EXE. Output Developer chỉ mang `MiniApps.OptimizeEngine.exe`; output Public không mang engine Optimize. Optimize không tải ZIP/source từ GitHub khi chạy. Xác minh kích thước/SHA-256 của gói phát hành MiniApps trong bootstrap vẫn được giữ nguyên. Xem [DEBLOAT-REWRITE.md](DEBLOAT-REWRITE.md).

Helper xác minh SHA-256, đường dẫn, tên trùng và các tệp bắt buộc trong payload trước khi giải nén. Khi chạy, 352 tệp chỉ xuất hiện trong thư mục phiên tạm rồi được dọn sau khi runner kết thúc; source vendor không còn được sao chép thành hàng trăm file cạnh ứng dụng. `Schemas/*.xaml` vẫn là dữ liệu runtime của upstream, không phải WPF `Content` của MiniApps. Fixture gói kiểm tra Developer có đúng helper, Public không chứa payload Optimize, assembly không đăng ký `mainwindow.xaml` bên ngoài và vẫn chứa resource `mainwindow.baml` của giao diện MiniApps.

Để tương thích giới hạn đường dẫn của PowerShell 5.1/.NET Framework, service tạo thư mục phiên ngắn `o-<12 ký tự>`. EXE giải nén payload vào `payload`, sau đó `Copy-OptimizeEngine.ps1` sao chép engine vào `src` qua staging cùng phiên rồi mới đổi tên. Các lớp kiểm tra chặn reparse point, thoát root, tên nguy hiểm và đường dẫn vượt giới hạn trước khi ghi; lỗi cleanup không che lỗi chuẩn bị. `Expand-OptimizeArchive.ps1` còn nằm trong source như fixture lịch sử, nhưng không được nhúng hoặc nằm trong luồng Optimize active.

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

Một nút khởi chạy đồng thời hai engine: `RemoveApps` xử lý danh sách app Default, còn `Features` gọi `-RunDefaults`, giữ kết quả lọc tương thích theo build Windows/Modern Standby của upstream rồi loại riêng phần app để xử lý các thiết lập và Registry backup. UI chia cùng lượt thành hai vùng: **Debloatware** chứa tác vụ gỡ phần mềm và **Optimize Windows** chứa các tùy chỉnh/tắt tính năng. Không có checkbox hoặc nút chạy riêng. Mỗi lệnh Appx hoặc WinGet chạy bằng worker `powershell.exe` riêng; mutex giữ các thay đổi package tuần tự trong khi chỉnh Registry/Windows vẫn chạy song song. Engine giám sát Appx tối đa 180 giây, WinGet tối đa 120 giây; cả nhóm dừng xếp thêm package sau 1.200 giây. Khi worker chưa trả về đúng hạn, worker không bị kill, UI nhận `OVERDUE` và trở lại tương tác trong khi mutex chặn Install/Optimize mới đến khi worker kết thúc.

Không tạo restore point, nhưng vẫn giữ backup Registry. Trước khi chuẩn bị engine, mỗi lượt tạo một thư mục riêng trong `%LocalAppData%\MiniApps\OptimizeLogs` để giữ transcript, lỗi chuẩn bị, stderr và `runtime-diagnostics.json`. Bản sao entry point được cấu hình để upstream ghi Registry backup trực tiếp vào `Backups` trong thư mục bền vững này. MiniApps thử ghi vào đích trước khi phát stage Applying; upstream cũng dừng trước khi áp dụng nếu thao tác tạo backup thực tế thất bại. Khi upstream kết thúc, MiniApps yêu cầu tìm thấy tệp `Win11Debloat-RegistryBackup-*.json` trước khi báo hoàn tất.

Script phát protocol nội bộ cho Preparing, Applying, Overdue và Completed, cùng dòng JSON `MINIAPPS_TASK_JSON` có event QUEUED/START/DONE/SKIP/ERROR/OVERDUE. Helper đọc stdout/stderr bất đồng bộ tới dòng hoàn chỉnh, kể cả đoạn cuối không có newline, và lấy mã thoát trực tiếp từ `System.Diagnostics.Process`. Bridge được engine nạp trực tiếp trước lời gọi top-level `Invoke-AllChanges` đã ghim và giữ nguyên kết quả boolean của hàm upstream. UI thật không dùng timer để nội suy: Applying và hàng đang chạy dùng thanh không xác định, còn tổng số hoàn tất chỉ đếm DONE/SKIP/ERROR. `OVERDUE` giữ 0%, được thống kê riêng và không bị đổi thành `ERROR`; các mục chưa có kết quả chuyển sang “Không có kết quả xác nhận”. ERROR, thiếu terminal event, hoặc không xác minh được kết quả gỡ ứng dụng đều làm lượt chạy thất bại ngay cả khi process trả mã 0. Marker Completed không được đưa lên UI trước khi service xác minh đủ 17 kết quả. `Test-OptimizeTaskBridge.ps1` kiểm tra protocol bằng fixture và không chạy thay đổi hệ thống.

`runtime-diagnostics.json` ghi PID runner, danh sách hai PID engine và PID Appx worker gần nhất, thời gian output/protocol cuối, stage, exit code và số tác vụ theo kết quả. Khi Appx quá hạn, engine tương ứng ngừng chờ và UI hiển thị trạng thái thật; worker giữ mutex `Global\MiniApps.DebloatAppxWorker` cho tới khi Windows trả về. Lượt thử lại chỉ có thể bắt đầu sau khi mutex được nhả.

Install và Optimize không bắt đầu đồng thời trong cùng cửa sổ. Người dùng vẫn chuyển qua Install app và Driver khi Optimize chạy; Setting bị khóa. Nút “Dừng hàng đợi” chỉ thuộc luồng Install và không xuất hiện trong Optimize.

Khi nâng upstream: đọc lại Default và danh sách Apps, kiểm tra thay đổi schema, điểm gắn bridge, tham số, backup và error handling; cập nhật cây vendor cùng commit đã ghim rồi test net48 bằng fixture và VM được chỉ định. Không chạy Optimize thật trên máy phát triển để xác minh profile.

Kiểm tra extraction-only ngày 2026-09-07: fixture đường dẫn `irm` giảm đường dẫn dài nhất được tái hiện từ 286 xuống 204 ký tự; các trường hợp traversal, root lạ, alias khác hoa thường, absolute path, ADS, quá dài và ZIP hỏng đều bị từ chối trước khi tạo file đích. ZIP thật của commit đã ghim giải nén 403 file, đường dẫn dài nhất 221 ký tự. Không chạy entry point upstream hoặc thay đổi hệ thống trong các kiểm tra này.
