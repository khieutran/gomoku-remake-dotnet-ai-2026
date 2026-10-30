# CI/CD

- Pull request: chạy `scripts/verify-phase4.sh verify` trong GitHub Actions. Script kiểm tra frontend, API và các luồng E2E trong môi trường Docker cô lập.
- Push lên `feat/phase-4-ui` hoặc `main`: chạy cùng bước kiểm tra. Chỉ khi bước này thành công, workflow mới build image từ `backend/Gomoku.Api/Dockerfile` và đẩy lên GitHub Container Registry với tag `sha-<commit SHA>`.
- Image chứa cả React frontend và .NET API, phục vụ cùng origin. Tên image: `ghcr.io/khieutran/gomoku-remake-dotnet-ai-2026:sha-<commit SHA>`.
- Workflow dùng `GITHUB_TOKEN` của GitHub Actions với quyền `packages: write`; không cần thêm token vào repository secrets. Nếu repository/package là private, máy triển khai sau này cần quyền đọc package để pull image.
- Chưa có bước tự triển khai lên VM hoặc Cloudflare. Khi tạo VM, thêm môi trường production, secrets và quy trình migrate/backup/health check trước khi bật CD lên máy chủ. Không gắn image `latest` để mỗi bản build đều truy vết được theo commit.

Sau khi push, xem kết quả tại GitHub → Actions → **Verify Gomoku**. Image được tạo sẽ xuất hiện trong mục **Packages** của repository hoặc tài khoản GitHub.
