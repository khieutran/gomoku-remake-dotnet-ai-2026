# GOMOKU - KioTranDev

Game Gomoku chơi trên trình duyệt máy tính và điện thoại. Người chơi đấu với bot tự xây bằng .NET; mỗi nước đi được xử lý qua API, lưu lại để tiếp tục ván dở và xem lịch sử. Giao diện React dùng cùng một API origin với backend trong bản build Docker.

## Hiện có gì?

- Chơi khách hoặc đăng ký, đăng nhập; chọn quân X/O và lượt đi trước/sau.
- Bot dùng đánh giá thế cờ và tìm kiếm nước đi có giới hạn thời gian. Luật thắng/hòa được xử lý trong lõi game.
- Lưu ván, xem lại nước đi, tiếp tục ván dở; bảng xếp hạng cho tài khoản đăng nhập.
- Giao diện retro thích ứng desktop/mobile, có hoạt cảnh bắt đầu và kết thúc ván.
- Bộ công cụ xuất dữ liệu, tự đấu, huấn luyện và đánh giá mô hình nằm trong `tools/` và `training/`. Việc huấn luyện và phát hành mô hình mới **chưa tự động chạy trên môi trường chơi**.

## Công nghệ và cấu trúc

| Thành phần | Vai trò |
| --- | --- |
| `backend/Gomoku.Core` | Bàn cờ và luật chơi |
| `backend/Gomoku.Application` | Luồng ván đấu, lịch sử và bảng xếp hạng |
| `backend/Gomoku.AI` | Bot, tìm kiếm và đánh giá nước đi |
| `backend/Gomoku.Infrastructure` | PostgreSQL, Redis và các triển khai lưu trữ |
| `backend/Gomoku.Api` | ASP.NET Core API, xác thực và phục vụ React build |
| `frontend/` | React, TypeScript, Vite |
| `tests/`, `scripts/` | Kiểm thử và lệnh vận hành |

Backend dùng .NET 10; frontend dùng Node.js 24. PostgreSQL lưu dữ liệu lâu dài, Redis hỗ trợ cache và bảng xếp hạng. Docker Compose ghép các dịch vụ để chạy cùng nhau. Xem [Dockerfile](backend/Gomoku.Api/Dockerfile) và [Compose local](compose.yaml).

## Chạy local bằng Docker

Cần Docker Engine/Colima và Docker Compose. Tại thư mục gốc repo:

```bash
docker compose up --build --wait -d api
```

Mở **http://127.0.0.1:58080**. Kiểm tra API đã sẵn sàng:

```bash
curl http://127.0.0.1:58080/ready
```

Nếu cổng PostgreSQL mặc định `55432` đang được máy khác dùng, chạy `GOMOKU_POSTGRES_PORT=55433 docker compose up --build --wait -d api`. Dừng dịch vụ bằng `docker compose down`; dữ liệu trong các Docker volume vẫn được giữ. Cấu hình này chỉ dành cho máy phát triển, với các cổng database/Redis gắn vào `127.0.0.1`.

## Phát triển và kiểm tra

Để chạy frontend bằng Vite trong lúc API Docker vẫn ở cổng `58080`:

```bash
npm ci
VITE_API_PROXY_TARGET=http://127.0.0.1:58080 npm run dev -w frontend
```

Frontend dùng đường dẫn API `/api/v1` cùng origin. Vite proxy chỉ phục vụ lúc phát triển; bản Docker gói React vào .NET API. Các lệnh kiểm tra chính:

```bash
bash scripts/test-web.sh
bash scripts/verify-phase4.sh preflight
bash scripts/verify-phase4.sh verify
```

Lệnh `verify` dùng Docker để chạy kiểm tra frontend, API và E2E trong stack cô lập; script tự dọn các container và volume kiểm thử sau khi kết thúc.

## CI/CD và triển khai

[GitHub Actions](.github/workflows/verify.yml) chạy kiểm tra khi tạo PR. Push lên `feat/phase-4-ui` hoặc `main` sẽ chạy kiểm tra rồi, nếu đạt, xuất image React + .NET lên GitHub Container Registry theo tag `sha-<commit SHA>`. **Chưa có bước tự triển khai lên VM**. Xem [chi tiết CI/CD](.github/CI-CD.md).

Repo có [Compose production](compose.production.yaml), [mẫu biến môi trường](.env.production.example) và [Caddyfile](deploy/Caddyfile). Khi triển khai cần domain thật, HTTPS, secrets riêng, migration, backup và kiểm tra sức khỏe. Không commit mật khẩu hay file `.env.production`.

Cloudflare Pages Free có thể đăng bản giao diện tĩnh để xem thử, nhưng **không chạy nguyên backend .NET/PostgreSQL/Redis** của game này. Một bản chơi đầy đủ vẫn cần máy chủ chạy backend và các dịch vụ dữ liệu.

## Trạng thái dự án

Code game và bộ kiểm tra đang tiếp tục được hoàn thiện. Image CI là bản build có thể truy vết theo commit, chưa đồng nghĩa với một bản production đã được triển khai hoặc vận hành công khai.

