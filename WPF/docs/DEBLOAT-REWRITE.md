# MiniApps Debloat cho Windows 11

## Mục tiêu

MiniApps duy trì một fork hẹp từ Win11Debloat commit `6012b02ea282f23ea943946206762fd430025c6f` cho net48. Engine chạy từ gói cục bộ, không tải source khi thực thi, áp dụng profile Default sau khi bỏ `CreateRestorePoint`, giữ backup Registry và báo kết quả xác minh cho từng tác vụ.

## Hợp đồng tích hợp

Entry point Win11Debloat có chế độ host `-MiniApps` và phạm vi `-MiniAppsLane`. Một lượt Optimize khởi chạy đồng thời hai engine từ cùng bản sao source: lane `Features` áp dụng 16 tính năng và tạo Registry backup, lane `RemoveApps` gỡ danh sách app Default và bỏ bước backup trùng. Mỗi lane có transcript/stdout/stderr riêng dưới cùng thư mục log; protocol được hợp nhất thành 17 tác vụ. Adapter được engine nạp trực tiếp trước `Invoke-AllChanges`; không sửa entry point bằng regex lúc chạy.

Mỗi package Appx, gồm cả bước Appx phụ của một số tính năng, và mỗi lệnh WinGet chạy trong một process `powershell.exe` riêng rồi ghi kết quả JSON có PID, package, thời gian, trạng thái cùng lỗi. Hai lane được chạy đồng thời nhưng các thay đổi package vẫn được mutex tuần tự hóa; vì vậy các chỉnh Registry/Windows có thể chạy trong lúc RemoveApps hoạt động. Process engine giám sát Appx trong 180 giây và WinGet trong 120 giây. Nếu worker chưa trả về khi hết hạn, engine phát `OVERDUE`, ngừng xếp package mới và thoát khỏi luồng điều khiển; worker vẫn tiếp tục để Windows tự hoàn tất.

Worker giữ mutex `Global\MiniApps.DebloatAppxWorker` trong suốt thời gian còn hoạt động. Optimize và Install mới bị chặn cho tới khi mutex được nhả, tránh hai lượt thay đổi package chạy chồng nhau. UI trở về trạng thái có thể tương tác, giữ hàng `RemoveApps` ở `Overdue`, phần trăm bằng 0 và đường dẫn log để chẩn đoán. Cả nhóm cũng ngừng xếp package mới khi đạt deadline 1.200 giây.

PowerShell con dùng output dạng text. Stderr được giữ làm chẩn đoán nhưng không tự biến thành thất bại khi exit code bằng 0; kết quả tác vụ dùng `MINIAPPS_TASK_JSON`. Trạng thái `OVERDUE` được giữ nguyên qua bridge và không bị ghi đè thành `ERROR`.

## Nguyên nhân vòng chờ đã xác nhận

Lượt chạy thật tại `%LocalAppData%\MiniApps\OptimizeLogs\20260907-141430-db784a9ebe2f41cf854702e18143b841` dừng phát protocol sau package 37/84 (`Microsoft.WindowsSoundRecorder`). Windows AppX ghi lỗi repository `0x80070002`, `0x80073CFA`, sau đó có Office C2RX `0x80073CF1`. Lời gọi AppX/COM không phản hồi với việc dừng runspace, nên timeout cũ chỉ được kiểm tra giữa các package và UI tiếp tục chờ process engine. CLIXML làm log khó đọc nhưng không phải nguyên nhân treo.

## Ranh giới kế thừa

Các tệp Registry, danh sách app, logic tương thích, scheduled task, ACL và backup vẫn kế thừa từ commit đã ghim. Những thay đổi trực tiếp trong `ThirdParty/Win11Debloat` chỉ phục vụ hợp đồng host, worker ngoài process, deadline và quan sát tiến trình. Khi nâng upstream phải đối chiếu lại từng thay đổi này, profile Default, schema, backup, app removal và kiểm thử VM.

## Kiểm thử

- `MiniApps.Tests.exe`: protocol, mutex worker, vòng đời `Overdue`, lỗi/thiếu kết quả, retry và diagnostics.
- `Test-OptimizeBundledEngine.ps1`: provenance, package net48, hai lane, host contract, worker ngoài process, profile và đường backup.
- `Test-OptimizeParallelEngine.ps1`: xác nhận hai process engine chồng thời gian, mã thoát thật, dòng protocol bị chia nhỏ, đoạn cuối không newline và lỗi khởi động lane.
- `Test-OptimizeTaskBridge.ps1`: boolean, exception, giữ `OVERDUE` và các sự kiện tác vụ.
- `Test-OptimizeTimeout.ps1`: xác nhận monitor trả quyền điều khiển mà không kill worker giả lập.
- `Test-OptimizeAppxWorker.ps1`: chạy pattern chắc chắn không khớp package, kiểm tra JSON và mutex; không gỡ ứng dụng.
- `Test-OptimizeWingetWorker.ps1`: dùng executable giả lập vô hại, kiểm tra JSON, mã thoát process con và mutex; không gỡ package.

Kiểm thử tự động không chứng minh tác động Windows thật. Bước xác nhận cuối là người dùng chạy trên VM Windows 10/11, sau đó đối chiếu UI, package cuối, PID worker, timestamps, backup và `runtime-diagnostics.json`.
