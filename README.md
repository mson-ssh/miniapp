# MiniApps

Ứng dụng Windows gọn nhẹ, giao diện WPF sáng dịu, sử dụng .NET Framework 4.8 có sẵn trên Windows 10 21H2+ và Windows 11.

- **Install app:** cài ứng dụng song song, chọn Office 2024 hoặc WPS, tự nhận diện ứng dụng đã cài và hiển thị tiến trình.
- **Optimize Windows:** giao diện preview mô phỏng; chưa áp dụng tối ưu hệ thống.
- **Driver:** đọc hãng, model, host, serial và mở trang hỗ trợ chính hãng.
- **Setting:** tìm kiếm, thêm/sửa/xóa ứng dụng và lệnh Windows; lưu hoặc hủy bản nháp theo từng tab.

## Build và preview

Trên máy phát triển có .NET SDK hiện đại:

```powershell
./WPF/Publish.ps1
./WPF/Preview.ps1
```

Preview không cài ứng dụng hoặc áp dụng thiết lập Windows. Gói net48 x64 hiện khoảng 0,45 MiB, không đóng gói .NET Desktop Runtime.

## Kiểm thử

```powershell
dotnet build WPF/MiniApps.Tests -c Release
./WPF/MiniApps.Tests/bin/Release/net48/MiniApps.Tests.exe
powershell -NoProfile -ExecutionPolicy Bypass -File WPF/Test-Bootstrap.ps1
```

## Phát hành

Build tạo ZIP, SHA-256 và manifest trong `WPF/artifacts`. Các file này phải được tải lên GitHub Release tương ứng trước khi bootstrap trực tuyến sử dụng được:

```powershell
irm https://raw.githubusercontent.com/mson-ssh/miniapp/main/WPF/bootstrap.ps1 | iex
```

Mã nguồn đã kiểm thử cục bộ trên .NET Framework 4.8 x64. Kiểm thử trên Windows 10/11 sạch còn cần hoàn tất; ARM64 chưa được xác minh. Bootstrap kiểm tra Framework, xác minh gói tải và dọn thư mục phiên sau khi ứng dụng cùng tiến trình con kết thúc.

Xem [tài liệu chi tiết](WPF/README.md) và [kế hoạch chuyển đổi net48](WPF/NET48-MIGRATION-PLAN.md).
