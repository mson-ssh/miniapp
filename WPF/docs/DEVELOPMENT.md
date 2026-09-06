# Chuyển máy và phát triển

## Chuẩn bị

Lấy repo WPF `https://github.com/mson-ssh/miniapp.git` hoặc sao chép source có cả tài liệu chưa push. Mở thư mục `WPF` trong công cụ AI, yêu cầu đọc `AGENTS.md`. Cần Windows x64, Git, .NET SDK hỗ trợ target net10 trong project (máy cũ dùng SDK 10) và Framework 4.8 để chạy test/preview net48. NuGet restore lấy reference assemblies cho net48; không cần mang theo thư mục SDK riêng từ máy cũ.

Các lệnh dưới đây chạy **từ thư mục WPF**. Kiểm tra `dotnet --info` và `git status --short` trước. Nếu SDK không có trên PATH, thay `dotnet` bằng đường dẫn SDK đã xác minh trên máy đó.

```powershell
dotnet build ./MiniApps.Tests/MiniApps.Tests.csproj -c Release -f net48
# Chỉ chạy tiếp nếu build thành công.
& ./MiniApps.Tests/bin/Release/net48/MiniApps.Tests.exe
```

Test hiện dùng fake runner/download và WPF mô phỏng, không chạy installer hoặc Windows settings thật. Khi sửa bootstrap, chạy riêng:

```powershell
& ./Test-Bootstrap.ps1
```

Mở preview sau khi sửa tính năng:

```powershell
& ./Preview.ps1 -Target net48 -Developer -Dotnet dotnet
```

Preview này đọc/ghi `ReleaseConfig`, do đó lưu trong Setting là thay đổi thật cần review diff. Nếu executable đang bị khóa, đóng đúng cửa sổ preview sau khi kiểm tra không có tác vụ hoặc bản nháp cần giữ; không kill mọi process MiniApps.

## Mang theo và bỏ qua

Mang theo source `MiniApps/`, `MiniApps.Tests/`, `ReleaseConfig/`, các script/thông tin build, `AGENTS.md`, `docs/` và README. Có thể clone repo để lấy các file đã commit. Tài liệu chưa push phải sao chép riêng nếu đổi máy ngay.

Không cần mang `bin/`, `obj/`, `artifacts/`, checkout tạm, NuGet cache hoặc token đăng nhập. Restore/build lại trên máy mới. Đường dẫn `F:\Project\scr-miniaz` và SDK dưới LocalAppData là đặc thù máy cũ, không phải yêu cầu của dự án.

## Git và phát hành

Chỉ commit/push khi người dùng yêu cầu. Trước đó kiểm tra `git remote -v`, branch, diff và trạng thái. Remote WPF đúng là **miniapp.git**, không phải **miniapps.git** của CLI. Checkout gốc máy cũ có WPF untracked và remote CLI; từng dùng checkout tạm riêng để push. Trên máy mới ưu tiên clone trực tiếp repo đúng, không phụ thuộc checkout tạm đó.

Chỉ chạy `Publish.ps1` khi được yêu cầu phát hành. Mặc định `-Target net48` build net48 và tải lại nguyên gói net10 v0.3.0 đã xác minh để giữ fallback. `-Target both` build cả hai và cần người dùng yêu cầu cập nhật net10. Build/test net48 luôn cần `-f net48`.

Khi được yêu cầu phát hành, kiểm tra phạm vi runtime trước, cấu hình đã lưu, version, ZIP/checksum/manifest cùng phiên bản. Nếu net10 phải giữ nguyên, cần thiết kế gói/manifest phù hợp thay vì rebuild cả hai. Chỉ đổi bootstrap khi các asset được tham chiếu thực sự tồn tại và được xác minh. Không overwrite Release đã phát hành để cập nhật âm thầm.
