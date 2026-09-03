# Wallpaper Control

Wallpaper Control là bảng điều khiển bổ sung cho [Lively Wallpaper](https://github.com/rocksdanister/lively), tập trung vào tự động đổi video, chuyển cảnh mượt, smart scaling và tăng màu an toàn bên trong MPV.

![Wallpaper Control icon](assets/WallpaperControl.png)

## Cài đặt nhanh

1. Cài và mở Lively Wallpaper ít nhất một lần.
2. Tải [`WallpaperControlSetup.exe`](dist/WallpaperControlSetup.exe).
3. Chạy file Setup và kiểm tra đường dẫn Lively được phát hiện.
4. Nhấn **CÀI ĐẶT / CẬP NHẬT**.

Bộ cài không cần quyền Administrator. Ứng dụng được cài vào:

```text
%LOCALAPPDATA%\Programs\Wallpaper Control
```

Setup tự tạo shortcut Desktop, Start Menu và tùy chọn khởi động cùng Windows. Sau khi cài, có thể bật/tắt lại tùy chọn này ngay trong app. Khi được Windows gọi lúc đăng nhập, Wallpaper Control chạy ẩn dưới khay hệ thống và tự nạp các chức năng nền mà không hiện cửa sổ. Chạy lại cùng file Setup để cập nhật hoặc gỡ Wallpaper Control. Thao tác gỡ không xóa video hay thư viện Lively.

> File hiện chưa được ký bằng chứng thư thương mại nên Windows SmartScreen có thể hiển thị `Unknown publisher`. Nếu Smart App Control đang ở chế độ `On`, Windows có thể chặn hoàn toàn một bản build mới chưa có uy tín theo hash; chế độ này không hỗ trợ ngoại lệ riêng cho từng app, vì vậy bản phát hành cần chữ ký từ nhà cung cấp chứng thư được Windows tin cậy.

## Tự phát hiện Lively

Setup hỗ trợ:

- Microsoft Store: dữ liệu trong package LocalAppData.
- Desktop Installer: dữ liệu trong `%LOCALAPPDATA%\Lively Wallpaper`.
- Thư viện đã được chuyển sang ổ khác thông qua khóa `WallpaperDir` trong `Settings.json`.

Video đã đăng ký trong Lively được đọc từ `LivelyInfo.json` và sử dụng bằng đường dẫn gốc. Setup không nhúng, sao chép hoặc tải lên video cá nhân.

## Màu và NVIDIA

- Ứng dụng không ghi, bật hoặc tắt trạng thái NVIDIA; game và NVIDIA App tự quản lý RTX Dynamic Vibrance/Game Filter.
- Color Boost dùng thuộc tính saturation của tiến trình MPV, nên chỉ thay đổi video hình nền.
- Giao diện có ba chế độ màu tách biệt: **Tắt tăng màu**, **Thủ công**, và **Dùng hồ sơ folder đã tạo**.
- Chế độ thủ công cho phép chỉnh Intensity và Saturation từ `0–100` rồi áp dụng chung, không đọc phân loại folder; mặc định Saturation là `100`.
- Chế độ hồ sơ tự đọc cấu hình từ tên folder; video chưa phân loại sẽ không được tăng màu.
- Nút **LƯU & TẠO FOLDER** tạo hồ sơ theo mẫu `RTX DYNAMIC VIBRANCE <Intensity>-<Saturation>`.
- Video luôn được phát từ thư mục `Videos`; các thư mục hồ sơ hard-link chỉ được dùng làm nhãn cấu hình.
- Dịch vụ tự nhận mọi thư mục đúng mẫu, không giới hạn ở ba mức `50/70/100` có sẵn.
- Bảng **Đang phát · Cấu hình thực tế** đọc trực tiếp `WallpaperLayout.json` của Lively để hiển thị video hiện tại, hồ sơ khớp, Intensity, Saturation và mức MPV đang dùng; trạng thái shuffle chỉ là dự phòng.
- Video MP4 mới được thêm vào folder cấu hình sẽ tự có hard-link tương ứng trong `Videos`.
- Nếu `Videos` và folder cấu hình chứa hai bản sao trùng nội dung, app thay bản trong folder cấu hình bằng hard-link; file trùng tên nhưng khác nội dung không bị ghi đè.
- Thiết lập mặc định sau lần cài đầu: Auto Wallpaper **Off**, Color Boost **Off**.

## Thư mục phân loại

Setup tạo bốn thư mục bên trong `WallpaperDir`:

```text
NONE RTX DYANMIC VIBRANCE
RTX DYNAMIC VIBRANCE 50-100
RTX DYNAMIC VIBRANCE 70-100
RTX DYNAMIC VIBRANCE 100-100
```

Wallpaper Control vẫn tương thích với cách phân loại bằng hard link. Bộ đếm gồm cả bốn folder: một folder `NONE` và ba hồ sơ màu có sẵn `50/100`, `70/100`, `100/100`. Người dùng có thể tạo thêm, ví dụ `RTX DYNAMIC VIBRANCE 85-90`; ở chế độ hồ sơ, video nằm trong folder đó sẽ tự dùng Intensity `85`, Saturation `90`. Folder `NONE RTX DYANMIC VIBRANCE` luôn tắt tăng màu trong chế độ hồ sơ.

## Tính năng

- Giao diện gaming đỏ–đen và tray icon.
- Cửa sổ co giãn từ `760×590`, kéo được ở mọi cạnh/góc, hỗ trợ phóng to và tự thu nhỏ toàn bộ bố cục.
- Có công tắc khởi động cùng Windows ngay trong app; phiên tự khởi động chạy ẩn dưới tray.
- Color Boost tự áp dụng lại sau mỗi lần đổi video và tiếp tục được giữ ổn định nếu MPV khởi tạo lại decoder/renderer.
- Chế độ hồ sơ lấy video đang phát trực tiếp từ MPV, tránh áp nhầm `NONE` do state hoặc WallpaperLayout cũ.
- Menu khay và yêu cầu mở cửa sổ đến muộn sẽ được bỏ qua an toàn khi app đang thoát, tránh lỗi `ObjectDisposedException` sau khi Windows hoặc driver khởi tạo lại desktop.
- Nút X thu ứng dụng xuống tray; menu tray có lệnh thoát hoàn toàn.
- Auto Wallpaper có khóa tắt riêng, không bị watchdog tự bật lại.
- Shuffle bag tránh lặp video trong cùng chu kỳ.
- Chuyển video qua MPV IPC và giữ cửa sổ Lively ổn định.
- Fade hai giai đoạn, rút ngắn vùng gần đen để giảm cảm giác lưu ảnh khi chuyển cảnh.
- Smart scaling theo độ phân giải/tỷ lệ khung hình.
- Đồng bộ hard-link nền giúp các folder cấu hình và thư viện Lively không nhân đôi dữ liệu video.
- Một tiến trình Control cho mỗi tài khoản Windows.
- Setup một file, tự phát hiện Lively và cấu hình smart scaling một lần.

## Build từ mã nguồn

Yêu cầu:

- Windows 10/11.
- .NET Framework C# compiler (`csc.exe`, có sẵn trên Windows phù hợp).
- MinGW-w64 `g++.exe` để biên dịch launcher native.
- PowerShell 5.1 trở lên.

Chạy:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Kết quả nằm tại `dist\WallpaperControlSetup.exe`.

## Mã nguồn chính

- `src/WallpaperControlApp.cs`: giao diện và điều khiển tray/service.
- `src/WallpaperControlSetup.cs`: bộ cài portable một file.
- `src/LivelyShuffleLauncher.cpp`: launcher ẩn, tự tìm script theo vị trí cài.
- `scripts/LivelyShuffle.ps1`: shuffle, transition, scaling và MPV color.
- `scripts/ApplyWallpaperControl.ps1`: áp dụng cấu hình màu đang chạy.

## Kiểm tra file phát hành

SHA-256 của bản `2.6.2.0` hiện tại:

```text
409D7B272A9F6BFEC1A6754656110FC4871D9B0CA7EA39E6E8185D43BE9C5D8F
```

## Tham khảo Lively chính thức

- [Getting Started / vị trí dữ liệu](https://github.com/rocksdanister/lively/wiki/Getting-Started)
- [Command Line Controls](https://github.com/rocksdanister/lively/wiki/Command-Line-Controls)
- [Video Guide / MPV](https://github.com/rocksdanister/lively/wiki/Video-Guide)
